import { useState } from 'react';
import type { AdapterCapabilityGroupResponse, AdapterOperationResponse, AdapterOperationSchemaResponse } from '../api/generated/v1/types.gen';
import { DataTable } from '../components/DataTable';
import { SelectField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { StatusIndicator } from '../components/Status';
import { SectionTabs } from '../components/SectionTabs';
import { t } from '../localization';
import { AdapterWorkflowReference } from './AdapterWorkflowReference';
import { CodeBlock } from '../components/CodeBlock';

/** Capability-level reference; field variants remain inside their declared family and never issue a data read. */
export function AdapterCapabilityDetails({ capability, operations }: {
  capability: AdapterCapabilityGroupResponse; operations: AdapterOperationResponse[];
}) {
  const [tab, setTab] = useState<'overview' | 'api' | 'contracts' | 'limits'>('overview');
  const [variant, setVariant] = useState(operations[0]?.key ?? '');
  const operation = operations.find(item => item.key === variant) ?? operations[0];
  const prefix = 'capability-details';
  return <>
    <SectionTabs prefix={prefix} label={t('adapterReference.details')} selected={tab} onSelect={setTab}
      tabs={[{ id: 'overview', label: t('adapterReference.overview') }, { id: 'api', label: t('adapterReference.api') },
        { id: 'contracts', label: t('adapterReference.contracts') }, { id: 'limits', label: t('adapterReference.limits') }]} />
    <div role="tabpanel" id={prefix + '-' + tab} aria-labelledby={prefix + '-tab-' + tab} className="split-type-detail">
      {tab === 'overview' && <>
        <p>{capability.description}</p>
        {[...new Set(operations.map(item => item.documentation?.executionBoundary).filter((boundary): boundary is string => !!boundary))]
          .map(boundary => <Notice key={boundary} announce={false}>{boundary}</Notice>)}
        {!!capability.workflows?.length && <section className="adapter-readiness-summary" aria-label={t('adapterReference.workflowReadiness')}>
          <h4>{t('adapterReference.workflowReadiness')}</h4>
          <ul>{capability.workflows.map(workflow => <li key={workflow.key ?? ''}>
            <span>{workflow.displayName}</span>
            <StatusIndicator tone={workflow.readiness === 'Implemented' ? 'success' : 'neutral'}
              label={t(workflow.readiness === 'Implemented' ?
                'adapterReference.workflowImplemented' : 'adapterReference.workflowReferenceOnly')} />
          </li>)}</ul>
          <p>{t('adapterReference.moduleAvailabilityUnverified')}</p>
        </section>}
        <h4>{t('adapterReference.supported')}</h4>
        <DataTable label={t('adapterReference.supported')}>
          <thead><tr><th>{t('adapterReference.data')}</th><th>{t('adapterReference.shape')}</th><th>{t('adapterOperations.colMeaning')}</th></tr></thead>
          <tbody>{operations.map(item => <tr key={item.key}><th scope="row">{item.quantity ?? item.displayName}</th>
            <td>{item.structuredSchemaId ?? item.response?.id}</td><td>{item.description}</td></tr>)}</tbody>
        </DataTable>
        <p>{t('adapterReference.scope')}</p>
      </>}
      {tab !== 'overview' && operation && <>
        {operations.length > 1 && <SelectField label={t('adapterReference.variant')} value={operation.key ?? ''} onChange={event => setVariant(event.target.value)}>
          {operations.map(item => <option key={item.key} value={item.key ?? ''}>{item.quantity ?? item.displayName}</option>)}
        </SelectField>}
        {tab === 'api' && (operation.documentation ? <>
          <AdapterWorkflowReference capability={capability} operation={operation} />

          {operation.documentation.providerReference && <><h4>{t('adapterReference.providerMapping')}</h4><p>{operation.documentation.providerReference}</p></>}
          <h4>{t('adapterReference.invocation')}</h4><CodeBlock label={t('adapterReference.invocation')}><code>{operation.documentation.invocation}</code></CodeBlock>
          <Notice announce={false}>{operation.documentation.executionBoundary}</Notice>
          <p>{t('adapterReference.synthetic')}</p>
          <h4>{t('adapterReference.requestExample')}</h4><CodeBlock label={t('adapterReference.requestExample')}><code>{operation.documentation.requestExampleJson}</code></CodeBlock>
          <h4>{t('adapterReference.responseExample')}</h4><CodeBlock label={t('adapterReference.responseExample')}><code>{operation.documentation.responseExampleJson}</code></CodeBlock>
        </> : <Notice announce={false}>{t('adapterReference.noExample')}</Notice>)}
        {tab === 'contracts' && <>
          <dl className="operation-facts"><dt>{t('adapterReference.contractVersion')}</dt><dd>{operation.contractVersion}</dd>
            <dt>{t('adapterReference.resource')}</dt><dd>{operation.resourceKind ?? t('adapterReference.adapterDefined')}</dd></dl>
          {operation.request && <Contract title={t('adapterReference.request')} schema={operation.request} />}
          {operation.response && <Contract title={t('adapterReference.response')} schema={operation.response} />}
          {operation.structuredResponse && <Contract title={t('adapterReference.payload')} schema={operation.structuredResponse} />}
          <section aria-label={t('adapterReference.dataTrust')}>
            <h4>{t('adapterReference.dataTrust')}</h4>
            <dl className="adapter-trust-facts">
              <dt>{t('adapterReference.timestamps')}</dt><dd>{operation.timestampSemantics}</dd>
              <dt>{t('adapterReference.quality')}</dt><dd>{operation.qualitySemantics}</dd>
            </dl>
            <p>{t('adapterReference.trustPolicy')}</p>
          </section>
        </>}
        {tab === 'limits' && <>
          <dl className="operation-facts">
            <dt>{t('adapterReference.quantities')}</dt><dd>{operation.limits?.maximumQuantities}</dd>
            {operation.limits?.maximumSourceBytes != null && <><dt>{t('adapterReference.sourceBytes')}</dt><dd>{operation.limits.maximumSourceBytes.toLocaleString()} bytes</dd></>}
            <dt>{t('adapterReference.valueBytes')}</dt><dd>{operation.limits?.maximumValueBytes?.toLocaleString()} bytes</dd>
            <dt>{t('adapterReference.replyBytes')}</dt><dd>{operation.limits?.maximumReplyBytes?.toLocaleString()} bytes</dd>
            {operation.limits?.timeoutSeconds != null && <><dt>{t('adapterReference.deadline')}</dt><dd>{t('adapterReference.seconds', { count: operation.limits.timeoutSeconds })}</dd></>}
            {operation.limits?.maximumWindowDays != null && <><dt>{t('adapterReference.window')}</dt><dd>{t('adapterReference.days', { count: operation.limits.maximumWindowDays })}</dd></>}
            {operation.limits?.maximumSamples != null && <><dt>{t('adapterReference.samples')}</dt><dd>{operation.limits.maximumSamples}</dd></>}
          </dl><p>{t('adapterReference.executionLimits')}</p>
        </>}
      </>}
    </div>
  </>;
}
function Contract({ title, schema }: { title: string; schema: AdapterOperationSchemaResponse }) {
  return <><h4>{title}</h4><code>{schema.id}</code><p>{schema.dataType}</p>
    <DataTable label={t('adapterReference.contractTable', { title })}>
      <thead><tr><th>{t('adapterOperations.colField')}</th><th>{t('adapterOperations.colType')}</th><th>{t('adapterOperations.colRequired')}</th><th>{t('adapterOperations.colMeaning')}</th></tr></thead>
      <tbody>{schema.fields?.map(field => <tr key={field.name}><th scope="row">{field.name}</th><td>{field.dataType}</td>
        <td>{field.required ? t('adapterReference.yes') : t('adapterReference.no')}</td><td>{field.description}</td></tr>)}</tbody>
    </DataTable>
  </>;
}
