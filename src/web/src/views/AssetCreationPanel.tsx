import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import type {
  AssetCreationCapabilitiesResponse,
  AssetResponse,
  AssetTypeResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';

type Props = {
  userId: string | null | undefined;
  assetTypes: AssetTypeResponse[];
  assets: AssetResponse[];
  capabilities: AssetCreationCapabilitiesResponse | null;
  onCreated: (asset: AssetResponse) => Promise<void> | void;
};

function isVerifiedAsset(value: unknown, typeId: string, name: string):
  value is AssetResponse {
  if (value === null || typeof value !== 'object') return false;
  const asset = value as Partial<AssetResponse>;
  return typeof asset.id === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(asset.id) &&
    asset.assetTypeId === typeId && asset.name === name && asset.isActive === true;
}

export function AssetCreationPanel({
  userId, assetTypes, assets, capabilities, onCreated,
}: Props) {
  const [typeId, setTypeId] = useState('');
  const [parentId, setParentId] = useState('');
  const [name, setName] = useState('');
  const [attributes, setAttributes] = useState('{}');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const active = useRef(true);
  const pending = useRef(false);

  useEffect(() => {
    active.current = true;
    return () => { active.current = false; };
  }, []);

  const selectedType = assetTypes.find(type => type.id === typeId && type.isActive);
  const activeTypes = assetTypes.filter(type => type.isActive);
  const canCreateRoot = !!userId && capabilities?.canCreateRoot === true;
  const canCreateChildren =
    !!userId && capabilities?.canCreateUnderAuthorizedParents === true;
  const parents = canCreateChildren ? assets.filter(asset => asset.isActive) : [];
  const hasOptions = activeTypes.length > 0 && (canCreateRoot || parents.length > 0);
  const selectedParentValid = parentId === '__root__'
    ? canCreateRoot
    : parents.some(asset => asset.id === parentId);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (pending.current || !hasOptions || !selectedParentValid || !selectedType) return;
    setNotice(null);
    setError(null);

    const trimmed = name.trim();
    if (!trimmed || trimmed.length > 256) {
      setError(t('assets.createNameInvalid'));
      return;
    }
    let parsed: unknown;
    try {
      parsed = JSON.parse(attributes);
    } catch {
      setError(t('assets.createAttributesInvalid'));
      return;
    }
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
      setError(t('assets.createAttributesInvalid'));
      return;
    }

    pending.current = true;
    setIsSubmitting(true);
    try {
      const response = await fetch('/api/v1/assets', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          assetTypeId: selectedType.id,
          name: trimmed,
          parentAssetId: parentId === '__root__' ? null : parentId,
          attributeValuesJson: JSON.stringify(parsed),
        }),
      });

      if (!response.ok) {
        if (active.current) {
          setError(response.status === 403 ? t('assets.createDenied')
            : response.status === 409 ? t('assets.createConflict')
            : response.status === 400 ? t('assets.createRejected')
            : response.status === 404 ? t('assets.createMissing')
            : t('assets.createUncertain'));
        }
        return;
      }
      if (response.status !== 201) {
        if (active.current) setError(t('assets.createUncertain'));
        return;
      }
      const created: unknown = await response.json();
      if (!isVerifiedAsset(created, selectedType.id, trimmed)) {
        if (active.current) setError(t('assets.createUncertain'));
        return;
      }
      // Read back from the authoritative, scoped API before selecting. Do not
      // insert an optimistic row into the cached first 100 results.
      const verifiedResponse = await fetch('/api/v1/assets/' + created.id);
      if (!verifiedResponse.ok) {
        if (active.current) setError(t('assets.createReadbackFailed'));
        return;
      }
      const verified: unknown = await verifiedResponse.json();
      if (!isVerifiedAsset(verified, selectedType.id, trimmed)) {
        if (active.current) setError(t('assets.createReadbackFailed'));
        return;
      }
      if (!active.current) return;
      setNotice(t('assets.createSuccess', { name: trimmed }));
      setName('');
      setAttributes('{}');
      await onCreated(verified);
    } catch {
      // A POST with an interrupted response may already have committed.
      // Never automatically retry it or claim that it definitely failed.
      if (active.current) setError(t('assets.createUncertain'));
    } finally {
      pending.current = false;
      if (active.current) setIsSubmitting(false);
    }
  };

  return (
    <section className="panel asset-create-panel" aria-labelledby="asset-create-title">
      <header className="asset-create-heading">
        <h2 id="asset-create-title">{t('assets.createTitle')}</h2>
        <p>{t('assets.createHelp')}</p>
      </header>
      {!userId || !capabilities ? (
        <p className="asset-create-hint">{t('assets.createUnavailable')}</p>
      ) : !activeTypes.length ? (
        <p className="asset-create-hint">{t('assets.createNoTypes')}</p>
      ) : !hasOptions ? (
        <p className="asset-create-hint">{t('assets.createNoScope')}</p>
      ) : (
        <form className="asset-create-form" onSubmit={event => void submit(event)}>
          <label>
            <span>{t('assets.createType')}</span>
            <select value={typeId} onChange={event => {
              setTypeId(event.target.value); setError(null);
            }} required disabled={isSubmitting}>
              <option value="">{t('assets.createChooseType')}</option>
              {activeTypes.map(type => (
                <option key={type.id} value={type.id}>{type.displayName}</option>
              ))}
            </select>
          </label>
          <label>
            <span>{t('assets.createParent')}</span>
            <select value={parentId} onChange={event => {
              setParentId(event.target.value); setError(null);
            }} required disabled={isSubmitting}>
              <option value="">{t('assets.createChooseParent')}</option>
              {canCreateRoot && (
                <option value="__root__">{t('assets.createRoot')}</option>
              )}
              {parents.map(asset => (
                <option value={asset.id} key={asset.id}>{asset.name}</option>
              ))}
            </select>
          </label>
          <label>
            <span>{t('assets.createName')}</span>
            <input type="text" value={name} maxLength={256} required
              disabled={isSubmitting}
              onChange={event => { setName(event.target.value); setError(null); }} />
          </label>
          <label className="asset-create-attributes">
            <span>{t('assets.createAttributes')}</span>
            <textarea value={attributes} rows={5} maxLength={65536}
              spellCheck={false} disabled={isSubmitting}
              onChange={event => {
                setAttributes(event.target.value); setError(null);
              }} />
          </label>
          {selectedType && (
            <details className="asset-create-schema">
              <summary>{t('assets.createSchemaInfo')}</summary>
              <pre>{selectedType.attributeSchemaJson}</pre>
            </details>
          )}
          <p className="asset-create-hint">{t('assets.createValidationNote')}</p>
          {error && <p className="asset-create-error" role="alert">{error}</p>}
          {notice && <p className="asset-create-success" role="status">{notice}</p>}
          <button type="submit" className="secondary-button" disabled={
            isSubmitting || !selectedType || !selectedParentValid || !name.trim()
          }>{isSubmitting ? t('assets.creating') : t('assets.createSubmit')}</button>
        </form>
      )}
    </section>
  );
}
