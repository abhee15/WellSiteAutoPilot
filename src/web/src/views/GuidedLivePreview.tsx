import { useEffect, useRef, useState } from 'react';
import { previewGuidedModuleInputHistory, previewGuidedModuleWithLiveInputs } from '../api/generated/v1/sdk.gen';
import type { GuidedHistoryPreviewResponse, GuidedLivePreviewResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { formatTimestamp } from '../format';
import { t } from '../localization';
import { outcomeLabel } from '../words';
import { fetchSearch, SEARCH_PAGE_SIZE, type SearchHit } from './assetBrowseApi';

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
  const [history, setHistory] = useState<GuidedHistoryPreviewResponse | null>(null);
  const [query, setQuery] = useState('');
  const [matches, setMatches] = useState<SearchHit[] | null>(null);
  const [more, setMore] = useState(false);
  const [offset, setOffset] = useState(0);
  const [assetId, setAssetId] = useState('');
  const [result, setResult] = useState<GuidedLivePreviewResponse | null>(null);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const operation = useRef<AbortController | null>(null);
  useEffect(() => () => operation.current?.abort(), []);

  async function search(pageOffset: number) {
    const controller = new AbortController();
    operation.current = controller;
    setBusy(true);
    setFailed(false);
    setMatches(null);
    setAssetId('');
    setResult(null);
    setHistory(null);
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
    setResult(null);
    setHistory(null);
    try {
      const { data } = await previewGuidedModuleWithLiveInputs({
        path: { id: draftId },
        body: { expectedRevision: revision, assetId },
        signal: controller.signal,
      });
      if (controller.signal.aborted) return;
      if (!data) throw new Error();
      setResult(data);
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
    setHistory(null);
    setResult(null);
    const end = Math.floor(Date.now() / 1000) * 1000;
    try {
      const { data } = await previewGuidedModuleInputHistory({
        path: { id: draftId },
        body: {
          expectedRevision: revision,
          assetId,
          inputId,
          maximumSamples: 100,
          startUtc: new Date(end - Number(hours) * 3600000).toISOString(),
          endUtc: new Date(end).toISOString(),
        },
        signal: controller.signal,
      });
      if (controller.signal.aborted) return;
      if (!data) throw new Error();
      setHistory(data);
    } catch {
      if (!controller.signal.aborted) setFailed(true);
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  }

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
              setResult(null);
              setHistory(null);
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
                  setResult(null);
                  setHistory(null);
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
                setHistory(null);
              }}
            >
              {inputs.map((input) => (
                <option key={input.id} value={input.id}>
                  {input.quantity}
                </option>
              ))}
            </SelectField>
            <SelectField
              label={t('guided.historyWindow')}
              value={hours}
              disabled={busy}
              onChange={(event) => {
                setHours(event.target.value);
                setHistory(null);
              }}
            >
              <option value="1">{t('guided.historyOneHour')}</option>
              <option value="4">{t('guided.historyFourHours')}</option>
              <option value="24">{t('guided.historyDay')}</option>
            </SelectField>
            <ActionButton
              label={t('guided.readHistory')}
              disabled={busy || !inputId}
              onClick={() => void previewHistory()}
            />
          </div>
        )}
        {history && (
          <div>
            <p>
              {history.quantity} · {formatTimestamp(history.startUtc)} – {formatTimestamp(history.endUtc)}
            </p>
            {history.truncated && <Notice tone="warning">{t('guided.historyTruncated')}</Notice>}
            {history.samples?.length ? (
              <ul>
                {history.samples.map((sample, index) => (
                  <li key={index}>
                    {formatTimestamp(sample.timestampUtc)} · {sample.value} {sample.unit} · {sample.quality}
                  </li>
                ))}
              </ul>
            ) : (
              <p>{t('guided.historyEmpty')}</p>
            )}
          </div>
        )}
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
