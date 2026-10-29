import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import { ExecutionDetailError, fetchExecutionDetail } from './executionDetail';

/** Reauthorize at download time and preserve the exact saved JSON, including decimal digits. */
export async function fetchExecutionOutput(executionId: string, signal: AbortSignal, fetcher: typeof fetch = fetch) {
  const detail = await fetchExecutionDetail(executionId, signal, fetcher);
  signal.throwIfAborted();
  const content = detail.outputJson;
  if (!content || content.length > 524288 || new TextEncoder().encode(content).length > 524288)
    throw new ExecutionDetailError('invalidResponse');
  try {
    const parsed: unknown = JSON.parse(content);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('Invalid envelope');
  } catch { throw new ExecutionDetailError('invalidResponse'); }
  return { content, filename: `execution-${executionId.toLowerCase()}.json` };
}

export function ExecutionOutputDownload({ executionId, onAccessLost }: { executionId: string; onAccessLost?: () => void }) {
  const request = useRef<AbortController | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  useEffect(() => () => { request.current?.abort(); }, [executionId]);
  async function download() {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setBusy(true); setError(false);
    try {
      const output = await fetchExecutionOutput(executionId, controller.signal);
      if (controller.signal.aborted) return;
      const url = URL.createObjectURL(new Blob([output.content], { type: 'application/json' }));
      try {
        const link = document.createElement('a');
        link.href = url; link.download = output.filename;
        document.body.appendChild(link);
        try { link.click(); } finally { link.remove(); }
      } finally { window.setTimeout(() => URL.revokeObjectURL(url), 1000); }
    } catch (reason) {
      if (controller.signal.aborted) return;
      setError(true);
      if (reason instanceof ExecutionDetailError && ['accessDenied', 'notFound'].includes(reason.kind)) onAccessLost?.();
    } finally { if (!controller.signal.aborted) setBusy(false); }
  }
  return <div>
    <button type="button" disabled={busy} onClick={() => { void download(); }}>
      {t(busy ? 'execution.downloading' : 'execution.downloadOutput')}
    </button>
    {error && <p role="alert">{t('execution.downloadFailed')}</p>}
  </div>;
}
