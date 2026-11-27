let jr = null;
function iv(e) {
  jr = e;
}
function Pe() {
  if (!jr && typeof globalThis < "u" && (jr = globalThis.__CORDIS_CONSOLE ?? null), !jr)
    throw new Error("[industrial-designer] console facade is not available");
  return jr;
}
const f = (...e) => Pe().jsxRuntime.jsx(...e), R = (...e) => Pe().jsxRuntime.jsxs(...e), dr = (e) => Pe().jsxRuntime.jsx(Pe().jsxRuntime.Fragment, e), Lo = (...e) => Pe().React.createElement(...e), F = (...e) => Pe().React.useCallback(...e), _e = (...e) => Pe().React.useEffect(...e), ht = (...e) => Pe().React.useMemo(...e), Te = (...e) => Pe().React.useRef(...e), I = (...e) => Pe().React.useState(...e), ye = (e) => {
  const r = (t) => {
    const n = e.split(".").reduce((a, i) => a == null ? void 0 : a[i], Pe().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Lo(n, t ?? {});
  };
  return r.displayName = `facade.${e}`, r;
}, Ee = ye("Alert"), $ = ye("Button"), pu = ye("Divider"), sv = ye("Drawer"), xh = ye("Empty"), $o = Object.assign(ye("Form"), {
  Item: ye("Form.Item"),
  useForm: (...e) => Pe().antd.Form.useForm(...e)
}), Q = Object.assign(ye("Input"), {
  Number: ye("InputNumber"),
  TextArea: ye("Input.TextArea"),
  Password: ye("Input.Password")
}), it = ye("InputNumber"), ov = Object.assign(ye("Layout"), {
  Header: ye("Layout.Header"),
  Content: ye("Layout.Content")
}), br = ye("Modal"), je = ye("Select"), Y = ye("Space"), kh = ye("Spin"), pt = ye("Switch"), Ae = ye("Table"), Le = ye("Tag"), U = Object.assign(ye("Typography"), {
  Title: ye("Typography.Title"),
  Text: ye("Typography.Text"),
  Paragraph: ye("Typography.Paragraph")
}), uv = new Proxy({}, {
  get: (e, r) => (...t) => {
    var a;
    const n = (a = Pe().antd.message) == null ? void 0 : a[r];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${r} unavailable`);
    return n(...t);
  }
}), Ah = 148, Ph = 46, vu = {
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
}, gu = (e) => vu[e] ?? vu[e.split(".")[0]] ?? "#4d6bfe";
function lv(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function cv(e, r = /* @__PURE__ */ new Map(), t) {
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
          fill: gu(i),
          stroke: s ? "#111111" : gu(i),
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
      id: lv(n),
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
function Oh(e) {
  const r = Object.values(e), t = /* @__PURE__ */ new Set();
  for (let n = 0; n < r.length; n++)
    for (let a = n + 1; a < r.length; a++)
      r[n].x === r[a].x && t.add(`x=${r[n].x}`), r[n].y === r[a].y && t.add(`y=${r[n].y}`);
  return [...t];
}
function jo(e, r, t) {
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
function dv(e, r) {
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
        size: [Ah, Ph],
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
    behaviors: Nh()
  };
}
const fv = "__industrial_drag_target__", hv = "__industrial_connect_source__", Dt = (e, r) => {
  var t;
  return (((t = e.isPanModifier) == null ? void 0 : t.call(e)) ?? !1) || r.button === 1 || r.buttons === 4 || r.shiftKey === !0;
}, yu = (e, r) => {
  var l, d;
  if (r.targetType !== "node" || !((l = r.target) != null && l.id)) return !1;
  const t = ((d = e.gestureOrigin) == null ? void 0 : d.call(e)) ?? r.client ?? r.canvas;
  if (!t || !e.graph) return !1;
  const n = String(r.target.id), a = e.graph.getNodeData().find((p) => String(p.id) === n), i = a == null ? void 0 : a.style;
  if (typeof (i == null ? void 0 : i.x) != "number" || typeof i.y != "number") return !1;
  const [s, o] = e.graph.getCanvasByClient([t.x, t.y]), u = Math.abs(s - i.x), c = Math.abs(o - i.y);
  return Math.abs(u - Ah / 2) <= 14 && c <= Ph / 2 + 10;
};
function Nh(e = {}) {
  const r = e.connectionState ?? { armed: !1 }, t = (n) => {
    var c, l, d;
    const a = e.graph;
    if (!a || !e.onPositions) return;
    const i = Array.isArray(n) ? n : n ? [String(n)] : void 0, s = a.getNodeData().map((p) => ({
      id: String(p.id),
      data: p.data ?? {},
      style: p.style ?? {}
    })), o = jo(
      { nodes: s },
      ((c = e.allowedIds) == null ? void 0 : c.call(e)) ?? /* @__PURE__ */ new Set(),
      i
    );
    e.onPositions(o);
    const u = jo(
      { nodes: s },
      ((l = e.allowedIds) == null ? void 0 : l.call(e)) ?? /* @__PURE__ */ new Set()
    );
    (d = e.onAlign) == null || d.call(e, Oh(u));
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
      enable: (n) => n.targetType === "canvas" || Dt(e, n),
      range: 0.9
    },
    {
      type: "drag-element",
      state: fv,
      enable: (n) => (n.targetType === "node" || n.targetType === "combo") && !yu(e, n) && !Dt(e, n),
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
      state: hv,
      enable: (n) => Dt(e, n) ? (r.armed = !1, !1) : r.armed ? n.targetType === "node" || n.targetType === "combo" : yu(e, n) ? (r.armed = !0, !0) : !1,
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
const pv = (e) => {
  var t;
  const r = e;
  return (r == null ? void 0 : r.targetType) === "node" ? (t = r.target) == null ? void 0 : t.id : void 0;
};
function vv(e) {
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
  } = e, g = Te(null), h = Te(null), y = Te(!1), C = Te(!1), x = Te({ armed: !1 }), _ = Te(null), [A, L] = I(null), [m, k] = I([]), N = Te({ onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o });
  N.current = { onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o, onSelectEdge: l };
  const H = ht(
    () => cv(r, t, n),
    [r, t, n]
  ), G = Te(H);
  G.current = H;
  const S = Te(/* @__PURE__ */ new Set());
  S.current = new Set(r.nodes.map((z) => z.id)), _e(() => {
    const z = (fe) => {
      fe.code === "Space" && !fe.repeat && (C.current = !0);
    }, ie = (fe) => {
      fe.code === "Space" && (C.current = !1);
    }, ue = () => {
      C.current = !1;
    };
    return window.addEventListener("keydown", z), window.addEventListener("keyup", ie), window.addEventListener("blur", ue), () => {
      window.removeEventListener("keydown", z), window.removeEventListener("keyup", ie), window.removeEventListener("blur", ue);
    };
  }, []), _e(() => {
    const z = g.current;
    if (!z || y.current) return;
    let ie = !1;
    return import("./esm-H_YWdHHj.js").then(({ Graph: ue }) => {
      if (ie || !g.current || y.current) return;
      const fe = dv(G.current, z), B = new ue({
        ...fe,
        behaviors: Nh({
          graph: {
            getNodeData: () => {
              var J;
              return ((J = h.current) == null ? void 0 : J.getNodeData()) ?? [];
            },
            getCanvasByClient: (J) => {
              var T;
              const E = ((T = h.current) == null ? void 0 : T.getCanvasByClient(J)) ?? J;
              return [E[0], E[1]];
            }
          },
          isPanModifier: () => C.current,
          gestureOrigin: () => _.current,
          connectionState: x.current,
          allowedIds: () => S.current,
          onPositions: (J) => N.current.onPositions(J),
          onAlign: (J) => k((E) => J.length > 0 ? J : E),
          onSelectIds: (J) => u == null ? void 0 : u(J),
          onCreateEdge: (J, E) => N.current.onCreateEdge(J, E)
        })
      });
      B.on("node:click", (J) => {
        const E = pv(J);
        E && N.current.onSelect(E);
      }), B.on("edge:click", (J) => {
        var T, M, ve;
        const E = J;
        (ve = (M = N.current).onSelectEdge) == null || ve.call(M, ((T = E.target) == null ? void 0 : T.id) ?? null);
      }), B.on("node:contextmenu", (J) => {
        var T, M, ve;
        const E = J;
        (T = E.target) != null && T.id && L({
          x: ((M = E.client) == null ? void 0 : M.x) ?? 0,
          y: ((ve = E.client) == null ? void 0 : ve.y) ?? 0,
          type: "node",
          id: E.target.id
        });
      }), B.on("edge:contextmenu", (J) => {
        var T, M, ve;
        const E = J;
        (T = E.target) != null && T.id && L({
          x: ((M = E.client) == null ? void 0 : M.x) ?? 0,
          y: ((ve = E.client) == null ? void 0 : ve.y) ?? 0,
          type: "edge",
          id: E.target.id
        });
      }), B.on("canvas:click", () => N.current.onSelect(null)), B.on("pointerdown", (J) => {
        const E = J;
        _.current = E.client ? { x: E.client.x, y: E.client.y } : null;
      }), B.on("pointerup", () => {
        queueMicrotask(() => {
          x.current.armed = !1, _.current = null;
        });
      }), h.current = B, z.__industrialGraph = B, c == null || c(B), y.current = !0, B.render();
    }), () => {
      ie = !0;
    };
  }, []), _e(() => {
    const z = h.current;
    !z || !y.current || (z.setData(H), z.draw());
  }, [H]), _e(() => () => {
    var z;
    (z = h.current) == null || z.destroy(), h.current = null, y.current = !1;
  }, []);
  const j = () => {
    const z = h.current;
    if (!z) return;
    const ie = z.getNodeData().map((B) => ({
      id: String(B.id),
      data: B.data ?? {},
      style: B.style ?? {}
    })), ue = jo({ nodes: ie }, S.current);
    N.current.onPositions(ue);
    const fe = Oh(ue);
    k(fe.length > 0 ? fe : ["已对齐到网格"]);
  };
  return /* @__PURE__ */ f("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ R("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ f(
      "div",
      {
        ref: g,
        className: "industrial-graph-host",
        onDragOver: (z) => z.preventDefault(),
        onDrop: (z) => {
          z.preventDefault();
          const ie = z.dataTransfer.getData("application/x-industrial-node") || z.dataTransfer.getData("text/plain");
          if (!ie) return;
          const ue = h.current, fe = g.current;
          if (!ue || !fe) return;
          const B = fe.getBoundingClientRect(), J = ue.getCanvasByViewport([
            z.clientX - B.left,
            z.clientY - B.top
          ]);
          N.current.onDropNode(ie, J[0], J[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    A && /* @__PURE__ */ f("div", { className: "industrial-context-menu", style: { left: A.x, top: A.y }, children: A.type === "node" ? /* @__PURE__ */ R(dr, { children: [
      /* @__PURE__ */ f("button", { onClick: () => {
        p == null || p(A.id), L(null);
      }, children: "复制节点" }),
      /* @__PURE__ */ f("button", { onClick: () => {
        d == null || d(A.id), L(null);
      }, children: "删除节点" })
    ] }) : /* @__PURE__ */ R(dr, { children: [
      /* @__PURE__ */ f("button", { onClick: () => {
        l == null || l(A.id), L(null);
      }, children: "编辑连线" }),
      /* @__PURE__ */ f("button", { onClick: () => {
        v == null || v(A.id), L(null);
      }, children: "删除连线" })
    ] }) }),
    m.length > 0 && /* @__PURE__ */ R("div", { className: "industrial-align-guide", "data-testid": "align-guide", children: [
      "已对齐 ",
      m.join(" · ")
    ] }),
    /* @__PURE__ */ R("div", { className: "industrial-graph-hint", children: [
      "Shift+拖拽框选 · 拖节点移动 · 端口拖拽连线 · 滚轮缩放 · 空格/中键平移",
      /* @__PURE__ */ f("button", { type: "button", "data-testid": "designer-snap", onClick: j, children: "对齐到网格" })
    ] })
  ] }) });
}
function gv({ descriptors: e, loading: r, error: t, onAdd: n }) {
  const a = /* @__PURE__ */ new Map();
  for (const s of e) {
    const o = s.category.split(".")[0] || "General", u = a.get(o) ?? [];
    u.push(s), a.set(o, u);
  }
  const i = (s, o) => {
    s.dataTransfer.setData("application/x-industrial-node", o.nodeTypeId), s.dataTransfer.setData("text/plain", o.nodeTypeId), s.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ R("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ f(U.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    r && /* @__PURE__ */ f(kh, { size: "small", style: { display: "block", marginTop: 16 } }),
    t && /* @__PURE__ */ f(U.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: t }),
    !r && !t && e.length === 0 && /* @__PURE__ */ f(xh, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(a.entries()).map(([s, o]) => /* @__PURE__ */ R("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ f(U.Text, { strong: !0, style: { fontSize: 12 }, children: s }),
      o.map((u) => /* @__PURE__ */ R(
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
            /* @__PURE__ */ R("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ f(Le, { bordered: !1, color: "blue", children: u.version }),
              /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 11 }, children: u.nodeTypeId })
            ] }),
            /* @__PURE__ */ R("div", { className: "industrial-palette-node-ports", children: [
              u.inputs.map((c) => /* @__PURE__ */ R(Le, { bordered: !1, color: "default", children: [
                "in ",
                c.name,
                ": ",
                c.type
              ] }, `in-${c.name}`)),
              u.outputs.map((c) => /* @__PURE__ */ R(Le, { bordered: !1, color: "green", children: [
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
function yv(e) {
  const r = {};
  for (const [t, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (r[t] = n.default);
  return r;
}
function Mt(e, r, t = 0, n = 0) {
  return { id: r, type: e.nodeTypeId, config: yv(e), x: t, y: n };
}
function tr(e, r) {
  return e.get(r);
}
function ot(e, r, t) {
  return e[t].find((n) => n.name === r);
}
function Ko(e, r) {
  return e !== "Unknown" && r !== "Unknown" && e === r;
}
function mu(e, r) {
  for (const t of e.outputs)
    for (const n of r.inputs)
      if (Ko(t.type, n.type))
        return { sourcePort: t.name, targetPort: n.name };
  return null;
}
const ke = (e, r, t) => ({ path: e, code: r, message: t });
function mv(e, r) {
  return e.source === r.source && e.sourcePort === r.sourcePort && e.target === r.target && e.targetPort === r.targetPort;
}
function bv(e, r, t) {
  const n = `edges.${e.edges.length}`, a = e.nodes.find((l) => l.id === r.source), i = e.nodes.find((l) => l.id === r.target);
  if (!a || !i)
    return { definition: e, issue: ke(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((l) => mv(l, r)))
    return { definition: e };
  const s = tr(t, a.type), o = tr(t, i.type);
  if (!s || !o)
    return { definition: e, issue: ke(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const u = ot(s, r.sourcePort, "outputs"), c = ot(o, r.targetPort, "inputs");
  return !u || !c ? { definition: e, issue: ke(n, "PORT_MISSING", "declared port does not exist") } : Ko(u.type, c.type) ? { definition: { ...e, edges: [...e.edges, r] } } : {
    definition: e,
    issue: ke(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${u.type} → ${c.type} is not a legal connection`
    )
  };
}
function Lt(e, r) {
  return {
    ...e,
    nodes: e.nodes.filter((t) => t.id !== r),
    edges: e.edges.filter((t) => t.source !== r && t.target !== r)
  };
}
function bu(e, r) {
  return { ...e, edges: e.edges.filter((t, n) => n !== r) };
}
function _v(e, r) {
  const t = { ...e };
  for (const [n, a] of Object.entries(r))
    a === void 0 ? delete t[n] : t[n] = a;
  return t;
}
function wv(e, r, t) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === r ? { ...n, config: t } : n)
  };
}
function Iv(e, r) {
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
function qv(e) {
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
function _u(e, r, t) {
  const n = [];
  e.workflowId.trim() || n.push(ke("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(ke("workflow", "INVALID_VERSION", "version must be positive"));
  const a = /* @__PURE__ */ new Map();
  for (const o of e.nodes)
    a.has(o.id) && n.push(ke(`nodes.${o.id}`, "DUPLICATE_NODE_ID", `duplicate node '${o.id}'`)), a.set(o.id, o);
  const i = /* @__PURE__ */ new Map();
  for (const o of e.nodes) {
    const u = tr(r, o.type);
    if (!u) {
      n.push(ke(
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
      (d == null || d === "") && n.push(ke(
        `nodes.${o.id}.config.${l}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${l}' is missing`
      ));
    }
    t && typeof o.config.resourceId == "string" && (t.has(o.config.resourceId) || n.push(ke(
      `nodes.${o.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${o.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((o, u) => {
    const c = a.get(o.source), l = a.get(o.target);
    if (!c || !l) {
      n.push(ke(`edges.${u}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const d = i.get(o.source), p = i.get(o.target);
    if (!d || !p) return;
    const v = ot(d, o.sourcePort, "outputs"), g = ot(p, o.targetPort, "inputs");
    if (!v || !g) {
      n.push(ke(`edges.${u}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    Ko(v.type, g.type) || n.push(ke(
      `edges.${u}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${v.type} → ${g.type} is not a legal connection`
    ));
  });
  for (const o of e.nodes) {
    const u = i.get(o.id);
    if (u)
      for (const c of u.inputs.filter((l) => l.required))
        e.edges.some((l) => l.target === o.id && l.targetPort === c.name && a.has(l.source)) || n.push(ke(
          `nodes.${o.id}.inputs.${c.name}`,
          "MISSING_INPUT",
          `required input '${c.name}' is not connected`
        ));
  }
  const s = qv(e);
  return s && n.push(ke(`nodes.${s[0]}`, "GRAPH_CYCLE", `cycle: ${s.join(" → ")}`)), n;
}
const Fo = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function Cv(e) {
  const r = Fo(e.additionalProperties).enum;
  if (!Array.isArray(r)) return null;
  const t = r.filter((n) => typeof n == "string");
  return t.length > 0 ? t : null;
}
function Dh(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && Fo(e.items).type === "string" ? "string-list" : e.type === "object" && Fo(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function Go(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, r]) => r != null && typeof r != "object").map(([r, t]) => ({ key: r, value: String(t) }));
}
function wu(e) {
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
function Ev(e, r) {
  const t = /* @__PURE__ */ new Set();
  for (const [n, a] of Object.entries(e))
    if (Dh(a) === "key-value")
      for (const i of Go(r[n]))
        i.key.trim() && t.add(i.key.trim());
  return [...t];
}
function Sv({
  value: e,
  onChange: r,
  valueOptions: t,
  keyTitle: n = "目标字段",
  valueTitle: a = "来源字段",
  testId: i
}) {
  const s = JSON.stringify(e ?? {}), [o, u] = I(() => Go(e)), [c, l] = I(s);
  _e(() => {
    s !== c && (u(Go(e)), l(s));
  }, [s, c, e]);
  const d = (v) => {
    u(v), r == null || r(wu(v).record);
  }, { duplicateKeys: p } = wu(o);
  return /* @__PURE__ */ R("div", { "data-testid": i, className: "industrial-kv-editor", children: [
    o.map((v, g) => /* @__PURE__ */ R("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ f(
        Q,
        {
          "aria-label": `${n} ${g + 1}`,
          value: v.key,
          placeholder: n,
          onChange: (h) => d(o.map((y, C) => C === g ? { ...y, key: h.target.value } : y))
        }
      ),
      t && t.length > 0 ? /* @__PURE__ */ f(
        je,
        {
          "aria-label": `${a} ${g + 1}`,
          value: v.value || void 0,
          placeholder: a,
          options: t.map((h) => ({ label: h, value: h })),
          onChange: (h) => d(o.map((y, C) => C === g ? { ...y, value: h ?? "" } : y))
        }
      ) : /* @__PURE__ */ f(
        Q,
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
    p.length > 0 && /* @__PURE__ */ R(U.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      p.join("、")
    ] })
  ] });
}
function Rv({ value: e, onChange: r, suggestions: t = [], testId: n }) {
  const a = JSON.stringify(rt(e)), [i, s] = I(() => rt(e)), [o, u] = I(a);
  _e(() => {
    a !== o && (s(rt(e)), u(a));
  }, [a, o, e]);
  const c = (l) => {
    s(l), r == null || r(rt(l));
  };
  return /* @__PURE__ */ R("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ f("datalist", { id: `${n ?? "list"}-suggestions`, children: t.map((l) => /* @__PURE__ */ f("option", { value: l }, l)) }),
    i.map((l, d) => /* @__PURE__ */ R("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ f(
        Q,
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
function Tv({ value: e, onChange: r }) {
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
  return /* @__PURE__ */ R("div", { children: [
    /* @__PURE__ */ f(Q.TextArea, { rows: 5, value: n, onChange: (u) => o(u.target.value) }),
    i && /* @__PURE__ */ f(U.Text, { type: "danger", style: { fontSize: 11 }, children: i })
  ] });
}
function xv({ value: e, onChange: r, resources: t, resourceType: n, label: a, onCreate: i }) {
  const s = t.filter((u) => u.enabled !== !1 && (!n || u.type === n)).map((u) => ({
    value: u.id,
    label: `${u.name} (${u.type})`
  })), o = t.find((u) => u.id === e);
  return /* @__PURE__ */ R("div", { children: [
    /* @__PURE__ */ R(
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
    o && /* @__PURE__ */ R(
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
function kv({ name: e, schema: r, required: t, resources: n, resourceType: a, properties: i, config: s, onCreateResource: o }) {
  let u, c = "value";
  switch (Dh(r)) {
    case "resource":
      u = /* @__PURE__ */ f(
        xv,
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
        je,
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
        Sv,
        {
          testId: `field-${e}`,
          valueOptions: Cv(r) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      u = /* @__PURE__ */ f(Rv, { testId: `field-${e}`, suggestions: Ev(i, s) });
      break;
    case "json":
      u = /* @__PURE__ */ f(Tv, {});
      break;
    default:
      u = /* @__PURE__ */ f(Q, { placeholder: r.description });
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
function Av(e) {
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
  } = e, [C] = $o.useForm(), [x, _] = I("{}"), [A, L] = I(null), [m, k] = I(), [N, H] = I(), [G, S] = I(!1), [j, V] = I(!1);
  _e(() => {
    C.setFieldsValue((r == null ? void 0 : r.config) ?? {}), _(JSON.stringify((r == null ? void 0 : r.config) ?? {}, null, 2)), L(null);
  }, [C, r == null ? void 0 : r.id, r == null ? void 0 : r.config]);
  const z = new Set((t == null ? void 0 : t.configurationSchema.required) ?? []), ie = Object.entries((t == null ? void 0 : t.configurationSchema.properties) ?? {}), ue = () => {
    if (ie.length > 0) {
      C.validateFields().then((E) => u(_v((r == null ? void 0 : r.config) ?? {}, E)));
      return;
    }
    try {
      const E = JSON.parse(x);
      if (E === null || typeof E != "object" || Array.isArray(E))
        throw new Error("配置必须是 JSON 对象");
      L(null), u(E);
    } catch (E) {
      L(E.message);
    }
  }, B = (r && !G ? a.map((E, T) => ({ edge: E, index: T })).filter(({ edge: E }) => E.source === r.id || E.target === r.id) : a.map((E, T) => ({ edge: E, index: T }))).map(({ edge: E, index: T }) => ({
    ...E,
    index: T,
    key: `${T}:${E.source}->${E.target}`
  })), J = [
    {
      title: "起点节点",
      key: "reconnect-source",
      width: 108,
      render: (E, T) => /* @__PURE__ */ f(
        "select",
        {
          "aria-label": `连线 ${T.index} 起点节点`,
          value: T.source,
          onChange: (M) => p(T.index, { ...T, source: M.target.value }),
          children: n.map((M) => /* @__PURE__ */ f("option", { value: M.id, children: M.id }, M.id))
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
          onChange: (M) => p(T.index, { ...T, target: M.target.value }),
          children: n.map((M) => /* @__PURE__ */ f("option", { value: M.id, children: M.id }, M.id))
        }
      )
    },
    {
      title: "端口",
      key: "ports",
      render: (E, T) => {
        const M = i.get(T.source), ve = i.get(T.target);
        return /* @__PURE__ */ R("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ f(
            je,
            {
              size: "small",
              value: T.sourcePort,
              style: { width: 110 },
              onChange: (we) => p(T.index, { ...T, sourcePort: we }),
              options: ((M == null ? void 0 : M.outputs) ?? []).map((we) => ({ label: `${we.name} (${we.type})`, value: we.name }))
            }
          ),
          /* @__PURE__ */ f(
            je,
            {
              size: "small",
              value: T.targetPort,
              style: { width: 110 },
              onChange: (we) => p(T.index, { ...T, targetPort: we }),
              options: ((ve == null ? void 0 : ve.inputs) ?? []).map((we) => ({ label: `${we.name} (${we.type})`, value: we.name }))
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
  return /* @__PURE__ */ R("div", { className: "industrial-properties-body", children: [
    !r && /* @__PURE__ */ f(xh, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    r && /* @__PURE__ */ R(dr, { children: [
      /* @__PURE__ */ f(U.Title, { level: 5, style: { margin: 0 }, children: (t == null ? void 0 : t.name) ?? r.type }),
      /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 11 }, children: r.id }),
      /* @__PURE__ */ f(U.Paragraph, { type: "secondary", style: { fontSize: 12, marginTop: 6 }, children: (t == null ? void 0 : t.description) ?? "请按业务含义配置该节点。" }),
      /* @__PURE__ */ R(Y, { wrap: !0, size: 4, style: { marginBottom: 8 }, children: [
        /* @__PURE__ */ f(Le, { color: "blue", children: (t == null ? void 0 : t.category) ?? "采集节点" }),
        /* @__PURE__ */ R(Le, { children: [
          "版本 ",
          (t == null ? void 0 : t.version) ?? "1.0.0"
        ] }),
        t == null ? void 0 : t.inputs.map((E) => /* @__PURE__ */ R(Le, { color: E.required ? "red" : "default", children: [
          "输入 ",
          E.name,
          " · ",
          E.type
        ] }, `in-${E.name}`)),
        t == null ? void 0 : t.outputs.map((E) => /* @__PURE__ */ R(Le, { color: "green", children: [
          "输出 ",
          E.name,
          " · ",
          E.type
        ] }, `out-${E.name}`))
      ] }),
      (r.type.includes("reader") || r.type.includes("mapping") || r.type.includes("source") || r.type.includes("filesystem")) && y && y.length > 0 && /* @__PURE__ */ R(dr, { children: [
        /* @__PURE__ */ f(U.Text, { strong: !0, style: { fontSize: 12 }, children: "样本预览" }),
        /* @__PURE__ */ f("pre", { className: "industrial-sample-preview", children: y.join(`
`) })
      ] }),
      /* @__PURE__ */ f($o, { form: C, layout: "vertical", size: "small", style: { marginTop: 12 }, children: ie.map(([E, T]) => /* @__PURE__ */ f(
        kv,
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
      ie.length === 0 && /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 12 }, children: "此节点无需填写业务参数；保留默认配置即可。" }),
      /* @__PURE__ */ f($, { type: "link", size: "small", onClick: () => V((E) => !E), children: j ? "收起高级配置" : "高级配置" }),
      j && /* @__PURE__ */ R(dr, { children: [
        /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 12 }, children: "高级配置直接编辑节点 JSON，仅在需要复杂表达式时使用。" }),
        /* @__PURE__ */ f(
          Q.TextArea,
          {
            rows: 8,
            value: x,
            style: { marginTop: 6 },
            onChange: (E) => _(E.target.value)
          }
        ),
        A && /* @__PURE__ */ f(U.Text, { type: "danger", style: { fontSize: 12 }, children: A }),
        (t == null ? void 0 : t.activityType) && /* @__PURE__ */ R(U.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
          "运行时 Activity：",
          t.activityType
        ] })
      ] }),
      /* @__PURE__ */ f($, { type: "primary", block: !0, onClick: ue, children: "应用配置" }),
      /* @__PURE__ */ f($, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: c, children: "删除节点" })
    ] }),
    h && (() => {
      const E = a.findIndex((M) => `e:${M.source}:${M.sourcePort}>${M.target}:${M.targetPort}` === h);
      if (E < 0) return null;
      const T = a[E];
      return /* @__PURE__ */ f(
        Ee,
        {
          type: "info",
          showIcon: !0,
          style: { marginTop: 8 },
          message: `当前连线：${T.source}.${T.sourcePort} → ${T.target}.${T.targetPort}`,
          description: /* @__PURE__ */ f($, { size: "small", danger: !0, onClick: () => l(E), children: "删除此连线" })
        }
      );
    })(),
    /* @__PURE__ */ R(pu, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "相关连线 (",
      B.length,
      " / ",
      a.length,
      ")"
    ] }),
    r && /* @__PURE__ */ f($, { type: "link", size: "small", onClick: () => S((E) => !E), children: G ? "仅看当前节点" : "显示全部连线" }),
    n.length > 1 && /* @__PURE__ */ R(Y, { wrap: !0, style: { marginBottom: 8 }, children: [
      /* @__PURE__ */ R(
        "select",
        {
          "aria-label": "连线起点",
          value: m ?? "",
          onChange: (E) => k(E.target.value || void 0),
          children: [
            /* @__PURE__ */ f("option", { value: "", children: "起点节点" }),
            n.map((E) => /* @__PURE__ */ f("option", { value: E.id, children: E.id }, E.id))
          ]
        }
      ),
      /* @__PURE__ */ R(
        "select",
        {
          "aria-label": "连线终点",
          value: N ?? "",
          onChange: (E) => H(E.target.value || void 0),
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
          disabled: !m || !N || m === N,
          onClick: () => {
            m && N && d(m, N);
          },
          children: "添加连线"
        }
      )
    ] }),
    a.length === 0 ? /* @__PURE__ */ f(U.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ f(Ae, { size: "small", columns: J, dataSource: B, pagination: !1 }),
    o.length > 0 && /* @__PURE__ */ R(dr, { children: [
      /* @__PURE__ */ R(pu, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        o.length,
        ")"
      ] }),
      o.map((E, T) => /* @__PURE__ */ R("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ f(Le, { color: "red", children: E.code }),
        /* @__PURE__ */ R(
          U.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const M = /^nodes\.([^.]+)/.exec(E.path);
              M && v(M[1]);
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
const Pv = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class Ov extends Error {
  constructor(r) {
    super(r), this.name = "DtoError";
  }
}
const ae = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, Ie = (e) => Array.isArray(e) ? e : [], O = (e, r = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : r ?? "", me = (e, r = 0) => {
  const t = typeof e == "string" ? Number(e) : e;
  return typeof t == "number" && Number.isFinite(t) ? t : r;
}, b = (e, ...r) => {
  for (const t of r)
    if (e[t] !== void 0 && e[t] !== null) return e[t];
};
function xe(e) {
  const r = ae(e);
  if (Object.prototype.hasOwnProperty.call(r, "data")) return r.data;
  if (typeof r.code == "string" && typeof r.message == "string")
    throw new Ov(`${r.code}: ${r.message}`);
  return e;
}
function Nv(e) {
  const r = O(e).trim().toLowerCase();
  return Pv.find((n) => n.toLowerCase() === r) ?? "Unknown";
}
function Dv(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const r = ae(e), t = O(b(r, "name", "port", "id")).trim();
  return t ? {
    name: t,
    type: Nv(b(r, "type", "portType", "dataType")),
    required: b(r, "required", "isRequired") === !0
  } : null;
}
function Iu(e) {
  const r = Ie(e).map(Dv).filter((n) => n !== null), t = /* @__PURE__ */ new Set();
  return r.filter((n) => t.has(n.name) ? !1 : (t.add(n.name), !0));
}
const Mv = (e) => {
  const r = ae(e);
  return Object.keys(r).length === 0 ? { type: "object", properties: {} } : r;
};
function Lv(e) {
  const r = ae(e), t = O(b(r, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return t ? {
    nodeTypeId: t,
    name: O(b(r, "name", "title"), t),
    description: O(b(r, "description", "desc")),
    category: O(b(r, "category", "group"), "General"),
    icon: O(b(r, "icon"), "AppstoreOutlined"),
    version: O(b(r, "version"), "1.0.0"),
    inputs: Iu(b(r, "inputs", "inputPorts", "input_ports")),
    outputs: Iu(b(r, "outputs", "outputPorts", "output_ports")),
    configurationSchema: Mv(b(r, "configurationSchema", "configSchema", "schema")),
    activityType: O(b(r, "activityType", "activity_type")),
    dependencies: Ie(b(r, "dependencies")).map((n) => O(n)).filter(Boolean)
  } : null;
}
function Mh(e) {
  const r = xe(e), t = Array.isArray(r) ? r : Ie(b(ae(r), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return t.map(Lv).filter((a) => a !== null).filter((a) => n.has(a.nodeTypeId) ? !1 : (n.add(a.nodeTypeId), !0));
}
function Lh(e) {
  const r = xe(e), t = Array.isArray(r) ? r : Ie(b(ae(r), "resources", "items")), n = /* @__PURE__ */ new Set();
  return t.map((a) => {
    const i = ae(a), s = O(b(i, "id")).trim();
    return !s || n.has(s) ? null : (n.add(s), {
      id: s,
      name: O(b(i, "name"), s),
      type: O(b(i, "type"), "unknown"),
      enabled: i.enabled !== !1,
      credentialId: typeof i.credentialId == "string" ? i.credentialId : null,
      credentialName: typeof i.credentialName == "string" ? i.credentialName : null
    });
  }).filter((a) => a !== null);
}
function $h(e) {
  const r = ae(e), t = O(b(r, "workflowId", "workflow_id", "id")).trim();
  return t ? {
    workflowId: t,
    name: O(b(r, "name", "title"), t),
    version: me(b(r, "version"), 1)
  } : null;
}
function jh(e) {
  const r = xe(e);
  return (Array.isArray(r) ? r : Ie(b(ae(r), "workflows", "items"))).map($h).filter((n) => n !== null);
}
function ut(e) {
  const r = ae(e), t = {};
  for (const [n, a] of Object.entries(r))
    a !== void 0 && (t[n] = a);
  return t;
}
function $v(e) {
  const r = ae(e), t = O(b(r, "id", "nodeId", "node_id")).trim();
  if (!t) return null;
  const n = ae(b(r, "position", "layout"));
  return {
    id: t,
    type: O(b(r, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: ut(b(r, "config", "configuration")),
    x: me(b(r, "x") ?? b(n, "x"), 0),
    y: me(b(r, "y") ?? b(n, "y"), 0)
  };
}
function jv(e) {
  const r = ae(e), t = O(b(r, "source", "from", "sourceId")).trim(), n = O(b(r, "target", "to", "targetId")).trim();
  return !t || !n ? null : {
    source: t,
    sourcePort: O(b(r, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: O(b(r, "targetPort", "target_port", "toPort"), "in")
  };
}
function Fv(e) {
  const r = xe(e), t = ae(r), n = $h(t) ?? { workflowId: "", name: "", version: 1 }, a = Ie(b(t, "nodes")).map($v).filter((u) => u !== null), i = Ie(b(t, "edges")).map(jv).filter((u) => u !== null), s = ae(b(t, "trigger")), o = {
    kind: O(b(s, "kind", "type"), "manual"),
    cron: b(s, "cron") === void 0 ? null : O(s.cron),
    eventName: b(s, "eventName", "event_name") === void 0 ? null : O(b(s, "eventName", "event_name")),
    config: ut(b(s, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: a,
    edges: i,
    trigger: o,
    resourceReferences: Ie(b(t, "resourceReferences", "resource_references")).map((u) => O(u)).filter(Boolean),
    layout: ut(b(t, "layout"))
  };
}
const Ar = (e) => e === !0 || typeof e == "string" && e.toLowerCase() === "true";
function Gv(e) {
  const r = xe(e);
  return (Array.isArray(r) ? r : Ie(b(ae(r), "resources", "items"))).map((n) => {
    const a = ae(n), i = b(a, "credentialId", "credential_id");
    return {
      id: O(b(a, "id"), "—"),
      name: O(b(a, "name"), "—"),
      type: O(b(a, "type"), "—"),
      provider: O(b(a, "provider"), "—"),
      enabled: Ar(b(a, "enabled")),
      credentialId: i == null ? null : O(i),
      credentialName: b(a, "credentialName") == null ? null : O(b(a, "credentialName")),
      config: ut(b(a, "config", "configuration"))
    };
  });
}
function Fh(e) {
  const r = xe(e);
  return (Array.isArray(r) ? r : Ie(b(ae(r), "credentials", "items"))).map((n) => {
    const a = ae(n);
    return {
      id: O(b(a, "id"), "—"),
      name: O(b(a, "name"), "—"),
      kind: O(b(a, "kind"), "—"),
      version: me(b(a, "version"))
    };
  });
}
function zv(e) {
  const r = xe(e);
  return (Array.isArray(r) ? r : Ie(b(ae(r), "tests", "items"))).map((n) => {
    const a = ae(n);
    return {
      resourceId: O(b(a, "resourceId", "resource_id"), "—"),
      ok: Ar(b(a, "ok")),
      detail: O(b(a, "detail"), "")
    };
  });
}
const Gh = (e) => {
  if (e == null) return null;
  const r = ae(e);
  return Object.keys(r).length === 0 ? null : {
    code: O(b(r, "code"), "OTHER"),
    message: O(b(r, "message"), ""),
    action: O(b(r, "action"), "")
  };
};
function Bv(e) {
  const r = xe(e);
  return Ie(b(ae(r), "triggers", "items")).map((n) => {
    const a = ae(n), i = (s) => b(a, s) == null ? null : O(b(a, s));
    return {
      workflowId: O(b(a, "workflowId", "workflow_id"), "—"),
      version: me(b(a, "version")),
      kind: O(b(a, "kind"), "manual"),
      cron: i("cron"),
      eventName: i("eventName"),
      timezone: i("timezone"),
      resourceId: i("resourceId"),
      nextRun: i("nextRun"),
      lastStatus: i("lastStatus"),
      lastStartedAt: i("lastStartedAt"),
      lastFireStatus: i("lastFireStatus"),
      lastFireDueAt: i("lastFireDueAt"),
      lastFireAttempts: b(a, "lastFireAttempts") == null ? null : me(b(a, "lastFireAttempts")),
      skippedFires: b(a, "skippedFires") == null ? null : me(b(a, "skippedFires")),
      backlogTruncated: b(a, "backlogTruncated") == null ? null : b(a, "backlogTruncated") === !0,
      lastError: i("lastError")
    };
  });
}
function Uv(e) {
  if (e == null) return null;
  const r = xe(e), t = ae(r);
  if (Object.keys(t).length === 0) return null;
  const n = Ie(b(t, "files")).map((i) => {
    const s = ae(i);
    return {
      path: O(b(s, "path"), "—"),
      sourceDate: O(b(s, "sourceDate"), ""),
      inLedger: Ar(b(s, "inLedger")),
      expectedTargets: me(b(s, "expectedTargets")),
      succeededTargets: me(b(s, "succeededTargets")),
      incompleteTargets: me(b(s, "incompleteTargets")),
      missingTargets: O(b(s, "missingTargets"), ""),
      wouldSkip: Ar(b(s, "wouldSkip")),
      wouldCollect: Ar(b(s, "wouldCollect")),
      duplicateRisk: Ar(b(s, "duplicateRisk"))
    };
  }), a = ae(b(t, "summary"));
  return {
    resourceId: O(b(t, "resourceId"), "—"),
    from: O(b(t, "from"), ""),
    to: O(b(t, "to"), ""),
    files: n,
    summary: {
      discovered: me(b(a, "discovered")),
      alreadyComplete: me(b(a, "alreadyComplete")),
      needsCollection: me(b(a, "needsCollection")),
      duplicateRisk: me(b(a, "duplicateRisk"))
    }
  };
}
function Kv(e) {
  const r = xe(e);
  return (Array.isArray(r) ? r : Ie(b(ae(r), "files", "items"))).map((n) => {
    const a = ae(n);
    return {
      id: O(b(a, "id"), "—"),
      path: O(b(a, "path"), "—"),
      identityKey: O(b(a, "identityKey"), ""),
      sourceResourceId: O(b(a, "sourceResourceId"), ""),
      length: me(b(a, "length")),
      sourceDate: O(b(a, "sourceDate"), ""),
      collectedAt: O(b(a, "collectedAt"), ""),
      status: O(b(a, "status"), "—"),
      deliveries: Ie(b(a, "deliveries")).map((i) => {
        const s = ae(i);
        return {
          id: O(b(s, "id"), "—"),
          collectionFileId: O(b(s, "collectionFileId"), ""),
          targetId: O(b(s, "targetId"), "—"),
          targetKind: O(b(s, "targetKind"), "record"),
          status: O(b(s, "status"), "—"),
          attempts: me(b(s, "attempts")),
          records: me(b(s, "records")),
          error: b(s, "error") == null ? null : O(b(s, "error")),
          accepted: me(b(s, "accepted")),
          rejected: me(b(s, "rejected")),
          warnings: me(b(s, "warnings")),
          diagnosis: Gh(b(s, "diagnosis"))
        };
      })
    };
  });
}
function Vv(e) {
  const r = xe(e), t = ae(r), n = Ie(b(t, "runs")).map((i) => {
    const s = ae(i);
    return {
      id: O(b(s, "id"), "—"),
      workflowId: O(b(s, "workflowId", "workflow_id"), "—"),
      status: O(b(s, "status"), "—"),
      startedAt: O(b(s, "startedAt", "started_at"), ""),
      finishedAt: b(s, "finishedAt", "finished_at") == null ? null : O(b(s, "finishedAt", "finished_at")),
      error: b(s, "error") == null ? null : O(b(s, "error"))
    };
  }), a = Ie(b(t, "deliveries")).map((i) => {
    const s = ae(i);
    return {
      id: O(b(s, "id"), "—"),
      collectionFileId: O(b(s, "collectionFileId", "collection_file_id"), ""),
      targetId: O(b(s, "targetId", "target_id"), "—"),
      targetKind: O(b(s, "targetKind", "target_kind"), "record"),
      status: O(b(s, "status"), "—"),
      attempts: me(b(s, "attempts")),
      records: me(b(s, "records")),
      error: b(s, "error") == null ? null : O(b(s, "error")),
      diagnosis: Gh(b(s, "diagnosis"))
    };
  });
  return { runs: n, deliveries: a };
}
function Hv(e) {
  if (e == null) return null;
  const r = xe(e), t = ae(r);
  if (Object.keys(t).length === 0) return null;
  const n = Ie(b(t, "nodes")).slice(0, 64).map((a) => {
    const i = ae(a);
    return {
      nodeId: O(b(i, "nodeId", "node_id"), "—"),
      status: O(b(i, "status"), "—"),
      records: me(b(i, "records")),
      files: me(b(i, "files")),
      error: b(i, "error") == null ? null : O(b(i, "error"), void 0),
      samples: Ie(b(i, "samples")).slice(0, 3).map((s) => O(s))
    };
  });
  return {
    status: O(b(t, "status"), "—"),
    error: b(t, "error") == null ? null : O(b(t, "error"), void 0),
    executed: Ie(b(t, "executed")).map((a) => O(a)),
    instanceId: b(t, "instanceId") == null ? null : O(b(t, "instanceId"), void 0),
    definitionId: b(t, "definitionId") == null ? null : O(b(t, "definitionId"), void 0),
    nodes: n
  };
}
const Wv = [
  { value: "manual", label: "手动运行" },
  { value: "schedule", label: "定时运行" },
  { value: "event", label: "事件触发" }
], Qv = [
  { label: "每天 02:00", cron: "0 2 * * *" },
  { label: "每小时", cron: "0 * * * *" },
  { label: "每 5 分钟", cron: "*/5 * * * *" },
  { label: "工作日 08:30", cron: "30 8 * * 1-5" }
];
function Yv({
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
    e && i.hubQuery("industrial/trigger-status").then((h) => l(Bv(h))).catch((h) => p(h instanceof Error ? h.message : String(h)));
  }, [i, e]);
  const v = c.find((h) => h.workflowId === s);
  return /* @__PURE__ */ f(
    br,
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
      children: /* @__PURE__ */ R(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ R(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(U.Text, { children: "触发方式" }),
          /* @__PURE__ */ f(Y, { wrap: !0, children: Wv.map((h) => /* @__PURE__ */ f(
            $,
            {
              type: o.kind === h.value ? "primary" : "default",
              onClick: () => u({ ...o, kind: h.value }),
              children: h.label
            },
            h.value
          )) })
        ] }),
        /* @__PURE__ */ f(Y, { wrap: !0, children: /* @__PURE__ */ f(
          je,
          {
            "aria-label": "运行资源",
            allowClear: !0,
            placeholder: "运行资源",
            value: Jv(o),
            options: n.map((h) => ({ value: h, label: h })),
            onChange: (h) => u({
              ...o,
              config: { ...o.config, resourceId: h ?? "" }
            })
          }
        ) }),
        /* @__PURE__ */ R(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(U.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ R(Y, { wrap: !0, children: [
            /* @__PURE__ */ f(U.Text, { children: "常用" }),
            Qv.map((h) => /* @__PURE__ */ f(
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
            Q,
            {
              "aria-label": "Cron 表达式",
              placeholder: "分 时 日 月 周，例如 0 2 * * *",
              value: o.cron ?? "",
              onChange: (h) => u({ ...o, cron: h.target.value })
            }
          ),
          /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "调度时区",
              placeholder: "时区（默认 UTC，例如 Asia/Shanghai）",
              value: Xv(o),
              onChange: (h) => u({
                ...o,
                config: { ...o.config, timezone: h.target.value }
              })
            }
          ),
          /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "事件名称",
              placeholder: "事件名称",
              value: o.eventName ?? "",
              onChange: (h) => u({ ...o, eventName: h.target.value })
            }
          ),
          /* @__PURE__ */ R(U.Text, { type: "secondary", children: [
            "下次运行：",
            (v == null ? void 0 : v.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        v && /* @__PURE__ */ f(
          Ae,
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
        d && /* @__PURE__ */ f(Ee, { type: "error", showIcon: !0, message: d })
      ] })
    }
  );
}
function Jv(e) {
  const r = e.config.resourceId;
  return typeof r == "string" && r.trim() ? r : void 0;
}
function Xv(e) {
  const r = e.config.timezone;
  return typeof r == "string" && r.trim() ? r : "UTC";
}
function Zv({
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
      }), o(Uv(await t.hubQuery("industrial/backfill-preview")));
    } catch (v) {
      c(v instanceof Error ? v.message : String(v));
    } finally {
      d(!1);
    }
  }, [t, i, a, n]);
  return /* @__PURE__ */ f(
    br,
    {
      open: e,
      title: "补采预览",
      onCancel: r,
      width: 820,
      footer: [
        /* @__PURE__ */ f($, { loading: l, onClick: () => void p(), children: "重新扫描" }, "refresh"),
        /* @__PURE__ */ f($, { type: "primary", onClick: r, children: "关闭" }, "close")
      ],
      children: /* @__PURE__ */ R(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ R(Y, { children: [
          /* @__PURE__ */ R($, { type: "primary", loading: l, onClick: () => void p(), children: [
            "扫描最近 ",
            i,
            " 天"
          ] }),
          /* @__PURE__ */ R(U.Text, { type: "secondary", children: [
            "资源：",
            a || "未选择"
          ] })
        ] }),
        u && /* @__PURE__ */ f(Ee, { type: "error", showIcon: !0, message: u }),
        s && /* @__PURE__ */ R(dr, { children: [
          /* @__PURE__ */ R(U.Text, { children: [
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
            Ae,
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
function eg(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
function Vo(e) {
  throw new Error('Could not dynamically require "' + e + '". Please configure the dynamicRequireTargets or/and ignoreDynamicRequires option of @rollup/plugin-commonjs appropriately for this require call to work.');
}
var $t, qu;
function rg() {
  if (qu) return $t;
  qu = 1;
  function e() {
    this.__data__ = [], this.size = 0;
  }
  return $t = e, $t;
}
var jt, Cu;
function Pr() {
  if (Cu) return jt;
  Cu = 1;
  function e(r, t) {
    return r === t || r !== r && t !== t;
  }
  return jt = e, jt;
}
var Ft, Eu;
function vt() {
  if (Eu) return Ft;
  Eu = 1;
  var e = Pr();
  function r(t, n) {
    for (var a = t.length; a--; )
      if (e(t[a][0], n))
        return a;
    return -1;
  }
  return Ft = r, Ft;
}
var Gt, Su;
function tg() {
  if (Su) return Gt;
  Su = 1;
  var e = vt(), r = Array.prototype, t = r.splice;
  function n(a) {
    var i = this.__data__, s = e(i, a);
    if (s < 0)
      return !1;
    var o = i.length - 1;
    return s == o ? i.pop() : t.call(i, s, 1), --this.size, !0;
  }
  return Gt = n, Gt;
}
var zt, Ru;
function ng() {
  if (Ru) return zt;
  Ru = 1;
  var e = vt();
  function r(t) {
    var n = this.__data__, a = e(n, t);
    return a < 0 ? void 0 : n[a][1];
  }
  return zt = r, zt;
}
var Bt, Tu;
function ag() {
  if (Tu) return Bt;
  Tu = 1;
  var e = vt();
  function r(t) {
    return e(this.__data__, t) > -1;
  }
  return Bt = r, Bt;
}
var Ut, xu;
function ig() {
  if (xu) return Ut;
  xu = 1;
  var e = vt();
  function r(t, n) {
    var a = this.__data__, i = e(a, t);
    return i < 0 ? (++this.size, a.push([t, n])) : a[i][1] = n, this;
  }
  return Ut = r, Ut;
}
var Kt, ku;
function gt() {
  if (ku) return Kt;
  ku = 1;
  var e = rg(), r = tg(), t = ng(), n = ag(), a = ig();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, Kt = i, Kt;
}
var Vt, Au;
function sg() {
  if (Au) return Vt;
  Au = 1;
  var e = gt();
  function r() {
    this.__data__ = new e(), this.size = 0;
  }
  return Vt = r, Vt;
}
var Ht, Pu;
function og() {
  if (Pu) return Ht;
  Pu = 1;
  function e(r) {
    var t = this.__data__, n = t.delete(r);
    return this.size = t.size, n;
  }
  return Ht = e, Ht;
}
var Wt, Ou;
function ug() {
  if (Ou) return Wt;
  Ou = 1;
  function e(r) {
    return this.__data__.get(r);
  }
  return Wt = e, Wt;
}
var Qt, Nu;
function lg() {
  if (Nu) return Qt;
  Nu = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return Qt = e, Qt;
}
var Yt, Du;
function zh() {
  if (Du) return Yt;
  Du = 1;
  var e = typeof tt == "object" && tt && tt.Object === Object && tt;
  return Yt = e, Yt;
}
var Jt, Mu;
function Ve() {
  if (Mu) return Jt;
  Mu = 1;
  var e = zh(), r = typeof self == "object" && self && self.Object === Object && self, t = e || r || Function("return this")();
  return Jt = t, Jt;
}
var Xt, Lu;
function Or() {
  if (Lu) return Xt;
  Lu = 1;
  var e = Ve(), r = e.Symbol;
  return Xt = r, Xt;
}
var Zt, $u;
function cg() {
  if ($u) return Zt;
  $u = 1;
  var e = Or(), r = Object.prototype, t = r.hasOwnProperty, n = r.toString, a = e ? e.toStringTag : void 0;
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
  return Zt = i, Zt;
}
var en, ju;
function dg() {
  if (ju) return en;
  ju = 1;
  var e = Object.prototype, r = e.toString;
  function t(n) {
    return r.call(n);
  }
  return en = t, en;
}
var rn, Fu;
function _r() {
  if (Fu) return rn;
  Fu = 1;
  var e = Or(), r = cg(), t = dg(), n = "[object Null]", a = "[object Undefined]", i = e ? e.toStringTag : void 0;
  function s(o) {
    return o == null ? o === void 0 ? a : n : i && i in Object(o) ? r(o) : t(o);
  }
  return rn = s, rn;
}
var tn, Gu;
function Fe() {
  if (Gu) return tn;
  Gu = 1;
  function e(r) {
    var t = typeof r;
    return r != null && (t == "object" || t == "function");
  }
  return tn = e, tn;
}
var nn, zu;
function Qr() {
  if (zu) return nn;
  zu = 1;
  var e = _r(), r = Fe(), t = "[object AsyncFunction]", n = "[object Function]", a = "[object GeneratorFunction]", i = "[object Proxy]";
  function s(o) {
    if (!r(o))
      return !1;
    var u = e(o);
    return u == n || u == a || u == t || u == i;
  }
  return nn = s, nn;
}
var an, Bu;
function fg() {
  if (Bu) return an;
  Bu = 1;
  var e = Ve(), r = e["__core-js_shared__"];
  return an = r, an;
}
var sn, Uu;
function hg() {
  if (Uu) return sn;
  Uu = 1;
  var e = fg(), r = function() {
    var n = /[^.]+$/.exec(e && e.keys && e.keys.IE_PROTO || "");
    return n ? "Symbol(src)_1." + n : "";
  }();
  function t(n) {
    return !!r && r in n;
  }
  return sn = t, sn;
}
var on, Ku;
function Bh() {
  if (Ku) return on;
  Ku = 1;
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
  return on = t, on;
}
var un, Vu;
function pg() {
  if (Vu) return un;
  Vu = 1;
  var e = Qr(), r = hg(), t = Fe(), n = Bh(), a = /[\\^$.*+?()[\]{}|]/g, i = /^\[object .+?Constructor\]$/, s = Function.prototype, o = Object.prototype, u = s.toString, c = o.hasOwnProperty, l = RegExp(
    "^" + u.call(c).replace(a, "\\$&").replace(/hasOwnProperty|(function).*?(?=\\\()| for .+?(?=\\\])/g, "$1.*?") + "$"
  );
  function d(p) {
    if (!t(p) || r(p))
      return !1;
    var v = e(p) ? l : i;
    return v.test(n(p));
  }
  return un = d, un;
}
var ln, Hu;
function vg() {
  if (Hu) return ln;
  Hu = 1;
  function e(r, t) {
    return r == null ? void 0 : r[t];
  }
  return ln = e, ln;
}
var cn, Wu;
function wr() {
  if (Wu) return cn;
  Wu = 1;
  var e = pg(), r = vg();
  function t(n, a) {
    var i = r(n, a);
    return e(i) ? i : void 0;
  }
  return cn = t, cn;
}
var dn, Qu;
function Ho() {
  if (Qu) return dn;
  Qu = 1;
  var e = wr(), r = Ve(), t = e(r, "Map");
  return dn = t, dn;
}
var fn, Yu;
function yt() {
  if (Yu) return fn;
  Yu = 1;
  var e = wr(), r = e(Object, "create");
  return fn = r, fn;
}
var hn, Ju;
function gg() {
  if (Ju) return hn;
  Ju = 1;
  var e = yt();
  function r() {
    this.__data__ = e ? e(null) : {}, this.size = 0;
  }
  return hn = r, hn;
}
var pn, Xu;
function yg() {
  if (Xu) return pn;
  Xu = 1;
  function e(r) {
    var t = this.has(r) && delete this.__data__[r];
    return this.size -= t ? 1 : 0, t;
  }
  return pn = e, pn;
}
var vn, Zu;
function mg() {
  if (Zu) return vn;
  Zu = 1;
  var e = yt(), r = "__lodash_hash_undefined__", t = Object.prototype, n = t.hasOwnProperty;
  function a(i) {
    var s = this.__data__;
    if (e) {
      var o = s[i];
      return o === r ? void 0 : o;
    }
    return n.call(s, i) ? s[i] : void 0;
  }
  return vn = a, vn;
}
var gn, el;
function bg() {
  if (el) return gn;
  el = 1;
  var e = yt(), r = Object.prototype, t = r.hasOwnProperty;
  function n(a) {
    var i = this.__data__;
    return e ? i[a] !== void 0 : t.call(i, a);
  }
  return gn = n, gn;
}
var yn, rl;
function _g() {
  if (rl) return yn;
  rl = 1;
  var e = yt(), r = "__lodash_hash_undefined__";
  function t(n, a) {
    var i = this.__data__;
    return this.size += this.has(n) ? 0 : 1, i[n] = e && a === void 0 ? r : a, this;
  }
  return yn = t, yn;
}
var mn, tl;
function wg() {
  if (tl) return mn;
  tl = 1;
  var e = gg(), r = yg(), t = mg(), n = bg(), a = _g();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, mn = i, mn;
}
var bn, nl;
function Ig() {
  if (nl) return bn;
  nl = 1;
  var e = wg(), r = gt(), t = Ho();
  function n() {
    this.size = 0, this.__data__ = {
      hash: new e(),
      map: new (t || r)(),
      string: new e()
    };
  }
  return bn = n, bn;
}
var _n, al;
function qg() {
  if (al) return _n;
  al = 1;
  function e(r) {
    var t = typeof r;
    return t == "string" || t == "number" || t == "symbol" || t == "boolean" ? r !== "__proto__" : r === null;
  }
  return _n = e, _n;
}
var wn, il;
function mt() {
  if (il) return wn;
  il = 1;
  var e = qg();
  function r(t, n) {
    var a = t.__data__;
    return e(n) ? a[typeof n == "string" ? "string" : "hash"] : a.map;
  }
  return wn = r, wn;
}
var In, sl;
function Cg() {
  if (sl) return In;
  sl = 1;
  var e = mt();
  function r(t) {
    var n = e(this, t).delete(t);
    return this.size -= n ? 1 : 0, n;
  }
  return In = r, In;
}
var qn, ol;
function Eg() {
  if (ol) return qn;
  ol = 1;
  var e = mt();
  function r(t) {
    return e(this, t).get(t);
  }
  return qn = r, qn;
}
var Cn, ul;
function Sg() {
  if (ul) return Cn;
  ul = 1;
  var e = mt();
  function r(t) {
    return e(this, t).has(t);
  }
  return Cn = r, Cn;
}
var En, ll;
function Rg() {
  if (ll) return En;
  ll = 1;
  var e = mt();
  function r(t, n) {
    var a = e(this, t), i = a.size;
    return a.set(t, n), this.size += a.size == i ? 0 : 1, this;
  }
  return En = r, En;
}
var Sn, cl;
function Wo() {
  if (cl) return Sn;
  cl = 1;
  var e = Ig(), r = Cg(), t = Eg(), n = Sg(), a = Rg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, Sn = i, Sn;
}
var Rn, dl;
function Tg() {
  if (dl) return Rn;
  dl = 1;
  var e = gt(), r = Ho(), t = Wo(), n = 200;
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
  return Rn = a, Rn;
}
var Tn, fl;
function bt() {
  if (fl) return Tn;
  fl = 1;
  var e = gt(), r = sg(), t = og(), n = ug(), a = lg(), i = Tg();
  function s(o) {
    var u = this.__data__ = new e(o);
    this.size = u.size;
  }
  return s.prototype.clear = r, s.prototype.delete = t, s.prototype.get = n, s.prototype.has = a, s.prototype.set = i, Tn = s, Tn;
}
var xn, hl;
function Qo() {
  if (hl) return xn;
  hl = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a && t(r[n], n, r) !== !1; )
      ;
    return r;
  }
  return xn = e, xn;
}
var kn, pl;
function Uh() {
  if (pl) return kn;
  pl = 1;
  var e = wr(), r = function() {
    try {
      var t = e(Object, "defineProperty");
      return t({}, "", {}), t;
    } catch {
    }
  }();
  return kn = r, kn;
}
var An, vl;
function _t() {
  if (vl) return An;
  vl = 1;
  var e = Uh();
  function r(t, n, a) {
    n == "__proto__" && e ? e(t, n, {
      configurable: !0,
      enumerable: !0,
      value: a,
      writable: !0
    }) : t[n] = a;
  }
  return An = r, An;
}
var Pn, gl;
function wt() {
  if (gl) return Pn;
  gl = 1;
  var e = _t(), r = Pr(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o) {
    var u = i[s];
    (!(n.call(i, s) && r(u, o)) || o === void 0 && !(s in i)) && e(i, s, o);
  }
  return Pn = a, Pn;
}
var On, yl;
function Yr() {
  if (yl) return On;
  yl = 1;
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
  return On = t, On;
}
var Nn, ml;
function xg() {
  if (ml) return Nn;
  ml = 1;
  function e(r, t) {
    for (var n = -1, a = Array(r); ++n < r; )
      a[n] = t(n);
    return a;
  }
  return Nn = e, Nn;
}
var Dn, bl;
function Xe() {
  if (bl) return Dn;
  bl = 1;
  function e(r) {
    return r != null && typeof r == "object";
  }
  return Dn = e, Dn;
}
var Mn, _l;
function kg() {
  if (_l) return Mn;
  _l = 1;
  var e = _r(), r = Xe(), t = "[object Arguments]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return Mn = n, Mn;
}
var Ln, wl;
function Jr() {
  if (wl) return Ln;
  wl = 1;
  var e = kg(), r = Xe(), t = Object.prototype, n = t.hasOwnProperty, a = t.propertyIsEnumerable, i = e(/* @__PURE__ */ function() {
    return arguments;
  }()) ? e : function(s) {
    return r(s) && n.call(s, "callee") && !a.call(s, "callee");
  };
  return Ln = i, Ln;
}
var $n, Il;
function qe() {
  if (Il) return $n;
  Il = 1;
  var e = Array.isArray;
  return $n = e, $n;
}
var Gr = { exports: {} }, jn, ql;
function Ag() {
  if (ql) return jn;
  ql = 1;
  function e() {
    return !1;
  }
  return jn = e, jn;
}
Gr.exports;
var Cl;
function Nr() {
  return Cl || (Cl = 1, function(e, r) {
    var t = Ve(), n = Ag(), a = r && !r.nodeType && r, i = a && !0 && e && !e.nodeType && e, s = i && i.exports === a, o = s ? t.Buffer : void 0, u = o ? o.isBuffer : void 0, c = u || n;
    e.exports = c;
  }(Gr, Gr.exports)), Gr.exports;
}
var Fn, El;
function It() {
  if (El) return Fn;
  El = 1;
  var e = 9007199254740991, r = /^(?:0|[1-9]\d*)$/;
  function t(n, a) {
    var i = typeof n;
    return a = a ?? e, !!a && (i == "number" || i != "symbol" && r.test(n)) && n > -1 && n % 1 == 0 && n < a;
  }
  return Fn = t, Fn;
}
var Gn, Sl;
function Yo() {
  if (Sl) return Gn;
  Sl = 1;
  var e = 9007199254740991;
  function r(t) {
    return typeof t == "number" && t > -1 && t % 1 == 0 && t <= e;
  }
  return Gn = r, Gn;
}
var zn, Rl;
function Pg() {
  if (Rl) return zn;
  Rl = 1;
  var e = _r(), r = Yo(), t = Xe(), n = "[object Arguments]", a = "[object Array]", i = "[object Boolean]", s = "[object Date]", o = "[object Error]", u = "[object Function]", c = "[object Map]", l = "[object Number]", d = "[object Object]", p = "[object RegExp]", v = "[object Set]", g = "[object String]", h = "[object WeakMap]", y = "[object ArrayBuffer]", C = "[object DataView]", x = "[object Float32Array]", _ = "[object Float64Array]", A = "[object Int8Array]", L = "[object Int16Array]", m = "[object Int32Array]", k = "[object Uint8Array]", N = "[object Uint8ClampedArray]", H = "[object Uint16Array]", G = "[object Uint32Array]", S = {};
  S[x] = S[_] = S[A] = S[L] = S[m] = S[k] = S[N] = S[H] = S[G] = !0, S[n] = S[a] = S[y] = S[i] = S[C] = S[s] = S[o] = S[u] = S[c] = S[l] = S[d] = S[p] = S[v] = S[g] = S[h] = !1;
  function j(V) {
    return t(V) && r(V.length) && !!S[e(V)];
  }
  return zn = j, zn;
}
var Bn, Tl;
function qt() {
  if (Tl) return Bn;
  Tl = 1;
  function e(r) {
    return function(t) {
      return r(t);
    };
  }
  return Bn = e, Bn;
}
var zr = { exports: {} };
zr.exports;
var xl;
function Jo() {
  return xl || (xl = 1, function(e, r) {
    var t = zh(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i && t.process, o = function() {
      try {
        var u = a && a.require && a.require("util").types;
        return u || s && s.binding && s.binding("util");
      } catch {
      }
    }();
    e.exports = o;
  }(zr, zr.exports)), zr.exports;
}
var Un, kl;
function Xr() {
  if (kl) return Un;
  kl = 1;
  var e = Pg(), r = qt(), t = Jo(), n = t && t.isTypedArray, a = n ? r(n) : e;
  return Un = a, Un;
}
var Kn, Al;
function Kh() {
  if (Al) return Kn;
  Al = 1;
  var e = xg(), r = Jr(), t = qe(), n = Nr(), a = It(), i = Xr(), s = Object.prototype, o = s.hasOwnProperty;
  function u(c, l) {
    var d = t(c), p = !d && r(c), v = !d && !p && n(c), g = !d && !p && !v && i(c), h = d || p || v || g, y = h ? e(c.length, String) : [], C = y.length;
    for (var x in c)
      (l || o.call(c, x)) && !(h && // Safari 9 has enumerable `arguments.length` in strict mode.
      (x == "length" || // Node.js 0.10 has enumerable non-index properties on buffers.
      v && (x == "offset" || x == "parent") || // PhantomJS 2 has enumerable non-index properties on typed arrays.
      g && (x == "buffer" || x == "byteLength" || x == "byteOffset") || // Skip index properties.
      a(x, C))) && y.push(x);
    return y;
  }
  return Kn = u, Kn;
}
var Vn, Pl;
function Ct() {
  if (Pl) return Vn;
  Pl = 1;
  var e = Object.prototype;
  function r(t) {
    var n = t && t.constructor, a = typeof n == "function" && n.prototype || e;
    return t === a;
  }
  return Vn = r, Vn;
}
var Hn, Ol;
function Vh() {
  if (Ol) return Hn;
  Ol = 1;
  function e(r, t) {
    return function(n) {
      return r(t(n));
    };
  }
  return Hn = e, Hn;
}
var Wn, Nl;
function Og() {
  if (Nl) return Wn;
  Nl = 1;
  var e = Vh(), r = e(Object.keys, Object);
  return Wn = r, Wn;
}
var Qn, Dl;
function Xo() {
  if (Dl) return Qn;
  Dl = 1;
  var e = Ct(), r = Og(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i) {
    if (!e(i))
      return r(i);
    var s = [];
    for (var o in Object(i))
      n.call(i, o) && o != "constructor" && s.push(o);
    return s;
  }
  return Qn = a, Qn;
}
var Yn, Ml;
function sr() {
  if (Ml) return Yn;
  Ml = 1;
  var e = Qr(), r = Yo();
  function t(n) {
    return n != null && r(n.length) && !e(n);
  }
  return Yn = t, Yn;
}
var Jn, Ll;
function pr() {
  if (Ll) return Jn;
  Ll = 1;
  var e = Kh(), r = Xo(), t = sr();
  function n(a) {
    return t(a) ? e(a) : r(a);
  }
  return Jn = n, Jn;
}
var Xn, $l;
function Ng() {
  if ($l) return Xn;
  $l = 1;
  var e = Yr(), r = pr();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return Xn = t, Xn;
}
var Zn, jl;
function Dg() {
  if (jl) return Zn;
  jl = 1;
  function e(r) {
    var t = [];
    if (r != null)
      for (var n in Object(r))
        t.push(n);
    return t;
  }
  return Zn = e, Zn;
}
var ea, Fl;
function Mg() {
  if (Fl) return ea;
  Fl = 1;
  var e = Fe(), r = Ct(), t = Dg(), n = Object.prototype, a = n.hasOwnProperty;
  function i(s) {
    if (!e(s))
      return t(s);
    var o = r(s), u = [];
    for (var c in s)
      c == "constructor" && (o || !a.call(s, c)) || u.push(c);
    return u;
  }
  return ea = i, ea;
}
var ra, Gl;
function Ir() {
  if (Gl) return ra;
  Gl = 1;
  var e = Kh(), r = Mg(), t = sr();
  function n(a) {
    return t(a) ? e(a, !0) : r(a);
  }
  return ra = n, ra;
}
var ta, zl;
function Lg() {
  if (zl) return ta;
  zl = 1;
  var e = Yr(), r = Ir();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return ta = t, ta;
}
var Br = { exports: {} };
Br.exports;
var Bl;
function Hh() {
  return Bl || (Bl = 1, function(e, r) {
    var t = Ve(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i ? t.Buffer : void 0, o = s ? s.allocUnsafe : void 0;
    function u(c, l) {
      if (l)
        return c.slice();
      var d = c.length, p = o ? o(d) : new c.constructor(d);
      return c.copy(p), p;
    }
    e.exports = u;
  }(Br, Br.exports)), Br.exports;
}
var na, Ul;
function Wh() {
  if (Ul) return na;
  Ul = 1;
  function e(r, t) {
    var n = -1, a = r.length;
    for (t || (t = Array(a)); ++n < a; )
      t[n] = r[n];
    return t;
  }
  return na = e, na;
}
var aa, Kl;
function Qh() {
  if (Kl) return aa;
  Kl = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = 0, s = []; ++n < a; ) {
      var o = r[n];
      t(o, n, r) && (s[i++] = o);
    }
    return s;
  }
  return aa = e, aa;
}
var ia, Vl;
function Yh() {
  if (Vl) return ia;
  Vl = 1;
  function e() {
    return [];
  }
  return ia = e, ia;
}
var sa, Hl;
function Zo() {
  if (Hl) return sa;
  Hl = 1;
  var e = Qh(), r = Yh(), t = Object.prototype, n = t.propertyIsEnumerable, a = Object.getOwnPropertySymbols, i = a ? function(s) {
    return s == null ? [] : (s = Object(s), e(a(s), function(o) {
      return n.call(s, o);
    }));
  } : r;
  return sa = i, sa;
}
var oa, Wl;
function $g() {
  if (Wl) return oa;
  Wl = 1;
  var e = Yr(), r = Zo();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return oa = t, oa;
}
var ua, Ql;
function eu() {
  if (Ql) return ua;
  Ql = 1;
  function e(r, t) {
    for (var n = -1, a = t.length, i = r.length; ++n < a; )
      r[i + n] = t[n];
    return r;
  }
  return ua = e, ua;
}
var la, Yl;
function Et() {
  if (Yl) return la;
  Yl = 1;
  var e = Vh(), r = e(Object.getPrototypeOf, Object);
  return la = r, la;
}
var ca, Jl;
function Jh() {
  if (Jl) return ca;
  Jl = 1;
  var e = eu(), r = Et(), t = Zo(), n = Yh(), a = Object.getOwnPropertySymbols, i = a ? function(s) {
    for (var o = []; s; )
      e(o, t(s)), s = r(s);
    return o;
  } : n;
  return ca = i, ca;
}
var da, Xl;
function jg() {
  if (Xl) return da;
  Xl = 1;
  var e = Yr(), r = Jh();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return da = t, da;
}
var fa, Zl;
function Xh() {
  if (Zl) return fa;
  Zl = 1;
  var e = eu(), r = qe();
  function t(n, a, i) {
    var s = a(n);
    return r(n) ? s : e(s, i(n));
  }
  return fa = t, fa;
}
var ha, ec;
function Zh() {
  if (ec) return ha;
  ec = 1;
  var e = Xh(), r = Zo(), t = pr();
  function n(a) {
    return e(a, t, r);
  }
  return ha = n, ha;
}
var pa, rc;
function Fg() {
  if (rc) return pa;
  rc = 1;
  var e = Xh(), r = Jh(), t = Ir();
  function n(a) {
    return e(a, t, r);
  }
  return pa = n, pa;
}
var va, tc;
function Gg() {
  if (tc) return va;
  tc = 1;
  var e = wr(), r = Ve(), t = e(r, "DataView");
  return va = t, va;
}
var ga, nc;
function zg() {
  if (nc) return ga;
  nc = 1;
  var e = wr(), r = Ve(), t = e(r, "Promise");
  return ga = t, ga;
}
var ya, ac;
function ep() {
  if (ac) return ya;
  ac = 1;
  var e = wr(), r = Ve(), t = e(r, "Set");
  return ya = t, ya;
}
var ma, ic;
function Bg() {
  if (ic) return ma;
  ic = 1;
  var e = wr(), r = Ve(), t = e(r, "WeakMap");
  return ma = t, ma;
}
var ba, sc;
function Dr() {
  if (sc) return ba;
  sc = 1;
  var e = Gg(), r = Ho(), t = zg(), n = ep(), a = Bg(), i = _r(), s = Bh(), o = "[object Map]", u = "[object Object]", c = "[object Promise]", l = "[object Set]", d = "[object WeakMap]", p = "[object DataView]", v = s(e), g = s(r), h = s(t), y = s(n), C = s(a), x = i;
  return (e && x(new e(new ArrayBuffer(1))) != p || r && x(new r()) != o || t && x(t.resolve()) != c || n && x(new n()) != l || a && x(new a()) != d) && (x = function(_) {
    var A = i(_), L = A == u ? _.constructor : void 0, m = L ? s(L) : "";
    if (m)
      switch (m) {
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
  }), ba = x, ba;
}
var _a, oc;
function Ug() {
  if (oc) return _a;
  oc = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n) {
    var a = n.length, i = new n.constructor(a);
    return a && typeof n[0] == "string" && r.call(n, "index") && (i.index = n.index, i.input = n.input), i;
  }
  return _a = t, _a;
}
var wa, uc;
function rp() {
  if (uc) return wa;
  uc = 1;
  var e = Ve(), r = e.Uint8Array;
  return wa = r, wa;
}
var Ia, lc;
function ru() {
  if (lc) return Ia;
  lc = 1;
  var e = rp();
  function r(t) {
    var n = new t.constructor(t.byteLength);
    return new e(n).set(new e(t)), n;
  }
  return Ia = r, Ia;
}
var qa, cc;
function Kg() {
  if (cc) return qa;
  cc = 1;
  var e = ru();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.byteLength);
  }
  return qa = r, qa;
}
var Ca, dc;
function Vg() {
  if (dc) return Ca;
  dc = 1;
  var e = /\w*$/;
  function r(t) {
    var n = new t.constructor(t.source, e.exec(t));
    return n.lastIndex = t.lastIndex, n;
  }
  return Ca = r, Ca;
}
var Ea, fc;
function Hg() {
  if (fc) return Ea;
  fc = 1;
  var e = Or(), r = e ? e.prototype : void 0, t = r ? r.valueOf : void 0;
  function n(a) {
    return t ? Object(t.call(a)) : {};
  }
  return Ea = n, Ea;
}
var Sa, hc;
function tp() {
  if (hc) return Sa;
  hc = 1;
  var e = ru();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.length);
  }
  return Sa = r, Sa;
}
var Ra, pc;
function Wg() {
  if (pc) return Ra;
  pc = 1;
  var e = ru(), r = Kg(), t = Vg(), n = Hg(), a = tp(), i = "[object Boolean]", s = "[object Date]", o = "[object Map]", u = "[object Number]", c = "[object RegExp]", l = "[object Set]", d = "[object String]", p = "[object Symbol]", v = "[object ArrayBuffer]", g = "[object DataView]", h = "[object Float32Array]", y = "[object Float64Array]", C = "[object Int8Array]", x = "[object Int16Array]", _ = "[object Int32Array]", A = "[object Uint8Array]", L = "[object Uint8ClampedArray]", m = "[object Uint16Array]", k = "[object Uint32Array]";
  function N(H, G, S) {
    var j = H.constructor;
    switch (G) {
      case v:
        return e(H);
      case i:
      case s:
        return new j(+H);
      case g:
        return r(H, S);
      case h:
      case y:
      case C:
      case x:
      case _:
      case A:
      case L:
      case m:
      case k:
        return a(H, S);
      case o:
        return new j();
      case u:
      case d:
        return new j(H);
      case c:
        return t(H);
      case l:
        return new j();
      case p:
        return n(H);
    }
  }
  return Ra = N, Ra;
}
var Ta, vc;
function np() {
  if (vc) return Ta;
  vc = 1;
  var e = Fe(), r = Object.create, t = /* @__PURE__ */ function() {
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
var xa, gc;
function ap() {
  if (gc) return xa;
  gc = 1;
  var e = np(), r = Et(), t = Ct();
  function n(a) {
    return typeof a.constructor == "function" && !t(a) ? e(r(a)) : {};
  }
  return xa = n, xa;
}
var ka, yc;
function Qg() {
  if (yc) return ka;
  yc = 1;
  var e = Dr(), r = Xe(), t = "[object Map]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return ka = n, ka;
}
var Aa, mc;
function Yg() {
  if (mc) return Aa;
  mc = 1;
  var e = Qg(), r = qt(), t = Jo(), n = t && t.isMap, a = n ? r(n) : e;
  return Aa = a, Aa;
}
var Pa, bc;
function Jg() {
  if (bc) return Pa;
  bc = 1;
  var e = Dr(), r = Xe(), t = "[object Set]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return Pa = n, Pa;
}
var Oa, _c;
function Xg() {
  if (_c) return Oa;
  _c = 1;
  var e = Jg(), r = qt(), t = Jo(), n = t && t.isSet, a = n ? r(n) : e;
  return Oa = a, Oa;
}
var Na, wc;
function ip() {
  if (wc) return Na;
  wc = 1;
  var e = bt(), r = Qo(), t = wt(), n = Ng(), a = Lg(), i = Hh(), s = Wh(), o = $g(), u = jg(), c = Zh(), l = Fg(), d = Dr(), p = Ug(), v = Wg(), g = ap(), h = qe(), y = Nr(), C = Yg(), x = Fe(), _ = Xg(), A = pr(), L = Ir(), m = 1, k = 2, N = 4, H = "[object Arguments]", G = "[object Array]", S = "[object Boolean]", j = "[object Date]", V = "[object Error]", z = "[object Function]", ie = "[object GeneratorFunction]", ue = "[object Map]", fe = "[object Number]", B = "[object Object]", J = "[object RegExp]", E = "[object Set]", T = "[object String]", M = "[object Symbol]", ve = "[object WeakMap]", we = "[object ArrayBuffer]", Se = "[object DataView]", Ze = "[object Float32Array]", Er = "[object Float64Array]", vr = "[object Int8Array]", Sr = "[object Int16Array]", We = "[object Int32Array]", Rr = "[object Uint8Array]", ze = "[object Uint8ClampedArray]", Qe = "[object Uint16Array]", Be = "[object Uint32Array]", le = {};
  le[H] = le[G] = le[we] = le[Se] = le[S] = le[j] = le[Ze] = le[Er] = le[vr] = le[Sr] = le[We] = le[ue] = le[fe] = le[B] = le[J] = le[E] = le[T] = le[M] = le[Rr] = le[ze] = le[Qe] = le[Be] = !0, le[V] = le[z] = le[ve] = !1;
  function ge(te, Re, Ue, er, ne, Ce) {
    var ee, X = Re & m, Ne = Re & k, ur = Re & N;
    if (Ue && (ee = ne ? Ue(te, er, ne, Ce) : Ue(te)), ee !== void 0)
      return ee;
    if (!x(te))
      return te;
    var Tr = h(te);
    if (Tr) {
      if (ee = p(te), !X)
        return s(te, ee);
    } else {
      var De = d(te), xr = De == z || De == ie;
      if (y(te))
        return i(te, X);
      if (De == B || De == H || xr && !ne) {
        if (ee = Ne || xr ? {} : g(te), !X)
          return Ne ? u(te, a(ee, te)) : o(te, n(ee, te));
      } else {
        if (!le[De])
          return ne ? te : {};
        ee = v(te, De, X);
      }
    }
    Ce || (Ce = new e());
    var gr = Ce.get(te);
    if (gr)
      return gr;
    Ce.set(te, ee), _(te) ? te.forEach(function($e) {
      ee.add(ge($e, Re, Ue, $e, te, Ce));
    }) : C(te) && te.forEach(function($e, Ke) {
      ee.set(Ke, ge($e, Re, Ue, Ke, te, Ce));
    });
    var yr = ur ? Ne ? l : c : Ne ? L : A, kr = Tr ? void 0 : yr(te);
    return r(kr || te, function($e, Ke) {
      kr && (Ke = $e, $e = te[Ke]), t(ee, Ke, ge($e, Re, Ue, Ke, te, Ce));
    }), ee;
  }
  return Na = ge, Na;
}
var Da, Ic;
function Zg() {
  if (Ic) return Da;
  Ic = 1;
  var e = ip(), r = 4;
  function t(n) {
    return e(n, r);
  }
  return Da = t, Da;
}
var Ma, qc;
function tu() {
  if (qc) return Ma;
  qc = 1;
  function e(r) {
    return function() {
      return r;
    };
  }
  return Ma = e, Ma;
}
var La, Cc;
function ey() {
  if (Cc) return La;
  Cc = 1;
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
  return La = e, La;
}
var $a, Ec;
function nu() {
  if (Ec) return $a;
  Ec = 1;
  var e = ey(), r = e();
  return $a = r, $a;
}
var ja, Sc;
function au() {
  if (Sc) return ja;
  Sc = 1;
  var e = nu(), r = pr();
  function t(n, a) {
    return n && e(n, a, r);
  }
  return ja = t, ja;
}
var Fa, Rc;
function ry() {
  if (Rc) return Fa;
  Rc = 1;
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
var Ga, Tc;
function St() {
  if (Tc) return Ga;
  Tc = 1;
  var e = au(), r = ry(), t = r(e);
  return Ga = t, Ga;
}
var za, xc;
function qr() {
  if (xc) return za;
  xc = 1;
  function e(r) {
    return r;
  }
  return za = e, za;
}
var Ba, kc;
function sp() {
  if (kc) return Ba;
  kc = 1;
  var e = qr();
  function r(t) {
    return typeof t == "function" ? t : e;
  }
  return Ba = r, Ba;
}
var Ua, Ac;
function op() {
  if (Ac) return Ua;
  Ac = 1;
  var e = Qo(), r = St(), t = sp(), n = qe();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s));
  }
  return Ua = a, Ua;
}
var Ka, Pc;
function up() {
  return Pc || (Pc = 1, Ka = op()), Ka;
}
var Va, Oc;
function ty() {
  if (Oc) return Va;
  Oc = 1;
  var e = St();
  function r(t, n) {
    var a = [];
    return e(t, function(i, s, o) {
      n(i, s, o) && a.push(i);
    }), a;
  }
  return Va = r, Va;
}
var Ha, Nc;
function ny() {
  if (Nc) return Ha;
  Nc = 1;
  var e = "__lodash_hash_undefined__";
  function r(t) {
    return this.__data__.set(t, e), this;
  }
  return Ha = r, Ha;
}
var Wa, Dc;
function ay() {
  if (Dc) return Wa;
  Dc = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return Wa = e, Wa;
}
var Qa, Mc;
function lp() {
  if (Mc) return Qa;
  Mc = 1;
  var e = Wo(), r = ny(), t = ay();
  function n(a) {
    var i = -1, s = a == null ? 0 : a.length;
    for (this.__data__ = new e(); ++i < s; )
      this.add(a[i]);
  }
  return n.prototype.add = n.prototype.push = r, n.prototype.has = t, Qa = n, Qa;
}
var Ya, Lc;
function iy() {
  if (Lc) return Ya;
  Lc = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a; )
      if (t(r[n], n, r))
        return !0;
    return !1;
  }
  return Ya = e, Ya;
}
var Ja, $c;
function cp() {
  if ($c) return Ja;
  $c = 1;
  function e(r, t) {
    return r.has(t);
  }
  return Ja = e, Ja;
}
var Xa, jc;
function dp() {
  if (jc) return Xa;
  jc = 1;
  var e = lp(), r = iy(), t = cp(), n = 1, a = 2;
  function i(s, o, u, c, l, d) {
    var p = u & n, v = s.length, g = o.length;
    if (v != g && !(p && g > v))
      return !1;
    var h = d.get(s), y = d.get(o);
    if (h && y)
      return h == o && y == s;
    var C = -1, x = !0, _ = u & a ? new e() : void 0;
    for (d.set(s, o), d.set(o, s); ++C < v; ) {
      var A = s[C], L = o[C];
      if (c)
        var m = p ? c(L, A, C, o, s, d) : c(A, L, C, s, o, d);
      if (m !== void 0) {
        if (m)
          continue;
        x = !1;
        break;
      }
      if (_) {
        if (!r(o, function(k, N) {
          if (!t(_, N) && (A === k || l(A, k, u, c, d)))
            return _.push(N);
        })) {
          x = !1;
          break;
        }
      } else if (!(A === L || l(A, L, u, c, d))) {
        x = !1;
        break;
      }
    }
    return d.delete(s), d.delete(o), x;
  }
  return Xa = i, Xa;
}
var Za, Fc;
function sy() {
  if (Fc) return Za;
  Fc = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a, i) {
      n[++t] = [i, a];
    }), n;
  }
  return Za = e, Za;
}
var ei, Gc;
function iu() {
  if (Gc) return ei;
  Gc = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a) {
      n[++t] = a;
    }), n;
  }
  return ei = e, ei;
}
var ri, zc;
function oy() {
  if (zc) return ri;
  zc = 1;
  var e = Or(), r = rp(), t = Pr(), n = dp(), a = sy(), i = iu(), s = 1, o = 2, u = "[object Boolean]", c = "[object Date]", l = "[object Error]", d = "[object Map]", p = "[object Number]", v = "[object RegExp]", g = "[object Set]", h = "[object String]", y = "[object Symbol]", C = "[object ArrayBuffer]", x = "[object DataView]", _ = e ? e.prototype : void 0, A = _ ? _.valueOf : void 0;
  function L(m, k, N, H, G, S, j) {
    switch (N) {
      case x:
        if (m.byteLength != k.byteLength || m.byteOffset != k.byteOffset)
          return !1;
        m = m.buffer, k = k.buffer;
      case C:
        return !(m.byteLength != k.byteLength || !S(new r(m), new r(k)));
      case u:
      case c:
      case p:
        return t(+m, +k);
      case l:
        return m.name == k.name && m.message == k.message;
      case v:
      case h:
        return m == k + "";
      case d:
        var V = a;
      case g:
        var z = H & s;
        if (V || (V = i), m.size != k.size && !z)
          return !1;
        var ie = j.get(m);
        if (ie)
          return ie == k;
        H |= o, j.set(m, k);
        var ue = n(V(m), V(k), H, G, S, j);
        return j.delete(m), ue;
      case y:
        if (A)
          return A.call(m) == A.call(k);
    }
    return !1;
  }
  return ri = L, ri;
}
var ti, Bc;
function uy() {
  if (Bc) return ti;
  Bc = 1;
  var e = Zh(), r = 1, t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o, u, c, l) {
    var d = o & r, p = e(i), v = p.length, g = e(s), h = g.length;
    if (v != h && !d)
      return !1;
    for (var y = v; y--; ) {
      var C = p[y];
      if (!(d ? C in s : n.call(s, C)))
        return !1;
    }
    var x = l.get(i), _ = l.get(s);
    if (x && _)
      return x == s && _ == i;
    var A = !0;
    l.set(i, s), l.set(s, i);
    for (var L = d; ++y < v; ) {
      C = p[y];
      var m = i[C], k = s[C];
      if (u)
        var N = d ? u(k, m, C, s, i, l) : u(m, k, C, i, s, l);
      if (!(N === void 0 ? m === k || c(m, k, o, u, l) : N)) {
        A = !1;
        break;
      }
      L || (L = C == "constructor");
    }
    if (A && !L) {
      var H = i.constructor, G = s.constructor;
      H != G && "constructor" in i && "constructor" in s && !(typeof H == "function" && H instanceof H && typeof G == "function" && G instanceof G) && (A = !1);
    }
    return l.delete(i), l.delete(s), A;
  }
  return ti = a, ti;
}
var ni, Uc;
function ly() {
  if (Uc) return ni;
  Uc = 1;
  var e = bt(), r = dp(), t = oy(), n = uy(), a = Dr(), i = qe(), s = Nr(), o = Xr(), u = 1, c = "[object Arguments]", l = "[object Array]", d = "[object Object]", p = Object.prototype, v = p.hasOwnProperty;
  function g(h, y, C, x, _, A) {
    var L = i(h), m = i(y), k = L ? l : a(h), N = m ? l : a(y);
    k = k == c ? d : k, N = N == c ? d : N;
    var H = k == d, G = N == d, S = k == N;
    if (S && s(h)) {
      if (!s(y))
        return !1;
      L = !0, H = !1;
    }
    if (S && !H)
      return A || (A = new e()), L || o(h) ? r(h, y, C, x, _, A) : t(h, y, k, C, x, _, A);
    if (!(C & u)) {
      var j = H && v.call(h, "__wrapped__"), V = G && v.call(y, "__wrapped__");
      if (j || V) {
        var z = j ? h.value() : h, ie = V ? y.value() : y;
        return A || (A = new e()), _(z, ie, C, x, A);
      }
    }
    return S ? (A || (A = new e()), n(h, y, C, x, _, A)) : !1;
  }
  return ni = g, ni;
}
var ai, Kc;
function fp() {
  if (Kc) return ai;
  Kc = 1;
  var e = ly(), r = Xe();
  function t(n, a, i, s, o) {
    return n === a ? !0 : n == null || a == null || !r(n) && !r(a) ? n !== n && a !== a : e(n, a, i, s, t, o);
  }
  return ai = t, ai;
}
var ii, Vc;
function cy() {
  if (Vc) return ii;
  Vc = 1;
  var e = bt(), r = fp(), t = 1, n = 2;
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
  return ii = a, ii;
}
var si, Hc;
function hp() {
  if (Hc) return si;
  Hc = 1;
  var e = Fe();
  function r(t) {
    return t === t && !e(t);
  }
  return si = r, si;
}
var oi, Wc;
function dy() {
  if (Wc) return oi;
  Wc = 1;
  var e = hp(), r = pr();
  function t(n) {
    for (var a = r(n), i = a.length; i--; ) {
      var s = a[i], o = n[s];
      a[i] = [s, o, e(o)];
    }
    return a;
  }
  return oi = t, oi;
}
var ui, Qc;
function pp() {
  if (Qc) return ui;
  Qc = 1;
  function e(r, t) {
    return function(n) {
      return n == null ? !1 : n[r] === t && (t !== void 0 || r in Object(n));
    };
  }
  return ui = e, ui;
}
var li, Yc;
function fy() {
  if (Yc) return li;
  Yc = 1;
  var e = cy(), r = dy(), t = pp();
  function n(a) {
    var i = r(a);
    return i.length == 1 && i[0][2] ? t(i[0][0], i[0][1]) : function(s) {
      return s === a || e(s, a, i);
    };
  }
  return li = n, li;
}
var ci, Jc;
function Mr() {
  if (Jc) return ci;
  Jc = 1;
  var e = _r(), r = Xe(), t = "[object Symbol]";
  function n(a) {
    return typeof a == "symbol" || r(a) && e(a) == t;
  }
  return ci = n, ci;
}
var di, Xc;
function su() {
  if (Xc) return di;
  Xc = 1;
  var e = qe(), r = Mr(), t = /\.|\[(?:[^[\]]*|(["'])(?:(?!\1)[^\\]|\\.)*?\1)\]/, n = /^\w*$/;
  function a(i, s) {
    if (e(i))
      return !1;
    var o = typeof i;
    return o == "number" || o == "symbol" || o == "boolean" || i == null || r(i) ? !0 : n.test(i) || !t.test(i) || s != null && i in Object(s);
  }
  return di = a, di;
}
var fi, Zc;
function hy() {
  if (Zc) return fi;
  Zc = 1;
  var e = Wo(), r = "Expected a function";
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
  return t.Cache = e, fi = t, fi;
}
var hi, ed;
function py() {
  if (ed) return hi;
  ed = 1;
  var e = hy(), r = 500;
  function t(n) {
    var a = e(n, function(s) {
      return i.size === r && i.clear(), s;
    }), i = a.cache;
    return a;
  }
  return hi = t, hi;
}
var pi, rd;
function vy() {
  if (rd) return pi;
  rd = 1;
  var e = py(), r = /[^.[\]]+|\[(?:(-?\d+(?:\.\d+)?)|(["'])((?:(?!\2)[^\\]|\\.)*?)\2)\]|(?=(?:\.|\[\])(?:\.|\[\]|$))/g, t = /\\(\\)?/g, n = e(function(a) {
    var i = [];
    return a.charCodeAt(0) === 46 && i.push(""), a.replace(r, function(s, o, u, c) {
      i.push(u ? c.replace(t, "$1") : o || s);
    }), i;
  });
  return pi = n, pi;
}
var vi, td;
function Rt() {
  if (td) return vi;
  td = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = Array(a); ++n < a; )
      i[n] = t(r[n], n, r);
    return i;
  }
  return vi = e, vi;
}
var gi, nd;
function gy() {
  if (nd) return gi;
  nd = 1;
  var e = Or(), r = Rt(), t = qe(), n = Mr(), a = e ? e.prototype : void 0, i = a ? a.toString : void 0;
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
  return gi = s, gi;
}
var yi, ad;
function vp() {
  if (ad) return yi;
  ad = 1;
  var e = gy();
  function r(t) {
    return t == null ? "" : e(t);
  }
  return yi = r, yi;
}
var mi, id;
function Tt() {
  if (id) return mi;
  id = 1;
  var e = qe(), r = su(), t = vy(), n = vp();
  function a(i, s) {
    return e(i) ? i : r(i, s) ? [i] : t(n(i));
  }
  return mi = a, mi;
}
var bi, sd;
function Zr() {
  if (sd) return bi;
  sd = 1;
  var e = Mr();
  function r(t) {
    if (typeof t == "string" || e(t))
      return t;
    var n = t + "";
    return n == "0" && 1 / t == -1 / 0 ? "-0" : n;
  }
  return bi = r, bi;
}
var _i, od;
function xt() {
  if (od) return _i;
  od = 1;
  var e = Tt(), r = Zr();
  function t(n, a) {
    a = e(a, n);
    for (var i = 0, s = a.length; n != null && i < s; )
      n = n[r(a[i++])];
    return i && i == s ? n : void 0;
  }
  return _i = t, _i;
}
var wi, ud;
function yy() {
  if (ud) return wi;
  ud = 1;
  var e = xt();
  function r(t, n, a) {
    var i = t == null ? void 0 : e(t, n);
    return i === void 0 ? a : i;
  }
  return wi = r, wi;
}
var Ii, ld;
function my() {
  if (ld) return Ii;
  ld = 1;
  function e(r, t) {
    return r != null && t in Object(r);
  }
  return Ii = e, Ii;
}
var qi, cd;
function gp() {
  if (cd) return qi;
  cd = 1;
  var e = Tt(), r = Jr(), t = qe(), n = It(), a = Yo(), i = Zr();
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
  return qi = s, qi;
}
var Ci, dd;
function yp() {
  if (dd) return Ci;
  dd = 1;
  var e = my(), r = gp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return Ci = t, Ci;
}
var Ei, fd;
function by() {
  if (fd) return Ei;
  fd = 1;
  var e = fp(), r = yy(), t = yp(), n = su(), a = hp(), i = pp(), s = Zr(), o = 1, u = 2;
  function c(l, d) {
    return n(l) && a(d) ? i(s(l), d) : function(p) {
      var v = r(p, l);
      return v === void 0 && v === d ? t(p, l) : e(d, v, o | u);
    };
  }
  return Ei = c, Ei;
}
var Si, hd;
function mp() {
  if (hd) return Si;
  hd = 1;
  function e(r) {
    return function(t) {
      return t == null ? void 0 : t[r];
    };
  }
  return Si = e, Si;
}
var Ri, pd;
function _y() {
  if (pd) return Ri;
  pd = 1;
  var e = xt();
  function r(t) {
    return function(n) {
      return e(n, t);
    };
  }
  return Ri = r, Ri;
}
var Ti, vd;
function wy() {
  if (vd) return Ti;
  vd = 1;
  var e = mp(), r = _y(), t = su(), n = Zr();
  function a(i) {
    return t(i) ? e(n(i)) : r(i);
  }
  return Ti = a, Ti;
}
var xi, gd;
function or() {
  if (gd) return xi;
  gd = 1;
  var e = fy(), r = by(), t = qr(), n = qe(), a = wy();
  function i(s) {
    return typeof s == "function" ? s : s == null ? t : typeof s == "object" ? n(s) ? r(s[0], s[1]) : e(s) : a(s);
  }
  return xi = i, xi;
}
var ki, yd;
function bp() {
  if (yd) return ki;
  yd = 1;
  var e = Qh(), r = ty(), t = or(), n = qe();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s, 3));
  }
  return ki = a, ki;
}
var Ai, md;
function Iy() {
  if (md) return Ai;
  md = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n, a) {
    return n != null && r.call(n, a);
  }
  return Ai = t, Ai;
}
var Pi, bd;
function _p() {
  if (bd) return Pi;
  bd = 1;
  var e = Iy(), r = gp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return Pi = t, Pi;
}
var Oi, _d;
function qy() {
  if (_d) return Oi;
  _d = 1;
  var e = Xo(), r = Dr(), t = Jr(), n = qe(), a = sr(), i = Nr(), s = Ct(), o = Xr(), u = "[object Map]", c = "[object Set]", l = Object.prototype, d = l.hasOwnProperty;
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
  return Oi = p, Oi;
}
var Ni, wd;
function wp() {
  if (wd) return Ni;
  wd = 1;
  function e(r) {
    return r === void 0;
  }
  return Ni = e, Ni;
}
var Di, Id;
function Ip() {
  if (Id) return Di;
  Id = 1;
  var e = St(), r = sr();
  function t(n, a) {
    var i = -1, s = r(n) ? Array(n.length) : [];
    return e(n, function(o, u, c) {
      s[++i] = a(o, u, c);
    }), s;
  }
  return Di = t, Di;
}
var Mi, qd;
function qp() {
  if (qd) return Mi;
  qd = 1;
  var e = Rt(), r = or(), t = Ip(), n = qe();
  function a(i, s) {
    var o = n(i) ? e : t;
    return o(i, r(s, 3));
  }
  return Mi = a, Mi;
}
var Li, Cd;
function Cy() {
  if (Cd) return Li;
  Cd = 1;
  function e(r, t, n, a) {
    var i = -1, s = r == null ? 0 : r.length;
    for (a && s && (n = r[++i]); ++i < s; )
      n = t(n, r[i], i, r);
    return n;
  }
  return Li = e, Li;
}
var $i, Ed;
function Ey() {
  if (Ed) return $i;
  Ed = 1;
  function e(r, t, n, a, i) {
    return i(r, function(s, o, u) {
      n = a ? (a = !1, s) : t(n, s, o, u);
    }), n;
  }
  return $i = e, $i;
}
var ji, Sd;
function Cp() {
  if (Sd) return ji;
  Sd = 1;
  var e = Cy(), r = St(), t = or(), n = Ey(), a = qe();
  function i(s, o, u) {
    var c = a(s) ? e : n, l = arguments.length < 3;
    return c(s, t(o, 4), u, l, r);
  }
  return ji = i, ji;
}
var Fi, Rd;
function Sy() {
  if (Rd) return Fi;
  Rd = 1;
  var e = _r(), r = qe(), t = Xe(), n = "[object String]";
  function a(i) {
    return typeof i == "string" || !r(i) && t(i) && e(i) == n;
  }
  return Fi = a, Fi;
}
var Gi, Td;
function Ry() {
  if (Td) return Gi;
  Td = 1;
  var e = mp(), r = e("length");
  return Gi = r, Gi;
}
var zi, xd;
function Ty() {
  if (xd) return zi;
  xd = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "\\u200d", o = RegExp("[" + s + e + a + i + "]");
  function u(c) {
    return o.test(c);
  }
  return zi = u, zi;
}
var Bi, kd;
function xy() {
  if (kd) return Bi;
  kd = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "[" + e + "]", o = "[" + a + "]", u = "\\ud83c[\\udffb-\\udfff]", c = "(?:" + o + "|" + u + ")", l = "[^" + e + "]", d = "(?:\\ud83c[\\udde6-\\uddff]){2}", p = "[\\ud800-\\udbff][\\udc00-\\udfff]", v = "\\u200d", g = c + "?", h = "[" + i + "]?", y = "(?:" + v + "(?:" + [l, d, p].join("|") + ")" + h + g + ")*", C = h + g + y, x = "(?:" + [l + o + "?", o, d, p, s].join("|") + ")", _ = RegExp(u + "(?=" + u + ")|" + x + C, "g");
  function A(L) {
    for (var m = _.lastIndex = 0; _.test(L); )
      ++m;
    return m;
  }
  return Bi = A, Bi;
}
var Ui, Ad;
function ky() {
  if (Ad) return Ui;
  Ad = 1;
  var e = Ry(), r = Ty(), t = xy();
  function n(a) {
    return r(a) ? t(a) : e(a);
  }
  return Ui = n, Ui;
}
var Ki, Pd;
function Ay() {
  if (Pd) return Ki;
  Pd = 1;
  var e = Xo(), r = Dr(), t = sr(), n = Sy(), a = ky(), i = "[object Map]", s = "[object Set]";
  function o(u) {
    if (u == null)
      return 0;
    if (t(u))
      return n(u) ? a(u) : u.length;
    var c = r(u);
    return c == i || c == s ? u.size : e(u).length;
  }
  return Ki = o, Ki;
}
var Vi, Od;
function Py() {
  if (Od) return Vi;
  Od = 1;
  var e = Qo(), r = np(), t = au(), n = or(), a = Et(), i = qe(), s = Nr(), o = Qr(), u = Fe(), c = Xr();
  function l(d, p, v) {
    var g = i(d), h = g || s(d) || c(d);
    if (p = n(p, 4), v == null) {
      var y = d && d.constructor;
      h ? v = g ? new y() : [] : u(d) ? v = o(y) ? r(a(d)) : {} : v = {};
    }
    return (h ? e : t)(d, function(C, x, _) {
      return p(v, C, x, _);
    }), v;
  }
  return Vi = l, Vi;
}
var Hi, Nd;
function Oy() {
  if (Nd) return Hi;
  Nd = 1;
  var e = Or(), r = Jr(), t = qe(), n = e ? e.isConcatSpreadable : void 0;
  function a(i) {
    return t(i) || r(i) || !!(n && i && i[n]);
  }
  return Hi = a, Hi;
}
var Wi, Dd;
function ou() {
  if (Dd) return Wi;
  Dd = 1;
  var e = eu(), r = Oy();
  function t(n, a, i, s, o) {
    var u = -1, c = n.length;
    for (i || (i = r), o || (o = []); ++u < c; ) {
      var l = n[u];
      a > 0 && i(l) ? a > 1 ? t(l, a - 1, i, s, o) : e(o, l) : s || (o[o.length] = l);
    }
    return o;
  }
  return Wi = t, Wi;
}
var Qi, Md;
function Ny() {
  if (Md) return Qi;
  Md = 1;
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
  return Qi = e, Qi;
}
var Yi, Ld;
function Ep() {
  if (Ld) return Yi;
  Ld = 1;
  var e = Ny(), r = Math.max;
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
  return Yi = t, Yi;
}
var Ji, $d;
function Dy() {
  if ($d) return Ji;
  $d = 1;
  var e = tu(), r = Uh(), t = qr(), n = r ? function(a, i) {
    return r(a, "toString", {
      configurable: !0,
      enumerable: !1,
      value: e(i),
      writable: !0
    });
  } : t;
  return Ji = n, Ji;
}
var Xi, jd;
function My() {
  if (jd) return Xi;
  jd = 1;
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
  return Xi = n, Xi;
}
var Zi, Fd;
function Sp() {
  if (Fd) return Zi;
  Fd = 1;
  var e = Dy(), r = My(), t = r(e);
  return Zi = t, Zi;
}
var es, Gd;
function kt() {
  if (Gd) return es;
  Gd = 1;
  var e = qr(), r = Ep(), t = Sp();
  function n(a, i) {
    return t(r(a, i, e), a + "");
  }
  return es = n, es;
}
var rs, zd;
function Rp() {
  if (zd) return rs;
  zd = 1;
  function e(r, t, n, a) {
    for (var i = r.length, s = n + (a ? 1 : -1); a ? s-- : ++s < i; )
      if (t(r[s], s, r))
        return s;
    return -1;
  }
  return rs = e, rs;
}
var ts, Bd;
function Ly() {
  if (Bd) return ts;
  Bd = 1;
  function e(r) {
    return r !== r;
  }
  return ts = e, ts;
}
var ns, Ud;
function $y() {
  if (Ud) return ns;
  Ud = 1;
  function e(r, t, n) {
    for (var a = n - 1, i = r.length; ++a < i; )
      if (r[a] === t)
        return a;
    return -1;
  }
  return ns = e, ns;
}
var as, Kd;
function jy() {
  if (Kd) return as;
  Kd = 1;
  var e = Rp(), r = Ly(), t = $y();
  function n(a, i, s) {
    return i === i ? t(a, i, s) : e(a, r, s);
  }
  return as = n, as;
}
var is, Vd;
function Fy() {
  if (Vd) return is;
  Vd = 1;
  var e = jy();
  function r(t, n) {
    var a = t == null ? 0 : t.length;
    return !!a && e(t, n, 0) > -1;
  }
  return is = r, is;
}
var ss, Hd;
function Gy() {
  if (Hd) return ss;
  Hd = 1;
  function e(r, t, n) {
    for (var a = -1, i = r == null ? 0 : r.length; ++a < i; )
      if (n(t, r[a]))
        return !0;
    return !1;
  }
  return ss = e, ss;
}
var os, Wd;
function zy() {
  if (Wd) return os;
  Wd = 1;
  function e() {
  }
  return os = e, os;
}
var us, Qd;
function By() {
  if (Qd) return us;
  Qd = 1;
  var e = ep(), r = zy(), t = iu(), n = 1 / 0, a = e && 1 / t(new e([, -0]))[1] == n ? function(i) {
    return new e(i);
  } : r;
  return us = a, us;
}
var ls, Yd;
function Uy() {
  if (Yd) return ls;
  Yd = 1;
  var e = lp(), r = Fy(), t = Gy(), n = cp(), a = By(), i = iu(), s = 200;
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
        var x = u[d], _ = c ? c(x) : x;
        if (x = l || x !== 0 ? x : 0, g && _ === _) {
          for (var A = y.length; A--; )
            if (y[A] === _)
              continue e;
          c && y.push(_), h.push(x);
        } else p(y, _, l) || (y !== h && y.push(_), h.push(x));
      }
    return h;
  }
  return ls = o, ls;
}
var cs, Jd;
function Tp() {
  if (Jd) return cs;
  Jd = 1;
  var e = sr(), r = Xe();
  function t(n) {
    return r(n) && e(n);
  }
  return cs = t, cs;
}
var ds, Xd;
function Ky() {
  if (Xd) return ds;
  Xd = 1;
  var e = ou(), r = kt(), t = Uy(), n = Tp(), a = r(function(i) {
    return t(e(i, 1, n, !0));
  });
  return ds = a, ds;
}
var fs, Zd;
function Vy() {
  if (Zd) return fs;
  Zd = 1;
  var e = Rt();
  function r(t, n) {
    return e(n, function(a) {
      return t[a];
    });
  }
  return fs = r, fs;
}
var hs, ef;
function xp() {
  if (ef) return hs;
  ef = 1;
  var e = Vy(), r = pr();
  function t(n) {
    return n == null ? [] : e(n, r(n));
  }
  return hs = t, hs;
}
var ps, rf;
function Ge() {
  if (rf) return ps;
  rf = 1;
  var e;
  if (typeof Vo == "function")
    try {
      e = {
        clone: Zg(),
        constant: tu(),
        each: up(),
        filter: bp(),
        has: _p(),
        isArray: qe(),
        isEmpty: qy(),
        isFunction: Qr(),
        isUndefined: wp(),
        keys: pr(),
        map: qp(),
        reduce: Cp(),
        size: Ay(),
        transform: Py(),
        union: Ky(),
        values: xp()
      };
    } catch {
    }
  return e || (e = window._), ps = e, ps;
}
var vs, tf;
function uu() {
  if (tf) return vs;
  tf = 1;
  var e = Ge();
  vs = a;
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
  return vs;
}
var gs, nf;
function Hy() {
  return nf || (nf = 1, gs = "2.1.8"), gs;
}
var ys, af;
function Wy() {
  return af || (af = 1, ys = {
    Graph: uu(),
    version: Hy()
  }), ys;
}
var ms, sf;
function Qy() {
  if (sf) return ms;
  sf = 1;
  var e = Ge(), r = uu();
  ms = {
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
  return ms;
}
var bs, of;
function Yy() {
  if (of) return bs;
  of = 1;
  var e = Ge();
  bs = r;
  function r(t) {
    var n = {}, a = [], i;
    function s(o) {
      e.has(n, o) || (n[o] = !0, i.push(o), e.each(t.successors(o), s), e.each(t.predecessors(o), s));
    }
    return e.each(t.nodes(), function(o) {
      i = [], s(o), i.length && a.push(i);
    }), a;
  }
  return bs;
}
var _s, uf;
function kp() {
  if (uf) return _s;
  uf = 1;
  var e = Ge();
  _s = r;
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
  }, _s;
}
var ws, lf;
function Ap() {
  if (lf) return ws;
  lf = 1;
  var e = Ge(), r = kp();
  ws = n;
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
      var h = g.v !== d ? g.v : g.w, y = c[h], C = o(g), x = p.distance + C;
      if (C < 0)
        throw new Error("dijkstra does not allow negative edge weights. Bad edge: " + g + " Weight: " + C);
      x < y.distance && (y.distance = x, y.predecessor = d, l.decrease(h, x));
    };
    for (i.nodes().forEach(function(g) {
      var h = g === s ? 0 : Number.POSITIVE_INFINITY;
      c[g] = { distance: h }, l.add(g, h);
    }); l.size() > 0 && (d = l.removeMin(), p = c[d], p.distance !== Number.POSITIVE_INFINITY); )
      u(d).forEach(v);
    return c;
  }
  return ws;
}
var Is, cf;
function Jy() {
  if (cf) return Is;
  cf = 1;
  var e = Ap(), r = Ge();
  Is = t;
  function t(n, a, i) {
    return r.transform(n.nodes(), function(s, o) {
      s[o] = e(n, o, a, i);
    }, {});
  }
  return Is;
}
var qs, df;
function Pp() {
  if (df) return qs;
  df = 1;
  var e = Ge();
  qs = r;
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
  return qs;
}
var Cs, ff;
function Xy() {
  if (ff) return Cs;
  ff = 1;
  var e = Ge(), r = Pp();
  Cs = t;
  function t(n) {
    return e.filter(r(n), function(a) {
      return a.length > 1 || a.length === 1 && n.hasEdge(a[0], a[0]);
    });
  }
  return Cs;
}
var Es, hf;
function Zy() {
  if (hf) return Es;
  hf = 1;
  var e = Ge();
  Es = t;
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
  return Es;
}
var Ss, pf;
function Op() {
  if (pf) return Ss;
  pf = 1;
  var e = Ge();
  Ss = r, r.CycleException = t;
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
  return t.prototype = new Error(), Ss;
}
var Rs, vf;
function em() {
  if (vf) return Rs;
  vf = 1;
  var e = Op();
  Rs = r;
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
  return Rs;
}
var Ts, gf;
function Np() {
  if (gf) return Ts;
  gf = 1;
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
var xs, yf;
function rm() {
  if (yf) return xs;
  yf = 1;
  var e = Np();
  xs = r;
  function r(t, n) {
    return e(t, n, "post");
  }
  return xs;
}
var ks, mf;
function tm() {
  if (mf) return ks;
  mf = 1;
  var e = Np();
  ks = r;
  function r(t, n) {
    return e(t, n, "pre");
  }
  return ks;
}
var As, bf;
function nm() {
  if (bf) return As;
  bf = 1;
  var e = Ge(), r = uu(), t = kp();
  As = n;
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
  return As;
}
var Ps, _f;
function am() {
  return _f || (_f = 1, Ps = {
    components: Yy(),
    dijkstra: Ap(),
    dijkstraAll: Jy(),
    findCycles: Xy(),
    floydWarshall: Zy(),
    isAcyclic: em(),
    postorder: rm(),
    preorder: tm(),
    prim: nm(),
    tarjan: Pp(),
    topsort: Op()
  }), Ps;
}
var Os, wf;
function im() {
  if (wf) return Os;
  wf = 1;
  var e = Wy();
  return Os = {
    Graph: e.Graph,
    json: Qy(),
    alg: am(),
    version: e.version
  }, Os;
}
var lt;
if (typeof Vo == "function")
  try {
    lt = im();
  } catch {
  }
lt || (lt = window.graphlib);
var He = lt, Ns, If;
function sm() {
  if (If) return Ns;
  If = 1;
  var e = ip(), r = 1, t = 4;
  function n(a) {
    return e(a, r | t);
  }
  return Ns = n, Ns;
}
var Ds, qf;
function At() {
  if (qf) return Ds;
  qf = 1;
  var e = Pr(), r = sr(), t = It(), n = Fe();
  function a(i, s, o) {
    if (!n(o))
      return !1;
    var u = typeof s;
    return (u == "number" ? r(o) && t(s, o.length) : u == "string" && s in o) ? e(o[s], i) : !1;
  }
  return Ds = a, Ds;
}
var Ms, Cf;
function om() {
  if (Cf) return Ms;
  Cf = 1;
  var e = kt(), r = Pr(), t = At(), n = Ir(), a = Object.prototype, i = a.hasOwnProperty, s = e(function(o, u) {
    o = Object(o);
    var c = -1, l = u.length, d = l > 2 ? u[2] : void 0;
    for (d && t(u[0], u[1], d) && (l = 1); ++c < l; )
      for (var p = u[c], v = n(p), g = -1, h = v.length; ++g < h; ) {
        var y = v[g], C = o[y];
        (C === void 0 || r(C, a[y]) && !i.call(o, y)) && (o[y] = p[y]);
      }
    return o;
  });
  return Ms = s, Ms;
}
var Ls, Ef;
function um() {
  if (Ef) return Ls;
  Ef = 1;
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
  return Ls = n, Ls;
}
var $s, Sf;
function lm() {
  if (Sf) return $s;
  Sf = 1;
  var e = /\s/;
  function r(t) {
    for (var n = t.length; n-- && e.test(t.charAt(n)); )
      ;
    return n;
  }
  return $s = r, $s;
}
var js, Rf;
function cm() {
  if (Rf) return js;
  Rf = 1;
  var e = lm(), r = /^\s+/;
  function t(n) {
    return n && n.slice(0, e(n) + 1).replace(r, "");
  }
  return js = t, js;
}
var Fs, Tf;
function dm() {
  if (Tf) return Fs;
  Tf = 1;
  var e = cm(), r = Fe(), t = Mr(), n = NaN, a = /^[-+]0x[0-9a-f]+$/i, i = /^0b[01]+$/i, s = /^0o[0-7]+$/i, o = parseInt;
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
var Gs, xf;
function Dp() {
  if (xf) return Gs;
  xf = 1;
  var e = dm(), r = 1 / 0, t = 17976931348623157e292;
  function n(a) {
    if (!a)
      return a === 0 ? a : 0;
    if (a = e(a), a === r || a === -r) {
      var i = a < 0 ? -1 : 1;
      return i * t;
    }
    return a === a ? a : 0;
  }
  return Gs = n, Gs;
}
var zs, kf;
function fm() {
  if (kf) return zs;
  kf = 1;
  var e = Dp();
  function r(t) {
    var n = e(t), a = n % 1;
    return n === n ? a ? n - a : n : 0;
  }
  return zs = r, zs;
}
var Bs, Af;
function hm() {
  if (Af) return Bs;
  Af = 1;
  var e = Rp(), r = or(), t = fm(), n = Math.max;
  function a(i, s, o) {
    var u = i == null ? 0 : i.length;
    if (!u)
      return -1;
    var c = o == null ? 0 : t(o);
    return c < 0 && (c = n(u + c, 0)), e(i, r(s, 3), c);
  }
  return Bs = a, Bs;
}
var Us, Pf;
function pm() {
  if (Pf) return Us;
  Pf = 1;
  var e = um(), r = hm(), t = e(r);
  return Us = t, Us;
}
var Ks, Of;
function Mp() {
  if (Of) return Ks;
  Of = 1;
  var e = ou();
  function r(t) {
    var n = t == null ? 0 : t.length;
    return n ? e(t, 1) : [];
  }
  return Ks = r, Ks;
}
var Vs, Nf;
function vm() {
  if (Nf) return Vs;
  Nf = 1;
  var e = nu(), r = sp(), t = Ir();
  function n(a, i) {
    return a == null ? a : e(a, r(i), t);
  }
  return Vs = n, Vs;
}
var Hs, Df;
function gm() {
  if (Df) return Hs;
  Df = 1;
  function e(r) {
    var t = r == null ? 0 : r.length;
    return t ? r[t - 1] : void 0;
  }
  return Hs = e, Hs;
}
var Ws, Mf;
function ym() {
  if (Mf) return Ws;
  Mf = 1;
  var e = _t(), r = au(), t = or();
  function n(a, i) {
    var s = {};
    return i = t(i, 3), r(a, function(o, u, c) {
      e(s, u, i(o, u, c));
    }), s;
  }
  return Ws = n, Ws;
}
var Qs, Lf;
function lu() {
  if (Lf) return Qs;
  Lf = 1;
  var e = Mr();
  function r(t, n, a) {
    for (var i = -1, s = t.length; ++i < s; ) {
      var o = t[i], u = n(o);
      if (u != null && (c === void 0 ? u === u && !e(u) : a(u, c)))
        var c = u, l = o;
    }
    return l;
  }
  return Qs = r, Qs;
}
var Ys, $f;
function mm() {
  if ($f) return Ys;
  $f = 1;
  function e(r, t) {
    return r > t;
  }
  return Ys = e, Ys;
}
var Js, jf;
function bm() {
  if (jf) return Js;
  jf = 1;
  var e = lu(), r = mm(), t = qr();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return Js = n, Js;
}
var Xs, Ff;
function Lp() {
  if (Ff) return Xs;
  Ff = 1;
  var e = _t(), r = Pr();
  function t(n, a, i) {
    (i !== void 0 && !r(n[a], i) || i === void 0 && !(a in n)) && e(n, a, i);
  }
  return Xs = t, Xs;
}
var Zs, Gf;
function _m() {
  if (Gf) return Zs;
  Gf = 1;
  var e = _r(), r = Et(), t = Xe(), n = "[object Object]", a = Function.prototype, i = Object.prototype, s = a.toString, o = i.hasOwnProperty, u = s.call(Object);
  function c(l) {
    if (!t(l) || e(l) != n)
      return !1;
    var d = r(l);
    if (d === null)
      return !0;
    var p = o.call(d, "constructor") && d.constructor;
    return typeof p == "function" && p instanceof p && s.call(p) == u;
  }
  return Zs = c, Zs;
}
var eo, zf;
function $p() {
  if (zf) return eo;
  zf = 1;
  function e(r, t) {
    if (!(t === "constructor" && typeof r[t] == "function") && t != "__proto__")
      return r[t];
  }
  return eo = e, eo;
}
var ro, Bf;
function wm() {
  if (Bf) return ro;
  Bf = 1;
  var e = Yr(), r = Ir();
  function t(n) {
    return e(n, r(n));
  }
  return ro = t, ro;
}
var to, Uf;
function Im() {
  if (Uf) return to;
  Uf = 1;
  var e = Lp(), r = Hh(), t = tp(), n = Wh(), a = ap(), i = Jr(), s = qe(), o = Tp(), u = Nr(), c = Qr(), l = Fe(), d = _m(), p = Xr(), v = $p(), g = wm();
  function h(y, C, x, _, A, L, m) {
    var k = v(y, x), N = v(C, x), H = m.get(N);
    if (H) {
      e(y, x, H);
      return;
    }
    var G = L ? L(k, N, x + "", y, C, m) : void 0, S = G === void 0;
    if (S) {
      var j = s(N), V = !j && u(N), z = !j && !V && p(N);
      G = N, j || V || z ? s(k) ? G = k : o(k) ? G = n(k) : V ? (S = !1, G = r(N, !0)) : z ? (S = !1, G = t(N, !0)) : G = [] : d(N) || i(N) ? (G = k, i(k) ? G = g(k) : (!l(k) || c(k)) && (G = a(N))) : S = !1;
    }
    S && (m.set(N, G), A(G, N, _, L, m), m.delete(N)), e(y, x, G);
  }
  return to = h, to;
}
var no, Kf;
function qm() {
  if (Kf) return no;
  Kf = 1;
  var e = bt(), r = Lp(), t = nu(), n = Im(), a = Fe(), i = Ir(), s = $p();
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
  return no = o, no;
}
var ao, Vf;
function Cm() {
  if (Vf) return ao;
  Vf = 1;
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
  return ao = t, ao;
}
var io, Hf;
function Em() {
  if (Hf) return io;
  Hf = 1;
  var e = qm(), r = Cm(), t = r(function(n, a, i) {
    e(n, a, i);
  });
  return io = t, io;
}
var so, Wf;
function jp() {
  if (Wf) return so;
  Wf = 1;
  function e(r, t) {
    return r < t;
  }
  return so = e, so;
}
var oo, Qf;
function Sm() {
  if (Qf) return oo;
  Qf = 1;
  var e = lu(), r = jp(), t = qr();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return oo = n, oo;
}
var uo, Yf;
function Rm() {
  if (Yf) return uo;
  Yf = 1;
  var e = lu(), r = or(), t = jp();
  function n(a, i) {
    return a && a.length ? e(a, r(i, 2), t) : void 0;
  }
  return uo = n, uo;
}
var lo, Jf;
function Tm() {
  if (Jf) return lo;
  Jf = 1;
  var e = Ve(), r = function() {
    return e.Date.now();
  };
  return lo = r, lo;
}
var co, Xf;
function xm() {
  if (Xf) return co;
  Xf = 1;
  var e = wt(), r = Tt(), t = It(), n = Fe(), a = Zr();
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
  return co = i, co;
}
var fo, Zf;
function km() {
  if (Zf) return fo;
  Zf = 1;
  var e = xt(), r = xm(), t = Tt();
  function n(a, i, s) {
    for (var o = -1, u = i.length, c = {}; ++o < u; ) {
      var l = i[o], d = e(a, l);
      s(d, l) && r(c, t(l, a), d);
    }
    return c;
  }
  return fo = n, fo;
}
var ho, eh;
function Am() {
  if (eh) return ho;
  eh = 1;
  var e = km(), r = yp();
  function t(n, a) {
    return e(n, a, function(i, s) {
      return r(n, s);
    });
  }
  return ho = t, ho;
}
var po, rh;
function Pm() {
  if (rh) return po;
  rh = 1;
  var e = Mp(), r = Ep(), t = Sp();
  function n(a) {
    return t(r(a, void 0, e), a + "");
  }
  return po = n, po;
}
var vo, th;
function Om() {
  if (th) return vo;
  th = 1;
  var e = Am(), r = Pm(), t = r(function(n, a) {
    return n == null ? {} : e(n, a);
  });
  return vo = t, vo;
}
var go, nh;
function Nm() {
  if (nh) return go;
  nh = 1;
  var e = Math.ceil, r = Math.max;
  function t(n, a, i, s) {
    for (var o = -1, u = r(e((a - n) / (i || 1)), 0), c = Array(u); u--; )
      c[s ? u : ++o] = n, n += i;
    return c;
  }
  return go = t, go;
}
var yo, ah;
function Dm() {
  if (ah) return yo;
  ah = 1;
  var e = Nm(), r = At(), t = Dp();
  function n(a) {
    return function(i, s, o) {
      return o && typeof o != "number" && r(i, s, o) && (s = o = void 0), i = t(i), s === void 0 ? (s = i, i = 0) : s = t(s), o = o === void 0 ? i < s ? 1 : -1 : t(o), e(i, s, o, a);
    };
  }
  return yo = n, yo;
}
var mo, ih;
function Mm() {
  if (ih) return mo;
  ih = 1;
  var e = Dm(), r = e();
  return mo = r, mo;
}
var bo, sh;
function Lm() {
  if (sh) return bo;
  sh = 1;
  function e(r, t) {
    var n = r.length;
    for (r.sort(t); n--; )
      r[n] = r[n].value;
    return r;
  }
  return bo = e, bo;
}
var _o, oh;
function $m() {
  if (oh) return _o;
  oh = 1;
  var e = Mr();
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
  return _o = r, _o;
}
var wo, uh;
function jm() {
  if (uh) return wo;
  uh = 1;
  var e = $m();
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
  return wo = r, wo;
}
var Io, lh;
function Fm() {
  if (lh) return Io;
  lh = 1;
  var e = Rt(), r = xt(), t = or(), n = Ip(), a = Lm(), i = qt(), s = jm(), o = qr(), u = qe();
  function c(l, d, p) {
    d.length ? d = e(d, function(h) {
      return u(h) ? function(y) {
        return r(y, h.length === 1 ? h[0] : h);
      } : h;
    }) : d = [o];
    var v = -1;
    d = e(d, i(t));
    var g = n(l, function(h, y, C) {
      var x = e(d, function(_) {
        return _(h);
      });
      return { criteria: x, index: ++v, value: h };
    });
    return a(g, function(h, y) {
      return s(h, y, p);
    });
  }
  return Io = c, Io;
}
var qo, ch;
function Gm() {
  if (ch) return qo;
  ch = 1;
  var e = ou(), r = Fm(), t = kt(), n = At(), a = t(function(i, s) {
    if (i == null)
      return [];
    var o = s.length;
    return o > 1 && n(i, s[0], s[1]) ? s = [] : o > 2 && n(s[0], s[1], s[2]) && (s = [s[0]]), r(i, e(s, 1), []);
  });
  return qo = a, qo;
}
var Co, dh;
function zm() {
  if (dh) return Co;
  dh = 1;
  var e = vp(), r = 0;
  function t(n) {
    var a = ++r;
    return e(n) + a;
  }
  return Co = t, Co;
}
var Eo, fh;
function Bm() {
  if (fh) return Eo;
  fh = 1;
  function e(r, t, n) {
    for (var a = -1, i = r.length, s = t.length, o = {}; ++a < i; ) {
      var u = a < s ? t[a] : void 0;
      n(o, r[a], u);
    }
    return o;
  }
  return Eo = e, Eo;
}
var So, hh;
function Um() {
  if (hh) return So;
  hh = 1;
  var e = wt(), r = Bm();
  function t(n, a) {
    return r(n || [], a || [], e);
  }
  return So = t, So;
}
var ct;
if (typeof Vo == "function")
  try {
    ct = {
      cloneDeep: sm(),
      constant: tu(),
      defaults: om(),
      each: up(),
      filter: bp(),
      find: pm(),
      flatten: Mp(),
      forEach: op(),
      forIn: vm(),
      has: _p(),
      isUndefined: wp(),
      last: gm(),
      map: qp(),
      mapValues: ym(),
      max: bm(),
      merge: Em(),
      min: Sm(),
      minBy: Rm(),
      now: Tm(),
      pick: Om(),
      range: Mm(),
      reduce: Cp(),
      sortBy: Gm(),
      uniqueId: zm(),
      values: xp(),
      zipObject: Um()
    };
  } catch {
  }
ct || (ct = window._);
var be = ct, Km = Pt;
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
    e.push(JSON.stringify(t, Vm)), t = t._prev;
  return "[" + e.join(", ") + "]";
};
function Fp(e) {
  e._prev._next = e._next, e._next._prev = e._prev, delete e._next, delete e._prev;
}
function Vm(e, r) {
  if (e !== "_next" && e !== "_prev")
    return r;
}
var nr = be, Hm = He.Graph, Wm = Km, Qm = Jm, Ym = nr.constant(1);
function Jm(e, r) {
  if (e.nodeCount() <= 1)
    return [];
  var t = Zm(e, r || Ym), n = Xm(t.graph, t.buckets, t.zeroIdx);
  return nr.flatten(nr.map(n, function(a) {
    return e.outEdges(a.v, a.w);
  }), !0);
}
function Xm(e, r, t) {
  for (var n = [], a = r[r.length - 1], i = r[0], s; e.nodeCount(); ) {
    for (; s = i.dequeue(); )
      Ro(e, r, t, s);
    for (; s = a.dequeue(); )
      Ro(e, r, t, s);
    if (e.nodeCount()) {
      for (var o = r.length - 2; o > 0; --o)
        if (s = r[o].dequeue(), s) {
          n = n.concat(Ro(e, r, t, s, !0));
          break;
        }
    }
  }
  return n;
}
function Ro(e, r, t, n, a) {
  var i = a ? [] : void 0;
  return nr.forEach(e.inEdges(n.v), function(s) {
    var o = e.edge(s), u = e.node(s.v);
    a && i.push({ v: s.v, w: s.w }), u.out -= o, zo(r, t, u);
  }), nr.forEach(e.outEdges(n.v), function(s) {
    var o = e.edge(s), u = s.w, c = e.node(u);
    c.in -= o, zo(r, t, c);
  }), e.removeNode(n.v), i;
}
function Zm(e, r) {
  var t = new Hm(), n = 0, a = 0;
  nr.forEach(e.nodes(), function(o) {
    t.setNode(o, { v: o, in: 0, out: 0 });
  }), nr.forEach(e.edges(), function(o) {
    var u = t.edge(o.v, o.w) || 0, c = r(o), l = u + c;
    t.setEdge(o.v, o.w, l), a = Math.max(a, t.node(o.v).out += c), n = Math.max(n, t.node(o.w).in += c);
  });
  var i = nr.range(a + n + 3).map(function() {
    return new Wm();
  }), s = n + 1;
  return nr.forEach(t.nodes(), function(o) {
    zo(i, s, t.node(o));
  }), { graph: t, buckets: i, zeroIdx: s };
}
function zo(e, r, t) {
  t.out ? t.in ? e[t.out - t.in + r].enqueue(t) : e[e.length - 1].enqueue(t) : e[0].enqueue(t);
}
var mr = be, eb = Qm, rb = {
  run: tb,
  undo: ab
};
function tb(e) {
  var r = e.graph().acyclicer === "greedy" ? eb(e, t(e)) : nb(e);
  mr.forEach(r, function(n) {
    var a = e.edge(n);
    e.removeEdge(n), a.forwardName = n.name, a.reversed = !0, e.setEdge(n.w, n.v, a, mr.uniqueId("rev"));
  });
  function t(n) {
    return function(a) {
      return n.edge(a).weight;
    };
  }
}
function nb(e) {
  var r = [], t = {}, n = {};
  function a(i) {
    mr.has(n, i) || (n[i] = !0, t[i] = !0, mr.forEach(e.outEdges(i), function(s) {
      mr.has(t, s.w) ? r.push(s) : a(s.w);
    }), delete t[i]);
  }
  return mr.forEach(e.nodes(), a), r;
}
function ab(e) {
  mr.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (t.reversed) {
      e.removeEdge(r);
      var n = t.forwardName;
      delete t.reversed, delete t.forwardName, e.setEdge(r.w, r.v, t, n);
    }
  });
}
var de = be, Gp = He.Graph, Oe = {
  addDummyNode: zp,
  simplify: ib,
  asNonCompoundGraph: sb,
  successorWeights: ob,
  predecessorWeights: ub,
  intersectRect: lb,
  buildLayerMatrix: cb,
  normalizeRanks: db,
  removeEmptyRanks: fb,
  addBorderNode: hb,
  maxRank: Bp,
  partition: pb,
  time: vb,
  notime: gb
};
function zp(e, r, t, n) {
  var a;
  do
    a = de.uniqueId(n);
  while (e.hasNode(a));
  return t.dummy = r, e.setNode(a, t), a;
}
function ib(e) {
  var r = new Gp().setGraph(e.graph());
  return de.forEach(e.nodes(), function(t) {
    r.setNode(t, e.node(t));
  }), de.forEach(e.edges(), function(t) {
    var n = r.edge(t.v, t.w) || { weight: 0, minlen: 1 }, a = e.edge(t);
    r.setEdge(t.v, t.w, {
      weight: n.weight + a.weight,
      minlen: Math.max(n.minlen, a.minlen)
    });
  }), r;
}
function sb(e) {
  var r = new Gp({ multigraph: e.isMultigraph() }).setGraph(e.graph());
  return de.forEach(e.nodes(), function(t) {
    e.children(t).length || r.setNode(t, e.node(t));
  }), de.forEach(e.edges(), function(t) {
    r.setEdge(t, e.edge(t));
  }), r;
}
function ob(e) {
  var r = de.map(e.nodes(), function(t) {
    var n = {};
    return de.forEach(e.outEdges(t), function(a) {
      n[a.w] = (n[a.w] || 0) + e.edge(a).weight;
    }), n;
  });
  return de.zipObject(e.nodes(), r);
}
function ub(e) {
  var r = de.map(e.nodes(), function(t) {
    var n = {};
    return de.forEach(e.inEdges(t), function(a) {
      n[a.v] = (n[a.v] || 0) + e.edge(a).weight;
    }), n;
  });
  return de.zipObject(e.nodes(), r);
}
function lb(e, r) {
  var t = e.x, n = e.y, a = r.x - t, i = r.y - n, s = e.width / 2, o = e.height / 2;
  if (!a && !i)
    throw new Error("Not possible to find intersection inside of the rectangle");
  var u, c;
  return Math.abs(i) * s > Math.abs(a) * o ? (i < 0 && (o = -o), u = o * a / i, c = o) : (a < 0 && (s = -s), u = s, c = s * i / a), { x: t + u, y: n + c };
}
function cb(e) {
  var r = de.map(de.range(Bp(e) + 1), function() {
    return [];
  });
  return de.forEach(e.nodes(), function(t) {
    var n = e.node(t), a = n.rank;
    de.isUndefined(a) || (r[a][n.order] = t);
  }), r;
}
function db(e) {
  var r = de.min(de.map(e.nodes(), function(t) {
    return e.node(t).rank;
  }));
  de.forEach(e.nodes(), function(t) {
    var n = e.node(t);
    de.has(n, "rank") && (n.rank -= r);
  });
}
function fb(e) {
  var r = de.min(de.map(e.nodes(), function(i) {
    return e.node(i).rank;
  })), t = [];
  de.forEach(e.nodes(), function(i) {
    var s = e.node(i).rank - r;
    t[s] || (t[s] = []), t[s].push(i);
  });
  var n = 0, a = e.graph().nodeRankFactor;
  de.forEach(t, function(i, s) {
    de.isUndefined(i) && s % a !== 0 ? --n : n && de.forEach(i, function(o) {
      e.node(o).rank += n;
    });
  });
}
function hb(e, r, t, n) {
  var a = {
    width: 0,
    height: 0
  };
  return arguments.length >= 4 && (a.rank = t, a.order = n), zp(e, "border", a, r);
}
function Bp(e) {
  return de.max(de.map(e.nodes(), function(r) {
    var t = e.node(r).rank;
    if (!de.isUndefined(t))
      return t;
  }));
}
function pb(e, r) {
  var t = { lhs: [], rhs: [] };
  return de.forEach(e, function(n) {
    r(n) ? t.lhs.push(n) : t.rhs.push(n);
  }), t;
}
function vb(e, r) {
  var t = de.now();
  try {
    return r();
  } finally {
    console.log(e + " time: " + (de.now() - t) + "ms");
  }
}
function gb(e, r) {
  return r();
}
var Up = be, yb = Oe, mb = {
  run: bb,
  undo: wb
};
function bb(e) {
  e.graph().dummyChains = [], Up.forEach(e.edges(), function(r) {
    _b(e, r);
  });
}
function _b(e, r) {
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
      }, c = yb.addDummyNode(e, "edge", l, "_d"), n === u && (l.width = o.width, l.height = o.height, l.dummy = "edge-label", l.labelpos = o.labelpos), e.setEdge(t, c, { weight: o.weight }, s), d === 0 && e.graph().dummyChains.push(c), t = c;
    e.setEdge(t, a, { weight: o.weight }, s);
  }
}
function wb(e) {
  Up.forEach(e.graph().dummyChains, function(r) {
    var t = e.node(r), n = t.edgeLabel, a;
    for (e.setEdge(t.edgeObj, n); t.dummy; )
      a = e.successors(r)[0], e.removeNode(r), n.points.push({ x: t.x, y: t.y }), t.dummy === "edge-label" && (n.x = t.x, n.y = t.y, n.width = t.width, n.height = t.height), r = a, t = e.node(r);
  });
}
var nt = be, Ot = {
  longestPath: Ib,
  slack: qb
};
function Ib(e) {
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
function qb(e, r) {
  return e.node(r.w).rank - e.node(r.v).rank - e.edge(r).minlen;
}
var dt = be, Cb = He.Graph, ft = Ot.slack, Kp = Eb;
function Eb(e) {
  var r = new Cb({ directed: !1 }), t = e.nodes()[0], n = e.nodeCount();
  r.setNode(t, {});
  for (var a, i; Sb(r, e) < n; )
    a = Rb(r, e), i = r.hasNode(a.v) ? ft(e, a) : -ft(e, a), Tb(r, e, i);
  return r;
}
function Sb(e, r) {
  function t(n) {
    dt.forEach(r.nodeEdges(n), function(a) {
      var i = a.v, s = n === i ? a.w : i;
      !e.hasNode(s) && !ft(r, a) && (e.setNode(s, {}), e.setEdge(n, s, {}), t(s));
    });
  }
  return dt.forEach(e.nodes(), t), e.nodeCount();
}
function Rb(e, r) {
  return dt.minBy(r.edges(), function(t) {
    if (e.hasNode(t.v) !== e.hasNode(t.w))
      return ft(r, t);
  });
}
function Tb(e, r, t) {
  dt.forEach(e.nodes(), function(n) {
    r.node(n).rank += t;
  });
}
var ir = be, xb = Kp, kb = Ot.slack, Ab = Ot.longestPath, Pb = He.alg.preorder, Ob = He.alg.postorder, Nb = Oe.simplify, Db = Cr;
Cr.initLowLimValues = du;
Cr.initCutValues = cu;
Cr.calcCutValue = Vp;
Cr.leaveEdge = Wp;
Cr.enterEdge = Qp;
Cr.exchangeEdges = Yp;
function Cr(e) {
  e = Nb(e), Ab(e);
  var r = xb(e);
  du(r), cu(r, e);
  for (var t, n; t = Wp(r); )
    n = Qp(r, e, t), Yp(r, e, t, n);
}
function cu(e, r) {
  var t = Ob(e, e.nodes());
  t = t.slice(0, t.length - 1), ir.forEach(t, function(n) {
    Mb(e, r, n);
  });
}
function Mb(e, r, t) {
  var n = e.node(t), a = n.parent;
  e.edge(t, a).cutvalue = Vp(e, r, t);
}
function Vp(e, r, t) {
  var n = e.node(t), a = n.parent, i = !0, s = r.edge(t, a), o = 0;
  return s || (i = !1, s = r.edge(a, t)), o = s.weight, ir.forEach(r.nodeEdges(t), function(u) {
    var c = u.v === t, l = c ? u.w : u.v;
    if (l !== a) {
      var d = c === i, p = r.edge(u).weight;
      if (o += d ? p : -p, $b(e, t, l)) {
        var v = e.edge(t, l).cutvalue;
        o += d ? -v : v;
      }
    }
  }), o;
}
function du(e, r) {
  arguments.length < 2 && (r = e.nodes()[0]), Hp(e, {}, 1, r);
}
function Hp(e, r, t, n, a) {
  var i = t, s = e.node(n);
  return r[n] = !0, ir.forEach(e.neighbors(n), function(o) {
    ir.has(r, o) || (t = Hp(e, r, t, o, n));
  }), s.low = i, s.lim = t++, a ? s.parent = a : delete s.parent, t;
}
function Wp(e) {
  return ir.find(e.edges(), function(r) {
    return e.edge(r).cutvalue < 0;
  });
}
function Qp(e, r, t) {
  var n = t.v, a = t.w;
  r.hasEdge(n, a) || (n = t.w, a = t.v);
  var i = e.node(n), s = e.node(a), o = i, u = !1;
  i.lim > s.lim && (o = s, u = !0);
  var c = ir.filter(r.edges(), function(l) {
    return u === ph(e, e.node(l.v), o) && u !== ph(e, e.node(l.w), o);
  });
  return ir.minBy(c, function(l) {
    return kb(r, l);
  });
}
function Yp(e, r, t, n) {
  var a = t.v, i = t.w;
  e.removeEdge(a, i), e.setEdge(n.v, n.w, {}), du(e), cu(e, r), Lb(e, r);
}
function Lb(e, r) {
  var t = ir.find(e.nodes(), function(a) {
    return !r.node(a).parent;
  }), n = Pb(e, t);
  n = n.slice(1), ir.forEach(n, function(a) {
    var i = e.node(a).parent, s = r.edge(a, i), o = !1;
    s || (s = r.edge(i, a), o = !0), r.node(a).rank = r.node(i).rank + (o ? s.minlen : -s.minlen);
  });
}
function $b(e, r, t) {
  return e.hasEdge(r, t);
}
function ph(e, r, t) {
  return t.low <= r.lim && r.lim <= t.lim;
}
var jb = Ot, Jp = jb.longestPath, Fb = Kp, Gb = Db, zb = Bb;
function Bb(e) {
  switch (e.graph().ranker) {
    case "network-simplex":
      vh(e);
      break;
    case "tight-tree":
      Kb(e);
      break;
    case "longest-path":
      Ub(e);
      break;
    default:
      vh(e);
  }
}
var Ub = Jp;
function Kb(e) {
  Jp(e), Fb(e);
}
function vh(e) {
  Gb(e);
}
var Bo = be, Vb = Hb;
function Hb(e) {
  var r = Qb(e);
  Bo.forEach(e.graph().dummyChains, function(t) {
    for (var n = e.node(t), a = n.edgeObj, i = Wb(e, r, a.v, a.w), s = i.path, o = i.lca, u = 0, c = s[u], l = !0; t !== a.w; ) {
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
function Wb(e, r, t, n) {
  var a = [], i = [], s = Math.min(r[t].low, r[n].low), o = Math.max(r[t].lim, r[n].lim), u, c;
  u = t;
  do
    u = e.parent(u), a.push(u);
  while (u && (r[u].low > s || o > r[u].lim));
  for (c = u, u = n; (u = e.parent(u)) !== c; )
    i.push(u);
  return { path: a.concat(i.reverse()), lca: c };
}
function Qb(e) {
  var r = {}, t = 0;
  function n(a) {
    var i = t;
    Bo.forEach(e.children(a), n), r[a] = { low: i, lim: t++ };
  }
  return Bo.forEach(e.children(), n), r;
}
var ar = be, Uo = Oe, Yb = {
  run: Jb,
  cleanup: e_
};
function Jb(e) {
  var r = Uo.addDummyNode(e, "root", {}, "_root"), t = Xb(e), n = ar.max(ar.values(t)) - 1, a = 2 * n + 1;
  e.graph().nestingRoot = r, ar.forEach(e.edges(), function(s) {
    e.edge(s).minlen *= a;
  });
  var i = Zb(e) + 1;
  ar.forEach(e.children(), function(s) {
    Xp(e, r, a, i, n, t, s);
  }), e.graph().nodeRankFactor = a;
}
function Xp(e, r, t, n, a, i, s) {
  var o = e.children(s);
  if (!o.length) {
    s !== r && e.setEdge(r, s, { weight: 0, minlen: t });
    return;
  }
  var u = Uo.addBorderNode(e, "_bt"), c = Uo.addBorderNode(e, "_bb"), l = e.node(s);
  e.setParent(u, s), l.borderTop = u, e.setParent(c, s), l.borderBottom = c, ar.forEach(o, function(d) {
    Xp(e, r, t, n, a, i, d);
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
function Xb(e) {
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
function Zb(e) {
  return ar.reduce(e.edges(), function(r, t) {
    return r + e.edge(t).weight;
  }, 0);
}
function e_(e) {
  var r = e.graph();
  e.removeNode(r.nestingRoot), delete r.nestingRoot, ar.forEach(e.edges(), function(t) {
    var n = e.edge(t);
    n.nestingEdge && e.removeEdge(t);
  });
}
var To = be, r_ = Oe, t_ = n_;
function n_(e) {
  function r(t) {
    var n = e.children(t), a = e.node(t);
    if (n.length && To.forEach(n, r), To.has(a, "minRank")) {
      a.borderLeft = [], a.borderRight = [];
      for (var i = a.minRank, s = a.maxRank + 1; i < s; ++i)
        gh(e, "borderLeft", "_bl", t, a, i), gh(e, "borderRight", "_br", t, a, i);
    }
  }
  To.forEach(e.children(), r);
}
function gh(e, r, t, n, a, i) {
  var s = { width: 0, height: 0, rank: i, borderType: r }, o = a[r][i - 1], u = r_.addDummyNode(e, "border", s, t);
  a[r][i] = u, e.setParent(u, n), o && e.setEdge(o, u, { weight: 1 });
}
var Je = be, a_ = {
  adjust: i_,
  undo: s_
};
function i_(e) {
  var r = e.graph().rankdir.toLowerCase();
  (r === "lr" || r === "rl") && Zp(e);
}
function s_(e) {
  var r = e.graph().rankdir.toLowerCase();
  (r === "bt" || r === "rl") && o_(e), (r === "lr" || r === "rl") && (u_(e), Zp(e));
}
function Zp(e) {
  Je.forEach(e.nodes(), function(r) {
    yh(e.node(r));
  }), Je.forEach(e.edges(), function(r) {
    yh(e.edge(r));
  });
}
function yh(e) {
  var r = e.width;
  e.width = e.height, e.height = r;
}
function o_(e) {
  Je.forEach(e.nodes(), function(r) {
    xo(e.node(r));
  }), Je.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    Je.forEach(t.points, xo), Je.has(t, "y") && xo(t);
  });
}
function xo(e) {
  e.y = -e.y;
}
function u_(e) {
  Je.forEach(e.nodes(), function(r) {
    ko(e.node(r));
  }), Je.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    Je.forEach(t.points, ko), Je.has(t, "x") && ko(t);
  });
}
function ko(e) {
  var r = e.x;
  e.x = e.y, e.y = r;
}
var rr = be, l_ = c_;
function c_(e) {
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
var lr = be, d_ = f_;
function f_(e, r) {
  for (var t = 0, n = 1; n < r.length; ++n)
    t += h_(e, r[n - 1], r[n]);
  return t;
}
function h_(e, r, t) {
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
var mh = be, p_ = v_;
function v_(e, r) {
  return mh.map(r, function(t) {
    var n = e.inEdges(t);
    if (n.length) {
      var a = mh.reduce(n, function(i, s) {
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
var Me = be, g_ = y_;
function y_(e, r) {
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
  return m_(n);
}
function m_(e) {
  var r = [];
  function t(i) {
    return function(s) {
      s.merged || (Me.isUndefined(s.barycenter) || Me.isUndefined(i.barycenter) || s.barycenter >= i.barycenter) && b_(i, s);
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
function b_(e, r) {
  var t = 0, n = 0;
  e.weight && (t += e.barycenter * e.weight, n += e.weight), r.weight && (t += r.barycenter * r.weight, n += r.weight), e.vs = r.vs.concat(e.vs), e.barycenter = t / n, e.weight = n, e.i = Math.min(r.i, e.i), r.merged = !0;
}
var Ur = be, __ = Oe, w_ = I_;
function I_(e, r) {
  var t = __.partition(e, function(l) {
    return Ur.has(l, "barycenter");
  }), n = t.lhs, a = Ur.sortBy(t.rhs, function(l) {
    return -l.i;
  }), i = [], s = 0, o = 0, u = 0;
  n.sort(q_(!!r)), u = bh(i, a, u), Ur.forEach(n, function(l) {
    u += l.vs.length, i.push(l.vs), s += l.barycenter * l.weight, o += l.weight, u = bh(i, a, u);
  });
  var c = { vs: Ur.flatten(i, !0) };
  return o && (c.barycenter = s / o, c.weight = o), c;
}
function bh(e, r, t) {
  for (var n; r.length && (n = Ur.last(r)).i <= t; )
    r.pop(), e.push(n.vs), t++;
  return t;
}
function q_(e) {
  return function(r, t) {
    return r.barycenter < t.barycenter ? -1 : r.barycenter > t.barycenter ? 1 : e ? t.i - r.i : r.i - t.i;
  };
}
var cr = be, C_ = p_, E_ = g_, S_ = w_, R_ = ev;
function ev(e, r, t, n) {
  var a = e.children(r), i = e.node(r), s = i ? i.borderLeft : void 0, o = i ? i.borderRight : void 0, u = {};
  s && (a = cr.filter(a, function(g) {
    return g !== s && g !== o;
  }));
  var c = C_(e, a);
  cr.forEach(c, function(g) {
    if (e.children(g.v).length) {
      var h = ev(e, g.v, t, n);
      u[g.v] = h, cr.has(h, "barycenter") && x_(g, h);
    }
  });
  var l = E_(c, t);
  T_(l, u);
  var d = S_(l, n);
  if (s && (d.vs = cr.flatten([s, d.vs, o], !0), e.predecessors(s).length)) {
    var p = e.node(e.predecessors(s)[0]), v = e.node(e.predecessors(o)[0]);
    cr.has(d, "barycenter") || (d.barycenter = 0, d.weight = 0), d.barycenter = (d.barycenter * d.weight + p.order + v.order) / (d.weight + 2), d.weight += 2;
  }
  return d;
}
function T_(e, r) {
  cr.forEach(e, function(t) {
    t.vs = cr.flatten(t.vs.map(function(n) {
      return r[n] ? r[n].vs : n;
    }), !0);
  });
}
function x_(e, r) {
  cr.isUndefined(e.barycenter) ? (e.barycenter = r.barycenter, e.weight = r.weight) : (e.barycenter = (e.barycenter * e.weight + r.barycenter * r.weight) / (e.weight + r.weight), e.weight += r.weight);
}
var Kr = be, k_ = He.Graph, A_ = P_;
function P_(e, r, t) {
  var n = O_(e), a = new k_({ compound: !0 }).setGraph({ root: n }).setDefaultNodeLabel(function(i) {
    return e.node(i);
  });
  return Kr.forEach(e.nodes(), function(i) {
    var s = e.node(i), o = e.parent(i);
    (s.rank === r || s.minRank <= r && r <= s.maxRank) && (a.setNode(i), a.setParent(i, o || n), Kr.forEach(e[t](i), function(u) {
      var c = u.v === i ? u.w : u.v, l = a.edge(c, i), d = Kr.isUndefined(l) ? 0 : l.weight;
      a.setEdge(c, i, { weight: e.edge(u).weight + d });
    }), Kr.has(s, "minRank") && a.setNode(i, {
      borderLeft: s.borderLeft[r],
      borderRight: s.borderRight[r]
    }));
  }), a;
}
function O_(e) {
  for (var r; e.hasNode(r = Kr.uniqueId("_root")); ) ;
  return r;
}
var N_ = be, D_ = M_;
function M_(e, r, t) {
  var n = {}, a;
  N_.forEach(t, function(i) {
    for (var s = e.parent(i), o, u; s; ) {
      if (o = e.parent(s), o ? (u = n[o], n[o] = s) : (u = a, a = s), u && u !== s) {
        r.setEdge(u, s);
        return;
      }
      s = o;
    }
  });
}
var fr = be, L_ = l_, $_ = d_, j_ = R_, F_ = A_, G_ = D_, z_ = He.Graph, _h = Oe, B_ = U_;
function U_(e) {
  var r = _h.maxRank(e), t = wh(e, fr.range(1, r + 1), "inEdges"), n = wh(e, fr.range(r - 1, -1, -1), "outEdges"), a = L_(e);
  Ih(e, a);
  for (var i = Number.POSITIVE_INFINITY, s, o = 0, u = 0; u < 4; ++o, ++u) {
    K_(o % 2 ? t : n, o % 4 >= 2), a = _h.buildLayerMatrix(e);
    var c = $_(e, a);
    c < i && (u = 0, s = fr.cloneDeep(a), i = c);
  }
  Ih(e, s);
}
function wh(e, r, t) {
  return fr.map(r, function(n) {
    return F_(e, n, t);
  });
}
function K_(e, r) {
  var t = new z_();
  fr.forEach(e, function(n) {
    var a = n.graph().root, i = j_(n, a, t, r);
    fr.forEach(i.vs, function(s, o) {
      n.node(s).order = o;
    }), G_(n, t, i.vs);
  });
}
function Ih(e, r) {
  fr.forEach(r, function(t) {
    fr.forEach(t, function(n, a) {
      e.node(n).order = a;
    });
  });
}
var Z = be, V_ = He.Graph, H_ = Oe, W_ = {
  positionX: iw
};
function Q_(e, r) {
  var t = {};
  function n(a, i) {
    var s = 0, o = 0, u = a.length, c = Z.last(i);
    return Z.forEach(i, function(l, d) {
      var p = J_(e, l), v = p ? e.node(p).order : u;
      (p || l === c) && (Z.forEach(i.slice(o, d + 1), function(g) {
        Z.forEach(e.predecessors(g), function(h) {
          var y = e.node(h), C = y.order;
          (C < s || v < C) && !(y.dummy && e.node(g).dummy) && rv(t, h, g);
        });
      }), o = d + 1, s = v);
    }), i;
  }
  return Z.reduce(r, n), t;
}
function Y_(e, r) {
  var t = {};
  function n(i, s, o, u, c) {
    var l;
    Z.forEach(Z.range(s, o), function(d) {
      l = i[d], e.node(l).dummy && Z.forEach(e.predecessors(l), function(p) {
        var v = e.node(p);
        v.dummy && (v.order < u || v.order > c) && rv(t, p, l);
      });
    });
  }
  function a(i, s) {
    var o = -1, u, c = 0;
    return Z.forEach(s, function(l, d) {
      if (e.node(l).dummy === "border") {
        var p = e.predecessors(l);
        p.length && (u = e.node(p[0]).order, n(s, c, d, o, u), c = d, o = u);
      }
      n(s, c, s.length, u, i.length);
    }), s;
  }
  return Z.reduce(r, a), t;
}
function J_(e, r) {
  if (e.node(r).dummy)
    return Z.find(e.predecessors(r), function(t) {
      return e.node(t).dummy;
    });
}
function rv(e, r, t) {
  if (r > t) {
    var n = r;
    r = t, t = n;
  }
  var a = e[r];
  a || (e[r] = a = {}), a[t] = !0;
}
function X_(e, r, t) {
  if (r > t) {
    var n = r;
    r = t, t = n;
  }
  return Z.has(e[r], t);
}
function Z_(e, r, t, n) {
  var a = {}, i = {}, s = {};
  return Z.forEach(r, function(o) {
    Z.forEach(o, function(u, c) {
      a[u] = u, i[u] = u, s[u] = c;
    });
  }), Z.forEach(r, function(o) {
    var u = -1;
    Z.forEach(o, function(c) {
      var l = n(c);
      if (l.length) {
        l = Z.sortBy(l, function(h) {
          return s[h];
        });
        for (var d = (l.length - 1) / 2, p = Math.floor(d), v = Math.ceil(d); p <= v; ++p) {
          var g = l[p];
          i[c] === c && u < s[g] && !X_(t, c, g) && (i[g] = c, i[c] = a[c] = a[g], u = s[g]);
        }
      }
    });
  }), { root: a, align: i };
}
function ew(e, r, t, n, a) {
  var i = {}, s = rw(e, r, t, a), o = a ? "borderLeft" : "borderRight";
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
  return u(c, s.predecessors.bind(s)), u(l, s.successors.bind(s)), Z.forEach(n, function(d) {
    i[d] = i[t[d]];
  }), i;
}
function rw(e, r, t, n) {
  var a = new V_(), i = e.graph(), s = sw(i.nodesep, i.edgesep, n);
  return Z.forEach(r, function(o) {
    var u;
    Z.forEach(o, function(c) {
      var l = t[c];
      if (a.setNode(l), u) {
        var d = t[u], p = a.edge(d, l);
        a.setEdge(d, l, Math.max(s(e, c, u), p || 0));
      }
      u = c;
    });
  }), a;
}
function tw(e, r) {
  return Z.minBy(Z.values(r), function(t) {
    var n = Number.NEGATIVE_INFINITY, a = Number.POSITIVE_INFINITY;
    return Z.forIn(t, function(i, s) {
      var o = ow(e, s) / 2;
      n = Math.max(i + o, n), a = Math.min(i - o, a);
    }), n - a;
  });
}
function nw(e, r) {
  var t = Z.values(r), n = Z.min(t), a = Z.max(t);
  Z.forEach(["u", "d"], function(i) {
    Z.forEach(["l", "r"], function(s) {
      var o = i + s, u = e[o], c;
      if (u !== r) {
        var l = Z.values(u);
        c = s === "l" ? n - Z.min(l) : a - Z.max(l), c && (e[o] = Z.mapValues(u, function(d) {
          return d + c;
        }));
      }
    });
  });
}
function aw(e, r) {
  return Z.mapValues(e.ul, function(t, n) {
    if (r)
      return e[r.toLowerCase()][n];
    var a = Z.sortBy(Z.map(e, n));
    return (a[1] + a[2]) / 2;
  });
}
function iw(e) {
  var r = H_.buildLayerMatrix(e), t = Z.merge(
    Q_(e, r),
    Y_(e, r)
  ), n = {}, a;
  Z.forEach(["u", "d"], function(s) {
    a = s === "u" ? r : Z.values(r).reverse(), Z.forEach(["l", "r"], function(o) {
      o === "r" && (a = Z.map(a, function(d) {
        return Z.values(d).reverse();
      }));
      var u = (s === "u" ? e.predecessors : e.successors).bind(e), c = Z_(e, a, t, u), l = ew(
        e,
        a,
        c.root,
        c.align,
        o === "r"
      );
      o === "r" && (l = Z.mapValues(l, function(d) {
        return -d;
      })), n[s + o] = l;
    });
  });
  var i = tw(e, n);
  return nw(n, i), aw(n, e.graph().align);
}
function sw(e, r, t) {
  return function(n, a, i) {
    var s = n.node(a), o = n.node(i), u = 0, c;
    if (u += s.width / 2, Z.has(s, "labelpos"))
      switch (s.labelpos.toLowerCase()) {
        case "l":
          c = -s.width / 2;
          break;
        case "r":
          c = s.width / 2;
          break;
      }
    if (c && (u += t ? c : -c), c = 0, u += (s.dummy ? r : e) / 2, u += (o.dummy ? r : e) / 2, u += o.width / 2, Z.has(o, "labelpos"))
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
function ow(e, r) {
  return e.node(r).width;
}
var Vr = be, tv = Oe, uw = W_.positionX, lw = cw;
function cw(e) {
  e = tv.asNonCompoundGraph(e), dw(e), Vr.forEach(uw(e), function(r, t) {
    e.node(t).x = r;
  });
}
function dw(e) {
  var r = tv.buildLayerMatrix(e), t = e.graph().ranksep, n = 0;
  Vr.forEach(r, function(a) {
    var i = Vr.max(Vr.map(a, function(s) {
      return e.node(s).height;
    }));
    Vr.forEach(a, function(s) {
      e.node(s).y = n + i / 2;
    }), n += i + t;
  });
}
var re = be, qh = rb, Ch = mb, fw = zb, hw = Oe.normalizeRanks, pw = Vb, vw = Oe.removeEmptyRanks, Eh = Yb, gw = t_, Sh = a_, yw = B_, mw = lw, hr = Oe, bw = He.Graph, _w = ww;
function ww(e, r) {
  var t = r && r.debugTiming ? hr.time : hr.notime;
  t("layout", function() {
    var n = t("  buildLayoutGraph", function() {
      return Pw(e);
    });
    t("  runLayout", function() {
      Iw(n, t);
    }), t("  updateInputGraph", function() {
      qw(e, n);
    });
  });
}
function Iw(e, r) {
  r("    makeSpaceForEdgeLabels", function() {
    Ow(e);
  }), r("    removeSelfEdges", function() {
    zw(e);
  }), r("    acyclic", function() {
    qh.run(e);
  }), r("    nestingGraph.run", function() {
    Eh.run(e);
  }), r("    rank", function() {
    fw(hr.asNonCompoundGraph(e));
  }), r("    injectEdgeLabelProxies", function() {
    Nw(e);
  }), r("    removeEmptyRanks", function() {
    vw(e);
  }), r("    nestingGraph.cleanup", function() {
    Eh.cleanup(e);
  }), r("    normalizeRanks", function() {
    hw(e);
  }), r("    assignRankMinMax", function() {
    Dw(e);
  }), r("    removeEdgeLabelProxies", function() {
    Mw(e);
  }), r("    normalize.run", function() {
    Ch.run(e);
  }), r("    parentDummyChains", function() {
    pw(e);
  }), r("    addBorderSegments", function() {
    gw(e);
  }), r("    order", function() {
    yw(e);
  }), r("    insertSelfEdges", function() {
    Bw(e);
  }), r("    adjustCoordinateSystem", function() {
    Sh.adjust(e);
  }), r("    position", function() {
    mw(e);
  }), r("    positionSelfEdges", function() {
    Uw(e);
  }), r("    removeBorderNodes", function() {
    Gw(e);
  }), r("    normalize.undo", function() {
    Ch.undo(e);
  }), r("    fixupEdgeLabelCoords", function() {
    jw(e);
  }), r("    undoCoordinateSystem", function() {
    Sh.undo(e);
  }), r("    translateGraph", function() {
    Lw(e);
  }), r("    assignNodeIntersects", function() {
    $w(e);
  }), r("    reversePoints", function() {
    Fw(e);
  }), r("    acyclic.undo", function() {
    qh.undo(e);
  });
}
function qw(e, r) {
  re.forEach(e.nodes(), function(t) {
    var n = e.node(t), a = r.node(t);
    n && (n.x = a.x, n.y = a.y, r.children(t).length && (n.width = a.width, n.height = a.height));
  }), re.forEach(e.edges(), function(t) {
    var n = e.edge(t), a = r.edge(t);
    n.points = a.points, re.has(a, "x") && (n.x = a.x, n.y = a.y);
  }), e.graph().width = r.graph().width, e.graph().height = r.graph().height;
}
var Cw = ["nodesep", "edgesep", "ranksep", "marginx", "marginy"], Ew = { ranksep: 50, edgesep: 20, nodesep: 50, rankdir: "tb" }, Sw = ["acyclicer", "ranker", "rankdir", "align"], Rw = ["width", "height"], Tw = { width: 0, height: 0 }, xw = ["minlen", "weight", "width", "height", "labeloffset"], kw = {
  minlen: 1,
  weight: 1,
  width: 0,
  height: 0,
  labeloffset: 10,
  labelpos: "r"
}, Aw = ["labelpos"];
function Pw(e) {
  var r = new bw({ multigraph: !0, compound: !0 }), t = Po(e.graph());
  return r.setGraph(re.merge(
    {},
    Ew,
    Ao(t, Cw),
    re.pick(t, Sw)
  )), re.forEach(e.nodes(), function(n) {
    var a = Po(e.node(n));
    r.setNode(n, re.defaults(Ao(a, Rw), Tw)), r.setParent(n, e.parent(n));
  }), re.forEach(e.edges(), function(n) {
    var a = Po(e.edge(n));
    r.setEdge(n, re.merge(
      {},
      kw,
      Ao(a, xw),
      re.pick(a, Aw)
    ));
  }), r;
}
function Ow(e) {
  var r = e.graph();
  r.ranksep /= 2, re.forEach(e.edges(), function(t) {
    var n = e.edge(t);
    n.minlen *= 2, n.labelpos.toLowerCase() !== "c" && (r.rankdir === "TB" || r.rankdir === "BT" ? n.width += n.labeloffset : n.height += n.labeloffset);
  });
}
function Nw(e) {
  re.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (t.width && t.height) {
      var n = e.node(r.v), a = e.node(r.w), i = { rank: (a.rank - n.rank) / 2 + n.rank, e: r };
      hr.addDummyNode(e, "edge-proxy", i, "_ep");
    }
  });
}
function Dw(e) {
  var r = 0;
  re.forEach(e.nodes(), function(t) {
    var n = e.node(t);
    n.borderTop && (n.minRank = e.node(n.borderTop).rank, n.maxRank = e.node(n.borderBottom).rank, r = re.max(r, n.maxRank));
  }), e.graph().maxRank = r;
}
function Mw(e) {
  re.forEach(e.nodes(), function(r) {
    var t = e.node(r);
    t.dummy === "edge-proxy" && (e.edge(t.e).labelRank = t.rank, e.removeNode(r));
  });
}
function Lw(e) {
  var r = Number.POSITIVE_INFINITY, t = 0, n = Number.POSITIVE_INFINITY, a = 0, i = e.graph(), s = i.marginx || 0, o = i.marginy || 0;
  function u(c) {
    var l = c.x, d = c.y, p = c.width, v = c.height;
    r = Math.min(r, l - p / 2), t = Math.max(t, l + p / 2), n = Math.min(n, d - v / 2), a = Math.max(a, d + v / 2);
  }
  re.forEach(e.nodes(), function(c) {
    u(e.node(c));
  }), re.forEach(e.edges(), function(c) {
    var l = e.edge(c);
    re.has(l, "x") && u(l);
  }), r -= s, n -= o, re.forEach(e.nodes(), function(c) {
    var l = e.node(c);
    l.x -= r, l.y -= n;
  }), re.forEach(e.edges(), function(c) {
    var l = e.edge(c);
    re.forEach(l.points, function(d) {
      d.x -= r, d.y -= n;
    }), re.has(l, "x") && (l.x -= r), re.has(l, "y") && (l.y -= n);
  }), i.width = t - r + s, i.height = a - n + o;
}
function $w(e) {
  re.forEach(e.edges(), function(r) {
    var t = e.edge(r), n = e.node(r.v), a = e.node(r.w), i, s;
    t.points ? (i = t.points[0], s = t.points[t.points.length - 1]) : (t.points = [], i = a, s = n), t.points.unshift(hr.intersectRect(n, i)), t.points.push(hr.intersectRect(a, s));
  });
}
function jw(e) {
  re.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (re.has(t, "x"))
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
function Fw(e) {
  re.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    t.reversed && t.points.reverse();
  });
}
function Gw(e) {
  re.forEach(e.nodes(), function(r) {
    if (e.children(r).length) {
      var t = e.node(r), n = e.node(t.borderTop), a = e.node(t.borderBottom), i = e.node(re.last(t.borderLeft)), s = e.node(re.last(t.borderRight));
      t.width = Math.abs(s.x - i.x), t.height = Math.abs(a.y - n.y), t.x = i.x + t.width / 2, t.y = n.y + t.height / 2;
    }
  }), re.forEach(e.nodes(), function(r) {
    e.node(r).dummy === "border" && e.removeNode(r);
  });
}
function zw(e) {
  re.forEach(e.edges(), function(r) {
    if (r.v === r.w) {
      var t = e.node(r.v);
      t.selfEdges || (t.selfEdges = []), t.selfEdges.push({ e: r, label: e.edge(r) }), e.removeEdge(r);
    }
  });
}
function Bw(e) {
  var r = hr.buildLayerMatrix(e);
  re.forEach(r, function(t) {
    var n = 0;
    re.forEach(t, function(a, i) {
      var s = e.node(a);
      s.order = i + n, re.forEach(s.selfEdges, function(o) {
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
function Uw(e) {
  re.forEach(e.nodes(), function(r) {
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
function Ao(e, r) {
  return re.mapValues(re.pick(e, r), Number);
}
function Po(e) {
  var r = {};
  return re.forEach(e, function(t, n) {
    r[n.toLowerCase()] = t;
  }), r;
}
var at = be, Kw = Oe, Vw = He.Graph, Hw = {
  debugOrdering: Ww
};
function Ww(e) {
  var r = Kw.buildLayerMatrix(e), t = new Vw({ compound: !0, multigraph: !0 }).setGraph({});
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
var Qw = "0.8.5", Yw = {
  graphlib: He,
  layout: _w,
  debug: Hw,
  util: {
    time: Oe.time,
    notime: Oe.notime
  },
  version: Qw
};
const Rh = /* @__PURE__ */ eg(Yw), Hr = 148, Wr = 46, fu = 80, hu = 44;
function Jw(e) {
  if (e.nodes.length === 0) return e;
  const r = new Rh.graphlib.Graph();
  r.setDefaultEdgeLabel(() => ({})), r.setGraph({ rankdir: "LR", nodesep: 60, ranksep: 120, marginx: 40, marginy: 40 });
  for (const n of e.nodes) r.setNode(n.id, { width: Hr, height: Wr });
  for (const n of e.edges) r.setEdge(n.source, n.target);
  Rh.layout(r);
  const t = {};
  for (const n of e.nodes) {
    const a = r.node(n.id);
    t[n.id] = a ? { x: Math.round(a.x - Hr / 2), y: Math.round(a.y - Wr / 2) } : { x: n.x, y: n.y };
  }
  return {
    ...e,
    nodes: e.nodes.map((n) => ({ ...n, ...t[n.id] })),
    layout: { ...e.layout, positions: t }
  };
}
const nv = (e, r) => Math.abs(e.x - r.x) < Hr + fu && Math.abs(e.y - r.y) < Wr + hu;
function Xw(e) {
  const r = [], t = e.nodes.map((a) => {
    let i = Number.isFinite(a.x) ? a.x : 0, s = Number.isFinite(a.y) ? a.y : 0, o = 0;
    for (; r.some((u) => nv({ x: i, y: s }, u)) && o++ < 200; )
      i += Hr + fu, i > 2400 && (i = 80, s += Wr + hu);
    return r.push({ x: i, y: s }), { ...a, x: i, y: s };
  });
  if (t.every((a, i) => a.x === e.nodes[i].x && a.y === e.nodes[i].y)) return e;
  const n = Object.fromEntries(t.map((a) => [a.id, { x: a.x, y: a.y }]));
  return { ...e, nodes: t, layout: { ...e.layout, positions: n } };
}
function Oo(e, r, t) {
  let n = { x: Math.round(r), y: Math.round(t) }, a = 0;
  for (; e.nodes.some((i) => nv(n, i)) && a++ < 200; )
    n = { x: n.x + Hr + fu, y: n.y }, n.x > 2400 && (n = { x: 80, y: n.y + Wr + hu });
  return n;
}
const { Header: Zw, Content: e1 } = ov;
function r1({ ctx: e }) {
  var et, Lr;
  const [r, t] = uv.useMessage(), [n, a] = I([]), [i, s] = I([]), [o, u] = I([]), [c, l] = I([]), [d, p] = I(""), [v, g] = I(1), [h, y] = I(!0), [C, x] = I([]), [_, A] = I(() => st()), [L, m] = I(null), [k, N] = I(null), [H, G] = I([]), [S, j] = I(!1), [V, z] = I(!0), [ie, ue] = I(!1), [fe, B] = I(!1), [J, E] = I(0), T = Te([]), M = Te([]), ve = Te(null), we = Te(null), [Se, Ze] = I(null), [Er, vr] = I({}), [Sr, We] = I(!1), [Rr, ze] = I(!1), Qe = Te(1), Be = Te(e);
  Be.current = e;
  const le = Te(_.trigger);
  _e(() => {
    le.current = _.trigger;
  }, [_.trigger]), _e(() => {
    let q = !0;
    const D = Be.current.hubQuery;
    return Promise.allSettled([
      D("industrial/nodes"),
      D("industrial/workflows"),
      D("industrial/resources")
    ]).then(async ([ce, se, he]) => {
      var w;
      if (!q) return;
      const pe = [];
      if (ce.status === "fulfilled" ? a(Mh(ce.value)) : pe.push(`节点描述: ${String(ce.reason)}`), se.status === "fulfilled") {
        const P = jh(se.value);
        s(P);
        const W = new URLSearchParams(window.location.search).get("workflowId"), oe = W && P.some((K) => K.workflowId === W) ? W : (w = P[0]) == null ? void 0 : w.workflowId;
        oe && Ue(oe);
      } else pe.push(`工作流: ${String(se.reason)}`);
      if (he.status === "fulfilled") {
        const P = Lh(he.value);
        l(P);
        const W = P.map((oe) => oe.id);
        u(W), p((oe) => oe || W[0] || "");
      } else pe.push(`资源: ${String(he.reason)}`);
      x(pe);
      try {
        const P = await Be.current.hubQuery("industrial/file-samples");
        q && vr((P == null ? void 0 : P.data) ?? P ?? {});
      } catch {
      }
    }).finally(() => {
      q && y(!1);
    }), () => {
      q = !1;
    };
  }, []);
  const ge = ht(
    () => new Map(n.map((q) => [q.nodeTypeId, q])),
    [n]
  ), te = _.nodes.find((q) => q.id === L) ?? null, Re = F(async (q, D, ce) => {
    j(!0);
    try {
      return await Pe().api.hubCommand(q, D), r.success(ce), !0;
    } catch (se) {
      return r.error(`${q} failed: ${se instanceof Error ? se.message : String(se)}`), !1;
    } finally {
      j(!1);
    }
  }, [r]), Ue = F(async (q) => {
    try {
      const D = await e.hubQuery("industrial/workflow-definitions"), se = (xe(D) ?? []).find((P) => {
        const W = P;
        return String((W == null ? void 0 : W.workflowId) ?? "") === q;
      }), he = se === void 0 ? { ...st(q), name: q } : Fv(se), pe = Xw(he);
      A(pe), T.current = [], M.current = [], E((P) => P + 1), g(Number(pe.layout.lookbackDays ?? 1) || 1);
      const w = he.nodes.map((P) => /^n(\d+)$/.exec(P.id)).filter(Boolean).reduce((P, W) => Math.max(P, Number(W[1])), 0);
      Qe.current = w + 1, m(null), ue(!1), G([]), B(!1);
    } catch (D) {
      r.error(`加载工作流失败: ${D instanceof Error ? D.message : String(D)}`);
    }
  }, [e, r]);
  _e(() => {
    _.workflowId && G(_u(_, ge, new Set(c.map((q) => q.id))));
  }, [ge, _, c]);
  const er = F(async () => {
    try {
      const q = Hv(await e.hubQuery("industrial/execution"));
      if (q === null) return;
      Ze(q);
    } catch {
    }
  }, [e]), ne = F(async () => {
    var ce;
    const q = new Set(_.resourceReferences ?? []);
    for (const se of _.nodes)
      for (const he of ["resourceId", "targetResourceId"]) {
        const pe = (ce = se.config) == null ? void 0 : ce[he];
        typeof pe == "string" && pe.trim() && q.add(pe.trim());
      }
    const D = {
      ..._,
      trigger: le.current,
      resourceReferences: [...q],
      layout: { ..._.layout, lookbackDays: v }
    };
    await Re("industrial/save-workflow", D, "已保存") && (A(D), B(!1));
  }, [Re, _, v]), Ce = F(async () => {
    if (fe) {
      r.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await Re("industrial/publish-workflow", { workflowId: _.workflowId }, "已发布") && await er();
  }, [Re, _.workflowId, fe, r, er]), ee = F(async () => {
    if (fe) {
      r.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await Re(
      "industrial/run-workflow",
      { workflowId: _.workflowId, resourceId: d },
      "采集已启动"
    ) && await er();
  }, [Re, _.workflowId, fe, r, er, d]), X = F((q) => {
    q !== _ && (T.current.length >= 50 && T.current.shift(), T.current.push(_), M.current = [], E((D) => D + 1), A(q), B(!0));
  }, [_]), Ne = F(() => {
    const q = T.current.pop();
    q && (M.current.push(_), A(q), B(!0), E((D) => D + 1));
  }, [_]), ur = F(() => {
    const q = M.current.pop();
    q && (T.current.push(_), A(q), B(!0), E((D) => D + 1));
  }, [_]);
  _e(() => {
    const q = (D) => {
      const ce = D.target;
      if (ce && /INPUT|TEXTAREA|SELECT/.test(ce.tagName)) return;
      const se = D.key.toLowerCase();
      if ((D.ctrlKey || D.metaKey) && se === "z") {
        D.preventDefault(), D.shiftKey ? ur() : Ne();
        return;
      }
      if ((D.ctrlKey || D.metaKey) && se === "y") {
        D.preventDefault(), ur();
        return;
      }
      if (D.key === "Delete" && L) {
        D.preventDefault(), X(Lt(_, L)), m(null);
        return;
      }
      if ((D.ctrlKey || D.metaKey) && se === "c" && L) {
        we.current = _.nodes.find((he) => he.id === L) ?? null;
        return;
      }
      if ((D.ctrlKey || D.metaKey) && se === "v" && we.current) {
        D.preventDefault();
        const he = tr(ge, we.current.type);
        if (!he) return;
        const pe = `n${Qe.current++}`, w = we.current, P = Oo(_, w.x + 40, w.y + 40), W = Mt(he, pe, P.x, P.y);
        X({
          ..._,
          nodes: [..._.nodes, { ...W, config: { ...w.config } }]
        }), m(pe);
      }
    };
    return window.addEventListener("keydown", q), () => window.removeEventListener("keydown", q);
  }, [ge, X, _, ur, L, Ne]);
  const Tr = F(() => {
    X(Jw(_)), requestAnimationFrame(() => {
      var q, D;
      return (D = (q = ve.current) == null ? void 0 : q.fitView) == null ? void 0 : D.call(q, { when: "always" });
    });
  }, [X, _]), De = F((q, D, ce) => {
    const se = tr(ge, q);
    if (!se) return;
    const he = `n${Qe.current++}`, pe = Oo(_, D, ce);
    X({ ..._, nodes: [..._.nodes, Mt(se, he, pe.x, pe.y)] }), m(he), ue(!0), G([]);
  }, [ge, X, _]), xr = F((q) => {
    const D = Qe.current - 1;
    De(q, 80 + D % 3 * 190, 80 + Math.floor(D / 3) * 130);
  }, [De]), gr = F((q, D) => {
    const ce = _.nodes.find((K) => K.id === q), se = _.nodes.find((K) => K.id === D);
    if (!ce || !se)
      return r.warning("连线失败：端点不在画布上"), null;
    const he = tr(ge, ce.type), pe = tr(ge, se.type);
    if (!he || !pe) return null;
    let w = ce, P = se, W = mu(he, pe);
    if (!W) {
      const K = mu(pe, he);
      if (!K)
        return r.warning(`端口类型不兼容：${he.name} → ${pe.name}`), null;
      w = se, P = ce, W = K;
    }
    const oe = bv(_, {
      source: w.id,
      sourcePort: W.sourcePort,
      target: P.id,
      targetPort: W.targetPort
    }, ge);
    return oe.issue ? (r.warning(`${oe.issue.code}: ${oe.issue.message}`), G((K) => [...K, oe.issue]), null) : oe.definition === _ ? (r.info("这条连线已经存在"), null) : (X(oe.definition), W);
  }, [ge, X, _, r]), yr = F(() => {
    const q = _u(_, ge, new Set(o));
    G(q), q.length === 0 ? r.success("本地校验通过（发布时服务端仍会复核）") : (r.warning(`发现 ${q.length} 个问题，见属性抽屉定位列表`), ue(!0));
  }, [_, ge, o, r]), kr = F((q) => {
    X(Lt(_, q)), L === q && m(null);
  }, [X, _, L]), $e = F((q) => {
    const D = _.nodes.find((pe) => pe.id === q);
    if (!D) return;
    const ce = tr(ge, D.type);
    if (!ce) return;
    const se = `n${Qe.current++}`, he = Oo(_, D.x + 40, D.y + 40);
    X({ ..._, nodes: [
      ..._.nodes,
      { ...Mt(ce, se, he.x, he.y), config: { ...D.config } }
    ] }), m(se);
  }, [ge, X, _]), Ke = F((q) => {
    const D = _.edges.findIndex((ce) => `e:${ce.source}:${ce.sourcePort}>${ce.target}:${ce.targetPort}` === q);
    D >= 0 && X(bu(_, D));
  }, [X, _]), Nt = F((q, D) => {
    X({
      ..._,
      edges: _.edges.map((ce, se) => se === q ? D : ce)
    });
  }, [X, _]);
  return h ? /* @__PURE__ */ f(kh, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ R("div", { className: "industrial-designer", children: [
    t,
    /* @__PURE__ */ R(Zw, { className: "industrial-header", children: [
      /* @__PURE__ */ R("div", { className: "industrial-title", children: [
        /* @__PURE__ */ f(U.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ f(U.Text, { type: "secondary", children: _.name || _.workflowId }),
        fe && /* @__PURE__ */ f(Le, { color: "orange", children: "未保存" }),
        Se && /* @__PURE__ */ f(Le, { color: Se.error ? "error" : "success", children: Se.status })
      ] }),
      /* @__PURE__ */ R("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ f(
          $,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": V,
            onClick: () => z((q) => !q),
            children: V ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ f(
          $,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => ue((q) => !q),
            children: "属性"
          }
        ),
        /* @__PURE__ */ f($, { size: "small", "data-testid": "open-trigger", onClick: () => We(!0), children: "触发器" }),
        /* @__PURE__ */ f(
          $,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => ze(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ f(
          $,
          {
            size: "small",
            "data-testid": "designer-undo",
            disabled: T.current.length === 0,
            onClick: Ne,
            children: "撤销"
          }
        ),
        /* @__PURE__ */ f(
          $,
          {
            size: "small",
            "data-testid": "designer-redo",
            disabled: M.current.length === 0,
            onClick: ur,
            children: "重做"
          }
        ),
        /* @__PURE__ */ f($, { size: "small", "data-testid": "designer-auto-layout", onClick: Tr, children: "自动布局" }),
        /* @__PURE__ */ f($, { size: "small", "data-testid": "designer-fit", onClick: () => {
          var q, D;
          return (D = (q = ve.current) == null ? void 0 : q.fitView) == null ? void 0 : D.call(q, { when: "always" });
        }, children: "适应画布" }),
        /* @__PURE__ */ f($, { size: "small", "data-testid": "designer-center", onClick: () => {
          var q, D;
          return (D = (q = ve.current) == null ? void 0 : q.fitCenter) == null ? void 0 : D.call(q);
        }, children: "居中" }),
        /* @__PURE__ */ f("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ R(Y, { size: 4, children: [
          /* @__PURE__ */ f($, { size: "small", loading: S, onClick: () => void ne(), children: "保存" }),
          /* @__PURE__ */ f($, { size: "small", onClick: yr, children: "校验" }),
          /* @__PURE__ */ f($, { size: "small", loading: S, onClick: () => void Ce(), children: "发布" }),
          /* @__PURE__ */ f($, { size: "small", type: "primary", loading: S, onClick: () => void ee(), children: "运行" })
        ] }),
        /* @__PURE__ */ R(
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
    /* @__PURE__ */ R(e1, { className: `industrial-body${V ? "" : " industrial-body--collapsed"}`, children: [
      V ? /* @__PURE__ */ R("aside", { className: "industrial-palette", children: [
        i.length > 0 && /* @__PURE__ */ f(
          je,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: _.workflowId,
            placeholder: "选择工作流",
            onChange: (q) => void Ue(q),
            options: i.map((q) => ({ value: q.workflowId, label: q.name }))
          }
        ),
        /* @__PURE__ */ f(gv, { descriptors: n, loading: !1, onAdd: xr })
      ] }) : /* @__PURE__ */ f("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ f($, { type: "text", size: "small", onClick: () => z(!0), children: "节点库" }) }),
      /* @__PURE__ */ R("section", { className: "industrial-center", children: [
        C.length > 0 && /* @__PURE__ */ f(
          Ee,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: C.join("；")
          }
        ),
        /* @__PURE__ */ f(
          vv,
          {
            definition: _,
            catalog: ge,
            selectedId: L,
            onSelect: (q) => {
              m(q), q && ue(!0);
            },
            onDropNode: De,
            onCreateEdge: gr,
            onPositions: (q) => X(Iv(_, q)),
            onSelectIds: (q) => {
              q.length && m(q[0]);
            },
            onGraphReady: (q) => {
              ve.current = q;
            },
            onSelectEdge: (q) => N(q),
            onDeleteNodeById: kr,
            onDuplicateNode: $e,
            onDeleteEdgeById: Ke
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ R(
      sv,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: ie,
        mask: !1,
        getContainer: !1,
        onClose: () => ue(!1),
        children: [
          Se && /* @__PURE__ */ f(
            Ee,
            {
              style: { marginBottom: 12 },
              type: Se.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${Se.status}`,
              description: /* @__PURE__ */ R(Y, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                Se.error && /* @__PURE__ */ f("span", { children: Se.error }),
                Se.executed.length > 0 && /* @__PURE__ */ R("span", { children: [
                  "已执行节点: ",
                  Se.executed.join(", ")
                ] }),
                Se.nodes.length > 0 && /* @__PURE__ */ f(
                  Ae,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: Se.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (q, D) => D.error ?? (D.samples.length > 0 ? D.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ f(
            Av,
            {
              node: te,
              descriptor: te ? tr(ge, te.type) ?? null : null,
              nodes: _.nodes,
              edges: _.edges,
              catalog: ge,
              resources: c,
              issues: H,
              onApplyConfig: (q) => {
                L && X(wv(_, L, q));
              },
              onDeleteNode: () => {
                X(Lt(_, L ?? "")), m(null);
              },
              onDeleteEdge: (q) => X(bu(_, q)),
              onCreateEdge: (q, D) => void gr(q, D),
              onUpdateEdge: Nt,
              onCreateResource: () => {
                var q;
                return (q = e.navigate) == null ? void 0 : q.call(e, "/industrial/resources");
              },
              selectedEdgeId: k,
              sampleLines: te ? Er[String(((et = te.config) == null ? void 0 : et.resourceId) ?? ((Lr = te.config) == null ? void 0 : Lr.targetResourceId) ?? "")] ?? [] : [],
              onSelectNode: (q) => {
                m(q), ue(!0);
              }
            }
          )
        ]
      }
    ),
    Sr && /* @__PURE__ */ f(
      Yv,
      {
        open: Sr,
        onClose: () => We(!1),
        trigger: _.trigger,
        resourceIds: o,
        workflowId: _.workflowId,
        ctx: e,
        onSave: (q) => {
          le.current = q, X({ ..._, trigger: q });
        }
      }
    ),
    Rr && /* @__PURE__ */ f(
      Zv,
      {
        open: Rr,
        onClose: () => ze(!1),
        ctx: e,
        workflowId: _.workflowId,
        resourceId: d,
        lookbackDays: v
      }
    )
  ] });
}
function t1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(!1), [s, o] = I(""), [u, c] = I(null), [l, d] = I(!1), p = F(async () => {
    try {
      n(jh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
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
      const x = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await r.hubCommand("industrial/save-workflow", {
        ...st(x),
        name: h,
        layout: { lookbackDays: 1 }
      }), i(!1), o(""), await p(), (y = r.navigate) == null || y.call(r, `/industrial/designer?workflowId=${encodeURIComponent(x)}`);
    } catch (C) {
      c(C instanceof Error ? C.message : String(C));
    } finally {
      d(!1);
    }
  }, [r, p, s]), g = F((h) => {
    var y;
    (y = r.navigate) == null || y.call(r, `/industrial/designer?workflowId=${encodeURIComponent(h)}`);
  }, [r]);
  return /* @__PURE__ */ R(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ R(Y, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ f($, { type: "primary", onClick: () => i(!0), children: "新建工作流" })
    ] }),
    u && /* @__PURE__ */ f(U.Text, { type: "danger", children: u }),
    /* @__PURE__ */ f(
      Ae,
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
      br,
      {
        open: a,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: l,
        onCancel: () => i(!1),
        onOk: () => void v(),
        children: /* @__PURE__ */ R(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(U.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ f(
            Q,
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
const No = {
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
}, n1 = [
  { value: "filesystem.local", label: "本地目录" },
  { value: "filesystem.unc", label: "Windows UNC 共享" },
  { value: "sqlserver", label: "SQL Server" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mysql", label: "MySQL" },
  { value: "sqlite", label: "SQLite" }
];
function a1(e) {
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
function i1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I([]), [u, c] = I(No), [l, d] = I(!1), [p, v] = I(null), [g, h] = I(!1), y = F(async () => {
    try {
      const [m, k, N] = await Promise.all([
        r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        r.hubQuery("industrial/credentials"),
        r.hubQuery("industrial/resource-tests")
      ]);
      n(Gv(m)), i(Fh(k)), o(zv(N));
    } catch (m) {
      v(m instanceof Error ? m.message : String(m));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    y();
  }, [y]);
  const C = F((m) => {
    const k = (...N) => {
      for (const H of N) {
        const G = m.config[H];
        if (G != null && String(G).trim()) return String(G);
      }
      return "";
    };
    c({
      id: m.id,
      name: m.name,
      type: m.type,
      provider: m.provider,
      root: k("root", "path"),
      host: k("server", "host", "dataSource", "data_source"),
      database: k("database", "initialCatalog", "initial_catalog", "path"),
      port: k("port"),
      table: k("table"),
      credentialId: m.credentialId ?? "",
      enabled: m.enabled,
      config: m.config
    }), d(!0);
  }, []), x = F(async () => {
    const m = u.id.trim();
    if (!m) {
      v("请输入资源 ID");
      return;
    }
    h(!0), v(null);
    try {
      await r.hubCommand("industrial/save-resource", {
        id: m,
        name: u.name || m,
        type: u.type,
        provider: u.provider || "manual",
        config: a1(u),
        credentialId: u.credentialId || null,
        enabled: u.enabled
      }), d(!1), c(No), await y();
    } catch (k) {
      v(k instanceof Error ? k.message : String(k));
    } finally {
      h(!1);
    }
  }, [r, u, y]), _ = F(async (m) => {
    v(null);
    try {
      await r.hubCommand("industrial/test-resource", { id: m });
    } catch (k) {
      v(k instanceof Error ? k.message : String(k));
    }
    await y();
  }, [r, y]), A = F(async (m) => {
    v(null);
    try {
      await r.hubCommand("industrial/delete-resource", { id: m }), await y();
    } catch (k) {
      v(k instanceof Error ? k.message : String(k));
    }
  }, [r, y]), L = new Map(s.map((m) => [m.resourceId, m]));
  return /* @__PURE__ */ R(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ R(Y, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ R(Y, { children: [
        /* @__PURE__ */ f($, { onClick: () => {
          var m;
          return (m = r.navigate) == null ? void 0 : m.call(r, "/industrial/designer");
        }, children: "返回设计器" }),
        /* @__PURE__ */ f($, { type: "primary", onClick: () => {
          c(No), d(!0);
        }, children: "新建资源" })
      ] })
    ] }),
    p && /* @__PURE__ */ f(Ee, { type: "error", showIcon: !0, message: p }),
    /* @__PURE__ */ f(
      Ae,
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
            render: (m, k) => {
              const N = L.get(k.id);
              return N ? /* @__PURE__ */ f(U.Text, { type: N.ok ? "success" : "danger", children: N.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (m, k) => /* @__PURE__ */ R(Y, { size: 4, children: [
              /* @__PURE__ */ f($, { size: "small", onClick: () => void _(k.id), children: "测试连接" }),
              /* @__PURE__ */ f($, { size: "small", onClick: () => C(k), children: "编辑" }),
              /* @__PURE__ */ f($, { size: "small", danger: !0, onClick: () => void A(k.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ f(
      br,
      {
        open: l,
        title: u.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: g,
        onCancel: () => d(!1),
        onOk: () => void x(),
        children: /* @__PURE__ */ R(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: u.id,
              onChange: (m) => c({ ...u, id: m.target.value })
            }
          ),
          /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: u.name,
              onChange: (m) => c({ ...u, name: m.target.value })
            }
          ),
          /* @__PURE__ */ f(U.Text, { children: "资源类型" }),
          /* @__PURE__ */ f(Y, { wrap: !0, children: n1.map((m) => /* @__PURE__ */ f(
            $,
            {
              size: "small",
              type: u.type === m.value ? "primary" : "default",
              onClick: () => c({ ...u, type: m.value }),
              children: m.label
            },
            m.value
          )) }),
          (u.type === "filesystem.local" || u.type === "filesystem.unc") && /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "目录路径",
              placeholder: u.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: u.root,
              onChange: (m) => c({ ...u, root: m.target.value })
            }
          ),
          (u.type === "sqlserver" || u.type === "postgresql" || u.type === "mysql") && /* @__PURE__ */ R(Y, { children: [
            /* @__PURE__ */ f(
              Q,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: u.host,
                onChange: (m) => c({ ...u, host: m.target.value })
              }
            ),
            /* @__PURE__ */ f(
              Q,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: u.database,
                onChange: (m) => c({ ...u, database: m.target.value })
              }
            ),
            /* @__PURE__ */ f(
              Q,
              {
                "aria-label": "端口",
                placeholder: "端口",
                value: u.port,
                onChange: (m) => c({ ...u, port: m.target.value })
              }
            )
          ] }),
          u.type === "sqlite" && /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: u.database,
              onChange: (m) => c({ ...u, database: m.target.value })
            }
          ),
          /* @__PURE__ */ f(
            je,
            {
              "aria-label": "凭证",
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: u.credentialId || void 0,
              options: a.map((m) => ({ value: m.id, label: m.name })),
              onChange: (m) => c({ ...u, credentialId: m ?? "" })
            }
          ),
          /* @__PURE__ */ R(Y, { children: [
            /* @__PURE__ */ f(
              pt,
              {
                "aria-label": "启用资源",
                checked: u.enabled,
                onChange: (m) => c({ ...u, enabled: m })
              }
            ),
            /* @__PURE__ */ f(U.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
const Do = ["username-password", "domain-account", "api-token"];
function s1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(Do), [s, o] = I(!1), [u, c] = I(""), [l, d] = I(""), [p, v] = I(Do[0]), [g, h] = I(""), [y, C] = I(""), [x, _] = I(""), [A, L] = I(""), [m, k] = I(""), [N, H] = I(!1), [G, S] = I(null), [j, V] = I(!1), z = F(async () => {
    try {
      n(Fh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
      const T = await r.hubQuery("industrial/credential-types"), M = Array.isArray(T) ? T : Array.isArray(T == null ? void 0 : T.data) ? T.data : [];
      M.length && i(M.map((ve) => ve.kind));
    } catch (T) {
      S(T instanceof Error ? T.message : String(T));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    z();
  }, [z]);
  const ie = F(() => {
    c(""), d(""), v(Do[0]), h(""), C(""), _(""), L(""), k(""), H(!1), S(null);
  }, []), ue = F(() => {
    ie(), o(!0);
  }, [ie]), fe = F((T) => {
    ie(), c(T.id), d(T.name), a.includes(T.kind) && v(T.kind), H(!0), o(!0);
  }, [a, ie]), B = F(() => m.trim() ? m.trim() : p === "api-token" ? A.trim() ? JSON.stringify({ token: A.trim() }) : null : !g.trim() || !y.trim() ? null : JSON.stringify(p === "domain-account" ? { username: g.trim(), password: y.trim(), domain: x.trim() } : { username: g.trim(), password: y.trim() }), [x, p, y, m, A, g]), J = F(async () => {
    if (!u.trim()) {
      S("请输入凭证 ID");
      return;
    }
    const T = B();
    if (!T) {
      S("请填写凭证字段，或在高级内容中提供凭证");
      return;
    }
    V(!0), S(null);
    try {
      N ? await r.hubCommand("industrial/rotate-credential", { id: u, secret: T }) : await r.hubCommand("industrial/save-credential", { id: u, name: l || u, kind: p, secret: T }), o(!1), await z();
    } catch (M) {
      S(M instanceof Error ? M.message : String(M));
    } finally {
      V(!1);
    }
  }, [B, r, u, p, z, l, N, m]), E = F(async (T) => {
    S(null);
    try {
      await r.hubCommand("industrial/delete-credential", { id: T.id }), await z();
    } catch (M) {
      S(M instanceof Error ? M.message : String(M));
    }
  }, [r, z]);
  return /* @__PURE__ */ R(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ R(Y, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ f($, { type: "primary", onClick: ue, children: "新建凭证" })
    ] }),
    G && /* @__PURE__ */ f(Ee, { type: "error", showIcon: !0, message: G }),
    /* @__PURE__ */ f(
      Ae,
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
            render: (T, M) => /* @__PURE__ */ R(Y, { children: [
              /* @__PURE__ */ f($, { size: "small", onClick: () => fe(M), children: "轮换" }),
              /* @__PURE__ */ f($, { size: "small", danger: !0, onClick: () => void E(M), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ f(
      br,
      {
        open: s,
        title: N ? "轮换凭证" : "新建凭证",
        okText: N ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: j,
        onCancel: () => o(!1),
        onOk: () => void J(),
        children: /* @__PURE__ */ R(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: u,
              disabled: N,
              onChange: (T) => c(T.target.value)
            }
          ),
          !N && /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: l,
              onChange: (T) => d(T.target.value)
            }
          ),
          /* @__PURE__ */ f(
            je,
            {
              "aria-label": "凭证类型",
              style: { width: "100%" },
              value: p,
              onChange: (T) => v(T),
              options: a.map((T) => ({ value: T, label: T }))
            }
          ),
          p === "api-token" ? /* @__PURE__ */ f(
            Q.Password,
            {
              "aria-label": "令牌",
              placeholder: "令牌",
              value: A,
              onChange: (T) => L(T.target.value)
            }
          ) : /* @__PURE__ */ R(dr, { children: [
            /* @__PURE__ */ f(
              Q,
              {
                "aria-label": "用户名",
                placeholder: "用户名",
                value: g,
                onChange: (T) => h(T.target.value)
              }
            ),
            /* @__PURE__ */ f(
              Q.Password,
              {
                "aria-label": "密码",
                placeholder: "密码",
                value: y,
                onChange: (T) => C(T.target.value)
              }
            ),
            p === "domain-account" && /* @__PURE__ */ f(
              Q,
              {
                "aria-label": "域",
                placeholder: "域（可选）",
                value: x,
                onChange: (T) => _(T.target.value)
              }
            )
          ] }),
          /* @__PURE__ */ f(
            Q.Password,
            {
              "aria-label": "凭证内容",
              placeholder: "高级：直接提供凭证内容（优先）",
              value: m,
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
function o1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I(null), [u, c] = I(null), [l, d] = I(!1), p = F(async () => {
    try {
      const g = Vv(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/execution-center"));
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
  return /* @__PURE__ */ R(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    s && /* @__PURE__ */ f(Ee, { type: "error", showIcon: !0, message: s }),
    u && /* @__PURE__ */ f(Ee, { type: "success", showIcon: !0, message: u }),
    /* @__PURE__ */ f(
      Ae,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ f(Le, { color: Th(g), children: g }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (g) => g ?? "—" },
          { title: "错误", dataIndex: "error", render: (g) => g ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ f(
      Ae,
      {
        rowKey: "id",
        dataSource: a,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ f(Le, { color: Th(g), children: g }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (g, h) => h.diagnosis ? /* @__PURE__ */ R(Y, { direction: "vertical", size: 0, children: [
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
function u1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(null), s = F(async () => {
    try {
      n(Kv(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (u) {
      i(u instanceof Error ? u.message : String(u));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    s();
  }, [s]);
  const o = (u) => /* @__PURE__ */ f(
    Ae,
    {
      rowKey: "id",
      dataSource: u.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (c) => /* @__PURE__ */ f(Le, { children: c }) },
        { title: "记录", dataIndex: "records" },
        {
          title: "接受/拒绝/警告",
          key: "counts",
          render: (c, l) => `${l.accepted} / ${l.rejected} / ${l.warnings}`
        },
        {
          title: "原因与下一步",
          key: "diagnosis",
          render: (c, l) => l.diagnosis ? /* @__PURE__ */ R(Y, { direction: "vertical", size: 0, children: [
            /* @__PURE__ */ f(U.Text, { children: l.diagnosis.message }),
            /* @__PURE__ */ f(U.Text, { type: "secondary", children: l.diagnosis.action }),
            l.error && /* @__PURE__ */ f(U.Text, { type: "secondary", children: l.error })
          ] }) : l.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ R(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    a && /* @__PURE__ */ f(Ee, { type: "error", showIcon: !0, message: a }),
    /* @__PURE__ */ f(
      Ae,
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
const Mo = (e) => {
  const r = xe(e);
  return Array.isArray(r) ? r : [];
}, l1 = () => `machine-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 12) : Date.now().toString(36)}`;
function c1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I([]), [u, c] = I(null), [l, d] = I(""), [p, v] = I(""), [g, h] = I(), [y, C] = I(!0), [x, _] = I(null), A = F(async () => {
    try {
      n(Mo(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/machines"))), i(Mo(await r.hubQuery("industrial/resources"))), o(Mo(await r.hubQuery("industrial/credentials")));
    } catch (S) {
      _(S instanceof Error ? S.message : String(S));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    A();
  }, [A]);
  const L = F(() => {
    c({ id: l1(), name: "", endpoint: "", credentialId: null, enabled: !0 }), d(""), v(""), h(void 0), C(!0), _(null);
  }, []), m = F((S) => {
    c(S), d(S.name), v(S.endpoint), h(S.credentialId ?? void 0), C(S.enabled), _(null);
  }, []), k = F(async () => {
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
        }), c(null), await A();
      } catch (S) {
        _(S instanceof Error ? S.message : String(S));
      }
    }
  }, [r, g, u, p, y, A, l]), N = F(async (S) => {
    _(null);
    try {
      await r.hubCommand("industrial/delete-machine", { id: S.id }), await A();
    } catch (j) {
      _(j instanceof Error ? j.message : String(j));
    }
  }, [r, A]), H = F(async (S, j) => {
    _(null);
    try {
      await r.hubCommand("industrial/save-machine", {
        id: S.id,
        name: S.name,
        endpoint: S.endpoint,
        credentialId: S.credentialId,
        enabled: j
      }), await A();
    } catch (V) {
      _(V instanceof Error ? V.message : String(V));
    }
  }, [r, A]), G = F(async (S, j) => {
    _(null);
    try {
      await r.hubCommand("industrial/save-resource", {
        id: S.id,
        machineId: j || null
      }), await A();
    } catch (V) {
      _(V instanceof Error ? V.message : String(V));
    }
  }, [r, A]);
  return /* @__PURE__ */ R(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ R(Y, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "设备与资源" }),
      /* @__PURE__ */ f($, { type: "primary", onClick: L, children: "新增设备" })
    ] }),
    /* @__PURE__ */ f(U.Text, { type: "secondary", children: "设备地址是台账属性；实际连接由数据源资源配置决定。绑定凭证供需要认证的数据源引用。" }),
    x && /* @__PURE__ */ f(Ee, { type: "error", showIcon: !0, message: x }),
    /* @__PURE__ */ f(
      Ae,
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
              return ((j = s.find((V) => V.id === S)) == null ? void 0 : j.name) ?? "—";
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
                onChange: (V) => void H(j, V)
              }
            )
          },
          { title: "操作", key: "actions", render: (S, j) => /* @__PURE__ */ R(Y, { children: [
            /* @__PURE__ */ f($, { size: "small", onClick: () => m(j), children: "编辑" }),
            /* @__PURE__ */ f($, { size: "small", danger: !0, onClick: () => void N(j), children: "删除" })
          ] }) }
        ]
      }
    ),
    /* @__PURE__ */ f(U.Title, { level: 5, style: { margin: 0 }, children: "资源归属" }),
    /* @__PURE__ */ f(
      Ae,
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
              je,
              {
                "aria-label": `资源 ${j.name} 的设备`,
                style: { width: 220 },
                value: j.machineId ?? void 0,
                allowClear: !0,
                onChange: (V) => void G(j, V ?? ""),
                options: t.map((V) => ({ value: V.id, label: V.name }))
              }
            )
          }
        ]
      }
    ),
    /* @__PURE__ */ f(
      br,
      {
        open: u !== null,
        title: u != null && u.name ? "编辑设备" : "新增设备",
        okText: "保存设备",
        cancelText: "取消",
        onCancel: () => c(null),
        onOk: () => void k(),
        children: /* @__PURE__ */ R(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "设备名称",
              placeholder: "设备名称",
              value: l,
              onChange: (S) => d(S.target.value)
            }
          ),
          /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "设备地址",
              placeholder: "台账地址 / IP",
              value: p,
              onChange: (S) => v(S.target.value)
            }
          ),
          /* @__PURE__ */ f(
            je,
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
const d1 = ["filesystem.local", "filesystem.unc"], f1 = ["sink.sqlite", "sink.sqlserver", "sink.postgresql", "sink.mysql"];
function h1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I(""), [u, c] = I(""), [l, d] = I("csv"), [p, v] = I([]), [g, h] = I(null), [y, C] = I(!1);
  _e(() => {
    (async () => {
      try {
        n(Lh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"))), i(Mh(await r.hubQuery((e == null ? void 0 : e.nodesQuery) ?? "industrial/nodes")));
      } catch (m) {
        h(m instanceof Error ? m.message : String(m));
      }
    })();
  }, [e == null ? void 0 : e.nodesQuery, e == null ? void 0 : e.query, r]);
  const x = t.filter((m) => d1.includes(m.type)), _ = t.filter((m) => f1.some((k) => m.type === k.split(".")[1])), A = ht(
    () => new Map(a.map((m) => [m.nodeTypeId, m])),
    [a]
  ), L = F(async () => {
    var m;
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
      const N = `quick-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`, G = `industrial.${t.find((B) => B.id === u).type}`, S = `industrial.reader.${l}`, j = (B, J, E, T, M) => ({ id: B, type: J, config: M, x: E, y: T }), V = (B, J) => {
        var M, ve;
        const E = A.get(ie[B]), T = A.get(ie[J]);
        return {
          source: B,
          sourcePort: ((M = E.outputs[0]) == null ? void 0 : M.name) ?? "output",
          target: J,
          targetPort: ((ve = T.inputs[0]) == null ? void 0 : ve.name) ?? "input"
        };
      }, z = [
        j("src", G, 80, 120, { resourceId: u }),
        j("read", S, 280, 120, {}),
        ...p.map((B, J) => j(
          `sink${J + 1}`,
          `industrial.sink.${t.find((E) => E.id === B).type}`,
          500,
          80 + J * 120,
          { targetResourceId: B }
        ))
      ], ie = Object.fromEntries(z.map((B) => [B.id, B.type])), ue = [V("src", "read"), ...p.map((B, J) => V("read", `sink${J + 1}`))], fe = {
        ...st(N),
        name: s.trim(),
        nodes: z,
        edges: ue,
        resourceReferences: [u, ...p],
        layout: { lookbackDays: 1 }
      };
      await r.hubCommand("industrial/save-workflow", fe), await r.hubCommand("industrial/publish-workflow", { workflowId: N }), (m = r.navigate) == null || m.call(r, `/industrial/designer?workflowId=${encodeURIComponent(N)}`);
    } catch (k) {
      h(k instanceof Error ? k.message : String(k));
    } finally {
      C(!1);
    }
  }, [r, l, A, s, t, u, p]);
  return /* @__PURE__ */ R(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "快速创建采集任务" }),
    /* @__PURE__ */ f(U.Text, { type: "secondary", children: "生成标准工作流，可在设计器中继续修改。" }),
    g && /* @__PURE__ */ f(Ee, { type: "error", showIcon: !0, message: g }),
    /* @__PURE__ */ f(
      Q,
      {
        "aria-label": "任务名称",
        placeholder: "任务名称",
        value: s,
        onChange: (m) => o(m.target.value)
      }
    ),
    /* @__PURE__ */ R(Y, { wrap: !0, children: [
      /* @__PURE__ */ f(
        je,
        {
          "aria-label": "数据来源",
          style: { width: 260 },
          placeholder: "数据来源",
          value: u || void 0,
          onChange: (m) => c(m),
          options: x.map((m) => ({ value: m.id, label: `${m.name} (${m.type})` }))
        }
      ),
      /* @__PURE__ */ f(
        je,
        {
          "aria-label": "数据格式",
          style: { width: 160 },
          value: l,
          onChange: (m) => d(m),
          options: ["csv", "excel", "json"].map((m) => ({ value: m, label: m.toUpperCase() }))
        }
      )
    ] }),
    /* @__PURE__ */ f(
      "select",
      {
        "aria-label": "目标资源 ID 列表",
        multiple: !0,
        style: { width: 420, minHeight: 72 },
        value: p,
        onChange: (m) => v(Array.from(m.target.selectedOptions).map((k) => k.value)),
        children: _.map((m) => /* @__PURE__ */ R("option", { value: m.id, children: [
          m.name,
          " (",
          m.type,
          ")"
        ] }, m.id))
      }
    ),
    /* @__PURE__ */ f($, { type: "primary", loading: y, onClick: () => void L(), children: "创建并发布" })
  ] });
}
const p1 = (e) => {
  const r = xe(e);
  return Array.isArray(r) ? r : [];
}, v1 = () => ({
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
function g1({ block: e, ctx: r }) {
  const [t, n] = I([]), [a, i] = I(null), [s, o] = I(""), [u, c] = I(""), [l, d] = I(null), p = F(async () => {
    try {
      n(p1(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-profiles")));
    } catch (h) {
      d(h instanceof Error ? h.message : String(h));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    p();
  }, [p]);
  const v = F((h) => {
    const y = h ? { ...h } : v1();
    i(y), o(Object.entries(y.mapping ?? {}).map(([C, x]) => `${C}=${x}`).join(",")), c((y.requiredFields ?? []).join(",")), d(null);
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
          const [C, x] = y.split("=").map((_) => _ == null ? void 0 : _.trim());
          C && x && (h[C] = x);
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
  return /* @__PURE__ */ R(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ R(Y, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "文件格式配置" }),
      /* @__PURE__ */ f($, { type: "primary", onClick: () => v(), children: "新建文件配置" })
    ] }),
    l && /* @__PURE__ */ f(Ee, { type: "error", showIcon: !0, message: l }),
    /* @__PURE__ */ f(
      Ae,
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
      br,
      {
        open: a !== null,
        title: a != null && a.version ? "编辑文件配置" : "新建文件配置",
        okText: "保存配置",
        cancelText: "取消",
        onCancel: () => i(null),
        onOk: () => void g(),
        children: a && /* @__PURE__ */ R(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "配置名称",
              placeholder: "配置名称",
              value: a.name,
              onChange: (h) => i({ ...a, name: h.target.value })
            }
          ),
          /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "文件匹配规则",
              placeholder: "文件匹配，例如 *.csv",
              value: a.fileNamePattern,
              onChange: (h) => i({ ...a, fileNamePattern: h.target.value })
            }
          ),
          /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "文件格式",
              placeholder: "csv / excel / json",
              value: a.format,
              onChange: (h) => i({ ...a, format: h.target.value })
            }
          ),
          /* @__PURE__ */ f(
            Q,
            {
              "aria-label": "分隔符",
              placeholder: "分隔符",
              value: a.delimiter,
              onChange: (h) => i({ ...a, delimiter: h.target.value })
            }
          ),
          /* @__PURE__ */ f(
            Q,
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
            Q,
            {
              "aria-label": "字段映射",
              placeholder: "目标字段=来源字段，逗号分隔",
              value: s,
              onChange: (h) => o(h.target.value)
            }
          ),
          /* @__PURE__ */ f(
            Q,
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
  const r = xe(e);
  return Array.isArray(r) ? r : [];
}, y1 = (e) => new Promise((r, t) => {
  const n = new FileReader();
  n.onload = () => r(String(n.result).split(",")[1] ?? ""), n.onerror = () => t(n.error ?? new Error("无法读取 Excel 文件")), n.readAsDataURL(e);
});
function m1({ block: e, ctx: r }) {
  var ce, se, he, pe;
  const [t, n] = I([]), [a, i] = I([]), [s, o] = I([]), [u, c] = I([]), [l, d] = I([]), [p, v] = I([]), [g, h] = I([]), [y, C] = I(), [x, _] = I(1), [A, L] = I("resource-{machine}"), [m, k] = I([]), [N, H] = I(""), [G, S] = I(""), [j, V] = I(null), [z, ie] = I(""), [ue, fe] = I(""), [B, J] = I(null), [E, T] = I("manual"), [M, ve] = I(""), [we, Se] = I(""), [Ze, Er] = I(""), [vr, Sr] = I(""), [We, Rr] = I(""), [ze, Qe] = I(null), [Be, le] = I(null), [ge, te] = I(1), [Re, Ue] = I(""), [er, ne] = I(null), [Ce, ee] = I(!1), X = F(async () => {
    try {
      n($r(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/bindings"))), i($r(await r.hubQuery("industrial/machines"))), o($r(await r.hubQuery("industrial/file-profiles"))), c($r(await r.hubQuery("industrial/workflows"))), d($r(await r.hubQuery("industrial/resources")));
      try {
        J(await r.hubQuery("industrial/binding-status"));
      } catch {
      }
    } catch (w) {
      ne(w instanceof Error ? w.message : String(w));
    }
  }, [e == null ? void 0 : e.query, r]);
  _e(() => {
    X();
  }, [X]);
  const Ne = ht(() => {
    const w = [];
    for (const oe of p) for (const K of g) {
      const Ye = `binding:${y ?? ""}:v${x}:${oe}:${K}`;
      t.some((av) => av.id === Ye) && w.push(Ye);
    }
    const P = m.filter((oe) => !l.some((K) => K.id === oe)), W = p.map((oe) => A.replace("{machine}", oe)).filter((oe) => !l.some((K) => K.id === oe));
    return {
      total: p.length * g.length,
      create: p.length * g.length - w.length,
      conflicts: w,
      missing: [...P, ...W]
    };
  }, [t, p, g, l, A, m, x, y]), ur = F(async () => {
    var P, W, oe;
    if (!y || p.length === 0 || g.length === 0 || m.length === 0) {
      ne("请选择工作流、机台、文件配置和目标资源");
      return;
    }
    const w = {
      machineIds: p,
      fileProfileIds: g,
      workflowId: y,
      workflowVersion: x,
      sourceResourcePattern: A,
      targetResourceIds: m,
      triggerKind: E,
      cron: M || null,
      parameters: Object.fromEntries(vr.split(";").map((K) => K.split("=")).filter((K) => K.length === 2 && K[0].trim() && K[1].trim()).map((K) => [K[0].trim(), K[1].trim()])),
      exclusions: We.split(/[,\n;]/).map((K) => K.trim()).filter(Boolean)
    };
    ee(!0), ne(null);
    try {
      await r.hubCommand("industrial/preview-bindings", w);
      const K = await r.hubQuery("industrial/binding-preview");
      if (Qe(K), (((P = K == null ? void 0 : K.errors) == null ? void 0 : P.length) ?? 0) > 0 || (((W = K == null ? void 0 : K.missing) == null ? void 0 : W.length) ?? 0) > 0 || (((oe = K == null ? void 0 : K.conflicts) == null ? void 0 : oe.length) ?? 0) > 0) {
        ne("服务端预览发现问题，请检查冲突、缺失资源或例外规则后再创建");
        return;
      }
      await r.hubCommand("industrial/create-bindings", w), await X();
    } catch (K) {
      ne(K instanceof Error ? K.message : String(K));
    } finally {
      ee(!1);
    }
  }, [r, M, We, X, g, vr, A, m, E, x, y, p]), Tr = F(async (w, P) => {
    ee(!0), ne(null);
    try {
      await r.hubCommand("industrial/set-binding-enabled", { id: w.id, enabled: P }), await X();
    } catch (W) {
      ne(W instanceof Error ? W.message : String(W));
    } finally {
      ee(!1);
    }
  }, [r, X]), De = F(async (w, P) => {
    ee(!0), ne(null);
    try {
      await r.hubCommand("industrial/run-binding", {
        bindingId: w.id,
        lookbackDays: P
      }), J(await r.hubQuery("industrial/binding-status"));
    } catch (W) {
      ne(W instanceof Error ? W.message : String(W));
    } finally {
      ee(!1);
    }
  }, [r]), xr = F(async () => {
    ee(!0), ne(null);
    try {
      await r.hubCommand("industrial/import-file-profiles", { csv: we }), Se(""), await X();
    } catch (w) {
      ne(w instanceof Error ? w.message : String(w));
    } finally {
      ee(!1);
    }
  }, [r, X, we]), gr = F(async () => {
    ee(!0), ne(null);
    try {
      await r.hubCommand("industrial/import-bindings", { csv: Ze }), Er(""), await X();
    } catch (w) {
      ne(w instanceof Error ? w.message : String(w));
    } finally {
      ee(!1);
    }
  }, [r, X, Ze]), yr = F(async (w, P) => {
    if (P) {
      ee(!0), ne(null);
      try {
        const W = w === "profiles" ? "industrial/import-file-profiles" : `industrial/import-${w}`;
        await r.hubCommand(W, { xlsx: await y1(P) }), await X();
      } catch (W) {
        ne(W instanceof Error ? W.message : String(W));
      } finally {
        ee(!1);
      }
    }
  }, [r, X]), kr = F(async () => {
    ee(!0), ne(null);
    try {
      await r.hubCommand("industrial/preview-bindings", {
        machineIds: p,
        fileProfileIds: g,
        workflowId: y,
        workflowVersion: x,
        sourceResourcePattern: A,
        targetResourceIds: m,
        triggerKind: E,
        cron: M || null,
        exclusions: We.split(/[,\n;]/).map((w) => w.trim()).filter(Boolean)
      }), Qe(await r.hubQuery("industrial/binding-preview"));
    } catch (w) {
      ne(w instanceof Error ? w.message : String(w));
    } finally {
      ee(!1);
    }
  }, [r, M, We, p, g, g, A, m, E, x, y]), $e = F(async () => {
    ee(!0), ne(null);
    try {
      await r.hubCommand("industrial/preview-import", { csv: Ze }), le(await r.hubQuery("industrial/import-preview"));
    } catch (w) {
      ne(w instanceof Error ? w.message : String(w));
    } finally {
      ee(!1);
    }
  }, [r, Ze]), Ke = F(async () => {
    ee(!0), ne(null);
    try {
      const w = t.filter((P) => P.workflowId === y).map((P) => P.id);
      await r.hubCommand("industrial/upgrade-binding-version", { ids: w, version: ge }), await X();
    } catch (w) {
      ne(w instanceof Error ? w.message : String(w));
    } finally {
      ee(!1);
    }
  }, [t, r, X, ge, y]), Nt = F(async () => {
    ee(!0), ne(null);
    try {
      await r.hubCommand("industrial/import-machines", { csv: N }), H(""), await X();
    } catch (w) {
      ne(w instanceof Error ? w.message : String(w));
    } finally {
      ee(!1);
    }
  }, [r, N, X]), et = F(async () => {
    ne(null);
    try {
      const w = await r.hubQuery("industrial/export-config"), P = (w == null ? void 0 : w.data) ?? w, W = JSON.stringify(P, null, 2);
      S(W), V(P);
      const oe = new Blob([W], { type: "application/json" }), K = window.URL.createObjectURL(oe), Ye = document.createElement("a");
      Ye.href = K, Ye.download = `gocordis-industrial-config-v${(P == null ? void 0 : P.formatVersion) ?? 1}.json`, Ye.click(), window.URL.revokeObjectURL(K);
    } catch (w) {
      ne(w instanceof Error ? w.message : String(w));
    }
  }, [r]), Lr = (w) => {
    const P = {};
    for (const W of w.split(/[\n,]/)) {
      const [oe, K] = W.split("=").map((Ye) => Ye == null ? void 0 : Ye.trim());
      oe && K && (P[oe] = K);
    }
    return P;
  }, q = F(async (w) => {
    if (w) {
      ne(null);
      try {
        const P = await w.text();
        S(P), V(JSON.parse(P));
      } catch (P) {
        ne(P instanceof Error ? P.message : String(P));
      }
    }
  }, []), D = F(async () => {
    ee(!0), ne(null);
    try {
      const w = JSON.parse(G);
      w.resourceMap = Lr(z), w.credentialMap = Lr(ue), await r.hubCommand("industrial/import-config", w), await X();
    } catch (w) {
      ne(w instanceof Error ? w.message : String(w));
    } finally {
      ee(!1);
    }
  }, [r, ue, X, G, z]);
  return /* @__PURE__ */ R(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(U.Title, { level: 4, style: { margin: 0 }, children: "采集任务绑定" }),
    er && /* @__PURE__ */ f(Ee, { type: "error", showIcon: !0, message: er }),
    /* @__PURE__ */ R(Y, { wrap: !0, children: [
      /* @__PURE__ */ f(
        Q,
        {
          "aria-label": "工作流 ID",
          placeholder: "工作流 ID",
          value: y ?? "",
          onChange: (w) => C(w.target.value)
        }
      ),
      /* @__PURE__ */ f(
        it,
        {
          "aria-label": "模板版本",
          min: 1,
          value: x,
          onChange: (w) => _(Number(w) || 1)
        }
      ),
      /* @__PURE__ */ f(
        Q,
        {
          "aria-label": "机台 ID 列表",
          placeholder: "机台 ID，逗号分隔",
          value: p.join(","),
          onChange: (w) => v(w.target.value.split(",").map((P) => P.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ f(
        Q,
        {
          "aria-label": "文件配置 ID 列表",
          placeholder: "文件配置 ID，逗号分隔",
          value: g.join(","),
          onChange: (w) => h(w.target.value.split(",").map((P) => P.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ f(
        Q,
        {
          "aria-label": "目标资源 ID 列表",
          placeholder: "目标资源 ID，逗号分隔",
          value: m.join(","),
          onChange: (w) => k(w.target.value.split(",").map((P) => P.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ R(
        "select",
        {
          "aria-label": "绑定触发策略",
          value: E,
          onChange: (w) => T(w.target.value),
          children: [
            /* @__PURE__ */ f("option", { value: "manual", children: "手动" }),
            /* @__PURE__ */ f("option", { value: "schedule", children: "定时" })
          ]
        }
      ),
      /* @__PURE__ */ f(
        Q,
        {
          "aria-label": "绑定 Cron",
          placeholder: "cron（定时策略）",
          value: M,
          onChange: (w) => ve(w.target.value)
        }
      ),
      /* @__PURE__ */ f(
        Q,
        {
          "aria-label": "来源资源模板",
          placeholder: "来源资源模板，{machine} 占位符",
          value: A,
          onChange: (w) => L(w.target.value)
        }
      ),
      /* @__PURE__ */ f(
        Q,
        {
          "aria-label": "运行参数",
          placeholder: "运行参数，如 lookbackDays=7;shift=day",
          value: vr,
          onChange: (w) => Sr(w.target.value)
        }
      ),
      /* @__PURE__ */ f(
        Q,
        {
          "aria-label": "例外规则",
          placeholder: "例外规则：机台:文件配置，逗号或换行分隔",
          value: We,
          onChange: (w) => Rr(w.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ R(Y, { wrap: !0, children: [
      /* @__PURE__ */ f($, { size: "small", onClick: () => v(a.map((w) => w.id)), children: "全选机台" }),
      /* @__PURE__ */ f($, { size: "small", onClick: () => h(s.map((w) => w.id)), children: "全选文件配置" }),
      /* @__PURE__ */ f($, { size: "small", onClick: () => {
        v([]), h([]);
      }, children: "清空范围" })
    ] }),
    /* @__PURE__ */ f(
      Ee,
      {
        type: "info",
        showIcon: !0,
        message: `预计生成 ${Ne.total} 条绑定；可创建 ${Ne.create} 条`,
        description: `冲突 ${Ne.conflicts.length} 条；缺失资源 ${Ne.missing.length} 条`
      }
    ),
    /* @__PURE__ */ R(Y, { wrap: !0, children: [
      /* @__PURE__ */ f($, { type: "primary", loading: Ce, onClick: () => void ur(), children: "生成绑定" }),
      /* @__PURE__ */ f($, { loading: Ce, onClick: () => void kr(), children: "预览绑定" }),
      /* @__PURE__ */ f($, { loading: Ce, onClick: () => void $e(), children: "预览导入" })
    ] }),
    ze && /* @__PURE__ */ f(
      Ee,
      {
        type: "info",
        showIcon: !0,
        "data-testid": "binding-preview-report",
        message: `批量预览：${ze.total} 组合，创建 ${ze.create}`,
        description: `冲突 ${((ce = ze.conflicts) == null ? void 0 : ce.length) ?? 0}；缺失 ${((se = ze.missing) == null ? void 0 : se.length) ?? 0}；错误 ${((he = ze.errors) == null ? void 0 : he.length) ?? 0}`
      }
    ),
    Be && /* @__PURE__ */ f(
      Ee,
      {
        type: (pe = Be.errors) != null && pe.length ? "warning" : "success",
        showIcon: !0,
        "data-testid": "import-preview-report",
        message: `导入预览：${Be.valid ?? 0}/${Be.total ?? 0} 行有效`,
        description: (Be.errors ?? []).map((w, P) => /* @__PURE__ */ R("div", { "data-testid": `import-error-row-${w.row}`, children: [
          "第 ",
          w.row,
          " 行：",
          w.message
        ] }, P))
      }
    ),
    /* @__PURE__ */ R(Y, { wrap: !0, children: [
      /* @__PURE__ */ f(
        it,
        {
          "aria-label": "升级到模板版本",
          min: 1,
          value: ge,
          onChange: (w) => te(Number(w) || 1)
        }
      ),
      /* @__PURE__ */ f($, { loading: Ce, onClick: () => void Ke(), children: "升级当前工作流绑定版本" })
    ] }),
    /* @__PURE__ */ f(U.Title, { level: 5, style: { margin: 0 }, children: "批量导入机台（CSV）" }),
    /* @__PURE__ */ f(
      Q.TextArea,
      {
        "aria-label": "机台 CSV",
        rows: 4,
        value: N,
        placeholder: "id,name,endpoint,credentialId",
        onChange: (w) => H(w.target.value)
      }
    ),
    /* @__PURE__ */ f($, { loading: Ce, onClick: () => void Nt(), children: "导入机台" }),
    /* @__PURE__ */ f(
      Q.TextArea,
      {
        "aria-label": "文件配置 CSV",
        rows: 3,
        value: we,
        placeholder: "id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required",
        onChange: (w) => Se(w.target.value)
      }
    ),
    /* @__PURE__ */ f($, { loading: Ce, onClick: () => void xr(), children: "导入文件配置" }),
    /* @__PURE__ */ f(
      Q.TextArea,
      {
        "aria-label": "绑定 CSV",
        rows: 3,
        value: Ze,
        placeholder: "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId,targetResourceIds,triggerKind,cron",
        onChange: (w) => Er(w.target.value)
      }
    ),
    /* @__PURE__ */ f($, { loading: Ce, onClick: () => void gr(), children: "导入采集绑定" }),
    /* @__PURE__ */ f(U.Title, { level: 5, style: { margin: 0 }, children: "Excel 批量导入（.xlsx，选择即导入）" }),
    /* @__PURE__ */ R(Y, { wrap: !0, children: [
      /* @__PURE__ */ R("label", { children: [
        "机台 Excel",
        /* @__PURE__ */ f(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "机台 Excel 文件",
            onChange: (w) => {
              var P;
              return void yr("machines", (P = w.target.files) == null ? void 0 : P[0]);
            }
          }
        )
      ] }),
      /* @__PURE__ */ R("label", { children: [
        "文件配置 Excel",
        /* @__PURE__ */ f(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "文件配置 Excel 文件",
            onChange: (w) => {
              var P;
              return void yr("profiles", (P = w.target.files) == null ? void 0 : P[0]);
            }
          }
        )
      ] }),
      /* @__PURE__ */ R("label", { children: [
        "采集绑定 Excel",
        /* @__PURE__ */ f(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "绑定 Excel 文件",
            onChange: (w) => {
              var P;
              return void yr("bindings", (P = w.target.files) == null ? void 0 : P[0]);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ f(U.Title, { level: 5, style: { margin: 0 }, children: "配置包（不含凭证）" }),
    /* @__PURE__ */ R(Y, { wrap: !0, children: [
      /* @__PURE__ */ f($, { onClick: () => void et(), children: "导出配置" }),
      /* @__PURE__ */ f($, { loading: Ce, onClick: () => void D(), children: "导入配置" }),
      /* @__PURE__ */ R("label", { children: [
        "上传配置文件",
        /* @__PURE__ */ f(
          "input",
          {
            type: "file",
            accept: ".json,application/json",
            "aria-label": "上传配置文件",
            onChange: (w) => {
              var P;
              return void q((P = w.target.files) == null ? void 0 : P[0]);
            }
          }
        )
      ] })
    ] }),
    (j == null ? void 0 : j.dependencies) && /* @__PURE__ */ f(
      Ee,
      {
        type: "info",
        showIcon: !0,
        "data-testid": "package-dependencies",
        message: `配置包 v${j.formatVersion ?? 1} · ${j.dependencies.length} 项依赖`,
        description: j.dependencies.map((w, P) => /* @__PURE__ */ R("div", { children: [
          w.kind,
          ": ",
          w.id,
          w.version ? ` @ ${w.version}` : ""
        ] }, P))
      }
    ),
    /* @__PURE__ */ f(
      Q,
      {
        "aria-label": "资源 ID 映射",
        placeholder: "旧资源ID=新资源ID，逗号或换行分隔",
        value: z,
        onChange: (w) => ie(w.target.value)
      }
    ),
    /* @__PURE__ */ f(
      Q,
      {
        "aria-label": "凭证别名映射",
        placeholder: "旧凭证ID=新凭证ID",
        value: ue,
        onChange: (w) => fe(w.target.value)
      }
    ),
    /* @__PURE__ */ f(
      Q.TextArea,
      {
        "aria-label": "配置包",
        rows: 5,
        value: G,
        placeholder: "导出后可跨环境导入；资源 ID 需自行映射",
        onChange: (w) => S(w.target.value)
      }
    ),
    /* @__PURE__ */ f(
      Q,
      {
        "aria-label": "绑定筛选",
        placeholder: "按绑定名称/ID筛选",
        value: Re,
        onChange: (w) => Ue(w.target.value)
      }
    ),
    /* @__PURE__ */ f(
      Ae,
      {
        rowKey: "id",
        dataSource: t.filter((w) => !Re || `${w.id} ${w.name}`.includes(Re)),
        pagination: { pageSize: 20 },
        columns: [
          { title: "绑定", dataIndex: "name" },
          { title: "机台", dataIndex: "machineId" },
          { title: "文件配置", dataIndex: "fileProfileId" },
          { title: "模板版本", dataIndex: "workflowVersion" },
          { title: "运行参数", key: "parameters", render: (w, P) => /* @__PURE__ */ f("span", { "data-testid": `binding-parameters-${P.id}`, children: Object.entries(P.parameters ?? {}).map(([W, oe]) => `${W}=${oe}`).join(";") || "—" }) },
          { title: "状态", dataIndex: "enabled", render: (w, P) => /* @__PURE__ */ R(Y, { children: [
            /* @__PURE__ */ f($, { size: "small", onClick: () => void Tr(P, !P.enabled), children: P.enabled ? "暂停" : "恢复" }),
            /* @__PURE__ */ f($, { size: "small", onClick: () => void De(P, 7), children: "补采 7 天" }),
            /* @__PURE__ */ f($, { size: "small", onClick: () => void De(P, 1), children: "运行" })
          ] }) },
          { title: "排障", key: "diagnostics", render: (w, P) => {
            const W = ((B == null ? void 0 : B.runs) ?? []).filter((K) => K.workflowId === P.workflowId), oe = ((B == null ? void 0 : B.deliveries) ?? []).filter((K) => K.status === "Failed");
            return /* @__PURE__ */ R(U.Text, { style: { fontSize: 11 }, children: [
              W.length ? `最近运行 ${W[0].status}` : "暂无运行",
              " · 失败交付 ",
              oe.length
            ] });
          } }
        ]
      }
    )
  ] });
}
const b1 = `
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
function _1(e) {
  iv(e);
  const r = typeof document > "u" ? null : document.createElement("style");
  r && (r.dataset.industrialDesigner = "true", r.textContent = b1, document.head.appendChild(r));
  const t = ({ ctx: i }) => Lo(r1, { ctx: i });
  t.displayName = "IndustrialWorkflowCanvas";
  const n = (i, s) => {
    const o = ({ block: u, ctx: c }) => Lo(s, { block: u, ctx: c });
    return o.displayName = i, e.blocks.register(i, o);
  }, a = [
    e.blocks.register("industrial-workflow-canvas", t),
    n("industrial-workflow-list", t1),
    n("industrial-resource-console", i1),
    n("industrial-credential-console", s1),
    n("industrial-execution-center", o1),
    n("industrial-file-details", u1),
    n("industrial-machine-console", c1),
    n("industrial-collection-wizard", h1),
    n("industrial-file-profiles", g1),
    n("industrial-binding-console", m1)
  ];
  return () => {
    for (const i of a) i == null || i();
    r == null || r.remove();
  };
}
export {
  eg as g,
  _1 as r
};
