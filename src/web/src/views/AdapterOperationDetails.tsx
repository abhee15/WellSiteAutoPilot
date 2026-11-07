import { DataTable } from '../components/DataTable';
import { useState } from 'react';
import type { AdapterOperationResponse, AdapterOperationSchemaResponse } from '../api/generated/v1/types.gen';
import { SectionTabs } from '../components/SectionTabs';
import { t } from '../localization';

/** Reusable read contract inspector, independent of provider and connection selection. */
export function AdapterOperationDetails({ operation }: { operation: AdapterOperationResponse }) {
  const [tab, setTab] = useState<'overview' | 'contracts' | 'limits'>('overview');
  const prefix = 'operation-details';
  return <>
    <SectionTabs prefix={prefix} label="Operation details" selected={tab} onSelect={setTab}
      tabs={[{ id: 'overview', label: 'Overview' }, { id: 'contracts', label: 'Request and response' }, { id: 'limits', label: 'Limits' }]} />
    <div role="tabpanel" id={prefix + '-' + tab} aria-labelledby={prefix + '-tab-' + tab} className="split-type-detail">
      {tab === 'overview' && <>
        <p>{operation.description}</p>
        <dl className="operation-facts">
          <dt>Capability</dt><dd>{operation.capabilityId}</dd>
          <dt>Contract version</dt><dd>{operation.contractVersion}</dd>
          <dt>Quantity</dt><dd>{operation.quantity ?? 'Declared by the request'}</dd>
          <dt>Resource</dt><dd>{operation.resourceKind ?? 'Adapter-defined'}</dd>
          <dt>Units</dt><dd>{operation.unit ?? 'Returned by the source or configured point mapping'}</dd>
          {operation.structuredSchemaId && <><dt>Structured schema</dt><dd>{operation.structuredSchemaId}</dd></>}
        </dl>
        <h4>Timestamps</h4><p>{operation.timestampSemantics}</p>
        <h4>Quality</h4><p>{operation.qualitySemantics}</p>
      </>}
      {tab === 'contracts' && <>{operation.request && <Contract title="Request" schema={operation.request} />}
        {operation.response && <Contract title="Response" schema={operation.response} />}
        {operation.structuredResponse && <Contract title="Structured payload" schema={operation.structuredResponse} />}</>}
      {tab === 'limits' && <>
        <dl className="operation-facts">
          <dt>Quantities per read</dt><dd>{operation.limits?.maximumQuantities}</dd>
          <dt>Value payload</dt><dd>{operation.limits?.maximumValueBytes?.toLocaleString()} bytes</dd>
          <dt>Reply payload</dt><dd>{operation.limits?.maximumReplyBytes?.toLocaleString()} bytes</dd>
          {operation.limits?.timeoutSeconds != null && <><dt>Gateway deadline</dt><dd>{operation.limits.timeoutSeconds} seconds</dd></>}
          {operation.limits?.maximumWindowDays != null && <><dt>History window</dt><dd>{operation.limits.maximumWindowDays} days maximum</dd></>}
          {operation.limits?.maximumSamples != null && <><dt>History samples</dt><dd>{operation.limits.maximumSamples} maximum; inspect truncation</dd></>}
        </dl>
        <p>These are enforced read bounds. Scheduling frequency and individual asset permissions are checked separately.</p>
      </>}
    </div>
  </>;
}
function Contract({ title, schema }: { title: string; schema: AdapterOperationSchemaResponse }) {
  return <>
    <h4>{title}</h4><code>{schema.id}</code><p>{schema.dataType}</p>
    <DataTable label={`${title} contract`}>
      <thead><tr><th>{t('adapterOperations.colField')}</th><th>{t('adapterOperations.colType')}</th><th>{t('adapterOperations.colRequired')}</th><th>{t('adapterOperations.colMeaning')}</th></tr></thead>
      <tbody>{schema.fields?.map(field => <tr key={field.name}><th scope="row">{field.name}</th><td>{field.dataType}</td>
        <td>{field.required ? 'Yes' : 'No'}</td><td>{field.description}</td></tr>)}</tbody>
    </DataTable>
  </>;
}
