import type { ConfiguredLogicResponse, ExecutionResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { logicStatusSentence, recentRunsFor, scheduleText } from './logicSummary';
import { orderedRevisions } from './configuredLogicViews';
import { formatTimestamp } from '../format';
import { statusLabel } from '../words';
import './logicSummary.css';

type Props = {
  logic: ConfiguredLogicResponse;
  onClose: () => void;
  /** Runs already loaded by the page; omitted when the person may not read runs. */
  executions?: readonly ExecutionResponse[];
};

/**
 * The top of a configured logic: its name, where it stands, and the few facts people look for first.
 * Each revision's own data is shown once, in the revision list below this. Display metadata only:
 * never render module manifests, mapping JSON or credentials.
 */
export function ConfiguredLogicRevisionOverview({ logic, onClose, executions }: Props) {
  const revisions = orderedRevisions(logic);
  const live = revisions.find(revision => revision.id === logic.activeRevisionId);
  const shown = live ?? revisions[0];
  const runs = executions ? recentRunsFor(executions, logic.id) : [];

  return (
    <section id="configured-revisions" className="execution-detail"
      role="region"
      aria-label={t('configured.revisionsFor', { name: logic.name ?? '—' })}>
      <div className="execution-detail-header">
        <h3>{logic.name ?? '—'}</h3>
        <div className="execution-detail-actions">
          <button type="button" onClick={onClose}>
            {t('configured.close')}
          </button>
        </div>
      </div>
      <p className="logic-status-sentence">{logicStatusSentence(logic, runs[0])}</p>
      {shown && <dl className="logic-facts">
        <div><dt>{t('configured.liveRevision')}</dt>
          <dd>{live ? t('setup.revision', { number: live.revisionNumber ?? '—' }) : t('configured.noLive')}</dd></div>
        <div><dt>{t('configured.module')}</dt>
          <dd>{shown.moduleId ?? '—'}{shown.moduleVersion ? ` v${shown.moduleVersion}` : ''}</dd></div>
        <div><dt>{t('configured.schedule')}</dt><dd>{scheduleText(shown.schedule)}</dd></div>
        <div><dt>{t('configured.factAssets')}</dt><dd>{shown.assetBindings?.length ?? 0}</dd></div>
      </dl>}
      {revisions.length === 0 && <p className="empty-state">{t('configured.noRevisions')}</p>}
      {executions && <div className="logic-recent-runs">
        <h4>{t('logicSummary.recentRuns')}</h4>
        {runs.length === 0 ? <p>{t('logicSummary.noRuns')}</p> : <ul>
          {runs.map(run => <li key={run.executionId}>
            <a href={'#operations/' + run.executionId}>{formatTimestamp(run.requestedAtUtc)}</a>
            {' · '}{statusLabel(run.status)}
          </li>)}
        </ul>}
      </div>}
    </section>
  );
}
