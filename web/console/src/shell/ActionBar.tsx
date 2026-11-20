// ActionBar surfaces the Action extension point: plugin-registered context
// operations, filtered by the live context, in one shell-level dropdown above
// the page card. No matching actions — no bar; the shell stays clean.
import React, { useMemo, useState, useSyncExternalStore } from "react";
import { Button, Dropdown } from "antd";
import {
  listActions,
  rendererRegistryVersion,
  subscribeRenderers,
  type ActionContext,
  type ViewContext,
} from "../views/registry";
import type { UIPage } from "../types";

export function ActionBar({ ctx, page }: { ctx: ViewContext; page: UIPage | null }) {
  const version = useSyncExternalStore(subscribeRenderers, rendererRegistryVersion);
  // P3 fix (2026-11 audit): the lifecycle doc promises the shell "tracks run
  // completion without blocking the UI" — `void action.run(...)` did neither
  // (a rejecting run became an unhandled rejection). Track per-action busy
  // state, surface it on the item, and contain rejections.
  const [busyIds, setBusyIds] = useState<ReadonlySet<string>>(new Set());
  const matched = useMemo(() => {
    void version; // re-filter when the action set changes (register/dispose)
    const cx: ActionContext = { ctx, page };
    return listActions().filter((a) => !a.when || a.when(cx));
  }, [ctx, page, version]);

  const runAction = (action: { id: string; run: (cx: ActionContext) => void | Promise<void> }) => {
    if (busyIds.has(action.id)) return; // one in-flight run per action
    setBusyIds((prev) => new Set(prev).add(action.id));
    void Promise.resolve()
      .then(() => action.run({ ctx, page }))
      .catch((e) => console.warn(`[console] action "${action.id}" failed`, e))
      .finally(() => {
        setBusyIds((prev) => {
          const next = new Set(prev);
          next.delete(action.id);
          return next;
        });
      });
  };

  if (matched.length === 0) return null;
  return (
    <div style={{ display: "flex", justifyContent: "flex-end", marginBottom: 12 }}>
      <Dropdown
        menu={{
          items: matched.map((a) => ({
            key: a.id,
            label: busyIds.has(a.id) ? `${a.title} …` : a.title,
            disabled: busyIds.has(a.id),
          })),
          onClick: ({ key }) => {
            const action = matched.find((a) => a.id === key);
            if (action) runAction(action);
          },
        }}
        trigger={["click"]}
      >
        <Button size="small">上下文操作</Button>
      </Dropdown>
    </div>
  );
}
