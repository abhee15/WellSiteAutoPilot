import './assetCreation.css';
import { useEffect, useRef, useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { TextAreaField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import type { FormEvent } from 'react';
import type { AssetTypeResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { checkAssetTypeDraft, jsonSyntaxProblem, problemCode } from './assetTypeDefinition';
import type { AssetTypeDraftProblem } from './assetTypeDefinition';
import { CodeBlock } from '../components/CodeBlock';

const PROBLEM_KEYS: Record<AssetTypeDraftProblem, MessageKey> = {
  key: 'assetTypes.keyInvalid',
  name: 'assetTypes.nameInvalid',
  schemaSyntax: 'assetTypes.schemaSyntax',
  schemaShape: 'assetTypes.schemaShape',
  schemaSize: 'assetTypes.schemaSize',
};

export function AssetTypeDefinitionPanel({ onCreated }: {
  onCreated: (created: AssetTypeResponse) => Promise<void> | void;
}) {
  const [key, setKey] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [schemaJson, setSchemaJson] = useState('{}');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const active = useRef(true);
  const pending = useRef(false);
  const request = useRef<AbortController | null>(null);

  useEffect(() => {
    active.current = true;
    return () => { active.current = false; request.current?.abort(); };
  }, []);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (pending.current) return;
    setNotice(null);
    const problem = checkAssetTypeDraft({ key, displayName, schemaJson });
    if (problem) {
      const where = problem === 'schemaSyntax' ? jsonSyntaxProblem(schemaJson) : null;
      setError(where
        ? t('assetTypes.schemaSyntaxAt', { line: where.line, column: where.column })
        : t(PROBLEM_KEYS[problem]));
      return;
    }
    setError(null);

    const trimmedKey = key.trim();
    const trimmedName = displayName.trim();
    pending.current = true;
    setIsSubmitting(true);
    const controller = new AbortController();
    request.current = controller;
    try {
      const response = await fetch('/api/v1/asset-types', {
        method: 'POST', credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ key: trimmedKey, displayName: trimmedName, attributeSchemaJson: schemaJson }),
      });
      if (!response.ok) {
        const code = await response.json().then(problemCode, () => null);
        if (!active.current) return;
        setError(response.status === 403 ? t('assetTypes.denied')
          : code === 'ASSET_TYPE_KEY_HIDDEN' ? t('assetTypes.keyHidden')
          : code === 'ASSET_TYPE_KEY_CONFLICT' ? t('assetTypes.keyConflict')
          : code === 'ASSET_ATTRIBUTE_SCHEMA_UNSUPPORTED' ? t('assetTypes.schemaUnsupported')
          : response.status === 400 ? t('assetTypes.rejected')
          : response.status === 409 ? t('assetTypes.keyConflict')
          : t('assetTypes.uncertain'));
        return;
      }
      const created: unknown = response.status === 201 ? await response.json() : null;
      const echoed = created as Partial<AssetTypeResponse> | null;
      if (!echoed || echoed.key !== trimmedKey || echoed.displayName !== trimmedName) {
        if (active.current) setError(t('assetTypes.uncertain'));
        return;
      }
      if (!active.current) return;
      setNotice(t('assetTypes.created', { name: trimmedName }));
      setKey(''); setDisplayName(''); setSchemaJson('{}');
      await onCreated(echoed as AssetTypeResponse);
    } catch {
      // The POST may already have committed; never retry or claim it failed.
      if (active.current) setError(t('assetTypes.uncertain'));
    } finally {
      if (request.current === controller) request.current = null;
      pending.current = false;
      if (active.current) setIsSubmitting(false);
    }
  };

  return (
    <details className="asset-create-panel asset-type-define">
      <summary>{t('assetTypes.defineTitle')}</summary>
      <form className="asset-create-form" onSubmit={event => void submit(event)}>
        <p className="asset-create-hint">{t('assetTypes.defineHelp')}</p>
        <TextField label={t('assetTypes.key')} value={key} maxLength={128} required disabled={isSubmitting}
          onChange={event => { setKey(event.target.value); setError(null); }} />
        <TextField label={t('assetTypes.displayName')} value={displayName} maxLength={256} required disabled={isSubmitting}
          onChange={event => { setDisplayName(event.target.value); setError(null); }} />
        <div className="asset-create-attributes">
          <TextAreaField label={t('assetTypes.schema')} value={schemaJson} rows={8} spellCheck={false} disabled={isSubmitting}
            onChange={event => { setSchemaJson(event.target.value); setError(null); }} />
        </div>
        <details className="asset-create-schema">
          <summary>{t('assetTypes.example')}</summary>
          <CodeBlock label={t('assetTypes.example')}>{EXAMPLE_SCHEMA}</CodeBlock>
          <ActionButton label={t('assetTypes.useExample')} disabled={isSubmitting} onClick={() => { setSchemaJson(EXAMPLE_SCHEMA); setError(null); }} />
        </details>
        <p className="asset-create-hint">{t('assetTypes.immutableNote')}</p>
        {error && <Notice tone="error">{error}</Notice>}
        {notice && <Notice tone="success">{notice}</Notice>}
        <ActionButton label={isSubmitting ? t('assetTypes.creating') : t('assetTypes.submit')} type="submit" disabled={isSubmitting || !key.trim() || !displayName.trim()} />
      </form>
    </details>
  );
}

const EXAMPLE_SCHEMA = `{
  "type": "object",
  "properties": {
    "apiNumber": { "type": "string", "title": "API number", "maxLength": 32 },
    "totalDepthFt": { "type": "number", "title": "Total depth (ft)", "minimum": 0 },
    "status": { "type": "string", "enum": ["producing", "shut-in"] }
  },
  "required": ["apiNumber"]
}`;
