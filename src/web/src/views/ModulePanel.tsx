import { ActionButton } from '../components/ActionButton';
import type { ConfiguredLogicResponse, LogicModuleResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { describeModule } from './moduleSummary';
import { ModuleApprovalControls } from './ModuleApprovalControls';
import { ModulePackageState } from './ModulePackageState';
import './logicSummary.css';

export function ModulePanel({ module, configurations, onClose, canManage = false, onChanged = async () => {} }: {
  module: LogicModuleResponse;
  configurations: readonly ConfiguredLogicResponse[];
  onClose: () => void;
  canManage?: boolean;
  onChanged?: () => Promise<void>;
}) {
  const summary = describeModule(module, configurations);
  return (
    <section className="module-panel" role="region" aria-label={t('modulePanel.label', { name: module.displayName ?? '—' })}>
      <div className="module-panel-header">
        <div>
          <h3>{module.displayName}</h3>
          <p>{summary.provenance} · v{module.version}</p>
        </div>
        <ActionButton label={t('configured.close')} onClick={onClose} />
      </div>
      <p className="logic-status-sentence">{summary.sentence}</p>
      <p>{t('moduleUpload.identity')} <code>{module.packageSha256 ?? '—'}</code></p>
      <p>{t(module.runtime === 'Python' ? 'moduleUpload.pythonUnavailable' : 'moduleUpload.readiness')}</p>
      <ModuleApprovalControls key={module.id} module={module} canManage={canManage} onChanged={onChanged} />
      <ModulePackageState module={module} />
      {summary.needs.length > 0 && <>
        <h4>{t('modulePanel.needs')}</h4>
        <ul>{summary.needs.map(line => <li key={line}>{line}</li>)}</ul>
      </>}
      <p>{summary.declaresCommands ? t('modulePanel.commands') : t('modulePanel.noCommands')}</p>
      <h4>{t('modulePanel.usedBy')}</h4>
      {summary.usedBy.length === 0 ? <p>{t('modulePanel.unused')}</p>
        : <ul>{summary.usedBy.map(name => <li key={name}>{name}</li>)}</ul>}
    </section>
  );
}
