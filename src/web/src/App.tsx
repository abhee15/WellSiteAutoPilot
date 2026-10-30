import { canOpenSection, hasPermission, isAwaitingAccess, isSignInRequired, loadSession, sessionAccessKey } from './security/sessionAccess';
import { AwaitingAccessNotice } from './components/AwaitingAccessNotice';
import { LoadingIndicator } from './components/LoadingIndicator';
import { loadWorkspaceSnapshot } from './views/workspaceSnapshot';
import type { SnapshotKey } from './views/workspaceSnapshot';
import { useCallback, useEffect, useRef, useState } from 'react';
import type {
  AssetResponse,
  AssetCreationCapabilitiesResponse,
  AssetTypeResponse,
  ConfiguredLogicResponse,
  CurrentUserResponse,
  ExecutionResponse,
  LogicModuleResponse,
  SystemHealthResponse,
} from './api/generated/v1/types.gen';
import { t } from './localization';
import { UserMenu } from './components/UserMenu';
import { NavigationLink } from './components/NavigationLink';
import { useNavCollapsed } from './components/useNavCollapsed';
import { useSplitPaneViewport } from './components/useSplitPaneViewport';
import type { Section } from './components/NavigationLink';
import { PageHeader } from './components/PageHeader';
import { NavigationGroup } from './components/NavigationGroup';
import { StatusPage } from './views/StatusPage';
import { ConnectionsPage } from './views/ConnectionsPage';
import { firstOpenableSection, hasExplicitSection, sectionFromHash } from './routing';
import { useChangeRefresh, useLiveUpdates, usePollingRefresh } from './live/LiveUpdates';
import { keepIfSame } from './keepIfSame';

// Compatibility with existing consumers; new components live in their own files.
export { UserMenu, NavigationLink };
import { RecommendationsPage } from './views/RecommendationsPage';
import { AdministrationWorkspace } from './views/AdministrationWorkspace';
import { OperationsPage } from './views/OperationsPage';
import { OverviewPage } from './views/OverviewPage';
import { AssetsPage } from './views/AssetsPage';
import { LogicPage } from './views/LogicPage';


