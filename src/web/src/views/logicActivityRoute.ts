/** Logic Activity stays inside the selected configuration's address, so Back can restore
 * the configuration without discarding its editor or accidentally opening a different page. */
export function parseLogicActivityDetail(detail: string | null): {
  configurationId: string | null;
  activity: boolean;
  runId: string | null;
} {
  if (!detail) return { configurationId: null, activity: false, runId: null };
  const [configurationId, query] = detail.split('?', 2);
  if (!configurationId || !query) return { configurationId: detail, activity: false, runId: null };
  const params = new URLSearchParams(query);
  if (params.get('tab') !== 'activity') return { configurationId: detail, activity: false, runId: null };
  const runId = params.get('runId');
  const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  return { configurationId, activity: true, runId: runId && guid.test(runId) ? runId : null };
}

export function logicActivityDetail(configurationId: string, runId?: string | null): string {
  return configurationId + '?' + new URLSearchParams({
    tab: 'activity', ...(runId ? { runId } : {}),
  });
}
