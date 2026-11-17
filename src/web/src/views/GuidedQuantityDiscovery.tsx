import { useEffect, useRef, useState } from 'react';
import { discoverGuidedModuleQuantities } from '../api/generated/v1/sdk.gen';
import type { GuidedQuantityDiscoveryResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { SelectField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { formatTimestamp } from '../format';
import { t } from '../localization';

type Capability = 'DailyAverages' | 'WellTrend';
type Discovery =
  | { kind: 'loading' }
  | { kind: 'error' }
  | { kind: 'result'; data: GuidedQuantityDiscoveryResponse; startUtc: string; endUtc: string };

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
    const startUtc = new Date(end - (capability === 'DailyAverages' ? 7 : 1) * 86400000).toISOString();
    const endUtc = new Date(end).toISOString();
    try {
      const { data } = await discoverGuidedModuleQuantities({
        path: { id: draftId },
        body: { expectedRevision: revision, assetId, capability, startUtc, endUtc },
        signal: controller.signal,
      });
      if (!controller.signal.aborted) {
        setResult(data ? { kind: 'result', data, startUtc, endUtc } : { kind: 'error' });
      }
    } catch {
      if (!controller.signal.aborted) setResult({ kind: 'error' });
    }
  }

  return (
    <details>
      <summary>{t('guided.discoverQuantities')}</summary>
      <div className="guided-authoring-fields">
        <SelectField
          label={t('guided.quantitySource')}
          value={capability}
          onChange={(event) => {
            request.current?.abort();
            setCapability(event.target.value === 'DailyAverages' ? 'DailyAverages' : 'WellTrend');
            setResult(null);
          }}
        >
          <option value="WellTrend">{t('guided.wellTrendQuantities')}</option>
          <option value="DailyAverages">{t('guided.dailyAverageQuantities')}</option>
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
          (result.data.supported ? (
            <>
              <p>
                {t('guided.quantityWindow')} {formatTimestamp(result.startUtc)} – {formatTimestamp(result.endUtc)}
              </p>
              <ul>
                {result.data.quantities?.map((entry) => (
                  <li key={entry.quantity}>
                    {entry.quantity}
                    {entry.unit && <> ({entry.unit})</>} ·{' '}
                    {t(
                      entry.status === 'Observed'
                        ? 'guided.quantityObserved'
                        : entry.status === 'NoSamples'
                          ? 'guided.quantityNoSamples'
                          : 'guided.quantityUnavailable',
                    )}
                  </li>
                ))}
              </ul>
            </>
          ) : (
            <Notice>{t('guided.quantityUnsupported')}</Notice>
          ))}
      </div>
    </details>
  );
}
