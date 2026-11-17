import { createRoot } from 'react-dom/client';
import { client } from '../../src/api/generated/v1/client.gen';
import { NavigationLink } from '../../src/components/NavigationLink';
import { useNavCollapsed } from '../../src/components/useNavCollapsed';
import { t } from '../../src/localization';
import { GuidedAuthoringPage } from '../../src/views/GuidedAuthoringPage';
import { GuidedSurfaceCardsPreview } from '../../src/views/GuidedSurfaceCardsPreview';
import '../../src/tokens.css';
import '../../src/styles.css';
import '@fontsource/roboto/400.css';
import '@fontsource/roboto/500.css';
import '@fontsource/roboto/700.css';

const view = new URLSearchParams(window.location.search).get('view');
const id = '8f52644a-daa0-4d1b-8949-cb20942b4dc8';
const manifestJson = JSON.stringify({
  displayName: 'Fillage check',
  dataRequirements: [{ id: 'fillage', quantity: 'PumpFillage', access: 'Current', canonicalUnit: '%' }],
});
const definition = { default: 70, title: 'Low pump fillage' };

client.setConfig({
  baseUrl: window.location.origin,
  fetch: async (request: Request) => {
    const url = new URL(request.url);
    if (url.pathname.endsWith('/submissions/1')) {
      return Response.json({
        draftId: id,
        revision: 1,
        packageSha256: 'a'.repeat(64),
        reviewedManifestJson: manifestJson,
        definition,
        cases: [{ value: 62, outcomeCode: 'LOW_FILLAGE' }],
      });
    }
    if (url.pathname.endsWith('/preview')) {
      const { value } = (await request.json()) as { value: number | null };
      return Response.json(
        value === null
          ? { failureCode: 'MODULE_DECLARATIVE_INPUT_UNAVAILABLE' }
          : { outcomeCode: value < 70 ? 'LOW_FILLAGE' : 'THRESHOLD_NOT_MET' },
      );
    }
    return Response.json([{ id, revision: 1, manifestJson, definition }]);
  },
});

if (view === 'ide') window.localStorage.setItem('wsa.authoring.view.visual-fixture', 'ide');

function AuthoringFixture() {
  const nav = useNavCollapsed();
  return (
    <div className={nav.collapsed ? 'app-shell nav-collapsed' : 'app-shell'}>
      <aside className="navigation">
        <nav aria-label="Primary">
          <NavigationLink section="assets" current="authoring">
            {t('nav.assets')}
          </NavigationLink>
          <NavigationLink section="logic" current="authoring">
            {t('nav.logic')}
          </NavigationLink>
          <NavigationLink section="authoring" current="authoring">
            {t('nav.authoring')}
          </NavigationLink>
        </nav>
      </aside>
      <main className="content">
        <h1>{t('nav.authoring')}</h1>
        {view === 'surface-cards' ? (
          <GuidedSurfaceCardsPreview
            preview={{
              quality: 'Unknown',
              retrievedAtUtc: '2026-11-03T12:03:00Z',
              cards: [
                {
                  id: 'current',
                  kind: 'CURRENT',
                  capturedAtUtc: '2026-11-03T12:02:00Z',
                  positionUnit: 'in',
                  loadUnit: 'lb',
                  position: [0, 5, 12, 10, 2, 0],
                  load: [2200, 3100, 3600, 2700, 1950, 2200],
                },
              ],
            }}
          />
        ) : (
          <GuidedAuthoringPage
            canAuthor={view !== 'review'}
            userId={view === 'ide' ? 'visual-fixture' : undefined}
            canReview={view === 'review'}
            canApprove
            initialReviewReference={view === 'review' ? id + '/1' : undefined}
            onRefresh={async () => {}}
          />
        )}
      </main>
    </div>
  );
}

createRoot(document.getElementById('root')!).render(<AuthoringFixture />);
