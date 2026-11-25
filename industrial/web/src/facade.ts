// The console owns the one React/antd instance. Client modules receive the
// facade at register() time; facade shims read it at render time.
type Facade = any;
let current: Facade | null = null;

export function setFacade(api: Facade): void {
  current = api;
}

export function getFacade(): Facade {
  if (!current && typeof globalThis !== "undefined") {
    current = (globalThis as any).__CORDIS_CONSOLE ?? null;
  }
  if (!current) {
    throw new Error("[industrial-designer] console facade is not available");
  }
  return current;
}
