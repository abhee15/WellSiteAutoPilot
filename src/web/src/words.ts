/**
 * Plain-language wording for values the API reports as codes. The raw value is never hidden:
 * callers keep it in a tooltip or disclosure for support. Terms follow docs/design/ux-plan.md.
 */

function spaced(value: string): string {
  const words = value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim()
    .toLowerCase();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : value;
}

const STATUS_LABELS: Record<string, string> = {
  TimedOut: 'Timed out',
};

/** A status word for display. Unknown values are spaced out rather than shown as code. */
export function statusLabel(value?: string | null): string {
  if (!value) return 'Unknown';
  return STATUS_LABELS[value] ?? spaced(value);
}

const TRUST_LABELS: Record<string, string> = {
  Untrusted: 'Unverified',
  TrustedPublisher: 'Trusted publisher',
  CustomerApproved: 'Approved by your company',
  SignatureInvalid: 'Signature invalid',
};

export function trustLabel(value?: string | null): string {
  return value ? TRUST_LABELS[value] ?? spaced(value) : 'Unknown';
}

const PROFILE_LABELS: Record<string, string> = {
  ScheduledOneShot: 'Runs on a schedule',
  BoundedIterative: 'Repeats until done',
  Reactive: 'Reacts to changes',
  ContinuousController: 'Runs continuously',
  DurableWorkflow: 'Long-running workflow',
};

export function profileLabel(value?: string | null): string {
  return value ? PROFILE_LABELS[value] ?? spaced(value) : '—';
}

const OUTCOME_LABELS: Record<string, string> = {
  RecommendationProduced: 'Produced a recommendation',
  EXECUTION_INPUT_STALE: 'An input was too old',
  EXECUTION_INPUT_QUALITY_REJECTED: 'An input’s data quality was not good enough',
  EXECUTION_INPUT_QUANTITY_MISMATCH: 'An input measured the wrong quantity',
  EXECUTION_INPUT_FUTURE: 'An input had a timestamp in the future',
  EXECUTION_INPUT_ACCESS_UNSUPPORTED: 'An input needs history access, which is not supported yet',
  EXECUTION_ASSET_OVERLAP: 'Another run was already using one of the assets',
  MODULE_NOT_AVAILABLE: 'The logic module is not available (disabled or not installed)',
  MODULE_NOT_INSTALLED: 'The logic module is not installed',
  PROVIDER_TIMEOUT: 'The data source did not respond in time',
};

/** What happened, in words. Returns the readable text and the original code for a tooltip. */
export function outcomeLabel(code?: string | null): { text: string; code: string | null } {
  if (!code) return { text: '', code: null };
  return { text: OUTCOME_LABELS[code] ?? spaced(code), code };
}
