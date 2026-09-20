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

// replace 改写当前历史项（不新增）：用于"/ → 首页"这类规范化跳转，
// 回退键不应把用户送回一个从未有意义的地址。
export function replace(path: string): void {
  if (window.location.pathname !== path) {
    window.history.replaceState({}, "", path);
    window.dispatchEvent(new PopStateEvent("popstate"));
  }
}
