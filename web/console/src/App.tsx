import { useEffect, useState } from "react";
import { Layout, Menu, Badge } from "antd";
import { api } from "./api";

interface Page { id: string; title: string; route: string; renderer: string; views?: any[] }
interface Panel { id: string; title: string; position: string; renderer: string; pages?: string[] }

export default function App() {
  const [pages, setPages] = useState<Page[]>([]);
  const [panels, setPanels] = useState<Panel[]>([]);
  const [route, setRoute] = useState(location.pathname);
  const [dark, setDark] = useState(() => localStorage.getItem("gc-theme") !== "light");
  const [live, setLive] = useState(false);

  useEffect(() => {
    fetch("/api/ui/pages").then(r => r.json()).then(d => setPages(d.data?.pages ?? [])).catch(() => {});
    fetch("/api/ui/panels").then(r => r.json()).then(d => setPanels(d.data?.panels ?? [])).catch(() => {});
  }, []);

  useEffect(() => {
    const es = new EventSource("/api/stream");
    es.onopen = () => setLive(true);
    es.onerror = () => setLive(false);
    return () => es.close();
  }, []);

  const active = pages.find(p => p.route === route) ?? pages[0];
  const dark = localStorage.getItem("gc-theme") !== "light";
  const bg = dark ? "#0b0d12" : "#f5f6f8";
  const borderC = dark ? "#232733" : "#e5e7eb";
  const textC = dark ? "#e8ebf3" : "#1a1d23";
  const subC = dark ? "#8a93a6" : "#6b7280";

  const rightPanels = panels.filter(p => p.position === "right" && (!p.pages?.length || p.pages.includes(active?.id ?? "")));
  const bottomPanels = panels.filter(p => p.position !== "right" && (!p.pages?.length || p.pages.includes(active?.id ?? "")));

  return (
    <Layout style={{ minHeight: "100vh", background: bg, color: textC }}>
      <Sider width={200} style={{ position: "sticky", top: 0, height: "100vh", overflow: "auto", background: siderBg, borderRight: `1px solid ${borderC}` }}>
        <div style={{ padding: "18px 16px 14px", fontWeight: 700, fontSize: 14 }}>gocordis</div>
        <Menu theme={dark ? "dark" : "light"} mode="inline" style={{ borderInlineEnd: "none", background: "transparent" }}
          selectedKeys={active ? [active.route] : []}
          items={pages.map(p => ({ key: p.route, label: p.title }))}
          onClick={({ key }) => navigate(key)} />
        <div style={{ padding: "14px 16px", fontSize: 11, color: subC }}>
          <Badge status={live ? "processing" : "warning"} text={live ? "观察流在线" : "重连中"} />
        </div>
      </Sider>
      <Layout>
        <Content style={{ padding: 24, minWidth: 0 }}>
          <h1 style={{ fontSize: 20, margin: "0 0 16px" }}>{active?.title ?? ""}</h1>
          <SchemaViews views={active?.views ?? []} dark={dark} />
        </Content>
      </Layout>
      {rightPanels.length > 0 && (
        <div style={{ width: 320, padding: "20px 0", display: "flex", flexDirection: "column", gap: 14 }}>
          {rightPanels.map((p, i) => <SchemaPanel key={p.id} panel={p} dark={dark} />)}
        </div>
      )}
      {bottomPanels.length > 0 && (
        <div style={{ padding: "0 24px 24px" }}>
          {bottomPanels.map((p, i) => <SchemaPanel key={p.id} panel={p} dark={dark} />)}
        </div>
      )}
    </Layout>
  );

  function SchemaViews({ views, dark }: { views: ViewBlock[]; dark: boolean }) {
    return <>{views.map((v, i) => <ViewBlock key={i} block={v} dark={dark} />)}</>;
  }

  function SchemaPanel({ panel, dark }: { panel: any; dark: boolean }) {
    return null;
  }

  function ViewBlock({ block, dark }: { block: any; dark: boolean }) {
    const [data, setData] = useState<any>(null);
    const [err, setErr] = useState("");
    const [loading, setLoading] = useState(true);
    const params = block.params ? "?" + Object.entries(block.params).map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`).join("&") : "";
    useEffect(() => {
      fetch(`/api/query/${block.query}${params}`).then(r => r.json()).then(d => { setData(d.data); setLoading(false); }).catch(() => setLoading(false));
    }, [block.query, params]);
    if (loading) return <p style={{ color: subC, padding: 8 }}>加载中…</p>;
    if (err) return <p style={{ color: "#f0655a", padding: 8 }}>{err}</p>;
    if (!data || (Array.isArray(data) && !data.length)) return <p style={{ color: subC, padding: 8 }}>暂无数据。</p>;
    const cols = block.columns ?? Object.keys(Array.isArray(data) ? data[0] ?? {} : data);
    return (
      <div style={{ overflowX: "auto" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 13 }}>
          <thead><tr>{cols.map((c: any) => <th key={c.key ?? c} style={{ textAlign: "left", padding: "6px 10px", borderBottom: `1px solid ${borderC}`, fontWeight: 600, fontSize: 12 }}>{c.title ?? c.key ?? c}</th>)}</tr></thead>
          <tbody>{(Array.isArray(data) ? data : [data]).map((row: any, i: number) => (
            <tr key={i}>{cols.map((c: any) => <td key={c.key ?? c} style={{ padding: "6px 10px", borderBottom: `1px solid ${isDark ? "#1a1e28" : "#eee"}`, fontSize: 12 }}>{String(row[c.key ?? c] ?? "—")}</td>)}</tr>
          ))}</tbody>
        </table>
      </div>
    );
  }
}
