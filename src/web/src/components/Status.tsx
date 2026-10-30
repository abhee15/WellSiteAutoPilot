import { CircleCheck, CircleMinus, CircleX, Loader, TriangleAlert } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { statusLabel } from '../words';

/**
 * The one way the app shows a state (Healthy, Failed, Running, Draft, ...): a small shape and a plain word.
 * Every status in every table, list and panel goes through here, so changing how a status looks, or which
 * words count as a problem, is a change to this file and the `.status-indicator` rules in styles.css.
 *
 * Meaning never rests on colour alone: each tone has its own shape (and the word is always shown).
 */
export type StatusTone = 'success' | 'warning' | 'error' | 'info' | 'neutral';

/** One shape per tone, always the same, so a shape means the same thing wherever it appears. */
const ICONS: Record<StatusTone, LucideIcon> = {
  success: CircleCheck,
  warning: TriangleAlert,
  error: CircleX,
  info: Loader,
  neutral: CircleMinus,
};

/** Which tone each reported state has. Keys are lower-case letters and digits only (see `normalize`). */
const TONES: Record<string, StatusTone> = {
  // Done, healthy, trusted.
  completed: 'success', approved: 'success', active: 'success', healthy: 'success', good: 'success',
  trustedpublisher: 'success', customerapproved: 'success',
  // Needs a look, not broken.
  pending: 'warning', awaitingreview: 'warning', suspended: 'warning', waiting: 'warning', untrusted: 'warning', draft: 'warning', validated: 'warning',
  degraded: 'warning', needsattention: 'warning', outdated: 'warning',
  // Broken or unreachable.
  failed: 'error', timedout: 'error', signatureinvalid: 'error', unavailable: 'error', unreachable: 'error', bad: 'error',
  // In motion.
  running: 'info', starting: 'info', queued: 'info', requested: 'info',
  // Anything else (inactive, hidden, retired, disabled, not tested, unknown, ...) is quiet and grey.
};

function normalize(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]/g, '');
}

/** The tone for a reported state; a state this app does not know yet is neutral, never alarming. */
export function statusTone(value?: string | null): StatusTone {
  return (value ? TONES[normalize(value)] : undefined) ?? 'neutral';
}

/** A tone and a word, for the places that already know both. Prefer `Status` when you have the reported value. */
export function StatusIndicator({ tone, label }: { tone: StatusTone; label: string }) {
  const Icon = ICONS[tone];
  return (
    <span className="status-indicator" data-tone={tone}>
      <Icon className="status-indicator-icon" size={14} strokeWidth={2} aria-hidden="true" focusable="false" />
      <span className="status-indicator-label">{label}</span>
    </span>
  );
}

/** The reported state, in plain words. Pass `label` when the page words it differently from the default. */
export function Status({ value, label, tone }: { value?: string | null; label?: string; tone?: StatusTone }) {
  return <StatusIndicator tone={tone ?? statusTone(value)} label={label ?? statusLabel(value)} />;
}