export function App() {
  const [section, setSection] = useState<Section>(() => sectionFromHash());
  const [systemHealth, setSystemHealth] = useState<SystemHealthResponse | null>(null);
  const [currentUser, setCurrentUser] = useState<CurrentUserResponse | null>(null);
  const mainRef = useRef<HTMLElement | null>(null);
  useSplitPaneViewport(mainRef);
  const nav = useNavCollapsed();
  const activeRefresh = useRef<AbortController | null>(null);
  const accessKey = useRef<string | null>(null);
  // A 401 means the browser sent no Windows credentials; polling would only repeat prompts.
  const signInRequired = useRef(false);
  const [executions, setExecutions] = useState<ExecutionResponse[]>([]);
  const [assetTypes, setAssetTypes] = useState<AssetTypeResponse[]>([]);
  const [assets, setAssets] = useState<AssetResponse[]>([]);
  const [assetCapabilities, setAssetCapabilities] = useState<AssetCreationCapabilitiesResponse | null>(null);
  const [logicModules, setLogicModules] = useState<LogicModuleResponse[]>([]);
  const [configuredLogic, setConfiguredLogic] = useState<ConfiguredLogicResponse[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  // False until the first complete read for this session, so "nothing here" is never shown while loading.
  const [isLoaded, setIsLoaded] = useState(false);

  useEffect(() => {
    const onHashChange = () => setSection(sectionFromHash());
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  useEffect(() => {
    document.title = `${t(('nav.' + section) as 'nav.operations')} · WellSite AutoPilot`;
  }, [section]);

  // The default landing page moves to the first section this user may open; an explicit
  // address is respected and answered with the "not allowed" message instead.
  useEffect(() => {
    if (!currentUser || isAwaitingAccess(currentUser) || hasExplicitSection()) return;
    const landing = firstOpenableSection(currentUser);
    if (landing && landing !== 'overview') window.location.replace('#' + landing);
  }, [currentUser]);

  const refresh = useCallback(async () => {
    // Only the latest refresh may update the view.
    activeRefresh.current?.abort();
    const controller = new AbortController();
    activeRefresh.current = controller;
    setIsRefreshing(true);

    const clearWorkspace = () => {
      setSystemHealth(null); setExecutions([]); setAssetTypes([]); setAssets([]);
      setAssetCapabilities(null); setLogicModules([]); setConfiguredLogic([]); setLastUpdated(null);
      setIsLoaded(false);
    };
    try {
      const user = await loadSession(controller.signal);
      if (controller.signal.aborted) return;
      signInRequired.current = false;
      const nextKey = sessionAccessKey(user);
      if (nextKey !== accessKey.current) clearWorkspace();
      accessKey.current = nextKey;
      setCurrentUser(user);
      const snapshot = await loadWorkspaceSnapshot(user, controller.signal);
      if (controller.signal.aborted) return;
      const effectiveUser = { ...user, permissions: user.permissions.filter(p => !snapshot.deniedPermissions.includes(p)) };
      accessKey.current = sessionAccessKey(effectiveUser);
      setCurrentUser(effectiveUser);
      // A dataset that could not be read this time keeps its last good copy instead of emptying the page.
      const fresh = (key: SnapshotKey) => !snapshot.unavailable.includes(key);
      if (fresh('system')) setSystemHealth(snapshot.system);
      if (fresh('executions')) setExecutions(snapshot.executions);
      // Unchanged lists keep their identity, so a refresh that found nothing new redraws nothing below.
      if (fresh('assetTypes')) setAssetTypes(current => keepIfSame(current, snapshot.assetTypes));
      if (fresh('assets')) setAssets(current => keepIfSame(current, snapshot.assets));
      if (fresh('capabilities')) setAssetCapabilities(snapshot.capabilities);
      if (fresh('modules')) setLogicModules(snapshot.modules);
      if (fresh('configurations')) setConfiguredLogic(snapshot.configurations);
      setIsLoaded(true);
      setError(snapshot.deniedPermissions.length ? t('refresh.accessChanged')
        : snapshot.incomplete ? t('refresh.partial') : null);
      setLastUpdated(new Date());
    } catch (reason) {
      if (controller.signal.aborted ||
          (reason instanceof DOMException && reason.name === 'AbortError')) return;

      clearWorkspace(); setCurrentUser(null); accessKey.current = null;
      signInRequired.current = isSignInRequired(reason);
      setError(t(signInRequired.current ? 'refresh.signInRequired' : 'refresh.sessionUnavailable'));
    } finally {
      if (activeRefresh.current === controller) {
        activeRefresh.current = null;
        setIsRefreshing(false);
      }
    }
  }, []);

  useEffect(() => {
    void refresh();
    return () => {
      activeRefresh.current?.abort();
      activeRefresh.current = null;
    };
  }, [refresh]);

  // Polling stays the fallback: every 10 s without a live connection, about once a minute with one.
  // A poll never interrupts a load that is still running: the first request to a Negotiate site can outlast the
  // interval (sign-in prompt, Kerberos/NTLM round trips), and aborting it restarts the handshake and re-prompts.
  usePollingRefresh(() => { if (!signInRequired.current && activeRefresh.current === null) void refresh(); });
  // The workspace snapshot covers these four areas; the Server names the one that changed.
  useChangeRefresh(['executions', 'assets', 'configured-logic', 'modules'], () => {
    if (!signInRequired.current) void refresh();
  });
  // Connect only with a verified session. A session that cannot be read disconnects, so a missing
  // Windows sign-in is never retried through the hub.
  const { setEnabled: setLiveEnabled } = useLiveUpdates();
  const hasSession = currentUser !== null;
  useEffect(() => {
    setLiveEnabled(hasSession);
  }, [hasSession, setLiveEnabled]);

  return (
    <div className={nav.collapsed ? 'app-shell nav-collapsed' : 'app-shell'}>
      <a className="skip-link" href="#main-content" onClick={event => {
        // The address is the app's router, so move focus instead of changing it.
        event.preventDefault();
        mainRef.current?.focus();
      }}>{t('page.skipToContent')}</a>
      <aside className="navigation">
        <div className="product-mark">
          <div className="product-name">
            <span className="product-kicker">Weatherford</span>
            <strong>WellSite AutoPilot</strong>
          </div>
          {!nav.narrow && (
            <button type="button" className="nav-toggle" aria-expanded={!nav.collapsed} aria-controls="primary-nav"
              aria-label={t(nav.collapsed ? 'nav.expand' : 'nav.collapse')} title={t(nav.collapsed ? 'nav.expand' : 'nav.collapse')}
              onClick={nav.toggle}>
              <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="1.75"
                strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
                <path d="M4 5h16v14H4zM9 5v14" />
                <path d={nav.collapsed ? 'M13 9l3 3-3 3' : 'M16 9l-3 3 3 3'} />
              </svg>
            </button>
          )}
        </div>
        <nav aria-label="Primary" id="primary-nav">
          <NavigationGroup id="nav-group-operate" label={t('nav.groupOperate')}>
            {[canOpenSection(currentUser, 'overview') && <NavigationLink key="overview" section="overview" current={section}>{t('nav.overview')}</NavigationLink>,
              canOpenSection(currentUser, 'operations') && <NavigationLink key="operations" section="operations" current={section}>{t('nav.operations')}</NavigationLink>,
              canOpenSection(currentUser, 'recommendations') && <NavigationLink key="recommendations" section="recommendations" current={section}>{t('nav.recommendations')}</NavigationLink>]}
          </NavigationGroup>
          <NavigationGroup id="nav-group-understand" label={t('nav.groupUnderstand')}>
            {[canOpenSection(currentUser, 'assets') && <NavigationLink key="assets" section="assets" current={section}>{t('nav.assets')}</NavigationLink>,
              canOpenSection(currentUser, 'logic') && <NavigationLink key="logic" section="logic" current={section}>{t('nav.logic')}</NavigationLink>]}
          </NavigationGroup>
          <NavigationGroup id="nav-group-administer" label={t('nav.groupAdminister')}>
            {[canOpenSection(currentUser, 'connections') && <NavigationLink key="connections" section="connections" current={section}>{t('nav.connections')}</NavigationLink>,
              canOpenSection(currentUser, 'status') && <NavigationLink key="status" section="status" current={section}>{t('nav.status')}</NavigationLink>,
              canOpenSection(currentUser, 'access') && <NavigationLink key="access" section="access" current={section}>{t('nav.access')}</NavigationLink>]}
          </NavigationGroup>
        </nav>
        <UserMenu user={currentUser} isLoading={isRefreshing} />
      </aside>

      <main className="content" id="main-content" tabIndex={-1} ref={mainRef}>
        <PageHeader
          section={section}
          isRefreshing={section === 'recommendations' || section === 'access' || section === 'connections' || section === 'status' ? false : isRefreshing}
          lastUpdated={section === 'recommendations' || section === 'access' || section === 'connections' || section === 'status' ? null : lastUpdated}
          onRefresh={section === 'recommendations' || section === 'access' || section === 'connections' || section === 'status' ? undefined : () => void refresh()}
        />

        <div className="page-body" key={section}>
          {error && (
            <div className="status-error" role="alert">
              {error}
            </div>
          )}

          {/* Until the session is read there is nothing else to show, so say so instead of leaving the page blank. */}
          {!currentUser && !error && <LoadingIndicator size="large" label={t('session.opening')} />}
          {isAwaitingAccess(currentUser) && currentUser && <AwaitingAccessNotice user={currentUser} />}
          {currentUser && !isAwaitingAccess(currentUser) && section !== 'access' && !canOpenSection(currentUser, section) && (
            <div className="status-error" role="alert">{t('session.sectionDenied')}</div>
          )}
          {section === 'overview' && canOpenSection(currentUser, section) && (
            <OverviewPage
              key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
              permissions={currentUser?.permissions ?? []}
              systemHealth={systemHealth}
              executions={executions}
              assets={assets}
              configuredLogic={configuredLogic}
              isLoaded={isLoaded}
            />
          )}
          {section === 'operations' && canOpenSection(currentUser, section) && (
            <OperationsPage
              key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
              executions={executions}
              assets={assets}
              configuredLogic={configuredLogic}
              isLoaded={isLoaded}
              userId={currentUser?.userId}
              canReadConnections={hasPermission(currentUser, 'connections.read')}
            />
          )}
          {section === 'recommendations' && canOpenSection(currentUser, section) && (
            <RecommendationsPage
              key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
              userId={currentUser?.userId}
              permissions={currentUser?.permissions}
              assets={assets}
              configuredLogic={configuredLogic}
            />
          )}
          {section === 'connections' && canOpenSection(currentUser, section) && (
            <ConnectionsPage key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
              canManage={hasPermission(currentUser, 'connections.manage')} />
          )}
          {section === 'status' && canOpenSection(currentUser, section) && (
            <StatusPage key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
              canSeeServices={hasPermission(currentUser, 'connections.read') || hasPermission(currentUser, 'security.manage')}
              canSeeConnections={hasPermission(currentUser, 'connections.read')} />
          )}
          {section === 'access' && (
            currentUser?.userId && canOpenSection(currentUser, 'access')
              ? <AdministrationWorkspace key={currentUser.userId} currentUserId={currentUser.userId} />
              : <div className="status-error" role="alert">{t('access.denied')}</div>
          )}
          {section === 'assets' && canOpenSection(currentUser, section) && (
            <AssetsPage
              key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
              assetTypes={assetTypes}
              assets={assets}
              isLoaded={isLoaded}
              userId={currentUser?.userId}
              capabilities={assetCapabilities}
              configuredLogic={configuredLogic}
              permissions={currentUser?.permissions ?? []}
              onRefresh={refresh}
            />
          )}
          {section === 'logic' && canOpenSection(currentUser, section) && (
            <LogicPage
              key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
              userId={currentUser?.userId}
              assets={assets}
              assetTypes={assetTypes.filter(type => type.isActive !== false)}
              onRefresh={refresh}
              modules={logicModules}
              configuredLogic={configuredLogic}
              executions={executions}
              isLoaded={isLoaded}
              permissions={currentUser?.permissions ?? []}
            />
          )}
        </div>
      </main>
    </div>
  );
}
