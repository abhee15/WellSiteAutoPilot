import { ContextLink } from '../components/ContextLink';
import { hashFor } from '../routing';
import { CloseButton } from '../components/CloseButton';
import type { ConfiguredLogicResponse, LogicModuleResponse } from '../api/generated/v1/types.gen';
import { SelectField } from '../components/FormFields';
import { t } from '../localization';
import { describeModule } from './moduleSummary';
import { ModuleApprovalControls } from './ModuleApprovalControls';
import { ModulePackageState } from './ModulePackageState';
import './logicSummary.css';
import { moduleStateLabel } from './moduleApproval';

export function ModulePanel({ module, configurations, onClose, canManage = false, onChanged = async () => {}, versions = [], onSelectVersion }: {
  module: LogicModuleResponse;
  configurations: readonly ConfiguredLogicResponse[];
  onClose: () => void;
  canManage?: boolean;
  onChanged?: () => Promise<void>;
  versions?: readonly LogicModuleResponse[];
  onSelectVersion?: (id: string) => void;
}) {
  const summary = describeModule(module);
  const usedBy = configurations.filter(item => item.id && item.revisions?.some(revision =>
    revision.moduleId === module.moduleId)).sort((a, b) => (a.name ?? '').localeCompare(b.name ?? ''));
  return (
    <section className="module-panel" role="region" aria-label={t('modulePanel.label', { name: module.displayName ?? '—' })}>
      <div className="module-panel-header">
        <div>
          <h3>{module.displayName}</h3>
          <p>{summary.provenance} · v{module.version}</p>
        </div>
        <CloseButton onClick={onClose} />
      </div>
      {versions.length > 1 && onSelectVersion && <SelectField label={t('moduleGroup.version')} helper={t('moduleGroup.versionHint')}
        value={module.id ?? ''} onChange={event => onSelectVersion(event.target.value)}>
        {versions.map(version => <option key={version.id} value={version.id ?? ''}>
          {version.version} · {moduleStateLabel(version)}
        </option>)}
      </SelectField>}
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
      {usedBy.length === 0 ? <p>{t('modulePanel.unused')}</p>
        : <ul>{usedBy.map(item => <li key={item.id}>
          <ContextLink appearance="record" href={hashFor('logic', item.id)} label={item.name ?? '—'} />
        </li>)}</ul>}
    </section>
  );
}
