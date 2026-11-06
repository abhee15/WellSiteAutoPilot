// A fixture-only build: no API proxy, authentication bypass, provider data or production entry change.
import { fileURLToPath } from 'node:url';
import react from '../../src/web/node_modules/@vitejs/plugin-react/dist/index.js';
export default {
  root: fileURLToPath(new URL('../../src/web/', import.meta.url)),
  plugins: [react()],
  build: { rollupOptions: { input: fileURLToPath(new URL('../../src/web/tests/visual/shared-forms.html', import.meta.url)) } },
};
