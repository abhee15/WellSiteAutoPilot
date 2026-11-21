import type { MessageKey } from '../localization';

/**
 * The words for why a scheduled run did not start. The server sends a stable reason code; the page owns the wording,
 * so a code it does not know yet still reads as a sentence that names the code instead of showing nothing.
 */
const REASONS: Readonly<Record<string, MessageKey>> = {
  EXECUTION_READ_AUTHORITY_REQUIRED: 'schedules.reason.readAuthority',
  SCHEDULER_READ_AUTHORITY_REVOKED: 'schedules.reason.authorityRevoked',
  SCHEDULER_MODULE_NOT_INSTALLED: 'schedules.reason.moduleMissing',
  SCHEDULER_MODULE_NOT_ENABLED: 'schedules.reason.moduleDisabled',
  MODULE_NOT_ENABLED: 'schedules.reason.moduleDisabled',
  SCHEDULER_EXECUTION_OVERLAP: 'schedules.reason.overlap',
  EXECUTION_ASSET_OVERLAP: 'schedules.reason.overlap',
  SCHEDULER_ASSET_SCOPE_EMPTY: 'schedules.reason.noAssets',
  SCHEDULER_CONFIGURATION_INVALID: 'schedules.reason.configInvalid',
  SCHEDULER_ACTIVE_REVISION_INVALID: 'schedules.reason.revisionInvalid',
  SCHEDULER_DISPATCH_FAILED: 'schedules.reason.dispatchFailed',
};

export function scheduleBlockReasonKey(reasonCode: string): MessageKey {
  return (
    REASONS[reasonCode] ??
    (reasonCode.startsWith('EXECUTION_READ_') ? 'schedules.reason.read' : 'schedules.reason.unknown')
  );
}
