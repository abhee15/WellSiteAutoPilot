import { useEffect, useRef, useState } from 'react';
import { Check, CircleAlert, Copy } from 'lucide-react';
import { t } from '../localization';
import { ActionButton } from '../components/ActionButton';

/** Copies text with the clipboard API, or with a temporary text box where the page is not a secure origin. */
async function copyText(value: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(value);
      return true;
    }
  } catch { /* fall through to the text box */ }
  try {
    const box = document.createElement('textarea');
    box.value = value;
    box.setAttribute('readonly', '');
    box.style.position = 'fixed';
    box.style.opacity = '0';
    document.body.appendChild(box);
    box.select();
    const copied = document.execCommand('copy');
    box.remove();
    return copied;
  } catch { return false; }
}

/**
 * A copy icon beside an identifier. After the browser confirms the copy it turns into a check for a moment (an alert if
 * the copy failed), and a hidden live region says so, because the icon change alone is invisible to a screen reader.
 * `label` names what is copied ("Record ID"), so each button on a page has its own accessible name.
 */
export function CopyButton({ value, label }: { value: string; label: string }) {
  const [outcome, setOutcome] = useState<'copied' | 'failed' | null>(null);
  const timer = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(timer.current), []);
  const name = t('execution.copy', { label });
  return (
    <>
      <ActionButton className="copy-button" variant="quiet" iconOnly icon={outcome === 'copied' ? Check : outcome === 'failed' ? CircleAlert : Copy}
        label={name} title={outcome === 'copied' ? t('execution.copied') : outcome === 'failed' ? t('execution.copyFailed') : name}
        state={outcome === 'copied' ? 'success' : outcome === 'failed' ? 'error' : undefined}
        onClick={() => {
          void copyText(value).then(done => {
            setOutcome(done ? 'copied' : 'failed');
            window.clearTimeout(timer.current);
            timer.current = window.setTimeout(() => setOutcome(null), 2000);
          });
        }} />
      <span className="visually-hidden" role="status">
        {outcome === 'copied' ? t('execution.copied') : outcome === 'failed' ? t('execution.copyFailed') : ''}
      </span>
    </>
  );
}
