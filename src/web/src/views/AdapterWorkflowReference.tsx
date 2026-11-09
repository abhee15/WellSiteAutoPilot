import type { AdapterCapabilityGroupResponse, AdapterOperationResponse } from '../api/generated/v1/types.gen';
import { Notice } from '../components/Notice';
import { StatusIndicator } from '../components/Status';
import { t } from '../localization';

/**
 * Adapter-owned reference documentation, never an executable provider workflow.
 * All content comes from the validated capability descriptor, not provider-specific UI branches.
 */
export function AdapterWorkflowReference({
  capability,
  operation,
}: {
  capability: AdapterCapabilityGroupResponse;
  operation: AdapterOperationResponse;
}) {
  const workflows = capability.workflows?.filter((item) => item.operationKeys?.includes(operation.key ?? '')) ?? [];
  if (!workflows.length) return null;

  return (
    <section className="adapter-workflows" aria-label={t('adapterReference.workflow')}>
      <h4>{t('adapterReference.workflow')}</h4>
      {workflows.map((workflow) => (
        <section className="adapter-workflow" key={workflow.key ?? ''} aria-label={workflow.displayName ?? undefined}>
          <header className="adapter-workflow-header">
            <div>
              <h5>{workflow.displayName}</h5>
              <p>{workflow.purpose}</p>
            </div>
            <StatusIndicator
              tone={workflow.readiness === 'Implemented' ? 'success' : 'neutral'}
              label={t(
                workflow.readiness === 'Implemented'
                  ? 'adapterReference.workflowImplemented'
                  : 'adapterReference.workflowReferenceOnly',
              )}
            />
          </header>
          <div className="adapter-workflow-body">
            <ol className="adapter-workflow-steps">
              {workflow.steps?.map((step) => (
                <li key={step.key ?? ''}>
                  <strong>{step.description}</strong>
                  {step.providerApi && (
                    <div className="adapter-workflow-api">
                      <code>{step.providerApi}</code>
                    </div>
                  )}
                  {step.requirement && (
                    <p>
                      <b>{t('adapterReference.workflowRequires')}:</b> {step.requirement}
                    </p>
                  )}
                  {step.onFailure && (
                    <p>
                      <b>{t('adapterReference.workflowOnFailure')}:</b> {step.onFailure}
                    </p>
                  )}
                </li>
              ))}
            </ol>
            <aside className="adapter-workflow-guidance" aria-label={t('adapterReference.authoringGuidance')}>
              <h5>{t('adapterReference.authoringGuidance')}</h5>
              <p>{t('adapterReference.authoringBoundary')}</p>
              <Notice announce={false}>{t('adapterReference.moduleAvailabilityUnverified')}</Notice>
              {workflow.readiness !== 'Implemented' && <p>{t('adapterReference.workflowPendingNote')}</p>}
            </aside>
          </div>
        </section>
      ))}
    </section>
  );
}
