import { useRef, useState } from 'react';
import { t } from '../localization';
import { ConnectionsError, importFromConnection, previewImport } from './connections';
import type { Adapter, Connection, ImportGroup, ImportPreview, ImportResult, ImportSelection } from './connections';

const renderBudget = 400;

function importError(error: unknown): string {
  if (!(error instanceof ConnectionsError)) return t('connections.errUncertain');
  switch (error.kind) {
    case 'needsAllAssets': return t('connections.importNeedsAllAssets');
    case 'planChanged': return t('connections.importPlanChanged');
    case 'importNotAllowed': return t('connections.importNotAllowed');
    case 'incomplete': return t('connections.importIncomplete', { detail: error.detail ?? '' });
    case 'invalidLevels': return t('connections.importLevelsInvalid');
    case 'tooLarge': return t('connections.errTooLarge', { detail: error.detail ?? '' });
    case 'importTooLarge': return error.detail ?? t('connections.importTooLarge');
    case 'snapshotExpired': return t('connections.errSnapshotExpired');
    case 'disabled': return t('connections.errDisabled');
    case 'accessDenied': return t('connections.errDenied');
    case 'notFound': return t('connections.errNotFound');
    case 'gatewayUnavailable': return t('connections.errGateway');
    default: return t('connections.errUncertain');
  }
}

