import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';

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

/** A small "Copy" button for an identifier; says "Copied" for a moment. `label` names what is copied, for screen readers. */
export function CopyButton({ value, label }: { value: string; label: string }) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(timer.current), []);
  return (
    <button type="button" className="copy-button" aria-label={t('execution.copy', { label })}
      onClick={() => {
        void copyText(value).then(done => {
          if (!done) return;
          setCopied(true);
          window.clearTimeout(timer.current);
          timer.current = window.setTimeout(() => setCopied(false), 1500);
        });
      }}>
      {copied ? t('execution.copied') : t('execution.copyShort')}
    </button>
  );
}
