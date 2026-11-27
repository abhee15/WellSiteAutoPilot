import { useEffect, useMemo, useState } from 'react';
import {
  listGuidedModuleDrafts,
  listGuidedModuleProviders,
  listLogicModules,
  listPendingGuidedModuleSubmissions,
} from '../api/generated/v1';
import type { GuidedModuleDraftResponse, GuidedProviderResponse } from '../api/generated/v1';
import { ActionButton, ActionLink } from '../components/ActionButton';
import { SummaryCard } from '../components/common';
import { ContextLink } from '../components/ContextLink';
import { DataTable } from '../components/DataTable';
import { Notice } from '../components/Notice';
import { SectionTabs } from '../components/SectionTabs';
import type { SectionTab } from '../components/SectionTabs';
import { StatusIndicator } from '../components/Status';
import type { StatusTone } from '../components/Status';
import { formatTimestamp } from '../format';
import { t } from '../localization';
import { hashFor } from '../routing';
import { moduleStage } from './moduleStage';
import type { ModuleStage } from './moduleStage';

type Manifest = { moduleId?: string; displayName?: string; requiredAdapterKey?: string };
type StageFilter = 'all' | ModuleStage;

function manifestOf(draft: GuidedModuleDraftResponse): Manifest {
  try {
    return JSON.parse(draft.manifestJson ?? '{}') as Manifest;
  } catch {
    return {};
  }
}

const stageTone: Record<ModuleStage, StatusTone> = {
  notStarted: 'neutral',
  inProgress: 'neutral',
  review: 'info',
  approved: 'success',
};

const stageLabel: Record<ModuleStage, () => string> = {
  notStarted: () => t('flow.stageNotStarted'),
  inProgress: () => t('flow.stageInProgress'),
  review: () => t('flow.stageReview'),
  approved: () => t('flow.stageApproved'),
};

const pageSize = 25;
const panelId = 'authoring-modules-panel';

/**
 * The author's modules. A module is the thing being built; a draft is only its first stage, so the list is organised
 * by stage (in progress, in review, approved) and by the provider it works with. Modules nobody has started are kept
 * out of the default view so they do not bury real work.
 */
