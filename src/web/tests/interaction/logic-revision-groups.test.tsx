import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { ConfiguredLogicWorkspace } from '../../src/views/ConfiguredLogicWorkspace';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

const revision = (id: string, revisionNumber: number, status: string) => ({ id, revisionNumber, status, moduleId: 'sample',
  moduleVersion: '1.0.0', mode: 'Shadow', parametersJson: '{}', assetBindings: [], dataBindings: [] });
const logic = { id: '11111111-1111-4111-8111-111111111111', name: 'Observer', canManage: false, canRequestShadow: false,
  activeRevisionId: '00000003-0000-4000-8000-000000000000', revisions: [revision('00000001-0000-4000-8000-000000000000', 1, 'Superseded'), revision('00000002-0000-4000-8000-000000000000', 2, 'Superseded'),
    revision('00000003-0000-4000-8000-000000000000', 3, 'Active'), revision('00000004-0000-4000-8000-000000000000', 4, 'Draft')] };

it('selects one saved revision at a time while retaining active, pending and historical revisions', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(logic))));
  render(<ConfiguredLogicWorkspace selectedId={logic.id} canManage={false}
    modules={[]} assets={[]} assetTypes={[]} onClose={() => undefined} onChanged={async () => undefined} />);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('tab', { name: 'Configuration', exact: true }));
  const picker = screen.getByRole('combobox', { name: 'Saved revision' });
  expect((picker as HTMLSelectElement).value).toBe(logic.activeRevisionId);
  expect(screen.getByRole('group', { name: 'Earlier revisions (2)' })).toBeTruthy();
  await user.selectOptions(picker, logic.revisions[3]!.id);
  expect(screen.getByRole('heading', { name: 'Revision 4 · Draft' })).toBeTruthy();
  expect(screen.queryByRole('heading', { name: 'Revision 3 · Active' })).toBeNull();
  await user.selectOptions(picker, logic.revisions[0]!.id);
  expect(screen.getByRole('heading', { name: 'Revision 1 · Superseded' })).toBeTruthy();
});
