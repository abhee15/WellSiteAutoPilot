import { createRoot } from 'react-dom/client';
import { client } from '../../src/api/generated/v1/client.gen';
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

createRoot(document.getElementById('root')!).render(
  <main>
    <h1>Logic</h1>
    <GuidedAuthoringPage
      canAuthor
      canReview
      canApprove
      initialReviewReference="8f52644a-daa0-4d1b-8949-cb20942b4dc8/1"
      onRefresh={async () => {}}
    />
  </main>,
);
