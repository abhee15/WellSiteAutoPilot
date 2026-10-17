import './awaitingAccess.css';
import { useState } from 'react';
import type { CurrentUserResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';

/** Shown to an authenticated user who has no effective permissions yet. */
export function AwaitingAccessNotice({ user }: { user: CurrentUserResponse }) {
  const [copied, setCopied] = useState(false);
  const identityName = user.identityName ?? '';
  const copy = () => {
    void navigator.clipboard?.writeText(identityName).then(() => setCopied(true), () => setCopied(false));
  };
  return (
    <section className="awaiting-access" aria-labelledby="awaiting-access-heading">
      <h2 id="awaiting-access-heading">{t('awaitingAccess.heading')}</h2>
      <p>{t('awaitingAccess.body')}</p>
      <dl>
        <dt>{t('awaitingAccess.identity')}</dt>
        <dd><code>{identityName}</code></dd>
      </dl>
      <button type="button" onClick={copy}>{copied ? t('awaitingAccess.copied') : t('awaitingAccess.copy')}</button>
    </section>
  );
}
