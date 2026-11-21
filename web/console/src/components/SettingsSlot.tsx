// SettingsSlot: renders a plugin's registered settings UI when one exists
// (the Settings extension point); otherwise falls back to the generic
// structured config editor. The registered component owns the editing UX;
// the slot owns the config round-trip (fetch + save), so every settings UI
// stays a pure view of its plugin's config — persistence never forks.
import React, { useCallback, useEffect, useMemo, useState, useSyncExternalStore } from "react";
import { api } from "../api";
import { getSettings, rendererRegistryVersion, subscribeRenderers } from "../views/registry";
import { ConfigEditor } from "./ConfigEditor";

export function SettingsSlot({ pluginId, componentType }: { pluginId: string; componentType?: string }) {
  // A settings UI registered after this slot mounted (late module import,
  // freshly enabled plugin) must swap in reactively, same as renderers.
  const settingsVersion = useSyncExternalStore(subscribeRenderers, rendererRegistryVersion);
  const Component = useMemo(() => getSettings(pluginId), [pluginId, settingsVersion]);
  const [config, setConfig] = useState<Record<string, unknown> | null>(null);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!Component) return; // the legacy editor fetches on its own
    let alive = true;
    setLoaded(false);
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
  }, [pluginId, Component]);

  const onSaved = useCallback(
    async (cfg: Record<string, unknown>) => {
      await api.setPluginConfig(pluginId, cfg);
      setConfig(cfg);
    },
    [pluginId]
  );

  if (!Component) return <ConfigEditor key={pluginId} pluginId={pluginId} componentType={componentType} />;
  return loaded ? <Component key={pluginId} pluginId={pluginId} config={config} onSaved={onSaved} /> : null;
}
