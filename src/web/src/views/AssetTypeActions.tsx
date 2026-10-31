import { useState } from 'react';
import type { AssetTypeResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { problemCode } from './assetTypeDefinition';

const NOTE_LIMIT = 200;

/** Hide, restore and delete for one Asset Type. The server re-checks every permission and rule. */
export function AssetTypeActions({ type, count, countKnown, canHide, canManage, onChanged }: {
  type: AssetTypeResponse;
  count: number;
  /** False when the asset list is only a partial snapshot, so a zero count proves nothing. */
  countKnown: boolean;
  canHide: boolean;
  canManage: boolean;
  onChanged: () => Promise<void>;
}) {
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [error, setError] = useState<MessageKey | null>(null);
  const hidden = type.isActive === false;
  const base = `/api/v1/asset-types/${String(type.id)}`;

  const send = async (path: string, method: 'PUT' | 'DELETE', body?: unknown) => {
    setBusy(true);
    setError(null);
    try {
      const response = await fetch(base + path, {
        method, credentials: 'same-origin', cache: 'no-store',
        headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      if (!response.ok) {
        const code = await response.json().then(problemCode, () => null);
        setError(response.status === 403 ? 'assetTypes.actionDenied'
          : code === 'ASSET_TYPE_IN_USE' ? 'assetTypes.inUse'
          : response.status === 404 ? 'assetTypes.gone'
          : response.status === 400 ? 'assetTypes.noteInvalid'
          : 'assetTypes.actionUncertain');
        return;
      }
      setConfirmDelete(false);
      setNote('');
      await onChanged();
    } catch {
      setError('assetTypes.actionUncertain');
    } finally {
      setBusy(false);
    }
  };

  const canDelete = canManage && countKnown && count === 0;
  if (!(hidden ? canManage : canHide) && !canDelete) return null;
  return (
    <div className="asset-create-form" aria-label={t('assetTypes.actions')}>
      {hidden && canManage && <ActionButton label={t('assetTypes.unhide')} disabled={busy} onClick={() => void send('/unhide', 'PUT')} />}
      {!hidden && canHide && <>
        <p className="asset-create-hint">{t('assetTypes.hideHelp')}</p>
        <label>
          <span>{t('assetTypes.hideNote')}</span>
          <input type="text" value={note} maxLength={NOTE_LIMIT} disabled={busy}
            onChange={event => setNote(event.target.value)} />
        </label>
        <ActionButton label={t('assetTypes.hide')} disabled={busy} onClick={() => void send('/hide', 'PUT', { note: note.trim() || null })} />
      </>}
      {canDelete && (confirmDelete
        ? <>
          <p className="asset-create-hint">{t('assetTypes.deleteConfirm', { name: type.displayName ?? type.key ?? '' })}</p>
          <ActionButton label={t('assetTypes.deleteYes')} disabled={busy} onClick={() => void send('', 'DELETE')} />
          <ActionButton label={t('assetTypes.deleteNo')} disabled={busy} onClick={() => setConfirmDelete(false)} />
        </>
        : <ActionButton label={t('assetTypes.delete')} disabled={busy} onClick={() => setConfirmDelete(true)} />)}
      {error && <p role="alert" className="status-error">{t(error)}</p>}
    </div>
  );
}
