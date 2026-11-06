import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { CheckboxField, ConfirmPanel, FormActions, NumberField, RadioGroup, SelectField, TextAreaField, TextField } from '../../src/components/FormFields';
import { Notice } from '../../src/components/Notice';
import { ActionButton } from '../../src/components/ActionButton';
import '../../src/tokens.css';
import '../../src/styles.css';
import '../../src/views/decisionBrief.css';
import '@fontsource/roboto/400.css';
import '@fontsource/roboto/500.css';
import '@fontsource/roboto/700.css';
function Review() {
  const [value, setValue] = useState<string | null>(null);
  return <div className="app-shell nav-collapsed"><aside className="navigation" aria-hidden="true" /><main className="content"><h1>Shared form review</h1><div className="page-body"><div className="execution-detail"><div className="decision-form">
    <TextField label="Start date" type="date" required defaultValue="2026-11-06" helper="Choose the date in the site time zone." />
    <TextField label="Start time" type="time" defaultValue="08:30" />
    <NumberField label="Repeat every" min={1} max={100} defaultValue={2} />
    <SelectField label="Frequency unit" defaultValue="hours"><option value="hours">Hours</option><option value="weeks">Weeks</option></SelectField>
    <TextField label="Source" readOnly value="ForeSite" helper="Derived from the selected wells." />
    <TextField label="Unavailable setting" disabled value="Unavailable" />
    <TextAreaField label="Audit reason" helper="Enter a reason." error="Enter at least 2 characters." defaultValue="x" rows={3} />
    <RadioGroup label="Decision" required value={value} onChange={setValue} options={[{ value: 'approve', label: 'Approve' }, { value: 'reject', label: 'Reject' }]} />
    <ConfirmPanel title="Confirm decision"><CheckboxField label="I reviewed the proposal and expiry." /><FormActions><ActionButton label="Submit decision" variant="primary" /></FormActions></ConfirmPanel>
    <Notice announce={false}>Static guidance stays in context.</Notice><Notice tone="error">The request failed. Your edits are kept.</Notice>
    <Notice tone="success">Saved.</Notice><Notice tone="warning" announce={false}>This change requires confirmation.</Notice>
  </div></div></div></main></div>;
}
createRoot(document.getElementById('root')!).render(<Review />);
