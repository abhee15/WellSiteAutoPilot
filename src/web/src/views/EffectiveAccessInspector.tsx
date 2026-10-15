import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import type { AssetChoice } from './scopedAccessGrants';
import { loadEffectiveAccess } from './effectiveAccessInspection';
import type { EffectiveAccessInspection } from './effectiveAccessInspection';

type Props = { userId: string; assets: readonly AssetChoice[] };

/**
 * Read-only operator-facing admin diagnostics: no browser-side authorization
 * estimates, no cutover action and no accidental blanket parity assertion.
 */
export function EffectiveAccessInspector({ userId, assets }: Props) {
  const [assetId, setAssetId] = useState('');
  const [result, setResult] = useState<EffectiveAccessInspection | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);
  const request = useRef<AbortController | null>(null);

  useEffect(() => {
    request.current?.abort();
    setAssetId(''); setResult(null); setLoading(false); setError(false);
    return () => { request.current?.abort(); };
  }, [userId]);

  const inspect = async () => {
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    setLoading(true); setError(false); setResult(null);
    try {
      const response = await loadEffectiveAccess(
        userId, assetId || null, abort.signal);
      if (!abort.signal.aborted) setResult(response);
    } catch {
      if (!abort.signal.aborted) setError(true);
    } finally {
      if (request.current === abort) {
        request.current = null; setLoading(false);
      }
    }
  };

  const active = assets.filter(asset => asset.isActive).sort(
    (a, b) => a.name.localeCompare(b.name) || a.id.localeCompare(b.id));

  return (
    <section className="panel access-inspector" aria-label={t('grants.inspectorHeading')}>
      <div className="panel-header"><h3>{t('grants.inspectorHeading')}</h3></div>
      <p className="access-note">{t('grants.inspectorExplain')}</p>
      <div className="grant-fields">
        <label>{t('grants.inspectorAsset')}
          <select value={assetId} disabled={loading}
            onChange={event => {
              request.current?.abort();
              setAssetId(event.target.value); setResult(null); setError(false);
            }}>
            <option value="">{t('grants.inspectorGlobal')}</option>
            {active.map(asset =>
              <option key={asset.id} value={asset.id}>{asset.name}</option>)}
          </select>
        </label>
        <button type="button" disabled={loading} onClick={() => void inspect()}>
          {loading ? t('access.loading') : t('grants.inspect')}
        </button>
      </div>
      {error && <p role="alert" className="execution-detail-error">
        {t('grants.inspectorFailed')}
      </p>}
      {result && (
        <div className="access-inspector-results" role="region"
          aria-label={t('grants.inspectorResult')}>
          <p className="access-warning">{t('grants.inspectorDiagnostic')}</p>
          <p>{result.assetName ?? t('grants.inspectorGlobal')}: {
            t('grants.inspectorWidening', { count: result.wideningCount })}, {
            t('grants.inspectorReductions', { count: result.reductionCount })}</p>
          <div className="grant-asset-list">
            <table>
              <thead><tr>
                <th scope="col">{t('grants.inspectorPermission')}</th>
                <th scope="col">{t('grants.inspectorLive')}</th>
                <th scope="col">{t('grants.inspectorProposed')}</th>
                <th scope="col">{t('grants.inspectorChange')}</th>
              </tr></thead>
              <tbody>
                {result.permissions.map(item => {
                  const change = item.proposedAllowed === item.legacyAllowed
                    ? t('grants.inspectorUnchanged')
                    : item.proposedAllowed
                      ? t('grants.inspectorExpansion')
                      : t('grants.inspectorReduction');
                  return <tr key={item.permission}>
                    <th scope="row">{item.permission}</th>
                    <td>{item.legacyAllowed ? t('grants.allowed') : t('grants.denied')}</td>
                    <td>{item.proposedAllowed ? t('grants.allowed') : t('grants.denied')}</td>
                    <td>{change}</td>
                  </tr>;
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </section>
  );
}
