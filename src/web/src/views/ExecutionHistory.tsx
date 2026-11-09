import { useCallback, useEffect, useRef, useState } from 'react';
import type { AssetResponse, ConfiguredLogicResponse } from '../api/generated/v1/types.gen';
import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import { ActionButton } from '../components/ActionButton';
import { LoadingIndicator, PanelLoading } from '../components/LoadingIndicator';
import { Status } from '../components/Status';
import { formatTimestamp, shortId } from '../format';
import { t } from '../localization';
import { Notice } from '../components/Notice';
import { ExecutionDetailPanel } from './ExecutionDetailPanel';
import { fetchRunHistory } from './executionHistoryApi';
import { useLiveResource } from './useLiveResource';
import './splitLayout.css';
import './assetPage.css';
import './executionHistory.css';

type Props = {
  assetId?: string;
  logicId: string;
  runId?: string | null;
  assets: readonly AssetResponse[];
  configuredLogic: readonly ConfiguredLogicResponse[];
  enabled?: boolean;
  /** The focused variant is the primary page content. Embedded Logic Activity adopts it in the next slice. */
  focused?: boolean;
  /** When supplied, the URL is the source of truth for the selected run. */
  onRunSelect?: (runId: string | null) => void;
};

/** One authorized, scoped paging and inspection flow shared by Asset history and Logic Activity.
 * Its two presentation modes deliberately share selection/query semantics and the detail renderer. */
export function ExecutionHistory({ assetId, logicId, runId = null, assets, configuredLogic,
  enabled = true, focused = false, onRunSelect }: Props) {
  const [localSelected, setLocalSelected] = useState<string | null>(runId);
  const [cursor, setCursor] = useState<string | null>(null);
  const selected = onRunSelect ? runId : localSelected;
  const initialSelectionResolved = useRef(Boolean(runId));
  const detailScroll = useRef<HTMLDivElement>(null);
  const splitFocus = useSplitPaneFocus(focused ? selected : null);
  const load = useCallback((signal: AbortSignal) =>
    fetchRunHistory(assetId, logicId, cursor, signal), [assetId, logicId, cursor]);
  const history = useLiveResource(`${assetId}:${logicId}:${cursor}`, load, ['executions'], enabled);
  const page = history.data?.items ?? [];
  const selectedRow = page.find(run => run.executionId === selected);
  const selectedIndex = page.findIndex(run => run.executionId === selected);
  const showInspector = Boolean(selected) && !history.denied;

  function selectRun(next: string | null) {
    if (onRunSelect) onRunSelect(next);
    else setLocalSelected(next);
  }

  // Pick the newest run only once on initial focused load, not on SignalR or polling updates.
  // A deep-linked older run stays selected even when it is outside the first history page.
  useEffect(() => {
    if (!focused || initialSelectionResolved.current || !history.data || history.denied) return;
    initialSelectionResolved.current = true;
    const first = history.data.items?.[0]?.executionId;
    if (!selected && first) {
      if (onRunSelect) onRunSelect(first);
      else setLocalSelected(first);
    }
  }, [focused, history.data, history.denied, onRunSelect, selected]);

  // History denial revokes the selected projection as well; a transient history failure does not.
  useEffect(() => {
    if (!history.denied || !selected) return;
    if (onRunSelect) onRunSelect(null);
    else setLocalSelected(null);
  }, [history.denied, onRunSelect, selected]);

  useEffect(() => { detailScroll.current?.scrollTo?.({ top: 0 }); }, [selected]);

  return <div {...(focused ? splitFocus : {})}
    className={focused ? 'split-layout runs-layout execution-history execution-history-focus' : 'execution-history'}
    data-has-selection={showInspector}>
    <div className={focused ? 'split-panes execution-history-panes' : 'execution-history-inline'}>
      <section className={focused ? 'panel split-browse execution-history-browse' : 'execution-history-browse'}
        aria-label={t('assetDetail.historyEntries')}>
        {focused && <header className="execution-history-browse-header">
          <h4>{t('assetDetail.historyTitle')}</h4>
        </header>}
        {history.loading && (focused && page.length === 0
          ? <PanelLoading label={t('assetPage.loading')} />
          : <LoadingIndicator size="small" label={t('assetPage.loading')} />)}
        {history.failed && <Notice tone="warning">{t('assetDetail.runsUnavailable')}{' '}
          <ActionButton variant="quiet" onClick={history.refresh} label={t('assetDetail.retry')} />
        </Notice>}
        {history.data?.items?.length === 0 && <p>{t('assetPage.noRuns')}</p>}
        <ul className={focused ? 'asset-page-list execution-history-results' : 'asset-page-list'}>
          {page.map(run => <li key={run.executionId} data-selected={selected === run.executionId}
            className={focused ? 'execution-history-row' : undefined}
            onClick={event => {
              if ((event.target as HTMLElement).closest('button, a')) return;
              if (run.executionId) selectRun(run.executionId);
            }}>
            <Status value={run.status} />
            <button type="button" className="row-link" aria-pressed={selected === run.executionId}
              aria-controls={selected === run.executionId && focused ? 'execution-details' : undefined}
              aria-label={`${formatTimestamp(run.requestedAtUtc)} · ${t('assetDetail.inspect', { id: shortId(run.executionId) })}`}
              onClick={() => run.executionId && selectRun(run.executionId)}>
              {formatTimestamp(run.requestedAtUtc)}
            </button>
            <small>{run.mode} · {shortId(run.executionId)}</small>
          </li>)}
        </ul>
        <div className="asset-detail-actions">
          {cursor && <ActionButton onClick={() => setCursor(null)} label={t('assetDetail.newest')} />}
          {history.data?.nextCursor && <ActionButton
            onClick={() => setCursor(history.data!.nextCursor!)} label={t('assetDetail.older')} />}
        </div>
      </section>
      <section className={focused ? 'split-selected execution-history-inspector' : 'execution-history-inspector'}
        tabIndex={focused ? -1 : undefined} aria-label={t('assetDetail.selectedRunInspector')}>
        {focused && <ActionButton className="split-back" label={t('assetDetail.backToHistory')}
          onClick={() => selectRun(null)} />}
        {showInspector && selected && <>
          {focused && <header className="execution-history-selected-header">
            <div>
              <h4>{selectedRow
                ? t('assetDetail.selectedRunAt', { time: formatTimestamp(selectedRow.requestedAtUtc) })
                : t('assetDetail.selectedRunId', { id: shortId(selected) })}</h4>
              {selectedRow ? <p><Status value={selectedRow.status} /> · {selectedRow.mode} · {shortId(selected)}</p>
                : history.data && <p>{t('assetDetail.selectedRunOutsidePage')}</p>}
            </div>
            {selectedIndex >= 0 && <div className="execution-history-step-actions">
              <ActionButton variant="quiet" disabled={selectedIndex === 0}
                label={t('assetDetail.previousRun')}
                onClick={() => selectRun(page[selectedIndex - 1]?.executionId ?? null)} />
              <ActionButton variant="quiet" disabled={selectedIndex === page.length - 1}
                label={t('assetDetail.nextRun')}
                onClick={() => selectRun(page[selectedIndex + 1]?.executionId ?? null)} />
            </div>}
          </header>}
          <div ref={detailScroll} className="execution-history-inspector-content">
            <ExecutionDetailPanel key={selected} executionId={selected}
              assets={assets} configuredLogic={configuredLogic} poll
              onClose={() => selectRun(null)} onAccessLost={() => selectRun(null)} />
          </div>
        </>}
      </section>
    </div>
  </div>;
}