/** Choose how discovered objects are grouped, review exactly what would be created, then import it. */
export function ConnectionImport({ connection, adapter, selection = null, selectedCount = null, totalCount = null, disabled = false, onBusyChange }: {
  connection: Connection; adapter: Adapter;
  /** The part of what was found that the person chose; null means discovery or selection is not ready for review. */
  selection?: ImportSelection | null; selectedCount?: number | null; totalCount?: number | null;
  disabled?: boolean; onBusyChange?: (busy: boolean) => void;
}) {
  const fields = adapter.groupingFields;
  const label = (key: string) => fields.find(field => field.key === key)?.label ?? key;
  const recommended = ['assetName', 'wellType', 'deviceType'];
  const has = (keys: string[]) => keys.every(key => fields.some(field => field.key === key));
  const presets = [
    ...(has(recommended) ? [{ key: 'recommended', label: t('hierarchy.recommended'), levels: recommended }] : []),
    ...(has(['assetName']) ? [{ key: 'asset', label: t('hierarchy.byAsset'), levels: ['assetName'] }] : []),
    ...(has(['wellType', 'deviceType']) ? [{ key: 'device', label: t('hierarchy.byDevice'), levels: ['wellType', 'deviceType'] }] : []),
    { key: 'flat', label: t('hierarchy.flat'), levels: [] },
  ];
  const [levels, setLevels] = useState<string[]>(() => presets[0].levels);
  const [nextLevel, setNextLevel] = useState('');
  const pending = useRef(false);
  const [reviewed, setReviewed] = useState<{ plan: ImportPreview; key: string } | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [busy, setBusy] = useState<'review' | 'import' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const unused = fields.filter(field => !levels.includes(field.key));
  // A review belongs to the selection it was made for; choosing something else makes it stale.
  const selectionKey = JSON.stringify([selection, levels, connection.revision]);
  const ready = !disabled && selection !== null && selectedCount !== null && selectedCount > 0 && selectedCount <= 5000;
  const preview = reviewed && reviewed.key === selectionKey ? reviewed.plan : null;
  const setPreview = (plan: ImportPreview | null) => setReviewed(plan ? { plan, key: selectionKey } : null);

  const changeLevels = (next: string[]) => {
    setLevels(next); setNextLevel(''); setPreview(null); setResult(null); setConfirming(false); setError(null);
  };
  const move = (index: number, by: number) => {
    const next = [...levels];
    [next[index], next[index + by]] = [next[index + by], next[index]];
    changeLevels(next);
  };

  const review = async () => {
    if (!ready || pending.current) return;
    pending.current = true; onBusyChange?.(true);
    setBusy('review'); setError(null); setResult(null); setConfirming(false);
    try { setPreview(await previewImport(connection, levels, selection)); } catch (caught) {
      setPreview(null); setError(importError(caught));
    } finally { pending.current = false; setBusy(null); onBusyChange?.(false); }
  };

  const apply = async () => {
    if (!preview || !ready || pending.current) return;
    pending.current = true; onBusyChange?.(true);
    setBusy('import'); setError(null);
    try {
      setResult(await importFromConnection(connection, levels, preview.fingerprint, selection));
      setPreview(null); setConfirming(false);
    } catch (caught) {
      setError(importError(caught)); setConfirming(false);
      // The source or the Assets moved on: the reviewed plan no longer applies, so it is not left on screen.
      if (caught instanceof ConnectionsError && (caught.kind === 'planChanged' || caught.kind === 'snapshotExpired')) setPreview(null);
    } finally { pending.current = false; setBusy(null); onBusyChange?.(false); }
  };

  return (
    <section className="connection-import" aria-label={t('connections.importHeading')}>
      <p className="connection-step">{t('connections.stageImport')}</p>
      <h3>{t('connections.importHeading')}</h3>
      <p className="connection-help">{t('connections.importDestination', { name: connection.name })}</p>
      <p className="connection-help">{t('connections.importHelp')}</p>
      {selection && selectedCount !== null && totalCount !== null && (
        <p role="status" className="connection-help">
          {selectedCount === totalCount
            ? t('connections.importSelectedAll', { total: totalCount })
            : t('connections.importSelected', { count: selectedCount, total: totalCount })}
        </p>
      )}
      <section className="hierarchy-builder" aria-label={t('hierarchy.heading')}>
        <h4>{t('hierarchy.heading')}</h4>
        <p className="connection-help">{t('hierarchy.help')}</p>
        <div className="hierarchy-presets" role="group" aria-label={t('hierarchy.presets')}>
          {presets.map(preset => <button key={preset.key} type="button" className="secondary-button" disabled={busy !== null || disabled}
            aria-pressed={JSON.stringify(levels) === JSON.stringify(preset.levels)} onClick={() => changeLevels(preset.levels)}>{preset.label}</button>)}
        </div>
        <ol className="hierarchy-path" aria-label={t('hierarchy.path')}>
          {[connection.name, ...levels.map(label), t('hierarchy.wells')].map((name, index) => <li key={index}>{name}</li>)}
        </ol>
        {levels.length === 0 && <p className="connection-help">{t('connections.importNoLevels')}</p>}
        <details className="hierarchy-custom"><summary>{t('hierarchy.customize')}</summary>
          <ol className="connection-levels">
            {levels.map((key, index) => <li key={key}>
              <span>{t('connections.importLevel', { number: index + 1, field: label(key) })}</span>
              <div className="connection-actions">
                <button type="button" className="secondary-button" disabled={busy !== null || disabled || index === 0}
                  aria-label={t('connections.importMoveUp', { field: label(key) })} onClick={() => move(index, -1)}>↑</button>
                <button type="button" className="secondary-button" disabled={busy !== null || disabled || index === levels.length - 1}
                  aria-label={t('connections.importMoveDown', { field: label(key) })} onClick={() => move(index, 1)}>↓</button>
                <button type="button" className="secondary-button" disabled={busy !== null || disabled}
                  aria-label={t('connections.importRemove', { field: label(key) })} onClick={() => changeLevels(levels.filter(item => item !== key))}>×</button>
              </div>
            </li>)}
          </ol>
          {unused.length > 0 && levels.length < 5 && <div className="connection-control-row">
            <label><span>{t('connections.importAddLevel')}</span><select value={nextLevel} disabled={busy !== null || disabled} onChange={event => setNextLevel(event.target.value)}>
              <option value="">{t('connections.importChooseLevel')}</option>
              {unused.map(field => <option key={field.key} value={field.key}>{field.label}</option>)}
            </select></label>
            <button type="button" className="secondary-button" disabled={busy !== null || disabled || !unused.some(field => field.key === nextLevel)}
              onClick={() => { changeLevels([...levels, nextLevel]); setNextLevel(''); }}>{t('hierarchy.add')}</button>
          </div>}
        </details>
      </section>
      {!selection && <p role="status" className="connection-help">{t('hierarchy.needsSelection')}</p>}
      {selectedCount !== null && selectedCount > 5000 && <p role="status" className="connection-help">{t('inventory.batchLimit')}</p>}
      <div className="hierarchy-review-action">
        <p className="connection-help">{t('hierarchy.reviewHelp')}</p>
        <button type="button" className="primary-button" disabled={busy !== null || !ready} onClick={() => void review()}>
          {busy === 'review' ? t('connections.importReviewing') : t('connections.importReview')}
        </button>
      </div>
      {error && <p role="alert" className="status-error">{error}</p>}
      {preview && <ImportReview preview={preview} label={label} busy={busy} disabled={!ready} confirming={confirming}
        onConfirm={() => setConfirming(true)} onCancel={() => setConfirming(false)} onApply={() => void apply()} />}
      {result && (
        <div role="status" className="connection-test connection-test-healthy">
          <strong>{t('connections.importDone', { wells: result.wellsCreated, groups: result.groupsCreated })}</strong>
          {result.groupsReused > 0 && <span>{t('connections.importReused', { count: result.groupsReused })}</span>}
          {result.alreadyImported > 0 && <span>{t('connections.importSkipped', { count: result.alreadyImported })}</span>}
          <a href="#assets">{t('connections.importOpenAssets')}</a>
          <a href={'#assets/' + encodeURIComponent('new-view:' + connection.id)}>{t('assetViews.create')}</a>
        </div>
      )}
    </section>
  );
}

