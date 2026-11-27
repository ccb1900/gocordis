let Pr = null;
function ev(e) {
  Pr = e;
}
function ke() {
  if (!Pr && typeof globalThis < "u" && (Pr = globalThis.__CORDIS_CONSOLE ?? null), !Pr)
    throw new Error("[industrial-designer] console facade is not available");
  return Pr;
}
const p = (...e) => ke().jsxRuntime.jsx(...e), R = (...e) => ke().jsxRuntime.jsxs(...e), sr = (e) => ke().jsxRuntime.jsx(ke().jsxRuntime.Fragment, e), Ao = (...e) => ke().React.createElement(...e), j = (...e) => ke().React.useCallback(...e), ye = (...e) => ke().React.useEffect(...e), ot = (...e) => ke().React.useMemo(...e), Ce = (...e) => ke().React.useRef(...e), I = (...e) => ke().React.useState(...e), ce = (e) => {
  const r = (t) => {
    const n = e.split(".").reduce((a, i) => a == null ? void 0 : a[i], ke().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Ao(n, t ?? {});
  };
  return r.displayName = `facade.${e}`, r;
}, Re = ce("Alert"), $ = ce("Button"), lu = ce("Divider"), rv = ce("Drawer"), Ch = ce("Empty"), Oo = Object.assign(ce("Form"), {
  Item: ce("Form.Item"),
  useForm: (...e) => ke().antd.Form.useForm(...e)
}), Z = Object.assign(ce("Input"), {
  Number: ce("InputNumber"),
  TextArea: ce("Input.TextArea")
}), Fo = ce("InputNumber"), tv = Object.assign(ce("Layout"), {
  Header: ce("Layout.Header"),
  Content: ce("Layout.Content")
}), fr = ce("Modal"), Ie = ce("Select"), H = ce("Space"), Sh = ce("Spin"), ut = ce("Switch"), xe = ce("Table"), Me = ce("Tag"), G = Object.assign(ce("Typography"), {
  Title: ce("Typography.Title"),
  Text: ce("Typography.Text"),
  Paragraph: ce("Typography.Paragraph")
}), nv = new Proxy({}, {
  get: (e, r) => (...t) => {
    var a;
    const n = (a = ke().antd.message) == null ? void 0 : a[r];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${r} unavailable`);
    return n(...t);
  }
}), Th = 148, Rh = 46, du = {
  trigger: "#52c41a",
  Trigger: "#52c41a",
  filesystem: "#1677ff",
  Filesystem: "#1677ff",
  source: "#1677ff",
  parsing: "#722ed1",
  Parsing: "#722ed1",
  transform: "#fa8c16",
  Transform: "#fa8c16",
  delivery: "#13a8a8",
  Delivery: "#13a8a8",
  sink: "#13a8a8"
}, fu = (e) => du[e] ?? du[e.split(".")[0]] ?? "#4d6bfe";
function av(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function iv(e, r = /* @__PURE__ */ new Map(), t) {
  return {
    nodes: e.nodes.map((n) => {
      const a = r.get(n.type), i = (a == null ? void 0 : a.category) ?? "General", s = t === n.id;
      return {
        id: n.id,
        data: {
          label: (a == null ? void 0 : a.name) ?? n.type,
          nodeTypeId: n.type,
          category: i,
          icon: (a == null ? void 0 : a.icon) ?? "AppstoreOutlined",
          config: { ...n.config }
        },
        style: {
          x: n.x,
          y: n.y,
          fill: fu(i),
          stroke: s ? "#111111" : fu(i),
          lineWidth: s ? 3 : 1.5,
          radius: 10,
          port: !0,
          ports: [
            ...((a == null ? void 0 : a.inputs) ?? []).map((o, u, l) => ({
              key: `in:${o.name}`,
              placement: [0, (u + 1) / (l.length + 1)],
              r: 5,
              stroke: o.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((a == null ? void 0 : a.outputs) ?? []).map((o, u, l) => ({
              key: `out:${o.name}`,
              placement: [1, (u + 1) / (l.length + 1)],
              r: 5,
              stroke: o.type === "Control" ? "#52c41a" : "#1677ff",
              fill: "#fff"
            }))
          ]
        }
      };
    }),
    edges: e.edges.map((n) => ({
      id: av(n),
      source: n.source,
      sourcePort: `out:${n.sourcePort}`,
      target: n.target,
      targetPort: `in:${n.targetPort}`,
      data: {
        label: `${n.sourcePort} → ${n.targetPort}`,
        sourcePort: n.sourcePort,
        targetPort: n.targetPort
      },
      style: { stroke: "#8a93a6", lineWidth: 1.5, endArrow: !0 }
    }))
  };
}
function hu(e, r, t) {
  const n = t && t.length > 0 ? new Set(t) : null, a = {};
  for (const i of e.nodes) {
    if (!r.has(i.id) || n && !n.has(i.id)) continue;
    const s = i.style.x, o = i.style.y;
    typeof s == "number" && typeof o == "number" && (a[i.id] = { x: s, y: o });
  }
  return a;
}
function sv(e, r) {
  return {
    container: r,
    autoFit: "view",
    data: e,
    node: {
      type: "rect",
      style: {
        labelText: (t) => {
          var n;
          return ((n = t == null ? void 0 : t.data) == null ? void 0 : n.label) ?? "";
        },
        labelPlacement: "bottom",
        size: [Th, Rh],
        radius: 10,
        fill: "#f8fbff",
        stroke: "#4d6bfe",
        lineWidth: 1.5,
        labelFill: "#1f1f1f",
        labelFontSize: 11
      }
    },
    edge: {
      style: {
        labelText: (t) => {
          var n;
          return ((n = t == null ? void 0 : t.data) == null ? void 0 : n.label) ?? "";
        },
        labelFontSize: 10,
        stroke: "#8a93a6",
        endArrow: !0
      }
    },
    plugins: [
      { type: "grid-line", size: 20, stroke: "#e8edf4" },
      { type: "minimap", size: [180, 110], position: "right-bottom" },
      { type: "snapline", lineWidth: 1, stroke: "#4d6bfe" }
    ],
    behaviors: xh()
  };
}
const ov = "__industrial_drag_target__", uv = "__industrial_connect_source__", xt = (e, r) => {
  var t;
  return (((t = e.isPanModifier) == null ? void 0 : t.call(e)) ?? !1) || r.button === 1 || r.buttons === 4 || r.shiftKey === !0;
}, pu = (e, r) => {
  var c, d;
  if (r.targetType !== "node" || !((c = r.target) != null && c.id)) return !1;
  const t = ((d = e.gestureOrigin) == null ? void 0 : d.call(e)) ?? r.client ?? r.canvas;
  if (!t || !e.graph) return !1;
  const n = String(r.target.id), a = e.graph.getNodeData().find((h) => String(h.id) === n), i = a == null ? void 0 : a.style;
  if (typeof (i == null ? void 0 : i.x) != "number" || typeof i.y != "number") return !1;
  const [s, o] = e.graph.getCanvasByClient([t.x, t.y]), u = Math.abs(s - i.x), l = Math.abs(o - i.y);
  return Math.abs(u - Th / 2) <= 14 && l <= Rh / 2 + 10;
};
function xh(e = {}) {
  const r = e.connectionState ?? { armed: !1 };
  return [
    "zoom-canvas",
    {
      type: "click-select",
      multiple: !0,
      trigger: ["shift"],
      state: "__industrial_selected__"
    },
    {
      type: "brush-select",
      trigger: ["shift"],
      state: "__industrial_selected__",
      onSelect: (t) => {
        var n;
        return (n = e.onSelectIds) == null ? void 0 : n.call(e, Object.keys(t ?? {}));
      }
    },
    {
      type: "drag-canvas",
      enable: (t) => t.targetType === "canvas" || xt(e, t),
      range: 0.9
    },
    {
      type: "drag-element",
      state: ov,
      enable: (t) => (t.targetType === "node" || t.targetType === "combo") && !pu(e, t) && !xt(e, t),
      onFinish: (t) => {
        var s, o;
        const n = e.graph;
        if (!n) return;
        const a = Array.isArray(t) ? t : t ? [String(t)] : void 0, i = n.getNodeData().map((u) => ({
          id: String(u.id),
          data: u.data ?? {},
          style: u.style ?? {}
        }));
        (o = e.onPositions) == null || o.call(e, hu(
          { nodes: i },
          ((s = e.allowedIds) == null ? void 0 : s.call(e)) ?? /* @__PURE__ */ new Set(),
          a
        ));
      }
    },
    {
      type: "drag-element",
      state: "__industrial_selected__",
      enable: (t) => t.targetType === "node" && t.shiftKey === !0,
      onFinish: (t) => {
        var i;
        const n = e.graph;
        if (!n || !e.onPositions) return;
        const a = Array.isArray(t) ? t : t ? [String(t)] : [];
        e.onPositions(hu(
          { nodes: n.getNodeData().map((s) => ({
            id: String(s.id),
            data: s.data ?? {},
            style: s.style ?? {}
          })) },
          ((i = e.allowedIds) == null ? void 0 : i.call(e)) ?? /* @__PURE__ */ new Set(),
          a
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: uv,
      enable: (t) => xt(e, t) ? (r.armed = !1, !1) : r.armed ? t.targetType === "node" || t.targetType === "combo" : pu(e, t) ? (r.armed = !0, !0) : !1,
      onCreate: (t) => {
        var s;
        r.armed = !1;
        const n = String(t.source ?? ""), a = String(t.target ?? "");
        if (!n || !a || n === a) return;
        const i = (s = e.onCreateEdge) == null ? void 0 : s.call(e, n, a);
        if (i)
          return {
            ...t,
            sourcePort: `out:${i.sourcePort}`,
            targetPort: `in:${i.targetPort}`
          };
      }
    }
  ];
}
const cv = (e) => {
  var t;
  const r = e;
  return (r == null ? void 0 : r.targetType) === "node" ? (t = r.target) == null ? void 0 : t.id : void 0;
};
function lv(e) {
  const {
    definition: r,
    catalog: t,
    selectedId: n,
    onSelect: a,
    onDropNode: i,
    onCreateEdge: s,
    onPositions: o,
    onSelectIds: u,
    onGraphReady: l,
    onSelectEdge: c,
    onDeleteNodeById: d,
    onDuplicateNode: h,
    onDeleteEdgeById: v
  } = e, g = Ce(null), f = Ce(null), y = Ce(!1), q = Ce(!1), T = Ce({ armed: !1 }), _ = Ce(null), [x, N] = I(null), m = Ce({ onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o });
  m.current = { onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o, onSelectEdge: c };
  const S = ot(
    () => iv(r, t, n),
    [r, t, n]
  ), M = Ce(S);
  M.current = S;
  const B = Ce(/* @__PURE__ */ new Set());
  return B.current = new Set(r.nodes.map((w) => w.id)), ye(() => {
    const w = (J) => {
      J.code === "Space" && !J.repeat && (q.current = !0);
    }, D = (J) => {
      J.code === "Space" && (q.current = !1);
    }, L = () => {
      q.current = !1;
    };
    return window.addEventListener("keydown", w), window.addEventListener("keyup", D), window.addEventListener("blur", L), () => {
      window.removeEventListener("keydown", w), window.removeEventListener("keyup", D), window.removeEventListener("blur", L);
    };
  }, []), ye(() => {
    const w = g.current;
    if (!w || y.current) return;
    let D = !1;
    return import("./esm-CIqjNAcM.js").then(({ Graph: L }) => {
      if (D || !g.current || y.current) return;
      const J = sv(M.current, w), re = new L({
        ...J,
        behaviors: xh({
          graph: {
            getNodeData: () => {
              var U;
              return ((U = f.current) == null ? void 0 : U.getNodeData()) ?? [];
            },
            getCanvasByClient: (U) => {
              var V;
              const Q = ((V = f.current) == null ? void 0 : V.getCanvasByClient(U)) ?? U;
              return [Q[0], Q[1]];
            }
          },
          isPanModifier: () => q.current,
          gestureOrigin: () => _.current,
          connectionState: T.current,
          allowedIds: () => B.current,
          onPositions: (U) => m.current.onPositions(U),
          onSelectIds: (U) => u == null ? void 0 : u(U),
          onCreateEdge: (U, Q) => m.current.onCreateEdge(U, Q)
        })
      });
      re.on("node:click", (U) => {
        const Q = cv(U);
        Q && m.current.onSelect(Q);
      }), re.on("edge:click", (U) => {
        var V, C, F;
        const Q = U;
        (F = (C = m.current).onSelectEdge) == null || F.call(C, ((V = Q.target) == null ? void 0 : V.id) ?? null);
      }), re.on("node:contextmenu", (U) => {
        var V, C, F;
        const Q = U;
        (V = Q.target) != null && V.id && N({
          x: ((C = Q.client) == null ? void 0 : C.x) ?? 0,
          y: ((F = Q.client) == null ? void 0 : F.y) ?? 0,
          type: "node",
          id: Q.target.id
        });
      }), re.on("edge:contextmenu", (U) => {
        var V, C, F;
        const Q = U;
        (V = Q.target) != null && V.id && N({
          x: ((C = Q.client) == null ? void 0 : C.x) ?? 0,
          y: ((F = Q.client) == null ? void 0 : F.y) ?? 0,
          type: "edge",
          id: Q.target.id
        });
      }), re.on("canvas:click", () => m.current.onSelect(null)), re.on("pointerdown", (U) => {
        const Q = U;
        _.current = Q.client ? { x: Q.client.x, y: Q.client.y } : null;
      }), re.on("pointerup", () => {
        queueMicrotask(() => {
          T.current.armed = !1, _.current = null;
        });
      }), f.current = re, w.__industrialGraph = re, l == null || l(re), y.current = !0, re.render();
    }), () => {
      D = !0;
    };
  }, []), ye(() => {
    const w = f.current;
    !w || !y.current || (w.setData(S), w.draw());
  }, [S]), ye(() => () => {
    var w;
    (w = f.current) == null || w.destroy(), f.current = null, y.current = !1;
  }, []), /* @__PURE__ */ p("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ R("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ p(
      "div",
      {
        ref: g,
        className: "industrial-graph-host",
        onDragOver: (w) => w.preventDefault(),
        onDrop: (w) => {
          w.preventDefault();
          const D = w.dataTransfer.getData("application/x-industrial-node") || w.dataTransfer.getData("text/plain");
          if (!D) return;
          const L = f.current, J = g.current;
          if (!L || !J) return;
          const re = J.getBoundingClientRect(), U = L.getCanvasByViewport([
            w.clientX - re.left,
            w.clientY - re.top
          ]);
          m.current.onDropNode(D, U[0], U[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    x && /* @__PURE__ */ p("div", { className: "industrial-context-menu", style: { left: x.x, top: x.y }, children: x.type === "node" ? /* @__PURE__ */ R(sr, { children: [
      /* @__PURE__ */ p("button", { onClick: () => {
        h == null || h(x.id), N(null);
      }, children: "复制节点" }),
      /* @__PURE__ */ p("button", { onClick: () => {
        d == null || d(x.id), N(null);
      }, children: "删除节点" })
    ] }) : /* @__PURE__ */ R(sr, { children: [
      /* @__PURE__ */ p("button", { onClick: () => {
        c == null || c(x.id), N(null);
      }, children: "编辑连线" }),
      /* @__PURE__ */ p("button", { onClick: () => {
        v == null || v(x.id), N(null);
      }, children: "删除连线" })
    ] }) }),
    /* @__PURE__ */ p("div", { className: "industrial-graph-hint", children: "Shift+拖拽框选 · 拖节点移动 · 端口拖拽连线 · 滚轮缩放 · 空格/中键平移" })
  ] }) });
}
function dv({ descriptors: e, loading: r, error: t, onAdd: n }) {
  const a = /* @__PURE__ */ new Map();
  for (const s of e) {
    const o = s.category.split(".")[0] || "General", u = a.get(o) ?? [];
    u.push(s), a.set(o, u);
  }
  const i = (s, o) => {
    s.dataTransfer.setData("application/x-industrial-node", o.nodeTypeId), s.dataTransfer.setData("text/plain", o.nodeTypeId), s.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ R("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ p(G.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ p(G.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    r && /* @__PURE__ */ p(Sh, { size: "small", style: { display: "block", marginTop: 16 } }),
    t && /* @__PURE__ */ p(G.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: t }),
    !r && !t && e.length === 0 && /* @__PURE__ */ p(Ch, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(a.entries()).map(([s, o]) => /* @__PURE__ */ R("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ p(G.Text, { strong: !0, style: { fontSize: 12 }, children: s }),
      o.map((u) => /* @__PURE__ */ R(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          role: "button",
          tabIndex: 0,
          onDragStart: (l) => i(l, u),
          onClick: () => n == null ? void 0 : n(u.nodeTypeId),
          onKeyDown: (l) => {
            (l.key === "Enter" || l.key === " ") && (n == null || n(u.nodeTypeId));
          },
          title: u.description || u.nodeTypeId,
          children: [
            /* @__PURE__ */ p("div", { className: "industrial-palette-node-title", children: u.name }),
            /* @__PURE__ */ R("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ p(Me, { bordered: !1, color: "blue", children: u.version }),
              /* @__PURE__ */ p(G.Text, { type: "secondary", style: { fontSize: 11 }, children: u.nodeTypeId })
            ] }),
            /* @__PURE__ */ R("div", { className: "industrial-palette-node-ports", children: [
              u.inputs.map((l) => /* @__PURE__ */ R(Me, { bordered: !1, color: "default", children: [
                "in ",
                l.name,
                ": ",
                l.type
              ] }, `in-${l.name}`)),
              u.outputs.map((l) => /* @__PURE__ */ R(Me, { bordered: !1, color: "green", children: [
                "out ",
                l.name,
                ": ",
                l.type
              ] }, `out-${l.name}`))
            ] })
          ]
        },
        u.nodeTypeId
      ))
    ] }, s))
  ] });
}
const et = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {}
});
function fv(e) {
  const r = {};
  for (const [t, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (r[t] = n.default);
  return r;
}
function kt(e, r, t = 0, n = 0) {
  return { id: r, type: e.nodeTypeId, config: fv(e), x: t, y: n };
}
function Je(e, r) {
  return e.get(r);
}
function rt(e, r, t) {
  return e[t].find((n) => n.name === r);
}
function jo(e, r) {
  return e !== "Unknown" && r !== "Unknown" && e === r;
}
function vu(e, r) {
  for (const t of e.outputs)
    for (const n of r.inputs)
      if (jo(t.type, n.type))
        return { sourcePort: t.name, targetPort: n.name };
  return null;
}
const Te = (e, r, t) => ({ path: e, code: r, message: t });
function hv(e, r) {
  return e.source === r.source && e.sourcePort === r.sourcePort && e.target === r.target && e.targetPort === r.targetPort;
}
function pv(e, r, t) {
  const n = `edges.${e.edges.length}`, a = e.nodes.find((c) => c.id === r.source), i = e.nodes.find((c) => c.id === r.target);
  if (!a || !i)
    return { definition: e, issue: Te(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((c) => hv(c, r)))
    return { definition: e };
  const s = Je(t, a.type), o = Je(t, i.type);
  if (!s || !o)
    return { definition: e, issue: Te(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const u = rt(s, r.sourcePort, "outputs"), l = rt(o, r.targetPort, "inputs");
  return !u || !l ? { definition: e, issue: Te(n, "PORT_MISSING", "declared port does not exist") } : jo(u.type, l.type) ? { definition: { ...e, edges: [...e.edges, r] } } : {
    definition: e,
    issue: Te(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${u.type} → ${l.type} is not a legal connection`
    )
  };
}
function At(e, r) {
  return {
    ...e,
    nodes: e.nodes.filter((t) => t.id !== r),
    edges: e.edges.filter((t) => t.source !== r && t.target !== r)
  };
}
function gu(e, r) {
  return { ...e, edges: e.edges.filter((t, n) => n !== r) };
}
function vv(e, r) {
  const t = { ...e };
  for (const [n, a] of Object.entries(r))
    a === void 0 ? delete t[n] : t[n] = a;
  return t;
}
function gv(e, r, t) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === r ? { ...n, config: t } : n)
  };
}
function yv(e, r) {
  return {
    ...e,
    nodes: e.nodes.map((t) => {
      const n = r[t.id];
      return n ? { ...t, x: n.x, y: n.y } : t;
    }),
    // One canonical layout field: the domain ships positions with the
    // definition, never as a separate browser-only cache.
    layout: { ...e.layout, positions: { ...r } }
  };
}
function mv(e) {
  var i;
  const r = /* @__PURE__ */ new Map();
  for (const s of e.nodes) r.set(s.id, []);
  for (const s of e.edges)
    (i = r.get(s.source)) == null || i.push(s.target);
  const t = /* @__PURE__ */ new Map(), n = [], a = (s) => {
    t.set(s, 1), n.push(s);
    for (const o of r.get(s) ?? []) {
      if (t.get(o) === 1)
        return [...n.slice(n.indexOf(o)), o];
      if (t.get(o) !== 2) {
        const u = a(o);
        if (u) return u;
      }
    }
    return n.pop(), t.set(s, 2), null;
  };
  for (const s of e.nodes)
    if (t.get(s.id) !== 2) {
      const o = a(s.id);
      if (o) return o;
    }
  return null;
}
function bv(e, r, t) {
  const n = [];
  e.workflowId.trim() || n.push(Te("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(Te("workflow", "INVALID_VERSION", "version must be positive"));
  const a = /* @__PURE__ */ new Map();
  for (const o of e.nodes)
    a.has(o.id) && n.push(Te(`nodes.${o.id}`, "DUPLICATE_NODE_ID", `duplicate node '${o.id}'`)), a.set(o.id, o);
  const i = /* @__PURE__ */ new Map();
  for (const o of e.nodes) {
    const u = Je(r, o.type);
    if (!u) {
      n.push(Te(
        `nodes.${o.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${o.type}' is not registered`
      ));
      continue;
    }
    i.set(o.id, u);
    const l = u.configurationSchema.required ?? [];
    for (const c of l) {
      const d = o.config[c];
      (d == null || d === "") && n.push(Te(
        `nodes.${o.id}.config.${c}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${c}' is missing`
      ));
    }
    t && typeof o.config.resourceId == "string" && (t.has(o.config.resourceId) || n.push(Te(
      `nodes.${o.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${o.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((o, u) => {
    const l = a.get(o.source), c = a.get(o.target);
    if (!l || !c) {
      n.push(Te(`edges.${u}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const d = i.get(o.source), h = i.get(o.target);
    if (!d || !h) return;
    const v = rt(d, o.sourcePort, "outputs"), g = rt(h, o.targetPort, "inputs");
    if (!v || !g) {
      n.push(Te(`edges.${u}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    jo(v.type, g.type) || n.push(Te(
      `edges.${u}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${v.type} → ${g.type} is not a legal connection`
    ));
  });
  for (const o of e.nodes) {
    const u = i.get(o.id);
    if (u)
      for (const l of u.inputs.filter((c) => c.required))
        e.edges.some((c) => c.target === o.id && c.targetPort === l.name && a.has(c.source)) || n.push(Te(
          `nodes.${o.id}.inputs.${l.name}`,
          "MISSING_INPUT",
          `required input '${l.name}' is not connected`
        ));
  }
  const s = mv(e);
  return s && n.push(Te(`nodes.${s[0]}`, "GRAPH_CYCLE", `cycle: ${s.join(" → ")}`)), n;
}
const Po = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function _v(e) {
  const r = Po(e.additionalProperties).enum;
  if (!Array.isArray(r)) return null;
  const t = r.filter((n) => typeof n == "string");
  return t.length > 0 ? t : null;
}
function kh(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && Po(e.items).type === "string" ? "string-list" : e.type === "object" && Po(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function No(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, r]) => r != null && typeof r != "object").map(([r, t]) => ({ key: r, value: String(t) }));
}
function yu(e) {
  const r = {}, t = [];
  for (const n of e) {
    const a = n.key.trim();
    a && (Object.prototype.hasOwnProperty.call(r, a) && t.push(a), r[a] = n.value);
  }
  return { record: r, duplicateKeys: t };
}
function Yr(e) {
  if (!Array.isArray(e)) return [];
  const r = /* @__PURE__ */ new Set(), t = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const a = n.trim();
    !a || r.has(a) || (r.add(a), t.push(a));
  }
  return t;
}
function wv(e, r) {
  const t = /* @__PURE__ */ new Set();
  for (const [n, a] of Object.entries(e))
    if (kh(a) === "key-value")
      for (const i of No(r[n]))
        i.key.trim() && t.add(i.key.trim());
  return [...t];
}
function Iv({
  value: e,
  onChange: r,
  valueOptions: t,
  keyTitle: n = "目标字段",
  valueTitle: a = "来源字段",
  testId: i
}) {
  const s = JSON.stringify(e ?? {}), [o, u] = I(() => No(e)), [l, c] = I(s);
  ye(() => {
    s !== l && (u(No(e)), c(s));
  }, [s, l, e]);
  const d = (v) => {
    u(v), r == null || r(yu(v).record);
  }, { duplicateKeys: h } = yu(o);
  return /* @__PURE__ */ R("div", { "data-testid": i, className: "industrial-kv-editor", children: [
    o.map((v, g) => /* @__PURE__ */ R("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ p(
        Z,
        {
          "aria-label": `${n} ${g + 1}`,
          value: v.key,
          placeholder: n,
          onChange: (f) => d(o.map((y, q) => q === g ? { ...y, key: f.target.value } : y))
        }
      ),
      t && t.length > 0 ? /* @__PURE__ */ p(
        Ie,
        {
          "aria-label": `${a} ${g + 1}`,
          value: v.value || void 0,
          placeholder: a,
          options: t.map((f) => ({ label: f, value: f })),
          onChange: (f) => d(o.map((y, q) => q === g ? { ...y, value: f ?? "" } : y))
        }
      ) : /* @__PURE__ */ p(
        Z,
        {
          "aria-label": `${a} ${g + 1}`,
          value: v.value,
          placeholder: a,
          onChange: (f) => d(o.map((y, q) => q === g ? { ...y, value: f.target.value } : y))
        }
      ),
      /* @__PURE__ */ p(
        $,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${g + 1}`,
          onClick: () => d(o.filter((f, y) => y !== g)),
          children: "删"
        }
      )
    ] }, g)),
    /* @__PURE__ */ p(
      $,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => d([...o, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    h.length > 0 && /* @__PURE__ */ R(G.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      h.join("、")
    ] })
  ] });
}
function qv({ value: e, onChange: r, suggestions: t = [], testId: n }) {
  const a = JSON.stringify(Yr(e)), [i, s] = I(() => Yr(e)), [o, u] = I(a);
  ye(() => {
    a !== o && (s(Yr(e)), u(a));
  }, [a, o, e]);
  const l = (c) => {
    s(c), r == null || r(Yr(c));
  };
  return /* @__PURE__ */ R("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ p("datalist", { id: `${n ?? "list"}-suggestions`, children: t.map((c) => /* @__PURE__ */ p("option", { value: c }, c)) }),
    i.map((c, d) => /* @__PURE__ */ R("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ p(
        Z,
        {
          "aria-label": `字段 ${d + 1}`,
          value: c,
          list: `${n ?? "list"}-suggestions`,
          onChange: (h) => l(i.map((v, g) => g === d ? h.target.value : v))
        }
      ),
      /* @__PURE__ */ p(
        $,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${d + 1}`,
          onClick: () => l(i.filter((h, v) => v !== d)),
          children: "删"
        }
      )
    ] }, d)),
    /* @__PURE__ */ p($, { size: "small", type: "dashed", block: !0, onClick: () => l([...i, ""]), children: "添加字段" }),
    i.length === 0 && /* @__PURE__ */ p(G.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Ev({ value: e, onChange: r }) {
  const t = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, a] = I(t), [i, s] = I(null);
  ye(() => {
    a(t), s(null);
  }, [t]);
  const o = (u) => {
    a(u);
    try {
      const l = JSON.parse(u);
      s(null), r == null || r(l);
    } catch (l) {
      s(l.message);
    }
  };
  return /* @__PURE__ */ R("div", { children: [
    /* @__PURE__ */ p(Z.TextArea, { rows: 5, value: n, onChange: (u) => o(u.target.value) }),
    i && /* @__PURE__ */ p(G.Text, { type: "danger", style: { fontSize: 11 }, children: i })
  ] });
}
function Cv({ value: e, onChange: r, resources: t, resourceType: n, label: a }) {
  const i = t.filter((o) => o.enabled !== !1 && (!n || o.type === n)).map((o) => ({
    value: o.id,
    label: `${o.name} (${o.type})`
  })), s = t.find((o) => o.id === e);
  return /* @__PURE__ */ R("div", { children: [
    /* @__PURE__ */ R(
      "select",
      {
        "aria-label": a,
        value: e ?? "",
        onChange: (o) => r == null ? void 0 : r(o.target.value || void 0),
        children: [
          /* @__PURE__ */ p("option", { value: "", children: "未选择" }),
          i.map((o) => /* @__PURE__ */ p("option", { value: o.value, children: o.label }, o.value))
        ]
      }
    ),
    (s == null ? void 0 : s.credentialId) && /* @__PURE__ */ R(G.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      s.credentialName ?? s.credentialId
    ] })
  ] });
}
function Sv({ name: e, schema: r, required: t, resources: n, resourceType: a, properties: i, config: s }) {
  let o, u = "value";
  switch (kh(r)) {
    case "resource":
      o = /* @__PURE__ */ p(
        Cv,
        {
          resources: n,
          resourceType: a,
          label: String(r.title ?? e)
        }
      );
      break;
    case "enum":
      o = /* @__PURE__ */ p(
        Ie,
        {
          allowClear: !0,
          options: (r.enum ?? []).map((l) => ({ label: String(l), value: l }))
        }
      );
      break;
    case "number":
      o = /* @__PURE__ */ p(Fo, { style: { width: "100%" } });
      break;
    case "boolean":
      o = /* @__PURE__ */ p(ut, {}), u = "checked";
      break;
    case "key-value":
      o = /* @__PURE__ */ p(
        Iv,
        {
          testId: `field-${e}`,
          valueOptions: _v(r) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      o = /* @__PURE__ */ p(qv, { testId: `field-${e}`, suggestions: wv(i, s) });
      break;
    case "json":
      o = /* @__PURE__ */ p(Ev, {});
      break;
    default:
      o = /* @__PURE__ */ p(Z, { placeholder: r.description });
      break;
  }
  return /* @__PURE__ */ p(
    Oo.Item,
    {
      name: e,
      label: r.title ?? e,
      tooltip: r.description,
      valuePropName: u,
      rules: t && u === "value" ? [{ required: !0, message: `请填写 ${r.title ?? e}` }] : [],
      children: o
    },
    e
  );
}
function Tv(e) {
  const {
    node: r,
    descriptor: t,
    nodes: n,
    edges: a,
    catalog: i,
    resources: s,
    issues: o,
    onApplyConfig: u,
    onDeleteNode: l,
    onDeleteEdge: c,
    onCreateEdge: d,
    onUpdateEdge: h,
    onSelectNode: v,
    selectedEdgeId: g,
    sampleLines: f
  } = e, [y] = Oo.useForm(), [q, T] = I("{}"), [_, x] = I(null), [N, m] = I(), [S, M] = I(), [B, z] = I(!1), [w, D] = I(!1);
  ye(() => {
    y.setFieldsValue((r == null ? void 0 : r.config) ?? {}), T(JSON.stringify((r == null ? void 0 : r.config) ?? {}, null, 2)), x(null);
  }, [y, r == null ? void 0 : r.id, r == null ? void 0 : r.config]);
  const L = new Set((t == null ? void 0 : t.configurationSchema.required) ?? []), J = Object.entries((t == null ? void 0 : t.configurationSchema.properties) ?? {}), re = () => {
    if (J.length > 0) {
      y.validateFields().then((C) => u(vv((r == null ? void 0 : r.config) ?? {}, C)));
      return;
    }
    try {
      const C = JSON.parse(q);
      if (C === null || typeof C != "object" || Array.isArray(C))
        throw new Error("配置必须是 JSON 对象");
      x(null), u(C);
    } catch (C) {
      x(C.message);
    }
  }, Q = (r && !B ? a.map((C, F) => ({ edge: C, index: F })).filter(({ edge: C }) => C.source === r.id || C.target === r.id) : a.map((C, F) => ({ edge: C, index: F }))).map(({ edge: C, index: F }) => ({
    ...C,
    index: F,
    key: `${F}:${C.source}->${C.target}`
  })), V = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (C, F) => /* @__PURE__ */ R(G.Text, { style: { fontSize: 11 }, children: [
        F.source,
        " → ",
        F.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (C, F) => {
        const A = i.get(F.source), Y = i.get(F.target);
        return /* @__PURE__ */ R("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ p(
            Ie,
            {
              size: "small",
              value: F.sourcePort,
              style: { width: 110 },
              onChange: (ae) => h(F.index, { ...F, sourcePort: ae }),
              options: ((A == null ? void 0 : A.outputs) ?? []).map((ae) => ({ label: `${ae.name} (${ae.type})`, value: ae.name }))
            }
          ),
          /* @__PURE__ */ p(
            Ie,
            {
              size: "small",
              value: F.targetPort,
              style: { width: 110 },
              onChange: (ae) => h(F.index, { ...F, targetPort: ae }),
              options: ((Y == null ? void 0 : Y.inputs) ?? []).map((ae) => ({ label: `${ae.name} (${ae.type})`, value: ae.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (C, F) => /* @__PURE__ */ p($, { danger: !0, size: "small", type: "text", onClick: () => c(F.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ R("div", { className: "industrial-properties-body", children: [
    !r && /* @__PURE__ */ p(Ch, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    r && /* @__PURE__ */ R(sr, { children: [
      /* @__PURE__ */ p(G.Title, { level: 5, style: { margin: 0 }, children: (t == null ? void 0 : t.name) ?? r.type }),
      /* @__PURE__ */ p(G.Text, { type: "secondary", style: { fontSize: 11 }, children: r.id }),
      /* @__PURE__ */ p(G.Paragraph, { type: "secondary", style: { fontSize: 12, marginTop: 6 }, children: (t == null ? void 0 : t.description) ?? "请按业务含义配置该节点。" }),
      /* @__PURE__ */ R(H, { wrap: !0, size: 4, style: { marginBottom: 8 }, children: [
        /* @__PURE__ */ p(Me, { color: "blue", children: (t == null ? void 0 : t.category) ?? "采集节点" }),
        /* @__PURE__ */ R(Me, { children: [
          "版本 ",
          (t == null ? void 0 : t.version) ?? "1.0.0"
        ] }),
        t == null ? void 0 : t.inputs.map((C) => /* @__PURE__ */ R(Me, { color: C.required ? "red" : "default", children: [
          "输入 ",
          C.name,
          " · ",
          C.type
        ] }, `in-${C.name}`)),
        t == null ? void 0 : t.outputs.map((C) => /* @__PURE__ */ R(Me, { color: "green", children: [
          "输出 ",
          C.name,
          " · ",
          C.type
        ] }, `out-${C.name}`))
      ] }),
      (r.type.includes("reader") || r.type.includes("mapping") || r.type.includes("source") || r.type.includes("filesystem")) && f && f.length > 0 && /* @__PURE__ */ R(sr, { children: [
        /* @__PURE__ */ p(G.Text, { strong: !0, style: { fontSize: 12 }, children: "样本预览" }),
        /* @__PURE__ */ p("pre", { className: "industrial-sample-preview", children: f.join(`
`) })
      ] }),
      /* @__PURE__ */ p(Oo, { form: y, layout: "vertical", size: "small", style: { marginTop: 12 }, children: J.map(([C, F]) => /* @__PURE__ */ p(
        Sv,
        {
          name: C,
          schema: F,
          required: L.has(C),
          resources: s,
          resourceType: typeof (t == null ? void 0 : t.configurationSchema.resourceType) == "string" ? t.configurationSchema.resourceType : void 0,
          properties: (t == null ? void 0 : t.configurationSchema.properties) ?? {},
          config: (r == null ? void 0 : r.config) ?? {}
        },
        C
      )) }),
      J.length === 0 && /* @__PURE__ */ p(G.Text, { type: "secondary", style: { fontSize: 12 }, children: "此节点无需填写业务参数；保留默认配置即可。" }),
      /* @__PURE__ */ p($, { type: "link", size: "small", onClick: () => D((C) => !C), children: w ? "收起高级配置" : "高级配置" }),
      w && /* @__PURE__ */ R(sr, { children: [
        /* @__PURE__ */ p(G.Text, { type: "secondary", style: { fontSize: 12 }, children: "高级配置直接编辑节点 JSON，仅在需要复杂表达式时使用。" }),
        /* @__PURE__ */ p(
          Z.TextArea,
          {
            rows: 8,
            value: q,
            style: { marginTop: 6 },
            onChange: (C) => T(C.target.value)
          }
        ),
        _ && /* @__PURE__ */ p(G.Text, { type: "danger", style: { fontSize: 12 }, children: _ }),
        (t == null ? void 0 : t.activityType) && /* @__PURE__ */ R(G.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
          "运行时 Activity：",
          t.activityType
        ] })
      ] }),
      /* @__PURE__ */ p($, { type: "primary", block: !0, onClick: re, children: "应用配置" }),
      /* @__PURE__ */ p($, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: l, children: "删除节点" })
    ] }),
    g && (() => {
      const C = a.findIndex((A) => `e:${A.source}:${A.sourcePort}>${A.target}:${A.targetPort}` === g);
      if (C < 0) return null;
      const F = a[C];
      return /* @__PURE__ */ p(
        Re,
        {
          type: "info",
          showIcon: !0,
          style: { marginTop: 8 },
          message: `当前连线：${F.source}.${F.sourcePort} → ${F.target}.${F.targetPort}`,
          description: /* @__PURE__ */ p($, { size: "small", danger: !0, onClick: () => c(C), children: "删除此连线" })
        }
      );
    })(),
    /* @__PURE__ */ R(lu, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "相关连线 (",
      Q.length,
      " / ",
      a.length,
      ")"
    ] }),
    r && /* @__PURE__ */ p($, { type: "link", size: "small", onClick: () => z((C) => !C), children: B ? "仅看当前节点" : "显示全部连线" }),
    n.length > 1 && /* @__PURE__ */ R(H, { wrap: !0, style: { marginBottom: 8 }, children: [
      /* @__PURE__ */ R(
        "select",
        {
          "aria-label": "连线起点",
          value: N ?? "",
          onChange: (C) => m(C.target.value || void 0),
          children: [
            /* @__PURE__ */ p("option", { value: "", children: "起点节点" }),
            n.map((C) => /* @__PURE__ */ p("option", { value: C.id, children: C.id }, C.id))
          ]
        }
      ),
      /* @__PURE__ */ R(
        "select",
        {
          "aria-label": "连线终点",
          value: S ?? "",
          onChange: (C) => M(C.target.value || void 0),
          children: [
            /* @__PURE__ */ p("option", { value: "", children: "终点节点" }),
            n.map((C) => /* @__PURE__ */ p("option", { value: C.id, children: C.id }, C.id))
          ]
        }
      ),
      /* @__PURE__ */ p(
        $,
        {
          size: "small",
          disabled: !N || !S || N === S,
          onClick: () => {
            N && S && d(N, S);
          },
          children: "添加连线"
        }
      )
    ] }),
    a.length === 0 ? /* @__PURE__ */ p(G.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ p(xe, { size: "small", columns: V, dataSource: Q, pagination: !1 }),
    o.length > 0 && /* @__PURE__ */ R(sr, { children: [
      /* @__PURE__ */ R(lu, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        o.length,
        ")"
      ] }),
      o.map((C, F) => /* @__PURE__ */ R("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ p(Me, { color: "red", children: C.code }),
        /* @__PURE__ */ R(
          G.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const A = /^nodes\.([^.]+)/.exec(C.path);
              A && v(A[1]);
            },
            children: [
              C.path,
              ": ",
              C.message
            ]
          }
        )
      ] }, `${C.path}-${F}`))
    ] })
  ] });
}
const Rv = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class xv extends Error {
  constructor(r) {
    super(r), this.name = "DtoError";
  }
}
const te = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, me = (e) => Array.isArray(e) ? e : [], k = (e, r = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : r ?? "", fe = (e, r = 0) => {
  const t = typeof e == "string" ? Number(e) : e;
  return typeof t == "number" && Number.isFinite(t) ? t : r;
}, b = (e, ...r) => {
  for (const t of r)
    if (e[t] !== void 0 && e[t] !== null) return e[t];
};
function Se(e) {
  const r = te(e);
  if (Object.prototype.hasOwnProperty.call(r, "data")) return r.data;
  if (typeof r.code == "string" && typeof r.message == "string")
    throw new xv(`${r.code}: ${r.message}`);
  return e;
}
function kv(e) {
  const r = k(e).trim().toLowerCase();
  return Rv.find((n) => n.toLowerCase() === r) ?? "Unknown";
}
function Av(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const r = te(e), t = k(b(r, "name", "port", "id")).trim();
  return t ? {
    name: t,
    type: kv(b(r, "type", "portType", "dataType")),
    required: b(r, "required", "isRequired") === !0
  } : null;
}
function mu(e) {
  const r = me(e).map(Av).filter((n) => n !== null), t = /* @__PURE__ */ new Set();
  return r.filter((n) => t.has(n.name) ? !1 : (t.add(n.name), !0));
}
const Ov = (e) => {
  const r = te(e);
  return Object.keys(r).length === 0 ? { type: "object", properties: {} } : r;
};
function Pv(e) {
  const r = te(e), t = k(b(r, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return t ? {
    nodeTypeId: t,
    name: k(b(r, "name", "title"), t),
    description: k(b(r, "description", "desc")),
    category: k(b(r, "category", "group"), "General"),
    icon: k(b(r, "icon"), "AppstoreOutlined"),
    version: k(b(r, "version"), "1.0.0"),
    inputs: mu(b(r, "inputs", "inputPorts", "input_ports")),
    outputs: mu(b(r, "outputs", "outputPorts", "output_ports")),
    configurationSchema: Ov(b(r, "configurationSchema", "configSchema", "schema")),
    activityType: k(b(r, "activityType", "activity_type")),
    dependencies: me(b(r, "dependencies")).map((n) => k(n)).filter(Boolean)
  } : null;
}
function Ah(e) {
  const r = Se(e), t = Array.isArray(r) ? r : me(b(te(r), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return t.map(Pv).filter((a) => a !== null).filter((a) => n.has(a.nodeTypeId) ? !1 : (n.add(a.nodeTypeId), !0));
}
function Oh(e) {
  const r = Se(e), t = Array.isArray(r) ? r : me(b(te(r), "resources", "items")), n = /* @__PURE__ */ new Set();
  return t.map((a) => {
    const i = te(a), s = k(b(i, "id")).trim();
    return !s || n.has(s) ? null : (n.add(s), {
      id: s,
      name: k(b(i, "name"), s),
      type: k(b(i, "type"), "unknown"),
      enabled: i.enabled !== !1,
      credentialId: typeof i.credentialId == "string" ? i.credentialId : null,
      credentialName: typeof i.credentialName == "string" ? i.credentialName : null
    });
  }).filter((a) => a !== null);
}
function Ph(e) {
  const r = te(e), t = k(b(r, "workflowId", "workflow_id", "id")).trim();
  return t ? {
    workflowId: t,
    name: k(b(r, "name", "title"), t),
    version: fe(b(r, "version"), 1)
  } : null;
}
function Nh(e) {
  const r = Se(e);
  return (Array.isArray(r) ? r : me(b(te(r), "workflows", "items"))).map(Ph).filter((n) => n !== null);
}
function tt(e) {
  const r = te(e), t = {};
  for (const [n, a] of Object.entries(r))
    a !== void 0 && (t[n] = a);
  return t;
}
function Nv(e) {
  const r = te(e), t = k(b(r, "id", "nodeId", "node_id")).trim();
  if (!t) return null;
  const n = te(b(r, "position", "layout"));
  return {
    id: t,
    type: k(b(r, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: tt(b(r, "config", "configuration")),
    x: fe(b(r, "x") ?? b(n, "x"), 0),
    y: fe(b(r, "y") ?? b(n, "y"), 0)
  };
}
function Mv(e) {
  const r = te(e), t = k(b(r, "source", "from", "sourceId")).trim(), n = k(b(r, "target", "to", "targetId")).trim();
  return !t || !n ? null : {
    source: t,
    sourcePort: k(b(r, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: k(b(r, "targetPort", "target_port", "toPort"), "in")
  };
}
function Dv(e) {
  const r = Se(e), t = te(r), n = Ph(t) ?? { workflowId: "", name: "", version: 1 }, a = me(b(t, "nodes")).map(Nv).filter((u) => u !== null), i = me(b(t, "edges")).map(Mv).filter((u) => u !== null), s = te(b(t, "trigger")), o = {
    kind: k(b(s, "kind", "type"), "manual"),
    cron: b(s, "cron") === void 0 ? null : k(s.cron),
    eventName: b(s, "eventName", "event_name") === void 0 ? null : k(b(s, "eventName", "event_name")),
    config: tt(b(s, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: a,
    edges: i,
    trigger: o,
    resourceReferences: me(b(t, "resourceReferences", "resource_references")).map((u) => k(u)).filter(Boolean),
    layout: tt(b(t, "layout"))
  };
}
const qr = (e) => e === !0 || typeof e == "string" && e.toLowerCase() === "true";
function Lv(e) {
  const r = Se(e);
  return (Array.isArray(r) ? r : me(b(te(r), "resources", "items"))).map((n) => {
    const a = te(n), i = b(a, "credentialId", "credential_id");
    return {
      id: k(b(a, "id"), "—"),
      name: k(b(a, "name"), "—"),
      type: k(b(a, "type"), "—"),
      provider: k(b(a, "provider"), "—"),
      enabled: qr(b(a, "enabled")),
      credentialId: i == null ? null : k(i),
      credentialName: b(a, "credentialName") == null ? null : k(b(a, "credentialName")),
      config: tt(b(a, "config", "configuration"))
    };
  });
}
function Mh(e) {
  const r = Se(e);
  return (Array.isArray(r) ? r : me(b(te(r), "credentials", "items"))).map((n) => {
    const a = te(n);
    return {
      id: k(b(a, "id"), "—"),
      name: k(b(a, "name"), "—"),
      kind: k(b(a, "kind"), "—"),
      version: fe(b(a, "version"))
    };
  });
}
function Fv(e) {
  const r = Se(e);
  return (Array.isArray(r) ? r : me(b(te(r), "tests", "items"))).map((n) => {
    const a = te(n);
    return {
      resourceId: k(b(a, "resourceId", "resource_id"), "—"),
      ok: qr(b(a, "ok")),
      detail: k(b(a, "detail"), "")
    };
  });
}
const Dh = (e) => {
  if (e == null) return null;
  const r = te(e);
  return Object.keys(r).length === 0 ? null : {
    code: k(b(r, "code"), "OTHER"),
    message: k(b(r, "message"), ""),
    action: k(b(r, "action"), "")
  };
};
function jv(e) {
  const r = Se(e);
  return me(b(te(r), "triggers", "items")).map((n) => {
    const a = te(n), i = (s) => b(a, s) == null ? null : k(b(a, s));
    return {
      workflowId: k(b(a, "workflowId", "workflow_id"), "—"),
      version: fe(b(a, "version")),
      kind: k(b(a, "kind"), "manual"),
      cron: i("cron"),
      eventName: i("eventName"),
      timezone: i("timezone"),
      resourceId: i("resourceId"),
      nextRun: i("nextRun"),
      lastStatus: i("lastStatus"),
      lastStartedAt: i("lastStartedAt"),
      lastFireStatus: i("lastFireStatus"),
      lastFireDueAt: i("lastFireDueAt"),
      lastFireAttempts: b(a, "lastFireAttempts") == null ? null : fe(b(a, "lastFireAttempts")),
      skippedFires: b(a, "skippedFires") == null ? null : fe(b(a, "skippedFires")),
      backlogTruncated: b(a, "backlogTruncated") == null ? null : b(a, "backlogTruncated") === !0,
      lastError: i("lastError")
    };
  });
}
function $v(e) {
  if (e == null) return null;
  const r = Se(e), t = te(r);
  if (Object.keys(t).length === 0) return null;
  const n = me(b(t, "files")).map((i) => {
    const s = te(i);
    return {
      path: k(b(s, "path"), "—"),
      sourceDate: k(b(s, "sourceDate"), ""),
      inLedger: qr(b(s, "inLedger")),
      expectedTargets: fe(b(s, "expectedTargets")),
      succeededTargets: fe(b(s, "succeededTargets")),
      incompleteTargets: fe(b(s, "incompleteTargets")),
      missingTargets: k(b(s, "missingTargets"), ""),
      wouldSkip: qr(b(s, "wouldSkip")),
      wouldCollect: qr(b(s, "wouldCollect")),
      duplicateRisk: qr(b(s, "duplicateRisk"))
    };
  }), a = te(b(t, "summary"));
  return {
    resourceId: k(b(t, "resourceId"), "—"),
    from: k(b(t, "from"), ""),
    to: k(b(t, "to"), ""),
    files: n,
    summary: {
      discovered: fe(b(a, "discovered")),
      alreadyComplete: fe(b(a, "alreadyComplete")),
      needsCollection: fe(b(a, "needsCollection")),
      duplicateRisk: fe(b(a, "duplicateRisk"))
    }
  };
}
function Gv(e) {
  const r = Se(e);
  return (Array.isArray(r) ? r : me(b(te(r), "files", "items"))).map((n) => {
    const a = te(n);
    return {
      id: k(b(a, "id"), "—"),
      path: k(b(a, "path"), "—"),
      identityKey: k(b(a, "identityKey"), ""),
      sourceResourceId: k(b(a, "sourceResourceId"), ""),
      length: fe(b(a, "length")),
      sourceDate: k(b(a, "sourceDate"), ""),
      collectedAt: k(b(a, "collectedAt"), ""),
      status: k(b(a, "status"), "—"),
      deliveries: me(b(a, "deliveries")).map((i) => {
        const s = te(i);
        return {
          id: k(b(s, "id"), "—"),
          collectionFileId: k(b(s, "collectionFileId"), ""),
          targetId: k(b(s, "targetId"), "—"),
          targetKind: k(b(s, "targetKind"), "record"),
          status: k(b(s, "status"), "—"),
          attempts: fe(b(s, "attempts")),
          records: fe(b(s, "records")),
          error: b(s, "error") == null ? null : k(b(s, "error")),
          accepted: fe(b(s, "accepted")),
          rejected: fe(b(s, "rejected")),
          warnings: fe(b(s, "warnings")),
          diagnosis: Dh(b(s, "diagnosis"))
        };
      })
    };
  });
}
function zv(e) {
  const r = Se(e), t = te(r), n = me(b(t, "runs")).map((i) => {
    const s = te(i);
    return {
      id: k(b(s, "id"), "—"),
      workflowId: k(b(s, "workflowId", "workflow_id"), "—"),
      status: k(b(s, "status"), "—"),
      startedAt: k(b(s, "startedAt", "started_at"), ""),
      finishedAt: b(s, "finishedAt", "finished_at") == null ? null : k(b(s, "finishedAt", "finished_at")),
      error: b(s, "error") == null ? null : k(b(s, "error"))
    };
  }), a = me(b(t, "deliveries")).map((i) => {
    const s = te(i);
    return {
      id: k(b(s, "id"), "—"),
      collectionFileId: k(b(s, "collectionFileId", "collection_file_id"), ""),
      targetId: k(b(s, "targetId", "target_id"), "—"),
      targetKind: k(b(s, "targetKind", "target_kind"), "record"),
      status: k(b(s, "status"), "—"),
      attempts: fe(b(s, "attempts")),
      records: fe(b(s, "records")),
      error: b(s, "error") == null ? null : k(b(s, "error")),
      diagnosis: Dh(b(s, "diagnosis"))
    };
  });
  return { runs: n, deliveries: a };
}
function Bv(e) {
  if (e == null) return null;
  const r = Se(e), t = te(r);
  if (Object.keys(t).length === 0) return null;
  const n = me(b(t, "nodes")).slice(0, 64).map((a) => {
    const i = te(a);
    return {
      nodeId: k(b(i, "nodeId", "node_id"), "—"),
      status: k(b(i, "status"), "—"),
      records: fe(b(i, "records")),
      files: fe(b(i, "files")),
      error: b(i, "error") == null ? null : k(b(i, "error"), void 0),
      samples: me(b(i, "samples")).slice(0, 3).map((s) => k(s))
    };
  });
  return {
    status: k(b(t, "status"), "—"),
    error: b(t, "error") == null ? null : k(b(t, "error"), void 0),
    executed: me(b(t, "executed")).map((a) => k(a)),
    instanceId: b(t, "instanceId") == null ? null : k(b(t, "instanceId"), void 0),
    definitionId: b(t, "definitionId") == null ? null : k(b(t, "definitionId"), void 0),
    nodes: n
  };
}
const Uv = [
  { value: "manual", label: "手动运行" },
  { value: "schedule", label: "定时运行" },
  { value: "event", label: "事件触发" }
], Kv = [
  { label: "每天 02:00", cron: "0 2 * * *" },
  { label: "每小时", cron: "0 * * * *" },
  { label: "每 5 分钟", cron: "*/5 * * * *" },
  { label: "工作日 08:30", cron: "30 8 * * 1-5" }
];
function Hv({
  open: e,
  onClose: r,
  trigger: t,
  resourceIds: n,
  onSave: a,
  ctx: i,
  workflowId: s
}) {
  const [o, u] = I(t), [l, c] = I([]), [d, h] = I(null);
  ye(() => {
    u(t);
  }, [t]), ye(() => {
    e && i.hubQuery("industrial/trigger-status").then((f) => c(jv(f))).catch((f) => h(f instanceof Error ? f.message : String(f)));
  }, [i, e]);
  const v = l.find((f) => f.workflowId === s);
  return /* @__PURE__ */ p(
    fr,
    {
      open: e,
      title: "触发器配置",
      okText: "保存触发器",
      cancelText: "取消",
      onCancel: r,
      onOk: () => {
        var f, y;
        if (o.kind === "schedule" && !((f = o.cron) != null && f.trim())) {
          h("定时运行需要填写 cron 表达式");
          return;
        }
        if (o.kind === "event" && !((y = o.eventName) != null && y.trim())) {
          h("事件触发需要填写事件名称");
          return;
        }
        h(null), a(o), r();
      },
      width: 720,
      children: /* @__PURE__ */ R(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ R(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ p(G.Text, { children: "触发方式" }),
          /* @__PURE__ */ p(H, { wrap: !0, children: Uv.map((f) => /* @__PURE__ */ p(
            $,
            {
              type: o.kind === f.value ? "primary" : "default",
              onClick: () => u({ ...o, kind: f.value }),
              children: f.label
            },
            f.value
          )) })
        ] }),
        /* @__PURE__ */ p(H, { wrap: !0, children: /* @__PURE__ */ p(
          Ie,
          {
            "aria-label": "运行资源",
            allowClear: !0,
            placeholder: "运行资源",
            value: Vv(o),
            options: n.map((f) => ({ value: f, label: f })),
            onChange: (f) => u({
              ...o,
              config: { ...o.config, resourceId: f ?? "" }
            })
          }
        ) }),
        /* @__PURE__ */ R(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ p(G.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ R(H, { wrap: !0, children: [
            /* @__PURE__ */ p(G.Text, { children: "常用" }),
            Kv.map((f) => /* @__PURE__ */ p(
              $,
              {
                size: "small",
                onClick: () => u({ ...o, cron: f.cron }),
                children: f.label
              },
              f.cron
            ))
          ] }),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "Cron 表达式",
              placeholder: "分 时 日 月 周，例如 0 2 * * *",
              value: o.cron ?? "",
              onChange: (f) => u({ ...o, cron: f.target.value })
            }
          ),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "调度时区",
              placeholder: "时区（默认 UTC，例如 Asia/Shanghai）",
              value: Wv(o),
              onChange: (f) => u({
                ...o,
                config: { ...o.config, timezone: f.target.value }
              })
            }
          ),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "事件名称",
              placeholder: "事件名称",
              value: o.eventName ?? "",
              onChange: (f) => u({ ...o, eventName: f.target.value })
            }
          ),
          /* @__PURE__ */ R(G.Text, { type: "secondary", children: [
            "下次运行：",
            (v == null ? void 0 : v.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        v && /* @__PURE__ */ p(
          xe,
          {
            rowKey: "workflowId",
            dataSource: [v],
            pagination: !1,
            columns: [
              { title: "时区", dataIndex: "timezone", render: (f) => f ?? "UTC" },
              { title: "状态", dataIndex: "lastStatus", render: (f) => f ?? "未运行" },
              { title: "上次运行", dataIndex: "lastStartedAt", render: (f) => f ?? "—" },
              {
                title: "触发点",
                dataIndex: "lastFireStatus",
                render: (f, y) => y.lastFireStatus ? `${y.lastFireStatus} · ${y.lastFireDueAt ?? "—"} · ${y.lastFireAttempts ?? 0} 次` + (y.skippedFires ? ` · 跳过≥${y.skippedFires}` : "") + (y.backlogTruncated ? " · 积压扫描截断" : "") : "—"
              },
              { title: "错误", dataIndex: "lastError", render: (f) => f ?? "—" }
            ]
          }
        ),
        d && /* @__PURE__ */ p(Re, { type: "error", showIcon: !0, message: d })
      ] })
    }
  );
}
function Vv(e) {
  const r = e.config.resourceId;
  return typeof r == "string" && r.trim() ? r : void 0;
}
function Wv(e) {
  const r = e.config.timezone;
  return typeof r == "string" && r.trim() ? r : "UTC";
}
function Qv({
  open: e,
  onClose: r,
  ctx: t,
  workflowId: n,
  resourceId: a,
  lookbackDays: i
}) {
  const [s, o] = I(null), [u, l] = I(null), [c, d] = I(!1), h = j(async () => {
    d(!0), l(null);
    try {
      await t.hubCommand("industrial/preview-backfill", {
        workflowId: n,
        resourceId: a,
        lookbackDays: i
      }), o($v(await t.hubQuery("industrial/backfill-preview")));
    } catch (v) {
      l(v instanceof Error ? v.message : String(v));
    } finally {
      d(!1);
    }
  }, [t, i, a, n]);
  return /* @__PURE__ */ p(
    fr,
    {
      open: e,
      title: "补采预览",
      onCancel: r,
      width: 820,
      footer: [
        /* @__PURE__ */ p($, { loading: c, onClick: () => void h(), children: "重新扫描" }, "refresh"),
        /* @__PURE__ */ p($, { type: "primary", onClick: r, children: "关闭" }, "close")
      ],
      children: /* @__PURE__ */ R(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ R(H, { children: [
          /* @__PURE__ */ R($, { type: "primary", loading: c, onClick: () => void h(), children: [
            "扫描最近 ",
            i,
            " 天"
          ] }),
          /* @__PURE__ */ R(G.Text, { type: "secondary", children: [
            "资源：",
            a || "未选择"
          ] })
        ] }),
        u && /* @__PURE__ */ p(Re, { type: "error", showIcon: !0, message: u }),
        s && /* @__PURE__ */ R(sr, { children: [
          /* @__PURE__ */ R(G.Text, { children: [
            "发现 ",
            s.summary.discovered,
            " 个文件；已完成 ",
            s.summary.alreadyComplete,
            "； 待采集 ",
            s.summary.needsCollection,
            "；重复风险 ",
            s.summary.duplicateRisk,
            "。"
          ] }),
          /* @__PURE__ */ p(
            xe,
            {
              rowKey: "path",
              dataSource: s.files,
              pagination: !1,
              size: "small",
              columns: [
                { title: "文件", dataIndex: "path" },
                { title: "源日期", dataIndex: "sourceDate" },
                { title: "账本", dataIndex: "inLedger", render: (v) => v ? "已登记" : "新文件" },
                { title: "当前目标", dataIndex: "expectedTargets" },
                { title: "成功目标", dataIndex: "succeededTargets" },
                { title: "待补目标", dataIndex: "missingTargets", render: (v) => v || "—" },
                { title: "未完成目标", dataIndex: "incompleteTargets" },
                {
                  title: "预计动作",
                  render: (v, g) => g.wouldSkip ? "跳过" : g.duplicateRisk ? "仅补缺失目标" : "采集"
                }
              ]
            }
          )
        ] })
      ] })
    }
  );
}
var Jr = typeof globalThis < "u" ? globalThis : typeof window < "u" ? window : typeof global < "u" ? global : typeof self < "u" ? self : {};
function Yv(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
function $o(e) {
  throw new Error('Could not dynamically require "' + e + '". Please configure the dynamicRequireTargets or/and ignoreDynamicRequires option of @rollup/plugin-commonjs appropriately for this require call to work.');
}
var Ot, bu;
function Jv() {
  if (bu) return Ot;
  bu = 1;
  function e() {
    this.__data__ = [], this.size = 0;
  }
  return Ot = e, Ot;
}
var Pt, _u;
function Er() {
  if (_u) return Pt;
  _u = 1;
  function e(r, t) {
    return r === t || r !== r && t !== t;
  }
  return Pt = e, Pt;
}
var Nt, wu;
function ct() {
  if (wu) return Nt;
  wu = 1;
  var e = Er();
  function r(t, n) {
    for (var a = t.length; a--; )
      if (e(t[a][0], n))
        return a;
    return -1;
  }
  return Nt = r, Nt;
}
var Mt, Iu;
function Xv() {
  if (Iu) return Mt;
  Iu = 1;
  var e = ct(), r = Array.prototype, t = r.splice;
  function n(a) {
    var i = this.__data__, s = e(i, a);
    if (s < 0)
      return !1;
    var o = i.length - 1;
    return s == o ? i.pop() : t.call(i, s, 1), --this.size, !0;
  }
  return Mt = n, Mt;
}
var Dt, qu;
function Zv() {
  if (qu) return Dt;
  qu = 1;
  var e = ct();
  function r(t) {
    var n = this.__data__, a = e(n, t);
    return a < 0 ? void 0 : n[a][1];
  }
  return Dt = r, Dt;
}
var Lt, Eu;
function eg() {
  if (Eu) return Lt;
  Eu = 1;
  var e = ct();
  function r(t) {
    return e(this.__data__, t) > -1;
  }
  return Lt = r, Lt;
}
var Ft, Cu;
function rg() {
  if (Cu) return Ft;
  Cu = 1;
  var e = ct();
  function r(t, n) {
    var a = this.__data__, i = e(a, t);
    return i < 0 ? (++this.size, a.push([t, n])) : a[i][1] = n, this;
  }
  return Ft = r, Ft;
}
var jt, Su;
function lt() {
  if (Su) return jt;
  Su = 1;
  var e = Jv(), r = Xv(), t = Zv(), n = eg(), a = rg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var l = s[o];
      this.set(l[0], l[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, jt = i, jt;
}
var $t, Tu;
function tg() {
  if (Tu) return $t;
  Tu = 1;
  var e = lt();
  function r() {
    this.__data__ = new e(), this.size = 0;
  }
  return $t = r, $t;
}
var Gt, Ru;
function ng() {
  if (Ru) return Gt;
  Ru = 1;
  function e(r) {
    var t = this.__data__, n = t.delete(r);
    return this.size = t.size, n;
  }
  return Gt = e, Gt;
}
var zt, xu;
function ag() {
  if (xu) return zt;
  xu = 1;
  function e(r) {
    return this.__data__.get(r);
  }
  return zt = e, zt;
}
var Bt, ku;
function ig() {
  if (ku) return Bt;
  ku = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return Bt = e, Bt;
}
var Ut, Au;
function Lh() {
  if (Au) return Ut;
  Au = 1;
  var e = typeof Jr == "object" && Jr && Jr.Object === Object && Jr;
  return Ut = e, Ut;
}
var Kt, Ou;
function Fe() {
  if (Ou) return Kt;
  Ou = 1;
  var e = Lh(), r = typeof self == "object" && self && self.Object === Object && self, t = e || r || Function("return this")();
  return Kt = t, Kt;
}
var Ht, Pu;
function Cr() {
  if (Pu) return Ht;
  Pu = 1;
  var e = Fe(), r = e.Symbol;
  return Ht = r, Ht;
}
var Vt, Nu;
function sg() {
  if (Nu) return Vt;
  Nu = 1;
  var e = Cr(), r = Object.prototype, t = r.hasOwnProperty, n = r.toString, a = e ? e.toStringTag : void 0;
  function i(s) {
    var o = t.call(s, a), u = s[a];
    try {
      s[a] = void 0;
      var l = !0;
    } catch {
    }
    var c = n.call(s);
    return l && (o ? s[a] = u : delete s[a]), c;
  }
  return Vt = i, Vt;
}
var Wt, Mu;
function og() {
  if (Mu) return Wt;
  Mu = 1;
  var e = Object.prototype, r = e.toString;
  function t(n) {
    return r.call(n);
  }
  return Wt = t, Wt;
}
var Qt, Du;
function hr() {
  if (Du) return Qt;
  Du = 1;
  var e = Cr(), r = sg(), t = og(), n = "[object Null]", a = "[object Undefined]", i = e ? e.toStringTag : void 0;
  function s(o) {
    return o == null ? o === void 0 ? a : n : i && i in Object(o) ? r(o) : t(o);
  }
  return Qt = s, Qt;
}
var Yt, Lu;
function De() {
  if (Lu) return Yt;
  Lu = 1;
  function e(r) {
    var t = typeof r;
    return r != null && (t == "object" || t == "function");
  }
  return Yt = e, Yt;
}
var Jt, Fu;
function zr() {
  if (Fu) return Jt;
  Fu = 1;
  var e = hr(), r = De(), t = "[object AsyncFunction]", n = "[object Function]", a = "[object GeneratorFunction]", i = "[object Proxy]";
  function s(o) {
    if (!r(o))
      return !1;
    var u = e(o);
    return u == n || u == a || u == t || u == i;
  }
  return Jt = s, Jt;
}
var Xt, ju;
function ug() {
  if (ju) return Xt;
  ju = 1;
  var e = Fe(), r = e["__core-js_shared__"];
  return Xt = r, Xt;
}
var Zt, $u;
function cg() {
  if ($u) return Zt;
  $u = 1;
  var e = ug(), r = function() {
    var n = /[^.]+$/.exec(e && e.keys && e.keys.IE_PROTO || "");
    return n ? "Symbol(src)_1." + n : "";
  }();
  function t(n) {
    return !!r && r in n;
  }
  return Zt = t, Zt;
}
var en, Gu;
function Fh() {
  if (Gu) return en;
  Gu = 1;
  var e = Function.prototype, r = e.toString;
  function t(n) {
    if (n != null) {
      try {
        return r.call(n);
      } catch {
      }
      try {
        return n + "";
      } catch {
      }
    }
    return "";
  }
  return en = t, en;
}
var rn, zu;
function lg() {
  if (zu) return rn;
  zu = 1;
  var e = zr(), r = cg(), t = De(), n = Fh(), a = /[\\^$.*+?()[\]{}|]/g, i = /^\[object .+?Constructor\]$/, s = Function.prototype, o = Object.prototype, u = s.toString, l = o.hasOwnProperty, c = RegExp(
    "^" + u.call(l).replace(a, "\\$&").replace(/hasOwnProperty|(function).*?(?=\\\()| for .+?(?=\\\])/g, "$1.*?") + "$"
  );
  function d(h) {
    if (!t(h) || r(h))
      return !1;
    var v = e(h) ? c : i;
    return v.test(n(h));
  }
  return rn = d, rn;
}
var tn, Bu;
function dg() {
  if (Bu) return tn;
  Bu = 1;
  function e(r, t) {
    return r == null ? void 0 : r[t];
  }
  return tn = e, tn;
}
var nn, Uu;
function pr() {
  if (Uu) return nn;
  Uu = 1;
  var e = lg(), r = dg();
  function t(n, a) {
    var i = r(n, a);
    return e(i) ? i : void 0;
  }
  return nn = t, nn;
}
var an, Ku;
function Go() {
  if (Ku) return an;
  Ku = 1;
  var e = pr(), r = Fe(), t = e(r, "Map");
  return an = t, an;
}
var sn, Hu;
function dt() {
  if (Hu) return sn;
  Hu = 1;
  var e = pr(), r = e(Object, "create");
  return sn = r, sn;
}
var on, Vu;
function fg() {
  if (Vu) return on;
  Vu = 1;
  var e = dt();
  function r() {
    this.__data__ = e ? e(null) : {}, this.size = 0;
  }
  return on = r, on;
}
var un, Wu;
function hg() {
  if (Wu) return un;
  Wu = 1;
  function e(r) {
    var t = this.has(r) && delete this.__data__[r];
    return this.size -= t ? 1 : 0, t;
  }
  return un = e, un;
}
var cn, Qu;
function pg() {
  if (Qu) return cn;
  Qu = 1;
  var e = dt(), r = "__lodash_hash_undefined__", t = Object.prototype, n = t.hasOwnProperty;
  function a(i) {
    var s = this.__data__;
    if (e) {
      var o = s[i];
      return o === r ? void 0 : o;
    }
    return n.call(s, i) ? s[i] : void 0;
  }
  return cn = a, cn;
}
var ln, Yu;
function vg() {
  if (Yu) return ln;
  Yu = 1;
  var e = dt(), r = Object.prototype, t = r.hasOwnProperty;
  function n(a) {
    var i = this.__data__;
    return e ? i[a] !== void 0 : t.call(i, a);
  }
  return ln = n, ln;
}
var dn, Ju;
function gg() {
  if (Ju) return dn;
  Ju = 1;
  var e = dt(), r = "__lodash_hash_undefined__";
  function t(n, a) {
    var i = this.__data__;
    return this.size += this.has(n) ? 0 : 1, i[n] = e && a === void 0 ? r : a, this;
  }
  return dn = t, dn;
}
var fn, Xu;
function yg() {
  if (Xu) return fn;
  Xu = 1;
  var e = fg(), r = hg(), t = pg(), n = vg(), a = gg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var l = s[o];
      this.set(l[0], l[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, fn = i, fn;
}
var hn, Zu;
function mg() {
  if (Zu) return hn;
  Zu = 1;
  var e = yg(), r = lt(), t = Go();
  function n() {
    this.size = 0, this.__data__ = {
      hash: new e(),
      map: new (t || r)(),
      string: new e()
    };
  }
  return hn = n, hn;
}
var pn, ec;
function bg() {
  if (ec) return pn;
  ec = 1;
  function e(r) {
    var t = typeof r;
    return t == "string" || t == "number" || t == "symbol" || t == "boolean" ? r !== "__proto__" : r === null;
  }
  return pn = e, pn;
}
var vn, rc;
function ft() {
  if (rc) return vn;
  rc = 1;
  var e = bg();
  function r(t, n) {
    var a = t.__data__;
    return e(n) ? a[typeof n == "string" ? "string" : "hash"] : a.map;
  }
  return vn = r, vn;
}
var gn, tc;
function _g() {
  if (tc) return gn;
  tc = 1;
  var e = ft();
  function r(t) {
    var n = e(this, t).delete(t);
    return this.size -= n ? 1 : 0, n;
  }
  return gn = r, gn;
}
var yn, nc;
function wg() {
  if (nc) return yn;
  nc = 1;
  var e = ft();
  function r(t) {
    return e(this, t).get(t);
  }
  return yn = r, yn;
}
var mn, ac;
function Ig() {
  if (ac) return mn;
  ac = 1;
  var e = ft();
  function r(t) {
    return e(this, t).has(t);
  }
  return mn = r, mn;
}
var bn, ic;
function qg() {
  if (ic) return bn;
  ic = 1;
  var e = ft();
  function r(t, n) {
    var a = e(this, t), i = a.size;
    return a.set(t, n), this.size += a.size == i ? 0 : 1, this;
  }
  return bn = r, bn;
}
var _n, sc;
function zo() {
  if (sc) return _n;
  sc = 1;
  var e = mg(), r = _g(), t = wg(), n = Ig(), a = qg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var l = s[o];
      this.set(l[0], l[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, _n = i, _n;
}
var wn, oc;
function Eg() {
  if (oc) return wn;
  oc = 1;
  var e = lt(), r = Go(), t = zo(), n = 200;
  function a(i, s) {
    var o = this.__data__;
    if (o instanceof e) {
      var u = o.__data__;
      if (!r || u.length < n - 1)
        return u.push([i, s]), this.size = ++o.size, this;
      o = this.__data__ = new t(u);
    }
    return o.set(i, s), this.size = o.size, this;
  }
  return wn = a, wn;
}
var In, uc;
function ht() {
  if (uc) return In;
  uc = 1;
  var e = lt(), r = tg(), t = ng(), n = ag(), a = ig(), i = Eg();
  function s(o) {
    var u = this.__data__ = new e(o);
    this.size = u.size;
  }
  return s.prototype.clear = r, s.prototype.delete = t, s.prototype.get = n, s.prototype.has = a, s.prototype.set = i, In = s, In;
}
var qn, cc;
function Bo() {
  if (cc) return qn;
  cc = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a && t(r[n], n, r) !== !1; )
      ;
    return r;
  }
  return qn = e, qn;
}
var En, lc;
function jh() {
  if (lc) return En;
  lc = 1;
  var e = pr(), r = function() {
    try {
      var t = e(Object, "defineProperty");
      return t({}, "", {}), t;
    } catch {
    }
  }();
  return En = r, En;
}
var Cn, dc;
function pt() {
  if (dc) return Cn;
  dc = 1;
  var e = jh();
  function r(t, n, a) {
    n == "__proto__" && e ? e(t, n, {
      configurable: !0,
      enumerable: !0,
      value: a,
      writable: !0
    }) : t[n] = a;
  }
  return Cn = r, Cn;
}
var Sn, fc;
function vt() {
  if (fc) return Sn;
  fc = 1;
  var e = pt(), r = Er(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o) {
    var u = i[s];
    (!(n.call(i, s) && r(u, o)) || o === void 0 && !(s in i)) && e(i, s, o);
  }
  return Sn = a, Sn;
}
var Tn, hc;
function Br() {
  if (hc) return Tn;
  hc = 1;
  var e = vt(), r = pt();
  function t(n, a, i, s) {
    var o = !i;
    i || (i = {});
    for (var u = -1, l = a.length; ++u < l; ) {
      var c = a[u], d = s ? s(i[c], n[c], c, i, n) : void 0;
      d === void 0 && (d = n[c]), o ? r(i, c, d) : e(i, c, d);
    }
    return i;
  }
  return Tn = t, Tn;
}
var Rn, pc;
function Cg() {
  if (pc) return Rn;
  pc = 1;
  function e(r, t) {
    for (var n = -1, a = Array(r); ++n < r; )
      a[n] = t(n);
    return a;
  }
  return Rn = e, Rn;
}
var xn, vc;
function He() {
  if (vc) return xn;
  vc = 1;
  function e(r) {
    return r != null && typeof r == "object";
  }
  return xn = e, xn;
}
var kn, gc;
function Sg() {
  if (gc) return kn;
  gc = 1;
  var e = hr(), r = He(), t = "[object Arguments]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return kn = n, kn;
}
var An, yc;
function Ur() {
  if (yc) return An;
  yc = 1;
  var e = Sg(), r = He(), t = Object.prototype, n = t.hasOwnProperty, a = t.propertyIsEnumerable, i = e(/* @__PURE__ */ function() {
    return arguments;
  }()) ? e : function(s) {
    return r(s) && n.call(s, "callee") && !a.call(s, "callee");
  };
  return An = i, An;
}
var On, mc;
function be() {
  if (mc) return On;
  mc = 1;
  var e = Array.isArray;
  return On = e, On;
}
var Nr = { exports: {} }, Pn, bc;
function Tg() {
  if (bc) return Pn;
  bc = 1;
  function e() {
    return !1;
  }
  return Pn = e, Pn;
}
Nr.exports;
var _c;
function Sr() {
  return _c || (_c = 1, function(e, r) {
    var t = Fe(), n = Tg(), a = r && !r.nodeType && r, i = a && !0 && e && !e.nodeType && e, s = i && i.exports === a, o = s ? t.Buffer : void 0, u = o ? o.isBuffer : void 0, l = u || n;
    e.exports = l;
  }(Nr, Nr.exports)), Nr.exports;
}
var Nn, wc;
function gt() {
  if (wc) return Nn;
  wc = 1;
  var e = 9007199254740991, r = /^(?:0|[1-9]\d*)$/;
  function t(n, a) {
    var i = typeof n;
    return a = a ?? e, !!a && (i == "number" || i != "symbol" && r.test(n)) && n > -1 && n % 1 == 0 && n < a;
  }
  return Nn = t, Nn;
}
var Mn, Ic;
function Uo() {
  if (Ic) return Mn;
  Ic = 1;
  var e = 9007199254740991;
  function r(t) {
    return typeof t == "number" && t > -1 && t % 1 == 0 && t <= e;
  }
  return Mn = r, Mn;
}
var Dn, qc;
function Rg() {
  if (qc) return Dn;
  qc = 1;
  var e = hr(), r = Uo(), t = He(), n = "[object Arguments]", a = "[object Array]", i = "[object Boolean]", s = "[object Date]", o = "[object Error]", u = "[object Function]", l = "[object Map]", c = "[object Number]", d = "[object Object]", h = "[object RegExp]", v = "[object Set]", g = "[object String]", f = "[object WeakMap]", y = "[object ArrayBuffer]", q = "[object DataView]", T = "[object Float32Array]", _ = "[object Float64Array]", x = "[object Int8Array]", N = "[object Int16Array]", m = "[object Int32Array]", S = "[object Uint8Array]", M = "[object Uint8ClampedArray]", B = "[object Uint16Array]", z = "[object Uint32Array]", w = {};
  w[T] = w[_] = w[x] = w[N] = w[m] = w[S] = w[M] = w[B] = w[z] = !0, w[n] = w[a] = w[y] = w[i] = w[q] = w[s] = w[o] = w[u] = w[l] = w[c] = w[d] = w[h] = w[v] = w[g] = w[f] = !1;
  function D(L) {
    return t(L) && r(L.length) && !!w[e(L)];
  }
  return Dn = D, Dn;
}
var Ln, Ec;
function yt() {
  if (Ec) return Ln;
  Ec = 1;
  function e(r) {
    return function(t) {
      return r(t);
    };
  }
  return Ln = e, Ln;
}
var Mr = { exports: {} };
Mr.exports;
var Cc;
function Ko() {
  return Cc || (Cc = 1, function(e, r) {
    var t = Lh(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i && t.process, o = function() {
      try {
        var u = a && a.require && a.require("util").types;
        return u || s && s.binding && s.binding("util");
      } catch {
      }
    }();
    e.exports = o;
  }(Mr, Mr.exports)), Mr.exports;
}
var Fn, Sc;
function Kr() {
  if (Sc) return Fn;
  Sc = 1;
  var e = Rg(), r = yt(), t = Ko(), n = t && t.isTypedArray, a = n ? r(n) : e;
  return Fn = a, Fn;
}
var jn, Tc;
function $h() {
  if (Tc) return jn;
  Tc = 1;
  var e = Cg(), r = Ur(), t = be(), n = Sr(), a = gt(), i = Kr(), s = Object.prototype, o = s.hasOwnProperty;
  function u(l, c) {
    var d = t(l), h = !d && r(l), v = !d && !h && n(l), g = !d && !h && !v && i(l), f = d || h || v || g, y = f ? e(l.length, String) : [], q = y.length;
    for (var T in l)
      (c || o.call(l, T)) && !(f && // Safari 9 has enumerable `arguments.length` in strict mode.
      (T == "length" || // Node.js 0.10 has enumerable non-index properties on buffers.
      v && (T == "offset" || T == "parent") || // PhantomJS 2 has enumerable non-index properties on typed arrays.
      g && (T == "buffer" || T == "byteLength" || T == "byteOffset") || // Skip index properties.
      a(T, q))) && y.push(T);
    return y;
  }
  return jn = u, jn;
}
var $n, Rc;
function mt() {
  if (Rc) return $n;
  Rc = 1;
  var e = Object.prototype;
  function r(t) {
    var n = t && t.constructor, a = typeof n == "function" && n.prototype || e;
    return t === a;
  }
  return $n = r, $n;
}
var Gn, xc;
function Gh() {
  if (xc) return Gn;
  xc = 1;
  function e(r, t) {
    return function(n) {
      return r(t(n));
    };
  }
  return Gn = e, Gn;
}
var zn, kc;
function xg() {
  if (kc) return zn;
  kc = 1;
  var e = Gh(), r = e(Object.keys, Object);
  return zn = r, zn;
}
var Bn, Ac;
function Ho() {
  if (Ac) return Bn;
  Ac = 1;
  var e = mt(), r = xg(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i) {
    if (!e(i))
      return r(i);
    var s = [];
    for (var o in Object(i))
      n.call(i, o) && o != "constructor" && s.push(o);
    return s;
  }
  return Bn = a, Bn;
}
var Un, Oc;
function rr() {
  if (Oc) return Un;
  Oc = 1;
  var e = zr(), r = Uo();
  function t(n) {
    return n != null && r(n.length) && !e(n);
  }
  return Un = t, Un;
}
var Kn, Pc;
function cr() {
  if (Pc) return Kn;
  Pc = 1;
  var e = $h(), r = Ho(), t = rr();
  function n(a) {
    return t(a) ? e(a) : r(a);
  }
  return Kn = n, Kn;
}
var Hn, Nc;
function kg() {
  if (Nc) return Hn;
  Nc = 1;
  var e = Br(), r = cr();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return Hn = t, Hn;
}
var Vn, Mc;
function Ag() {
  if (Mc) return Vn;
  Mc = 1;
  function e(r) {
    var t = [];
    if (r != null)
      for (var n in Object(r))
        t.push(n);
    return t;
  }
  return Vn = e, Vn;
}
var Wn, Dc;
function Og() {
  if (Dc) return Wn;
  Dc = 1;
  var e = De(), r = mt(), t = Ag(), n = Object.prototype, a = n.hasOwnProperty;
  function i(s) {
    if (!e(s))
      return t(s);
    var o = r(s), u = [];
    for (var l in s)
      l == "constructor" && (o || !a.call(s, l)) || u.push(l);
    return u;
  }
  return Wn = i, Wn;
}
var Qn, Lc;
function vr() {
  if (Lc) return Qn;
  Lc = 1;
  var e = $h(), r = Og(), t = rr();
  function n(a) {
    return t(a) ? e(a, !0) : r(a);
  }
  return Qn = n, Qn;
}
var Yn, Fc;
function Pg() {
  if (Fc) return Yn;
  Fc = 1;
  var e = Br(), r = vr();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return Yn = t, Yn;
}
var Dr = { exports: {} };
Dr.exports;
var jc;
function zh() {
  return jc || (jc = 1, function(e, r) {
    var t = Fe(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i ? t.Buffer : void 0, o = s ? s.allocUnsafe : void 0;
    function u(l, c) {
      if (c)
        return l.slice();
      var d = l.length, h = o ? o(d) : new l.constructor(d);
      return l.copy(h), h;
    }
    e.exports = u;
  }(Dr, Dr.exports)), Dr.exports;
}
var Jn, $c;
function Bh() {
  if ($c) return Jn;
  $c = 1;
  function e(r, t) {
    var n = -1, a = r.length;
    for (t || (t = Array(a)); ++n < a; )
      t[n] = r[n];
    return t;
  }
  return Jn = e, Jn;
}
var Xn, Gc;
function Uh() {
  if (Gc) return Xn;
  Gc = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = 0, s = []; ++n < a; ) {
      var o = r[n];
      t(o, n, r) && (s[i++] = o);
    }
    return s;
  }
  return Xn = e, Xn;
}
var Zn, zc;
function Kh() {
  if (zc) return Zn;
  zc = 1;
  function e() {
    return [];
  }
  return Zn = e, Zn;
}
var ea, Bc;
function Vo() {
  if (Bc) return ea;
  Bc = 1;
  var e = Uh(), r = Kh(), t = Object.prototype, n = t.propertyIsEnumerable, a = Object.getOwnPropertySymbols, i = a ? function(s) {
    return s == null ? [] : (s = Object(s), e(a(s), function(o) {
      return n.call(s, o);
    }));
  } : r;
  return ea = i, ea;
}
var ra, Uc;
function Ng() {
  if (Uc) return ra;
  Uc = 1;
  var e = Br(), r = Vo();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return ra = t, ra;
}
var ta, Kc;
function Wo() {
  if (Kc) return ta;
  Kc = 1;
  function e(r, t) {
    for (var n = -1, a = t.length, i = r.length; ++n < a; )
      r[i + n] = t[n];
    return r;
  }
  return ta = e, ta;
}
var na, Hc;
function bt() {
  if (Hc) return na;
  Hc = 1;
  var e = Gh(), r = e(Object.getPrototypeOf, Object);
  return na = r, na;
}
var aa, Vc;
function Hh() {
  if (Vc) return aa;
  Vc = 1;
  var e = Wo(), r = bt(), t = Vo(), n = Kh(), a = Object.getOwnPropertySymbols, i = a ? function(s) {
    for (var o = []; s; )
      e(o, t(s)), s = r(s);
    return o;
  } : n;
  return aa = i, aa;
}
var ia, Wc;
function Mg() {
  if (Wc) return ia;
  Wc = 1;
  var e = Br(), r = Hh();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return ia = t, ia;
}
var sa, Qc;
function Vh() {
  if (Qc) return sa;
  Qc = 1;
  var e = Wo(), r = be();
  function t(n, a, i) {
    var s = a(n);
    return r(n) ? s : e(s, i(n));
  }
  return sa = t, sa;
}
var oa, Yc;
function Wh() {
  if (Yc) return oa;
  Yc = 1;
  var e = Vh(), r = Vo(), t = cr();
  function n(a) {
    return e(a, t, r);
  }
  return oa = n, oa;
}
var ua, Jc;
function Dg() {
  if (Jc) return ua;
  Jc = 1;
  var e = Vh(), r = Hh(), t = vr();
  function n(a) {
    return e(a, t, r);
  }
  return ua = n, ua;
}
var ca, Xc;
function Lg() {
  if (Xc) return ca;
  Xc = 1;
  var e = pr(), r = Fe(), t = e(r, "DataView");
  return ca = t, ca;
}
var la, Zc;
function Fg() {
  if (Zc) return la;
  Zc = 1;
  var e = pr(), r = Fe(), t = e(r, "Promise");
  return la = t, la;
}
var da, el;
function Qh() {
  if (el) return da;
  el = 1;
  var e = pr(), r = Fe(), t = e(r, "Set");
  return da = t, da;
}
var fa, rl;
function jg() {
  if (rl) return fa;
  rl = 1;
  var e = pr(), r = Fe(), t = e(r, "WeakMap");
  return fa = t, fa;
}
var ha, tl;
function Tr() {
  if (tl) return ha;
  tl = 1;
  var e = Lg(), r = Go(), t = Fg(), n = Qh(), a = jg(), i = hr(), s = Fh(), o = "[object Map]", u = "[object Object]", l = "[object Promise]", c = "[object Set]", d = "[object WeakMap]", h = "[object DataView]", v = s(e), g = s(r), f = s(t), y = s(n), q = s(a), T = i;
  return (e && T(new e(new ArrayBuffer(1))) != h || r && T(new r()) != o || t && T(t.resolve()) != l || n && T(new n()) != c || a && T(new a()) != d) && (T = function(_) {
    var x = i(_), N = x == u ? _.constructor : void 0, m = N ? s(N) : "";
    if (m)
      switch (m) {
        case v:
          return h;
        case g:
          return o;
        case f:
          return l;
        case y:
          return c;
        case q:
          return d;
      }
    return x;
  }), ha = T, ha;
}
var pa, nl;
function $g() {
  if (nl) return pa;
  nl = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n) {
    var a = n.length, i = new n.constructor(a);
    return a && typeof n[0] == "string" && r.call(n, "index") && (i.index = n.index, i.input = n.input), i;
  }
  return pa = t, pa;
}
var va, al;
function Yh() {
  if (al) return va;
  al = 1;
  var e = Fe(), r = e.Uint8Array;
  return va = r, va;
}
var ga, il;
function Qo() {
  if (il) return ga;
  il = 1;
  var e = Yh();
  function r(t) {
    var n = new t.constructor(t.byteLength);
    return new e(n).set(new e(t)), n;
  }
  return ga = r, ga;
}
var ya, sl;
function Gg() {
  if (sl) return ya;
  sl = 1;
  var e = Qo();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.byteLength);
  }
  return ya = r, ya;
}
var ma, ol;
function zg() {
  if (ol) return ma;
  ol = 1;
  var e = /\w*$/;
  function r(t) {
    var n = new t.constructor(t.source, e.exec(t));
    return n.lastIndex = t.lastIndex, n;
  }
  return ma = r, ma;
}
var ba, ul;
function Bg() {
  if (ul) return ba;
  ul = 1;
  var e = Cr(), r = e ? e.prototype : void 0, t = r ? r.valueOf : void 0;
  function n(a) {
    return t ? Object(t.call(a)) : {};
  }
  return ba = n, ba;
}
var _a, cl;
function Jh() {
  if (cl) return _a;
  cl = 1;
  var e = Qo();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.length);
  }
  return _a = r, _a;
}
var wa, ll;
function Ug() {
  if (ll) return wa;
  ll = 1;
  var e = Qo(), r = Gg(), t = zg(), n = Bg(), a = Jh(), i = "[object Boolean]", s = "[object Date]", o = "[object Map]", u = "[object Number]", l = "[object RegExp]", c = "[object Set]", d = "[object String]", h = "[object Symbol]", v = "[object ArrayBuffer]", g = "[object DataView]", f = "[object Float32Array]", y = "[object Float64Array]", q = "[object Int8Array]", T = "[object Int16Array]", _ = "[object Int32Array]", x = "[object Uint8Array]", N = "[object Uint8ClampedArray]", m = "[object Uint16Array]", S = "[object Uint32Array]";
  function M(B, z, w) {
    var D = B.constructor;
    switch (z) {
      case v:
        return e(B);
      case i:
      case s:
        return new D(+B);
      case g:
        return r(B, w);
      case f:
      case y:
      case q:
      case T:
      case _:
      case x:
      case N:
      case m:
      case S:
        return a(B, w);
      case o:
        return new D();
      case u:
      case d:
        return new D(B);
      case l:
        return t(B);
      case c:
        return new D();
      case h:
        return n(B);
    }
  }
  return wa = M, wa;
}
var Ia, dl;
function Xh() {
  if (dl) return Ia;
  dl = 1;
  var e = De(), r = Object.create, t = /* @__PURE__ */ function() {
    function n() {
    }
    return function(a) {
      if (!e(a))
        return {};
      if (r)
        return r(a);
      n.prototype = a;
      var i = new n();
      return n.prototype = void 0, i;
    };
  }();
  return Ia = t, Ia;
}
var qa, fl;
function Zh() {
  if (fl) return qa;
  fl = 1;
  var e = Xh(), r = bt(), t = mt();
  function n(a) {
    return typeof a.constructor == "function" && !t(a) ? e(r(a)) : {};
  }
  return qa = n, qa;
}
var Ea, hl;
function Kg() {
  if (hl) return Ea;
  hl = 1;
  var e = Tr(), r = He(), t = "[object Map]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return Ea = n, Ea;
}
var Ca, pl;
function Hg() {
  if (pl) return Ca;
  pl = 1;
  var e = Kg(), r = yt(), t = Ko(), n = t && t.isMap, a = n ? r(n) : e;
  return Ca = a, Ca;
}
var Sa, vl;
function Vg() {
  if (vl) return Sa;
  vl = 1;
  var e = Tr(), r = He(), t = "[object Set]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return Sa = n, Sa;
}
var Ta, gl;
function Wg() {
  if (gl) return Ta;
  gl = 1;
  var e = Vg(), r = yt(), t = Ko(), n = t && t.isSet, a = n ? r(n) : e;
  return Ta = a, Ta;
}
var Ra, yl;
function ep() {
  if (yl) return Ra;
  yl = 1;
  var e = ht(), r = Bo(), t = vt(), n = kg(), a = Pg(), i = zh(), s = Bh(), o = Ng(), u = Mg(), l = Wh(), c = Dg(), d = Tr(), h = $g(), v = Ug(), g = Zh(), f = be(), y = Sr(), q = Hg(), T = De(), _ = Wg(), x = cr(), N = vr(), m = 1, S = 2, M = 4, B = "[object Arguments]", z = "[object Array]", w = "[object Boolean]", D = "[object Date]", L = "[object Error]", J = "[object Function]", re = "[object GeneratorFunction]", U = "[object Map]", Q = "[object Number]", V = "[object Object]", C = "[object RegExp]", F = "[object Set]", A = "[object String]", Y = "[object Symbol]", ae = "[object WeakMap]", Ve = "[object ArrayBuffer]", qe = "[object DataView]", mr = "[object Float32Array]", xr = "[object Float64Array]", kr = "[object Int8Array]", lr = "[object Int16Array]", br = "[object Int32Array]", O = "[object Uint8Array]", pe = "[object Uint8ClampedArray]", ve = "[object Uint16Array]", _e = "[object Uint32Array]", W = {};
  W[B] = W[z] = W[Ve] = W[qe] = W[w] = W[D] = W[mr] = W[xr] = W[kr] = W[lr] = W[br] = W[U] = W[Q] = W[V] = W[C] = W[F] = W[A] = W[Y] = W[O] = W[pe] = W[ve] = W[_e] = !0, W[L] = W[J] = W[ae] = !1;
  function oe(ee, Oe, We, _r, wr, $e) {
    var Ee, ue = Oe & m, nr = Oe & S, Ir = Oe & M;
    if (We && (Ee = wr ? We(ee, _r, wr, $e) : We(ee)), Ee !== void 0)
      return Ee;
    if (!T(ee))
      return ee;
    var Vr = f(ee);
    if (Vr) {
      if (Ee = h(ee), !ue)
        return s(ee, Ee);
    } else {
      var Ge = d(ee), Wr = Ge == J || Ge == re;
      if (y(ee))
        return i(ee, ue);
      if (Ge == V || Ge == B || Wr && !wr) {
        if (Ee = nr || Wr ? {} : g(ee), !ue)
          return nr ? u(ee, a(Ee, ee)) : o(ee, n(Ee, ee));
      } else {
        if (!W[Ge])
          return wr ? ee : {};
        Ee = v(ee, Ge, ue);
      }
    }
    $e || ($e = new e());
    var Ar = $e.get(ee);
    if (Ar)
      return Ar;
    $e.set(ee, Ee), _(ee) ? ee.forEach(function(ze) {
      Ee.add(oe(ze, Oe, We, ze, ee, $e));
    }) : q(ee) && ee.forEach(function(ze, Qe) {
      Ee.set(Qe, oe(ze, Oe, We, Qe, ee, $e));
    });
    var Rt = Ir ? nr ? c : l : nr ? N : x, Qr = Vr ? void 0 : Rt(ee);
    return r(Qr || ee, function(ze, Qe) {
      Qr && (Qe = ze, ze = ee[Qe]), t(Ee, Qe, oe(ze, Oe, We, Qe, ee, $e));
    }), Ee;
  }
  return Ra = oe, Ra;
}
var xa, ml;
function Qg() {
  if (ml) return xa;
  ml = 1;
  var e = ep(), r = 4;
  function t(n) {
    return e(n, r);
  }
  return xa = t, xa;
}
var ka, bl;
function Yo() {
  if (bl) return ka;
  bl = 1;
  function e(r) {
    return function() {
      return r;
    };
  }
  return ka = e, ka;
}
var Aa, _l;
function Yg() {
  if (_l) return Aa;
  _l = 1;
  function e(r) {
    return function(t, n, a) {
      for (var i = -1, s = Object(t), o = a(t), u = o.length; u--; ) {
        var l = o[r ? u : ++i];
        if (n(s[l], l, s) === !1)
          break;
      }
      return t;
    };
  }
  return Aa = e, Aa;
}
var Oa, wl;
function Jo() {
  if (wl) return Oa;
  wl = 1;
  var e = Yg(), r = e();
  return Oa = r, Oa;
}
var Pa, Il;
function Xo() {
  if (Il) return Pa;
  Il = 1;
  var e = Jo(), r = cr();
  function t(n, a) {
    return n && e(n, a, r);
  }
  return Pa = t, Pa;
}
var Na, ql;
function Jg() {
  if (ql) return Na;
  ql = 1;
  var e = rr();
  function r(t, n) {
    return function(a, i) {
      if (a == null)
        return a;
      if (!e(a))
        return t(a, i);
      for (var s = a.length, o = n ? s : -1, u = Object(a); (n ? o-- : ++o < s) && i(u[o], o, u) !== !1; )
        ;
      return a;
    };
  }
  return Na = r, Na;
}
var Ma, El;
function _t() {
  if (El) return Ma;
  El = 1;
  var e = Xo(), r = Jg(), t = r(e);
  return Ma = t, Ma;
}
var Da, Cl;
function gr() {
  if (Cl) return Da;
  Cl = 1;
  function e(r) {
    return r;
  }
  return Da = e, Da;
}
var La, Sl;
function rp() {
  if (Sl) return La;
  Sl = 1;
  var e = gr();
  function r(t) {
    return typeof t == "function" ? t : e;
  }
  return La = r, La;
}
var Fa, Tl;
function tp() {
  if (Tl) return Fa;
  Tl = 1;
  var e = Bo(), r = _t(), t = rp(), n = be();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s));
  }
  return Fa = a, Fa;
}
var ja, Rl;
function np() {
  return Rl || (Rl = 1, ja = tp()), ja;
}
var $a, xl;
function Xg() {
  if (xl) return $a;
  xl = 1;
  var e = _t();
  function r(t, n) {
    var a = [];
    return e(t, function(i, s, o) {
      n(i, s, o) && a.push(i);
    }), a;
  }
  return $a = r, $a;
}
var Ga, kl;
function Zg() {
  if (kl) return Ga;
  kl = 1;
  var e = "__lodash_hash_undefined__";
  function r(t) {
    return this.__data__.set(t, e), this;
  }
  return Ga = r, Ga;
}
var za, Al;
function ey() {
  if (Al) return za;
  Al = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return za = e, za;
}
var Ba, Ol;
function ap() {
  if (Ol) return Ba;
  Ol = 1;
  var e = zo(), r = Zg(), t = ey();
  function n(a) {
    var i = -1, s = a == null ? 0 : a.length;
    for (this.__data__ = new e(); ++i < s; )
      this.add(a[i]);
  }
  return n.prototype.add = n.prototype.push = r, n.prototype.has = t, Ba = n, Ba;
}
var Ua, Pl;
function ry() {
  if (Pl) return Ua;
  Pl = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a; )
      if (t(r[n], n, r))
        return !0;
    return !1;
  }
  return Ua = e, Ua;
}
var Ka, Nl;
function ip() {
  if (Nl) return Ka;
  Nl = 1;
  function e(r, t) {
    return r.has(t);
  }
  return Ka = e, Ka;
}
var Ha, Ml;
function sp() {
  if (Ml) return Ha;
  Ml = 1;
  var e = ap(), r = ry(), t = ip(), n = 1, a = 2;
  function i(s, o, u, l, c, d) {
    var h = u & n, v = s.length, g = o.length;
    if (v != g && !(h && g > v))
      return !1;
    var f = d.get(s), y = d.get(o);
    if (f && y)
      return f == o && y == s;
    var q = -1, T = !0, _ = u & a ? new e() : void 0;
    for (d.set(s, o), d.set(o, s); ++q < v; ) {
      var x = s[q], N = o[q];
      if (l)
        var m = h ? l(N, x, q, o, s, d) : l(x, N, q, s, o, d);
      if (m !== void 0) {
        if (m)
          continue;
        T = !1;
        break;
      }
      if (_) {
        if (!r(o, function(S, M) {
          if (!t(_, M) && (x === S || c(x, S, u, l, d)))
            return _.push(M);
        })) {
          T = !1;
          break;
        }
      } else if (!(x === N || c(x, N, u, l, d))) {
        T = !1;
        break;
      }
    }
    return d.delete(s), d.delete(o), T;
  }
  return Ha = i, Ha;
}
var Va, Dl;
function ty() {
  if (Dl) return Va;
  Dl = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a, i) {
      n[++t] = [i, a];
    }), n;
  }
  return Va = e, Va;
}
var Wa, Ll;
function Zo() {
  if (Ll) return Wa;
  Ll = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a) {
      n[++t] = a;
    }), n;
  }
  return Wa = e, Wa;
}
var Qa, Fl;
function ny() {
  if (Fl) return Qa;
  Fl = 1;
  var e = Cr(), r = Yh(), t = Er(), n = sp(), a = ty(), i = Zo(), s = 1, o = 2, u = "[object Boolean]", l = "[object Date]", c = "[object Error]", d = "[object Map]", h = "[object Number]", v = "[object RegExp]", g = "[object Set]", f = "[object String]", y = "[object Symbol]", q = "[object ArrayBuffer]", T = "[object DataView]", _ = e ? e.prototype : void 0, x = _ ? _.valueOf : void 0;
  function N(m, S, M, B, z, w, D) {
    switch (M) {
      case T:
        if (m.byteLength != S.byteLength || m.byteOffset != S.byteOffset)
          return !1;
        m = m.buffer, S = S.buffer;
      case q:
        return !(m.byteLength != S.byteLength || !w(new r(m), new r(S)));
      case u:
      case l:
      case h:
        return t(+m, +S);
      case c:
        return m.name == S.name && m.message == S.message;
      case v:
      case f:
        return m == S + "";
      case d:
        var L = a;
      case g:
        var J = B & s;
        if (L || (L = i), m.size != S.size && !J)
          return !1;
        var re = D.get(m);
        if (re)
          return re == S;
        B |= o, D.set(m, S);
        var U = n(L(m), L(S), B, z, w, D);
        return D.delete(m), U;
      case y:
        if (x)
          return x.call(m) == x.call(S);
    }
    return !1;
  }
  return Qa = N, Qa;
}
var Ya, jl;
function ay() {
  if (jl) return Ya;
  jl = 1;
  var e = Wh(), r = 1, t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o, u, l, c) {
    var d = o & r, h = e(i), v = h.length, g = e(s), f = g.length;
    if (v != f && !d)
      return !1;
    for (var y = v; y--; ) {
      var q = h[y];
      if (!(d ? q in s : n.call(s, q)))
        return !1;
    }
    var T = c.get(i), _ = c.get(s);
    if (T && _)
      return T == s && _ == i;
    var x = !0;
    c.set(i, s), c.set(s, i);
    for (var N = d; ++y < v; ) {
      q = h[y];
      var m = i[q], S = s[q];
      if (u)
        var M = d ? u(S, m, q, s, i, c) : u(m, S, q, i, s, c);
      if (!(M === void 0 ? m === S || l(m, S, o, u, c) : M)) {
        x = !1;
        break;
      }
      N || (N = q == "constructor");
    }
    if (x && !N) {
      var B = i.constructor, z = s.constructor;
      B != z && "constructor" in i && "constructor" in s && !(typeof B == "function" && B instanceof B && typeof z == "function" && z instanceof z) && (x = !1);
    }
    return c.delete(i), c.delete(s), x;
  }
  return Ya = a, Ya;
}
var Ja, $l;
function iy() {
  if ($l) return Ja;
  $l = 1;
  var e = ht(), r = sp(), t = ny(), n = ay(), a = Tr(), i = be(), s = Sr(), o = Kr(), u = 1, l = "[object Arguments]", c = "[object Array]", d = "[object Object]", h = Object.prototype, v = h.hasOwnProperty;
  function g(f, y, q, T, _, x) {
    var N = i(f), m = i(y), S = N ? c : a(f), M = m ? c : a(y);
    S = S == l ? d : S, M = M == l ? d : M;
    var B = S == d, z = M == d, w = S == M;
    if (w && s(f)) {
      if (!s(y))
        return !1;
      N = !0, B = !1;
    }
    if (w && !B)
      return x || (x = new e()), N || o(f) ? r(f, y, q, T, _, x) : t(f, y, S, q, T, _, x);
    if (!(q & u)) {
      var D = B && v.call(f, "__wrapped__"), L = z && v.call(y, "__wrapped__");
      if (D || L) {
        var J = D ? f.value() : f, re = L ? y.value() : y;
        return x || (x = new e()), _(J, re, q, T, x);
      }
    }
    return w ? (x || (x = new e()), n(f, y, q, T, _, x)) : !1;
  }
  return Ja = g, Ja;
}
var Xa, Gl;
function op() {
  if (Gl) return Xa;
  Gl = 1;
  var e = iy(), r = He();
  function t(n, a, i, s, o) {
    return n === a ? !0 : n == null || a == null || !r(n) && !r(a) ? n !== n && a !== a : e(n, a, i, s, t, o);
  }
  return Xa = t, Xa;
}
var Za, zl;
function sy() {
  if (zl) return Za;
  zl = 1;
  var e = ht(), r = op(), t = 1, n = 2;
  function a(i, s, o, u) {
    var l = o.length, c = l, d = !u;
    if (i == null)
      return !c;
    for (i = Object(i); l--; ) {
      var h = o[l];
      if (d && h[2] ? h[1] !== i[h[0]] : !(h[0] in i))
        return !1;
    }
    for (; ++l < c; ) {
      h = o[l];
      var v = h[0], g = i[v], f = h[1];
      if (d && h[2]) {
        if (g === void 0 && !(v in i))
          return !1;
      } else {
        var y = new e();
        if (u)
          var q = u(g, f, v, i, s, y);
        if (!(q === void 0 ? r(f, g, t | n, u, y) : q))
          return !1;
      }
    }
    return !0;
  }
  return Za = a, Za;
}
var ei, Bl;
function up() {
  if (Bl) return ei;
  Bl = 1;
  var e = De();
  function r(t) {
    return t === t && !e(t);
  }
  return ei = r, ei;
}
var ri, Ul;
function oy() {
  if (Ul) return ri;
  Ul = 1;
  var e = up(), r = cr();
  function t(n) {
    for (var a = r(n), i = a.length; i--; ) {
      var s = a[i], o = n[s];
      a[i] = [s, o, e(o)];
    }
    return a;
  }
  return ri = t, ri;
}
var ti, Kl;
function cp() {
  if (Kl) return ti;
  Kl = 1;
  function e(r, t) {
    return function(n) {
      return n == null ? !1 : n[r] === t && (t !== void 0 || r in Object(n));
    };
  }
  return ti = e, ti;
}
var ni, Hl;
function uy() {
  if (Hl) return ni;
  Hl = 1;
  var e = sy(), r = oy(), t = cp();
  function n(a) {
    var i = r(a);
    return i.length == 1 && i[0][2] ? t(i[0][0], i[0][1]) : function(s) {
      return s === a || e(s, a, i);
    };
  }
  return ni = n, ni;
}
var ai, Vl;
function Rr() {
  if (Vl) return ai;
  Vl = 1;
  var e = hr(), r = He(), t = "[object Symbol]";
  function n(a) {
    return typeof a == "symbol" || r(a) && e(a) == t;
  }
  return ai = n, ai;
}
var ii, Wl;
function eu() {
  if (Wl) return ii;
  Wl = 1;
  var e = be(), r = Rr(), t = /\.|\[(?:[^[\]]*|(["'])(?:(?!\1)[^\\]|\\.)*?\1)\]/, n = /^\w*$/;
  function a(i, s) {
    if (e(i))
      return !1;
    var o = typeof i;
    return o == "number" || o == "symbol" || o == "boolean" || i == null || r(i) ? !0 : n.test(i) || !t.test(i) || s != null && i in Object(s);
  }
  return ii = a, ii;
}
var si, Ql;
function cy() {
  if (Ql) return si;
  Ql = 1;
  var e = zo(), r = "Expected a function";
  function t(n, a) {
    if (typeof n != "function" || a != null && typeof a != "function")
      throw new TypeError(r);
    var i = function() {
      var s = arguments, o = a ? a.apply(this, s) : s[0], u = i.cache;
      if (u.has(o))
        return u.get(o);
      var l = n.apply(this, s);
      return i.cache = u.set(o, l) || u, l;
    };
    return i.cache = new (t.Cache || e)(), i;
  }
  return t.Cache = e, si = t, si;
}
var oi, Yl;
function ly() {
  if (Yl) return oi;
  Yl = 1;
  var e = cy(), r = 500;
  function t(n) {
    var a = e(n, function(s) {
      return i.size === r && i.clear(), s;
    }), i = a.cache;
    return a;
  }
  return oi = t, oi;
}
var ui, Jl;
function dy() {
  if (Jl) return ui;
  Jl = 1;
  var e = ly(), r = /[^.[\]]+|\[(?:(-?\d+(?:\.\d+)?)|(["'])((?:(?!\2)[^\\]|\\.)*?)\2)\]|(?=(?:\.|\[\])(?:\.|\[\]|$))/g, t = /\\(\\)?/g, n = e(function(a) {
    var i = [];
    return a.charCodeAt(0) === 46 && i.push(""), a.replace(r, function(s, o, u, l) {
      i.push(u ? l.replace(t, "$1") : o || s);
    }), i;
  });
  return ui = n, ui;
}
var ci, Xl;
function wt() {
  if (Xl) return ci;
  Xl = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = Array(a); ++n < a; )
      i[n] = t(r[n], n, r);
    return i;
  }
  return ci = e, ci;
}
var li, Zl;
function fy() {
  if (Zl) return li;
  Zl = 1;
  var e = Cr(), r = wt(), t = be(), n = Rr(), a = e ? e.prototype : void 0, i = a ? a.toString : void 0;
  function s(o) {
    if (typeof o == "string")
      return o;
    if (t(o))
      return r(o, s) + "";
    if (n(o))
      return i ? i.call(o) : "";
    var u = o + "";
    return u == "0" && 1 / o == -1 / 0 ? "-0" : u;
  }
  return li = s, li;
}
var di, ed;
function lp() {
  if (ed) return di;
  ed = 1;
  var e = fy();
  function r(t) {
    return t == null ? "" : e(t);
  }
  return di = r, di;
}
var fi, rd;
function It() {
  if (rd) return fi;
  rd = 1;
  var e = be(), r = eu(), t = dy(), n = lp();
  function a(i, s) {
    return e(i) ? i : r(i, s) ? [i] : t(n(i));
  }
  return fi = a, fi;
}
var hi, td;
function Hr() {
  if (td) return hi;
  td = 1;
  var e = Rr();
  function r(t) {
    if (typeof t == "string" || e(t))
      return t;
    var n = t + "";
    return n == "0" && 1 / t == -1 / 0 ? "-0" : n;
  }
  return hi = r, hi;
}
var pi, nd;
function qt() {
  if (nd) return pi;
  nd = 1;
  var e = It(), r = Hr();
  function t(n, a) {
    a = e(a, n);
    for (var i = 0, s = a.length; n != null && i < s; )
      n = n[r(a[i++])];
    return i && i == s ? n : void 0;
  }
  return pi = t, pi;
}
var vi, ad;
function hy() {
  if (ad) return vi;
  ad = 1;
  var e = qt();
  function r(t, n, a) {
    var i = t == null ? void 0 : e(t, n);
    return i === void 0 ? a : i;
  }
  return vi = r, vi;
}
var gi, id;
function py() {
  if (id) return gi;
  id = 1;
  function e(r, t) {
    return r != null && t in Object(r);
  }
  return gi = e, gi;
}
var yi, sd;
function dp() {
  if (sd) return yi;
  sd = 1;
  var e = It(), r = Ur(), t = be(), n = gt(), a = Uo(), i = Hr();
  function s(o, u, l) {
    u = e(u, o);
    for (var c = -1, d = u.length, h = !1; ++c < d; ) {
      var v = i(u[c]);
      if (!(h = o != null && l(o, v)))
        break;
      o = o[v];
    }
    return h || ++c != d ? h : (d = o == null ? 0 : o.length, !!d && a(d) && n(v, d) && (t(o) || r(o)));
  }
  return yi = s, yi;
}
var mi, od;
function fp() {
  if (od) return mi;
  od = 1;
  var e = py(), r = dp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return mi = t, mi;
}
var bi, ud;
function vy() {
  if (ud) return bi;
  ud = 1;
  var e = op(), r = hy(), t = fp(), n = eu(), a = up(), i = cp(), s = Hr(), o = 1, u = 2;
  function l(c, d) {
    return n(c) && a(d) ? i(s(c), d) : function(h) {
      var v = r(h, c);
      return v === void 0 && v === d ? t(h, c) : e(d, v, o | u);
    };
  }
  return bi = l, bi;
}
var _i, cd;
function hp() {
  if (cd) return _i;
  cd = 1;
  function e(r) {
    return function(t) {
      return t == null ? void 0 : t[r];
    };
  }
  return _i = e, _i;
}
var wi, ld;
function gy() {
  if (ld) return wi;
  ld = 1;
  var e = qt();
  function r(t) {
    return function(n) {
      return e(n, t);
    };
  }
  return wi = r, wi;
}
var Ii, dd;
function yy() {
  if (dd) return Ii;
  dd = 1;
  var e = hp(), r = gy(), t = eu(), n = Hr();
  function a(i) {
    return t(i) ? e(n(i)) : r(i);
  }
  return Ii = a, Ii;
}
var qi, fd;
function tr() {
  if (fd) return qi;
  fd = 1;
  var e = uy(), r = vy(), t = gr(), n = be(), a = yy();
  function i(s) {
    return typeof s == "function" ? s : s == null ? t : typeof s == "object" ? n(s) ? r(s[0], s[1]) : e(s) : a(s);
  }
  return qi = i, qi;
}
var Ei, hd;
function pp() {
  if (hd) return Ei;
  hd = 1;
  var e = Uh(), r = Xg(), t = tr(), n = be();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s, 3));
  }
  return Ei = a, Ei;
}
var Ci, pd;
function my() {
  if (pd) return Ci;
  pd = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n, a) {
    return n != null && r.call(n, a);
  }
  return Ci = t, Ci;
}
var Si, vd;
function vp() {
  if (vd) return Si;
  vd = 1;
  var e = my(), r = dp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return Si = t, Si;
}
var Ti, gd;
function by() {
  if (gd) return Ti;
  gd = 1;
  var e = Ho(), r = Tr(), t = Ur(), n = be(), a = rr(), i = Sr(), s = mt(), o = Kr(), u = "[object Map]", l = "[object Set]", c = Object.prototype, d = c.hasOwnProperty;
  function h(v) {
    if (v == null)
      return !0;
    if (a(v) && (n(v) || typeof v == "string" || typeof v.splice == "function" || i(v) || o(v) || t(v)))
      return !v.length;
    var g = r(v);
    if (g == u || g == l)
      return !v.size;
    if (s(v))
      return !e(v).length;
    for (var f in v)
      if (d.call(v, f))
        return !1;
    return !0;
  }
  return Ti = h, Ti;
}
var Ri, yd;
function gp() {
  if (yd) return Ri;
  yd = 1;
  function e(r) {
    return r === void 0;
  }
  return Ri = e, Ri;
}
var xi, md;
function yp() {
  if (md) return xi;
  md = 1;
  var e = _t(), r = rr();
  function t(n, a) {
    var i = -1, s = r(n) ? Array(n.length) : [];
    return e(n, function(o, u, l) {
      s[++i] = a(o, u, l);
    }), s;
  }
  return xi = t, xi;
}
var ki, bd;
function mp() {
  if (bd) return ki;
  bd = 1;
  var e = wt(), r = tr(), t = yp(), n = be();
  function a(i, s) {
    var o = n(i) ? e : t;
    return o(i, r(s, 3));
  }
  return ki = a, ki;
}
var Ai, _d;
function _y() {
  if (_d) return Ai;
  _d = 1;
  function e(r, t, n, a) {
    var i = -1, s = r == null ? 0 : r.length;
    for (a && s && (n = r[++i]); ++i < s; )
      n = t(n, r[i], i, r);
    return n;
  }
  return Ai = e, Ai;
}
var Oi, wd;
function wy() {
  if (wd) return Oi;
  wd = 1;
  function e(r, t, n, a, i) {
    return i(r, function(s, o, u) {
      n = a ? (a = !1, s) : t(n, s, o, u);
    }), n;
  }
  return Oi = e, Oi;
}
var Pi, Id;
function bp() {
  if (Id) return Pi;
  Id = 1;
  var e = _y(), r = _t(), t = tr(), n = wy(), a = be();
  function i(s, o, u) {
    var l = a(s) ? e : n, c = arguments.length < 3;
    return l(s, t(o, 4), u, c, r);
  }
  return Pi = i, Pi;
}
var Ni, qd;
function Iy() {
  if (qd) return Ni;
  qd = 1;
  var e = hr(), r = be(), t = He(), n = "[object String]";
  function a(i) {
    return typeof i == "string" || !r(i) && t(i) && e(i) == n;
  }
  return Ni = a, Ni;
}
var Mi, Ed;
function qy() {
  if (Ed) return Mi;
  Ed = 1;
  var e = hp(), r = e("length");
  return Mi = r, Mi;
}
var Di, Cd;
function Ey() {
  if (Cd) return Di;
  Cd = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "\\u200d", o = RegExp("[" + s + e + a + i + "]");
  function u(l) {
    return o.test(l);
  }
  return Di = u, Di;
}
var Li, Sd;
function Cy() {
  if (Sd) return Li;
  Sd = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "[" + e + "]", o = "[" + a + "]", u = "\\ud83c[\\udffb-\\udfff]", l = "(?:" + o + "|" + u + ")", c = "[^" + e + "]", d = "(?:\\ud83c[\\udde6-\\uddff]){2}", h = "[\\ud800-\\udbff][\\udc00-\\udfff]", v = "\\u200d", g = l + "?", f = "[" + i + "]?", y = "(?:" + v + "(?:" + [c, d, h].join("|") + ")" + f + g + ")*", q = f + g + y, T = "(?:" + [c + o + "?", o, d, h, s].join("|") + ")", _ = RegExp(u + "(?=" + u + ")|" + T + q, "g");
  function x(N) {
    for (var m = _.lastIndex = 0; _.test(N); )
      ++m;
    return m;
  }
  return Li = x, Li;
}
var Fi, Td;
function Sy() {
  if (Td) return Fi;
  Td = 1;
  var e = qy(), r = Ey(), t = Cy();
  function n(a) {
    return r(a) ? t(a) : e(a);
  }
  return Fi = n, Fi;
}
var ji, Rd;
function Ty() {
  if (Rd) return ji;
  Rd = 1;
  var e = Ho(), r = Tr(), t = rr(), n = Iy(), a = Sy(), i = "[object Map]", s = "[object Set]";
  function o(u) {
    if (u == null)
      return 0;
    if (t(u))
      return n(u) ? a(u) : u.length;
    var l = r(u);
    return l == i || l == s ? u.size : e(u).length;
  }
  return ji = o, ji;
}
var $i, xd;
function Ry() {
  if (xd) return $i;
  xd = 1;
  var e = Bo(), r = Xh(), t = Xo(), n = tr(), a = bt(), i = be(), s = Sr(), o = zr(), u = De(), l = Kr();
  function c(d, h, v) {
    var g = i(d), f = g || s(d) || l(d);
    if (h = n(h, 4), v == null) {
      var y = d && d.constructor;
      f ? v = g ? new y() : [] : u(d) ? v = o(y) ? r(a(d)) : {} : v = {};
    }
    return (f ? e : t)(d, function(q, T, _) {
      return h(v, q, T, _);
    }), v;
  }
  return $i = c, $i;
}
var Gi, kd;
function xy() {
  if (kd) return Gi;
  kd = 1;
  var e = Cr(), r = Ur(), t = be(), n = e ? e.isConcatSpreadable : void 0;
  function a(i) {
    return t(i) || r(i) || !!(n && i && i[n]);
  }
  return Gi = a, Gi;
}
var zi, Ad;
function ru() {
  if (Ad) return zi;
  Ad = 1;
  var e = Wo(), r = xy();
  function t(n, a, i, s, o) {
    var u = -1, l = n.length;
    for (i || (i = r), o || (o = []); ++u < l; ) {
      var c = n[u];
      a > 0 && i(c) ? a > 1 ? t(c, a - 1, i, s, o) : e(o, c) : s || (o[o.length] = c);
    }
    return o;
  }
  return zi = t, zi;
}
var Bi, Od;
function ky() {
  if (Od) return Bi;
  Od = 1;
  function e(r, t, n) {
    switch (n.length) {
      case 0:
        return r.call(t);
      case 1:
        return r.call(t, n[0]);
      case 2:
        return r.call(t, n[0], n[1]);
      case 3:
        return r.call(t, n[0], n[1], n[2]);
    }
    return r.apply(t, n);
  }
  return Bi = e, Bi;
}
var Ui, Pd;
function _p() {
  if (Pd) return Ui;
  Pd = 1;
  var e = ky(), r = Math.max;
  function t(n, a, i) {
    return a = r(a === void 0 ? n.length - 1 : a, 0), function() {
      for (var s = arguments, o = -1, u = r(s.length - a, 0), l = Array(u); ++o < u; )
        l[o] = s[a + o];
      o = -1;
      for (var c = Array(a + 1); ++o < a; )
        c[o] = s[o];
      return c[a] = i(l), e(n, this, c);
    };
  }
  return Ui = t, Ui;
}
var Ki, Nd;
function Ay() {
  if (Nd) return Ki;
  Nd = 1;
  var e = Yo(), r = jh(), t = gr(), n = r ? function(a, i) {
    return r(a, "toString", {
      configurable: !0,
      enumerable: !1,
      value: e(i),
      writable: !0
    });
  } : t;
  return Ki = n, Ki;
}
var Hi, Md;
function Oy() {
  if (Md) return Hi;
  Md = 1;
  var e = 800, r = 16, t = Date.now;
  function n(a) {
    var i = 0, s = 0;
    return function() {
      var o = t(), u = r - (o - s);
      if (s = o, u > 0) {
        if (++i >= e)
          return arguments[0];
      } else
        i = 0;
      return a.apply(void 0, arguments);
    };
  }
  return Hi = n, Hi;
}
var Vi, Dd;
function wp() {
  if (Dd) return Vi;
  Dd = 1;
  var e = Ay(), r = Oy(), t = r(e);
  return Vi = t, Vi;
}
var Wi, Ld;
function Et() {
  if (Ld) return Wi;
  Ld = 1;
  var e = gr(), r = _p(), t = wp();
  function n(a, i) {
    return t(r(a, i, e), a + "");
  }
  return Wi = n, Wi;
}
var Qi, Fd;
function Ip() {
  if (Fd) return Qi;
  Fd = 1;
  function e(r, t, n, a) {
    for (var i = r.length, s = n + (a ? 1 : -1); a ? s-- : ++s < i; )
      if (t(r[s], s, r))
        return s;
    return -1;
  }
  return Qi = e, Qi;
}
var Yi, jd;
function Py() {
  if (jd) return Yi;
  jd = 1;
  function e(r) {
    return r !== r;
  }
  return Yi = e, Yi;
}
var Ji, $d;
function Ny() {
  if ($d) return Ji;
  $d = 1;
  function e(r, t, n) {
    for (var a = n - 1, i = r.length; ++a < i; )
      if (r[a] === t)
        return a;
    return -1;
  }
  return Ji = e, Ji;
}
var Xi, Gd;
function My() {
  if (Gd) return Xi;
  Gd = 1;
  var e = Ip(), r = Py(), t = Ny();
  function n(a, i, s) {
    return i === i ? t(a, i, s) : e(a, r, s);
  }
  return Xi = n, Xi;
}
var Zi, zd;
function Dy() {
  if (zd) return Zi;
  zd = 1;
  var e = My();
  function r(t, n) {
    var a = t == null ? 0 : t.length;
    return !!a && e(t, n, 0) > -1;
  }
  return Zi = r, Zi;
}
var es, Bd;
function Ly() {
  if (Bd) return es;
  Bd = 1;
  function e(r, t, n) {
    for (var a = -1, i = r == null ? 0 : r.length; ++a < i; )
      if (n(t, r[a]))
        return !0;
    return !1;
  }
  return es = e, es;
}
var rs, Ud;
function Fy() {
  if (Ud) return rs;
  Ud = 1;
  function e() {
  }
  return rs = e, rs;
}
var ts, Kd;
function jy() {
  if (Kd) return ts;
  Kd = 1;
  var e = Qh(), r = Fy(), t = Zo(), n = 1 / 0, a = e && 1 / t(new e([, -0]))[1] == n ? function(i) {
    return new e(i);
  } : r;
  return ts = a, ts;
}
var ns, Hd;
function $y() {
  if (Hd) return ns;
  Hd = 1;
  var e = ap(), r = Dy(), t = Ly(), n = ip(), a = jy(), i = Zo(), s = 200;
  function o(u, l, c) {
    var d = -1, h = r, v = u.length, g = !0, f = [], y = f;
    if (c)
      g = !1, h = t;
    else if (v >= s) {
      var q = l ? null : a(u);
      if (q)
        return i(q);
      g = !1, h = n, y = new e();
    } else
      y = l ? [] : f;
    e:
      for (; ++d < v; ) {
        var T = u[d], _ = l ? l(T) : T;
        if (T = c || T !== 0 ? T : 0, g && _ === _) {
          for (var x = y.length; x--; )
            if (y[x] === _)
              continue e;
          l && y.push(_), f.push(T);
        } else h(y, _, c) || (y !== f && y.push(_), f.push(T));
      }
    return f;
  }
  return ns = o, ns;
}
var as, Vd;
function qp() {
  if (Vd) return as;
  Vd = 1;
  var e = rr(), r = He();
  function t(n) {
    return r(n) && e(n);
  }
  return as = t, as;
}
var is, Wd;
function Gy() {
  if (Wd) return is;
  Wd = 1;
  var e = ru(), r = Et(), t = $y(), n = qp(), a = r(function(i) {
    return t(e(i, 1, n, !0));
  });
  return is = a, is;
}
var ss, Qd;
function zy() {
  if (Qd) return ss;
  Qd = 1;
  var e = wt();
  function r(t, n) {
    return e(n, function(a) {
      return t[a];
    });
  }
  return ss = r, ss;
}
var os, Yd;
function Ep() {
  if (Yd) return os;
  Yd = 1;
  var e = zy(), r = cr();
  function t(n) {
    return n == null ? [] : e(n, r(n));
  }
  return os = t, os;
}
var us, Jd;
function Le() {
  if (Jd) return us;
  Jd = 1;
  var e;
  if (typeof $o == "function")
    try {
      e = {
        clone: Qg(),
        constant: Yo(),
        each: np(),
        filter: pp(),
        has: vp(),
        isArray: be(),
        isEmpty: by(),
        isFunction: zr(),
        isUndefined: gp(),
        keys: cr(),
        map: mp(),
        reduce: bp(),
        size: Ty(),
        transform: Ry(),
        union: Gy(),
        values: Ep()
      };
    } catch {
    }
  return e || (e = window._), us = e, us;
}
var cs, Xd;
function tu() {
  if (Xd) return cs;
  Xd = 1;
  var e = Le();
  cs = a;
  var r = "\0", t = "\0", n = "";
  function a(c) {
    this._isDirected = e.has(c, "directed") ? c.directed : !0, this._isMultigraph = e.has(c, "multigraph") ? c.multigraph : !1, this._isCompound = e.has(c, "compound") ? c.compound : !1, this._label = void 0, this._defaultNodeLabelFn = e.constant(void 0), this._defaultEdgeLabelFn = e.constant(void 0), this._nodes = {}, this._isCompound && (this._parent = {}, this._children = {}, this._children[t] = {}), this._in = {}, this._preds = {}, this._out = {}, this._sucs = {}, this._edgeObjs = {}, this._edgeLabels = {};
  }
  a.prototype._nodeCount = 0, a.prototype._edgeCount = 0, a.prototype.isDirected = function() {
    return this._isDirected;
  }, a.prototype.isMultigraph = function() {
    return this._isMultigraph;
  }, a.prototype.isCompound = function() {
    return this._isCompound;
  }, a.prototype.setGraph = function(c) {
    return this._label = c, this;
  }, a.prototype.graph = function() {
    return this._label;
  }, a.prototype.setDefaultNodeLabel = function(c) {
    return e.isFunction(c) || (c = e.constant(c)), this._defaultNodeLabelFn = c, this;
  }, a.prototype.nodeCount = function() {
    return this._nodeCount;
  }, a.prototype.nodes = function() {
    return e.keys(this._nodes);
  }, a.prototype.sources = function() {
    var c = this;
    return e.filter(this.nodes(), function(d) {
      return e.isEmpty(c._in[d]);
    });
  }, a.prototype.sinks = function() {
    var c = this;
    return e.filter(this.nodes(), function(d) {
      return e.isEmpty(c._out[d]);
    });
  }, a.prototype.setNodes = function(c, d) {
    var h = arguments, v = this;
    return e.each(c, function(g) {
      h.length > 1 ? v.setNode(g, d) : v.setNode(g);
    }), this;
  }, a.prototype.setNode = function(c, d) {
    return e.has(this._nodes, c) ? (arguments.length > 1 && (this._nodes[c] = d), this) : (this._nodes[c] = arguments.length > 1 ? d : this._defaultNodeLabelFn(c), this._isCompound && (this._parent[c] = t, this._children[c] = {}, this._children[t][c] = !0), this._in[c] = {}, this._preds[c] = {}, this._out[c] = {}, this._sucs[c] = {}, ++this._nodeCount, this);
  }, a.prototype.node = function(c) {
    return this._nodes[c];
  }, a.prototype.hasNode = function(c) {
    return e.has(this._nodes, c);
  }, a.prototype.removeNode = function(c) {
    var d = this;
    if (e.has(this._nodes, c)) {
      var h = function(v) {
        d.removeEdge(d._edgeObjs[v]);
      };
      delete this._nodes[c], this._isCompound && (this._removeFromParentsChildList(c), delete this._parent[c], e.each(this.children(c), function(v) {
        d.setParent(v);
      }), delete this._children[c]), e.each(e.keys(this._in[c]), h), delete this._in[c], delete this._preds[c], e.each(e.keys(this._out[c]), h), delete this._out[c], delete this._sucs[c], --this._nodeCount;
    }
    return this;
  }, a.prototype.setParent = function(c, d) {
    if (!this._isCompound)
      throw new Error("Cannot set parent in a non-compound graph");
    if (e.isUndefined(d))
      d = t;
    else {
      d += "";
      for (var h = d; !e.isUndefined(h); h = this.parent(h))
        if (h === c)
          throw new Error("Setting " + d + " as parent of " + c + " would create a cycle");
      this.setNode(d);
    }
    return this.setNode(c), this._removeFromParentsChildList(c), this._parent[c] = d, this._children[d][c] = !0, this;
  }, a.prototype._removeFromParentsChildList = function(c) {
    delete this._children[this._parent[c]][c];
  }, a.prototype.parent = function(c) {
    if (this._isCompound) {
      var d = this._parent[c];
      if (d !== t)
        return d;
    }
  }, a.prototype.children = function(c) {
    if (e.isUndefined(c) && (c = t), this._isCompound) {
      var d = this._children[c];
      if (d)
        return e.keys(d);
    } else {
      if (c === t)
        return this.nodes();
      if (this.hasNode(c))
        return [];
    }
  }, a.prototype.predecessors = function(c) {
    var d = this._preds[c];
    if (d)
      return e.keys(d);
  }, a.prototype.successors = function(c) {
    var d = this._sucs[c];
    if (d)
      return e.keys(d);
  }, a.prototype.neighbors = function(c) {
    var d = this.predecessors(c);
    if (d)
      return e.union(d, this.successors(c));
  }, a.prototype.isLeaf = function(c) {
    var d;
    return this.isDirected() ? d = this.successors(c) : d = this.neighbors(c), d.length === 0;
  }, a.prototype.filterNodes = function(c) {
    var d = new this.constructor({
      directed: this._isDirected,
      multigraph: this._isMultigraph,
      compound: this._isCompound
    });
    d.setGraph(this.graph());
    var h = this;
    e.each(this._nodes, function(f, y) {
      c(y) && d.setNode(y, f);
    }), e.each(this._edgeObjs, function(f) {
      d.hasNode(f.v) && d.hasNode(f.w) && d.setEdge(f, h.edge(f));
    });
    var v = {};
    function g(f) {
      var y = h.parent(f);
      return y === void 0 || d.hasNode(y) ? (v[f] = y, y) : y in v ? v[y] : g(y);
    }
    return this._isCompound && e.each(d.nodes(), function(f) {
      d.setParent(f, g(f));
    }), d;
  }, a.prototype.setDefaultEdgeLabel = function(c) {
    return e.isFunction(c) || (c = e.constant(c)), this._defaultEdgeLabelFn = c, this;
  }, a.prototype.edgeCount = function() {
    return this._edgeCount;
  }, a.prototype.edges = function() {
    return e.values(this._edgeObjs);
  }, a.prototype.setPath = function(c, d) {
    var h = this, v = arguments;
    return e.reduce(c, function(g, f) {
      return v.length > 1 ? h.setEdge(g, f, d) : h.setEdge(g, f), f;
    }), this;
  }, a.prototype.setEdge = function() {
    var c, d, h, v, g = !1, f = arguments[0];
    typeof f == "object" && f !== null && "v" in f ? (c = f.v, d = f.w, h = f.name, arguments.length === 2 && (v = arguments[1], g = !0)) : (c = f, d = arguments[1], h = arguments[3], arguments.length > 2 && (v = arguments[2], g = !0)), c = "" + c, d = "" + d, e.isUndefined(h) || (h = "" + h);
    var y = o(this._isDirected, c, d, h);
    if (e.has(this._edgeLabels, y))
      return g && (this._edgeLabels[y] = v), this;
    if (!e.isUndefined(h) && !this._isMultigraph)
      throw new Error("Cannot set a named edge when isMultigraph = false");
    this.setNode(c), this.setNode(d), this._edgeLabels[y] = g ? v : this._defaultEdgeLabelFn(c, d, h);
    var q = u(this._isDirected, c, d, h);
    return c = q.v, d = q.w, Object.freeze(q), this._edgeObjs[y] = q, i(this._preds[d], c), i(this._sucs[c], d), this._in[d][y] = q, this._out[c][y] = q, this._edgeCount++, this;
  }, a.prototype.edge = function(c, d, h) {
    var v = arguments.length === 1 ? l(this._isDirected, arguments[0]) : o(this._isDirected, c, d, h);
    return this._edgeLabels[v];
  }, a.prototype.hasEdge = function(c, d, h) {
    var v = arguments.length === 1 ? l(this._isDirected, arguments[0]) : o(this._isDirected, c, d, h);
    return e.has(this._edgeLabels, v);
  }, a.prototype.removeEdge = function(c, d, h) {
    var v = arguments.length === 1 ? l(this._isDirected, arguments[0]) : o(this._isDirected, c, d, h), g = this._edgeObjs[v];
    return g && (c = g.v, d = g.w, delete this._edgeLabels[v], delete this._edgeObjs[v], s(this._preds[d], c), s(this._sucs[c], d), delete this._in[d][v], delete this._out[c][v], this._edgeCount--), this;
  }, a.prototype.inEdges = function(c, d) {
    var h = this._in[c];
    if (h) {
      var v = e.values(h);
      return d ? e.filter(v, function(g) {
        return g.v === d;
      }) : v;
    }
  }, a.prototype.outEdges = function(c, d) {
    var h = this._out[c];
    if (h) {
      var v = e.values(h);
      return d ? e.filter(v, function(g) {
        return g.w === d;
      }) : v;
    }
  }, a.prototype.nodeEdges = function(c, d) {
    var h = this.inEdges(c, d);
    if (h)
      return h.concat(this.outEdges(c, d));
  };
  function i(c, d) {
    c[d] ? c[d]++ : c[d] = 1;
  }
  function s(c, d) {
    --c[d] || delete c[d];
  }
  function o(c, d, h, v) {
    var g = "" + d, f = "" + h;
    if (!c && g > f) {
      var y = g;
      g = f, f = y;
    }
    return g + n + f + n + (e.isUndefined(v) ? r : v);
  }
  function u(c, d, h, v) {
    var g = "" + d, f = "" + h;
    if (!c && g > f) {
      var y = g;
      g = f, f = y;
    }
    var q = { v: g, w: f };
    return v && (q.name = v), q;
  }
  function l(c, d) {
    return o(c, d.v, d.w, d.name);
  }
  return cs;
}
var ls, Zd;
function By() {
  return Zd || (Zd = 1, ls = "2.1.8"), ls;
}
var ds, ef;
function Uy() {
  return ef || (ef = 1, ds = {
    Graph: tu(),
    version: By()
  }), ds;
}
var fs, rf;
function Ky() {
  if (rf) return fs;
  rf = 1;
  var e = Le(), r = tu();
  fs = {
    write: t,
    read: i
  };
  function t(s) {
    var o = {
      options: {
        directed: s.isDirected(),
        multigraph: s.isMultigraph(),
        compound: s.isCompound()
      },
      nodes: n(s),
      edges: a(s)
    };
    return e.isUndefined(s.graph()) || (o.value = e.clone(s.graph())), o;
  }
  function n(s) {
    return e.map(s.nodes(), function(o) {
      var u = s.node(o), l = s.parent(o), c = { v: o };
      return e.isUndefined(u) || (c.value = u), e.isUndefined(l) || (c.parent = l), c;
    });
  }
  function a(s) {
    return e.map(s.edges(), function(o) {
      var u = s.edge(o), l = { v: o.v, w: o.w };
      return e.isUndefined(o.name) || (l.name = o.name), e.isUndefined(u) || (l.value = u), l;
    });
  }
  function i(s) {
    var o = new r(s.options).setGraph(s.value);
    return e.each(s.nodes, function(u) {
      o.setNode(u.v, u.value), u.parent && o.setParent(u.v, u.parent);
    }), e.each(s.edges, function(u) {
      o.setEdge({ v: u.v, w: u.w, name: u.name }, u.value);
    }), o;
  }
  return fs;
}
var hs, tf;
function Hy() {
  if (tf) return hs;
  tf = 1;
  var e = Le();
  hs = r;
  function r(t) {
    var n = {}, a = [], i;
    function s(o) {
      e.has(n, o) || (n[o] = !0, i.push(o), e.each(t.successors(o), s), e.each(t.predecessors(o), s));
    }
    return e.each(t.nodes(), function(o) {
      i = [], s(o), i.length && a.push(i);
    }), a;
  }
  return hs;
}
var ps, nf;
function Cp() {
  if (nf) return ps;
  nf = 1;
  var e = Le();
  ps = r;
  function r() {
    this._arr = [], this._keyIndices = {};
  }
  return r.prototype.size = function() {
    return this._arr.length;
  }, r.prototype.keys = function() {
    return this._arr.map(function(t) {
      return t.key;
    });
  }, r.prototype.has = function(t) {
    return e.has(this._keyIndices, t);
  }, r.prototype.priority = function(t) {
    var n = this._keyIndices[t];
    if (n !== void 0)
      return this._arr[n].priority;
  }, r.prototype.min = function() {
    if (this.size() === 0)
      throw new Error("Queue underflow");
    return this._arr[0].key;
  }, r.prototype.add = function(t, n) {
    var a = this._keyIndices;
    if (t = String(t), !e.has(a, t)) {
      var i = this._arr, s = i.length;
      return a[t] = s, i.push({ key: t, priority: n }), this._decrease(s), !0;
    }
    return !1;
  }, r.prototype.removeMin = function() {
    this._swap(0, this._arr.length - 1);
    var t = this._arr.pop();
    return delete this._keyIndices[t.key], this._heapify(0), t.key;
  }, r.prototype.decrease = function(t, n) {
    var a = this._keyIndices[t];
    if (n > this._arr[a].priority)
      throw new Error("New priority is greater than current priority. Key: " + t + " Old: " + this._arr[a].priority + " New: " + n);
    this._arr[a].priority = n, this._decrease(a);
  }, r.prototype._heapify = function(t) {
    var n = this._arr, a = 2 * t, i = a + 1, s = t;
    a < n.length && (s = n[a].priority < n[s].priority ? a : s, i < n.length && (s = n[i].priority < n[s].priority ? i : s), s !== t && (this._swap(t, s), this._heapify(s)));
  }, r.prototype._decrease = function(t) {
    for (var n = this._arr, a = n[t].priority, i; t !== 0 && (i = t >> 1, !(n[i].priority < a)); )
      this._swap(t, i), t = i;
  }, r.prototype._swap = function(t, n) {
    var a = this._arr, i = this._keyIndices, s = a[t], o = a[n];
    a[t] = o, a[n] = s, i[o.key] = t, i[s.key] = n;
  }, ps;
}
var vs, af;
function Sp() {
  if (af) return vs;
  af = 1;
  var e = Le(), r = Cp();
  vs = n;
  var t = e.constant(1);
  function n(i, s, o, u) {
    return a(
      i,
      String(s),
      o || t,
      u || function(l) {
        return i.outEdges(l);
      }
    );
  }
  function a(i, s, o, u) {
    var l = {}, c = new r(), d, h, v = function(g) {
      var f = g.v !== d ? g.v : g.w, y = l[f], q = o(g), T = h.distance + q;
      if (q < 0)
        throw new Error("dijkstra does not allow negative edge weights. Bad edge: " + g + " Weight: " + q);
      T < y.distance && (y.distance = T, y.predecessor = d, c.decrease(f, T));
    };
    for (i.nodes().forEach(function(g) {
      var f = g === s ? 0 : Number.POSITIVE_INFINITY;
      l[g] = { distance: f }, c.add(g, f);
    }); c.size() > 0 && (d = c.removeMin(), h = l[d], h.distance !== Number.POSITIVE_INFINITY); )
      u(d).forEach(v);
    return l;
  }
  return vs;
}
var gs, sf;
function Vy() {
  if (sf) return gs;
  sf = 1;
  var e = Sp(), r = Le();
  gs = t;
  function t(n, a, i) {
    return r.transform(n.nodes(), function(s, o) {
      s[o] = e(n, o, a, i);
    }, {});
  }
  return gs;
}
var ys, of;
function Tp() {
  if (of) return ys;
  of = 1;
  var e = Le();
  ys = r;
  function r(t) {
    var n = 0, a = [], i = {}, s = [];
    function o(u) {
      var l = i[u] = {
        onStack: !0,
        lowlink: n,
        index: n++
      };
      if (a.push(u), t.successors(u).forEach(function(h) {
        e.has(i, h) ? i[h].onStack && (l.lowlink = Math.min(l.lowlink, i[h].index)) : (o(h), l.lowlink = Math.min(l.lowlink, i[h].lowlink));
      }), l.lowlink === l.index) {
        var c = [], d;
        do
          d = a.pop(), i[d].onStack = !1, c.push(d);
        while (u !== d);
        s.push(c);
      }
    }
    return t.nodes().forEach(function(u) {
      e.has(i, u) || o(u);
    }), s;
  }
  return ys;
}
var ms, uf;
function Wy() {
  if (uf) return ms;
  uf = 1;
  var e = Le(), r = Tp();
  ms = t;
  function t(n) {
    return e.filter(r(n), function(a) {
      return a.length > 1 || a.length === 1 && n.hasEdge(a[0], a[0]);
    });
  }
  return ms;
}
var bs, cf;
function Qy() {
  if (cf) return bs;
  cf = 1;
  var e = Le();
  bs = t;
  var r = e.constant(1);
  function t(a, i, s) {
    return n(
      a,
      i || r,
      s || function(o) {
        return a.outEdges(o);
      }
    );
  }
  function n(a, i, s) {
    var o = {}, u = a.nodes();
    return u.forEach(function(l) {
      o[l] = {}, o[l][l] = { distance: 0 }, u.forEach(function(c) {
        l !== c && (o[l][c] = { distance: Number.POSITIVE_INFINITY });
      }), s(l).forEach(function(c) {
        var d = c.v === l ? c.w : c.v, h = i(c);
        o[l][d] = { distance: h, predecessor: l };
      });
    }), u.forEach(function(l) {
      var c = o[l];
      u.forEach(function(d) {
        var h = o[d];
        u.forEach(function(v) {
          var g = h[l], f = c[v], y = h[v], q = g.distance + f.distance;
          q < y.distance && (y.distance = q, y.predecessor = f.predecessor);
        });
      });
    }), o;
  }
  return bs;
}
var _s, lf;
function Rp() {
  if (lf) return _s;
  lf = 1;
  var e = Le();
  _s = r, r.CycleException = t;
  function r(n) {
    var a = {}, i = {}, s = [];
    function o(u) {
      if (e.has(i, u))
        throw new t();
      e.has(a, u) || (i[u] = !0, a[u] = !0, e.each(n.predecessors(u), o), delete i[u], s.push(u));
    }
    if (e.each(n.sinks(), o), e.size(a) !== n.nodeCount())
      throw new t();
    return s;
  }
  function t() {
  }
  return t.prototype = new Error(), _s;
}
var ws, df;
function Yy() {
  if (df) return ws;
  df = 1;
  var e = Rp();
  ws = r;
  function r(t) {
    try {
      e(t);
    } catch (n) {
      if (n instanceof e.CycleException)
        return !1;
      throw n;
    }
    return !0;
  }
  return ws;
}
var Is, ff;
function xp() {
  if (ff) return Is;
  ff = 1;
  var e = Le();
  Is = r;
  function r(n, a, i) {
    e.isArray(a) || (a = [a]);
    var s = (n.isDirected() ? n.successors : n.neighbors).bind(n), o = [], u = {};
    return e.each(a, function(l) {
      if (!n.hasNode(l))
        throw new Error("Graph does not have node: " + l);
      t(n, l, i === "post", u, s, o);
    }), o;
  }
  function t(n, a, i, s, o, u) {
    e.has(s, a) || (s[a] = !0, i || u.push(a), e.each(o(a), function(l) {
      t(n, l, i, s, o, u);
    }), i && u.push(a));
  }
  return Is;
}
var qs, hf;
function Jy() {
  if (hf) return qs;
  hf = 1;
  var e = xp();
  qs = r;
  function r(t, n) {
    return e(t, n, "post");
  }
  return qs;
}
var Es, pf;
function Xy() {
  if (pf) return Es;
  pf = 1;
  var e = xp();
  Es = r;
  function r(t, n) {
    return e(t, n, "pre");
  }
  return Es;
}
var Cs, vf;
function Zy() {
  if (vf) return Cs;
  vf = 1;
  var e = Le(), r = tu(), t = Cp();
  Cs = n;
  function n(a, i) {
    var s = new r(), o = {}, u = new t(), l;
    function c(h) {
      var v = h.v === l ? h.w : h.v, g = u.priority(v);
      if (g !== void 0) {
        var f = i(h);
        f < g && (o[v] = l, u.decrease(v, f));
      }
    }
    if (a.nodeCount() === 0)
      return s;
    e.each(a.nodes(), function(h) {
      u.add(h, Number.POSITIVE_INFINITY), s.setNode(h);
    }), u.decrease(a.nodes()[0], 0);
    for (var d = !1; u.size() > 0; ) {
      if (l = u.removeMin(), e.has(o, l))
        s.setEdge(l, o[l]);
      else {
        if (d)
          throw new Error("Input graph is not connected: " + a);
        d = !0;
      }
      a.nodeEdges(l).forEach(c);
    }
    return s;
  }
  return Cs;
}
var Ss, gf;
function em() {
  return gf || (gf = 1, Ss = {
    components: Hy(),
    dijkstra: Sp(),
    dijkstraAll: Vy(),
    findCycles: Wy(),
    floydWarshall: Qy(),
    isAcyclic: Yy(),
    postorder: Jy(),
    preorder: Xy(),
    prim: Zy(),
    tarjan: Tp(),
    topsort: Rp()
  }), Ss;
}
var Ts, yf;
function rm() {
  if (yf) return Ts;
  yf = 1;
  var e = Uy();
  return Ts = {
    Graph: e.Graph,
    json: Ky(),
    alg: em(),
    version: e.version
  }, Ts;
}
var nt;
if (typeof $o == "function")
  try {
    nt = rm();
  } catch {
  }
nt || (nt = window.graphlib);
var je = nt, Rs, mf;
function tm() {
  if (mf) return Rs;
  mf = 1;
  var e = ep(), r = 1, t = 4;
  function n(a) {
    return e(a, r | t);
  }
  return Rs = n, Rs;
}
var xs, bf;
function Ct() {
  if (bf) return xs;
  bf = 1;
  var e = Er(), r = rr(), t = gt(), n = De();
  function a(i, s, o) {
    if (!n(o))
      return !1;
    var u = typeof s;
    return (u == "number" ? r(o) && t(s, o.length) : u == "string" && s in o) ? e(o[s], i) : !1;
  }
  return xs = a, xs;
}
var ks, _f;
function nm() {
  if (_f) return ks;
  _f = 1;
  var e = Et(), r = Er(), t = Ct(), n = vr(), a = Object.prototype, i = a.hasOwnProperty, s = e(function(o, u) {
    o = Object(o);
    var l = -1, c = u.length, d = c > 2 ? u[2] : void 0;
    for (d && t(u[0], u[1], d) && (c = 1); ++l < c; )
      for (var h = u[l], v = n(h), g = -1, f = v.length; ++g < f; ) {
        var y = v[g], q = o[y];
        (q === void 0 || r(q, a[y]) && !i.call(o, y)) && (o[y] = h[y]);
      }
    return o;
  });
  return ks = s, ks;
}
var As, wf;
function am() {
  if (wf) return As;
  wf = 1;
  var e = tr(), r = rr(), t = cr();
  function n(a) {
    return function(i, s, o) {
      var u = Object(i);
      if (!r(i)) {
        var l = e(s, 3);
        i = t(i), s = function(d) {
          return l(u[d], d, u);
        };
      }
      var c = a(i, s, o);
      return c > -1 ? u[l ? i[c] : c] : void 0;
    };
  }
  return As = n, As;
}
var Os, If;
function im() {
  if (If) return Os;
  If = 1;
  var e = /\s/;
  function r(t) {
    for (var n = t.length; n-- && e.test(t.charAt(n)); )
      ;
    return n;
  }
  return Os = r, Os;
}
var Ps, qf;
function sm() {
  if (qf) return Ps;
  qf = 1;
  var e = im(), r = /^\s+/;
  function t(n) {
    return n && n.slice(0, e(n) + 1).replace(r, "");
  }
  return Ps = t, Ps;
}
var Ns, Ef;
function om() {
  if (Ef) return Ns;
  Ef = 1;
  var e = sm(), r = De(), t = Rr(), n = NaN, a = /^[-+]0x[0-9a-f]+$/i, i = /^0b[01]+$/i, s = /^0o[0-7]+$/i, o = parseInt;
  function u(l) {
    if (typeof l == "number")
      return l;
    if (t(l))
      return n;
    if (r(l)) {
      var c = typeof l.valueOf == "function" ? l.valueOf() : l;
      l = r(c) ? c + "" : c;
    }
    if (typeof l != "string")
      return l === 0 ? l : +l;
    l = e(l);
    var d = i.test(l);
    return d || s.test(l) ? o(l.slice(2), d ? 2 : 8) : a.test(l) ? n : +l;
  }
  return Ns = u, Ns;
}
var Ms, Cf;
function kp() {
  if (Cf) return Ms;
  Cf = 1;
  var e = om(), r = 1 / 0, t = 17976931348623157e292;
  function n(a) {
    if (!a)
      return a === 0 ? a : 0;
    if (a = e(a), a === r || a === -r) {
      var i = a < 0 ? -1 : 1;
      return i * t;
    }
    return a === a ? a : 0;
  }
  return Ms = n, Ms;
}
var Ds, Sf;
function um() {
  if (Sf) return Ds;
  Sf = 1;
  var e = kp();
  function r(t) {
    var n = e(t), a = n % 1;
    return n === n ? a ? n - a : n : 0;
  }
  return Ds = r, Ds;
}
var Ls, Tf;
function cm() {
  if (Tf) return Ls;
  Tf = 1;
  var e = Ip(), r = tr(), t = um(), n = Math.max;
  function a(i, s, o) {
    var u = i == null ? 0 : i.length;
    if (!u)
      return -1;
    var l = o == null ? 0 : t(o);
    return l < 0 && (l = n(u + l, 0)), e(i, r(s, 3), l);
  }
  return Ls = a, Ls;
}
var Fs, Rf;
function lm() {
  if (Rf) return Fs;
  Rf = 1;
  var e = am(), r = cm(), t = e(r);
  return Fs = t, Fs;
}
var js, xf;
function Ap() {
  if (xf) return js;
  xf = 1;
  var e = ru();
  function r(t) {
    var n = t == null ? 0 : t.length;
    return n ? e(t, 1) : [];
  }
  return js = r, js;
}
var $s, kf;
function dm() {
  if (kf) return $s;
  kf = 1;
  var e = Jo(), r = rp(), t = vr();
  function n(a, i) {
    return a == null ? a : e(a, r(i), t);
  }
  return $s = n, $s;
}
var Gs, Af;
function fm() {
  if (Af) return Gs;
  Af = 1;
  function e(r) {
    var t = r == null ? 0 : r.length;
    return t ? r[t - 1] : void 0;
  }
  return Gs = e, Gs;
}
var zs, Of;
function hm() {
  if (Of) return zs;
  Of = 1;
  var e = pt(), r = Xo(), t = tr();
  function n(a, i) {
    var s = {};
    return i = t(i, 3), r(a, function(o, u, l) {
      e(s, u, i(o, u, l));
    }), s;
  }
  return zs = n, zs;
}
var Bs, Pf;
function nu() {
  if (Pf) return Bs;
  Pf = 1;
  var e = Rr();
  function r(t, n, a) {
    for (var i = -1, s = t.length; ++i < s; ) {
      var o = t[i], u = n(o);
      if (u != null && (l === void 0 ? u === u && !e(u) : a(u, l)))
        var l = u, c = o;
    }
    return c;
  }
  return Bs = r, Bs;
}
var Us, Nf;
function pm() {
  if (Nf) return Us;
  Nf = 1;
  function e(r, t) {
    return r > t;
  }
  return Us = e, Us;
}
var Ks, Mf;
function vm() {
  if (Mf) return Ks;
  Mf = 1;
  var e = nu(), r = pm(), t = gr();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return Ks = n, Ks;
}
var Hs, Df;
function Op() {
  if (Df) return Hs;
  Df = 1;
  var e = pt(), r = Er();
  function t(n, a, i) {
    (i !== void 0 && !r(n[a], i) || i === void 0 && !(a in n)) && e(n, a, i);
  }
  return Hs = t, Hs;
}
var Vs, Lf;
function gm() {
  if (Lf) return Vs;
  Lf = 1;
  var e = hr(), r = bt(), t = He(), n = "[object Object]", a = Function.prototype, i = Object.prototype, s = a.toString, o = i.hasOwnProperty, u = s.call(Object);
  function l(c) {
    if (!t(c) || e(c) != n)
      return !1;
    var d = r(c);
    if (d === null)
      return !0;
    var h = o.call(d, "constructor") && d.constructor;
    return typeof h == "function" && h instanceof h && s.call(h) == u;
  }
  return Vs = l, Vs;
}
var Ws, Ff;
function Pp() {
  if (Ff) return Ws;
  Ff = 1;
  function e(r, t) {
    if (!(t === "constructor" && typeof r[t] == "function") && t != "__proto__")
      return r[t];
  }
  return Ws = e, Ws;
}
var Qs, jf;
function ym() {
  if (jf) return Qs;
  jf = 1;
  var e = Br(), r = vr();
  function t(n) {
    return e(n, r(n));
  }
  return Qs = t, Qs;
}
var Ys, $f;
function mm() {
  if ($f) return Ys;
  $f = 1;
  var e = Op(), r = zh(), t = Jh(), n = Bh(), a = Zh(), i = Ur(), s = be(), o = qp(), u = Sr(), l = zr(), c = De(), d = gm(), h = Kr(), v = Pp(), g = ym();
  function f(y, q, T, _, x, N, m) {
    var S = v(y, T), M = v(q, T), B = m.get(M);
    if (B) {
      e(y, T, B);
      return;
    }
    var z = N ? N(S, M, T + "", y, q, m) : void 0, w = z === void 0;
    if (w) {
      var D = s(M), L = !D && u(M), J = !D && !L && h(M);
      z = M, D || L || J ? s(S) ? z = S : o(S) ? z = n(S) : L ? (w = !1, z = r(M, !0)) : J ? (w = !1, z = t(M, !0)) : z = [] : d(M) || i(M) ? (z = S, i(S) ? z = g(S) : (!c(S) || l(S)) && (z = a(M))) : w = !1;
    }
    w && (m.set(M, z), x(z, M, _, N, m), m.delete(M)), e(y, T, z);
  }
  return Ys = f, Ys;
}
var Js, Gf;
function bm() {
  if (Gf) return Js;
  Gf = 1;
  var e = ht(), r = Op(), t = Jo(), n = mm(), a = De(), i = vr(), s = Pp();
  function o(u, l, c, d, h) {
    u !== l && t(l, function(v, g) {
      if (h || (h = new e()), a(v))
        n(u, l, g, c, o, d, h);
      else {
        var f = d ? d(s(u, g), v, g + "", u, l, h) : void 0;
        f === void 0 && (f = v), r(u, g, f);
      }
    }, i);
  }
  return Js = o, Js;
}
var Xs, zf;
function _m() {
  if (zf) return Xs;
  zf = 1;
  var e = Et(), r = Ct();
  function t(n) {
    return e(function(a, i) {
      var s = -1, o = i.length, u = o > 1 ? i[o - 1] : void 0, l = o > 2 ? i[2] : void 0;
      for (u = n.length > 3 && typeof u == "function" ? (o--, u) : void 0, l && r(i[0], i[1], l) && (u = o < 3 ? void 0 : u, o = 1), a = Object(a); ++s < o; ) {
        var c = i[s];
        c && n(a, c, s, u);
      }
      return a;
    });
  }
  return Xs = t, Xs;
}
var Zs, Bf;
function wm() {
  if (Bf) return Zs;
  Bf = 1;
  var e = bm(), r = _m(), t = r(function(n, a, i) {
    e(n, a, i);
  });
  return Zs = t, Zs;
}
var eo, Uf;
function Np() {
  if (Uf) return eo;
  Uf = 1;
  function e(r, t) {
    return r < t;
  }
  return eo = e, eo;
}
var ro, Kf;
function Im() {
  if (Kf) return ro;
  Kf = 1;
  var e = nu(), r = Np(), t = gr();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return ro = n, ro;
}
var to, Hf;
function qm() {
  if (Hf) return to;
  Hf = 1;
  var e = nu(), r = tr(), t = Np();
  function n(a, i) {
    return a && a.length ? e(a, r(i, 2), t) : void 0;
  }
  return to = n, to;
}
var no, Vf;
function Em() {
  if (Vf) return no;
  Vf = 1;
  var e = Fe(), r = function() {
    return e.Date.now();
  };
  return no = r, no;
}
var ao, Wf;
function Cm() {
  if (Wf) return ao;
  Wf = 1;
  var e = vt(), r = It(), t = gt(), n = De(), a = Hr();
  function i(s, o, u, l) {
    if (!n(s))
      return s;
    o = r(o, s);
    for (var c = -1, d = o.length, h = d - 1, v = s; v != null && ++c < d; ) {
      var g = a(o[c]), f = u;
      if (g === "__proto__" || g === "constructor" || g === "prototype")
        return s;
      if (c != h) {
        var y = v[g];
        f = l ? l(y, g, v) : void 0, f === void 0 && (f = n(y) ? y : t(o[c + 1]) ? [] : {});
      }
      e(v, g, f), v = v[g];
    }
    return s;
  }
  return ao = i, ao;
}
var io, Qf;
function Sm() {
  if (Qf) return io;
  Qf = 1;
  var e = qt(), r = Cm(), t = It();
  function n(a, i, s) {
    for (var o = -1, u = i.length, l = {}; ++o < u; ) {
      var c = i[o], d = e(a, c);
      s(d, c) && r(l, t(c, a), d);
    }
    return l;
  }
  return io = n, io;
}
var so, Yf;
function Tm() {
  if (Yf) return so;
  Yf = 1;
  var e = Sm(), r = fp();
  function t(n, a) {
    return e(n, a, function(i, s) {
      return r(n, s);
    });
  }
  return so = t, so;
}
var oo, Jf;
function Rm() {
  if (Jf) return oo;
  Jf = 1;
  var e = Ap(), r = _p(), t = wp();
  function n(a) {
    return t(r(a, void 0, e), a + "");
  }
  return oo = n, oo;
}
var uo, Xf;
function xm() {
  if (Xf) return uo;
  Xf = 1;
  var e = Tm(), r = Rm(), t = r(function(n, a) {
    return n == null ? {} : e(n, a);
  });
  return uo = t, uo;
}
var co, Zf;
function km() {
  if (Zf) return co;
  Zf = 1;
  var e = Math.ceil, r = Math.max;
  function t(n, a, i, s) {
    for (var o = -1, u = r(e((a - n) / (i || 1)), 0), l = Array(u); u--; )
      l[s ? u : ++o] = n, n += i;
    return l;
  }
  return co = t, co;
}
var lo, eh;
function Am() {
  if (eh) return lo;
  eh = 1;
  var e = km(), r = Ct(), t = kp();
  function n(a) {
    return function(i, s, o) {
      return o && typeof o != "number" && r(i, s, o) && (s = o = void 0), i = t(i), s === void 0 ? (s = i, i = 0) : s = t(s), o = o === void 0 ? i < s ? 1 : -1 : t(o), e(i, s, o, a);
    };
  }
  return lo = n, lo;
}
var fo, rh;
function Om() {
  if (rh) return fo;
  rh = 1;
  var e = Am(), r = e();
  return fo = r, fo;
}
var ho, th;
function Pm() {
  if (th) return ho;
  th = 1;
  function e(r, t) {
    var n = r.length;
    for (r.sort(t); n--; )
      r[n] = r[n].value;
    return r;
  }
  return ho = e, ho;
}
var po, nh;
function Nm() {
  if (nh) return po;
  nh = 1;
  var e = Rr();
  function r(t, n) {
    if (t !== n) {
      var a = t !== void 0, i = t === null, s = t === t, o = e(t), u = n !== void 0, l = n === null, c = n === n, d = e(n);
      if (!l && !d && !o && t > n || o && u && c && !l && !d || i && u && c || !a && c || !s)
        return 1;
      if (!i && !o && !d && t < n || d && a && s && !i && !o || l && a && s || !u && s || !c)
        return -1;
    }
    return 0;
  }
  return po = r, po;
}
var vo, ah;
function Mm() {
  if (ah) return vo;
  ah = 1;
  var e = Nm();
  function r(t, n, a) {
    for (var i = -1, s = t.criteria, o = n.criteria, u = s.length, l = a.length; ++i < u; ) {
      var c = e(s[i], o[i]);
      if (c) {
        if (i >= l)
          return c;
        var d = a[i];
        return c * (d == "desc" ? -1 : 1);
      }
    }
    return t.index - n.index;
  }
  return vo = r, vo;
}
var go, ih;
function Dm() {
  if (ih) return go;
  ih = 1;
  var e = wt(), r = qt(), t = tr(), n = yp(), a = Pm(), i = yt(), s = Mm(), o = gr(), u = be();
  function l(c, d, h) {
    d.length ? d = e(d, function(f) {
      return u(f) ? function(y) {
        return r(y, f.length === 1 ? f[0] : f);
      } : f;
    }) : d = [o];
    var v = -1;
    d = e(d, i(t));
    var g = n(c, function(f, y, q) {
      var T = e(d, function(_) {
        return _(f);
      });
      return { criteria: T, index: ++v, value: f };
    });
    return a(g, function(f, y) {
      return s(f, y, h);
    });
  }
  return go = l, go;
}
var yo, sh;
function Lm() {
  if (sh) return yo;
  sh = 1;
  var e = ru(), r = Dm(), t = Et(), n = Ct(), a = t(function(i, s) {
    if (i == null)
      return [];
    var o = s.length;
    return o > 1 && n(i, s[0], s[1]) ? s = [] : o > 2 && n(s[0], s[1], s[2]) && (s = [s[0]]), r(i, e(s, 1), []);
  });
  return yo = a, yo;
}
var mo, oh;
function Fm() {
  if (oh) return mo;
  oh = 1;
  var e = lp(), r = 0;
  function t(n) {
    var a = ++r;
    return e(n) + a;
  }
  return mo = t, mo;
}
var bo, uh;
function jm() {
  if (uh) return bo;
  uh = 1;
  function e(r, t, n) {
    for (var a = -1, i = r.length, s = t.length, o = {}; ++a < i; ) {
      var u = a < s ? t[a] : void 0;
      n(o, r[a], u);
    }
    return o;
  }
  return bo = e, bo;
}
var _o, ch;
function $m() {
  if (ch) return _o;
  ch = 1;
  var e = vt(), r = jm();
  function t(n, a) {
    return r(n || [], a || [], e);
  }
  return _o = t, _o;
}
var at;
if (typeof $o == "function")
  try {
    at = {
      cloneDeep: tm(),
      constant: Yo(),
      defaults: nm(),
      each: np(),
      filter: pp(),
      find: lm(),
      flatten: Ap(),
      forEach: tp(),
      forIn: dm(),
      has: vp(),
      isUndefined: gp(),
      last: fm(),
      map: mp(),
      mapValues: hm(),
      max: vm(),
      merge: wm(),
      min: Im(),
      minBy: qm(),
      now: Em(),
      pick: xm(),
      range: Om(),
      reduce: bp(),
      sortBy: Lm(),
      uniqueId: Fm(),
      values: Ep(),
      zipObject: $m()
    };
  } catch {
  }
at || (at = window._);
var he = at, Gm = St;
function St() {
  var e = {};
  e._next = e._prev = e, this._sentinel = e;
}
St.prototype.dequeue = function() {
  var e = this._sentinel, r = e._prev;
  if (r !== e)
    return Mp(r), r;
};
St.prototype.enqueue = function(e) {
  var r = this._sentinel;
  e._prev && e._next && Mp(e), e._next = r._next, r._next._prev = e, r._next = e, e._prev = r;
};
St.prototype.toString = function() {
  for (var e = [], r = this._sentinel, t = r._prev; t !== r; )
    e.push(JSON.stringify(t, zm)), t = t._prev;
  return "[" + e.join(", ") + "]";
};
function Mp(e) {
  e._prev._next = e._next, e._next._prev = e._prev, delete e._next, delete e._prev;
}
function zm(e, r) {
  if (e !== "_next" && e !== "_prev")
    return r;
}
var Xe = he, Bm = je.Graph, Um = Gm, Km = Vm, Hm = Xe.constant(1);
function Vm(e, r) {
  if (e.nodeCount() <= 1)
    return [];
  var t = Qm(e, r || Hm), n = Wm(t.graph, t.buckets, t.zeroIdx);
  return Xe.flatten(Xe.map(n, function(a) {
    return e.outEdges(a.v, a.w);
  }), !0);
}
function Wm(e, r, t) {
  for (var n = [], a = r[r.length - 1], i = r[0], s; e.nodeCount(); ) {
    for (; s = i.dequeue(); )
      wo(e, r, t, s);
    for (; s = a.dequeue(); )
      wo(e, r, t, s);
    if (e.nodeCount()) {
      for (var o = r.length - 2; o > 0; --o)
        if (s = r[o].dequeue(), s) {
          n = n.concat(wo(e, r, t, s, !0));
          break;
        }
    }
  }
  return n;
}
function wo(e, r, t, n, a) {
  var i = a ? [] : void 0;
  return Xe.forEach(e.inEdges(n.v), function(s) {
    var o = e.edge(s), u = e.node(s.v);
    a && i.push({ v: s.v, w: s.w }), u.out -= o, Mo(r, t, u);
  }), Xe.forEach(e.outEdges(n.v), function(s) {
    var o = e.edge(s), u = s.w, l = e.node(u);
    l.in -= o, Mo(r, t, l);
  }), e.removeNode(n.v), i;
}
function Qm(e, r) {
  var t = new Bm(), n = 0, a = 0;
  Xe.forEach(e.nodes(), function(o) {
    t.setNode(o, { v: o, in: 0, out: 0 });
  }), Xe.forEach(e.edges(), function(o) {
    var u = t.edge(o.v, o.w) || 0, l = r(o), c = u + l;
    t.setEdge(o.v, o.w, c), a = Math.max(a, t.node(o.v).out += l), n = Math.max(n, t.node(o.w).in += l);
  });
  var i = Xe.range(a + n + 3).map(function() {
    return new Um();
  }), s = n + 1;
  return Xe.forEach(t.nodes(), function(o) {
    Mo(i, s, t.node(o));
  }), { graph: t, buckets: i, zeroIdx: s };
}
function Mo(e, r, t) {
  t.out ? t.in ? e[t.out - t.in + r].enqueue(t) : e[e.length - 1].enqueue(t) : e[0].enqueue(t);
}
var dr = he, Ym = Km, Jm = {
  run: Xm,
  undo: eb
};
function Xm(e) {
  var r = e.graph().acyclicer === "greedy" ? Ym(e, t(e)) : Zm(e);
  dr.forEach(r, function(n) {
    var a = e.edge(n);
    e.removeEdge(n), a.forwardName = n.name, a.reversed = !0, e.setEdge(n.w, n.v, a, dr.uniqueId("rev"));
  });
  function t(n) {
    return function(a) {
      return n.edge(a).weight;
    };
  }
}
function Zm(e) {
  var r = [], t = {}, n = {};
  function a(i) {
    dr.has(n, i) || (n[i] = !0, t[i] = !0, dr.forEach(e.outEdges(i), function(s) {
      dr.has(t, s.w) ? r.push(s) : a(s.w);
    }), delete t[i]);
  }
  return dr.forEach(e.nodes(), a), r;
}
function eb(e) {
  dr.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (t.reversed) {
      e.removeEdge(r);
      var n = t.forwardName;
      delete t.reversed, delete t.forwardName, e.setEdge(r.w, r.v, t, n);
    }
  });
}
var ne = he, Dp = je.Graph, Ae = {
  addDummyNode: Lp,
  simplify: rb,
  asNonCompoundGraph: tb,
  successorWeights: nb,
  predecessorWeights: ab,
  intersectRect: ib,
  buildLayerMatrix: sb,
  normalizeRanks: ob,
  removeEmptyRanks: ub,
  addBorderNode: cb,
  maxRank: Fp,
  partition: lb,
  time: db,
  notime: fb
};
function Lp(e, r, t, n) {
  var a;
  do
    a = ne.uniqueId(n);
  while (e.hasNode(a));
  return t.dummy = r, e.setNode(a, t), a;
}
function rb(e) {
  var r = new Dp().setGraph(e.graph());
  return ne.forEach(e.nodes(), function(t) {
    r.setNode(t, e.node(t));
  }), ne.forEach(e.edges(), function(t) {
    var n = r.edge(t.v, t.w) || { weight: 0, minlen: 1 }, a = e.edge(t);
    r.setEdge(t.v, t.w, {
      weight: n.weight + a.weight,
      minlen: Math.max(n.minlen, a.minlen)
    });
  }), r;
}
function tb(e) {
  var r = new Dp({ multigraph: e.isMultigraph() }).setGraph(e.graph());
  return ne.forEach(e.nodes(), function(t) {
    e.children(t).length || r.setNode(t, e.node(t));
  }), ne.forEach(e.edges(), function(t) {
    r.setEdge(t, e.edge(t));
  }), r;
}
function nb(e) {
  var r = ne.map(e.nodes(), function(t) {
    var n = {};
    return ne.forEach(e.outEdges(t), function(a) {
      n[a.w] = (n[a.w] || 0) + e.edge(a).weight;
    }), n;
  });
  return ne.zipObject(e.nodes(), r);
}
function ab(e) {
  var r = ne.map(e.nodes(), function(t) {
    var n = {};
    return ne.forEach(e.inEdges(t), function(a) {
      n[a.v] = (n[a.v] || 0) + e.edge(a).weight;
    }), n;
  });
  return ne.zipObject(e.nodes(), r);
}
function ib(e, r) {
  var t = e.x, n = e.y, a = r.x - t, i = r.y - n, s = e.width / 2, o = e.height / 2;
  if (!a && !i)
    throw new Error("Not possible to find intersection inside of the rectangle");
  var u, l;
  return Math.abs(i) * s > Math.abs(a) * o ? (i < 0 && (o = -o), u = o * a / i, l = o) : (a < 0 && (s = -s), u = s, l = s * i / a), { x: t + u, y: n + l };
}
function sb(e) {
  var r = ne.map(ne.range(Fp(e) + 1), function() {
    return [];
  });
  return ne.forEach(e.nodes(), function(t) {
    var n = e.node(t), a = n.rank;
    ne.isUndefined(a) || (r[a][n.order] = t);
  }), r;
}
function ob(e) {
  var r = ne.min(ne.map(e.nodes(), function(t) {
    return e.node(t).rank;
  }));
  ne.forEach(e.nodes(), function(t) {
    var n = e.node(t);
    ne.has(n, "rank") && (n.rank -= r);
  });
}
function ub(e) {
  var r = ne.min(ne.map(e.nodes(), function(i) {
    return e.node(i).rank;
  })), t = [];
  ne.forEach(e.nodes(), function(i) {
    var s = e.node(i).rank - r;
    t[s] || (t[s] = []), t[s].push(i);
  });
  var n = 0, a = e.graph().nodeRankFactor;
  ne.forEach(t, function(i, s) {
    ne.isUndefined(i) && s % a !== 0 ? --n : n && ne.forEach(i, function(o) {
      e.node(o).rank += n;
    });
  });
}
function cb(e, r, t, n) {
  var a = {
    width: 0,
    height: 0
  };
  return arguments.length >= 4 && (a.rank = t, a.order = n), Lp(e, "border", a, r);
}
function Fp(e) {
  return ne.max(ne.map(e.nodes(), function(r) {
    var t = e.node(r).rank;
    if (!ne.isUndefined(t))
      return t;
  }));
}
function lb(e, r) {
  var t = { lhs: [], rhs: [] };
  return ne.forEach(e, function(n) {
    r(n) ? t.lhs.push(n) : t.rhs.push(n);
  }), t;
}
function db(e, r) {
  var t = ne.now();
  try {
    return r();
  } finally {
    console.log(e + " time: " + (ne.now() - t) + "ms");
  }
}
function fb(e, r) {
  return r();
}
var jp = he, hb = Ae, pb = {
  run: vb,
  undo: yb
};
function vb(e) {
  e.graph().dummyChains = [], jp.forEach(e.edges(), function(r) {
    gb(e, r);
  });
}
function gb(e, r) {
  var t = r.v, n = e.node(t).rank, a = r.w, i = e.node(a).rank, s = r.name, o = e.edge(r), u = o.labelRank;
  if (i !== n + 1) {
    e.removeEdge(r);
    var l, c, d;
    for (d = 0, ++n; n < i; ++d, ++n)
      o.points = [], c = {
        width: 0,
        height: 0,
        edgeLabel: o,
        edgeObj: r,
        rank: n
      }, l = hb.addDummyNode(e, "edge", c, "_d"), n === u && (c.width = o.width, c.height = o.height, c.dummy = "edge-label", c.labelpos = o.labelpos), e.setEdge(t, l, { weight: o.weight }, s), d === 0 && e.graph().dummyChains.push(l), t = l;
    e.setEdge(t, a, { weight: o.weight }, s);
  }
}
function yb(e) {
  jp.forEach(e.graph().dummyChains, function(r) {
    var t = e.node(r), n = t.edgeLabel, a;
    for (e.setEdge(t.edgeObj, n); t.dummy; )
      a = e.successors(r)[0], e.removeNode(r), n.points.push({ x: t.x, y: t.y }), t.dummy === "edge-label" && (n.x = t.x, n.y = t.y, n.width = t.width, n.height = t.height), r = a, t = e.node(r);
  });
}
var Xr = he, Tt = {
  longestPath: mb,
  slack: bb
};
function mb(e) {
  var r = {};
  function t(n) {
    var a = e.node(n);
    if (Xr.has(r, n))
      return a.rank;
    r[n] = !0;
    var i = Xr.min(Xr.map(e.outEdges(n), function(s) {
      return t(s.w) - e.edge(s).minlen;
    }));
    return (i === Number.POSITIVE_INFINITY || // return value of _.map([]) for Lodash 3
    i === void 0 || // return value of _.map([]) for Lodash 4
    i === null) && (i = 0), a.rank = i;
  }
  Xr.forEach(e.sources(), t);
}
function bb(e, r) {
  return e.node(r.w).rank - e.node(r.v).rank - e.edge(r).minlen;
}
var it = he, _b = je.Graph, st = Tt.slack, $p = wb;
function wb(e) {
  var r = new _b({ directed: !1 }), t = e.nodes()[0], n = e.nodeCount();
  r.setNode(t, {});
  for (var a, i; Ib(r, e) < n; )
    a = qb(r, e), i = r.hasNode(a.v) ? st(e, a) : -st(e, a), Eb(r, e, i);
  return r;
}
function Ib(e, r) {
  function t(n) {
    it.forEach(r.nodeEdges(n), function(a) {
      var i = a.v, s = n === i ? a.w : i;
      !e.hasNode(s) && !st(r, a) && (e.setNode(s, {}), e.setEdge(n, s, {}), t(s));
    });
  }
  return it.forEach(e.nodes(), t), e.nodeCount();
}
function qb(e, r) {
  return it.minBy(r.edges(), function(t) {
    if (e.hasNode(t.v) !== e.hasNode(t.w))
      return st(r, t);
  });
}
function Eb(e, r, t) {
  it.forEach(e.nodes(), function(n) {
    r.node(n).rank += t;
  });
}
var er = he, Cb = $p, Sb = Tt.slack, Tb = Tt.longestPath, Rb = je.alg.preorder, xb = je.alg.postorder, kb = Ae.simplify, Ab = yr;
yr.initLowLimValues = iu;
yr.initCutValues = au;
yr.calcCutValue = Gp;
yr.leaveEdge = Bp;
yr.enterEdge = Up;
yr.exchangeEdges = Kp;
function yr(e) {
  e = kb(e), Tb(e);
  var r = Cb(e);
  iu(r), au(r, e);
  for (var t, n; t = Bp(r); )
    n = Up(r, e, t), Kp(r, e, t, n);
}
function au(e, r) {
  var t = xb(e, e.nodes());
  t = t.slice(0, t.length - 1), er.forEach(t, function(n) {
    Ob(e, r, n);
  });
}
function Ob(e, r, t) {
  var n = e.node(t), a = n.parent;
  e.edge(t, a).cutvalue = Gp(e, r, t);
}
function Gp(e, r, t) {
  var n = e.node(t), a = n.parent, i = !0, s = r.edge(t, a), o = 0;
  return s || (i = !1, s = r.edge(a, t)), o = s.weight, er.forEach(r.nodeEdges(t), function(u) {
    var l = u.v === t, c = l ? u.w : u.v;
    if (c !== a) {
      var d = l === i, h = r.edge(u).weight;
      if (o += d ? h : -h, Nb(e, t, c)) {
        var v = e.edge(t, c).cutvalue;
        o += d ? -v : v;
      }
    }
  }), o;
}
function iu(e, r) {
  arguments.length < 2 && (r = e.nodes()[0]), zp(e, {}, 1, r);
}
function zp(e, r, t, n, a) {
  var i = t, s = e.node(n);
  return r[n] = !0, er.forEach(e.neighbors(n), function(o) {
    er.has(r, o) || (t = zp(e, r, t, o, n));
  }), s.low = i, s.lim = t++, a ? s.parent = a : delete s.parent, t;
}
function Bp(e) {
  return er.find(e.edges(), function(r) {
    return e.edge(r).cutvalue < 0;
  });
}
function Up(e, r, t) {
  var n = t.v, a = t.w;
  r.hasEdge(n, a) || (n = t.w, a = t.v);
  var i = e.node(n), s = e.node(a), o = i, u = !1;
  i.lim > s.lim && (o = s, u = !0);
  var l = er.filter(r.edges(), function(c) {
    return u === lh(e, e.node(c.v), o) && u !== lh(e, e.node(c.w), o);
  });
  return er.minBy(l, function(c) {
    return Sb(r, c);
  });
}
function Kp(e, r, t, n) {
  var a = t.v, i = t.w;
  e.removeEdge(a, i), e.setEdge(n.v, n.w, {}), iu(e), au(e, r), Pb(e, r);
}
function Pb(e, r) {
  var t = er.find(e.nodes(), function(a) {
    return !r.node(a).parent;
  }), n = Rb(e, t);
  n = n.slice(1), er.forEach(n, function(a) {
    var i = e.node(a).parent, s = r.edge(a, i), o = !1;
    s || (s = r.edge(i, a), o = !0), r.node(a).rank = r.node(i).rank + (o ? s.minlen : -s.minlen);
  });
}
function Nb(e, r, t) {
  return e.hasEdge(r, t);
}
function lh(e, r, t) {
  return t.low <= r.lim && r.lim <= t.lim;
}
var Mb = Tt, Hp = Mb.longestPath, Db = $p, Lb = Ab, Fb = jb;
function jb(e) {
  switch (e.graph().ranker) {
    case "network-simplex":
      dh(e);
      break;
    case "tight-tree":
      Gb(e);
      break;
    case "longest-path":
      $b(e);
      break;
    default:
      dh(e);
  }
}
var $b = Hp;
function Gb(e) {
  Hp(e), Db(e);
}
function dh(e) {
  Lb(e);
}
var Do = he, zb = Bb;
function Bb(e) {
  var r = Kb(e);
  Do.forEach(e.graph().dummyChains, function(t) {
    for (var n = e.node(t), a = n.edgeObj, i = Ub(e, r, a.v, a.w), s = i.path, o = i.lca, u = 0, l = s[u], c = !0; t !== a.w; ) {
      if (n = e.node(t), c) {
        for (; (l = s[u]) !== o && e.node(l).maxRank < n.rank; )
          u++;
        l === o && (c = !1);
      }
      if (!c) {
        for (; u < s.length - 1 && e.node(l = s[u + 1]).minRank <= n.rank; )
          u++;
        l = s[u];
      }
      e.setParent(t, l), t = e.successors(t)[0];
    }
  });
}
function Ub(e, r, t, n) {
  var a = [], i = [], s = Math.min(r[t].low, r[n].low), o = Math.max(r[t].lim, r[n].lim), u, l;
  u = t;
  do
    u = e.parent(u), a.push(u);
  while (u && (r[u].low > s || o > r[u].lim));
  for (l = u, u = n; (u = e.parent(u)) !== l; )
    i.push(u);
  return { path: a.concat(i.reverse()), lca: l };
}
function Kb(e) {
  var r = {}, t = 0;
  function n(a) {
    var i = t;
    Do.forEach(e.children(a), n), r[a] = { low: i, lim: t++ };
  }
  return Do.forEach(e.children(), n), r;
}
var Ze = he, Lo = Ae, Hb = {
  run: Vb,
  cleanup: Yb
};
function Vb(e) {
  var r = Lo.addDummyNode(e, "root", {}, "_root"), t = Wb(e), n = Ze.max(Ze.values(t)) - 1, a = 2 * n + 1;
  e.graph().nestingRoot = r, Ze.forEach(e.edges(), function(s) {
    e.edge(s).minlen *= a;
  });
  var i = Qb(e) + 1;
  Ze.forEach(e.children(), function(s) {
    Vp(e, r, a, i, n, t, s);
  }), e.graph().nodeRankFactor = a;
}
function Vp(e, r, t, n, a, i, s) {
  var o = e.children(s);
  if (!o.length) {
    s !== r && e.setEdge(r, s, { weight: 0, minlen: t });
    return;
  }
  var u = Lo.addBorderNode(e, "_bt"), l = Lo.addBorderNode(e, "_bb"), c = e.node(s);
  e.setParent(u, s), c.borderTop = u, e.setParent(l, s), c.borderBottom = l, Ze.forEach(o, function(d) {
    Vp(e, r, t, n, a, i, d);
    var h = e.node(d), v = h.borderTop ? h.borderTop : d, g = h.borderBottom ? h.borderBottom : d, f = h.borderTop ? n : 2 * n, y = v !== g ? 1 : a - i[s] + 1;
    e.setEdge(u, v, {
      weight: f,
      minlen: y,
      nestingEdge: !0
    }), e.setEdge(g, l, {
      weight: f,
      minlen: y,
      nestingEdge: !0
    });
  }), e.parent(s) || e.setEdge(r, u, { weight: 0, minlen: a + i[s] });
}
function Wb(e) {
  var r = {};
  function t(n, a) {
    var i = e.children(n);
    i && i.length && Ze.forEach(i, function(s) {
      t(s, a + 1);
    }), r[n] = a;
  }
  return Ze.forEach(e.children(), function(n) {
    t(n, 1);
  }), r;
}
function Qb(e) {
  return Ze.reduce(e.edges(), function(r, t) {
    return r + e.edge(t).weight;
  }, 0);
}
function Yb(e) {
  var r = e.graph();
  e.removeNode(r.nestingRoot), delete r.nestingRoot, Ze.forEach(e.edges(), function(t) {
    var n = e.edge(t);
    n.nestingEdge && e.removeEdge(t);
  });
}
var Io = he, Jb = Ae, Xb = Zb;
function Zb(e) {
  function r(t) {
    var n = e.children(t), a = e.node(t);
    if (n.length && Io.forEach(n, r), Io.has(a, "minRank")) {
      a.borderLeft = [], a.borderRight = [];
      for (var i = a.minRank, s = a.maxRank + 1; i < s; ++i)
        fh(e, "borderLeft", "_bl", t, a, i), fh(e, "borderRight", "_br", t, a, i);
    }
  }
  Io.forEach(e.children(), r);
}
function fh(e, r, t, n, a, i) {
  var s = { width: 0, height: 0, rank: i, borderType: r }, o = a[r][i - 1], u = Jb.addDummyNode(e, "border", s, t);
  a[r][i] = u, e.setParent(u, n), o && e.setEdge(o, u, { weight: 1 });
}
var Ke = he, e_ = {
  adjust: r_,
  undo: t_
};
function r_(e) {
  var r = e.graph().rankdir.toLowerCase();
  (r === "lr" || r === "rl") && Wp(e);
}
function t_(e) {
  var r = e.graph().rankdir.toLowerCase();
  (r === "bt" || r === "rl") && n_(e), (r === "lr" || r === "rl") && (a_(e), Wp(e));
}
function Wp(e) {
  Ke.forEach(e.nodes(), function(r) {
    hh(e.node(r));
  }), Ke.forEach(e.edges(), function(r) {
    hh(e.edge(r));
  });
}
function hh(e) {
  var r = e.width;
  e.width = e.height, e.height = r;
}
function n_(e) {
  Ke.forEach(e.nodes(), function(r) {
    qo(e.node(r));
  }), Ke.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    Ke.forEach(t.points, qo), Ke.has(t, "y") && qo(t);
  });
}
function qo(e) {
  e.y = -e.y;
}
function a_(e) {
  Ke.forEach(e.nodes(), function(r) {
    Eo(e.node(r));
  }), Ke.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    Ke.forEach(t.points, Eo), Ke.has(t, "x") && Eo(t);
  });
}
function Eo(e) {
  var r = e.x;
  e.x = e.y, e.y = r;
}
var Ye = he, i_ = s_;
function s_(e) {
  var r = {}, t = Ye.filter(e.nodes(), function(o) {
    return !e.children(o).length;
  }), n = Ye.max(Ye.map(t, function(o) {
    return e.node(o).rank;
  })), a = Ye.map(Ye.range(n + 1), function() {
    return [];
  });
  function i(o) {
    if (!Ye.has(r, o)) {
      r[o] = !0;
      var u = e.node(o);
      a[u.rank].push(o), Ye.forEach(e.successors(o), i);
    }
  }
  var s = Ye.sortBy(t, function(o) {
    return e.node(o).rank;
  });
  return Ye.forEach(s, i), a;
}
var ar = he, o_ = u_;
function u_(e, r) {
  for (var t = 0, n = 1; n < r.length; ++n)
    t += c_(e, r[n - 1], r[n]);
  return t;
}
function c_(e, r, t) {
  for (var n = ar.zipObject(
    t,
    ar.map(t, function(l, c) {
      return c;
    })
  ), a = ar.flatten(ar.map(r, function(l) {
    return ar.sortBy(ar.map(e.outEdges(l), function(c) {
      return { pos: n[c.w], weight: e.edge(c).weight };
    }), "pos");
  }), !0), i = 1; i < t.length; ) i <<= 1;
  var s = 2 * i - 1;
  i -= 1;
  var o = ar.map(new Array(s), function() {
    return 0;
  }), u = 0;
  return ar.forEach(a.forEach(function(l) {
    var c = l.pos + i;
    o[c] += l.weight;
    for (var d = 0; c > 0; )
      c % 2 && (d += o[c + 1]), c = c - 1 >> 1, o[c] += l.weight;
    u += l.weight * d;
  })), u;
}
var ph = he, l_ = d_;
function d_(e, r) {
  return ph.map(r, function(t) {
    var n = e.inEdges(t);
    if (n.length) {
      var a = ph.reduce(n, function(i, s) {
        var o = e.edge(s), u = e.node(s.v);
        return {
          sum: i.sum + o.weight * u.order,
          weight: i.weight + o.weight
        };
      }, { sum: 0, weight: 0 });
      return {
        v: t,
        barycenter: a.sum / a.weight,
        weight: a.weight
      };
    } else
      return { v: t };
  });
}
var Ne = he, f_ = h_;
function h_(e, r) {
  var t = {};
  Ne.forEach(e, function(a, i) {
    var s = t[a.v] = {
      indegree: 0,
      in: [],
      out: [],
      vs: [a.v],
      i
    };
    Ne.isUndefined(a.barycenter) || (s.barycenter = a.barycenter, s.weight = a.weight);
  }), Ne.forEach(r.edges(), function(a) {
    var i = t[a.v], s = t[a.w];
    !Ne.isUndefined(i) && !Ne.isUndefined(s) && (s.indegree++, i.out.push(t[a.w]));
  });
  var n = Ne.filter(t, function(a) {
    return !a.indegree;
  });
  return p_(n);
}
function p_(e) {
  var r = [];
  function t(i) {
    return function(s) {
      s.merged || (Ne.isUndefined(s.barycenter) || Ne.isUndefined(i.barycenter) || s.barycenter >= i.barycenter) && v_(i, s);
    };
  }
  function n(i) {
    return function(s) {
      s.in.push(i), --s.indegree === 0 && e.push(s);
    };
  }
  for (; e.length; ) {
    var a = e.pop();
    r.push(a), Ne.forEach(a.in.reverse(), t(a)), Ne.forEach(a.out, n(a));
  }
  return Ne.map(
    Ne.filter(r, function(i) {
      return !i.merged;
    }),
    function(i) {
      return Ne.pick(i, ["vs", "i", "barycenter", "weight"]);
    }
  );
}
function v_(e, r) {
  var t = 0, n = 0;
  e.weight && (t += e.barycenter * e.weight, n += e.weight), r.weight && (t += r.barycenter * r.weight, n += r.weight), e.vs = r.vs.concat(e.vs), e.barycenter = t / n, e.weight = n, e.i = Math.min(r.i, e.i), r.merged = !0;
}
var Lr = he, g_ = Ae, y_ = m_;
function m_(e, r) {
  var t = g_.partition(e, function(c) {
    return Lr.has(c, "barycenter");
  }), n = t.lhs, a = Lr.sortBy(t.rhs, function(c) {
    return -c.i;
  }), i = [], s = 0, o = 0, u = 0;
  n.sort(b_(!!r)), u = vh(i, a, u), Lr.forEach(n, function(c) {
    u += c.vs.length, i.push(c.vs), s += c.barycenter * c.weight, o += c.weight, u = vh(i, a, u);
  });
  var l = { vs: Lr.flatten(i, !0) };
  return o && (l.barycenter = s / o, l.weight = o), l;
}
function vh(e, r, t) {
  for (var n; r.length && (n = Lr.last(r)).i <= t; )
    r.pop(), e.push(n.vs), t++;
  return t;
}
function b_(e) {
  return function(r, t) {
    return r.barycenter < t.barycenter ? -1 : r.barycenter > t.barycenter ? 1 : e ? t.i - r.i : r.i - t.i;
  };
}
var ir = he, __ = l_, w_ = f_, I_ = y_, q_ = Qp;
function Qp(e, r, t, n) {
  var a = e.children(r), i = e.node(r), s = i ? i.borderLeft : void 0, o = i ? i.borderRight : void 0, u = {};
  s && (a = ir.filter(a, function(g) {
    return g !== s && g !== o;
  }));
  var l = __(e, a);
  ir.forEach(l, function(g) {
    if (e.children(g.v).length) {
      var f = Qp(e, g.v, t, n);
      u[g.v] = f, ir.has(f, "barycenter") && C_(g, f);
    }
  });
  var c = w_(l, t);
  E_(c, u);
  var d = I_(c, n);
  if (s && (d.vs = ir.flatten([s, d.vs, o], !0), e.predecessors(s).length)) {
    var h = e.node(e.predecessors(s)[0]), v = e.node(e.predecessors(o)[0]);
    ir.has(d, "barycenter") || (d.barycenter = 0, d.weight = 0), d.barycenter = (d.barycenter * d.weight + h.order + v.order) / (d.weight + 2), d.weight += 2;
  }
  return d;
}
function E_(e, r) {
  ir.forEach(e, function(t) {
    t.vs = ir.flatten(t.vs.map(function(n) {
      return r[n] ? r[n].vs : n;
    }), !0);
  });
}
function C_(e, r) {
  ir.isUndefined(e.barycenter) ? (e.barycenter = r.barycenter, e.weight = r.weight) : (e.barycenter = (e.barycenter * e.weight + r.barycenter * r.weight) / (e.weight + r.weight), e.weight += r.weight);
}
var Fr = he, S_ = je.Graph, T_ = R_;
function R_(e, r, t) {
  var n = x_(e), a = new S_({ compound: !0 }).setGraph({ root: n }).setDefaultNodeLabel(function(i) {
    return e.node(i);
  });
  return Fr.forEach(e.nodes(), function(i) {
    var s = e.node(i), o = e.parent(i);
    (s.rank === r || s.minRank <= r && r <= s.maxRank) && (a.setNode(i), a.setParent(i, o || n), Fr.forEach(e[t](i), function(u) {
      var l = u.v === i ? u.w : u.v, c = a.edge(l, i), d = Fr.isUndefined(c) ? 0 : c.weight;
      a.setEdge(l, i, { weight: e.edge(u).weight + d });
    }), Fr.has(s, "minRank") && a.setNode(i, {
      borderLeft: s.borderLeft[r],
      borderRight: s.borderRight[r]
    }));
  }), a;
}
function x_(e) {
  for (var r; e.hasNode(r = Fr.uniqueId("_root")); ) ;
  return r;
}
var k_ = he, A_ = O_;
function O_(e, r, t) {
  var n = {}, a;
  k_.forEach(t, function(i) {
    for (var s = e.parent(i), o, u; s; ) {
      if (o = e.parent(s), o ? (u = n[o], n[o] = s) : (u = a, a = s), u && u !== s) {
        r.setEdge(u, s);
        return;
      }
      s = o;
    }
  });
}
var or = he, P_ = i_, N_ = o_, M_ = q_, D_ = T_, L_ = A_, F_ = je.Graph, gh = Ae, j_ = $_;
function $_(e) {
  var r = gh.maxRank(e), t = yh(e, or.range(1, r + 1), "inEdges"), n = yh(e, or.range(r - 1, -1, -1), "outEdges"), a = P_(e);
  mh(e, a);
  for (var i = Number.POSITIVE_INFINITY, s, o = 0, u = 0; u < 4; ++o, ++u) {
    G_(o % 2 ? t : n, o % 4 >= 2), a = gh.buildLayerMatrix(e);
    var l = N_(e, a);
    l < i && (u = 0, s = or.cloneDeep(a), i = l);
  }
  mh(e, s);
}
function yh(e, r, t) {
  return or.map(r, function(n) {
    return D_(e, n, t);
  });
}
function G_(e, r) {
  var t = new F_();
  or.forEach(e, function(n) {
    var a = n.graph().root, i = M_(n, a, t, r);
    or.forEach(i.vs, function(s, o) {
      n.node(s).order = o;
    }), L_(n, t, i.vs);
  });
}
function mh(e, r) {
  or.forEach(r, function(t) {
    or.forEach(t, function(n, a) {
      e.node(n).order = a;
    });
  });
}
var K = he, z_ = je.Graph, B_ = Ae, U_ = {
  positionX: rw
};
function K_(e, r) {
  var t = {};
  function n(a, i) {
    var s = 0, o = 0, u = a.length, l = K.last(i);
    return K.forEach(i, function(c, d) {
      var h = V_(e, c), v = h ? e.node(h).order : u;
      (h || c === l) && (K.forEach(i.slice(o, d + 1), function(g) {
        K.forEach(e.predecessors(g), function(f) {
          var y = e.node(f), q = y.order;
          (q < s || v < q) && !(y.dummy && e.node(g).dummy) && Yp(t, f, g);
        });
      }), o = d + 1, s = v);
    }), i;
  }
  return K.reduce(r, n), t;
}
function H_(e, r) {
  var t = {};
  function n(i, s, o, u, l) {
    var c;
    K.forEach(K.range(s, o), function(d) {
      c = i[d], e.node(c).dummy && K.forEach(e.predecessors(c), function(h) {
        var v = e.node(h);
        v.dummy && (v.order < u || v.order > l) && Yp(t, h, c);
      });
    });
  }
  function a(i, s) {
    var o = -1, u, l = 0;
    return K.forEach(s, function(c, d) {
      if (e.node(c).dummy === "border") {
        var h = e.predecessors(c);
        h.length && (u = e.node(h[0]).order, n(s, l, d, o, u), l = d, o = u);
      }
      n(s, l, s.length, u, i.length);
    }), s;
  }
  return K.reduce(r, a), t;
}
function V_(e, r) {
  if (e.node(r).dummy)
    return K.find(e.predecessors(r), function(t) {
      return e.node(t).dummy;
    });
}
function Yp(e, r, t) {
  if (r > t) {
    var n = r;
    r = t, t = n;
  }
  var a = e[r];
  a || (e[r] = a = {}), a[t] = !0;
}
function W_(e, r, t) {
  if (r > t) {
    var n = r;
    r = t, t = n;
  }
  return K.has(e[r], t);
}
function Q_(e, r, t, n) {
  var a = {}, i = {}, s = {};
  return K.forEach(r, function(o) {
    K.forEach(o, function(u, l) {
      a[u] = u, i[u] = u, s[u] = l;
    });
  }), K.forEach(r, function(o) {
    var u = -1;
    K.forEach(o, function(l) {
      var c = n(l);
      if (c.length) {
        c = K.sortBy(c, function(f) {
          return s[f];
        });
        for (var d = (c.length - 1) / 2, h = Math.floor(d), v = Math.ceil(d); h <= v; ++h) {
          var g = c[h];
          i[l] === l && u < s[g] && !W_(t, l, g) && (i[g] = l, i[l] = a[l] = a[g], u = s[g]);
        }
      }
    });
  }), { root: a, align: i };
}
function Y_(e, r, t, n, a) {
  var i = {}, s = J_(e, r, t, a), o = a ? "borderLeft" : "borderRight";
  function u(d, h) {
    for (var v = s.nodes(), g = v.pop(), f = {}; g; )
      f[g] ? d(g) : (f[g] = !0, v.push(g), v = v.concat(h(g))), g = v.pop();
  }
  function l(d) {
    i[d] = s.inEdges(d).reduce(function(h, v) {
      return Math.max(h, i[v.v] + s.edge(v));
    }, 0);
  }
  function c(d) {
    var h = s.outEdges(d).reduce(function(g, f) {
      return Math.min(g, i[f.w] - s.edge(f));
    }, Number.POSITIVE_INFINITY), v = e.node(d);
    h !== Number.POSITIVE_INFINITY && v.borderType !== o && (i[d] = Math.max(i[d], h));
  }
  return u(l, s.predecessors.bind(s)), u(c, s.successors.bind(s)), K.forEach(n, function(d) {
    i[d] = i[t[d]];
  }), i;
}
function J_(e, r, t, n) {
  var a = new z_(), i = e.graph(), s = tw(i.nodesep, i.edgesep, n);
  return K.forEach(r, function(o) {
    var u;
    K.forEach(o, function(l) {
      var c = t[l];
      if (a.setNode(c), u) {
        var d = t[u], h = a.edge(d, c);
        a.setEdge(d, c, Math.max(s(e, l, u), h || 0));
      }
      u = l;
    });
  }), a;
}
function X_(e, r) {
  return K.minBy(K.values(r), function(t) {
    var n = Number.NEGATIVE_INFINITY, a = Number.POSITIVE_INFINITY;
    return K.forIn(t, function(i, s) {
      var o = nw(e, s) / 2;
      n = Math.max(i + o, n), a = Math.min(i - o, a);
    }), n - a;
  });
}
function Z_(e, r) {
  var t = K.values(r), n = K.min(t), a = K.max(t);
  K.forEach(["u", "d"], function(i) {
    K.forEach(["l", "r"], function(s) {
      var o = i + s, u = e[o], l;
      if (u !== r) {
        var c = K.values(u);
        l = s === "l" ? n - K.min(c) : a - K.max(c), l && (e[o] = K.mapValues(u, function(d) {
          return d + l;
        }));
      }
    });
  });
}
function ew(e, r) {
  return K.mapValues(e.ul, function(t, n) {
    if (r)
      return e[r.toLowerCase()][n];
    var a = K.sortBy(K.map(e, n));
    return (a[1] + a[2]) / 2;
  });
}
function rw(e) {
  var r = B_.buildLayerMatrix(e), t = K.merge(
    K_(e, r),
    H_(e, r)
  ), n = {}, a;
  K.forEach(["u", "d"], function(s) {
    a = s === "u" ? r : K.values(r).reverse(), K.forEach(["l", "r"], function(o) {
      o === "r" && (a = K.map(a, function(d) {
        return K.values(d).reverse();
      }));
      var u = (s === "u" ? e.predecessors : e.successors).bind(e), l = Q_(e, a, t, u), c = Y_(
        e,
        a,
        l.root,
        l.align,
        o === "r"
      );
      o === "r" && (c = K.mapValues(c, function(d) {
        return -d;
      })), n[s + o] = c;
    });
  });
  var i = X_(e, n);
  return Z_(n, i), ew(n, e.graph().align);
}
function tw(e, r, t) {
  return function(n, a, i) {
    var s = n.node(a), o = n.node(i), u = 0, l;
    if (u += s.width / 2, K.has(s, "labelpos"))
      switch (s.labelpos.toLowerCase()) {
        case "l":
          l = -s.width / 2;
          break;
        case "r":
          l = s.width / 2;
          break;
      }
    if (l && (u += t ? l : -l), l = 0, u += (s.dummy ? r : e) / 2, u += (o.dummy ? r : e) / 2, u += o.width / 2, K.has(o, "labelpos"))
      switch (o.labelpos.toLowerCase()) {
        case "l":
          l = o.width / 2;
          break;
        case "r":
          l = -o.width / 2;
          break;
      }
    return l && (u += t ? l : -l), l = 0, u;
  };
}
function nw(e, r) {
  return e.node(r).width;
}
var jr = he, Jp = Ae, aw = U_.positionX, iw = sw;
function sw(e) {
  e = Jp.asNonCompoundGraph(e), ow(e), jr.forEach(aw(e), function(r, t) {
    e.node(t).x = r;
  });
}
function ow(e) {
  var r = Jp.buildLayerMatrix(e), t = e.graph().ranksep, n = 0;
  jr.forEach(r, function(a) {
    var i = jr.max(jr.map(a, function(s) {
      return e.node(s).height;
    }));
    jr.forEach(a, function(s) {
      e.node(s).y = n + i / 2;
    }), n += i + t;
  });
}
var X = he, bh = Jm, _h = pb, uw = Fb, cw = Ae.normalizeRanks, lw = zb, dw = Ae.removeEmptyRanks, wh = Hb, fw = Xb, Ih = e_, hw = j_, pw = iw, ur = Ae, vw = je.Graph, gw = yw;
function yw(e, r) {
  var t = r && r.debugTiming ? ur.time : ur.notime;
  t("layout", function() {
    var n = t("  buildLayoutGraph", function() {
      return Rw(e);
    });
    t("  runLayout", function() {
      mw(n, t);
    }), t("  updateInputGraph", function() {
      bw(e, n);
    });
  });
}
function mw(e, r) {
  r("    makeSpaceForEdgeLabels", function() {
    xw(e);
  }), r("    removeSelfEdges", function() {
    Fw(e);
  }), r("    acyclic", function() {
    bh.run(e);
  }), r("    nestingGraph.run", function() {
    wh.run(e);
  }), r("    rank", function() {
    uw(ur.asNonCompoundGraph(e));
  }), r("    injectEdgeLabelProxies", function() {
    kw(e);
  }), r("    removeEmptyRanks", function() {
    dw(e);
  }), r("    nestingGraph.cleanup", function() {
    wh.cleanup(e);
  }), r("    normalizeRanks", function() {
    cw(e);
  }), r("    assignRankMinMax", function() {
    Aw(e);
  }), r("    removeEdgeLabelProxies", function() {
    Ow(e);
  }), r("    normalize.run", function() {
    _h.run(e);
  }), r("    parentDummyChains", function() {
    lw(e);
  }), r("    addBorderSegments", function() {
    fw(e);
  }), r("    order", function() {
    hw(e);
  }), r("    insertSelfEdges", function() {
    jw(e);
  }), r("    adjustCoordinateSystem", function() {
    Ih.adjust(e);
  }), r("    position", function() {
    pw(e);
  }), r("    positionSelfEdges", function() {
    $w(e);
  }), r("    removeBorderNodes", function() {
    Lw(e);
  }), r("    normalize.undo", function() {
    _h.undo(e);
  }), r("    fixupEdgeLabelCoords", function() {
    Mw(e);
  }), r("    undoCoordinateSystem", function() {
    Ih.undo(e);
  }), r("    translateGraph", function() {
    Pw(e);
  }), r("    assignNodeIntersects", function() {
    Nw(e);
  }), r("    reversePoints", function() {
    Dw(e);
  }), r("    acyclic.undo", function() {
    bh.undo(e);
  });
}
function bw(e, r) {
  X.forEach(e.nodes(), function(t) {
    var n = e.node(t), a = r.node(t);
    n && (n.x = a.x, n.y = a.y, r.children(t).length && (n.width = a.width, n.height = a.height));
  }), X.forEach(e.edges(), function(t) {
    var n = e.edge(t), a = r.edge(t);
    n.points = a.points, X.has(a, "x") && (n.x = a.x, n.y = a.y);
  }), e.graph().width = r.graph().width, e.graph().height = r.graph().height;
}
var _w = ["nodesep", "edgesep", "ranksep", "marginx", "marginy"], ww = { ranksep: 50, edgesep: 20, nodesep: 50, rankdir: "tb" }, Iw = ["acyclicer", "ranker", "rankdir", "align"], qw = ["width", "height"], Ew = { width: 0, height: 0 }, Cw = ["minlen", "weight", "width", "height", "labeloffset"], Sw = {
  minlen: 1,
  weight: 1,
  width: 0,
  height: 0,
  labeloffset: 10,
  labelpos: "r"
}, Tw = ["labelpos"];
function Rw(e) {
  var r = new vw({ multigraph: !0, compound: !0 }), t = So(e.graph());
  return r.setGraph(X.merge(
    {},
    ww,
    Co(t, _w),
    X.pick(t, Iw)
  )), X.forEach(e.nodes(), function(n) {
    var a = So(e.node(n));
    r.setNode(n, X.defaults(Co(a, qw), Ew)), r.setParent(n, e.parent(n));
  }), X.forEach(e.edges(), function(n) {
    var a = So(e.edge(n));
    r.setEdge(n, X.merge(
      {},
      Sw,
      Co(a, Cw),
      X.pick(a, Tw)
    ));
  }), r;
}
function xw(e) {
  var r = e.graph();
  r.ranksep /= 2, X.forEach(e.edges(), function(t) {
    var n = e.edge(t);
    n.minlen *= 2, n.labelpos.toLowerCase() !== "c" && (r.rankdir === "TB" || r.rankdir === "BT" ? n.width += n.labeloffset : n.height += n.labeloffset);
  });
}
function kw(e) {
  X.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (t.width && t.height) {
      var n = e.node(r.v), a = e.node(r.w), i = { rank: (a.rank - n.rank) / 2 + n.rank, e: r };
      ur.addDummyNode(e, "edge-proxy", i, "_ep");
    }
  });
}
function Aw(e) {
  var r = 0;
  X.forEach(e.nodes(), function(t) {
    var n = e.node(t);
    n.borderTop && (n.minRank = e.node(n.borderTop).rank, n.maxRank = e.node(n.borderBottom).rank, r = X.max(r, n.maxRank));
  }), e.graph().maxRank = r;
}
function Ow(e) {
  X.forEach(e.nodes(), function(r) {
    var t = e.node(r);
    t.dummy === "edge-proxy" && (e.edge(t.e).labelRank = t.rank, e.removeNode(r));
  });
}
function Pw(e) {
  var r = Number.POSITIVE_INFINITY, t = 0, n = Number.POSITIVE_INFINITY, a = 0, i = e.graph(), s = i.marginx || 0, o = i.marginy || 0;
  function u(l) {
    var c = l.x, d = l.y, h = l.width, v = l.height;
    r = Math.min(r, c - h / 2), t = Math.max(t, c + h / 2), n = Math.min(n, d - v / 2), a = Math.max(a, d + v / 2);
  }
  X.forEach(e.nodes(), function(l) {
    u(e.node(l));
  }), X.forEach(e.edges(), function(l) {
    var c = e.edge(l);
    X.has(c, "x") && u(c);
  }), r -= s, n -= o, X.forEach(e.nodes(), function(l) {
    var c = e.node(l);
    c.x -= r, c.y -= n;
  }), X.forEach(e.edges(), function(l) {
    var c = e.edge(l);
    X.forEach(c.points, function(d) {
      d.x -= r, d.y -= n;
    }), X.has(c, "x") && (c.x -= r), X.has(c, "y") && (c.y -= n);
  }), i.width = t - r + s, i.height = a - n + o;
}
function Nw(e) {
  X.forEach(e.edges(), function(r) {
    var t = e.edge(r), n = e.node(r.v), a = e.node(r.w), i, s;
    t.points ? (i = t.points[0], s = t.points[t.points.length - 1]) : (t.points = [], i = a, s = n), t.points.unshift(ur.intersectRect(n, i)), t.points.push(ur.intersectRect(a, s));
  });
}
function Mw(e) {
  X.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (X.has(t, "x"))
      switch ((t.labelpos === "l" || t.labelpos === "r") && (t.width -= t.labeloffset), t.labelpos) {
        case "l":
          t.x -= t.width / 2 + t.labeloffset;
          break;
        case "r":
          t.x += t.width / 2 + t.labeloffset;
          break;
      }
  });
}
function Dw(e) {
  X.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    t.reversed && t.points.reverse();
  });
}
function Lw(e) {
  X.forEach(e.nodes(), function(r) {
    if (e.children(r).length) {
      var t = e.node(r), n = e.node(t.borderTop), a = e.node(t.borderBottom), i = e.node(X.last(t.borderLeft)), s = e.node(X.last(t.borderRight));
      t.width = Math.abs(s.x - i.x), t.height = Math.abs(a.y - n.y), t.x = i.x + t.width / 2, t.y = n.y + t.height / 2;
    }
  }), X.forEach(e.nodes(), function(r) {
    e.node(r).dummy === "border" && e.removeNode(r);
  });
}
function Fw(e) {
  X.forEach(e.edges(), function(r) {
    if (r.v === r.w) {
      var t = e.node(r.v);
      t.selfEdges || (t.selfEdges = []), t.selfEdges.push({ e: r, label: e.edge(r) }), e.removeEdge(r);
    }
  });
}
function jw(e) {
  var r = ur.buildLayerMatrix(e);
  X.forEach(r, function(t) {
    var n = 0;
    X.forEach(t, function(a, i) {
      var s = e.node(a);
      s.order = i + n, X.forEach(s.selfEdges, function(o) {
        ur.addDummyNode(e, "selfedge", {
          width: o.label.width,
          height: o.label.height,
          rank: s.rank,
          order: i + ++n,
          e: o.e,
          label: o.label
        }, "_se");
      }), delete s.selfEdges;
    });
  });
}
function $w(e) {
  X.forEach(e.nodes(), function(r) {
    var t = e.node(r);
    if (t.dummy === "selfedge") {
      var n = e.node(t.e.v), a = n.x + n.width / 2, i = n.y, s = t.x - a, o = n.height / 2;
      e.setEdge(t.e, t.label), e.removeNode(r), t.label.points = [
        { x: a + 2 * s / 3, y: i - o },
        { x: a + 5 * s / 6, y: i - o },
        { x: a + s, y: i },
        { x: a + 5 * s / 6, y: i + o },
        { x: a + 2 * s / 3, y: i + o }
      ], t.label.x = t.x, t.label.y = t.y;
    }
  });
}
function Co(e, r) {
  return X.mapValues(X.pick(e, r), Number);
}
function So(e) {
  var r = {};
  return X.forEach(e, function(t, n) {
    r[n.toLowerCase()] = t;
  }), r;
}
var Zr = he, Gw = Ae, zw = je.Graph, Bw = {
  debugOrdering: Uw
};
function Uw(e) {
  var r = Gw.buildLayerMatrix(e), t = new zw({ compound: !0, multigraph: !0 }).setGraph({});
  return Zr.forEach(e.nodes(), function(n) {
    t.setNode(n, { label: n }), t.setParent(n, "layer" + e.node(n).rank);
  }), Zr.forEach(e.edges(), function(n) {
    t.setEdge(n.v, n.w, {}, n.name);
  }), Zr.forEach(r, function(n, a) {
    var i = "layer" + a;
    t.setNode(i, { rank: "same" }), Zr.reduce(n, function(s, o) {
      return t.setEdge(s, o, { style: "invis" }), o;
    });
  }), t;
}
var Kw = "0.8.5", Hw = {
  graphlib: je,
  layout: gw,
  debug: Bw,
  util: {
    time: Ae.time,
    notime: Ae.notime
  },
  version: Kw
};
const qh = /* @__PURE__ */ Yv(Hw), $r = 148, Gr = 46, su = 80, ou = 44;
function Vw(e) {
  if (e.nodes.length === 0) return e;
  const r = new qh.graphlib.Graph();
  r.setDefaultEdgeLabel(() => ({})), r.setGraph({ rankdir: "LR", nodesep: 60, ranksep: 120, marginx: 40, marginy: 40 });
  for (const n of e.nodes) r.setNode(n.id, { width: $r, height: Gr });
  for (const n of e.edges) r.setEdge(n.source, n.target);
  qh.layout(r);
  const t = {};
  for (const n of e.nodes) {
    const a = r.node(n.id);
    t[n.id] = a ? { x: Math.round(a.x - $r / 2), y: Math.round(a.y - Gr / 2) } : { x: n.x, y: n.y };
  }
  return {
    ...e,
    nodes: e.nodes.map((n) => ({ ...n, ...t[n.id] })),
    layout: { ...e.layout, positions: t }
  };
}
const Xp = (e, r) => Math.abs(e.x - r.x) < $r + su && Math.abs(e.y - r.y) < Gr + ou;
function Ww(e) {
  const r = [], t = e.nodes.map((a) => {
    let i = Number.isFinite(a.x) ? a.x : 0, s = Number.isFinite(a.y) ? a.y : 0, o = 0;
    for (; r.some((u) => Xp({ x: i, y: s }, u)) && o++ < 200; )
      i += $r + su, i > 2400 && (i = 80, s += Gr + ou);
    return r.push({ x: i, y: s }), { ...a, x: i, y: s };
  });
  if (t.every((a, i) => a.x === e.nodes[i].x && a.y === e.nodes[i].y)) return e;
  const n = Object.fromEntries(t.map((a) => [a.id, { x: a.x, y: a.y }]));
  return { ...e, nodes: t, layout: { ...e.layout, positions: n } };
}
function To(e, r, t) {
  let n = { x: Math.round(r), y: Math.round(t) }, a = 0;
  for (; e.nodes.some((i) => Xp(n, i)) && a++ < 200; )
    n = { x: n.x + $r + su, y: n.y }, n.x > 2400 && (n = { x: 80, y: n.y + Gr + ou });
  return n;
}
const { Header: Qw, Content: Yw } = tv;
function Jw({ ctx: e }) {
  var uu, cu;
  const [r, t] = nv.useMessage(), [n, a] = I([]), [i, s] = I([]), [o, u] = I([]), [l, c] = I([]), [d, h] = I(""), [v, g] = I(1), [f, y] = I(!0), [q, T] = I([]), [_, x] = I(() => et()), [N, m] = I(null), [S, M] = I(null), [B, z] = I([]), [w, D] = I(!1), [L, J] = I(!0), [re, U] = I(!1), [Q, V] = I(!1), [C, F] = I(0), A = Ce([]), Y = Ce([]), ae = Ce(null), Ve = Ce(null), [qe, mr] = I(null), [xr, kr] = I({}), [lr, br] = I(!1), [O, pe] = I(!1), ve = Ce(1), _e = Ce(e);
  _e.current = e;
  const W = Ce(_.trigger);
  ye(() => {
    W.current = _.trigger;
  }, [_.trigger]), ye(() => {
    let E = !0;
    const P = _e.current.hubQuery;
    return Promise.allSettled([
      P("industrial/nodes"),
      P("industrial/workflows"),
      P("industrial/resources")
    ]).then(async ([se, ie, le]) => {
      var Be;
      if (!E) return;
      const de = [];
      if (se.status === "fulfilled" ? a(Ah(se.value)) : de.push(`节点描述: ${String(se.reason)}`), ie.status === "fulfilled") {
        const ge = Nh(ie.value);
        s(ge);
        const we = new URLSearchParams(window.location.search).get("workflowId"), Pe = we && ge.some((Ue) => Ue.workflowId === we) ? we : (Be = ge[0]) == null ? void 0 : Be.workflowId;
        Pe && We(Pe);
      } else de.push(`工作流: ${String(ie.reason)}`);
      if (le.status === "fulfilled") {
        const ge = Oh(le.value);
        c(ge);
        const we = ge.map((Pe) => Pe.id);
        u(we), h((Pe) => Pe || we[0] || "");
      } else de.push(`资源: ${String(le.reason)}`);
      T(de);
      try {
        const ge = await _e.current.hubQuery("industrial/file-samples");
        E && kr((ge == null ? void 0 : ge.data) ?? ge ?? {});
      } catch {
      }
    }).finally(() => {
      E && y(!1);
    }), () => {
      E = !1;
    };
  }, []);
  const oe = ot(
    () => new Map(n.map((E) => [E.nodeTypeId, E])),
    [n]
  ), ee = _.nodes.find((E) => E.id === N) ?? null, Oe = j(async (E, P, se) => {
    D(!0);
    try {
      return await ke().api.hubCommand(E, P), r.success(se), !0;
    } catch (ie) {
      return r.error(`${E} failed: ${ie instanceof Error ? ie.message : String(ie)}`), !1;
    } finally {
      D(!1);
    }
  }, [r]), We = j(async (E) => {
    try {
      const P = await e.hubQuery("industrial/workflow-definitions"), ie = (Se(P) ?? []).find((ge) => {
        const we = ge;
        return String((we == null ? void 0 : we.workflowId) ?? "") === E;
      }), le = ie === void 0 ? { ...et(E), name: E } : Dv(ie), de = Ww(le);
      x(de), A.current = [], Y.current = [], F((ge) => ge + 1), g(Number(de.layout.lookbackDays ?? 1) || 1);
      const Be = le.nodes.map((ge) => /^n(\d+)$/.exec(ge.id)).filter(Boolean).reduce((ge, we) => Math.max(ge, Number(we[1])), 0);
      ve.current = Be + 1, m(null), U(!1), z([]), V(!1);
    } catch (P) {
      r.error(`加载工作流失败: ${P instanceof Error ? P.message : String(P)}`);
    }
  }, [e, r]), _r = j(async () => {
    try {
      const E = Bv(await e.hubQuery("industrial/execution"));
      if (E === null) return;
      mr(E);
    } catch {
    }
  }, [e]), wr = j(async () => {
    var se;
    const E = new Set(_.resourceReferences ?? []);
    for (const ie of _.nodes)
      for (const le of ["resourceId", "targetResourceId"]) {
        const de = (se = ie.config) == null ? void 0 : se[le];
        typeof de == "string" && de.trim() && E.add(de.trim());
      }
    const P = {
      ..._,
      trigger: W.current,
      resourceReferences: [...E],
      layout: { ..._.layout, lookbackDays: v }
    };
    await Oe("industrial/save-workflow", P, "已保存") && (x(P), V(!1));
  }, [Oe, _, v]), $e = j(async () => {
    if (Q) {
      r.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await Oe("industrial/publish-workflow", { workflowId: _.workflowId }, "已发布") && await _r();
  }, [Oe, _.workflowId, Q, r, _r]), Ee = j(async () => {
    if (Q) {
      r.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await Oe(
      "industrial/run-workflow",
      { workflowId: _.workflowId, resourceId: d },
      "采集已启动"
    ) && await _r();
  }, [Oe, _.workflowId, Q, r, _r, d]), ue = j((E) => {
    E !== _ && (A.current.length >= 50 && A.current.shift(), A.current.push(_), Y.current = [], F((P) => P + 1), x(E), V(!0));
  }, [_]), nr = j(() => {
    const E = A.current.pop();
    E && (Y.current.push(_), x(E), V(!0), F((P) => P + 1));
  }, [_]), Ir = j(() => {
    const E = Y.current.pop();
    E && (A.current.push(_), x(E), V(!0), F((P) => P + 1));
  }, [_]);
  ye(() => {
    const E = (P) => {
      const se = P.target;
      if (se && /INPUT|TEXTAREA|SELECT/.test(se.tagName)) return;
      const ie = P.key.toLowerCase();
      if ((P.ctrlKey || P.metaKey) && ie === "z") {
        P.preventDefault(), P.shiftKey ? Ir() : nr();
        return;
      }
      if ((P.ctrlKey || P.metaKey) && ie === "y") {
        P.preventDefault(), Ir();
        return;
      }
      if (P.key === "Delete" && N) {
        P.preventDefault(), ue(At(_, N)), m(null);
        return;
      }
      if ((P.ctrlKey || P.metaKey) && ie === "c" && N) {
        Ve.current = _.nodes.find((le) => le.id === N) ?? null;
        return;
      }
      if ((P.ctrlKey || P.metaKey) && ie === "v" && Ve.current) {
        P.preventDefault();
        const le = Je(oe, Ve.current.type);
        if (!le) return;
        const de = `n${ve.current++}`, Be = To(_, Ve.current.x + 40, Ve.current.y + 40);
        ue({
          ..._,
          nodes: [..._.nodes, kt(le, de, Be.x, Be.y)]
        }), m(de);
      }
    };
    return window.addEventListener("keydown", E), () => window.removeEventListener("keydown", E);
  }, [oe, ue, _, Ir, N, nr]);
  const Vr = j(() => {
    ue(Vw(_)), requestAnimationFrame(() => {
      var E, P;
      return (P = (E = ae.current) == null ? void 0 : E.fitView) == null ? void 0 : P.call(E, { when: "always" });
    });
  }, [ue, _]), Ge = j((E, P, se) => {
    const ie = Je(oe, E);
    if (!ie) return;
    const le = `n${ve.current++}`, de = To(_, P, se);
    ue({ ..._, nodes: [..._.nodes, kt(ie, le, de.x, de.y)] }), m(le), U(!0), z([]);
  }, [oe, ue, _]), Wr = j((E) => {
    const P = ve.current - 1;
    Ge(E, 80 + P % 3 * 190, 80 + Math.floor(P / 3) * 130);
  }, [Ge]), Ar = j((E, P) => {
    const se = _.nodes.find((Ue) => Ue.id === E), ie = _.nodes.find((Ue) => Ue.id === P);
    if (!se || !ie)
      return r.warning("连线失败：端点不在画布上"), null;
    const le = Je(oe, se.type), de = Je(oe, ie.type);
    if (!le || !de) return null;
    let Be = se, ge = ie, we = vu(le, de);
    if (!we) {
      const Ue = vu(de, le);
      if (!Ue)
        return r.warning(`端口类型不兼容：${le.name} → ${de.name}`), null;
      Be = ie, ge = se, we = Ue;
    }
    const Pe = pv(_, {
      source: Be.id,
      sourcePort: we.sourcePort,
      target: ge.id,
      targetPort: we.targetPort
    }, oe);
    return Pe.issue ? (r.warning(`${Pe.issue.code}: ${Pe.issue.message}`), z((Ue) => [...Ue, Pe.issue]), null) : Pe.definition === _ ? (r.info("这条连线已经存在"), null) : (ue(Pe.definition), we);
  }, [oe, ue, _, r]), Rt = j(() => {
    const E = bv(_, oe, new Set(o));
    z(E), E.length === 0 ? r.success("本地校验通过（发布时服务端仍会复核）") : (r.warning(`发现 ${E.length} 个问题，见属性抽屉定位列表`), U(!0));
  }, [_, oe, o, r]), Qr = j((E) => {
    ue(At(_, E)), N === E && m(null);
  }, [ue, _, N]), ze = j((E) => {
    const P = _.nodes.find((de) => de.id === E);
    if (!P) return;
    const se = Je(oe, P.type);
    if (!se) return;
    const ie = `n${ve.current++}`, le = To(_, P.x + 40, P.y + 40);
    ue({ ..._, nodes: [
      ..._.nodes,
      { ...kt(se, ie, le.x, le.y), config: { ...P.config } }
    ] }), m(ie);
  }, [oe, ue, _]), Qe = j((E) => {
    const P = _.edges.findIndex((se) => `e:${se.source}:${se.sourcePort}>${se.target}:${se.targetPort}` === E);
    P >= 0 && ue(gu(_, P));
  }, [ue, _]), Zp = j((E, P) => {
    ue({
      ..._,
      edges: _.edges.map((se, ie) => ie === E ? P : se)
    });
  }, [ue, _]);
  return f ? /* @__PURE__ */ p(Sh, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ R("div", { className: "industrial-designer", children: [
    t,
    /* @__PURE__ */ R(Qw, { className: "industrial-header", children: [
      /* @__PURE__ */ R("div", { className: "industrial-title", children: [
        /* @__PURE__ */ p(G.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ p(G.Text, { type: "secondary", children: _.name || _.workflowId }),
        Q && /* @__PURE__ */ p(Me, { color: "orange", children: "未保存" }),
        qe && /* @__PURE__ */ p(Me, { color: qe.error ? "error" : "success", children: qe.status })
      ] }),
      /* @__PURE__ */ R("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ p(
          $,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": L,
            onClick: () => J((E) => !E),
            children: L ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ p(
          $,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => U((E) => !E),
            children: "属性"
          }
        ),
        /* @__PURE__ */ p($, { size: "small", "data-testid": "open-trigger", onClick: () => br(!0), children: "触发器" }),
        /* @__PURE__ */ p(
          $,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => pe(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ p(
          $,
          {
            size: "small",
            "data-testid": "designer-undo",
            disabled: A.current.length === 0,
            onClick: nr,
            children: "撤销"
          }
        ),
        /* @__PURE__ */ p(
          $,
          {
            size: "small",
            "data-testid": "designer-redo",
            disabled: Y.current.length === 0,
            onClick: Ir,
            children: "重做"
          }
        ),
        /* @__PURE__ */ p($, { size: "small", "data-testid": "designer-auto-layout", onClick: Vr, children: "自动布局" }),
        /* @__PURE__ */ p($, { size: "small", "data-testid": "designer-fit", onClick: () => {
          var E, P;
          return (P = (E = ae.current) == null ? void 0 : E.fitView) == null ? void 0 : P.call(E, { when: "always" });
        }, children: "适应画布" }),
        /* @__PURE__ */ p($, { size: "small", "data-testid": "designer-center", onClick: () => {
          var E, P;
          return (P = (E = ae.current) == null ? void 0 : E.fitCenter) == null ? void 0 : P.call(E);
        }, children: "居中" }),
        /* @__PURE__ */ p("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ R(H, { size: 4, children: [
          /* @__PURE__ */ p($, { size: "small", loading: w, onClick: () => void wr(), children: "保存" }),
          /* @__PURE__ */ p($, { size: "small", onClick: Rt, children: "校验" }),
          /* @__PURE__ */ p($, { size: "small", loading: w, onClick: () => void $e(), children: "发布" }),
          /* @__PURE__ */ p($, { size: "small", type: "primary", loading: w, onClick: () => void Ee(), children: "运行" })
        ] }),
        /* @__PURE__ */ R(
          "select",
          {
            className: "industrial-resource-select",
            "aria-label": "运行资源",
            value: d,
            onChange: (E) => h(E.target.value),
            children: [
              /* @__PURE__ */ p("option", { value: "", children: "运行资源" }),
              o.map((E) => /* @__PURE__ */ p("option", { value: E, children: E }, E))
            ]
          }
        ),
        /* @__PURE__ */ p(
          Fo,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: v,
            onChange: (E) => {
              g(Number(E) || 1), V(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ R(Yw, { className: `industrial-body${L ? "" : " industrial-body--collapsed"}`, children: [
      L ? /* @__PURE__ */ R("aside", { className: "industrial-palette", children: [
        i.length > 0 && /* @__PURE__ */ p(
          Ie,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: _.workflowId,
            placeholder: "选择工作流",
            onChange: (E) => void We(E),
            options: i.map((E) => ({ value: E.workflowId, label: E.name }))
          }
        ),
        /* @__PURE__ */ p(dv, { descriptors: n, loading: !1, onAdd: Wr })
      ] }) : /* @__PURE__ */ p("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ p($, { type: "text", size: "small", onClick: () => J(!0), children: "节点库" }) }),
      /* @__PURE__ */ R("section", { className: "industrial-center", children: [
        q.length > 0 && /* @__PURE__ */ p(
          Re,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: q.join("；")
          }
        ),
        /* @__PURE__ */ p(
          lv,
          {
            definition: _,
            catalog: oe,
            selectedId: N,
            onSelect: (E) => {
              m(E), E && U(!0);
            },
            onDropNode: Ge,
            onCreateEdge: Ar,
            onPositions: (E) => ue(yv(_, E)),
            onSelectIds: (E) => {
              E.length && m(E[0]);
            },
            onGraphReady: (E) => {
              ae.current = E;
            },
            onSelectEdge: (E) => M(E),
            onDeleteNodeById: Qr,
            onDuplicateNode: ze,
            onDeleteEdgeById: Qe
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ R(
      rv,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: re,
        mask: !1,
        getContainer: !1,
        onClose: () => U(!1),
        children: [
          qe && /* @__PURE__ */ p(
            Re,
            {
              style: { marginBottom: 12 },
              type: qe.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${qe.status}`,
              description: /* @__PURE__ */ R(H, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                qe.error && /* @__PURE__ */ p("span", { children: qe.error }),
                qe.executed.length > 0 && /* @__PURE__ */ R("span", { children: [
                  "已执行节点: ",
                  qe.executed.join(", ")
                ] }),
                qe.nodes.length > 0 && /* @__PURE__ */ p(
                  xe,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: qe.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (E, P) => P.error ?? (P.samples.length > 0 ? P.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ p(
            Tv,
            {
              node: ee,
              descriptor: ee ? Je(oe, ee.type) ?? null : null,
              nodes: _.nodes,
              edges: _.edges,
              catalog: oe,
              resources: l,
              issues: B,
              onApplyConfig: (E) => {
                N && ue(gv(_, N, E));
              },
              onDeleteNode: () => {
                ue(At(_, N ?? "")), m(null);
              },
              onDeleteEdge: (E) => ue(gu(_, E)),
              onCreateEdge: (E, P) => void Ar(E, P),
              onUpdateEdge: Zp,
              selectedEdgeId: S,
              sampleLines: ee ? xr[String(((uu = ee.config) == null ? void 0 : uu.resourceId) ?? ((cu = ee.config) == null ? void 0 : cu.targetResourceId) ?? "")] ?? [] : [],
              onSelectNode: (E) => {
                m(E), U(!0);
              }
            }
          )
        ]
      }
    ),
    lr && /* @__PURE__ */ p(
      Hv,
      {
        open: lr,
        onClose: () => br(!1),
        trigger: _.trigger,
        resourceIds: o,
        workflowId: _.workflowId,
        ctx: e,
        onSave: (E) => {
          W.current = E, ue({ ..._, trigger: E });
        }
      }
    ),
    O && /* @__PURE__ */ p(
      Qv,
      {
        open: O,
        onClose: () => pe(!1),
        ctx: e,
        workflowId: _.workflowId,
        resourceId: d,
        lookbackDays: v
      }
    )
  ] });
}
function Xw({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(!1), [s, o] = I(""), [u, l] = I(null), [c, d] = I(!1), h = j(async () => {
    try {
      n(Nh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
    } catch (f) {
      l(f instanceof Error ? f.message : String(f));
    }
  }, [e == null ? void 0 : e.query, r]);
  ye(() => {
    h();
  }, [h]);
  const v = j(async () => {
    var y;
    const f = s.trim();
    if (!f) {
      l("请输入工作流名称");
      return;
    }
    d(!0), l(null);
    try {
      const T = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await r.hubCommand("industrial/save-workflow", {
        ...et(T),
        name: f,
        layout: { lookbackDays: 1 }
      }), i(!1), o(""), await h(), (y = r.navigate) == null || y.call(r, `/industrial/designer?workflowId=${encodeURIComponent(T)}`);
    } catch (q) {
      l(q instanceof Error ? q.message : String(q));
    } finally {
      d(!1);
    }
  }, [r, h, s]), g = j((f) => {
    var y;
    (y = r.navigate) == null || y.call(r, `/industrial/designer?workflowId=${encodeURIComponent(f)}`);
  }, [r]);
  return /* @__PURE__ */ R(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ R(H, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ p(G.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ p($, { type: "primary", onClick: () => i(!0), children: "新建工作流" })
    ] }),
    u && /* @__PURE__ */ p(G.Text, { type: "danger", children: u }),
    /* @__PURE__ */ p(
      xe,
      {
        rowKey: "workflowId",
        dataSource: t,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (f) => /* @__PURE__ */ p($, { type: "link", style: { padding: 0 }, onClick: () => g(f), children: f })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ p(
      fr,
      {
        open: a,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: c,
        onCancel: () => i(!1),
        onOk: () => void v(),
        children: /* @__PURE__ */ R(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ p(G.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: s,
              onChange: (f) => o(f.target.value ?? "")
            }
          ),
          u && /* @__PURE__ */ p(G.Text, { type: "danger", children: u })
        ] })
      }
    )
  ] });
}
const Ro = {
  id: "",
  name: "",
  type: "filesystem.local",
  provider: "local",
  root: "",
  host: "",
  database: "",
  port: "",
  table: "",
  credentialId: "",
  enabled: !0,
  config: {}
}, Zw = [
  { value: "filesystem.local", label: "本地目录" },
  { value: "filesystem.unc", label: "Windows UNC 共享" },
  { value: "sqlserver", label: "SQL Server" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mysql", label: "MySQL" },
  { value: "sqlite", label: "SQLite" }
];
function e1(e) {
  const r = { ...e.config }, t = (n, a) => {
    a.trim() && (r[n] = a);
  };
  switch (e.type) {
    case "filesystem.local":
    case "filesystem.unc":
      t("root", e.root);
      break;
    case "sqlserver":
      t("server", e.host), t("host", e.host), t("database", e.database), t("table", e.table || "IndustrialRecords");
      break;
    case "postgresql":
    case "mysql":
      t("host", e.host), t("database", e.database), t("port", e.port || (e.type === "mysql" ? "3306" : "5432"));
      break;
    case "sqlite":
      t("database", e.database);
      break;
  }
  return r;
}
function r1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I([]), [u, l] = I(Ro), [c, d] = I(!1), [h, v] = I(null), [g, f] = I(!1), y = j(async () => {
    try {
      const [m, S, M] = await Promise.all([
        r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        r.hubQuery("industrial/credentials"),
        r.hubQuery("industrial/resource-tests")
      ]);
      n(Lv(m)), i(Mh(S)), o(Fv(M));
    } catch (m) {
      v(m instanceof Error ? m.message : String(m));
    }
  }, [e == null ? void 0 : e.query, r]);
  ye(() => {
    y();
  }, [y]);
  const q = j((m) => {
    const S = (...M) => {
      for (const B of M) {
        const z = m.config[B];
        if (z != null && String(z).trim()) return String(z);
      }
      return "";
    };
    l({
      id: m.id,
      name: m.name,
      type: m.type,
      provider: m.provider,
      root: S("root", "path"),
      host: S("server", "host", "dataSource", "data_source"),
      database: S("database", "initialCatalog", "initial_catalog", "path"),
      port: S("port"),
      table: S("table"),
      credentialId: m.credentialId ?? "",
      enabled: m.enabled,
      config: m.config
    }), d(!0);
  }, []), T = j(async () => {
    const m = u.id.trim();
    if (!m) {
      v("请输入资源 ID");
      return;
    }
    f(!0), v(null);
    try {
      await r.hubCommand("industrial/save-resource", {
        id: m,
        name: u.name || m,
        type: u.type,
        provider: u.provider || "manual",
        config: e1(u),
        credentialId: u.credentialId || null,
        enabled: u.enabled
      }), d(!1), l(Ro), await y();
    } catch (S) {
      v(S instanceof Error ? S.message : String(S));
    } finally {
      f(!1);
    }
  }, [r, u, y]), _ = j(async (m) => {
    v(null);
    try {
      await r.hubCommand("industrial/test-resource", { id: m });
    } catch (S) {
      v(S instanceof Error ? S.message : String(S));
    }
    await y();
  }, [r, y]), x = j(async (m) => {
    v(null);
    try {
      await r.hubCommand("industrial/delete-resource", { id: m }), await y();
    } catch (S) {
      v(S instanceof Error ? S.message : String(S));
    }
  }, [r, y]), N = new Map(s.map((m) => [m.resourceId, m]));
  return /* @__PURE__ */ R(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ R(H, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ p(G.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ R(H, { children: [
        /* @__PURE__ */ p($, { onClick: () => {
          var m;
          return (m = r.navigate) == null ? void 0 : m.call(r, "/industrial/designer");
        }, children: "返回设计器" }),
        /* @__PURE__ */ p($, { type: "primary", onClick: () => {
          l(Ro), d(!0);
        }, children: "新建资源" })
      ] })
    ] }),
    h && /* @__PURE__ */ p(Re, { type: "error", showIcon: !0, message: h }),
    /* @__PURE__ */ p(
      xe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "ID", dataIndex: "id" },
          { title: "名称", dataIndex: "name" },
          { title: "类型", dataIndex: "type" },
          { title: "凭证", dataIndex: "credentialName", render: (m) => m ?? "—" },
          {
            title: "连接测试",
            key: "test",
            render: (m, S) => {
              const M = N.get(S.id);
              return M ? /* @__PURE__ */ p(G.Text, { type: M.ok ? "success" : "danger", children: M.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (m, S) => /* @__PURE__ */ R(H, { size: 4, children: [
              /* @__PURE__ */ p($, { size: "small", onClick: () => void _(S.id), children: "测试连接" }),
              /* @__PURE__ */ p($, { size: "small", onClick: () => q(S), children: "编辑" }),
              /* @__PURE__ */ p($, { size: "small", danger: !0, onClick: () => void x(S.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ p(
      fr,
      {
        open: c,
        title: u.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: g,
        onCancel: () => d(!1),
        onOk: () => void T(),
        children: /* @__PURE__ */ R(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: u.id,
              onChange: (m) => l({ ...u, id: m.target.value })
            }
          ),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: u.name,
              onChange: (m) => l({ ...u, name: m.target.value })
            }
          ),
          /* @__PURE__ */ p(G.Text, { children: "资源类型" }),
          /* @__PURE__ */ p(H, { wrap: !0, children: Zw.map((m) => /* @__PURE__ */ p(
            $,
            {
              size: "small",
              type: u.type === m.value ? "primary" : "default",
              onClick: () => l({ ...u, type: m.value }),
              children: m.label
            },
            m.value
          )) }),
          (u.type === "filesystem.local" || u.type === "filesystem.unc") && /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "目录路径",
              placeholder: u.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: u.root,
              onChange: (m) => l({ ...u, root: m.target.value })
            }
          ),
          (u.type === "sqlserver" || u.type === "postgresql" || u.type === "mysql") && /* @__PURE__ */ R(H, { children: [
            /* @__PURE__ */ p(
              Z,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: u.host,
                onChange: (m) => l({ ...u, host: m.target.value })
              }
            ),
            /* @__PURE__ */ p(
              Z,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: u.database,
                onChange: (m) => l({ ...u, database: m.target.value })
              }
            ),
            /* @__PURE__ */ p(
              Z,
              {
                "aria-label": "端口",
                placeholder: "端口",
                value: u.port,
                onChange: (m) => l({ ...u, port: m.target.value })
              }
            )
          ] }),
          u.type === "sqlite" && /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: u.database,
              onChange: (m) => l({ ...u, database: m.target.value })
            }
          ),
          /* @__PURE__ */ p(
            Ie,
            {
              "aria-label": "凭证",
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: u.credentialId || void 0,
              options: a.map((m) => ({ value: m.id, label: m.name })),
              onChange: (m) => l({ ...u, credentialId: m ?? "" })
            }
          ),
          /* @__PURE__ */ R(H, { children: [
            /* @__PURE__ */ p(
              ut,
              {
                "aria-label": "启用资源",
                checked: u.enabled,
                onChange: (m) => l({ ...u, enabled: m })
              }
            ),
            /* @__PURE__ */ p(G.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
const xo = ["username-password", "domain-account", "api-token"];
function t1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(xo), [s, o] = I(!1), [u, l] = I(""), [c, d] = I(""), [h, v] = I(xo[0]), [g, f] = I(""), [y, q] = I(""), [T, _] = I(""), [x, N] = I(""), [m, S] = I(""), [M, B] = I(!1), [z, w] = I(null), [D, L] = I(!1), J = j(async () => {
    try {
      n(Mh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
      const A = await r.hubQuery("industrial/credential-types"), Y = Array.isArray(A) ? A : Array.isArray(A == null ? void 0 : A.data) ? A.data : [];
      Y.length && i(Y.map((ae) => ae.kind));
    } catch (A) {
      w(A instanceof Error ? A.message : String(A));
    }
  }, [e == null ? void 0 : e.query, r]);
  ye(() => {
    J();
  }, [J]);
  const re = j(() => {
    l(""), d(""), v(xo[0]), f(""), q(""), _(""), N(""), S(""), B(!1), w(null);
  }, []), U = j(() => {
    re(), o(!0);
  }, [re]), Q = j((A) => {
    re(), l(A.id), d(A.name), a.includes(A.kind) && v(A.kind), B(!0), o(!0);
  }, [a, re]), V = j(() => m.trim() ? m.trim() : h === "api-token" ? x.trim() ? JSON.stringify({ token: x.trim() }) : null : !g.trim() || !y.trim() ? null : JSON.stringify(h === "domain-account" ? { username: g.trim(), password: y.trim(), domain: T.trim() } : { username: g.trim(), password: y.trim() }), [T, h, y, m, x, g]), C = j(async () => {
    if (!u.trim()) {
      w("请输入凭证 ID");
      return;
    }
    const A = V();
    if (!A) {
      w("请填写凭证字段，或在高级内容中提供凭证");
      return;
    }
    L(!0), w(null);
    try {
      M ? await r.hubCommand("industrial/rotate-credential", { id: u, secret: A }) : await r.hubCommand("industrial/save-credential", { id: u, name: c || u, kind: h, secret: A }), o(!1), await J();
    } catch (Y) {
      w(Y instanceof Error ? Y.message : String(Y));
    } finally {
      L(!1);
    }
  }, [V, r, u, h, J, c, M, m]), F = j(async (A) => {
    w(null);
    try {
      await r.hubCommand("industrial/delete-credential", { id: A.id }), await J();
    } catch (Y) {
      w(Y instanceof Error ? Y.message : String(Y));
    }
  }, [r, J]);
  return /* @__PURE__ */ R(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ R(H, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ p(G.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ p($, { type: "primary", onClick: U, children: "新建凭证" })
    ] }),
    z && /* @__PURE__ */ p(Re, { type: "error", showIcon: !0, message: z }),
    /* @__PURE__ */ p(
      xe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "ID", dataIndex: "id" },
          { title: "名称", dataIndex: "name" },
          { title: "类型", dataIndex: "kind" },
          { title: "版本", dataIndex: "version" },
          {
            title: "操作",
            key: "actions",
            render: (A, Y) => /* @__PURE__ */ R(H, { children: [
              /* @__PURE__ */ p($, { size: "small", onClick: () => Q(Y), children: "轮换" }),
              /* @__PURE__ */ p($, { size: "small", danger: !0, onClick: () => void F(Y), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ p(
      fr,
      {
        open: s,
        title: M ? "轮换凭证" : "新建凭证",
        okText: M ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: D,
        onCancel: () => o(!1),
        onOk: () => void C(),
        children: /* @__PURE__ */ R(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: u,
              disabled: M,
              onChange: (A) => l(A.target.value)
            }
          ),
          !M && /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: c,
              onChange: (A) => d(A.target.value)
            }
          ),
          /* @__PURE__ */ p(
            Ie,
            {
              "aria-label": "凭证类型",
              style: { width: "100%" },
              value: h,
              onChange: (A) => v(A),
              options: a.map((A) => ({ value: A, label: A }))
            }
          ),
          h === "api-token" ? /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "令牌",
              placeholder: "令牌",
              value: x,
              onChange: (A) => N(A.target.value)
            }
          ) : /* @__PURE__ */ R(sr, { children: [
            /* @__PURE__ */ p(
              Z,
              {
                "aria-label": "用户名",
                placeholder: "用户名",
                value: g,
                onChange: (A) => f(A.target.value)
              }
            ),
            /* @__PURE__ */ p(
              Z,
              {
                "aria-label": "密码",
                placeholder: "密码",
                value: y,
                onChange: (A) => q(A.target.value)
              }
            ),
            h === "domain-account" && /* @__PURE__ */ p(
              Z,
              {
                "aria-label": "域",
                placeholder: "域（可选）",
                value: T,
                onChange: (A) => _(A.target.value)
              }
            )
          ] }),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "凭证内容",
              placeholder: "高级：直接提供凭证内容（优先）",
              value: m,
              onChange: (A) => S(A.target.value)
            }
          ),
          z && /* @__PURE__ */ p(G.Text, { type: "danger", children: z })
        ] })
      }
    )
  ] });
}
const Eh = (e) => e === "Succeeded" || e === "Completed" ? "success" : e === "Failed" || e === "PartialFailure" ? "error" : e === "Running" || e === "Interrupted" ? "processing" : "default";
function n1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I(null), [u, l] = I(null), [c, d] = I(!1), h = j(async () => {
    try {
      const g = zv(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/execution-center"));
      n(g.runs), i(g.deliveries);
    } catch (g) {
      o(g instanceof Error ? g.message : String(g));
    }
  }, [e == null ? void 0 : e.query, r]);
  ye(() => {
    h();
  }, [h]);
  const v = j(async (g) => {
    d(!0), o(null), l(null);
    try {
      await r.hubCommand("industrial/retry-failed", {
        deliveryId: g.id,
        targetId: g.targetId
      }), l(`已提交目标 ${g.targetId} 的重试`), await h();
    } catch (f) {
      o(f instanceof Error ? f.message : String(f));
    } finally {
      d(!1);
    }
  }, [r, h]);
  return /* @__PURE__ */ R(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ p(G.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    s && /* @__PURE__ */ p(Re, { type: "error", showIcon: !0, message: s }),
    u && /* @__PURE__ */ p(Re, { type: "success", showIcon: !0, message: u }),
    /* @__PURE__ */ p(
      xe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ p(Me, { color: Eh(g), children: g }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (g) => g ?? "—" },
          { title: "错误", dataIndex: "error", render: (g) => g ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ p(
      xe,
      {
        rowKey: "id",
        dataSource: a,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ p(Me, { color: Eh(g), children: g }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (g, f) => f.diagnosis ? /* @__PURE__ */ R(H, { direction: "vertical", size: 0, children: [
              /* @__PURE__ */ p(G.Text, { children: f.diagnosis.message }),
              /* @__PURE__ */ p(G.Text, { type: "secondary", children: f.diagnosis.action })
            ] }) : f.error ?? "—"
          },
          {
            title: "操作",
            key: "actions",
            render: (g, f) => /* @__PURE__ */ p(
              $,
              {
                size: "small",
                disabled: c || f.status !== "Failed" && f.status !== "Interrupted",
                onClick: () => void v(f),
                children: "重试此目标"
              }
            )
          }
        ]
      }
    )
  ] });
}
function a1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(null), s = j(async () => {
    try {
      n(Gv(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (u) {
      i(u instanceof Error ? u.message : String(u));
    }
  }, [e == null ? void 0 : e.query, r]);
  ye(() => {
    s();
  }, [s]);
  const o = (u) => /* @__PURE__ */ p(
    xe,
    {
      rowKey: "id",
      dataSource: u.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (l) => /* @__PURE__ */ p(Me, { children: l }) },
        { title: "记录", dataIndex: "records" },
        {
          title: "接受/拒绝/警告",
          key: "counts",
          render: (l, c) => `${c.accepted} / ${c.rejected} / ${c.warnings}`
        },
        {
          title: "原因与下一步",
          key: "diagnosis",
          render: (l, c) => c.diagnosis ? /* @__PURE__ */ R(H, { direction: "vertical", size: 0, children: [
            /* @__PURE__ */ p(G.Text, { children: c.diagnosis.message }),
            /* @__PURE__ */ p(G.Text, { type: "secondary", children: c.diagnosis.action }),
            c.error && /* @__PURE__ */ p(G.Text, { type: "secondary", children: c.error })
          ] }) : c.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ R(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ p(G.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    a && /* @__PURE__ */ p(Re, { type: "error", showIcon: !0, message: a }),
    /* @__PURE__ */ p(
      xe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        expandable: { expandedRowRender: o },
        columns: [
          { title: "文件", dataIndex: "path" },
          { title: "源日期", dataIndex: "sourceDate" },
          { title: "状态", dataIndex: "status" },
          { title: "大小", dataIndex: "length", render: (u) => `${u} B` },
          {
            title: "目标数",
            key: "targets",
            render: (u, l) => l.deliveries.length
          }
        ]
      }
    )
  ] });
}
const ko = (e) => {
  const r = Se(e);
  return Array.isArray(r) ? r : [];
}, i1 = () => `machine-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 12) : Date.now().toString(36)}`;
function s1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I([]), [u, l] = I(null), [c, d] = I(""), [h, v] = I(""), [g, f] = I(), [y, q] = I(!0), [T, _] = I(null), x = j(async () => {
    try {
      n(ko(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/machines"))), i(ko(await r.hubQuery("industrial/resources"))), o(ko(await r.hubQuery("industrial/credentials")));
    } catch (w) {
      _(w instanceof Error ? w.message : String(w));
    }
  }, [e == null ? void 0 : e.query, r]);
  ye(() => {
    x();
  }, [x]);
  const N = j(() => {
    l({ id: i1(), name: "", endpoint: "", credentialId: null, enabled: !0 }), d(""), v(""), f(void 0), q(!0), _(null);
  }, []), m = j((w) => {
    l(w), d(w.name), v(w.endpoint), f(w.credentialId ?? void 0), q(w.enabled), _(null);
  }, []), S = j(async () => {
    if (u) {
      if (!c.trim()) {
        _("请输入设备名称");
        return;
      }
      _(null);
      try {
        await r.hubCommand("industrial/save-machine", {
          id: u.id,
          name: c.trim(),
          endpoint: h.trim(),
          credentialId: g ?? null,
          enabled: y
        }), l(null), await x();
      } catch (w) {
        _(w instanceof Error ? w.message : String(w));
      }
    }
  }, [r, g, u, h, y, x, c]), M = j(async (w) => {
    _(null);
    try {
      await r.hubCommand("industrial/delete-machine", { id: w.id }), await x();
    } catch (D) {
      _(D instanceof Error ? D.message : String(D));
    }
  }, [r, x]), B = j(async (w, D) => {
    _(null);
    try {
      await r.hubCommand("industrial/save-machine", {
        id: w.id,
        name: w.name,
        endpoint: w.endpoint,
        credentialId: w.credentialId,
        enabled: D
      }), await x();
    } catch (L) {
      _(L instanceof Error ? L.message : String(L));
    }
  }, [r, x]), z = j(async (w, D) => {
    _(null);
    try {
      await r.hubCommand("industrial/save-resource", {
        id: w.id,
        machineId: D || null
      }), await x();
    } catch (L) {
      _(L instanceof Error ? L.message : String(L));
    }
  }, [r, x]);
  return /* @__PURE__ */ R(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ R(H, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ p(G.Title, { level: 4, style: { margin: 0 }, children: "设备与资源" }),
      /* @__PURE__ */ p($, { type: "primary", onClick: N, children: "新增设备" })
    ] }),
    /* @__PURE__ */ p(G.Text, { type: "secondary", children: "设备地址是台账属性；实际连接由数据源资源配置决定。绑定凭证供需要认证的数据源引用。" }),
    T && /* @__PURE__ */ p(Re, { type: "error", showIcon: !0, message: T }),
    /* @__PURE__ */ p(
      xe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "设备", dataIndex: "name" },
          { title: "台账地址", dataIndex: "endpoint" },
          {
            title: "凭证",
            dataIndex: "credentialId",
            render: (w) => {
              var D;
              return ((D = s.find((L) => L.id === w)) == null ? void 0 : D.name) ?? "—";
            }
          },
          {
            title: "启用",
            dataIndex: "enabled",
            render: (w, D) => /* @__PURE__ */ p(
              ut,
              {
                "aria-label": `设备 ${D.name} 启用`,
                checked: w,
                onChange: (L) => void B(D, L)
              }
            )
          },
          { title: "操作", key: "actions", render: (w, D) => /* @__PURE__ */ R(H, { children: [
            /* @__PURE__ */ p($, { size: "small", onClick: () => m(D), children: "编辑" }),
            /* @__PURE__ */ p($, { size: "small", danger: !0, onClick: () => void M(D), children: "删除" })
          ] }) }
        ]
      }
    ),
    /* @__PURE__ */ p(G.Title, { level: 5, style: { margin: 0 }, children: "资源归属" }),
    /* @__PURE__ */ p(
      xe,
      {
        rowKey: "id",
        dataSource: a,
        pagination: !1,
        columns: [
          { title: "资源", dataIndex: "name" },
          { title: "类型", dataIndex: "type" },
          {
            title: "所属设备",
            key: "machineId",
            render: (w, D) => /* @__PURE__ */ p(
              Ie,
              {
                "aria-label": `资源 ${D.name} 的设备`,
                style: { width: 220 },
                value: D.machineId ?? void 0,
                allowClear: !0,
                onChange: (L) => void z(D, L ?? ""),
                options: t.map((L) => ({ value: L.id, label: L.name }))
              }
            )
          }
        ]
      }
    ),
    /* @__PURE__ */ p(
      fr,
      {
        open: u !== null,
        title: u != null && u.name ? "编辑设备" : "新增设备",
        okText: "保存设备",
        cancelText: "取消",
        onCancel: () => l(null),
        onOk: () => void S(),
        children: /* @__PURE__ */ R(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "设备名称",
              placeholder: "设备名称",
              value: c,
              onChange: (w) => d(w.target.value)
            }
          ),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "设备地址",
              placeholder: "台账地址 / IP",
              value: h,
              onChange: (w) => v(w.target.value)
            }
          ),
          /* @__PURE__ */ p(
            Ie,
            {
              "aria-label": "设备凭证",
              style: { width: "100%" },
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: g,
              onChange: (w) => f(w),
              options: s.map((w) => ({
                value: w.id,
                label: `${w.name} (${w.kind})`
              }))
            }
          )
        ] })
      }
    )
  ] });
}
const o1 = ["filesystem.local", "filesystem.unc"], u1 = ["sink.sqlite", "sink.sqlserver", "sink.postgresql", "sink.mysql"];
function c1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I(""), [u, l] = I(""), [c, d] = I("csv"), [h, v] = I([]), [g, f] = I(null), [y, q] = I(!1);
  ye(() => {
    (async () => {
      try {
        n(Oh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"))), i(Ah(await r.hubQuery((e == null ? void 0 : e.nodesQuery) ?? "industrial/nodes")));
      } catch (m) {
        f(m instanceof Error ? m.message : String(m));
      }
    })();
  }, [e == null ? void 0 : e.nodesQuery, e == null ? void 0 : e.query, r]);
  const T = t.filter((m) => o1.includes(m.type)), _ = t.filter((m) => u1.some((S) => m.type === S.split(".")[1])), x = ot(
    () => new Map(a.map((m) => [m.nodeTypeId, m])),
    [a]
  ), N = j(async () => {
    var m;
    if (!s.trim()) {
      f("请输入任务名称");
      return;
    }
    if (!u) {
      f("请选择数据来源");
      return;
    }
    if (h.length === 0) {
      f("请至少选择一个目标");
      return;
    }
    q(!0), f(null);
    try {
      const M = `quick-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`, z = `industrial.${t.find((V) => V.id === u).type}`, w = `industrial.reader.${c}`, D = (V, C, F, A, Y) => ({ id: V, type: C, config: Y, x: F, y: A }), L = (V, C) => {
        var Y, ae;
        const F = x.get(re[V]), A = x.get(re[C]);
        return {
          source: V,
          sourcePort: ((Y = F.outputs[0]) == null ? void 0 : Y.name) ?? "output",
          target: C,
          targetPort: ((ae = A.inputs[0]) == null ? void 0 : ae.name) ?? "input"
        };
      }, J = [
        D("src", z, 80, 120, { resourceId: u }),
        D("read", w, 280, 120, {}),
        ...h.map((V, C) => D(
          `sink${C + 1}`,
          `industrial.sink.${t.find((F) => F.id === V).type}`,
          500,
          80 + C * 120,
          { targetResourceId: V }
        ))
      ], re = Object.fromEntries(J.map((V) => [V.id, V.type])), U = [L("src", "read"), ...h.map((V, C) => L("read", `sink${C + 1}`))], Q = {
        ...et(M),
        name: s.trim(),
        nodes: J,
        edges: U,
        resourceReferences: [u, ...h],
        layout: { lookbackDays: 1 }
      };
      await r.hubCommand("industrial/save-workflow", Q), await r.hubCommand("industrial/publish-workflow", { workflowId: M }), (m = r.navigate) == null || m.call(r, `/industrial/designer?workflowId=${encodeURIComponent(M)}`);
    } catch (S) {
      f(S instanceof Error ? S.message : String(S));
    } finally {
      q(!1);
    }
  }, [r, c, x, s, t, u, h]);
  return /* @__PURE__ */ R(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ p(G.Title, { level: 4, style: { margin: 0 }, children: "快速创建采集任务" }),
    /* @__PURE__ */ p(G.Text, { type: "secondary", children: "生成标准工作流，可在设计器中继续修改。" }),
    g && /* @__PURE__ */ p(Re, { type: "error", showIcon: !0, message: g }),
    /* @__PURE__ */ p(
      Z,
      {
        "aria-label": "任务名称",
        placeholder: "任务名称",
        value: s,
        onChange: (m) => o(m.target.value)
      }
    ),
    /* @__PURE__ */ R(H, { wrap: !0, children: [
      /* @__PURE__ */ p(
        Ie,
        {
          "aria-label": "数据来源",
          style: { width: 260 },
          placeholder: "数据来源",
          value: u || void 0,
          onChange: (m) => l(m),
          options: T.map((m) => ({ value: m.id, label: `${m.name} (${m.type})` }))
        }
      ),
      /* @__PURE__ */ p(
        Ie,
        {
          "aria-label": "数据格式",
          style: { width: 160 },
          value: c,
          onChange: (m) => d(m),
          options: ["csv", "excel", "json"].map((m) => ({ value: m, label: m.toUpperCase() }))
        }
      )
    ] }),
    /* @__PURE__ */ p(
      Ie,
      {
        "aria-label": "目标资源",
        mode: "multiple",
        style: { width: 420 },
        placeholder: "目标资源",
        value: h,
        onChange: (m) => v(m),
        options: _.map((m) => ({ value: m.id, label: `${m.name} (${m.type})` }))
      }
    ),
    /* @__PURE__ */ p($, { type: "primary", loading: y, onClick: () => void N(), children: "创建并发布" })
  ] });
}
const l1 = (e) => {
  const r = Se(e);
  return Array.isArray(r) ? r : [];
}, d1 = () => ({
  id: `profile-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`,
  name: "",
  version: 0,
  fileNamePattern: "*.csv",
  format: "csv",
  delimiter: ",",
  hasHeader: !0,
  encoding: "utf-8",
  enabled: !0,
  mapping: {},
  requiredFields: []
});
function f1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(null), [s, o] = I(""), [u, l] = I(""), [c, d] = I(null), h = j(async () => {
    try {
      n(l1(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-profiles")));
    } catch (f) {
      d(f instanceof Error ? f.message : String(f));
    }
  }, [e == null ? void 0 : e.query, r]);
  ye(() => {
    h();
  }, [h]);
  const v = j((f) => {
    const y = f ? { ...f } : d1();
    i(y), o(Object.entries(y.mapping ?? {}).map(([q, T]) => `${q}=${T}`).join(",")), l((y.requiredFields ?? []).join(",")), d(null);
  }, []), g = j(async () => {
    if (a) {
      if (!a.name.trim()) {
        d("请输入配置名称");
        return;
      }
      d(null);
      try {
        const f = {};
        for (const y of s.split(",")) {
          const [q, T] = y.split("=").map((_) => _ == null ? void 0 : _.trim());
          q && T && (f[q] = T);
        }
        await r.hubCommand("industrial/save-file-profile", {
          id: a.id,
          name: a.name.trim(),
          fileNamePattern: a.fileNamePattern,
          format: a.format,
          delimiter: a.delimiter,
          hasHeader: a.hasHeader,
          encoding: a.encoding,
          enabled: a.enabled,
          mapping: f,
          required: u.split(",").map((y) => y.trim()).filter(Boolean)
        }), i(null), await h();
      } catch (f) {
        d(f instanceof Error ? f.message : String(f));
      }
    }
  }, [r, a, h, s, u]);
  return /* @__PURE__ */ R(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ R(H, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ p(G.Title, { level: 4, style: { margin: 0 }, children: "文件格式配置" }),
      /* @__PURE__ */ p($, { type: "primary", onClick: () => v(), children: "新建文件配置" })
    ] }),
    c && /* @__PURE__ */ p(Re, { type: "error", showIcon: !0, message: c }),
    /* @__PURE__ */ p(
      xe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "名称", dataIndex: "name" },
          { title: "文件匹配", dataIndex: "fileNamePattern" },
          { title: "格式", dataIndex: "format" },
          { title: "版本", dataIndex: "version" },
          { title: "操作", key: "actions", render: (f, y) => /* @__PURE__ */ p($, { size: "small", onClick: () => v(y), children: "编辑" }) }
        ]
      }
    ),
    /* @__PURE__ */ p(
      fr,
      {
        open: a !== null,
        title: a != null && a.version ? "编辑文件配置" : "新建文件配置",
        okText: "保存配置",
        cancelText: "取消",
        onCancel: () => i(null),
        onOk: () => void g(),
        children: a && /* @__PURE__ */ R(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "配置名称",
              placeholder: "配置名称",
              value: a.name,
              onChange: (f) => i({ ...a, name: f.target.value })
            }
          ),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "文件匹配规则",
              placeholder: "文件匹配，例如 *.csv",
              value: a.fileNamePattern,
              onChange: (f) => i({ ...a, fileNamePattern: f.target.value })
            }
          ),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "文件格式",
              placeholder: "csv / excel / json",
              value: a.format,
              onChange: (f) => i({ ...a, format: f.target.value })
            }
          ),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "分隔符",
              placeholder: "分隔符",
              value: a.delimiter,
              onChange: (f) => i({ ...a, delimiter: f.target.value })
            }
          ),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "编码",
              placeholder: "编码",
              value: a.encoding,
              onChange: (f) => i({ ...a, encoding: f.target.value })
            }
          ),
          /* @__PURE__ */ p(
            ut,
            {
              "aria-label": "首行为表头",
              checked: a.hasHeader,
              onChange: (f) => i({ ...a, hasHeader: f })
            }
          ),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "字段映射",
              placeholder: "目标字段=来源字段，逗号分隔",
              value: s,
              onChange: (f) => o(f.target.value)
            }
          ),
          /* @__PURE__ */ p(
            Z,
            {
              "aria-label": "必填字段",
              placeholder: "必填字段，逗号分隔",
              value: u,
              onChange: (f) => l(f.target.value)
            }
          )
        ] })
      }
    )
  ] });
}
const Or = (e) => {
  const r = Se(e);
  return Array.isArray(r) ? r : [];
};
function h1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I([]), [u, l] = I([]), [c, d] = I([]), [h, v] = I([]), [g, f] = I([]), [y, q] = I(), [T, _] = I(1), [x, N] = I("resource-{machine}"), [m, S] = I([]), [M, B] = I(""), [z, w] = I(""), [D, L] = I(""), [J, re] = I(""), [U, Q] = I(null), [V, C] = I(null), [F, A] = I(!1), Y = j(async () => {
    try {
      n(Or(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/bindings"))), i(Or(await r.hubQuery("industrial/machines"))), o(Or(await r.hubQuery("industrial/file-profiles"))), l(Or(await r.hubQuery("industrial/workflows"))), d(Or(await r.hubQuery("industrial/resources")));
      try {
        Q(await r.hubQuery("industrial/binding-status"));
      } catch {
      }
    } catch (O) {
      C(O instanceof Error ? O.message : String(O));
    }
  }, [e == null ? void 0 : e.query, r]);
  ye(() => {
    Y();
  }, [Y]);
  const ae = ot(() => {
    const O = [];
    for (const _e of h) for (const W of g) {
      const oe = `binding:${y ?? ""}:v${T}:${_e}:${W}`;
      t.some((ee) => ee.id === oe) && O.push(oe);
    }
    const pe = m.filter((_e) => !c.some((W) => W.id === _e)), ve = h.map((_e) => x.replace("{machine}", _e)).filter((_e) => !c.some((W) => W.id === _e));
    return {
      total: h.length * g.length,
      create: h.length * g.length - O.length,
      conflicts: O,
      missing: [...pe, ...ve]
    };
  }, [t, h, g, c, x, m, T, y]), Ve = j(async () => {
    if (!y || h.length === 0 || g.length === 0 || m.length === 0) {
      C("请选择工作流、机台、文件配置和目标资源");
      return;
    }
    if (ae.missing.length > 0) {
      C("存在缺失资源，请先创建后再导入");
      return;
    }
    A(!0), C(null);
    try {
      await r.hubCommand("industrial/create-bindings", {
        machineIds: h,
        fileProfileIds: g,
        workflowId: y,
        workflowVersion: T,
        sourceResourcePattern: x,
        targetResourceIds: m
      }), await Y();
    } catch (O) {
      C(O instanceof Error ? O.message : String(O));
    } finally {
      A(!1);
    }
  }, [r, Y, ae.missing.length, g, x, m, T, y, h]), qe = j(async (O, pe) => {
    A(!0), C(null);
    try {
      await r.hubCommand("industrial/set-binding-enabled", { id: O.id, enabled: pe }), await Y();
    } catch (ve) {
      C(ve instanceof Error ? ve.message : String(ve));
    } finally {
      A(!1);
    }
  }, [r, Y]), mr = j(async (O, pe) => {
    A(!0), C(null);
    try {
      await r.hubCommand("industrial/run-binding", {
        bindingId: O.id,
        lookbackDays: pe
      }), Q(await r.hubQuery("industrial/binding-status"));
    } catch (ve) {
      C(ve instanceof Error ? ve.message : String(ve));
    } finally {
      A(!1);
    }
  }, [r]), xr = j(async () => {
    A(!0), C(null);
    try {
      await r.hubCommand("industrial/import-machines", { csv: M }), B(""), await Y();
    } catch (O) {
      C(O instanceof Error ? O.message : String(O));
    } finally {
      A(!1);
    }
  }, [r, M, Y]), kr = j(async () => {
    C(null);
    try {
      const O = await r.hubQuery("industrial/export-config"), pe = (O == null ? void 0 : O.data) ?? O;
      w(JSON.stringify(pe, null, 2));
    } catch (O) {
      C(O instanceof Error ? O.message : String(O));
    }
  }, [r]), lr = (O) => {
    const pe = {};
    for (const ve of O.split(/[\n,]/)) {
      const [_e, W] = ve.split("=").map((oe) => oe == null ? void 0 : oe.trim());
      _e && W && (pe[_e] = W);
    }
    return pe;
  }, br = j(async () => {
    A(!0), C(null);
    try {
      const O = JSON.parse(z);
      O.resourceMap = lr(D), O.credentialMap = lr(J), await r.hubCommand("industrial/import-config", O), await Y();
    } catch (O) {
      C(O instanceof Error ? O.message : String(O));
    } finally {
      A(!1);
    }
  }, [r, J, Y, z, D]);
  return /* @__PURE__ */ R(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ p(G.Title, { level: 4, style: { margin: 0 }, children: "采集任务绑定" }),
    V && /* @__PURE__ */ p(Re, { type: "error", showIcon: !0, message: V }),
    /* @__PURE__ */ R(H, { wrap: !0, children: [
      /* @__PURE__ */ p(
        Ie,
        {
          "aria-label": "工作流模板",
          mode: "single",
          style: { width: 220 },
          value: y,
          onChange: (O) => q(O),
          options: u.map((O) => ({ value: O.workflowId, label: O.name }))
        }
      ),
      /* @__PURE__ */ p(
        Fo,
        {
          "aria-label": "模板版本",
          min: 1,
          value: T,
          onChange: (O) => _(Number(O) || 1)
        }
      ),
      /* @__PURE__ */ p(
        Ie,
        {
          "aria-label": "机台范围",
          mode: "multiple",
          style: { width: 300 },
          value: h,
          onChange: (O) => v(O),
          options: a.map((O) => ({ value: O.id, label: O.name }))
        }
      ),
      /* @__PURE__ */ p(
        Ie,
        {
          "aria-label": "文件配置范围",
          mode: "multiple",
          style: { width: 300 },
          value: g,
          onChange: (O) => f(O),
          options: s.map((O) => ({ value: O.id, label: O.name }))
        }
      ),
      /* @__PURE__ */ p(
        Ie,
        {
          "aria-label": "目标资源",
          mode: "multiple",
          style: { width: 300 },
          value: m,
          onChange: (O) => S(O),
          options: c.map((O) => ({ value: O.id, label: `${O.name} (${O.type})` }))
        }
      ),
      /* @__PURE__ */ p(
        Z,
        {
          "aria-label": "来源资源模板",
          placeholder: "来源资源模板，{machine} 占位符",
          value: x,
          onChange: (O) => N(O.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ p(
      Re,
      {
        type: "info",
        showIcon: !0,
        message: `预计生成 ${ae.total} 条绑定；可创建 ${ae.create} 条`,
        description: `冲突 ${ae.conflicts.length} 条；缺失资源 ${ae.missing.length} 条`
      }
    ),
    /* @__PURE__ */ p($, { type: "primary", loading: F, onClick: () => void Ve(), children: "生成绑定" }),
    /* @__PURE__ */ p(G.Title, { level: 5, style: { margin: 0 }, children: "批量导入机台（CSV）" }),
    /* @__PURE__ */ p(
      Z.TextArea,
      {
        "aria-label": "机台 CSV",
        rows: 4,
        value: M,
        placeholder: "id,name,endpoint,credentialId",
        onChange: (O) => B(O.target.value)
      }
    ),
    /* @__PURE__ */ p($, { loading: F, onClick: () => void xr(), children: "导入机台" }),
    /* @__PURE__ */ p(G.Title, { level: 5, style: { margin: 0 }, children: "配置包（不含凭证）" }),
    /* @__PURE__ */ R(H, { wrap: !0, children: [
      /* @__PURE__ */ p($, { onClick: () => void kr(), children: "导出配置" }),
      /* @__PURE__ */ p($, { loading: F, onClick: () => void br(), children: "导入配置" })
    ] }),
    /* @__PURE__ */ p(
      Z,
      {
        "aria-label": "资源 ID 映射",
        placeholder: "旧资源ID=新资源ID，逗号或换行分隔",
        value: D,
        onChange: (O) => L(O.target.value)
      }
    ),
    /* @__PURE__ */ p(
      Z,
      {
        "aria-label": "凭证别名映射",
        placeholder: "旧凭证ID=新凭证ID",
        value: J,
        onChange: (O) => re(O.target.value)
      }
    ),
    /* @__PURE__ */ p(
      Z.TextArea,
      {
        "aria-label": "配置包",
        rows: 5,
        value: z,
        placeholder: "导出后可跨环境导入；资源 ID 需自行映射",
        onChange: (O) => w(O.target.value)
      }
    ),
    /* @__PURE__ */ p(
      xe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "绑定", dataIndex: "name" },
          { title: "机台", dataIndex: "machineId" },
          { title: "文件配置", dataIndex: "fileProfileId" },
          { title: "模板版本", dataIndex: "workflowVersion" },
          { title: "状态", dataIndex: "enabled", render: (O, pe) => /* @__PURE__ */ R(H, { children: [
            /* @__PURE__ */ p($, { size: "small", onClick: () => void qe(pe, !pe.enabled), children: pe.enabled ? "暂停" : "恢复" }),
            /* @__PURE__ */ p($, { size: "small", onClick: () => void mr(pe, 7), children: "补采 7 天" }),
            /* @__PURE__ */ p($, { size: "small", onClick: () => void mr(pe, 1), children: "运行" })
          ] }) },
          { title: "排障", key: "diagnostics", render: (O, pe) => {
            const ve = ((U == null ? void 0 : U.runs) ?? []).filter((W) => W.workflowId === pe.workflowId), _e = ((U == null ? void 0 : U.deliveries) ?? []).filter((W) => W.status === "Failed");
            return /* @__PURE__ */ R(G.Text, { style: { fontSize: 11 }, children: [
              ve.length ? `最近运行 ${ve[0].status}` : "暂无运行",
              " · 失败交付 ",
              _e.length
            ] });
          } }
        ]
      }
    )
  ] });
}
const p1 = `
.industrial-designer{display:flex;flex-direction:column;width:100%;height:calc(100vh - 152px);min-height:520px;background:transparent;overflow:hidden}
.industrial-header{height:auto;min-height:52px;padding:8px 14px;background:#102542;display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:nowrap;line-height:normal;border-radius:10px}
.industrial-title{display:flex;align-items:center;gap:8px;min-width:0;color:#fff;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.industrial-title .ant-typography{color:#fff!important}
.industrial-title .ant-typography-secondary{color:#9db2d0!important}
.industrial-toolbar{display:flex;align-items:center;gap:8px;flex-wrap:nowrap;min-width:0}
.industrial-toolbar-divider{width:1px;height:20px;background:rgba(255,255,255,.24);flex:none}
.industrial-resource-select{width:132px;flex:none}
.industrial-backfill-input{width:128px;flex:none}
.industrial-body{display:grid;grid-template-columns:264px minmax(0,1fr);gap:0;flex:1;min-height:0;margin-top:8px;background:transparent}
.industrial-body--collapsed{grid-template-columns:44px minmax(0,1fr)}
.industrial-palette{height:100%;overflow:auto;padding:12px;background:#fff;border-radius:10px;border:1px solid #e8edf4}
.industrial-palette-rail{height:100%;display:flex;align-items:flex-start;justify-content:center;padding-top:12px;background:#fff;border-radius:10px;border:1px solid #e8edf4;writing-mode:vertical-rl}
.industrial-palette-rail .ant-btn{writing-mode:vertical-rl;height:auto;padding:8px 2px}
.industrial-workflow-select{width:100%;margin-bottom:10px}
.industrial-center{display:flex;flex-direction:column;min-width:0;min-height:0;padding:0;background:transparent}
.industrial-palette-group{margin-top:12px}
.industrial-palette-group>.ant-typography{display:block;margin-bottom:5px;color:#5c6b80;font-size:11px;letter-spacing:.08em;text-transform:uppercase}
.industrial-palette-node{margin-top:7px;padding:8px 10px;border:1px solid #dae3f0;border-radius:9px;background:#f9fbff;cursor:grab;transition:border-color .12s ease,box-shadow .12s ease,transform .12s ease}
.industrial-palette-node:hover{border-color:#4d6bfe;box-shadow:0 3px 12px rgba(77,107,254,.14);transform:translateY(-1px)}
.industrial-palette-node-title{font-size:13px;font-weight:600;color:#1f2b3d}
.industrial-palette-node-meta,.industrial-palette-node-ports{display:flex;flex-wrap:wrap;gap:4px;margin-top:4px}
.industrial-canvas-wrap{position:relative;flex:1;min-width:0;min-height:0;border:1px solid #dfe7f2;border-radius:10px;background:#fff;overflow:hidden}
.industrial-graph{position:relative;width:100%;height:100%}
.industrial-graph-host{width:100%;height:100%;cursor:grab}
.industrial-graph-hint{position:absolute;left:10px;top:10px;z-index:2;pointer-events:none;padding:3px 9px;border-radius:999px;background:rgba(16,37,66,.78);color:#fff;font-size:11px}
.industrial-properties-drawer .ant-drawer-body{padding:14px 18px}
.industrial-properties-body{min-width:0}
.industrial-edge-ports{display:flex;flex-direction:column;gap:4px}
.industrial-issue{display:flex;flex-direction:column;gap:2px;margin-bottom:8px;font-size:12px}
.industrial-kv-editor,.industrial-list-editor{display:flex;flex-direction:column;gap:5px}
.industrial-kv-row,.industrial-list-row{display:flex;align-items:center;gap:4px}
.industrial-kv-row>*:first-child,.industrial-list-row>*:first-child{flex:1;min-width:0}
.industrial-editor-error{display:block;font-size:11px}
@media(max-width:1180px){.industrial-body{grid-template-columns:224px minmax(0,1fr)}.industrial-resource-select{width:104px}.industrial-backfill-input{width:104px}}
.industrial-context-menu{position:absolute;z-index:30;display:flex;flex-direction:column;min-width:120px;padding:4px;background:#fff;border:1px solid #dfe7f2;border-radius:8px;box-shadow:0 6px 18px rgba(20,40,80,.16)}
.industrial-context-menu button{border:0;background:transparent;padding:6px 8px;text-align:left;font-size:12px;color:#1f2b3d;cursor:pointer;border-radius:4px}
.industrial-context-menu button:hover{background:#f0f4ff}
.industrial-sample-preview{max-height:110px;overflow:auto;padding:6px 8px;background:#f7f9fc;border:1px solid #e8edf4;border-radius:6px;font-size:11px;margin:4px 0 8px}
`;
function v1(e) {
  ev(e);
  const r = typeof document > "u" ? null : document.createElement("style");
  r && (r.dataset.industrialDesigner = "true", r.textContent = p1, document.head.appendChild(r));
  const t = ({ ctx: i }) => Ao(Jw, { ctx: i });
  t.displayName = "IndustrialWorkflowCanvas";
  const n = (i, s) => {
    const o = ({ block: u, ctx: l }) => Ao(s, { block: u, ctx: l });
    return o.displayName = i, e.blocks.register(i, o);
  }, a = [
    e.blocks.register("industrial-workflow-canvas", t),
    n("industrial-workflow-list", Xw),
    n("industrial-resource-console", r1),
    n("industrial-credential-console", t1),
    n("industrial-execution-center", n1),
    n("industrial-file-details", a1),
    n("industrial-machine-console", s1),
    n("industrial-collection-wizard", c1),
    n("industrial-file-profiles", f1),
    n("industrial-binding-console", h1)
  ];
  return () => {
    for (const i of a) i == null || i();
    r == null || r.remove();
  };
}
export {
  Yv as g,
  v1 as r
};
