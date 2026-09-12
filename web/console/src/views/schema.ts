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
  items?: Array<{ label: string; query?: string; op?: "count" | "sum"; field?: string; warn?: boolean }>;
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
export function resolveParams(
  params: Record<string, string> | undefined,
  focus: Focus | null
): Record<string, string> {
  const out: Record<string, string> = {};
  let needsFocus = false;
  for (const [k, v] of Object.entries(params ?? {})) {
    if (v === "$focus.sourceId") {
      needsFocus = true;
      if (focus) out[k] = focus.sourceId;
    } else if (v === "$focus.date") {
      needsFocus = true;
      if (focus) out[k] = focus.date;
    } else {
      out[k] = v;
    }
  }
  return needsFocus && !focus ? {} : out;
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