function ImportReview({ preview, label, busy, disabled, confirming, onConfirm, onCancel, onApply }: {
  preview: ImportPreview; label: (key: string) => string; busy: 'review' | 'import' | null; disabled: boolean; confirming: boolean;
  onConfirm: () => void; onCancel: () => void; onApply: () => void;
}) {
  const budget = { left: renderBudget };
  return (
    <div className="connection-review" aria-label={t('connections.importReviewLabel')} role="group">
      <p role="status">
        <strong>{t('connections.importSummary', { wells: preview.objectCount, groups: preview.groupCount })}</strong>
        {preview.levels.length > 0 && ' ' + t('connections.importGroupedBy', { fields: preview.levels.map(label).join(' › ') })}
      </p>
      {preview.alreadyImported > 0 && <p className="connection-help">{t('connections.importSkipped', { count: preview.alreadyImported })}</p>}
      {preview.fallbackObjectCount > 0 && <p className="connection-help">{t('connections.importFallback', { count: preview.fallbackObjectCount })}</p>}
      {preview.typesToCreate.length > 0 && <p className="connection-help">{t('connections.importTypes', { types: preview.typesToCreate.join(', ') })}</p>}
      {preview.warnings.map(warning => <p key={warning} role="status" className="connection-help">{warning}</p>)}
      {preview.conflicts.map(conflict => <p key={conflict} role="alert" className="status-error">{conflict}</p>)}
      {preview.actingAs && <p className="connection-help">{t('connections.findActingAs', { account: preview.actingAs })}</p>}
      {preview.groups.length > 0 && <ul className="connection-tree">{preview.groups.map(group => <GroupNode key={group.name} group={group} budget={budget} />)}</ul>}
      {preview.rootObjectCount > 0 && <p className="connection-help">{t('connections.importRoot', { count: preview.rootObjectCount })}</p>}
      {preview.objectCount === 0
        ? <p className="empty-state">{t('connections.importNothing')}</p>
        : confirming ? (
          <div className="connection-actions">
            <span>{t('connections.importConfirm', { count: preview.objectCount })}</span>
            <button type="button" className="primary-button" disabled={busy !== null || disabled || !preview.canImport} onClick={onApply}>
              {busy === 'import' ? t('connections.importing') : t('connections.importYes')}
            </button>
            <button type="button" className="secondary-button" disabled={busy !== null || disabled} onClick={onCancel}>{t('connections.importNo')}</button>
          </div>
        ) : (
          <button type="button" className="primary-button" disabled={busy !== null || disabled || !preview.canImport} onClick={onConfirm}>
            {t('connections.importButton', { count: preview.objectCount })}
          </button>
        )}
    </div>
  );
}

function GroupNode({ group, budget }: { group: ImportGroup; budget: { left: number } }) {
  if (budget.left <= 0) return null;
  budget.left -= 1;
  return (
    <li>
      <span>{group.name}{group.isFallback ? ` (${t('connections.importFallbackTag')})` : ''}</span>
      {' '}<small>{t('connections.importGroupCount', { count: group.objectCount })}</small>
      {group.sampleNames.length > 0 && <small className="connection-help"> — {group.sampleNames.join(', ')}</small>}
      {group.children.length > 0 && <ul>{group.children.map(child => <GroupNode key={child.name} group={child} budget={budget} />)}</ul>}
    </li>
  );
}
