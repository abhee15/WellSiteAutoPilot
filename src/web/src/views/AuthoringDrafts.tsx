import { useEffect, useState } from 'react';
import { listGuidedModuleDrafts } from '../api/generated/v1';
import type { GuidedModuleDraftResponse } from '../api/generated/v1';
import { ContextLink } from '../components/ContextLink';
import { DataTable } from '../components/DataTable';
import { Notice } from '../components/Notice';
import { StatusIndicator } from '../components/Status';
import { formatTimestamp } from '../format';
import { t } from '../localization';
import { hashFor } from '../routing';

type Manifest = { displayName?: string; requiredAdapterKey?: string };

function manifestOf(draft: GuidedModuleDraftResponse): Manifest {
  try {
    return JSON.parse(draft.manifestJson ?? '{}') as Manifest;
  } catch {
    return {};
  }
}

function kindLabel(draft: GuidedModuleDraftResponse) {
  if (draft.rulesJson) return t('flow.draftKindRules');
  return draft.definition ? t('flow.draftKindThreshold') : t('flow.draftKindContract');
}

/** The author's saved drafts, shown first so returning to unfinished work is one click. */
export function AuthoringDrafts() {
  const [drafts, setDrafts] = useState<GuidedModuleDraftResponse[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    void listGuidedModuleDrafts({ query: { offset: 0, limit: 25 }, signal: controller.signal })
      .then(({ data }) => {
        if (controller.signal.aborted) return;
        if (data) setDrafts(data);
        else setFailed(true);
      })
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    return () => controller.abort();
  }, []);

  return (
    <section className="authoring-drafts" aria-labelledby="authoring-drafts-title">
      <h2 id="authoring-drafts-title">{t('flow.yourDrafts')}</h2>
      {failed && <Notice tone="error">{t('flow.draftsFailed')}</Notice>}
      {!failed && drafts === null && <p>{t('flow.draftsLoading')}</p>}
      {drafts?.length === 0 && <p>{t('flow.noDrafts')}</p>}
      {drafts && drafts.length > 0 && (
        <DataTable label={t('flow.yourDrafts')}>
          <thead>
            <tr>
              <th scope="col">{t('flow.draftColName')}</th>
              <th scope="col">{t('flow.draftColKind')}</th>
              <th scope="col">{t('flow.draftColProvider')}</th>
              <th scope="col">{t('flow.draftColStatus')}</th>
              <th scope="col">{t('flow.draftColUpdated')}</th>
            </tr>
          </thead>
          <tbody>
            {drafts.map((draft) => {
              const manifest = manifestOf(draft);
              const name = manifest.displayName || t('flow.untitled');
              return (
                <tr key={draft.id}>
                  <th scope="row">
                    <ContextLink appearance="record" href={hashFor('authoring', `draft:${draft.id}`)} label={name} />
                  </th>
                  <td>{kindLabel(draft)}</td>
                  <td>{manifest.requiredAdapterKey ?? '—'}</td>
                  <td>
                    <StatusIndicator
                      tone={draft.submitted ? 'success' : 'neutral'}
                      label={draft.submitted ? t('flow.draftSubmitted') : t('flow.draftEditing')}
                    />
                  </td>
                  <td>{formatTimestamp(draft.updatedAtUtc)}</td>
                </tr>
              );
            })}
          </tbody>
        </DataTable>
      )}
    </section>
  );
}
