using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>Runs a saved draft against one authorized imported asset using current, read-only measurements.</summary>
public sealed class GuidedLivePreviewService(GuidedModuleDraftService drafts, IAssetRepository assets,
    IAssetConnectionSourceReader sources, IConnectionRepository connections, IRequestAccessRepository access,
    IExecutionMeasurementGateway gateway, IGatewayAdapters adapters, IGuidedHistoryGateway history, TimeProvider clock)
{
    public async Task<GuidedLivePreview> PreviewAsync(Guid draftId, int expectedRevision, Guid assetId,
        string? correlationId, AccessSubject subject, CancellationToken cancellationToken = default)
    {
        var draft = await drafts.GetAsync(draftId, subject, cancellationToken);
        draft.RequireRevision(expectedRevision);
        draft.RequireThresholdLogic();
        var artifact = await DeclarativeThresholdPackageBuilder.BuildAsync(draft.Content.Manifest, draft.Content.Definition!, cancellationToken);
        var module = artifact.Package.DeclarativeModule!;
        var initial = await ReadContextAsync(subject, assetId, cancellationToken);
        var manifest = artifact.Package.Manifest;
        if (manifest.DataRequirements.Any(input => !SupportsCurrentInput(manifest, initial, input)))
            throw new WellSiteAutoPilotException("GUIDED_LIVE_INPUT_UNSUPPORTED", FailureKind.Validation,
                "This connection does not support the draft inputs.");

        await RequireMatchingAssetAsync(manifest, initial, subject, cancellationToken);

        var readings = new List<GuidedLiveReading>(manifest.DataRequirements.Count);
        var first = await ObserveAsync(module.Input.Id, module.Input.Quantity);
        var second = module.Subtrahend is { } input ? await ObserveAsync(input.Id, input.Quantity) : null;
        await RequireCurrentContextAsync();
        var currentDraft = await drafts.GetAsync(draftId, subject, cancellationToken);
        currentDraft.RequireRevision(expectedRevision);
        var failure = readings.Any(item => item.TimestampKind == "UnverifiedScan") ? "EXECUTION_INPUT_TIMESTAMP_UNVERIFIED" : null;
        var runId = Guid.NewGuid();
        var sample = new DeclarativeThresholdSample(runId, correlationId ?? runId.ToString("D"), assetId, clock.GetUtcNow(), "{}", "{}", first, second);
        var evidence = failure is null
            ? await DeclarativeThresholdPreview.EvaluateAsync(artifact, sample, cancellationToken)
            : new DeclarativeThresholdEvidence(artifact.Package.Sha256, sample, null, failure);
        return new(evidence, readings);

        async Task<LogicInputValue?> ObserveAsync(string id, string quantity)
        {
            await RequireCurrentContextAsync();
            var value = await ReadCurrentAsync(initial, quantity, cancellationToken);
            readings.Add(new(id, value.Quantity, value.Value, value.Unit, value.TimestampUtc, value.Quality, value.TimestampKind));
            return value.Value is { } number && value.StructuredValue is null
                ? new(id, assetId, value.Quantity, number, value.Unit, value.TimestampUtc, value.Quality, value.Source)
                : null;
        }

        async Task RequireCurrentContextAsync()
        {
            var current = await ReadContextAsync(subject, assetId, cancellationToken);
            if (!SameSource(initial, current))
                throw new WellSiteAutoPilotException("GUIDED_LIVE_SOURCE_CHANGED", FailureKind.Conflict,
                    "The sample asset or connection changed. Select it again.");
        }

    }

    public async Task<IReadOnlyList<GuidedInputCompatibility>> CheckCompatibilityAsync(Guid draftId, int expectedRevision,
        Guid assetId, AccessSubject subject, CancellationToken cancellationToken = default)
    {
        var draft = await drafts.GetAsync(draftId, subject, cancellationToken);
        draft.RequireRevision(expectedRevision);
        var manifest = draft.Content.Manifest;
        var source = await ReadContextAsync(subject, assetId, cancellationToken);
        var matchesAsset = await MatchesAssetAsync(manifest, source, subject, cancellationToken);
        var results = new List<GuidedInputCompatibility>(manifest.DataRequirements.Count);
        foreach (var input in manifest.DataRequirements)
        {
            var external = draft.Content.SourceHints?.SingleOrDefault(item => item.InputId == input.Id);
            if (!matchesAsset)
                results.Add(new(input.Id, input.Quantity, "Unsupported", "GUIDED_LIVE_ASSET_UNSUPPORTED"));
            // An external input is not read from this asset's provider, so there is nothing here to check yet.
            else if (external is not null)
                results.Add(new(input.Id, input.Quantity, "External", external.ExternalSource));
            else if (UnsupportedReason(manifest, source, input) is { } reason)
                results.Add(new(input.Id, input.Quantity, "Unsupported", reason));
            else
            {
                await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, source, cancellationToken);
                results.Add(await CheckCurrentInputAsync(source, input, cancellationToken));
            }
        }

        await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, source, cancellationToken);
        return results;
    }

    private async Task<GuidedInputCompatibility> CheckCurrentInputAsync(PreviewSource source, LogicDataRequirement input,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await ReadCurrentAsync(source, input.Quantity, cancellationToken, input.RequiredCapability);
            var structuredInput = input.RequiredCapability == AdapterReadRules.StructuredCurrentCapability;
            if ((value.StructuredValue is not null) != structuredInput)
                return new(input.Id, input.Quantity, "Rejected", "EXECUTION_INPUT_SHAPE_UNSUPPORTED");
            if (!structuredInput && value.Value is null)
                return new(input.Id, input.Quantity, "Missing", "MODULE_DECLARATIVE_INPUT_UNAVAILABLE");
            if (value.TimestampKind == "UnverifiedScan")
                return new(input.Id, input.Quantity, "Rejected", "EXECUTION_INPUT_TIMESTAMP_UNVERIFIED");
            LogicModuleInputValidation.ValidateObservation(value.Quantity, value.Unit, value.TimestampUtc,
                value.Quality, input, clock.GetUtcNow());
            return new(input.Id, input.Quantity, "Satisfied", null);
        }
        catch (WellSiteAutoPilotException exception) when (exception.Kind is FailureKind.Validation or FailureKind.DependencyUnavailable or FailureKind.DependencyTimeout)
        {
            var status = exception.Code switch
            {
                "CYGNET_QUANTITY_UNSUPPORTED" => "Unsupported",
                "FORESITE_MEASUREMENT_MISSING" or "CYGNET_POINT_NO_VALUE" or "CYGNET_POINT_NOT_FOUND" => "Missing",
                "FORESITE_MEASUREMENT_INVALID" or "FORESITE_TIMESTAMP_INVALID" or "CYGNET_VALUE_NOT_NUMERIC" or
                    "CYGNET_TIMESTAMP_INVALID" => "Rejected",
                _ when exception.Kind == FailureKind.Validation => "Rejected",
                _ => "Unavailable"
            };
            return new(input.Id, input.Quantity, status, exception.Code);
        }
    }

    private static string? UnsupportedReason(LogicModuleManifest manifest, PreviewSource source, LogicDataRequirement input) =>
        GuidedInputSupport.UnsupportedReason(manifest.RequiredAdapterKey, source.Connection.AdapterKey, source.Binding.ResourceKind, input);

    private static bool SupportsCurrentInput(LogicModuleManifest manifest, PreviewSource source, LogicDataRequirement input) =>
        UnsupportedReason(manifest, source, input) is null;

    private Task<EngineeringValue> ReadCurrentAsync(PreviewSource source, string quantity, CancellationToken cancellationToken,
        string? capability = null) =>
        gateway.GetCurrentAsync(new(source.Connection.AdapterKey, source.Connection.Id, source.Connection.Revision,
            source.Binding.ExternalId, quantity,
            source.Connection.Settings.ToDictionary(entry => entry.Key, entry => (string?)entry.Value, StringComparer.Ordinal),
            capability),
            cancellationToken);

    public async Task<HistoricalReadResult> ReadHistoryAsync(Guid draftId, int expectedRevision, Guid assetId, string inputId,
        DateTimeOffset? start, DateTimeOffset? end, int maximumSamples, AccessSubject subject,
        GuidedSiteHistoryWindow? siteWindow = null, CancellationToken cancellationToken = default)
    {
        var draft = await drafts.GetAsync(draftId, subject, cancellationToken);
        draft.RequireRevision(expectedRevision);
        var input = draft.Content.Manifest.DataRequirements.SingleOrDefault(item => item.Id == inputId);
        // Authoring declares every input with a value schema, so a plain numeric schema is still a scalar series.
        if (input is null || !GuidedInputSupport.IsScalarSeries(input))
            throw new WellSiteAutoPilotException("GUIDED_HISTORY_INPUT_UNSUPPORTED", FailureKind.Validation,
                "Choose a declared scalar input.");
        var (startUtc, endUtc) = GuidedSiteHistoryWindow.Resolve(start, end, siteWindow);
        if (!IsValidWindow(startUtc, endUtc) || maximumSamples is < 1 or > HistoricalReadLimits.MaximumSamples)
            throw new WellSiteAutoPilotException("GUIDED_HISTORY_WINDOW_INVALID", FailureKind.Validation,
                "Specify an ordered history window with whole-second bounds and a supported sample limit.");

        var initial = await ReadContextAsync(subject, assetId, cancellationToken);
        var manifest = draft.Content.Manifest;
        await RequireMatchingAssetAsync(manifest, initial, subject, cancellationToken);
        var adapter = (await adapters.ListAsync(cancellationToken)).SingleOrDefault(item => item.Key == initial.Connection.AdapterKey);
        var operation = adapter?.ReadOperations().SingleOrDefault(item => item.CapabilityId == "engineering.history" &&
            item.Quantity == input.Quantity && item.ResourceKind == initial.Binding.ResourceKind);
        if (operation is null || (manifest.RequiredAdapterKey is { } requiredAdapter &&
                !string.Equals(requiredAdapter, initial.Connection.AdapterKey, StringComparison.OrdinalIgnoreCase)) ||
            operation.Limits.MaximumWindowDays is not { } days || endUtc - startUtc > TimeSpan.FromDays(days) ||
            operation.Limits.MaximumSamples is not { } samples || maximumSamples > samples)
            throw new WellSiteAutoPilotException("GUIDED_HISTORY_INPUT_UNSUPPORTED", FailureKind.Validation,
                "The selected connection does not support this input or history window.");

        await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);
        var result = await ReadHistoryOperationAsync(initial, operation, input.Quantity, startUtc, endUtc, maximumSamples, cancellationToken);
        await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);
        return result;
    }

    private async Task<HistoricalReadResult> ReadHistoryOperationAsync(PreviewSource initial, AdapterOperationDescriptor operation,
        string quantity, DateTimeOffset startUtc, DateTimeOffset endUtc, int maximumSamples, CancellationToken cancellationToken)
    {
        var request = new HistoricalReadRequest(initial.Binding.ExternalId, quantity, startUtc.ToUniversalTime(),
            endUtc.ToUniversalTime(), maximumSamples);
        HistoricalReadLimits.Validate(request);
        var result = await history.ReadAsync(initial.Connection, operation, request, cancellationToken);
        if (result.Quantity != request.Quantity || result.StartUtc != request.StartUtc || result.EndUtc != request.EndUtc ||
            result.Aggregation != "Raw" || result.Samples.Count > maximumSamples ||
            result.Samples.Any(sample => sample.Quantity != request.Quantity || sample.TimestampUtc < request.StartUtc ||
                sample.TimestampUtc >= request.EndUtc || sample.Value is null || sample.StructuredValue is not null) ||
            result.Samples.Zip(result.Samples.Skip(1)).Any(pair => pair.First.TimestampUtc > pair.Second.TimestampUtc))
            throw new WellSiteAutoPilotException("GUIDED_HISTORY_RESPONSE_INVALID", FailureKind.DependencyUnavailable,
                "The history response did not match the requested input and window.");
        return result;
    }

    public async Task<ForeSiteDailyAverageResult> ReadProductionDaysAsync(Guid draftId, int expectedRevision,
        Guid assetId, DateTimeOffset startUtc, DateTimeOffset endUtc, AccessSubject subject, string? quantity = null,
        CancellationToken cancellationToken = default)
    {
        var draft = await drafts.GetAsync(draftId, subject, cancellationToken);
        draft.RequireRevision(expectedRevision);
        if (quantity is not null && !ForeSiteDailyAverageContract.IsObservableQuantityName(quantity))
            throw new WellSiteAutoPilotException("GUIDED_PRODUCTION_DAYS_QUANTITY_INVALID", FailureKind.Validation,
                "Choose a daily quantity the provider reported.");
        if (!IsValidWindow(startUtc, endUtc))
            throw new WellSiteAutoPilotException("GUIDED_PRODUCTION_DAYS_WINDOW_INVALID", FailureKind.Validation,
                "Specify an ordered production-day window with whole-second bounds.");

        var initial = await ReadContextAsync(subject, assetId, cancellationToken);
        await RequireMatchingAssetAsync(draft.Content.Manifest, initial, subject, cancellationToken);
        var adapter = (await adapters.ListAsync(cancellationToken)).SingleOrDefault(item => item.Key == initial.Connection.AdapterKey);
        var operation = adapter?.ReadOperations().SingleOrDefault(item =>
            item.Key == ForeSiteDailyAverageContract.OperationKey && item.ContractVersion == ForeSiteDailyAverageContract.Version &&
            item.ResourceKind == initial.Binding.ResourceKind);
        if (operation?.Limits.MaximumSamples is not { } maximum || operation.Limits.MaximumWindowDays is not { } days ||
            endUtc - startUtc > TimeSpan.FromDays(days) ||
            (draft.Content.Manifest.RequiredAdapterKey is { } requiredAdapter &&
                !string.Equals(requiredAdapter, initial.Connection.AdapterKey, StringComparison.OrdinalIgnoreCase)))
            throw new WellSiteAutoPilotException("GUIDED_PRODUCTION_DAYS_UNSUPPORTED", FailureKind.Validation,
                "The selected connection does not support this production-day preview.");

        await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);
        var result = await ReadDailyOperationAsync(initial, operation, startUtc, endUtc, maximum, cancellationToken, quantity);
        await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);
        return result;
    }

    private async Task<ForeSiteDailyAverageResult> ReadDailyOperationAsync(PreviewSource initial, AdapterOperationDescriptor operation,
        DateTimeOffset startUtc, DateTimeOffset endUtc, int maximum, CancellationToken cancellationToken, string? quantity = null)
    {
        const string utcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
        var request = new ForeSiteDailyAverageRequest(initial.Binding.ExternalId,
            startUtc.UtcDateTime.ToString(utcFormat, CultureInfo.InvariantCulture),
            endUtc.UtcDateTime.ToString(utcFormat, CultureInfo.InvariantCulture), quantity);
        var result = await history.ReadProductionDaysAsync(initial.Connection, operation, request, cancellationToken);
        if (result.AssetExternalId != initial.Binding.ExternalId || result.Quantity != (quantity ?? ForeSiteDailyAverageContract.Quantity) ||
            result.StartUtc != startUtc || result.EndUtc != endUtc || result.RetrievedAtUtc == default ||
            result.Days is null || result.Days.Count > maximum ||
            result.Days.Any(day => day is null || day.Start < startUtc || day.Start >= endUtc || day.End <= day.Start ||
                (day.Value is not null && string.IsNullOrWhiteSpace(result.Unit))))
            throw new WellSiteAutoPilotException("GUIDED_PRODUCTION_DAYS_RESPONSE_INVALID", FailureKind.DependencyUnavailable,
                "The production-day response did not match the requested asset and window.");
        return result;
    }

    public async Task<GuidedQuantityDiscovery> DiscoverQuantitiesAsync(Guid draftId, int expectedRevision,
        Guid assetId, string capability, DateTimeOffset? startUtc, DateTimeOffset? endUtc, AccessSubject subject,
        GuidedSiteHistoryWindow? siteWindow = null, CancellationToken cancellationToken = default)
    {
        if (capability == "WellTrend" && siteWindow is not null)
            (startUtc, endUtc) = GuidedSiteHistoryWindow.Resolve(startUtc, endUtc, siteWindow);
        var usesWindow = capability is "DailyAverages" or "WellTrend";
        if ((siteWindow is not null && capability != "WellTrend") || (usesWindow
            ? startUtc is null || endUtc is null || !IsValidWindow(startUtc.Value, endUtc.Value)
            : capability is not ("Surveillance" or "WellTest") || startUtc is not null || endUtc is not null))
            throw new WellSiteAutoPilotException("GUIDED_QUANTITY_DISCOVERY_REQUEST_INVALID", FailureKind.Validation,
                "Choose a supported quantity source and an ordered whole-second window.");

        var draft = await drafts.GetAsync(draftId, subject, cancellationToken);
        draft.RequireRevision(expectedRevision);
        var initial = await ReadContextAsync(subject, assetId, cancellationToken);
        await RequireMatchingAssetAsync(draft.Content.Manifest, initial, subject, cancellationToken);
        if (draft.Content.Manifest.RequiredAdapterKey is { } requiredAdapter &&
            !string.Equals(requiredAdapter, initial.Connection.AdapterKey, StringComparison.OrdinalIgnoreCase))
            return new(false, []);

        if (capability == "Surveillance")
            return await DiscoverSurveillanceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);

        var adapter = (await adapters.ListAsync(cancellationToken)).SingleOrDefault(item => item.Key == initial.Connection.AdapterKey);
        await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);
        if (capability == "WellTest")
        {
            var result = await DiscoverWellTestAsync(initial, adapter, cancellationToken);
            await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);
            return result;
        }

        return await DiscoverWindowQuantitiesAsync(draftId, expectedRevision, assetId, capability,
            startUtc!.Value, endUtc!.Value, subject, initial, adapter, cancellationToken);
    }

    private async Task<GuidedQuantityDiscovery> DiscoverWindowQuantitiesAsync(Guid draftId, int expectedRevision,
        Guid assetId, string capability, DateTimeOffset startUtc, DateTimeOffset endUtc, AccessSubject subject,
        PreviewSource initial, AdapterDescriptor? adapter, CancellationToken cancellationToken)
    {
        var operations = adapter?.ReadOperations().Where(item =>
            item.ResourceKind == initial.Binding.ResourceKind &&
            item.Limits.MaximumWindowDays is { } days && endUtc - startUtc <= TimeSpan.FromDays(days) &&
            item.Limits.MaximumSamples is > 0 &&
            (capability == "WellTrend"
                ? item.CapabilityId == "engineering.history" && item.Quantity is not null
                : initial.Connection.AdapterKey == "foresite" && item.Key == ForeSiteDailyAverageContract.OperationKey &&
                  item.ContractVersion == ForeSiteDailyAverageContract.Version)).ToArray() ?? [];

        // Keep provider work bounded even if a later adapter registers a large number of historical reads.
        const int maximumOperations = 8;
        if (operations.Length > maximumOperations)
            throw new WellSiteAutoPilotException("GUIDED_QUANTITY_DISCOVERY_LIMIT", FailureKind.DependencyUnavailable,
                "This adapter needs a bounded quantity catalog operation.");
        var quantities = new List<GuidedDiscoveredQuantity>(operations.Length);
        foreach (var operation in operations)
        {
            await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);
            try
            {
                if (capability == "DailyAverages")
                {
                    var daily = await ReadDailyOperationAsync(initial, operation, startUtc, endUtc,
                        operation.Limits.MaximumSamples!.Value, cancellationToken);
                    var observed = daily.ObservedQuantities ?? [];
                    if (observed.Count > ForeSiteDailyAverageContract.MaximumObservedQuantities ||
                        observed.Any(item => item is null ||
                            !ForeSiteDailyAverageContract.IsObservableQuantityName(item.Quantity) ||
                            item.Quantity == ForeSiteDailyAverageContract.Quantity ||
                            string.IsNullOrWhiteSpace(item.Unit) || item.Unit.Length > 32 || item.Unit.Any(char.IsControl)) ||
                        observed.Select(item => item.Quantity).Distinct(StringComparer.Ordinal).Count() != observed.Count)
                        throw new WellSiteAutoPilotException("GUIDED_PRODUCTION_DAYS_RESPONSE_INVALID",
                            FailureKind.DependencyUnavailable, "The production-day quantity response was invalid.");
                    quantities.Add(new(daily.Quantity, daily.Unit, daily.Days.Any(day => day.Value is not null)
                        ? "Observed" : "NoSamples"));
                    quantities.AddRange(observed.Select(item => new GuidedDiscoveredQuantity(item.Quantity, item.Unit, "Observed")));
                }
                else
                {
                    var trend = await ReadHistoryOperationAsync(initial, operation, operation.Quantity!, startUtc, endUtc, 1, cancellationToken);
                    quantities.Add(trend.Samples.Count > 0
                        ? new(trend.Quantity, trend.Samples[0].Unit, "Observed")
                        : new(trend.Quantity, null, "NoSamples"));
                }
            }
            catch (WellSiteAutoPilotException exception) when (exception.Kind is FailureKind.DependencyUnavailable or FailureKind.DependencyTimeout)
            {
                quantities.Add(new(capability == "DailyAverages" ? ForeSiteDailyAverageContract.Quantity : operation.Quantity!,
                    null, "Unavailable"));
            }
        }

        await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);
        return new(operations.Length > 0, quantities);
    }

    private async Task<GuidedQuantityDiscovery> DiscoverSurveillanceAsync(Guid draftId, int expectedRevision,
        Guid assetId, AccessSubject subject, PreviewSource initial, CancellationToken cancellationToken)
    {
        if (initial.Connection.AdapterKey != AdapterReadRules.ForeSiteKey ||
            AdapterReadRules.Find(initial.Connection.AdapterKey) is not { } rule || rule.ResourceKind != initial.Binding.ResourceKind)
            return new(false, []);

        var report = await history.ReadSurveillanceAsync(initial.Connection, initial.Binding.ExternalId, cancellationToken);
        if (report.AssetExternalId != initial.Binding.ExternalId ||
            report.Observations.Select(item => item.Quantity).Order(StringComparer.Ordinal)
                .SequenceEqual(rule.CurrentQuantities.Order(StringComparer.Ordinal), StringComparer.Ordinal) is false)
            throw new WellSiteAutoPilotException("GUIDED_SURVEILLANCE_RESPONSE_INVALID",
                FailureKind.DependencyUnavailable, "The surveillance response did not match the requested asset or catalogue.");

        await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);
        var quantities = report.Observations.OrderBy(item => item.Quantity, StringComparer.Ordinal).Select(item => item.Status switch
        {
            ForeSiteSurveillanceObservation.Observed => new GuidedDiscoveredQuantity(item.Quantity, item.Unit, "CurrentObserved"),
            ForeSiteSurveillanceObservation.NoValue => new(item.Quantity, null, "NoReading"),
            ForeSiteSurveillanceObservation.UnitMismatch => new(item.Quantity, null, "UnitMismatch"),
            _ => new(item.Quantity, null, "Unavailable")
        }).ToArray();
        return new(true, quantities, "ApplicabilityUnverified");
    }

    private async Task<GuidedQuantityDiscovery> DiscoverWellTestAsync(PreviewSource initial, AdapterDescriptor? adapter,
        CancellationToken cancellationToken)
    {
        var operation = adapter?.ReadOperations().SingleOrDefault(item =>
            item.Key == ForeSiteLatestWellTestContract.OperationKey && item.ContractVersion == ForeSiteLatestWellTestContract.Version &&
            item.ResourceKind == initial.Binding.ResourceKind);
        if (initial.Connection.AdapterKey != "foresite" || operation is null)
            return new(false, []);

        var request = new LatestWellTestReadRequest(initial.Binding.ExternalId);
        var report = await history.ReadLatestWellTestAsync(initial.Connection, operation, request, cancellationToken);
        if (report.AssetExternalId != request.AssetExternalId || report.RetrievedAtUtc == default)
            throw InvalidWellTest();
        if (report.Test is not { } test)
            return new(true, [], "NoTest");
        if (test.Validation is null || string.IsNullOrWhiteSpace(test.Id))
            throw InvalidWellTest();
        if (test.ReportedQuantityFields is not { } fields)
            return new(true, [], "DiscoveryUnavailable");
        if (fields.Count > ForeSiteLatestWellTestContract.QuantityFields.Count ||
            fields.Any(field => !ForeSiteLatestWellTestContract.QuantityFields.Contains(field, StringComparer.Ordinal)) ||
            fields.Distinct(StringComparer.Ordinal).Count() != fields.Count)
            throw InvalidWellTest();

        var summary = new GuidedReportedWellTest(test.SampleDate, test.TestEndDate, test.Status,
            test.Validation.ClassificationDescription, test.Validation.Description);
        return new(true, fields.Select(field => new GuidedDiscoveredQuantity(field, null, "Reported")).ToArray(),
            WellTest: summary);
    }

    private static WellSiteAutoPilotException InvalidWellTest() =>
        new("GUIDED_WELL_TEST_RESPONSE_INVALID", FailureKind.DependencyUnavailable,
            "The well test response did not match the requested asset or contract.");

    public async Task<GuidedSurfaceCards> ReadSurfaceCardsAsync(Guid draftId, int expectedRevision, Guid assetId,
        AccessSubject subject, CancellationToken cancellationToken = default)
    {
        var draft = await drafts.GetAsync(draftId, subject, cancellationToken);
        draft.RequireRevision(expectedRevision);
        var initial = await ReadContextAsync(subject, assetId, cancellationToken);
        await RequireMatchingAssetAsync(draft.Content.Manifest, initial, subject, cancellationToken);
        var rule = AdapterReadRules.Find(initial.Connection.AdapterKey);
        if (rule is null || rule.ResourceKind != initial.Binding.ResourceKind ||
            !rule.AllowsCurrentRead(ForeSiteSurfaceCard.Quantity, AdapterReadRules.StructuredCurrentCapability, ForeSiteSurfaceCard.SchemaId) ||
            (draft.Content.Manifest.RequiredAdapterKey is { } requiredAdapter &&
                !string.Equals(requiredAdapter, initial.Connection.AdapterKey, StringComparison.OrdinalIgnoreCase)))
            throw new WellSiteAutoPilotException("GUIDED_SURFACE_CARDS_UNSUPPORTED", FailureKind.Validation,
                "The selected connection does not support stored surface cards.");

        await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);
        EngineeringValue value;
        try
        {
            value = await gateway.GetCurrentAsync(new(initial.Connection.AdapterKey, initial.Connection.Id,
                initial.Connection.Revision, initial.Binding.ExternalId, ForeSiteSurfaceCard.Quantity,
                initial.Connection.Settings.ToDictionary(entry => entry.Key, entry => (string?)entry.Value, StringComparer.Ordinal)),
                cancellationToken);
        }
        catch (WellSiteAutoPilotException exception) when (exception.Code == "FORESITE_MEASUREMENT_MISSING")
        {
            await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);
            return new(null, null, []);
        }
        await RequireCurrentDraftSourceAsync(draftId, expectedRevision, assetId, subject, initial, cancellationToken);

        if (value.Quantity != ForeSiteSurfaceCard.Quantity || value.Value is not null ||
            value.StructuredValue is not { SchemaId: ForeSiteSurfaceCard.SchemaId } structured ||
            structured.Data.ValueKind != JsonValueKind.Array ||
            structured.Data.GetArrayLength() is < 1 or > ForeSiteSurfaceCard.MaximumCards || value.RetrievedAtUtc is null)
            throw InvalidSurfaceCards();
        try
        {
            var cards = structured.Data.Deserialize<ForeSiteSurfaceCard[]>(JsonSerializerOptions.Web) ?? throw InvalidSurfaceCards();
            if (cards.Any(card => card is null || string.IsNullOrWhiteSpace(card.Id) || card.Representation != "SURFACE" ||
                card.Kind is not ("CURRENT" or "FULL") ||
                !DateTimeOffset.TryParseExact(card.CapturedAtUtc, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var captured) ||
                captured == default || captured.Offset != TimeSpan.Zero ||
                card.PositionUnit != "in" || card.LoadUnit != "lb" || card.Position is null || card.Load is null ||
                card.Position.Count is < 2 or > ForeSiteSurfaceCard.MaximumPoints || card.Position.Count != card.Load.Count) ||
                cards.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != cards.Length ||
                cards.Select(card => card.CapturedAtUtc).Min(StringComparer.Ordinal) !=
                    value.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                throw InvalidSurfaceCards();
            return new(value.Quality, value.RetrievedAtUtc, cards);
        }
        catch (JsonException) { throw InvalidSurfaceCards(); }
    }

    private static WellSiteAutoPilotException InvalidSurfaceCards() =>
        new("GUIDED_SURFACE_CARDS_RESPONSE_INVALID", FailureKind.DependencyUnavailable,
            "The stored surface card data did not match the requested schema.");

    private bool IsValidWindow(DateTimeOffset startUtc, DateTimeOffset endUtc) =>
        startUtc != default && endUtc > startUtc && endUtc <= clock.GetUtcNow() &&
        endUtc - startUtc <= TimeSpan.FromDays(HistoricalReadLimits.MaximumWindowDays) &&
        startUtc.Ticks % TimeSpan.TicksPerSecond == 0 && endUtc.Ticks % TimeSpan.TicksPerSecond == 0;

    private async Task RequireCurrentDraftSourceAsync(Guid draftId, int expectedRevision, Guid assetId, AccessSubject subject,
        PreviewSource initial, CancellationToken cancellationToken)
    {
        var current = await ReadContextAsync(subject, assetId, cancellationToken);
        if (!SameSource(initial, current))
            throw new WellSiteAutoPilotException("GUIDED_LIVE_SOURCE_CHANGED", FailureKind.Conflict,
                "The sample asset or connection changed. Select it again.");
        (await drafts.GetAsync(draftId, subject, cancellationToken)).RequireRevision(expectedRevision);
    }

    private async Task RequireMatchingAssetAsync(LogicModuleManifest manifest, PreviewSource source,
        AccessSubject subject, CancellationToken cancellationToken)
    {
        if (!await MatchesAssetAsync(manifest, source, subject, cancellationToken))
            throw new WellSiteAutoPilotException("GUIDED_LIVE_ASSET_UNSUPPORTED", FailureKind.Validation,
                "The sample asset does not meet the draft asset requirements.");
    }

    private async Task<bool> MatchesAssetAsync(LogicModuleManifest manifest, PreviewSource source,
        AccessSubject subject, CancellationToken cancellationToken)
    {
        var role = manifest.AssetRequirements.Single();
        var assetType = await assets.GetAssetTypeAuthorizedAsync(subject, source.AssetTypeId, cancellationToken);
        return assetType is { IsActive: true } &&
            (role.RequiredAssetTypeKey is null || string.Equals(role.RequiredAssetTypeKey, assetType.Key, StringComparison.OrdinalIgnoreCase)) &&
            role.RequiredTraits.Count == 0;
    }

    private async Task<PreviewSource> ReadContextAsync(AccessSubject subject, Guid assetId, CancellationToken cancellationToken)
    {
        if (!await access.HasDeploymentPermissionAsync(subject, WellSitePermissions.GuidedDraftManage, cancellationToken) ||
            !await access.HasDeploymentPermissionAsync(subject, WellSitePermissions.ConnectionsRead, cancellationToken))
            throw new WellSiteAutoPilotException("GUIDED_LIVE_ACCESS_REQUIRED", FailureKind.Authorization,
                "Authoring and connection access are required for live preview.");
        var asset = await assets.GetAuthorizedAsync(subject, assetId, cancellationToken);
        if (asset is not { IsActive: true, OwnerConnectionId: { } connectionId }) throw SourceUnavailable();
        var source = await sources.GetConnectionSourceAuthorizedAsync(subject, assetId, connectionId, cancellationToken);
        if (source is not { IsActive: true } || source.Bindings.Count != 1) throw SourceUnavailable();
        var connection = await connections.FindAsync(connectionId, cancellationToken);
        if (connection is not { IsEnabled: true }) throw SourceUnavailable();
        return new(connection, source.Bindings[0], asset.AssetTypeId);
    }

    private static bool SameSource(PreviewSource initial, PreviewSource current) =>
        current.Connection.Id == initial.Connection.Id && current.Connection.Revision == initial.Connection.Revision &&
        current.Connection.AdapterKey == initial.Connection.AdapterKey && current.Binding == initial.Binding &&
        current.AssetTypeId == initial.AssetTypeId;

    private sealed record PreviewSource(ConnectionRecord Connection, AssetConnectionBinding Binding, Guid AssetTypeId);

    private static WellSiteAutoPilotException SourceUnavailable() => new("GUIDED_LIVE_SOURCE_UNAVAILABLE",
        FailureKind.NotFound, "An active, authorized imported asset with one source is required.");
}

public sealed record GuidedQuantityDiscovery(bool Supported, IReadOnlyList<GuidedDiscoveredQuantity> Quantities,
    string? EvidenceStatus = null, GuidedReportedWellTest? WellTest = null);
public sealed record GuidedReportedWellTest(DateTimeOffset? SampleDate, DateTimeOffset? TestEndDate,
    string? Status, string? Classification, string? Validation);
public sealed record GuidedDiscoveredQuantity(string Quantity, string? Unit, string Status);

public sealed record GuidedInputCompatibility(string InputId, string Quantity, string Status, string? ReasonCode);

public sealed record GuidedLivePreview(DeclarativeThresholdEvidence Evidence, IReadOnlyList<GuidedLiveReading> Readings);
public sealed record GuidedLiveReading(string InputId, string Quantity, decimal? Value, string Unit,
    DateTimeOffset TimestampUtc, string Quality, string? TimestampKind);

public sealed record GuidedSurfaceCards(string? Quality, DateTimeOffset? RetrievedAtUtc, IReadOnlyList<ForeSiteSurfaceCard> Cards);
