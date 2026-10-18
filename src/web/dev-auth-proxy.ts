import type { ProxyOptions } from 'vite';

export const DEV_AUTH_SETUP_MESSAGE =
  'WellSite AutoPilot: Vite cannot reliably proxy connection-bound Windows Negotiate. ' +
  'For local frontend development set WSA_DEV_AUTH_MODE=TestHeader and ' +
  'WSA_DEV_TEST_IDENTITY in src/web/.env.local, and run the backend in ' +
  'Development with Security__AuthenticationMode=TestHeader. ' +
  'To verify real Windows Negotiate, use the server-hosted UI directly (no Vite proxy).';

const identityHeader = 'X-WSA-Test-Identity';
const displayNameHeader = 'X-WSA-Test-Display-Name';

export type DevProxyEnvironment = Record<string, string | undefined>;

/**
 * Browser requests never get to choose their authenticated development user.
 * Only the local Vite process creates the identity header, and only for /api and /hubs.
 * No proxy is configured implicitly when the mode has not been selected.
 */
export function buildDevelopmentProxy(
  environment: DevProxyEnvironment
): Record<string, ProxyOptions> | undefined {
  const mode = environment.WSA_DEV_AUTH_MODE?.trim();
  if (!mode) {
    return undefined;
  }
  if (mode.toLowerCase() !== 'testheader') {
    throw new Error(DEV_AUTH_SETUP_MESSAGE);
  }

  const identity = environment.WSA_DEV_TEST_IDENTITY?.trim() ?? '';
  // Allow local DOMAIN\account or user@domain, but prohibit header splitting,
  // multiple identities and ambiguous control characters.
  if (
    identity.length < 1 ||
    identity.length > 160 ||
    !/^[a-zA-Z0-9_.@\\-]+$/.test(identity)
  ) {
    throw new Error(
      'WSA_DEV_TEST_IDENTITY must be one local development account ' +
        '(for example LOCAL\\wsa-operator), without spaces or control characters.'
    );
  }

  const rawTarget = environment.WSA_DEV_API_TARGET?.trim() ||
    'http://127.0.0.1:5080';
  let target: URL;
  try {
    target = new URL(rawTarget);
  } catch {
    throw new Error('WSA_DEV_API_TARGET must be a valid loopback HTTP URL.');
  }
  if (
    target.protocol !== 'http:' ||
    target.hostname !== '127.0.0.1' ||
    target.username !== '' ||
    target.password !== '' ||
    target.pathname !== '/' ||
    target.search !== '' ||
    target.hash !== ''
  ) {
    throw new Error(
      'WSA_DEV_API_TARGET must be a local loopback HTTP origin, ' +
        'such as http://127.0.0.1:5080. Remote targets are forbidden.'
    );
  }

  function configureProxy(
    proxy: Parameters<NonNullable<ProxyOptions['configure']>>[0],
    includeIdentity: boolean
  ): void {
    const scrub = (proxyRequest: {
      removeHeader(name: string): void;
      setHeader(name: string, value: string): void;
    }) => {
      // Remove any values supplied by the browser. In particular, do not
      // forward an arbitrary display-name assertion alongside our identity.
      proxyRequest.removeHeader(identityHeader);
      proxyRequest.removeHeader(displayNameHeader);
      // No browser-integrated credentials or arbitrary bearer tokens should
      // be forwarded to a development-only synthetic-identity endpoint.
      proxyRequest.removeHeader('Authorization');
      proxyRequest.removeHeader('Proxy-Authorization');
      if (includeIdentity) {
        proxyRequest.setHeader(identityHeader, identity);
      }
    };
    proxy.on('proxyReq', scrub);
    // WebSocket upgrades (the live-update hub) take this path instead.
    proxy.on('proxyReqWs', scrub);
  }

  return {
    '/api': {
      target: target.origin,
      changeOrigin: true,
      configure: (proxy) => configureProxy(proxy, true)
    },
    // Live-update hub (ADR-008): the same synthetic identity, plus WebSocket upgrades.
    '/hubs': {
      target: target.origin,
      changeOrigin: true,
      ws: true,
      configure: (proxy) => configureProxy(proxy, true)
    },
    '/health': {
      target: target.origin,
      changeOrigin: true,
      configure: (proxy) => configureProxy(proxy, false)
    }
  };
}

export function requireConfiguredDevelopmentAuth(
  proxy: Record<string, ProxyOptions> | undefined
): void {
  if (!proxy) {
    throw new Error(DEV_AUTH_SETUP_MESSAGE);
  }
}
