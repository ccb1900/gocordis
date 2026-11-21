// Page-level renderer registration: backend pages (notably /insight) declare a
// renderer name but no views. These tests pin the alias mapping to the SAME
// block kind and verify the alias renders through GenericPage.
import type { ReactElement } from "react";
import { describe, expect, it } from "vitest";
import type { UIPage } from "../types";
import { GenericPage } from "./GenericPage";
import {
  BUILTIN_PAGE_ALIASES, pageAliasFor, pageAliasViews, registerBuiltinPageAliases,
} from "./page-renderers";
import { getBlockRenderer, getPageRenderer, type ViewContext } from "./registry";

const ctx: ViewContext = {
  hubQuery: async <T,>() => ({}) as T,
  hubCommand: async () => undefined,
  focus: null,
  onFocus: () => undefined,
  busy: false,
  generation: 0,
};

const page: UIPage = {
  id: "insight", title: "Insight", route: "/insight", renderer: "insight-graph",
};

function isElement(v: unknown): v is ReactElement {
  return !!v && typeof v === "object" && "type" in v && "props" in v;
}

describe("built-in page renderer aliases", () => {
  it("maps insight-graph (and the new kinds) to existing block renderers", () => {
    expect(BUILTIN_PAGE_ALIASES.map((a) => a.kind)).toContain("insight-graph");
    for (const spec of BUILTIN_PAGE_ALIASES) {
      expect(getBlockRenderer(spec.kind)).toBeTypeOf("function");
      expect(pageAliasViews(spec, page)[0]).toMatchObject({ kind: spec.kind, query: spec.query });
    }
    expect(pageAliasViews(BUILTIN_PAGE_ALIASES[0], page)[0].kind).toBe("insight-graph");
  });

  it("registers the aliases as page renderers and disposes cleanly", () => {
    const before = BUILTIN_PAGE_ALIASES.map((s) => getPageRenderer(s.kind));
    const dispose = registerBuiltinPageAliases();
    try {
      for (const spec of BUILTIN_PAGE_ALIASES) {
        expect(getPageRenderer(spec.kind)).toBeTypeOf("function");
      }
    } finally {
      dispose();
    }
    BUILTIN_PAGE_ALIASES.forEach((spec, i) => {
      expect(getPageRenderer(spec.kind)).toBe(before[i]);
    });
  });

  it("renders the insight page through GenericPage with an insight-graph view", () => {
    const spec = BUILTIN_PAGE_ALIASES.find((a) => a.kind === "insight-graph")!;
    const Renderer = pageAliasFor(spec) as (props: { page: UIPage; ctx: ViewContext }) => ReactElement;
    const element = Renderer({ page, ctx });
    const child: unknown = element.props.children;
    const nested = (Array.isArray(child) ? child : [child]).filter(isElement);
    const generic = nested.find((e) => e.type === GenericPage);
    expect(generic).toBeDefined();
    expect(generic!.props.title).toBe("Insight");
    expect(generic!.props.views).toEqual([{ kind: "insight-graph", query: "insight/graph" }]);
  });
});
