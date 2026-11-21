// Page-level aliases for renderer kinds that are implemented as view blocks.
// The backend declares pages with `renderer: "insight-graph"` (etc.) and no
// `views`; the shell's page path only resolves PAGE renderers, so without an
// alias such a page degrades to an "unknown renderer" warning even though the
// block renderer exists. Each alias renders the same component via GenericPage.
//
// Kept in its own module so the registration mapping is testable without
// importing App.tsx (which pulls in styles and the rest of the shell).

import type { UIPage } from "../types";
import { GenericPage } from "./GenericPage";
import { registerPageRenderer, type PageRenderer } from "./registry";
import type { ViewBlock } from "./schema";

export interface PageAliasSpec {
  /** Page renderer name; identical to the block kind it delegates to. */
  kind: string;
  /** Fallback title when the declared page has none. */
  title: string;
  query?: string;
  params?: Record<string, string>;
}

export const BUILTIN_PAGE_ALIASES: readonly PageAliasSpec[] = [
  { kind: "insight-graph", title: "依赖图", query: "insight/graph" },
  { kind: "runtime-inspector", title: "运行时检视", query: "runtime/snapshot" },
  { kind: "framework-dashboard", title: "框架概览", query: "framework/dashboard", params: { inspectorRoute: "/inspector" } },
];

/** The single view block a page alias feeds to GenericPage. */
export function pageAliasViews(spec: PageAliasSpec, page: UIPage): ViewBlock[] {
  return [{
    kind: spec.kind,
    ...(spec.query ? { query: spec.query } : {}),
    ...(spec.params ? { params: spec.params } : {}),
  }];
}

export function pageAliasFor(spec: PageAliasSpec): PageRenderer {
  return function PageAlias({ page, ctx }) {
    return (
      <section className="card" style={{ padding: "20px 22px" }}>
        <GenericPage
          title={page.title || spec.title}
          description={page.description}
          views={pageAliasViews(spec, page)}
          actions={page.actions ?? []}
          ctx={ctx}
        />
      </section>
    );
  };
}

/** Registers all built-in page aliases; returns a disposer for tests/unmount. */
export function registerBuiltinPageAliases(): () => void {
  const disposers = BUILTIN_PAGE_ALIASES.map((spec) =>
    registerPageRenderer(spec.kind, pageAliasFor(spec))
  );
  return () => {
    for (const dispose of disposers) dispose();
  };
}
