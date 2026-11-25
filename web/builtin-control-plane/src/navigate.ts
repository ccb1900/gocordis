// Plugin-local navigation helper. It dispatches the same popstate event as
// the shell router without importing shell code into the client module.
export function navigate(path: string): void {
  if (window.location.pathname !== path) {
    window.history.pushState({}, "", path);
    window.dispatchEvent(new PopStateEvent("popstate"));
  }
}
