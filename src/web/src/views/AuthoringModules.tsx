import { useEffect, useMemo, useState } from 'react';
import {
  deleteGuidedModuleDraft,
  listGuidedModuleDrafts,
  listGuidedModuleProviders,
  listLogicModules,
  listPendingGuidedModuleSubmissions,
} from '../api/generated/v1';
import type { GuidedModuleDraftResponse, GuidedProviderResponse } from '../api/generated/v1';
import { ActionButton, ActionLink } from '../components/ActionButton';
import { PanelHeader } from '../components/common';
import { ContextLink } from '../components/ContextLink';
import { DataTable } from '../components/DataTable';
import { ConfirmPanel, FormActions } from '../components/FormFields';
import { FilterToolbar } from '../components/FilterToolbar';
import { MultiSelect } from '../components/MultiSelect';
import { PanelLoading } from '../components/LoadingIndicator';
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
  const [providerKeys, setProviderKeys] = useState<string[]>([]);
  const [search, setSearch] = useState('');
  const [discarding, setDiscarding] = useState<GuidedModuleDraftResponse | null>(null);
  const [discardFailed, setDiscardFailed] = useState(false);
  const [busy, setBusy] = useState(false);

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

  const needle = search.trim().toLowerCase();
  const inProvider = modules.filter(
    ({ manifest }) =>
      (providerKeys.length === 0 || providerKeys.includes(manifest.requiredAdapterKey ?? '')) &&
      (!needle || (manifest.displayName ?? '').toLowerCase().includes(needle)),
  );
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

  const chosen = providerKeys.length === 1 ? providers.find((item) => item.adapterKey === providerKeys[0]) : undefined;
  const providerName = (item: GuidedProviderResponse) => item.displayName ?? item.adapterKey ?? '';
  const started = modules.filter(({ stage }) => stage !== 'notStarted');
  const countFor = (key: string | null | undefined) =>
    started.filter(({ manifest }) => manifest.requiredAdapterKey === key).length;
  const nameOf = (key: string | undefined) => {
    const match = providers.find((item) => item.adapterKey === key);
    return match ? providerName(match) : (key ?? '—');
  };

  const discard = async (target: GuidedModuleDraftResponse) => {
    setBusy(true);
    setDiscardFailed(false);
    try {
      const { error } = await deleteGuidedModuleDraft({
        path: { id: target.id ?? '' },
        query: { expectedRevision: target.revision },
      });
      if (error) {
        setDiscardFailed(true);
        return;
      }
      setDrafts((current) => (current ?? []).filter((item) => item.id !== target.id));
      setDiscarding(null);
    } catch {
      setDiscardFailed(true);
    } finally {
      setBusy(false);
    }
  };

  const newModule = (
    <ActionLink
      href={hashFor('authoring', chosen?.adapterKey ? `new:${chosen.adapterKey}` : 'new')}
      label={chosen ? t('flow.newProviderModule', { provider: providerName(chosen) }) : t('flow.startModule')}
      variant="primary"
    />
  );

  return (
    <section className="panel" aria-label={t('flow.modulesTitle')}>
      <PanelHeader title={t('flow.modulesTitle')} actions={newModule} />
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
      {!failed && drafts === null && <PanelLoading label={t('flow.draftsLoading')} />}
      {drafts?.length === 0 && (
        <Notice>
          {t('flow.firstModuleTitle')}. {t('flow.firstModuleBody')}
        </Notice>
      )}
      {drafts && drafts.length > 0 && (
        <>
          <FilterToolbar
            label={t('flow.moduleFilters')}
            search={{
              value: search,
              onChange: setSearch,
              label: t('flow.searchModules'),
              placeholder: t('flow.searchModules'),
            }}
            chips={providerKeys.map((key) => ({
              id: key,
              label: nameOf(key),
              onRemove: () => setProviderKeys(providerKeys.filter((item) => item !== key)),
            }))}
            isFiltered={providerKeys.length > 0 || search !== ''}
            onClear={() => {
              setProviderKeys([]);
              setSearch('');
            }}
            note={t('flow.modulesShown', { shown: String(visible.length), total: String(started.length) })}
          >
            {providers.length > 1 && (
              <MultiSelect
                label={t('flow.provider')}
                allLabel={t('flow.allProviders')}
                options={providers.map((item) => ({
                  value: item.adapterKey ?? '',
                  label: `${providerName(item)} (${countFor(item.adapterKey)})`,
                }))}
                selected={providerKeys}
                onChange={setProviderKeys}
              />
            )}
          </FilterToolbar>
          <SectionTabs
            prefix="authoring-stage"
            label={t('flow.stageFilter')}
            tabs={tabs}
            selected={stageFilter}
            panelId={panelId}
            onSelect={setStageFilter}
          />
          {discarding && (
            <ConfirmPanel
              title={t('flow.discardTitle', { name: manifestOf(discarding).displayName || t('flow.untitled') })}
              focusOnOpen
            >
              <p className="ui-section-hint">{t('flow.discardBody')}</p>
              {discardFailed && <Notice tone="error">{t('flow.discardFailed')}</Notice>}
              <FormActions>
                <ActionButton label={t('flow.discardNo')} disabled={busy} onClick={() => setDiscarding(null)} />
                <ActionButton
                  variant="primary"
                  label={t('flow.discardYes')}
                  disabled={busy}
                  onClick={() => void discard(discarding)}
                />
              </FormActions>
            </ConfirmPanel>
          )}
          <div id={panelId} role="tabpanel" aria-labelledby={`authoring-stage-tab-${stageFilter}`}>
            {visible.length === 0 ? (
              <Notice>{t('flow.noModulesInView')}</Notice>
            ) : (
              <DataTable label={t('flow.modulesTitle')}>
                <thead>
                  <tr>
                    <th scope="col">{t('flow.draftColName')}</th>
                    <th scope="col">{t('flow.draftColProvider')}</th>
                    <th scope="col">{t('flow.draftColStatus')}</th>
                    <th scope="col">{t('flow.draftColUpdated')}</th>
                    <th scope="col">{t('flow.draftColActions')}</th>
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
                      <td>
                        {(stage === 'inProgress' || stage === 'notStarted') && (
                          <ActionButton
                            label={t('flow.discard')}
                            aria-label={t('flow.discardNamed', { name: manifest.displayName || t('flow.untitled') })}
                            variant="quiet"
                            onClick={() => {
                              setDiscardFailed(false);
                              setDiscarding(draft);
                            }}
                          />
                        )}
                      </td>
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
