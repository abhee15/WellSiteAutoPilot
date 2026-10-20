import { useState } from 'react';
import { t } from '../localization';
import { ConnectionsError, importFromConnection, previewImport } from './connections';
import type { Adapter, Connection, ImportGroup, ImportPreview, ImportResult } from './connections';

const renderBudget = 400;

function importError(error: unknown): string {
  if (!(error instanceof ConnectionsError)) return t('connections.errUncertain');
  switch (error.kind) {
    case 'needsAllAssets': return t('connections.importNeedsAllAssets');
    case 'planChanged': return t('connections.importPlanChanged');
    case 'importNotAllowed': return t('connections.importNotAllowed');
    case 'incomplete': return t('connections.importIncomplete', { detail: error.detail ?? '' });
    case 'invalidLevels': return t('connections.importLevelsInvalid');
    case 'tooLarge': return t('connections.errTooLarge');
    case 'disabled': return t('connections.errDisabled');
    case 'accessDenied': return t('connections.errDenied');
    case 'notFound': return t('connections.errNotFound');
    case 'gatewayUnavailable': return t('connections.errGateway');
    default: return t('connections.errUncertain');
  }
}

/** Choose how discovered objects are grouped, review exactly what would be created, then import it. */
export function ConnectionImport({ connection, adapter }: { connection: Connection; adapter: Adapter }) {
  const fields = adapter.groupingFields;
  const label = (key: string) => fields.find(field => field.key === key)?.label ?? key;
  const [levels, setLevels] = useState<string[]>([]);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [busy, setBusy] = useState<'review' | 'import' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const unused = fields.filter(field => !levels.includes(field.key));

  const changeLevels = (next: string[]) => {
    setLevels(next); setPreview(null); setResult(null); setConfirming(false); setError(null);
  };
  const move = (index: number, by: number) => {
    const next = [...levels];
    [next[index], next[index + by]] = [next[index + by], next[index]];
    changeLevels(next);
  };

  const review = async () => {
    setBusy('review'); setError(null); setResult(null); setConfirming(false);
    try { setPreview(await previewImport(connection, levels)); } catch (caught) {
      setPreview(null); setError(importError(caught));
    } finally { setBusy(null); }
  };

  const apply = async () => {
    if (!preview) return;
    setBusy('import'); setError(null);
    try {
      setResult(await importFromConnection(connection, levels, preview.fingerprint));
      setPreview(null); setConfirming(false);
    } catch (caught) {
      setError(importError(caught)); setConfirming(false);
      // The source or the Assets moved on: the reviewed plan no longer applies, so it is not left on screen.
      if (caught instanceof ConnectionsError && caught.kind === 'planChanged') setPreview(null);
    } finally { setBusy(null); }
  };

  return (
    <section className="connection-import" aria-label={t('connections.importHeading')}>
      <h3>{t('connections.importHeading')}</h3>
      <p className="connection-help">{t('connections.importHelp')}</p>
      {levels.length === 0
        ? <p className="connection-help">{t('connections.importNoLevels')}</p>
        : <ol className="connection-levels">
          {levels.map((key, index) => (
            <li key={key}>
              <span>{t('connections.importLevel', { number: index + 1, field: label(key) })}</span>
              <button type="button" className="secondary-button" disabled={busy !== null || index === 0}
                aria-label={t('connections.importMoveUp', { field: label(key) })} onClick={() => move(index, -1)}>↑</button>
              <button type="button" className="secondary-button" disabled={busy !== null || index === levels.length - 1}
                aria-label={t('connections.importMoveDown', { field: label(key) })} onClick={() => move(index, 1)}>↓</button>
              <button type="button" className="secondary-button" disabled={busy !== null}
                aria-label={t('connections.importRemove', { field: label(key) })}
                onClick={() => changeLevels(levels.filter(item => item !== key))}>×</button>
            </li>
          ))}
        </ol>}
      <div className="connection-actions">
        {unused.length > 0 && levels.length < 5 && (
          <label>
            <span>{t('connections.importAddLevel')}</span>
            <select value="" disabled={busy !== null} onChange={event => event.target.value && changeLevels([...levels, event.target.value])}>
              <option value="">{t('connections.importChooseLevel')}</option>
              {unused.map(field => <option key={field.key} value={field.key}>{field.label}</option>)}
            </select>
          </label>
        )}
        <button type="button" className="secondary-button" disabled={busy !== null} onClick={() => void review()}>
          {busy === 'review' ? t('connections.importReviewing') : t('connections.importReview')}
        </button>
      </div>
      {error && <p role="alert" className="status-error">{error}</p>}
      {preview && <ImportReview preview={preview} label={label} busy={busy} confirming={confirming}
        onConfirm={() => setConfirming(true)} onCancel={() => setConfirming(false)} onApply={() => void apply()} />}
      {result && (
        <div role="status" className="connection-test connection-test-healthy">
          <strong>{t('connections.importDone', { wells: result.wellsCreated, groups: result.groupsCreated })}</strong>
          {result.groupsReused > 0 && <span>{t('connections.importReused', { count: result.groupsReused })}</span>}
          {result.alreadyImported > 0 && <span>{t('connections.importSkipped', { count: result.alreadyImported })}</span>}
          <a href="#assets">{t('connections.importOpenAssets')}</a>
        </div>
      )}
    </section>
  );
}

function ImportReview({ preview, label, busy, confirming, onConfirm, onCancel, onApply }: {
  preview: ImportPreview; label: (key: string) => string; busy: 'review' | 'import' | null; confirming: boolean;
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
            <button type="button" className="primary-button" disabled={busy !== null || !preview.canImport} onClick={onApply}>
              {busy === 'import' ? t('connections.importing') : t('connections.importYes')}
            </button>
            <button type="button" className="secondary-button" disabled={busy !== null} onClick={onCancel}>{t('connections.importNo')}</button>
          </div>
        ) : (
          <button type="button" className="primary-button" disabled={busy !== null || !preview.canImport} onClick={onConfirm}>
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
