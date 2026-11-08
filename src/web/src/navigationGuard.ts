import { useCallback, useEffect, useId, useSyncExternalStore } from 'react';

/**
 * Warns before in-app navigation or a reload throws away edits.
 *
 * A form calls `useUnsavedChangesGuard(unsaved)` where `unsaved` means "differs from what was loaded or last saved",
 * not "the person touched a field", so reverting an edit makes the form clean again. While any form is unsaved:
 *  - reload and close show the browser's own prompt (its text cannot be customised);
 *  - a sidebar link, Back/Forward or an edited address is caught at `hashchange`: the address is put back and
 *    <UnsavedChangesPrompt /> asks Stay or Leave;
 *  - in-page selection changes (`useHashDetail`) ask through `requestNavigation`.
 * Nothing is saved as a draft: that would raise stale-revision questions the API contract does not answer.
 */
const unsaved = new Set<string>();
const listeners = new Set<() => void>();
let pending: (() => void) | null = null;
/** The address the person is really on, kept so an intercepted change can be undone. */
let committedHash = '';
/** The address a confirmed Leave is about to set; its hashchange passes through once. */
let allowedHash: string | null = null;

const emit = () => listeners.forEach(listener => listener());
const hashRepresentation = () => window.location.pathname + window.location.search;

function onBeforeUnload(event: BeforeUnloadEvent) {
  event.preventDefault();
  event.returnValue = '';
}

function onHashChange(event: HashChangeEvent) {
  const target = window.location.hash;
  if (allowedHash === target) { allowedHash = null; committedHash = target; return; }
  if (unsaved.size === 0) { committedHash = target; return; }
  // Registered in the capture phase, so the router's own listeners never see a change that is being asked about.
  event.stopImmediatePropagation();
  const attempted = target;
  window.history.replaceState(window.history.state, '', hashRepresentation() + committedHash);
  requestNavigation(() => { allowedHash = attempted; window.location.hash = attempted; });
}

function attach() {
  committedHash = window.location.hash;
  window.addEventListener('beforeunload', onBeforeUnload);
  window.addEventListener('hashchange', onHashChange, true);
}
function detach() {
  window.removeEventListener('beforeunload', onBeforeUnload);
  window.removeEventListener('hashchange', onHashChange, true);
}

/** For a navigation the person already confirmed: lets that one address change through while edits still exist. */
export function allowHash(hash: string) { if (unsaved.size > 0) allowedHash = hash; }

export function hasUnsavedChanges(): boolean { return unsaved.size > 0; }

/** Runs `proceed` now when nothing is unsaved; otherwise holds it until the person chooses Leave. */
export function requestNavigation(proceed: () => void) {
  if (unsaved.size === 0) { proceed(); return; }
  pending = proceed;
  emit();
}
export function confirmLeave() {
  const proceed = pending;
  pending = null;
  emit();
  proceed?.();
}
export function stayHere() {
  pending = null;
  emit();
}

/**
 * Returns `markSaved`, to call the moment a save succeeds and before the page navigates on: the form is clean from
 * then on, even though React has not re-rendered yet.
 */
export function useUnsavedChangesGuard(hasUnsaved: boolean): () => void {
  const token = useId();
  const release = useCallback(() => {
    if (!unsaved.delete(token)) return;
    if (unsaved.size === 0) { detach(); if (pending) { pending = null; emit(); } }
  }, [token]);
  useEffect(() => {
    if (!hasUnsaved) return;
    const wasEmpty = unsaved.size === 0;
    unsaved.add(token);
    if (wasEmpty) attach();
    return release;
  }, [hasUnsaved, token, release]);
  return release;
}

export function useNavigationQuestion(): boolean {
  return useSyncExternalStore(
    listener => { listeners.add(listener); return () => { listeners.delete(listener); }; },
    () => pending !== null,
    () => false,
  );
}
