import { canOpenSection, isAwaitingAccess, isSignInRequired, loadSession, sessionAccessKey } from './security/sessionAccess';
import { AwaitingAccessNotice } from './components/AwaitingAccessNotice';
import { loadWorkspaceSnapshot } from './views/workspaceSnapshot';
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
import { sectionFromHash } from './routing';

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

  useEffect(() => {
    const onHashChange = () => setSection(sectionFromHash());
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  const refresh = useCallback(async () => {
    // Only the latest refresh may update the view.
    activeRefresh.current?.abort();
    const controller = new AbortController();
    activeRefresh.current = controller;
    setIsRefreshing(true);

    const clearWorkspace = () => {
      setSystemInfo(null); setExecutions([]); setAssetTypes([]); setAssets([]);
      setAssetCapabilities(null); setLogicModules([]); setConfiguredLogic([]); setLastUpdated(null);
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
      setSystemInfo(snapshot.system); setExecutions(snapshot.executions);
      setAssetTypes(snapshot.assetTypes); setAssets(snapshot.assets);
      setAssetCapabilities(snapshot.capabilities); setLogicModules(snapshot.modules);
      setConfiguredLogic(snapshot.configurations);
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
          {canOpenSection(currentUser, 'operations') && <NavigationLink section="operations" current={section}>{t('nav.operations')}</NavigationLink>}
          {canOpenSection(currentUser, 'recommendations') && <NavigationLink section="recommendations" current={section}>{t('nav.recommendations')}</NavigationLink>}
          {canOpenSection(currentUser, 'assets') && <NavigationLink section="assets" current={section}>{t('nav.assets')}</NavigationLink>}
          {canOpenSection(currentUser, 'logic') && <NavigationLink section="logic" current={section}>{t('nav.logic')}</NavigationLink>}
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
            permissions={currentUser?.permissions ?? []}
          />
        )}
      </main>
    </div>
  );
}
