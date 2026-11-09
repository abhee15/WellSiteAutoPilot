import type { AssetResponse, ConfiguredLogicRevisionResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatTimestamp } from '../format';
import type { OwnerGroup } from './assetHierarchy';
import { assetIdentityLabel } from './runContext';
import { describeSettings } from './revisionSettings';
import { scheduleText } from './logicSummary';
import { CollapsibleSection } from '../components/Section';

/** Read-only, module-driven settings. No assumption about parameter count, shape or provider. */
export function ConfiguredRevisionDetails({ revision, assets, owners = [] }: {
  revision: ConfiguredLogicRevisionResponse; assets: readonly AssetResponse[]; owners?: readonly OwnerGroup[];
}) {
  const lines = describeSettings(revision.parametersJson, revision.moduleManifestJson) ?? [];
  const name = (id?: string) => assetIdentityLabel(id, assets, owners);
  return <div className="configured-revision-details">
    <dl className="logic-facts" aria-label={revision.view ? t('setup.revisionSettings') : undefined}>
      <div><dt>{t('configured.module')}</dt><dd>{revision.moduleId} v{revision.moduleVersion}</dd></div>
      <div><dt>{t('logicDetail.mode')}</dt><dd>{revision.mode === 'Shadow' ? t('logicDetail.shadowMode') : revision.mode}</dd></div>
      <div><dt>{t('configured.schedule')}</dt><dd>{scheduleText(revision.schedule)}</dd></div>
      {revision.view && <div><dt>{t('setup.targetView')}</dt><dd>{t('setup.viewSnapshot', {
        revision: revision.view.revision ?? 0, count: revision.assetBindings?.filter(b => b.role?.toLowerCase() === revision.view?.role?.toLowerCase()).length ?? 0,
      })}</dd></div>}
    </dl>
    {revision.mode === 'Shadow' && <p className="logic-detail-note">{t('logicDetail.shadowModeHint')}</p>}
    <CollapsibleSection title={t('logicDetail.parameters', { count: lines.length })} open={lines.length <= 6}>
      {lines.length === 0 ? <p>{t('logicDetail.noParameters')}</p> : <dl className="revision-settings" aria-label={!revision.view ? t('setup.revisionSettings') : undefined}>
        {lines.map((line, index) => <div key={index}><dt>{line.label}</dt><dd className="parameter-value">{line.value}</dd></div>)}
      </dl>}
    </CollapsibleSection>
    <CollapsibleSection title={t('logicDetail.targets', { count: revision.assetBindings?.length ?? 0 })} open={(revision.assetBindings?.length ?? 0) <= 2}>
      <ul className="configured-binding-list">{revision.assetBindings?.map((binding, index) => <li key={index}>
        <strong>{binding.role}</strong> · {name(binding.assetId)}
        {describeSettings(binding.parameterOverridesJson, revision.moduleManifestJson)?.map((line, i) =>
          <p className="parameter-value" key={i}>{line.label}: {line.value}</p>)}
      </li>)}</ul>
    </CollapsibleSection>
    <CollapsibleSection title={t('logicDetail.providers', { count: revision.dataBindings?.length ?? 0 })} open={(revision.dataBindings?.length ?? 0) <= 2}>
      <ul className="configured-binding-list">{revision.dataBindings?.map((binding, index) => <li key={index}>
        <strong>{binding.requirementId}</strong> · {binding.providerId} · {name(binding.assetId)}
      </li>)}</ul>
    </CollapsibleSection>
    <CollapsibleSection title={t('assetDetail.technical')} quiet><dl className="logic-facts">
      <div><dt>{t('logicDetail.revisionId')}</dt><dd><code>{revision.id}</code></dd></div>
      <div><dt>{t('logicDetail.created')}</dt><dd>{formatTimestamp(revision.createdAtUtc)}</dd></div>
    </dl></CollapsibleSection>
  </div>;
}
