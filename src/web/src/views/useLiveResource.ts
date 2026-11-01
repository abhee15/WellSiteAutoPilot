import { useCallback, useEffect, useRef, useState } from 'react';
import { useChangeRefresh, usePollingRefresh } from '../live/LiveUpdates';
import type { ChangeArea } from '../live/changeSignal';

import { DetailReadError } from './detailRead';

/** Targeted live reads keep content during refresh, cancel stale requests and clear denied data. */
export function useLiveResource<T>(key: string, load: (signal: AbortSignal) => Promise<T>,
  areas: readonly ChangeArea[], enabled = true) {
  const [result, setResult] = useState<{ key: string; data?: T; failed: boolean }>({ key, failed: false });
  const current = useRef<AbortController | null>(null);
  const refresh = useCallback(() => {
    if (!enabled) return;
    current.current?.abort();
    const controller = new AbortController();
    current.current = controller;
    load(controller.signal).then(data => {
      if (!controller.signal.aborted) setResult({ key, data, failed: false });
    }).catch((error: unknown) => {
      if (controller.signal.aborted) return;
      const denied = error instanceof DetailReadError && [401, 403, 404].includes(error.status);
      setResult(previous => ({ key, data: !denied && previous.key === key ? previous.data : undefined, failed: true }));
    });
  }, [key, load, enabled]);
  useEffect(() => {
    refresh();
    return () => current.current?.abort();
  }, [refresh]);
  useChangeRefresh(areas, refresh);
  usePollingRefresh(refresh, enabled);
  const visible = enabled && result.key === key ? result : { data: undefined, failed: false };
  return { ...visible, loading: enabled && visible.data === undefined && !visible.failed, refresh };
}
