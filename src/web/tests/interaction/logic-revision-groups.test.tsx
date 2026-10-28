import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { ConfiguredLogicWorkspace } from '../../src/views/ConfiguredLogicWorkspace';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

const revision = (id: string, revisionNumber: number, status: string) => ({ id, revisionNumber, status, moduleId: 'sample',
  moduleVersion: '1.0.0', mode: 'Shadow', parametersJson: '{}', assetBindings: [], dataBindings: [] });
const logic = { id: '11111111-1111-4111-8111-111111111111', name: 'Observer', canManage: false, canRequestShadow: false,
  activeRevisionId: '00000003-0000-4000-8000-000000000000', revisions: [revision('00000001-0000-4000-8000-000000000000', 1, 'Superseded'), revision('00000002-0000-4000-8000-000000000000', 2, 'Superseded'),
    revision('00000003-0000-4000-8000-000000000000', 3, 'Active'), revision('00000004-0000-4000-8000-000000000000', 4, 'Draft')] };

it('shows the live revision and waiting drafts first and tucks older revisions into a collapsed group', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(logic))));
  const { container } = render(<ConfiguredLogicWorkspace selectedId={logic.id} canManage={false}
    modules={[]} assets={[]} assetTypes={[]} onClose={() => undefined} onChanged={async () => undefined} />);
  await screen.findByText('Revision 3 · Active');
  const history = container.querySelector('details.revision-history') as HTMLDetailsElement;
  expect(history.open).toBe(false);
  expect(history.textContent).toContain('Earlier revisions (2)');
  expect(history.textContent).toContain('Revision 1 · Superseded');
  // Live first, then the draft waiting for action; each revision appears once.
  const visible = [...container.querySelectorAll('.setup-revisions > li > strong')].map(node => node.textContent);
  expect(visible.slice(0, 2)).toEqual(['Revision 3 · Active', 'Revision 4 · Draft']);
  expect(screen.getAllByText('Revision 3 · Active')).toHaveLength(1);
});
