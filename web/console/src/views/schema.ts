// Declarative view schema: a page/panel is an ordered list of view blocks.
// Plugins contribute data (TOML/JSON declarations); the generic renderers
// interpret them. A new kind is a new renderer registration — plugins never
// ship frontend code.

export interface ViewBlock {
  kind: string;
  title?: string;
  query?: string;
  params?: Record<string, string>;
  columns?: Array<{ key: string; title: string; format?: string }>;
  rowActions?: Array<{ label: string; command: string; args?: Record<string, string> }>;
  items?: Array<{
    label: string;
    query?: string;
    op?: "count" | "sum";
    field?: string;
    warn?: boolean;
    filter?: { key: string; in: string[] };
  }>;
  dateKey?: string;
  series?: Array<{ key: string; label: string }>;
  fields?: Array<{ key: string; label: string }>;
  filters?: Array<{
    key: string;
    label: string;
    /** Initial value; "$today" / "$yesterday" resolve at render time. */
    default?: string;
    optionsQuery?: string;
    optionKey?: string;
    optionLabel?: string;
    type?: "date";
    required?: boolean;
  }>;
  titleKey?: string;
  pageSize?: number;
  selectFocus?: boolean;
  expand?: string;
  /** Client-side row filter after fetch: exact membership on one field.
   * Lets one query serve several presentations (e.g. "needs attention")
   * without a new backend query per slice. */
  filter?: { key: string; in: string[] };
  /** master-detail: nested views rendered beside the list, with the
   * selected row exposed as $focus (sourceId = row[focusKey]). */
  detailViews?: ViewBlock[];
  focusKey?: string;
  /** Interest domain for invalidation: "collection" (default), "composition",
   * "source:<id>", or "all". The block re-queries only when this moves. */
  domain?: string;
}

export interface PageAction {
  label: string;
  command: string;
  datePicker?: boolean;
}

export interface Focus {
  sourceId: string;
  date: string;
}

// $focus.sourceId / $focus.date resolve from the shell's selection state.
// Unresolved references omit the key so dependent views stay dormant until
// a row is selected.
// resolveParams 把 $focus 引用解析为查询参数。任一 $focus 引用无法解析
//（未选行，或选了源但没选日期）时返回 null——依赖方保持休眠，绝不带
// 半截参数发查询（空 date 会被后端判 invalid_request）。
export function resolveParams(
  params: Record<string, string> | undefined,
  focus: Focus | null
): Record<string, string> | null {
  const out: Record<string, string> = {};
  let needsFocus = false;
  for (const [k, v] of Object.entries(params ?? {})) {
    if (v === "$focus.sourceId") {
      needsFocus = true;
      if (!focus || !focus.sourceId) return null;
      out[k] = focus.sourceId;
    } else if (v === "$focus.date") {
      needsFocus = true;
      if (!focus || !focus.date) return null;
      out[k] = focus.date;
    } else {
      out[k] = v;
    }
  }
  return out;
}

export function needsFocus(params: Record<string, string> | undefined): boolean {
  return Object.values(params ?? {}).some((v) => v.startsWith("$focus."));
}

// "{a}/{b}" column templates interpolate row fields.
export function formatRow(format: string, row: Record<string, unknown>): string {
  return format.replace(/\{(\w+)\}/g, (_, key: string) =>
    row[key] === undefined || row[key] === null ? "—" : String(row[key])
  );
}
