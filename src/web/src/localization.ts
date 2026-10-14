/**
 * English-first message catalog. Keep UI messages keyed so other site languages
 * can be added without editing component markup or persisted event codes.
 */
export const englishMessages = {
  'nav.operations': 'Operations',
  'nav.assets': 'Assets',
  'nav.logic': 'Logic',
  'session.loading': 'Loading user…',
  'session.unavailable': 'Session unavailable',
  'session.label': 'Current user: {name}',
  'session.heading': 'Operator session',
  'session.noDetails': 'User details are unavailable.',
  'session.identity': 'Identity',
  'session.account': 'Account',
  'session.active': 'Active',
  'session.inactive': 'Inactive',
  'session.roles': 'Assigned roles',
  'session.noRoles': 'None assigned',
  'session.scope': 'Scoped assets',
  'session.assigned': '{count} assigned',
  'session.authNotice': 'Server-side access rules remain authoritative.',
  'refresh.sessionUnavailable':
    'Session access is unavailable. Sign in with an authorized account.',
  'refresh.runtimeFailed':
    'Runtime data request failed ({status}). Previous data may be stale.',
  'refresh.unavailable': 'WellSite AutoPilot information is unavailable.',
} as const;

export type MessageKey = keyof typeof englishMessages;

export function t(
  key: MessageKey,
  variables: Record<string, string | number> = {},
): string {
  return englishMessages[key].replace(/\{([a-zA-Z][a-zA-Z0-9]*)\}/g, (placeholder, name: string) => {
    const value = variables[name];
    return value === undefined ? placeholder : String(value);
  });
}
