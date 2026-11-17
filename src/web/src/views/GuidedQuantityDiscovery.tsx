import { useEffect, useRef, useState } from 'react';
import { discoverGuidedModuleQuantities } from '../api/generated/v1/sdk.gen';
import type { GuidedQuantityDiscoveryResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { SelectField } from '../components/FormFields';
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
type HistoryWindow = { startUtc: string; endUtc: string };

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
  | { kind: 'loading' }
  | { kind: 'error' }
  | { kind: 'result'; data: GuidedQuantityDiscoveryResponse; window?: HistoryWindow };

export function GuidedQuantityDiscovery({
  draftId,
  revision,
  assetId,
}: {
  draftId: string;
  revision: number;
  assetId: string;
}) {
  const [capability, setCapability] = useState<Capability>('WellTrend');
  const [result, setResult] = useState<Discovery | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);

  async function discover() {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setResult({ kind: 'loading' });
    const end = Math.floor(Date.now() / 1000) * 1000;
    const window =
      capability === 'DailyAverages' || capability === 'WellTrend'
        ? {
            startUtc: new Date(end - (capability === 'DailyAverages' ? 7 : 1) * 86400000).toISOString(),
            endUtc: new Date(end).toISOString(),
          }
        : undefined;
    try {
      const { data } = await discoverGuidedModuleQuantities({
        path: { id: draftId },
        body: { expectedRevision: revision, assetId, capability, ...window },
        signal: controller.signal,
      });
      if (!controller.signal.aborted) {
        setResult(data ? { kind: 'result', data, window } : { kind: 'error' });
      }
    } catch {
      if (!controller.signal.aborted) setResult({ kind: 'error' });
    }
  }

  const evidenceStatus = result?.kind === 'result' ? result.data.evidenceStatus : undefined;
  const evidenceNotice = evidenceStatus ? evidenceNotices[evidenceStatus as keyof typeof evidenceNotices] : undefined;

  return (
    <details>
      <summary>{t('guided.discoverQuantities')}</summary>
      <div className="guided-authoring-fields">
        <SelectField
          label={t('guided.quantitySource')}
          value={capability}
          onChange={(event) => {
            request.current?.abort();
            setCapability(event.target.value as Capability);
            setResult(null);
          }}
        >
          {sources.map((source) => (
            <option key={source.key} value={source.key}>
              {t(source.label)}
            </option>
          ))}
        </SelectField>
        <ActionButton
          label={t('guided.discoverQuantityAction')}
          disabled={result?.kind === 'loading'}
          onClick={() => void discover()}
        />
      </div>
      <div aria-live="polite">
        {result?.kind === 'error' && <Notice tone="warning">{t('guided.quantityDiscoveryFailed')}</Notice>}
        {result?.kind === 'result' &&
          (!result.data.supported ? (
            <Notice>{t('guided.quantityUnsupported')}</Notice>
          ) : (
            <>
              {evidenceNotice && <Notice>{t(evidenceNotice)}</Notice>}
              {result.window && (
                <p>
                  {t('guided.quantityWindow')} {formatTimestamp(result.window.startUtc)} –{' '}
                  {formatTimestamp(result.window.endUtc)}
                </p>
              )}
              {result.data.wellTest && <ReportedWellTest test={result.data.wellTest} />}
              {result.data.quantities?.length ? (
                <ul>
                  {result.data.quantities.map((entry) => (
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
    </details>
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
