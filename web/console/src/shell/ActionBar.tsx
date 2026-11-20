// ActionBar surfaces the Action extension point: plugin-registered context
// operations, filtered by the live context, in one shell-level dropdown above
// the page card. No matching actions — no bar; the shell stays clean.
import React, { useMemo, useSyncExternalStore } from "react";
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
  const matched = useMemo(() => {
    void version; // re-filter when the action set changes (register/dispose)
    const cx: ActionContext = { ctx, page };
    return listActions().filter((a) => !a.when || a.when(cx));
  }, [ctx, page, version]);

  if (matched.length === 0) return null;
  return (
    <div style={{ display: "flex", justifyContent: "flex-end", marginBottom: 12 }}>
      <Dropdown
        menu={{
          items: matched.map((a) => ({ key: a.id, label: a.title })),
          onClick: ({ key }) => {
            const action = matched.find((a) => a.id === key);
            if (action) void action.run({ ctx, page });
          },
        }}
        trigger={["click"]}
      >
        <Button size="small">上下文操作</Button>
      </Dropdown>
    </div>
  );
}
