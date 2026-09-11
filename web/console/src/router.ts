import { useSyncExternalStore } from "react";

// History router: page routes are URL paths; the host serves the SPA
// fallback so refresh restores the active page.
const listeners = new Set<() => void>();

function subscribe(fn: () => void) {
  listeners.add(fn);
  window.addEventListener("popstate", fn);
  return () => {
    listeners.delete(fn);
    window.removeEventListener("popstate", fn);
  };
}

export function usePath(): string {
  return useSyncExternalStore(subscribe, () => window.location.pathname, () => window.location.pathname);
}

export function navigate(path: string): void {
  if (window.location.pathname !== path) {
    window.history.pushState({}, "", path);
    window.dispatchEvent(new PopStateEvent("popstate"));
  }
}
