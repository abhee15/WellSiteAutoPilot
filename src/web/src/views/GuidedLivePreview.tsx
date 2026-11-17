import { useEffect, useRef, useState } from 'react';
import {
  checkGuidedModuleAssetCompatibility,
  previewGuidedModuleInputHistory,
  previewGuidedModuleProductionDays,
  previewGuidedModuleSurfaceCards,
  previewGuidedModuleWithLiveInputs,
} from '../api/generated/v1/sdk.gen';
import type {
  GuidedAssetCompatibilityResponse,
  GuidedHistoryPreviewResponse,
  GuidedLivePreviewResponse,
  GuidedProductionDaysPreviewResponse,
  GuidedSurfaceCardsPreviewResponse,
} from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { formatTimestamp } from '../format';
import { t } from '../localization';
import { outcomeLabel } from '../words';
import { GuidedQuantityDiscovery } from './GuidedQuantityDiscovery';
import { GuidedSurfaceCardsPreview } from './GuidedSurfaceCardsPreview';
import { fetchSearch, SEARCH_PAGE_SIZE, type SearchHit } from './assetBrowseApi';

type WellPreview =
  | { kind: 'current'; data: GuidedLivePreviewResponse }
  | { kind: 'history'; data: GuidedHistoryPreviewResponse; siteWindow?: { endLocal: string; timeZoneId: string } }
  | { kind: 'historyTimeError'; message: 'guided.siteTimeUnresolved' | 'guided.siteZoneInvalid' }
  | { kind: 'productionDays'; data: GuidedProductionDaysPreviewResponse }
  | { kind: 'surfaceCards'; data: GuidedSurfaceCardsPreviewResponse }
  | { kind: 'unsupported'; preview: 'productionDays' | 'surfaceCards' };

type Compatibility =
  | { kind: 'checking' }
  | { kind: 'result'; inputs: NonNullable<GuidedAssetCompatibilityResponse['inputs']> }
  | { kind: 'error' };

const compatibilityLabels = {
  Satisfied: 'guided.assetInputSatisfied',
  Unsupported: 'guided.assetInputUnsupported',
  Missing: 'guided.assetInputMissing',
  Rejected: 'guided.assetInputRejected',
  Unavailable: 'guided.assetInputUnavailable',
} as const;

