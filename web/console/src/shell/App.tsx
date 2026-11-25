import React, { useCallback, useEffect, useMemo, useState, useSyncExternalStore } from "react";
import "../styles.css";
import { Badge, Button, Layout, Menu, Space, Switch, Tabs, Typography } from "antd";
import {
  ApiOutlined, BellOutlined, BlockOutlined, DashboardOutlined, DatabaseOutlined,
  FileTextOutlined, FolderOutlined, ProfileOutlined, SearchOutlined,
} from "@ant-design/icons";
import { Alert } from "antd";
import {
  api,
  reconcileClientModules,
  subscribeClientModulesState,
  clientModuleManagerState,
  useObservationGeneration,
  useStreamStatus,
  onObservation,
  useDomainVersion,
  buildMenuTree,
  GenericPage,
  ViewBlockRenderer,
  getPageRenderer,
  getPanelRenderer,
  rendererRegistryVersion,
  subscribeRenderers,
} from "@gocordis/console-runtime";
import type { ViewContext, UIPage, UIPanel, MenuNode } from "@gocordis/console-runtime";
import { usePath, navigate, replace } from "../router";
import { useTheme } from "../theme";
import { ActionBar } from "./ActionBar";

const { Sider, Content } = Layout;

// Product renderers (plugin-explorer, event-feed, dashboards, graphs) are not
// registered here. They arrive as plugin client modules through the shared
// extension registry, exactly like third-party pages and blocks.

// Pages declare their menu icon by name from this curated set; the shell
// never keys icons off application routes.
const PAGE_ICONS: Record<string, React.ReactNode> = {
  dashboard: <DashboardOutlined />,
  profile: <ProfileOutlined />,
  folder: <FolderOutlined />,
  api: <ApiOutlined />,
  search: <SearchOutlined />,
  block: <BlockOutlined />,
  database: <DatabaseOutlined />,
  bell: <BellOutlined />,
  file: <FileTextOutlined />,
};

function pageIcon(name?: string): React.ReactNode {
  return (name && PAGE_ICONS[name]) || <FileTextOutlined />;
}

// Map the renderer-agnostic menu tree onto antd's nested item shape.
function toMenuItems(nodes: MenuNode[]): any[] {
  return nodes.map((node) => ({
    key: node.key,
    icon: node.icon ? pageIcon(node.icon) : undefined,
    label: node.label,
    children: node.children ? toMenuItems(node.children) : undefined,
  }));
}

