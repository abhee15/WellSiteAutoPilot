import { canOpenSection, isAwaitingAccess, isSignInRequired, loadSession, sessionAccessKey } from './security/sessionAccess';
import { AwaitingAccessNotice } from './components/AwaitingAccessNotice';
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
  SystemInfoResponse,
} from './api/generated/v1/types.gen';
import { t } from './localization';
import { UserMenu } from './components/UserMenu';
import { NavigationLink } from './components/NavigationLink';
import type { Section } from './components/NavigationLink';
import { PageHeader } from './components/PageHeader';
import { NavigationGroup } from './components/NavigationGroup';
import { firstOpenableSection, hasExplicitSection, sectionFromHash } from './routing';

// Compatibility with existing consumers; new components live in their own files.
export { UserMenu, NavigationLink };
import { RecommendationsPage } from './views/RecommendationsPage';
import { AdministrationWorkspace } from './views/AdministrationWorkspace';
import { OperationsPage } from './views/OperationsPage';
import { AssetsPage } from './views/AssetsPage';
import { LogicPage } from './views/LogicPage';

const REFRESH_INTERVAL_MS = 10_000;


export function App() {
  const [section, setSection] = useState<Section>(() => sectionFromHash());
  const [systemInfo, setSystemInfo] = useState<SystemInfoResponse | null>(null);
  const [currentUser, setCurrentUser] = useState<CurrentUserResponse | null>(null);
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

  // The default landing page moves to the first section this user may open; an explicit
  // address is respected and answered with the "not allowed" message instead.
  useEffect(() => {
    if (!currentUser || isAwaitingAccess(currentUser) || hasExplicitSection()) return;
    const landing = firstOpenableSection(currentUser);
    if (landing && landing !== 'operations') window.location.replace('#' + landing);
  }, [currentUser]);

  const refresh = useCallback(async () => {
    // Only the latest refresh may update the view.
    activeRefresh.current?.abort();
    const controller = new AbortController();
    activeRefresh.current = controller;
    setIsRefreshing(true);

    const clearWorkspace = () => {
      setSystemInfo(null); setExecutions([]); setAssetTypes([]); setAssets([]);
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
      if (fresh('system')) setSystemInfo(snapshot.system);
      if (fresh('executions')) setExecutions(snapshot.executions);
      if (fresh('assetTypes')) setAssetTypes(snapshot.assetTypes);
      if (fresh('assets')) setAssets(snapshot.assets);
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
    const interval = window.setInterval(() => {
      if (!signInRequired.current) void refresh();
    }, REFRESH_INTERVAL_MS);
    return () => {
      window.clearInterval(interval);
      activeRefresh.current?.abort();
      activeRefresh.current = null;
    };
  }, [refresh]);

  return (
    <div className="app-shell">
      <aside className="navigation">
        <div className="product-mark">
          <span className="product-kicker">Weatherford</span>
          <strong>WellSite AutoPilot</strong>
        </div>
        <nav aria-label="Primary">
          <NavigationGroup id="nav-group-operate" label={t('nav.groupOperate')}>
            {[canOpenSection(currentUser, 'operations') && <NavigationLink key="operations" section="operations" current={section}>{t('nav.operations')}</NavigationLink>,
              canOpenSection(currentUser, 'recommendations') && <NavigationLink key="recommendations" section="recommendations" current={section}>{t('nav.recommendations')}</NavigationLink>]}
          </NavigationGroup>
          <NavigationGroup id="nav-group-understand" label={t('nav.groupUnderstand')}>
            {[canOpenSection(currentUser, 'assets') && <NavigationLink key="assets" section="assets" current={section}>{t('nav.assets')}</NavigationLink>,
              canOpenSection(currentUser, 'logic') && <NavigationLink key="logic" section="logic" current={section}>{t('nav.logic')}</NavigationLink>]}
          </NavigationGroup>
          <NavigationGroup id="nav-group-administer" label={t('nav.groupAdminister')}>
            {[canOpenSection(currentUser, 'access') && <NavigationLink key="access" section="access" current={section}>{t('nav.access')}</NavigationLink>]}
          </NavigationGroup>
        </nav>
        <UserMenu user={currentUser} isLoading={isRefreshing} />
      </aside>

      <main className="content">
        <PageHeader
          section={section}
          isRefreshing={section === 'recommendations' || section === 'access' ? false : isRefreshing}
          lastUpdated={section === 'recommendations' || section === 'access' ? null : lastUpdated}
          onRefresh={section === 'recommendations' || section === 'access' ? undefined : () => void refresh()}
        />

        {error && (
          <div className="status-error" role="alert">
            {error}
          </div>
        )}

        {isAwaitingAccess(currentUser) && currentUser && <AwaitingAccessNotice user={currentUser} />}
        {currentUser && !isAwaitingAccess(currentUser) && section !== 'access' && !canOpenSection(currentUser, section) && (
          <div className="status-error" role="alert">{t('session.sectionDenied')}</div>
        )}
        {section === 'operations' && canOpenSection(currentUser, section) && (
          <OperationsPage
            key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
            systemInfo={systemInfo}
            executions={executions}
            isLoaded={isLoaded}
            userId={currentUser?.userId}
          />
        )}
        {section === 'recommendations' && canOpenSection(currentUser, section) && (
          <RecommendationsPage
            key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
            userId={currentUser?.userId}
            permissions={currentUser?.permissions}
          />
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
            onRefresh={refresh}
          />
        )}
        {section === 'logic' && canOpenSection(currentUser, section) && (
          <LogicPage
            key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
            userId={currentUser?.userId}
            assets={assets}
            assetTypes={assetTypes}
            onRefresh={refresh}
            modules={logicModules}
            configuredLogic={configuredLogic}
            isLoaded={isLoaded}
            permissions={currentUser?.permissions ?? []}
          />
        )}
      </main>
    </div>
  );
}
