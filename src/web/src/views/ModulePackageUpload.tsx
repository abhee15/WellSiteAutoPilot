import { useEffect, useRef, useState } from 'react';
import { uploadLogicModulePackage } from '../api/generated/v1/sdk.gen';
import type { LogicModuleResponse } from '../api/generated/v1/types.gen';
import { isSecurityId } from '../security/identifiers';
import { t } from '../localization';

const MAXIMUM_UPLOAD_BYTES = 16 * 1024 * 1024;

/** Upload uses the existing immutable package endpoint; approval remains a separate reviewed action. */
export function ModulePackageUpload({ canManage, onUploaded }: {
  canManage: boolean; onUploaded: (module: LogicModuleResponse) => Promise<void>;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const pending = useRef(false);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);
  const tooLarge = !!file && file.size > MAXIMUM_UPLOAD_BYTES;

  async function upload() {
    if (!canManage || !file || file.size === 0 || tooLarge || pending.current) return;
    pending.current = true; setBusy(true); setError(null); setNotice(null);
    const controller = new AbortController(); request.current = controller;
    try {
      const result = await uploadLogicModulePackage({ body: file, credentials: 'same-origin', signal: controller.signal });
      if (controller.signal.aborted) return;
      const status = result.response?.status;
      if (status !== 201 || !result.data) {
        const code = result.error && typeof result.error === 'object' && 'code' in result.error ? result.error.code : null;
        setError(t(code === 'MODULE_VERSION_IMMUTABLE' || code === 'MODULE_PACKAGE_REQUIRES_NEW_VERSION'
          ? 'moduleUpload.newVersion' : status === 403 ? 'moduleUpload.denied'
            : status === undefined || status >= 500 ? 'moduleUpload.uncertain' : 'moduleUpload.rejected'));
        return;
      }
      const module = result.data;
      if (!isSecurityId(module.id) || !module.moduleId || !module.version ||
          !/^[a-f0-9]{64}$/.test(module.packageSha256 ?? '') || typeof module.isEnabled !== 'boolean') {
        setError(t('moduleUpload.uncertain')); return;
      }
      setNotice(t('moduleUpload.saved', { module: module.moduleId, version: module.version }));
      setFile(null);
      try { await onUploaded(module); }
      catch { setError(t('moduleUpload.refreshFailed')); }
    } catch {
      if (!controller.signal.aborted) setError(t('moduleUpload.uncertain'));
    } finally {
      pending.current = false;
      setBusy(false);
    }
  }

  if (!canManage) return null;
  return <details className="module-approval">
    <summary>{t('moduleUpload.heading')}</summary>
    <p>{t('moduleUpload.hint')}</p>
    <label>{t('moduleUpload.file')}<input type="file" accept=".wsamodule,.zip" disabled={busy}
      onChange={event => { setFile(event.target.files?.[0] ?? null); setError(null); setNotice(null); }} /></label>
    {tooLarge && <p role="alert">{t('moduleUpload.tooLarge')}</p>}
    {error && <p role="alert">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    <button type="button" disabled={busy || !file || file.size === 0 || tooLarge} onClick={() => void upload()}>
      {t(busy ? 'moduleUpload.working' : 'moduleUpload.upload')}
    </button>
  </details>;
}
