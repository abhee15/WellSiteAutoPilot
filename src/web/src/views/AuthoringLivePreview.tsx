import { useState } from 'react';
import { previewAuthoringLiveValue, searchAssets } from '../api/generated/v1/sdk.gen';
import type { AssetSearchResultResponse, AuthoringPreviewResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { Status } from '../components/Status';
import { t } from '../localization';
import type { DraftDocument } from './authoring';

const SEARCH_LIMIT = 10;

function previewFailure(status: number | undefined, code: string | null): string {
  if (status === 403) return t('authoring.preview.denied');
  if (code === 'AUTHORING_PREVIEW_QUANTITY_UNSUPPORTED') return t('authoring.preview.quantityUnsupported');
  if (code === 'AUTHORING_PREVIEW_PROVIDER_UNSUPPORTED' || code === 'AUTHORING_PREVIEW_SOURCE_INVALID'
    || code === 'AUTHORING_PREVIEW_CONNECTION_DISABLED') return t('authoring.preview.sourceInvalid');
  return t('authoring.preview.failed');
}

/**
 * Reads one current value through the same authorised read path as Shadow runs, so an author can see live
 * data while editing. It creates no execution and never writes to the provider.
 */
export function LivePreview({ doc }: { doc: DraftDocument }) {
  const [query, setQuery] = useState('');
  const [assets, setAssets] = useState<AssetSearchResultResponse[]>([]);
  const [assetId, setAssetId] = useState('');
  const quantities = [...new Set(doc.inputs.map(input => input.quantity).filter(Boolean))];
  const [quantity, setQuantity] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [value, setValue] = useState<AuthoringPreviewResponse | null>(null);
  const chosenQuantity = quantity || quantities[0] || '';

  async function search() {
    setError(null);
    try {
      const result = await searchAssets({ query: { q: query, limit: SEARCH_LIMIT }, credentials: 'same-origin' });
      const found = result.data ?? [];
      setAssets(found);
      setAssetId(found[0]?.id ?? '');
    } catch { setError(t('authoring.preview.failed')); }
  }

  async function read() {
    setBusy(true); setError(null); setValue(null);
    try {
      const result = await previewAuthoringLiveValue({ body: { assetId, quantity: chosenQuantity }, credentials: 'same-origin' });
      if (result.data) setValue(result.data);
      else {
        const code = result.error && typeof result.error === 'object' && 'code' in result.error && typeof result.error.code === 'string'
          ? result.error.code : null;
        setError(previewFailure(result.response?.status, code));
      }
    } catch { setError(t('authoring.preview.failed')); }
    finally { setBusy(false); }
  }

  return <div className="authoring-preview" aria-label={t('authoring.preview.heading')}>
    <h3>{t('authoring.preview.heading')}</h3>
    <p className="authoring-note">{t('authoring.preview.note')}</p>
    <div className="authoring-inline">
      <label className="authoring-field"><span>{t('authoring.preview.search')}</span>
        <input value={query} onChange={event => setQuery(event.target.value)} />
      </label>
      <ActionButton label={t('authoring.preview.find')} onClick={() => void search()} />
    </div>
    {assets.length > 0 && <div className="authoring-inline">
      <label className="authoring-field"><span>{t('authoring.preview.asset')}</span>
        <select value={assetId} onChange={event => setAssetId(event.target.value)}>
          {assets.map(asset => <option key={asset.id} value={asset.id ?? ''}>{asset.name}</option>)}
        </select>
      </label>
      <label className="authoring-field"><span>{t('authoring.preview.quantity')}</span>
        <input list="authoring-preview-quantities" value={chosenQuantity} onChange={event => setQuantity(event.target.value)} />
        <datalist id="authoring-preview-quantities">{quantities.map(item => <option key={item} value={item} />)}</datalist>
      </label>
      <ActionButton label={t('authoring.preview.read')} disabled={busy || !assetId || !chosenQuantity} onClick={() => void read()} />
    </div>}
    {error && <p role="alert">{error}</p>}
    {value && <p role="status">
      <strong>{value.value ?? t('authoring.preview.noValue')} {value.unit}</strong>{' '}
      <Status tone={value.quality === 'Good' ? 'success' : 'warning'} label={value.quality ?? ''} />{' '}
      {t('authoring.preview.observed', { time: value.timestampUtc ?? '', adapter: value.adapterKey ?? '' })}
    </p>}
  </div>;
}