export function GuidedLivePreview({
  draftId,
  revision,
  inputs = [],
}: {
  draftId: string;
  revision: number;
  inputs?: readonly { id: string; quantity: string }[];
}) {
  const [inputId, setInputId] = useState(inputs[0]?.id ?? '');
  const [hours, setHours] = useState('4');
  const [historyAnchor, setHistoryAnchor] = useState('now');
  const [endLocal, setEndLocal] = useState('');
  const [timeZoneId, setTimeZoneId] = useState('');
  const [wellPreview, setWellPreview] = useState<WellPreview | null>(null);
  const [query, setQuery] = useState('');
  const [matches, setMatches] = useState<SearchHit[] | null>(null);
  const [more, setMore] = useState(false);
  const [offset, setOffset] = useState(0);
  const [assetId, setAssetId] = useState('');
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const operation = useRef<AbortController | null>(null);
  const compatibilityRequest = useRef<AbortController | null>(null);
  const [compatibility, setCompatibility] = useState<Compatibility | null>(null);
  useEffect(
    () => () => {
      operation.current?.abort();
      compatibilityRequest.current?.abort();
    },
    [],
  );

  function clearCompatibility() {
    compatibilityRequest.current?.abort();
    setCompatibility(null);
  }

  async function checkWell(selectedAssetId: string) {
    clearCompatibility();
    if (!selectedAssetId) return;
    const controller = new AbortController();
    compatibilityRequest.current = controller;
    setCompatibility({ kind: 'checking' });
    try {
      const { data } = await checkGuidedModuleAssetCompatibility({
        path: { id: draftId },
        body: { expectedRevision: revision, assetId: selectedAssetId },
        signal: controller.signal,
      });
      if (controller.signal.aborted) return;
      setCompatibility(data?.inputs?.length ? { kind: 'result', inputs: data.inputs } : { kind: 'error' });
    } catch {
      if (!controller.signal.aborted) setCompatibility({ kind: 'error' });
    }
  }

  async function search(pageOffset: number) {
    const controller = new AbortController();
    operation.current = controller;
    setBusy(true);
    setFailed(false);
    setMatches(null);
    setAssetId('');
    clearCompatibility();
    setWellPreview(null);
    try {
      const page = await fetchSearch(query.trim(), pageOffset, controller.signal);
      if (controller.signal.aborted) return;
      setMatches(page.filter((asset) => asset.isActive && asset.ownerConnectionId));
      setMore(page.length === SEARCH_PAGE_SIZE);
      setOffset(pageOffset);
    } catch {
      if (!controller.signal.aborted) setFailed(true);
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  async function preview() {
    const controller = new AbortController();
    operation.current = controller;
    setBusy(true);
    setFailed(false);
    setWellPreview(null);
    try {
      const { data } = await previewGuidedModuleWithLiveInputs({
        path: { id: draftId },
        body: { expectedRevision: revision, assetId },
        signal: controller.signal,
      });
      if (controller.signal.aborted) return;
      if (!data) throw new Error();
      setWellPreview({ kind: 'current', data });
    } catch {
      if (!controller.signal.aborted) setFailed(true);
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  async function previewHistory() {
    const controller = new AbortController();
    operation.current = controller;
    setBusy(true);
    setFailed(false);
    setWellPreview(null);
    const end = Math.floor(Date.now() / 1000) * 1000;
    const siteWindow =
      historyAnchor === 'site'
        ? { endLocal, timeZoneId: timeZoneId.trim(), durationMinutes: Number(hours) * 60 }
        : undefined;
    const bounds = siteWindow
      ? { siteWindow }
      : { startUtc: new Date(end - Number(hours) * 3600000).toISOString(), endUtc: new Date(end).toISOString() };
    try {
      const { data, error } = await previewGuidedModuleInputHistory({
        path: { id: draftId },
        body: {
          expectedRevision: revision,
          assetId,
          inputId,
          maximumSamples: 100,
          ...bounds,
        },
        signal: controller.signal,
      });
      if (controller.signal.aborted) return;
      if (error && typeof error === 'object' && 'code' in error) {
        if (error.code === 'GUIDED_HISTORY_SITE_TIME_UNRESOLVED') {
          setWellPreview({ kind: 'historyTimeError', message: 'guided.siteTimeUnresolved' });
          return;
        }
        if (error.code === 'GUIDED_HISTORY_SITE_ZONE_INVALID') {
          setWellPreview({ kind: 'historyTimeError', message: 'guided.siteZoneInvalid' });
          return;
        }
      }
      if (!data) throw new Error();
      setWellPreview({ kind: 'history', data, siteWindow });
    } catch {
      if (!controller.signal.aborted) setFailed(true);
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  async function previewProductionDays() {
    const controller = new AbortController();
    operation.current = controller;
    setBusy(true);
    setFailed(false);
    setWellPreview(null);
    const end = Math.floor(Date.now() / 1000) * 1000;
    try {
      const { data, error } = await previewGuidedModuleProductionDays({
        path: { id: draftId },
        body: {
          expectedRevision: revision,
          assetId,
          startUtc: new Date(end - 7 * 24 * 3600000).toISOString(),
          endUtc: new Date(end).toISOString(),
        },
        signal: controller.signal,
      });
      if (controller.signal.aborted) return;
      if (
        error &&
        typeof error === 'object' &&
        'code' in error &&
        error.code === 'GUIDED_PRODUCTION_DAYS_UNSUPPORTED'
      ) {
        setWellPreview({ kind: 'unsupported', preview: 'productionDays' });
        return;
      }
      if (!data) throw new Error();
      setWellPreview({ kind: 'productionDays', data });
    } catch {
      if (!controller.signal.aborted) setFailed(true);
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  async function previewSurfaceCards() {
    const controller = new AbortController();
    operation.current = controller;
    setBusy(true);
    setFailed(false);
    setWellPreview(null);
    try {
      const { data, error } = await previewGuidedModuleSurfaceCards({
        path: { id: draftId },
        body: { expectedRevision: revision, assetId },
        signal: controller.signal,
      });
      if (controller.signal.aborted) return;
      if (error && typeof error === 'object' && 'code' in error && error.code === 'GUIDED_SURFACE_CARDS_UNSUPPORTED') {
        setWellPreview({ kind: 'unsupported', preview: 'surfaceCards' });
        return;
      }
      if (!data) throw new Error();
      setWellPreview({ kind: 'surfaceCards', data });
    } catch {
      if (!controller.signal.aborted) setFailed(true);
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  const historyResult = wellPreview?.kind === 'history' ? wellPreview : null;
  const history = historyResult?.data;

  function clearHistoryResult() {
    if (wellPreview?.kind === 'history' || wellPreview?.kind === 'historyTimeError') setWellPreview(null);
  }
  const result = wellPreview?.kind === 'current' ? wellPreview.data : null;
  const productionDays = wellPreview?.kind === 'productionDays' ? wellPreview.data : null;

  return (
    <details>
      <summary>{t('guided.livePreview')}</summary>
      <div className="guided-authoring-review">
        <p>{t('guided.liveScope')}</p>
        <form
          className="guided-authoring-fields"
          onSubmit={(event) => {
            event.preventDefault();
            if (query.trim()) void search(0);
          }}
        >
          <TextField
            label={t('guided.findWell')}
            value={query}
            maxLength={120}
            disabled={busy}
            onChange={(event) => {
              setQuery(event.target.value);
              setMatches(null);
              setAssetId('');
              clearCompatibility();
              setWellPreview(null);
              setFailed(false);
            }}
          />
          <ActionButton type="submit" label={t('guided.searchWells')} disabled={busy || !query.trim()} />
        </form>
        {matches && (
          <>
            {matches.length ? (
              <SelectField
                label={t('guided.selectWell')}
                value={assetId}
                disabled={busy}
                onChange={(event) => {
                  setAssetId(event.target.value);
                  void checkWell(event.target.value);
                  setWellPreview(null);
                  setFailed(false);
                }}
              >
                <option value="">{t('guided.chooseWell')}</option>
                {matches.map((asset) => (
                  <option key={asset.id} value={asset.id}>
                    {[
                      ...asset.path.filter((entry) => entry.id !== asset.id).map((entry) => entry.name),
                      asset.name,
                    ].join(' / ')}
                  </option>
                ))}
              </SelectField>
            ) : (
              <p>{t('guided.noWells')}</p>
            )}
            {(offset > 0 || more) && (
              <div className="guided-authoring-actions">
                {offset > 0 && (
                  <ActionButton
                    label={t('guided.previousWells')}
                    disabled={busy}
                    onClick={() => void search(offset - SEARCH_PAGE_SIZE)}
                  />
                )}
                {more && (
                  <ActionButton
                    label={t('guided.nextWells')}
                    disabled={busy}
                    onClick={() => void search(offset + SEARCH_PAGE_SIZE)}
                  />
                )}
              </div>
            )}
          </>
        )}
        {compatibility?.kind === 'checking' && <Notice>{t('guided.assetCompatibilityChecking')}</Notice>}
        {compatibility?.kind === 'error' && <Notice tone="warning">{t('guided.assetCompatibilityFailed')}</Notice>}
        {compatibility?.kind === 'result' && (
          <Notice>
            <p>
              {t(
                compatibility.inputs.every((input) => input.status === 'Satisfied')
                  ? 'guided.assetCompatible'
                  : 'guided.assetCompatibilityIncomplete',
              )}
            </p>
            <ul>
              {compatibility.inputs.map((input) => {
                const label =
                  compatibilityLabels[input.status as keyof typeof compatibilityLabels] ??
                  'guided.assetInputUnavailable';
                return (
                  <li key={input.inputId}>
                    {input.quantity} · {t(label)}
                    {input.status === 'Rejected' && input.reasonCode && <> · {outcomeLabel(input.reasonCode).text}</>}
                  </li>
                );
              })}
            </ul>
          </Notice>
        )}
        {(compatibility?.kind === 'error' ||
          (compatibility?.kind === 'result' &&
            compatibility.inputs.some((input) => input.status === 'Unavailable'))) && (
          <ActionButton label={t('guided.recheckAsset')} disabled={busy} onClick={() => void checkWell(assetId)} />
        )}
        {assetId && (
          <GuidedQuantityDiscovery key={`${draftId}:${revision}:${assetId}`} {...{ draftId, revision, assetId }} />
        )}
        <div>
          <ActionButton label={t('guided.runLive')} disabled={busy || !assetId} onClick={() => void preview()} />
        </div>
        {assetId && inputs.length > 0 && (
          <div className="guided-authoring-fields">
            <SelectField
              label={t('guided.historyInput')}
              value={inputId}
              disabled={busy}
              onChange={(event) => {
                setInputId(event.target.value);
                clearHistoryResult();
              }}
            >
              {inputs.map((input) => (
                <option key={input.id} value={input.id}>
                  {input.quantity}
                </option>
              ))}
            </SelectField>
            <SelectField
              label={t('guided.historyAnchor')}
              value={historyAnchor}
              disabled={busy}
              onChange={(event) => {
                setHistoryAnchor(event.target.value);
                clearHistoryResult();
              }}
            >
              <option value="now">{t('guided.historyNow')}</option>
              <option value="site">{t('guided.historySiteTime')}</option>
            </SelectField>
            {historyAnchor === 'site' && (
              <>
                <TextField
                  type="datetime-local"
                  label={t('guided.historyEndLocal')}
                  value={endLocal}
                  disabled={busy}
                  onChange={(event) => {
                    setEndLocal(event.target.value);
                    clearHistoryResult();
                  }}
                />
                <TextField
                  label={t('guided.historySiteZone')}
                  helper={t('guided.historySiteZoneExample')}
                  value={timeZoneId}
                  maxLength={128}
                  disabled={busy}
                  onChange={(event) => {
                    setTimeZoneId(event.target.value);
                    clearHistoryResult();
                  }}
                />
              </>
            )}
            <SelectField
              label={t('guided.historyWindow')}
              value={hours}
              disabled={busy}
              onChange={(event) => {
                setHours(event.target.value);
                clearHistoryResult();
              }}
            >
              <option value="1">{t('guided.historyOneHour')}</option>
              <option value="4">{t('guided.historyFourHours')}</option>
              <option value="24">{t('guided.historyDay')}</option>
            </SelectField>
            <ActionButton
              label={t('guided.readHistory')}
              disabled={busy || !inputId || (historyAnchor === 'site' && (!endLocal || !timeZoneId.trim()))}
              onClick={() => void previewHistory()}
            />
          </div>
        )}
        {wellPreview?.kind === 'historyTimeError' && <Notice tone="error">{t(wellPreview.message)}</Notice>}
        {historyResult && history && (
          <div aria-live="polite">
            <p>
              {history.quantity} ·{' '}
              {historyResult.siteWindow ? (
                t('guided.historySiteResult', {
                  local: historyResult.siteWindow.endLocal.replace('T', ' '),
                  zone: historyResult.siteWindow.timeZoneId,
                })
              ) : (
                <>
                  {formatTimestamp(history.startUtc)} – {formatTimestamp(history.endUtc)}
                </>
              )}
            </p>
            {historyResult.siteWindow && history.startUtc && history.endUtc && (
              <p>
                {t('guided.historyUtcBounds')} <time dateTime={history.startUtc}>{history.startUtc}</time> –{' '}
                <time dateTime={history.endUtc}>{history.endUtc}</time>
              </p>
            )}
            {history.truncated && <Notice tone="warning">{t('guided.historyTruncated')}</Notice>}
            {history.samples?.length ? (
              <ul>
                {history.samples.map((sample, index) => (
                  <li key={index}>
                    {historyResult.siteWindow && sample.timestampUtc ? (
                      <time dateTime={sample.timestampUtc}>{sample.timestampUtc}</time>
                    ) : (
                      formatTimestamp(sample.timestampUtc)
                    )}{' '}
                    · {sample.value} {sample.unit} · {sample.quality}
                  </li>
                ))}
              </ul>
            ) : (
              <p>{t('guided.historyEmpty')}</p>
            )}
          </div>
        )}
        {assetId && (
          <div className="guided-authoring-actions">
            <ActionButton
              label={t('guided.readProductionDays')}
              disabled={busy}
              onClick={() => void previewProductionDays()}
            />
            <ActionButton
              label={t('guided.readSurfaceCards')}
              disabled={busy}
              onClick={() => void previewSurfaceCards()}
            />
          </div>
        )}
        {productionDays && (
          <div aria-live="polite">
            <p>{t('guided.productionDaysTitle')}</p>
            {productionDays.days?.length ? (
              <ul>
                {productionDays.days.map((day) => (
                  <li key={day.start}>
                    <time dateTime={day.start}>{day.start}</time> – <time dateTime={day.end}>{day.end}</time>
                    {' · '}
                    {day.value == null ? t('guided.unavailable') : day.value + ' ' + (productionDays.unit ?? '')}
                  </li>
                ))}
              </ul>
            ) : (
              <p>{t('guided.productionDaysEmpty')}</p>
            )}
            <p>
              {t('guided.previewRetrieved')} {formatTimestamp(productionDays.retrievedAtUtc)}
            </p>
          </div>
        )}
        {wellPreview?.kind === 'unsupported' && (
          <Notice tone="info">
            {wellPreview.preview === 'productionDays'
              ? t('guided.productionDaysUnsupported')
              : t('guided.surfaceCardsUnsupported')}
          </Notice>
        )}
        {wellPreview?.kind === 'surfaceCards' && <GuidedSurfaceCardsPreview preview={wellPreview.data} />}
        {failed && <Notice tone="error">{t('guided.liveError')}</Notice>}
        {result && (
          <Notice tone={result.failureCode ? 'warning' : 'info'}>
            <p>{outcomeLabel(result.outcomeCode ?? result.failureCode).text}</p>
            {result.differentialPsi != null && (
              <p>
                {t('guided.difference')} {result.differentialPsi} psi
              </p>
            )}
            <ul>
              {result.readings?.map((reading) => (
                <li key={reading.inputId}>
                  {reading.quantity} ·{' '}
                  {reading.value == null ? t('guided.unavailable') : reading.value + ' ' + reading.unit}
                  {' · '}
                  {reading.quality} {' · '}
                  {formatTimestamp(reading.timestampUtc)}
                </li>
              ))}
            </ul>
          </Notice>
        )}
      </div>
    </details>
  );
}
