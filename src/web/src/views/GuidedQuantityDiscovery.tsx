import { useEffect, useState } from 'react';
import { discoverGuidedModuleQuantities } from '../api/generated/v1/sdk.gen';
import type { GuidedQuantityDiscoveryResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { formatTimestamp } from '../format';
import { t } from '../localization';

const sources = [
  { key: 'Surveillance', label: 'guided.surveillanceQuantities' },
  { key: 'DailyAverages', label: 'guided.dailyAverageQuantities' },
  { key: 'WellTrend', label: 'guided.wellTrendQuantities' },
  { key: 'WellTest', label: 'guided.wellTestQuantities' },
] as const;
type Capability = (typeof sources)[number]['key'];
export type UtcWindow = { startUtc: string; endUtc: string };
export type HistoryBounds =
  UtcWindow | { siteWindow: { endLocal: string; timeZoneId: string; durationMinutes: number } };

function trailingWindow(hours: number): UtcWindow {
  const end = Math.floor(Date.now() / 1000) * 1000;
  return { startUtc: new Date(end - hours * 3600000).toISOString(), endUtc: new Date(end).toISOString() };
}

const evidenceNotices = {
  ApplicabilityUnverified: 'guided.surveillanceApplicabilityUnverified',
  DiscoveryUnavailable: 'guided.quantityDiscoveryFailed',
  NoTest: 'guided.noReportedWellTest',
} as const;
const quantityStatuses = {
  Observed: 'guided.quantityObserved',
  NoSamples: 'guided.quantityNoSamples',
  CurrentObserved: 'guided.quantityCurrentObserved',
  NoReading: 'guided.quantityNoReading',
  Reported: 'guided.quantityReportedOnTest',
} as const;

type Discovery =
  | {
      kind: 'error';
      message: 'guided.quantityDiscoveryFailed' | 'guided.siteTimeUnresolved' | 'guided.siteZoneInvalid';
    }
  | { kind: 'result'; data: GuidedQuantityDiscoveryResponse; bounds?: HistoryBounds };

export function GuidedQuantityDiscovery({
  draftId,
  revision,
  assetId,
  inputs,
  busy,
  onHistory,
  onDaily,
  onUseDailyField,
  declaredDaily = [],
  onChange,
}: {
  draftId: string;
  revision: number;
  assetId: string;
  inputs: readonly { id: string; quantity: string }[];
  busy: boolean;
  onHistory: (inputId: string, bounds: HistoryBounds) => void;
  onDaily: (bounds: UtcWindow) => void;
  /** Offered for a discovered production-day field when the draft can take new inputs. */
  onUseDailyField?: (quantity: string, unit: string | null) => void;
  declaredDaily?: readonly string[];
  onChange: () => void;
}) {
  const [capability, setCapability] = useState<Capability | ''>('');
  const [hours, setHours] = useState('4');
  const [anchor, setAnchor] = useState('now');
  const [endLocal, setEndLocal] = useState('');
  const [timeZoneId, setTimeZoneId] = useState('');
  const [quantity, setQuantity] = useState('');
  const [result, setResult] = useState<Discovery | null>(null);
  const [attempt, setAttempt] = useState(0);
  const hasSiteWindow = capability === 'WellTrend' && anchor === 'site';
  const ready = Boolean(capability) && (!hasSiteWindow || Boolean(endLocal && timeZoneId.trim()));

  function reset() {
    setResult(null);
    setQuantity('');
    onChange();
  }

  useEffect(() => {
    if (!ready) return;
    const controller = new AbortController();
    // Wait for site time and zone typing before querying the provider.
    const timer = setTimeout(() => void discover(), hasSiteWindow ? 350 : 0);
    async function discover() {
      const bounds: HistoryBounds | undefined = hasSiteWindow
        ? { siteWindow: { endLocal, timeZoneId: timeZoneId.trim(), durationMinutes: Number(hours) * 60 } }
        : capability === 'DailyAverages'
          ? trailingWindow(7 * 24)
          : capability === 'WellTrend'
            ? trailingWindow(Number(hours))
            : undefined;
      try {
        const { data, error } = await discoverGuidedModuleQuantities({
          path: { id: draftId },
          body: { expectedRevision: revision, assetId, capability, ...bounds },
          signal: controller.signal,
        });
        if (controller.signal.aborted) return;
        if (data) {
          setResult({ kind: 'result', data, bounds });
          return;
        }
        const code = error && typeof error === 'object' && 'code' in error ? error.code : undefined;
        setResult({
          kind: 'error',
          message:
            code === 'GUIDED_HISTORY_SITE_TIME_UNRESOLVED'
              ? 'guided.siteTimeUnresolved'
              : code === 'GUIDED_HISTORY_SITE_ZONE_INVALID'
                ? 'guided.siteZoneInvalid'
                : 'guided.quantityDiscoveryFailed',
        });
      } catch {
        if (!controller.signal.aborted) setResult({ kind: 'error', message: 'guided.quantityDiscoveryFailed' });
      }
    }
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [assetId, attempt, capability, draftId, endLocal, hasSiteWindow, hours, ready, revision, timeZoneId]);

  const historical = capability === 'WellTrend' || capability === 'DailyAverages';
  const catalog = result?.kind === 'result' && result.data.supported ? (result.data.quantities ?? []) : [];
  const observed = catalog.flatMap((entry) =>
    entry.status === 'Observed' && entry.quantity ? [{ ...entry, quantity: entry.quantity }] : [],
  );
  const evidence = historical ? catalog.filter((entry) => entry.status !== 'Observed') : catalog;
  const selected = observed.find((entry) => entry.quantity === quantity);
  const matchingInput = inputs.find((input) => input.quantity === selected?.quantity);
  const canPreview =
    capability === 'DailyAverages' ? selected?.quantity === 'TotalFluidRateInferred' : Boolean(matchingInput);
  const bounds = result?.kind === 'result' ? result.bounds : undefined;
  const window = bounds && 'startUtc' in bounds ? bounds : undefined;

  const evidenceStatus = result?.kind === 'result' ? result.data.evidenceStatus : undefined;
  const evidenceNotice = evidenceStatus ? evidenceNotices[evidenceStatus as keyof typeof evidenceNotices] : undefined;

  return (
    <div>
      <div className="guided-authoring-fields">
        <SelectField
          label={t('guided.quantitySource')}
          value={capability}
          disabled={busy}
          onChange={(event) => {
            reset();
            setCapability(event.target.value as Capability | '');
          }}
        >
          <option value="">{t('guided.chooseQuantitySource')}</option>
          {sources.map((source) => (
            <option key={source.key} value={source.key}>
              {t(source.label)}
            </option>
          ))}
        </SelectField>
        {capability === 'WellTrend' && (
          <>
            <SelectField
              label={t('guided.historyAnchor')}
              value={anchor}
              disabled={busy}
              onChange={(event) => {
                reset();
                setAnchor(event.target.value);
              }}
            >
              <option value="now">{t('guided.historyNow')}</option>
              <option value="site">{t('guided.historySiteTime')}</option>
            </SelectField>
            {hasSiteWindow && (
              <>
                <TextField
                  type="datetime-local"
                  label={t('guided.historyEndLocal')}
                  value={endLocal}
                  disabled={busy}
                  onChange={(event) => {
                    reset();
                    setEndLocal(event.target.value);
                  }}
                />
                <TextField
                  label={t('guided.historySiteZone')}
                  helper={t('guided.historySiteZoneExample')}
                  value={timeZoneId}
                  maxLength={128}
                  disabled={busy}
                  onChange={(event) => {
                    reset();
                    setTimeZoneId(event.target.value);
                  }}
                />
              </>
            )}
            <SelectField
              label={t('guided.historyWindow')}
              value={hours}
              disabled={busy}
              onChange={(event) => {
                reset();
                setHours(event.target.value);
              }}
            >
              <option value="1">{t('guided.historyOneHour')}</option>
              <option value="4">{t('guided.historyFourHours')}</option>
              <option value="24">{t('guided.historyDay')}</option>
            </SelectField>
          </>
        )}
        {historical && (
          <SelectField
            label={t('guided.historyInput')}
            value={quantity}
            disabled={busy || !observed.length}
            onChange={(event) => {
              onChange();
              setQuantity(event.target.value);
            }}
          >
            <option value="">{t('guided.chooseHistoricalQuantity')}</option>
            {observed.map((entry) => (
              <option key={entry.quantity} value={entry.quantity}>
                {entry.unit ? `${entry.quantity} (${entry.unit})` : entry.quantity}
              </option>
            ))}
          </SelectField>
        )}
        {capability === 'DailyAverages' && selected && onUseDailyField && (
          <ActionButton
            label={t('guided.useDailyField')}
            disabled={busy || declaredDaily.includes(selected.quantity)}
            onClick={() => onUseDailyField(selected.quantity, selected.unit ?? null)}
          />
        )}
        {selected &&
          canPreview &&
          bounds &&
          (capability === 'DailyAverages' && window ? (
            <ActionButton label={t('guided.readProductionDays')} disabled={busy} onClick={() => onDaily(window)} />
          ) : (
            <ActionButton
              label={t('guided.readHistory')}
              disabled={busy}
              onClick={() => {
                if (matchingInput) onHistory(matchingInput.id, bounds);
              }}
            />
          ))}
      </div>
      <div aria-live="polite">
        {ready && !result && <Notice>{t('guided.quantityDiscoveryLoading')}</Notice>}
        {selected && !canPreview && (
          <Notice>
            {t(capability === 'WellTrend' ? 'guided.trendRequiresDraftInput' : 'guided.dailyDiscoveryOnly')}
          </Notice>
        )}
        {result?.kind === 'error' && (
          <Notice tone="warning">
            {t(result.message)}
            <ActionButton
              label={t('guided.discoverQuantityAction')}
              disabled={busy}
              onClick={() => {
                reset();
                setAttempt(attempt + 1);
              }}
            />
          </Notice>
        )}
        {result?.kind === 'result' &&
          (!result.data.supported ? (
            <Notice>{t('guided.quantityUnsupported')}</Notice>
          ) : (
            <>
              {evidenceNotice && <Notice>{t(evidenceNotice)}</Notice>}
              {window && (
                <p>
                  {t('guided.quantityWindow')} {formatTimestamp(window.startUtc)} – {formatTimestamp(window.endUtc)}
                </p>
              )}
              {result.data.wellTest && <ReportedWellTest test={result.data.wellTest} />}
              {historical && !catalog.length && <Notice>{t('guided.quantityNoSamples')}</Notice>}
              {evidence.length ? (
                <ul>
                  {evidence.map((entry) => (
                    <li key={entry.quantity}>
                      {entry.quantity}
                      {entry.unit && <> ({entry.unit})</>} ·{' '}
                      {t(
                        quantityStatuses[entry.status as keyof typeof quantityStatuses] ?? 'guided.quantityUnavailable',
                      )}
                    </li>
                  ))}
                </ul>
              ) : result.data.wellTest ? (
                <Notice>{t('guided.noWellTestQuantities')}</Notice>
              ) : null}
            </>
          ))}
      </div>
    </div>
  );
}

function ReportedWellTest({ test }: { test: NonNullable<GuidedQuantityDiscoveryResponse['wellTest']> }) {
  const { status: reportedStatus } = test;
  return (
    <div>
      <p>{t('guided.reportedWellTestEvidence')}</p>
      <dl>
        {test.sampleDate && (
          <>
            <dt>{t('guided.wellTestSampleDate')}</dt>
            <dd>
              <time dateTime={test.sampleDate}>{test.sampleDate}</time>
            </dd>
          </>
        )}
        {test.testEndDate && (
          <>
            <dt>{t('guided.wellTestEndDate')}</dt>
            <dd>
              <time dateTime={test.testEndDate}>{test.testEndDate}</time>
            </dd>
          </>
        )}
        {reportedStatus && (
          <>
            <dt>{t('guided.wellTestStatus')}</dt>
            <dd>{reportedStatus}</dd>
          </>
        )}
        {test.classification && (
          <>
            <dt>{t('guided.wellTestClassification')}</dt>
            <dd>{test.classification}</dd>
          </>
        )}
        {test.validation && (
          <>
            <dt>{t('guided.wellTestValidation')}</dt>
            <dd>{test.validation}</dd>
          </>
        )}
      </dl>
    </div>
  );
}
