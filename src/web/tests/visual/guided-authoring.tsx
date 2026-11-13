import { createRoot } from 'react-dom/client';
import { client } from '../../src/api/generated/v1/client.gen';
import { NavigationLink } from '../../src/components/NavigationLink';
import { useNavCollapsed } from '../../src/components/useNavCollapsed';
import { t } from '../../src/localization';
import { GuidedAuthoringPage } from '../../src/views/GuidedAuthoringPage';
import '../../src/tokens.css';
import '../../src/styles.css';
import '@fontsource/roboto/400.css';
import '@fontsource/roboto/500.css';
import '@fontsource/roboto/700.css';

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
        : [],
    ),
});

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
        <GuidedAuthoringPage
          canAuthor
          canReview
          canApprove
          initialReviewReference="8f52644a-daa0-4d1b-8949-cb20942b4dc8/1"
          onRefresh={async () => {}}
        />
      </main>
    </div>
  );
}

createRoot(document.getElementById('root')!).render(<AuthoringFixture />);
