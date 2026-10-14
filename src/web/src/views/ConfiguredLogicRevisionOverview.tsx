import type { ConfiguredLogicResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import { orderedRevisions } from './configuredLogicViews';

type Props = {
  logic: ConfiguredLogicResponse;
  onClose: () => void;
};

/** Display metadata only: never render module manifests, mapping JSON or credentials. */
export function ConfiguredLogicRevisionOverview({ logic, onClose }: Props) {
  const revisions = orderedRevisions(logic);

  return (
    <section id="configured-revisions" className="execution-detail"
      role="region"
      aria-label={t('configured.revisionsFor', { name: logic.name ?? '—' })}>
      <div className="execution-detail-header">
        <div>
          <h3>{t('configured.revisionsHeading')}</h3>
          <p>{logic.name ?? '—'} · {t('configured.metadataOnly')}</p>
        </div>
        <div className="execution-detail-actions">
          <button type="button" onClick={onClose}>
            {t('configured.close')}
          </button>
        </div>
      </div>
      <div className="configured-revisions-summary">
        <span>{t('configured.summaryCount', { count: revisions.length })}</span>
        <span>{t('configured.summaryActive', {
          value: logic.activeRevisionId ? logic.activeRevisionId : '—',
        })}</span>
      </div>
      <div className="table-wrap">
        <table className="activity-table">
          <thead>
            <tr>
              <th scope="col">{t('configured.revisionNumber')}</th>
              <th scope="col">{t('configured.module')}</th>
              <th scope="col">{t('configured.revisionStatus')}</th>
              <th scope="col">{t('configured.schedule')}</th>
              <th scope="col">{t('configured.bindings')}</th>
              <th scope="col">{t('configured.created')}</th>
            </tr>
          </thead>
          <tbody>
            {revisions.map(revision => (
              <tr key={revision.id}>
                <td>
                  <strong>{revision.revisionNumber ?? '—'}</strong>
                  {revision.id && <small>{revision.id}</small>}
                </td>
                <td>
                  <strong>{revision.moduleId ?? '—'}</strong>
                  <small>{revision.moduleVersion ?? '—'}</small>
                </td>
                <td>
                  <span>{revision.status ?? '—'}</span>
                  {logic.activeRevisionId === revision.id && (
                    <small>{t('configured.current')}</small>
                  )}
                  <small>{revision.mode ?? '—'}</small>
                </td>
                <td>
                  {revision.schedule ? (
                    <>
                      <strong>{revision.schedule.enabled
                        ? t('configured.enabled') : t('configured.disabled')}</strong>
                      <small>{t('configured.cadence', {
                        seconds: revision.schedule.cadenceSeconds ?? 0,
                      })}</small>
                      <small>{revision.schedule.timeZoneId ?? 'UTC'}</small>
                      <small>{formatDetailTimestamp(revision.schedule.startAtUtc)}</small>
                    </>
                  ) : '—'}
                </td>
                <td>{t('configured.bindingCounts', {
                  assets: revision.assetBindings?.length ?? 0,
                  data: revision.dataBindings?.length ?? 0,
                })}</td>
                <td>{formatDetailTimestamp(revision.createdAtUtc)}</td>
              </tr>
            ))}
            {revisions.length === 0 && (
              <tr>
                <td colSpan={6} className="empty-state">
                  {t('configured.noRevisions')}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </section>
  );
}
