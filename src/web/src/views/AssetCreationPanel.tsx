import './assetCreation.css';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import type {
  AssetCreationCapabilitiesResponse,
  AssetResponse,
  AssetTypeResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { isSecurityId } from '../security/identifiers';
import { attributeFields, buildAttributes } from './assetAttributeForm';
import { AttributeFieldsEditor } from '../components/AttributeFieldsEditor';
import type { AttributeFieldError, AttributeValues } from './assetAttributeForm';

type Props = {
  userId: string | null | undefined;
  assetTypes: AssetTypeResponse[];
  assets: AssetResponse[];
  capabilities: AssetCreationCapabilitiesResponse | null;
  onCreated: (asset: AssetResponse) => Promise<void> | void;
  onRefresh: () => Promise<void> | void;
  /** Closes the form without creating anything. */
  onCancel?: () => void;
  /** Pre-fills the parent, for adding beneath the asset the person is looking at. */
  suggestedParentId?: string | null;
};

type VerifiedAsset = AssetResponse & {
  id: string;
  assetTypeId: string;
  name: string;
  parentAssetId: string | null;
  isActive: true;
};

function isVerifiedAsset(
  value: unknown, typeId: string, name: string,
  expectedParentId: string | null, expectedId?: string
): value is VerifiedAsset {
  if (value === null || typeof value !== 'object') return false;
  const asset = value as Partial<AssetResponse>;
  return typeof asset.id === 'string' &&
    isSecurityId(asset.id) &&
    asset.assetTypeId === typeId && asset.name === name &&
    asset.parentAssetId === expectedParentId &&
    (expectedId === undefined || asset.id === expectedId) &&
    asset.isActive === true;
}

export function AssetCreationPanel({
  userId, assetTypes, assets, capabilities, onCreated, onRefresh, suggestedParentId, onCancel,
}: Props) {
  const [typeId, setTypeId] = useState('');
  const [parentId, setParentId] = useState(suggestedParentId ?? '');
  const [name, setName] = useState('');
  const [attributes, setAttributes] = useState('{}');
  const [values, setValues] = useState<AttributeValues>({});
  const [fieldErrors, setFieldErrors] = useState<Record<string, AttributeFieldError>>({});
  const [jsonMode, setJsonMode] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const active = useRef(true);
  const pending = useRef(false);
  const request = useRef<AbortController | null>(null);

  useEffect(() => {
    active.current = true;
    return () => { active.current = false; request.current?.abort(); };
  }, []);

  const selectedType = assetTypes.find(type => type.id === typeId && type.isActive);
  const activeTypes = assetTypes.filter(type => type.isActive);
  const fields = useMemo(() => attributeFields(selectedType?.attributeSchemaJson), [selectedType]);
  const formMode = fields !== null && !jsonMode;
  const canCreateRoot = !!userId && capabilities?.canCreateRoot === true;
  const canCreateChildren =
    !!userId && capabilities?.canCreateUnderAuthorizedParents === true;
  const parents = canCreateChildren ? assets.filter(asset => asset.isActive) : [];
  const hasOptions = activeTypes.length > 0 && (canCreateRoot || parents.length > 0);
  const selectedParentValid = parentId === '__root__'
    ? canCreateRoot
    : parents.some(asset => asset.id === parentId);

  useEffect(() => {
    if (!hasOptions || parentId && !selectedParentValid) {
      request.current?.abort(); pending.current = false; setIsSubmitting(false);
      setParentId(''); setTypeId(''); setName(''); setAttributes('{}'); setValues({}); setFieldErrors({}); setJsonMode(false);
      setNotice(null); setError(null);
    }
  }, [hasOptions, parentId, selectedParentValid]);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const assetTypeId = selectedType?.id;
    if (pending.current || !hasOptions || !selectedParentValid || !selectedType || !isSecurityId(assetTypeId)) return;
    setNotice(null);
    setError(null);

    const trimmed = name.trim();
    if (!trimmed || trimmed.length > 256) {
      setError(t('assets.createNameInvalid'));
      return;
    }
    let attributesJson = attributes;
    if (formMode && fields) {
      const built = buildAttributes(fields, values);
      setFieldErrors(built.errors);
      if (Object.keys(built.errors).length > 0) { setError(t('assets.createFieldsInvalid')); return; }
      attributesJson = built.json;
    } else {
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
    }

    pending.current = true;
    setIsSubmitting(true);
    const controller = new AbortController();
    request.current = controller;
    let confirmed = false;
    try {
      const response = await fetch('/api/v1/assets', {
        method: 'POST', credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          assetTypeId,
          name: trimmed,
          parentAssetId: parentId === '__root__' ? null : parentId,
          attributeValuesJson: attributesJson,
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
      const expectedParentId = parentId === '__root__' ? null : parentId;
      const created: unknown = await response.json();
      if (!isVerifiedAsset(created, assetTypeId, trimmed, expectedParentId)) {
        if (active.current) setError(t('assets.createUncertain'));
        return;
      }
      confirmed = true;
      if (!active.current || controller.signal.aborted) return;
      // Read back from the authoritative, scoped API before selecting. Do not
      // insert an optimistic row into the cached first 100 results.
      const verifiedResponse = await fetch('/api/v1/assets/' + created.id, {
        credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
      });
      if (!verifiedResponse.ok) {
        if (active.current && verifiedResponse.status === 403) {
          // The write is committed, but explicit Asset scopes do not
          // automatically include newly created descendants (see #65).
          // Don't imply the operator can read/select the child.
          setNotice(t('assets.createPendingAccess', { name: trimmed }));
          setName('');
          setAttributes('{}'); setValues({}); setJsonMode(false);
          await onRefresh();
        } else if (active.current) {
          setError(t('assets.createReadbackFailed'));
        }
        return;
      }
      if (verifiedResponse.status !== 200) {
        if (active.current) setError(t('assets.createReadbackFailed'));
        return;
      }
      const verified: unknown = await verifiedResponse.json();
      if (!isVerifiedAsset(
        verified, assetTypeId, trimmed, expectedParentId, created.id
      )) {
        if (active.current) setError(t('assets.createReadbackFailed'));
        return;
      }
      if (!active.current) return;
      setNotice(t('assets.createSuccess', { name: trimmed }));
      setName('');
      setAttributes('{}'); setValues({}); setJsonMode(false);
      await onCreated(verified);
    } catch {
      // A POST with an interrupted response may already have committed.
      // Never automatically retry it or claim that it definitely failed.
      if (active.current) setError(t(confirmed ? 'assets.createReadbackFailed' : 'assets.createUncertain'));
    } finally {
      if (request.current === controller) request.current = null;
      pending.current = false;
      if (active.current) setIsSubmitting(false);
    }
  };

  // Someone who can never create an Asset here (no root permission, no authorized parent) is not
  // shown a creation form or an explanation of one; the server still decides every write.
  if (userId && capabilities && !canCreateRoot && !canCreateChildren) return null;

  return (
    <section className="panel asset-create-panel" aria-labelledby="asset-create-title">
      <header className="asset-create-heading">
        <h2 id="asset-create-title">{t('assets.createTitle')}</h2>
        <p>{t('assets.createHelp')}</p>
        {onCancel && <button type="button" className="secondary-button" onClick={onCancel}>
          {t('assets.cancelAdd')}</button>}
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
              setValues({}); setFieldErrors({}); setJsonMode(false); setAttributes('{}');
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
          {formMode && fields ? (
            <div className="asset-create-fields">
              <AttributeFieldsEditor fields={fields} values={values} errors={fieldErrors}
                idPrefix="asset-attr-" legend={t('assets.createDetails')} disabled={isSubmitting}
                onChange={(key, value) => {
                  setValues(current => ({ ...current, [key]: value }));
                  setFieldErrors(current => Object.fromEntries(
                    Object.entries(current).filter(([k]) => k !== key)));
                  setError(null);
                }}
                onUseJson={() => {
                  const built = buildAttributes(fields, values);
                  setAttributes(built.json); setJsonMode(true); setFieldErrors({});
                }} />
            </div>
          ) : (
          <label className="asset-create-attributes">
            <span>{t('assets.createAttributes')}</span>
            <textarea value={attributes} rows={5} maxLength={65536}
              spellCheck={false} disabled={isSubmitting}
              onChange={event => {
                setAttributes(event.target.value); setError(null);
              }} />
          </label>
          )}
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
          {onCancel && <button type="button" className="secondary-button" disabled={isSubmitting}
            onClick={onCancel}>{t('assets.cancelAdd')}</button>}
        </form>
      )}
    </section>
  );
}
