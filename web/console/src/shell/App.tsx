import React, { Suspense, lazy, useCallback, useEffect, useMemo, useState } from "react";
import "../styles.css";
import { Badge, Button, Layout, Menu, Space, Switch, Tabs, Typography } from "antd";
import {
  ApiOutlined, BellOutlined, BlockOutlined, DashboardOutlined, DatabaseOutlined,
  FileTextOutlined, FolderOutlined, ProfileOutlined, SearchOutlined,
} from "@ant-design/icons";
import { api } from "../api";
import { loadClientModules } from "../lib/client-modules";
import { useObservationGeneration, useStreamStatus, onObservation } from "../stream";
import { useDomainVersion } from "../lib/projections";
import { usePath, navigate } from "../router";
import { useTheme } from "../theme";
import { GenericPage } from "../views/GenericPage";
import { ViewBlockRenderer, type ViewContext } from "../views/blocks";
import { getPageRenderer, getPanelRenderer, registerPageRenderer, registerPanelRenderer } from "../views/registry";
import type { UIPage, UIPanel } from "../types";

const { Sider, Content } = Layout;

// Console infrastructure renderers register exactly like third-party ones —
// through the keyed registry, not a switch statement.
registerPageRenderer("plugin-explorer", function PluginExplorerPage() {
  return <Lazy><PluginExplorerLazy /></Lazy>;
});
registerPanelRenderer("event-feed", function EventFeedPanel({ generation }: { generation: number }) {
  return <Lazy><EventFeedLazy generation={generation} /></Lazy>;
});

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
  useEffect(() => {
    refresh();
    // A newly installed ui-client component must load without a manual
    // reload; already-loaded modules are skipped by the loader.
    void loadClientModules();
    return onObservation(() => undefined);
  }, [refresh, compositionVersion]);

  const active = useMemo(() => pages.find((p) => p.route === path), [pages, path]);
  useEffect(() => {
    if (!pages.length) return;
    if (!pages.some((p) => p.route === path)) {
      // Unknown route: render the not-found state instead of forcing the
      // first page on the user.
      return;
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
    [hubCommand, focus, busy, generation]
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
            <Typography.Text type="secondary">
              渲染器 “{page.renderer}” 未在控制台注册，且页面未声明 views。
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
    return <Typography.Text type="secondary">渲染器 “{panel.renderer}” 未注册。</Typography.Text>;
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
            items={pages.map((p) => ({ key: p.route, icon: pageIcon(p.icon), label: p.title }))}
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
          {active ? (
            renderPage(active)
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

const PluginExplorerLazy = lazy(() =>
  import("../components/Explorer").then((m) => ({ default: m.PluginExplorer }))
);
const EventFeedLazy = lazy(() => import("../components/EventFeed").then((m) => ({ default: m.EventFeed })));

function Lazy({ children }: { children: React.ReactNode }) {
  return <Suspense fallback={<div style={{ padding: 16, color: "#8a93a6" }}>加载中…</div>}>{children}</Suspense>;
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
