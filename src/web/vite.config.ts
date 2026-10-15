import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import {
  buildDevelopmentProxy,
  requireConfiguredDevelopmentAuth
} from './dev-auth-proxy';

export default defineConfig(({ command, mode }) => {
  // Builds never read or embed local test identities. Only the Vite dev
  // server can proxy authenticated requests.
  const proxy =
    command === 'serve'
      ? buildDevelopmentProxy(loadEnv(mode, process.cwd(), 'WSA_DEV_'))
      : undefined;

  return {
    plugins: [
      react(),
      {
        name: 'wellsite-dev-auth-preflight',
        configureServer(server) {
          // The existing Node UI tests use Vite in middleware/SSR mode,
          // without an HTTP API proxy. Do not require a test persona there.
          if (!server.config.server.middlewareMode) {
            requireConfiguredDevelopmentAuth(proxy);
          }
        }
      }
    ],
    server: {
      host: '127.0.0.1',
      strictPort: true,
      port: 5173,
      // Absent explicit opt-in, no Negotiate requests reach a proxy that
      // cannot preserve connection-bound authentication.
      proxy
    }
  };
});
