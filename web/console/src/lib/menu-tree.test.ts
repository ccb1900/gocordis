import { describe, expect, it } from "vitest";
import { buildMenuTree, groupSegments, menuRoutes, type MenuNode } from "./menu-tree";
import type { UIPage } from "../types";

const page = (id: string, group?: string, order = 0): UIPage => ({
  id, title: id, route: `/${id}`, renderer: "generic", group, order,
});

describe("menu group paths", () => {
  it("normalizes '/'-separated segments defensively", () => {
    expect(groupSegments(undefined)).toEqual([]);
    expect(groupSegments("")).toEqual([]);
    expect(groupSegments(" 运维 / 监控 / ")).toEqual(["运维", "监控"]);
    expect(groupSegments("////")).toEqual([]);
    expect(groupSegments("a/b/c/d/e/f/g/h/i/j")).toHaveLength(8);
  });
});

describe("buildMenuTree", () => {
  it("keeps ungrouped pages as top-level items in order", () => {
    const tree = buildMenuTree([page("b", undefined, 2), page("a", undefined, 1)]);
    expect(tree.map((n) => n.key)).toEqual(["/a", "/b"]);
    expect(menuRoutes(tree)).toEqual(["/a", "/b"]);
  });

  it("nests shared prefixes into multi-level SubMenus", () => {
    const tree = buildMenuTree([
      page("daily", "运维/监控", 2),
      page("alerts", "运维/监控", 1),
      page("settings", "运维", 3),
    ]);
    expect(tree).toHaveLength(1);
    const ops = tree[0];
    expect(ops.key).toBe("group:运维");
    expect(ops.children!.map((n) => n.key)).toEqual(["group:运维/监控", "/settings"]);
    const monitor = ops.children![0];
    expect(monitor.key).toBe("group:运维/监控");
    expect(monitor.children!.map((n) => n.key)).toEqual(["/alerts", "/daily"]);
  });

  it("groups come before leaves at the same level", () => {
    const tree = buildMenuTree([
      page("top", undefined, 99),
      page("nested", "运维", 1),
    ]);
    expect(tree[0].key).toBe("group:运维");
    expect(tree[1].key).toBe("/top");
  });

  it("keeps every page reachable exactly once (no lost routes)", () => {
    const pages = [
      page("a", "x/y"), page("b", "x/y"), page("c", "x"), page("d"), page("e", "z"),
    ];
    const tree = buildMenuTree(pages);
    expect(menuRoutes(tree).sort()).toEqual(pages.map((p) => p.route).sort());
  });

  it("handles empty and malformed input without throwing", () => {
    expect(buildMenuTree([])).toEqual([]);
    const tree = buildMenuTree([page("a", "///")]);
    expect(tree.map((n) => n.key)).toEqual(["/a"]);
  });
});
