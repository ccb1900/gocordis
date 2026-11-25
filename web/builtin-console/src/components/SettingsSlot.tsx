// SettingsSlot: renders a plugin's registered settings UI when one exists
// (the Settings extension point); otherwise falls back to the generic
// structured config editor. The registered component owns the editing UX;
// the slot owns the config round-trip, so every settings UI stays a pure view
// of its plugin's config — persistence never forks. Mutations now go through
// the hub's POST /api/plugins/{id}/config and report `persisted` explicitly.

import React, { useCallback, useEffect, useMemo, useState, useSyncExternalStore } from "react";
import { Alert, Button, Tag } from "antd";
import { facade } from "../facade";
import { errorCodeOf, mutationMessage } from "../lib/plugin-control";
import { facade } from "../facade";
import { ConfigEditor } from "./ConfigEditor";

interface SaveStatus {
  persisted: boolean | null;
  code?: string;
  error?: string;
}

export function SettingsSlot({ pluginId, componentType }: { pluginId: string; componentType?: string }) {
  // A settings UI registered after this slot mounted (late module import,
  // freshly enabled plugin) must swap in reactively, same as renderers.
  const settingsVersion = useSyncExternalStore(facade().subscribeRenderers, facade().rendererRegistryVersion);
  const Component = useMemo(() => facade().getSettings(pluginId) as never, [pluginId, settingsVersion]);
  const [config, setConfig] = useState<Record<string, unknown> | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [saveStatus, setSaveStatus] = useState<SaveStatus | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    if (!Component) return; // the legacy editor fetches on its own
    let alive = true;
    setLoaded(false);
    setSaveStatus(null);
    api
      .pluginConfig(pluginId)
      .then((cfg) => {
        if (alive) {
          setConfig(cfg ?? null);
          setLoaded(true);
        }
      })
      .catch(() => {
        if (alive) {
          setConfig(null);
          setLoaded(true);
        }
      });
    return () => {
      alive = false;
    };
  }, [pluginId, Component, reloadKey]);

  const onSaved = useCallback(
    async (cfg: Record<string, unknown>) => {
      try {
        const result = await facade().api.savePluginConfig(pluginId, cfg);
        if (!result.accepted) {
          const message = mutationMessage(result, "配置保存未接受");
          setSaveStatus({ persisted: result.persisted ?? null, code: result.code, error: message });
          // Settings components await onSaved to surface failure themselves.
          const err = new Error(message) as Error & { code?: string };
          err.code = result.code;
          throw err;
        }
        setSaveStatus({ persisted: result.persisted ?? null });
        setConfig(cfg);
      } catch (e) {
        const code = errorCodeOf(e);
        setSaveStatus({ persisted: null, code, error: e instanceof Error ? e.message : String(e) });
        throw e;
      }
    },
    [pluginId]
  );

  const saveHint =
    saveStatus?.code === "invalid_config" ? "配置校验失败：请修正字段后重新保存。"
    : saveStatus?.code === "conflict" ? "声明已被其他变更更新（conflict）：请重新加载后再保存。"
    : undefined;

  if (!Component) return <ConfigEditor key={pluginId} pluginId={pluginId} componentType={componentType} />;
  return loaded ? (
    <>
      <Component key={pluginId} pluginId={pluginId} config={config} onSaved={onSaved} />
      {saveStatus?.error && (
        <Alert
          style={{ marginTop: 8 }}
          type={saveStatus.code ? "warning" : "error"}
          showIcon
          message={saveStatus.error}
          description={saveHint}
          action={saveStatus.code === "conflict" ? (
            <Button size="small" onClick={() => setReloadKey((k) => k + 1)}>重新加载</Button>
          ) : undefined}
        />
      )}
      {saveStatus && !saveStatus.error && (
        <div style={{ marginTop: 8 }}>
          <Tag color={saveStatus.persisted ? "success" : "warning"}>
            {saveStatus.persisted ? "已持久化（期望态覆盖）" : "未持久化（临时，调和会覆盖）"}
          </Tag>
        </div>
      )}
    </>
  ) : null;
}
