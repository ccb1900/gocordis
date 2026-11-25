// Pure normalization for the plugin control-plane wire DTOs. The hub is being
// rolled out alongside this console, so every field is read defensively:
// missing/null arrays become [] and missing scalar flags become false. No
// React / DOM dependency — these helpers are node-testable.

import type {
  ExplorerPlugin, PackageDependencyView, PluginDependencyRef,
  PluginDependencyView, PluginDiagnostic, PluginMutationResult,
  StorePluginEntry, UploadedPackage,
} from "../types";

const str = (v: unknown): string =>
  typeof v === "string" ? v : typeof v === "number" ? String(v) : "";

const arr = (v: unknown): unknown[] => (Array.isArray(v) ? v : []);

const strings = (v: unknown): string[] => arr(v).map(str).filter((s) => s.length > 0);

const flag = (v: unknown): boolean =>
  v === true || v === "true" || v === 1 || v === "1";

const obj = (v: unknown): Record<string, unknown> =>
  v && typeof v === "object" && !Array.isArray(v) ? (v as Record<string, unknown>) : {};

export function dependencyRefs(v: unknown): PluginDependencyRef[] {
  return arr(v)
    .map((raw) => {
      const d = obj(raw);
      return { id: str(d.id), versionRange: str(d.versionRange) };
    })
    .filter((d) => d.id.length > 0);
}

export function normalizePlugin(raw: unknown): ExplorerPlugin {
  const p = obj(raw);
  return {
    id: str(p.id),
    name: str(p.name) || str(p.id),
    type: str(p.type),
    enabled: flag(p.enabled),
    state: str(p.state) || "Unknown",
    error: str(p.error) || undefined,
    version: str(p.version) || undefined,
    installedVersion: str(p.installedVersion) || undefined,
    protected: flag(p.protected),
    controllable: flag(p.controllable),
    declared: flag(p.declared),
    config: obj(p.config),
    components: strings(p.components),
    capabilities: strings(p.capabilities),
    requires: strings(p.requires),
    provides: strings(p.provides),
    dependencies: strings(p.dependencies),
  };
}

export function normalizePlugins(raw: unknown): ExplorerPlugin[] {
  return arr(raw).map(normalizePlugin);
}

export function normalizeStorePlugin(raw: unknown): StorePluginEntry {
  const s = obj(raw);
  return {
    id: str(s.id),
    version: str(s.version),
    displayName: str(s.displayName) || undefined,
    hostVersionRange: str(s.hostVersionRange) || undefined,
    installPath: str(s.installPath) || undefined,
    dependencies: dependencyRefs(s.dependencies),
    provides: strings(s.provides),
    checksum: str(s.checksum) || undefined,
    declared: flag(s.declared),
    running: flag(s.running),
  };
}

export function normalizeStorePlugins(raw: unknown): StorePluginEntry[] {
  return arr(raw).map(normalizeStorePlugin);
}

function diagnostic(v: unknown): string | PluginDiagnostic {
  if (typeof v === "string") return v;
  const d = obj(v);
  return {
    code: str(d.code) || undefined,
    message: str(d.message) || undefined,
    level: str(d.level) || undefined,
    severity: str(d.severity) || undefined,
    ...d,
  } as PluginDiagnostic;
}

export function normalizeDependencyView(raw: unknown, fallbackId: string): PluginDependencyView {
  const d = obj(raw);
  const packageDependencies: PackageDependencyView[] = arr(d.packageDependencies).map((item) => {
    const dep = obj(item);
    return {
      id: str(dep.id),
      versionRange: str(dep.versionRange),
      installedVersion: str(dep.installedVersion) || undefined,
      satisfied: flag(dep.satisfied),
    };
  });
  return {
    id: str(d.id) || fallbackId,
    version: str(d.version) || undefined,
    packageDependencies,
    dependents: strings(d.dependents),
    capabilityRequires: strings(d.capabilityRequires),
    capabilityProvides: strings(d.capabilityProvides),
    diagnostics: arr(d.diagnostics).map(diagnostic),
  };
}

export function normalizeUpload(raw: unknown): UploadedPackage {
  const u = obj(raw);
  return {
    uploadKey: str(u.uploadKey),
    id: str(u.id),
    version: str(u.version),
    type: str(u.type) || undefined,
    hostVersionRange: str(u.hostVersionRange) || undefined,
    checksum: str(u.checksum) || undefined,
    dependencies: dependencyRefs(u.dependencies),
  };
}

export function normalizeMutation(raw: unknown): PluginMutationResult {
  const m = obj(raw);
  return {
    accepted: flag(m.accepted),
    code: str(m.code) || undefined,
    error: str(m.error) || undefined,
    id: str(m.id) || undefined,
    state: str(m.state) || undefined,
    // A missing `persisted` is not evidence of persistence.
    persisted: "persisted" in m ? flag(m.persisted) : undefined,
    changes: Array.isArray(m.changes) ? (m.changes as Array<string | Record<string, unknown>>) : [],
  };
}

/** Code from either a mutation result or a thrown unified `{code,message}`
 * transport error. Used by the config editor to branch on
 * `invalid_config` / `conflict`. */
export function errorCodeOf(value: unknown): string | undefined {
  if (!value || typeof value !== "object") return undefined;
  const code = str((value as Record<string, unknown>).code);
  return code || undefined;
}

export function mutationMessage(result: PluginMutationResult, fallback: string): string {
  const parts = [result.code, result.error || fallback].filter((p): p is string => Boolean(p));
  return parts.join(": ");
}

export function diagnosticText(d: string | PluginDiagnostic): string {
  if (typeof d === "string") return d;
  const parts = [d.code, d.message].filter((p): p is string => Boolean(p));
  return parts.join(": ") || "diagnostic";
}
