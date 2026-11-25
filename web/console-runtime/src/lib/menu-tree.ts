// Pure UIPage[] → hierarchical menu tree logic. Kept free of React/antd so it
// is node-testable; the shell (views/App.tsx) only maps these nodes onto antd
// Menu items. A page's optional `group` is a '/'-separated path; pages sharing
// a prefix are nested under group nodes. Pages without a group stay top-level.
import type { UIPage } from "../types";

export interface MenuNode {
  /** Stable key: route for leaf pages, "group:<path>" for groups. */
  key: string;
  label: string;
  icon?: string;
  route?: string;
  children?: MenuNode[];
  /** Sort hint carried from the page's order (groups use the min child order). */
  order: number;
}

/** Defensive group-path normalization: split on '/', trim, drop empties,
 * cap depth. Returns [] for null/empty — a top-level page. */
export function groupSegments(group: string | undefined): string[] {
  if (!group || typeof group !== "string") return [];
  return group.split("/")
    .map((segment) => segment.trim())
    .filter((segment) => segment.length > 0)
    .slice(0, 8);
}

/**
 * Build the menu tree for the given pages. Ordering:
 *  - leaf pages by their `order` (missing = 0), then id for stable ties;
 *  - group nodes before leaves when both share a level (groups first), then
 *    by their own first-child order and label.
 */
export function buildMenuTree(pages: UIPage[]): MenuNode[] {
  const roots: MenuNode[] = [];
  const groups = new Map<string, MenuNode>();

  const pageOrder = (page: UIPage): number =>
    typeof page.order === "number" ? page.order : 0;

  for (const page of pages) {
    const path = groupSegments(page.group);
    const leaf: MenuNode = {
      key: page.route,
      label: page.title,
      icon: page.icon,
      route: page.route,
      order: pageOrder(page),
    };

    if (path.length === 0) {
      roots.push(leaf);
      continue;
    }

    let siblings = roots;
    let prefix = "";
    for (let depth = 0; depth < path.length; depth++) {
      prefix = prefix ? `${prefix}/${path[depth]}` : path[depth];
      let group = groups.get(prefix);
      if (!group) {
        group = {
          key: `group:${prefix}`,
          label: path[depth],
          order: leaf.order,
          children: [],
        };
        groups.set(prefix, group);
        siblings.push(group);
      }
      siblings = group.children!;
    }
    siblings.push(leaf);
  }

  const sort = (nodes: MenuNode[]): MenuNode[] => {
    nodes.sort((a, b) => {
      const aGroup = a.children !== undefined;
      const bGroup = b.children !== undefined;
      if (aGroup !== bGroup) return aGroup ? -1 : 1; // groups first
      if (a.order !== b.order) return a.order - b.order;
      return a.label.localeCompare(b.label);
    });
    for (const node of nodes) {
      if (node.children) {
        node.children = sort(node.children);
        node.order = node.children[0]?.order ?? node.order;
      }
    }
    return nodes;
  };

  return sort(roots);
}

/** Flatten menu leaves to their routes (used for grouping count / tests). */
export function menuRoutes(nodes: MenuNode[]): string[] {
  const out: string[] = [];
  for (const node of nodes) {
    if (node.route) out.push(node.route);
    if (node.children) out.push(...menuRoutes(node.children));
  }
  return out;
}
