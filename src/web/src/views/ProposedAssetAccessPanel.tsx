import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import type { PermissionDefinition } from './roleCatalog';
import { loadProposedAssetAccess } from './proposedAssetAccess';
import type { ProposedAssetAccess } from './proposedAssetAccess';

export function ProposedAssetAccessPanel({ userId, permissions }: {
  userId: string;
  permissions: readonly PermissionDefinition[];
}) {
  const [permission, setPermission] = useState('');
  const [result, setResult] = useState<ProposedAssetAccess | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);
  const requestRef = useRef<AbortController | null>(null);
  useEffect(() => () => requestRef.current?.abort(), []);

  const scoped = permissions.filter(x => x.scopeKind === 'ResourceScoped')
    .sort((a, b) => a.key.localeCompare(b.key));

  const load = async (offset: number) => {
    if (!scoped.some(item => item.key === permission)) return;
    requestRef.current?.abort();
    const controller = new AbortController();
    requestRef.current = controller;
    setLoading(true); setError(false); setResult(null);
    try {
      const page = await loadProposedAssetAccess(
        userId, permission, offset, controller.signal);
      if (!controller.signal.aborted) setResult(page);
    } catch {
      if (!controller.signal.aborted) setError(true);
    } finally {
      if (requestRef.current === controller) {
        requestRef.current = null; setLoading(false);
      }
    }
  };

  return (
    <section className="panel" aria-label={t('grants.proposedAssetsHeading')}>
      <div className="panel-header"><h3>{t('grants.proposedAssetsHeading')}</h3></div>
      <p className="access-note">{t('grants.proposedAssetsExplanation')}</p>
      <div className="grant-fields">
        <label>{t('grants.inspectorPermission')}
          <select value={permission} disabled={loading}
            onChange={event => {
              requestRef.current?.abort();
              setPermission(event.target.value);
              setResult(null); setError(false); setLoading(false);
            }}>
            <option value="">{t('grants.proposedAssetsSelect')}</option>
            {scoped.map(item => <option key={item.key} value={item.key}>
              {item.key}
            </option>)}
          </select>
        </label>
        <button type="button" disabled={!permission || loading}
          onClick={() => void load(0)}>
          {loading ? t('access.loading') : t('grants.inspect')}
        </button>
      </div>
      {error && <p role="alert" className="execution-detail-error">
        {t('grants.proposedAssetsFailed')}
      </p>}
      {result && (
        <div role="region" aria-label={t('grants.proposedAssetsHeading')}>
          <p className="access-warning">{t('grants.inspectorDiagnostic')}</p>
          {result.assets.length === 0
            ? <p>{t('grants.proposedAssetsEmpty')}</p>
            : <ul>{result.assets.map(item =>
              <li key={item.id}>{item.name} <small>{item.id}</small></li>)}</ul>}
          <div className="execution-detail-actions">
            <button type="button" className="secondary-button"
              disabled={loading || result.offset === 0}
              onClick={() => void load(Math.max(0, result.offset - result.limit))}>
              {t('grants.proposedAssetsPrevious')}
            </button>
            <span>{t('grants.proposedAssetsPage', {
              start: result.offset + 1, count: result.assets.length,
            })}</span>
            <button type="button" className="secondary-button"
              disabled={loading || result.assets.length < result.limit ||
                result.offset + result.limit > 1_000_000}
              onClick={() => void load(result.offset + result.limit)}>
              {t('grants.proposedAssetsNext')}
            </button>
          </div>
        </div>
      )}
    </section>
  );
}
