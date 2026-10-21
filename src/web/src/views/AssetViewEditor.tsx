import { useState } from 'react';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { MAX_VIEW_LEVELS, MAX_VIEW_NAME, STANDALONE, VIEW_LEVELS, ViewsError, createView, deleteView, updateView } from './assetViewsApi';
import type { AssetView, ViewActivity, ViewDefinition, ViewLevel } from './assetViewsApi';
import type { OwnerGroup } from './assetHierarchy';
import './assetViews.css';

const levelKey = (level: ViewLevel): MessageKey => `assetViews.level.${level}` as MessageKey;

const errorKey = (error: unknown): MessageKey => {
  if (!(error instanceof ViewsError)) return 'assetViews.errUnavailable';
  switch (error.kind) {
    case 'nameTaken': return 'assetViews.errNameTaken';
    case 'stale': return 'assetViews.errStale';
    case 'limit': return 'assetViews.errLimit';
    case 'invalid': return 'assetViews.errInvalid';
    case 'notFound': return 'assetViews.errNotFound';
    case 'accessDenied': return 'assetViews.errDenied';
    default: return 'assetViews.errUnavailable';
  }
};

/** Create or change one View: what it shows and how it is grouped. A View never moves or copies Assets. */
export function AssetViewEditor({
  view, owners, assetTypes, onSaved, onDeleted, onCancel,
}: {
  view: AssetView | null;
  owners: readonly OwnerGroup[];
  assetTypes: readonly { id: string; name: string }[];
  onSaved: (saved: AssetView) => void;
  onDeleted: () => void;
  onCancel: () => void;
}) {
  const [name, setName] = useState(view?.name ?? '');
  const [definition, setDefinition] = useState<ViewDefinition>(view?.definition ?? { version: 1, connections: [],
    assetTypeIds: [], activity: 'all', search: null, grouping: [] });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<MessageKey | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const connectionChoices = [
    ...owners.filter(owner => owner.connectionId !== null).map(owner =>
      ({ key: owner.connectionId as string, label: owner.name ?? t('assetOwner.unknown') })),
    { key: STANDALONE, label: t('assetOwner.standalone') },
  ];
  const toggle = (list: string[], value: string) =>
    list.includes(value) ? list.filter(item => item !== value) : [...list, value];
  const move = (index: number, by: -1 | 1) => setDefinition(current => {
    const grouping = [...current.grouping];
    const target = index + by;
    if (target < 0 || target >= grouping.length) return current;
    [grouping[index], grouping[target]] = [grouping[target], grouping[index]];
    return { ...current, grouping };
  });
  const remaining = VIEW_LEVELS.filter(level => !definition.grouping.includes(level));
  const run = async (action: () => Promise<void>) => {
    setBusy(true); setError(null);
    try { await action(); } catch (caught) { setError(errorKey(caught)); } finally { setBusy(false); }
  };
  const save = () => run(async () => {
    const cleaned = { ...definition, search: definition.search?.trim() ? definition.search.trim() : null };
    onSaved(view ? await updateView(view, name.trim(), cleaned) : await createView(name.trim(), cleaned));
  });

  return (
    <form className="asset-view-editor panel" aria-label={t(view ? 'assetViews.editTitle' : 'assetViews.createTitle')}
      onSubmit={event => { event.preventDefault(); void save(); }}>
      <h3>{t(view ? 'assetViews.editTitle' : 'assetViews.createTitle')}</h3>
      <p className="connection-help">{t('assetViews.intro')}</p>
      <label>
        <span>{t('assetViews.name')}</span>
        <input type="text" value={name} maxLength={MAX_VIEW_NAME} disabled={busy}
          onChange={event => setName(event.target.value)} />
      </label>

      <fieldset disabled={busy}>
        <legend>{t('assetViews.connections')}</legend>
        <p className="connection-help">{t('assetViews.connectionsHelp')}</p>
        {connectionChoices.map(choice => (
          <label key={choice.key} className="asset-view-check">
            <input type="checkbox" checked={definition.connections.includes(choice.key)}
              onChange={() => setDefinition(current => ({ ...current, connections: toggle(current.connections, choice.key) }))} />
            {' '}{choice.label}
          </label>
        ))}
      </fieldset>

      {assetTypes.length > 0 && (
        <fieldset disabled={busy}>
          <legend>{t('assetViews.types')}</legend>
          <p className="connection-help">{t('assetViews.typesHelp')}</p>
          {assetTypes.map(type => (
            <label key={type.id} className="asset-view-check">
              <input type="checkbox" checked={definition.assetTypeIds.includes(type.id)}
                onChange={() => setDefinition(current => ({ ...current, assetTypeIds: toggle(current.assetTypeIds, type.id) }))} />
              {' '}{type.name}
            </label>
          ))}
        </fieldset>
      )}

      <label>
        <span>{t('assetViews.activity')}</span>
        <select value={definition.activity} disabled={busy}
          onChange={event => setDefinition(current => ({ ...current, activity: event.target.value as ViewActivity }))}>
          <option value="all">{t('assetViews.activityAll')}</option>
          <option value="active">{t('assetViews.activityActive')}</option>
          <option value="inactive">{t('assetViews.activityInactive')}</option>
        </select>
      </label>
      <label>
        <span>{t('assetViews.search')}</span>
        <input type="text" value={definition.search ?? ''} maxLength={120} disabled={busy}
          onChange={event => setDefinition(current => ({ ...current, search: event.target.value }))} />
      </label>

      <fieldset disabled={busy}>
        <legend>{t('assetViews.grouping')}</legend>
        <p className="connection-help">{t('assetViews.groupingHelp')}</p>
        {definition.grouping.length === 0 && <p className="split-note">{t('assetViews.flat')}</p>}
        <ol className="asset-view-levels">
          {definition.grouping.map((level, index) => (
            <li key={level}>
              <span>{t(levelKey(level))}</span>
              <button type="button" className="secondary-button" disabled={index === 0}
                aria-label={t('assetViews.moveUp', { name: t(levelKey(level)) })} onClick={() => move(index, -1)}>↑</button>
              <button type="button" className="secondary-button" disabled={index === definition.grouping.length - 1}
                aria-label={t('assetViews.moveDown', { name: t(levelKey(level)) })} onClick={() => move(index, 1)}>↓</button>
              <button type="button" className="secondary-button"
                aria-label={t('assetViews.removeLevel', { name: t(levelKey(level)) })}
                onClick={() => setDefinition(current => ({ ...current, grouping: current.grouping.filter(item => item !== level) }))}>
                {t('assetViews.remove')}</button>
            </li>
          ))}
        </ol>
        {remaining.length > 0 && definition.grouping.length < MAX_VIEW_LEVELS && (
          <div className="connection-actions">
            {remaining.map(level => (
              <button key={level} type="button" className="secondary-button"
                onClick={() => setDefinition(current => ({ ...current, grouping: [...current.grouping, level] }))}>
                {t('assetViews.addLevel', { name: t(levelKey(level)) })}</button>
            ))}
          </div>
        )}
      </fieldset>

      {error && <p role="alert" className="status-error">{t(error)}</p>}
      <div className="connection-actions">
        <button type="submit" className="primary-button" disabled={busy || !name.trim()}>
          {t('assetViews.save')}</button>
        <button type="button" className="secondary-button" disabled={busy} onClick={onCancel}>{t('assetViews.cancel')}</button>
        {view && !confirmDelete && <button type="button" className="secondary-button" disabled={busy}
          onClick={() => setConfirmDelete(true)}>{t('assetViews.delete')}</button>}
      </div>
      {view && confirmDelete && (
        <div className="connection-actions connection-delete-confirm" role="alertdialog" aria-label={t('assetViews.delete')}>
          <span>{t('assetViews.deleteConfirm', { name: view.name })}</span>
          <button type="button" className="secondary-button" disabled={busy}
            onClick={() => void run(async () => { await deleteView(view); onDeleted(); })}>{t('assetViews.deleteYes')}</button>
          <button type="button" className="secondary-button" disabled={busy}
            onClick={() => setConfirmDelete(false)}>{t('assetViews.deleteNo')}</button>
        </div>
      )}
    </form>
  );
}
