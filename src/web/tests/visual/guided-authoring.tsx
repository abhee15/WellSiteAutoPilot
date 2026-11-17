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

const calibrationFixture = {
  id: 'visual-card-calibration',
  revision: 1,
  manifestJson: JSON.stringify({
    displayName: 'Surface-card calibration · sample',
    moduleId: 'sample.card-calibration',
    version: '2.0.0',
    runtime: 'DotNet',
    entryPoint: 'WellSiteAutoPilot.Samples.StructuredCards.CardCalibrationModule',
    dataRequirements: [
      {
        id: 'cards',
        quantity: 'SurfaceCards',
        assetRole: 'well',
        access: 'Current',
        maximumAgeSeconds: 60,
        requiredCapability: 'structured.current',
        valueSchema: {
          schemaId: 'foresite.surface-cards/v1',
          schema: {
            type: 'array',
            maxItems: 16,
            items: {
              type: 'object',
              properties: {
                id: { type: 'string' },
                capturedAtUtc: { type: 'string' },
                position: { type: 'array', items: { type: 'number' } },
                load: { type: 'array', items: { type: 'number' } },
              },
            },
          },
        },
      },
      {
        id: 'calibration',
        quantity: 'Calibration',
        assetRole: 'well',
        valueSchema: {
          schemaId: 'sample.calibration/v1',
          schema: {
            type: 'object',
            properties: { loadScale: { type: 'number' }, loadOffset: { type: 'number' } },
          },
        },
      },
    ],
    outputRequirements: [
      {
        id: 'calibrated-cards',
        quantity: 'CalibratedCards',
        valueSchema: {
          schemaId: 'sample.calibrated-cards/v2',
          schema: { type: 'array', items: { type: 'object' } },
        },
      },
      {
        id: 'peak-load',
        quantity: 'PeakLoad',
        unit: 'lb',
        valueSchema: { schemaId: 'sample.peak-load/v2', schema: { type: 'number' } },
      },
    ],
  }),
  definition: { default: 70 },
};

client.setConfig({
  baseUrl: window.location.origin,
  fetch: async (request: Request) =>
    Response.json(
      request.url.endsWith('/submissions/1')
        ? {
            draftId: '8f52644a-daa0-4d1b-8949-cb20942b4dc8',
            revision: 1,
            packageSha256: 'a'.repeat(64),
            reviewedManifestJson: '{"displayName":"Fillage check"}',
            definition: { default: 70, title: 'Low pump fillage' },
            cases: [{ value: 62, outcomeCode: 'LOW_FILLAGE' }],
          }
        : view === 'contract'
          ? [calibrationFixture]
          : [],
    ),
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
            canAuthor
            userId={view === 'ide' ? 'visual-fixture' : undefined}
            canReview={view !== 'contract'}
            canApprove={view !== 'contract'}
            initialReviewReference="8f52644a-daa0-4d1b-8949-cb20942b4dc8/1"
            onRefresh={async () => {}}
          />
        )}
      </main>
    </div>
  );
}

createRoot(document.getElementById('root')!).render(<AuthoringFixture />);
