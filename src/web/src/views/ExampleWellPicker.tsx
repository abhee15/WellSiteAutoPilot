import { useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { t } from '../localization';
import { fetchSearch, type SearchHit } from './assetBrowseApi';

/** Finds one active, imported well to read real values from while authoring. Nothing is run against it. */
export function ExampleWellPicker({ value, onChange }: { value: string; onChange: (assetId: string) => void }) {
  const [query, setQuery] = useState('');
  const [matches, setMatches] = useState<SearchHit[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  async function search() {
    setBusy(true);
    setFailed(false);
    onChange('');
    try {
      const page = await fetchSearch(query.trim(), 0, new AbortController().signal);
      setMatches(page.filter((asset) => asset.isActive && asset.ownerConnectionId));
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="authoring-flow-picker">
      <form
        className="authoring-flow-search"
        onSubmit={(event) => {
          event.preventDefault();
          if (query.trim()) void search();
        }}
      >
        <TextField
          label={t('flow.findWell')}
          value={query}
          maxLength={120}
          onChange={(event) => {
            setQuery(event.target.value);
            setMatches(null);
            onChange('');
          }}
        />
        <ActionButton type="submit" label={t('flow.search')} busy={busy} disabled={!query.trim()} />
      </form>
      {failed && <Notice tone="error">{t('flow.wellSearchFailed')}</Notice>}
      {matches &&
        (matches.length ? (
          <SelectField label={t('flow.well')} value={value} onChange={(event) => onChange(event.target.value)}>
            <option value="">{t('guided.chooseWell')}</option>
            {matches.map((asset) => (
              <option key={asset.id} value={asset.id}>
                {[...asset.path.filter((entry) => entry.id !== asset.id).map((entry) => entry.name), asset.name].join(
                  ' / ',
                )}
              </option>
            ))}
          </SelectField>
        ) : (
          <Notice tone="info" announce={false}>
            {t('guided.noWells')}
          </Notice>
        ))}
    </div>
  );
}
