import type { ConfiguredLogicResponse, LogicModuleResponse } from '../api/generated/v1/types.gen';
import { profileLabel } from '../words';
import { moduleStateLabel } from './moduleApproval';

type Manifest = {
  assetRequirements?: { role?: string; minimumCount?: number; maximumCount?: number; requiredAssetTypeKey?: string | null }[];
  dataRequirements?: { id?: string; assetRole?: string; quantity?: string; canonicalUnit?: string;
    maximumAgeSeconds?: number; allowUncertainQuality?: boolean }[];
  commandRequirements?: unknown[];
};

export interface ModuleSummary {
  /** One sentence: what it reads and for how many assets. */
  sentence: string;
  /** What it needs, one line each. */
  needs: string[];
  /** Whether the module declares any command it may send (never sent in Shadow mode). */
  declaresCommands: boolean;
  /** Publisher, how it runs, and trust, in one line. */
  provenance: string;
  /** Names of configured logic that use this module in any saved revision. */
  usedBy: string[];
}

const words = (value: string) => value.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ').toLowerCase();
const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;
function age(seconds: number): string {
  if (seconds % 3600 === 0 && seconds >= 3600) return `${seconds / 3600} h`;
  if (seconds % 60 === 0 && seconds >= 60) return `${seconds / 60} min`;
  return `${seconds} s`;
}

/** A plain-language description built only from the module's own manifest; unreadable manifests say so. */
export function describeModule(
  module: LogicModuleResponse,
  configurations: readonly ConfiguredLogicResponse[] = [],
): ModuleSummary {
  let manifest: Manifest | null = null;
  try { manifest = JSON.parse(module.manifestJson ?? '') as Manifest; } catch { manifest = null; }
  const assets = Array.isArray(manifest?.assetRequirements) ? manifest.assetRequirements : [];
  const data = Array.isArray(manifest?.dataRequirements) ? manifest.dataRequirements : [];
  const provenance = [module.publisher, profileLabel(module.executionProfile), moduleStateLabel(module)]
    .filter(Boolean).join(' · ');
  const usedBy = configurations
    .filter(item => (item.revisions ?? []).some(revision => revision.moduleId === module.moduleId))
    .map(item => item.name ?? '—').sort((a, b) => a.localeCompare(b));
  if (!manifest) {
    return { sentence: 'This module’s description could not be read.', needs: [], declaresCommands: false, provenance, usedBy };
  }
  const quantities = [...new Set(data.flatMap(item => (item.quantity ? [words(item.quantity)] : [])))];
  const count = assets.reduce((total, item) => total + (item.maximumCount ?? item.minimumCount ?? 0), 0);
  const typeKeys = [...new Set(assets.flatMap(item => (item.requiredAssetTypeKey ? [item.requiredAssetTypeKey] : [])))];
  const subject = typeKeys.length > 0 ? typeKeys.join(' or ') : 'asset';
  const sentence = quantities.length > 0
    ? `Reads ${quantities.join(', ')}${count > 0 ? ` for ${plural(count, subject, subject + 's')}` : ''}.`
    : count > 0 ? `Works on ${plural(count, subject, subject + 's')}.` : 'Does not declare any data it reads.';
  const needs: string[] = [];
  for (const item of assets) {
    const min = item.minimumCount ?? 0; const max = item.maximumCount ?? min;
    const range = min === max ? String(min) : `${min} to ${max}`;
    needs.push(`${range} ${item.requiredAssetTypeKey ?? 'asset'} for the “${item.role ?? 'asset'}” role`);
  }
  for (const item of data) {
    const parts = [`${words(item.quantity ?? 'value')}${item.canonicalUnit ? ` (${item.canonicalUnit})` : ''}`];
    if (typeof item.maximumAgeSeconds === 'number') parts.push(`no older than ${age(item.maximumAgeSeconds)}`);
    parts.push(item.allowUncertainQuality ? 'uncertain quality allowed' : 'good quality only');
    needs.push(parts.join(', '));
  }
  return { sentence, needs, declaresCommands: (manifest.commandRequirements ?? []).length > 0, provenance, usedBy };
}