export function AuthoringModules() {
  const [drafts, setDrafts] = useState<GuidedModuleDraftResponse[] | null>(null);
  const [providers, setProviders] = useState<GuidedProviderResponse[]>([]);
  const [installed, setInstalled] = useState<ReadonlySet<string> | null>(null);
  const [pending, setPending] = useState<ReadonlySet<string> | null>(null);
  const [failed, setFailed] = useState(false);
  const [offset, setOffset] = useState(0);
  const [attempt, setAttempt] = useState(0);
  const [stageFilter, setStageFilter] = useState<StageFilter>('all');
  const [providerKey, setProviderKey] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    void listGuidedModuleDrafts({ query: { offset, limit: pageSize }, signal: controller.signal })
      .then(({ data }) => {
        if (controller.signal.aborted) return;
        if (data) {
          setDrafts((current) => (offset === 0 ? data : [...(current ?? []), ...data]));
          // Every page is read, so the stage and provider counts describe all of the author's modules.
          if (data.length === pageSize) setOffset(offset + pageSize);
        } else setFailed(true);
      })
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    return () => controller.abort();
  }, [offset, attempt]);

  // Only the stage and the provider cards depend on these; without them the list still works and stays "in progress".
  useEffect(() => {
    const controller = new AbortController();
    const ids = <T,>(rows: T[] | undefined, pick: (row: T) => string | null | undefined) =>
      new Set((rows ?? []).flatMap((row) => pick(row) ?? []));
    void listLogicModules({ query: { limit: 200 }, signal: controller.signal })
      .then(({ data }) => {
        if (data && !controller.signal.aborted) setInstalled(ids(data, (item) => item.moduleId));
      })
      .catch(() => undefined);
    void listPendingGuidedModuleSubmissions({ query: { limit: 200 }, signal: controller.signal })
      .then(({ data }) => {
        if (data && !controller.signal.aborted) setPending(ids(data, (item) => item.draftId));
      })
      .catch(() => undefined);
    void listGuidedModuleProviders({ signal: controller.signal })
      .then(({ data }) => {
        if (data && !controller.signal.aborted) setProviders(data);
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, [attempt]);

  const modules = useMemo(
    () =>
      (drafts ?? []).map((draft) => {
        const manifest = manifestOf(draft);
        return { draft, manifest, stage: moduleStage(draft, manifest.moduleId, installed, pending) };
      }),
    [drafts, installed, pending],
  );

  const inProvider = modules.filter(({ manifest }) => !providerKey || manifest.requiredAdapterKey === providerKey);
  const stageCount = (filter: StageFilter) =>
    inProvider.filter(({ stage }) => (filter === 'all' ? stage !== 'notStarted' : stage === filter)).length;
  const visible = inProvider.filter(({ stage }) =>
    stageFilter === 'all' ? stage !== 'notStarted' : stage === stageFilter,
  );

  const tabs: SectionTab<StageFilter>[] = [
    { id: 'all', label: `${t('flow.stageAll')} (${stageCount('all')})` },
    { id: 'inProgress', label: `${stageLabel.inProgress()} (${stageCount('inProgress')})` },
    { id: 'review', label: `${stageLabel.review()} (${stageCount('review')})` },
    { id: 'approved', label: `${stageLabel.approved()} (${stageCount('approved')})` },
    ...(stageCount('notStarted') > 0 || stageFilter === 'notStarted'
      ? [{ id: 'notStarted' as const, label: `${stageLabel.notStarted()} (${stageCount('notStarted')})` }]
      : []),
  ];

  const chosen = providers.find((item) => item.adapterKey === providerKey);
  const providerName = (item: GuidedProviderResponse) => item.displayName ?? item.adapterKey ?? '';
  const started = modules.filter(({ stage }) => stage !== 'notStarted');
  const countFor = (key: string | null | undefined) =>
    started.filter(({ manifest }) => manifest.requiredAdapterKey === key).length;
  const nameOf = (key: string | undefined) => {
    const match = providers.find((item) => item.adapterKey === key);
    return match ? providerName(match) : (key ?? '—');
  };

  const newModule = (
    <ActionLink
      href={hashFor('authoring', chosen?.adapterKey ? `new:${chosen.adapterKey}` : 'new')}
      label={chosen ? t('flow.newProviderModule', { provider: providerName(chosen) }) : t('flow.startModule')}
      variant="primary"
    />
  );

  return (
    <section className="authoring-modules" aria-labelledby="authoring-modules-title">
      <div className="authoring-modules-head">
        <div>
          <h2 id="authoring-modules-title">{t('flow.modulesTitle')}</h2>
          <p>{t('flow.modulesPurpose')}</p>
        </div>
        {newModule}
      </div>
      {failed && (
        <>
          <Notice tone="error">{t('flow.draftsFailed')}</Notice>
          <ActionButton
            label={t('flow.draftsRetry')}
            onClick={() => {
              setFailed(false);
              setAttempt((value) => value + 1);
            }}
          />
        </>
      )}
      {!failed && drafts === null && <p>{t('flow.draftsLoading')}</p>}
      {drafts?.length === 0 && (
        <>
          <h3>{t('flow.firstModuleTitle')}</h3>
          <p>{t('flow.firstModuleBody')}</p>
          {providers.length > 0 && (
            <div className="summary-grid" role="group" aria-label={t('flow.providerFilter')}>
              {providers.map((item) => (
                <SummaryCard
                  key={item.adapterKey}
                  label={providerName(item)}
                  value={t('flow.startWithProvider')}
                  detail=""
                  href={hashFor('authoring', `new:${item.adapterKey}`)}
                />
              ))}
            </div>
          )}
        </>
      )}
      {drafts && drafts.length > 0 && (
        <>
          {providers.length > 1 && (
            <div className="summary-grid" role="group" aria-label={t('flow.providerFilter')}>
              <SummaryCard
                label={t('flow.allProviders')}
                value={String(started.length)}
                detail=""
                selected={providerKey === null}
                onSelect={() => setProviderKey(null)}
              />
              {providers.map((item) => (
                <SummaryCard
                  key={item.adapterKey}
                  label={providerName(item)}
                  value={String(countFor(item.adapterKey))}
                  detail=""
                  selected={providerKey === item.adapterKey}
                  onSelect={() => setProviderKey(providerKey === item.adapterKey ? null : (item.adapterKey ?? null))}
                />
              ))}
            </div>
          )}
          <SectionTabs
            prefix="authoring-stage"
            label={t('flow.stageFilter')}
            tabs={tabs}
            selected={stageFilter}
            panelId={panelId}
            onSelect={setStageFilter}
          />
          <div id={panelId} role="tabpanel" aria-labelledby={`authoring-stage-tab-${stageFilter}`}>
            {visible.length === 0 ? (
              <p>{t('flow.noModulesInView')}</p>
            ) : (
              <DataTable label={t('flow.modulesTitle')}>
                <thead>
                  <tr>
                    <th scope="col">{t('flow.draftColName')}</th>
                    <th scope="col">{t('flow.draftColProvider')}</th>
                    <th scope="col">{t('flow.draftColStatus')}</th>
                    <th scope="col">{t('flow.draftColUpdated')}</th>
                  </tr>
                </thead>
                <tbody>
                  {visible.map(({ draft, manifest, stage }) => (
                    <tr key={draft.id}>
                      <th scope="row">
                        <ContextLink
                          appearance="record"
                          href={hashFor('authoring', `draft:${draft.id}`)}
                          label={manifest.displayName || t('flow.untitled')}
                        />
                      </th>
                      <td>{nameOf(manifest.requiredAdapterKey)}</td>
                      <td>
                        <StatusIndicator tone={stageTone[stage]} label={stageLabel[stage]()} />
                      </td>
                      <td>{formatTimestamp(draft.updatedAtUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </DataTable>
            )}
          </div>
        </>
      )}
    </section>
  );
}
