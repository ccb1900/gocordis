let Lr = null;
function ev(e) {
  Lr = e;
}
function De() {
  if (!Lr && typeof globalThis < "u" && (Lr = globalThis.__CORDIS_CONSOLE ?? null), !Lr)
    throw new Error("[industrial-designer] console facade is not available");
  return Lr;
}
const h = (...e) => De().jsxRuntime.jsx(...e), T = (...e) => De().jsxRuntime.jsxs(...e), pr = (e) => De().jsxRuntime.jsx(De().jsxRuntime.Fragment, e), Oo = (...e) => De().React.createElement(...e), F = (...e) => De().React.useCallback(...e), _e = (...e) => De().React.useEffect(...e), lt = (...e) => De().React.useMemo(...e), ke = (...e) => De().React.useRef(...e), q = (...e) => De().React.useState(...e), fe = (e) => {
  const r = (t) => {
    const n = e.split(".").reduce((a, i) => a == null ? void 0 : a[i], De().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Oo(n, t ?? {});
  };
  return r.displayName = `facade.${e}`, r;
}, Pe = fe("Alert"), L = fe("Button"), cu = fe("Divider"), rv = fe("Drawer"), Eh = fe("Empty"), Po = Object.assign(fe("Form"), {
  Item: fe("Form.Item"),
  useForm: (...e) => De().antd.Form.useForm(...e)
}), V = Object.assign(fe("Input"), {
  Number: fe("InputNumber"),
  TextArea: fe("Input.TextArea")
}), rt = fe("InputNumber"), tv = Object.assign(fe("Layout"), {
  Header: fe("Layout.Header"),
  Content: fe("Layout.Content")
}), Ir = fe("Modal"), Se = fe("Select"), H = fe("Space"), Sh = fe("Spin"), ct = fe("Switch"), Ne = fe("Table"), je = fe("Tag"), G = Object.assign(fe("Typography"), {
  Title: fe("Typography.Title"),
  Text: fe("Typography.Text"),
  Paragraph: fe("Typography.Paragraph")
}), nv = new Proxy({}, {
  get: (e, r) => (...t) => {
    var a;
    const n = (a = De().antd.message) == null ? void 0 : a[r];
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
            ...((a == null ? void 0 : a.inputs) ?? []).map((o, u, c) => ({
              key: `in:${o.name}`,
              placement: [0, (u + 1) / (c.length + 1)],
              r: 5,
              stroke: o.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((a == null ? void 0 : a.outputs) ?? []).map((o, u, c) => ({
              key: `out:${o.name}`,
              placement: [1, (u + 1) / (c.length + 1)],
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
const ov = "__industrial_drag_target__", uv = "__industrial_connect_source__", kt = (e, r) => {
  var t;
  return (((t = e.isPanModifier) == null ? void 0 : t.call(e)) ?? !1) || r.button === 1 || r.buttons === 4 || r.shiftKey === !0;
}, pu = (e, r) => {
  var l, d;
  if (r.targetType !== "node" || !((l = r.target) != null && l.id)) return !1;
  const t = ((d = e.gestureOrigin) == null ? void 0 : d.call(e)) ?? r.client ?? r.canvas;
  if (!t || !e.graph) return !1;
  const n = String(r.target.id), a = e.graph.getNodeData().find((p) => String(p.id) === n), i = a == null ? void 0 : a.style;
  if (typeof (i == null ? void 0 : i.x) != "number" || typeof i.y != "number") return !1;
  const [s, o] = e.graph.getCanvasByClient([t.x, t.y]), u = Math.abs(s - i.x), c = Math.abs(o - i.y);
  return Math.abs(u - Th / 2) <= 14 && c <= Rh / 2 + 10;
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
      enable: (t) => t.targetType === "canvas" || kt(e, t),
      range: 0.9
    },
    {
      type: "drag-element",
      state: ov,
      enable: (t) => (t.targetType === "node" || t.targetType === "combo") && !pu(e, t) && !kt(e, t),
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
      enable: (t) => kt(e, t) ? (r.armed = !1, !1) : r.armed ? t.targetType === "node" || t.targetType === "combo" : pu(e, t) ? (r.armed = !0, !0) : !1,
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
const lv = (e) => {
  var t;
  const r = e;
  return (r == null ? void 0 : r.targetType) === "node" ? (t = r.target) == null ? void 0 : t.id : void 0;
};
function cv(e) {
  const {
    definition: r,
    catalog: t,
    selectedId: n,
    onSelect: a,
    onDropNode: i,
    onCreateEdge: s,
    onPositions: o,
    onSelectIds: u,
    onGraphReady: c,
    onSelectEdge: l,
    onDeleteNodeById: d,
    onDuplicateNode: p,
    onDeleteEdgeById: v
  } = e, g = ke(null), f = ke(null), y = ke(!1), C = ke(!1), R = ke({ armed: !1 }), _ = ke(null), [k, N] = q(null), m = ke({ onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o });
  m.current = { onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o, onSelectEdge: l };
  const S = lt(
    () => iv(r, t, n),
    [r, t, n]
  ), D = ke(S);
  D.current = S;
  const U = ke(/* @__PURE__ */ new Set());
  return U.current = new Set(r.nodes.map((I) => I.id)), _e(() => {
    const I = (J) => {
      J.code === "Space" && !J.repeat && (C.current = !0);
    }, M = (J) => {
      J.code === "Space" && (C.current = !1);
    }, j = () => {
      C.current = !1;
    };
    return window.addEventListener("keydown", I), window.addEventListener("keyup", M), window.addEventListener("blur", j), () => {
      window.removeEventListener("keydown", I), window.removeEventListener("keyup", M), window.removeEventListener("blur", j);
    };
  }, []), _e(() => {
    const I = g.current;
    if (!I || y.current) return;
    let M = !1;
    return import("./esm-DfrWNZwa.js").then(({ Graph: j }) => {
      if (M || !g.current || y.current) return;
      const J = sv(D.current, I), re = new j({
        ...J,
        behaviors: xh({
          graph: {
            getNodeData: () => {
              var K;
              return ((K = f.current) == null ? void 0 : K.getNodeData()) ?? [];
            },
            getCanvasByClient: (K) => {
              var W;
              const Y = ((W = f.current) == null ? void 0 : W.getCanvasByClient(K)) ?? K;
              return [Y[0], Y[1]];
            }
          },
          isPanModifier: () => C.current,
          gestureOrigin: () => _.current,
          connectionState: R.current,
          allowedIds: () => U.current,
          onPositions: (K) => m.current.onPositions(K),
          onSelectIds: (K) => u == null ? void 0 : u(K),
          onCreateEdge: (K, Y) => m.current.onCreateEdge(K, Y)
        })
      });
      re.on("node:click", (K) => {
        const Y = lv(K);
        Y && m.current.onSelect(Y);
      }), re.on("edge:click", (K) => {
        var W, x, $;
        const Y = K;
        ($ = (x = m.current).onSelectEdge) == null || $.call(x, ((W = Y.target) == null ? void 0 : W.id) ?? null);
      }), re.on("node:contextmenu", (K) => {
        var W, x, $;
        const Y = K;
        (W = Y.target) != null && W.id && N({
          x: ((x = Y.client) == null ? void 0 : x.x) ?? 0,
          y: (($ = Y.client) == null ? void 0 : $.y) ?? 0,
          type: "node",
          id: Y.target.id
        });
      }), re.on("edge:contextmenu", (K) => {
        var W, x, $;
        const Y = K;
        (W = Y.target) != null && W.id && N({
          x: ((x = Y.client) == null ? void 0 : x.x) ?? 0,
          y: (($ = Y.client) == null ? void 0 : $.y) ?? 0,
          type: "edge",
          id: Y.target.id
        });
      }), re.on("canvas:click", () => m.current.onSelect(null)), re.on("pointerdown", (K) => {
        const Y = K;
        _.current = Y.client ? { x: Y.client.x, y: Y.client.y } : null;
      }), re.on("pointerup", () => {
        queueMicrotask(() => {
          R.current.armed = !1, _.current = null;
        });
      }), f.current = re, I.__industrialGraph = re, c == null || c(re), y.current = !0, re.render();
    }), () => {
      M = !0;
    };
  }, []), _e(() => {
    const I = f.current;
    !I || !y.current || (I.setData(S), I.draw());
  }, [S]), _e(() => () => {
    var I;
    (I = f.current) == null || I.destroy(), f.current = null, y.current = !1;
  }, []), /* @__PURE__ */ h("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ T("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ h(
      "div",
      {
        ref: g,
        className: "industrial-graph-host",
        onDragOver: (I) => I.preventDefault(),
        onDrop: (I) => {
          I.preventDefault();
          const M = I.dataTransfer.getData("application/x-industrial-node") || I.dataTransfer.getData("text/plain");
          if (!M) return;
          const j = f.current, J = g.current;
          if (!j || !J) return;
          const re = J.getBoundingClientRect(), K = j.getCanvasByViewport([
            I.clientX - re.left,
            I.clientY - re.top
          ]);
          m.current.onDropNode(M, K[0], K[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    k && /* @__PURE__ */ h("div", { className: "industrial-context-menu", style: { left: k.x, top: k.y }, children: k.type === "node" ? /* @__PURE__ */ T(pr, { children: [
      /* @__PURE__ */ h("button", { onClick: () => {
        p == null || p(k.id), N(null);
      }, children: "复制节点" }),
      /* @__PURE__ */ h("button", { onClick: () => {
        d == null || d(k.id), N(null);
      }, children: "删除节点" })
    ] }) : /* @__PURE__ */ T(pr, { children: [
      /* @__PURE__ */ h("button", { onClick: () => {
        l == null || l(k.id), N(null);
      }, children: "编辑连线" }),
      /* @__PURE__ */ h("button", { onClick: () => {
        v == null || v(k.id), N(null);
      }, children: "删除连线" })
    ] }) }),
    /* @__PURE__ */ h("div", { className: "industrial-graph-hint", children: "Shift+拖拽框选 · 拖节点移动 · 端口拖拽连线 · 滚轮缩放 · 空格/中键平移" })
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
  return /* @__PURE__ */ T("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ h(G.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ h(G.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    r && /* @__PURE__ */ h(Sh, { size: "small", style: { display: "block", marginTop: 16 } }),
    t && /* @__PURE__ */ h(G.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: t }),
    !r && !t && e.length === 0 && /* @__PURE__ */ h(Eh, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(a.entries()).map(([s, o]) => /* @__PURE__ */ T("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ h(G.Text, { strong: !0, style: { fontSize: 12 }, children: s }),
      o.map((u) => /* @__PURE__ */ T(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          role: "button",
          tabIndex: 0,
          onDragStart: (c) => i(c, u),
          onClick: () => n == null ? void 0 : n(u.nodeTypeId),
          onKeyDown: (c) => {
            (c.key === "Enter" || c.key === " ") && (n == null || n(u.nodeTypeId));
          },
          title: u.description || u.nodeTypeId,
          children: [
            /* @__PURE__ */ h("div", { className: "industrial-palette-node-title", children: u.name }),
            /* @__PURE__ */ T("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ h(je, { bordered: !1, color: "blue", children: u.version }),
              /* @__PURE__ */ h(G.Text, { type: "secondary", style: { fontSize: 11 }, children: u.nodeTypeId })
            ] }),
            /* @__PURE__ */ T("div", { className: "industrial-palette-node-ports", children: [
              u.inputs.map((c) => /* @__PURE__ */ T(je, { bordered: !1, color: "default", children: [
                "in ",
                c.name,
                ": ",
                c.type
              ] }, `in-${c.name}`)),
              u.outputs.map((c) => /* @__PURE__ */ T(je, { bordered: !1, color: "green", children: [
                "out ",
                c.name,
                ": ",
                c.type
              ] }, `out-${c.name}`))
            ] })
          ]
        },
        u.nodeTypeId
      ))
    ] }, s))
  ] });
}
const tt = (e = "daily-collection") => ({
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
function At(e, r, t = 0, n = 0) {
  return { id: r, type: e.nodeTypeId, config: fv(e), x: t, y: n };
}
function ar(e, r) {
  return e.get(r);
}
function nt(e, r, t) {
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
const Oe = (e, r, t) => ({ path: e, code: r, message: t });
function hv(e, r) {
  return e.source === r.source && e.sourcePort === r.sourcePort && e.target === r.target && e.targetPort === r.targetPort;
}
function pv(e, r, t) {
  const n = `edges.${e.edges.length}`, a = e.nodes.find((l) => l.id === r.source), i = e.nodes.find((l) => l.id === r.target);
  if (!a || !i)
    return { definition: e, issue: Oe(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((l) => hv(l, r)))
    return { definition: e };
  const s = ar(t, a.type), o = ar(t, i.type);
  if (!s || !o)
    return { definition: e, issue: Oe(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const u = nt(s, r.sourcePort, "outputs"), c = nt(o, r.targetPort, "inputs");
  return !u || !c ? { definition: e, issue: Oe(n, "PORT_MISSING", "declared port does not exist") } : jo(u.type, c.type) ? { definition: { ...e, edges: [...e.edges, r] } } : {
    definition: e,
    issue: Oe(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${u.type} → ${c.type} is not a legal connection`
    )
  };
}
function Ot(e, r) {
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
  e.workflowId.trim() || n.push(Oe("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(Oe("workflow", "INVALID_VERSION", "version must be positive"));
  const a = /* @__PURE__ */ new Map();
  for (const o of e.nodes)
    a.has(o.id) && n.push(Oe(`nodes.${o.id}`, "DUPLICATE_NODE_ID", `duplicate node '${o.id}'`)), a.set(o.id, o);
  const i = /* @__PURE__ */ new Map();
  for (const o of e.nodes) {
    const u = ar(r, o.type);
    if (!u) {
      n.push(Oe(
        `nodes.${o.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${o.type}' is not registered`
      ));
      continue;
    }
    i.set(o.id, u);
    const c = u.configurationSchema.required ?? [];
    for (const l of c) {
      const d = o.config[l];
      (d == null || d === "") && n.push(Oe(
        `nodes.${o.id}.config.${l}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${l}' is missing`
      ));
    }
    t && typeof o.config.resourceId == "string" && (t.has(o.config.resourceId) || n.push(Oe(
      `nodes.${o.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${o.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((o, u) => {
    const c = a.get(o.source), l = a.get(o.target);
    if (!c || !l) {
      n.push(Oe(`edges.${u}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const d = i.get(o.source), p = i.get(o.target);
    if (!d || !p) return;
    const v = nt(d, o.sourcePort, "outputs"), g = nt(p, o.targetPort, "inputs");
    if (!v || !g) {
      n.push(Oe(`edges.${u}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    jo(v.type, g.type) || n.push(Oe(
      `edges.${u}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${v.type} → ${g.type} is not a legal connection`
    ));
  });
  for (const o of e.nodes) {
    const u = i.get(o.id);
    if (u)
      for (const c of u.inputs.filter((l) => l.required))
        e.edges.some((l) => l.target === o.id && l.targetPort === c.name && a.has(l.source)) || n.push(Oe(
          `nodes.${o.id}.inputs.${c.name}`,
          "MISSING_INPUT",
          `required input '${c.name}' is not connected`
        ));
  }
  const s = mv(e);
  return s && n.push(Oe(`nodes.${s[0]}`, "GRAPH_CYCLE", `cycle: ${s.join(" → ")}`)), n;
}
const No = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function _v(e) {
  const r = No(e.additionalProperties).enum;
  if (!Array.isArray(r)) return null;
  const t = r.filter((n) => typeof n == "string");
  return t.length > 0 ? t : null;
}
function kh(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && No(e.items).type === "string" ? "string-list" : e.type === "object" && No(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function Do(e) {
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
function Jr(e) {
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
      for (const i of Do(r[n]))
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
  const s = JSON.stringify(e ?? {}), [o, u] = q(() => Do(e)), [c, l] = q(s);
  _e(() => {
    s !== c && (u(Do(e)), l(s));
  }, [s, c, e]);
  const d = (v) => {
    u(v), r == null || r(yu(v).record);
  }, { duplicateKeys: p } = yu(o);
  return /* @__PURE__ */ T("div", { "data-testid": i, className: "industrial-kv-editor", children: [
    o.map((v, g) => /* @__PURE__ */ T("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ h(
        V,
        {
          "aria-label": `${n} ${g + 1}`,
          value: v.key,
          placeholder: n,
          onChange: (f) => d(o.map((y, C) => C === g ? { ...y, key: f.target.value } : y))
        }
      ),
      t && t.length > 0 ? /* @__PURE__ */ h(
        Se,
        {
          "aria-label": `${a} ${g + 1}`,
          value: v.value || void 0,
          placeholder: a,
          options: t.map((f) => ({ label: f, value: f })),
          onChange: (f) => d(o.map((y, C) => C === g ? { ...y, value: f ?? "" } : y))
        }
      ) : /* @__PURE__ */ h(
        V,
        {
          "aria-label": `${a} ${g + 1}`,
          value: v.value,
          placeholder: a,
          onChange: (f) => d(o.map((y, C) => C === g ? { ...y, value: f.target.value } : y))
        }
      ),
      /* @__PURE__ */ h(
        L,
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
    /* @__PURE__ */ h(
      L,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => d([...o, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    p.length > 0 && /* @__PURE__ */ T(G.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      p.join("、")
    ] })
  ] });
}
function qv({ value: e, onChange: r, suggestions: t = [], testId: n }) {
  const a = JSON.stringify(Jr(e)), [i, s] = q(() => Jr(e)), [o, u] = q(a);
  _e(() => {
    a !== o && (s(Jr(e)), u(a));
  }, [a, o, e]);
  const c = (l) => {
    s(l), r == null || r(Jr(l));
  };
  return /* @__PURE__ */ T("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ h("datalist", { id: `${n ?? "list"}-suggestions`, children: t.map((l) => /* @__PURE__ */ h("option", { value: l }, l)) }),
    i.map((l, d) => /* @__PURE__ */ T("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ h(
        V,
        {
          "aria-label": `字段 ${d + 1}`,
          value: l,
          list: `${n ?? "list"}-suggestions`,
          onChange: (p) => c(i.map((v, g) => g === d ? p.target.value : v))
        }
      ),
      /* @__PURE__ */ h(
        L,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${d + 1}`,
          onClick: () => c(i.filter((p, v) => v !== d)),
          children: "删"
        }
      )
    ] }, d)),
    /* @__PURE__ */ h(L, { size: "small", type: "dashed", block: !0, onClick: () => c([...i, ""]), children: "添加字段" }),
    i.length === 0 && /* @__PURE__ */ h(G.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Cv({ value: e, onChange: r }) {
  const t = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, a] = q(t), [i, s] = q(null);
  _e(() => {
    a(t), s(null);
  }, [t]);
  const o = (u) => {
    a(u);
    try {
      const c = JSON.parse(u);
      s(null), r == null || r(c);
    } catch (c) {
      s(c.message);
    }
  };
  return /* @__PURE__ */ T("div", { children: [
    /* @__PURE__ */ h(V.TextArea, { rows: 5, value: n, onChange: (u) => o(u.target.value) }),
    i && /* @__PURE__ */ h(G.Text, { type: "danger", style: { fontSize: 11 }, children: i })
  ] });
}
function Ev({ value: e, onChange: r, resources: t, resourceType: n, label: a }) {
  const i = t.filter((o) => o.enabled !== !1 && (!n || o.type === n)).map((o) => ({
    value: o.id,
    label: `${o.name} (${o.type})`
  })), s = t.find((o) => o.id === e);
  return /* @__PURE__ */ T("div", { children: [
    /* @__PURE__ */ T(
      "select",
      {
        "aria-label": a,
        value: e ?? "",
        onChange: (o) => r == null ? void 0 : r(o.target.value || void 0),
        children: [
          /* @__PURE__ */ h("option", { value: "", children: "未选择" }),
          i.map((o) => /* @__PURE__ */ h("option", { value: o.value, children: o.label }, o.value))
        ]
      }
    ),
    (s == null ? void 0 : s.credentialId) && /* @__PURE__ */ T(G.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      s.credentialName ?? s.credentialId
    ] })
  ] });
}
function Sv({ name: e, schema: r, required: t, resources: n, resourceType: a, properties: i, config: s }) {
  let o, u = "value";
  switch (kh(r)) {
    case "resource":
      o = /* @__PURE__ */ h(
        Ev,
        {
          resources: n,
          resourceType: a,
          label: String(r.title ?? e)
        }
      );
      break;
    case "enum":
      o = /* @__PURE__ */ h(
        Se,
        {
          allowClear: !0,
          options: (r.enum ?? []).map((c) => ({ label: String(c), value: c }))
        }
      );
      break;
    case "number":
      o = /* @__PURE__ */ h(rt, { style: { width: "100%" } });
      break;
    case "boolean":
      o = /* @__PURE__ */ h(ct, {}), u = "checked";
      break;
    case "key-value":
      o = /* @__PURE__ */ h(
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
      o = /* @__PURE__ */ h(qv, { testId: `field-${e}`, suggestions: wv(i, s) });
      break;
    case "json":
      o = /* @__PURE__ */ h(Cv, {});
      break;
    default:
      o = /* @__PURE__ */ h(V, { placeholder: r.description });
      break;
  }
  return /* @__PURE__ */ h(
    Po.Item,
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
    onDeleteNode: c,
    onDeleteEdge: l,
    onCreateEdge: d,
    onUpdateEdge: p,
    onSelectNode: v,
    selectedEdgeId: g,
    sampleLines: f
  } = e, [y] = Po.useForm(), [C, R] = q("{}"), [_, k] = q(null), [N, m] = q(), [S, D] = q(), [U, z] = q(!1), [I, M] = q(!1);
  _e(() => {
    y.setFieldsValue((r == null ? void 0 : r.config) ?? {}), R(JSON.stringify((r == null ? void 0 : r.config) ?? {}, null, 2)), k(null);
  }, [y, r == null ? void 0 : r.id, r == null ? void 0 : r.config]);
  const j = new Set((t == null ? void 0 : t.configurationSchema.required) ?? []), J = Object.entries((t == null ? void 0 : t.configurationSchema.properties) ?? {}), re = () => {
    if (J.length > 0) {
      y.validateFields().then((x) => u(vv((r == null ? void 0 : r.config) ?? {}, x)));
      return;
    }
    try {
      const x = JSON.parse(C);
      if (x === null || typeof x != "object" || Array.isArray(x))
        throw new Error("配置必须是 JSON 对象");
      k(null), u(x);
    } catch (x) {
      k(x.message);
    }
  }, Y = (r && !U ? a.map((x, $) => ({ edge: x, index: $ })).filter(({ edge: x }) => x.source === r.id || x.target === r.id) : a.map((x, $) => ({ edge: x, index: $ }))).map(({ edge: x, index: $ }) => ({
    ...x,
    index: $,
    key: `${$}:${x.source}->${x.target}`
  })), W = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (x, $) => /* @__PURE__ */ T(G.Text, { style: { fontSize: 11 }, children: [
        $.source,
        " → ",
        $.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (x, $) => {
        const P = i.get($.source), ne = i.get($.target);
        return /* @__PURE__ */ T("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ h(
            Se,
            {
              size: "small",
              value: $.sourcePort,
              style: { width: 110 },
              onChange: (le) => p($.index, { ...$, sourcePort: le }),
              options: ((P == null ? void 0 : P.outputs) ?? []).map((le) => ({ label: `${le.name} (${le.type})`, value: le.name }))
            }
          ),
          /* @__PURE__ */ h(
            Se,
            {
              size: "small",
              value: $.targetPort,
              style: { width: 110 },
              onChange: (le) => p($.index, { ...$, targetPort: le }),
              options: ((ne == null ? void 0 : ne.inputs) ?? []).map((le) => ({ label: `${le.name} (${le.type})`, value: le.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (x, $) => /* @__PURE__ */ h(L, { danger: !0, size: "small", type: "text", onClick: () => l($.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ T("div", { className: "industrial-properties-body", children: [
    !r && /* @__PURE__ */ h(Eh, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    r && /* @__PURE__ */ T(pr, { children: [
      /* @__PURE__ */ h(G.Title, { level: 5, style: { margin: 0 }, children: (t == null ? void 0 : t.name) ?? r.type }),
      /* @__PURE__ */ h(G.Text, { type: "secondary", style: { fontSize: 11 }, children: r.id }),
      /* @__PURE__ */ h(G.Paragraph, { type: "secondary", style: { fontSize: 12, marginTop: 6 }, children: (t == null ? void 0 : t.description) ?? "请按业务含义配置该节点。" }),
      /* @__PURE__ */ T(H, { wrap: !0, size: 4, style: { marginBottom: 8 }, children: [
        /* @__PURE__ */ h(je, { color: "blue", children: (t == null ? void 0 : t.category) ?? "采集节点" }),
        /* @__PURE__ */ T(je, { children: [
          "版本 ",
          (t == null ? void 0 : t.version) ?? "1.0.0"
        ] }),
        t == null ? void 0 : t.inputs.map((x) => /* @__PURE__ */ T(je, { color: x.required ? "red" : "default", children: [
          "输入 ",
          x.name,
          " · ",
          x.type
        ] }, `in-${x.name}`)),
        t == null ? void 0 : t.outputs.map((x) => /* @__PURE__ */ T(je, { color: "green", children: [
          "输出 ",
          x.name,
          " · ",
          x.type
        ] }, `out-${x.name}`))
      ] }),
      (r.type.includes("reader") || r.type.includes("mapping") || r.type.includes("source") || r.type.includes("filesystem")) && f && f.length > 0 && /* @__PURE__ */ T(pr, { children: [
        /* @__PURE__ */ h(G.Text, { strong: !0, style: { fontSize: 12 }, children: "样本预览" }),
        /* @__PURE__ */ h("pre", { className: "industrial-sample-preview", children: f.join(`
`) })
      ] }),
      /* @__PURE__ */ h(Po, { form: y, layout: "vertical", size: "small", style: { marginTop: 12 }, children: J.map(([x, $]) => /* @__PURE__ */ h(
        Sv,
        {
          name: x,
          schema: $,
          required: j.has(x),
          resources: s,
          resourceType: typeof (t == null ? void 0 : t.configurationSchema.resourceType) == "string" ? t.configurationSchema.resourceType : void 0,
          properties: (t == null ? void 0 : t.configurationSchema.properties) ?? {},
          config: (r == null ? void 0 : r.config) ?? {}
        },
        x
      )) }),
      J.length === 0 && /* @__PURE__ */ h(G.Text, { type: "secondary", style: { fontSize: 12 }, children: "此节点无需填写业务参数；保留默认配置即可。" }),
      /* @__PURE__ */ h(L, { type: "link", size: "small", onClick: () => M((x) => !x), children: I ? "收起高级配置" : "高级配置" }),
      I && /* @__PURE__ */ T(pr, { children: [
        /* @__PURE__ */ h(G.Text, { type: "secondary", style: { fontSize: 12 }, children: "高级配置直接编辑节点 JSON，仅在需要复杂表达式时使用。" }),
        /* @__PURE__ */ h(
          V.TextArea,
          {
            rows: 8,
            value: C,
            style: { marginTop: 6 },
            onChange: (x) => R(x.target.value)
          }
        ),
        _ && /* @__PURE__ */ h(G.Text, { type: "danger", style: { fontSize: 12 }, children: _ }),
        (t == null ? void 0 : t.activityType) && /* @__PURE__ */ T(G.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
          "运行时 Activity：",
          t.activityType
        ] })
      ] }),
      /* @__PURE__ */ h(L, { type: "primary", block: !0, onClick: re, children: "应用配置" }),
      /* @__PURE__ */ h(L, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: c, children: "删除节点" })
    ] }),
    g && (() => {
      const x = a.findIndex((P) => `e:${P.source}:${P.sourcePort}>${P.target}:${P.targetPort}` === g);
      if (x < 0) return null;
      const $ = a[x];
      return /* @__PURE__ */ h(
        Pe,
        {
          type: "info",
          showIcon: !0,
          style: { marginTop: 8 },
          message: `当前连线：${$.source}.${$.sourcePort} → ${$.target}.${$.targetPort}`,
          description: /* @__PURE__ */ h(L, { size: "small", danger: !0, onClick: () => l(x), children: "删除此连线" })
        }
      );
    })(),
    /* @__PURE__ */ T(cu, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "相关连线 (",
      Y.length,
      " / ",
      a.length,
      ")"
    ] }),
    r && /* @__PURE__ */ h(L, { type: "link", size: "small", onClick: () => z((x) => !x), children: U ? "仅看当前节点" : "显示全部连线" }),
    n.length > 1 && /* @__PURE__ */ T(H, { wrap: !0, style: { marginBottom: 8 }, children: [
      /* @__PURE__ */ T(
        "select",
        {
          "aria-label": "连线起点",
          value: N ?? "",
          onChange: (x) => m(x.target.value || void 0),
          children: [
            /* @__PURE__ */ h("option", { value: "", children: "起点节点" }),
            n.map((x) => /* @__PURE__ */ h("option", { value: x.id, children: x.id }, x.id))
          ]
        }
      ),
      /* @__PURE__ */ T(
        "select",
        {
          "aria-label": "连线终点",
          value: S ?? "",
          onChange: (x) => D(x.target.value || void 0),
          children: [
            /* @__PURE__ */ h("option", { value: "", children: "终点节点" }),
            n.map((x) => /* @__PURE__ */ h("option", { value: x.id, children: x.id }, x.id))
          ]
        }
      ),
      /* @__PURE__ */ h(
        L,
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
    a.length === 0 ? /* @__PURE__ */ h(G.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ h(Ne, { size: "small", columns: W, dataSource: Y, pagination: !1 }),
    o.length > 0 && /* @__PURE__ */ T(pr, { children: [
      /* @__PURE__ */ T(cu, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        o.length,
        ")"
      ] }),
      o.map((x, $) => /* @__PURE__ */ T("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ h(je, { color: "red", children: x.code }),
        /* @__PURE__ */ T(
          G.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const P = /^nodes\.([^.]+)/.exec(x.path);
              P && v(P[1]);
            },
            children: [
              x.path,
              ": ",
              x.message
            ]
          }
        )
      ] }, `${x.path}-${$}`))
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
const te = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, we = (e) => Array.isArray(e) ? e : [], A = (e, r = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : r ?? "", ge = (e, r = 0) => {
  const t = typeof e == "string" ? Number(e) : e;
  return typeof t == "number" && Number.isFinite(t) ? t : r;
}, b = (e, ...r) => {
  for (const t of r)
    if (e[t] !== void 0 && e[t] !== null) return e[t];
};
function Ae(e) {
  const r = te(e);
  if (Object.prototype.hasOwnProperty.call(r, "data")) return r.data;
  if (typeof r.code == "string" && typeof r.message == "string")
    throw new xv(`${r.code}: ${r.message}`);
  return e;
}
function kv(e) {
  const r = A(e).trim().toLowerCase();
  return Rv.find((n) => n.toLowerCase() === r) ?? "Unknown";
}
function Av(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const r = te(e), t = A(b(r, "name", "port", "id")).trim();
  return t ? {
    name: t,
    type: kv(b(r, "type", "portType", "dataType")),
    required: b(r, "required", "isRequired") === !0
  } : null;
}
function mu(e) {
  const r = we(e).map(Av).filter((n) => n !== null), t = /* @__PURE__ */ new Set();
  return r.filter((n) => t.has(n.name) ? !1 : (t.add(n.name), !0));
}
const Ov = (e) => {
  const r = te(e);
  return Object.keys(r).length === 0 ? { type: "object", properties: {} } : r;
};
function Pv(e) {
  const r = te(e), t = A(b(r, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return t ? {
    nodeTypeId: t,
    name: A(b(r, "name", "title"), t),
    description: A(b(r, "description", "desc")),
    category: A(b(r, "category", "group"), "General"),
    icon: A(b(r, "icon"), "AppstoreOutlined"),
    version: A(b(r, "version"), "1.0.0"),
    inputs: mu(b(r, "inputs", "inputPorts", "input_ports")),
    outputs: mu(b(r, "outputs", "outputPorts", "output_ports")),
    configurationSchema: Ov(b(r, "configurationSchema", "configSchema", "schema")),
    activityType: A(b(r, "activityType", "activity_type")),
    dependencies: we(b(r, "dependencies")).map((n) => A(n)).filter(Boolean)
  } : null;
}
function Ah(e) {
  const r = Ae(e), t = Array.isArray(r) ? r : we(b(te(r), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return t.map(Pv).filter((a) => a !== null).filter((a) => n.has(a.nodeTypeId) ? !1 : (n.add(a.nodeTypeId), !0));
}
function Oh(e) {
  const r = Ae(e), t = Array.isArray(r) ? r : we(b(te(r), "resources", "items")), n = /* @__PURE__ */ new Set();
  return t.map((a) => {
    const i = te(a), s = A(b(i, "id")).trim();
    return !s || n.has(s) ? null : (n.add(s), {
      id: s,
      name: A(b(i, "name"), s),
      type: A(b(i, "type"), "unknown"),
      enabled: i.enabled !== !1,
      credentialId: typeof i.credentialId == "string" ? i.credentialId : null,
      credentialName: typeof i.credentialName == "string" ? i.credentialName : null
    });
  }).filter((a) => a !== null);
}
function Ph(e) {
  const r = te(e), t = A(b(r, "workflowId", "workflow_id", "id")).trim();
  return t ? {
    workflowId: t,
    name: A(b(r, "name", "title"), t),
    version: ge(b(r, "version"), 1)
  } : null;
}
function Nh(e) {
  const r = Ae(e);
  return (Array.isArray(r) ? r : we(b(te(r), "workflows", "items"))).map(Ph).filter((n) => n !== null);
}
function at(e) {
  const r = te(e), t = {};
  for (const [n, a] of Object.entries(r))
    a !== void 0 && (t[n] = a);
  return t;
}
function Nv(e) {
  const r = te(e), t = A(b(r, "id", "nodeId", "node_id")).trim();
  if (!t) return null;
  const n = te(b(r, "position", "layout"));
  return {
    id: t,
    type: A(b(r, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: at(b(r, "config", "configuration")),
    x: ge(b(r, "x") ?? b(n, "x"), 0),
    y: ge(b(r, "y") ?? b(n, "y"), 0)
  };
}
function Dv(e) {
  const r = te(e), t = A(b(r, "source", "from", "sourceId")).trim(), n = A(b(r, "target", "to", "targetId")).trim();
  return !t || !n ? null : {
    source: t,
    sourcePort: A(b(r, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: A(b(r, "targetPort", "target_port", "toPort"), "in")
  };
}
function Mv(e) {
  const r = Ae(e), t = te(r), n = Ph(t) ?? { workflowId: "", name: "", version: 1 }, a = we(b(t, "nodes")).map(Nv).filter((u) => u !== null), i = we(b(t, "edges")).map(Dv).filter((u) => u !== null), s = te(b(t, "trigger")), o = {
    kind: A(b(s, "kind", "type"), "manual"),
    cron: b(s, "cron") === void 0 ? null : A(s.cron),
    eventName: b(s, "eventName", "event_name") === void 0 ? null : A(b(s, "eventName", "event_name")),
    config: at(b(s, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: a,
    edges: i,
    trigger: o,
    resourceReferences: we(b(t, "resourceReferences", "resource_references")).map((u) => A(u)).filter(Boolean),
    layout: at(b(t, "layout"))
  };
}
const xr = (e) => e === !0 || typeof e == "string" && e.toLowerCase() === "true";
function Lv(e) {
  const r = Ae(e);
  return (Array.isArray(r) ? r : we(b(te(r), "resources", "items"))).map((n) => {
    const a = te(n), i = b(a, "credentialId", "credential_id");
    return {
      id: A(b(a, "id"), "—"),
      name: A(b(a, "name"), "—"),
      type: A(b(a, "type"), "—"),
      provider: A(b(a, "provider"), "—"),
      enabled: xr(b(a, "enabled")),
      credentialId: i == null ? null : A(i),
      credentialName: b(a, "credentialName") == null ? null : A(b(a, "credentialName")),
      config: at(b(a, "config", "configuration"))
    };
  });
}
function Dh(e) {
  const r = Ae(e);
  return (Array.isArray(r) ? r : we(b(te(r), "credentials", "items"))).map((n) => {
    const a = te(n);
    return {
      id: A(b(a, "id"), "—"),
      name: A(b(a, "name"), "—"),
      kind: A(b(a, "kind"), "—"),
      version: ge(b(a, "version"))
    };
  });
}
function Fv(e) {
  const r = Ae(e);
  return (Array.isArray(r) ? r : we(b(te(r), "tests", "items"))).map((n) => {
    const a = te(n);
    return {
      resourceId: A(b(a, "resourceId", "resource_id"), "—"),
      ok: xr(b(a, "ok")),
      detail: A(b(a, "detail"), "")
    };
  });
}
const Mh = (e) => {
  if (e == null) return null;
  const r = te(e);
  return Object.keys(r).length === 0 ? null : {
    code: A(b(r, "code"), "OTHER"),
    message: A(b(r, "message"), ""),
    action: A(b(r, "action"), "")
  };
};
function jv(e) {
  const r = Ae(e);
  return we(b(te(r), "triggers", "items")).map((n) => {
    const a = te(n), i = (s) => b(a, s) == null ? null : A(b(a, s));
    return {
      workflowId: A(b(a, "workflowId", "workflow_id"), "—"),
      version: ge(b(a, "version")),
      kind: A(b(a, "kind"), "manual"),
      cron: i("cron"),
      eventName: i("eventName"),
      timezone: i("timezone"),
      resourceId: i("resourceId"),
      nextRun: i("nextRun"),
      lastStatus: i("lastStatus"),
      lastStartedAt: i("lastStartedAt"),
      lastFireStatus: i("lastFireStatus"),
      lastFireDueAt: i("lastFireDueAt"),
      lastFireAttempts: b(a, "lastFireAttempts") == null ? null : ge(b(a, "lastFireAttempts")),
      skippedFires: b(a, "skippedFires") == null ? null : ge(b(a, "skippedFires")),
      backlogTruncated: b(a, "backlogTruncated") == null ? null : b(a, "backlogTruncated") === !0,
      lastError: i("lastError")
    };
  });
}
function $v(e) {
  if (e == null) return null;
  const r = Ae(e), t = te(r);
  if (Object.keys(t).length === 0) return null;
  const n = we(b(t, "files")).map((i) => {
    const s = te(i);
    return {
      path: A(b(s, "path"), "—"),
      sourceDate: A(b(s, "sourceDate"), ""),
      inLedger: xr(b(s, "inLedger")),
      expectedTargets: ge(b(s, "expectedTargets")),
      succeededTargets: ge(b(s, "succeededTargets")),
      incompleteTargets: ge(b(s, "incompleteTargets")),
      missingTargets: A(b(s, "missingTargets"), ""),
      wouldSkip: xr(b(s, "wouldSkip")),
      wouldCollect: xr(b(s, "wouldCollect")),
      duplicateRisk: xr(b(s, "duplicateRisk"))
    };
  }), a = te(b(t, "summary"));
  return {
    resourceId: A(b(t, "resourceId"), "—"),
    from: A(b(t, "from"), ""),
    to: A(b(t, "to"), ""),
    files: n,
    summary: {
      discovered: ge(b(a, "discovered")),
      alreadyComplete: ge(b(a, "alreadyComplete")),
      needsCollection: ge(b(a, "needsCollection")),
      duplicateRisk: ge(b(a, "duplicateRisk"))
    }
  };
}
function Gv(e) {
  const r = Ae(e);
  return (Array.isArray(r) ? r : we(b(te(r), "files", "items"))).map((n) => {
    const a = te(n);
    return {
      id: A(b(a, "id"), "—"),
      path: A(b(a, "path"), "—"),
      identityKey: A(b(a, "identityKey"), ""),
      sourceResourceId: A(b(a, "sourceResourceId"), ""),
      length: ge(b(a, "length")),
      sourceDate: A(b(a, "sourceDate"), ""),
      collectedAt: A(b(a, "collectedAt"), ""),
      status: A(b(a, "status"), "—"),
      deliveries: we(b(a, "deliveries")).map((i) => {
        const s = te(i);
        return {
          id: A(b(s, "id"), "—"),
          collectionFileId: A(b(s, "collectionFileId"), ""),
          targetId: A(b(s, "targetId"), "—"),
          targetKind: A(b(s, "targetKind"), "record"),
          status: A(b(s, "status"), "—"),
          attempts: ge(b(s, "attempts")),
          records: ge(b(s, "records")),
          error: b(s, "error") == null ? null : A(b(s, "error")),
          accepted: ge(b(s, "accepted")),
          rejected: ge(b(s, "rejected")),
          warnings: ge(b(s, "warnings")),
          diagnosis: Mh(b(s, "diagnosis"))
        };
      })
    };
  });
}
function zv(e) {
  const r = Ae(e), t = te(r), n = we(b(t, "runs")).map((i) => {
    const s = te(i);
    return {
      id: A(b(s, "id"), "—"),
      workflowId: A(b(s, "workflowId", "workflow_id"), "—"),
      status: A(b(s, "status"), "—"),
      startedAt: A(b(s, "startedAt", "started_at"), ""),
      finishedAt: b(s, "finishedAt", "finished_at") == null ? null : A(b(s, "finishedAt", "finished_at")),
      error: b(s, "error") == null ? null : A(b(s, "error"))
    };
  }), a = we(b(t, "deliveries")).map((i) => {
    const s = te(i);
    return {
      id: A(b(s, "id"), "—"),
      collectionFileId: A(b(s, "collectionFileId", "collection_file_id"), ""),
      targetId: A(b(s, "targetId", "target_id"), "—"),
      targetKind: A(b(s, "targetKind", "target_kind"), "record"),
      status: A(b(s, "status"), "—"),
      attempts: ge(b(s, "attempts")),
      records: ge(b(s, "records")),
      error: b(s, "error") == null ? null : A(b(s, "error")),
      diagnosis: Mh(b(s, "diagnosis"))
    };
  });
  return { runs: n, deliveries: a };
}
function Bv(e) {
  if (e == null) return null;
  const r = Ae(e), t = te(r);
  if (Object.keys(t).length === 0) return null;
  const n = we(b(t, "nodes")).slice(0, 64).map((a) => {
    const i = te(a);
    return {
      nodeId: A(b(i, "nodeId", "node_id"), "—"),
      status: A(b(i, "status"), "—"),
      records: ge(b(i, "records")),
      files: ge(b(i, "files")),
      error: b(i, "error") == null ? null : A(b(i, "error"), void 0),
      samples: we(b(i, "samples")).slice(0, 3).map((s) => A(s))
    };
  });
  return {
    status: A(b(t, "status"), "—"),
    error: b(t, "error") == null ? null : A(b(t, "error"), void 0),
    executed: we(b(t, "executed")).map((a) => A(a)),
    instanceId: b(t, "instanceId") == null ? null : A(b(t, "instanceId"), void 0),
    definitionId: b(t, "definitionId") == null ? null : A(b(t, "definitionId"), void 0),
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
function Vv({
  open: e,
  onClose: r,
  trigger: t,
  resourceIds: n,
  onSave: a,
  ctx: i,
  workflowId: s
}) {
  const [o, u] = q(t), [c, l] = q([]), [d, p] = q(null);
  _e(() => {
    u(t);
  }, [t]), _e(() => {
    e && i.hubQuery("industrial/trigger-status").then((f) => l(jv(f))).catch((f) => p(f instanceof Error ? f.message : String(f)));
  }, [i, e]);
  const v = c.find((f) => f.workflowId === s);
  return /* @__PURE__ */ h(
    Ir,
    {
      open: e,
      title: "触发器配置",
      okText: "保存触发器",
      cancelText: "取消",
      onCancel: r,
      onOk: () => {
        var f, y;
        if (o.kind === "schedule" && !((f = o.cron) != null && f.trim())) {
          p("定时运行需要填写 cron 表达式");
          return;
        }
        if (o.kind === "event" && !((y = o.eventName) != null && y.trim())) {
          p("事件触发需要填写事件名称");
          return;
        }
        p(null), a(o), r();
      },
      width: 720,
      children: /* @__PURE__ */ T(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ T(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ h(G.Text, { children: "触发方式" }),
          /* @__PURE__ */ h(H, { wrap: !0, children: Uv.map((f) => /* @__PURE__ */ h(
            L,
            {
              type: o.kind === f.value ? "primary" : "default",
              onClick: () => u({ ...o, kind: f.value }),
              children: f.label
            },
            f.value
          )) })
        ] }),
        /* @__PURE__ */ h(H, { wrap: !0, children: /* @__PURE__ */ h(
          Se,
          {
            "aria-label": "运行资源",
            allowClear: !0,
            placeholder: "运行资源",
            value: Hv(o),
            options: n.map((f) => ({ value: f, label: f })),
            onChange: (f) => u({
              ...o,
              config: { ...o.config, resourceId: f ?? "" }
            })
          }
        ) }),
        /* @__PURE__ */ T(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ h(G.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ T(H, { wrap: !0, children: [
            /* @__PURE__ */ h(G.Text, { children: "常用" }),
            Kv.map((f) => /* @__PURE__ */ h(
              L,
              {
                size: "small",
                onClick: () => u({ ...o, cron: f.cron }),
                children: f.label
              },
              f.cron
            ))
          ] }),
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "Cron 表达式",
              placeholder: "分 时 日 月 周，例如 0 2 * * *",
              value: o.cron ?? "",
              onChange: (f) => u({ ...o, cron: f.target.value })
            }
          ),
          /* @__PURE__ */ h(
            V,
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
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "事件名称",
              placeholder: "事件名称",
              value: o.eventName ?? "",
              onChange: (f) => u({ ...o, eventName: f.target.value })
            }
          ),
          /* @__PURE__ */ T(G.Text, { type: "secondary", children: [
            "下次运行：",
            (v == null ? void 0 : v.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        v && /* @__PURE__ */ h(
          Ne,
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
        d && /* @__PURE__ */ h(Pe, { type: "error", showIcon: !0, message: d })
      ] })
    }
  );
}
function Hv(e) {
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
  const [s, o] = q(null), [u, c] = q(null), [l, d] = q(!1), p = F(async () => {
    d(!0), c(null);
    try {
      await t.hubCommand("industrial/preview-backfill", {
        workflowId: n,
        resourceId: a,
        lookbackDays: i
      }), o($v(await t.hubQuery("industrial/backfill-preview")));
    } catch (v) {
      c(v instanceof Error ? v.message : String(v));
    } finally {
      d(!1);
    }
  }, [t, i, a, n]);
  return /* @__PURE__ */ h(
    Ir,
    {
      open: e,
      title: "补采预览",
      onCancel: r,
      width: 820,
      footer: [
        /* @__PURE__ */ h(L, { loading: l, onClick: () => void p(), children: "重新扫描" }, "refresh"),
        /* @__PURE__ */ h(L, { type: "primary", onClick: r, children: "关闭" }, "close")
      ],
      children: /* @__PURE__ */ T(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ T(H, { children: [
          /* @__PURE__ */ T(L, { type: "primary", loading: l, onClick: () => void p(), children: [
            "扫描最近 ",
            i,
            " 天"
          ] }),
          /* @__PURE__ */ T(G.Text, { type: "secondary", children: [
            "资源：",
            a || "未选择"
          ] })
        ] }),
        u && /* @__PURE__ */ h(Pe, { type: "error", showIcon: !0, message: u }),
        s && /* @__PURE__ */ T(pr, { children: [
          /* @__PURE__ */ T(G.Text, { children: [
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
          /* @__PURE__ */ h(
            Ne,
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
var Xr = typeof globalThis < "u" ? globalThis : typeof window < "u" ? window : typeof global < "u" ? global : typeof self < "u" ? self : {};
function Yv(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
function $o(e) {
  throw new Error('Could not dynamically require "' + e + '". Please configure the dynamicRequireTargets or/and ignoreDynamicRequires option of @rollup/plugin-commonjs appropriately for this require call to work.');
}
var Pt, bu;
function Jv() {
  if (bu) return Pt;
  bu = 1;
  function e() {
    this.__data__ = [], this.size = 0;
  }
  return Pt = e, Pt;
}
var Nt, _u;
function kr() {
  if (_u) return Nt;
  _u = 1;
  function e(r, t) {
    return r === t || r !== r && t !== t;
  }
  return Nt = e, Nt;
}
var Dt, wu;
function dt() {
  if (wu) return Dt;
  wu = 1;
  var e = kr();
  function r(t, n) {
    for (var a = t.length; a--; )
      if (e(t[a][0], n))
        return a;
    return -1;
  }
  return Dt = r, Dt;
}
var Mt, Iu;
function Xv() {
  if (Iu) return Mt;
  Iu = 1;
  var e = dt(), r = Array.prototype, t = r.splice;
  function n(a) {
    var i = this.__data__, s = e(i, a);
    if (s < 0)
      return !1;
    var o = i.length - 1;
    return s == o ? i.pop() : t.call(i, s, 1), --this.size, !0;
  }
  return Mt = n, Mt;
}
var Lt, qu;
function Zv() {
  if (qu) return Lt;
  qu = 1;
  var e = dt();
  function r(t) {
    var n = this.__data__, a = e(n, t);
    return a < 0 ? void 0 : n[a][1];
  }
  return Lt = r, Lt;
}
var Ft, Cu;
function eg() {
  if (Cu) return Ft;
  Cu = 1;
  var e = dt();
  function r(t) {
    return e(this.__data__, t) > -1;
  }
  return Ft = r, Ft;
}
var jt, Eu;
function rg() {
  if (Eu) return jt;
  Eu = 1;
  var e = dt();
  function r(t, n) {
    var a = this.__data__, i = e(a, t);
    return i < 0 ? (++this.size, a.push([t, n])) : a[i][1] = n, this;
  }
  return jt = r, jt;
}
var $t, Su;
function ft() {
  if (Su) return $t;
  Su = 1;
  var e = Jv(), r = Xv(), t = Zv(), n = eg(), a = rg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, $t = i, $t;
}
var Gt, Tu;
function tg() {
  if (Tu) return Gt;
  Tu = 1;
  var e = ft();
  function r() {
    this.__data__ = new e(), this.size = 0;
  }
  return Gt = r, Gt;
}
var zt, Ru;
function ng() {
  if (Ru) return zt;
  Ru = 1;
  function e(r) {
    var t = this.__data__, n = t.delete(r);
    return this.size = t.size, n;
  }
  return zt = e, zt;
}
var Bt, xu;
function ag() {
  if (xu) return Bt;
  xu = 1;
  function e(r) {
    return this.__data__.get(r);
  }
  return Bt = e, Bt;
}
var Ut, ku;
function ig() {
  if (ku) return Ut;
  ku = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return Ut = e, Ut;
}
var Kt, Au;
function Lh() {
  if (Au) return Kt;
  Au = 1;
  var e = typeof Xr == "object" && Xr && Xr.Object === Object && Xr;
  return Kt = e, Kt;
}
var Vt, Ou;
function Qe() {
  if (Ou) return Vt;
  Ou = 1;
  var e = Lh(), r = typeof self == "object" && self && self.Object === Object && self, t = e || r || Function("return this")();
  return Vt = t, Vt;
}
var Ht, Pu;
function Ar() {
  if (Pu) return Ht;
  Pu = 1;
  var e = Qe(), r = e.Symbol;
  return Ht = r, Ht;
}
var Wt, Nu;
function sg() {
  if (Nu) return Wt;
  Nu = 1;
  var e = Ar(), r = Object.prototype, t = r.hasOwnProperty, n = r.toString, a = e ? e.toStringTag : void 0;
  function i(s) {
    var o = t.call(s, a), u = s[a];
    try {
      s[a] = void 0;
      var c = !0;
    } catch {
    }
    var l = n.call(s);
    return c && (o ? s[a] = u : delete s[a]), l;
  }
  return Wt = i, Wt;
}
var Qt, Du;
function og() {
  if (Du) return Qt;
  Du = 1;
  var e = Object.prototype, r = e.toString;
  function t(n) {
    return r.call(n);
  }
  return Qt = t, Qt;
}
var Yt, Mu;
function qr() {
  if (Mu) return Yt;
  Mu = 1;
  var e = Ar(), r = sg(), t = og(), n = "[object Null]", a = "[object Undefined]", i = e ? e.toStringTag : void 0;
  function s(o) {
    return o == null ? o === void 0 ? a : n : i && i in Object(o) ? r(o) : t(o);
  }
  return Yt = s, Yt;
}
var Jt, Lu;
function ze() {
  if (Lu) return Jt;
  Lu = 1;
  function e(r) {
    var t = typeof r;
    return r != null && (t == "object" || t == "function");
  }
  return Jt = e, Jt;
}
var Xt, Fu;
function Vr() {
  if (Fu) return Xt;
  Fu = 1;
  var e = qr(), r = ze(), t = "[object AsyncFunction]", n = "[object Function]", a = "[object GeneratorFunction]", i = "[object Proxy]";
  function s(o) {
    if (!r(o))
      return !1;
    var u = e(o);
    return u == n || u == a || u == t || u == i;
  }
  return Xt = s, Xt;
}
var Zt, ju;
function ug() {
  if (ju) return Zt;
  ju = 1;
  var e = Qe(), r = e["__core-js_shared__"];
  return Zt = r, Zt;
}
var en, $u;
function lg() {
  if ($u) return en;
  $u = 1;
  var e = ug(), r = function() {
    var n = /[^.]+$/.exec(e && e.keys && e.keys.IE_PROTO || "");
    return n ? "Symbol(src)_1." + n : "";
  }();
  function t(n) {
    return !!r && r in n;
  }
  return en = t, en;
}
var rn, Gu;
function Fh() {
  if (Gu) return rn;
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
  return rn = t, rn;
}
var tn, zu;
function cg() {
  if (zu) return tn;
  zu = 1;
  var e = Vr(), r = lg(), t = ze(), n = Fh(), a = /[\\^$.*+?()[\]{}|]/g, i = /^\[object .+?Constructor\]$/, s = Function.prototype, o = Object.prototype, u = s.toString, c = o.hasOwnProperty, l = RegExp(
    "^" + u.call(c).replace(a, "\\$&").replace(/hasOwnProperty|(function).*?(?=\\\()| for .+?(?=\\\])/g, "$1.*?") + "$"
  );
  function d(p) {
    if (!t(p) || r(p))
      return !1;
    var v = e(p) ? l : i;
    return v.test(n(p));
  }
  return tn = d, tn;
}
var nn, Bu;
function dg() {
  if (Bu) return nn;
  Bu = 1;
  function e(r, t) {
    return r == null ? void 0 : r[t];
  }
  return nn = e, nn;
}
var an, Uu;
function Cr() {
  if (Uu) return an;
  Uu = 1;
  var e = cg(), r = dg();
  function t(n, a) {
    var i = r(n, a);
    return e(i) ? i : void 0;
  }
  return an = t, an;
}
var sn, Ku;
function Go() {
  if (Ku) return sn;
  Ku = 1;
  var e = Cr(), r = Qe(), t = e(r, "Map");
  return sn = t, sn;
}
var on, Vu;
function ht() {
  if (Vu) return on;
  Vu = 1;
  var e = Cr(), r = e(Object, "create");
  return on = r, on;
}
var un, Hu;
function fg() {
  if (Hu) return un;
  Hu = 1;
  var e = ht();
  function r() {
    this.__data__ = e ? e(null) : {}, this.size = 0;
  }
  return un = r, un;
}
var ln, Wu;
function hg() {
  if (Wu) return ln;
  Wu = 1;
  function e(r) {
    var t = this.has(r) && delete this.__data__[r];
    return this.size -= t ? 1 : 0, t;
  }
  return ln = e, ln;
}
var cn, Qu;
function pg() {
  if (Qu) return cn;
  Qu = 1;
  var e = ht(), r = "__lodash_hash_undefined__", t = Object.prototype, n = t.hasOwnProperty;
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
var dn, Yu;
function vg() {
  if (Yu) return dn;
  Yu = 1;
  var e = ht(), r = Object.prototype, t = r.hasOwnProperty;
  function n(a) {
    var i = this.__data__;
    return e ? i[a] !== void 0 : t.call(i, a);
  }
  return dn = n, dn;
}
var fn, Ju;
function gg() {
  if (Ju) return fn;
  Ju = 1;
  var e = ht(), r = "__lodash_hash_undefined__";
  function t(n, a) {
    var i = this.__data__;
    return this.size += this.has(n) ? 0 : 1, i[n] = e && a === void 0 ? r : a, this;
  }
  return fn = t, fn;
}
var hn, Xu;
function yg() {
  if (Xu) return hn;
  Xu = 1;
  var e = fg(), r = hg(), t = pg(), n = vg(), a = gg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, hn = i, hn;
}
var pn, Zu;
function mg() {
  if (Zu) return pn;
  Zu = 1;
  var e = yg(), r = ft(), t = Go();
  function n() {
    this.size = 0, this.__data__ = {
      hash: new e(),
      map: new (t || r)(),
      string: new e()
    };
  }
  return pn = n, pn;
}
var vn, el;
function bg() {
  if (el) return vn;
  el = 1;
  function e(r) {
    var t = typeof r;
    return t == "string" || t == "number" || t == "symbol" || t == "boolean" ? r !== "__proto__" : r === null;
  }
  return vn = e, vn;
}
var gn, rl;
function pt() {
  if (rl) return gn;
  rl = 1;
  var e = bg();
  function r(t, n) {
    var a = t.__data__;
    return e(n) ? a[typeof n == "string" ? "string" : "hash"] : a.map;
  }
  return gn = r, gn;
}
var yn, tl;
function _g() {
  if (tl) return yn;
  tl = 1;
  var e = pt();
  function r(t) {
    var n = e(this, t).delete(t);
    return this.size -= n ? 1 : 0, n;
  }
  return yn = r, yn;
}
var mn, nl;
function wg() {
  if (nl) return mn;
  nl = 1;
  var e = pt();
  function r(t) {
    return e(this, t).get(t);
  }
  return mn = r, mn;
}
var bn, al;
function Ig() {
  if (al) return bn;
  al = 1;
  var e = pt();
  function r(t) {
    return e(this, t).has(t);
  }
  return bn = r, bn;
}
var _n, il;
function qg() {
  if (il) return _n;
  il = 1;
  var e = pt();
  function r(t, n) {
    var a = e(this, t), i = a.size;
    return a.set(t, n), this.size += a.size == i ? 0 : 1, this;
  }
  return _n = r, _n;
}
var wn, sl;
function zo() {
  if (sl) return wn;
  sl = 1;
  var e = mg(), r = _g(), t = wg(), n = Ig(), a = qg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, wn = i, wn;
}
var In, ol;
function Cg() {
  if (ol) return In;
  ol = 1;
  var e = ft(), r = Go(), t = zo(), n = 200;
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
  return In = a, In;
}
var qn, ul;
function vt() {
  if (ul) return qn;
  ul = 1;
  var e = ft(), r = tg(), t = ng(), n = ag(), a = ig(), i = Cg();
  function s(o) {
    var u = this.__data__ = new e(o);
    this.size = u.size;
  }
  return s.prototype.clear = r, s.prototype.delete = t, s.prototype.get = n, s.prototype.has = a, s.prototype.set = i, qn = s, qn;
}
var Cn, ll;
function Bo() {
  if (ll) return Cn;
  ll = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a && t(r[n], n, r) !== !1; )
      ;
    return r;
  }
  return Cn = e, Cn;
}
var En, cl;
function jh() {
  if (cl) return En;
  cl = 1;
  var e = Cr(), r = function() {
    try {
      var t = e(Object, "defineProperty");
      return t({}, "", {}), t;
    } catch {
    }
  }();
  return En = r, En;
}
var Sn, dl;
function gt() {
  if (dl) return Sn;
  dl = 1;
  var e = jh();
  function r(t, n, a) {
    n == "__proto__" && e ? e(t, n, {
      configurable: !0,
      enumerable: !0,
      value: a,
      writable: !0
    }) : t[n] = a;
  }
  return Sn = r, Sn;
}
var Tn, fl;
function yt() {
  if (fl) return Tn;
  fl = 1;
  var e = gt(), r = kr(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o) {
    var u = i[s];
    (!(n.call(i, s) && r(u, o)) || o === void 0 && !(s in i)) && e(i, s, o);
  }
  return Tn = a, Tn;
}
var Rn, hl;
function Hr() {
  if (hl) return Rn;
  hl = 1;
  var e = yt(), r = gt();
  function t(n, a, i, s) {
    var o = !i;
    i || (i = {});
    for (var u = -1, c = a.length; ++u < c; ) {
      var l = a[u], d = s ? s(i[l], n[l], l, i, n) : void 0;
      d === void 0 && (d = n[l]), o ? r(i, l, d) : e(i, l, d);
    }
    return i;
  }
  return Rn = t, Rn;
}
var xn, pl;
function Eg() {
  if (pl) return xn;
  pl = 1;
  function e(r, t) {
    for (var n = -1, a = Array(r); ++n < r; )
      a[n] = t(n);
    return a;
  }
  return xn = e, xn;
}
var kn, vl;
function rr() {
  if (vl) return kn;
  vl = 1;
  function e(r) {
    return r != null && typeof r == "object";
  }
  return kn = e, kn;
}
var An, gl;
function Sg() {
  if (gl) return An;
  gl = 1;
  var e = qr(), r = rr(), t = "[object Arguments]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return An = n, An;
}
var On, yl;
function Wr() {
  if (yl) return On;
  yl = 1;
  var e = Sg(), r = rr(), t = Object.prototype, n = t.hasOwnProperty, a = t.propertyIsEnumerable, i = e(/* @__PURE__ */ function() {
    return arguments;
  }()) ? e : function(s) {
    return r(s) && n.call(s, "callee") && !a.call(s, "callee");
  };
  return On = i, On;
}
var Pn, ml;
function Ie() {
  if (ml) return Pn;
  ml = 1;
  var e = Array.isArray;
  return Pn = e, Pn;
}
var Fr = { exports: {} }, Nn, bl;
function Tg() {
  if (bl) return Nn;
  bl = 1;
  function e() {
    return !1;
  }
  return Nn = e, Nn;
}
Fr.exports;
var _l;
function Or() {
  return _l || (_l = 1, function(e, r) {
    var t = Qe(), n = Tg(), a = r && !r.nodeType && r, i = a && !0 && e && !e.nodeType && e, s = i && i.exports === a, o = s ? t.Buffer : void 0, u = o ? o.isBuffer : void 0, c = u || n;
    e.exports = c;
  }(Fr, Fr.exports)), Fr.exports;
}
var Dn, wl;
function mt() {
  if (wl) return Dn;
  wl = 1;
  var e = 9007199254740991, r = /^(?:0|[1-9]\d*)$/;
  function t(n, a) {
    var i = typeof n;
    return a = a ?? e, !!a && (i == "number" || i != "symbol" && r.test(n)) && n > -1 && n % 1 == 0 && n < a;
  }
  return Dn = t, Dn;
}
var Mn, Il;
function Uo() {
  if (Il) return Mn;
  Il = 1;
  var e = 9007199254740991;
  function r(t) {
    return typeof t == "number" && t > -1 && t % 1 == 0 && t <= e;
  }
  return Mn = r, Mn;
}
var Ln, ql;
function Rg() {
  if (ql) return Ln;
  ql = 1;
  var e = qr(), r = Uo(), t = rr(), n = "[object Arguments]", a = "[object Array]", i = "[object Boolean]", s = "[object Date]", o = "[object Error]", u = "[object Function]", c = "[object Map]", l = "[object Number]", d = "[object Object]", p = "[object RegExp]", v = "[object Set]", g = "[object String]", f = "[object WeakMap]", y = "[object ArrayBuffer]", C = "[object DataView]", R = "[object Float32Array]", _ = "[object Float64Array]", k = "[object Int8Array]", N = "[object Int16Array]", m = "[object Int32Array]", S = "[object Uint8Array]", D = "[object Uint8ClampedArray]", U = "[object Uint16Array]", z = "[object Uint32Array]", I = {};
  I[R] = I[_] = I[k] = I[N] = I[m] = I[S] = I[D] = I[U] = I[z] = !0, I[n] = I[a] = I[y] = I[i] = I[C] = I[s] = I[o] = I[u] = I[c] = I[l] = I[d] = I[p] = I[v] = I[g] = I[f] = !1;
  function M(j) {
    return t(j) && r(j.length) && !!I[e(j)];
  }
  return Ln = M, Ln;
}
var Fn, Cl;
function bt() {
  if (Cl) return Fn;
  Cl = 1;
  function e(r) {
    return function(t) {
      return r(t);
    };
  }
  return Fn = e, Fn;
}
var jr = { exports: {} };
jr.exports;
var El;
function Ko() {
  return El || (El = 1, function(e, r) {
    var t = Lh(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i && t.process, o = function() {
      try {
        var u = a && a.require && a.require("util").types;
        return u || s && s.binding && s.binding("util");
      } catch {
      }
    }();
    e.exports = o;
  }(jr, jr.exports)), jr.exports;
}
var jn, Sl;
function Qr() {
  if (Sl) return jn;
  Sl = 1;
  var e = Rg(), r = bt(), t = Ko(), n = t && t.isTypedArray, a = n ? r(n) : e;
  return jn = a, jn;
}
var $n, Tl;
function $h() {
  if (Tl) return $n;
  Tl = 1;
  var e = Eg(), r = Wr(), t = Ie(), n = Or(), a = mt(), i = Qr(), s = Object.prototype, o = s.hasOwnProperty;
  function u(c, l) {
    var d = t(c), p = !d && r(c), v = !d && !p && n(c), g = !d && !p && !v && i(c), f = d || p || v || g, y = f ? e(c.length, String) : [], C = y.length;
    for (var R in c)
      (l || o.call(c, R)) && !(f && // Safari 9 has enumerable `arguments.length` in strict mode.
      (R == "length" || // Node.js 0.10 has enumerable non-index properties on buffers.
      v && (R == "offset" || R == "parent") || // PhantomJS 2 has enumerable non-index properties on typed arrays.
      g && (R == "buffer" || R == "byteLength" || R == "byteOffset") || // Skip index properties.
      a(R, C))) && y.push(R);
    return y;
  }
  return $n = u, $n;
}
var Gn, Rl;
function _t() {
  if (Rl) return Gn;
  Rl = 1;
  var e = Object.prototype;
  function r(t) {
    var n = t && t.constructor, a = typeof n == "function" && n.prototype || e;
    return t === a;
  }
  return Gn = r, Gn;
}
var zn, xl;
function Gh() {
  if (xl) return zn;
  xl = 1;
  function e(r, t) {
    return function(n) {
      return r(t(n));
    };
  }
  return zn = e, zn;
}
var Bn, kl;
function xg() {
  if (kl) return Bn;
  kl = 1;
  var e = Gh(), r = e(Object.keys, Object);
  return Bn = r, Bn;
}
var Un, Al;
function Vo() {
  if (Al) return Un;
  Al = 1;
  var e = _t(), r = xg(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i) {
    if (!e(i))
      return r(i);
    var s = [];
    for (var o in Object(i))
      n.call(i, o) && o != "constructor" && s.push(o);
    return s;
  }
  return Un = a, Un;
}
var Kn, Ol;
function ur() {
  if (Ol) return Kn;
  Ol = 1;
  var e = Vr(), r = Uo();
  function t(n) {
    return n != null && r(n.length) && !e(n);
  }
  return Kn = t, Kn;
}
var Vn, Pl;
function yr() {
  if (Pl) return Vn;
  Pl = 1;
  var e = $h(), r = Vo(), t = ur();
  function n(a) {
    return t(a) ? e(a) : r(a);
  }
  return Vn = n, Vn;
}
var Hn, Nl;
function kg() {
  if (Nl) return Hn;
  Nl = 1;
  var e = Hr(), r = yr();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return Hn = t, Hn;
}
var Wn, Dl;
function Ag() {
  if (Dl) return Wn;
  Dl = 1;
  function e(r) {
    var t = [];
    if (r != null)
      for (var n in Object(r))
        t.push(n);
    return t;
  }
  return Wn = e, Wn;
}
var Qn, Ml;
function Og() {
  if (Ml) return Qn;
  Ml = 1;
  var e = ze(), r = _t(), t = Ag(), n = Object.prototype, a = n.hasOwnProperty;
  function i(s) {
    if (!e(s))
      return t(s);
    var o = r(s), u = [];
    for (var c in s)
      c == "constructor" && (o || !a.call(s, c)) || u.push(c);
    return u;
  }
  return Qn = i, Qn;
}
var Yn, Ll;
function Er() {
  if (Ll) return Yn;
  Ll = 1;
  var e = $h(), r = Og(), t = ur();
  function n(a) {
    return t(a) ? e(a, !0) : r(a);
  }
  return Yn = n, Yn;
}
var Jn, Fl;
function Pg() {
  if (Fl) return Jn;
  Fl = 1;
  var e = Hr(), r = Er();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return Jn = t, Jn;
}
var $r = { exports: {} };
$r.exports;
var jl;
function zh() {
  return jl || (jl = 1, function(e, r) {
    var t = Qe(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i ? t.Buffer : void 0, o = s ? s.allocUnsafe : void 0;
    function u(c, l) {
      if (l)
        return c.slice();
      var d = c.length, p = o ? o(d) : new c.constructor(d);
      return c.copy(p), p;
    }
    e.exports = u;
  }($r, $r.exports)), $r.exports;
}
var Xn, $l;
function Bh() {
  if ($l) return Xn;
  $l = 1;
  function e(r, t) {
    var n = -1, a = r.length;
    for (t || (t = Array(a)); ++n < a; )
      t[n] = r[n];
    return t;
  }
  return Xn = e, Xn;
}
var Zn, Gl;
function Uh() {
  if (Gl) return Zn;
  Gl = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = 0, s = []; ++n < a; ) {
      var o = r[n];
      t(o, n, r) && (s[i++] = o);
    }
    return s;
  }
  return Zn = e, Zn;
}
var ea, zl;
function Kh() {
  if (zl) return ea;
  zl = 1;
  function e() {
    return [];
  }
  return ea = e, ea;
}
var ra, Bl;
function Ho() {
  if (Bl) return ra;
  Bl = 1;
  var e = Uh(), r = Kh(), t = Object.prototype, n = t.propertyIsEnumerable, a = Object.getOwnPropertySymbols, i = a ? function(s) {
    return s == null ? [] : (s = Object(s), e(a(s), function(o) {
      return n.call(s, o);
    }));
  } : r;
  return ra = i, ra;
}
var ta, Ul;
function Ng() {
  if (Ul) return ta;
  Ul = 1;
  var e = Hr(), r = Ho();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return ta = t, ta;
}
var na, Kl;
function Wo() {
  if (Kl) return na;
  Kl = 1;
  function e(r, t) {
    for (var n = -1, a = t.length, i = r.length; ++n < a; )
      r[i + n] = t[n];
    return r;
  }
  return na = e, na;
}
var aa, Vl;
function wt() {
  if (Vl) return aa;
  Vl = 1;
  var e = Gh(), r = e(Object.getPrototypeOf, Object);
  return aa = r, aa;
}
var ia, Hl;
function Vh() {
  if (Hl) return ia;
  Hl = 1;
  var e = Wo(), r = wt(), t = Ho(), n = Kh(), a = Object.getOwnPropertySymbols, i = a ? function(s) {
    for (var o = []; s; )
      e(o, t(s)), s = r(s);
    return o;
  } : n;
  return ia = i, ia;
}
var sa, Wl;
function Dg() {
  if (Wl) return sa;
  Wl = 1;
  var e = Hr(), r = Vh();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return sa = t, sa;
}
var oa, Ql;
function Hh() {
  if (Ql) return oa;
  Ql = 1;
  var e = Wo(), r = Ie();
  function t(n, a, i) {
    var s = a(n);
    return r(n) ? s : e(s, i(n));
  }
  return oa = t, oa;
}
var ua, Yl;
function Wh() {
  if (Yl) return ua;
  Yl = 1;
  var e = Hh(), r = Ho(), t = yr();
  function n(a) {
    return e(a, t, r);
  }
  return ua = n, ua;
}
var la, Jl;
function Mg() {
  if (Jl) return la;
  Jl = 1;
  var e = Hh(), r = Vh(), t = Er();
  function n(a) {
    return e(a, t, r);
  }
  return la = n, la;
}
var ca, Xl;
function Lg() {
  if (Xl) return ca;
  Xl = 1;
  var e = Cr(), r = Qe(), t = e(r, "DataView");
  return ca = t, ca;
}
var da, Zl;
function Fg() {
  if (Zl) return da;
  Zl = 1;
  var e = Cr(), r = Qe(), t = e(r, "Promise");
  return da = t, da;
}
var fa, ec;
function Qh() {
  if (ec) return fa;
  ec = 1;
  var e = Cr(), r = Qe(), t = e(r, "Set");
  return fa = t, fa;
}
var ha, rc;
function jg() {
  if (rc) return ha;
  rc = 1;
  var e = Cr(), r = Qe(), t = e(r, "WeakMap");
  return ha = t, ha;
}
var pa, tc;
function Pr() {
  if (tc) return pa;
  tc = 1;
  var e = Lg(), r = Go(), t = Fg(), n = Qh(), a = jg(), i = qr(), s = Fh(), o = "[object Map]", u = "[object Object]", c = "[object Promise]", l = "[object Set]", d = "[object WeakMap]", p = "[object DataView]", v = s(e), g = s(r), f = s(t), y = s(n), C = s(a), R = i;
  return (e && R(new e(new ArrayBuffer(1))) != p || r && R(new r()) != o || t && R(t.resolve()) != c || n && R(new n()) != l || a && R(new a()) != d) && (R = function(_) {
    var k = i(_), N = k == u ? _.constructor : void 0, m = N ? s(N) : "";
    if (m)
      switch (m) {
        case v:
          return p;
        case g:
          return o;
        case f:
          return c;
        case y:
          return l;
        case C:
          return d;
      }
    return k;
  }), pa = R, pa;
}
var va, nc;
function $g() {
  if (nc) return va;
  nc = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n) {
    var a = n.length, i = new n.constructor(a);
    return a && typeof n[0] == "string" && r.call(n, "index") && (i.index = n.index, i.input = n.input), i;
  }
  return va = t, va;
}
var ga, ac;
function Yh() {
  if (ac) return ga;
  ac = 1;
  var e = Qe(), r = e.Uint8Array;
  return ga = r, ga;
}
var ya, ic;
function Qo() {
  if (ic) return ya;
  ic = 1;
  var e = Yh();
  function r(t) {
    var n = new t.constructor(t.byteLength);
    return new e(n).set(new e(t)), n;
  }
  return ya = r, ya;
}
var ma, sc;
function Gg() {
  if (sc) return ma;
  sc = 1;
  var e = Qo();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.byteLength);
  }
  return ma = r, ma;
}
var ba, oc;
function zg() {
  if (oc) return ba;
  oc = 1;
  var e = /\w*$/;
  function r(t) {
    var n = new t.constructor(t.source, e.exec(t));
    return n.lastIndex = t.lastIndex, n;
  }
  return ba = r, ba;
}
var _a, uc;
function Bg() {
  if (uc) return _a;
  uc = 1;
  var e = Ar(), r = e ? e.prototype : void 0, t = r ? r.valueOf : void 0;
  function n(a) {
    return t ? Object(t.call(a)) : {};
  }
  return _a = n, _a;
}
var wa, lc;
function Jh() {
  if (lc) return wa;
  lc = 1;
  var e = Qo();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.length);
  }
  return wa = r, wa;
}
var Ia, cc;
function Ug() {
  if (cc) return Ia;
  cc = 1;
  var e = Qo(), r = Gg(), t = zg(), n = Bg(), a = Jh(), i = "[object Boolean]", s = "[object Date]", o = "[object Map]", u = "[object Number]", c = "[object RegExp]", l = "[object Set]", d = "[object String]", p = "[object Symbol]", v = "[object ArrayBuffer]", g = "[object DataView]", f = "[object Float32Array]", y = "[object Float64Array]", C = "[object Int8Array]", R = "[object Int16Array]", _ = "[object Int32Array]", k = "[object Uint8Array]", N = "[object Uint8ClampedArray]", m = "[object Uint16Array]", S = "[object Uint32Array]";
  function D(U, z, I) {
    var M = U.constructor;
    switch (z) {
      case v:
        return e(U);
      case i:
      case s:
        return new M(+U);
      case g:
        return r(U, I);
      case f:
      case y:
      case C:
      case R:
      case _:
      case k:
      case N:
      case m:
      case S:
        return a(U, I);
      case o:
        return new M();
      case u:
      case d:
        return new M(U);
      case c:
        return t(U);
      case l:
        return new M();
      case p:
        return n(U);
    }
  }
  return Ia = D, Ia;
}
var qa, dc;
function Xh() {
  if (dc) return qa;
  dc = 1;
  var e = ze(), r = Object.create, t = /* @__PURE__ */ function() {
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
  return qa = t, qa;
}
var Ca, fc;
function Zh() {
  if (fc) return Ca;
  fc = 1;
  var e = Xh(), r = wt(), t = _t();
  function n(a) {
    return typeof a.constructor == "function" && !t(a) ? e(r(a)) : {};
  }
  return Ca = n, Ca;
}
var Ea, hc;
function Kg() {
  if (hc) return Ea;
  hc = 1;
  var e = Pr(), r = rr(), t = "[object Map]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return Ea = n, Ea;
}
var Sa, pc;
function Vg() {
  if (pc) return Sa;
  pc = 1;
  var e = Kg(), r = bt(), t = Ko(), n = t && t.isMap, a = n ? r(n) : e;
  return Sa = a, Sa;
}
var Ta, vc;
function Hg() {
  if (vc) return Ta;
  vc = 1;
  var e = Pr(), r = rr(), t = "[object Set]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return Ta = n, Ta;
}
var Ra, gc;
function Wg() {
  if (gc) return Ra;
  gc = 1;
  var e = Hg(), r = bt(), t = Ko(), n = t && t.isSet, a = n ? r(n) : e;
  return Ra = a, Ra;
}
var xa, yc;
function ep() {
  if (yc) return xa;
  yc = 1;
  var e = vt(), r = Bo(), t = yt(), n = kg(), a = Pg(), i = zh(), s = Bh(), o = Ng(), u = Dg(), c = Wh(), l = Mg(), d = Pr(), p = $g(), v = Ug(), g = Zh(), f = Ie(), y = Or(), C = Vg(), R = ze(), _ = Wg(), k = yr(), N = Er(), m = 1, S = 2, D = 4, U = "[object Arguments]", z = "[object Array]", I = "[object Boolean]", M = "[object Date]", j = "[object Error]", J = "[object Function]", re = "[object GeneratorFunction]", K = "[object Map]", Y = "[object Number]", W = "[object Object]", x = "[object RegExp]", $ = "[object Set]", P = "[object String]", ne = "[object Symbol]", le = "[object WeakMap]", Ue = "[object ArrayBuffer]", Te = "[object DataView]", mr = "[object Float32Array]", Dr = "[object Float64Array]", br = "[object Int8Array]", Rr = "[object Int16Array]", _r = "[object Int32Array]", ue = "[object Uint8Array]", Ke = "[object Uint8ClampedArray]", ce = "[object Uint16Array]", he = "[object Uint32Array]", ee = {};
  ee[U] = ee[z] = ee[Ue] = ee[Te] = ee[I] = ee[M] = ee[mr] = ee[Dr] = ee[br] = ee[Rr] = ee[_r] = ee[K] = ee[Y] = ee[W] = ee[x] = ee[$] = ee[P] = ee[ne] = ee[ue] = ee[Ke] = ee[ce] = ee[he] = !0, ee[j] = ee[J] = ee[le] = !1;
  function me(Z, Re, Ve, cr, Je, $e) {
    var qe, se = Re & m, He = Re & S, dr = Re & D;
    if (Ve && (qe = Je ? Ve(Z, cr, Je, $e) : Ve(Z)), qe !== void 0)
      return qe;
    if (!R(Z))
      return Z;
    var w = f(Z);
    if (w) {
      if (qe = p(Z), !se)
        return s(Z, qe);
    } else {
      var B = d(Z), de = B == J || B == re;
      if (y(Z))
        return i(Z, se);
      if (B == W || B == U || de && !Je) {
        if (qe = He || de ? {} : g(Z), !se)
          return He ? u(Z, a(qe, Z)) : o(Z, n(qe, Z));
      } else {
        if (!ee[B])
          return Je ? Z : {};
        qe = v(Z, B, se);
      }
    }
    $e || ($e = new e());
    var Ce = $e.get(Z);
    if (Ce)
      return Ce;
    $e.set(Z, qe), _(Z) ? Z.forEach(function(Ge) {
      qe.add(me(Ge, Re, Ve, Ge, Z, $e));
    }) : C(Z) && Z.forEach(function(Ge, tr) {
      qe.set(tr, me(Ge, Re, Ve, tr, Z, $e));
    });
    var xe = dr ? He ? l : c : He ? N : k, We = w ? void 0 : xe(Z);
    return r(We || Z, function(Ge, tr) {
      We && (tr = Ge, Ge = Z[tr]), t(qe, tr, me(Ge, Re, Ve, tr, Z, $e));
    }), qe;
  }
  return xa = me, xa;
}
var ka, mc;
function Qg() {
  if (mc) return ka;
  mc = 1;
  var e = ep(), r = 4;
  function t(n) {
    return e(n, r);
  }
  return ka = t, ka;
}
var Aa, bc;
function Yo() {
  if (bc) return Aa;
  bc = 1;
  function e(r) {
    return function() {
      return r;
    };
  }
  return Aa = e, Aa;
}
var Oa, _c;
function Yg() {
  if (_c) return Oa;
  _c = 1;
  function e(r) {
    return function(t, n, a) {
      for (var i = -1, s = Object(t), o = a(t), u = o.length; u--; ) {
        var c = o[r ? u : ++i];
        if (n(s[c], c, s) === !1)
          break;
      }
      return t;
    };
  }
  return Oa = e, Oa;
}
var Pa, wc;
function Jo() {
  if (wc) return Pa;
  wc = 1;
  var e = Yg(), r = e();
  return Pa = r, Pa;
}
var Na, Ic;
function Xo() {
  if (Ic) return Na;
  Ic = 1;
  var e = Jo(), r = yr();
  function t(n, a) {
    return n && e(n, a, r);
  }
  return Na = t, Na;
}
var Da, qc;
function Jg() {
  if (qc) return Da;
  qc = 1;
  var e = ur();
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
  return Da = r, Da;
}
var Ma, Cc;
function It() {
  if (Cc) return Ma;
  Cc = 1;
  var e = Xo(), r = Jg(), t = r(e);
  return Ma = t, Ma;
}
var La, Ec;
function Sr() {
  if (Ec) return La;
  Ec = 1;
  function e(r) {
    return r;
  }
  return La = e, La;
}
var Fa, Sc;
function rp() {
  if (Sc) return Fa;
  Sc = 1;
  var e = Sr();
  function r(t) {
    return typeof t == "function" ? t : e;
  }
  return Fa = r, Fa;
}
var ja, Tc;
function tp() {
  if (Tc) return ja;
  Tc = 1;
  var e = Bo(), r = It(), t = rp(), n = Ie();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s));
  }
  return ja = a, ja;
}
var $a, Rc;
function np() {
  return Rc || (Rc = 1, $a = tp()), $a;
}
var Ga, xc;
function Xg() {
  if (xc) return Ga;
  xc = 1;
  var e = It();
  function r(t, n) {
    var a = [];
    return e(t, function(i, s, o) {
      n(i, s, o) && a.push(i);
    }), a;
  }
  return Ga = r, Ga;
}
var za, kc;
function Zg() {
  if (kc) return za;
  kc = 1;
  var e = "__lodash_hash_undefined__";
  function r(t) {
    return this.__data__.set(t, e), this;
  }
  return za = r, za;
}
var Ba, Ac;
function ey() {
  if (Ac) return Ba;
  Ac = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return Ba = e, Ba;
}
var Ua, Oc;
function ap() {
  if (Oc) return Ua;
  Oc = 1;
  var e = zo(), r = Zg(), t = ey();
  function n(a) {
    var i = -1, s = a == null ? 0 : a.length;
    for (this.__data__ = new e(); ++i < s; )
      this.add(a[i]);
  }
  return n.prototype.add = n.prototype.push = r, n.prototype.has = t, Ua = n, Ua;
}
var Ka, Pc;
function ry() {
  if (Pc) return Ka;
  Pc = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a; )
      if (t(r[n], n, r))
        return !0;
    return !1;
  }
  return Ka = e, Ka;
}
var Va, Nc;
function ip() {
  if (Nc) return Va;
  Nc = 1;
  function e(r, t) {
    return r.has(t);
  }
  return Va = e, Va;
}
var Ha, Dc;
function sp() {
  if (Dc) return Ha;
  Dc = 1;
  var e = ap(), r = ry(), t = ip(), n = 1, a = 2;
  function i(s, o, u, c, l, d) {
    var p = u & n, v = s.length, g = o.length;
    if (v != g && !(p && g > v))
      return !1;
    var f = d.get(s), y = d.get(o);
    if (f && y)
      return f == o && y == s;
    var C = -1, R = !0, _ = u & a ? new e() : void 0;
    for (d.set(s, o), d.set(o, s); ++C < v; ) {
      var k = s[C], N = o[C];
      if (c)
        var m = p ? c(N, k, C, o, s, d) : c(k, N, C, s, o, d);
      if (m !== void 0) {
        if (m)
          continue;
        R = !1;
        break;
      }
      if (_) {
        if (!r(o, function(S, D) {
          if (!t(_, D) && (k === S || l(k, S, u, c, d)))
            return _.push(D);
        })) {
          R = !1;
          break;
        }
      } else if (!(k === N || l(k, N, u, c, d))) {
        R = !1;
        break;
      }
    }
    return d.delete(s), d.delete(o), R;
  }
  return Ha = i, Ha;
}
var Wa, Mc;
function ty() {
  if (Mc) return Wa;
  Mc = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a, i) {
      n[++t] = [i, a];
    }), n;
  }
  return Wa = e, Wa;
}
var Qa, Lc;
function Zo() {
  if (Lc) return Qa;
  Lc = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a) {
      n[++t] = a;
    }), n;
  }
  return Qa = e, Qa;
}
var Ya, Fc;
function ny() {
  if (Fc) return Ya;
  Fc = 1;
  var e = Ar(), r = Yh(), t = kr(), n = sp(), a = ty(), i = Zo(), s = 1, o = 2, u = "[object Boolean]", c = "[object Date]", l = "[object Error]", d = "[object Map]", p = "[object Number]", v = "[object RegExp]", g = "[object Set]", f = "[object String]", y = "[object Symbol]", C = "[object ArrayBuffer]", R = "[object DataView]", _ = e ? e.prototype : void 0, k = _ ? _.valueOf : void 0;
  function N(m, S, D, U, z, I, M) {
    switch (D) {
      case R:
        if (m.byteLength != S.byteLength || m.byteOffset != S.byteOffset)
          return !1;
        m = m.buffer, S = S.buffer;
      case C:
        return !(m.byteLength != S.byteLength || !I(new r(m), new r(S)));
      case u:
      case c:
      case p:
        return t(+m, +S);
      case l:
        return m.name == S.name && m.message == S.message;
      case v:
      case f:
        return m == S + "";
      case d:
        var j = a;
      case g:
        var J = U & s;
        if (j || (j = i), m.size != S.size && !J)
          return !1;
        var re = M.get(m);
        if (re)
          return re == S;
        U |= o, M.set(m, S);
        var K = n(j(m), j(S), U, z, I, M);
        return M.delete(m), K;
      case y:
        if (k)
          return k.call(m) == k.call(S);
    }
    return !1;
  }
  return Ya = N, Ya;
}
var Ja, jc;
function ay() {
  if (jc) return Ja;
  jc = 1;
  var e = Wh(), r = 1, t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o, u, c, l) {
    var d = o & r, p = e(i), v = p.length, g = e(s), f = g.length;
    if (v != f && !d)
      return !1;
    for (var y = v; y--; ) {
      var C = p[y];
      if (!(d ? C in s : n.call(s, C)))
        return !1;
    }
    var R = l.get(i), _ = l.get(s);
    if (R && _)
      return R == s && _ == i;
    var k = !0;
    l.set(i, s), l.set(s, i);
    for (var N = d; ++y < v; ) {
      C = p[y];
      var m = i[C], S = s[C];
      if (u)
        var D = d ? u(S, m, C, s, i, l) : u(m, S, C, i, s, l);
      if (!(D === void 0 ? m === S || c(m, S, o, u, l) : D)) {
        k = !1;
        break;
      }
      N || (N = C == "constructor");
    }
    if (k && !N) {
      var U = i.constructor, z = s.constructor;
      U != z && "constructor" in i && "constructor" in s && !(typeof U == "function" && U instanceof U && typeof z == "function" && z instanceof z) && (k = !1);
    }
    return l.delete(i), l.delete(s), k;
  }
  return Ja = a, Ja;
}
var Xa, $c;
function iy() {
  if ($c) return Xa;
  $c = 1;
  var e = vt(), r = sp(), t = ny(), n = ay(), a = Pr(), i = Ie(), s = Or(), o = Qr(), u = 1, c = "[object Arguments]", l = "[object Array]", d = "[object Object]", p = Object.prototype, v = p.hasOwnProperty;
  function g(f, y, C, R, _, k) {
    var N = i(f), m = i(y), S = N ? l : a(f), D = m ? l : a(y);
    S = S == c ? d : S, D = D == c ? d : D;
    var U = S == d, z = D == d, I = S == D;
    if (I && s(f)) {
      if (!s(y))
        return !1;
      N = !0, U = !1;
    }
    if (I && !U)
      return k || (k = new e()), N || o(f) ? r(f, y, C, R, _, k) : t(f, y, S, C, R, _, k);
    if (!(C & u)) {
      var M = U && v.call(f, "__wrapped__"), j = z && v.call(y, "__wrapped__");
      if (M || j) {
        var J = M ? f.value() : f, re = j ? y.value() : y;
        return k || (k = new e()), _(J, re, C, R, k);
      }
    }
    return I ? (k || (k = new e()), n(f, y, C, R, _, k)) : !1;
  }
  return Xa = g, Xa;
}
var Za, Gc;
function op() {
  if (Gc) return Za;
  Gc = 1;
  var e = iy(), r = rr();
  function t(n, a, i, s, o) {
    return n === a ? !0 : n == null || a == null || !r(n) && !r(a) ? n !== n && a !== a : e(n, a, i, s, t, o);
  }
  return Za = t, Za;
}
var ei, zc;
function sy() {
  if (zc) return ei;
  zc = 1;
  var e = vt(), r = op(), t = 1, n = 2;
  function a(i, s, o, u) {
    var c = o.length, l = c, d = !u;
    if (i == null)
      return !l;
    for (i = Object(i); c--; ) {
      var p = o[c];
      if (d && p[2] ? p[1] !== i[p[0]] : !(p[0] in i))
        return !1;
    }
    for (; ++c < l; ) {
      p = o[c];
      var v = p[0], g = i[v], f = p[1];
      if (d && p[2]) {
        if (g === void 0 && !(v in i))
          return !1;
      } else {
        var y = new e();
        if (u)
          var C = u(g, f, v, i, s, y);
        if (!(C === void 0 ? r(f, g, t | n, u, y) : C))
          return !1;
      }
    }
    return !0;
  }
  return ei = a, ei;
}
var ri, Bc;
function up() {
  if (Bc) return ri;
  Bc = 1;
  var e = ze();
  function r(t) {
    return t === t && !e(t);
  }
  return ri = r, ri;
}
var ti, Uc;
function oy() {
  if (Uc) return ti;
  Uc = 1;
  var e = up(), r = yr();
  function t(n) {
    for (var a = r(n), i = a.length; i--; ) {
      var s = a[i], o = n[s];
      a[i] = [s, o, e(o)];
    }
    return a;
  }
  return ti = t, ti;
}
var ni, Kc;
function lp() {
  if (Kc) return ni;
  Kc = 1;
  function e(r, t) {
    return function(n) {
      return n == null ? !1 : n[r] === t && (t !== void 0 || r in Object(n));
    };
  }
  return ni = e, ni;
}
var ai, Vc;
function uy() {
  if (Vc) return ai;
  Vc = 1;
  var e = sy(), r = oy(), t = lp();
  function n(a) {
    var i = r(a);
    return i.length == 1 && i[0][2] ? t(i[0][0], i[0][1]) : function(s) {
      return s === a || e(s, a, i);
    };
  }
  return ai = n, ai;
}
var ii, Hc;
function Nr() {
  if (Hc) return ii;
  Hc = 1;
  var e = qr(), r = rr(), t = "[object Symbol]";
  function n(a) {
    return typeof a == "symbol" || r(a) && e(a) == t;
  }
  return ii = n, ii;
}
var si, Wc;
function eu() {
  if (Wc) return si;
  Wc = 1;
  var e = Ie(), r = Nr(), t = /\.|\[(?:[^[\]]*|(["'])(?:(?!\1)[^\\]|\\.)*?\1)\]/, n = /^\w*$/;
  function a(i, s) {
    if (e(i))
      return !1;
    var o = typeof i;
    return o == "number" || o == "symbol" || o == "boolean" || i == null || r(i) ? !0 : n.test(i) || !t.test(i) || s != null && i in Object(s);
  }
  return si = a, si;
}
var oi, Qc;
function ly() {
  if (Qc) return oi;
  Qc = 1;
  var e = zo(), r = "Expected a function";
  function t(n, a) {
    if (typeof n != "function" || a != null && typeof a != "function")
      throw new TypeError(r);
    var i = function() {
      var s = arguments, o = a ? a.apply(this, s) : s[0], u = i.cache;
      if (u.has(o))
        return u.get(o);
      var c = n.apply(this, s);
      return i.cache = u.set(o, c) || u, c;
    };
    return i.cache = new (t.Cache || e)(), i;
  }
  return t.Cache = e, oi = t, oi;
}
var ui, Yc;
function cy() {
  if (Yc) return ui;
  Yc = 1;
  var e = ly(), r = 500;
  function t(n) {
    var a = e(n, function(s) {
      return i.size === r && i.clear(), s;
    }), i = a.cache;
    return a;
  }
  return ui = t, ui;
}
var li, Jc;
function dy() {
  if (Jc) return li;
  Jc = 1;
  var e = cy(), r = /[^.[\]]+|\[(?:(-?\d+(?:\.\d+)?)|(["'])((?:(?!\2)[^\\]|\\.)*?)\2)\]|(?=(?:\.|\[\])(?:\.|\[\]|$))/g, t = /\\(\\)?/g, n = e(function(a) {
    var i = [];
    return a.charCodeAt(0) === 46 && i.push(""), a.replace(r, function(s, o, u, c) {
      i.push(u ? c.replace(t, "$1") : o || s);
    }), i;
  });
  return li = n, li;
}
var ci, Xc;
function qt() {
  if (Xc) return ci;
  Xc = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = Array(a); ++n < a; )
      i[n] = t(r[n], n, r);
    return i;
  }
  return ci = e, ci;
}
var di, Zc;
function fy() {
  if (Zc) return di;
  Zc = 1;
  var e = Ar(), r = qt(), t = Ie(), n = Nr(), a = e ? e.prototype : void 0, i = a ? a.toString : void 0;
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
  return di = s, di;
}
var fi, ed;
function cp() {
  if (ed) return fi;
  ed = 1;
  var e = fy();
  function r(t) {
    return t == null ? "" : e(t);
  }
  return fi = r, fi;
}
var hi, rd;
function Ct() {
  if (rd) return hi;
  rd = 1;
  var e = Ie(), r = eu(), t = dy(), n = cp();
  function a(i, s) {
    return e(i) ? i : r(i, s) ? [i] : t(n(i));
  }
  return hi = a, hi;
}
var pi, td;
function Yr() {
  if (td) return pi;
  td = 1;
  var e = Nr();
  function r(t) {
    if (typeof t == "string" || e(t))
      return t;
    var n = t + "";
    return n == "0" && 1 / t == -1 / 0 ? "-0" : n;
  }
  return pi = r, pi;
}
var vi, nd;
function Et() {
  if (nd) return vi;
  nd = 1;
  var e = Ct(), r = Yr();
  function t(n, a) {
    a = e(a, n);
    for (var i = 0, s = a.length; n != null && i < s; )
      n = n[r(a[i++])];
    return i && i == s ? n : void 0;
  }
  return vi = t, vi;
}
var gi, ad;
function hy() {
  if (ad) return gi;
  ad = 1;
  var e = Et();
  function r(t, n, a) {
    var i = t == null ? void 0 : e(t, n);
    return i === void 0 ? a : i;
  }
  return gi = r, gi;
}
var yi, id;
function py() {
  if (id) return yi;
  id = 1;
  function e(r, t) {
    return r != null && t in Object(r);
  }
  return yi = e, yi;
}
var mi, sd;
function dp() {
  if (sd) return mi;
  sd = 1;
  var e = Ct(), r = Wr(), t = Ie(), n = mt(), a = Uo(), i = Yr();
  function s(o, u, c) {
    u = e(u, o);
    for (var l = -1, d = u.length, p = !1; ++l < d; ) {
      var v = i(u[l]);
      if (!(p = o != null && c(o, v)))
        break;
      o = o[v];
    }
    return p || ++l != d ? p : (d = o == null ? 0 : o.length, !!d && a(d) && n(v, d) && (t(o) || r(o)));
  }
  return mi = s, mi;
}
var bi, od;
function fp() {
  if (od) return bi;
  od = 1;
  var e = py(), r = dp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return bi = t, bi;
}
var _i, ud;
function vy() {
  if (ud) return _i;
  ud = 1;
  var e = op(), r = hy(), t = fp(), n = eu(), a = up(), i = lp(), s = Yr(), o = 1, u = 2;
  function c(l, d) {
    return n(l) && a(d) ? i(s(l), d) : function(p) {
      var v = r(p, l);
      return v === void 0 && v === d ? t(p, l) : e(d, v, o | u);
    };
  }
  return _i = c, _i;
}
var wi, ld;
function hp() {
  if (ld) return wi;
  ld = 1;
  function e(r) {
    return function(t) {
      return t == null ? void 0 : t[r];
    };
  }
  return wi = e, wi;
}
var Ii, cd;
function gy() {
  if (cd) return Ii;
  cd = 1;
  var e = Et();
  function r(t) {
    return function(n) {
      return e(n, t);
    };
  }
  return Ii = r, Ii;
}
var qi, dd;
function yy() {
  if (dd) return qi;
  dd = 1;
  var e = hp(), r = gy(), t = eu(), n = Yr();
  function a(i) {
    return t(i) ? e(n(i)) : r(i);
  }
  return qi = a, qi;
}
var Ci, fd;
function lr() {
  if (fd) return Ci;
  fd = 1;
  var e = uy(), r = vy(), t = Sr(), n = Ie(), a = yy();
  function i(s) {
    return typeof s == "function" ? s : s == null ? t : typeof s == "object" ? n(s) ? r(s[0], s[1]) : e(s) : a(s);
  }
  return Ci = i, Ci;
}
var Ei, hd;
function pp() {
  if (hd) return Ei;
  hd = 1;
  var e = Uh(), r = Xg(), t = lr(), n = Ie();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s, 3));
  }
  return Ei = a, Ei;
}
var Si, pd;
function my() {
  if (pd) return Si;
  pd = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n, a) {
    return n != null && r.call(n, a);
  }
  return Si = t, Si;
}
var Ti, vd;
function vp() {
  if (vd) return Ti;
  vd = 1;
  var e = my(), r = dp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return Ti = t, Ti;
}
var Ri, gd;
function by() {
  if (gd) return Ri;
  gd = 1;
  var e = Vo(), r = Pr(), t = Wr(), n = Ie(), a = ur(), i = Or(), s = _t(), o = Qr(), u = "[object Map]", c = "[object Set]", l = Object.prototype, d = l.hasOwnProperty;
  function p(v) {
    if (v == null)
      return !0;
    if (a(v) && (n(v) || typeof v == "string" || typeof v.splice == "function" || i(v) || o(v) || t(v)))
      return !v.length;
    var g = r(v);
    if (g == u || g == c)
      return !v.size;
    if (s(v))
      return !e(v).length;
    for (var f in v)
      if (d.call(v, f))
        return !1;
    return !0;
  }
  return Ri = p, Ri;
}
var xi, yd;
function gp() {
  if (yd) return xi;
  yd = 1;
  function e(r) {
    return r === void 0;
  }
  return xi = e, xi;
}
var ki, md;
function yp() {
  if (md) return ki;
  md = 1;
  var e = It(), r = ur();
  function t(n, a) {
    var i = -1, s = r(n) ? Array(n.length) : [];
    return e(n, function(o, u, c) {
      s[++i] = a(o, u, c);
    }), s;
  }
  return ki = t, ki;
}
var Ai, bd;
function mp() {
  if (bd) return Ai;
  bd = 1;
  var e = qt(), r = lr(), t = yp(), n = Ie();
  function a(i, s) {
    var o = n(i) ? e : t;
    return o(i, r(s, 3));
  }
  return Ai = a, Ai;
}
var Oi, _d;
function _y() {
  if (_d) return Oi;
  _d = 1;
  function e(r, t, n, a) {
    var i = -1, s = r == null ? 0 : r.length;
    for (a && s && (n = r[++i]); ++i < s; )
      n = t(n, r[i], i, r);
    return n;
  }
  return Oi = e, Oi;
}
var Pi, wd;
function wy() {
  if (wd) return Pi;
  wd = 1;
  function e(r, t, n, a, i) {
    return i(r, function(s, o, u) {
      n = a ? (a = !1, s) : t(n, s, o, u);
    }), n;
  }
  return Pi = e, Pi;
}
var Ni, Id;
function bp() {
  if (Id) return Ni;
  Id = 1;
  var e = _y(), r = It(), t = lr(), n = wy(), a = Ie();
  function i(s, o, u) {
    var c = a(s) ? e : n, l = arguments.length < 3;
    return c(s, t(o, 4), u, l, r);
  }
  return Ni = i, Ni;
}
var Di, qd;
function Iy() {
  if (qd) return Di;
  qd = 1;
  var e = qr(), r = Ie(), t = rr(), n = "[object String]";
  function a(i) {
    return typeof i == "string" || !r(i) && t(i) && e(i) == n;
  }
  return Di = a, Di;
}
var Mi, Cd;
function qy() {
  if (Cd) return Mi;
  Cd = 1;
  var e = hp(), r = e("length");
  return Mi = r, Mi;
}
var Li, Ed;
function Cy() {
  if (Ed) return Li;
  Ed = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "\\u200d", o = RegExp("[" + s + e + a + i + "]");
  function u(c) {
    return o.test(c);
  }
  return Li = u, Li;
}
var Fi, Sd;
function Ey() {
  if (Sd) return Fi;
  Sd = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "[" + e + "]", o = "[" + a + "]", u = "\\ud83c[\\udffb-\\udfff]", c = "(?:" + o + "|" + u + ")", l = "[^" + e + "]", d = "(?:\\ud83c[\\udde6-\\uddff]){2}", p = "[\\ud800-\\udbff][\\udc00-\\udfff]", v = "\\u200d", g = c + "?", f = "[" + i + "]?", y = "(?:" + v + "(?:" + [l, d, p].join("|") + ")" + f + g + ")*", C = f + g + y, R = "(?:" + [l + o + "?", o, d, p, s].join("|") + ")", _ = RegExp(u + "(?=" + u + ")|" + R + C, "g");
  function k(N) {
    for (var m = _.lastIndex = 0; _.test(N); )
      ++m;
    return m;
  }
  return Fi = k, Fi;
}
var ji, Td;
function Sy() {
  if (Td) return ji;
  Td = 1;
  var e = qy(), r = Cy(), t = Ey();
  function n(a) {
    return r(a) ? t(a) : e(a);
  }
  return ji = n, ji;
}
var $i, Rd;
function Ty() {
  if (Rd) return $i;
  Rd = 1;
  var e = Vo(), r = Pr(), t = ur(), n = Iy(), a = Sy(), i = "[object Map]", s = "[object Set]";
  function o(u) {
    if (u == null)
      return 0;
    if (t(u))
      return n(u) ? a(u) : u.length;
    var c = r(u);
    return c == i || c == s ? u.size : e(u).length;
  }
  return $i = o, $i;
}
var Gi, xd;
function Ry() {
  if (xd) return Gi;
  xd = 1;
  var e = Bo(), r = Xh(), t = Xo(), n = lr(), a = wt(), i = Ie(), s = Or(), o = Vr(), u = ze(), c = Qr();
  function l(d, p, v) {
    var g = i(d), f = g || s(d) || c(d);
    if (p = n(p, 4), v == null) {
      var y = d && d.constructor;
      f ? v = g ? new y() : [] : u(d) ? v = o(y) ? r(a(d)) : {} : v = {};
    }
    return (f ? e : t)(d, function(C, R, _) {
      return p(v, C, R, _);
    }), v;
  }
  return Gi = l, Gi;
}
var zi, kd;
function xy() {
  if (kd) return zi;
  kd = 1;
  var e = Ar(), r = Wr(), t = Ie(), n = e ? e.isConcatSpreadable : void 0;
  function a(i) {
    return t(i) || r(i) || !!(n && i && i[n]);
  }
  return zi = a, zi;
}
var Bi, Ad;
function ru() {
  if (Ad) return Bi;
  Ad = 1;
  var e = Wo(), r = xy();
  function t(n, a, i, s, o) {
    var u = -1, c = n.length;
    for (i || (i = r), o || (o = []); ++u < c; ) {
      var l = n[u];
      a > 0 && i(l) ? a > 1 ? t(l, a - 1, i, s, o) : e(o, l) : s || (o[o.length] = l);
    }
    return o;
  }
  return Bi = t, Bi;
}
var Ui, Od;
function ky() {
  if (Od) return Ui;
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
  return Ui = e, Ui;
}
var Ki, Pd;
function _p() {
  if (Pd) return Ki;
  Pd = 1;
  var e = ky(), r = Math.max;
  function t(n, a, i) {
    return a = r(a === void 0 ? n.length - 1 : a, 0), function() {
      for (var s = arguments, o = -1, u = r(s.length - a, 0), c = Array(u); ++o < u; )
        c[o] = s[a + o];
      o = -1;
      for (var l = Array(a + 1); ++o < a; )
        l[o] = s[o];
      return l[a] = i(c), e(n, this, l);
    };
  }
  return Ki = t, Ki;
}
var Vi, Nd;
function Ay() {
  if (Nd) return Vi;
  Nd = 1;
  var e = Yo(), r = jh(), t = Sr(), n = r ? function(a, i) {
    return r(a, "toString", {
      configurable: !0,
      enumerable: !1,
      value: e(i),
      writable: !0
    });
  } : t;
  return Vi = n, Vi;
}
var Hi, Dd;
function Oy() {
  if (Dd) return Hi;
  Dd = 1;
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
var Wi, Md;
function wp() {
  if (Md) return Wi;
  Md = 1;
  var e = Ay(), r = Oy(), t = r(e);
  return Wi = t, Wi;
}
var Qi, Ld;
function St() {
  if (Ld) return Qi;
  Ld = 1;
  var e = Sr(), r = _p(), t = wp();
  function n(a, i) {
    return t(r(a, i, e), a + "");
  }
  return Qi = n, Qi;
}
var Yi, Fd;
function Ip() {
  if (Fd) return Yi;
  Fd = 1;
  function e(r, t, n, a) {
    for (var i = r.length, s = n + (a ? 1 : -1); a ? s-- : ++s < i; )
      if (t(r[s], s, r))
        return s;
    return -1;
  }
  return Yi = e, Yi;
}
var Ji, jd;
function Py() {
  if (jd) return Ji;
  jd = 1;
  function e(r) {
    return r !== r;
  }
  return Ji = e, Ji;
}
var Xi, $d;
function Ny() {
  if ($d) return Xi;
  $d = 1;
  function e(r, t, n) {
    for (var a = n - 1, i = r.length; ++a < i; )
      if (r[a] === t)
        return a;
    return -1;
  }
  return Xi = e, Xi;
}
var Zi, Gd;
function Dy() {
  if (Gd) return Zi;
  Gd = 1;
  var e = Ip(), r = Py(), t = Ny();
  function n(a, i, s) {
    return i === i ? t(a, i, s) : e(a, r, s);
  }
  return Zi = n, Zi;
}
var es, zd;
function My() {
  if (zd) return es;
  zd = 1;
  var e = Dy();
  function r(t, n) {
    var a = t == null ? 0 : t.length;
    return !!a && e(t, n, 0) > -1;
  }
  return es = r, es;
}
var rs, Bd;
function Ly() {
  if (Bd) return rs;
  Bd = 1;
  function e(r, t, n) {
    for (var a = -1, i = r == null ? 0 : r.length; ++a < i; )
      if (n(t, r[a]))
        return !0;
    return !1;
  }
  return rs = e, rs;
}
var ts, Ud;
function Fy() {
  if (Ud) return ts;
  Ud = 1;
  function e() {
  }
  return ts = e, ts;
}
var ns, Kd;
function jy() {
  if (Kd) return ns;
  Kd = 1;
  var e = Qh(), r = Fy(), t = Zo(), n = 1 / 0, a = e && 1 / t(new e([, -0]))[1] == n ? function(i) {
    return new e(i);
  } : r;
  return ns = a, ns;
}
var as, Vd;
function $y() {
  if (Vd) return as;
  Vd = 1;
  var e = ap(), r = My(), t = Ly(), n = ip(), a = jy(), i = Zo(), s = 200;
  function o(u, c, l) {
    var d = -1, p = r, v = u.length, g = !0, f = [], y = f;
    if (l)
      g = !1, p = t;
    else if (v >= s) {
      var C = c ? null : a(u);
      if (C)
        return i(C);
      g = !1, p = n, y = new e();
    } else
      y = c ? [] : f;
    e:
      for (; ++d < v; ) {
        var R = u[d], _ = c ? c(R) : R;
        if (R = l || R !== 0 ? R : 0, g && _ === _) {
          for (var k = y.length; k--; )
            if (y[k] === _)
              continue e;
          c && y.push(_), f.push(R);
        } else p(y, _, l) || (y !== f && y.push(_), f.push(R));
      }
    return f;
  }
  return as = o, as;
}
var is, Hd;
function qp() {
  if (Hd) return is;
  Hd = 1;
  var e = ur(), r = rr();
  function t(n) {
    return r(n) && e(n);
  }
  return is = t, is;
}
var ss, Wd;
function Gy() {
  if (Wd) return ss;
  Wd = 1;
  var e = ru(), r = St(), t = $y(), n = qp(), a = r(function(i) {
    return t(e(i, 1, n, !0));
  });
  return ss = a, ss;
}
var os, Qd;
function zy() {
  if (Qd) return os;
  Qd = 1;
  var e = qt();
  function r(t, n) {
    return e(n, function(a) {
      return t[a];
    });
  }
  return os = r, os;
}
var us, Yd;
function Cp() {
  if (Yd) return us;
  Yd = 1;
  var e = zy(), r = yr();
  function t(n) {
    return n == null ? [] : e(n, r(n));
  }
  return us = t, us;
}
var ls, Jd;
function Be() {
  if (Jd) return ls;
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
        isArray: Ie(),
        isEmpty: by(),
        isFunction: Vr(),
        isUndefined: gp(),
        keys: yr(),
        map: mp(),
        reduce: bp(),
        size: Ty(),
        transform: Ry(),
        union: Gy(),
        values: Cp()
      };
    } catch {
    }
  return e || (e = window._), ls = e, ls;
}
var cs, Xd;
function tu() {
  if (Xd) return cs;
  Xd = 1;
  var e = Be();
  cs = a;
  var r = "\0", t = "\0", n = "";
  function a(l) {
    this._isDirected = e.has(l, "directed") ? l.directed : !0, this._isMultigraph = e.has(l, "multigraph") ? l.multigraph : !1, this._isCompound = e.has(l, "compound") ? l.compound : !1, this._label = void 0, this._defaultNodeLabelFn = e.constant(void 0), this._defaultEdgeLabelFn = e.constant(void 0), this._nodes = {}, this._isCompound && (this._parent = {}, this._children = {}, this._children[t] = {}), this._in = {}, this._preds = {}, this._out = {}, this._sucs = {}, this._edgeObjs = {}, this._edgeLabels = {};
  }
  a.prototype._nodeCount = 0, a.prototype._edgeCount = 0, a.prototype.isDirected = function() {
    return this._isDirected;
  }, a.prototype.isMultigraph = function() {
    return this._isMultigraph;
  }, a.prototype.isCompound = function() {
    return this._isCompound;
  }, a.prototype.setGraph = function(l) {
    return this._label = l, this;
  }, a.prototype.graph = function() {
    return this._label;
  }, a.prototype.setDefaultNodeLabel = function(l) {
    return e.isFunction(l) || (l = e.constant(l)), this._defaultNodeLabelFn = l, this;
  }, a.prototype.nodeCount = function() {
    return this._nodeCount;
  }, a.prototype.nodes = function() {
    return e.keys(this._nodes);
  }, a.prototype.sources = function() {
    var l = this;
    return e.filter(this.nodes(), function(d) {
      return e.isEmpty(l._in[d]);
    });
  }, a.prototype.sinks = function() {
    var l = this;
    return e.filter(this.nodes(), function(d) {
      return e.isEmpty(l._out[d]);
    });
  }, a.prototype.setNodes = function(l, d) {
    var p = arguments, v = this;
    return e.each(l, function(g) {
      p.length > 1 ? v.setNode(g, d) : v.setNode(g);
    }), this;
  }, a.prototype.setNode = function(l, d) {
    return e.has(this._nodes, l) ? (arguments.length > 1 && (this._nodes[l] = d), this) : (this._nodes[l] = arguments.length > 1 ? d : this._defaultNodeLabelFn(l), this._isCompound && (this._parent[l] = t, this._children[l] = {}, this._children[t][l] = !0), this._in[l] = {}, this._preds[l] = {}, this._out[l] = {}, this._sucs[l] = {}, ++this._nodeCount, this);
  }, a.prototype.node = function(l) {
    return this._nodes[l];
  }, a.prototype.hasNode = function(l) {
    return e.has(this._nodes, l);
  }, a.prototype.removeNode = function(l) {
    var d = this;
    if (e.has(this._nodes, l)) {
      var p = function(v) {
        d.removeEdge(d._edgeObjs[v]);
      };
      delete this._nodes[l], this._isCompound && (this._removeFromParentsChildList(l), delete this._parent[l], e.each(this.children(l), function(v) {
        d.setParent(v);
      }), delete this._children[l]), e.each(e.keys(this._in[l]), p), delete this._in[l], delete this._preds[l], e.each(e.keys(this._out[l]), p), delete this._out[l], delete this._sucs[l], --this._nodeCount;
    }
    return this;
  }, a.prototype.setParent = function(l, d) {
    if (!this._isCompound)
      throw new Error("Cannot set parent in a non-compound graph");
    if (e.isUndefined(d))
      d = t;
    else {
      d += "";
      for (var p = d; !e.isUndefined(p); p = this.parent(p))
        if (p === l)
          throw new Error("Setting " + d + " as parent of " + l + " would create a cycle");
      this.setNode(d);
    }
    return this.setNode(l), this._removeFromParentsChildList(l), this._parent[l] = d, this._children[d][l] = !0, this;
  }, a.prototype._removeFromParentsChildList = function(l) {
    delete this._children[this._parent[l]][l];
  }, a.prototype.parent = function(l) {
    if (this._isCompound) {
      var d = this._parent[l];
      if (d !== t)
        return d;
    }
  }, a.prototype.children = function(l) {
    if (e.isUndefined(l) && (l = t), this._isCompound) {
      var d = this._children[l];
      if (d)
        return e.keys(d);
    } else {
      if (l === t)
        return this.nodes();
      if (this.hasNode(l))
        return [];
    }
  }, a.prototype.predecessors = function(l) {
    var d = this._preds[l];
    if (d)
      return e.keys(d);
  }, a.prototype.successors = function(l) {
    var d = this._sucs[l];
    if (d)
      return e.keys(d);
  }, a.prototype.neighbors = function(l) {
    var d = this.predecessors(l);
    if (d)
      return e.union(d, this.successors(l));
  }, a.prototype.isLeaf = function(l) {
    var d;
    return this.isDirected() ? d = this.successors(l) : d = this.neighbors(l), d.length === 0;
  }, a.prototype.filterNodes = function(l) {
    var d = new this.constructor({
      directed: this._isDirected,
      multigraph: this._isMultigraph,
      compound: this._isCompound
    });
    d.setGraph(this.graph());
    var p = this;
    e.each(this._nodes, function(f, y) {
      l(y) && d.setNode(y, f);
    }), e.each(this._edgeObjs, function(f) {
      d.hasNode(f.v) && d.hasNode(f.w) && d.setEdge(f, p.edge(f));
    });
    var v = {};
    function g(f) {
      var y = p.parent(f);
      return y === void 0 || d.hasNode(y) ? (v[f] = y, y) : y in v ? v[y] : g(y);
    }
    return this._isCompound && e.each(d.nodes(), function(f) {
      d.setParent(f, g(f));
    }), d;
  }, a.prototype.setDefaultEdgeLabel = function(l) {
    return e.isFunction(l) || (l = e.constant(l)), this._defaultEdgeLabelFn = l, this;
  }, a.prototype.edgeCount = function() {
    return this._edgeCount;
  }, a.prototype.edges = function() {
    return e.values(this._edgeObjs);
  }, a.prototype.setPath = function(l, d) {
    var p = this, v = arguments;
    return e.reduce(l, function(g, f) {
      return v.length > 1 ? p.setEdge(g, f, d) : p.setEdge(g, f), f;
    }), this;
  }, a.prototype.setEdge = function() {
    var l, d, p, v, g = !1, f = arguments[0];
    typeof f == "object" && f !== null && "v" in f ? (l = f.v, d = f.w, p = f.name, arguments.length === 2 && (v = arguments[1], g = !0)) : (l = f, d = arguments[1], p = arguments[3], arguments.length > 2 && (v = arguments[2], g = !0)), l = "" + l, d = "" + d, e.isUndefined(p) || (p = "" + p);
    var y = o(this._isDirected, l, d, p);
    if (e.has(this._edgeLabels, y))
      return g && (this._edgeLabels[y] = v), this;
    if (!e.isUndefined(p) && !this._isMultigraph)
      throw new Error("Cannot set a named edge when isMultigraph = false");
    this.setNode(l), this.setNode(d), this._edgeLabels[y] = g ? v : this._defaultEdgeLabelFn(l, d, p);
    var C = u(this._isDirected, l, d, p);
    return l = C.v, d = C.w, Object.freeze(C), this._edgeObjs[y] = C, i(this._preds[d], l), i(this._sucs[l], d), this._in[d][y] = C, this._out[l][y] = C, this._edgeCount++, this;
  }, a.prototype.edge = function(l, d, p) {
    var v = arguments.length === 1 ? c(this._isDirected, arguments[0]) : o(this._isDirected, l, d, p);
    return this._edgeLabels[v];
  }, a.prototype.hasEdge = function(l, d, p) {
    var v = arguments.length === 1 ? c(this._isDirected, arguments[0]) : o(this._isDirected, l, d, p);
    return e.has(this._edgeLabels, v);
  }, a.prototype.removeEdge = function(l, d, p) {
    var v = arguments.length === 1 ? c(this._isDirected, arguments[0]) : o(this._isDirected, l, d, p), g = this._edgeObjs[v];
    return g && (l = g.v, d = g.w, delete this._edgeLabels[v], delete this._edgeObjs[v], s(this._preds[d], l), s(this._sucs[l], d), delete this._in[d][v], delete this._out[l][v], this._edgeCount--), this;
  }, a.prototype.inEdges = function(l, d) {
    var p = this._in[l];
    if (p) {
      var v = e.values(p);
      return d ? e.filter(v, function(g) {
        return g.v === d;
      }) : v;
    }
  }, a.prototype.outEdges = function(l, d) {
    var p = this._out[l];
    if (p) {
      var v = e.values(p);
      return d ? e.filter(v, function(g) {
        return g.w === d;
      }) : v;
    }
  }, a.prototype.nodeEdges = function(l, d) {
    var p = this.inEdges(l, d);
    if (p)
      return p.concat(this.outEdges(l, d));
  };
  function i(l, d) {
    l[d] ? l[d]++ : l[d] = 1;
  }
  function s(l, d) {
    --l[d] || delete l[d];
  }
  function o(l, d, p, v) {
    var g = "" + d, f = "" + p;
    if (!l && g > f) {
      var y = g;
      g = f, f = y;
    }
    return g + n + f + n + (e.isUndefined(v) ? r : v);
  }
  function u(l, d, p, v) {
    var g = "" + d, f = "" + p;
    if (!l && g > f) {
      var y = g;
      g = f, f = y;
    }
    var C = { v: g, w: f };
    return v && (C.name = v), C;
  }
  function c(l, d) {
    return o(l, d.v, d.w, d.name);
  }
  return cs;
}
var ds, Zd;
function By() {
  return Zd || (Zd = 1, ds = "2.1.8"), ds;
}
var fs, ef;
function Uy() {
  return ef || (ef = 1, fs = {
    Graph: tu(),
    version: By()
  }), fs;
}
var hs, rf;
function Ky() {
  if (rf) return hs;
  rf = 1;
  var e = Be(), r = tu();
  hs = {
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
      var u = s.node(o), c = s.parent(o), l = { v: o };
      return e.isUndefined(u) || (l.value = u), e.isUndefined(c) || (l.parent = c), l;
    });
  }
  function a(s) {
    return e.map(s.edges(), function(o) {
      var u = s.edge(o), c = { v: o.v, w: o.w };
      return e.isUndefined(o.name) || (c.name = o.name), e.isUndefined(u) || (c.value = u), c;
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
  return hs;
}
var ps, tf;
function Vy() {
  if (tf) return ps;
  tf = 1;
  var e = Be();
  ps = r;
  function r(t) {
    var n = {}, a = [], i;
    function s(o) {
      e.has(n, o) || (n[o] = !0, i.push(o), e.each(t.successors(o), s), e.each(t.predecessors(o), s));
    }
    return e.each(t.nodes(), function(o) {
      i = [], s(o), i.length && a.push(i);
    }), a;
  }
  return ps;
}
var vs, nf;
function Ep() {
  if (nf) return vs;
  nf = 1;
  var e = Be();
  vs = r;
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
  }, vs;
}
var gs, af;
function Sp() {
  if (af) return gs;
  af = 1;
  var e = Be(), r = Ep();
  gs = n;
  var t = e.constant(1);
  function n(i, s, o, u) {
    return a(
      i,
      String(s),
      o || t,
      u || function(c) {
        return i.outEdges(c);
      }
    );
  }
  function a(i, s, o, u) {
    var c = {}, l = new r(), d, p, v = function(g) {
      var f = g.v !== d ? g.v : g.w, y = c[f], C = o(g), R = p.distance + C;
      if (C < 0)
        throw new Error("dijkstra does not allow negative edge weights. Bad edge: " + g + " Weight: " + C);
      R < y.distance && (y.distance = R, y.predecessor = d, l.decrease(f, R));
    };
    for (i.nodes().forEach(function(g) {
      var f = g === s ? 0 : Number.POSITIVE_INFINITY;
      c[g] = { distance: f }, l.add(g, f);
    }); l.size() > 0 && (d = l.removeMin(), p = c[d], p.distance !== Number.POSITIVE_INFINITY); )
      u(d).forEach(v);
    return c;
  }
  return gs;
}
var ys, sf;
function Hy() {
  if (sf) return ys;
  sf = 1;
  var e = Sp(), r = Be();
  ys = t;
  function t(n, a, i) {
    return r.transform(n.nodes(), function(s, o) {
      s[o] = e(n, o, a, i);
    }, {});
  }
  return ys;
}
var ms, of;
function Tp() {
  if (of) return ms;
  of = 1;
  var e = Be();
  ms = r;
  function r(t) {
    var n = 0, a = [], i = {}, s = [];
    function o(u) {
      var c = i[u] = {
        onStack: !0,
        lowlink: n,
        index: n++
      };
      if (a.push(u), t.successors(u).forEach(function(p) {
        e.has(i, p) ? i[p].onStack && (c.lowlink = Math.min(c.lowlink, i[p].index)) : (o(p), c.lowlink = Math.min(c.lowlink, i[p].lowlink));
      }), c.lowlink === c.index) {
        var l = [], d;
        do
          d = a.pop(), i[d].onStack = !1, l.push(d);
        while (u !== d);
        s.push(l);
      }
    }
    return t.nodes().forEach(function(u) {
      e.has(i, u) || o(u);
    }), s;
  }
  return ms;
}
var bs, uf;
function Wy() {
  if (uf) return bs;
  uf = 1;
  var e = Be(), r = Tp();
  bs = t;
  function t(n) {
    return e.filter(r(n), function(a) {
      return a.length > 1 || a.length === 1 && n.hasEdge(a[0], a[0]);
    });
  }
  return bs;
}
var _s, lf;
function Qy() {
  if (lf) return _s;
  lf = 1;
  var e = Be();
  _s = t;
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
    return u.forEach(function(c) {
      o[c] = {}, o[c][c] = { distance: 0 }, u.forEach(function(l) {
        c !== l && (o[c][l] = { distance: Number.POSITIVE_INFINITY });
      }), s(c).forEach(function(l) {
        var d = l.v === c ? l.w : l.v, p = i(l);
        o[c][d] = { distance: p, predecessor: c };
      });
    }), u.forEach(function(c) {
      var l = o[c];
      u.forEach(function(d) {
        var p = o[d];
        u.forEach(function(v) {
          var g = p[c], f = l[v], y = p[v], C = g.distance + f.distance;
          C < y.distance && (y.distance = C, y.predecessor = f.predecessor);
        });
      });
    }), o;
  }
  return _s;
}
var ws, cf;
function Rp() {
  if (cf) return ws;
  cf = 1;
  var e = Be();
  ws = r, r.CycleException = t;
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
  return t.prototype = new Error(), ws;
}
var Is, df;
function Yy() {
  if (df) return Is;
  df = 1;
  var e = Rp();
  Is = r;
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
  return Is;
}
var qs, ff;
function xp() {
  if (ff) return qs;
  ff = 1;
  var e = Be();
  qs = r;
  function r(n, a, i) {
    e.isArray(a) || (a = [a]);
    var s = (n.isDirected() ? n.successors : n.neighbors).bind(n), o = [], u = {};
    return e.each(a, function(c) {
      if (!n.hasNode(c))
        throw new Error("Graph does not have node: " + c);
      t(n, c, i === "post", u, s, o);
    }), o;
  }
  function t(n, a, i, s, o, u) {
    e.has(s, a) || (s[a] = !0, i || u.push(a), e.each(o(a), function(c) {
      t(n, c, i, s, o, u);
    }), i && u.push(a));
  }
  return qs;
}
var Cs, hf;
function Jy() {
  if (hf) return Cs;
  hf = 1;
  var e = xp();
  Cs = r;
  function r(t, n) {
    return e(t, n, "post");
  }
  return Cs;
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
var Ss, vf;
function Zy() {
  if (vf) return Ss;
  vf = 1;
  var e = Be(), r = tu(), t = Ep();
  Ss = n;
  function n(a, i) {
    var s = new r(), o = {}, u = new t(), c;
    function l(p) {
      var v = p.v === c ? p.w : p.v, g = u.priority(v);
      if (g !== void 0) {
        var f = i(p);
        f < g && (o[v] = c, u.decrease(v, f));
      }
    }
    if (a.nodeCount() === 0)
      return s;
    e.each(a.nodes(), function(p) {
      u.add(p, Number.POSITIVE_INFINITY), s.setNode(p);
    }), u.decrease(a.nodes()[0], 0);
    for (var d = !1; u.size() > 0; ) {
      if (c = u.removeMin(), e.has(o, c))
        s.setEdge(c, o[c]);
      else {
        if (d)
          throw new Error("Input graph is not connected: " + a);
        d = !0;
      }
      a.nodeEdges(c).forEach(l);
    }
    return s;
  }
  return Ss;
}
var Ts, gf;
function em() {
  return gf || (gf = 1, Ts = {
    components: Vy(),
    dijkstra: Sp(),
    dijkstraAll: Hy(),
    findCycles: Wy(),
    floydWarshall: Qy(),
    isAcyclic: Yy(),
    postorder: Jy(),
    preorder: Xy(),
    prim: Zy(),
    tarjan: Tp(),
    topsort: Rp()
  }), Ts;
}
var Rs, yf;
function rm() {
  if (yf) return Rs;
  yf = 1;
  var e = Uy();
  return Rs = {
    Graph: e.Graph,
    json: Ky(),
    alg: em(),
    version: e.version
  }, Rs;
}
var it;
if (typeof $o == "function")
  try {
    it = rm();
  } catch {
  }
it || (it = window.graphlib);
var Ye = it, xs, mf;
function tm() {
  if (mf) return xs;
  mf = 1;
  var e = ep(), r = 1, t = 4;
  function n(a) {
    return e(a, r | t);
  }
  return xs = n, xs;
}
var ks, bf;
function Tt() {
  if (bf) return ks;
  bf = 1;
  var e = kr(), r = ur(), t = mt(), n = ze();
  function a(i, s, o) {
    if (!n(o))
      return !1;
    var u = typeof s;
    return (u == "number" ? r(o) && t(s, o.length) : u == "string" && s in o) ? e(o[s], i) : !1;
  }
  return ks = a, ks;
}
var As, _f;
function nm() {
  if (_f) return As;
  _f = 1;
  var e = St(), r = kr(), t = Tt(), n = Er(), a = Object.prototype, i = a.hasOwnProperty, s = e(function(o, u) {
    o = Object(o);
    var c = -1, l = u.length, d = l > 2 ? u[2] : void 0;
    for (d && t(u[0], u[1], d) && (l = 1); ++c < l; )
      for (var p = u[c], v = n(p), g = -1, f = v.length; ++g < f; ) {
        var y = v[g], C = o[y];
        (C === void 0 || r(C, a[y]) && !i.call(o, y)) && (o[y] = p[y]);
      }
    return o;
  });
  return As = s, As;
}
var Os, wf;
function am() {
  if (wf) return Os;
  wf = 1;
  var e = lr(), r = ur(), t = yr();
  function n(a) {
    return function(i, s, o) {
      var u = Object(i);
      if (!r(i)) {
        var c = e(s, 3);
        i = t(i), s = function(d) {
          return c(u[d], d, u);
        };
      }
      var l = a(i, s, o);
      return l > -1 ? u[c ? i[l] : l] : void 0;
    };
  }
  return Os = n, Os;
}
var Ps, If;
function im() {
  if (If) return Ps;
  If = 1;
  var e = /\s/;
  function r(t) {
    for (var n = t.length; n-- && e.test(t.charAt(n)); )
      ;
    return n;
  }
  return Ps = r, Ps;
}
var Ns, qf;
function sm() {
  if (qf) return Ns;
  qf = 1;
  var e = im(), r = /^\s+/;
  function t(n) {
    return n && n.slice(0, e(n) + 1).replace(r, "");
  }
  return Ns = t, Ns;
}
var Ds, Cf;
function om() {
  if (Cf) return Ds;
  Cf = 1;
  var e = sm(), r = ze(), t = Nr(), n = NaN, a = /^[-+]0x[0-9a-f]+$/i, i = /^0b[01]+$/i, s = /^0o[0-7]+$/i, o = parseInt;
  function u(c) {
    if (typeof c == "number")
      return c;
    if (t(c))
      return n;
    if (r(c)) {
      var l = typeof c.valueOf == "function" ? c.valueOf() : c;
      c = r(l) ? l + "" : l;
    }
    if (typeof c != "string")
      return c === 0 ? c : +c;
    c = e(c);
    var d = i.test(c);
    return d || s.test(c) ? o(c.slice(2), d ? 2 : 8) : a.test(c) ? n : +c;
  }
  return Ds = u, Ds;
}
var Ms, Ef;
function kp() {
  if (Ef) return Ms;
  Ef = 1;
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
var Ls, Sf;
function um() {
  if (Sf) return Ls;
  Sf = 1;
  var e = kp();
  function r(t) {
    var n = e(t), a = n % 1;
    return n === n ? a ? n - a : n : 0;
  }
  return Ls = r, Ls;
}
var Fs, Tf;
function lm() {
  if (Tf) return Fs;
  Tf = 1;
  var e = Ip(), r = lr(), t = um(), n = Math.max;
  function a(i, s, o) {
    var u = i == null ? 0 : i.length;
    if (!u)
      return -1;
    var c = o == null ? 0 : t(o);
    return c < 0 && (c = n(u + c, 0)), e(i, r(s, 3), c);
  }
  return Fs = a, Fs;
}
var js, Rf;
function cm() {
  if (Rf) return js;
  Rf = 1;
  var e = am(), r = lm(), t = e(r);
  return js = t, js;
}
var $s, xf;
function Ap() {
  if (xf) return $s;
  xf = 1;
  var e = ru();
  function r(t) {
    var n = t == null ? 0 : t.length;
    return n ? e(t, 1) : [];
  }
  return $s = r, $s;
}
var Gs, kf;
function dm() {
  if (kf) return Gs;
  kf = 1;
  var e = Jo(), r = rp(), t = Er();
  function n(a, i) {
    return a == null ? a : e(a, r(i), t);
  }
  return Gs = n, Gs;
}
var zs, Af;
function fm() {
  if (Af) return zs;
  Af = 1;
  function e(r) {
    var t = r == null ? 0 : r.length;
    return t ? r[t - 1] : void 0;
  }
  return zs = e, zs;
}
var Bs, Of;
function hm() {
  if (Of) return Bs;
  Of = 1;
  var e = gt(), r = Xo(), t = lr();
  function n(a, i) {
    var s = {};
    return i = t(i, 3), r(a, function(o, u, c) {
      e(s, u, i(o, u, c));
    }), s;
  }
  return Bs = n, Bs;
}
var Us, Pf;
function nu() {
  if (Pf) return Us;
  Pf = 1;
  var e = Nr();
  function r(t, n, a) {
    for (var i = -1, s = t.length; ++i < s; ) {
      var o = t[i], u = n(o);
      if (u != null && (c === void 0 ? u === u && !e(u) : a(u, c)))
        var c = u, l = o;
    }
    return l;
  }
  return Us = r, Us;
}
var Ks, Nf;
function pm() {
  if (Nf) return Ks;
  Nf = 1;
  function e(r, t) {
    return r > t;
  }
  return Ks = e, Ks;
}
var Vs, Df;
function vm() {
  if (Df) return Vs;
  Df = 1;
  var e = nu(), r = pm(), t = Sr();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return Vs = n, Vs;
}
var Hs, Mf;
function Op() {
  if (Mf) return Hs;
  Mf = 1;
  var e = gt(), r = kr();
  function t(n, a, i) {
    (i !== void 0 && !r(n[a], i) || i === void 0 && !(a in n)) && e(n, a, i);
  }
  return Hs = t, Hs;
}
var Ws, Lf;
function gm() {
  if (Lf) return Ws;
  Lf = 1;
  var e = qr(), r = wt(), t = rr(), n = "[object Object]", a = Function.prototype, i = Object.prototype, s = a.toString, o = i.hasOwnProperty, u = s.call(Object);
  function c(l) {
    if (!t(l) || e(l) != n)
      return !1;
    var d = r(l);
    if (d === null)
      return !0;
    var p = o.call(d, "constructor") && d.constructor;
    return typeof p == "function" && p instanceof p && s.call(p) == u;
  }
  return Ws = c, Ws;
}
var Qs, Ff;
function Pp() {
  if (Ff) return Qs;
  Ff = 1;
  function e(r, t) {
    if (!(t === "constructor" && typeof r[t] == "function") && t != "__proto__")
      return r[t];
  }
  return Qs = e, Qs;
}
var Ys, jf;
function ym() {
  if (jf) return Ys;
  jf = 1;
  var e = Hr(), r = Er();
  function t(n) {
    return e(n, r(n));
  }
  return Ys = t, Ys;
}
var Js, $f;
function mm() {
  if ($f) return Js;
  $f = 1;
  var e = Op(), r = zh(), t = Jh(), n = Bh(), a = Zh(), i = Wr(), s = Ie(), o = qp(), u = Or(), c = Vr(), l = ze(), d = gm(), p = Qr(), v = Pp(), g = ym();
  function f(y, C, R, _, k, N, m) {
    var S = v(y, R), D = v(C, R), U = m.get(D);
    if (U) {
      e(y, R, U);
      return;
    }
    var z = N ? N(S, D, R + "", y, C, m) : void 0, I = z === void 0;
    if (I) {
      var M = s(D), j = !M && u(D), J = !M && !j && p(D);
      z = D, M || j || J ? s(S) ? z = S : o(S) ? z = n(S) : j ? (I = !1, z = r(D, !0)) : J ? (I = !1, z = t(D, !0)) : z = [] : d(D) || i(D) ? (z = S, i(S) ? z = g(S) : (!l(S) || c(S)) && (z = a(D))) : I = !1;
    }
    I && (m.set(D, z), k(z, D, _, N, m), m.delete(D)), e(y, R, z);
  }
  return Js = f, Js;
}
var Xs, Gf;
function bm() {
  if (Gf) return Xs;
  Gf = 1;
  var e = vt(), r = Op(), t = Jo(), n = mm(), a = ze(), i = Er(), s = Pp();
  function o(u, c, l, d, p) {
    u !== c && t(c, function(v, g) {
      if (p || (p = new e()), a(v))
        n(u, c, g, l, o, d, p);
      else {
        var f = d ? d(s(u, g), v, g + "", u, c, p) : void 0;
        f === void 0 && (f = v), r(u, g, f);
      }
    }, i);
  }
  return Xs = o, Xs;
}
var Zs, zf;
function _m() {
  if (zf) return Zs;
  zf = 1;
  var e = St(), r = Tt();
  function t(n) {
    return e(function(a, i) {
      var s = -1, o = i.length, u = o > 1 ? i[o - 1] : void 0, c = o > 2 ? i[2] : void 0;
      for (u = n.length > 3 && typeof u == "function" ? (o--, u) : void 0, c && r(i[0], i[1], c) && (u = o < 3 ? void 0 : u, o = 1), a = Object(a); ++s < o; ) {
        var l = i[s];
        l && n(a, l, s, u);
      }
      return a;
    });
  }
  return Zs = t, Zs;
}
var eo, Bf;
function wm() {
  if (Bf) return eo;
  Bf = 1;
  var e = bm(), r = _m(), t = r(function(n, a, i) {
    e(n, a, i);
  });
  return eo = t, eo;
}
var ro, Uf;
function Np() {
  if (Uf) return ro;
  Uf = 1;
  function e(r, t) {
    return r < t;
  }
  return ro = e, ro;
}
var to, Kf;
function Im() {
  if (Kf) return to;
  Kf = 1;
  var e = nu(), r = Np(), t = Sr();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return to = n, to;
}
var no, Vf;
function qm() {
  if (Vf) return no;
  Vf = 1;
  var e = nu(), r = lr(), t = Np();
  function n(a, i) {
    return a && a.length ? e(a, r(i, 2), t) : void 0;
  }
  return no = n, no;
}
var ao, Hf;
function Cm() {
  if (Hf) return ao;
  Hf = 1;
  var e = Qe(), r = function() {
    return e.Date.now();
  };
  return ao = r, ao;
}
var io, Wf;
function Em() {
  if (Wf) return io;
  Wf = 1;
  var e = yt(), r = Ct(), t = mt(), n = ze(), a = Yr();
  function i(s, o, u, c) {
    if (!n(s))
      return s;
    o = r(o, s);
    for (var l = -1, d = o.length, p = d - 1, v = s; v != null && ++l < d; ) {
      var g = a(o[l]), f = u;
      if (g === "__proto__" || g === "constructor" || g === "prototype")
        return s;
      if (l != p) {
        var y = v[g];
        f = c ? c(y, g, v) : void 0, f === void 0 && (f = n(y) ? y : t(o[l + 1]) ? [] : {});
      }
      e(v, g, f), v = v[g];
    }
    return s;
  }
  return io = i, io;
}
var so, Qf;
function Sm() {
  if (Qf) return so;
  Qf = 1;
  var e = Et(), r = Em(), t = Ct();
  function n(a, i, s) {
    for (var o = -1, u = i.length, c = {}; ++o < u; ) {
      var l = i[o], d = e(a, l);
      s(d, l) && r(c, t(l, a), d);
    }
    return c;
  }
  return so = n, so;
}
var oo, Yf;
function Tm() {
  if (Yf) return oo;
  Yf = 1;
  var e = Sm(), r = fp();
  function t(n, a) {
    return e(n, a, function(i, s) {
      return r(n, s);
    });
  }
  return oo = t, oo;
}
var uo, Jf;
function Rm() {
  if (Jf) return uo;
  Jf = 1;
  var e = Ap(), r = _p(), t = wp();
  function n(a) {
    return t(r(a, void 0, e), a + "");
  }
  return uo = n, uo;
}
var lo, Xf;
function xm() {
  if (Xf) return lo;
  Xf = 1;
  var e = Tm(), r = Rm(), t = r(function(n, a) {
    return n == null ? {} : e(n, a);
  });
  return lo = t, lo;
}
var co, Zf;
function km() {
  if (Zf) return co;
  Zf = 1;
  var e = Math.ceil, r = Math.max;
  function t(n, a, i, s) {
    for (var o = -1, u = r(e((a - n) / (i || 1)), 0), c = Array(u); u--; )
      c[s ? u : ++o] = n, n += i;
    return c;
  }
  return co = t, co;
}
var fo, eh;
function Am() {
  if (eh) return fo;
  eh = 1;
  var e = km(), r = Tt(), t = kp();
  function n(a) {
    return function(i, s, o) {
      return o && typeof o != "number" && r(i, s, o) && (s = o = void 0), i = t(i), s === void 0 ? (s = i, i = 0) : s = t(s), o = o === void 0 ? i < s ? 1 : -1 : t(o), e(i, s, o, a);
    };
  }
  return fo = n, fo;
}
var ho, rh;
function Om() {
  if (rh) return ho;
  rh = 1;
  var e = Am(), r = e();
  return ho = r, ho;
}
var po, th;
function Pm() {
  if (th) return po;
  th = 1;
  function e(r, t) {
    var n = r.length;
    for (r.sort(t); n--; )
      r[n] = r[n].value;
    return r;
  }
  return po = e, po;
}
var vo, nh;
function Nm() {
  if (nh) return vo;
  nh = 1;
  var e = Nr();
  function r(t, n) {
    if (t !== n) {
      var a = t !== void 0, i = t === null, s = t === t, o = e(t), u = n !== void 0, c = n === null, l = n === n, d = e(n);
      if (!c && !d && !o && t > n || o && u && l && !c && !d || i && u && l || !a && l || !s)
        return 1;
      if (!i && !o && !d && t < n || d && a && s && !i && !o || c && a && s || !u && s || !l)
        return -1;
    }
    return 0;
  }
  return vo = r, vo;
}
var go, ah;
function Dm() {
  if (ah) return go;
  ah = 1;
  var e = Nm();
  function r(t, n, a) {
    for (var i = -1, s = t.criteria, o = n.criteria, u = s.length, c = a.length; ++i < u; ) {
      var l = e(s[i], o[i]);
      if (l) {
        if (i >= c)
          return l;
        var d = a[i];
        return l * (d == "desc" ? -1 : 1);
      }
    }
    return t.index - n.index;
  }
  return go = r, go;
}
var yo, ih;
function Mm() {
  if (ih) return yo;
  ih = 1;
  var e = qt(), r = Et(), t = lr(), n = yp(), a = Pm(), i = bt(), s = Dm(), o = Sr(), u = Ie();
  function c(l, d, p) {
    d.length ? d = e(d, function(f) {
      return u(f) ? function(y) {
        return r(y, f.length === 1 ? f[0] : f);
      } : f;
    }) : d = [o];
    var v = -1;
    d = e(d, i(t));
    var g = n(l, function(f, y, C) {
      var R = e(d, function(_) {
        return _(f);
      });
      return { criteria: R, index: ++v, value: f };
    });
    return a(g, function(f, y) {
      return s(f, y, p);
    });
  }
  return yo = c, yo;
}
var mo, sh;
function Lm() {
  if (sh) return mo;
  sh = 1;
  var e = ru(), r = Mm(), t = St(), n = Tt(), a = t(function(i, s) {
    if (i == null)
      return [];
    var o = s.length;
    return o > 1 && n(i, s[0], s[1]) ? s = [] : o > 2 && n(s[0], s[1], s[2]) && (s = [s[0]]), r(i, e(s, 1), []);
  });
  return mo = a, mo;
}
var bo, oh;
function Fm() {
  if (oh) return bo;
  oh = 1;
  var e = cp(), r = 0;
  function t(n) {
    var a = ++r;
    return e(n) + a;
  }
  return bo = t, bo;
}
var _o, uh;
function jm() {
  if (uh) return _o;
  uh = 1;
  function e(r, t, n) {
    for (var a = -1, i = r.length, s = t.length, o = {}; ++a < i; ) {
      var u = a < s ? t[a] : void 0;
      n(o, r[a], u);
    }
    return o;
  }
  return _o = e, _o;
}
var wo, lh;
function $m() {
  if (lh) return wo;
  lh = 1;
  var e = yt(), r = jm();
  function t(n, a) {
    return r(n || [], a || [], e);
  }
  return wo = t, wo;
}
var st;
if (typeof $o == "function")
  try {
    st = {
      cloneDeep: tm(),
      constant: Yo(),
      defaults: nm(),
      each: np(),
      filter: pp(),
      find: cm(),
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
      now: Cm(),
      pick: xm(),
      range: Om(),
      reduce: bp(),
      sortBy: Lm(),
      uniqueId: Fm(),
      values: Cp(),
      zipObject: $m()
    };
  } catch {
  }
st || (st = window._);
var ye = st, Gm = Rt;
function Rt() {
  var e = {};
  e._next = e._prev = e, this._sentinel = e;
}
Rt.prototype.dequeue = function() {
  var e = this._sentinel, r = e._prev;
  if (r !== e)
    return Dp(r), r;
};
Rt.prototype.enqueue = function(e) {
  var r = this._sentinel;
  e._prev && e._next && Dp(e), e._next = r._next, r._next._prev = e, r._next = e, e._prev = r;
};
Rt.prototype.toString = function() {
  for (var e = [], r = this._sentinel, t = r._prev; t !== r; )
    e.push(JSON.stringify(t, zm)), t = t._prev;
  return "[" + e.join(", ") + "]";
};
function Dp(e) {
  e._prev._next = e._next, e._next._prev = e._prev, delete e._next, delete e._prev;
}
function zm(e, r) {
  if (e !== "_next" && e !== "_prev")
    return r;
}
var ir = ye, Bm = Ye.Graph, Um = Gm, Km = Hm, Vm = ir.constant(1);
function Hm(e, r) {
  if (e.nodeCount() <= 1)
    return [];
  var t = Qm(e, r || Vm), n = Wm(t.graph, t.buckets, t.zeroIdx);
  return ir.flatten(ir.map(n, function(a) {
    return e.outEdges(a.v, a.w);
  }), !0);
}
function Wm(e, r, t) {
  for (var n = [], a = r[r.length - 1], i = r[0], s; e.nodeCount(); ) {
    for (; s = i.dequeue(); )
      Io(e, r, t, s);
    for (; s = a.dequeue(); )
      Io(e, r, t, s);
    if (e.nodeCount()) {
      for (var o = r.length - 2; o > 0; --o)
        if (s = r[o].dequeue(), s) {
          n = n.concat(Io(e, r, t, s, !0));
          break;
        }
    }
  }
  return n;
}
function Io(e, r, t, n, a) {
  var i = a ? [] : void 0;
  return ir.forEach(e.inEdges(n.v), function(s) {
    var o = e.edge(s), u = e.node(s.v);
    a && i.push({ v: s.v, w: s.w }), u.out -= o, Mo(r, t, u);
  }), ir.forEach(e.outEdges(n.v), function(s) {
    var o = e.edge(s), u = s.w, c = e.node(u);
    c.in -= o, Mo(r, t, c);
  }), e.removeNode(n.v), i;
}
function Qm(e, r) {
  var t = new Bm(), n = 0, a = 0;
  ir.forEach(e.nodes(), function(o) {
    t.setNode(o, { v: o, in: 0, out: 0 });
  }), ir.forEach(e.edges(), function(o) {
    var u = t.edge(o.v, o.w) || 0, c = r(o), l = u + c;
    t.setEdge(o.v, o.w, l), a = Math.max(a, t.node(o.v).out += c), n = Math.max(n, t.node(o.w).in += c);
  });
  var i = ir.range(a + n + 3).map(function() {
    return new Um();
  }), s = n + 1;
  return ir.forEach(t.nodes(), function(o) {
    Mo(i, s, t.node(o));
  }), { graph: t, buckets: i, zeroIdx: s };
}
function Mo(e, r, t) {
  t.out ? t.in ? e[t.out - t.in + r].enqueue(t) : e[e.length - 1].enqueue(t) : e[0].enqueue(t);
}
var wr = ye, Ym = Km, Jm = {
  run: Xm,
  undo: eb
};
function Xm(e) {
  var r = e.graph().acyclicer === "greedy" ? Ym(e, t(e)) : Zm(e);
  wr.forEach(r, function(n) {
    var a = e.edge(n);
    e.removeEdge(n), a.forwardName = n.name, a.reversed = !0, e.setEdge(n.w, n.v, a, wr.uniqueId("rev"));
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
    wr.has(n, i) || (n[i] = !0, t[i] = !0, wr.forEach(e.outEdges(i), function(s) {
      wr.has(t, s.w) ? r.push(s) : a(s.w);
    }), delete t[i]);
  }
  return wr.forEach(e.nodes(), a), r;
}
function eb(e) {
  wr.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (t.reversed) {
      e.removeEdge(r);
      var n = t.forwardName;
      delete t.reversed, delete t.forwardName, e.setEdge(r.w, r.v, t, n);
    }
  });
}
var ae = ye, Mp = Ye.Graph, Me = {
  addDummyNode: Lp,
  simplify: rb,
  asNonCompoundGraph: tb,
  successorWeights: nb,
  predecessorWeights: ab,
  intersectRect: ib,
  buildLayerMatrix: sb,
  normalizeRanks: ob,
  removeEmptyRanks: ub,
  addBorderNode: lb,
  maxRank: Fp,
  partition: cb,
  time: db,
  notime: fb
};
function Lp(e, r, t, n) {
  var a;
  do
    a = ae.uniqueId(n);
  while (e.hasNode(a));
  return t.dummy = r, e.setNode(a, t), a;
}
function rb(e) {
  var r = new Mp().setGraph(e.graph());
  return ae.forEach(e.nodes(), function(t) {
    r.setNode(t, e.node(t));
  }), ae.forEach(e.edges(), function(t) {
    var n = r.edge(t.v, t.w) || { weight: 0, minlen: 1 }, a = e.edge(t);
    r.setEdge(t.v, t.w, {
      weight: n.weight + a.weight,
      minlen: Math.max(n.minlen, a.minlen)
    });
  }), r;
}
function tb(e) {
  var r = new Mp({ multigraph: e.isMultigraph() }).setGraph(e.graph());
  return ae.forEach(e.nodes(), function(t) {
    e.children(t).length || r.setNode(t, e.node(t));
  }), ae.forEach(e.edges(), function(t) {
    r.setEdge(t, e.edge(t));
  }), r;
}
function nb(e) {
  var r = ae.map(e.nodes(), function(t) {
    var n = {};
    return ae.forEach(e.outEdges(t), function(a) {
      n[a.w] = (n[a.w] || 0) + e.edge(a).weight;
    }), n;
  });
  return ae.zipObject(e.nodes(), r);
}
function ab(e) {
  var r = ae.map(e.nodes(), function(t) {
    var n = {};
    return ae.forEach(e.inEdges(t), function(a) {
      n[a.v] = (n[a.v] || 0) + e.edge(a).weight;
    }), n;
  });
  return ae.zipObject(e.nodes(), r);
}
function ib(e, r) {
  var t = e.x, n = e.y, a = r.x - t, i = r.y - n, s = e.width / 2, o = e.height / 2;
  if (!a && !i)
    throw new Error("Not possible to find intersection inside of the rectangle");
  var u, c;
  return Math.abs(i) * s > Math.abs(a) * o ? (i < 0 && (o = -o), u = o * a / i, c = o) : (a < 0 && (s = -s), u = s, c = s * i / a), { x: t + u, y: n + c };
}
function sb(e) {
  var r = ae.map(ae.range(Fp(e) + 1), function() {
    return [];
  });
  return ae.forEach(e.nodes(), function(t) {
    var n = e.node(t), a = n.rank;
    ae.isUndefined(a) || (r[a][n.order] = t);
  }), r;
}
function ob(e) {
  var r = ae.min(ae.map(e.nodes(), function(t) {
    return e.node(t).rank;
  }));
  ae.forEach(e.nodes(), function(t) {
    var n = e.node(t);
    ae.has(n, "rank") && (n.rank -= r);
  });
}
function ub(e) {
  var r = ae.min(ae.map(e.nodes(), function(i) {
    return e.node(i).rank;
  })), t = [];
  ae.forEach(e.nodes(), function(i) {
    var s = e.node(i).rank - r;
    t[s] || (t[s] = []), t[s].push(i);
  });
  var n = 0, a = e.graph().nodeRankFactor;
  ae.forEach(t, function(i, s) {
    ae.isUndefined(i) && s % a !== 0 ? --n : n && ae.forEach(i, function(o) {
      e.node(o).rank += n;
    });
  });
}
function lb(e, r, t, n) {
  var a = {
    width: 0,
    height: 0
  };
  return arguments.length >= 4 && (a.rank = t, a.order = n), Lp(e, "border", a, r);
}
function Fp(e) {
  return ae.max(ae.map(e.nodes(), function(r) {
    var t = e.node(r).rank;
    if (!ae.isUndefined(t))
      return t;
  }));
}
function cb(e, r) {
  var t = { lhs: [], rhs: [] };
  return ae.forEach(e, function(n) {
    r(n) ? t.lhs.push(n) : t.rhs.push(n);
  }), t;
}
function db(e, r) {
  var t = ae.now();
  try {
    return r();
  } finally {
    console.log(e + " time: " + (ae.now() - t) + "ms");
  }
}
function fb(e, r) {
  return r();
}
var jp = ye, hb = Me, pb = {
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
    var c, l, d;
    for (d = 0, ++n; n < i; ++d, ++n)
      o.points = [], l = {
        width: 0,
        height: 0,
        edgeLabel: o,
        edgeObj: r,
        rank: n
      }, c = hb.addDummyNode(e, "edge", l, "_d"), n === u && (l.width = o.width, l.height = o.height, l.dummy = "edge-label", l.labelpos = o.labelpos), e.setEdge(t, c, { weight: o.weight }, s), d === 0 && e.graph().dummyChains.push(c), t = c;
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
var Zr = ye, xt = {
  longestPath: mb,
  slack: bb
};
function mb(e) {
  var r = {};
  function t(n) {
    var a = e.node(n);
    if (Zr.has(r, n))
      return a.rank;
    r[n] = !0;
    var i = Zr.min(Zr.map(e.outEdges(n), function(s) {
      return t(s.w) - e.edge(s).minlen;
    }));
    return (i === Number.POSITIVE_INFINITY || // return value of _.map([]) for Lodash 3
    i === void 0 || // return value of _.map([]) for Lodash 4
    i === null) && (i = 0), a.rank = i;
  }
  Zr.forEach(e.sources(), t);
}
function bb(e, r) {
  return e.node(r.w).rank - e.node(r.v).rank - e.edge(r).minlen;
}
var ot = ye, _b = Ye.Graph, ut = xt.slack, $p = wb;
function wb(e) {
  var r = new _b({ directed: !1 }), t = e.nodes()[0], n = e.nodeCount();
  r.setNode(t, {});
  for (var a, i; Ib(r, e) < n; )
    a = qb(r, e), i = r.hasNode(a.v) ? ut(e, a) : -ut(e, a), Cb(r, e, i);
  return r;
}
function Ib(e, r) {
  function t(n) {
    ot.forEach(r.nodeEdges(n), function(a) {
      var i = a.v, s = n === i ? a.w : i;
      !e.hasNode(s) && !ut(r, a) && (e.setNode(s, {}), e.setEdge(n, s, {}), t(s));
    });
  }
  return ot.forEach(e.nodes(), t), e.nodeCount();
}
function qb(e, r) {
  return ot.minBy(r.edges(), function(t) {
    if (e.hasNode(t.v) !== e.hasNode(t.w))
      return ut(r, t);
  });
}
function Cb(e, r, t) {
  ot.forEach(e.nodes(), function(n) {
    r.node(n).rank += t;
  });
}
var or = ye, Eb = $p, Sb = xt.slack, Tb = xt.longestPath, Rb = Ye.alg.preorder, xb = Ye.alg.postorder, kb = Me.simplify, Ab = Tr;
Tr.initLowLimValues = iu;
Tr.initCutValues = au;
Tr.calcCutValue = Gp;
Tr.leaveEdge = Bp;
Tr.enterEdge = Up;
Tr.exchangeEdges = Kp;
function Tr(e) {
  e = kb(e), Tb(e);
  var r = Eb(e);
  iu(r), au(r, e);
  for (var t, n; t = Bp(r); )
    n = Up(r, e, t), Kp(r, e, t, n);
}
function au(e, r) {
  var t = xb(e, e.nodes());
  t = t.slice(0, t.length - 1), or.forEach(t, function(n) {
    Ob(e, r, n);
  });
}
function Ob(e, r, t) {
  var n = e.node(t), a = n.parent;
  e.edge(t, a).cutvalue = Gp(e, r, t);
}
function Gp(e, r, t) {
  var n = e.node(t), a = n.parent, i = !0, s = r.edge(t, a), o = 0;
  return s || (i = !1, s = r.edge(a, t)), o = s.weight, or.forEach(r.nodeEdges(t), function(u) {
    var c = u.v === t, l = c ? u.w : u.v;
    if (l !== a) {
      var d = c === i, p = r.edge(u).weight;
      if (o += d ? p : -p, Nb(e, t, l)) {
        var v = e.edge(t, l).cutvalue;
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
  return r[n] = !0, or.forEach(e.neighbors(n), function(o) {
    or.has(r, o) || (t = zp(e, r, t, o, n));
  }), s.low = i, s.lim = t++, a ? s.parent = a : delete s.parent, t;
}
function Bp(e) {
  return or.find(e.edges(), function(r) {
    return e.edge(r).cutvalue < 0;
  });
}
function Up(e, r, t) {
  var n = t.v, a = t.w;
  r.hasEdge(n, a) || (n = t.w, a = t.v);
  var i = e.node(n), s = e.node(a), o = i, u = !1;
  i.lim > s.lim && (o = s, u = !0);
  var c = or.filter(r.edges(), function(l) {
    return u === ch(e, e.node(l.v), o) && u !== ch(e, e.node(l.w), o);
  });
  return or.minBy(c, function(l) {
    return Sb(r, l);
  });
}
function Kp(e, r, t, n) {
  var a = t.v, i = t.w;
  e.removeEdge(a, i), e.setEdge(n.v, n.w, {}), iu(e), au(e, r), Pb(e, r);
}
function Pb(e, r) {
  var t = or.find(e.nodes(), function(a) {
    return !r.node(a).parent;
  }), n = Rb(e, t);
  n = n.slice(1), or.forEach(n, function(a) {
    var i = e.node(a).parent, s = r.edge(a, i), o = !1;
    s || (s = r.edge(i, a), o = !0), r.node(a).rank = r.node(i).rank + (o ? s.minlen : -s.minlen);
  });
}
function Nb(e, r, t) {
  return e.hasEdge(r, t);
}
function ch(e, r, t) {
  return t.low <= r.lim && r.lim <= t.lim;
}
var Db = xt, Vp = Db.longestPath, Mb = $p, Lb = Ab, Fb = jb;
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
var $b = Vp;
function Gb(e) {
  Vp(e), Mb(e);
}
function dh(e) {
  Lb(e);
}
var Lo = ye, zb = Bb;
function Bb(e) {
  var r = Kb(e);
  Lo.forEach(e.graph().dummyChains, function(t) {
    for (var n = e.node(t), a = n.edgeObj, i = Ub(e, r, a.v, a.w), s = i.path, o = i.lca, u = 0, c = s[u], l = !0; t !== a.w; ) {
      if (n = e.node(t), l) {
        for (; (c = s[u]) !== o && e.node(c).maxRank < n.rank; )
          u++;
        c === o && (l = !1);
      }
      if (!l) {
        for (; u < s.length - 1 && e.node(c = s[u + 1]).minRank <= n.rank; )
          u++;
        c = s[u];
      }
      e.setParent(t, c), t = e.successors(t)[0];
    }
  });
}
function Ub(e, r, t, n) {
  var a = [], i = [], s = Math.min(r[t].low, r[n].low), o = Math.max(r[t].lim, r[n].lim), u, c;
  u = t;
  do
    u = e.parent(u), a.push(u);
  while (u && (r[u].low > s || o > r[u].lim));
  for (c = u, u = n; (u = e.parent(u)) !== c; )
    i.push(u);
  return { path: a.concat(i.reverse()), lca: c };
}
function Kb(e) {
  var r = {}, t = 0;
  function n(a) {
    var i = t;
    Lo.forEach(e.children(a), n), r[a] = { low: i, lim: t++ };
  }
  return Lo.forEach(e.children(), n), r;
}
var sr = ye, Fo = Me, Vb = {
  run: Hb,
  cleanup: Yb
};
function Hb(e) {
  var r = Fo.addDummyNode(e, "root", {}, "_root"), t = Wb(e), n = sr.max(sr.values(t)) - 1, a = 2 * n + 1;
  e.graph().nestingRoot = r, sr.forEach(e.edges(), function(s) {
    e.edge(s).minlen *= a;
  });
  var i = Qb(e) + 1;
  sr.forEach(e.children(), function(s) {
    Hp(e, r, a, i, n, t, s);
  }), e.graph().nodeRankFactor = a;
}
function Hp(e, r, t, n, a, i, s) {
  var o = e.children(s);
  if (!o.length) {
    s !== r && e.setEdge(r, s, { weight: 0, minlen: t });
    return;
  }
  var u = Fo.addBorderNode(e, "_bt"), c = Fo.addBorderNode(e, "_bb"), l = e.node(s);
  e.setParent(u, s), l.borderTop = u, e.setParent(c, s), l.borderBottom = c, sr.forEach(o, function(d) {
    Hp(e, r, t, n, a, i, d);
    var p = e.node(d), v = p.borderTop ? p.borderTop : d, g = p.borderBottom ? p.borderBottom : d, f = p.borderTop ? n : 2 * n, y = v !== g ? 1 : a - i[s] + 1;
    e.setEdge(u, v, {
      weight: f,
      minlen: y,
      nestingEdge: !0
    }), e.setEdge(g, c, {
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
    i && i.length && sr.forEach(i, function(s) {
      t(s, a + 1);
    }), r[n] = a;
  }
  return sr.forEach(e.children(), function(n) {
    t(n, 1);
  }), r;
}
function Qb(e) {
  return sr.reduce(e.edges(), function(r, t) {
    return r + e.edge(t).weight;
  }, 0);
}
function Yb(e) {
  var r = e.graph();
  e.removeNode(r.nestingRoot), delete r.nestingRoot, sr.forEach(e.edges(), function(t) {
    var n = e.edge(t);
    n.nestingEdge && e.removeEdge(t);
  });
}
var qo = ye, Jb = Me, Xb = Zb;
function Zb(e) {
  function r(t) {
    var n = e.children(t), a = e.node(t);
    if (n.length && qo.forEach(n, r), qo.has(a, "minRank")) {
      a.borderLeft = [], a.borderRight = [];
      for (var i = a.minRank, s = a.maxRank + 1; i < s; ++i)
        fh(e, "borderLeft", "_bl", t, a, i), fh(e, "borderRight", "_br", t, a, i);
    }
  }
  qo.forEach(e.children(), r);
}
function fh(e, r, t, n, a, i) {
  var s = { width: 0, height: 0, rank: i, borderType: r }, o = a[r][i - 1], u = Jb.addDummyNode(e, "border", s, t);
  a[r][i] = u, e.setParent(u, n), o && e.setEdge(o, u, { weight: 1 });
}
var er = ye, e_ = {
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
  er.forEach(e.nodes(), function(r) {
    hh(e.node(r));
  }), er.forEach(e.edges(), function(r) {
    hh(e.edge(r));
  });
}
function hh(e) {
  var r = e.width;
  e.width = e.height, e.height = r;
}
function n_(e) {
  er.forEach(e.nodes(), function(r) {
    Co(e.node(r));
  }), er.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    er.forEach(t.points, Co), er.has(t, "y") && Co(t);
  });
}
function Co(e) {
  e.y = -e.y;
}
function a_(e) {
  er.forEach(e.nodes(), function(r) {
    Eo(e.node(r));
  }), er.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    er.forEach(t.points, Eo), er.has(t, "x") && Eo(t);
  });
}
function Eo(e) {
  var r = e.x;
  e.x = e.y, e.y = r;
}
var nr = ye, i_ = s_;
function s_(e) {
  var r = {}, t = nr.filter(e.nodes(), function(o) {
    return !e.children(o).length;
  }), n = nr.max(nr.map(t, function(o) {
    return e.node(o).rank;
  })), a = nr.map(nr.range(n + 1), function() {
    return [];
  });
  function i(o) {
    if (!nr.has(r, o)) {
      r[o] = !0;
      var u = e.node(o);
      a[u.rank].push(o), nr.forEach(e.successors(o), i);
    }
  }
  var s = nr.sortBy(t, function(o) {
    return e.node(o).rank;
  });
  return nr.forEach(s, i), a;
}
var fr = ye, o_ = u_;
function u_(e, r) {
  for (var t = 0, n = 1; n < r.length; ++n)
    t += l_(e, r[n - 1], r[n]);
  return t;
}
function l_(e, r, t) {
  for (var n = fr.zipObject(
    t,
    fr.map(t, function(c, l) {
      return l;
    })
  ), a = fr.flatten(fr.map(r, function(c) {
    return fr.sortBy(fr.map(e.outEdges(c), function(l) {
      return { pos: n[l.w], weight: e.edge(l).weight };
    }), "pos");
  }), !0), i = 1; i < t.length; ) i <<= 1;
  var s = 2 * i - 1;
  i -= 1;
  var o = fr.map(new Array(s), function() {
    return 0;
  }), u = 0;
  return fr.forEach(a.forEach(function(c) {
    var l = c.pos + i;
    o[l] += c.weight;
    for (var d = 0; l > 0; )
      l % 2 && (d += o[l + 1]), l = l - 1 >> 1, o[l] += c.weight;
    u += c.weight * d;
  })), u;
}
var ph = ye, c_ = d_;
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
var Fe = ye, f_ = h_;
function h_(e, r) {
  var t = {};
  Fe.forEach(e, function(a, i) {
    var s = t[a.v] = {
      indegree: 0,
      in: [],
      out: [],
      vs: [a.v],
      i
    };
    Fe.isUndefined(a.barycenter) || (s.barycenter = a.barycenter, s.weight = a.weight);
  }), Fe.forEach(r.edges(), function(a) {
    var i = t[a.v], s = t[a.w];
    !Fe.isUndefined(i) && !Fe.isUndefined(s) && (s.indegree++, i.out.push(t[a.w]));
  });
  var n = Fe.filter(t, function(a) {
    return !a.indegree;
  });
  return p_(n);
}
function p_(e) {
  var r = [];
  function t(i) {
    return function(s) {
      s.merged || (Fe.isUndefined(s.barycenter) || Fe.isUndefined(i.barycenter) || s.barycenter >= i.barycenter) && v_(i, s);
    };
  }
  function n(i) {
    return function(s) {
      s.in.push(i), --s.indegree === 0 && e.push(s);
    };
  }
  for (; e.length; ) {
    var a = e.pop();
    r.push(a), Fe.forEach(a.in.reverse(), t(a)), Fe.forEach(a.out, n(a));
  }
  return Fe.map(
    Fe.filter(r, function(i) {
      return !i.merged;
    }),
    function(i) {
      return Fe.pick(i, ["vs", "i", "barycenter", "weight"]);
    }
  );
}
function v_(e, r) {
  var t = 0, n = 0;
  e.weight && (t += e.barycenter * e.weight, n += e.weight), r.weight && (t += r.barycenter * r.weight, n += r.weight), e.vs = r.vs.concat(e.vs), e.barycenter = t / n, e.weight = n, e.i = Math.min(r.i, e.i), r.merged = !0;
}
var Gr = ye, g_ = Me, y_ = m_;
function m_(e, r) {
  var t = g_.partition(e, function(l) {
    return Gr.has(l, "barycenter");
  }), n = t.lhs, a = Gr.sortBy(t.rhs, function(l) {
    return -l.i;
  }), i = [], s = 0, o = 0, u = 0;
  n.sort(b_(!!r)), u = vh(i, a, u), Gr.forEach(n, function(l) {
    u += l.vs.length, i.push(l.vs), s += l.barycenter * l.weight, o += l.weight, u = vh(i, a, u);
  });
  var c = { vs: Gr.flatten(i, !0) };
  return o && (c.barycenter = s / o, c.weight = o), c;
}
function vh(e, r, t) {
  for (var n; r.length && (n = Gr.last(r)).i <= t; )
    r.pop(), e.push(n.vs), t++;
  return t;
}
function b_(e) {
  return function(r, t) {
    return r.barycenter < t.barycenter ? -1 : r.barycenter > t.barycenter ? 1 : e ? t.i - r.i : r.i - t.i;
  };
}
var hr = ye, __ = c_, w_ = f_, I_ = y_, q_ = Qp;
function Qp(e, r, t, n) {
  var a = e.children(r), i = e.node(r), s = i ? i.borderLeft : void 0, o = i ? i.borderRight : void 0, u = {};
  s && (a = hr.filter(a, function(g) {
    return g !== s && g !== o;
  }));
  var c = __(e, a);
  hr.forEach(c, function(g) {
    if (e.children(g.v).length) {
      var f = Qp(e, g.v, t, n);
      u[g.v] = f, hr.has(f, "barycenter") && E_(g, f);
    }
  });
  var l = w_(c, t);
  C_(l, u);
  var d = I_(l, n);
  if (s && (d.vs = hr.flatten([s, d.vs, o], !0), e.predecessors(s).length)) {
    var p = e.node(e.predecessors(s)[0]), v = e.node(e.predecessors(o)[0]);
    hr.has(d, "barycenter") || (d.barycenter = 0, d.weight = 0), d.barycenter = (d.barycenter * d.weight + p.order + v.order) / (d.weight + 2), d.weight += 2;
  }
  return d;
}
function C_(e, r) {
  hr.forEach(e, function(t) {
    t.vs = hr.flatten(t.vs.map(function(n) {
      return r[n] ? r[n].vs : n;
    }), !0);
  });
}
function E_(e, r) {
  hr.isUndefined(e.barycenter) ? (e.barycenter = r.barycenter, e.weight = r.weight) : (e.barycenter = (e.barycenter * e.weight + r.barycenter * r.weight) / (e.weight + r.weight), e.weight += r.weight);
}
var zr = ye, S_ = Ye.Graph, T_ = R_;
function R_(e, r, t) {
  var n = x_(e), a = new S_({ compound: !0 }).setGraph({ root: n }).setDefaultNodeLabel(function(i) {
    return e.node(i);
  });
  return zr.forEach(e.nodes(), function(i) {
    var s = e.node(i), o = e.parent(i);
    (s.rank === r || s.minRank <= r && r <= s.maxRank) && (a.setNode(i), a.setParent(i, o || n), zr.forEach(e[t](i), function(u) {
      var c = u.v === i ? u.w : u.v, l = a.edge(c, i), d = zr.isUndefined(l) ? 0 : l.weight;
      a.setEdge(c, i, { weight: e.edge(u).weight + d });
    }), zr.has(s, "minRank") && a.setNode(i, {
      borderLeft: s.borderLeft[r],
      borderRight: s.borderRight[r]
    }));
  }), a;
}
function x_(e) {
  for (var r; e.hasNode(r = zr.uniqueId("_root")); ) ;
  return r;
}
var k_ = ye, A_ = O_;
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
var vr = ye, P_ = i_, N_ = o_, D_ = q_, M_ = T_, L_ = A_, F_ = Ye.Graph, gh = Me, j_ = $_;
function $_(e) {
  var r = gh.maxRank(e), t = yh(e, vr.range(1, r + 1), "inEdges"), n = yh(e, vr.range(r - 1, -1, -1), "outEdges"), a = P_(e);
  mh(e, a);
  for (var i = Number.POSITIVE_INFINITY, s, o = 0, u = 0; u < 4; ++o, ++u) {
    G_(o % 2 ? t : n, o % 4 >= 2), a = gh.buildLayerMatrix(e);
    var c = N_(e, a);
    c < i && (u = 0, s = vr.cloneDeep(a), i = c);
  }
  mh(e, s);
}
function yh(e, r, t) {
  return vr.map(r, function(n) {
    return M_(e, n, t);
  });
}
function G_(e, r) {
  var t = new F_();
  vr.forEach(e, function(n) {
    var a = n.graph().root, i = D_(n, a, t, r);
    vr.forEach(i.vs, function(s, o) {
      n.node(s).order = o;
    }), L_(n, t, i.vs);
  });
}
function mh(e, r) {
  vr.forEach(r, function(t) {
    vr.forEach(t, function(n, a) {
      e.node(n).order = a;
    });
  });
}
var Q = ye, z_ = Ye.Graph, B_ = Me, U_ = {
  positionX: rw
};
function K_(e, r) {
  var t = {};
  function n(a, i) {
    var s = 0, o = 0, u = a.length, c = Q.last(i);
    return Q.forEach(i, function(l, d) {
      var p = H_(e, l), v = p ? e.node(p).order : u;
      (p || l === c) && (Q.forEach(i.slice(o, d + 1), function(g) {
        Q.forEach(e.predecessors(g), function(f) {
          var y = e.node(f), C = y.order;
          (C < s || v < C) && !(y.dummy && e.node(g).dummy) && Yp(t, f, g);
        });
      }), o = d + 1, s = v);
    }), i;
  }
  return Q.reduce(r, n), t;
}
function V_(e, r) {
  var t = {};
  function n(i, s, o, u, c) {
    var l;
    Q.forEach(Q.range(s, o), function(d) {
      l = i[d], e.node(l).dummy && Q.forEach(e.predecessors(l), function(p) {
        var v = e.node(p);
        v.dummy && (v.order < u || v.order > c) && Yp(t, p, l);
      });
    });
  }
  function a(i, s) {
    var o = -1, u, c = 0;
    return Q.forEach(s, function(l, d) {
      if (e.node(l).dummy === "border") {
        var p = e.predecessors(l);
        p.length && (u = e.node(p[0]).order, n(s, c, d, o, u), c = d, o = u);
      }
      n(s, c, s.length, u, i.length);
    }), s;
  }
  return Q.reduce(r, a), t;
}
function H_(e, r) {
  if (e.node(r).dummy)
    return Q.find(e.predecessors(r), function(t) {
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
  return Q.has(e[r], t);
}
function Q_(e, r, t, n) {
  var a = {}, i = {}, s = {};
  return Q.forEach(r, function(o) {
    Q.forEach(o, function(u, c) {
      a[u] = u, i[u] = u, s[u] = c;
    });
  }), Q.forEach(r, function(o) {
    var u = -1;
    Q.forEach(o, function(c) {
      var l = n(c);
      if (l.length) {
        l = Q.sortBy(l, function(f) {
          return s[f];
        });
        for (var d = (l.length - 1) / 2, p = Math.floor(d), v = Math.ceil(d); p <= v; ++p) {
          var g = l[p];
          i[c] === c && u < s[g] && !W_(t, c, g) && (i[g] = c, i[c] = a[c] = a[g], u = s[g]);
        }
      }
    });
  }), { root: a, align: i };
}
function Y_(e, r, t, n, a) {
  var i = {}, s = J_(e, r, t, a), o = a ? "borderLeft" : "borderRight";
  function u(d, p) {
    for (var v = s.nodes(), g = v.pop(), f = {}; g; )
      f[g] ? d(g) : (f[g] = !0, v.push(g), v = v.concat(p(g))), g = v.pop();
  }
  function c(d) {
    i[d] = s.inEdges(d).reduce(function(p, v) {
      return Math.max(p, i[v.v] + s.edge(v));
    }, 0);
  }
  function l(d) {
    var p = s.outEdges(d).reduce(function(g, f) {
      return Math.min(g, i[f.w] - s.edge(f));
    }, Number.POSITIVE_INFINITY), v = e.node(d);
    p !== Number.POSITIVE_INFINITY && v.borderType !== o && (i[d] = Math.max(i[d], p));
  }
  return u(c, s.predecessors.bind(s)), u(l, s.successors.bind(s)), Q.forEach(n, function(d) {
    i[d] = i[t[d]];
  }), i;
}
function J_(e, r, t, n) {
  var a = new z_(), i = e.graph(), s = tw(i.nodesep, i.edgesep, n);
  return Q.forEach(r, function(o) {
    var u;
    Q.forEach(o, function(c) {
      var l = t[c];
      if (a.setNode(l), u) {
        var d = t[u], p = a.edge(d, l);
        a.setEdge(d, l, Math.max(s(e, c, u), p || 0));
      }
      u = c;
    });
  }), a;
}
function X_(e, r) {
  return Q.minBy(Q.values(r), function(t) {
    var n = Number.NEGATIVE_INFINITY, a = Number.POSITIVE_INFINITY;
    return Q.forIn(t, function(i, s) {
      var o = nw(e, s) / 2;
      n = Math.max(i + o, n), a = Math.min(i - o, a);
    }), n - a;
  });
}
function Z_(e, r) {
  var t = Q.values(r), n = Q.min(t), a = Q.max(t);
  Q.forEach(["u", "d"], function(i) {
    Q.forEach(["l", "r"], function(s) {
      var o = i + s, u = e[o], c;
      if (u !== r) {
        var l = Q.values(u);
        c = s === "l" ? n - Q.min(l) : a - Q.max(l), c && (e[o] = Q.mapValues(u, function(d) {
          return d + c;
        }));
      }
    });
  });
}
function ew(e, r) {
  return Q.mapValues(e.ul, function(t, n) {
    if (r)
      return e[r.toLowerCase()][n];
    var a = Q.sortBy(Q.map(e, n));
    return (a[1] + a[2]) / 2;
  });
}
function rw(e) {
  var r = B_.buildLayerMatrix(e), t = Q.merge(
    K_(e, r),
    V_(e, r)
  ), n = {}, a;
  Q.forEach(["u", "d"], function(s) {
    a = s === "u" ? r : Q.values(r).reverse(), Q.forEach(["l", "r"], function(o) {
      o === "r" && (a = Q.map(a, function(d) {
        return Q.values(d).reverse();
      }));
      var u = (s === "u" ? e.predecessors : e.successors).bind(e), c = Q_(e, a, t, u), l = Y_(
        e,
        a,
        c.root,
        c.align,
        o === "r"
      );
      o === "r" && (l = Q.mapValues(l, function(d) {
        return -d;
      })), n[s + o] = l;
    });
  });
  var i = X_(e, n);
  return Z_(n, i), ew(n, e.graph().align);
}
function tw(e, r, t) {
  return function(n, a, i) {
    var s = n.node(a), o = n.node(i), u = 0, c;
    if (u += s.width / 2, Q.has(s, "labelpos"))
      switch (s.labelpos.toLowerCase()) {
        case "l":
          c = -s.width / 2;
          break;
        case "r":
          c = s.width / 2;
          break;
      }
    if (c && (u += t ? c : -c), c = 0, u += (s.dummy ? r : e) / 2, u += (o.dummy ? r : e) / 2, u += o.width / 2, Q.has(o, "labelpos"))
      switch (o.labelpos.toLowerCase()) {
        case "l":
          c = o.width / 2;
          break;
        case "r":
          c = -o.width / 2;
          break;
      }
    return c && (u += t ? c : -c), c = 0, u;
  };
}
function nw(e, r) {
  return e.node(r).width;
}
var Br = ye, Jp = Me, aw = U_.positionX, iw = sw;
function sw(e) {
  e = Jp.asNonCompoundGraph(e), ow(e), Br.forEach(aw(e), function(r, t) {
    e.node(t).x = r;
  });
}
function ow(e) {
  var r = Jp.buildLayerMatrix(e), t = e.graph().ranksep, n = 0;
  Br.forEach(r, function(a) {
    var i = Br.max(Br.map(a, function(s) {
      return e.node(s).height;
    }));
    Br.forEach(a, function(s) {
      e.node(s).y = n + i / 2;
    }), n += i + t;
  });
}
var X = ye, bh = Jm, _h = pb, uw = Fb, lw = Me.normalizeRanks, cw = zb, dw = Me.removeEmptyRanks, wh = Vb, fw = Xb, Ih = e_, hw = j_, pw = iw, gr = Me, vw = Ye.Graph, gw = yw;
function yw(e, r) {
  var t = r && r.debugTiming ? gr.time : gr.notime;
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
    uw(gr.asNonCompoundGraph(e));
  }), r("    injectEdgeLabelProxies", function() {
    kw(e);
  }), r("    removeEmptyRanks", function() {
    dw(e);
  }), r("    nestingGraph.cleanup", function() {
    wh.cleanup(e);
  }), r("    normalizeRanks", function() {
    lw(e);
  }), r("    assignRankMinMax", function() {
    Aw(e);
  }), r("    removeEdgeLabelProxies", function() {
    Ow(e);
  }), r("    normalize.run", function() {
    _h.run(e);
  }), r("    parentDummyChains", function() {
    cw(e);
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
    Dw(e);
  }), r("    undoCoordinateSystem", function() {
    Ih.undo(e);
  }), r("    translateGraph", function() {
    Pw(e);
  }), r("    assignNodeIntersects", function() {
    Nw(e);
  }), r("    reversePoints", function() {
    Mw(e);
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
var _w = ["nodesep", "edgesep", "ranksep", "marginx", "marginy"], ww = { ranksep: 50, edgesep: 20, nodesep: 50, rankdir: "tb" }, Iw = ["acyclicer", "ranker", "rankdir", "align"], qw = ["width", "height"], Cw = { width: 0, height: 0 }, Ew = ["minlen", "weight", "width", "height", "labeloffset"], Sw = {
  minlen: 1,
  weight: 1,
  width: 0,
  height: 0,
  labeloffset: 10,
  labelpos: "r"
}, Tw = ["labelpos"];
function Rw(e) {
  var r = new vw({ multigraph: !0, compound: !0 }), t = To(e.graph());
  return r.setGraph(X.merge(
    {},
    ww,
    So(t, _w),
    X.pick(t, Iw)
  )), X.forEach(e.nodes(), function(n) {
    var a = To(e.node(n));
    r.setNode(n, X.defaults(So(a, qw), Cw)), r.setParent(n, e.parent(n));
  }), X.forEach(e.edges(), function(n) {
    var a = To(e.edge(n));
    r.setEdge(n, X.merge(
      {},
      Sw,
      So(a, Ew),
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
      gr.addDummyNode(e, "edge-proxy", i, "_ep");
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
  function u(c) {
    var l = c.x, d = c.y, p = c.width, v = c.height;
    r = Math.min(r, l - p / 2), t = Math.max(t, l + p / 2), n = Math.min(n, d - v / 2), a = Math.max(a, d + v / 2);
  }
  X.forEach(e.nodes(), function(c) {
    u(e.node(c));
  }), X.forEach(e.edges(), function(c) {
    var l = e.edge(c);
    X.has(l, "x") && u(l);
  }), r -= s, n -= o, X.forEach(e.nodes(), function(c) {
    var l = e.node(c);
    l.x -= r, l.y -= n;
  }), X.forEach(e.edges(), function(c) {
    var l = e.edge(c);
    X.forEach(l.points, function(d) {
      d.x -= r, d.y -= n;
    }), X.has(l, "x") && (l.x -= r), X.has(l, "y") && (l.y -= n);
  }), i.width = t - r + s, i.height = a - n + o;
}
function Nw(e) {
  X.forEach(e.edges(), function(r) {
    var t = e.edge(r), n = e.node(r.v), a = e.node(r.w), i, s;
    t.points ? (i = t.points[0], s = t.points[t.points.length - 1]) : (t.points = [], i = a, s = n), t.points.unshift(gr.intersectRect(n, i)), t.points.push(gr.intersectRect(a, s));
  });
}
function Dw(e) {
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
function Mw(e) {
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
  var r = gr.buildLayerMatrix(e);
  X.forEach(r, function(t) {
    var n = 0;
    X.forEach(t, function(a, i) {
      var s = e.node(a);
      s.order = i + n, X.forEach(s.selfEdges, function(o) {
        gr.addDummyNode(e, "selfedge", {
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
function So(e, r) {
  return X.mapValues(X.pick(e, r), Number);
}
function To(e) {
  var r = {};
  return X.forEach(e, function(t, n) {
    r[n.toLowerCase()] = t;
  }), r;
}
var et = ye, Gw = Me, zw = Ye.Graph, Bw = {
  debugOrdering: Uw
};
function Uw(e) {
  var r = Gw.buildLayerMatrix(e), t = new zw({ compound: !0, multigraph: !0 }).setGraph({});
  return et.forEach(e.nodes(), function(n) {
    t.setNode(n, { label: n }), t.setParent(n, "layer" + e.node(n).rank);
  }), et.forEach(e.edges(), function(n) {
    t.setEdge(n.v, n.w, {}, n.name);
  }), et.forEach(r, function(n, a) {
    var i = "layer" + a;
    t.setNode(i, { rank: "same" }), et.reduce(n, function(s, o) {
      return t.setEdge(s, o, { style: "invis" }), o;
    });
  }), t;
}
var Kw = "0.8.5", Vw = {
  graphlib: Ye,
  layout: gw,
  debug: Bw,
  util: {
    time: Me.time,
    notime: Me.notime
  },
  version: Kw
};
const qh = /* @__PURE__ */ Yv(Vw), Ur = 148, Kr = 46, su = 80, ou = 44;
function Hw(e) {
  if (e.nodes.length === 0) return e;
  const r = new qh.graphlib.Graph();
  r.setDefaultEdgeLabel(() => ({})), r.setGraph({ rankdir: "LR", nodesep: 60, ranksep: 120, marginx: 40, marginy: 40 });
  for (const n of e.nodes) r.setNode(n.id, { width: Ur, height: Kr });
  for (const n of e.edges) r.setEdge(n.source, n.target);
  qh.layout(r);
  const t = {};
  for (const n of e.nodes) {
    const a = r.node(n.id);
    t[n.id] = a ? { x: Math.round(a.x - Ur / 2), y: Math.round(a.y - Kr / 2) } : { x: n.x, y: n.y };
  }
  return {
    ...e,
    nodes: e.nodes.map((n) => ({ ...n, ...t[n.id] })),
    layout: { ...e.layout, positions: t }
  };
}
const Xp = (e, r) => Math.abs(e.x - r.x) < Ur + su && Math.abs(e.y - r.y) < Kr + ou;
function Ww(e) {
  const r = [], t = e.nodes.map((a) => {
    let i = Number.isFinite(a.x) ? a.x : 0, s = Number.isFinite(a.y) ? a.y : 0, o = 0;
    for (; r.some((u) => Xp({ x: i, y: s }, u)) && o++ < 200; )
      i += Ur + su, i > 2400 && (i = 80, s += Kr + ou);
    return r.push({ x: i, y: s }), { ...a, x: i, y: s };
  });
  if (t.every((a, i) => a.x === e.nodes[i].x && a.y === e.nodes[i].y)) return e;
  const n = Object.fromEntries(t.map((a) => [a.id, { x: a.x, y: a.y }]));
  return { ...e, nodes: t, layout: { ...e.layout, positions: n } };
}
function Ro(e, r, t) {
  let n = { x: Math.round(r), y: Math.round(t) }, a = 0;
  for (; e.nodes.some((i) => Xp(n, i)) && a++ < 200; )
    n = { x: n.x + Ur + su, y: n.y }, n.x > 2400 && (n = { x: 80, y: n.y + Kr + ou });
  return n;
}
const { Header: Qw, Content: Yw } = tv;
function Jw({ ctx: e }) {
  var uu, lu;
  const [r, t] = nv.useMessage(), [n, a] = q([]), [i, s] = q([]), [o, u] = q([]), [c, l] = q([]), [d, p] = q(""), [v, g] = q(1), [f, y] = q(!0), [C, R] = q([]), [_, k] = q(() => tt()), [N, m] = q(null), [S, D] = q(null), [U, z] = q([]), [I, M] = q(!1), [j, J] = q(!0), [re, K] = q(!1), [Y, W] = q(!1), [x, $] = q(0), P = ke([]), ne = ke([]), le = ke(null), Ue = ke(null), [Te, mr] = q(null), [Dr, br] = q({}), [Rr, _r] = q(!1), [ue, Ke] = q(!1), ce = ke(1), he = ke(e);
  he.current = e;
  const ee = ke(_.trigger);
  _e(() => {
    ee.current = _.trigger;
  }, [_.trigger]), _e(() => {
    let E = !0;
    const O = he.current.hubQuery;
    return Promise.allSettled([
      O("industrial/nodes"),
      O("industrial/workflows"),
      O("industrial/resources")
    ]).then(async ([oe, ie, pe]) => {
      var Xe;
      if (!E) return;
      const ve = [];
      if (oe.status === "fulfilled" ? a(Ah(oe.value)) : ve.push(`节点描述: ${String(oe.reason)}`), ie.status === "fulfilled") {
        const be = Nh(ie.value);
        s(be);
        const Ee = new URLSearchParams(window.location.search).get("workflowId"), Le = Ee && be.some((Ze) => Ze.workflowId === Ee) ? Ee : (Xe = be[0]) == null ? void 0 : Xe.workflowId;
        Le && Ve(Le);
      } else ve.push(`工作流: ${String(ie.reason)}`);
      if (pe.status === "fulfilled") {
        const be = Oh(pe.value);
        l(be);
        const Ee = be.map((Le) => Le.id);
        u(Ee), p((Le) => Le || Ee[0] || "");
      } else ve.push(`资源: ${String(pe.reason)}`);
      R(ve);
      try {
        const be = await he.current.hubQuery("industrial/file-samples");
        E && br((be == null ? void 0 : be.data) ?? be ?? {});
      } catch {
      }
    }).finally(() => {
      E && y(!1);
    }), () => {
      E = !1;
    };
  }, []);
  const me = lt(
    () => new Map(n.map((E) => [E.nodeTypeId, E])),
    [n]
  ), Z = _.nodes.find((E) => E.id === N) ?? null, Re = F(async (E, O, oe) => {
    M(!0);
    try {
      return await De().api.hubCommand(E, O), r.success(oe), !0;
    } catch (ie) {
      return r.error(`${E} failed: ${ie instanceof Error ? ie.message : String(ie)}`), !1;
    } finally {
      M(!1);
    }
  }, [r]), Ve = F(async (E) => {
    try {
      const O = await e.hubQuery("industrial/workflow-definitions"), ie = (Ae(O) ?? []).find((be) => {
        const Ee = be;
        return String((Ee == null ? void 0 : Ee.workflowId) ?? "") === E;
      }), pe = ie === void 0 ? { ...tt(E), name: E } : Mv(ie), ve = Ww(pe);
      k(ve), P.current = [], ne.current = [], $((be) => be + 1), g(Number(ve.layout.lookbackDays ?? 1) || 1);
      const Xe = pe.nodes.map((be) => /^n(\d+)$/.exec(be.id)).filter(Boolean).reduce((be, Ee) => Math.max(be, Number(Ee[1])), 0);
      ce.current = Xe + 1, m(null), K(!1), z([]), W(!1);
    } catch (O) {
      r.error(`加载工作流失败: ${O instanceof Error ? O.message : String(O)}`);
    }
  }, [e, r]), cr = F(async () => {
    try {
      const E = Bv(await e.hubQuery("industrial/execution"));
      if (E === null) return;
      mr(E);
    } catch {
    }
  }, [e]), Je = F(async () => {
    var oe;
    const E = new Set(_.resourceReferences ?? []);
    for (const ie of _.nodes)
      for (const pe of ["resourceId", "targetResourceId"]) {
        const ve = (oe = ie.config) == null ? void 0 : oe[pe];
        typeof ve == "string" && ve.trim() && E.add(ve.trim());
      }
    const O = {
      ..._,
      trigger: ee.current,
      resourceReferences: [...E],
      layout: { ..._.layout, lookbackDays: v }
    };
    await Re("industrial/save-workflow", O, "已保存") && (k(O), W(!1));
  }, [Re, _, v]), $e = F(async () => {
    if (Y) {
      r.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await Re("industrial/publish-workflow", { workflowId: _.workflowId }, "已发布") && await cr();
  }, [Re, _.workflowId, Y, r, cr]), qe = F(async () => {
    if (Y) {
      r.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await Re(
      "industrial/run-workflow",
      { workflowId: _.workflowId, resourceId: d },
      "采集已启动"
    ) && await cr();
  }, [Re, _.workflowId, Y, r, cr, d]), se = F((E) => {
    E !== _ && (P.current.length >= 50 && P.current.shift(), P.current.push(_), ne.current = [], $((O) => O + 1), k(E), W(!0));
  }, [_]), He = F(() => {
    const E = P.current.pop();
    E && (ne.current.push(_), k(E), W(!0), $((O) => O + 1));
  }, [_]), dr = F(() => {
    const E = ne.current.pop();
    E && (P.current.push(_), k(E), W(!0), $((O) => O + 1));
  }, [_]);
  _e(() => {
    const E = (O) => {
      const oe = O.target;
      if (oe && /INPUT|TEXTAREA|SELECT/.test(oe.tagName)) return;
      const ie = O.key.toLowerCase();
      if ((O.ctrlKey || O.metaKey) && ie === "z") {
        O.preventDefault(), O.shiftKey ? dr() : He();
        return;
      }
      if ((O.ctrlKey || O.metaKey) && ie === "y") {
        O.preventDefault(), dr();
        return;
      }
      if (O.key === "Delete" && N) {
        O.preventDefault(), se(Ot(_, N)), m(null);
        return;
      }
      if ((O.ctrlKey || O.metaKey) && ie === "c" && N) {
        Ue.current = _.nodes.find((pe) => pe.id === N) ?? null;
        return;
      }
      if ((O.ctrlKey || O.metaKey) && ie === "v" && Ue.current) {
        O.preventDefault();
        const pe = ar(me, Ue.current.type);
        if (!pe) return;
        const ve = `n${ce.current++}`, Xe = Ro(_, Ue.current.x + 40, Ue.current.y + 40);
        se({
          ..._,
          nodes: [..._.nodes, At(pe, ve, Xe.x, Xe.y)]
        }), m(ve);
      }
    };
    return window.addEventListener("keydown", E), () => window.removeEventListener("keydown", E);
  }, [me, se, _, dr, N, He]);
  const w = F(() => {
    se(Hw(_)), requestAnimationFrame(() => {
      var E, O;
      return (O = (E = le.current) == null ? void 0 : E.fitView) == null ? void 0 : O.call(E, { when: "always" });
    });
  }, [se, _]), B = F((E, O, oe) => {
    const ie = ar(me, E);
    if (!ie) return;
    const pe = `n${ce.current++}`, ve = Ro(_, O, oe);
    se({ ..._, nodes: [..._.nodes, At(ie, pe, ve.x, ve.y)] }), m(pe), K(!0), z([]);
  }, [me, se, _]), de = F((E) => {
    const O = ce.current - 1;
    B(E, 80 + O % 3 * 190, 80 + Math.floor(O / 3) * 130);
  }, [B]), Ce = F((E, O) => {
    const oe = _.nodes.find((Ze) => Ze.id === E), ie = _.nodes.find((Ze) => Ze.id === O);
    if (!oe || !ie)
      return r.warning("连线失败：端点不在画布上"), null;
    const pe = ar(me, oe.type), ve = ar(me, ie.type);
    if (!pe || !ve) return null;
    let Xe = oe, be = ie, Ee = vu(pe, ve);
    if (!Ee) {
      const Ze = vu(ve, pe);
      if (!Ze)
        return r.warning(`端口类型不兼容：${pe.name} → ${ve.name}`), null;
      Xe = ie, be = oe, Ee = Ze;
    }
    const Le = pv(_, {
      source: Xe.id,
      sourcePort: Ee.sourcePort,
      target: be.id,
      targetPort: Ee.targetPort
    }, me);
    return Le.issue ? (r.warning(`${Le.issue.code}: ${Le.issue.message}`), z((Ze) => [...Ze, Le.issue]), null) : Le.definition === _ ? (r.info("这条连线已经存在"), null) : (se(Le.definition), Ee);
  }, [me, se, _, r]), xe = F(() => {
    const E = bv(_, me, new Set(o));
    z(E), E.length === 0 ? r.success("本地校验通过（发布时服务端仍会复核）") : (r.warning(`发现 ${E.length} 个问题，见属性抽屉定位列表`), K(!0));
  }, [_, me, o, r]), We = F((E) => {
    se(Ot(_, E)), N === E && m(null);
  }, [se, _, N]), Ge = F((E) => {
    const O = _.nodes.find((ve) => ve.id === E);
    if (!O) return;
    const oe = ar(me, O.type);
    if (!oe) return;
    const ie = `n${ce.current++}`, pe = Ro(_, O.x + 40, O.y + 40);
    se({ ..._, nodes: [
      ..._.nodes,
      { ...At(oe, ie, pe.x, pe.y), config: { ...O.config } }
    ] }), m(ie);
  }, [me, se, _]), tr = F((E) => {
    const O = _.edges.findIndex((oe) => `e:${oe.source}:${oe.sourcePort}>${oe.target}:${oe.targetPort}` === E);
    O >= 0 && se(gu(_, O));
  }, [se, _]), Zp = F((E, O) => {
    se({
      ..._,
      edges: _.edges.map((oe, ie) => ie === E ? O : oe)
    });
  }, [se, _]);
  return f ? /* @__PURE__ */ h(Sh, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ T("div", { className: "industrial-designer", children: [
    t,
    /* @__PURE__ */ T(Qw, { className: "industrial-header", children: [
      /* @__PURE__ */ T("div", { className: "industrial-title", children: [
        /* @__PURE__ */ h(G.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ h(G.Text, { type: "secondary", children: _.name || _.workflowId }),
        Y && /* @__PURE__ */ h(je, { color: "orange", children: "未保存" }),
        Te && /* @__PURE__ */ h(je, { color: Te.error ? "error" : "success", children: Te.status })
      ] }),
      /* @__PURE__ */ T("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ h(
          L,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": j,
            onClick: () => J((E) => !E),
            children: j ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ h(
          L,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => K((E) => !E),
            children: "属性"
          }
        ),
        /* @__PURE__ */ h(L, { size: "small", "data-testid": "open-trigger", onClick: () => _r(!0), children: "触发器" }),
        /* @__PURE__ */ h(
          L,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => Ke(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ h(
          L,
          {
            size: "small",
            "data-testid": "designer-undo",
            disabled: P.current.length === 0,
            onClick: He,
            children: "撤销"
          }
        ),
        /* @__PURE__ */ h(
          L,
          {
            size: "small",
            "data-testid": "designer-redo",
            disabled: ne.current.length === 0,
            onClick: dr,
            children: "重做"
          }
        ),
        /* @__PURE__ */ h(L, { size: "small", "data-testid": "designer-auto-layout", onClick: w, children: "自动布局" }),
        /* @__PURE__ */ h(L, { size: "small", "data-testid": "designer-fit", onClick: () => {
          var E, O;
          return (O = (E = le.current) == null ? void 0 : E.fitView) == null ? void 0 : O.call(E, { when: "always" });
        }, children: "适应画布" }),
        /* @__PURE__ */ h(L, { size: "small", "data-testid": "designer-center", onClick: () => {
          var E, O;
          return (O = (E = le.current) == null ? void 0 : E.fitCenter) == null ? void 0 : O.call(E);
        }, children: "居中" }),
        /* @__PURE__ */ h("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ T(H, { size: 4, children: [
          /* @__PURE__ */ h(L, { size: "small", loading: I, onClick: () => void Je(), children: "保存" }),
          /* @__PURE__ */ h(L, { size: "small", onClick: xe, children: "校验" }),
          /* @__PURE__ */ h(L, { size: "small", loading: I, onClick: () => void $e(), children: "发布" }),
          /* @__PURE__ */ h(L, { size: "small", type: "primary", loading: I, onClick: () => void qe(), children: "运行" })
        ] }),
        /* @__PURE__ */ T(
          "select",
          {
            className: "industrial-resource-select",
            "aria-label": "运行资源",
            value: d,
            onChange: (E) => p(E.target.value),
            children: [
              /* @__PURE__ */ h("option", { value: "", children: "运行资源" }),
              o.map((E) => /* @__PURE__ */ h("option", { value: E, children: E }, E))
            ]
          }
        ),
        /* @__PURE__ */ h(
          rt,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: v,
            onChange: (E) => {
              g(Number(E) || 1), W(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ T(Yw, { className: `industrial-body${j ? "" : " industrial-body--collapsed"}`, children: [
      j ? /* @__PURE__ */ T("aside", { className: "industrial-palette", children: [
        i.length > 0 && /* @__PURE__ */ h(
          Se,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: _.workflowId,
            placeholder: "选择工作流",
            onChange: (E) => void Ve(E),
            options: i.map((E) => ({ value: E.workflowId, label: E.name }))
          }
        ),
        /* @__PURE__ */ h(dv, { descriptors: n, loading: !1, onAdd: de })
      ] }) : /* @__PURE__ */ h("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ h(L, { type: "text", size: "small", onClick: () => J(!0), children: "节点库" }) }),
      /* @__PURE__ */ T("section", { className: "industrial-center", children: [
        C.length > 0 && /* @__PURE__ */ h(
          Pe,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: C.join("；")
          }
        ),
        /* @__PURE__ */ h(
          cv,
          {
            definition: _,
            catalog: me,
            selectedId: N,
            onSelect: (E) => {
              m(E), E && K(!0);
            },
            onDropNode: B,
            onCreateEdge: Ce,
            onPositions: (E) => se(yv(_, E)),
            onSelectIds: (E) => {
              E.length && m(E[0]);
            },
            onGraphReady: (E) => {
              le.current = E;
            },
            onSelectEdge: (E) => D(E),
            onDeleteNodeById: We,
            onDuplicateNode: Ge,
            onDeleteEdgeById: tr
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ T(
      rv,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: re,
        mask: !1,
        getContainer: !1,
        onClose: () => K(!1),
        children: [
          Te && /* @__PURE__ */ h(
            Pe,
            {
              style: { marginBottom: 12 },
              type: Te.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${Te.status}`,
              description: /* @__PURE__ */ T(H, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                Te.error && /* @__PURE__ */ h("span", { children: Te.error }),
                Te.executed.length > 0 && /* @__PURE__ */ T("span", { children: [
                  "已执行节点: ",
                  Te.executed.join(", ")
                ] }),
                Te.nodes.length > 0 && /* @__PURE__ */ h(
                  Ne,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: Te.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (E, O) => O.error ?? (O.samples.length > 0 ? O.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ h(
            Tv,
            {
              node: Z,
              descriptor: Z ? ar(me, Z.type) ?? null : null,
              nodes: _.nodes,
              edges: _.edges,
              catalog: me,
              resources: c,
              issues: U,
              onApplyConfig: (E) => {
                N && se(gv(_, N, E));
              },
              onDeleteNode: () => {
                se(Ot(_, N ?? "")), m(null);
              },
              onDeleteEdge: (E) => se(gu(_, E)),
              onCreateEdge: (E, O) => void Ce(E, O),
              onUpdateEdge: Zp,
              selectedEdgeId: S,
              sampleLines: Z ? Dr[String(((uu = Z.config) == null ? void 0 : uu.resourceId) ?? ((lu = Z.config) == null ? void 0 : lu.targetResourceId) ?? "")] ?? [] : [],
              onSelectNode: (E) => {
                m(E), K(!0);
              }
            }
          )
        ]
      }
    ),
    Rr && /* @__PURE__ */ h(
      Vv,
      {
        open: Rr,
        onClose: () => _r(!1),
        trigger: _.trigger,
        resourceIds: o,
        workflowId: _.workflowId,
        ctx: e,
        onSave: (E) => {
          ee.current = E, se({ ..._, trigger: E });
        }
      }
    ),
    ue && /* @__PURE__ */ h(
      Qv,
      {
        open: ue,
        onClose: () => Ke(!1),
        ctx: e,
        workflowId: _.workflowId,
        resourceId: d,
        lookbackDays: v
      }
    )
  ] });
}
function Xw({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q(!1), [s, o] = q(""), [u, c] = q(null), [l, d] = q(!1), p = F(async () => {
    try {
      n(Nh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
    } catch (f) {
      c(f instanceof Error ? f.message : String(f));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    p();
  }, [p]);
  const v = F(async () => {
    var y;
    const f = s.trim();
    if (!f) {
      c("请输入工作流名称");
      return;
    }
    d(!0), c(null);
    try {
      const R = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await r.hubCommand("industrial/save-workflow", {
        ...tt(R),
        name: f,
        layout: { lookbackDays: 1 }
      }), i(!1), o(""), await p(), (y = r.navigate) == null || y.call(r, `/industrial/designer?workflowId=${encodeURIComponent(R)}`);
    } catch (C) {
      c(C instanceof Error ? C.message : String(C));
    } finally {
      d(!1);
    }
  }, [r, p, s]), g = F((f) => {
    var y;
    (y = r.navigate) == null || y.call(r, `/industrial/designer?workflowId=${encodeURIComponent(f)}`);
  }, [r]);
  return /* @__PURE__ */ T(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ T(H, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ h(G.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ h(L, { type: "primary", onClick: () => i(!0), children: "新建工作流" })
    ] }),
    u && /* @__PURE__ */ h(G.Text, { type: "danger", children: u }),
    /* @__PURE__ */ h(
      Ne,
      {
        rowKey: "workflowId",
        dataSource: t,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (f) => /* @__PURE__ */ h(L, { type: "link", style: { padding: 0 }, onClick: () => g(f), children: f })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ h(
      Ir,
      {
        open: a,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: l,
        onCancel: () => i(!1),
        onOk: () => void v(),
        children: /* @__PURE__ */ T(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ h(G.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: s,
              onChange: (f) => o(f.target.value ?? "")
            }
          ),
          u && /* @__PURE__ */ h(G.Text, { type: "danger", children: u })
        ] })
      }
    )
  ] });
}
const xo = {
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
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q([]), [u, c] = q(xo), [l, d] = q(!1), [p, v] = q(null), [g, f] = q(!1), y = F(async () => {
    try {
      const [m, S, D] = await Promise.all([
        r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        r.hubQuery("industrial/credentials"),
        r.hubQuery("industrial/resource-tests")
      ]);
      n(Lv(m)), i(Dh(S)), o(Fv(D));
    } catch (m) {
      v(m instanceof Error ? m.message : String(m));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    y();
  }, [y]);
  const C = F((m) => {
    const S = (...D) => {
      for (const U of D) {
        const z = m.config[U];
        if (z != null && String(z).trim()) return String(z);
      }
      return "";
    };
    c({
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
  }, []), R = F(async () => {
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
      }), d(!1), c(xo), await y();
    } catch (S) {
      v(S instanceof Error ? S.message : String(S));
    } finally {
      f(!1);
    }
  }, [r, u, y]), _ = F(async (m) => {
    v(null);
    try {
      await r.hubCommand("industrial/test-resource", { id: m });
    } catch (S) {
      v(S instanceof Error ? S.message : String(S));
    }
    await y();
  }, [r, y]), k = F(async (m) => {
    v(null);
    try {
      await r.hubCommand("industrial/delete-resource", { id: m }), await y();
    } catch (S) {
      v(S instanceof Error ? S.message : String(S));
    }
  }, [r, y]), N = new Map(s.map((m) => [m.resourceId, m]));
  return /* @__PURE__ */ T(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ T(H, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ h(G.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ T(H, { children: [
        /* @__PURE__ */ h(L, { onClick: () => {
          var m;
          return (m = r.navigate) == null ? void 0 : m.call(r, "/industrial/designer");
        }, children: "返回设计器" }),
        /* @__PURE__ */ h(L, { type: "primary", onClick: () => {
          c(xo), d(!0);
        }, children: "新建资源" })
      ] })
    ] }),
    p && /* @__PURE__ */ h(Pe, { type: "error", showIcon: !0, message: p }),
    /* @__PURE__ */ h(
      Ne,
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
              const D = N.get(S.id);
              return D ? /* @__PURE__ */ h(G.Text, { type: D.ok ? "success" : "danger", children: D.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (m, S) => /* @__PURE__ */ T(H, { size: 4, children: [
              /* @__PURE__ */ h(L, { size: "small", onClick: () => void _(S.id), children: "测试连接" }),
              /* @__PURE__ */ h(L, { size: "small", onClick: () => C(S), children: "编辑" }),
              /* @__PURE__ */ h(L, { size: "small", danger: !0, onClick: () => void k(S.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ h(
      Ir,
      {
        open: l,
        title: u.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: g,
        onCancel: () => d(!1),
        onOk: () => void R(),
        children: /* @__PURE__ */ T(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: u.id,
              onChange: (m) => c({ ...u, id: m.target.value })
            }
          ),
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: u.name,
              onChange: (m) => c({ ...u, name: m.target.value })
            }
          ),
          /* @__PURE__ */ h(G.Text, { children: "资源类型" }),
          /* @__PURE__ */ h(H, { wrap: !0, children: Zw.map((m) => /* @__PURE__ */ h(
            L,
            {
              size: "small",
              type: u.type === m.value ? "primary" : "default",
              onClick: () => c({ ...u, type: m.value }),
              children: m.label
            },
            m.value
          )) }),
          (u.type === "filesystem.local" || u.type === "filesystem.unc") && /* @__PURE__ */ h(
            V,
            {
              "aria-label": "目录路径",
              placeholder: u.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: u.root,
              onChange: (m) => c({ ...u, root: m.target.value })
            }
          ),
          (u.type === "sqlserver" || u.type === "postgresql" || u.type === "mysql") && /* @__PURE__ */ T(H, { children: [
            /* @__PURE__ */ h(
              V,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: u.host,
                onChange: (m) => c({ ...u, host: m.target.value })
              }
            ),
            /* @__PURE__ */ h(
              V,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: u.database,
                onChange: (m) => c({ ...u, database: m.target.value })
              }
            ),
            /* @__PURE__ */ h(
              V,
              {
                "aria-label": "端口",
                placeholder: "端口",
                value: u.port,
                onChange: (m) => c({ ...u, port: m.target.value })
              }
            )
          ] }),
          u.type === "sqlite" && /* @__PURE__ */ h(
            V,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: u.database,
              onChange: (m) => c({ ...u, database: m.target.value })
            }
          ),
          /* @__PURE__ */ h(
            Se,
            {
              "aria-label": "凭证",
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: u.credentialId || void 0,
              options: a.map((m) => ({ value: m.id, label: m.name })),
              onChange: (m) => c({ ...u, credentialId: m ?? "" })
            }
          ),
          /* @__PURE__ */ T(H, { children: [
            /* @__PURE__ */ h(
              ct,
              {
                "aria-label": "启用资源",
                checked: u.enabled,
                onChange: (m) => c({ ...u, enabled: m })
              }
            ),
            /* @__PURE__ */ h(G.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
const ko = ["username-password", "domain-account", "api-token"];
function t1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q(ko), [s, o] = q(!1), [u, c] = q(""), [l, d] = q(""), [p, v] = q(ko[0]), [g, f] = q(""), [y, C] = q(""), [R, _] = q(""), [k, N] = q(""), [m, S] = q(""), [D, U] = q(!1), [z, I] = q(null), [M, j] = q(!1), J = F(async () => {
    try {
      n(Dh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
      const P = await r.hubQuery("industrial/credential-types"), ne = Array.isArray(P) ? P : Array.isArray(P == null ? void 0 : P.data) ? P.data : [];
      ne.length && i(ne.map((le) => le.kind));
    } catch (P) {
      I(P instanceof Error ? P.message : String(P));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    J();
  }, [J]);
  const re = F(() => {
    c(""), d(""), v(ko[0]), f(""), C(""), _(""), N(""), S(""), U(!1), I(null);
  }, []), K = F(() => {
    re(), o(!0);
  }, [re]), Y = F((P) => {
    re(), c(P.id), d(P.name), a.includes(P.kind) && v(P.kind), U(!0), o(!0);
  }, [a, re]), W = F(() => m.trim() ? m.trim() : p === "api-token" ? k.trim() ? JSON.stringify({ token: k.trim() }) : null : !g.trim() || !y.trim() ? null : JSON.stringify(p === "domain-account" ? { username: g.trim(), password: y.trim(), domain: R.trim() } : { username: g.trim(), password: y.trim() }), [R, p, y, m, k, g]), x = F(async () => {
    if (!u.trim()) {
      I("请输入凭证 ID");
      return;
    }
    const P = W();
    if (!P) {
      I("请填写凭证字段，或在高级内容中提供凭证");
      return;
    }
    j(!0), I(null);
    try {
      D ? await r.hubCommand("industrial/rotate-credential", { id: u, secret: P }) : await r.hubCommand("industrial/save-credential", { id: u, name: l || u, kind: p, secret: P }), o(!1), await J();
    } catch (ne) {
      I(ne instanceof Error ? ne.message : String(ne));
    } finally {
      j(!1);
    }
  }, [W, r, u, p, J, l, D, m]), $ = F(async (P) => {
    I(null);
    try {
      await r.hubCommand("industrial/delete-credential", { id: P.id }), await J();
    } catch (ne) {
      I(ne instanceof Error ? ne.message : String(ne));
    }
  }, [r, J]);
  return /* @__PURE__ */ T(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ T(H, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ h(G.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ h(L, { type: "primary", onClick: K, children: "新建凭证" })
    ] }),
    z && /* @__PURE__ */ h(Pe, { type: "error", showIcon: !0, message: z }),
    /* @__PURE__ */ h(
      Ne,
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
            render: (P, ne) => /* @__PURE__ */ T(H, { children: [
              /* @__PURE__ */ h(L, { size: "small", onClick: () => Y(ne), children: "轮换" }),
              /* @__PURE__ */ h(L, { size: "small", danger: !0, onClick: () => void $(ne), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ h(
      Ir,
      {
        open: s,
        title: D ? "轮换凭证" : "新建凭证",
        okText: D ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: M,
        onCancel: () => o(!1),
        onOk: () => void x(),
        children: /* @__PURE__ */ T(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: u,
              disabled: D,
              onChange: (P) => c(P.target.value)
            }
          ),
          !D && /* @__PURE__ */ h(
            V,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: l,
              onChange: (P) => d(P.target.value)
            }
          ),
          /* @__PURE__ */ h(
            Se,
            {
              "aria-label": "凭证类型",
              style: { width: "100%" },
              value: p,
              onChange: (P) => v(P),
              options: a.map((P) => ({ value: P, label: P }))
            }
          ),
          p === "api-token" ? /* @__PURE__ */ h(
            V,
            {
              "aria-label": "令牌",
              placeholder: "令牌",
              value: k,
              onChange: (P) => N(P.target.value)
            }
          ) : /* @__PURE__ */ T(pr, { children: [
            /* @__PURE__ */ h(
              V,
              {
                "aria-label": "用户名",
                placeholder: "用户名",
                value: g,
                onChange: (P) => f(P.target.value)
              }
            ),
            /* @__PURE__ */ h(
              V,
              {
                "aria-label": "密码",
                placeholder: "密码",
                value: y,
                onChange: (P) => C(P.target.value)
              }
            ),
            p === "domain-account" && /* @__PURE__ */ h(
              V,
              {
                "aria-label": "域",
                placeholder: "域（可选）",
                value: R,
                onChange: (P) => _(P.target.value)
              }
            )
          ] }),
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "凭证内容",
              placeholder: "高级：直接提供凭证内容（优先）",
              value: m,
              onChange: (P) => S(P.target.value)
            }
          ),
          z && /* @__PURE__ */ h(G.Text, { type: "danger", children: z })
        ] })
      }
    )
  ] });
}
const Ch = (e) => e === "Succeeded" || e === "Completed" ? "success" : e === "Failed" || e === "PartialFailure" ? "error" : e === "Running" || e === "Interrupted" ? "processing" : "default";
function n1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q(null), [u, c] = q(null), [l, d] = q(!1), p = F(async () => {
    try {
      const g = zv(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/execution-center"));
      n(g.runs), i(g.deliveries);
    } catch (g) {
      o(g instanceof Error ? g.message : String(g));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    p();
  }, [p]);
  const v = F(async (g) => {
    d(!0), o(null), c(null);
    try {
      await r.hubCommand("industrial/retry-failed", {
        deliveryId: g.id,
        targetId: g.targetId
      }), c(`已提交目标 ${g.targetId} 的重试`), await p();
    } catch (f) {
      o(f instanceof Error ? f.message : String(f));
    } finally {
      d(!1);
    }
  }, [r, p]);
  return /* @__PURE__ */ T(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(G.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    s && /* @__PURE__ */ h(Pe, { type: "error", showIcon: !0, message: s }),
    u && /* @__PURE__ */ h(Pe, { type: "success", showIcon: !0, message: u }),
    /* @__PURE__ */ h(
      Ne,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ h(je, { color: Ch(g), children: g }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (g) => g ?? "—" },
          { title: "错误", dataIndex: "error", render: (g) => g ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ h(
      Ne,
      {
        rowKey: "id",
        dataSource: a,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ h(je, { color: Ch(g), children: g }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (g, f) => f.diagnosis ? /* @__PURE__ */ T(H, { direction: "vertical", size: 0, children: [
              /* @__PURE__ */ h(G.Text, { children: f.diagnosis.message }),
              /* @__PURE__ */ h(G.Text, { type: "secondary", children: f.diagnosis.action })
            ] }) : f.error ?? "—"
          },
          {
            title: "操作",
            key: "actions",
            render: (g, f) => /* @__PURE__ */ h(
              L,
              {
                size: "small",
                disabled: l || f.status !== "Failed" && f.status !== "Interrupted",
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
  const [t, n] = q([]), [a, i] = q(null), s = F(async () => {
    try {
      n(Gv(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (u) {
      i(u instanceof Error ? u.message : String(u));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    s();
  }, [s]);
  const o = (u) => /* @__PURE__ */ h(
    Ne,
    {
      rowKey: "id",
      dataSource: u.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (c) => /* @__PURE__ */ h(je, { children: c }) },
        { title: "记录", dataIndex: "records" },
        {
          title: "接受/拒绝/警告",
          key: "counts",
          render: (c, l) => `${l.accepted} / ${l.rejected} / ${l.warnings}`
        },
        {
          title: "原因与下一步",
          key: "diagnosis",
          render: (c, l) => l.diagnosis ? /* @__PURE__ */ T(H, { direction: "vertical", size: 0, children: [
            /* @__PURE__ */ h(G.Text, { children: l.diagnosis.message }),
            /* @__PURE__ */ h(G.Text, { type: "secondary", children: l.diagnosis.action }),
            l.error && /* @__PURE__ */ h(G.Text, { type: "secondary", children: l.error })
          ] }) : l.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ T(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(G.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    a && /* @__PURE__ */ h(Pe, { type: "error", showIcon: !0, message: a }),
    /* @__PURE__ */ h(
      Ne,
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
            render: (u, c) => c.deliveries.length
          }
        ]
      }
    )
  ] });
}
const Ao = (e) => {
  const r = Ae(e);
  return Array.isArray(r) ? r : [];
}, i1 = () => `machine-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 12) : Date.now().toString(36)}`;
function s1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q([]), [u, c] = q(null), [l, d] = q(""), [p, v] = q(""), [g, f] = q(), [y, C] = q(!0), [R, _] = q(null), k = F(async () => {
    try {
      n(Ao(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/machines"))), i(Ao(await r.hubQuery("industrial/resources"))), o(Ao(await r.hubQuery("industrial/credentials")));
    } catch (I) {
      _(I instanceof Error ? I.message : String(I));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    k();
  }, [k]);
  const N = F(() => {
    c({ id: i1(), name: "", endpoint: "", credentialId: null, enabled: !0 }), d(""), v(""), f(void 0), C(!0), _(null);
  }, []), m = F((I) => {
    c(I), d(I.name), v(I.endpoint), f(I.credentialId ?? void 0), C(I.enabled), _(null);
  }, []), S = F(async () => {
    if (u) {
      if (!l.trim()) {
        _("请输入设备名称");
        return;
      }
      _(null);
      try {
        await r.hubCommand("industrial/save-machine", {
          id: u.id,
          name: l.trim(),
          endpoint: p.trim(),
          credentialId: g ?? null,
          enabled: y
        }), c(null), await k();
      } catch (I) {
        _(I instanceof Error ? I.message : String(I));
      }
    }
  }, [r, g, u, p, y, k, l]), D = F(async (I) => {
    _(null);
    try {
      await r.hubCommand("industrial/delete-machine", { id: I.id }), await k();
    } catch (M) {
      _(M instanceof Error ? M.message : String(M));
    }
  }, [r, k]), U = F(async (I, M) => {
    _(null);
    try {
      await r.hubCommand("industrial/save-machine", {
        id: I.id,
        name: I.name,
        endpoint: I.endpoint,
        credentialId: I.credentialId,
        enabled: M
      }), await k();
    } catch (j) {
      _(j instanceof Error ? j.message : String(j));
    }
  }, [r, k]), z = F(async (I, M) => {
    _(null);
    try {
      await r.hubCommand("industrial/save-resource", {
        id: I.id,
        machineId: M || null
      }), await k();
    } catch (j) {
      _(j instanceof Error ? j.message : String(j));
    }
  }, [r, k]);
  return /* @__PURE__ */ T(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ T(H, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ h(G.Title, { level: 4, style: { margin: 0 }, children: "设备与资源" }),
      /* @__PURE__ */ h(L, { type: "primary", onClick: N, children: "新增设备" })
    ] }),
    /* @__PURE__ */ h(G.Text, { type: "secondary", children: "设备地址是台账属性；实际连接由数据源资源配置决定。绑定凭证供需要认证的数据源引用。" }),
    R && /* @__PURE__ */ h(Pe, { type: "error", showIcon: !0, message: R }),
    /* @__PURE__ */ h(
      Ne,
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
            render: (I) => {
              var M;
              return ((M = s.find((j) => j.id === I)) == null ? void 0 : M.name) ?? "—";
            }
          },
          {
            title: "启用",
            dataIndex: "enabled",
            render: (I, M) => /* @__PURE__ */ h(
              ct,
              {
                "aria-label": `设备 ${M.name} 启用`,
                checked: I,
                onChange: (j) => void U(M, j)
              }
            )
          },
          { title: "操作", key: "actions", render: (I, M) => /* @__PURE__ */ T(H, { children: [
            /* @__PURE__ */ h(L, { size: "small", onClick: () => m(M), children: "编辑" }),
            /* @__PURE__ */ h(L, { size: "small", danger: !0, onClick: () => void D(M), children: "删除" })
          ] }) }
        ]
      }
    ),
    /* @__PURE__ */ h(G.Title, { level: 5, style: { margin: 0 }, children: "资源归属" }),
    /* @__PURE__ */ h(
      Ne,
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
            render: (I, M) => /* @__PURE__ */ h(
              Se,
              {
                "aria-label": `资源 ${M.name} 的设备`,
                style: { width: 220 },
                value: M.machineId ?? void 0,
                allowClear: !0,
                onChange: (j) => void z(M, j ?? ""),
                options: t.map((j) => ({ value: j.id, label: j.name }))
              }
            )
          }
        ]
      }
    ),
    /* @__PURE__ */ h(
      Ir,
      {
        open: u !== null,
        title: u != null && u.name ? "编辑设备" : "新增设备",
        okText: "保存设备",
        cancelText: "取消",
        onCancel: () => c(null),
        onOk: () => void S(),
        children: /* @__PURE__ */ T(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "设备名称",
              placeholder: "设备名称",
              value: l,
              onChange: (I) => d(I.target.value)
            }
          ),
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "设备地址",
              placeholder: "台账地址 / IP",
              value: p,
              onChange: (I) => v(I.target.value)
            }
          ),
          /* @__PURE__ */ h(
            Se,
            {
              "aria-label": "设备凭证",
              style: { width: "100%" },
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: g,
              onChange: (I) => f(I),
              options: s.map((I) => ({
                value: I.id,
                label: `${I.name} (${I.kind})`
              }))
            }
          )
        ] })
      }
    )
  ] });
}
const o1 = ["filesystem.local", "filesystem.unc"], u1 = ["sink.sqlite", "sink.sqlserver", "sink.postgresql", "sink.mysql"];
function l1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q(""), [u, c] = q(""), [l, d] = q("csv"), [p, v] = q([]), [g, f] = q(null), [y, C] = q(!1);
  _e(() => {
    (async () => {
      try {
        n(Oh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"))), i(Ah(await r.hubQuery((e == null ? void 0 : e.nodesQuery) ?? "industrial/nodes")));
      } catch (m) {
        f(m instanceof Error ? m.message : String(m));
      }
    })();
  }, [e == null ? void 0 : e.nodesQuery, e == null ? void 0 : e.query, r]);
  const R = t.filter((m) => o1.includes(m.type)), _ = t.filter((m) => u1.some((S) => m.type === S.split(".")[1])), k = lt(
    () => new Map(a.map((m) => [m.nodeTypeId, m])),
    [a]
  ), N = F(async () => {
    var m;
    if (!s.trim()) {
      f("请输入任务名称");
      return;
    }
    if (!u) {
      f("请选择数据来源");
      return;
    }
    if (p.length === 0) {
      f("请至少选择一个目标");
      return;
    }
    C(!0), f(null);
    try {
      const D = `quick-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`, z = `industrial.${t.find((W) => W.id === u).type}`, I = `industrial.reader.${l}`, M = (W, x, $, P, ne) => ({ id: W, type: x, config: ne, x: $, y: P }), j = (W, x) => {
        var ne, le;
        const $ = k.get(re[W]), P = k.get(re[x]);
        return {
          source: W,
          sourcePort: ((ne = $.outputs[0]) == null ? void 0 : ne.name) ?? "output",
          target: x,
          targetPort: ((le = P.inputs[0]) == null ? void 0 : le.name) ?? "input"
        };
      }, J = [
        M("src", z, 80, 120, { resourceId: u }),
        M("read", I, 280, 120, {}),
        ...p.map((W, x) => M(
          `sink${x + 1}`,
          `industrial.sink.${t.find(($) => $.id === W).type}`,
          500,
          80 + x * 120,
          { targetResourceId: W }
        ))
      ], re = Object.fromEntries(J.map((W) => [W.id, W.type])), K = [j("src", "read"), ...p.map((W, x) => j("read", `sink${x + 1}`))], Y = {
        ...tt(D),
        name: s.trim(),
        nodes: J,
        edges: K,
        resourceReferences: [u, ...p],
        layout: { lookbackDays: 1 }
      };
      await r.hubCommand("industrial/save-workflow", Y), await r.hubCommand("industrial/publish-workflow", { workflowId: D }), (m = r.navigate) == null || m.call(r, `/industrial/designer?workflowId=${encodeURIComponent(D)}`);
    } catch (S) {
      f(S instanceof Error ? S.message : String(S));
    } finally {
      C(!1);
    }
  }, [r, l, k, s, t, u, p]);
  return /* @__PURE__ */ T(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(G.Title, { level: 4, style: { margin: 0 }, children: "快速创建采集任务" }),
    /* @__PURE__ */ h(G.Text, { type: "secondary", children: "生成标准工作流，可在设计器中继续修改。" }),
    g && /* @__PURE__ */ h(Pe, { type: "error", showIcon: !0, message: g }),
    /* @__PURE__ */ h(
      V,
      {
        "aria-label": "任务名称",
        placeholder: "任务名称",
        value: s,
        onChange: (m) => o(m.target.value)
      }
    ),
    /* @__PURE__ */ T(H, { wrap: !0, children: [
      /* @__PURE__ */ h(
        Se,
        {
          "aria-label": "数据来源",
          style: { width: 260 },
          placeholder: "数据来源",
          value: u || void 0,
          onChange: (m) => c(m),
          options: R.map((m) => ({ value: m.id, label: `${m.name} (${m.type})` }))
        }
      ),
      /* @__PURE__ */ h(
        Se,
        {
          "aria-label": "数据格式",
          style: { width: 160 },
          value: l,
          onChange: (m) => d(m),
          options: ["csv", "excel", "json"].map((m) => ({ value: m, label: m.toUpperCase() }))
        }
      )
    ] }),
    /* @__PURE__ */ h(
      Se,
      {
        "aria-label": "目标资源",
        mode: "multiple",
        style: { width: 420 },
        placeholder: "目标资源",
        value: p,
        onChange: (m) => v(m),
        options: _.map((m) => ({ value: m.id, label: `${m.name} (${m.type})` }))
      }
    ),
    /* @__PURE__ */ h(L, { type: "primary", loading: y, onClick: () => void N(), children: "创建并发布" })
  ] });
}
const c1 = (e) => {
  const r = Ae(e);
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
  const [t, n] = q([]), [a, i] = q(null), [s, o] = q(""), [u, c] = q(""), [l, d] = q(null), p = F(async () => {
    try {
      n(c1(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-profiles")));
    } catch (f) {
      d(f instanceof Error ? f.message : String(f));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    p();
  }, [p]);
  const v = F((f) => {
    const y = f ? { ...f } : d1();
    i(y), o(Object.entries(y.mapping ?? {}).map(([C, R]) => `${C}=${R}`).join(",")), c((y.requiredFields ?? []).join(",")), d(null);
  }, []), g = F(async () => {
    if (a) {
      if (!a.name.trim()) {
        d("请输入配置名称");
        return;
      }
      d(null);
      try {
        const f = {};
        for (const y of s.split(",")) {
          const [C, R] = y.split("=").map((_) => _ == null ? void 0 : _.trim());
          C && R && (f[C] = R);
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
        }), i(null), await p();
      } catch (f) {
        d(f instanceof Error ? f.message : String(f));
      }
    }
  }, [r, a, p, s, u]);
  return /* @__PURE__ */ T(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ T(H, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ h(G.Title, { level: 4, style: { margin: 0 }, children: "文件格式配置" }),
      /* @__PURE__ */ h(L, { type: "primary", onClick: () => v(), children: "新建文件配置" })
    ] }),
    l && /* @__PURE__ */ h(Pe, { type: "error", showIcon: !0, message: l }),
    /* @__PURE__ */ h(
      Ne,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "名称", dataIndex: "name" },
          { title: "文件匹配", dataIndex: "fileNamePattern" },
          { title: "格式", dataIndex: "format" },
          { title: "版本", dataIndex: "version" },
          { title: "操作", key: "actions", render: (f, y) => /* @__PURE__ */ h(L, { size: "small", onClick: () => v(y), children: "编辑" }) }
        ]
      }
    ),
    /* @__PURE__ */ h(
      Ir,
      {
        open: a !== null,
        title: a != null && a.version ? "编辑文件配置" : "新建文件配置",
        okText: "保存配置",
        cancelText: "取消",
        onCancel: () => i(null),
        onOk: () => void g(),
        children: a && /* @__PURE__ */ T(H, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "配置名称",
              placeholder: "配置名称",
              value: a.name,
              onChange: (f) => i({ ...a, name: f.target.value })
            }
          ),
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "文件匹配规则",
              placeholder: "文件匹配，例如 *.csv",
              value: a.fileNamePattern,
              onChange: (f) => i({ ...a, fileNamePattern: f.target.value })
            }
          ),
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "文件格式",
              placeholder: "csv / excel / json",
              value: a.format,
              onChange: (f) => i({ ...a, format: f.target.value })
            }
          ),
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "分隔符",
              placeholder: "分隔符",
              value: a.delimiter,
              onChange: (f) => i({ ...a, delimiter: f.target.value })
            }
          ),
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "编码",
              placeholder: "编码",
              value: a.encoding,
              onChange: (f) => i({ ...a, encoding: f.target.value })
            }
          ),
          /* @__PURE__ */ h(
            ct,
            {
              "aria-label": "首行为表头",
              checked: a.hasHeader,
              onChange: (f) => i({ ...a, hasHeader: f })
            }
          ),
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "字段映射",
              placeholder: "目标字段=来源字段，逗号分隔",
              value: s,
              onChange: (f) => o(f.target.value)
            }
          ),
          /* @__PURE__ */ h(
            V,
            {
              "aria-label": "必填字段",
              placeholder: "必填字段，逗号分隔",
              value: u,
              onChange: (f) => c(f.target.value)
            }
          )
        ] })
      }
    )
  ] });
}
const Mr = (e) => {
  const r = Ae(e);
  return Array.isArray(r) ? r : [];
}, h1 = (e) => new Promise((r, t) => {
  const n = new FileReader();
  n.onload = () => r(String(n.result).split(",")[1] ?? ""), n.onerror = () => t(n.error ?? new Error("无法读取 Excel 文件")), n.readAsDataURL(e);
});
function p1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q([]), [u, c] = q([]), [l, d] = q([]), [p, v] = q([]), [g, f] = q([]), [y, C] = q(), [R, _] = q(1), [k, N] = q("resource-{machine}"), [m, S] = q([]), [D, U] = q(""), [z, I] = q(""), [M, j] = q(""), [J, re] = q(""), [K, Y] = q(null), [W, x] = q("manual"), [$, P] = q(""), [ne, le] = q(""), [Ue, Te] = q(""), [mr, Dr] = q(1), [br, Rr] = q(""), [_r, ue] = q(null), [Ke, ce] = q(!1), he = F(async () => {
    try {
      n(Mr(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/bindings"))), i(Mr(await r.hubQuery("industrial/machines"))), o(Mr(await r.hubQuery("industrial/file-profiles"))), c(Mr(await r.hubQuery("industrial/workflows"))), d(Mr(await r.hubQuery("industrial/resources")));
      try {
        Y(await r.hubQuery("industrial/binding-status"));
      } catch {
      }
    } catch (w) {
      ue(w instanceof Error ? w.message : String(w));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    he();
  }, [he]);
  const ee = lt(() => {
    const w = [];
    for (const Ce of p) for (const xe of g) {
      const We = `binding:${y ?? ""}:v${R}:${Ce}:${xe}`;
      t.some((Ge) => Ge.id === We) && w.push(We);
    }
    const B = m.filter((Ce) => !l.some((xe) => xe.id === Ce)), de = p.map((Ce) => k.replace("{machine}", Ce)).filter((Ce) => !l.some((xe) => xe.id === Ce));
    return {
      total: p.length * g.length,
      create: p.length * g.length - w.length,
      conflicts: w,
      missing: [...B, ...de]
    };
  }, [t, p, g, l, k, m, R, y]), me = F(async () => {
    if (!y || p.length === 0 || g.length === 0 || m.length === 0) {
      ue("请选择工作流、机台、文件配置和目标资源");
      return;
    }
    if (ee.missing.length > 0) {
      ue("存在缺失资源，请先创建后再导入");
      return;
    }
    ce(!0), ue(null);
    try {
      await r.hubCommand("industrial/create-bindings", {
        machineIds: p,
        fileProfileIds: g,
        workflowId: y,
        workflowVersion: R,
        sourceResourcePattern: k,
        targetResourceIds: m,
        triggerKind: W,
        cron: $ || null
      }), await he();
    } catch (w) {
      ue(w instanceof Error ? w.message : String(w));
    } finally {
      ce(!1);
    }
  }, [r, $, he, ee.missing.length, g, k, m, W, R, y, p]), Z = F(async (w, B) => {
    ce(!0), ue(null);
    try {
      await r.hubCommand("industrial/set-binding-enabled", { id: w.id, enabled: B }), await he();
    } catch (de) {
      ue(de instanceof Error ? de.message : String(de));
    } finally {
      ce(!1);
    }
  }, [r, he]), Re = F(async (w, B) => {
    ce(!0), ue(null);
    try {
      await r.hubCommand("industrial/run-binding", {
        bindingId: w.id,
        lookbackDays: B
      }), Y(await r.hubQuery("industrial/binding-status"));
    } catch (de) {
      ue(de instanceof Error ? de.message : String(de));
    } finally {
      ce(!1);
    }
  }, [r]), Ve = F(async () => {
    ce(!0), ue(null);
    try {
      await r.hubCommand("industrial/import-file-profiles", { csv: ne }), le(""), await he();
    } catch (w) {
      ue(w instanceof Error ? w.message : String(w));
    } finally {
      ce(!1);
    }
  }, [r, he, ne]), cr = F(async () => {
    ce(!0), ue(null);
    try {
      await r.hubCommand("industrial/import-bindings", { csv: Ue }), Te(""), await he();
    } catch (w) {
      ue(w instanceof Error ? w.message : String(w));
    } finally {
      ce(!1);
    }
  }, [r, he, Ue]), Je = F(async (w, B) => {
    if (B) {
      ce(!0), ue(null);
      try {
        const de = w === "profiles" ? "industrial/import-file-profiles" : `industrial/import-${w}`;
        await r.hubCommand(de, { xlsx: await h1(B) }), await he();
      } catch (de) {
        ue(de instanceof Error ? de.message : String(de));
      } finally {
        ce(!1);
      }
    }
  }, [r, he]), $e = F(async () => {
    ce(!0), ue(null);
    try {
      const w = t.filter((B) => B.workflowId === y).map((B) => B.id);
      await r.hubCommand("industrial/upgrade-binding-version", { ids: w, version: mr }), await he();
    } catch (w) {
      ue(w instanceof Error ? w.message : String(w));
    } finally {
      ce(!1);
    }
  }, [t, r, he, mr, y]), qe = F(async () => {
    ce(!0), ue(null);
    try {
      await r.hubCommand("industrial/import-machines", { csv: D }), U(""), await he();
    } catch (w) {
      ue(w instanceof Error ? w.message : String(w));
    } finally {
      ce(!1);
    }
  }, [r, D, he]), se = F(async () => {
    ue(null);
    try {
      const w = await r.hubQuery("industrial/export-config"), B = (w == null ? void 0 : w.data) ?? w;
      I(JSON.stringify(B, null, 2));
    } catch (w) {
      ue(w instanceof Error ? w.message : String(w));
    }
  }, [r]), He = (w) => {
    const B = {};
    for (const de of w.split(/[\n,]/)) {
      const [Ce, xe] = de.split("=").map((We) => We == null ? void 0 : We.trim());
      Ce && xe && (B[Ce] = xe);
    }
    return B;
  }, dr = F(async () => {
    ce(!0), ue(null);
    try {
      const w = JSON.parse(z);
      w.resourceMap = He(M), w.credentialMap = He(J), await r.hubCommand("industrial/import-config", w), await he();
    } catch (w) {
      ue(w instanceof Error ? w.message : String(w));
    } finally {
      ce(!1);
    }
  }, [r, J, he, z, M]);
  return /* @__PURE__ */ T(H, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(G.Title, { level: 4, style: { margin: 0 }, children: "采集任务绑定" }),
    _r && /* @__PURE__ */ h(Pe, { type: "error", showIcon: !0, message: _r }),
    /* @__PURE__ */ T(H, { wrap: !0, children: [
      /* @__PURE__ */ h(
        V,
        {
          "aria-label": "工作流 ID",
          placeholder: "工作流 ID",
          value: y ?? "",
          onChange: (w) => C(w.target.value)
        }
      ),
      /* @__PURE__ */ h(
        V,
        {
          "aria-label": "机台 ID 列表",
          placeholder: "机台 ID，逗号分隔",
          value: p.join(","),
          onChange: (w) => v(w.target.value.split(",").map((B) => B.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ h(
        V,
        {
          "aria-label": "文件配置 ID 列表",
          placeholder: "文件配置 ID，逗号分隔",
          value: g.join(","),
          onChange: (w) => f(w.target.value.split(",").map((B) => B.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ h(
        V,
        {
          "aria-label": "目标资源 ID 列表",
          placeholder: "目标资源 ID，逗号分隔",
          value: m.join(","),
          onChange: (w) => S(w.target.value.split(",").map((B) => B.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ h(
        Se,
        {
          "aria-label": "工作流模板",
          mode: "single",
          style: { width: 220 },
          value: y,
          onChange: (w) => C(w),
          options: u.map((w) => ({ value: w.workflowId, label: w.name }))
        }
      ),
      /* @__PURE__ */ h(
        rt,
        {
          "aria-label": "模板版本",
          min: 1,
          value: R,
          onChange: (w) => _(Number(w) || 1)
        }
      ),
      /* @__PURE__ */ h(
        Se,
        {
          "aria-label": "机台范围",
          mode: "multiple",
          style: { width: 300 },
          value: p,
          onChange: (w) => v(w),
          options: a.map((w) => ({ value: w.id, label: w.name }))
        }
      ),
      /* @__PURE__ */ h(
        Se,
        {
          "aria-label": "文件配置范围",
          mode: "multiple",
          style: { width: 300 },
          value: g,
          onChange: (w) => f(w),
          options: s.map((w) => ({ value: w.id, label: w.name }))
        }
      ),
      /* @__PURE__ */ h(
        Se,
        {
          "aria-label": "目标资源",
          mode: "multiple",
          style: { width: 300 },
          value: m,
          onChange: (w) => S(w),
          options: l.map((w) => ({ value: w.id, label: `${w.name} (${w.type})` }))
        }
      ),
      /* @__PURE__ */ T(
        "select",
        {
          "aria-label": "绑定触发策略选择",
          value: W,
          onChange: (w) => x(w.target.value),
          children: [
            /* @__PURE__ */ h("option", { value: "manual", children: "手动" }),
            /* @__PURE__ */ h("option", { value: "schedule", children: "定时" })
          ]
        }
      ),
      /* @__PURE__ */ h(
        Se,
        {
          "aria-label": "绑定触发策略",
          style: { width: 160 },
          value: W,
          onChange: (w) => x(w),
          options: [{ value: "manual", label: "手动" }, { value: "schedule", label: "定时" }]
        }
      ),
      /* @__PURE__ */ h(
        V,
        {
          "aria-label": "绑定 Cron",
          placeholder: "cron（定时策略）",
          value: $,
          onChange: (w) => P(w.target.value)
        }
      ),
      /* @__PURE__ */ h(
        V,
        {
          "aria-label": "来源资源模板",
          placeholder: "来源资源模板，{machine} 占位符",
          value: k,
          onChange: (w) => N(w.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ T(H, { wrap: !0, children: [
      /* @__PURE__ */ h(L, { size: "small", onClick: () => v(a.map((w) => w.id)), children: "全选机台" }),
      /* @__PURE__ */ h(L, { size: "small", onClick: () => f(s.map((w) => w.id)), children: "全选文件配置" }),
      /* @__PURE__ */ h(L, { size: "small", onClick: () => {
        v([]), f([]);
      }, children: "清空范围" })
    ] }),
    /* @__PURE__ */ h(
      Pe,
      {
        type: "info",
        showIcon: !0,
        message: `预计生成 ${ee.total} 条绑定；可创建 ${ee.create} 条`,
        description: `冲突 ${ee.conflicts.length} 条；缺失资源 ${ee.missing.length} 条`
      }
    ),
    /* @__PURE__ */ h(L, { type: "primary", loading: Ke, onClick: () => void me(), children: "生成绑定" }),
    /* @__PURE__ */ T(H, { wrap: !0, children: [
      /* @__PURE__ */ h(
        rt,
        {
          "aria-label": "升级到模板版本",
          min: 1,
          value: mr,
          onChange: (w) => Dr(Number(w) || 1)
        }
      ),
      /* @__PURE__ */ h(L, { loading: Ke, onClick: () => void $e(), children: "升级当前工作流绑定版本" })
    ] }),
    /* @__PURE__ */ h(G.Title, { level: 5, style: { margin: 0 }, children: "批量导入机台（CSV）" }),
    /* @__PURE__ */ h(
      V.TextArea,
      {
        "aria-label": "机台 CSV",
        rows: 4,
        value: D,
        placeholder: "id,name,endpoint,credentialId",
        onChange: (w) => U(w.target.value)
      }
    ),
    /* @__PURE__ */ h(L, { loading: Ke, onClick: () => void qe(), children: "导入机台" }),
    /* @__PURE__ */ h(
      V.TextArea,
      {
        "aria-label": "文件配置 CSV",
        rows: 3,
        value: ne,
        placeholder: "id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required",
        onChange: (w) => le(w.target.value)
      }
    ),
    /* @__PURE__ */ h(L, { loading: Ke, onClick: () => void Ve(), children: "导入文件配置" }),
    /* @__PURE__ */ h(
      V.TextArea,
      {
        "aria-label": "绑定 CSV",
        rows: 3,
        value: Ue,
        placeholder: "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId,targetResourceIds,triggerKind,cron",
        onChange: (w) => Te(w.target.value)
      }
    ),
    /* @__PURE__ */ h(L, { loading: Ke, onClick: () => void cr(), children: "导入采集绑定" }),
    /* @__PURE__ */ h(G.Title, { level: 5, style: { margin: 0 }, children: "Excel 批量导入（.xlsx，选择即导入）" }),
    /* @__PURE__ */ T(H, { wrap: !0, children: [
      /* @__PURE__ */ T("label", { children: [
        "机台 Excel",
        /* @__PURE__ */ h(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "机台 Excel 文件",
            onChange: (w) => {
              var B;
              return void Je("machines", (B = w.target.files) == null ? void 0 : B[0]);
            }
          }
        )
      ] }),
      /* @__PURE__ */ T("label", { children: [
        "文件配置 Excel",
        /* @__PURE__ */ h(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "文件配置 Excel 文件",
            onChange: (w) => {
              var B;
              return void Je("profiles", (B = w.target.files) == null ? void 0 : B[0]);
            }
          }
        )
      ] }),
      /* @__PURE__ */ T("label", { children: [
        "采集绑定 Excel",
        /* @__PURE__ */ h(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "绑定 Excel 文件",
            onChange: (w) => {
              var B;
              return void Je("bindings", (B = w.target.files) == null ? void 0 : B[0]);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ h(G.Title, { level: 5, style: { margin: 0 }, children: "配置包（不含凭证）" }),
    /* @__PURE__ */ T(H, { wrap: !0, children: [
      /* @__PURE__ */ h(L, { onClick: () => void se(), children: "导出配置" }),
      /* @__PURE__ */ h(L, { loading: Ke, onClick: () => void dr(), children: "导入配置" })
    ] }),
    /* @__PURE__ */ h(
      V,
      {
        "aria-label": "资源 ID 映射",
        placeholder: "旧资源ID=新资源ID，逗号或换行分隔",
        value: M,
        onChange: (w) => j(w.target.value)
      }
    ),
    /* @__PURE__ */ h(
      V,
      {
        "aria-label": "凭证别名映射",
        placeholder: "旧凭证ID=新凭证ID",
        value: J,
        onChange: (w) => re(w.target.value)
      }
    ),
    /* @__PURE__ */ h(
      V.TextArea,
      {
        "aria-label": "配置包",
        rows: 5,
        value: z,
        placeholder: "导出后可跨环境导入；资源 ID 需自行映射",
        onChange: (w) => I(w.target.value)
      }
    ),
    /* @__PURE__ */ h(
      V,
      {
        "aria-label": "绑定筛选",
        placeholder: "按绑定名称/ID筛选",
        value: br,
        onChange: (w) => Rr(w.target.value)
      }
    ),
    /* @__PURE__ */ h(
      Ne,
      {
        rowKey: "id",
        dataSource: t.filter((w) => !br || `${w.id} ${w.name}`.includes(br)),
        pagination: { pageSize: 20 },
        columns: [
          { title: "绑定", dataIndex: "name" },
          { title: "机台", dataIndex: "machineId" },
          { title: "文件配置", dataIndex: "fileProfileId" },
          { title: "模板版本", dataIndex: "workflowVersion" },
          { title: "状态", dataIndex: "enabled", render: (w, B) => /* @__PURE__ */ T(H, { children: [
            /* @__PURE__ */ h(L, { size: "small", onClick: () => void Z(B, !B.enabled), children: B.enabled ? "暂停" : "恢复" }),
            /* @__PURE__ */ h(L, { size: "small", onClick: () => void Re(B, 7), children: "补采 7 天" }),
            /* @__PURE__ */ h(L, { size: "small", onClick: () => void Re(B, 1), children: "运行" })
          ] }) },
          { title: "排障", key: "diagnostics", render: (w, B) => {
            const de = ((K == null ? void 0 : K.runs) ?? []).filter((xe) => xe.workflowId === B.workflowId), Ce = ((K == null ? void 0 : K.deliveries) ?? []).filter((xe) => xe.status === "Failed");
            return /* @__PURE__ */ T(G.Text, { style: { fontSize: 11 }, children: [
              de.length ? `最近运行 ${de[0].status}` : "暂无运行",
              " · 失败交付 ",
              Ce.length
            ] });
          } }
        ]
      }
    )
  ] });
}
const v1 = `
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
function g1(e) {
  ev(e);
  const r = typeof document > "u" ? null : document.createElement("style");
  r && (r.dataset.industrialDesigner = "true", r.textContent = v1, document.head.appendChild(r));
  const t = ({ ctx: i }) => Oo(Jw, { ctx: i });
  t.displayName = "IndustrialWorkflowCanvas";
  const n = (i, s) => {
    const o = ({ block: u, ctx: c }) => Oo(s, { block: u, ctx: c });
    return o.displayName = i, e.blocks.register(i, o);
  }, a = [
    e.blocks.register("industrial-workflow-canvas", t),
    n("industrial-workflow-list", Xw),
    n("industrial-resource-console", r1),
    n("industrial-credential-console", t1),
    n("industrial-execution-center", n1),
    n("industrial-file-details", a1),
    n("industrial-machine-console", s1),
    n("industrial-collection-wizard", l1),
    n("industrial-file-profiles", f1),
    n("industrial-binding-console", p1)
  ];
  return () => {
    for (const i of a) i == null || i();
    r == null || r.remove();
  };
}
export {
  Yv as g,
  g1 as r
};
