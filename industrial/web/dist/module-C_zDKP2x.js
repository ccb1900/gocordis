let Lr = null;
function nv(e) {
  Lr = e;
}
function Ne() {
  if (!Lr && typeof globalThis < "u" && (Lr = globalThis.__CORDIS_CONSOLE ?? null), !Lr)
    throw new Error("[industrial-designer] console facade is not available");
  return Lr;
}
const f = (...e) => Ne().jsxRuntime.jsx(...e), x = (...e) => Ne().jsxRuntime.jsxs(...e), dr = (e) => Ne().jsxRuntime.jsx(Ne().jsxRuntime.Fragment, e), Mo = (...e) => Ne().React.createElement(...e), F = (...e) => Ne().React.useCallback(...e), _e = (...e) => Ne().React.useEffect(...e), ht = (...e) => Ne().React.useMemo(...e), xe = (...e) => Ne().React.useRef(...e), I = (...e) => Ne().React.useState(...e), ye = (e) => {
  const r = (t) => {
    const n = e.split(".").reduce((a, i) => a == null ? void 0 : a[i], Ne().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Mo(n, t ?? {});
  };
  return r.displayName = `facade.${e}`, r;
}, Se = ye("Alert"), $ = ye("Button"), hu = ye("Divider"), av = ye("Drawer"), xh = ye("Empty"), $o = Object.assign(ye("Form"), {
  Item: ye("Form.Item"),
  useForm: (...e) => Ne().antd.Form.useForm(...e)
}), H = Object.assign(ye("Input"), {
  Number: ye("InputNumber"),
  TextArea: ye("Input.TextArea")
}), it = ye("InputNumber"), iv = Object.assign(ye("Layout"), {
  Header: ye("Layout.Header"),
  Content: ye("Layout.Content")
}), mr = ye("Modal"), Ce = ye("Select"), W = ye("Space"), Rh = ye("Spin"), pt = ye("Switch"), Oe = ye("Table"), $e = ye("Tag"), U = Object.assign(ye("Typography"), {
  Title: ye("Typography.Title"),
  Text: ye("Typography.Text"),
  Paragraph: ye("Typography.Paragraph")
}), sv = new Proxy({}, {
  get: (e, r) => (...t) => {
    var a;
    const n = (a = Ne().antd.message) == null ? void 0 : a[r];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${r} unavailable`);
    return n(...t);
  }
}), kh = 148, Ah = 46, pu = {
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
}, vu = (e) => pu[e] ?? pu[e.split(".")[0]] ?? "#4d6bfe";
function ov(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function uv(e, r = /* @__PURE__ */ new Map(), t) {
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
          fill: vu(i),
          stroke: s ? "#111111" : vu(i),
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
      id: ov(n),
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
const Fr = 20;
function Ph(e) {
  const r = Object.values(e), t = /* @__PURE__ */ new Set();
  for (let n = 0; n < r.length; n++)
    for (let a = n + 1; a < r.length; a++)
      r[n].x === r[a].x && t.add(`x=${r[n].x}`), r[n].y === r[a].y && t.add(`y=${r[n].y}`);
  return [...t];
}
function Lo(e, r, t) {
  const n = t && t.length > 0 ? new Set(t) : null, a = {};
  for (const i of e.nodes) {
    if (!r.has(i.id) || n && !n.has(i.id)) continue;
    const s = i.style.x, o = i.style.y;
    typeof s == "number" && typeof o == "number" && (a[i.id] = {
      x: Math.round(s / Fr) * Fr,
      y: Math.round(o / Fr) * Fr
    });
  }
  return a;
}
function lv(e, r) {
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
        size: [kh, Ah],
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
        endArrow: !0,
        // Orthogonal routing keeps edges out of node bodies and reduces
        // crossings in left-to-right industrial pipelines.
        router: { type: "orth" }
      }
    },
    plugins: [
      { type: "grid-line", size: Fr, stroke: "#e8edf4" },
      { type: "minimap", size: [180, 110], position: "right-bottom" },
      { type: "snapline", lineWidth: 1, stroke: "#4d6bfe" }
    ],
    behaviors: Oh()
  };
}
const cv = "__industrial_drag_target__", dv = "__industrial_connect_source__", Nt = (e, r) => {
  var t;
  return (((t = e.isPanModifier) == null ? void 0 : t.call(e)) ?? !1) || r.button === 1 || r.buttons === 4 || r.shiftKey === !0;
}, gu = (e, r) => {
  var l, d;
  if (r.targetType !== "node" || !((l = r.target) != null && l.id)) return !1;
  const t = ((d = e.gestureOrigin) == null ? void 0 : d.call(e)) ?? r.client ?? r.canvas;
  if (!t || !e.graph) return !1;
  const n = String(r.target.id), a = e.graph.getNodeData().find((p) => String(p.id) === n), i = a == null ? void 0 : a.style;
  if (typeof (i == null ? void 0 : i.x) != "number" || typeof i.y != "number") return !1;
  const [s, o] = e.graph.getCanvasByClient([t.x, t.y]), u = Math.abs(s - i.x), c = Math.abs(o - i.y);
  return Math.abs(u - kh / 2) <= 14 && c <= Ah / 2 + 10;
};
function Oh(e = {}) {
  const r = e.connectionState ?? { armed: !1 }, t = (n) => {
    var c, l, d;
    const a = e.graph;
    if (!a || !e.onPositions) return;
    const i = Array.isArray(n) ? n : n ? [String(n)] : void 0, s = a.getNodeData().map((p) => ({
      id: String(p.id),
      data: p.data ?? {},
      style: p.style ?? {}
    })), o = Lo(
      { nodes: s },
      ((c = e.allowedIds) == null ? void 0 : c.call(e)) ?? /* @__PURE__ */ new Set(),
      i
    );
    e.onPositions(o);
    const u = Lo(
      { nodes: s },
      ((l = e.allowedIds) == null ? void 0 : l.call(e)) ?? /* @__PURE__ */ new Set()
    );
    (d = e.onAlign) == null || d.call(e, Ph(u));
  };
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
      onSelect: (n) => {
        var a;
        return (a = e.onSelectIds) == null ? void 0 : a.call(e, Object.keys(n ?? {}));
      }
    },
    {
      type: "drag-canvas",
      enable: (n) => n.targetType === "canvas" || Nt(e, n),
      range: 0.9
    },
    {
      type: "drag-element",
      state: cv,
      enable: (n) => (n.targetType === "node" || n.targetType === "combo") && !gu(e, n) && !Nt(e, n),
      onFinish: t
    },
    {
      type: "drag-element",
      state: "__industrial_selected__",
      enable: (n) => n.targetType === "node" && n.shiftKey === !0,
      onFinish: t
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: dv,
      enable: (n) => Nt(e, n) ? (r.armed = !1, !1) : r.armed ? n.targetType === "node" || n.targetType === "combo" : gu(e, n) ? (r.armed = !0, !0) : !1,
      onCreate: (n) => {
        var o;
        r.armed = !1;
        const a = String(n.source ?? ""), i = String(n.target ?? "");
        if (!a || !i || a === i) return;
        const s = (o = e.onCreateEdge) == null ? void 0 : o.call(e, a, i);
        if (s)
          return {
            ...n,
            sourcePort: `out:${s.sourcePort}`,
            targetPort: `in:${s.targetPort}`
          };
      }
    }
  ];
}
const fv = (e) => {
  var t;
  const r = e;
  return (r == null ? void 0 : r.targetType) === "node" ? (t = r.target) == null ? void 0 : t.id : void 0;
};
function hv(e) {
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
  } = e, g = xe(null), h = xe(null), y = xe(!1), C = xe(!1), R = xe({ armed: !1 }), w = xe(null), [A, M] = I(null), [b, k] = I([]), O = xe({ onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o });
  O.current = { onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o, onSelectEdge: l };
  const V = ht(
    () => uv(r, t, n),
    [r, t, n]
  ), G = xe(V);
  G.current = V;
  const S = xe(/* @__PURE__ */ new Set());
  S.current = new Set(r.nodes.map((z) => z.id)), _e(() => {
    const z = (de) => {
      de.code === "Space" && !de.repeat && (C.current = !0);
    }, ae = (de) => {
      de.code === "Space" && (C.current = !1);
    }, re = () => {
      C.current = !1;
    };
    return window.addEventListener("keydown", z), window.addEventListener("keyup", ae), window.addEventListener("blur", re), () => {
      window.removeEventListener("keydown", z), window.removeEventListener("keyup", ae), window.removeEventListener("blur", re);
    };
  }, []), _e(() => {
    const z = g.current;
    if (!z || y.current) return;
    let ae = !1;
    return import("./esm-CBseSUoL.js").then(({ Graph: re }) => {
      if (ae || !g.current || y.current) return;
      const de = lv(G.current, z), B = new re({
        ...de,
        behaviors: Oh({
          graph: {
            getNodeData: () => {
              var Q;
              return ((Q = h.current) == null ? void 0 : Q.getNodeData()) ?? [];
            },
            getCanvasByClient: (Q) => {
              var T;
              const E = ((T = h.current) == null ? void 0 : T.getCanvasByClient(Q)) ?? Q;
              return [E[0], E[1]];
            }
          },
          isPanModifier: () => C.current,
          gestureOrigin: () => w.current,
          connectionState: R.current,
          allowedIds: () => S.current,
          onPositions: (Q) => O.current.onPositions(Q),
          onAlign: (Q) => k((E) => Q.length > 0 ? Q : E),
          onSelectIds: (Q) => u == null ? void 0 : u(Q),
          onCreateEdge: (Q, E) => O.current.onCreateEdge(Q, E)
        })
      });
      B.on("node:click", (Q) => {
        const E = fv(Q);
        E && O.current.onSelect(E);
      }), B.on("edge:click", (Q) => {
        var T, L, fe;
        const E = Q;
        (fe = (L = O.current).onSelectEdge) == null || fe.call(L, ((T = E.target) == null ? void 0 : T.id) ?? null);
      }), B.on("node:contextmenu", (Q) => {
        var T, L, fe;
        const E = Q;
        (T = E.target) != null && T.id && M({
          x: ((L = E.client) == null ? void 0 : L.x) ?? 0,
          y: ((fe = E.client) == null ? void 0 : fe.y) ?? 0,
          type: "node",
          id: E.target.id
        });
      }), B.on("edge:contextmenu", (Q) => {
        var T, L, fe;
        const E = Q;
        (T = E.target) != null && T.id && M({
          x: ((L = E.client) == null ? void 0 : L.x) ?? 0,
          y: ((fe = E.client) == null ? void 0 : fe.y) ?? 0,
          type: "edge",
          id: E.target.id
        });
      }), B.on("canvas:click", () => O.current.onSelect(null)), B.on("pointerdown", (Q) => {
        const E = Q;
        w.current = E.client ? { x: E.client.x, y: E.client.y } : null;
      }), B.on("pointerup", () => {
        queueMicrotask(() => {
          R.current.armed = !1, w.current = null;
        });
      }), h.current = B, z.__industrialGraph = B, c == null || c(B), y.current = !0, B.render();
    }), () => {
      ae = !0;
    };
  }, []), _e(() => {
    const z = h.current;
    !z || !y.current || (z.setData(V), z.draw());
  }, [V]), _e(() => () => {
    var z;
    (z = h.current) == null || z.destroy(), h.current = null, y.current = !1;
  }, []);
  const j = () => {
    const z = h.current;
    if (!z) return;
    const ae = z.getNodeData().map((B) => ({
      id: String(B.id),
      data: B.data ?? {},
      style: B.style ?? {}
    })), re = Lo({ nodes: ae }, S.current);
    O.current.onPositions(re);
    const de = Ph(re);
    k(de.length > 0 ? de : ["已对齐到网格"]);
  };
  return /* @__PURE__ */ f("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ x("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ f(
      "div",
      {
        ref: g,
        className: "industrial-graph-host",
        onDragOver: (z) => z.preventDefault(),
        onDrop: (z) => {
          z.preventDefault();
          const ae = z.dataTransfer.getData("application/x-industrial-node") || z.dataTransfer.getData("text/plain");
          if (!ae) return;
          const re = h.current, de = g.current;
          if (!re || !de) return;
          const B = de.getBoundingClientRect(), Q = re.getCanvasByViewport([
            z.clientX - B.left,
            z.clientY - B.top
          ]);
          O.current.onDropNode(ae, Q[0], Q[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    A && /* @__PURE__ */ f("div", { className: "industrial-context-menu", style: { left: A.x, top: A.y }, children: A.type === "node" ? /* @__PURE__ */ x(dr, { children: [
      /* @__PURE__ */ f("button", { onClick: () => {
        p == null || p(A.id), M(null);
      }, children: "复制节点" }),
      /* @__PURE__ */ f("button", { onClick: () => {
        d == null || d(A.id), M(null);
      }, children: "删除节点" })
    ] }) : /* @__PURE__ */ x(dr, { children: [
      /* @__PURE__ */ f("button", { onClick: () => {
        l == null || l(A.id), M(null);
      }, children: "编辑连线" }),
      /* @__PURE__ */ f("button", { onClick: () => {
        v == null || v(A.id), M(null);
      }, children: "删除连线" })
    ] }) }),
    b.length > 0 && /* @__PURE__ */ x("div", { className: "industrial-align-guide", "data-testid": "align-guide", children: [
      "已对齐 ",
      b.join(" · ")
    ] }),
    /* @__PURE__ */ x("div", { className: "industrial-graph-hint", children: [
      "Shift+拖拽框选 · 拖节点移动 · 端口拖拽连线 · 滚轮缩放 · 空格/中键平移",
      /* @__PURE__ */ f("button", { type: "button", "data-testid": "designer-snap", onClick: j, children: "对齐到网格" })
    ] })
  ] }) });
}
function pv({ descriptors: e, loading: r, error: t, onAdd: n }) {
  const a = /* @__PURE__ */ new Map();
  for (const s of e) {
    const o = s.category.split(".")[0] || "General", u = a.get(o) ?? [];
    u.push(s), a.set(o, u);
  }
  const i = (s, o) => {
    s.dataTransfer.setData("application/x-industrial-node", o.nodeTypeId), s.dataTransfer.setData("text/plain", o.nodeTypeId), s.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ x("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ f(U.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    r && /* @__PURE__ */ f(Rh, { size: "small", style: { display: "block", marginTop: 16 } }),
    t && /* @__PURE__ */ f(U.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: t }),
    !r && !t && e.length === 0 && /* @__PURE__ */ f(xh, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(a.entries()).map(([s, o]) => /* @__PURE__ */ x("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ f(U.Text, { strong: !0, style: { fontSize: 12 }, children: s }),
      o.map((u) => /* @__PURE__ */ x(
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
            /* @__PURE__ */ f("div", { className: "industrial-palette-node-title", children: u.name }),
            /* @__PURE__ */ x("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ f($e, { bordered: !1, color: "blue", children: u.version }),
              /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 11 }, children: u.nodeTypeId })
            ] }),
            /* @__PURE__ */ x("div", { className: "industrial-palette-node-ports", children: [
              u.inputs.map((c) => /* @__PURE__ */ x($e, { bordered: !1, color: "default", children: [
                "in ",
                c.name,
                ": ",
                c.type
              ] }, `in-${c.name}`)),
              u.outputs.map((c) => /* @__PURE__ */ x($e, { bordered: !1, color: "green", children: [
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
const st = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {}
});
function vv(e) {
  const r = {};
  for (const [t, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (r[t] = n.default);
  return r;
}
function Dt(e, r, t = 0, n = 0) {
  return { id: r, type: e.nodeTypeId, config: vv(e), x: t, y: n };
}
function tr(e, r) {
  return e.get(r);
}
function ot(e, r, t) {
  return e[t].find((n) => n.name === r);
}
function Uo(e, r) {
  return e !== "Unknown" && r !== "Unknown" && e === r;
}
function yu(e, r) {
  for (const t of e.outputs)
    for (const n of r.inputs)
      if (Uo(t.type, n.type))
        return { sourcePort: t.name, targetPort: n.name };
  return null;
}
const Pe = (e, r, t) => ({ path: e, code: r, message: t });
function gv(e, r) {
  return e.source === r.source && e.sourcePort === r.sourcePort && e.target === r.target && e.targetPort === r.targetPort;
}
function yv(e, r, t) {
  const n = `edges.${e.edges.length}`, a = e.nodes.find((l) => l.id === r.source), i = e.nodes.find((l) => l.id === r.target);
  if (!a || !i)
    return { definition: e, issue: Pe(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((l) => gv(l, r)))
    return { definition: e };
  const s = tr(t, a.type), o = tr(t, i.type);
  if (!s || !o)
    return { definition: e, issue: Pe(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const u = ot(s, r.sourcePort, "outputs"), c = ot(o, r.targetPort, "inputs");
  return !u || !c ? { definition: e, issue: Pe(n, "PORT_MISSING", "declared port does not exist") } : Uo(u.type, c.type) ? { definition: { ...e, edges: [...e.edges, r] } } : {
    definition: e,
    issue: Pe(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${u.type} → ${c.type} is not a legal connection`
    )
  };
}
function Mt(e, r) {
  return {
    ...e,
    nodes: e.nodes.filter((t) => t.id !== r),
    edges: e.edges.filter((t) => t.source !== r && t.target !== r)
  };
}
function mu(e, r) {
  return { ...e, edges: e.edges.filter((t, n) => n !== r) };
}
function mv(e, r) {
  const t = { ...e };
  for (const [n, a] of Object.entries(r))
    a === void 0 ? delete t[n] : t[n] = a;
  return t;
}
function bv(e, r, t) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === r ? { ...n, config: t } : n)
  };
}
function _v(e, r) {
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
function wv(e) {
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
function bu(e, r, t) {
  const n = [];
  e.workflowId.trim() || n.push(Pe("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(Pe("workflow", "INVALID_VERSION", "version must be positive"));
  const a = /* @__PURE__ */ new Map();
  for (const o of e.nodes)
    a.has(o.id) && n.push(Pe(`nodes.${o.id}`, "DUPLICATE_NODE_ID", `duplicate node '${o.id}'`)), a.set(o.id, o);
  const i = /* @__PURE__ */ new Map();
  for (const o of e.nodes) {
    const u = tr(r, o.type);
    if (!u) {
      n.push(Pe(
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
      (d == null || d === "") && n.push(Pe(
        `nodes.${o.id}.config.${l}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${l}' is missing`
      ));
    }
    t && typeof o.config.resourceId == "string" && (t.has(o.config.resourceId) || n.push(Pe(
      `nodes.${o.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${o.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((o, u) => {
    const c = a.get(o.source), l = a.get(o.target);
    if (!c || !l) {
      n.push(Pe(`edges.${u}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const d = i.get(o.source), p = i.get(o.target);
    if (!d || !p) return;
    const v = ot(d, o.sourcePort, "outputs"), g = ot(p, o.targetPort, "inputs");
    if (!v || !g) {
      n.push(Pe(`edges.${u}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    Uo(v.type, g.type) || n.push(Pe(
      `edges.${u}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${v.type} → ${g.type} is not a legal connection`
    ));
  });
  for (const o of e.nodes) {
    const u = i.get(o.id);
    if (u)
      for (const c of u.inputs.filter((l) => l.required))
        e.edges.some((l) => l.target === o.id && l.targetPort === c.name && a.has(l.source)) || n.push(Pe(
          `nodes.${o.id}.inputs.${c.name}`,
          "MISSING_INPUT",
          `required input '${c.name}' is not connected`
        ));
  }
  const s = wv(e);
  return s && n.push(Pe(`nodes.${s[0]}`, "GRAPH_CYCLE", `cycle: ${s.join(" → ")}`)), n;
}
const Fo = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function Iv(e) {
  const r = Fo(e.additionalProperties).enum;
  if (!Array.isArray(r)) return null;
  const t = r.filter((n) => typeof n == "string");
  return t.length > 0 ? t : null;
}
function Nh(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && Fo(e.items).type === "string" ? "string-list" : e.type === "object" && Fo(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function jo(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, r]) => r != null && typeof r != "object").map(([r, t]) => ({ key: r, value: String(t) }));
}
function _u(e) {
  const r = {}, t = [];
  for (const n of e) {
    const a = n.key.trim();
    a && (Object.prototype.hasOwnProperty.call(r, a) && t.push(a), r[a] = n.value);
  }
  return { record: r, duplicateKeys: t };
}
function rt(e) {
  if (!Array.isArray(e)) return [];
  const r = /* @__PURE__ */ new Set(), t = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const a = n.trim();
    !a || r.has(a) || (r.add(a), t.push(a));
  }
  return t;
}
function qv(e, r) {
  const t = /* @__PURE__ */ new Set();
  for (const [n, a] of Object.entries(e))
    if (Nh(a) === "key-value")
      for (const i of jo(r[n]))
        i.key.trim() && t.add(i.key.trim());
  return [...t];
}
function Cv({
  value: e,
  onChange: r,
  valueOptions: t,
  keyTitle: n = "目标字段",
  valueTitle: a = "来源字段",
  testId: i
}) {
  const s = JSON.stringify(e ?? {}), [o, u] = I(() => jo(e)), [c, l] = I(s);
  _e(() => {
    s !== c && (u(jo(e)), l(s));
  }, [s, c, e]);
  const d = (v) => {
    u(v), r == null || r(_u(v).record);
  }, { duplicateKeys: p } = _u(o);
  return /* @__PURE__ */ x("div", { "data-testid": i, className: "industrial-kv-editor", children: [
    o.map((v, g) => /* @__PURE__ */ x("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ f(
        H,
        {
          "aria-label": `${n} ${g + 1}`,
          value: v.key,
          placeholder: n,
          onChange: (h) => d(o.map((y, C) => C === g ? { ...y, key: h.target.value } : y))
        }
      ),
      t && t.length > 0 ? /* @__PURE__ */ f(
        Ce,
        {
          "aria-label": `${a} ${g + 1}`,
          value: v.value || void 0,
          placeholder: a,
          options: t.map((h) => ({ label: h, value: h })),
          onChange: (h) => d(o.map((y, C) => C === g ? { ...y, value: h ?? "" } : y))
        }
      ) : /* @__PURE__ */ f(
        H,
        {
          "aria-label": `${a} ${g + 1}`,
          value: v.value,
          placeholder: a,
          onChange: (h) => d(o.map((y, C) => C === g ? { ...y, value: h.target.value } : y))
        }
      ),
      /* @__PURE__ */ f(
        $,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${g + 1}`,
          onClick: () => d(o.filter((h, y) => y !== g)),
          children: "删"
        }
      )
    ] }, g)),
    /* @__PURE__ */ f(
      $,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => d([...o, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    p.length > 0 && /* @__PURE__ */ x(U.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      p.join("、")
    ] })
  ] });
}
function Ev({ value: e, onChange: r, suggestions: t = [], testId: n }) {
  const a = JSON.stringify(rt(e)), [i, s] = I(() => rt(e)), [o, u] = I(a);
  _e(() => {
    a !== o && (s(rt(e)), u(a));
  }, [a, o, e]);
  const c = (l) => {
    s(l), r == null || r(rt(l));
  };
  return /* @__PURE__ */ x("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ f("datalist", { id: `${n ?? "list"}-suggestions`, children: t.map((l) => /* @__PURE__ */ f("option", { value: l }, l)) }),
    i.map((l, d) => /* @__PURE__ */ x("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ f(
        H,
        {
          "aria-label": `字段 ${d + 1}`,
          value: l,
          list: `${n ?? "list"}-suggestions`,
          onChange: (p) => c(i.map((v, g) => g === d ? p.target.value : v))
        }
      ),
      /* @__PURE__ */ f(
        $,
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
    /* @__PURE__ */ f($, { size: "small", type: "dashed", block: !0, onClick: () => c([...i, ""]), children: "添加字段" }),
    i.length === 0 && /* @__PURE__ */ f(U.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Sv({ value: e, onChange: r }) {
  const t = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, a] = I(t), [i, s] = I(null);
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
  return /* @__PURE__ */ x("div", { children: [
    /* @__PURE__ */ f(H.TextArea, { rows: 5, value: n, onChange: (u) => o(u.target.value) }),
    i && /* @__PURE__ */ f(U.Text, { type: "danger", style: { fontSize: 11 }, children: i })
  ] });
}
function Tv({ value: e, onChange: r, resources: t, resourceType: n, label: a, onCreate: i }) {
  const s = t.filter((u) => u.enabled !== !1 && (!n || u.type === n)).map((u) => ({
    value: u.id,
    label: `${u.name} (${u.type})`
  })), o = t.find((u) => u.id === e);
  return /* @__PURE__ */ x("div", { children: [
    /* @__PURE__ */ x(
      "select",
      {
        "aria-label": a,
        value: e ?? "",
        onChange: (u) => r == null ? void 0 : r(u.target.value || void 0),
        children: [
          /* @__PURE__ */ f("option", { value: "", children: "未选择" }),
          s.map((u) => /* @__PURE__ */ f("option", { value: u.value, children: u.label }, u.value))
        ]
      }
    ),
    o && /* @__PURE__ */ x(
      U.Text,
      {
        type: o.enabled === !1 ? "danger" : "success",
        "data-testid": "resource-picker-status",
        style: { fontSize: 11, display: "block" },
        children: [
          o.name,
          " · ",
          o.type,
          " · ",
          o.enabled === !1 ? "已停用" : "已启用",
          o.credentialId ? ` · 凭证 ${o.credentialName ?? o.credentialId}` : ""
        ]
      }
    ),
    /* @__PURE__ */ f($, { type: "link", size: "small", "data-testid": "resource-picker-create", onClick: i, children: "新建资源" })
  ] });
}
function xv({ name: e, schema: r, required: t, resources: n, resourceType: a, properties: i, config: s, onCreateResource: o }) {
  let u, c = "value";
  switch (Nh(r)) {
    case "resource":
      u = /* @__PURE__ */ f(
        Tv,
        {
          resources: n,
          resourceType: a,
          label: String(r.title ?? e),
          onCreate: o
        }
      );
      break;
    case "enum":
      u = /* @__PURE__ */ f(
        Ce,
        {
          allowClear: !0,
          options: (r.enum ?? []).map((l) => ({ label: String(l), value: l }))
        }
      );
      break;
    case "number":
      u = /* @__PURE__ */ f(it, { style: { width: "100%" } });
      break;
    case "boolean":
      u = /* @__PURE__ */ f(pt, {}), c = "checked";
      break;
    case "key-value":
      u = /* @__PURE__ */ f(
        Cv,
        {
          testId: `field-${e}`,
          valueOptions: Iv(r) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      u = /* @__PURE__ */ f(Ev, { testId: `field-${e}`, suggestions: qv(i, s) });
      break;
    case "json":
      u = /* @__PURE__ */ f(Sv, {});
      break;
    default:
      u = /* @__PURE__ */ f(H, { placeholder: r.description });
      break;
  }
  return /* @__PURE__ */ f(
    $o.Item,
    {
      name: e,
      label: r.title ?? e,
      tooltip: r.description,
      extra: (() => {
        const l = r.examples, d = [
          r.description,
          r.default === void 0 ? null : `默认：${JSON.stringify(r.default)}`,
          Array.isArray(l) && l.length > 0 ? `示例：${l.map((p) => String(p)).join("、")}` : null
        ].filter((p) => typeof p == "string" && p.length > 0);
        return d.length > 0 ? /* @__PURE__ */ f("span", { "data-testid": `field-hint-${e}`, children: d.join(" · ") }) : void 0;
      })(),
      valuePropName: c,
      rules: t && c === "value" ? [{ required: !0, message: `请填写 ${r.title ?? e}` }] : [],
      children: u
    },
    e
  );
}
function Rv(e) {
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
    onCreateResource: g,
    selectedEdgeId: h,
    sampleLines: y
  } = e, [C] = $o.useForm(), [R, w] = I("{}"), [A, M] = I(null), [b, k] = I(), [O, V] = I(), [G, S] = I(!1), [j, K] = I(!1);
  _e(() => {
    C.setFieldsValue((r == null ? void 0 : r.config) ?? {}), w(JSON.stringify((r == null ? void 0 : r.config) ?? {}, null, 2)), M(null);
  }, [C, r == null ? void 0 : r.id, r == null ? void 0 : r.config]);
  const z = new Set((t == null ? void 0 : t.configurationSchema.required) ?? []), ae = Object.entries((t == null ? void 0 : t.configurationSchema.properties) ?? {}), re = () => {
    if (ae.length > 0) {
      C.validateFields().then((E) => u(mv((r == null ? void 0 : r.config) ?? {}, E)));
      return;
    }
    try {
      const E = JSON.parse(R);
      if (E === null || typeof E != "object" || Array.isArray(E))
        throw new Error("配置必须是 JSON 对象");
      M(null), u(E);
    } catch (E) {
      M(E.message);
    }
  }, B = (r && !G ? a.map((E, T) => ({ edge: E, index: T })).filter(({ edge: E }) => E.source === r.id || E.target === r.id) : a.map((E, T) => ({ edge: E, index: T }))).map(({ edge: E, index: T }) => ({
    ...E,
    index: T,
    key: `${T}:${E.source}->${E.target}`
  })), Q = [
    {
      title: "起点节点",
      key: "reconnect-source",
      width: 108,
      render: (E, T) => /* @__PURE__ */ f(
        "select",
        {
          "aria-label": `连线 ${T.index} 起点节点`,
          value: T.source,
          onChange: (L) => p(T.index, { ...T, source: L.target.value }),
          children: n.map((L) => /* @__PURE__ */ f("option", { value: L.id, children: L.id }, L.id))
        }
      )
    },
    {
      title: "终点节点",
      key: "reconnect-target",
      width: 108,
      render: (E, T) => /* @__PURE__ */ f(
        "select",
        {
          "aria-label": `连线 ${T.index} 终点节点`,
          value: T.target,
          onChange: (L) => p(T.index, { ...T, target: L.target.value }),
          children: n.map((L) => /* @__PURE__ */ f("option", { value: L.id, children: L.id }, L.id))
        }
      )
    },
    {
      title: "端口",
      key: "ports",
      render: (E, T) => {
        const L = i.get(T.source), fe = i.get(T.target);
        return /* @__PURE__ */ x("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ f(
            Ce,
            {
              size: "small",
              value: T.sourcePort,
              style: { width: 110 },
              onChange: (pe) => p(T.index, { ...T, sourcePort: pe }),
              options: ((L == null ? void 0 : L.outputs) ?? []).map((pe) => ({ label: `${pe.name} (${pe.type})`, value: pe.name }))
            }
          ),
          /* @__PURE__ */ f(
            Ce,
            {
              size: "small",
              value: T.targetPort,
              style: { width: 110 },
              onChange: (pe) => p(T.index, { ...T, targetPort: pe }),
              options: ((fe == null ? void 0 : fe.inputs) ?? []).map((pe) => ({ label: `${pe.name} (${pe.type})`, value: pe.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (E, T) => /* @__PURE__ */ f($, { danger: !0, size: "small", type: "text", onClick: () => l(T.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ x("div", { className: "industrial-properties-body", children: [
    !r && /* @__PURE__ */ f(xh, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    r && /* @__PURE__ */ x(dr, { children: [
      /* @__PURE__ */ f(U.Title, { level: 5, style: { margin: 0 }, children: (t == null ? void 0 : t.name) ?? r.type }),
      /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 11 }, children: r.id }),
      /* @__PURE__ */ f(U.Paragraph, { type: "secondary", style: { fontSize: 12, marginTop: 6 }, children: (t == null ? void 0 : t.description) ?? "请按业务含义配置该节点。" }),
      /* @__PURE__ */ x(W, { wrap: !0, size: 4, style: { marginBottom: 8 }, children: [
        /* @__PURE__ */ f($e, { color: "blue", children: (t == null ? void 0 : t.category) ?? "采集节点" }),
        /* @__PURE__ */ x($e, { children: [
          "版本 ",
          (t == null ? void 0 : t.version) ?? "1.0.0"
        ] }),
        t == null ? void 0 : t.inputs.map((E) => /* @__PURE__ */ x($e, { color: E.required ? "red" : "default", children: [
          "输入 ",
          E.name,
          " · ",
          E.type
        ] }, `in-${E.name}`)),
        t == null ? void 0 : t.outputs.map((E) => /* @__PURE__ */ x($e, { color: "green", children: [
          "输出 ",
          E.name,
          " · ",
          E.type
        ] }, `out-${E.name}`))
      ] }),
      (r.type.includes("reader") || r.type.includes("mapping") || r.type.includes("source") || r.type.includes("filesystem")) && y && y.length > 0 && /* @__PURE__ */ x(dr, { children: [
        /* @__PURE__ */ f(U.Text, { strong: !0, style: { fontSize: 12 }, children: "样本预览" }),
        /* @__PURE__ */ f("pre", { className: "industrial-sample-preview", children: y.join(`
`) })
      ] }),
      /* @__PURE__ */ f($o, { form: C, layout: "vertical", size: "small", style: { marginTop: 12 }, children: ae.map(([E, T]) => /* @__PURE__ */ f(
        xv,
        {
          name: E,
          schema: T,
          required: z.has(E),
          resources: s,
          resourceType: typeof (t == null ? void 0 : t.configurationSchema.resourceType) == "string" ? t.configurationSchema.resourceType : void 0,
          properties: (t == null ? void 0 : t.configurationSchema.properties) ?? {},
          config: (r == null ? void 0 : r.config) ?? {},
          onCreateResource: g
        },
        E
      )) }),
      ae.length === 0 && /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 12 }, children: "此节点无需填写业务参数；保留默认配置即可。" }),
      /* @__PURE__ */ f($, { type: "link", size: "small", onClick: () => K((E) => !E), children: j ? "收起高级配置" : "高级配置" }),
      j && /* @__PURE__ */ x(dr, { children: [
        /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 12 }, children: "高级配置直接编辑节点 JSON，仅在需要复杂表达式时使用。" }),
        /* @__PURE__ */ f(
          H.TextArea,
          {
            rows: 8,
            value: R,
            style: { marginTop: 6 },
            onChange: (E) => w(E.target.value)
          }
        ),
        A && /* @__PURE__ */ f(U.Text, { type: "danger", style: { fontSize: 12 }, children: A }),
        (t == null ? void 0 : t.activityType) && /* @__PURE__ */ x(U.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
          "运行时 Activity：",
          t.activityType
        ] })
      ] }),
      /* @__PURE__ */ f($, { type: "primary", block: !0, onClick: re, children: "应用配置" }),
      /* @__PURE__ */ f($, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: c, children: "删除节点" })
    ] }),
    h && (() => {
      const E = a.findIndex((L) => `e:${L.source}:${L.sourcePort}>${L.target}:${L.targetPort}` === h);
      if (E < 0) return null;
      const T = a[E];
      return /* @__PURE__ */ f(
        Se,
        {
          type: "info",
          showIcon: !0,
          style: { marginTop: 8 },
          message: `当前连线：${T.source}.${T.sourcePort} → ${T.target}.${T.targetPort}`,
          description: /* @__PURE__ */ f($, { size: "small", danger: !0, onClick: () => l(E), children: "删除此连线" })
        }
      );
    })(),
    /* @__PURE__ */ x(hu, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "相关连线 (",
      B.length,
      " / ",
      a.length,
      ")"
    ] }),
    r && /* @__PURE__ */ f($, { type: "link", size: "small", onClick: () => S((E) => !E), children: G ? "仅看当前节点" : "显示全部连线" }),
    n.length > 1 && /* @__PURE__ */ x(W, { wrap: !0, style: { marginBottom: 8 }, children: [
      /* @__PURE__ */ x(
        "select",
        {
          "aria-label": "连线起点",
          value: b ?? "",
          onChange: (E) => k(E.target.value || void 0),
          children: [
            /* @__PURE__ */ f("option", { value: "", children: "起点节点" }),
            n.map((E) => /* @__PURE__ */ f("option", { value: E.id, children: E.id }, E.id))
          ]
        }
      ),
      /* @__PURE__ */ x(
        "select",
        {
          "aria-label": "连线终点",
          value: O ?? "",
          onChange: (E) => V(E.target.value || void 0),
          children: [
            /* @__PURE__ */ f("option", { value: "", children: "终点节点" }),
            n.map((E) => /* @__PURE__ */ f("option", { value: E.id, children: E.id }, E.id))
          ]
        }
      ),
      /* @__PURE__ */ f(
        $,
        {
          size: "small",
          disabled: !b || !O || b === O,
          onClick: () => {
            b && O && d(b, O);
          },
          children: "添加连线"
        }
      )
    ] }),
    a.length === 0 ? /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ f(Oe, { size: "small", columns: Q, dataSource: B, pagination: !1 }),
    o.length > 0 && /* @__PURE__ */ x(dr, { children: [
      /* @__PURE__ */ x(hu, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        o.length,
        ")"
      ] }),
      o.map((E, T) => /* @__PURE__ */ x("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ f($e, { color: "red", children: E.code }),
        /* @__PURE__ */ x(
          U.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const L = /^nodes\.([^.]+)/.exec(E.path);
              L && v(L[1]);
            },
            children: [
              E.path,
              ": ",
              E.message
            ]
          }
        )
      ] }, `${E.path}-${T}`))
    ] })
  ] });
}
const kv = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class Av extends Error {
  constructor(r) {
    super(r), this.name = "DtoError";
  }
}
const ne = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, Ie = (e) => Array.isArray(e) ? e : [], P = (e, r = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : r ?? "", me = (e, r = 0) => {
  const t = typeof e == "string" ? Number(e) : e;
  return typeof t == "number" && Number.isFinite(t) ? t : r;
}, _ = (e, ...r) => {
  for (const t of r)
    if (e[t] !== void 0 && e[t] !== null) return e[t];
};
function Re(e) {
  const r = ne(e);
  if (Object.prototype.hasOwnProperty.call(r, "data")) return r.data;
  if (typeof r.code == "string" && typeof r.message == "string")
    throw new Av(`${r.code}: ${r.message}`);
  return e;
}
function Pv(e) {
  const r = P(e).trim().toLowerCase();
  return kv.find((n) => n.toLowerCase() === r) ?? "Unknown";
}
function Ov(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const r = ne(e), t = P(_(r, "name", "port", "id")).trim();
  return t ? {
    name: t,
    type: Pv(_(r, "type", "portType", "dataType")),
    required: _(r, "required", "isRequired") === !0
  } : null;
}
function wu(e) {
  const r = Ie(e).map(Ov).filter((n) => n !== null), t = /* @__PURE__ */ new Set();
  return r.filter((n) => t.has(n.name) ? !1 : (t.add(n.name), !0));
}
const Nv = (e) => {
  const r = ne(e);
  return Object.keys(r).length === 0 ? { type: "object", properties: {} } : r;
};
function Dv(e) {
  const r = ne(e), t = P(_(r, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return t ? {
    nodeTypeId: t,
    name: P(_(r, "name", "title"), t),
    description: P(_(r, "description", "desc")),
    category: P(_(r, "category", "group"), "General"),
    icon: P(_(r, "icon"), "AppstoreOutlined"),
    version: P(_(r, "version"), "1.0.0"),
    inputs: wu(_(r, "inputs", "inputPorts", "input_ports")),
    outputs: wu(_(r, "outputs", "outputPorts", "output_ports")),
    configurationSchema: Nv(_(r, "configurationSchema", "configSchema", "schema")),
    activityType: P(_(r, "activityType", "activity_type")),
    dependencies: Ie(_(r, "dependencies")).map((n) => P(n)).filter(Boolean)
  } : null;
}
function Dh(e) {
  const r = Re(e), t = Array.isArray(r) ? r : Ie(_(ne(r), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return t.map(Dv).filter((a) => a !== null).filter((a) => n.has(a.nodeTypeId) ? !1 : (n.add(a.nodeTypeId), !0));
}
function Mh(e) {
  const r = Re(e), t = Array.isArray(r) ? r : Ie(_(ne(r), "resources", "items")), n = /* @__PURE__ */ new Set();
  return t.map((a) => {
    const i = ne(a), s = P(_(i, "id")).trim();
    return !s || n.has(s) ? null : (n.add(s), {
      id: s,
      name: P(_(i, "name"), s),
      type: P(_(i, "type"), "unknown"),
      enabled: i.enabled !== !1,
      credentialId: typeof i.credentialId == "string" ? i.credentialId : null,
      credentialName: typeof i.credentialName == "string" ? i.credentialName : null
    });
  }).filter((a) => a !== null);
}
function $h(e) {
  const r = ne(e), t = P(_(r, "workflowId", "workflow_id", "id")).trim();
  return t ? {
    workflowId: t,
    name: P(_(r, "name", "title"), t),
    version: me(_(r, "version"), 1)
  } : null;
}
function Lh(e) {
  const r = Re(e);
  return (Array.isArray(r) ? r : Ie(_(ne(r), "workflows", "items"))).map($h).filter((n) => n !== null);
}
function ut(e) {
  const r = ne(e), t = {};
  for (const [n, a] of Object.entries(r))
    a !== void 0 && (t[n] = a);
  return t;
}
function Mv(e) {
  const r = ne(e), t = P(_(r, "id", "nodeId", "node_id")).trim();
  if (!t) return null;
  const n = ne(_(r, "position", "layout"));
  return {
    id: t,
    type: P(_(r, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: ut(_(r, "config", "configuration")),
    x: me(_(r, "x") ?? _(n, "x"), 0),
    y: me(_(r, "y") ?? _(n, "y"), 0)
  };
}
function $v(e) {
  const r = ne(e), t = P(_(r, "source", "from", "sourceId")).trim(), n = P(_(r, "target", "to", "targetId")).trim();
  return !t || !n ? null : {
    source: t,
    sourcePort: P(_(r, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: P(_(r, "targetPort", "target_port", "toPort"), "in")
  };
}
function Lv(e) {
  const r = Re(e), t = ne(r), n = $h(t) ?? { workflowId: "", name: "", version: 1 }, a = Ie(_(t, "nodes")).map(Mv).filter((u) => u !== null), i = Ie(_(t, "edges")).map($v).filter((u) => u !== null), s = ne(_(t, "trigger")), o = {
    kind: P(_(s, "kind", "type"), "manual"),
    cron: _(s, "cron") === void 0 ? null : P(s.cron),
    eventName: _(s, "eventName", "event_name") === void 0 ? null : P(_(s, "eventName", "event_name")),
    config: ut(_(s, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: a,
    edges: i,
    trigger: o,
    resourceReferences: Ie(_(t, "resourceReferences", "resource_references")).map((u) => P(u)).filter(Boolean),
    layout: ut(_(t, "layout"))
  };
}
const xr = (e) => e === !0 || typeof e == "string" && e.toLowerCase() === "true";
function Fv(e) {
  const r = Re(e);
  return (Array.isArray(r) ? r : Ie(_(ne(r), "resources", "items"))).map((n) => {
    const a = ne(n), i = _(a, "credentialId", "credential_id");
    return {
      id: P(_(a, "id"), "—"),
      name: P(_(a, "name"), "—"),
      type: P(_(a, "type"), "—"),
      provider: P(_(a, "provider"), "—"),
      enabled: xr(_(a, "enabled")),
      credentialId: i == null ? null : P(i),
      credentialName: _(a, "credentialName") == null ? null : P(_(a, "credentialName")),
      config: ut(_(a, "config", "configuration"))
    };
  });
}
function Fh(e) {
  const r = Re(e);
  return (Array.isArray(r) ? r : Ie(_(ne(r), "credentials", "items"))).map((n) => {
    const a = ne(n);
    return {
      id: P(_(a, "id"), "—"),
      name: P(_(a, "name"), "—"),
      kind: P(_(a, "kind"), "—"),
      version: me(_(a, "version"))
    };
  });
}
function jv(e) {
  const r = Re(e);
  return (Array.isArray(r) ? r : Ie(_(ne(r), "tests", "items"))).map((n) => {
    const a = ne(n);
    return {
      resourceId: P(_(a, "resourceId", "resource_id"), "—"),
      ok: xr(_(a, "ok")),
      detail: P(_(a, "detail"), "")
    };
  });
}
const jh = (e) => {
  if (e == null) return null;
  const r = ne(e);
  return Object.keys(r).length === 0 ? null : {
    code: P(_(r, "code"), "OTHER"),
    message: P(_(r, "message"), ""),
    action: P(_(r, "action"), "")
  };
};
function Gv(e) {
  const r = Re(e);
  return Ie(_(ne(r), "triggers", "items")).map((n) => {
    const a = ne(n), i = (s) => _(a, s) == null ? null : P(_(a, s));
    return {
      workflowId: P(_(a, "workflowId", "workflow_id"), "—"),
      version: me(_(a, "version")),
      kind: P(_(a, "kind"), "manual"),
      cron: i("cron"),
      eventName: i("eventName"),
      timezone: i("timezone"),
      resourceId: i("resourceId"),
      nextRun: i("nextRun"),
      lastStatus: i("lastStatus"),
      lastStartedAt: i("lastStartedAt"),
      lastFireStatus: i("lastFireStatus"),
      lastFireDueAt: i("lastFireDueAt"),
      lastFireAttempts: _(a, "lastFireAttempts") == null ? null : me(_(a, "lastFireAttempts")),
      skippedFires: _(a, "skippedFires") == null ? null : me(_(a, "skippedFires")),
      backlogTruncated: _(a, "backlogTruncated") == null ? null : _(a, "backlogTruncated") === !0,
      lastError: i("lastError")
    };
  });
}
function zv(e) {
  if (e == null) return null;
  const r = Re(e), t = ne(r);
  if (Object.keys(t).length === 0) return null;
  const n = Ie(_(t, "files")).map((i) => {
    const s = ne(i);
    return {
      path: P(_(s, "path"), "—"),
      sourceDate: P(_(s, "sourceDate"), ""),
      inLedger: xr(_(s, "inLedger")),
      expectedTargets: me(_(s, "expectedTargets")),
      succeededTargets: me(_(s, "succeededTargets")),
      incompleteTargets: me(_(s, "incompleteTargets")),
      missingTargets: P(_(s, "missingTargets"), ""),
      wouldSkip: xr(_(s, "wouldSkip")),
      wouldCollect: xr(_(s, "wouldCollect")),
      duplicateRisk: xr(_(s, "duplicateRisk"))
    };
  }), a = ne(_(t, "summary"));
  return {
    resourceId: P(_(t, "resourceId"), "—"),
    from: P(_(t, "from"), ""),
    to: P(_(t, "to"), ""),
    files: n,
    summary: {
      discovered: me(_(a, "discovered")),
      alreadyComplete: me(_(a, "alreadyComplete")),
      needsCollection: me(_(a, "needsCollection")),
      duplicateRisk: me(_(a, "duplicateRisk"))
    }
  };
}
function Bv(e) {
  const r = Re(e);
  return (Array.isArray(r) ? r : Ie(_(ne(r), "files", "items"))).map((n) => {
    const a = ne(n);
    return {
      id: P(_(a, "id"), "—"),
      path: P(_(a, "path"), "—"),
      identityKey: P(_(a, "identityKey"), ""),
      sourceResourceId: P(_(a, "sourceResourceId"), ""),
      length: me(_(a, "length")),
      sourceDate: P(_(a, "sourceDate"), ""),
      collectedAt: P(_(a, "collectedAt"), ""),
      status: P(_(a, "status"), "—"),
      deliveries: Ie(_(a, "deliveries")).map((i) => {
        const s = ne(i);
        return {
          id: P(_(s, "id"), "—"),
          collectionFileId: P(_(s, "collectionFileId"), ""),
          targetId: P(_(s, "targetId"), "—"),
          targetKind: P(_(s, "targetKind"), "record"),
          status: P(_(s, "status"), "—"),
          attempts: me(_(s, "attempts")),
          records: me(_(s, "records")),
          error: _(s, "error") == null ? null : P(_(s, "error")),
          accepted: me(_(s, "accepted")),
          rejected: me(_(s, "rejected")),
          warnings: me(_(s, "warnings")),
          diagnosis: jh(_(s, "diagnosis"))
        };
      })
    };
  });
}
function Uv(e) {
  const r = Re(e), t = ne(r), n = Ie(_(t, "runs")).map((i) => {
    const s = ne(i);
    return {
      id: P(_(s, "id"), "—"),
      workflowId: P(_(s, "workflowId", "workflow_id"), "—"),
      status: P(_(s, "status"), "—"),
      startedAt: P(_(s, "startedAt", "started_at"), ""),
      finishedAt: _(s, "finishedAt", "finished_at") == null ? null : P(_(s, "finishedAt", "finished_at")),
      error: _(s, "error") == null ? null : P(_(s, "error"))
    };
  }), a = Ie(_(t, "deliveries")).map((i) => {
    const s = ne(i);
    return {
      id: P(_(s, "id"), "—"),
      collectionFileId: P(_(s, "collectionFileId", "collection_file_id"), ""),
      targetId: P(_(s, "targetId", "target_id"), "—"),
      targetKind: P(_(s, "targetKind", "target_kind"), "record"),
      status: P(_(s, "status"), "—"),
      attempts: me(_(s, "attempts")),
      records: me(_(s, "records")),
      error: _(s, "error") == null ? null : P(_(s, "error")),
      diagnosis: jh(_(s, "diagnosis"))
    };
  });
  return { runs: n, deliveries: a };
}
function Kv(e) {
  if (e == null) return null;
  const r = Re(e), t = ne(r);
  if (Object.keys(t).length === 0) return null;
  const n = Ie(_(t, "nodes")).slice(0, 64).map((a) => {
    const i = ne(a);
    return {
      nodeId: P(_(i, "nodeId", "node_id"), "—"),
      status: P(_(i, "status"), "—"),
      records: me(_(i, "records")),
      files: me(_(i, "files")),
      error: _(i, "error") == null ? null : P(_(i, "error"), void 0),
      samples: Ie(_(i, "samples")).slice(0, 3).map((s) => P(s))
    };
  });
  return {
    status: P(_(t, "status"), "—"),
    error: _(t, "error") == null ? null : P(_(t, "error"), void 0),
    executed: Ie(_(t, "executed")).map((a) => P(a)),
    instanceId: _(t, "instanceId") == null ? null : P(_(t, "instanceId"), void 0),
    definitionId: _(t, "definitionId") == null ? null : P(_(t, "definitionId"), void 0),
    nodes: n
  };
}
const Vv = [
  { value: "manual", label: "手动运行" },
  { value: "schedule", label: "定时运行" },
  { value: "event", label: "事件触发" }
], Hv = [
  { label: "每天 02:00", cron: "0 2 * * *" },
  { label: "每小时", cron: "0 * * * *" },
  { label: "每 5 分钟", cron: "*/5 * * * *" },
  { label: "工作日 08:30", cron: "30 8 * * 1-5" }
];
function Wv({
  open: e,
  onClose: r,
  trigger: t,
  resourceIds: n,
  onSave: a,
  ctx: i,
  workflowId: s
}) {
  const [o, u] = I(t), [c, l] = I([]), [d, p] = I(null);
  _e(() => {
    u(t);
  }, [t]), _e(() => {
    e && i.hubQuery("industrial/trigger-status").then((h) => l(Gv(h))).catch((h) => p(h instanceof Error ? h.message : String(h)));
  }, [i, e]);
  const v = c.find((h) => h.workflowId === s);
  return /* @__PURE__ */ f(
    mr,
    {
      open: e,
      title: "触发器配置",
      okText: "保存触发器",
      cancelText: "取消",
      onCancel: r,
      onOk: () => {
        var h, y;
        if (o.kind === "schedule" && !((h = o.cron) != null && h.trim())) {
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
      children: /* @__PURE__ */ x(W, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ x(W, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(U.Text, { children: "触发方式" }),
          /* @__PURE__ */ f(W, { wrap: !0, children: Vv.map((h) => /* @__PURE__ */ f(
            $,
            {
              type: o.kind === h.value ? "primary" : "default",
              onClick: () => u({ ...o, kind: h.value }),
              children: h.label
            },
            h.value
          )) })
        ] }),
        /* @__PURE__ */ f(W, { wrap: !0, children: /* @__PURE__ */ f(
          Ce,
          {
            "aria-label": "运行资源",
            allowClear: !0,
            placeholder: "运行资源",
            value: Qv(o),
            options: n.map((h) => ({ value: h, label: h })),
            onChange: (h) => u({
              ...o,
              config: { ...o.config, resourceId: h ?? "" }
            })
          }
        ) }),
        /* @__PURE__ */ x(W, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(U.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ x(W, { wrap: !0, children: [
            /* @__PURE__ */ f(U.Text, { children: "常用" }),
            Hv.map((h) => /* @__PURE__ */ f(
              $,
              {
                size: "small",
                onClick: () => u({ ...o, cron: h.cron }),
                children: h.label
              },
              h.cron
            ))
          ] }),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "Cron 表达式",
              placeholder: "分 时 日 月 周，例如 0 2 * * *",
              value: o.cron ?? "",
              onChange: (h) => u({ ...o, cron: h.target.value })
            }
          ),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "调度时区",
              placeholder: "时区（默认 UTC，例如 Asia/Shanghai）",
              value: Yv(o),
              onChange: (h) => u({
                ...o,
                config: { ...o.config, timezone: h.target.value }
              })
            }
          ),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "事件名称",
              placeholder: "事件名称",
              value: o.eventName ?? "",
              onChange: (h) => u({ ...o, eventName: h.target.value })
            }
          ),
          /* @__PURE__ */ x(U.Text, { type: "secondary", children: [
            "下次运行：",
            (v == null ? void 0 : v.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        v && /* @__PURE__ */ f(
          Oe,
          {
            rowKey: "workflowId",
            dataSource: [v],
            pagination: !1,
            columns: [
              { title: "时区", dataIndex: "timezone", render: (h) => h ?? "UTC" },
              { title: "状态", dataIndex: "lastStatus", render: (h) => h ?? "未运行" },
              { title: "上次运行", dataIndex: "lastStartedAt", render: (h) => h ?? "—" },
              {
                title: "触发点",
                dataIndex: "lastFireStatus",
                render: (h, y) => y.lastFireStatus ? `${y.lastFireStatus} · ${y.lastFireDueAt ?? "—"} · ${y.lastFireAttempts ?? 0} 次` + (y.skippedFires ? ` · 跳过≥${y.skippedFires}` : "") + (y.backlogTruncated ? " · 积压扫描截断" : "") : "—"
              },
              { title: "错误", dataIndex: "lastError", render: (h) => h ?? "—" }
            ]
          }
        ),
        d && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: d })
      ] })
    }
  );
}
function Qv(e) {
  const r = e.config.resourceId;
  return typeof r == "string" && r.trim() ? r : void 0;
}
function Yv(e) {
  const r = e.config.timezone;
  return typeof r == "string" && r.trim() ? r : "UTC";
}
function Jv({
  open: e,
  onClose: r,
  ctx: t,
  workflowId: n,
  resourceId: a,
  lookbackDays: i
}) {
  const [s, o] = I(null), [u, c] = I(null), [l, d] = I(!1), p = F(async () => {
    d(!0), c(null);
    try {
      await t.hubCommand("industrial/preview-backfill", {
        workflowId: n,
        resourceId: a,
        lookbackDays: i
      }), o(zv(await t.hubQuery("industrial/backfill-preview")));
    } catch (v) {
      c(v instanceof Error ? v.message : String(v));
    } finally {
      d(!1);
    }
  }, [t, i, a, n]);
  return /* @__PURE__ */ f(
    mr,
    {
      open: e,
      title: "补采预览",
      onCancel: r,
      width: 820,
      footer: [
        /* @__PURE__ */ f($, { loading: l, onClick: () => void p(), children: "重新扫描" }, "refresh"),
        /* @__PURE__ */ f($, { type: "primary", onClick: r, children: "关闭" }, "close")
      ],
      children: /* @__PURE__ */ x(W, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ x(W, { children: [
          /* @__PURE__ */ x($, { type: "primary", loading: l, onClick: () => void p(), children: [
            "扫描最近 ",
            i,
            " 天"
          ] }),
          /* @__PURE__ */ x(U.Text, { type: "secondary", children: [
            "资源：",
            a || "未选择"
          ] })
        ] }),
        u && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: u }),
        s && /* @__PURE__ */ x(dr, { children: [
          /* @__PURE__ */ x(U.Text, { children: [
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
          /* @__PURE__ */ f(
            Oe,
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
var tt = typeof globalThis < "u" ? globalThis : typeof window < "u" ? window : typeof global < "u" ? global : typeof self < "u" ? self : {};
function Xv(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
function Ko(e) {
  throw new Error('Could not dynamically require "' + e + '". Please configure the dynamicRequireTargets or/and ignoreDynamicRequires option of @rollup/plugin-commonjs appropriately for this require call to work.');
}
var $t, Iu;
function Zv() {
  if (Iu) return $t;
  Iu = 1;
  function e() {
    this.__data__ = [], this.size = 0;
  }
  return $t = e, $t;
}
var Lt, qu;
function Rr() {
  if (qu) return Lt;
  qu = 1;
  function e(r, t) {
    return r === t || r !== r && t !== t;
  }
  return Lt = e, Lt;
}
var Ft, Cu;
function vt() {
  if (Cu) return Ft;
  Cu = 1;
  var e = Rr();
  function r(t, n) {
    for (var a = t.length; a--; )
      if (e(t[a][0], n))
        return a;
    return -1;
  }
  return Ft = r, Ft;
}
var jt, Eu;
function eg() {
  if (Eu) return jt;
  Eu = 1;
  var e = vt(), r = Array.prototype, t = r.splice;
  function n(a) {
    var i = this.__data__, s = e(i, a);
    if (s < 0)
      return !1;
    var o = i.length - 1;
    return s == o ? i.pop() : t.call(i, s, 1), --this.size, !0;
  }
  return jt = n, jt;
}
var Gt, Su;
function rg() {
  if (Su) return Gt;
  Su = 1;
  var e = vt();
  function r(t) {
    var n = this.__data__, a = e(n, t);
    return a < 0 ? void 0 : n[a][1];
  }
  return Gt = r, Gt;
}
var zt, Tu;
function tg() {
  if (Tu) return zt;
  Tu = 1;
  var e = vt();
  function r(t) {
    return e(this.__data__, t) > -1;
  }
  return zt = r, zt;
}
var Bt, xu;
function ng() {
  if (xu) return Bt;
  xu = 1;
  var e = vt();
  function r(t, n) {
    var a = this.__data__, i = e(a, t);
    return i < 0 ? (++this.size, a.push([t, n])) : a[i][1] = n, this;
  }
  return Bt = r, Bt;
}
var Ut, Ru;
function gt() {
  if (Ru) return Ut;
  Ru = 1;
  var e = Zv(), r = eg(), t = rg(), n = tg(), a = ng();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, Ut = i, Ut;
}
var Kt, ku;
function ag() {
  if (ku) return Kt;
  ku = 1;
  var e = gt();
  function r() {
    this.__data__ = new e(), this.size = 0;
  }
  return Kt = r, Kt;
}
var Vt, Au;
function ig() {
  if (Au) return Vt;
  Au = 1;
  function e(r) {
    var t = this.__data__, n = t.delete(r);
    return this.size = t.size, n;
  }
  return Vt = e, Vt;
}
var Ht, Pu;
function sg() {
  if (Pu) return Ht;
  Pu = 1;
  function e(r) {
    return this.__data__.get(r);
  }
  return Ht = e, Ht;
}
var Wt, Ou;
function og() {
  if (Ou) return Wt;
  Ou = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return Wt = e, Wt;
}
var Qt, Nu;
function Gh() {
  if (Nu) return Qt;
  Nu = 1;
  var e = typeof tt == "object" && tt && tt.Object === Object && tt;
  return Qt = e, Qt;
}
var Yt, Du;
function Ue() {
  if (Du) return Yt;
  Du = 1;
  var e = Gh(), r = typeof self == "object" && self && self.Object === Object && self, t = e || r || Function("return this")();
  return Yt = t, Yt;
}
var Jt, Mu;
function kr() {
  if (Mu) return Jt;
  Mu = 1;
  var e = Ue(), r = e.Symbol;
  return Jt = r, Jt;
}
var Xt, $u;
function ug() {
  if ($u) return Xt;
  $u = 1;
  var e = kr(), r = Object.prototype, t = r.hasOwnProperty, n = r.toString, a = e ? e.toStringTag : void 0;
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
  return Xt = i, Xt;
}
var Zt, Lu;
function lg() {
  if (Lu) return Zt;
  Lu = 1;
  var e = Object.prototype, r = e.toString;
  function t(n) {
    return r.call(n);
  }
  return Zt = t, Zt;
}
var en, Fu;
function br() {
  if (Fu) return en;
  Fu = 1;
  var e = kr(), r = ug(), t = lg(), n = "[object Null]", a = "[object Undefined]", i = e ? e.toStringTag : void 0;
  function s(o) {
    return o == null ? o === void 0 ? a : n : i && i in Object(o) ? r(o) : t(o);
  }
  return en = s, en;
}
var rn, ju;
function je() {
  if (ju) return rn;
  ju = 1;
  function e(r) {
    var t = typeof r;
    return r != null && (t == "object" || t == "function");
  }
  return rn = e, rn;
}
var tn, Gu;
function Wr() {
  if (Gu) return tn;
  Gu = 1;
  var e = br(), r = je(), t = "[object AsyncFunction]", n = "[object Function]", a = "[object GeneratorFunction]", i = "[object Proxy]";
  function s(o) {
    if (!r(o))
      return !1;
    var u = e(o);
    return u == n || u == a || u == t || u == i;
  }
  return tn = s, tn;
}
var nn, zu;
function cg() {
  if (zu) return nn;
  zu = 1;
  var e = Ue(), r = e["__core-js_shared__"];
  return nn = r, nn;
}
var an, Bu;
function dg() {
  if (Bu) return an;
  Bu = 1;
  var e = cg(), r = function() {
    var n = /[^.]+$/.exec(e && e.keys && e.keys.IE_PROTO || "");
    return n ? "Symbol(src)_1." + n : "";
  }();
  function t(n) {
    return !!r && r in n;
  }
  return an = t, an;
}
var sn, Uu;
function zh() {
  if (Uu) return sn;
  Uu = 1;
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
  return sn = t, sn;
}
var on, Ku;
function fg() {
  if (Ku) return on;
  Ku = 1;
  var e = Wr(), r = dg(), t = je(), n = zh(), a = /[\\^$.*+?()[\]{}|]/g, i = /^\[object .+?Constructor\]$/, s = Function.prototype, o = Object.prototype, u = s.toString, c = o.hasOwnProperty, l = RegExp(
    "^" + u.call(c).replace(a, "\\$&").replace(/hasOwnProperty|(function).*?(?=\\\()| for .+?(?=\\\])/g, "$1.*?") + "$"
  );
  function d(p) {
    if (!t(p) || r(p))
      return !1;
    var v = e(p) ? l : i;
    return v.test(n(p));
  }
  return on = d, on;
}
var un, Vu;
function hg() {
  if (Vu) return un;
  Vu = 1;
  function e(r, t) {
    return r == null ? void 0 : r[t];
  }
  return un = e, un;
}
var ln, Hu;
function _r() {
  if (Hu) return ln;
  Hu = 1;
  var e = fg(), r = hg();
  function t(n, a) {
    var i = r(n, a);
    return e(i) ? i : void 0;
  }
  return ln = t, ln;
}
var cn, Wu;
function Vo() {
  if (Wu) return cn;
  Wu = 1;
  var e = _r(), r = Ue(), t = e(r, "Map");
  return cn = t, cn;
}
var dn, Qu;
function yt() {
  if (Qu) return dn;
  Qu = 1;
  var e = _r(), r = e(Object, "create");
  return dn = r, dn;
}
var fn, Yu;
function pg() {
  if (Yu) return fn;
  Yu = 1;
  var e = yt();
  function r() {
    this.__data__ = e ? e(null) : {}, this.size = 0;
  }
  return fn = r, fn;
}
var hn, Ju;
function vg() {
  if (Ju) return hn;
  Ju = 1;
  function e(r) {
    var t = this.has(r) && delete this.__data__[r];
    return this.size -= t ? 1 : 0, t;
  }
  return hn = e, hn;
}
var pn, Xu;
function gg() {
  if (Xu) return pn;
  Xu = 1;
  var e = yt(), r = "__lodash_hash_undefined__", t = Object.prototype, n = t.hasOwnProperty;
  function a(i) {
    var s = this.__data__;
    if (e) {
      var o = s[i];
      return o === r ? void 0 : o;
    }
    return n.call(s, i) ? s[i] : void 0;
  }
  return pn = a, pn;
}
var vn, Zu;
function yg() {
  if (Zu) return vn;
  Zu = 1;
  var e = yt(), r = Object.prototype, t = r.hasOwnProperty;
  function n(a) {
    var i = this.__data__;
    return e ? i[a] !== void 0 : t.call(i, a);
  }
  return vn = n, vn;
}
var gn, el;
function mg() {
  if (el) return gn;
  el = 1;
  var e = yt(), r = "__lodash_hash_undefined__";
  function t(n, a) {
    var i = this.__data__;
    return this.size += this.has(n) ? 0 : 1, i[n] = e && a === void 0 ? r : a, this;
  }
  return gn = t, gn;
}
var yn, rl;
function bg() {
  if (rl) return yn;
  rl = 1;
  var e = pg(), r = vg(), t = gg(), n = yg(), a = mg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, yn = i, yn;
}
var mn, tl;
function _g() {
  if (tl) return mn;
  tl = 1;
  var e = bg(), r = gt(), t = Vo();
  function n() {
    this.size = 0, this.__data__ = {
      hash: new e(),
      map: new (t || r)(),
      string: new e()
    };
  }
  return mn = n, mn;
}
var bn, nl;
function wg() {
  if (nl) return bn;
  nl = 1;
  function e(r) {
    var t = typeof r;
    return t == "string" || t == "number" || t == "symbol" || t == "boolean" ? r !== "__proto__" : r === null;
  }
  return bn = e, bn;
}
var _n, al;
function mt() {
  if (al) return _n;
  al = 1;
  var e = wg();
  function r(t, n) {
    var a = t.__data__;
    return e(n) ? a[typeof n == "string" ? "string" : "hash"] : a.map;
  }
  return _n = r, _n;
}
var wn, il;
function Ig() {
  if (il) return wn;
  il = 1;
  var e = mt();
  function r(t) {
    var n = e(this, t).delete(t);
    return this.size -= n ? 1 : 0, n;
  }
  return wn = r, wn;
}
var In, sl;
function qg() {
  if (sl) return In;
  sl = 1;
  var e = mt();
  function r(t) {
    return e(this, t).get(t);
  }
  return In = r, In;
}
var qn, ol;
function Cg() {
  if (ol) return qn;
  ol = 1;
  var e = mt();
  function r(t) {
    return e(this, t).has(t);
  }
  return qn = r, qn;
}
var Cn, ul;
function Eg() {
  if (ul) return Cn;
  ul = 1;
  var e = mt();
  function r(t, n) {
    var a = e(this, t), i = a.size;
    return a.set(t, n), this.size += a.size == i ? 0 : 1, this;
  }
  return Cn = r, Cn;
}
var En, ll;
function Ho() {
  if (ll) return En;
  ll = 1;
  var e = _g(), r = Ig(), t = qg(), n = Cg(), a = Eg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, En = i, En;
}
var Sn, cl;
function Sg() {
  if (cl) return Sn;
  cl = 1;
  var e = gt(), r = Vo(), t = Ho(), n = 200;
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
  return Sn = a, Sn;
}
var Tn, dl;
function bt() {
  if (dl) return Tn;
  dl = 1;
  var e = gt(), r = ag(), t = ig(), n = sg(), a = og(), i = Sg();
  function s(o) {
    var u = this.__data__ = new e(o);
    this.size = u.size;
  }
  return s.prototype.clear = r, s.prototype.delete = t, s.prototype.get = n, s.prototype.has = a, s.prototype.set = i, Tn = s, Tn;
}
var xn, fl;
function Wo() {
  if (fl) return xn;
  fl = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a && t(r[n], n, r) !== !1; )
      ;
    return r;
  }
  return xn = e, xn;
}
var Rn, hl;
function Bh() {
  if (hl) return Rn;
  hl = 1;
  var e = _r(), r = function() {
    try {
      var t = e(Object, "defineProperty");
      return t({}, "", {}), t;
    } catch {
    }
  }();
  return Rn = r, Rn;
}
var kn, pl;
function _t() {
  if (pl) return kn;
  pl = 1;
  var e = Bh();
  function r(t, n, a) {
    n == "__proto__" && e ? e(t, n, {
      configurable: !0,
      enumerable: !0,
      value: a,
      writable: !0
    }) : t[n] = a;
  }
  return kn = r, kn;
}
var An, vl;
function wt() {
  if (vl) return An;
  vl = 1;
  var e = _t(), r = Rr(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o) {
    var u = i[s];
    (!(n.call(i, s) && r(u, o)) || o === void 0 && !(s in i)) && e(i, s, o);
  }
  return An = a, An;
}
var Pn, gl;
function Qr() {
  if (gl) return Pn;
  gl = 1;
  var e = wt(), r = _t();
  function t(n, a, i, s) {
    var o = !i;
    i || (i = {});
    for (var u = -1, c = a.length; ++u < c; ) {
      var l = a[u], d = s ? s(i[l], n[l], l, i, n) : void 0;
      d === void 0 && (d = n[l]), o ? r(i, l, d) : e(i, l, d);
    }
    return i;
  }
  return Pn = t, Pn;
}
var On, yl;
function Tg() {
  if (yl) return On;
  yl = 1;
  function e(r, t) {
    for (var n = -1, a = Array(r); ++n < r; )
      a[n] = t(n);
    return a;
  }
  return On = e, On;
}
var Nn, ml;
function Ye() {
  if (ml) return Nn;
  ml = 1;
  function e(r) {
    return r != null && typeof r == "object";
  }
  return Nn = e, Nn;
}
var Dn, bl;
function xg() {
  if (bl) return Dn;
  bl = 1;
  var e = br(), r = Ye(), t = "[object Arguments]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return Dn = n, Dn;
}
var Mn, _l;
function Yr() {
  if (_l) return Mn;
  _l = 1;
  var e = xg(), r = Ye(), t = Object.prototype, n = t.hasOwnProperty, a = t.propertyIsEnumerable, i = e(/* @__PURE__ */ function() {
    return arguments;
  }()) ? e : function(s) {
    return r(s) && n.call(s, "callee") && !a.call(s, "callee");
  };
  return Mn = i, Mn;
}
var $n, wl;
function qe() {
  if (wl) return $n;
  wl = 1;
  var e = Array.isArray;
  return $n = e, $n;
}
var jr = { exports: {} }, Ln, Il;
function Rg() {
  if (Il) return Ln;
  Il = 1;
  function e() {
    return !1;
  }
  return Ln = e, Ln;
}
jr.exports;
var ql;
function Ar() {
  return ql || (ql = 1, function(e, r) {
    var t = Ue(), n = Rg(), a = r && !r.nodeType && r, i = a && !0 && e && !e.nodeType && e, s = i && i.exports === a, o = s ? t.Buffer : void 0, u = o ? o.isBuffer : void 0, c = u || n;
    e.exports = c;
  }(jr, jr.exports)), jr.exports;
}
var Fn, Cl;
function It() {
  if (Cl) return Fn;
  Cl = 1;
  var e = 9007199254740991, r = /^(?:0|[1-9]\d*)$/;
  function t(n, a) {
    var i = typeof n;
    return a = a ?? e, !!a && (i == "number" || i != "symbol" && r.test(n)) && n > -1 && n % 1 == 0 && n < a;
  }
  return Fn = t, Fn;
}
var jn, El;
function Qo() {
  if (El) return jn;
  El = 1;
  var e = 9007199254740991;
  function r(t) {
    return typeof t == "number" && t > -1 && t % 1 == 0 && t <= e;
  }
  return jn = r, jn;
}
var Gn, Sl;
function kg() {
  if (Sl) return Gn;
  Sl = 1;
  var e = br(), r = Qo(), t = Ye(), n = "[object Arguments]", a = "[object Array]", i = "[object Boolean]", s = "[object Date]", o = "[object Error]", u = "[object Function]", c = "[object Map]", l = "[object Number]", d = "[object Object]", p = "[object RegExp]", v = "[object Set]", g = "[object String]", h = "[object WeakMap]", y = "[object ArrayBuffer]", C = "[object DataView]", R = "[object Float32Array]", w = "[object Float64Array]", A = "[object Int8Array]", M = "[object Int16Array]", b = "[object Int32Array]", k = "[object Uint8Array]", O = "[object Uint8ClampedArray]", V = "[object Uint16Array]", G = "[object Uint32Array]", S = {};
  S[R] = S[w] = S[A] = S[M] = S[b] = S[k] = S[O] = S[V] = S[G] = !0, S[n] = S[a] = S[y] = S[i] = S[C] = S[s] = S[o] = S[u] = S[c] = S[l] = S[d] = S[p] = S[v] = S[g] = S[h] = !1;
  function j(K) {
    return t(K) && r(K.length) && !!S[e(K)];
  }
  return Gn = j, Gn;
}
var zn, Tl;
function qt() {
  if (Tl) return zn;
  Tl = 1;
  function e(r) {
    return function(t) {
      return r(t);
    };
  }
  return zn = e, zn;
}
var Gr = { exports: {} };
Gr.exports;
var xl;
function Yo() {
  return xl || (xl = 1, function(e, r) {
    var t = Gh(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i && t.process, o = function() {
      try {
        var u = a && a.require && a.require("util").types;
        return u || s && s.binding && s.binding("util");
      } catch {
      }
    }();
    e.exports = o;
  }(Gr, Gr.exports)), Gr.exports;
}
var Bn, Rl;
function Jr() {
  if (Rl) return Bn;
  Rl = 1;
  var e = kg(), r = qt(), t = Yo(), n = t && t.isTypedArray, a = n ? r(n) : e;
  return Bn = a, Bn;
}
var Un, kl;
function Uh() {
  if (kl) return Un;
  kl = 1;
  var e = Tg(), r = Yr(), t = qe(), n = Ar(), a = It(), i = Jr(), s = Object.prototype, o = s.hasOwnProperty;
  function u(c, l) {
    var d = t(c), p = !d && r(c), v = !d && !p && n(c), g = !d && !p && !v && i(c), h = d || p || v || g, y = h ? e(c.length, String) : [], C = y.length;
    for (var R in c)
      (l || o.call(c, R)) && !(h && // Safari 9 has enumerable `arguments.length` in strict mode.
      (R == "length" || // Node.js 0.10 has enumerable non-index properties on buffers.
      v && (R == "offset" || R == "parent") || // PhantomJS 2 has enumerable non-index properties on typed arrays.
      g && (R == "buffer" || R == "byteLength" || R == "byteOffset") || // Skip index properties.
      a(R, C))) && y.push(R);
    return y;
  }
  return Un = u, Un;
}
var Kn, Al;
function Ct() {
  if (Al) return Kn;
  Al = 1;
  var e = Object.prototype;
  function r(t) {
    var n = t && t.constructor, a = typeof n == "function" && n.prototype || e;
    return t === a;
  }
  return Kn = r, Kn;
}
var Vn, Pl;
function Kh() {
  if (Pl) return Vn;
  Pl = 1;
  function e(r, t) {
    return function(n) {
      return r(t(n));
    };
  }
  return Vn = e, Vn;
}
var Hn, Ol;
function Ag() {
  if (Ol) return Hn;
  Ol = 1;
  var e = Kh(), r = e(Object.keys, Object);
  return Hn = r, Hn;
}
var Wn, Nl;
function Jo() {
  if (Nl) return Wn;
  Nl = 1;
  var e = Ct(), r = Ag(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i) {
    if (!e(i))
      return r(i);
    var s = [];
    for (var o in Object(i))
      n.call(i, o) && o != "constructor" && s.push(o);
    return s;
  }
  return Wn = a, Wn;
}
var Qn, Dl;
function sr() {
  if (Dl) return Qn;
  Dl = 1;
  var e = Wr(), r = Qo();
  function t(n) {
    return n != null && r(n.length) && !e(n);
  }
  return Qn = t, Qn;
}
var Yn, Ml;
function pr() {
  if (Ml) return Yn;
  Ml = 1;
  var e = Uh(), r = Jo(), t = sr();
  function n(a) {
    return t(a) ? e(a) : r(a);
  }
  return Yn = n, Yn;
}
var Jn, $l;
function Pg() {
  if ($l) return Jn;
  $l = 1;
  var e = Qr(), r = pr();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return Jn = t, Jn;
}
var Xn, Ll;
function Og() {
  if (Ll) return Xn;
  Ll = 1;
  function e(r) {
    var t = [];
    if (r != null)
      for (var n in Object(r))
        t.push(n);
    return t;
  }
  return Xn = e, Xn;
}
var Zn, Fl;
function Ng() {
  if (Fl) return Zn;
  Fl = 1;
  var e = je(), r = Ct(), t = Og(), n = Object.prototype, a = n.hasOwnProperty;
  function i(s) {
    if (!e(s))
      return t(s);
    var o = r(s), u = [];
    for (var c in s)
      c == "constructor" && (o || !a.call(s, c)) || u.push(c);
    return u;
  }
  return Zn = i, Zn;
}
var ea, jl;
function wr() {
  if (jl) return ea;
  jl = 1;
  var e = Uh(), r = Ng(), t = sr();
  function n(a) {
    return t(a) ? e(a, !0) : r(a);
  }
  return ea = n, ea;
}
var ra, Gl;
function Dg() {
  if (Gl) return ra;
  Gl = 1;
  var e = Qr(), r = wr();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return ra = t, ra;
}
var zr = { exports: {} };
zr.exports;
var zl;
function Vh() {
  return zl || (zl = 1, function(e, r) {
    var t = Ue(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i ? t.Buffer : void 0, o = s ? s.allocUnsafe : void 0;
    function u(c, l) {
      if (l)
        return c.slice();
      var d = c.length, p = o ? o(d) : new c.constructor(d);
      return c.copy(p), p;
    }
    e.exports = u;
  }(zr, zr.exports)), zr.exports;
}
var ta, Bl;
function Hh() {
  if (Bl) return ta;
  Bl = 1;
  function e(r, t) {
    var n = -1, a = r.length;
    for (t || (t = Array(a)); ++n < a; )
      t[n] = r[n];
    return t;
  }
  return ta = e, ta;
}
var na, Ul;
function Wh() {
  if (Ul) return na;
  Ul = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = 0, s = []; ++n < a; ) {
      var o = r[n];
      t(o, n, r) && (s[i++] = o);
    }
    return s;
  }
  return na = e, na;
}
var aa, Kl;
function Qh() {
  if (Kl) return aa;
  Kl = 1;
  function e() {
    return [];
  }
  return aa = e, aa;
}
var ia, Vl;
function Xo() {
  if (Vl) return ia;
  Vl = 1;
  var e = Wh(), r = Qh(), t = Object.prototype, n = t.propertyIsEnumerable, a = Object.getOwnPropertySymbols, i = a ? function(s) {
    return s == null ? [] : (s = Object(s), e(a(s), function(o) {
      return n.call(s, o);
    }));
  } : r;
  return ia = i, ia;
}
var sa, Hl;
function Mg() {
  if (Hl) return sa;
  Hl = 1;
  var e = Qr(), r = Xo();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return sa = t, sa;
}
var oa, Wl;
function Zo() {
  if (Wl) return oa;
  Wl = 1;
  function e(r, t) {
    for (var n = -1, a = t.length, i = r.length; ++n < a; )
      r[i + n] = t[n];
    return r;
  }
  return oa = e, oa;
}
var ua, Ql;
function Et() {
  if (Ql) return ua;
  Ql = 1;
  var e = Kh(), r = e(Object.getPrototypeOf, Object);
  return ua = r, ua;
}
var la, Yl;
function Yh() {
  if (Yl) return la;
  Yl = 1;
  var e = Zo(), r = Et(), t = Xo(), n = Qh(), a = Object.getOwnPropertySymbols, i = a ? function(s) {
    for (var o = []; s; )
      e(o, t(s)), s = r(s);
    return o;
  } : n;
  return la = i, la;
}
var ca, Jl;
function $g() {
  if (Jl) return ca;
  Jl = 1;
  var e = Qr(), r = Yh();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return ca = t, ca;
}
var da, Xl;
function Jh() {
  if (Xl) return da;
  Xl = 1;
  var e = Zo(), r = qe();
  function t(n, a, i) {
    var s = a(n);
    return r(n) ? s : e(s, i(n));
  }
  return da = t, da;
}
var fa, Zl;
function Xh() {
  if (Zl) return fa;
  Zl = 1;
  var e = Jh(), r = Xo(), t = pr();
  function n(a) {
    return e(a, t, r);
  }
  return fa = n, fa;
}
var ha, ec;
function Lg() {
  if (ec) return ha;
  ec = 1;
  var e = Jh(), r = Yh(), t = wr();
  function n(a) {
    return e(a, t, r);
  }
  return ha = n, ha;
}
var pa, rc;
function Fg() {
  if (rc) return pa;
  rc = 1;
  var e = _r(), r = Ue(), t = e(r, "DataView");
  return pa = t, pa;
}
var va, tc;
function jg() {
  if (tc) return va;
  tc = 1;
  var e = _r(), r = Ue(), t = e(r, "Promise");
  return va = t, va;
}
var ga, nc;
function Zh() {
  if (nc) return ga;
  nc = 1;
  var e = _r(), r = Ue(), t = e(r, "Set");
  return ga = t, ga;
}
var ya, ac;
function Gg() {
  if (ac) return ya;
  ac = 1;
  var e = _r(), r = Ue(), t = e(r, "WeakMap");
  return ya = t, ya;
}
var ma, ic;
function Pr() {
  if (ic) return ma;
  ic = 1;
  var e = Fg(), r = Vo(), t = jg(), n = Zh(), a = Gg(), i = br(), s = zh(), o = "[object Map]", u = "[object Object]", c = "[object Promise]", l = "[object Set]", d = "[object WeakMap]", p = "[object DataView]", v = s(e), g = s(r), h = s(t), y = s(n), C = s(a), R = i;
  return (e && R(new e(new ArrayBuffer(1))) != p || r && R(new r()) != o || t && R(t.resolve()) != c || n && R(new n()) != l || a && R(new a()) != d) && (R = function(w) {
    var A = i(w), M = A == u ? w.constructor : void 0, b = M ? s(M) : "";
    if (b)
      switch (b) {
        case v:
          return p;
        case g:
          return o;
        case h:
          return c;
        case y:
          return l;
        case C:
          return d;
      }
    return A;
  }), ma = R, ma;
}
var ba, sc;
function zg() {
  if (sc) return ba;
  sc = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n) {
    var a = n.length, i = new n.constructor(a);
    return a && typeof n[0] == "string" && r.call(n, "index") && (i.index = n.index, i.input = n.input), i;
  }
  return ba = t, ba;
}
var _a, oc;
function ep() {
  if (oc) return _a;
  oc = 1;
  var e = Ue(), r = e.Uint8Array;
  return _a = r, _a;
}
var wa, uc;
function eu() {
  if (uc) return wa;
  uc = 1;
  var e = ep();
  function r(t) {
    var n = new t.constructor(t.byteLength);
    return new e(n).set(new e(t)), n;
  }
  return wa = r, wa;
}
var Ia, lc;
function Bg() {
  if (lc) return Ia;
  lc = 1;
  var e = eu();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.byteLength);
  }
  return Ia = r, Ia;
}
var qa, cc;
function Ug() {
  if (cc) return qa;
  cc = 1;
  var e = /\w*$/;
  function r(t) {
    var n = new t.constructor(t.source, e.exec(t));
    return n.lastIndex = t.lastIndex, n;
  }
  return qa = r, qa;
}
var Ca, dc;
function Kg() {
  if (dc) return Ca;
  dc = 1;
  var e = kr(), r = e ? e.prototype : void 0, t = r ? r.valueOf : void 0;
  function n(a) {
    return t ? Object(t.call(a)) : {};
  }
  return Ca = n, Ca;
}
var Ea, fc;
function rp() {
  if (fc) return Ea;
  fc = 1;
  var e = eu();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.length);
  }
  return Ea = r, Ea;
}
var Sa, hc;
function Vg() {
  if (hc) return Sa;
  hc = 1;
  var e = eu(), r = Bg(), t = Ug(), n = Kg(), a = rp(), i = "[object Boolean]", s = "[object Date]", o = "[object Map]", u = "[object Number]", c = "[object RegExp]", l = "[object Set]", d = "[object String]", p = "[object Symbol]", v = "[object ArrayBuffer]", g = "[object DataView]", h = "[object Float32Array]", y = "[object Float64Array]", C = "[object Int8Array]", R = "[object Int16Array]", w = "[object Int32Array]", A = "[object Uint8Array]", M = "[object Uint8ClampedArray]", b = "[object Uint16Array]", k = "[object Uint32Array]";
  function O(V, G, S) {
    var j = V.constructor;
    switch (G) {
      case v:
        return e(V);
      case i:
      case s:
        return new j(+V);
      case g:
        return r(V, S);
      case h:
      case y:
      case C:
      case R:
      case w:
      case A:
      case M:
      case b:
      case k:
        return a(V, S);
      case o:
        return new j();
      case u:
      case d:
        return new j(V);
      case c:
        return t(V);
      case l:
        return new j();
      case p:
        return n(V);
    }
  }
  return Sa = O, Sa;
}
var Ta, pc;
function tp() {
  if (pc) return Ta;
  pc = 1;
  var e = je(), r = Object.create, t = /* @__PURE__ */ function() {
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
  return Ta = t, Ta;
}
var xa, vc;
function np() {
  if (vc) return xa;
  vc = 1;
  var e = tp(), r = Et(), t = Ct();
  function n(a) {
    return typeof a.constructor == "function" && !t(a) ? e(r(a)) : {};
  }
  return xa = n, xa;
}
var Ra, gc;
function Hg() {
  if (gc) return Ra;
  gc = 1;
  var e = Pr(), r = Ye(), t = "[object Map]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return Ra = n, Ra;
}
var ka, yc;
function Wg() {
  if (yc) return ka;
  yc = 1;
  var e = Hg(), r = qt(), t = Yo(), n = t && t.isMap, a = n ? r(n) : e;
  return ka = a, ka;
}
var Aa, mc;
function Qg() {
  if (mc) return Aa;
  mc = 1;
  var e = Pr(), r = Ye(), t = "[object Set]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return Aa = n, Aa;
}
var Pa, bc;
function Yg() {
  if (bc) return Pa;
  bc = 1;
  var e = Qg(), r = qt(), t = Yo(), n = t && t.isSet, a = n ? r(n) : e;
  return Pa = a, Pa;
}
var Oa, _c;
function ap() {
  if (_c) return Oa;
  _c = 1;
  var e = bt(), r = Wo(), t = wt(), n = Pg(), a = Dg(), i = Vh(), s = Hh(), o = Mg(), u = $g(), c = Xh(), l = Lg(), d = Pr(), p = zg(), v = Vg(), g = np(), h = qe(), y = Ar(), C = Wg(), R = je(), w = Yg(), A = pr(), M = wr(), b = 1, k = 2, O = 4, V = "[object Arguments]", G = "[object Array]", S = "[object Boolean]", j = "[object Date]", K = "[object Error]", z = "[object Function]", ae = "[object GeneratorFunction]", re = "[object Map]", de = "[object Number]", B = "[object Object]", Q = "[object RegExp]", E = "[object Set]", T = "[object String]", L = "[object Symbol]", fe = "[object WeakMap]", pe = "[object ArrayBuffer]", Ee = "[object DataView]", vr = "[object Float32Array]", Nr = "[object Float64Array]", Je = "[object Int8Array]", Cr = "[object Int16Array]", ze = "[object Int32Array]", Er = "[object Uint8Array]", Ve = "[object Uint8ClampedArray]", Xe = "[object Uint16Array]", Ze = "[object Uint32Array]", ie = {};
  ie[V] = ie[G] = ie[pe] = ie[Ee] = ie[S] = ie[j] = ie[vr] = ie[Nr] = ie[Je] = ie[Cr] = ie[ze] = ie[re] = ie[de] = ie[B] = ie[Q] = ie[E] = ie[T] = ie[L] = ie[Er] = ie[Ve] = ie[Xe] = ie[Ze] = !0, ie[K] = ie[z] = ie[fe] = !1;
  function he(Z, Te, ee, ke, ue, le) {
    var we, ce = Te & b, He = Te & k, er = Te & O;
    if (ee && (we = ue ? ee(Z, ke, ue, le) : ee(Z)), we !== void 0)
      return we;
    if (!R(Z))
      return Z;
    var Sr = h(Z);
    if (Sr) {
      if (we = p(Z), !ce)
        return s(Z, we);
    } else {
      var Le = d(Z), ur = Le == z || Le == ae;
      if (y(Z))
        return i(Z, ce);
      if (Le == B || Le == V || ur && !ue) {
        if (we = He || ur ? {} : g(Z), !ce)
          return He ? u(Z, a(we, Z)) : o(Z, n(we, Z));
      } else {
        if (!ie[Le])
          return ue ? Z : {};
        we = v(Z, Le, ce);
      }
    }
    le || (le = new e());
    var gr = le.get(Z);
    if (gr)
      return gr;
    le.set(Z, we), w(Z) ? Z.forEach(function(Fe) {
      we.add(he(Fe, Te, ee, Fe, Z, le));
    }) : C(Z) && Z.forEach(function(Fe, Be) {
      we.set(Be, he(Fe, Te, ee, Be, Z, le));
    });
    var Dr = er ? He ? l : c : He ? M : A, Tr = Sr ? void 0 : Dr(Z);
    return r(Tr || Z, function(Fe, Be) {
      Tr && (Be = Fe, Fe = Z[Be]), t(we, Be, he(Fe, Te, ee, Be, Z, le));
    }), we;
  }
  return Oa = he, Oa;
}
var Na, wc;
function Jg() {
  if (wc) return Na;
  wc = 1;
  var e = ap(), r = 4;
  function t(n) {
    return e(n, r);
  }
  return Na = t, Na;
}
var Da, Ic;
function ru() {
  if (Ic) return Da;
  Ic = 1;
  function e(r) {
    return function() {
      return r;
    };
  }
  return Da = e, Da;
}
var Ma, qc;
function Xg() {
  if (qc) return Ma;
  qc = 1;
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
  return Ma = e, Ma;
}
var $a, Cc;
function tu() {
  if (Cc) return $a;
  Cc = 1;
  var e = Xg(), r = e();
  return $a = r, $a;
}
var La, Ec;
function nu() {
  if (Ec) return La;
  Ec = 1;
  var e = tu(), r = pr();
  function t(n, a) {
    return n && e(n, a, r);
  }
  return La = t, La;
}
var Fa, Sc;
function Zg() {
  if (Sc) return Fa;
  Sc = 1;
  var e = sr();
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
  return Fa = r, Fa;
}
var ja, Tc;
function St() {
  if (Tc) return ja;
  Tc = 1;
  var e = nu(), r = Zg(), t = r(e);
  return ja = t, ja;
}
var Ga, xc;
function Ir() {
  if (xc) return Ga;
  xc = 1;
  function e(r) {
    return r;
  }
  return Ga = e, Ga;
}
var za, Rc;
function ip() {
  if (Rc) return za;
  Rc = 1;
  var e = Ir();
  function r(t) {
    return typeof t == "function" ? t : e;
  }
  return za = r, za;
}
var Ba, kc;
function sp() {
  if (kc) return Ba;
  kc = 1;
  var e = Wo(), r = St(), t = ip(), n = qe();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s));
  }
  return Ba = a, Ba;
}
var Ua, Ac;
function op() {
  return Ac || (Ac = 1, Ua = sp()), Ua;
}
var Ka, Pc;
function ey() {
  if (Pc) return Ka;
  Pc = 1;
  var e = St();
  function r(t, n) {
    var a = [];
    return e(t, function(i, s, o) {
      n(i, s, o) && a.push(i);
    }), a;
  }
  return Ka = r, Ka;
}
var Va, Oc;
function ry() {
  if (Oc) return Va;
  Oc = 1;
  var e = "__lodash_hash_undefined__";
  function r(t) {
    return this.__data__.set(t, e), this;
  }
  return Va = r, Va;
}
var Ha, Nc;
function ty() {
  if (Nc) return Ha;
  Nc = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return Ha = e, Ha;
}
var Wa, Dc;
function up() {
  if (Dc) return Wa;
  Dc = 1;
  var e = Ho(), r = ry(), t = ty();
  function n(a) {
    var i = -1, s = a == null ? 0 : a.length;
    for (this.__data__ = new e(); ++i < s; )
      this.add(a[i]);
  }
  return n.prototype.add = n.prototype.push = r, n.prototype.has = t, Wa = n, Wa;
}
var Qa, Mc;
function ny() {
  if (Mc) return Qa;
  Mc = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a; )
      if (t(r[n], n, r))
        return !0;
    return !1;
  }
  return Qa = e, Qa;
}
var Ya, $c;
function lp() {
  if ($c) return Ya;
  $c = 1;
  function e(r, t) {
    return r.has(t);
  }
  return Ya = e, Ya;
}
var Ja, Lc;
function cp() {
  if (Lc) return Ja;
  Lc = 1;
  var e = up(), r = ny(), t = lp(), n = 1, a = 2;
  function i(s, o, u, c, l, d) {
    var p = u & n, v = s.length, g = o.length;
    if (v != g && !(p && g > v))
      return !1;
    var h = d.get(s), y = d.get(o);
    if (h && y)
      return h == o && y == s;
    var C = -1, R = !0, w = u & a ? new e() : void 0;
    for (d.set(s, o), d.set(o, s); ++C < v; ) {
      var A = s[C], M = o[C];
      if (c)
        var b = p ? c(M, A, C, o, s, d) : c(A, M, C, s, o, d);
      if (b !== void 0) {
        if (b)
          continue;
        R = !1;
        break;
      }
      if (w) {
        if (!r(o, function(k, O) {
          if (!t(w, O) && (A === k || l(A, k, u, c, d)))
            return w.push(O);
        })) {
          R = !1;
          break;
        }
      } else if (!(A === M || l(A, M, u, c, d))) {
        R = !1;
        break;
      }
    }
    return d.delete(s), d.delete(o), R;
  }
  return Ja = i, Ja;
}
var Xa, Fc;
function ay() {
  if (Fc) return Xa;
  Fc = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a, i) {
      n[++t] = [i, a];
    }), n;
  }
  return Xa = e, Xa;
}
var Za, jc;
function au() {
  if (jc) return Za;
  jc = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a) {
      n[++t] = a;
    }), n;
  }
  return Za = e, Za;
}
var ei, Gc;
function iy() {
  if (Gc) return ei;
  Gc = 1;
  var e = kr(), r = ep(), t = Rr(), n = cp(), a = ay(), i = au(), s = 1, o = 2, u = "[object Boolean]", c = "[object Date]", l = "[object Error]", d = "[object Map]", p = "[object Number]", v = "[object RegExp]", g = "[object Set]", h = "[object String]", y = "[object Symbol]", C = "[object ArrayBuffer]", R = "[object DataView]", w = e ? e.prototype : void 0, A = w ? w.valueOf : void 0;
  function M(b, k, O, V, G, S, j) {
    switch (O) {
      case R:
        if (b.byteLength != k.byteLength || b.byteOffset != k.byteOffset)
          return !1;
        b = b.buffer, k = k.buffer;
      case C:
        return !(b.byteLength != k.byteLength || !S(new r(b), new r(k)));
      case u:
      case c:
      case p:
        return t(+b, +k);
      case l:
        return b.name == k.name && b.message == k.message;
      case v:
      case h:
        return b == k + "";
      case d:
        var K = a;
      case g:
        var z = V & s;
        if (K || (K = i), b.size != k.size && !z)
          return !1;
        var ae = j.get(b);
        if (ae)
          return ae == k;
        V |= o, j.set(b, k);
        var re = n(K(b), K(k), V, G, S, j);
        return j.delete(b), re;
      case y:
        if (A)
          return A.call(b) == A.call(k);
    }
    return !1;
  }
  return ei = M, ei;
}
var ri, zc;
function sy() {
  if (zc) return ri;
  zc = 1;
  var e = Xh(), r = 1, t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o, u, c, l) {
    var d = o & r, p = e(i), v = p.length, g = e(s), h = g.length;
    if (v != h && !d)
      return !1;
    for (var y = v; y--; ) {
      var C = p[y];
      if (!(d ? C in s : n.call(s, C)))
        return !1;
    }
    var R = l.get(i), w = l.get(s);
    if (R && w)
      return R == s && w == i;
    var A = !0;
    l.set(i, s), l.set(s, i);
    for (var M = d; ++y < v; ) {
      C = p[y];
      var b = i[C], k = s[C];
      if (u)
        var O = d ? u(k, b, C, s, i, l) : u(b, k, C, i, s, l);
      if (!(O === void 0 ? b === k || c(b, k, o, u, l) : O)) {
        A = !1;
        break;
      }
      M || (M = C == "constructor");
    }
    if (A && !M) {
      var V = i.constructor, G = s.constructor;
      V != G && "constructor" in i && "constructor" in s && !(typeof V == "function" && V instanceof V && typeof G == "function" && G instanceof G) && (A = !1);
    }
    return l.delete(i), l.delete(s), A;
  }
  return ri = a, ri;
}
var ti, Bc;
function oy() {
  if (Bc) return ti;
  Bc = 1;
  var e = bt(), r = cp(), t = iy(), n = sy(), a = Pr(), i = qe(), s = Ar(), o = Jr(), u = 1, c = "[object Arguments]", l = "[object Array]", d = "[object Object]", p = Object.prototype, v = p.hasOwnProperty;
  function g(h, y, C, R, w, A) {
    var M = i(h), b = i(y), k = M ? l : a(h), O = b ? l : a(y);
    k = k == c ? d : k, O = O == c ? d : O;
    var V = k == d, G = O == d, S = k == O;
    if (S && s(h)) {
      if (!s(y))
        return !1;
      M = !0, V = !1;
    }
    if (S && !V)
      return A || (A = new e()), M || o(h) ? r(h, y, C, R, w, A) : t(h, y, k, C, R, w, A);
    if (!(C & u)) {
      var j = V && v.call(h, "__wrapped__"), K = G && v.call(y, "__wrapped__");
      if (j || K) {
        var z = j ? h.value() : h, ae = K ? y.value() : y;
        return A || (A = new e()), w(z, ae, C, R, A);
      }
    }
    return S ? (A || (A = new e()), n(h, y, C, R, w, A)) : !1;
  }
  return ti = g, ti;
}
var ni, Uc;
function dp() {
  if (Uc) return ni;
  Uc = 1;
  var e = oy(), r = Ye();
  function t(n, a, i, s, o) {
    return n === a ? !0 : n == null || a == null || !r(n) && !r(a) ? n !== n && a !== a : e(n, a, i, s, t, o);
  }
  return ni = t, ni;
}
var ai, Kc;
function uy() {
  if (Kc) return ai;
  Kc = 1;
  var e = bt(), r = dp(), t = 1, n = 2;
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
      var v = p[0], g = i[v], h = p[1];
      if (d && p[2]) {
        if (g === void 0 && !(v in i))
          return !1;
      } else {
        var y = new e();
        if (u)
          var C = u(g, h, v, i, s, y);
        if (!(C === void 0 ? r(h, g, t | n, u, y) : C))
          return !1;
      }
    }
    return !0;
  }
  return ai = a, ai;
}
var ii, Vc;
function fp() {
  if (Vc) return ii;
  Vc = 1;
  var e = je();
  function r(t) {
    return t === t && !e(t);
  }
  return ii = r, ii;
}
var si, Hc;
function ly() {
  if (Hc) return si;
  Hc = 1;
  var e = fp(), r = pr();
  function t(n) {
    for (var a = r(n), i = a.length; i--; ) {
      var s = a[i], o = n[s];
      a[i] = [s, o, e(o)];
    }
    return a;
  }
  return si = t, si;
}
var oi, Wc;
function hp() {
  if (Wc) return oi;
  Wc = 1;
  function e(r, t) {
    return function(n) {
      return n == null ? !1 : n[r] === t && (t !== void 0 || r in Object(n));
    };
  }
  return oi = e, oi;
}
var ui, Qc;
function cy() {
  if (Qc) return ui;
  Qc = 1;
  var e = uy(), r = ly(), t = hp();
  function n(a) {
    var i = r(a);
    return i.length == 1 && i[0][2] ? t(i[0][0], i[0][1]) : function(s) {
      return s === a || e(s, a, i);
    };
  }
  return ui = n, ui;
}
var li, Yc;
function Or() {
  if (Yc) return li;
  Yc = 1;
  var e = br(), r = Ye(), t = "[object Symbol]";
  function n(a) {
    return typeof a == "symbol" || r(a) && e(a) == t;
  }
  return li = n, li;
}
var ci, Jc;
function iu() {
  if (Jc) return ci;
  Jc = 1;
  var e = qe(), r = Or(), t = /\.|\[(?:[^[\]]*|(["'])(?:(?!\1)[^\\]|\\.)*?\1)\]/, n = /^\w*$/;
  function a(i, s) {
    if (e(i))
      return !1;
    var o = typeof i;
    return o == "number" || o == "symbol" || o == "boolean" || i == null || r(i) ? !0 : n.test(i) || !t.test(i) || s != null && i in Object(s);
  }
  return ci = a, ci;
}
var di, Xc;
function dy() {
  if (Xc) return di;
  Xc = 1;
  var e = Ho(), r = "Expected a function";
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
  return t.Cache = e, di = t, di;
}
var fi, Zc;
function fy() {
  if (Zc) return fi;
  Zc = 1;
  var e = dy(), r = 500;
  function t(n) {
    var a = e(n, function(s) {
      return i.size === r && i.clear(), s;
    }), i = a.cache;
    return a;
  }
  return fi = t, fi;
}
var hi, ed;
function hy() {
  if (ed) return hi;
  ed = 1;
  var e = fy(), r = /[^.[\]]+|\[(?:(-?\d+(?:\.\d+)?)|(["'])((?:(?!\2)[^\\]|\\.)*?)\2)\]|(?=(?:\.|\[\])(?:\.|\[\]|$))/g, t = /\\(\\)?/g, n = e(function(a) {
    var i = [];
    return a.charCodeAt(0) === 46 && i.push(""), a.replace(r, function(s, o, u, c) {
      i.push(u ? c.replace(t, "$1") : o || s);
    }), i;
  });
  return hi = n, hi;
}
var pi, rd;
function Tt() {
  if (rd) return pi;
  rd = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = Array(a); ++n < a; )
      i[n] = t(r[n], n, r);
    return i;
  }
  return pi = e, pi;
}
var vi, td;
function py() {
  if (td) return vi;
  td = 1;
  var e = kr(), r = Tt(), t = qe(), n = Or(), a = e ? e.prototype : void 0, i = a ? a.toString : void 0;
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
  return vi = s, vi;
}
var gi, nd;
function pp() {
  if (nd) return gi;
  nd = 1;
  var e = py();
  function r(t) {
    return t == null ? "" : e(t);
  }
  return gi = r, gi;
}
var yi, ad;
function xt() {
  if (ad) return yi;
  ad = 1;
  var e = qe(), r = iu(), t = hy(), n = pp();
  function a(i, s) {
    return e(i) ? i : r(i, s) ? [i] : t(n(i));
  }
  return yi = a, yi;
}
var mi, id;
function Xr() {
  if (id) return mi;
  id = 1;
  var e = Or();
  function r(t) {
    if (typeof t == "string" || e(t))
      return t;
    var n = t + "";
    return n == "0" && 1 / t == -1 / 0 ? "-0" : n;
  }
  return mi = r, mi;
}
var bi, sd;
function Rt() {
  if (sd) return bi;
  sd = 1;
  var e = xt(), r = Xr();
  function t(n, a) {
    a = e(a, n);
    for (var i = 0, s = a.length; n != null && i < s; )
      n = n[r(a[i++])];
    return i && i == s ? n : void 0;
  }
  return bi = t, bi;
}
var _i, od;
function vy() {
  if (od) return _i;
  od = 1;
  var e = Rt();
  function r(t, n, a) {
    var i = t == null ? void 0 : e(t, n);
    return i === void 0 ? a : i;
  }
  return _i = r, _i;
}
var wi, ud;
function gy() {
  if (ud) return wi;
  ud = 1;
  function e(r, t) {
    return r != null && t in Object(r);
  }
  return wi = e, wi;
}
var Ii, ld;
function vp() {
  if (ld) return Ii;
  ld = 1;
  var e = xt(), r = Yr(), t = qe(), n = It(), a = Qo(), i = Xr();
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
  return Ii = s, Ii;
}
var qi, cd;
function gp() {
  if (cd) return qi;
  cd = 1;
  var e = gy(), r = vp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return qi = t, qi;
}
var Ci, dd;
function yy() {
  if (dd) return Ci;
  dd = 1;
  var e = dp(), r = vy(), t = gp(), n = iu(), a = fp(), i = hp(), s = Xr(), o = 1, u = 2;
  function c(l, d) {
    return n(l) && a(d) ? i(s(l), d) : function(p) {
      var v = r(p, l);
      return v === void 0 && v === d ? t(p, l) : e(d, v, o | u);
    };
  }
  return Ci = c, Ci;
}
var Ei, fd;
function yp() {
  if (fd) return Ei;
  fd = 1;
  function e(r) {
    return function(t) {
      return t == null ? void 0 : t[r];
    };
  }
  return Ei = e, Ei;
}
var Si, hd;
function my() {
  if (hd) return Si;
  hd = 1;
  var e = Rt();
  function r(t) {
    return function(n) {
      return e(n, t);
    };
  }
  return Si = r, Si;
}
var Ti, pd;
function by() {
  if (pd) return Ti;
  pd = 1;
  var e = yp(), r = my(), t = iu(), n = Xr();
  function a(i) {
    return t(i) ? e(n(i)) : r(i);
  }
  return Ti = a, Ti;
}
var xi, vd;
function or() {
  if (vd) return xi;
  vd = 1;
  var e = cy(), r = yy(), t = Ir(), n = qe(), a = by();
  function i(s) {
    return typeof s == "function" ? s : s == null ? t : typeof s == "object" ? n(s) ? r(s[0], s[1]) : e(s) : a(s);
  }
  return xi = i, xi;
}
var Ri, gd;
function mp() {
  if (gd) return Ri;
  gd = 1;
  var e = Wh(), r = ey(), t = or(), n = qe();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s, 3));
  }
  return Ri = a, Ri;
}
var ki, yd;
function _y() {
  if (yd) return ki;
  yd = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n, a) {
    return n != null && r.call(n, a);
  }
  return ki = t, ki;
}
var Ai, md;
function bp() {
  if (md) return Ai;
  md = 1;
  var e = _y(), r = vp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return Ai = t, Ai;
}
var Pi, bd;
function wy() {
  if (bd) return Pi;
  bd = 1;
  var e = Jo(), r = Pr(), t = Yr(), n = qe(), a = sr(), i = Ar(), s = Ct(), o = Jr(), u = "[object Map]", c = "[object Set]", l = Object.prototype, d = l.hasOwnProperty;
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
    for (var h in v)
      if (d.call(v, h))
        return !1;
    return !0;
  }
  return Pi = p, Pi;
}
var Oi, _d;
function _p() {
  if (_d) return Oi;
  _d = 1;
  function e(r) {
    return r === void 0;
  }
  return Oi = e, Oi;
}
var Ni, wd;
function wp() {
  if (wd) return Ni;
  wd = 1;
  var e = St(), r = sr();
  function t(n, a) {
    var i = -1, s = r(n) ? Array(n.length) : [];
    return e(n, function(o, u, c) {
      s[++i] = a(o, u, c);
    }), s;
  }
  return Ni = t, Ni;
}
var Di, Id;
function Ip() {
  if (Id) return Di;
  Id = 1;
  var e = Tt(), r = or(), t = wp(), n = qe();
  function a(i, s) {
    var o = n(i) ? e : t;
    return o(i, r(s, 3));
  }
  return Di = a, Di;
}
var Mi, qd;
function Iy() {
  if (qd) return Mi;
  qd = 1;
  function e(r, t, n, a) {
    var i = -1, s = r == null ? 0 : r.length;
    for (a && s && (n = r[++i]); ++i < s; )
      n = t(n, r[i], i, r);
    return n;
  }
  return Mi = e, Mi;
}
var $i, Cd;
function qy() {
  if (Cd) return $i;
  Cd = 1;
  function e(r, t, n, a, i) {
    return i(r, function(s, o, u) {
      n = a ? (a = !1, s) : t(n, s, o, u);
    }), n;
  }
  return $i = e, $i;
}
var Li, Ed;
function qp() {
  if (Ed) return Li;
  Ed = 1;
  var e = Iy(), r = St(), t = or(), n = qy(), a = qe();
  function i(s, o, u) {
    var c = a(s) ? e : n, l = arguments.length < 3;
    return c(s, t(o, 4), u, l, r);
  }
  return Li = i, Li;
}
var Fi, Sd;
function Cy() {
  if (Sd) return Fi;
  Sd = 1;
  var e = br(), r = qe(), t = Ye(), n = "[object String]";
  function a(i) {
    return typeof i == "string" || !r(i) && t(i) && e(i) == n;
  }
  return Fi = a, Fi;
}
var ji, Td;
function Ey() {
  if (Td) return ji;
  Td = 1;
  var e = yp(), r = e("length");
  return ji = r, ji;
}
var Gi, xd;
function Sy() {
  if (xd) return Gi;
  xd = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "\\u200d", o = RegExp("[" + s + e + a + i + "]");
  function u(c) {
    return o.test(c);
  }
  return Gi = u, Gi;
}
var zi, Rd;
function Ty() {
  if (Rd) return zi;
  Rd = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "[" + e + "]", o = "[" + a + "]", u = "\\ud83c[\\udffb-\\udfff]", c = "(?:" + o + "|" + u + ")", l = "[^" + e + "]", d = "(?:\\ud83c[\\udde6-\\uddff]){2}", p = "[\\ud800-\\udbff][\\udc00-\\udfff]", v = "\\u200d", g = c + "?", h = "[" + i + "]?", y = "(?:" + v + "(?:" + [l, d, p].join("|") + ")" + h + g + ")*", C = h + g + y, R = "(?:" + [l + o + "?", o, d, p, s].join("|") + ")", w = RegExp(u + "(?=" + u + ")|" + R + C, "g");
  function A(M) {
    for (var b = w.lastIndex = 0; w.test(M); )
      ++b;
    return b;
  }
  return zi = A, zi;
}
var Bi, kd;
function xy() {
  if (kd) return Bi;
  kd = 1;
  var e = Ey(), r = Sy(), t = Ty();
  function n(a) {
    return r(a) ? t(a) : e(a);
  }
  return Bi = n, Bi;
}
var Ui, Ad;
function Ry() {
  if (Ad) return Ui;
  Ad = 1;
  var e = Jo(), r = Pr(), t = sr(), n = Cy(), a = xy(), i = "[object Map]", s = "[object Set]";
  function o(u) {
    if (u == null)
      return 0;
    if (t(u))
      return n(u) ? a(u) : u.length;
    var c = r(u);
    return c == i || c == s ? u.size : e(u).length;
  }
  return Ui = o, Ui;
}
var Ki, Pd;
function ky() {
  if (Pd) return Ki;
  Pd = 1;
  var e = Wo(), r = tp(), t = nu(), n = or(), a = Et(), i = qe(), s = Ar(), o = Wr(), u = je(), c = Jr();
  function l(d, p, v) {
    var g = i(d), h = g || s(d) || c(d);
    if (p = n(p, 4), v == null) {
      var y = d && d.constructor;
      h ? v = g ? new y() : [] : u(d) ? v = o(y) ? r(a(d)) : {} : v = {};
    }
    return (h ? e : t)(d, function(C, R, w) {
      return p(v, C, R, w);
    }), v;
  }
  return Ki = l, Ki;
}
var Vi, Od;
function Ay() {
  if (Od) return Vi;
  Od = 1;
  var e = kr(), r = Yr(), t = qe(), n = e ? e.isConcatSpreadable : void 0;
  function a(i) {
    return t(i) || r(i) || !!(n && i && i[n]);
  }
  return Vi = a, Vi;
}
var Hi, Nd;
function su() {
  if (Nd) return Hi;
  Nd = 1;
  var e = Zo(), r = Ay();
  function t(n, a, i, s, o) {
    var u = -1, c = n.length;
    for (i || (i = r), o || (o = []); ++u < c; ) {
      var l = n[u];
      a > 0 && i(l) ? a > 1 ? t(l, a - 1, i, s, o) : e(o, l) : s || (o[o.length] = l);
    }
    return o;
  }
  return Hi = t, Hi;
}
var Wi, Dd;
function Py() {
  if (Dd) return Wi;
  Dd = 1;
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
  return Wi = e, Wi;
}
var Qi, Md;
function Cp() {
  if (Md) return Qi;
  Md = 1;
  var e = Py(), r = Math.max;
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
  return Qi = t, Qi;
}
var Yi, $d;
function Oy() {
  if ($d) return Yi;
  $d = 1;
  var e = ru(), r = Bh(), t = Ir(), n = r ? function(a, i) {
    return r(a, "toString", {
      configurable: !0,
      enumerable: !1,
      value: e(i),
      writable: !0
    });
  } : t;
  return Yi = n, Yi;
}
var Ji, Ld;
function Ny() {
  if (Ld) return Ji;
  Ld = 1;
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
  return Ji = n, Ji;
}
var Xi, Fd;
function Ep() {
  if (Fd) return Xi;
  Fd = 1;
  var e = Oy(), r = Ny(), t = r(e);
  return Xi = t, Xi;
}
var Zi, jd;
function kt() {
  if (jd) return Zi;
  jd = 1;
  var e = Ir(), r = Cp(), t = Ep();
  function n(a, i) {
    return t(r(a, i, e), a + "");
  }
  return Zi = n, Zi;
}
var es, Gd;
function Sp() {
  if (Gd) return es;
  Gd = 1;
  function e(r, t, n, a) {
    for (var i = r.length, s = n + (a ? 1 : -1); a ? s-- : ++s < i; )
      if (t(r[s], s, r))
        return s;
    return -1;
  }
  return es = e, es;
}
var rs, zd;
function Dy() {
  if (zd) return rs;
  zd = 1;
  function e(r) {
    return r !== r;
  }
  return rs = e, rs;
}
var ts, Bd;
function My() {
  if (Bd) return ts;
  Bd = 1;
  function e(r, t, n) {
    for (var a = n - 1, i = r.length; ++a < i; )
      if (r[a] === t)
        return a;
    return -1;
  }
  return ts = e, ts;
}
var ns, Ud;
function $y() {
  if (Ud) return ns;
  Ud = 1;
  var e = Sp(), r = Dy(), t = My();
  function n(a, i, s) {
    return i === i ? t(a, i, s) : e(a, r, s);
  }
  return ns = n, ns;
}
var as, Kd;
function Ly() {
  if (Kd) return as;
  Kd = 1;
  var e = $y();
  function r(t, n) {
    var a = t == null ? 0 : t.length;
    return !!a && e(t, n, 0) > -1;
  }
  return as = r, as;
}
var is, Vd;
function Fy() {
  if (Vd) return is;
  Vd = 1;
  function e(r, t, n) {
    for (var a = -1, i = r == null ? 0 : r.length; ++a < i; )
      if (n(t, r[a]))
        return !0;
    return !1;
  }
  return is = e, is;
}
var ss, Hd;
function jy() {
  if (Hd) return ss;
  Hd = 1;
  function e() {
  }
  return ss = e, ss;
}
var os, Wd;
function Gy() {
  if (Wd) return os;
  Wd = 1;
  var e = Zh(), r = jy(), t = au(), n = 1 / 0, a = e && 1 / t(new e([, -0]))[1] == n ? function(i) {
    return new e(i);
  } : r;
  return os = a, os;
}
var us, Qd;
function zy() {
  if (Qd) return us;
  Qd = 1;
  var e = up(), r = Ly(), t = Fy(), n = lp(), a = Gy(), i = au(), s = 200;
  function o(u, c, l) {
    var d = -1, p = r, v = u.length, g = !0, h = [], y = h;
    if (l)
      g = !1, p = t;
    else if (v >= s) {
      var C = c ? null : a(u);
      if (C)
        return i(C);
      g = !1, p = n, y = new e();
    } else
      y = c ? [] : h;
    e:
      for (; ++d < v; ) {
        var R = u[d], w = c ? c(R) : R;
        if (R = l || R !== 0 ? R : 0, g && w === w) {
          for (var A = y.length; A--; )
            if (y[A] === w)
              continue e;
          c && y.push(w), h.push(R);
        } else p(y, w, l) || (y !== h && y.push(w), h.push(R));
      }
    return h;
  }
  return us = o, us;
}
var ls, Yd;
function Tp() {
  if (Yd) return ls;
  Yd = 1;
  var e = sr(), r = Ye();
  function t(n) {
    return r(n) && e(n);
  }
  return ls = t, ls;
}
var cs, Jd;
function By() {
  if (Jd) return cs;
  Jd = 1;
  var e = su(), r = kt(), t = zy(), n = Tp(), a = r(function(i) {
    return t(e(i, 1, n, !0));
  });
  return cs = a, cs;
}
var ds, Xd;
function Uy() {
  if (Xd) return ds;
  Xd = 1;
  var e = Tt();
  function r(t, n) {
    return e(n, function(a) {
      return t[a];
    });
  }
  return ds = r, ds;
}
var fs, Zd;
function xp() {
  if (Zd) return fs;
  Zd = 1;
  var e = Uy(), r = pr();
  function t(n) {
    return n == null ? [] : e(n, r(n));
  }
  return fs = t, fs;
}
var hs, ef;
function Ge() {
  if (ef) return hs;
  ef = 1;
  var e;
  if (typeof Ko == "function")
    try {
      e = {
        clone: Jg(),
        constant: ru(),
        each: op(),
        filter: mp(),
        has: bp(),
        isArray: qe(),
        isEmpty: wy(),
        isFunction: Wr(),
        isUndefined: _p(),
        keys: pr(),
        map: Ip(),
        reduce: qp(),
        size: Ry(),
        transform: ky(),
        union: By(),
        values: xp()
      };
    } catch {
    }
  return e || (e = window._), hs = e, hs;
}
var ps, rf;
function ou() {
  if (rf) return ps;
  rf = 1;
  var e = Ge();
  ps = a;
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
    e.each(this._nodes, function(h, y) {
      l(y) && d.setNode(y, h);
    }), e.each(this._edgeObjs, function(h) {
      d.hasNode(h.v) && d.hasNode(h.w) && d.setEdge(h, p.edge(h));
    });
    var v = {};
    function g(h) {
      var y = p.parent(h);
      return y === void 0 || d.hasNode(y) ? (v[h] = y, y) : y in v ? v[y] : g(y);
    }
    return this._isCompound && e.each(d.nodes(), function(h) {
      d.setParent(h, g(h));
    }), d;
  }, a.prototype.setDefaultEdgeLabel = function(l) {
    return e.isFunction(l) || (l = e.constant(l)), this._defaultEdgeLabelFn = l, this;
  }, a.prototype.edgeCount = function() {
    return this._edgeCount;
  }, a.prototype.edges = function() {
    return e.values(this._edgeObjs);
  }, a.prototype.setPath = function(l, d) {
    var p = this, v = arguments;
    return e.reduce(l, function(g, h) {
      return v.length > 1 ? p.setEdge(g, h, d) : p.setEdge(g, h), h;
    }), this;
  }, a.prototype.setEdge = function() {
    var l, d, p, v, g = !1, h = arguments[0];
    typeof h == "object" && h !== null && "v" in h ? (l = h.v, d = h.w, p = h.name, arguments.length === 2 && (v = arguments[1], g = !0)) : (l = h, d = arguments[1], p = arguments[3], arguments.length > 2 && (v = arguments[2], g = !0)), l = "" + l, d = "" + d, e.isUndefined(p) || (p = "" + p);
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
    var g = "" + d, h = "" + p;
    if (!l && g > h) {
      var y = g;
      g = h, h = y;
    }
    return g + n + h + n + (e.isUndefined(v) ? r : v);
  }
  function u(l, d, p, v) {
    var g = "" + d, h = "" + p;
    if (!l && g > h) {
      var y = g;
      g = h, h = y;
    }
    var C = { v: g, w: h };
    return v && (C.name = v), C;
  }
  function c(l, d) {
    return o(l, d.v, d.w, d.name);
  }
  return ps;
}
var vs, tf;
function Ky() {
  return tf || (tf = 1, vs = "2.1.8"), vs;
}
var gs, nf;
function Vy() {
  return nf || (nf = 1, gs = {
    Graph: ou(),
    version: Ky()
  }), gs;
}
var ys, af;
function Hy() {
  if (af) return ys;
  af = 1;
  var e = Ge(), r = ou();
  ys = {
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
  return ys;
}
var ms, sf;
function Wy() {
  if (sf) return ms;
  sf = 1;
  var e = Ge();
  ms = r;
  function r(t) {
    var n = {}, a = [], i;
    function s(o) {
      e.has(n, o) || (n[o] = !0, i.push(o), e.each(t.successors(o), s), e.each(t.predecessors(o), s));
    }
    return e.each(t.nodes(), function(o) {
      i = [], s(o), i.length && a.push(i);
    }), a;
  }
  return ms;
}
var bs, of;
function Rp() {
  if (of) return bs;
  of = 1;
  var e = Ge();
  bs = r;
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
  }, bs;
}
var _s, uf;
function kp() {
  if (uf) return _s;
  uf = 1;
  var e = Ge(), r = Rp();
  _s = n;
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
      var h = g.v !== d ? g.v : g.w, y = c[h], C = o(g), R = p.distance + C;
      if (C < 0)
        throw new Error("dijkstra does not allow negative edge weights. Bad edge: " + g + " Weight: " + C);
      R < y.distance && (y.distance = R, y.predecessor = d, l.decrease(h, R));
    };
    for (i.nodes().forEach(function(g) {
      var h = g === s ? 0 : Number.POSITIVE_INFINITY;
      c[g] = { distance: h }, l.add(g, h);
    }); l.size() > 0 && (d = l.removeMin(), p = c[d], p.distance !== Number.POSITIVE_INFINITY); )
      u(d).forEach(v);
    return c;
  }
  return _s;
}
var ws, lf;
function Qy() {
  if (lf) return ws;
  lf = 1;
  var e = kp(), r = Ge();
  ws = t;
  function t(n, a, i) {
    return r.transform(n.nodes(), function(s, o) {
      s[o] = e(n, o, a, i);
    }, {});
  }
  return ws;
}
var Is, cf;
function Ap() {
  if (cf) return Is;
  cf = 1;
  var e = Ge();
  Is = r;
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
  return Is;
}
var qs, df;
function Yy() {
  if (df) return qs;
  df = 1;
  var e = Ge(), r = Ap();
  qs = t;
  function t(n) {
    return e.filter(r(n), function(a) {
      return a.length > 1 || a.length === 1 && n.hasEdge(a[0], a[0]);
    });
  }
  return qs;
}
var Cs, ff;
function Jy() {
  if (ff) return Cs;
  ff = 1;
  var e = Ge();
  Cs = t;
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
          var g = p[c], h = l[v], y = p[v], C = g.distance + h.distance;
          C < y.distance && (y.distance = C, y.predecessor = h.predecessor);
        });
      });
    }), o;
  }
  return Cs;
}
var Es, hf;
function Pp() {
  if (hf) return Es;
  hf = 1;
  var e = Ge();
  Es = r, r.CycleException = t;
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
  return t.prototype = new Error(), Es;
}
var Ss, pf;
function Xy() {
  if (pf) return Ss;
  pf = 1;
  var e = Pp();
  Ss = r;
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
  return Ss;
}
var Ts, vf;
function Op() {
  if (vf) return Ts;
  vf = 1;
  var e = Ge();
  Ts = r;
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
  return Ts;
}
var xs, gf;
function Zy() {
  if (gf) return xs;
  gf = 1;
  var e = Op();
  xs = r;
  function r(t, n) {
    return e(t, n, "post");
  }
  return xs;
}
var Rs, yf;
function em() {
  if (yf) return Rs;
  yf = 1;
  var e = Op();
  Rs = r;
  function r(t, n) {
    return e(t, n, "pre");
  }
  return Rs;
}
var ks, mf;
function rm() {
  if (mf) return ks;
  mf = 1;
  var e = Ge(), r = ou(), t = Rp();
  ks = n;
  function n(a, i) {
    var s = new r(), o = {}, u = new t(), c;
    function l(p) {
      var v = p.v === c ? p.w : p.v, g = u.priority(v);
      if (g !== void 0) {
        var h = i(p);
        h < g && (o[v] = c, u.decrease(v, h));
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
  return ks;
}
var As, bf;
function tm() {
  return bf || (bf = 1, As = {
    components: Wy(),
    dijkstra: kp(),
    dijkstraAll: Qy(),
    findCycles: Yy(),
    floydWarshall: Jy(),
    isAcyclic: Xy(),
    postorder: Zy(),
    preorder: em(),
    prim: rm(),
    tarjan: Ap(),
    topsort: Pp()
  }), As;
}
var Ps, _f;
function nm() {
  if (_f) return Ps;
  _f = 1;
  var e = Vy();
  return Ps = {
    Graph: e.Graph,
    json: Hy(),
    alg: tm(),
    version: e.version
  }, Ps;
}
var lt;
if (typeof Ko == "function")
  try {
    lt = nm();
  } catch {
  }
lt || (lt = window.graphlib);
var Ke = lt, Os, wf;
function am() {
  if (wf) return Os;
  wf = 1;
  var e = ap(), r = 1, t = 4;
  function n(a) {
    return e(a, r | t);
  }
  return Os = n, Os;
}
var Ns, If;
function At() {
  if (If) return Ns;
  If = 1;
  var e = Rr(), r = sr(), t = It(), n = je();
  function a(i, s, o) {
    if (!n(o))
      return !1;
    var u = typeof s;
    return (u == "number" ? r(o) && t(s, o.length) : u == "string" && s in o) ? e(o[s], i) : !1;
  }
  return Ns = a, Ns;
}
var Ds, qf;
function im() {
  if (qf) return Ds;
  qf = 1;
  var e = kt(), r = Rr(), t = At(), n = wr(), a = Object.prototype, i = a.hasOwnProperty, s = e(function(o, u) {
    o = Object(o);
    var c = -1, l = u.length, d = l > 2 ? u[2] : void 0;
    for (d && t(u[0], u[1], d) && (l = 1); ++c < l; )
      for (var p = u[c], v = n(p), g = -1, h = v.length; ++g < h; ) {
        var y = v[g], C = o[y];
        (C === void 0 || r(C, a[y]) && !i.call(o, y)) && (o[y] = p[y]);
      }
    return o;
  });
  return Ds = s, Ds;
}
var Ms, Cf;
function sm() {
  if (Cf) return Ms;
  Cf = 1;
  var e = or(), r = sr(), t = pr();
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
  return Ms = n, Ms;
}
var $s, Ef;
function om() {
  if (Ef) return $s;
  Ef = 1;
  var e = /\s/;
  function r(t) {
    for (var n = t.length; n-- && e.test(t.charAt(n)); )
      ;
    return n;
  }
  return $s = r, $s;
}
var Ls, Sf;
function um() {
  if (Sf) return Ls;
  Sf = 1;
  var e = om(), r = /^\s+/;
  function t(n) {
    return n && n.slice(0, e(n) + 1).replace(r, "");
  }
  return Ls = t, Ls;
}
var Fs, Tf;
function lm() {
  if (Tf) return Fs;
  Tf = 1;
  var e = um(), r = je(), t = Or(), n = NaN, a = /^[-+]0x[0-9a-f]+$/i, i = /^0b[01]+$/i, s = /^0o[0-7]+$/i, o = parseInt;
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
  return Fs = u, Fs;
}
var js, xf;
function Np() {
  if (xf) return js;
  xf = 1;
  var e = lm(), r = 1 / 0, t = 17976931348623157e292;
  function n(a) {
    if (!a)
      return a === 0 ? a : 0;
    if (a = e(a), a === r || a === -r) {
      var i = a < 0 ? -1 : 1;
      return i * t;
    }
    return a === a ? a : 0;
  }
  return js = n, js;
}
var Gs, Rf;
function cm() {
  if (Rf) return Gs;
  Rf = 1;
  var e = Np();
  function r(t) {
    var n = e(t), a = n % 1;
    return n === n ? a ? n - a : n : 0;
  }
  return Gs = r, Gs;
}
var zs, kf;
function dm() {
  if (kf) return zs;
  kf = 1;
  var e = Sp(), r = or(), t = cm(), n = Math.max;
  function a(i, s, o) {
    var u = i == null ? 0 : i.length;
    if (!u)
      return -1;
    var c = o == null ? 0 : t(o);
    return c < 0 && (c = n(u + c, 0)), e(i, r(s, 3), c);
  }
  return zs = a, zs;
}
var Bs, Af;
function fm() {
  if (Af) return Bs;
  Af = 1;
  var e = sm(), r = dm(), t = e(r);
  return Bs = t, Bs;
}
var Us, Pf;
function Dp() {
  if (Pf) return Us;
  Pf = 1;
  var e = su();
  function r(t) {
    var n = t == null ? 0 : t.length;
    return n ? e(t, 1) : [];
  }
  return Us = r, Us;
}
var Ks, Of;
function hm() {
  if (Of) return Ks;
  Of = 1;
  var e = tu(), r = ip(), t = wr();
  function n(a, i) {
    return a == null ? a : e(a, r(i), t);
  }
  return Ks = n, Ks;
}
var Vs, Nf;
function pm() {
  if (Nf) return Vs;
  Nf = 1;
  function e(r) {
    var t = r == null ? 0 : r.length;
    return t ? r[t - 1] : void 0;
  }
  return Vs = e, Vs;
}
var Hs, Df;
function vm() {
  if (Df) return Hs;
  Df = 1;
  var e = _t(), r = nu(), t = or();
  function n(a, i) {
    var s = {};
    return i = t(i, 3), r(a, function(o, u, c) {
      e(s, u, i(o, u, c));
    }), s;
  }
  return Hs = n, Hs;
}
var Ws, Mf;
function uu() {
  if (Mf) return Ws;
  Mf = 1;
  var e = Or();
  function r(t, n, a) {
    for (var i = -1, s = t.length; ++i < s; ) {
      var o = t[i], u = n(o);
      if (u != null && (c === void 0 ? u === u && !e(u) : a(u, c)))
        var c = u, l = o;
    }
    return l;
  }
  return Ws = r, Ws;
}
var Qs, $f;
function gm() {
  if ($f) return Qs;
  $f = 1;
  function e(r, t) {
    return r > t;
  }
  return Qs = e, Qs;
}
var Ys, Lf;
function ym() {
  if (Lf) return Ys;
  Lf = 1;
  var e = uu(), r = gm(), t = Ir();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return Ys = n, Ys;
}
var Js, Ff;
function Mp() {
  if (Ff) return Js;
  Ff = 1;
  var e = _t(), r = Rr();
  function t(n, a, i) {
    (i !== void 0 && !r(n[a], i) || i === void 0 && !(a in n)) && e(n, a, i);
  }
  return Js = t, Js;
}
var Xs, jf;
function mm() {
  if (jf) return Xs;
  jf = 1;
  var e = br(), r = Et(), t = Ye(), n = "[object Object]", a = Function.prototype, i = Object.prototype, s = a.toString, o = i.hasOwnProperty, u = s.call(Object);
  function c(l) {
    if (!t(l) || e(l) != n)
      return !1;
    var d = r(l);
    if (d === null)
      return !0;
    var p = o.call(d, "constructor") && d.constructor;
    return typeof p == "function" && p instanceof p && s.call(p) == u;
  }
  return Xs = c, Xs;
}
var Zs, Gf;
function $p() {
  if (Gf) return Zs;
  Gf = 1;
  function e(r, t) {
    if (!(t === "constructor" && typeof r[t] == "function") && t != "__proto__")
      return r[t];
  }
  return Zs = e, Zs;
}
var eo, zf;
function bm() {
  if (zf) return eo;
  zf = 1;
  var e = Qr(), r = wr();
  function t(n) {
    return e(n, r(n));
  }
  return eo = t, eo;
}
var ro, Bf;
function _m() {
  if (Bf) return ro;
  Bf = 1;
  var e = Mp(), r = Vh(), t = rp(), n = Hh(), a = np(), i = Yr(), s = qe(), o = Tp(), u = Ar(), c = Wr(), l = je(), d = mm(), p = Jr(), v = $p(), g = bm();
  function h(y, C, R, w, A, M, b) {
    var k = v(y, R), O = v(C, R), V = b.get(O);
    if (V) {
      e(y, R, V);
      return;
    }
    var G = M ? M(k, O, R + "", y, C, b) : void 0, S = G === void 0;
    if (S) {
      var j = s(O), K = !j && u(O), z = !j && !K && p(O);
      G = O, j || K || z ? s(k) ? G = k : o(k) ? G = n(k) : K ? (S = !1, G = r(O, !0)) : z ? (S = !1, G = t(O, !0)) : G = [] : d(O) || i(O) ? (G = k, i(k) ? G = g(k) : (!l(k) || c(k)) && (G = a(O))) : S = !1;
    }
    S && (b.set(O, G), A(G, O, w, M, b), b.delete(O)), e(y, R, G);
  }
  return ro = h, ro;
}
var to, Uf;
function wm() {
  if (Uf) return to;
  Uf = 1;
  var e = bt(), r = Mp(), t = tu(), n = _m(), a = je(), i = wr(), s = $p();
  function o(u, c, l, d, p) {
    u !== c && t(c, function(v, g) {
      if (p || (p = new e()), a(v))
        n(u, c, g, l, o, d, p);
      else {
        var h = d ? d(s(u, g), v, g + "", u, c, p) : void 0;
        h === void 0 && (h = v), r(u, g, h);
      }
    }, i);
  }
  return to = o, to;
}
var no, Kf;
function Im() {
  if (Kf) return no;
  Kf = 1;
  var e = kt(), r = At();
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
  return no = t, no;
}
var ao, Vf;
function qm() {
  if (Vf) return ao;
  Vf = 1;
  var e = wm(), r = Im(), t = r(function(n, a, i) {
    e(n, a, i);
  });
  return ao = t, ao;
}
var io, Hf;
function Lp() {
  if (Hf) return io;
  Hf = 1;
  function e(r, t) {
    return r < t;
  }
  return io = e, io;
}
var so, Wf;
function Cm() {
  if (Wf) return so;
  Wf = 1;
  var e = uu(), r = Lp(), t = Ir();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return so = n, so;
}
var oo, Qf;
function Em() {
  if (Qf) return oo;
  Qf = 1;
  var e = uu(), r = or(), t = Lp();
  function n(a, i) {
    return a && a.length ? e(a, r(i, 2), t) : void 0;
  }
  return oo = n, oo;
}
var uo, Yf;
function Sm() {
  if (Yf) return uo;
  Yf = 1;
  var e = Ue(), r = function() {
    return e.Date.now();
  };
  return uo = r, uo;
}
var lo, Jf;
function Tm() {
  if (Jf) return lo;
  Jf = 1;
  var e = wt(), r = xt(), t = It(), n = je(), a = Xr();
  function i(s, o, u, c) {
    if (!n(s))
      return s;
    o = r(o, s);
    for (var l = -1, d = o.length, p = d - 1, v = s; v != null && ++l < d; ) {
      var g = a(o[l]), h = u;
      if (g === "__proto__" || g === "constructor" || g === "prototype")
        return s;
      if (l != p) {
        var y = v[g];
        h = c ? c(y, g, v) : void 0, h === void 0 && (h = n(y) ? y : t(o[l + 1]) ? [] : {});
      }
      e(v, g, h), v = v[g];
    }
    return s;
  }
  return lo = i, lo;
}
var co, Xf;
function xm() {
  if (Xf) return co;
  Xf = 1;
  var e = Rt(), r = Tm(), t = xt();
  function n(a, i, s) {
    for (var o = -1, u = i.length, c = {}; ++o < u; ) {
      var l = i[o], d = e(a, l);
      s(d, l) && r(c, t(l, a), d);
    }
    return c;
  }
  return co = n, co;
}
var fo, Zf;
function Rm() {
  if (Zf) return fo;
  Zf = 1;
  var e = xm(), r = gp();
  function t(n, a) {
    return e(n, a, function(i, s) {
      return r(n, s);
    });
  }
  return fo = t, fo;
}
var ho, eh;
function km() {
  if (eh) return ho;
  eh = 1;
  var e = Dp(), r = Cp(), t = Ep();
  function n(a) {
    return t(r(a, void 0, e), a + "");
  }
  return ho = n, ho;
}
var po, rh;
function Am() {
  if (rh) return po;
  rh = 1;
  var e = Rm(), r = km(), t = r(function(n, a) {
    return n == null ? {} : e(n, a);
  });
  return po = t, po;
}
var vo, th;
function Pm() {
  if (th) return vo;
  th = 1;
  var e = Math.ceil, r = Math.max;
  function t(n, a, i, s) {
    for (var o = -1, u = r(e((a - n) / (i || 1)), 0), c = Array(u); u--; )
      c[s ? u : ++o] = n, n += i;
    return c;
  }
  return vo = t, vo;
}
var go, nh;
function Om() {
  if (nh) return go;
  nh = 1;
  var e = Pm(), r = At(), t = Np();
  function n(a) {
    return function(i, s, o) {
      return o && typeof o != "number" && r(i, s, o) && (s = o = void 0), i = t(i), s === void 0 ? (s = i, i = 0) : s = t(s), o = o === void 0 ? i < s ? 1 : -1 : t(o), e(i, s, o, a);
    };
  }
  return go = n, go;
}
var yo, ah;
function Nm() {
  if (ah) return yo;
  ah = 1;
  var e = Om(), r = e();
  return yo = r, yo;
}
var mo, ih;
function Dm() {
  if (ih) return mo;
  ih = 1;
  function e(r, t) {
    var n = r.length;
    for (r.sort(t); n--; )
      r[n] = r[n].value;
    return r;
  }
  return mo = e, mo;
}
var bo, sh;
function Mm() {
  if (sh) return bo;
  sh = 1;
  var e = Or();
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
  return bo = r, bo;
}
var _o, oh;
function $m() {
  if (oh) return _o;
  oh = 1;
  var e = Mm();
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
  return _o = r, _o;
}
var wo, uh;
function Lm() {
  if (uh) return wo;
  uh = 1;
  var e = Tt(), r = Rt(), t = or(), n = wp(), a = Dm(), i = qt(), s = $m(), o = Ir(), u = qe();
  function c(l, d, p) {
    d.length ? d = e(d, function(h) {
      return u(h) ? function(y) {
        return r(y, h.length === 1 ? h[0] : h);
      } : h;
    }) : d = [o];
    var v = -1;
    d = e(d, i(t));
    var g = n(l, function(h, y, C) {
      var R = e(d, function(w) {
        return w(h);
      });
      return { criteria: R, index: ++v, value: h };
    });
    return a(g, function(h, y) {
      return s(h, y, p);
    });
  }
  return wo = c, wo;
}
var Io, lh;
function Fm() {
  if (lh) return Io;
  lh = 1;
  var e = su(), r = Lm(), t = kt(), n = At(), a = t(function(i, s) {
    if (i == null)
      return [];
    var o = s.length;
    return o > 1 && n(i, s[0], s[1]) ? s = [] : o > 2 && n(s[0], s[1], s[2]) && (s = [s[0]]), r(i, e(s, 1), []);
  });
  return Io = a, Io;
}
var qo, ch;
function jm() {
  if (ch) return qo;
  ch = 1;
  var e = pp(), r = 0;
  function t(n) {
    var a = ++r;
    return e(n) + a;
  }
  return qo = t, qo;
}
var Co, dh;
function Gm() {
  if (dh) return Co;
  dh = 1;
  function e(r, t, n) {
    for (var a = -1, i = r.length, s = t.length, o = {}; ++a < i; ) {
      var u = a < s ? t[a] : void 0;
      n(o, r[a], u);
    }
    return o;
  }
  return Co = e, Co;
}
var Eo, fh;
function zm() {
  if (fh) return Eo;
  fh = 1;
  var e = wt(), r = Gm();
  function t(n, a) {
    return r(n || [], a || [], e);
  }
  return Eo = t, Eo;
}
var ct;
if (typeof Ko == "function")
  try {
    ct = {
      cloneDeep: am(),
      constant: ru(),
      defaults: im(),
      each: op(),
      filter: mp(),
      find: fm(),
      flatten: Dp(),
      forEach: sp(),
      forIn: hm(),
      has: bp(),
      isUndefined: _p(),
      last: pm(),
      map: Ip(),
      mapValues: vm(),
      max: ym(),
      merge: qm(),
      min: Cm(),
      minBy: Em(),
      now: Sm(),
      pick: Am(),
      range: Nm(),
      reduce: qp(),
      sortBy: Fm(),
      uniqueId: jm(),
      values: xp(),
      zipObject: zm()
    };
  } catch {
  }
ct || (ct = window._);
var be = ct, Bm = Pt;
function Pt() {
  var e = {};
  e._next = e._prev = e, this._sentinel = e;
}
Pt.prototype.dequeue = function() {
  var e = this._sentinel, r = e._prev;
  if (r !== e)
    return Fp(r), r;
};
Pt.prototype.enqueue = function(e) {
  var r = this._sentinel;
  e._prev && e._next && Fp(e), e._next = r._next, r._next._prev = e, r._next = e, e._prev = r;
};
Pt.prototype.toString = function() {
  for (var e = [], r = this._sentinel, t = r._prev; t !== r; )
    e.push(JSON.stringify(t, Um)), t = t._prev;
  return "[" + e.join(", ") + "]";
};
function Fp(e) {
  e._prev._next = e._next, e._next._prev = e._prev, delete e._next, delete e._prev;
}
function Um(e, r) {
  if (e !== "_next" && e !== "_prev")
    return r;
}
var nr = be, Km = Ke.Graph, Vm = Bm, Hm = Qm, Wm = nr.constant(1);
function Qm(e, r) {
  if (e.nodeCount() <= 1)
    return [];
  var t = Jm(e, r || Wm), n = Ym(t.graph, t.buckets, t.zeroIdx);
  return nr.flatten(nr.map(n, function(a) {
    return e.outEdges(a.v, a.w);
  }), !0);
}
function Ym(e, r, t) {
  for (var n = [], a = r[r.length - 1], i = r[0], s; e.nodeCount(); ) {
    for (; s = i.dequeue(); )
      So(e, r, t, s);
    for (; s = a.dequeue(); )
      So(e, r, t, s);
    if (e.nodeCount()) {
      for (var o = r.length - 2; o > 0; --o)
        if (s = r[o].dequeue(), s) {
          n = n.concat(So(e, r, t, s, !0));
          break;
        }
    }
  }
  return n;
}
function So(e, r, t, n, a) {
  var i = a ? [] : void 0;
  return nr.forEach(e.inEdges(n.v), function(s) {
    var o = e.edge(s), u = e.node(s.v);
    a && i.push({ v: s.v, w: s.w }), u.out -= o, Go(r, t, u);
  }), nr.forEach(e.outEdges(n.v), function(s) {
    var o = e.edge(s), u = s.w, c = e.node(u);
    c.in -= o, Go(r, t, c);
  }), e.removeNode(n.v), i;
}
function Jm(e, r) {
  var t = new Km(), n = 0, a = 0;
  nr.forEach(e.nodes(), function(o) {
    t.setNode(o, { v: o, in: 0, out: 0 });
  }), nr.forEach(e.edges(), function(o) {
    var u = t.edge(o.v, o.w) || 0, c = r(o), l = u + c;
    t.setEdge(o.v, o.w, l), a = Math.max(a, t.node(o.v).out += c), n = Math.max(n, t.node(o.w).in += c);
  });
  var i = nr.range(a + n + 3).map(function() {
    return new Vm();
  }), s = n + 1;
  return nr.forEach(t.nodes(), function(o) {
    Go(i, s, t.node(o));
  }), { graph: t, buckets: i, zeroIdx: s };
}
function Go(e, r, t) {
  t.out ? t.in ? e[t.out - t.in + r].enqueue(t) : e[e.length - 1].enqueue(t) : e[0].enqueue(t);
}
var yr = be, Xm = Hm, Zm = {
  run: eb,
  undo: tb
};
function eb(e) {
  var r = e.graph().acyclicer === "greedy" ? Xm(e, t(e)) : rb(e);
  yr.forEach(r, function(n) {
    var a = e.edge(n);
    e.removeEdge(n), a.forwardName = n.name, a.reversed = !0, e.setEdge(n.w, n.v, a, yr.uniqueId("rev"));
  });
  function t(n) {
    return function(a) {
      return n.edge(a).weight;
    };
  }
}
function rb(e) {
  var r = [], t = {}, n = {};
  function a(i) {
    yr.has(n, i) || (n[i] = !0, t[i] = !0, yr.forEach(e.outEdges(i), function(s) {
      yr.has(t, s.w) ? r.push(s) : a(s.w);
    }), delete t[i]);
  }
  return yr.forEach(e.nodes(), a), r;
}
function tb(e) {
  yr.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (t.reversed) {
      e.removeEdge(r);
      var n = t.forwardName;
      delete t.reversed, delete t.forwardName, e.setEdge(r.w, r.v, t, n);
    }
  });
}
var oe = be, jp = Ke.Graph, De = {
  addDummyNode: Gp,
  simplify: nb,
  asNonCompoundGraph: ab,
  successorWeights: ib,
  predecessorWeights: sb,
  intersectRect: ob,
  buildLayerMatrix: ub,
  normalizeRanks: lb,
  removeEmptyRanks: cb,
  addBorderNode: db,
  maxRank: zp,
  partition: fb,
  time: hb,
  notime: pb
};
function Gp(e, r, t, n) {
  var a;
  do
    a = oe.uniqueId(n);
  while (e.hasNode(a));
  return t.dummy = r, e.setNode(a, t), a;
}
function nb(e) {
  var r = new jp().setGraph(e.graph());
  return oe.forEach(e.nodes(), function(t) {
    r.setNode(t, e.node(t));
  }), oe.forEach(e.edges(), function(t) {
    var n = r.edge(t.v, t.w) || { weight: 0, minlen: 1 }, a = e.edge(t);
    r.setEdge(t.v, t.w, {
      weight: n.weight + a.weight,
      minlen: Math.max(n.minlen, a.minlen)
    });
  }), r;
}
function ab(e) {
  var r = new jp({ multigraph: e.isMultigraph() }).setGraph(e.graph());
  return oe.forEach(e.nodes(), function(t) {
    e.children(t).length || r.setNode(t, e.node(t));
  }), oe.forEach(e.edges(), function(t) {
    r.setEdge(t, e.edge(t));
  }), r;
}
function ib(e) {
  var r = oe.map(e.nodes(), function(t) {
    var n = {};
    return oe.forEach(e.outEdges(t), function(a) {
      n[a.w] = (n[a.w] || 0) + e.edge(a).weight;
    }), n;
  });
  return oe.zipObject(e.nodes(), r);
}
function sb(e) {
  var r = oe.map(e.nodes(), function(t) {
    var n = {};
    return oe.forEach(e.inEdges(t), function(a) {
      n[a.v] = (n[a.v] || 0) + e.edge(a).weight;
    }), n;
  });
  return oe.zipObject(e.nodes(), r);
}
function ob(e, r) {
  var t = e.x, n = e.y, a = r.x - t, i = r.y - n, s = e.width / 2, o = e.height / 2;
  if (!a && !i)
    throw new Error("Not possible to find intersection inside of the rectangle");
  var u, c;
  return Math.abs(i) * s > Math.abs(a) * o ? (i < 0 && (o = -o), u = o * a / i, c = o) : (a < 0 && (s = -s), u = s, c = s * i / a), { x: t + u, y: n + c };
}
function ub(e) {
  var r = oe.map(oe.range(zp(e) + 1), function() {
    return [];
  });
  return oe.forEach(e.nodes(), function(t) {
    var n = e.node(t), a = n.rank;
    oe.isUndefined(a) || (r[a][n.order] = t);
  }), r;
}
function lb(e) {
  var r = oe.min(oe.map(e.nodes(), function(t) {
    return e.node(t).rank;
  }));
  oe.forEach(e.nodes(), function(t) {
    var n = e.node(t);
    oe.has(n, "rank") && (n.rank -= r);
  });
}
function cb(e) {
  var r = oe.min(oe.map(e.nodes(), function(i) {
    return e.node(i).rank;
  })), t = [];
  oe.forEach(e.nodes(), function(i) {
    var s = e.node(i).rank - r;
    t[s] || (t[s] = []), t[s].push(i);
  });
  var n = 0, a = e.graph().nodeRankFactor;
  oe.forEach(t, function(i, s) {
    oe.isUndefined(i) && s % a !== 0 ? --n : n && oe.forEach(i, function(o) {
      e.node(o).rank += n;
    });
  });
}
function db(e, r, t, n) {
  var a = {
    width: 0,
    height: 0
  };
  return arguments.length >= 4 && (a.rank = t, a.order = n), Gp(e, "border", a, r);
}
function zp(e) {
  return oe.max(oe.map(e.nodes(), function(r) {
    var t = e.node(r).rank;
    if (!oe.isUndefined(t))
      return t;
  }));
}
function fb(e, r) {
  var t = { lhs: [], rhs: [] };
  return oe.forEach(e, function(n) {
    r(n) ? t.lhs.push(n) : t.rhs.push(n);
  }), t;
}
function hb(e, r) {
  var t = oe.now();
  try {
    return r();
  } finally {
    console.log(e + " time: " + (oe.now() - t) + "ms");
  }
}
function pb(e, r) {
  return r();
}
var Bp = be, vb = De, gb = {
  run: yb,
  undo: bb
};
function yb(e) {
  e.graph().dummyChains = [], Bp.forEach(e.edges(), function(r) {
    mb(e, r);
  });
}
function mb(e, r) {
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
      }, c = vb.addDummyNode(e, "edge", l, "_d"), n === u && (l.width = o.width, l.height = o.height, l.dummy = "edge-label", l.labelpos = o.labelpos), e.setEdge(t, c, { weight: o.weight }, s), d === 0 && e.graph().dummyChains.push(c), t = c;
    e.setEdge(t, a, { weight: o.weight }, s);
  }
}
function bb(e) {
  Bp.forEach(e.graph().dummyChains, function(r) {
    var t = e.node(r), n = t.edgeLabel, a;
    for (e.setEdge(t.edgeObj, n); t.dummy; )
      a = e.successors(r)[0], e.removeNode(r), n.points.push({ x: t.x, y: t.y }), t.dummy === "edge-label" && (n.x = t.x, n.y = t.y, n.width = t.width, n.height = t.height), r = a, t = e.node(r);
  });
}
var nt = be, Ot = {
  longestPath: _b,
  slack: wb
};
function _b(e) {
  var r = {};
  function t(n) {
    var a = e.node(n);
    if (nt.has(r, n))
      return a.rank;
    r[n] = !0;
    var i = nt.min(nt.map(e.outEdges(n), function(s) {
      return t(s.w) - e.edge(s).minlen;
    }));
    return (i === Number.POSITIVE_INFINITY || // return value of _.map([]) for Lodash 3
    i === void 0 || // return value of _.map([]) for Lodash 4
    i === null) && (i = 0), a.rank = i;
  }
  nt.forEach(e.sources(), t);
}
function wb(e, r) {
  return e.node(r.w).rank - e.node(r.v).rank - e.edge(r).minlen;
}
var dt = be, Ib = Ke.Graph, ft = Ot.slack, Up = qb;
function qb(e) {
  var r = new Ib({ directed: !1 }), t = e.nodes()[0], n = e.nodeCount();
  r.setNode(t, {});
  for (var a, i; Cb(r, e) < n; )
    a = Eb(r, e), i = r.hasNode(a.v) ? ft(e, a) : -ft(e, a), Sb(r, e, i);
  return r;
}
function Cb(e, r) {
  function t(n) {
    dt.forEach(r.nodeEdges(n), function(a) {
      var i = a.v, s = n === i ? a.w : i;
      !e.hasNode(s) && !ft(r, a) && (e.setNode(s, {}), e.setEdge(n, s, {}), t(s));
    });
  }
  return dt.forEach(e.nodes(), t), e.nodeCount();
}
function Eb(e, r) {
  return dt.minBy(r.edges(), function(t) {
    if (e.hasNode(t.v) !== e.hasNode(t.w))
      return ft(r, t);
  });
}
function Sb(e, r, t) {
  dt.forEach(e.nodes(), function(n) {
    r.node(n).rank += t;
  });
}
var ir = be, Tb = Up, xb = Ot.slack, Rb = Ot.longestPath, kb = Ke.alg.preorder, Ab = Ke.alg.postorder, Pb = De.simplify, Ob = qr;
qr.initLowLimValues = cu;
qr.initCutValues = lu;
qr.calcCutValue = Kp;
qr.leaveEdge = Hp;
qr.enterEdge = Wp;
qr.exchangeEdges = Qp;
function qr(e) {
  e = Pb(e), Rb(e);
  var r = Tb(e);
  cu(r), lu(r, e);
  for (var t, n; t = Hp(r); )
    n = Wp(r, e, t), Qp(r, e, t, n);
}
function lu(e, r) {
  var t = Ab(e, e.nodes());
  t = t.slice(0, t.length - 1), ir.forEach(t, function(n) {
    Nb(e, r, n);
  });
}
function Nb(e, r, t) {
  var n = e.node(t), a = n.parent;
  e.edge(t, a).cutvalue = Kp(e, r, t);
}
function Kp(e, r, t) {
  var n = e.node(t), a = n.parent, i = !0, s = r.edge(t, a), o = 0;
  return s || (i = !1, s = r.edge(a, t)), o = s.weight, ir.forEach(r.nodeEdges(t), function(u) {
    var c = u.v === t, l = c ? u.w : u.v;
    if (l !== a) {
      var d = c === i, p = r.edge(u).weight;
      if (o += d ? p : -p, Mb(e, t, l)) {
        var v = e.edge(t, l).cutvalue;
        o += d ? -v : v;
      }
    }
  }), o;
}
function cu(e, r) {
  arguments.length < 2 && (r = e.nodes()[0]), Vp(e, {}, 1, r);
}
function Vp(e, r, t, n, a) {
  var i = t, s = e.node(n);
  return r[n] = !0, ir.forEach(e.neighbors(n), function(o) {
    ir.has(r, o) || (t = Vp(e, r, t, o, n));
  }), s.low = i, s.lim = t++, a ? s.parent = a : delete s.parent, t;
}
function Hp(e) {
  return ir.find(e.edges(), function(r) {
    return e.edge(r).cutvalue < 0;
  });
}
function Wp(e, r, t) {
  var n = t.v, a = t.w;
  r.hasEdge(n, a) || (n = t.w, a = t.v);
  var i = e.node(n), s = e.node(a), o = i, u = !1;
  i.lim > s.lim && (o = s, u = !0);
  var c = ir.filter(r.edges(), function(l) {
    return u === hh(e, e.node(l.v), o) && u !== hh(e, e.node(l.w), o);
  });
  return ir.minBy(c, function(l) {
    return xb(r, l);
  });
}
function Qp(e, r, t, n) {
  var a = t.v, i = t.w;
  e.removeEdge(a, i), e.setEdge(n.v, n.w, {}), cu(e), lu(e, r), Db(e, r);
}
function Db(e, r) {
  var t = ir.find(e.nodes(), function(a) {
    return !r.node(a).parent;
  }), n = kb(e, t);
  n = n.slice(1), ir.forEach(n, function(a) {
    var i = e.node(a).parent, s = r.edge(a, i), o = !1;
    s || (s = r.edge(i, a), o = !0), r.node(a).rank = r.node(i).rank + (o ? s.minlen : -s.minlen);
  });
}
function Mb(e, r, t) {
  return e.hasEdge(r, t);
}
function hh(e, r, t) {
  return t.low <= r.lim && r.lim <= t.lim;
}
var $b = Ot, Yp = $b.longestPath, Lb = Up, Fb = Ob, jb = Gb;
function Gb(e) {
  switch (e.graph().ranker) {
    case "network-simplex":
      ph(e);
      break;
    case "tight-tree":
      Bb(e);
      break;
    case "longest-path":
      zb(e);
      break;
    default:
      ph(e);
  }
}
var zb = Yp;
function Bb(e) {
  Yp(e), Lb(e);
}
function ph(e) {
  Fb(e);
}
var zo = be, Ub = Kb;
function Kb(e) {
  var r = Hb(e);
  zo.forEach(e.graph().dummyChains, function(t) {
    for (var n = e.node(t), a = n.edgeObj, i = Vb(e, r, a.v, a.w), s = i.path, o = i.lca, u = 0, c = s[u], l = !0; t !== a.w; ) {
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
function Vb(e, r, t, n) {
  var a = [], i = [], s = Math.min(r[t].low, r[n].low), o = Math.max(r[t].lim, r[n].lim), u, c;
  u = t;
  do
    u = e.parent(u), a.push(u);
  while (u && (r[u].low > s || o > r[u].lim));
  for (c = u, u = n; (u = e.parent(u)) !== c; )
    i.push(u);
  return { path: a.concat(i.reverse()), lca: c };
}
function Hb(e) {
  var r = {}, t = 0;
  function n(a) {
    var i = t;
    zo.forEach(e.children(a), n), r[a] = { low: i, lim: t++ };
  }
  return zo.forEach(e.children(), n), r;
}
var ar = be, Bo = De, Wb = {
  run: Qb,
  cleanup: Xb
};
function Qb(e) {
  var r = Bo.addDummyNode(e, "root", {}, "_root"), t = Yb(e), n = ar.max(ar.values(t)) - 1, a = 2 * n + 1;
  e.graph().nestingRoot = r, ar.forEach(e.edges(), function(s) {
    e.edge(s).minlen *= a;
  });
  var i = Jb(e) + 1;
  ar.forEach(e.children(), function(s) {
    Jp(e, r, a, i, n, t, s);
  }), e.graph().nodeRankFactor = a;
}
function Jp(e, r, t, n, a, i, s) {
  var o = e.children(s);
  if (!o.length) {
    s !== r && e.setEdge(r, s, { weight: 0, minlen: t });
    return;
  }
  var u = Bo.addBorderNode(e, "_bt"), c = Bo.addBorderNode(e, "_bb"), l = e.node(s);
  e.setParent(u, s), l.borderTop = u, e.setParent(c, s), l.borderBottom = c, ar.forEach(o, function(d) {
    Jp(e, r, t, n, a, i, d);
    var p = e.node(d), v = p.borderTop ? p.borderTop : d, g = p.borderBottom ? p.borderBottom : d, h = p.borderTop ? n : 2 * n, y = v !== g ? 1 : a - i[s] + 1;
    e.setEdge(u, v, {
      weight: h,
      minlen: y,
      nestingEdge: !0
    }), e.setEdge(g, c, {
      weight: h,
      minlen: y,
      nestingEdge: !0
    });
  }), e.parent(s) || e.setEdge(r, u, { weight: 0, minlen: a + i[s] });
}
function Yb(e) {
  var r = {};
  function t(n, a) {
    var i = e.children(n);
    i && i.length && ar.forEach(i, function(s) {
      t(s, a + 1);
    }), r[n] = a;
  }
  return ar.forEach(e.children(), function(n) {
    t(n, 1);
  }), r;
}
function Jb(e) {
  return ar.reduce(e.edges(), function(r, t) {
    return r + e.edge(t).weight;
  }, 0);
}
function Xb(e) {
  var r = e.graph();
  e.removeNode(r.nestingRoot), delete r.nestingRoot, ar.forEach(e.edges(), function(t) {
    var n = e.edge(t);
    n.nestingEdge && e.removeEdge(t);
  });
}
var To = be, Zb = De, e_ = r_;
function r_(e) {
  function r(t) {
    var n = e.children(t), a = e.node(t);
    if (n.length && To.forEach(n, r), To.has(a, "minRank")) {
      a.borderLeft = [], a.borderRight = [];
      for (var i = a.minRank, s = a.maxRank + 1; i < s; ++i)
        vh(e, "borderLeft", "_bl", t, a, i), vh(e, "borderRight", "_br", t, a, i);
    }
  }
  To.forEach(e.children(), r);
}
function vh(e, r, t, n, a, i) {
  var s = { width: 0, height: 0, rank: i, borderType: r }, o = a[r][i - 1], u = Zb.addDummyNode(e, "border", s, t);
  a[r][i] = u, e.setParent(u, n), o && e.setEdge(o, u, { weight: 1 });
}
var Qe = be, t_ = {
  adjust: n_,
  undo: a_
};
function n_(e) {
  var r = e.graph().rankdir.toLowerCase();
  (r === "lr" || r === "rl") && Xp(e);
}
function a_(e) {
  var r = e.graph().rankdir.toLowerCase();
  (r === "bt" || r === "rl") && i_(e), (r === "lr" || r === "rl") && (s_(e), Xp(e));
}
function Xp(e) {
  Qe.forEach(e.nodes(), function(r) {
    gh(e.node(r));
  }), Qe.forEach(e.edges(), function(r) {
    gh(e.edge(r));
  });
}
function gh(e) {
  var r = e.width;
  e.width = e.height, e.height = r;
}
function i_(e) {
  Qe.forEach(e.nodes(), function(r) {
    xo(e.node(r));
  }), Qe.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    Qe.forEach(t.points, xo), Qe.has(t, "y") && xo(t);
  });
}
function xo(e) {
  e.y = -e.y;
}
function s_(e) {
  Qe.forEach(e.nodes(), function(r) {
    Ro(e.node(r));
  }), Qe.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    Qe.forEach(t.points, Ro), Qe.has(t, "x") && Ro(t);
  });
}
function Ro(e) {
  var r = e.x;
  e.x = e.y, e.y = r;
}
var rr = be, o_ = u_;
function u_(e) {
  var r = {}, t = rr.filter(e.nodes(), function(o) {
    return !e.children(o).length;
  }), n = rr.max(rr.map(t, function(o) {
    return e.node(o).rank;
  })), a = rr.map(rr.range(n + 1), function() {
    return [];
  });
  function i(o) {
    if (!rr.has(r, o)) {
      r[o] = !0;
      var u = e.node(o);
      a[u.rank].push(o), rr.forEach(e.successors(o), i);
    }
  }
  var s = rr.sortBy(t, function(o) {
    return e.node(o).rank;
  });
  return rr.forEach(s, i), a;
}
var lr = be, l_ = c_;
function c_(e, r) {
  for (var t = 0, n = 1; n < r.length; ++n)
    t += d_(e, r[n - 1], r[n]);
  return t;
}
function d_(e, r, t) {
  for (var n = lr.zipObject(
    t,
    lr.map(t, function(c, l) {
      return l;
    })
  ), a = lr.flatten(lr.map(r, function(c) {
    return lr.sortBy(lr.map(e.outEdges(c), function(l) {
      return { pos: n[l.w], weight: e.edge(l).weight };
    }), "pos");
  }), !0), i = 1; i < t.length; ) i <<= 1;
  var s = 2 * i - 1;
  i -= 1;
  var o = lr.map(new Array(s), function() {
    return 0;
  }), u = 0;
  return lr.forEach(a.forEach(function(c) {
    var l = c.pos + i;
    o[l] += c.weight;
    for (var d = 0; l > 0; )
      l % 2 && (d += o[l + 1]), l = l - 1 >> 1, o[l] += c.weight;
    u += c.weight * d;
  })), u;
}
var yh = be, f_ = h_;
function h_(e, r) {
  return yh.map(r, function(t) {
    var n = e.inEdges(t);
    if (n.length) {
      var a = yh.reduce(n, function(i, s) {
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
var Me = be, p_ = v_;
function v_(e, r) {
  var t = {};
  Me.forEach(e, function(a, i) {
    var s = t[a.v] = {
      indegree: 0,
      in: [],
      out: [],
      vs: [a.v],
      i
    };
    Me.isUndefined(a.barycenter) || (s.barycenter = a.barycenter, s.weight = a.weight);
  }), Me.forEach(r.edges(), function(a) {
    var i = t[a.v], s = t[a.w];
    !Me.isUndefined(i) && !Me.isUndefined(s) && (s.indegree++, i.out.push(t[a.w]));
  });
  var n = Me.filter(t, function(a) {
    return !a.indegree;
  });
  return g_(n);
}
function g_(e) {
  var r = [];
  function t(i) {
    return function(s) {
      s.merged || (Me.isUndefined(s.barycenter) || Me.isUndefined(i.barycenter) || s.barycenter >= i.barycenter) && y_(i, s);
    };
  }
  function n(i) {
    return function(s) {
      s.in.push(i), --s.indegree === 0 && e.push(s);
    };
  }
  for (; e.length; ) {
    var a = e.pop();
    r.push(a), Me.forEach(a.in.reverse(), t(a)), Me.forEach(a.out, n(a));
  }
  return Me.map(
    Me.filter(r, function(i) {
      return !i.merged;
    }),
    function(i) {
      return Me.pick(i, ["vs", "i", "barycenter", "weight"]);
    }
  );
}
function y_(e, r) {
  var t = 0, n = 0;
  e.weight && (t += e.barycenter * e.weight, n += e.weight), r.weight && (t += r.barycenter * r.weight, n += r.weight), e.vs = r.vs.concat(e.vs), e.barycenter = t / n, e.weight = n, e.i = Math.min(r.i, e.i), r.merged = !0;
}
var Br = be, m_ = De, b_ = __;
function __(e, r) {
  var t = m_.partition(e, function(l) {
    return Br.has(l, "barycenter");
  }), n = t.lhs, a = Br.sortBy(t.rhs, function(l) {
    return -l.i;
  }), i = [], s = 0, o = 0, u = 0;
  n.sort(w_(!!r)), u = mh(i, a, u), Br.forEach(n, function(l) {
    u += l.vs.length, i.push(l.vs), s += l.barycenter * l.weight, o += l.weight, u = mh(i, a, u);
  });
  var c = { vs: Br.flatten(i, !0) };
  return o && (c.barycenter = s / o, c.weight = o), c;
}
function mh(e, r, t) {
  for (var n; r.length && (n = Br.last(r)).i <= t; )
    r.pop(), e.push(n.vs), t++;
  return t;
}
function w_(e) {
  return function(r, t) {
    return r.barycenter < t.barycenter ? -1 : r.barycenter > t.barycenter ? 1 : e ? t.i - r.i : r.i - t.i;
  };
}
var cr = be, I_ = f_, q_ = p_, C_ = b_, E_ = Zp;
function Zp(e, r, t, n) {
  var a = e.children(r), i = e.node(r), s = i ? i.borderLeft : void 0, o = i ? i.borderRight : void 0, u = {};
  s && (a = cr.filter(a, function(g) {
    return g !== s && g !== o;
  }));
  var c = I_(e, a);
  cr.forEach(c, function(g) {
    if (e.children(g.v).length) {
      var h = Zp(e, g.v, t, n);
      u[g.v] = h, cr.has(h, "barycenter") && T_(g, h);
    }
  });
  var l = q_(c, t);
  S_(l, u);
  var d = C_(l, n);
  if (s && (d.vs = cr.flatten([s, d.vs, o], !0), e.predecessors(s).length)) {
    var p = e.node(e.predecessors(s)[0]), v = e.node(e.predecessors(o)[0]);
    cr.has(d, "barycenter") || (d.barycenter = 0, d.weight = 0), d.barycenter = (d.barycenter * d.weight + p.order + v.order) / (d.weight + 2), d.weight += 2;
  }
  return d;
}
function S_(e, r) {
  cr.forEach(e, function(t) {
    t.vs = cr.flatten(t.vs.map(function(n) {
      return r[n] ? r[n].vs : n;
    }), !0);
  });
}
function T_(e, r) {
  cr.isUndefined(e.barycenter) ? (e.barycenter = r.barycenter, e.weight = r.weight) : (e.barycenter = (e.barycenter * e.weight + r.barycenter * r.weight) / (e.weight + r.weight), e.weight += r.weight);
}
var Ur = be, x_ = Ke.Graph, R_ = k_;
function k_(e, r, t) {
  var n = A_(e), a = new x_({ compound: !0 }).setGraph({ root: n }).setDefaultNodeLabel(function(i) {
    return e.node(i);
  });
  return Ur.forEach(e.nodes(), function(i) {
    var s = e.node(i), o = e.parent(i);
    (s.rank === r || s.minRank <= r && r <= s.maxRank) && (a.setNode(i), a.setParent(i, o || n), Ur.forEach(e[t](i), function(u) {
      var c = u.v === i ? u.w : u.v, l = a.edge(c, i), d = Ur.isUndefined(l) ? 0 : l.weight;
      a.setEdge(c, i, { weight: e.edge(u).weight + d });
    }), Ur.has(s, "minRank") && a.setNode(i, {
      borderLeft: s.borderLeft[r],
      borderRight: s.borderRight[r]
    }));
  }), a;
}
function A_(e) {
  for (var r; e.hasNode(r = Ur.uniqueId("_root")); ) ;
  return r;
}
var P_ = be, O_ = N_;
function N_(e, r, t) {
  var n = {}, a;
  P_.forEach(t, function(i) {
    for (var s = e.parent(i), o, u; s; ) {
      if (o = e.parent(s), o ? (u = n[o], n[o] = s) : (u = a, a = s), u && u !== s) {
        r.setEdge(u, s);
        return;
      }
      s = o;
    }
  });
}
var fr = be, D_ = o_, M_ = l_, $_ = E_, L_ = R_, F_ = O_, j_ = Ke.Graph, bh = De, G_ = z_;
function z_(e) {
  var r = bh.maxRank(e), t = _h(e, fr.range(1, r + 1), "inEdges"), n = _h(e, fr.range(r - 1, -1, -1), "outEdges"), a = D_(e);
  wh(e, a);
  for (var i = Number.POSITIVE_INFINITY, s, o = 0, u = 0; u < 4; ++o, ++u) {
    B_(o % 2 ? t : n, o % 4 >= 2), a = bh.buildLayerMatrix(e);
    var c = M_(e, a);
    c < i && (u = 0, s = fr.cloneDeep(a), i = c);
  }
  wh(e, s);
}
function _h(e, r, t) {
  return fr.map(r, function(n) {
    return L_(e, n, t);
  });
}
function B_(e, r) {
  var t = new j_();
  fr.forEach(e, function(n) {
    var a = n.graph().root, i = $_(n, a, t, r);
    fr.forEach(i.vs, function(s, o) {
      n.node(s).order = o;
    }), F_(n, t, i.vs);
  });
}
function wh(e, r) {
  fr.forEach(r, function(t) {
    fr.forEach(t, function(n, a) {
      e.node(n).order = a;
    });
  });
}
var J = be, U_ = Ke.Graph, K_ = De, V_ = {
  positionX: nw
};
function H_(e, r) {
  var t = {};
  function n(a, i) {
    var s = 0, o = 0, u = a.length, c = J.last(i);
    return J.forEach(i, function(l, d) {
      var p = Q_(e, l), v = p ? e.node(p).order : u;
      (p || l === c) && (J.forEach(i.slice(o, d + 1), function(g) {
        J.forEach(e.predecessors(g), function(h) {
          var y = e.node(h), C = y.order;
          (C < s || v < C) && !(y.dummy && e.node(g).dummy) && ev(t, h, g);
        });
      }), o = d + 1, s = v);
    }), i;
  }
  return J.reduce(r, n), t;
}
function W_(e, r) {
  var t = {};
  function n(i, s, o, u, c) {
    var l;
    J.forEach(J.range(s, o), function(d) {
      l = i[d], e.node(l).dummy && J.forEach(e.predecessors(l), function(p) {
        var v = e.node(p);
        v.dummy && (v.order < u || v.order > c) && ev(t, p, l);
      });
    });
  }
  function a(i, s) {
    var o = -1, u, c = 0;
    return J.forEach(s, function(l, d) {
      if (e.node(l).dummy === "border") {
        var p = e.predecessors(l);
        p.length && (u = e.node(p[0]).order, n(s, c, d, o, u), c = d, o = u);
      }
      n(s, c, s.length, u, i.length);
    }), s;
  }
  return J.reduce(r, a), t;
}
function Q_(e, r) {
  if (e.node(r).dummy)
    return J.find(e.predecessors(r), function(t) {
      return e.node(t).dummy;
    });
}
function ev(e, r, t) {
  if (r > t) {
    var n = r;
    r = t, t = n;
  }
  var a = e[r];
  a || (e[r] = a = {}), a[t] = !0;
}
function Y_(e, r, t) {
  if (r > t) {
    var n = r;
    r = t, t = n;
  }
  return J.has(e[r], t);
}
function J_(e, r, t, n) {
  var a = {}, i = {}, s = {};
  return J.forEach(r, function(o) {
    J.forEach(o, function(u, c) {
      a[u] = u, i[u] = u, s[u] = c;
    });
  }), J.forEach(r, function(o) {
    var u = -1;
    J.forEach(o, function(c) {
      var l = n(c);
      if (l.length) {
        l = J.sortBy(l, function(h) {
          return s[h];
        });
        for (var d = (l.length - 1) / 2, p = Math.floor(d), v = Math.ceil(d); p <= v; ++p) {
          var g = l[p];
          i[c] === c && u < s[g] && !Y_(t, c, g) && (i[g] = c, i[c] = a[c] = a[g], u = s[g]);
        }
      }
    });
  }), { root: a, align: i };
}
function X_(e, r, t, n, a) {
  var i = {}, s = Z_(e, r, t, a), o = a ? "borderLeft" : "borderRight";
  function u(d, p) {
    for (var v = s.nodes(), g = v.pop(), h = {}; g; )
      h[g] ? d(g) : (h[g] = !0, v.push(g), v = v.concat(p(g))), g = v.pop();
  }
  function c(d) {
    i[d] = s.inEdges(d).reduce(function(p, v) {
      return Math.max(p, i[v.v] + s.edge(v));
    }, 0);
  }
  function l(d) {
    var p = s.outEdges(d).reduce(function(g, h) {
      return Math.min(g, i[h.w] - s.edge(h));
    }, Number.POSITIVE_INFINITY), v = e.node(d);
    p !== Number.POSITIVE_INFINITY && v.borderType !== o && (i[d] = Math.max(i[d], p));
  }
  return u(c, s.predecessors.bind(s)), u(l, s.successors.bind(s)), J.forEach(n, function(d) {
    i[d] = i[t[d]];
  }), i;
}
function Z_(e, r, t, n) {
  var a = new U_(), i = e.graph(), s = aw(i.nodesep, i.edgesep, n);
  return J.forEach(r, function(o) {
    var u;
    J.forEach(o, function(c) {
      var l = t[c];
      if (a.setNode(l), u) {
        var d = t[u], p = a.edge(d, l);
        a.setEdge(d, l, Math.max(s(e, c, u), p || 0));
      }
      u = c;
    });
  }), a;
}
function ew(e, r) {
  return J.minBy(J.values(r), function(t) {
    var n = Number.NEGATIVE_INFINITY, a = Number.POSITIVE_INFINITY;
    return J.forIn(t, function(i, s) {
      var o = iw(e, s) / 2;
      n = Math.max(i + o, n), a = Math.min(i - o, a);
    }), n - a;
  });
}
function rw(e, r) {
  var t = J.values(r), n = J.min(t), a = J.max(t);
  J.forEach(["u", "d"], function(i) {
    J.forEach(["l", "r"], function(s) {
      var o = i + s, u = e[o], c;
      if (u !== r) {
        var l = J.values(u);
        c = s === "l" ? n - J.min(l) : a - J.max(l), c && (e[o] = J.mapValues(u, function(d) {
          return d + c;
        }));
      }
    });
  });
}
function tw(e, r) {
  return J.mapValues(e.ul, function(t, n) {
    if (r)
      return e[r.toLowerCase()][n];
    var a = J.sortBy(J.map(e, n));
    return (a[1] + a[2]) / 2;
  });
}
function nw(e) {
  var r = K_.buildLayerMatrix(e), t = J.merge(
    H_(e, r),
    W_(e, r)
  ), n = {}, a;
  J.forEach(["u", "d"], function(s) {
    a = s === "u" ? r : J.values(r).reverse(), J.forEach(["l", "r"], function(o) {
      o === "r" && (a = J.map(a, function(d) {
        return J.values(d).reverse();
      }));
      var u = (s === "u" ? e.predecessors : e.successors).bind(e), c = J_(e, a, t, u), l = X_(
        e,
        a,
        c.root,
        c.align,
        o === "r"
      );
      o === "r" && (l = J.mapValues(l, function(d) {
        return -d;
      })), n[s + o] = l;
    });
  });
  var i = ew(e, n);
  return rw(n, i), tw(n, e.graph().align);
}
function aw(e, r, t) {
  return function(n, a, i) {
    var s = n.node(a), o = n.node(i), u = 0, c;
    if (u += s.width / 2, J.has(s, "labelpos"))
      switch (s.labelpos.toLowerCase()) {
        case "l":
          c = -s.width / 2;
          break;
        case "r":
          c = s.width / 2;
          break;
      }
    if (c && (u += t ? c : -c), c = 0, u += (s.dummy ? r : e) / 2, u += (o.dummy ? r : e) / 2, u += o.width / 2, J.has(o, "labelpos"))
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
function iw(e, r) {
  return e.node(r).width;
}
var Kr = be, rv = De, sw = V_.positionX, ow = uw;
function uw(e) {
  e = rv.asNonCompoundGraph(e), lw(e), Kr.forEach(sw(e), function(r, t) {
    e.node(t).x = r;
  });
}
function lw(e) {
  var r = rv.buildLayerMatrix(e), t = e.graph().ranksep, n = 0;
  Kr.forEach(r, function(a) {
    var i = Kr.max(Kr.map(a, function(s) {
      return e.node(s).height;
    }));
    Kr.forEach(a, function(s) {
      e.node(s).y = n + i / 2;
    }), n += i + t;
  });
}
var X = be, Ih = Zm, qh = gb, cw = jb, dw = De.normalizeRanks, fw = Ub, hw = De.removeEmptyRanks, Ch = Wb, pw = e_, Eh = t_, vw = G_, gw = ow, hr = De, yw = Ke.Graph, mw = bw;
function bw(e, r) {
  var t = r && r.debugTiming ? hr.time : hr.notime;
  t("layout", function() {
    var n = t("  buildLayoutGraph", function() {
      return kw(e);
    });
    t("  runLayout", function() {
      _w(n, t);
    }), t("  updateInputGraph", function() {
      ww(e, n);
    });
  });
}
function _w(e, r) {
  r("    makeSpaceForEdgeLabels", function() {
    Aw(e);
  }), r("    removeSelfEdges", function() {
    jw(e);
  }), r("    acyclic", function() {
    Ih.run(e);
  }), r("    nestingGraph.run", function() {
    Ch.run(e);
  }), r("    rank", function() {
    cw(hr.asNonCompoundGraph(e));
  }), r("    injectEdgeLabelProxies", function() {
    Pw(e);
  }), r("    removeEmptyRanks", function() {
    hw(e);
  }), r("    nestingGraph.cleanup", function() {
    Ch.cleanup(e);
  }), r("    normalizeRanks", function() {
    dw(e);
  }), r("    assignRankMinMax", function() {
    Ow(e);
  }), r("    removeEdgeLabelProxies", function() {
    Nw(e);
  }), r("    normalize.run", function() {
    qh.run(e);
  }), r("    parentDummyChains", function() {
    fw(e);
  }), r("    addBorderSegments", function() {
    pw(e);
  }), r("    order", function() {
    vw(e);
  }), r("    insertSelfEdges", function() {
    Gw(e);
  }), r("    adjustCoordinateSystem", function() {
    Eh.adjust(e);
  }), r("    position", function() {
    gw(e);
  }), r("    positionSelfEdges", function() {
    zw(e);
  }), r("    removeBorderNodes", function() {
    Fw(e);
  }), r("    normalize.undo", function() {
    qh.undo(e);
  }), r("    fixupEdgeLabelCoords", function() {
    $w(e);
  }), r("    undoCoordinateSystem", function() {
    Eh.undo(e);
  }), r("    translateGraph", function() {
    Dw(e);
  }), r("    assignNodeIntersects", function() {
    Mw(e);
  }), r("    reversePoints", function() {
    Lw(e);
  }), r("    acyclic.undo", function() {
    Ih.undo(e);
  });
}
function ww(e, r) {
  X.forEach(e.nodes(), function(t) {
    var n = e.node(t), a = r.node(t);
    n && (n.x = a.x, n.y = a.y, r.children(t).length && (n.width = a.width, n.height = a.height));
  }), X.forEach(e.edges(), function(t) {
    var n = e.edge(t), a = r.edge(t);
    n.points = a.points, X.has(a, "x") && (n.x = a.x, n.y = a.y);
  }), e.graph().width = r.graph().width, e.graph().height = r.graph().height;
}
var Iw = ["nodesep", "edgesep", "ranksep", "marginx", "marginy"], qw = { ranksep: 50, edgesep: 20, nodesep: 50, rankdir: "tb" }, Cw = ["acyclicer", "ranker", "rankdir", "align"], Ew = ["width", "height"], Sw = { width: 0, height: 0 }, Tw = ["minlen", "weight", "width", "height", "labeloffset"], xw = {
  minlen: 1,
  weight: 1,
  width: 0,
  height: 0,
  labeloffset: 10,
  labelpos: "r"
}, Rw = ["labelpos"];
function kw(e) {
  var r = new yw({ multigraph: !0, compound: !0 }), t = Ao(e.graph());
  return r.setGraph(X.merge(
    {},
    qw,
    ko(t, Iw),
    X.pick(t, Cw)
  )), X.forEach(e.nodes(), function(n) {
    var a = Ao(e.node(n));
    r.setNode(n, X.defaults(ko(a, Ew), Sw)), r.setParent(n, e.parent(n));
  }), X.forEach(e.edges(), function(n) {
    var a = Ao(e.edge(n));
    r.setEdge(n, X.merge(
      {},
      xw,
      ko(a, Tw),
      X.pick(a, Rw)
    ));
  }), r;
}
function Aw(e) {
  var r = e.graph();
  r.ranksep /= 2, X.forEach(e.edges(), function(t) {
    var n = e.edge(t);
    n.minlen *= 2, n.labelpos.toLowerCase() !== "c" && (r.rankdir === "TB" || r.rankdir === "BT" ? n.width += n.labeloffset : n.height += n.labeloffset);
  });
}
function Pw(e) {
  X.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (t.width && t.height) {
      var n = e.node(r.v), a = e.node(r.w), i = { rank: (a.rank - n.rank) / 2 + n.rank, e: r };
      hr.addDummyNode(e, "edge-proxy", i, "_ep");
    }
  });
}
function Ow(e) {
  var r = 0;
  X.forEach(e.nodes(), function(t) {
    var n = e.node(t);
    n.borderTop && (n.minRank = e.node(n.borderTop).rank, n.maxRank = e.node(n.borderBottom).rank, r = X.max(r, n.maxRank));
  }), e.graph().maxRank = r;
}
function Nw(e) {
  X.forEach(e.nodes(), function(r) {
    var t = e.node(r);
    t.dummy === "edge-proxy" && (e.edge(t.e).labelRank = t.rank, e.removeNode(r));
  });
}
function Dw(e) {
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
function Mw(e) {
  X.forEach(e.edges(), function(r) {
    var t = e.edge(r), n = e.node(r.v), a = e.node(r.w), i, s;
    t.points ? (i = t.points[0], s = t.points[t.points.length - 1]) : (t.points = [], i = a, s = n), t.points.unshift(hr.intersectRect(n, i)), t.points.push(hr.intersectRect(a, s));
  });
}
function $w(e) {
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
function Lw(e) {
  X.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    t.reversed && t.points.reverse();
  });
}
function Fw(e) {
  X.forEach(e.nodes(), function(r) {
    if (e.children(r).length) {
      var t = e.node(r), n = e.node(t.borderTop), a = e.node(t.borderBottom), i = e.node(X.last(t.borderLeft)), s = e.node(X.last(t.borderRight));
      t.width = Math.abs(s.x - i.x), t.height = Math.abs(a.y - n.y), t.x = i.x + t.width / 2, t.y = n.y + t.height / 2;
    }
  }), X.forEach(e.nodes(), function(r) {
    e.node(r).dummy === "border" && e.removeNode(r);
  });
}
function jw(e) {
  X.forEach(e.edges(), function(r) {
    if (r.v === r.w) {
      var t = e.node(r.v);
      t.selfEdges || (t.selfEdges = []), t.selfEdges.push({ e: r, label: e.edge(r) }), e.removeEdge(r);
    }
  });
}
function Gw(e) {
  var r = hr.buildLayerMatrix(e);
  X.forEach(r, function(t) {
    var n = 0;
    X.forEach(t, function(a, i) {
      var s = e.node(a);
      s.order = i + n, X.forEach(s.selfEdges, function(o) {
        hr.addDummyNode(e, "selfedge", {
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
function zw(e) {
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
function ko(e, r) {
  return X.mapValues(X.pick(e, r), Number);
}
function Ao(e) {
  var r = {};
  return X.forEach(e, function(t, n) {
    r[n.toLowerCase()] = t;
  }), r;
}
var at = be, Bw = De, Uw = Ke.Graph, Kw = {
  debugOrdering: Vw
};
function Vw(e) {
  var r = Bw.buildLayerMatrix(e), t = new Uw({ compound: !0, multigraph: !0 }).setGraph({});
  return at.forEach(e.nodes(), function(n) {
    t.setNode(n, { label: n }), t.setParent(n, "layer" + e.node(n).rank);
  }), at.forEach(e.edges(), function(n) {
    t.setEdge(n.v, n.w, {}, n.name);
  }), at.forEach(r, function(n, a) {
    var i = "layer" + a;
    t.setNode(i, { rank: "same" }), at.reduce(n, function(s, o) {
      return t.setEdge(s, o, { style: "invis" }), o;
    });
  }), t;
}
var Hw = "0.8.5", Ww = {
  graphlib: Ke,
  layout: mw,
  debug: Kw,
  util: {
    time: De.time,
    notime: De.notime
  },
  version: Hw
};
const Sh = /* @__PURE__ */ Xv(Ww), Vr = 148, Hr = 46, du = 80, fu = 44;
function Qw(e) {
  if (e.nodes.length === 0) return e;
  const r = new Sh.graphlib.Graph();
  r.setDefaultEdgeLabel(() => ({})), r.setGraph({ rankdir: "LR", nodesep: 60, ranksep: 120, marginx: 40, marginy: 40 });
  for (const n of e.nodes) r.setNode(n.id, { width: Vr, height: Hr });
  for (const n of e.edges) r.setEdge(n.source, n.target);
  Sh.layout(r);
  const t = {};
  for (const n of e.nodes) {
    const a = r.node(n.id);
    t[n.id] = a ? { x: Math.round(a.x - Vr / 2), y: Math.round(a.y - Hr / 2) } : { x: n.x, y: n.y };
  }
  return {
    ...e,
    nodes: e.nodes.map((n) => ({ ...n, ...t[n.id] })),
    layout: { ...e.layout, positions: t }
  };
}
const tv = (e, r) => Math.abs(e.x - r.x) < Vr + du && Math.abs(e.y - r.y) < Hr + fu;
function Yw(e) {
  const r = [], t = e.nodes.map((a) => {
    let i = Number.isFinite(a.x) ? a.x : 0, s = Number.isFinite(a.y) ? a.y : 0, o = 0;
    for (; r.some((u) => tv({ x: i, y: s }, u)) && o++ < 200; )
      i += Vr + du, i > 2400 && (i = 80, s += Hr + fu);
    return r.push({ x: i, y: s }), { ...a, x: i, y: s };
  });
  if (t.every((a, i) => a.x === e.nodes[i].x && a.y === e.nodes[i].y)) return e;
  const n = Object.fromEntries(t.map((a) => [a.id, { x: a.x, y: a.y }]));
  return { ...e, nodes: t, layout: { ...e.layout, positions: n } };
}
function Po(e, r, t) {
  let n = { x: Math.round(r), y: Math.round(t) }, a = 0;
  for (; e.nodes.some((i) => tv(n, i)) && a++ < 200; )
    n = { x: n.x + Vr + du, y: n.y }, n.x > 2400 && (n = { x: 80, y: n.y + Hr + fu });
  return n;
}
const { Header: Jw, Content: Xw } = iv;
function Zw({ ctx: e }) {
  var et, Mr;
  const [r, t] = sv.useMessage(), [n, a] = I([]), [i, s] = I([]), [o, u] = I([]), [c, l] = I([]), [d, p] = I(""), [v, g] = I(1), [h, y] = I(!0), [C, R] = I([]), [w, A] = I(() => st()), [M, b] = I(null), [k, O] = I(null), [V, G] = I([]), [S, j] = I(!1), [K, z] = I(!0), [ae, re] = I(!1), [de, B] = I(!1), [Q, E] = I(0), T = xe([]), L = xe([]), fe = xe(null), pe = xe(null), [Ee, vr] = I(null), [Nr, Je] = I({}), [Cr, ze] = I(!1), [Er, Ve] = I(!1), Xe = xe(1), Ze = xe(e);
  Ze.current = e;
  const ie = xe(w.trigger);
  _e(() => {
    ie.current = w.trigger;
  }, [w.trigger]), _e(() => {
    let q = !0;
    const N = Ze.current.hubQuery;
    return Promise.allSettled([
      N("industrial/nodes"),
      N("industrial/workflows"),
      N("industrial/resources")
    ]).then(async ([se, m, D]) => {
      var ve;
      if (!q) return;
      const Y = [];
      if (se.status === "fulfilled" ? a(Dh(se.value)) : Y.push(`节点描述: ${String(se.reason)}`), m.status === "fulfilled") {
        const te = Lh(m.value);
        s(te);
        const ge = new URLSearchParams(window.location.search).get("workflowId"), Ae = ge && te.some((We) => We.workflowId === ge) ? ge : (ve = te[0]) == null ? void 0 : ve.workflowId;
        Ae && ee(Ae);
      } else Y.push(`工作流: ${String(m.reason)}`);
      if (D.status === "fulfilled") {
        const te = Mh(D.value);
        l(te);
        const ge = te.map((Ae) => Ae.id);
        u(ge), p((Ae) => Ae || ge[0] || "");
      } else Y.push(`资源: ${String(D.reason)}`);
      R(Y);
      try {
        const te = await Ze.current.hubQuery("industrial/file-samples");
        q && Je((te == null ? void 0 : te.data) ?? te ?? {});
      } catch {
      }
    }).finally(() => {
      q && y(!1);
    }), () => {
      q = !1;
    };
  }, []);
  const he = ht(
    () => new Map(n.map((q) => [q.nodeTypeId, q])),
    [n]
  ), Z = w.nodes.find((q) => q.id === M) ?? null, Te = F(async (q, N, se) => {
    j(!0);
    try {
      return await Ne().api.hubCommand(q, N), r.success(se), !0;
    } catch (m) {
      return r.error(`${q} failed: ${m instanceof Error ? m.message : String(m)}`), !1;
    } finally {
      j(!1);
    }
  }, [r]), ee = F(async (q) => {
    try {
      const N = await e.hubQuery("industrial/workflow-definitions"), m = (Re(N) ?? []).find((te) => {
        const ge = te;
        return String((ge == null ? void 0 : ge.workflowId) ?? "") === q;
      }), D = m === void 0 ? { ...st(q), name: q } : Lv(m), Y = Yw(D);
      A(Y), T.current = [], L.current = [], E((te) => te + 1), g(Number(Y.layout.lookbackDays ?? 1) || 1);
      const ve = D.nodes.map((te) => /^n(\d+)$/.exec(te.id)).filter(Boolean).reduce((te, ge) => Math.max(te, Number(ge[1])), 0);
      Xe.current = ve + 1, b(null), re(!1), G([]), B(!1);
    } catch (N) {
      r.error(`加载工作流失败: ${N instanceof Error ? N.message : String(N)}`);
    }
  }, [e, r]);
  _e(() => {
    w.workflowId && G(bu(w, he, new Set(c.map((q) => q.id))));
  }, [he, w, c]);
  const ke = F(async () => {
    try {
      const q = Kv(await e.hubQuery("industrial/execution"));
      if (q === null) return;
      vr(q);
    } catch {
    }
  }, [e]), ue = F(async () => {
    var se;
    const q = new Set(w.resourceReferences ?? []);
    for (const m of w.nodes)
      for (const D of ["resourceId", "targetResourceId"]) {
        const Y = (se = m.config) == null ? void 0 : se[D];
        typeof Y == "string" && Y.trim() && q.add(Y.trim());
      }
    const N = {
      ...w,
      trigger: ie.current,
      resourceReferences: [...q],
      layout: { ...w.layout, lookbackDays: v }
    };
    await Te("industrial/save-workflow", N, "已保存") && (A(N), B(!1));
  }, [Te, w, v]), le = F(async () => {
    if (de) {
      r.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await Te("industrial/publish-workflow", { workflowId: w.workflowId }, "已发布") && await ke();
  }, [Te, w.workflowId, de, r, ke]), we = F(async () => {
    if (de) {
      r.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await Te(
      "industrial/run-workflow",
      { workflowId: w.workflowId, resourceId: d },
      "采集已启动"
    ) && await ke();
  }, [Te, w.workflowId, de, r, ke, d]), ce = F((q) => {
    q !== w && (T.current.length >= 50 && T.current.shift(), T.current.push(w), L.current = [], E((N) => N + 1), A(q), B(!0));
  }, [w]), He = F(() => {
    const q = T.current.pop();
    q && (L.current.push(w), A(q), B(!0), E((N) => N + 1));
  }, [w]), er = F(() => {
    const q = L.current.pop();
    q && (T.current.push(w), A(q), B(!0), E((N) => N + 1));
  }, [w]);
  _e(() => {
    const q = (N) => {
      const se = N.target;
      if (se && /INPUT|TEXTAREA|SELECT/.test(se.tagName)) return;
      const m = N.key.toLowerCase();
      if ((N.ctrlKey || N.metaKey) && m === "z") {
        N.preventDefault(), N.shiftKey ? er() : He();
        return;
      }
      if ((N.ctrlKey || N.metaKey) && m === "y") {
        N.preventDefault(), er();
        return;
      }
      if (N.key === "Delete" && M) {
        N.preventDefault(), ce(Mt(w, M)), b(null);
        return;
      }
      if ((N.ctrlKey || N.metaKey) && m === "c" && M) {
        pe.current = w.nodes.find((D) => D.id === M) ?? null;
        return;
      }
      if ((N.ctrlKey || N.metaKey) && m === "v" && pe.current) {
        N.preventDefault();
        const D = tr(he, pe.current.type);
        if (!D) return;
        const Y = `n${Xe.current++}`, ve = Po(w, pe.current.x + 40, pe.current.y + 40);
        ce({
          ...w,
          nodes: [...w.nodes, Dt(D, Y, ve.x, ve.y)]
        }), b(Y);
      }
    };
    return window.addEventListener("keydown", q), () => window.removeEventListener("keydown", q);
  }, [he, ce, w, er, M, He]);
  const Sr = F(() => {
    ce(Qw(w)), requestAnimationFrame(() => {
      var q, N;
      return (N = (q = fe.current) == null ? void 0 : q.fitView) == null ? void 0 : N.call(q, { when: "always" });
    });
  }, [ce, w]), Le = F((q, N, se) => {
    const m = tr(he, q);
    if (!m) return;
    const D = `n${Xe.current++}`, Y = Po(w, N, se);
    ce({ ...w, nodes: [...w.nodes, Dt(m, D, Y.x, Y.y)] }), b(D), re(!0), G([]);
  }, [he, ce, w]), ur = F((q) => {
    const N = Xe.current - 1;
    Le(q, 80 + N % 3 * 190, 80 + Math.floor(N / 3) * 130);
  }, [Le]), gr = F((q, N) => {
    const se = w.nodes.find((We) => We.id === q), m = w.nodes.find((We) => We.id === N);
    if (!se || !m)
      return r.warning("连线失败：端点不在画布上"), null;
    const D = tr(he, se.type), Y = tr(he, m.type);
    if (!D || !Y) return null;
    let ve = se, te = m, ge = yu(D, Y);
    if (!ge) {
      const We = yu(Y, D);
      if (!We)
        return r.warning(`端口类型不兼容：${D.name} → ${Y.name}`), null;
      ve = m, te = se, ge = We;
    }
    const Ae = yv(w, {
      source: ve.id,
      sourcePort: ge.sourcePort,
      target: te.id,
      targetPort: ge.targetPort
    }, he);
    return Ae.issue ? (r.warning(`${Ae.issue.code}: ${Ae.issue.message}`), G((We) => [...We, Ae.issue]), null) : Ae.definition === w ? (r.info("这条连线已经存在"), null) : (ce(Ae.definition), ge);
  }, [he, ce, w, r]), Dr = F(() => {
    const q = bu(w, he, new Set(o));
    G(q), q.length === 0 ? r.success("本地校验通过（发布时服务端仍会复核）") : (r.warning(`发现 ${q.length} 个问题，见属性抽屉定位列表`), re(!0));
  }, [w, he, o, r]), Tr = F((q) => {
    ce(Mt(w, q)), M === q && b(null);
  }, [ce, w, M]), Fe = F((q) => {
    const N = w.nodes.find((Y) => Y.id === q);
    if (!N) return;
    const se = tr(he, N.type);
    if (!se) return;
    const m = `n${Xe.current++}`, D = Po(w, N.x + 40, N.y + 40);
    ce({ ...w, nodes: [
      ...w.nodes,
      { ...Dt(se, m, D.x, D.y), config: { ...N.config } }
    ] }), b(m);
  }, [he, ce, w]), Be = F((q) => {
    const N = w.edges.findIndex((se) => `e:${se.source}:${se.sourcePort}>${se.target}:${se.targetPort}` === q);
    N >= 0 && ce(mu(w, N));
  }, [ce, w]), Zr = F((q, N) => {
    ce({
      ...w,
      edges: w.edges.map((se, m) => m === q ? N : se)
    });
  }, [ce, w]);
  return h ? /* @__PURE__ */ f(Rh, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ x("div", { className: "industrial-designer", children: [
    t,
    /* @__PURE__ */ x(Jw, { className: "industrial-header", children: [
      /* @__PURE__ */ x("div", { className: "industrial-title", children: [
        /* @__PURE__ */ f(U.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ f(U.Text, { type: "secondary", children: w.name || w.workflowId }),
        de && /* @__PURE__ */ f($e, { color: "orange", children: "未保存" }),
        Ee && /* @__PURE__ */ f($e, { color: Ee.error ? "error" : "success", children: Ee.status })
      ] }),
      /* @__PURE__ */ x("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ f(
          $,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": K,
            onClick: () => z((q) => !q),
            children: K ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ f(
          $,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => re((q) => !q),
            children: "属性"
          }
        ),
        /* @__PURE__ */ f($, { size: "small", "data-testid": "open-trigger", onClick: () => ze(!0), children: "触发器" }),
        /* @__PURE__ */ f(
          $,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => Ve(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ f(
          $,
          {
            size: "small",
            "data-testid": "designer-undo",
            disabled: T.current.length === 0,
            onClick: He,
            children: "撤销"
          }
        ),
        /* @__PURE__ */ f(
          $,
          {
            size: "small",
            "data-testid": "designer-redo",
            disabled: L.current.length === 0,
            onClick: er,
            children: "重做"
          }
        ),
        /* @__PURE__ */ f($, { size: "small", "data-testid": "designer-auto-layout", onClick: Sr, children: "自动布局" }),
        /* @__PURE__ */ f($, { size: "small", "data-testid": "designer-fit", onClick: () => {
          var q, N;
          return (N = (q = fe.current) == null ? void 0 : q.fitView) == null ? void 0 : N.call(q, { when: "always" });
        }, children: "适应画布" }),
        /* @__PURE__ */ f($, { size: "small", "data-testid": "designer-center", onClick: () => {
          var q, N;
          return (N = (q = fe.current) == null ? void 0 : q.fitCenter) == null ? void 0 : N.call(q);
        }, children: "居中" }),
        /* @__PURE__ */ f("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ x(W, { size: 4, children: [
          /* @__PURE__ */ f($, { size: "small", loading: S, onClick: () => void ue(), children: "保存" }),
          /* @__PURE__ */ f($, { size: "small", onClick: Dr, children: "校验" }),
          /* @__PURE__ */ f($, { size: "small", loading: S, onClick: () => void le(), children: "发布" }),
          /* @__PURE__ */ f($, { size: "small", type: "primary", loading: S, onClick: () => void we(), children: "运行" })
        ] }),
        /* @__PURE__ */ x(
          "select",
          {
            className: "industrial-resource-select",
            "aria-label": "运行资源",
            value: d,
            onChange: (q) => p(q.target.value),
            children: [
              /* @__PURE__ */ f("option", { value: "", children: "运行资源" }),
              o.map((q) => /* @__PURE__ */ f("option", { value: q, children: q }, q))
            ]
          }
        ),
        /* @__PURE__ */ f(
          it,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: v,
            onChange: (q) => {
              g(Number(q) || 1), B(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ x(Xw, { className: `industrial-body${K ? "" : " industrial-body--collapsed"}`, children: [
      K ? /* @__PURE__ */ x("aside", { className: "industrial-palette", children: [
        i.length > 0 && /* @__PURE__ */ f(
          Ce,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: w.workflowId,
            placeholder: "选择工作流",
            onChange: (q) => void ee(q),
            options: i.map((q) => ({ value: q.workflowId, label: q.name }))
          }
        ),
        /* @__PURE__ */ f(pv, { descriptors: n, loading: !1, onAdd: ur })
      ] }) : /* @__PURE__ */ f("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ f($, { type: "text", size: "small", onClick: () => z(!0), children: "节点库" }) }),
      /* @__PURE__ */ x("section", { className: "industrial-center", children: [
        C.length > 0 && /* @__PURE__ */ f(
          Se,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: C.join("；")
          }
        ),
        /* @__PURE__ */ f(
          hv,
          {
            definition: w,
            catalog: he,
            selectedId: M,
            onSelect: (q) => {
              b(q), q && re(!0);
            },
            onDropNode: Le,
            onCreateEdge: gr,
            onPositions: (q) => ce(_v(w, q)),
            onSelectIds: (q) => {
              q.length && b(q[0]);
            },
            onGraphReady: (q) => {
              fe.current = q;
            },
            onSelectEdge: (q) => O(q),
            onDeleteNodeById: Tr,
            onDuplicateNode: Fe,
            onDeleteEdgeById: Be
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ x(
      av,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: ae,
        mask: !1,
        getContainer: !1,
        onClose: () => re(!1),
        children: [
          Ee && /* @__PURE__ */ f(
            Se,
            {
              style: { marginBottom: 12 },
              type: Ee.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${Ee.status}`,
              description: /* @__PURE__ */ x(W, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                Ee.error && /* @__PURE__ */ f("span", { children: Ee.error }),
                Ee.executed.length > 0 && /* @__PURE__ */ x("span", { children: [
                  "已执行节点: ",
                  Ee.executed.join(", ")
                ] }),
                Ee.nodes.length > 0 && /* @__PURE__ */ f(
                  Oe,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: Ee.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (q, N) => N.error ?? (N.samples.length > 0 ? N.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ f(
            Rv,
            {
              node: Z,
              descriptor: Z ? tr(he, Z.type) ?? null : null,
              nodes: w.nodes,
              edges: w.edges,
              catalog: he,
              resources: c,
              issues: V,
              onApplyConfig: (q) => {
                M && ce(bv(w, M, q));
              },
              onDeleteNode: () => {
                ce(Mt(w, M ?? "")), b(null);
              },
              onDeleteEdge: (q) => ce(mu(w, q)),
              onCreateEdge: (q, N) => void gr(q, N),
              onUpdateEdge: Zr,
              onCreateResource: () => {
                var q;
                return (q = e.navigate) == null ? void 0 : q.call(e, "/industrial/resources");
              },
              selectedEdgeId: k,
              sampleLines: Z ? Nr[String(((et = Z.config) == null ? void 0 : et.resourceId) ?? ((Mr = Z.config) == null ? void 0 : Mr.targetResourceId) ?? "")] ?? [] : [],
              onSelectNode: (q) => {
                b(q), re(!0);
              }
            }
          )
        ]
      }
    ),
    Cr && /* @__PURE__ */ f(
      Wv,
      {
        open: Cr,
        onClose: () => ze(!1),
        trigger: w.trigger,
        resourceIds: o,
        workflowId: w.workflowId,
        ctx: e,
        onSave: (q) => {
          ie.current = q, ce({ ...w, trigger: q });
        }
      }
    ),
    Er && /* @__PURE__ */ f(
      Jv,
      {
        open: Er,
        onClose: () => Ve(!1),
        ctx: e,
        workflowId: w.workflowId,
        resourceId: d,
        lookbackDays: v
      }
    )
  ] });
}
function e1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(!1), [s, o] = I(""), [u, c] = I(null), [l, d] = I(!1), p = F(async () => {
    try {
      n(Lh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
    } catch (h) {
      c(h instanceof Error ? h.message : String(h));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    p();
  }, [p]);
  const v = F(async () => {
    var y;
    const h = s.trim();
    if (!h) {
      c("请输入工作流名称");
      return;
    }
    d(!0), c(null);
    try {
      const R = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await r.hubCommand("industrial/save-workflow", {
        ...st(R),
        name: h,
        layout: { lookbackDays: 1 }
      }), i(!1), o(""), await p(), (y = r.navigate) == null || y.call(r, `/industrial/designer?workflowId=${encodeURIComponent(R)}`);
    } catch (C) {
      c(C instanceof Error ? C.message : String(C));
    } finally {
      d(!1);
    }
  }, [r, p, s]), g = F((h) => {
    var y;
    (y = r.navigate) == null || y.call(r, `/industrial/designer?workflowId=${encodeURIComponent(h)}`);
  }, [r]);
  return /* @__PURE__ */ x(W, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ x(W, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ f($, { type: "primary", onClick: () => i(!0), children: "新建工作流" })
    ] }),
    u && /* @__PURE__ */ f(U.Text, { type: "danger", children: u }),
    /* @__PURE__ */ f(
      Oe,
      {
        rowKey: "workflowId",
        dataSource: t,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (h) => /* @__PURE__ */ f($, { type: "link", style: { padding: 0 }, onClick: () => g(h), children: h })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ f(
      mr,
      {
        open: a,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: l,
        onCancel: () => i(!1),
        onOk: () => void v(),
        children: /* @__PURE__ */ x(W, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(U.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: s,
              onChange: (h) => o(h.target.value ?? "")
            }
          ),
          u && /* @__PURE__ */ f(U.Text, { type: "danger", children: u })
        ] })
      }
    )
  ] });
}
const Oo = {
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
}, r1 = [
  { value: "filesystem.local", label: "本地目录" },
  { value: "filesystem.unc", label: "Windows UNC 共享" },
  { value: "sqlserver", label: "SQL Server" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mysql", label: "MySQL" },
  { value: "sqlite", label: "SQLite" }
];
function t1(e) {
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
function n1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I([]), [u, c] = I(Oo), [l, d] = I(!1), [p, v] = I(null), [g, h] = I(!1), y = F(async () => {
    try {
      const [b, k, O] = await Promise.all([
        r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        r.hubQuery("industrial/credentials"),
        r.hubQuery("industrial/resource-tests")
      ]);
      n(Fv(b)), i(Fh(k)), o(jv(O));
    } catch (b) {
      v(b instanceof Error ? b.message : String(b));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    y();
  }, [y]);
  const C = F((b) => {
    const k = (...O) => {
      for (const V of O) {
        const G = b.config[V];
        if (G != null && String(G).trim()) return String(G);
      }
      return "";
    };
    c({
      id: b.id,
      name: b.name,
      type: b.type,
      provider: b.provider,
      root: k("root", "path"),
      host: k("server", "host", "dataSource", "data_source"),
      database: k("database", "initialCatalog", "initial_catalog", "path"),
      port: k("port"),
      table: k("table"),
      credentialId: b.credentialId ?? "",
      enabled: b.enabled,
      config: b.config
    }), d(!0);
  }, []), R = F(async () => {
    const b = u.id.trim();
    if (!b) {
      v("请输入资源 ID");
      return;
    }
    h(!0), v(null);
    try {
      await r.hubCommand("industrial/save-resource", {
        id: b,
        name: u.name || b,
        type: u.type,
        provider: u.provider || "manual",
        config: t1(u),
        credentialId: u.credentialId || null,
        enabled: u.enabled
      }), d(!1), c(Oo), await y();
    } catch (k) {
      v(k instanceof Error ? k.message : String(k));
    } finally {
      h(!1);
    }
  }, [r, u, y]), w = F(async (b) => {
    v(null);
    try {
      await r.hubCommand("industrial/test-resource", { id: b });
    } catch (k) {
      v(k instanceof Error ? k.message : String(k));
    }
    await y();
  }, [r, y]), A = F(async (b) => {
    v(null);
    try {
      await r.hubCommand("industrial/delete-resource", { id: b }), await y();
    } catch (k) {
      v(k instanceof Error ? k.message : String(k));
    }
  }, [r, y]), M = new Map(s.map((b) => [b.resourceId, b]));
  return /* @__PURE__ */ x(W, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ x(W, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ x(W, { children: [
        /* @__PURE__ */ f($, { onClick: () => {
          var b;
          return (b = r.navigate) == null ? void 0 : b.call(r, "/industrial/designer");
        }, children: "返回设计器" }),
        /* @__PURE__ */ f($, { type: "primary", onClick: () => {
          c(Oo), d(!0);
        }, children: "新建资源" })
      ] })
    ] }),
    p && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: p }),
    /* @__PURE__ */ f(
      Oe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "ID", dataIndex: "id" },
          { title: "名称", dataIndex: "name" },
          { title: "类型", dataIndex: "type" },
          { title: "凭证", dataIndex: "credentialName", render: (b) => b ?? "—" },
          {
            title: "连接测试",
            key: "test",
            render: (b, k) => {
              const O = M.get(k.id);
              return O ? /* @__PURE__ */ f(U.Text, { type: O.ok ? "success" : "danger", children: O.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (b, k) => /* @__PURE__ */ x(W, { size: 4, children: [
              /* @__PURE__ */ f($, { size: "small", onClick: () => void w(k.id), children: "测试连接" }),
              /* @__PURE__ */ f($, { size: "small", onClick: () => C(k), children: "编辑" }),
              /* @__PURE__ */ f($, { size: "small", danger: !0, onClick: () => void A(k.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ f(
      mr,
      {
        open: l,
        title: u.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: g,
        onCancel: () => d(!1),
        onOk: () => void R(),
        children: /* @__PURE__ */ x(W, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: u.id,
              onChange: (b) => c({ ...u, id: b.target.value })
            }
          ),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: u.name,
              onChange: (b) => c({ ...u, name: b.target.value })
            }
          ),
          /* @__PURE__ */ f(U.Text, { children: "资源类型" }),
          /* @__PURE__ */ f(W, { wrap: !0, children: r1.map((b) => /* @__PURE__ */ f(
            $,
            {
              size: "small",
              type: u.type === b.value ? "primary" : "default",
              onClick: () => c({ ...u, type: b.value }),
              children: b.label
            },
            b.value
          )) }),
          (u.type === "filesystem.local" || u.type === "filesystem.unc") && /* @__PURE__ */ f(
            H,
            {
              "aria-label": "目录路径",
              placeholder: u.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: u.root,
              onChange: (b) => c({ ...u, root: b.target.value })
            }
          ),
          (u.type === "sqlserver" || u.type === "postgresql" || u.type === "mysql") && /* @__PURE__ */ x(W, { children: [
            /* @__PURE__ */ f(
              H,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: u.host,
                onChange: (b) => c({ ...u, host: b.target.value })
              }
            ),
            /* @__PURE__ */ f(
              H,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: u.database,
                onChange: (b) => c({ ...u, database: b.target.value })
              }
            ),
            /* @__PURE__ */ f(
              H,
              {
                "aria-label": "端口",
                placeholder: "端口",
                value: u.port,
                onChange: (b) => c({ ...u, port: b.target.value })
              }
            )
          ] }),
          u.type === "sqlite" && /* @__PURE__ */ f(
            H,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: u.database,
              onChange: (b) => c({ ...u, database: b.target.value })
            }
          ),
          /* @__PURE__ */ f(
            Ce,
            {
              "aria-label": "凭证",
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: u.credentialId || void 0,
              options: a.map((b) => ({ value: b.id, label: b.name })),
              onChange: (b) => c({ ...u, credentialId: b ?? "" })
            }
          ),
          /* @__PURE__ */ x(W, { children: [
            /* @__PURE__ */ f(
              pt,
              {
                "aria-label": "启用资源",
                checked: u.enabled,
                onChange: (b) => c({ ...u, enabled: b })
              }
            ),
            /* @__PURE__ */ f(U.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
const No = ["username-password", "domain-account", "api-token"];
function a1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(No), [s, o] = I(!1), [u, c] = I(""), [l, d] = I(""), [p, v] = I(No[0]), [g, h] = I(""), [y, C] = I(""), [R, w] = I(""), [A, M] = I(""), [b, k] = I(""), [O, V] = I(!1), [G, S] = I(null), [j, K] = I(!1), z = F(async () => {
    try {
      n(Fh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
      const T = await r.hubQuery("industrial/credential-types"), L = Array.isArray(T) ? T : Array.isArray(T == null ? void 0 : T.data) ? T.data : [];
      L.length && i(L.map((fe) => fe.kind));
    } catch (T) {
      S(T instanceof Error ? T.message : String(T));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    z();
  }, [z]);
  const ae = F(() => {
    c(""), d(""), v(No[0]), h(""), C(""), w(""), M(""), k(""), V(!1), S(null);
  }, []), re = F(() => {
    ae(), o(!0);
  }, [ae]), de = F((T) => {
    ae(), c(T.id), d(T.name), a.includes(T.kind) && v(T.kind), V(!0), o(!0);
  }, [a, ae]), B = F(() => b.trim() ? b.trim() : p === "api-token" ? A.trim() ? JSON.stringify({ token: A.trim() }) : null : !g.trim() || !y.trim() ? null : JSON.stringify(p === "domain-account" ? { username: g.trim(), password: y.trim(), domain: R.trim() } : { username: g.trim(), password: y.trim() }), [R, p, y, b, A, g]), Q = F(async () => {
    if (!u.trim()) {
      S("请输入凭证 ID");
      return;
    }
    const T = B();
    if (!T) {
      S("请填写凭证字段，或在高级内容中提供凭证");
      return;
    }
    K(!0), S(null);
    try {
      O ? await r.hubCommand("industrial/rotate-credential", { id: u, secret: T }) : await r.hubCommand("industrial/save-credential", { id: u, name: l || u, kind: p, secret: T }), o(!1), await z();
    } catch (L) {
      S(L instanceof Error ? L.message : String(L));
    } finally {
      K(!1);
    }
  }, [B, r, u, p, z, l, O, b]), E = F(async (T) => {
    S(null);
    try {
      await r.hubCommand("industrial/delete-credential", { id: T.id }), await z();
    } catch (L) {
      S(L instanceof Error ? L.message : String(L));
    }
  }, [r, z]);
  return /* @__PURE__ */ x(W, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ x(W, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ f($, { type: "primary", onClick: re, children: "新建凭证" })
    ] }),
    G && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: G }),
    /* @__PURE__ */ f(
      Oe,
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
            render: (T, L) => /* @__PURE__ */ x(W, { children: [
              /* @__PURE__ */ f($, { size: "small", onClick: () => de(L), children: "轮换" }),
              /* @__PURE__ */ f($, { size: "small", danger: !0, onClick: () => void E(L), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ f(
      mr,
      {
        open: s,
        title: O ? "轮换凭证" : "新建凭证",
        okText: O ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: j,
        onCancel: () => o(!1),
        onOk: () => void Q(),
        children: /* @__PURE__ */ x(W, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: u,
              disabled: O,
              onChange: (T) => c(T.target.value)
            }
          ),
          !O && /* @__PURE__ */ f(
            H,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: l,
              onChange: (T) => d(T.target.value)
            }
          ),
          /* @__PURE__ */ f(
            Ce,
            {
              "aria-label": "凭证类型",
              style: { width: "100%" },
              value: p,
              onChange: (T) => v(T),
              options: a.map((T) => ({ value: T, label: T }))
            }
          ),
          p === "api-token" ? /* @__PURE__ */ f(
            H,
            {
              "aria-label": "令牌",
              placeholder: "令牌",
              value: A,
              onChange: (T) => M(T.target.value)
            }
          ) : /* @__PURE__ */ x(dr, { children: [
            /* @__PURE__ */ f(
              H,
              {
                "aria-label": "用户名",
                placeholder: "用户名",
                value: g,
                onChange: (T) => h(T.target.value)
              }
            ),
            /* @__PURE__ */ f(
              H,
              {
                "aria-label": "密码",
                placeholder: "密码",
                value: y,
                onChange: (T) => C(T.target.value)
              }
            ),
            p === "domain-account" && /* @__PURE__ */ f(
              H,
              {
                "aria-label": "域",
                placeholder: "域（可选）",
                value: R,
                onChange: (T) => w(T.target.value)
              }
            )
          ] }),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "凭证内容",
              placeholder: "高级：直接提供凭证内容（优先）",
              value: b,
              onChange: (T) => k(T.target.value)
            }
          ),
          G && /* @__PURE__ */ f(U.Text, { type: "danger", children: G })
        ] })
      }
    )
  ] });
}
const Th = (e) => e === "Succeeded" || e === "Completed" ? "success" : e === "Failed" || e === "PartialFailure" ? "error" : e === "Running" || e === "Interrupted" ? "processing" : "default";
function i1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I(null), [u, c] = I(null), [l, d] = I(!1), p = F(async () => {
    try {
      const g = Uv(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/execution-center"));
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
    } catch (h) {
      o(h instanceof Error ? h.message : String(h));
    } finally {
      d(!1);
    }
  }, [r, p]);
  return /* @__PURE__ */ x(W, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    s && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: s }),
    u && /* @__PURE__ */ f(Se, { type: "success", showIcon: !0, message: u }),
    /* @__PURE__ */ f(
      Oe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ f($e, { color: Th(g), children: g }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (g) => g ?? "—" },
          { title: "错误", dataIndex: "error", render: (g) => g ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ f(
      Oe,
      {
        rowKey: "id",
        dataSource: a,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ f($e, { color: Th(g), children: g }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (g, h) => h.diagnosis ? /* @__PURE__ */ x(W, { direction: "vertical", size: 0, children: [
              /* @__PURE__ */ f(U.Text, { children: h.diagnosis.message }),
              /* @__PURE__ */ f(U.Text, { type: "secondary", children: h.diagnosis.action })
            ] }) : h.error ?? "—"
          },
          {
            title: "操作",
            key: "actions",
            render: (g, h) => /* @__PURE__ */ f(
              $,
              {
                size: "small",
                disabled: l || h.status !== "Failed" && h.status !== "Interrupted",
                onClick: () => void v(h),
                children: "重试此目标"
              }
            )
          }
        ]
      }
    )
  ] });
}
function s1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(null), s = F(async () => {
    try {
      n(Bv(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (u) {
      i(u instanceof Error ? u.message : String(u));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    s();
  }, [s]);
  const o = (u) => /* @__PURE__ */ f(
    Oe,
    {
      rowKey: "id",
      dataSource: u.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (c) => /* @__PURE__ */ f($e, { children: c }) },
        { title: "记录", dataIndex: "records" },
        {
          title: "接受/拒绝/警告",
          key: "counts",
          render: (c, l) => `${l.accepted} / ${l.rejected} / ${l.warnings}`
        },
        {
          title: "原因与下一步",
          key: "diagnosis",
          render: (c, l) => l.diagnosis ? /* @__PURE__ */ x(W, { direction: "vertical", size: 0, children: [
            /* @__PURE__ */ f(U.Text, { children: l.diagnosis.message }),
            /* @__PURE__ */ f(U.Text, { type: "secondary", children: l.diagnosis.action }),
            l.error && /* @__PURE__ */ f(U.Text, { type: "secondary", children: l.error })
          ] }) : l.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ x(W, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    a && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: a }),
    /* @__PURE__ */ f(
      Oe,
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
const Do = (e) => {
  const r = Re(e);
  return Array.isArray(r) ? r : [];
}, o1 = () => `machine-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 12) : Date.now().toString(36)}`;
function u1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I([]), [u, c] = I(null), [l, d] = I(""), [p, v] = I(""), [g, h] = I(), [y, C] = I(!0), [R, w] = I(null), A = F(async () => {
    try {
      n(Do(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/machines"))), i(Do(await r.hubQuery("industrial/resources"))), o(Do(await r.hubQuery("industrial/credentials")));
    } catch (S) {
      w(S instanceof Error ? S.message : String(S));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    A();
  }, [A]);
  const M = F(() => {
    c({ id: o1(), name: "", endpoint: "", credentialId: null, enabled: !0 }), d(""), v(""), h(void 0), C(!0), w(null);
  }, []), b = F((S) => {
    c(S), d(S.name), v(S.endpoint), h(S.credentialId ?? void 0), C(S.enabled), w(null);
  }, []), k = F(async () => {
    if (u) {
      if (!l.trim()) {
        w("请输入设备名称");
        return;
      }
      w(null);
      try {
        await r.hubCommand("industrial/save-machine", {
          id: u.id,
          name: l.trim(),
          endpoint: p.trim(),
          credentialId: g ?? null,
          enabled: y
        }), c(null), await A();
      } catch (S) {
        w(S instanceof Error ? S.message : String(S));
      }
    }
  }, [r, g, u, p, y, A, l]), O = F(async (S) => {
    w(null);
    try {
      await r.hubCommand("industrial/delete-machine", { id: S.id }), await A();
    } catch (j) {
      w(j instanceof Error ? j.message : String(j));
    }
  }, [r, A]), V = F(async (S, j) => {
    w(null);
    try {
      await r.hubCommand("industrial/save-machine", {
        id: S.id,
        name: S.name,
        endpoint: S.endpoint,
        credentialId: S.credentialId,
        enabled: j
      }), await A();
    } catch (K) {
      w(K instanceof Error ? K.message : String(K));
    }
  }, [r, A]), G = F(async (S, j) => {
    w(null);
    try {
      await r.hubCommand("industrial/save-resource", {
        id: S.id,
        machineId: j || null
      }), await A();
    } catch (K) {
      w(K instanceof Error ? K.message : String(K));
    }
  }, [r, A]);
  return /* @__PURE__ */ x(W, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ x(W, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "设备与资源" }),
      /* @__PURE__ */ f($, { type: "primary", onClick: M, children: "新增设备" })
    ] }),
    /* @__PURE__ */ f(U.Text, { type: "secondary", children: "设备地址是台账属性；实际连接由数据源资源配置决定。绑定凭证供需要认证的数据源引用。" }),
    R && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: R }),
    /* @__PURE__ */ f(
      Oe,
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
            render: (S) => {
              var j;
              return ((j = s.find((K) => K.id === S)) == null ? void 0 : j.name) ?? "—";
            }
          },
          {
            title: "启用",
            dataIndex: "enabled",
            render: (S, j) => /* @__PURE__ */ f(
              pt,
              {
                "aria-label": `设备 ${j.name} 启用`,
                checked: S,
                onChange: (K) => void V(j, K)
              }
            )
          },
          { title: "操作", key: "actions", render: (S, j) => /* @__PURE__ */ x(W, { children: [
            /* @__PURE__ */ f($, { size: "small", onClick: () => b(j), children: "编辑" }),
            /* @__PURE__ */ f($, { size: "small", danger: !0, onClick: () => void O(j), children: "删除" })
          ] }) }
        ]
      }
    ),
    /* @__PURE__ */ f(U.Title, { level: 5, style: { margin: 0 }, children: "资源归属" }),
    /* @__PURE__ */ f(
      Oe,
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
            render: (S, j) => /* @__PURE__ */ f(
              Ce,
              {
                "aria-label": `资源 ${j.name} 的设备`,
                style: { width: 220 },
                value: j.machineId ?? void 0,
                allowClear: !0,
                onChange: (K) => void G(j, K ?? ""),
                options: t.map((K) => ({ value: K.id, label: K.name }))
              }
            )
          }
        ]
      }
    ),
    /* @__PURE__ */ f(
      mr,
      {
        open: u !== null,
        title: u != null && u.name ? "编辑设备" : "新增设备",
        okText: "保存设备",
        cancelText: "取消",
        onCancel: () => c(null),
        onOk: () => void k(),
        children: /* @__PURE__ */ x(W, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "设备名称",
              placeholder: "设备名称",
              value: l,
              onChange: (S) => d(S.target.value)
            }
          ),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "设备地址",
              placeholder: "台账地址 / IP",
              value: p,
              onChange: (S) => v(S.target.value)
            }
          ),
          /* @__PURE__ */ f(
            Ce,
            {
              "aria-label": "设备凭证",
              style: { width: "100%" },
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: g,
              onChange: (S) => h(S),
              options: s.map((S) => ({
                value: S.id,
                label: `${S.name} (${S.kind})`
              }))
            }
          )
        ] })
      }
    )
  ] });
}
const l1 = ["filesystem.local", "filesystem.unc"], c1 = ["sink.sqlite", "sink.sqlserver", "sink.postgresql", "sink.mysql"];
function d1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I(""), [u, c] = I(""), [l, d] = I("csv"), [p, v] = I([]), [g, h] = I(null), [y, C] = I(!1);
  _e(() => {
    (async () => {
      try {
        n(Mh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"))), i(Dh(await r.hubQuery((e == null ? void 0 : e.nodesQuery) ?? "industrial/nodes")));
      } catch (b) {
        h(b instanceof Error ? b.message : String(b));
      }
    })();
  }, [e == null ? void 0 : e.nodesQuery, e == null ? void 0 : e.query, r]);
  const R = t.filter((b) => l1.includes(b.type)), w = t.filter((b) => c1.some((k) => b.type === k.split(".")[1])), A = ht(
    () => new Map(a.map((b) => [b.nodeTypeId, b])),
    [a]
  ), M = F(async () => {
    var b;
    if (!s.trim()) {
      h("请输入任务名称");
      return;
    }
    if (!u) {
      h("请选择数据来源");
      return;
    }
    if (p.length === 0) {
      h("请至少选择一个目标");
      return;
    }
    C(!0), h(null);
    try {
      const O = `quick-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`, G = `industrial.${t.find((B) => B.id === u).type}`, S = `industrial.reader.${l}`, j = (B, Q, E, T, L) => ({ id: B, type: Q, config: L, x: E, y: T }), K = (B, Q) => {
        var L, fe;
        const E = A.get(ae[B]), T = A.get(ae[Q]);
        return {
          source: B,
          sourcePort: ((L = E.outputs[0]) == null ? void 0 : L.name) ?? "output",
          target: Q,
          targetPort: ((fe = T.inputs[0]) == null ? void 0 : fe.name) ?? "input"
        };
      }, z = [
        j("src", G, 80, 120, { resourceId: u }),
        j("read", S, 280, 120, {}),
        ...p.map((B, Q) => j(
          `sink${Q + 1}`,
          `industrial.sink.${t.find((E) => E.id === B).type}`,
          500,
          80 + Q * 120,
          { targetResourceId: B }
        ))
      ], ae = Object.fromEntries(z.map((B) => [B.id, B.type])), re = [K("src", "read"), ...p.map((B, Q) => K("read", `sink${Q + 1}`))], de = {
        ...st(O),
        name: s.trim(),
        nodes: z,
        edges: re,
        resourceReferences: [u, ...p],
        layout: { lookbackDays: 1 }
      };
      await r.hubCommand("industrial/save-workflow", de), await r.hubCommand("industrial/publish-workflow", { workflowId: O }), (b = r.navigate) == null || b.call(r, `/industrial/designer?workflowId=${encodeURIComponent(O)}`);
    } catch (k) {
      h(k instanceof Error ? k.message : String(k));
    } finally {
      C(!1);
    }
  }, [r, l, A, s, t, u, p]);
  return /* @__PURE__ */ x(W, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "快速创建采集任务" }),
    /* @__PURE__ */ f(U.Text, { type: "secondary", children: "生成标准工作流，可在设计器中继续修改。" }),
    g && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: g }),
    /* @__PURE__ */ f(
      H,
      {
        "aria-label": "任务名称",
        placeholder: "任务名称",
        value: s,
        onChange: (b) => o(b.target.value)
      }
    ),
    /* @__PURE__ */ x(W, { wrap: !0, children: [
      /* @__PURE__ */ f(
        Ce,
        {
          "aria-label": "数据来源",
          style: { width: 260 },
          placeholder: "数据来源",
          value: u || void 0,
          onChange: (b) => c(b),
          options: R.map((b) => ({ value: b.id, label: `${b.name} (${b.type})` }))
        }
      ),
      /* @__PURE__ */ f(
        Ce,
        {
          "aria-label": "数据格式",
          style: { width: 160 },
          value: l,
          onChange: (b) => d(b),
          options: ["csv", "excel", "json"].map((b) => ({ value: b, label: b.toUpperCase() }))
        }
      )
    ] }),
    /* @__PURE__ */ f(
      Ce,
      {
        "aria-label": "目标资源",
        mode: "multiple",
        style: { width: 420 },
        placeholder: "目标资源",
        value: p,
        onChange: (b) => v(b),
        options: w.map((b) => ({ value: b.id, label: `${b.name} (${b.type})` }))
      }
    ),
    /* @__PURE__ */ f($, { type: "primary", loading: y, onClick: () => void M(), children: "创建并发布" })
  ] });
}
const f1 = (e) => {
  const r = Re(e);
  return Array.isArray(r) ? r : [];
}, h1 = () => ({
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
function p1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(null), [s, o] = I(""), [u, c] = I(""), [l, d] = I(null), p = F(async () => {
    try {
      n(f1(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-profiles")));
    } catch (h) {
      d(h instanceof Error ? h.message : String(h));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    p();
  }, [p]);
  const v = F((h) => {
    const y = h ? { ...h } : h1();
    i(y), o(Object.entries(y.mapping ?? {}).map(([C, R]) => `${C}=${R}`).join(",")), c((y.requiredFields ?? []).join(",")), d(null);
  }, []), g = F(async () => {
    if (a) {
      if (!a.name.trim()) {
        d("请输入配置名称");
        return;
      }
      d(null);
      try {
        const h = {};
        for (const y of s.split(",")) {
          const [C, R] = y.split("=").map((w) => w == null ? void 0 : w.trim());
          C && R && (h[C] = R);
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
          mapping: h,
          required: u.split(",").map((y) => y.trim()).filter(Boolean)
        }), i(null), await p();
      } catch (h) {
        d(h instanceof Error ? h.message : String(h));
      }
    }
  }, [r, a, p, s, u]);
  return /* @__PURE__ */ x(W, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ x(W, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "文件格式配置" }),
      /* @__PURE__ */ f($, { type: "primary", onClick: () => v(), children: "新建文件配置" })
    ] }),
    l && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: l }),
    /* @__PURE__ */ f(
      Oe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "名称", dataIndex: "name" },
          { title: "文件匹配", dataIndex: "fileNamePattern" },
          { title: "格式", dataIndex: "format" },
          { title: "版本", dataIndex: "version" },
          { title: "操作", key: "actions", render: (h, y) => /* @__PURE__ */ f($, { size: "small", onClick: () => v(y), children: "编辑" }) }
        ]
      }
    ),
    /* @__PURE__ */ f(
      mr,
      {
        open: a !== null,
        title: a != null && a.version ? "编辑文件配置" : "新建文件配置",
        okText: "保存配置",
        cancelText: "取消",
        onCancel: () => i(null),
        onOk: () => void g(),
        children: a && /* @__PURE__ */ x(W, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "配置名称",
              placeholder: "配置名称",
              value: a.name,
              onChange: (h) => i({ ...a, name: h.target.value })
            }
          ),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "文件匹配规则",
              placeholder: "文件匹配，例如 *.csv",
              value: a.fileNamePattern,
              onChange: (h) => i({ ...a, fileNamePattern: h.target.value })
            }
          ),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "文件格式",
              placeholder: "csv / excel / json",
              value: a.format,
              onChange: (h) => i({ ...a, format: h.target.value })
            }
          ),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "分隔符",
              placeholder: "分隔符",
              value: a.delimiter,
              onChange: (h) => i({ ...a, delimiter: h.target.value })
            }
          ),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "编码",
              placeholder: "编码",
              value: a.encoding,
              onChange: (h) => i({ ...a, encoding: h.target.value })
            }
          ),
          /* @__PURE__ */ f(
            pt,
            {
              "aria-label": "首行为表头",
              checked: a.hasHeader,
              onChange: (h) => i({ ...a, hasHeader: h })
            }
          ),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "字段映射",
              placeholder: "目标字段=来源字段，逗号分隔",
              value: s,
              onChange: (h) => o(h.target.value)
            }
          ),
          /* @__PURE__ */ f(
            H,
            {
              "aria-label": "必填字段",
              placeholder: "必填字段，逗号分隔",
              value: u,
              onChange: (h) => c(h.target.value)
            }
          )
        ] })
      }
    )
  ] });
}
const $r = (e) => {
  const r = Re(e);
  return Array.isArray(r) ? r : [];
}, v1 = (e) => new Promise((r, t) => {
  const n = new FileReader();
  n.onload = () => r(String(n.result).split(",")[1] ?? ""), n.onerror = () => t(n.error ?? new Error("无法读取 Excel 文件")), n.readAsDataURL(e);
});
function g1({ block: e, ctx: r }) {
  var Mr, q, N, se;
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I([]), [u, c] = I([]), [l, d] = I([]), [p, v] = I([]), [g, h] = I([]), [y, C] = I(), [R, w] = I(1), [A, M] = I("resource-{machine}"), [b, k] = I([]), [O, V] = I(""), [G, S] = I(""), [j, K] = I(""), [z, ae] = I(""), [re, de] = I(null), [B, Q] = I("manual"), [E, T] = I(""), [L, fe] = I(""), [pe, Ee] = I(""), [vr, Nr] = I(""), [Je, Cr] = I(""), [ze, Er] = I(null), [Ve, Xe] = I(null), [Ze, ie] = I(1), [he, Z] = I(""), [Te, ee] = I(null), [ke, ue] = I(!1), le = F(async () => {
    try {
      n($r(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/bindings"))), i($r(await r.hubQuery("industrial/machines"))), o($r(await r.hubQuery("industrial/file-profiles"))), c($r(await r.hubQuery("industrial/workflows"))), d($r(await r.hubQuery("industrial/resources")));
      try {
        de(await r.hubQuery("industrial/binding-status"));
      } catch {
      }
    } catch (m) {
      ee(m instanceof Error ? m.message : String(m));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    le();
  }, [le]);
  const we = ht(() => {
    const m = [];
    for (const ve of p) for (const te of g) {
      const ge = `binding:${y ?? ""}:v${R}:${ve}:${te}`;
      t.some((Ae) => Ae.id === ge) && m.push(ge);
    }
    const D = b.filter((ve) => !l.some((te) => te.id === ve)), Y = p.map((ve) => A.replace("{machine}", ve)).filter((ve) => !l.some((te) => te.id === ve));
    return {
      total: p.length * g.length,
      create: p.length * g.length - m.length,
      conflicts: m,
      missing: [...D, ...Y]
    };
  }, [t, p, g, l, A, b, R, y]), ce = F(async () => {
    if (!y || p.length === 0 || g.length === 0 || b.length === 0) {
      ee("请选择工作流、机台、文件配置和目标资源");
      return;
    }
    ue(!0), ee(null);
    try {
      await r.hubCommand("industrial/create-bindings", {
        machineIds: p,
        fileProfileIds: g,
        workflowId: y,
        workflowVersion: R,
        sourceResourcePattern: A,
        targetResourceIds: b,
        triggerKind: B,
        cron: E || null,
        parameters: Object.fromEntries(vr.split(";").map((m) => m.split("=")).filter((m) => m.length === 2 && m[0].trim() && m[1].trim()).map((m) => [m[0].trim(), m[1].trim()])),
        exclusions: Je.split(/[,\n;]/).map((m) => m.trim()).filter(Boolean)
      }), await le();
    } catch (m) {
      ee(m instanceof Error ? m.message : String(m));
    } finally {
      ue(!1);
    }
  }, [r, E, Je, le, g, vr, A, b, B, R, y, p]), He = F(async (m, D) => {
    ue(!0), ee(null);
    try {
      await r.hubCommand("industrial/set-binding-enabled", { id: m.id, enabled: D }), await le();
    } catch (Y) {
      ee(Y instanceof Error ? Y.message : String(Y));
    } finally {
      ue(!1);
    }
  }, [r, le]), er = F(async (m, D) => {
    ue(!0), ee(null);
    try {
      await r.hubCommand("industrial/run-binding", {
        bindingId: m.id,
        lookbackDays: D
      }), de(await r.hubQuery("industrial/binding-status"));
    } catch (Y) {
      ee(Y instanceof Error ? Y.message : String(Y));
    } finally {
      ue(!1);
    }
  }, [r]), Sr = F(async () => {
    ue(!0), ee(null);
    try {
      await r.hubCommand("industrial/import-file-profiles", { csv: L }), fe(""), await le();
    } catch (m) {
      ee(m instanceof Error ? m.message : String(m));
    } finally {
      ue(!1);
    }
  }, [r, le, L]), Le = F(async () => {
    ue(!0), ee(null);
    try {
      await r.hubCommand("industrial/import-bindings", { csv: pe }), Ee(""), await le();
    } catch (m) {
      ee(m instanceof Error ? m.message : String(m));
    } finally {
      ue(!1);
    }
  }, [r, le, pe]), ur = F(async (m, D) => {
    if (D) {
      ue(!0), ee(null);
      try {
        const Y = m === "profiles" ? "industrial/import-file-profiles" : `industrial/import-${m}`;
        await r.hubCommand(Y, { xlsx: await v1(D) }), await le();
      } catch (Y) {
        ee(Y instanceof Error ? Y.message : String(Y));
      } finally {
        ue(!1);
      }
    }
  }, [r, le]), gr = F(async () => {
    ue(!0), ee(null);
    try {
      await r.hubCommand("industrial/preview-bindings", {
        machineIds: p,
        fileProfileIds: g,
        workflowId: y,
        workflowVersion: R,
        sourceResourcePattern: A,
        targetResourceIds: b,
        triggerKind: B,
        cron: E || null,
        exclusions: Je.split(/[,\n;]/).map((m) => m.trim()).filter(Boolean)
      }), Er(await r.hubQuery("industrial/binding-preview"));
    } catch (m) {
      ee(m instanceof Error ? m.message : String(m));
    } finally {
      ue(!1);
    }
  }, [r, E, Je, p, g, g, A, b, B, R, y]), Dr = F(async () => {
    ue(!0), ee(null);
    try {
      await r.hubCommand("industrial/preview-import", { csv: pe }), Xe(await r.hubQuery("industrial/import-preview"));
    } catch (m) {
      ee(m instanceof Error ? m.message : String(m));
    } finally {
      ue(!1);
    }
  }, [r, pe]), Tr = F(async () => {
    ue(!0), ee(null);
    try {
      const m = t.filter((D) => D.workflowId === y).map((D) => D.id);
      await r.hubCommand("industrial/upgrade-binding-version", { ids: m, version: Ze }), await le();
    } catch (m) {
      ee(m instanceof Error ? m.message : String(m));
    } finally {
      ue(!1);
    }
  }, [t, r, le, Ze, y]), Fe = F(async () => {
    ue(!0), ee(null);
    try {
      await r.hubCommand("industrial/import-machines", { csv: O }), V(""), await le();
    } catch (m) {
      ee(m instanceof Error ? m.message : String(m));
    } finally {
      ue(!1);
    }
  }, [r, O, le]), Be = F(async () => {
    ee(null);
    try {
      const m = await r.hubQuery("industrial/export-config"), D = (m == null ? void 0 : m.data) ?? m;
      S(JSON.stringify(D, null, 2));
    } catch (m) {
      ee(m instanceof Error ? m.message : String(m));
    }
  }, [r]), Zr = (m) => {
    const D = {};
    for (const Y of m.split(/[\n,]/)) {
      const [ve, te] = Y.split("=").map((ge) => ge == null ? void 0 : ge.trim());
      ve && te && (D[ve] = te);
    }
    return D;
  }, et = F(async () => {
    ue(!0), ee(null);
    try {
      const m = JSON.parse(G);
      m.resourceMap = Zr(j), m.credentialMap = Zr(z), await r.hubCommand("industrial/import-config", m), await le();
    } catch (m) {
      ee(m instanceof Error ? m.message : String(m));
    } finally {
      ue(!1);
    }
  }, [r, z, le, G, j]);
  return /* @__PURE__ */ x(W, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "采集任务绑定" }),
    Te && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: Te }),
    /* @__PURE__ */ x(W, { wrap: !0, children: [
      /* @__PURE__ */ f(
        H,
        {
          "aria-label": "工作流 ID",
          placeholder: "工作流 ID",
          value: y ?? "",
          onChange: (m) => C(m.target.value)
        }
      ),
      /* @__PURE__ */ f(
        H,
        {
          "aria-label": "机台 ID 列表",
          placeholder: "机台 ID，逗号分隔",
          value: p.join(","),
          onChange: (m) => v(m.target.value.split(",").map((D) => D.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ f(
        H,
        {
          "aria-label": "文件配置 ID 列表",
          placeholder: "文件配置 ID，逗号分隔",
          value: g.join(","),
          onChange: (m) => h(m.target.value.split(",").map((D) => D.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ f(
        H,
        {
          "aria-label": "目标资源 ID 列表",
          placeholder: "目标资源 ID，逗号分隔",
          value: b.join(","),
          onChange: (m) => k(m.target.value.split(",").map((D) => D.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ f(
        Ce,
        {
          "aria-label": "工作流模板",
          mode: "single",
          style: { width: 220 },
          value: y,
          onChange: (m) => C(m),
          options: u.map((m) => ({ value: m.workflowId, label: m.name }))
        }
      ),
      /* @__PURE__ */ f(
        it,
        {
          "aria-label": "模板版本",
          min: 1,
          value: R,
          onChange: (m) => w(Number(m) || 1)
        }
      ),
      /* @__PURE__ */ f(
        Ce,
        {
          "aria-label": "机台范围",
          mode: "multiple",
          style: { width: 300 },
          value: p,
          onChange: (m) => v(m),
          options: a.map((m) => ({ value: m.id, label: m.name }))
        }
      ),
      /* @__PURE__ */ f(
        Ce,
        {
          "aria-label": "文件配置范围",
          mode: "multiple",
          style: { width: 300 },
          value: g,
          onChange: (m) => h(m),
          options: s.map((m) => ({ value: m.id, label: m.name }))
        }
      ),
      /* @__PURE__ */ f(
        Ce,
        {
          "aria-label": "目标资源",
          mode: "multiple",
          style: { width: 300 },
          value: b,
          onChange: (m) => k(m),
          options: l.map((m) => ({ value: m.id, label: `${m.name} (${m.type})` }))
        }
      ),
      /* @__PURE__ */ x(
        "select",
        {
          "aria-label": "绑定触发策略选择",
          value: B,
          onChange: (m) => Q(m.target.value),
          children: [
            /* @__PURE__ */ f("option", { value: "manual", children: "手动" }),
            /* @__PURE__ */ f("option", { value: "schedule", children: "定时" })
          ]
        }
      ),
      /* @__PURE__ */ f(
        Ce,
        {
          "aria-label": "绑定触发策略",
          style: { width: 160 },
          value: B,
          onChange: (m) => Q(m),
          options: [{ value: "manual", label: "手动" }, { value: "schedule", label: "定时" }]
        }
      ),
      /* @__PURE__ */ f(
        H,
        {
          "aria-label": "绑定 Cron",
          placeholder: "cron（定时策略）",
          value: E,
          onChange: (m) => T(m.target.value)
        }
      ),
      /* @__PURE__ */ f(
        H,
        {
          "aria-label": "来源资源模板",
          placeholder: "来源资源模板，{machine} 占位符",
          value: A,
          onChange: (m) => M(m.target.value)
        }
      ),
      /* @__PURE__ */ f(
        H,
        {
          "aria-label": "运行参数",
          placeholder: "运行参数，如 lookbackDays=7;shift=day",
          value: vr,
          onChange: (m) => Nr(m.target.value)
        }
      ),
      /* @__PURE__ */ f(
        H,
        {
          "aria-label": "例外规则",
          placeholder: "例外规则：机台:文件配置，逗号或换行分隔",
          value: Je,
          onChange: (m) => Cr(m.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ x(W, { wrap: !0, children: [
      /* @__PURE__ */ f($, { size: "small", onClick: () => v(a.map((m) => m.id)), children: "全选机台" }),
      /* @__PURE__ */ f($, { size: "small", onClick: () => h(s.map((m) => m.id)), children: "全选文件配置" }),
      /* @__PURE__ */ f($, { size: "small", onClick: () => {
        v([]), h([]);
      }, children: "清空范围" })
    ] }),
    /* @__PURE__ */ f(
      Se,
      {
        type: "info",
        showIcon: !0,
        message: `预计生成 ${we.total} 条绑定；可创建 ${we.create} 条`,
        description: `冲突 ${we.conflicts.length} 条；缺失资源 ${we.missing.length} 条`
      }
    ),
    /* @__PURE__ */ x(W, { wrap: !0, children: [
      /* @__PURE__ */ f($, { type: "primary", loading: ke, onClick: () => void ce(), children: "生成绑定" }),
      /* @__PURE__ */ f($, { loading: ke, onClick: () => void gr(), children: "预览绑定" }),
      /* @__PURE__ */ f($, { loading: ke, onClick: () => void Dr(), children: "预览导入" })
    ] }),
    ze && /* @__PURE__ */ f(
      Se,
      {
        type: "info",
        showIcon: !0,
        "data-testid": "binding-preview-report",
        message: `批量预览：${ze.total} 组合，创建 ${ze.create}`,
        description: `冲突 ${((Mr = ze.conflicts) == null ? void 0 : Mr.length) ?? 0}；缺失 ${((q = ze.missing) == null ? void 0 : q.length) ?? 0}；错误 ${((N = ze.errors) == null ? void 0 : N.length) ?? 0}`
      }
    ),
    Ve && /* @__PURE__ */ f(
      Se,
      {
        type: (se = Ve.errors) != null && se.length ? "warning" : "success",
        showIcon: !0,
        "data-testid": "import-preview-report",
        message: `导入预览：${Ve.valid ?? 0}/${Ve.total ?? 0} 行有效`,
        description: (Ve.errors ?? []).map((m, D) => /* @__PURE__ */ x("div", { "data-testid": `import-error-row-${m.row}`, children: [
          "第 ",
          m.row,
          " 行：",
          m.message
        ] }, D))
      }
    ),
    /* @__PURE__ */ x(W, { wrap: !0, children: [
      /* @__PURE__ */ f(
        it,
        {
          "aria-label": "升级到模板版本",
          min: 1,
          value: Ze,
          onChange: (m) => ie(Number(m) || 1)
        }
      ),
      /* @__PURE__ */ f($, { loading: ke, onClick: () => void Tr(), children: "升级当前工作流绑定版本" })
    ] }),
    /* @__PURE__ */ f(U.Title, { level: 5, style: { margin: 0 }, children: "批量导入机台（CSV）" }),
    /* @__PURE__ */ f(
      H.TextArea,
      {
        "aria-label": "机台 CSV",
        rows: 4,
        value: O,
        placeholder: "id,name,endpoint,credentialId",
        onChange: (m) => V(m.target.value)
      }
    ),
    /* @__PURE__ */ f($, { loading: ke, onClick: () => void Fe(), children: "导入机台" }),
    /* @__PURE__ */ f(
      H.TextArea,
      {
        "aria-label": "文件配置 CSV",
        rows: 3,
        value: L,
        placeholder: "id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required",
        onChange: (m) => fe(m.target.value)
      }
    ),
    /* @__PURE__ */ f($, { loading: ke, onClick: () => void Sr(), children: "导入文件配置" }),
    /* @__PURE__ */ f(
      H.TextArea,
      {
        "aria-label": "绑定 CSV",
        rows: 3,
        value: pe,
        placeholder: "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId,targetResourceIds,triggerKind,cron",
        onChange: (m) => Ee(m.target.value)
      }
    ),
    /* @__PURE__ */ f($, { loading: ke, onClick: () => void Le(), children: "导入采集绑定" }),
    /* @__PURE__ */ f(U.Title, { level: 5, style: { margin: 0 }, children: "Excel 批量导入（.xlsx，选择即导入）" }),
    /* @__PURE__ */ x(W, { wrap: !0, children: [
      /* @__PURE__ */ x("label", { children: [
        "机台 Excel",
        /* @__PURE__ */ f(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "机台 Excel 文件",
            onChange: (m) => {
              var D;
              return void ur("machines", (D = m.target.files) == null ? void 0 : D[0]);
            }
          }
        )
      ] }),
      /* @__PURE__ */ x("label", { children: [
        "文件配置 Excel",
        /* @__PURE__ */ f(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "文件配置 Excel 文件",
            onChange: (m) => {
              var D;
              return void ur("profiles", (D = m.target.files) == null ? void 0 : D[0]);
            }
          }
        )
      ] }),
      /* @__PURE__ */ x("label", { children: [
        "采集绑定 Excel",
        /* @__PURE__ */ f(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "绑定 Excel 文件",
            onChange: (m) => {
              var D;
              return void ur("bindings", (D = m.target.files) == null ? void 0 : D[0]);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ f(U.Title, { level: 5, style: { margin: 0 }, children: "配置包（不含凭证）" }),
    /* @__PURE__ */ x(W, { wrap: !0, children: [
      /* @__PURE__ */ f($, { onClick: () => void Be(), children: "导出配置" }),
      /* @__PURE__ */ f($, { loading: ke, onClick: () => void et(), children: "导入配置" })
    ] }),
    /* @__PURE__ */ f(
      H,
      {
        "aria-label": "资源 ID 映射",
        placeholder: "旧资源ID=新资源ID，逗号或换行分隔",
        value: j,
        onChange: (m) => K(m.target.value)
      }
    ),
    /* @__PURE__ */ f(
      H,
      {
        "aria-label": "凭证别名映射",
        placeholder: "旧凭证ID=新凭证ID",
        value: z,
        onChange: (m) => ae(m.target.value)
      }
    ),
    /* @__PURE__ */ f(
      H.TextArea,
      {
        "aria-label": "配置包",
        rows: 5,
        value: G,
        placeholder: "导出后可跨环境导入；资源 ID 需自行映射",
        onChange: (m) => S(m.target.value)
      }
    ),
    /* @__PURE__ */ f(
      H,
      {
        "aria-label": "绑定筛选",
        placeholder: "按绑定名称/ID筛选",
        value: he,
        onChange: (m) => Z(m.target.value)
      }
    ),
    /* @__PURE__ */ f(
      Oe,
      {
        rowKey: "id",
        dataSource: t.filter((m) => !he || `${m.id} ${m.name}`.includes(he)),
        pagination: { pageSize: 20 },
        columns: [
          { title: "绑定", dataIndex: "name" },
          { title: "机台", dataIndex: "machineId" },
          { title: "文件配置", dataIndex: "fileProfileId" },
          { title: "模板版本", dataIndex: "workflowVersion" },
          { title: "运行参数", key: "parameters", render: (m, D) => /* @__PURE__ */ f("span", { "data-testid": `binding-parameters-${D.id}`, children: Object.entries(D.parameters ?? {}).map(([Y, ve]) => `${Y}=${ve}`).join(";") || "—" }) },
          { title: "状态", dataIndex: "enabled", render: (m, D) => /* @__PURE__ */ x(W, { children: [
            /* @__PURE__ */ f($, { size: "small", onClick: () => void He(D, !D.enabled), children: D.enabled ? "暂停" : "恢复" }),
            /* @__PURE__ */ f($, { size: "small", onClick: () => void er(D, 7), children: "补采 7 天" }),
            /* @__PURE__ */ f($, { size: "small", onClick: () => void er(D, 1), children: "运行" })
          ] }) },
          { title: "排障", key: "diagnostics", render: (m, D) => {
            const Y = ((re == null ? void 0 : re.runs) ?? []).filter((te) => te.workflowId === D.workflowId), ve = ((re == null ? void 0 : re.deliveries) ?? []).filter((te) => te.status === "Failed");
            return /* @__PURE__ */ x(U.Text, { style: { fontSize: 11 }, children: [
              Y.length ? `最近运行 ${Y[0].status}` : "暂无运行",
              " · 失败交付 ",
              ve.length
            ] });
          } }
        ]
      }
    )
  ] });
}
const y1 = `
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
.industrial-align-guide{position:absolute;left:50%;top:10px;transform:translateX(-50%);z-index:3;pointer-events:none;padding:3px 9px;border-radius:999px;background:#4d6bfe;color:#fff;font-size:11px}
.industrial-graph-hint button{pointer-events:auto;margin-left:8px;border:0;border-radius:999px;background:#fff;color:#102548;padding:1px 8px;font-size:11px;cursor:pointer}
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
function m1(e) {
  nv(e);
  const r = typeof document > "u" ? null : document.createElement("style");
  r && (r.dataset.industrialDesigner = "true", r.textContent = y1, document.head.appendChild(r));
  const t = ({ ctx: i }) => Mo(Zw, { ctx: i });
  t.displayName = "IndustrialWorkflowCanvas";
  const n = (i, s) => {
    const o = ({ block: u, ctx: c }) => Mo(s, { block: u, ctx: c });
    return o.displayName = i, e.blocks.register(i, o);
  }, a = [
    e.blocks.register("industrial-workflow-canvas", t),
    n("industrial-workflow-list", e1),
    n("industrial-resource-console", n1),
    n("industrial-credential-console", a1),
    n("industrial-execution-center", i1),
    n("industrial-file-details", s1),
    n("industrial-machine-console", u1),
    n("industrial-collection-wizard", d1),
    n("industrial-file-profiles", p1),
    n("industrial-binding-console", g1)
  ];
  return () => {
    for (const i of a) i == null || i();
    r == null || r.remove();
  };
}
export {
  Xv as g,
  m1 as r
};