// Console shell — the second Cordis application. It renders whatever the
// composition declares: pages are declarative view stacks, panels bind to
// pages, and the only built-in renderers are console infrastructure
// (plugin-explorer, event-feed). Applications ship zero frontend code.
export default function App() {
  const [pages, setPages] = useState<UIPage[]>([]);
  const [panels, setPanels] = useState<UIPanel[]>([]);
  const [focus, setFocus] = useState<{ sourceId: string; date: string } | null>(() => {
    try {
      const raw = sessionStorage.getItem("console-focus");
      return raw ? (JSON.parse(raw) as { sourceId: string; date: string }) : null;
    } catch {
      return null;
    }
  });
  const [busy, setBusy] = useState(false);
  const [hubError, setHubError] = useState<string | null>(null);
  const generation = useObservationGeneration();
  // Renderer availability is a dependency, not a race: when a client module
  // finishes registering after the pages already rendered (late import), the
  // version bump re-renders and the lookup below resolves.
  const rendererVersion = useSyncExternalStore(subscribeRenderers, rendererRegistryVersion);
  // An honest in-between: composition already references a renderer whose
  // client module is still being imported/registered. During a reconcile a
  // missing renderer means "loading", never "broken" — the error text is
  // reserved for a settled module set (paper 铁律 4: not-ready ≠ failed).
  const modulesReconciling = useSyncExternalStore(
    subscribeClientModulesState,
    () => clientModuleManagerState().reconciling
  );
  const stream = useStreamStatus();
  const path = usePath();
  const { isDark, toggle } = useTheme();

  const refresh = useCallback(() => {
    api.pages().then(setPages).catch((e) => setHubError(e instanceof Error ? e.message : String(e)));
    api.panels().then(setPanels).catch(() => setPanels([]));
  }, []);

  // Composition projection interest: pages/panels re-fetch only when a
  // composition.* observation moves the composition domain — not on every
  // file event.
  const compositionVersion = useDomainVersion("composition");
  // Plugin control-plane events (SSE `plugin-state`, folded into the `plugin`
  // domain by projections) are a declaration change: enabling a UI plugin can
  // add/remove pages, panels and client modules. Re-read composition and
  // reconcile modules on that domain too — not only on observations.
  const pluginVersion = useDomainVersion("plugin");
  useEffect(() => {
    refresh();
    // A ui-client component appearing, disappearing (disable/uninstall) or
    // changing content (?v= revision) in the manifest reconciles without a
    // manual reload: new modules register, departed ones dispose their
    // renderers, changed ones re-import. Unchanged modules are left alone.
    void reconcileClientModules();
    return onObservation(() => undefined);
  }, [refresh, compositionVersion, pluginVersion]);

  const active = useMemo(() => pages.find((p) => p.route === path), [pages, path]);
  // "/" 不是任何页面的路由：规范化到组合序第一页（服务端按 order 排序
  // 返回，首页即 pages[0]）。replace 不留历史项——回退键不会把用户送回
  // 无意义的根地址。
  useEffect(() => {
    if (pages.length && path === "/") {
      replace((pages.find((page) => page.home) ?? pages[0]).route);
    }
  }, [pages, path]);

  // Command dispatch: mark busy, accept, then let observations invalidate.
  const hubCommand = useCallback(async (name: string, body: unknown) => {
    setBusy(true);
    setHubError(null);
    try {
      await api.hubCommand(name, body);
    } catch (e) {
      setHubError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }, []);

  const ctx: ViewContext = useMemo(
    () => ({
      hubQuery: api.hubQuery,
      hubCommand,
      navigate,
      focus,
      onFocus: (sourceId, date) => {
        const next = { sourceId, date };
        setFocus(next);
        try {
          sessionStorage.setItem("console-focus", JSON.stringify(next));
        } catch {
          /* storage unavailable: focus stays in memory */
        }
      },
      busy,
      generation,
    }),
    [hubCommand, focus, busy, generation, rendererVersion]
  );

  const visibleOn = (list: UIPanel[]) =>
    list.filter((p) => !p.pages?.length || (active && p.pages.includes(active.id)));
  const rightPanels = visibleOn(panels.filter((p) => p.position === "right"));
  const bottomPanels = visibleOn(panels.filter((p) => p.position !== "right"));

  const streamLabel = stream === "live" ? "观察流在线" : stream === "connecting" ? "重连中" : "离线";
  const streamStatus = stream === "live" ? "success" : stream === "connecting" ? "warning" : "error";

  const renderPage = (page: UIPage) => {
    if (page.views?.length) {
      return (
        <section className="card" style={{ padding: "20px 22px" }}>
          <GenericPage
            title={page.title}
            description={page.description}
            views={page.views}
            actions={page.actions ?? []}
            ctx={ctx}
          />
        </section>
      );
    }
    if (page.view) {
      const block = { kind: "table", ...(page.view as object) } as never;
      return (
        <section className="card" style={{ padding: "20px 22px" }}>
          <h1 style={{ margin: 0, fontSize: 22 }}>{page.title}</h1>
          {page.description && <p style={{ color: "#8a93a6" }}>{page.description}</p>}
          <ViewBlockRenderer block={block} ctx={ctx} />
        </section>
      );
    }
    switch (page.renderer) {
      default: {
        const Renderer = getPageRenderer(page.renderer);
        if (Renderer) return <Renderer page={page} ctx={ctx} />;
        return (
          <section className="card" style={{ padding: 16 }}>
            <Typography.Text type={modulesReconciling ? "secondary" : "warning"}>
              {modulesReconciling
                ? `插件模块加载中，渲染器 “${page.renderer}” 即将就绪…`
                : `渲染器 “${page.renderer}” 未在控制台注册，且页面未声明 views。`}
            </Typography.Text>
          </section>
        );
      }
    }
  };

  const renderPanelBody = (panel: UIPanel) => {
    if (panel.views?.length) {
      return (
        <>
          {panel.views.map((v, i) => (
            <div key={i} style={{ marginBottom: 12 }}>
              <ViewBlockRenderer block={v} ctx={ctx} />
            </div>
          ))}
        </>
      );
    }
    const Renderer = getPanelRenderer(panel.renderer);
    if (Renderer) return <Renderer panel={panel} ctx={ctx} generation={generation} />;
    return (
      <Typography.Text type={modulesReconciling ? "secondary" : "warning"}>
        {modulesReconciling
          ? `插件模块加载中，渲染器 “${panel.renderer}” 即将就绪…`
          : `渲染器 “${panel.renderer}” 未注册。`}
      </Typography.Text>
    );
  };

  return (
    <Layout style={{ minHeight: "100vh" }}>
      <Sider width={220} theme="dark" style={{ position: "sticky", top: 0, height: "100vh", overflow: "auto" }}>
        <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10, padding: "18px 16px 14px" }}>
            <div style={{
              width: 30, height: 30, borderRadius: 9, display: "grid", placeItems: "center",
              background: "linear-gradient(135deg, #4d6bfe, #7b5bff)", color: "#fff", fontWeight: 700, fontSize: 14,
            }}>C</div>
            <div>
              <Typography.Text strong style={{ display: "block", fontSize: 13, color: "#e8ebf3" }}>cordis console</Typography.Text>
              <Typography.Text style={{ display: "block", fontSize: 11, fontFamily: "monospace", color: "#626b80" }}>gocordis</Typography.Text>
            </div>
          </div>
          <Menu
            theme="dark" mode="inline"
            selectedKeys={active ? [active.route] : []}
            items={toMenuItems(buildMenuTree(pages))}
            onClick={({ key }) => navigate(key)}
          />
          <div style={{ marginTop: "auto", padding: "14px 16px", display: "flex", flexDirection: "column", gap: 8 }}>
            <Badge status={streamStatus} text={<span style={{ fontSize: 12, color: "#99a2b6" }}>{streamLabel}</span>} />
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <span style={{ fontSize: 12, color: "#626b80" }}>暗色主题</span>
              <Switch size="small" aria-label="切换暗色主题" checked={isDark} onChange={toggle} />
            </div>
            <Typography.Text type="secondary" style={{ fontSize: 11 }}>
              {pages.length} 页 · {panels.length} 板
            </Typography.Text>
          </div>
        </div>
      </Sider>
      <Layout hasSider={rightPanels.length > 0}>
        <Content style={{ padding: 20, minWidth: 0 }}>
          {hubError && (
            <Typography.Paragraph type="danger" style={{ marginBottom: 12 }}>{hubError}</Typography.Paragraph>
          )}
          <ActionBar ctx={ctx} page={active ?? null} />
          {active ? (
            <div key={path} className="route-enter">{renderPage(active)}</div>
          ) : pages.length ? (
            <NotFound route={path} />
          ) : (
            <Typography.Text type="secondary">尚未组合任何页面。</Typography.Text>
          )}
          {bottomPanels.length > 0 && (
            <div style={{ marginTop: 18 }}>
              <Tabs
                type="line" size="small"
                items={bottomPanels.map((p) => ({
                  key: p.id,
                  label: p.title,
                  children: <section className="card rail-card" style={{ padding: 12 }}>{renderPanelBody(p)}</section>,
                }))}
              />
            </div>
          )}
        </Content>
        {rightPanels.length > 0 && (
          <aside style={{ width: 320, padding: "20px 0", display: "flex", flexDirection: "column", gap: 14, paddingRight: 20 }}>
            {rightPanels.map((p) => (
              <section key={p.id} className="card rail-card" aria-label={p.title} style={{ padding: 12 }}>
                <h3 style={{ margin: "0 0 10px", fontSize: 14 }}>{p.title}</h3>
                {renderPanelBody(p)}
              </section>
            ))}
          </aside>
        )}
      </Layout>
    </Layout>
  );
}


function NotFound({ route }: { route: string }) {
  return (
    <section className="card" style={{ padding: "40px 24px", textAlign: "center" }}>
      <Typography.Title level={3} style={{ marginTop: 0 }}>页面不存在</Typography.Title>
      <Typography.Paragraph type="secondary">
        路由 <Typography.Text code>{route}</Typography.Text> 未被当前组合声明。
      </Typography.Paragraph>
      <Button type="primary" onClick={() => { history.pushState({}, "", "/"); window.dispatchEvent(new PopStateEvent("popstate")); }}>
        返回概览
      </Button>
    </section>
  );
}
