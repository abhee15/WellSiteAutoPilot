/**
 * English-first message catalog. Keep UI messages keyed so other site languages
 * can be added without editing component markup or persisted event codes.
 */
export const englishMessages = {
  'views.controls': 'Execution view controls',
  'views.search': 'Search',
  'views.searchPlaceholder': 'Asset, module, outcome…',
  'views.status': 'Status',
  'views.sort': 'Sort',
  'views.all': 'All statuses',
  'views.active': 'Active',
  'views.attention': 'Requires attention',
  'views.completed': 'Completed',
  'views.newest': 'Newest first',
  'views.oldest': 'Oldest first',
  'views.savedViews': 'Saved views',
  'views.custom': 'Custom filters',
  'views.name': 'View name',
  'views.save': 'Save view',
  'views.delete': 'Delete view',
  'views.reset': 'Reset filters',
  'views.signIn': 'Sign in before saving a view.',
  'views.nameInvalid': 'Enter a view name of up to 48 characters.',
  'views.limitReached': 'You can save up to 20 views.',
  'views.nameExists': 'A view with that name already exists.',
  'views.saveFailed': 'View preferences could not be saved in this browser.',
  'views.saved': 'View saved.',
  'views.deleted': 'View deleted.',
  'views.count': 'Showing {shown} of {total} recent executions.',
  'views.deviceNote': 'Saved views are stored in this browser for the signed-in user.',
  'views.noActivity': 'No execution activity has been recorded.',
  'views.noMatches': 'No recent executions match this view.',
  'assets.controls': 'Asset inventory controls',
  'assets.search': 'Search assets',
  'assets.searchPlaceholder': 'Asset name, ID, or type…',
  'assets.activity': 'Status',
  'assets.allActivity': 'All assets',
  'assets.active': 'Active',
  'assets.inactive': 'Inactive',
  'assets.hierarchy': 'Hierarchy',
  'assets.allHierarchy': 'All levels',
  'assets.roots': 'Root assets',
  'assets.children': 'Child assets',
  'assets.sort': 'Sort by',
  'assets.name': 'Name',
  'assets.newest': 'Newest first',
  'assets.oldest': 'Oldest first',
  'assets.count': 'Showing {shown} of {total} loaded assets (up to 100 per refresh).',
  'assets.none': 'No Assets are configured.',
  'assets.noMatches': 'No authorized assets match these filters.',
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
