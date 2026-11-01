import type { AssetResponse, ConfiguredLogicRevisionResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatTimestamp, shortId } from '../format';
import { describeSettings } from './revisionSettings';
import { scheduleText } from './logicSummary';

/** Read-only, module-driven settings. No assumption about parameter count, shape or provider. */
export function ConfiguredRevisionDetails({ revision, assets }: {
  revision: ConfiguredLogicRevisionResponse; assets: readonly AssetResponse[];
}) {
  const lines = describeSettings(revision.parametersJson, revision.moduleManifestJson) ?? [];
  const name = (id?: string) => assets.find(asset => asset.id === id)?.name ?? shortId(id);
  return <div className="configured-revision-details">
    <dl className="logic-facts" aria-label={revision.view ? t('setup.revisionSettings') : undefined}>
      <div><dt>{t('configured.module')}</dt><dd>{revision.moduleId} v{revision.moduleVersion}</dd></div>
      <div><dt>{t('logicDetail.mode')}</dt><dd>{revision.mode}</dd></div>
      <div><dt>{t('configured.schedule')}</dt><dd>{scheduleText(revision.schedule)}</dd></div>
      {revision.view && <div><dt>{t('setup.targetView')}</dt><dd>{t('setup.viewSnapshot', {
        revision: revision.view.revision ?? 0, count: revision.assetBindings?.filter(b => b.role?.toLowerCase() === revision.view?.role?.toLowerCase()).length ?? 0,
      })}</dd></div>}
    </dl>
    <details open={lines.length <= 6}>
      <summary>{t('logicDetail.parameters', { count: lines.length })}</summary>
      {lines.length === 0 ? <p>{t('logicDetail.noParameters')}</p> : <dl className="revision-settings" aria-label={!revision.view ? t('setup.revisionSettings') : undefined}>
        {lines.map((line, index) => <div key={index}><dt>{line.label}</dt><dd className="parameter-value">{line.value}</dd></div>)}
      </dl>}
    </details>
    <details open={(revision.assetBindings?.length ?? 0) <= 2}>
      <summary>{t('logicDetail.targets', { count: revision.assetBindings?.length ?? 0 })}</summary>
      <ul className="configured-binding-list">{revision.assetBindings?.map((binding, index) => <li key={index}>
        <strong>{binding.role}</strong> · {name(binding.assetId)}
        {describeSettings(binding.parameterOverridesJson, revision.moduleManifestJson)?.map((line, i) =>
          <p className="parameter-value" key={i}>{line.label}: {line.value}</p>)}
      </li>)}</ul>
    </details>
    <details open={(revision.dataBindings?.length ?? 0) <= 2}>
      <summary>{t('logicDetail.providers', { count: revision.dataBindings?.length ?? 0 })}</summary>
      <ul className="configured-binding-list">{revision.dataBindings?.map((binding, index) => <li key={index}>
        <strong>{binding.requirementId}</strong> · {binding.providerId} · {name(binding.assetId)}
      </li>)}</ul>
    </details>
    <details><summary>{t('assetDetail.technical')}</summary><dl className="logic-facts">
      <div><dt>{t('logicDetail.revisionId')}</dt><dd><code>{revision.id}</code></dd></div>
      <div><dt>{t('logicDetail.created')}</dt><dd>{formatTimestamp(revision.createdAtUtc)}</dd></div>
    </dl></details>
  </div>;
}
