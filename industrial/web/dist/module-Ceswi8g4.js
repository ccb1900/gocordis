let Tr = null;
function Bp(e) {
  Tr = e;
}
function Ee() {
  if (!Tr && typeof globalThis < "u" && (Tr = globalThis.__CORDIS_CONSOLE ?? null), !Tr)
    throw new Error("[industrial-designer] console facade is not available");
  return Tr;
}
const v = (...e) => Ee().jsxRuntime.jsx(...e), T = (...e) => Ee().jsxRuntime.jsxs(...e), Nr = (e) => Ee().jsxRuntime.jsx(Ee().jsxRuntime.Fragment, e), Io = (...e) => Ee().React.createElement(...e), z = (...e) => Ee().React.useCallback(...e), le = (...e) => Ee().React.useEffect(...e), tt = (...e) => Ee().React.useMemo(...e), be = (...e) => Ee().React.useRef(...e), q = (...e) => Ee().React.useState(...e), ie = (e) => {
  const r = (t) => {
    const n = e.split(".").reduce((a, i) => a == null ? void 0 : a[i], Ee().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Io(n, t ?? {});
  };
  return r.displayName = `facade.${e}`, r;
}, Ce = ie("Alert"), B = ie("Button"), Zo = ie("Divider"), Up = ie("Drawer"), vh = ie("Empty"), qo = Object.assign(ie("Form"), {
  Item: ie("Form.Item"),
  useForm: (...e) => Ee().antd.Form.useForm(...e)
}), V = Object.assign(ie("Input"), {
  Number: ie("InputNumber"),
  TextArea: ie("Input.TextArea")
}), xo = ie("InputNumber"), Kp = Object.assign(ie("Layout"), {
  Header: ie("Layout.Header"),
  Content: ie("Layout.Content")
}), ur = ie("Modal"), ye = ie("Select"), K = ie("Space"), gh = ie("Spin"), nt = ie("Switch"), qe = ie("Table"), ke = ie("Tag"), $ = Object.assign(ie("Typography"), {
  Title: ie("Typography.Title"),
  Text: ie("Typography.Text"),
  Paragraph: ie("Typography.Paragraph")
}), Hp = new Proxy({}, {
  get: (e, r) => (...t) => {
    var a;
    const n = (a = Ee().antd.message) == null ? void 0 : a[r];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${r} unavailable`);
    return n(...t);
  }
}), yh = 148, mh = 46, eu = {
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
}, ru = (e) => eu[e] ?? eu[e.split(".")[0]] ?? "#4d6bfe";
function Vp(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function Wp(e, r = /* @__PURE__ */ new Map(), t) {
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
          fill: ru(i),
          stroke: s ? "#111111" : ru(i),
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
      id: Vp(n),
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
function Yp(e, r, t) {
  const n = t && t.length > 0 ? new Set(t) : null, a = {};
  for (const i of e.nodes) {
    if (!r.has(i.id) || n && !n.has(i.id)) continue;
    const s = i.style.x, o = i.style.y;
    typeof s == "number" && typeof o == "number" && (a[i.id] = { x: s, y: o });
  }
  return a;
}
function Qp(e, r) {
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
        size: [yh, mh],
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
      { type: "minimap", size: [180, 110], position: "right-bottom" }
    ],
    behaviors: bh()
  };
}
const Jp = "__industrial_drag_target__", Xp = "__industrial_connect_source__", qt = (e, r) => {
  var t;
  return (((t = e.isPanModifier) == null ? void 0 : t.call(e)) ?? !1) || r.button === 1 || r.buttons === 4 || r.shiftKey === !0;
}, tu = (e, r) => {
  var c, d;
  if (r.targetType !== "node" || !((c = r.target) != null && c.id)) return !1;
  const t = ((d = e.gestureOrigin) == null ? void 0 : d.call(e)) ?? r.client ?? r.canvas;
  if (!t || !e.graph) return !1;
  const n = String(r.target.id), a = e.graph.getNodeData().find((h) => String(h.id) === n), i = a == null ? void 0 : a.style;
  if (typeof (i == null ? void 0 : i.x) != "number" || typeof i.y != "number") return !1;
  const [s, o] = e.graph.getCanvasByClient([t.x, t.y]), u = Math.abs(s - i.x), l = Math.abs(o - i.y);
  return Math.abs(u - yh / 2) <= 14 && l <= mh / 2 + 10;
};
function bh(e = {}) {
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
      enable: (t) => t.targetType === "canvas" || qt(e, t),
      range: 0.9
    },
    {
      type: "drag-element",
      state: Jp,
      enable: (t) => (t.targetType === "node" || t.targetType === "combo") && !tu(e, t) && !qt(e, t),
      onFinish: (t) => {
        var s, o;
        const n = e.graph;
        if (!n) return;
        const a = Array.isArray(t) ? t : t ? [String(t)] : void 0, i = n.getNodeData().map((u) => ({
          id: String(u.id),
          data: u.data ?? {},
          style: u.style ?? {}
        }));
        (o = e.onPositions) == null || o.call(e, Yp(
          { nodes: i },
          ((s = e.allowedIds) == null ? void 0 : s.call(e)) ?? /* @__PURE__ */ new Set(),
          a
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: Xp,
      enable: (t) => qt(e, t) ? (r.armed = !1, !1) : r.armed ? t.targetType === "node" || t.targetType === "combo" : tu(e, t) ? (r.armed = !0, !0) : !1,
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
const Zp = (e) => {
  var t;
  const r = e;
  return (r == null ? void 0 : r.targetType) === "node" ? (t = r.target) == null ? void 0 : t.id : void 0;
};
function ev(e) {
  const {
    definition: r,
    catalog: t,
    selectedId: n,
    onSelect: a,
    onDropNode: i,
    onCreateEdge: s,
    onPositions: o,
    onSelectIds: u,
    onGraphReady: l
  } = e, c = be(null), d = be(null), h = be(!1), p = be(!1), g = be({ armed: !1 }), f = be(null), y = be({ onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o });
  y.current = { onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o };
  const I = tt(
    () => Wp(r, t, n),
    [r, t, n]
  ), S = be(I);
  S.current = I;
  const _ = be(/* @__PURE__ */ new Set());
  return _.current = new Set(r.nodes.map((k) => k.id)), le(() => {
    const k = (O) => {
      O.code === "Space" && !O.repeat && (p.current = !0);
    }, m = (O) => {
      O.code === "Space" && (p.current = !1);
    }, C = () => {
      p.current = !1;
    };
    return window.addEventListener("keydown", k), window.addEventListener("keyup", m), window.addEventListener("blur", C), () => {
      window.removeEventListener("keydown", k), window.removeEventListener("keyup", m), window.removeEventListener("blur", C);
    };
  }, []), le(() => {
    const k = c.current;
    if (!k || h.current) return;
    let m = !1;
    return import("./esm-DqcK6hLR.js").then(({ Graph: C }) => {
      if (m || !c.current || h.current) return;
      const O = Qp(S.current, k), L = new C({
        ...O,
        behaviors: bh({
          graph: {
            getNodeData: () => {
              var N;
              return ((N = d.current) == null ? void 0 : N.getNodeData()) ?? [];
            },
            getCanvasByClient: (N) => {
              var F;
              const w = ((F = d.current) == null ? void 0 : F.getCanvasByClient(N)) ?? N;
              return [w[0], w[1]];
            }
          },
          isPanModifier: () => p.current,
          gestureOrigin: () => f.current,
          connectionState: g.current,
          allowedIds: () => _.current,
          onPositions: (N) => y.current.onPositions(N),
          onSelectIds: (N) => u == null ? void 0 : u(N),
          onCreateEdge: (N, w) => y.current.onCreateEdge(N, w)
        })
      });
      L.on("node:click", (N) => {
        const w = Zp(N);
        w && y.current.onSelect(w);
      }), L.on("edge:click", () => y.current.onSelect(null)), L.on("canvas:click", () => y.current.onSelect(null)), L.on("pointerdown", (N) => {
        const w = N;
        f.current = w.client ? { x: w.client.x, y: w.client.y } : null;
      }), L.on("pointerup", () => {
        queueMicrotask(() => {
          g.current.armed = !1, f.current = null;
        });
      }), d.current = L, k.__industrialGraph = L, l == null || l(L), h.current = !0, L.render();
    }), () => {
      m = !0;
    };
  }, []), le(() => {
    const k = d.current;
    !k || !h.current || (k.setData(I), k.draw());
  }, [I]), le(() => () => {
    var k;
    (k = d.current) == null || k.destroy(), d.current = null, h.current = !1;
  }, []), /* @__PURE__ */ v("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ T("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ v(
      "div",
      {
        ref: c,
        className: "industrial-graph-host",
        onDragOver: (k) => k.preventDefault(),
        onDrop: (k) => {
          k.preventDefault();
          const m = k.dataTransfer.getData("application/x-industrial-node") || k.dataTransfer.getData("text/plain");
          if (!m) return;
          const C = d.current, O = c.current;
          if (!C || !O) return;
          const L = O.getBoundingClientRect(), N = C.getCanvasByViewport([
            k.clientX - L.left,
            k.clientY - L.top
          ]);
          y.current.onDropNode(m, N[0], N[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ v("div", { className: "industrial-graph-hint", children: "Shift+拖拽框选 · 拖节点移动 · 端口拖拽连线 · 滚轮缩放 · 空格/中键平移" })
  ] }) });
}
function rv({ descriptors: e, loading: r, error: t, onAdd: n }) {
  const a = /* @__PURE__ */ new Map();
  for (const s of e) {
    const o = s.category.split(".")[0] || "General", u = a.get(o) ?? [];
    u.push(s), a.set(o, u);
  }
  const i = (s, o) => {
    s.dataTransfer.setData("application/x-industrial-node", o.nodeTypeId), s.dataTransfer.setData("text/plain", o.nodeTypeId), s.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ T("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ v($.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ v($.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    r && /* @__PURE__ */ v(gh, { size: "small", style: { display: "block", marginTop: 16 } }),
    t && /* @__PURE__ */ v($.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: t }),
    !r && !t && e.length === 0 && /* @__PURE__ */ v(vh, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(a.entries()).map(([s, o]) => /* @__PURE__ */ T("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ v($.Text, { strong: !0, style: { fontSize: 12 }, children: s }),
      o.map((u) => /* @__PURE__ */ T(
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
            /* @__PURE__ */ v("div", { className: "industrial-palette-node-title", children: u.name }),
            /* @__PURE__ */ T("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ v(ke, { bordered: !1, color: "blue", children: u.version }),
              /* @__PURE__ */ v($.Text, { type: "secondary", style: { fontSize: 11 }, children: u.nodeTypeId })
            ] }),
            /* @__PURE__ */ T("div", { className: "industrial-palette-node-ports", children: [
              u.inputs.map((l) => /* @__PURE__ */ T(ke, { bordered: !1, color: "default", children: [
                "in ",
                l.name,
                ": ",
                l.type
              ] }, `in-${l.name}`)),
              u.outputs.map((l) => /* @__PURE__ */ T(ke, { bordered: !1, color: "green", children: [
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
const Yr = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {}
});
function tv(e) {
  const r = {};
  for (const [t, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (r[t] = n.default);
  return r;
}
function nu(e, r, t = 0, n = 0) {
  return { id: r, type: e.nodeTypeId, config: tv(e), x: t, y: n };
}
function rr(e, r) {
  return e.get(r);
}
function Qr(e, r, t) {
  return e[t].find((n) => n.name === r);
}
function ko(e, r) {
  return e !== "Unknown" && r !== "Unknown" && e === r;
}
function au(e, r) {
  for (const t of e.outputs)
    for (const n of r.inputs)
      if (ko(t.type, n.type))
        return { sourcePort: t.name, targetPort: n.name };
  return null;
}
const Ie = (e, r, t) => ({ path: e, code: r, message: t });
function nv(e, r) {
  return e.source === r.source && e.sourcePort === r.sourcePort && e.target === r.target && e.targetPort === r.targetPort;
}
function av(e, r, t) {
  const n = `edges.${e.edges.length}`, a = e.nodes.find((c) => c.id === r.source), i = e.nodes.find((c) => c.id === r.target);
  if (!a || !i)
    return { definition: e, issue: Ie(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((c) => nv(c, r)))
    return { definition: e };
  const s = rr(t, a.type), o = rr(t, i.type);
  if (!s || !o)
    return { definition: e, issue: Ie(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const u = Qr(s, r.sourcePort, "outputs"), l = Qr(o, r.targetPort, "inputs");
  return !u || !l ? { definition: e, issue: Ie(n, "PORT_MISSING", "declared port does not exist") } : ko(u.type, l.type) ? { definition: { ...e, edges: [...e.edges, r] } } : {
    definition: e,
    issue: Ie(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${u.type} → ${l.type} is not a legal connection`
    )
  };
}
function iu(e, r) {
  return {
    ...e,
    nodes: e.nodes.filter((t) => t.id !== r),
    edges: e.edges.filter((t) => t.source !== r && t.target !== r)
  };
}
function iv(e, r) {
  return { ...e, edges: e.edges.filter((t, n) => n !== r) };
}
function sv(e, r) {
  const t = { ...e };
  for (const [n, a] of Object.entries(r))
    a === void 0 ? delete t[n] : t[n] = a;
  return t;
}
function ov(e, r, t) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === r ? { ...n, config: t } : n)
  };
}
function uv(e, r) {
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
function cv(e) {
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
function lv(e, r, t) {
  const n = [];
  e.workflowId.trim() || n.push(Ie("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(Ie("workflow", "INVALID_VERSION", "version must be positive"));
  const a = /* @__PURE__ */ new Map();
  for (const o of e.nodes)
    a.has(o.id) && n.push(Ie(`nodes.${o.id}`, "DUPLICATE_NODE_ID", `duplicate node '${o.id}'`)), a.set(o.id, o);
  const i = /* @__PURE__ */ new Map();
  for (const o of e.nodes) {
    const u = rr(r, o.type);
    if (!u) {
      n.push(Ie(
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
      (d == null || d === "") && n.push(Ie(
        `nodes.${o.id}.config.${c}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${c}' is missing`
      ));
    }
    t && typeof o.config.resourceId == "string" && (t.has(o.config.resourceId) || n.push(Ie(
      `nodes.${o.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${o.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((o, u) => {
    const l = a.get(o.source), c = a.get(o.target);
    if (!l || !c) {
      n.push(Ie(`edges.${u}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const d = i.get(o.source), h = i.get(o.target);
    if (!d || !h) return;
    const p = Qr(d, o.sourcePort, "outputs"), g = Qr(h, o.targetPort, "inputs");
    if (!p || !g) {
      n.push(Ie(`edges.${u}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    ko(p.type, g.type) || n.push(Ie(
      `edges.${u}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${p.type} → ${g.type} is not a legal connection`
    ));
  });
  for (const o of e.nodes) {
    const u = i.get(o.id);
    if (u)
      for (const l of u.inputs.filter((c) => c.required))
        e.edges.some((c) => c.target === o.id && c.targetPort === l.name && a.has(c.source)) || n.push(Ie(
          `nodes.${o.id}.inputs.${l.name}`,
          "MISSING_INPUT",
          `required input '${l.name}' is not connected`
        ));
  }
  const s = cv(e);
  return s && n.push(Ie(`nodes.${s[0]}`, "GRAPH_CYCLE", `cycle: ${s.join(" → ")}`)), n;
}
const Eo = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function dv(e) {
  const r = Eo(e.additionalProperties).enum;
  if (!Array.isArray(r)) return null;
  const t = r.filter((n) => typeof n == "string");
  return t.length > 0 ? t : null;
}
function _h(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && Eo(e.items).type === "string" ? "string-list" : e.type === "object" && Eo(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function Co(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, r]) => r != null && typeof r != "object").map(([r, t]) => ({ key: r, value: String(t) }));
}
function su(e) {
  const r = {}, t = [];
  for (const n of e) {
    const a = n.key.trim();
    a && (Object.prototype.hasOwnProperty.call(r, a) && t.push(a), r[a] = n.value);
  }
  return { record: r, duplicateKeys: t };
}
function Kr(e) {
  if (!Array.isArray(e)) return [];
  const r = /* @__PURE__ */ new Set(), t = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const a = n.trim();
    !a || r.has(a) || (r.add(a), t.push(a));
  }
  return t;
}
function fv(e, r) {
  const t = /* @__PURE__ */ new Set();
  for (const [n, a] of Object.entries(e))
    if (_h(a) === "key-value")
      for (const i of Co(r[n]))
        i.key.trim() && t.add(i.key.trim());
  return [...t];
}
function hv({
  value: e,
  onChange: r,
  valueOptions: t,
  keyTitle: n = "目标字段",
  valueTitle: a = "来源字段",
  testId: i
}) {
  const s = JSON.stringify(e ?? {}), [o, u] = q(() => Co(e)), [l, c] = q(s);
  le(() => {
    s !== l && (u(Co(e)), c(s));
  }, [s, l, e]);
  const d = (p) => {
    u(p), r == null || r(su(p).record);
  }, { duplicateKeys: h } = su(o);
  return /* @__PURE__ */ T("div", { "data-testid": i, className: "industrial-kv-editor", children: [
    o.map((p, g) => /* @__PURE__ */ T("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ v(
        V,
        {
          "aria-label": `${n} ${g + 1}`,
          value: p.key,
          placeholder: n,
          onChange: (f) => d(o.map((y, I) => I === g ? { ...y, key: f.target.value } : y))
        }
      ),
      t && t.length > 0 ? /* @__PURE__ */ v(
        ye,
        {
          "aria-label": `${a} ${g + 1}`,
          value: p.value || void 0,
          placeholder: a,
          options: t.map((f) => ({ label: f, value: f })),
          onChange: (f) => d(o.map((y, I) => I === g ? { ...y, value: f ?? "" } : y))
        }
      ) : /* @__PURE__ */ v(
        V,
        {
          "aria-label": `${a} ${g + 1}`,
          value: p.value,
          placeholder: a,
          onChange: (f) => d(o.map((y, I) => I === g ? { ...y, value: f.target.value } : y))
        }
      ),
      /* @__PURE__ */ v(
        B,
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
    /* @__PURE__ */ v(
      B,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => d([...o, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    h.length > 0 && /* @__PURE__ */ T($.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      h.join("、")
    ] })
  ] });
}
function pv({ value: e, onChange: r, suggestions: t = [], testId: n }) {
  const a = JSON.stringify(Kr(e)), [i, s] = q(() => Kr(e)), [o, u] = q(a);
  le(() => {
    a !== o && (s(Kr(e)), u(a));
  }, [a, o, e]);
  const l = (c) => {
    s(c), r == null || r(Kr(c));
  };
  return /* @__PURE__ */ T("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ v("datalist", { id: `${n ?? "list"}-suggestions`, children: t.map((c) => /* @__PURE__ */ v("option", { value: c }, c)) }),
    i.map((c, d) => /* @__PURE__ */ T("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ v(
        V,
        {
          "aria-label": `字段 ${d + 1}`,
          value: c,
          list: `${n ?? "list"}-suggestions`,
          onChange: (h) => l(i.map((p, g) => g === d ? h.target.value : p))
        }
      ),
      /* @__PURE__ */ v(
        B,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${d + 1}`,
          onClick: () => l(i.filter((h, p) => p !== d)),
          children: "删"
        }
      )
    ] }, d)),
    /* @__PURE__ */ v(B, { size: "small", type: "dashed", block: !0, onClick: () => l([...i, ""]), children: "添加字段" }),
    i.length === 0 && /* @__PURE__ */ v($.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function vv({ value: e, onChange: r }) {
  const t = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, a] = q(t), [i, s] = q(null);
  le(() => {
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
  return /* @__PURE__ */ T("div", { children: [
    /* @__PURE__ */ v(V.TextArea, { rows: 5, value: n, onChange: (u) => o(u.target.value) }),
    i && /* @__PURE__ */ v($.Text, { type: "danger", style: { fontSize: 11 }, children: i })
  ] });
}
function gv({ value: e, onChange: r, resources: t, resourceType: n, label: a }) {
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
          /* @__PURE__ */ v("option", { value: "", children: "未选择" }),
          i.map((o) => /* @__PURE__ */ v("option", { value: o.value, children: o.label }, o.value))
        ]
      }
    ),
    (s == null ? void 0 : s.credentialId) && /* @__PURE__ */ T($.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      s.credentialName ?? s.credentialId
    ] })
  ] });
}
function yv({ name: e, schema: r, required: t, resources: n, resourceType: a, properties: i, config: s }) {
  let o, u = "value";
  switch (_h(r)) {
    case "resource":
      o = /* @__PURE__ */ v(
        gv,
        {
          resources: n,
          resourceType: a,
          label: String(r.title ?? e)
        }
      );
      break;
    case "enum":
      o = /* @__PURE__ */ v(
        ye,
        {
          allowClear: !0,
          options: (r.enum ?? []).map((l) => ({ label: String(l), value: l }))
        }
      );
      break;
    case "number":
      o = /* @__PURE__ */ v(xo, { style: { width: "100%" } });
      break;
    case "boolean":
      o = /* @__PURE__ */ v(nt, {}), u = "checked";
      break;
    case "key-value":
      o = /* @__PURE__ */ v(
        hv,
        {
          testId: `field-${e}`,
          valueOptions: dv(r) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      o = /* @__PURE__ */ v(pv, { testId: `field-${e}`, suggestions: fv(i, s) });
      break;
    case "json":
      o = /* @__PURE__ */ v(vv, {});
      break;
    default:
      o = /* @__PURE__ */ v(V, { placeholder: r.description });
      break;
  }
  return /* @__PURE__ */ v(
    qo.Item,
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
function mv(e) {
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
    onSelectNode: p
  } = e, [g] = qo.useForm(), [f, y] = q("{}"), [I, S] = q(null), [_, A] = q(), [k, m] = q(), [C, O] = q(!1), [L, N] = q(!1);
  le(() => {
    g.setFieldsValue((r == null ? void 0 : r.config) ?? {}), y(JSON.stringify((r == null ? void 0 : r.config) ?? {}, null, 2)), S(null);
  }, [g, r == null ? void 0 : r.id, r == null ? void 0 : r.config]);
  const w = new Set((t == null ? void 0 : t.configurationSchema.required) ?? []), F = Object.entries((t == null ? void 0 : t.configurationSchema.properties) ?? {}), j = () => {
    if (F.length > 0) {
      g.validateFields().then((P) => u(sv((r == null ? void 0 : r.config) ?? {}, P)));
      return;
    }
    try {
      const P = JSON.parse(f);
      if (P === null || typeof P != "object" || Array.isArray(P))
        throw new Error("配置必须是 JSON 对象");
      S(null), u(P);
    } catch (P) {
      S(P.message);
    }
  }, X = (r && !C ? a.map((P, G) => ({ edge: P, index: G })).filter(({ edge: P }) => P.source === r.id || P.target === r.id) : a.map((P, G) => ({ edge: P, index: G }))).map(({ edge: P, index: G }) => ({
    ...P,
    index: G,
    key: `${G}:${P.source}->${P.target}`
  })), ae = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (P, G) => /* @__PURE__ */ T($.Text, { style: { fontSize: 11 }, children: [
        G.source,
        " → ",
        G.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (P, G) => {
        const Z = i.get(G.source), de = i.get(G.target);
        return /* @__PURE__ */ T("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ v(
            ye,
            {
              size: "small",
              value: G.sourcePort,
              style: { width: 110 },
              onChange: (D) => h(G.index, { ...G, sourcePort: D }),
              options: ((Z == null ? void 0 : Z.outputs) ?? []).map((D) => ({ label: `${D.name} (${D.type})`, value: D.name }))
            }
          ),
          /* @__PURE__ */ v(
            ye,
            {
              size: "small",
              value: G.targetPort,
              style: { width: 110 },
              onChange: (D) => h(G.index, { ...G, targetPort: D }),
              options: ((de == null ? void 0 : de.inputs) ?? []).map((D) => ({ label: `${D.name} (${D.type})`, value: D.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (P, G) => /* @__PURE__ */ v(B, { danger: !0, size: "small", type: "text", onClick: () => c(G.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ T("div", { className: "industrial-properties-body", children: [
    !r && /* @__PURE__ */ v(vh, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    r && /* @__PURE__ */ T(Nr, { children: [
      /* @__PURE__ */ v($.Title, { level: 5, style: { margin: 0 }, children: (t == null ? void 0 : t.name) ?? r.type }),
      /* @__PURE__ */ v($.Text, { type: "secondary", style: { fontSize: 11 }, children: r.id }),
      /* @__PURE__ */ v($.Paragraph, { type: "secondary", style: { fontSize: 12, marginTop: 6 }, children: (t == null ? void 0 : t.description) ?? "请按业务含义配置该节点。" }),
      /* @__PURE__ */ T(K, { wrap: !0, size: 4, style: { marginBottom: 8 }, children: [
        /* @__PURE__ */ v(ke, { color: "blue", children: (t == null ? void 0 : t.category) ?? "采集节点" }),
        /* @__PURE__ */ T(ke, { children: [
          "版本 ",
          (t == null ? void 0 : t.version) ?? "1.0.0"
        ] }),
        t == null ? void 0 : t.inputs.map((P) => /* @__PURE__ */ T(ke, { color: P.required ? "red" : "default", children: [
          "输入 ",
          P.name,
          " · ",
          P.type
        ] }, `in-${P.name}`)),
        t == null ? void 0 : t.outputs.map((P) => /* @__PURE__ */ T(ke, { color: "green", children: [
          "输出 ",
          P.name,
          " · ",
          P.type
        ] }, `out-${P.name}`))
      ] }),
      /* @__PURE__ */ v(qo, { form: g, layout: "vertical", size: "small", style: { marginTop: 12 }, children: F.map(([P, G]) => /* @__PURE__ */ v(
        yv,
        {
          name: P,
          schema: G,
          required: w.has(P),
          resources: s,
          resourceType: typeof (t == null ? void 0 : t.configurationSchema.resourceType) == "string" ? t.configurationSchema.resourceType : void 0,
          properties: (t == null ? void 0 : t.configurationSchema.properties) ?? {},
          config: (r == null ? void 0 : r.config) ?? {}
        },
        P
      )) }),
      F.length === 0 && /* @__PURE__ */ v($.Text, { type: "secondary", style: { fontSize: 12 }, children: "此节点无需填写业务参数；保留默认配置即可。" }),
      /* @__PURE__ */ v(B, { type: "link", size: "small", onClick: () => N((P) => !P), children: L ? "收起高级配置" : "高级配置" }),
      L && /* @__PURE__ */ T(Nr, { children: [
        /* @__PURE__ */ v($.Text, { type: "secondary", style: { fontSize: 12 }, children: "高级配置直接编辑节点 JSON，仅在需要复杂表达式时使用。" }),
        /* @__PURE__ */ v(
          V.TextArea,
          {
            rows: 8,
            value: f,
            style: { marginTop: 6 },
            onChange: (P) => y(P.target.value)
          }
        ),
        I && /* @__PURE__ */ v($.Text, { type: "danger", style: { fontSize: 12 }, children: I }),
        (t == null ? void 0 : t.activityType) && /* @__PURE__ */ T($.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
          "运行时 Activity：",
          t.activityType
        ] })
      ] }),
      /* @__PURE__ */ v(B, { type: "primary", block: !0, onClick: j, children: "应用配置" }),
      /* @__PURE__ */ v(B, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: l, children: "删除节点" })
    ] }),
    /* @__PURE__ */ T(Zo, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "相关连线 (",
      X.length,
      " / ",
      a.length,
      ")"
    ] }),
    r && /* @__PURE__ */ v(B, { type: "link", size: "small", onClick: () => O((P) => !P), children: C ? "仅看当前节点" : "显示全部连线" }),
    n.length > 1 && /* @__PURE__ */ T(K, { wrap: !0, style: { marginBottom: 8 }, children: [
      /* @__PURE__ */ T(
        "select",
        {
          "aria-label": "连线起点",
          value: _ ?? "",
          onChange: (P) => A(P.target.value || void 0),
          children: [
            /* @__PURE__ */ v("option", { value: "", children: "起点节点" }),
            n.map((P) => /* @__PURE__ */ v("option", { value: P.id, children: P.id }, P.id))
          ]
        }
      ),
      /* @__PURE__ */ T(
        "select",
        {
          "aria-label": "连线终点",
          value: k ?? "",
          onChange: (P) => m(P.target.value || void 0),
          children: [
            /* @__PURE__ */ v("option", { value: "", children: "终点节点" }),
            n.map((P) => /* @__PURE__ */ v("option", { value: P.id, children: P.id }, P.id))
          ]
        }
      ),
      /* @__PURE__ */ v(
        B,
        {
          size: "small",
          disabled: !_ || !k || _ === k,
          onClick: () => {
            _ && k && d(_, k);
          },
          children: "添加连线"
        }
      )
    ] }),
    a.length === 0 ? /* @__PURE__ */ v($.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ v(qe, { size: "small", columns: ae, dataSource: X, pagination: !1 }),
    o.length > 0 && /* @__PURE__ */ T(Nr, { children: [
      /* @__PURE__ */ T(Zo, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        o.length,
        ")"
      ] }),
      o.map((P, G) => /* @__PURE__ */ T("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ v(ke, { color: "red", children: P.code }),
        /* @__PURE__ */ T(
          $.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const Z = /^nodes\.([^.]+)/.exec(P.path);
              Z && p(Z[1]);
            },
            children: [
              P.path,
              ": ",
              P.message
            ]
          }
        )
      ] }, `${P.path}-${G}`))
    ] })
  ] });
}
const bv = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class _v extends Error {
  constructor(r) {
    super(r), this.name = "DtoError";
  }
}
const W = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, fe = (e) => Array.isArray(e) ? e : [], x = (e, r = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : r ?? "", se = (e, r = 0) => {
  const t = typeof e == "string" ? Number(e) : e;
  return typeof t == "number" && Number.isFinite(t) ? t : r;
}, b = (e, ...r) => {
  for (const t of r)
    if (e[t] !== void 0 && e[t] !== null) return e[t];
};
function _e(e) {
  const r = W(e);
  if (Object.prototype.hasOwnProperty.call(r, "data")) return r.data;
  if (typeof r.code == "string" && typeof r.message == "string")
    throw new _v(`${r.code}: ${r.message}`);
  return e;
}
function wv(e) {
  const r = x(e).trim().toLowerCase();
  return bv.find((n) => n.toLowerCase() === r) ?? "Unknown";
}
function Iv(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const r = W(e), t = x(b(r, "name", "port", "id")).trim();
  return t ? {
    name: t,
    type: wv(b(r, "type", "portType", "dataType")),
    required: b(r, "required", "isRequired") === !0
  } : null;
}
function ou(e) {
  const r = fe(e).map(Iv).filter((n) => n !== null), t = /* @__PURE__ */ new Set();
  return r.filter((n) => t.has(n.name) ? !1 : (t.add(n.name), !0));
}
const qv = (e) => {
  const r = W(e);
  return Object.keys(r).length === 0 ? { type: "object", properties: {} } : r;
};
function Ev(e) {
  const r = W(e), t = x(b(r, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return t ? {
    nodeTypeId: t,
    name: x(b(r, "name", "title"), t),
    description: x(b(r, "description", "desc")),
    category: x(b(r, "category", "group"), "General"),
    icon: x(b(r, "icon"), "AppstoreOutlined"),
    version: x(b(r, "version"), "1.0.0"),
    inputs: ou(b(r, "inputs", "inputPorts", "input_ports")),
    outputs: ou(b(r, "outputs", "outputPorts", "output_ports")),
    configurationSchema: qv(b(r, "configurationSchema", "configSchema", "schema")),
    activityType: x(b(r, "activityType", "activity_type")),
    dependencies: fe(b(r, "dependencies")).map((n) => x(n)).filter(Boolean)
  } : null;
}
function wh(e) {
  const r = _e(e), t = Array.isArray(r) ? r : fe(b(W(r), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return t.map(Ev).filter((a) => a !== null).filter((a) => n.has(a.nodeTypeId) ? !1 : (n.add(a.nodeTypeId), !0));
}
function Ih(e) {
  const r = _e(e), t = Array.isArray(r) ? r : fe(b(W(r), "resources", "items")), n = /* @__PURE__ */ new Set();
  return t.map((a) => {
    const i = W(a), s = x(b(i, "id")).trim();
    return !s || n.has(s) ? null : (n.add(s), {
      id: s,
      name: x(b(i, "name"), s),
      type: x(b(i, "type"), "unknown"),
      enabled: i.enabled !== !1,
      credentialId: typeof i.credentialId == "string" ? i.credentialId : null,
      credentialName: typeof i.credentialName == "string" ? i.credentialName : null
    });
  }).filter((a) => a !== null);
}
function qh(e) {
  const r = W(e), t = x(b(r, "workflowId", "workflow_id", "id")).trim();
  return t ? {
    workflowId: t,
    name: x(b(r, "name", "title"), t),
    version: se(b(r, "version"), 1)
  } : null;
}
function Eh(e) {
  const r = _e(e);
  return (Array.isArray(r) ? r : fe(b(W(r), "workflows", "items"))).map(qh).filter((n) => n !== null);
}
function Jr(e) {
  const r = W(e), t = {};
  for (const [n, a] of Object.entries(r))
    a !== void 0 && (t[n] = a);
  return t;
}
function Cv(e) {
  const r = W(e), t = x(b(r, "id", "nodeId", "node_id")).trim();
  if (!t) return null;
  const n = W(b(r, "position", "layout"));
  return {
    id: t,
    type: x(b(r, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: Jr(b(r, "config", "configuration")),
    x: se(b(r, "x") ?? b(n, "x"), 0),
    y: se(b(r, "y") ?? b(n, "y"), 0)
  };
}
function Sv(e) {
  const r = W(e), t = x(b(r, "source", "from", "sourceId")).trim(), n = x(b(r, "target", "to", "targetId")).trim();
  return !t || !n ? null : {
    source: t,
    sourcePort: x(b(r, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: x(b(r, "targetPort", "target_port", "toPort"), "in")
  };
}
function Tv(e) {
  const r = _e(e), t = W(r), n = qh(t) ?? { workflowId: "", name: "", version: 1 }, a = fe(b(t, "nodes")).map(Cv).filter((u) => u !== null), i = fe(b(t, "edges")).map(Sv).filter((u) => u !== null), s = W(b(t, "trigger")), o = {
    kind: x(b(s, "kind", "type"), "manual"),
    cron: b(s, "cron") === void 0 ? null : x(s.cron),
    eventName: b(s, "eventName", "event_name") === void 0 ? null : x(b(s, "eventName", "event_name")),
    config: Jr(b(s, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: a,
    edges: i,
    trigger: o,
    resourceReferences: fe(b(t, "resourceReferences", "resource_references")).map((u) => x(u)).filter(Boolean),
    layout: Jr(b(t, "layout"))
  };
}
const yr = (e) => e === !0 || typeof e == "string" && e.toLowerCase() === "true";
function Rv(e) {
  const r = _e(e);
  return (Array.isArray(r) ? r : fe(b(W(r), "resources", "items"))).map((n) => {
    const a = W(n), i = b(a, "credentialId", "credential_id");
    return {
      id: x(b(a, "id"), "—"),
      name: x(b(a, "name"), "—"),
      type: x(b(a, "type"), "—"),
      provider: x(b(a, "provider"), "—"),
      enabled: yr(b(a, "enabled")),
      credentialId: i == null ? null : x(i),
      credentialName: b(a, "credentialName") == null ? null : x(b(a, "credentialName")),
      config: Jr(b(a, "config", "configuration"))
    };
  });
}
function Ch(e) {
  const r = _e(e);
  return (Array.isArray(r) ? r : fe(b(W(r), "credentials", "items"))).map((n) => {
    const a = W(n);
    return {
      id: x(b(a, "id"), "—"),
      name: x(b(a, "name"), "—"),
      kind: x(b(a, "kind"), "—"),
      version: se(b(a, "version"))
    };
  });
}
function xv(e) {
  const r = _e(e);
  return (Array.isArray(r) ? r : fe(b(W(r), "tests", "items"))).map((n) => {
    const a = W(n);
    return {
      resourceId: x(b(a, "resourceId", "resource_id"), "—"),
      ok: yr(b(a, "ok")),
      detail: x(b(a, "detail"), "")
    };
  });
}
const Sh = (e) => {
  if (e == null) return null;
  const r = W(e);
  return Object.keys(r).length === 0 ? null : {
    code: x(b(r, "code"), "OTHER"),
    message: x(b(r, "message"), ""),
    action: x(b(r, "action"), "")
  };
};
function kv(e) {
  const r = _e(e);
  return fe(b(W(r), "triggers", "items")).map((n) => {
    const a = W(n), i = (s) => b(a, s) == null ? null : x(b(a, s));
    return {
      workflowId: x(b(a, "workflowId", "workflow_id"), "—"),
      version: se(b(a, "version")),
      kind: x(b(a, "kind"), "manual"),
      cron: i("cron"),
      eventName: i("eventName"),
      timezone: i("timezone"),
      resourceId: i("resourceId"),
      nextRun: i("nextRun"),
      lastStatus: i("lastStatus"),
      lastStartedAt: i("lastStartedAt"),
      lastFireStatus: i("lastFireStatus"),
      lastFireDueAt: i("lastFireDueAt"),
      lastFireAttempts: b(a, "lastFireAttempts") == null ? null : se(b(a, "lastFireAttempts")),
      skippedFires: b(a, "skippedFires") == null ? null : se(b(a, "skippedFires")),
      backlogTruncated: b(a, "backlogTruncated") == null ? null : b(a, "backlogTruncated") === !0,
      lastError: i("lastError")
    };
  });
}
function Av(e) {
  if (e == null) return null;
  const r = _e(e), t = W(r);
  if (Object.keys(t).length === 0) return null;
  const n = fe(b(t, "files")).map((i) => {
    const s = W(i);
    return {
      path: x(b(s, "path"), "—"),
      sourceDate: x(b(s, "sourceDate"), ""),
      inLedger: yr(b(s, "inLedger")),
      expectedTargets: se(b(s, "expectedTargets")),
      succeededTargets: se(b(s, "succeededTargets")),
      incompleteTargets: se(b(s, "incompleteTargets")),
      missingTargets: x(b(s, "missingTargets"), ""),
      wouldSkip: yr(b(s, "wouldSkip")),
      wouldCollect: yr(b(s, "wouldCollect")),
      duplicateRisk: yr(b(s, "duplicateRisk"))
    };
  }), a = W(b(t, "summary"));
  return {
    resourceId: x(b(t, "resourceId"), "—"),
    from: x(b(t, "from"), ""),
    to: x(b(t, "to"), ""),
    files: n,
    summary: {
      discovered: se(b(a, "discovered")),
      alreadyComplete: se(b(a, "alreadyComplete")),
      needsCollection: se(b(a, "needsCollection")),
      duplicateRisk: se(b(a, "duplicateRisk"))
    }
  };
}
function Ov(e) {
  const r = _e(e);
  return (Array.isArray(r) ? r : fe(b(W(r), "files", "items"))).map((n) => {
    const a = W(n);
    return {
      id: x(b(a, "id"), "—"),
      path: x(b(a, "path"), "—"),
      identityKey: x(b(a, "identityKey"), ""),
      sourceResourceId: x(b(a, "sourceResourceId"), ""),
      length: se(b(a, "length")),
      sourceDate: x(b(a, "sourceDate"), ""),
      collectedAt: x(b(a, "collectedAt"), ""),
      status: x(b(a, "status"), "—"),
      deliveries: fe(b(a, "deliveries")).map((i) => {
        const s = W(i);
        return {
          id: x(b(s, "id"), "—"),
          collectionFileId: x(b(s, "collectionFileId"), ""),
          targetId: x(b(s, "targetId"), "—"),
          targetKind: x(b(s, "targetKind"), "record"),
          status: x(b(s, "status"), "—"),
          attempts: se(b(s, "attempts")),
          records: se(b(s, "records")),
          error: b(s, "error") == null ? null : x(b(s, "error")),
          accepted: se(b(s, "accepted")),
          rejected: se(b(s, "rejected")),
          warnings: se(b(s, "warnings")),
          diagnosis: Sh(b(s, "diagnosis"))
        };
      })
    };
  });
}
function Pv(e) {
  const r = _e(e), t = W(r), n = fe(b(t, "runs")).map((i) => {
    const s = W(i);
    return {
      id: x(b(s, "id"), "—"),
      workflowId: x(b(s, "workflowId", "workflow_id"), "—"),
      status: x(b(s, "status"), "—"),
      startedAt: x(b(s, "startedAt", "started_at"), ""),
      finishedAt: b(s, "finishedAt", "finished_at") == null ? null : x(b(s, "finishedAt", "finished_at")),
      error: b(s, "error") == null ? null : x(b(s, "error"))
    };
  }), a = fe(b(t, "deliveries")).map((i) => {
    const s = W(i);
    return {
      id: x(b(s, "id"), "—"),
      collectionFileId: x(b(s, "collectionFileId", "collection_file_id"), ""),
      targetId: x(b(s, "targetId", "target_id"), "—"),
      targetKind: x(b(s, "targetKind", "target_kind"), "record"),
      status: x(b(s, "status"), "—"),
      attempts: se(b(s, "attempts")),
      records: se(b(s, "records")),
      error: b(s, "error") == null ? null : x(b(s, "error")),
      diagnosis: Sh(b(s, "diagnosis"))
    };
  });
  return { runs: n, deliveries: a };
}
function Nv(e) {
  if (e == null) return null;
  const r = _e(e), t = W(r);
  if (Object.keys(t).length === 0) return null;
  const n = fe(b(t, "nodes")).slice(0, 64).map((a) => {
    const i = W(a);
    return {
      nodeId: x(b(i, "nodeId", "node_id"), "—"),
      status: x(b(i, "status"), "—"),
      records: se(b(i, "records")),
      files: se(b(i, "files")),
      error: b(i, "error") == null ? null : x(b(i, "error"), void 0),
      samples: fe(b(i, "samples")).slice(0, 3).map((s) => x(s))
    };
  });
  return {
    status: x(b(t, "status"), "—"),
    error: b(t, "error") == null ? null : x(b(t, "error"), void 0),
    executed: fe(b(t, "executed")).map((a) => x(a)),
    instanceId: b(t, "instanceId") == null ? null : x(b(t, "instanceId"), void 0),
    definitionId: b(t, "definitionId") == null ? null : x(b(t, "definitionId"), void 0),
    nodes: n
  };
}
const Dv = [
  { value: "manual", label: "手动运行" },
  { value: "schedule", label: "定时运行" },
  { value: "event", label: "事件触发" }
], Mv = [
  { label: "每天 02:00", cron: "0 2 * * *" },
  { label: "每小时", cron: "0 * * * *" },
  { label: "每 5 分钟", cron: "*/5 * * * *" },
  { label: "工作日 08:30", cron: "30 8 * * 1-5" }
];
function Lv({
  open: e,
  onClose: r,
  trigger: t,
  resourceIds: n,
  onSave: a,
  ctx: i,
  workflowId: s
}) {
  const [o, u] = q(t), [l, c] = q([]), [d, h] = q(null);
  le(() => {
    u(t);
  }, [t]), le(() => {
    e && i.hubQuery("industrial/trigger-status").then((f) => c(kv(f))).catch((f) => h(f instanceof Error ? f.message : String(f)));
  }, [i, e]);
  const p = l.find((f) => f.workflowId === s);
  return /* @__PURE__ */ v(
    ur,
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
      children: /* @__PURE__ */ T(K, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ T(K, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ v($.Text, { children: "触发方式" }),
          /* @__PURE__ */ v(K, { wrap: !0, children: Dv.map((f) => /* @__PURE__ */ v(
            B,
            {
              type: o.kind === f.value ? "primary" : "default",
              onClick: () => u({ ...o, kind: f.value }),
              children: f.label
            },
            f.value
          )) })
        ] }),
        /* @__PURE__ */ v(K, { wrap: !0, children: /* @__PURE__ */ v(
          ye,
          {
            "aria-label": "运行资源",
            allowClear: !0,
            placeholder: "运行资源",
            value: Fv(o),
            options: n.map((f) => ({ value: f, label: f })),
            onChange: (f) => u({
              ...o,
              config: { ...o.config, resourceId: f ?? "" }
            })
          }
        ) }),
        /* @__PURE__ */ T(K, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ v($.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ T(K, { wrap: !0, children: [
            /* @__PURE__ */ v($.Text, { children: "常用" }),
            Mv.map((f) => /* @__PURE__ */ v(
              B,
              {
                size: "small",
                onClick: () => u({ ...o, cron: f.cron }),
                children: f.label
              },
              f.cron
            ))
          ] }),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "Cron 表达式",
              placeholder: "分 时 日 月 周，例如 0 2 * * *",
              value: o.cron ?? "",
              onChange: (f) => u({ ...o, cron: f.target.value })
            }
          ),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "调度时区",
              placeholder: "时区（默认 UTC，例如 Asia/Shanghai）",
              value: jv(o),
              onChange: (f) => u({
                ...o,
                config: { ...o.config, timezone: f.target.value }
              })
            }
          ),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "事件名称",
              placeholder: "事件名称",
              value: o.eventName ?? "",
              onChange: (f) => u({ ...o, eventName: f.target.value })
            }
          ),
          /* @__PURE__ */ T($.Text, { type: "secondary", children: [
            "下次运行：",
            (p == null ? void 0 : p.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        p && /* @__PURE__ */ v(
          qe,
          {
            rowKey: "workflowId",
            dataSource: [p],
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
        d && /* @__PURE__ */ v(Ce, { type: "error", showIcon: !0, message: d })
      ] })
    }
  );
}
function Fv(e) {
  const r = e.config.resourceId;
  return typeof r == "string" && r.trim() ? r : void 0;
}
function jv(e) {
  const r = e.config.timezone;
  return typeof r == "string" && r.trim() ? r : "UTC";
}
function $v({
  open: e,
  onClose: r,
  ctx: t,
  workflowId: n,
  resourceId: a,
  lookbackDays: i
}) {
  const [s, o] = q(null), [u, l] = q(null), [c, d] = q(!1), h = z(async () => {
    d(!0), l(null);
    try {
      await t.hubCommand("industrial/preview-backfill", {
        workflowId: n,
        resourceId: a,
        lookbackDays: i
      }), o(Av(await t.hubQuery("industrial/backfill-preview")));
    } catch (p) {
      l(p instanceof Error ? p.message : String(p));
    } finally {
      d(!1);
    }
  }, [t, i, a, n]);
  return /* @__PURE__ */ v(
    ur,
    {
      open: e,
      title: "补采预览",
      onCancel: r,
      width: 820,
      footer: [
        /* @__PURE__ */ v(B, { loading: c, onClick: () => void h(), children: "重新扫描" }, "refresh"),
        /* @__PURE__ */ v(B, { type: "primary", onClick: r, children: "关闭" }, "close")
      ],
      children: /* @__PURE__ */ T(K, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ T(K, { children: [
          /* @__PURE__ */ T(B, { type: "primary", loading: c, onClick: () => void h(), children: [
            "扫描最近 ",
            i,
            " 天"
          ] }),
          /* @__PURE__ */ T($.Text, { type: "secondary", children: [
            "资源：",
            a || "未选择"
          ] })
        ] }),
        u && /* @__PURE__ */ v(Ce, { type: "error", showIcon: !0, message: u }),
        s && /* @__PURE__ */ T(Nr, { children: [
          /* @__PURE__ */ T($.Text, { children: [
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
          /* @__PURE__ */ v(
            qe,
            {
              rowKey: "path",
              dataSource: s.files,
              pagination: !1,
              size: "small",
              columns: [
                { title: "文件", dataIndex: "path" },
                { title: "源日期", dataIndex: "sourceDate" },
                { title: "账本", dataIndex: "inLedger", render: (p) => p ? "已登记" : "新文件" },
                { title: "当前目标", dataIndex: "expectedTargets" },
                { title: "成功目标", dataIndex: "succeededTargets" },
                { title: "待补目标", dataIndex: "missingTargets", render: (p) => p || "—" },
                { title: "未完成目标", dataIndex: "incompleteTargets" },
                {
                  title: "预计动作",
                  render: (p, g) => g.wouldSkip ? "跳过" : g.duplicateRisk ? "仅补缺失目标" : "采集"
                }
              ]
            }
          )
        ] })
      ] })
    }
  );
}
var Hr = typeof globalThis < "u" ? globalThis : typeof window < "u" ? window : typeof global < "u" ? global : typeof self < "u" ? self : {};
function Gv(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
function Ao(e) {
  throw new Error('Could not dynamically require "' + e + '". Please configure the dynamicRequireTargets or/and ignoreDynamicRequires option of @rollup/plugin-commonjs appropriately for this require call to work.');
}
var Et, uu;
function zv() {
  if (uu) return Et;
  uu = 1;
  function e() {
    this.__data__ = [], this.size = 0;
  }
  return Et = e, Et;
}
var Ct, cu;
function mr() {
  if (cu) return Ct;
  cu = 1;
  function e(r, t) {
    return r === t || r !== r && t !== t;
  }
  return Ct = e, Ct;
}
var St, lu;
function at() {
  if (lu) return St;
  lu = 1;
  var e = mr();
  function r(t, n) {
    for (var a = t.length; a--; )
      if (e(t[a][0], n))
        return a;
    return -1;
  }
  return St = r, St;
}
var Tt, du;
function Bv() {
  if (du) return Tt;
  du = 1;
  var e = at(), r = Array.prototype, t = r.splice;
  function n(a) {
    var i = this.__data__, s = e(i, a);
    if (s < 0)
      return !1;
    var o = i.length - 1;
    return s == o ? i.pop() : t.call(i, s, 1), --this.size, !0;
  }
  return Tt = n, Tt;
}
var Rt, fu;
function Uv() {
  if (fu) return Rt;
  fu = 1;
  var e = at();
  function r(t) {
    var n = this.__data__, a = e(n, t);
    return a < 0 ? void 0 : n[a][1];
  }
  return Rt = r, Rt;
}
var xt, hu;
function Kv() {
  if (hu) return xt;
  hu = 1;
  var e = at();
  function r(t) {
    return e(this.__data__, t) > -1;
  }
  return xt = r, xt;
}
var kt, pu;
function Hv() {
  if (pu) return kt;
  pu = 1;
  var e = at();
  function r(t, n) {
    var a = this.__data__, i = e(a, t);
    return i < 0 ? (++this.size, a.push([t, n])) : a[i][1] = n, this;
  }
  return kt = r, kt;
}
var At, vu;
function it() {
  if (vu) return At;
  vu = 1;
  var e = zv(), r = Bv(), t = Uv(), n = Kv(), a = Hv();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var l = s[o];
      this.set(l[0], l[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, At = i, At;
}
var Ot, gu;
function Vv() {
  if (gu) return Ot;
  gu = 1;
  var e = it();
  function r() {
    this.__data__ = new e(), this.size = 0;
  }
  return Ot = r, Ot;
}
var Pt, yu;
function Wv() {
  if (yu) return Pt;
  yu = 1;
  function e(r) {
    var t = this.__data__, n = t.delete(r);
    return this.size = t.size, n;
  }
  return Pt = e, Pt;
}
var Nt, mu;
function Yv() {
  if (mu) return Nt;
  mu = 1;
  function e(r) {
    return this.__data__.get(r);
  }
  return Nt = e, Nt;
}
var Dt, bu;
function Qv() {
  if (bu) return Dt;
  bu = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return Dt = e, Dt;
}
var Mt, _u;
function Th() {
  if (_u) return Mt;
  _u = 1;
  var e = typeof Hr == "object" && Hr && Hr.Object === Object && Hr;
  return Mt = e, Mt;
}
var Lt, wu;
function Ne() {
  if (wu) return Lt;
  wu = 1;
  var e = Th(), r = typeof self == "object" && self && self.Object === Object && self, t = e || r || Function("return this")();
  return Lt = t, Lt;
}
var Ft, Iu;
function br() {
  if (Iu) return Ft;
  Iu = 1;
  var e = Ne(), r = e.Symbol;
  return Ft = r, Ft;
}
var jt, qu;
function Jv() {
  if (qu) return jt;
  qu = 1;
  var e = br(), r = Object.prototype, t = r.hasOwnProperty, n = r.toString, a = e ? e.toStringTag : void 0;
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
  return jt = i, jt;
}
var $t, Eu;
function Xv() {
  if (Eu) return $t;
  Eu = 1;
  var e = Object.prototype, r = e.toString;
  function t(n) {
    return r.call(n);
  }
  return $t = t, $t;
}
var Gt, Cu;
function cr() {
  if (Cu) return Gt;
  Cu = 1;
  var e = br(), r = Jv(), t = Xv(), n = "[object Null]", a = "[object Undefined]", i = e ? e.toStringTag : void 0;
  function s(o) {
    return o == null ? o === void 0 ? a : n : i && i in Object(o) ? r(o) : t(o);
  }
  return Gt = s, Gt;
}
var zt, Su;
function Oe() {
  if (Su) return zt;
  Su = 1;
  function e(r) {
    var t = typeof r;
    return r != null && (t == "object" || t == "function");
  }
  return zt = e, zt;
}
var Bt, Tu;
function Lr() {
  if (Tu) return Bt;
  Tu = 1;
  var e = cr(), r = Oe(), t = "[object AsyncFunction]", n = "[object Function]", a = "[object GeneratorFunction]", i = "[object Proxy]";
  function s(o) {
    if (!r(o))
      return !1;
    var u = e(o);
    return u == n || u == a || u == t || u == i;
  }
  return Bt = s, Bt;
}
var Ut, Ru;
function Zv() {
  if (Ru) return Ut;
  Ru = 1;
  var e = Ne(), r = e["__core-js_shared__"];
  return Ut = r, Ut;
}
var Kt, xu;
function eg() {
  if (xu) return Kt;
  xu = 1;
  var e = Zv(), r = function() {
    var n = /[^.]+$/.exec(e && e.keys && e.keys.IE_PROTO || "");
    return n ? "Symbol(src)_1." + n : "";
  }();
  function t(n) {
    return !!r && r in n;
  }
  return Kt = t, Kt;
}
var Ht, ku;
function Rh() {
  if (ku) return Ht;
  ku = 1;
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
  return Ht = t, Ht;
}
var Vt, Au;
function rg() {
  if (Au) return Vt;
  Au = 1;
  var e = Lr(), r = eg(), t = Oe(), n = Rh(), a = /[\\^$.*+?()[\]{}|]/g, i = /^\[object .+?Constructor\]$/, s = Function.prototype, o = Object.prototype, u = s.toString, l = o.hasOwnProperty, c = RegExp(
    "^" + u.call(l).replace(a, "\\$&").replace(/hasOwnProperty|(function).*?(?=\\\()| for .+?(?=\\\])/g, "$1.*?") + "$"
  );
  function d(h) {
    if (!t(h) || r(h))
      return !1;
    var p = e(h) ? c : i;
    return p.test(n(h));
  }
  return Vt = d, Vt;
}
var Wt, Ou;
function tg() {
  if (Ou) return Wt;
  Ou = 1;
  function e(r, t) {
    return r == null ? void 0 : r[t];
  }
  return Wt = e, Wt;
}
var Yt, Pu;
function lr() {
  if (Pu) return Yt;
  Pu = 1;
  var e = rg(), r = tg();
  function t(n, a) {
    var i = r(n, a);
    return e(i) ? i : void 0;
  }
  return Yt = t, Yt;
}
var Qt, Nu;
function Oo() {
  if (Nu) return Qt;
  Nu = 1;
  var e = lr(), r = Ne(), t = e(r, "Map");
  return Qt = t, Qt;
}
var Jt, Du;
function st() {
  if (Du) return Jt;
  Du = 1;
  var e = lr(), r = e(Object, "create");
  return Jt = r, Jt;
}
var Xt, Mu;
function ng() {
  if (Mu) return Xt;
  Mu = 1;
  var e = st();
  function r() {
    this.__data__ = e ? e(null) : {}, this.size = 0;
  }
  return Xt = r, Xt;
}
var Zt, Lu;
function ag() {
  if (Lu) return Zt;
  Lu = 1;
  function e(r) {
    var t = this.has(r) && delete this.__data__[r];
    return this.size -= t ? 1 : 0, t;
  }
  return Zt = e, Zt;
}
var en, Fu;
function ig() {
  if (Fu) return en;
  Fu = 1;
  var e = st(), r = "__lodash_hash_undefined__", t = Object.prototype, n = t.hasOwnProperty;
  function a(i) {
    var s = this.__data__;
    if (e) {
      var o = s[i];
      return o === r ? void 0 : o;
    }
    return n.call(s, i) ? s[i] : void 0;
  }
  return en = a, en;
}
var rn, ju;
function sg() {
  if (ju) return rn;
  ju = 1;
  var e = st(), r = Object.prototype, t = r.hasOwnProperty;
  function n(a) {
    var i = this.__data__;
    return e ? i[a] !== void 0 : t.call(i, a);
  }
  return rn = n, rn;
}
var tn, $u;
function og() {
  if ($u) return tn;
  $u = 1;
  var e = st(), r = "__lodash_hash_undefined__";
  function t(n, a) {
    var i = this.__data__;
    return this.size += this.has(n) ? 0 : 1, i[n] = e && a === void 0 ? r : a, this;
  }
  return tn = t, tn;
}
var nn, Gu;
function ug() {
  if (Gu) return nn;
  Gu = 1;
  var e = ng(), r = ag(), t = ig(), n = sg(), a = og();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var l = s[o];
      this.set(l[0], l[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, nn = i, nn;
}
var an, zu;
function cg() {
  if (zu) return an;
  zu = 1;
  var e = ug(), r = it(), t = Oo();
  function n() {
    this.size = 0, this.__data__ = {
      hash: new e(),
      map: new (t || r)(),
      string: new e()
    };
  }
  return an = n, an;
}
var sn, Bu;
function lg() {
  if (Bu) return sn;
  Bu = 1;
  function e(r) {
    var t = typeof r;
    return t == "string" || t == "number" || t == "symbol" || t == "boolean" ? r !== "__proto__" : r === null;
  }
  return sn = e, sn;
}
var on, Uu;
function ot() {
  if (Uu) return on;
  Uu = 1;
  var e = lg();
  function r(t, n) {
    var a = t.__data__;
    return e(n) ? a[typeof n == "string" ? "string" : "hash"] : a.map;
  }
  return on = r, on;
}
var un, Ku;
function dg() {
  if (Ku) return un;
  Ku = 1;
  var e = ot();
  function r(t) {
    var n = e(this, t).delete(t);
    return this.size -= n ? 1 : 0, n;
  }
  return un = r, un;
}
var cn, Hu;
function fg() {
  if (Hu) return cn;
  Hu = 1;
  var e = ot();
  function r(t) {
    return e(this, t).get(t);
  }
  return cn = r, cn;
}
var ln, Vu;
function hg() {
  if (Vu) return ln;
  Vu = 1;
  var e = ot();
  function r(t) {
    return e(this, t).has(t);
  }
  return ln = r, ln;
}
var dn, Wu;
function pg() {
  if (Wu) return dn;
  Wu = 1;
  var e = ot();
  function r(t, n) {
    var a = e(this, t), i = a.size;
    return a.set(t, n), this.size += a.size == i ? 0 : 1, this;
  }
  return dn = r, dn;
}
var fn, Yu;
function Po() {
  if (Yu) return fn;
  Yu = 1;
  var e = cg(), r = dg(), t = fg(), n = hg(), a = pg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var l = s[o];
      this.set(l[0], l[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, fn = i, fn;
}
var hn, Qu;
function vg() {
  if (Qu) return hn;
  Qu = 1;
  var e = it(), r = Oo(), t = Po(), n = 200;
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
  return hn = a, hn;
}
var pn, Ju;
function ut() {
  if (Ju) return pn;
  Ju = 1;
  var e = it(), r = Vv(), t = Wv(), n = Yv(), a = Qv(), i = vg();
  function s(o) {
    var u = this.__data__ = new e(o);
    this.size = u.size;
  }
  return s.prototype.clear = r, s.prototype.delete = t, s.prototype.get = n, s.prototype.has = a, s.prototype.set = i, pn = s, pn;
}
var vn, Xu;
function No() {
  if (Xu) return vn;
  Xu = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a && t(r[n], n, r) !== !1; )
      ;
    return r;
  }
  return vn = e, vn;
}
var gn, Zu;
function xh() {
  if (Zu) return gn;
  Zu = 1;
  var e = lr(), r = function() {
    try {
      var t = e(Object, "defineProperty");
      return t({}, "", {}), t;
    } catch {
    }
  }();
  return gn = r, gn;
}
var yn, ec;
function ct() {
  if (ec) return yn;
  ec = 1;
  var e = xh();
  function r(t, n, a) {
    n == "__proto__" && e ? e(t, n, {
      configurable: !0,
      enumerable: !0,
      value: a,
      writable: !0
    }) : t[n] = a;
  }
  return yn = r, yn;
}
var mn, rc;
function lt() {
  if (rc) return mn;
  rc = 1;
  var e = ct(), r = mr(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o) {
    var u = i[s];
    (!(n.call(i, s) && r(u, o)) || o === void 0 && !(s in i)) && e(i, s, o);
  }
  return mn = a, mn;
}
var bn, tc;
function Fr() {
  if (tc) return bn;
  tc = 1;
  var e = lt(), r = ct();
  function t(n, a, i, s) {
    var o = !i;
    i || (i = {});
    for (var u = -1, l = a.length; ++u < l; ) {
      var c = a[u], d = s ? s(i[c], n[c], c, i, n) : void 0;
      d === void 0 && (d = n[c]), o ? r(i, c, d) : e(i, c, d);
    }
    return i;
  }
  return bn = t, bn;
}
var _n, nc;
function gg() {
  if (nc) return _n;
  nc = 1;
  function e(r, t) {
    for (var n = -1, a = Array(r); ++n < r; )
      a[n] = t(n);
    return a;
  }
  return _n = e, _n;
}
var wn, ac;
function $e() {
  if (ac) return wn;
  ac = 1;
  function e(r) {
    return r != null && typeof r == "object";
  }
  return wn = e, wn;
}
var In, ic;
function yg() {
  if (ic) return In;
  ic = 1;
  var e = cr(), r = $e(), t = "[object Arguments]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return In = n, In;
}
var qn, sc;
function jr() {
  if (sc) return qn;
  sc = 1;
  var e = yg(), r = $e(), t = Object.prototype, n = t.hasOwnProperty, a = t.propertyIsEnumerable, i = e(/* @__PURE__ */ function() {
    return arguments;
  }()) ? e : function(s) {
    return r(s) && n.call(s, "callee") && !a.call(s, "callee");
  };
  return qn = i, qn;
}
var En, oc;
function he() {
  if (oc) return En;
  oc = 1;
  var e = Array.isArray;
  return En = e, En;
}
var Rr = { exports: {} }, Cn, uc;
function mg() {
  if (uc) return Cn;
  uc = 1;
  function e() {
    return !1;
  }
  return Cn = e, Cn;
}
Rr.exports;
var cc;
function _r() {
  return cc || (cc = 1, function(e, r) {
    var t = Ne(), n = mg(), a = r && !r.nodeType && r, i = a && !0 && e && !e.nodeType && e, s = i && i.exports === a, o = s ? t.Buffer : void 0, u = o ? o.isBuffer : void 0, l = u || n;
    e.exports = l;
  }(Rr, Rr.exports)), Rr.exports;
}
var Sn, lc;
function dt() {
  if (lc) return Sn;
  lc = 1;
  var e = 9007199254740991, r = /^(?:0|[1-9]\d*)$/;
  function t(n, a) {
    var i = typeof n;
    return a = a ?? e, !!a && (i == "number" || i != "symbol" && r.test(n)) && n > -1 && n % 1 == 0 && n < a;
  }
  return Sn = t, Sn;
}
var Tn, dc;
function Do() {
  if (dc) return Tn;
  dc = 1;
  var e = 9007199254740991;
  function r(t) {
    return typeof t == "number" && t > -1 && t % 1 == 0 && t <= e;
  }
  return Tn = r, Tn;
}
var Rn, fc;
function bg() {
  if (fc) return Rn;
  fc = 1;
  var e = cr(), r = Do(), t = $e(), n = "[object Arguments]", a = "[object Array]", i = "[object Boolean]", s = "[object Date]", o = "[object Error]", u = "[object Function]", l = "[object Map]", c = "[object Number]", d = "[object Object]", h = "[object RegExp]", p = "[object Set]", g = "[object String]", f = "[object WeakMap]", y = "[object ArrayBuffer]", I = "[object DataView]", S = "[object Float32Array]", _ = "[object Float64Array]", A = "[object Int8Array]", k = "[object Int16Array]", m = "[object Int32Array]", C = "[object Uint8Array]", O = "[object Uint8ClampedArray]", L = "[object Uint16Array]", N = "[object Uint32Array]", w = {};
  w[S] = w[_] = w[A] = w[k] = w[m] = w[C] = w[O] = w[L] = w[N] = !0, w[n] = w[a] = w[y] = w[i] = w[I] = w[s] = w[o] = w[u] = w[l] = w[c] = w[d] = w[h] = w[p] = w[g] = w[f] = !1;
  function F(j) {
    return t(j) && r(j.length) && !!w[e(j)];
  }
  return Rn = F, Rn;
}
var xn, hc;
function ft() {
  if (hc) return xn;
  hc = 1;
  function e(r) {
    return function(t) {
      return r(t);
    };
  }
  return xn = e, xn;
}
var xr = { exports: {} };
xr.exports;
var pc;
function Mo() {
  return pc || (pc = 1, function(e, r) {
    var t = Th(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i && t.process, o = function() {
      try {
        var u = a && a.require && a.require("util").types;
        return u || s && s.binding && s.binding("util");
      } catch {
      }
    }();
    e.exports = o;
  }(xr, xr.exports)), xr.exports;
}
var kn, vc;
function $r() {
  if (vc) return kn;
  vc = 1;
  var e = bg(), r = ft(), t = Mo(), n = t && t.isTypedArray, a = n ? r(n) : e;
  return kn = a, kn;
}
var An, gc;
function kh() {
  if (gc) return An;
  gc = 1;
  var e = gg(), r = jr(), t = he(), n = _r(), a = dt(), i = $r(), s = Object.prototype, o = s.hasOwnProperty;
  function u(l, c) {
    var d = t(l), h = !d && r(l), p = !d && !h && n(l), g = !d && !h && !p && i(l), f = d || h || p || g, y = f ? e(l.length, String) : [], I = y.length;
    for (var S in l)
      (c || o.call(l, S)) && !(f && // Safari 9 has enumerable `arguments.length` in strict mode.
      (S == "length" || // Node.js 0.10 has enumerable non-index properties on buffers.
      p && (S == "offset" || S == "parent") || // PhantomJS 2 has enumerable non-index properties on typed arrays.
      g && (S == "buffer" || S == "byteLength" || S == "byteOffset") || // Skip index properties.
      a(S, I))) && y.push(S);
    return y;
  }
  return An = u, An;
}
var On, yc;
function ht() {
  if (yc) return On;
  yc = 1;
  var e = Object.prototype;
  function r(t) {
    var n = t && t.constructor, a = typeof n == "function" && n.prototype || e;
    return t === a;
  }
  return On = r, On;
}
var Pn, mc;
function Ah() {
  if (mc) return Pn;
  mc = 1;
  function e(r, t) {
    return function(n) {
      return r(t(n));
    };
  }
  return Pn = e, Pn;
}
var Nn, bc;
function _g() {
  if (bc) return Nn;
  bc = 1;
  var e = Ah(), r = e(Object.keys, Object);
  return Nn = r, Nn;
}
var Dn, _c;
function Lo() {
  if (_c) return Dn;
  _c = 1;
  var e = ht(), r = _g(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i) {
    if (!e(i))
      return r(i);
    var s = [];
    for (var o in Object(i))
      n.call(i, o) && o != "constructor" && s.push(o);
    return s;
  }
  return Dn = a, Dn;
}
var Mn, wc;
function He() {
  if (wc) return Mn;
  wc = 1;
  var e = Lr(), r = Do();
  function t(n) {
    return n != null && r(n.length) && !e(n);
  }
  return Mn = t, Mn;
}
var Ln, Ic;
function ir() {
  if (Ic) return Ln;
  Ic = 1;
  var e = kh(), r = Lo(), t = He();
  function n(a) {
    return t(a) ? e(a) : r(a);
  }
  return Ln = n, Ln;
}
var Fn, qc;
function wg() {
  if (qc) return Fn;
  qc = 1;
  var e = Fr(), r = ir();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return Fn = t, Fn;
}
var jn, Ec;
function Ig() {
  if (Ec) return jn;
  Ec = 1;
  function e(r) {
    var t = [];
    if (r != null)
      for (var n in Object(r))
        t.push(n);
    return t;
  }
  return jn = e, jn;
}
var $n, Cc;
function qg() {
  if (Cc) return $n;
  Cc = 1;
  var e = Oe(), r = ht(), t = Ig(), n = Object.prototype, a = n.hasOwnProperty;
  function i(s) {
    if (!e(s))
      return t(s);
    var o = r(s), u = [];
    for (var l in s)
      l == "constructor" && (o || !a.call(s, l)) || u.push(l);
    return u;
  }
  return $n = i, $n;
}
var Gn, Sc;
function dr() {
  if (Sc) return Gn;
  Sc = 1;
  var e = kh(), r = qg(), t = He();
  function n(a) {
    return t(a) ? e(a, !0) : r(a);
  }
  return Gn = n, Gn;
}
var zn, Tc;
function Eg() {
  if (Tc) return zn;
  Tc = 1;
  var e = Fr(), r = dr();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return zn = t, zn;
}
var kr = { exports: {} };
kr.exports;
var Rc;
function Oh() {
  return Rc || (Rc = 1, function(e, r) {
    var t = Ne(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i ? t.Buffer : void 0, o = s ? s.allocUnsafe : void 0;
    function u(l, c) {
      if (c)
        return l.slice();
      var d = l.length, h = o ? o(d) : new l.constructor(d);
      return l.copy(h), h;
    }
    e.exports = u;
  }(kr, kr.exports)), kr.exports;
}
var Bn, xc;
function Ph() {
  if (xc) return Bn;
  xc = 1;
  function e(r, t) {
    var n = -1, a = r.length;
    for (t || (t = Array(a)); ++n < a; )
      t[n] = r[n];
    return t;
  }
  return Bn = e, Bn;
}
var Un, kc;
function Nh() {
  if (kc) return Un;
  kc = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = 0, s = []; ++n < a; ) {
      var o = r[n];
      t(o, n, r) && (s[i++] = o);
    }
    return s;
  }
  return Un = e, Un;
}
var Kn, Ac;
function Dh() {
  if (Ac) return Kn;
  Ac = 1;
  function e() {
    return [];
  }
  return Kn = e, Kn;
}
var Hn, Oc;
function Fo() {
  if (Oc) return Hn;
  Oc = 1;
  var e = Nh(), r = Dh(), t = Object.prototype, n = t.propertyIsEnumerable, a = Object.getOwnPropertySymbols, i = a ? function(s) {
    return s == null ? [] : (s = Object(s), e(a(s), function(o) {
      return n.call(s, o);
    }));
  } : r;
  return Hn = i, Hn;
}
var Vn, Pc;
function Cg() {
  if (Pc) return Vn;
  Pc = 1;
  var e = Fr(), r = Fo();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return Vn = t, Vn;
}
var Wn, Nc;
function jo() {
  if (Nc) return Wn;
  Nc = 1;
  function e(r, t) {
    for (var n = -1, a = t.length, i = r.length; ++n < a; )
      r[i + n] = t[n];
    return r;
  }
  return Wn = e, Wn;
}
var Yn, Dc;
function pt() {
  if (Dc) return Yn;
  Dc = 1;
  var e = Ah(), r = e(Object.getPrototypeOf, Object);
  return Yn = r, Yn;
}
var Qn, Mc;
function Mh() {
  if (Mc) return Qn;
  Mc = 1;
  var e = jo(), r = pt(), t = Fo(), n = Dh(), a = Object.getOwnPropertySymbols, i = a ? function(s) {
    for (var o = []; s; )
      e(o, t(s)), s = r(s);
    return o;
  } : n;
  return Qn = i, Qn;
}
var Jn, Lc;
function Sg() {
  if (Lc) return Jn;
  Lc = 1;
  var e = Fr(), r = Mh();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return Jn = t, Jn;
}
var Xn, Fc;
function Lh() {
  if (Fc) return Xn;
  Fc = 1;
  var e = jo(), r = he();
  function t(n, a, i) {
    var s = a(n);
    return r(n) ? s : e(s, i(n));
  }
  return Xn = t, Xn;
}
var Zn, jc;
function Fh() {
  if (jc) return Zn;
  jc = 1;
  var e = Lh(), r = Fo(), t = ir();
  function n(a) {
    return e(a, t, r);
  }
  return Zn = n, Zn;
}
var ea, $c;
function Tg() {
  if ($c) return ea;
  $c = 1;
  var e = Lh(), r = Mh(), t = dr();
  function n(a) {
    return e(a, t, r);
  }
  return ea = n, ea;
}
var ra, Gc;
function Rg() {
  if (Gc) return ra;
  Gc = 1;
  var e = lr(), r = Ne(), t = e(r, "DataView");
  return ra = t, ra;
}
var ta, zc;
function xg() {
  if (zc) return ta;
  zc = 1;
  var e = lr(), r = Ne(), t = e(r, "Promise");
  return ta = t, ta;
}
var na, Bc;
function jh() {
  if (Bc) return na;
  Bc = 1;
  var e = lr(), r = Ne(), t = e(r, "Set");
  return na = t, na;
}
var aa, Uc;
function kg() {
  if (Uc) return aa;
  Uc = 1;
  var e = lr(), r = Ne(), t = e(r, "WeakMap");
  return aa = t, aa;
}
var ia, Kc;
function wr() {
  if (Kc) return ia;
  Kc = 1;
  var e = Rg(), r = Oo(), t = xg(), n = jh(), a = kg(), i = cr(), s = Rh(), o = "[object Map]", u = "[object Object]", l = "[object Promise]", c = "[object Set]", d = "[object WeakMap]", h = "[object DataView]", p = s(e), g = s(r), f = s(t), y = s(n), I = s(a), S = i;
  return (e && S(new e(new ArrayBuffer(1))) != h || r && S(new r()) != o || t && S(t.resolve()) != l || n && S(new n()) != c || a && S(new a()) != d) && (S = function(_) {
    var A = i(_), k = A == u ? _.constructor : void 0, m = k ? s(k) : "";
    if (m)
      switch (m) {
        case p:
          return h;
        case g:
          return o;
        case f:
          return l;
        case y:
          return c;
        case I:
          return d;
      }
    return A;
  }), ia = S, ia;
}
var sa, Hc;
function Ag() {
  if (Hc) return sa;
  Hc = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n) {
    var a = n.length, i = new n.constructor(a);
    return a && typeof n[0] == "string" && r.call(n, "index") && (i.index = n.index, i.input = n.input), i;
  }
  return sa = t, sa;
}
var oa, Vc;
function $h() {
  if (Vc) return oa;
  Vc = 1;
  var e = Ne(), r = e.Uint8Array;
  return oa = r, oa;
}
var ua, Wc;
function $o() {
  if (Wc) return ua;
  Wc = 1;
  var e = $h();
  function r(t) {
    var n = new t.constructor(t.byteLength);
    return new e(n).set(new e(t)), n;
  }
  return ua = r, ua;
}
var ca, Yc;
function Og() {
  if (Yc) return ca;
  Yc = 1;
  var e = $o();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.byteLength);
  }
  return ca = r, ca;
}
var la, Qc;
function Pg() {
  if (Qc) return la;
  Qc = 1;
  var e = /\w*$/;
  function r(t) {
    var n = new t.constructor(t.source, e.exec(t));
    return n.lastIndex = t.lastIndex, n;
  }
  return la = r, la;
}
var da, Jc;
function Ng() {
  if (Jc) return da;
  Jc = 1;
  var e = br(), r = e ? e.prototype : void 0, t = r ? r.valueOf : void 0;
  function n(a) {
    return t ? Object(t.call(a)) : {};
  }
  return da = n, da;
}
var fa, Xc;
function Gh() {
  if (Xc) return fa;
  Xc = 1;
  var e = $o();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.length);
  }
  return fa = r, fa;
}
var ha, Zc;
function Dg() {
  if (Zc) return ha;
  Zc = 1;
  var e = $o(), r = Og(), t = Pg(), n = Ng(), a = Gh(), i = "[object Boolean]", s = "[object Date]", o = "[object Map]", u = "[object Number]", l = "[object RegExp]", c = "[object Set]", d = "[object String]", h = "[object Symbol]", p = "[object ArrayBuffer]", g = "[object DataView]", f = "[object Float32Array]", y = "[object Float64Array]", I = "[object Int8Array]", S = "[object Int16Array]", _ = "[object Int32Array]", A = "[object Uint8Array]", k = "[object Uint8ClampedArray]", m = "[object Uint16Array]", C = "[object Uint32Array]";
  function O(L, N, w) {
    var F = L.constructor;
    switch (N) {
      case p:
        return e(L);
      case i:
      case s:
        return new F(+L);
      case g:
        return r(L, w);
      case f:
      case y:
      case I:
      case S:
      case _:
      case A:
      case k:
      case m:
      case C:
        return a(L, w);
      case o:
        return new F();
      case u:
      case d:
        return new F(L);
      case l:
        return t(L);
      case c:
        return new F();
      case h:
        return n(L);
    }
  }
  return ha = O, ha;
}
var pa, el;
function zh() {
  if (el) return pa;
  el = 1;
  var e = Oe(), r = Object.create, t = /* @__PURE__ */ function() {
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
  return pa = t, pa;
}
var va, rl;
function Bh() {
  if (rl) return va;
  rl = 1;
  var e = zh(), r = pt(), t = ht();
  function n(a) {
    return typeof a.constructor == "function" && !t(a) ? e(r(a)) : {};
  }
  return va = n, va;
}
var ga, tl;
function Mg() {
  if (tl) return ga;
  tl = 1;
  var e = wr(), r = $e(), t = "[object Map]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return ga = n, ga;
}
var ya, nl;
function Lg() {
  if (nl) return ya;
  nl = 1;
  var e = Mg(), r = ft(), t = Mo(), n = t && t.isMap, a = n ? r(n) : e;
  return ya = a, ya;
}
var ma, al;
function Fg() {
  if (al) return ma;
  al = 1;
  var e = wr(), r = $e(), t = "[object Set]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return ma = n, ma;
}
var ba, il;
function jg() {
  if (il) return ba;
  il = 1;
  var e = Fg(), r = ft(), t = Mo(), n = t && t.isSet, a = n ? r(n) : e;
  return ba = a, ba;
}
var _a, sl;
function Uh() {
  if (sl) return _a;
  sl = 1;
  var e = ut(), r = No(), t = lt(), n = wg(), a = Eg(), i = Oh(), s = Ph(), o = Cg(), u = Sg(), l = Fh(), c = Tg(), d = wr(), h = Ag(), p = Dg(), g = Bh(), f = he(), y = _r(), I = Lg(), S = Oe(), _ = jg(), A = ir(), k = dr(), m = 1, C = 2, O = 4, L = "[object Arguments]", N = "[object Array]", w = "[object Boolean]", F = "[object Date]", j = "[object Error]", Y = "[object Function]", X = "[object GeneratorFunction]", ae = "[object Map]", P = "[object Number]", G = "[object Object]", Z = "[object RegExp]", de = "[object Set]", D = "[object String]", R = "[object Symbol]", ue = "[object WeakMap]", qr = "[object ArrayBuffer]", Ae = "[object DataView]", Me = "[object Float32Array]", sr = "[object Float64Array]", pr = "[object Int8Array]", vr = "[object Int16Array]", zr = "[object Int32Array]", Er = "[object Uint8Array]", we = "[object Uint8ClampedArray]", Cr = "[object Uint16Array]", We = "[object Uint32Array]", ne = {};
  ne[L] = ne[N] = ne[qr] = ne[Ae] = ne[w] = ne[F] = ne[Me] = ne[sr] = ne[pr] = ne[vr] = ne[zr] = ne[ae] = ne[P] = ne[G] = ne[Z] = ne[de] = ne[D] = ne[R] = ne[Er] = ne[we] = ne[Cr] = ne[We] = !0, ne[j] = ne[Y] = ne[ue] = !1;
  function Ge(Q, Ye, Qe, pe, Je, Te) {
    var me, Xe = Ye & m, gr = Ye & C, Br = Ye & O;
    if (Qe && (me = Je ? Qe(Q, pe, Je, Te) : Qe(Q)), me !== void 0)
      return me;
    if (!S(Q))
      return Q;
    var Ur = f(Q);
    if (Ur) {
      if (me = h(Q), !Xe)
        return s(Q, me);
    } else {
      var Ze = d(Q), E = Ze == Y || Ze == X;
      if (y(Q))
        return i(Q, Xe);
      if (Ze == G || Ze == L || E && !Je) {
        if (me = gr || E ? {} : g(Q), !Xe)
          return gr ? u(Q, a(me, Q)) : o(Q, n(me, Q));
      } else {
        if (!ne[Ze])
          return Je ? Q : {};
        me = p(Q, Ze, Xe);
      }
    }
    Te || (Te = new e());
    var M = Te.get(Q);
    if (M)
      return M;
    Te.set(Q, me), _(Q) ? Q.forEach(function(re) {
      me.add(Ge(re, Ye, Qe, re, Q, Te));
    }) : I(Q) && Q.forEach(function(re, te) {
      me.set(te, Ge(re, Ye, Qe, te, Q, Te));
    });
    var ce = Br ? gr ? c : l : gr ? k : A, ee = Ur ? void 0 : ce(Q);
    return r(ee || Q, function(re, te) {
      ee && (te = re, re = Q[te]), t(me, te, Ge(re, Ye, Qe, te, Q, Te));
    }), me;
  }
  return _a = Ge, _a;
}
var wa, ol;
function $g() {
  if (ol) return wa;
  ol = 1;
  var e = Uh(), r = 4;
  function t(n) {
    return e(n, r);
  }
  return wa = t, wa;
}
var Ia, ul;
function Go() {
  if (ul) return Ia;
  ul = 1;
  function e(r) {
    return function() {
      return r;
    };
  }
  return Ia = e, Ia;
}
var qa, cl;
function Gg() {
  if (cl) return qa;
  cl = 1;
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
  return qa = e, qa;
}
var Ea, ll;
function zo() {
  if (ll) return Ea;
  ll = 1;
  var e = Gg(), r = e();
  return Ea = r, Ea;
}
var Ca, dl;
function Bo() {
  if (dl) return Ca;
  dl = 1;
  var e = zo(), r = ir();
  function t(n, a) {
    return n && e(n, a, r);
  }
  return Ca = t, Ca;
}
var Sa, fl;
function zg() {
  if (fl) return Sa;
  fl = 1;
  var e = He();
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
  return Sa = r, Sa;
}
var Ta, hl;
function vt() {
  if (hl) return Ta;
  hl = 1;
  var e = Bo(), r = zg(), t = r(e);
  return Ta = t, Ta;
}
var Ra, pl;
function fr() {
  if (pl) return Ra;
  pl = 1;
  function e(r) {
    return r;
  }
  return Ra = e, Ra;
}
var xa, vl;
function Kh() {
  if (vl) return xa;
  vl = 1;
  var e = fr();
  function r(t) {
    return typeof t == "function" ? t : e;
  }
  return xa = r, xa;
}
var ka, gl;
function Hh() {
  if (gl) return ka;
  gl = 1;
  var e = No(), r = vt(), t = Kh(), n = he();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s));
  }
  return ka = a, ka;
}
var Aa, yl;
function Vh() {
  return yl || (yl = 1, Aa = Hh()), Aa;
}
var Oa, ml;
function Bg() {
  if (ml) return Oa;
  ml = 1;
  var e = vt();
  function r(t, n) {
    var a = [];
    return e(t, function(i, s, o) {
      n(i, s, o) && a.push(i);
    }), a;
  }
  return Oa = r, Oa;
}
var Pa, bl;
function Ug() {
  if (bl) return Pa;
  bl = 1;
  var e = "__lodash_hash_undefined__";
  function r(t) {
    return this.__data__.set(t, e), this;
  }
  return Pa = r, Pa;
}
var Na, _l;
function Kg() {
  if (_l) return Na;
  _l = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return Na = e, Na;
}
var Da, wl;
function Wh() {
  if (wl) return Da;
  wl = 1;
  var e = Po(), r = Ug(), t = Kg();
  function n(a) {
    var i = -1, s = a == null ? 0 : a.length;
    for (this.__data__ = new e(); ++i < s; )
      this.add(a[i]);
  }
  return n.prototype.add = n.prototype.push = r, n.prototype.has = t, Da = n, Da;
}
var Ma, Il;
function Hg() {
  if (Il) return Ma;
  Il = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a; )
      if (t(r[n], n, r))
        return !0;
    return !1;
  }
  return Ma = e, Ma;
}
var La, ql;
function Yh() {
  if (ql) return La;
  ql = 1;
  function e(r, t) {
    return r.has(t);
  }
  return La = e, La;
}
var Fa, El;
function Qh() {
  if (El) return Fa;
  El = 1;
  var e = Wh(), r = Hg(), t = Yh(), n = 1, a = 2;
  function i(s, o, u, l, c, d) {
    var h = u & n, p = s.length, g = o.length;
    if (p != g && !(h && g > p))
      return !1;
    var f = d.get(s), y = d.get(o);
    if (f && y)
      return f == o && y == s;
    var I = -1, S = !0, _ = u & a ? new e() : void 0;
    for (d.set(s, o), d.set(o, s); ++I < p; ) {
      var A = s[I], k = o[I];
      if (l)
        var m = h ? l(k, A, I, o, s, d) : l(A, k, I, s, o, d);
      if (m !== void 0) {
        if (m)
          continue;
        S = !1;
        break;
      }
      if (_) {
        if (!r(o, function(C, O) {
          if (!t(_, O) && (A === C || c(A, C, u, l, d)))
            return _.push(O);
        })) {
          S = !1;
          break;
        }
      } else if (!(A === k || c(A, k, u, l, d))) {
        S = !1;
        break;
      }
    }
    return d.delete(s), d.delete(o), S;
  }
  return Fa = i, Fa;
}
var ja, Cl;
function Vg() {
  if (Cl) return ja;
  Cl = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a, i) {
      n[++t] = [i, a];
    }), n;
  }
  return ja = e, ja;
}
var $a, Sl;
function Uo() {
  if (Sl) return $a;
  Sl = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a) {
      n[++t] = a;
    }), n;
  }
  return $a = e, $a;
}
var Ga, Tl;
function Wg() {
  if (Tl) return Ga;
  Tl = 1;
  var e = br(), r = $h(), t = mr(), n = Qh(), a = Vg(), i = Uo(), s = 1, o = 2, u = "[object Boolean]", l = "[object Date]", c = "[object Error]", d = "[object Map]", h = "[object Number]", p = "[object RegExp]", g = "[object Set]", f = "[object String]", y = "[object Symbol]", I = "[object ArrayBuffer]", S = "[object DataView]", _ = e ? e.prototype : void 0, A = _ ? _.valueOf : void 0;
  function k(m, C, O, L, N, w, F) {
    switch (O) {
      case S:
        if (m.byteLength != C.byteLength || m.byteOffset != C.byteOffset)
          return !1;
        m = m.buffer, C = C.buffer;
      case I:
        return !(m.byteLength != C.byteLength || !w(new r(m), new r(C)));
      case u:
      case l:
      case h:
        return t(+m, +C);
      case c:
        return m.name == C.name && m.message == C.message;
      case p:
      case f:
        return m == C + "";
      case d:
        var j = a;
      case g:
        var Y = L & s;
        if (j || (j = i), m.size != C.size && !Y)
          return !1;
        var X = F.get(m);
        if (X)
          return X == C;
        L |= o, F.set(m, C);
        var ae = n(j(m), j(C), L, N, w, F);
        return F.delete(m), ae;
      case y:
        if (A)
          return A.call(m) == A.call(C);
    }
    return !1;
  }
  return Ga = k, Ga;
}
var za, Rl;
function Yg() {
  if (Rl) return za;
  Rl = 1;
  var e = Fh(), r = 1, t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o, u, l, c) {
    var d = o & r, h = e(i), p = h.length, g = e(s), f = g.length;
    if (p != f && !d)
      return !1;
    for (var y = p; y--; ) {
      var I = h[y];
      if (!(d ? I in s : n.call(s, I)))
        return !1;
    }
    var S = c.get(i), _ = c.get(s);
    if (S && _)
      return S == s && _ == i;
    var A = !0;
    c.set(i, s), c.set(s, i);
    for (var k = d; ++y < p; ) {
      I = h[y];
      var m = i[I], C = s[I];
      if (u)
        var O = d ? u(C, m, I, s, i, c) : u(m, C, I, i, s, c);
      if (!(O === void 0 ? m === C || l(m, C, o, u, c) : O)) {
        A = !1;
        break;
      }
      k || (k = I == "constructor");
    }
    if (A && !k) {
      var L = i.constructor, N = s.constructor;
      L != N && "constructor" in i && "constructor" in s && !(typeof L == "function" && L instanceof L && typeof N == "function" && N instanceof N) && (A = !1);
    }
    return c.delete(i), c.delete(s), A;
  }
  return za = a, za;
}
var Ba, xl;
function Qg() {
  if (xl) return Ba;
  xl = 1;
  var e = ut(), r = Qh(), t = Wg(), n = Yg(), a = wr(), i = he(), s = _r(), o = $r(), u = 1, l = "[object Arguments]", c = "[object Array]", d = "[object Object]", h = Object.prototype, p = h.hasOwnProperty;
  function g(f, y, I, S, _, A) {
    var k = i(f), m = i(y), C = k ? c : a(f), O = m ? c : a(y);
    C = C == l ? d : C, O = O == l ? d : O;
    var L = C == d, N = O == d, w = C == O;
    if (w && s(f)) {
      if (!s(y))
        return !1;
      k = !0, L = !1;
    }
    if (w && !L)
      return A || (A = new e()), k || o(f) ? r(f, y, I, S, _, A) : t(f, y, C, I, S, _, A);
    if (!(I & u)) {
      var F = L && p.call(f, "__wrapped__"), j = N && p.call(y, "__wrapped__");
      if (F || j) {
        var Y = F ? f.value() : f, X = j ? y.value() : y;
        return A || (A = new e()), _(Y, X, I, S, A);
      }
    }
    return w ? (A || (A = new e()), n(f, y, I, S, _, A)) : !1;
  }
  return Ba = g, Ba;
}
var Ua, kl;
function Jh() {
  if (kl) return Ua;
  kl = 1;
  var e = Qg(), r = $e();
  function t(n, a, i, s, o) {
    return n === a ? !0 : n == null || a == null || !r(n) && !r(a) ? n !== n && a !== a : e(n, a, i, s, t, o);
  }
  return Ua = t, Ua;
}
var Ka, Al;
function Jg() {
  if (Al) return Ka;
  Al = 1;
  var e = ut(), r = Jh(), t = 1, n = 2;
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
      var p = h[0], g = i[p], f = h[1];
      if (d && h[2]) {
        if (g === void 0 && !(p in i))
          return !1;
      } else {
        var y = new e();
        if (u)
          var I = u(g, f, p, i, s, y);
        if (!(I === void 0 ? r(f, g, t | n, u, y) : I))
          return !1;
      }
    }
    return !0;
  }
  return Ka = a, Ka;
}
var Ha, Ol;
function Xh() {
  if (Ol) return Ha;
  Ol = 1;
  var e = Oe();
  function r(t) {
    return t === t && !e(t);
  }
  return Ha = r, Ha;
}
var Va, Pl;
function Xg() {
  if (Pl) return Va;
  Pl = 1;
  var e = Xh(), r = ir();
  function t(n) {
    for (var a = r(n), i = a.length; i--; ) {
      var s = a[i], o = n[s];
      a[i] = [s, o, e(o)];
    }
    return a;
  }
  return Va = t, Va;
}
var Wa, Nl;
function Zh() {
  if (Nl) return Wa;
  Nl = 1;
  function e(r, t) {
    return function(n) {
      return n == null ? !1 : n[r] === t && (t !== void 0 || r in Object(n));
    };
  }
  return Wa = e, Wa;
}
var Ya, Dl;
function Zg() {
  if (Dl) return Ya;
  Dl = 1;
  var e = Jg(), r = Xg(), t = Zh();
  function n(a) {
    var i = r(a);
    return i.length == 1 && i[0][2] ? t(i[0][0], i[0][1]) : function(s) {
      return s === a || e(s, a, i);
    };
  }
  return Ya = n, Ya;
}
var Qa, Ml;
function Ir() {
  if (Ml) return Qa;
  Ml = 1;
  var e = cr(), r = $e(), t = "[object Symbol]";
  function n(a) {
    return typeof a == "symbol" || r(a) && e(a) == t;
  }
  return Qa = n, Qa;
}
var Ja, Ll;
function Ko() {
  if (Ll) return Ja;
  Ll = 1;
  var e = he(), r = Ir(), t = /\.|\[(?:[^[\]]*|(["'])(?:(?!\1)[^\\]|\\.)*?\1)\]/, n = /^\w*$/;
  function a(i, s) {
    if (e(i))
      return !1;
    var o = typeof i;
    return o == "number" || o == "symbol" || o == "boolean" || i == null || r(i) ? !0 : n.test(i) || !t.test(i) || s != null && i in Object(s);
  }
  return Ja = a, Ja;
}
var Xa, Fl;
function ey() {
  if (Fl) return Xa;
  Fl = 1;
  var e = Po(), r = "Expected a function";
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
  return t.Cache = e, Xa = t, Xa;
}
var Za, jl;
function ry() {
  if (jl) return Za;
  jl = 1;
  var e = ey(), r = 500;
  function t(n) {
    var a = e(n, function(s) {
      return i.size === r && i.clear(), s;
    }), i = a.cache;
    return a;
  }
  return Za = t, Za;
}
var ei, $l;
function ty() {
  if ($l) return ei;
  $l = 1;
  var e = ry(), r = /[^.[\]]+|\[(?:(-?\d+(?:\.\d+)?)|(["'])((?:(?!\2)[^\\]|\\.)*?)\2)\]|(?=(?:\.|\[\])(?:\.|\[\]|$))/g, t = /\\(\\)?/g, n = e(function(a) {
    var i = [];
    return a.charCodeAt(0) === 46 && i.push(""), a.replace(r, function(s, o, u, l) {
      i.push(u ? l.replace(t, "$1") : o || s);
    }), i;
  });
  return ei = n, ei;
}
var ri, Gl;
function gt() {
  if (Gl) return ri;
  Gl = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = Array(a); ++n < a; )
      i[n] = t(r[n], n, r);
    return i;
  }
  return ri = e, ri;
}
var ti, zl;
function ny() {
  if (zl) return ti;
  zl = 1;
  var e = br(), r = gt(), t = he(), n = Ir(), a = e ? e.prototype : void 0, i = a ? a.toString : void 0;
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
  return ti = s, ti;
}
var ni, Bl;
function ep() {
  if (Bl) return ni;
  Bl = 1;
  var e = ny();
  function r(t) {
    return t == null ? "" : e(t);
  }
  return ni = r, ni;
}
var ai, Ul;
function yt() {
  if (Ul) return ai;
  Ul = 1;
  var e = he(), r = Ko(), t = ty(), n = ep();
  function a(i, s) {
    return e(i) ? i : r(i, s) ? [i] : t(n(i));
  }
  return ai = a, ai;
}
var ii, Kl;
function Gr() {
  if (Kl) return ii;
  Kl = 1;
  var e = Ir();
  function r(t) {
    if (typeof t == "string" || e(t))
      return t;
    var n = t + "";
    return n == "0" && 1 / t == -1 / 0 ? "-0" : n;
  }
  return ii = r, ii;
}
var si, Hl;
function mt() {
  if (Hl) return si;
  Hl = 1;
  var e = yt(), r = Gr();
  function t(n, a) {
    a = e(a, n);
    for (var i = 0, s = a.length; n != null && i < s; )
      n = n[r(a[i++])];
    return i && i == s ? n : void 0;
  }
  return si = t, si;
}
var oi, Vl;
function ay() {
  if (Vl) return oi;
  Vl = 1;
  var e = mt();
  function r(t, n, a) {
    var i = t == null ? void 0 : e(t, n);
    return i === void 0 ? a : i;
  }
  return oi = r, oi;
}
var ui, Wl;
function iy() {
  if (Wl) return ui;
  Wl = 1;
  function e(r, t) {
    return r != null && t in Object(r);
  }
  return ui = e, ui;
}
var ci, Yl;
function rp() {
  if (Yl) return ci;
  Yl = 1;
  var e = yt(), r = jr(), t = he(), n = dt(), a = Do(), i = Gr();
  function s(o, u, l) {
    u = e(u, o);
    for (var c = -1, d = u.length, h = !1; ++c < d; ) {
      var p = i(u[c]);
      if (!(h = o != null && l(o, p)))
        break;
      o = o[p];
    }
    return h || ++c != d ? h : (d = o == null ? 0 : o.length, !!d && a(d) && n(p, d) && (t(o) || r(o)));
  }
  return ci = s, ci;
}
var li, Ql;
function tp() {
  if (Ql) return li;
  Ql = 1;
  var e = iy(), r = rp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return li = t, li;
}
var di, Jl;
function sy() {
  if (Jl) return di;
  Jl = 1;
  var e = Jh(), r = ay(), t = tp(), n = Ko(), a = Xh(), i = Zh(), s = Gr(), o = 1, u = 2;
  function l(c, d) {
    return n(c) && a(d) ? i(s(c), d) : function(h) {
      var p = r(h, c);
      return p === void 0 && p === d ? t(h, c) : e(d, p, o | u);
    };
  }
  return di = l, di;
}
var fi, Xl;
function np() {
  if (Xl) return fi;
  Xl = 1;
  function e(r) {
    return function(t) {
      return t == null ? void 0 : t[r];
    };
  }
  return fi = e, fi;
}
var hi, Zl;
function oy() {
  if (Zl) return hi;
  Zl = 1;
  var e = mt();
  function r(t) {
    return function(n) {
      return e(n, t);
    };
  }
  return hi = r, hi;
}
var pi, ed;
function uy() {
  if (ed) return pi;
  ed = 1;
  var e = np(), r = oy(), t = Ko(), n = Gr();
  function a(i) {
    return t(i) ? e(n(i)) : r(i);
  }
  return pi = a, pi;
}
var vi, rd;
function Ve() {
  if (rd) return vi;
  rd = 1;
  var e = Zg(), r = sy(), t = fr(), n = he(), a = uy();
  function i(s) {
    return typeof s == "function" ? s : s == null ? t : typeof s == "object" ? n(s) ? r(s[0], s[1]) : e(s) : a(s);
  }
  return vi = i, vi;
}
var gi, td;
function ap() {
  if (td) return gi;
  td = 1;
  var e = Nh(), r = Bg(), t = Ve(), n = he();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s, 3));
  }
  return gi = a, gi;
}
var yi, nd;
function cy() {
  if (nd) return yi;
  nd = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n, a) {
    return n != null && r.call(n, a);
  }
  return yi = t, yi;
}
var mi, ad;
function ip() {
  if (ad) return mi;
  ad = 1;
  var e = cy(), r = rp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return mi = t, mi;
}
var bi, id;
function ly() {
  if (id) return bi;
  id = 1;
  var e = Lo(), r = wr(), t = jr(), n = he(), a = He(), i = _r(), s = ht(), o = $r(), u = "[object Map]", l = "[object Set]", c = Object.prototype, d = c.hasOwnProperty;
  function h(p) {
    if (p == null)
      return !0;
    if (a(p) && (n(p) || typeof p == "string" || typeof p.splice == "function" || i(p) || o(p) || t(p)))
      return !p.length;
    var g = r(p);
    if (g == u || g == l)
      return !p.size;
    if (s(p))
      return !e(p).length;
    for (var f in p)
      if (d.call(p, f))
        return !1;
    return !0;
  }
  return bi = h, bi;
}
var _i, sd;
function sp() {
  if (sd) return _i;
  sd = 1;
  function e(r) {
    return r === void 0;
  }
  return _i = e, _i;
}
var wi, od;
function op() {
  if (od) return wi;
  od = 1;
  var e = vt(), r = He();
  function t(n, a) {
    var i = -1, s = r(n) ? Array(n.length) : [];
    return e(n, function(o, u, l) {
      s[++i] = a(o, u, l);
    }), s;
  }
  return wi = t, wi;
}
var Ii, ud;
function up() {
  if (ud) return Ii;
  ud = 1;
  var e = gt(), r = Ve(), t = op(), n = he();
  function a(i, s) {
    var o = n(i) ? e : t;
    return o(i, r(s, 3));
  }
  return Ii = a, Ii;
}
var qi, cd;
function dy() {
  if (cd) return qi;
  cd = 1;
  function e(r, t, n, a) {
    var i = -1, s = r == null ? 0 : r.length;
    for (a && s && (n = r[++i]); ++i < s; )
      n = t(n, r[i], i, r);
    return n;
  }
  return qi = e, qi;
}
var Ei, ld;
function fy() {
  if (ld) return Ei;
  ld = 1;
  function e(r, t, n, a, i) {
    return i(r, function(s, o, u) {
      n = a ? (a = !1, s) : t(n, s, o, u);
    }), n;
  }
  return Ei = e, Ei;
}
var Ci, dd;
function cp() {
  if (dd) return Ci;
  dd = 1;
  var e = dy(), r = vt(), t = Ve(), n = fy(), a = he();
  function i(s, o, u) {
    var l = a(s) ? e : n, c = arguments.length < 3;
    return l(s, t(o, 4), u, c, r);
  }
  return Ci = i, Ci;
}
var Si, fd;
function hy() {
  if (fd) return Si;
  fd = 1;
  var e = cr(), r = he(), t = $e(), n = "[object String]";
  function a(i) {
    return typeof i == "string" || !r(i) && t(i) && e(i) == n;
  }
  return Si = a, Si;
}
var Ti, hd;
function py() {
  if (hd) return Ti;
  hd = 1;
  var e = np(), r = e("length");
  return Ti = r, Ti;
}
var Ri, pd;
function vy() {
  if (pd) return Ri;
  pd = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "\\u200d", o = RegExp("[" + s + e + a + i + "]");
  function u(l) {
    return o.test(l);
  }
  return Ri = u, Ri;
}
var xi, vd;
function gy() {
  if (vd) return xi;
  vd = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "[" + e + "]", o = "[" + a + "]", u = "\\ud83c[\\udffb-\\udfff]", l = "(?:" + o + "|" + u + ")", c = "[^" + e + "]", d = "(?:\\ud83c[\\udde6-\\uddff]){2}", h = "[\\ud800-\\udbff][\\udc00-\\udfff]", p = "\\u200d", g = l + "?", f = "[" + i + "]?", y = "(?:" + p + "(?:" + [c, d, h].join("|") + ")" + f + g + ")*", I = f + g + y, S = "(?:" + [c + o + "?", o, d, h, s].join("|") + ")", _ = RegExp(u + "(?=" + u + ")|" + S + I, "g");
  function A(k) {
    for (var m = _.lastIndex = 0; _.test(k); )
      ++m;
    return m;
  }
  return xi = A, xi;
}
var ki, gd;
function yy() {
  if (gd) return ki;
  gd = 1;
  var e = py(), r = vy(), t = gy();
  function n(a) {
    return r(a) ? t(a) : e(a);
  }
  return ki = n, ki;
}
var Ai, yd;
function my() {
  if (yd) return Ai;
  yd = 1;
  var e = Lo(), r = wr(), t = He(), n = hy(), a = yy(), i = "[object Map]", s = "[object Set]";
  function o(u) {
    if (u == null)
      return 0;
    if (t(u))
      return n(u) ? a(u) : u.length;
    var l = r(u);
    return l == i || l == s ? u.size : e(u).length;
  }
  return Ai = o, Ai;
}
var Oi, md;
function by() {
  if (md) return Oi;
  md = 1;
  var e = No(), r = zh(), t = Bo(), n = Ve(), a = pt(), i = he(), s = _r(), o = Lr(), u = Oe(), l = $r();
  function c(d, h, p) {
    var g = i(d), f = g || s(d) || l(d);
    if (h = n(h, 4), p == null) {
      var y = d && d.constructor;
      f ? p = g ? new y() : [] : u(d) ? p = o(y) ? r(a(d)) : {} : p = {};
    }
    return (f ? e : t)(d, function(I, S, _) {
      return h(p, I, S, _);
    }), p;
  }
  return Oi = c, Oi;
}
var Pi, bd;
function _y() {
  if (bd) return Pi;
  bd = 1;
  var e = br(), r = jr(), t = he(), n = e ? e.isConcatSpreadable : void 0;
  function a(i) {
    return t(i) || r(i) || !!(n && i && i[n]);
  }
  return Pi = a, Pi;
}
var Ni, _d;
function Ho() {
  if (_d) return Ni;
  _d = 1;
  var e = jo(), r = _y();
  function t(n, a, i, s, o) {
    var u = -1, l = n.length;
    for (i || (i = r), o || (o = []); ++u < l; ) {
      var c = n[u];
      a > 0 && i(c) ? a > 1 ? t(c, a - 1, i, s, o) : e(o, c) : s || (o[o.length] = c);
    }
    return o;
  }
  return Ni = t, Ni;
}
var Di, wd;
function wy() {
  if (wd) return Di;
  wd = 1;
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
  return Di = e, Di;
}
var Mi, Id;
function lp() {
  if (Id) return Mi;
  Id = 1;
  var e = wy(), r = Math.max;
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
  return Mi = t, Mi;
}
var Li, qd;
function Iy() {
  if (qd) return Li;
  qd = 1;
  var e = Go(), r = xh(), t = fr(), n = r ? function(a, i) {
    return r(a, "toString", {
      configurable: !0,
      enumerable: !1,
      value: e(i),
      writable: !0
    });
  } : t;
  return Li = n, Li;
}
var Fi, Ed;
function qy() {
  if (Ed) return Fi;
  Ed = 1;
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
  return Fi = n, Fi;
}
var ji, Cd;
function dp() {
  if (Cd) return ji;
  Cd = 1;
  var e = Iy(), r = qy(), t = r(e);
  return ji = t, ji;
}
var $i, Sd;
function bt() {
  if (Sd) return $i;
  Sd = 1;
  var e = fr(), r = lp(), t = dp();
  function n(a, i) {
    return t(r(a, i, e), a + "");
  }
  return $i = n, $i;
}
var Gi, Td;
function fp() {
  if (Td) return Gi;
  Td = 1;
  function e(r, t, n, a) {
    for (var i = r.length, s = n + (a ? 1 : -1); a ? s-- : ++s < i; )
      if (t(r[s], s, r))
        return s;
    return -1;
  }
  return Gi = e, Gi;
}
var zi, Rd;
function Ey() {
  if (Rd) return zi;
  Rd = 1;
  function e(r) {
    return r !== r;
  }
  return zi = e, zi;
}
var Bi, xd;
function Cy() {
  if (xd) return Bi;
  xd = 1;
  function e(r, t, n) {
    for (var a = n - 1, i = r.length; ++a < i; )
      if (r[a] === t)
        return a;
    return -1;
  }
  return Bi = e, Bi;
}
var Ui, kd;
function Sy() {
  if (kd) return Ui;
  kd = 1;
  var e = fp(), r = Ey(), t = Cy();
  function n(a, i, s) {
    return i === i ? t(a, i, s) : e(a, r, s);
  }
  return Ui = n, Ui;
}
var Ki, Ad;
function Ty() {
  if (Ad) return Ki;
  Ad = 1;
  var e = Sy();
  function r(t, n) {
    var a = t == null ? 0 : t.length;
    return !!a && e(t, n, 0) > -1;
  }
  return Ki = r, Ki;
}
var Hi, Od;
function Ry() {
  if (Od) return Hi;
  Od = 1;
  function e(r, t, n) {
    for (var a = -1, i = r == null ? 0 : r.length; ++a < i; )
      if (n(t, r[a]))
        return !0;
    return !1;
  }
  return Hi = e, Hi;
}
var Vi, Pd;
function xy() {
  if (Pd) return Vi;
  Pd = 1;
  function e() {
  }
  return Vi = e, Vi;
}
var Wi, Nd;
function ky() {
  if (Nd) return Wi;
  Nd = 1;
  var e = jh(), r = xy(), t = Uo(), n = 1 / 0, a = e && 1 / t(new e([, -0]))[1] == n ? function(i) {
    return new e(i);
  } : r;
  return Wi = a, Wi;
}
var Yi, Dd;
function Ay() {
  if (Dd) return Yi;
  Dd = 1;
  var e = Wh(), r = Ty(), t = Ry(), n = Yh(), a = ky(), i = Uo(), s = 200;
  function o(u, l, c) {
    var d = -1, h = r, p = u.length, g = !0, f = [], y = f;
    if (c)
      g = !1, h = t;
    else if (p >= s) {
      var I = l ? null : a(u);
      if (I)
        return i(I);
      g = !1, h = n, y = new e();
    } else
      y = l ? [] : f;
    e:
      for (; ++d < p; ) {
        var S = u[d], _ = l ? l(S) : S;
        if (S = c || S !== 0 ? S : 0, g && _ === _) {
          for (var A = y.length; A--; )
            if (y[A] === _)
              continue e;
          l && y.push(_), f.push(S);
        } else h(y, _, c) || (y !== f && y.push(_), f.push(S));
      }
    return f;
  }
  return Yi = o, Yi;
}
var Qi, Md;
function hp() {
  if (Md) return Qi;
  Md = 1;
  var e = He(), r = $e();
  function t(n) {
    return r(n) && e(n);
  }
  return Qi = t, Qi;
}
var Ji, Ld;
function Oy() {
  if (Ld) return Ji;
  Ld = 1;
  var e = Ho(), r = bt(), t = Ay(), n = hp(), a = r(function(i) {
    return t(e(i, 1, n, !0));
  });
  return Ji = a, Ji;
}
var Xi, Fd;
function Py() {
  if (Fd) return Xi;
  Fd = 1;
  var e = gt();
  function r(t, n) {
    return e(n, function(a) {
      return t[a];
    });
  }
  return Xi = r, Xi;
}
var Zi, jd;
function pp() {
  if (jd) return Zi;
  jd = 1;
  var e = Py(), r = ir();
  function t(n) {
    return n == null ? [] : e(n, r(n));
  }
  return Zi = t, Zi;
}
var es, $d;
function Pe() {
  if ($d) return es;
  $d = 1;
  var e;
  if (typeof Ao == "function")
    try {
      e = {
        clone: $g(),
        constant: Go(),
        each: Vh(),
        filter: ap(),
        has: ip(),
        isArray: he(),
        isEmpty: ly(),
        isFunction: Lr(),
        isUndefined: sp(),
        keys: ir(),
        map: up(),
        reduce: cp(),
        size: my(),
        transform: by(),
        union: Oy(),
        values: pp()
      };
    } catch {
    }
  return e || (e = window._), es = e, es;
}
var rs, Gd;
function Vo() {
  if (Gd) return rs;
  Gd = 1;
  var e = Pe();
  rs = a;
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
    var h = arguments, p = this;
    return e.each(c, function(g) {
      h.length > 1 ? p.setNode(g, d) : p.setNode(g);
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
      var h = function(p) {
        d.removeEdge(d._edgeObjs[p]);
      };
      delete this._nodes[c], this._isCompound && (this._removeFromParentsChildList(c), delete this._parent[c], e.each(this.children(c), function(p) {
        d.setParent(p);
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
    var p = {};
    function g(f) {
      var y = h.parent(f);
      return y === void 0 || d.hasNode(y) ? (p[f] = y, y) : y in p ? p[y] : g(y);
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
    var h = this, p = arguments;
    return e.reduce(c, function(g, f) {
      return p.length > 1 ? h.setEdge(g, f, d) : h.setEdge(g, f), f;
    }), this;
  }, a.prototype.setEdge = function() {
    var c, d, h, p, g = !1, f = arguments[0];
    typeof f == "object" && f !== null && "v" in f ? (c = f.v, d = f.w, h = f.name, arguments.length === 2 && (p = arguments[1], g = !0)) : (c = f, d = arguments[1], h = arguments[3], arguments.length > 2 && (p = arguments[2], g = !0)), c = "" + c, d = "" + d, e.isUndefined(h) || (h = "" + h);
    var y = o(this._isDirected, c, d, h);
    if (e.has(this._edgeLabels, y))
      return g && (this._edgeLabels[y] = p), this;
    if (!e.isUndefined(h) && !this._isMultigraph)
      throw new Error("Cannot set a named edge when isMultigraph = false");
    this.setNode(c), this.setNode(d), this._edgeLabels[y] = g ? p : this._defaultEdgeLabelFn(c, d, h);
    var I = u(this._isDirected, c, d, h);
    return c = I.v, d = I.w, Object.freeze(I), this._edgeObjs[y] = I, i(this._preds[d], c), i(this._sucs[c], d), this._in[d][y] = I, this._out[c][y] = I, this._edgeCount++, this;
  }, a.prototype.edge = function(c, d, h) {
    var p = arguments.length === 1 ? l(this._isDirected, arguments[0]) : o(this._isDirected, c, d, h);
    return this._edgeLabels[p];
  }, a.prototype.hasEdge = function(c, d, h) {
    var p = arguments.length === 1 ? l(this._isDirected, arguments[0]) : o(this._isDirected, c, d, h);
    return e.has(this._edgeLabels, p);
  }, a.prototype.removeEdge = function(c, d, h) {
    var p = arguments.length === 1 ? l(this._isDirected, arguments[0]) : o(this._isDirected, c, d, h), g = this._edgeObjs[p];
    return g && (c = g.v, d = g.w, delete this._edgeLabels[p], delete this._edgeObjs[p], s(this._preds[d], c), s(this._sucs[c], d), delete this._in[d][p], delete this._out[c][p], this._edgeCount--), this;
  }, a.prototype.inEdges = function(c, d) {
    var h = this._in[c];
    if (h) {
      var p = e.values(h);
      return d ? e.filter(p, function(g) {
        return g.v === d;
      }) : p;
    }
  }, a.prototype.outEdges = function(c, d) {
    var h = this._out[c];
    if (h) {
      var p = e.values(h);
      return d ? e.filter(p, function(g) {
        return g.w === d;
      }) : p;
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
  function o(c, d, h, p) {
    var g = "" + d, f = "" + h;
    if (!c && g > f) {
      var y = g;
      g = f, f = y;
    }
    return g + n + f + n + (e.isUndefined(p) ? r : p);
  }
  function u(c, d, h, p) {
    var g = "" + d, f = "" + h;
    if (!c && g > f) {
      var y = g;
      g = f, f = y;
    }
    var I = { v: g, w: f };
    return p && (I.name = p), I;
  }
  function l(c, d) {
    return o(c, d.v, d.w, d.name);
  }
  return rs;
}
var ts, zd;
function Ny() {
  return zd || (zd = 1, ts = "2.1.8"), ts;
}
var ns, Bd;
function Dy() {
  return Bd || (Bd = 1, ns = {
    Graph: Vo(),
    version: Ny()
  }), ns;
}
var as, Ud;
function My() {
  if (Ud) return as;
  Ud = 1;
  var e = Pe(), r = Vo();
  as = {
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
  return as;
}
var is, Kd;
function Ly() {
  if (Kd) return is;
  Kd = 1;
  var e = Pe();
  is = r;
  function r(t) {
    var n = {}, a = [], i;
    function s(o) {
      e.has(n, o) || (n[o] = !0, i.push(o), e.each(t.successors(o), s), e.each(t.predecessors(o), s));
    }
    return e.each(t.nodes(), function(o) {
      i = [], s(o), i.length && a.push(i);
    }), a;
  }
  return is;
}
var ss, Hd;
function vp() {
  if (Hd) return ss;
  Hd = 1;
  var e = Pe();
  ss = r;
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
  }, ss;
}
var os, Vd;
function gp() {
  if (Vd) return os;
  Vd = 1;
  var e = Pe(), r = vp();
  os = n;
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
    var l = {}, c = new r(), d, h, p = function(g) {
      var f = g.v !== d ? g.v : g.w, y = l[f], I = o(g), S = h.distance + I;
      if (I < 0)
        throw new Error("dijkstra does not allow negative edge weights. Bad edge: " + g + " Weight: " + I);
      S < y.distance && (y.distance = S, y.predecessor = d, c.decrease(f, S));
    };
    for (i.nodes().forEach(function(g) {
      var f = g === s ? 0 : Number.POSITIVE_INFINITY;
      l[g] = { distance: f }, c.add(g, f);
    }); c.size() > 0 && (d = c.removeMin(), h = l[d], h.distance !== Number.POSITIVE_INFINITY); )
      u(d).forEach(p);
    return l;
  }
  return os;
}
var us, Wd;
function Fy() {
  if (Wd) return us;
  Wd = 1;
  var e = gp(), r = Pe();
  us = t;
  function t(n, a, i) {
    return r.transform(n.nodes(), function(s, o) {
      s[o] = e(n, o, a, i);
    }, {});
  }
  return us;
}
var cs, Yd;
function yp() {
  if (Yd) return cs;
  Yd = 1;
  var e = Pe();
  cs = r;
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
  return cs;
}
var ls, Qd;
function jy() {
  if (Qd) return ls;
  Qd = 1;
  var e = Pe(), r = yp();
  ls = t;
  function t(n) {
    return e.filter(r(n), function(a) {
      return a.length > 1 || a.length === 1 && n.hasEdge(a[0], a[0]);
    });
  }
  return ls;
}
var ds, Jd;
function $y() {
  if (Jd) return ds;
  Jd = 1;
  var e = Pe();
  ds = t;
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
        u.forEach(function(p) {
          var g = h[l], f = c[p], y = h[p], I = g.distance + f.distance;
          I < y.distance && (y.distance = I, y.predecessor = f.predecessor);
        });
      });
    }), o;
  }
  return ds;
}
var fs, Xd;
function mp() {
  if (Xd) return fs;
  Xd = 1;
  var e = Pe();
  fs = r, r.CycleException = t;
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
  return t.prototype = new Error(), fs;
}
var hs, Zd;
function Gy() {
  if (Zd) return hs;
  Zd = 1;
  var e = mp();
  hs = r;
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
  return hs;
}
var ps, ef;
function bp() {
  if (ef) return ps;
  ef = 1;
  var e = Pe();
  ps = r;
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
  return ps;
}
var vs, rf;
function zy() {
  if (rf) return vs;
  rf = 1;
  var e = bp();
  vs = r;
  function r(t, n) {
    return e(t, n, "post");
  }
  return vs;
}
var gs, tf;
function By() {
  if (tf) return gs;
  tf = 1;
  var e = bp();
  gs = r;
  function r(t, n) {
    return e(t, n, "pre");
  }
  return gs;
}
var ys, nf;
function Uy() {
  if (nf) return ys;
  nf = 1;
  var e = Pe(), r = Vo(), t = vp();
  ys = n;
  function n(a, i) {
    var s = new r(), o = {}, u = new t(), l;
    function c(h) {
      var p = h.v === l ? h.w : h.v, g = u.priority(p);
      if (g !== void 0) {
        var f = i(h);
        f < g && (o[p] = l, u.decrease(p, f));
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
  return ys;
}
var ms, af;
function Ky() {
  return af || (af = 1, ms = {
    components: Ly(),
    dijkstra: gp(),
    dijkstraAll: Fy(),
    findCycles: jy(),
    floydWarshall: $y(),
    isAcyclic: Gy(),
    postorder: zy(),
    preorder: By(),
    prim: Uy(),
    tarjan: yp(),
    topsort: mp()
  }), ms;
}
var bs, sf;
function Hy() {
  if (sf) return bs;
  sf = 1;
  var e = Dy();
  return bs = {
    Graph: e.Graph,
    json: My(),
    alg: Ky(),
    version: e.version
  }, bs;
}
var Xr;
if (typeof Ao == "function")
  try {
    Xr = Hy();
  } catch {
  }
Xr || (Xr = window.graphlib);
var De = Xr, _s, of;
function Vy() {
  if (of) return _s;
  of = 1;
  var e = Uh(), r = 1, t = 4;
  function n(a) {
    return e(a, r | t);
  }
  return _s = n, _s;
}
var ws, uf;
function _t() {
  if (uf) return ws;
  uf = 1;
  var e = mr(), r = He(), t = dt(), n = Oe();
  function a(i, s, o) {
    if (!n(o))
      return !1;
    var u = typeof s;
    return (u == "number" ? r(o) && t(s, o.length) : u == "string" && s in o) ? e(o[s], i) : !1;
  }
  return ws = a, ws;
}
var Is, cf;
function Wy() {
  if (cf) return Is;
  cf = 1;
  var e = bt(), r = mr(), t = _t(), n = dr(), a = Object.prototype, i = a.hasOwnProperty, s = e(function(o, u) {
    o = Object(o);
    var l = -1, c = u.length, d = c > 2 ? u[2] : void 0;
    for (d && t(u[0], u[1], d) && (c = 1); ++l < c; )
      for (var h = u[l], p = n(h), g = -1, f = p.length; ++g < f; ) {
        var y = p[g], I = o[y];
        (I === void 0 || r(I, a[y]) && !i.call(o, y)) && (o[y] = h[y]);
      }
    return o;
  });
  return Is = s, Is;
}
var qs, lf;
function Yy() {
  if (lf) return qs;
  lf = 1;
  var e = Ve(), r = He(), t = ir();
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
  return qs = n, qs;
}
var Es, df;
function Qy() {
  if (df) return Es;
  df = 1;
  var e = /\s/;
  function r(t) {
    for (var n = t.length; n-- && e.test(t.charAt(n)); )
      ;
    return n;
  }
  return Es = r, Es;
}
var Cs, ff;
function Jy() {
  if (ff) return Cs;
  ff = 1;
  var e = Qy(), r = /^\s+/;
  function t(n) {
    return n && n.slice(0, e(n) + 1).replace(r, "");
  }
  return Cs = t, Cs;
}
var Ss, hf;
function Xy() {
  if (hf) return Ss;
  hf = 1;
  var e = Jy(), r = Oe(), t = Ir(), n = NaN, a = /^[-+]0x[0-9a-f]+$/i, i = /^0b[01]+$/i, s = /^0o[0-7]+$/i, o = parseInt;
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
  return Ss = u, Ss;
}
var Ts, pf;
function _p() {
  if (pf) return Ts;
  pf = 1;
  var e = Xy(), r = 1 / 0, t = 17976931348623157e292;
  function n(a) {
    if (!a)
      return a === 0 ? a : 0;
    if (a = e(a), a === r || a === -r) {
      var i = a < 0 ? -1 : 1;
      return i * t;
    }
    return a === a ? a : 0;
  }
  return Ts = n, Ts;
}
var Rs, vf;
function Zy() {
  if (vf) return Rs;
  vf = 1;
  var e = _p();
  function r(t) {
    var n = e(t), a = n % 1;
    return n === n ? a ? n - a : n : 0;
  }
  return Rs = r, Rs;
}
var xs, gf;
function em() {
  if (gf) return xs;
  gf = 1;
  var e = fp(), r = Ve(), t = Zy(), n = Math.max;
  function a(i, s, o) {
    var u = i == null ? 0 : i.length;
    if (!u)
      return -1;
    var l = o == null ? 0 : t(o);
    return l < 0 && (l = n(u + l, 0)), e(i, r(s, 3), l);
  }
  return xs = a, xs;
}
var ks, yf;
function rm() {
  if (yf) return ks;
  yf = 1;
  var e = Yy(), r = em(), t = e(r);
  return ks = t, ks;
}
var As, mf;
function wp() {
  if (mf) return As;
  mf = 1;
  var e = Ho();
  function r(t) {
    var n = t == null ? 0 : t.length;
    return n ? e(t, 1) : [];
  }
  return As = r, As;
}
var Os, bf;
function tm() {
  if (bf) return Os;
  bf = 1;
  var e = zo(), r = Kh(), t = dr();
  function n(a, i) {
    return a == null ? a : e(a, r(i), t);
  }
  return Os = n, Os;
}
var Ps, _f;
function nm() {
  if (_f) return Ps;
  _f = 1;
  function e(r) {
    var t = r == null ? 0 : r.length;
    return t ? r[t - 1] : void 0;
  }
  return Ps = e, Ps;
}
var Ns, wf;
function am() {
  if (wf) return Ns;
  wf = 1;
  var e = ct(), r = Bo(), t = Ve();
  function n(a, i) {
    var s = {};
    return i = t(i, 3), r(a, function(o, u, l) {
      e(s, u, i(o, u, l));
    }), s;
  }
  return Ns = n, Ns;
}
var Ds, If;
function Wo() {
  if (If) return Ds;
  If = 1;
  var e = Ir();
  function r(t, n, a) {
    for (var i = -1, s = t.length; ++i < s; ) {
      var o = t[i], u = n(o);
      if (u != null && (l === void 0 ? u === u && !e(u) : a(u, l)))
        var l = u, c = o;
    }
    return c;
  }
  return Ds = r, Ds;
}
var Ms, qf;
function im() {
  if (qf) return Ms;
  qf = 1;
  function e(r, t) {
    return r > t;
  }
  return Ms = e, Ms;
}
var Ls, Ef;
function sm() {
  if (Ef) return Ls;
  Ef = 1;
  var e = Wo(), r = im(), t = fr();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return Ls = n, Ls;
}
var Fs, Cf;
function Ip() {
  if (Cf) return Fs;
  Cf = 1;
  var e = ct(), r = mr();
  function t(n, a, i) {
    (i !== void 0 && !r(n[a], i) || i === void 0 && !(a in n)) && e(n, a, i);
  }
  return Fs = t, Fs;
}
var js, Sf;
function om() {
  if (Sf) return js;
  Sf = 1;
  var e = cr(), r = pt(), t = $e(), n = "[object Object]", a = Function.prototype, i = Object.prototype, s = a.toString, o = i.hasOwnProperty, u = s.call(Object);
  function l(c) {
    if (!t(c) || e(c) != n)
      return !1;
    var d = r(c);
    if (d === null)
      return !0;
    var h = o.call(d, "constructor") && d.constructor;
    return typeof h == "function" && h instanceof h && s.call(h) == u;
  }
  return js = l, js;
}
var $s, Tf;
function qp() {
  if (Tf) return $s;
  Tf = 1;
  function e(r, t) {
    if (!(t === "constructor" && typeof r[t] == "function") && t != "__proto__")
      return r[t];
  }
  return $s = e, $s;
}
var Gs, Rf;
function um() {
  if (Rf) return Gs;
  Rf = 1;
  var e = Fr(), r = dr();
  function t(n) {
    return e(n, r(n));
  }
  return Gs = t, Gs;
}
var zs, xf;
function cm() {
  if (xf) return zs;
  xf = 1;
  var e = Ip(), r = Oh(), t = Gh(), n = Ph(), a = Bh(), i = jr(), s = he(), o = hp(), u = _r(), l = Lr(), c = Oe(), d = om(), h = $r(), p = qp(), g = um();
  function f(y, I, S, _, A, k, m) {
    var C = p(y, S), O = p(I, S), L = m.get(O);
    if (L) {
      e(y, S, L);
      return;
    }
    var N = k ? k(C, O, S + "", y, I, m) : void 0, w = N === void 0;
    if (w) {
      var F = s(O), j = !F && u(O), Y = !F && !j && h(O);
      N = O, F || j || Y ? s(C) ? N = C : o(C) ? N = n(C) : j ? (w = !1, N = r(O, !0)) : Y ? (w = !1, N = t(O, !0)) : N = [] : d(O) || i(O) ? (N = C, i(C) ? N = g(C) : (!c(C) || l(C)) && (N = a(O))) : w = !1;
    }
    w && (m.set(O, N), A(N, O, _, k, m), m.delete(O)), e(y, S, N);
  }
  return zs = f, zs;
}
var Bs, kf;
function lm() {
  if (kf) return Bs;
  kf = 1;
  var e = ut(), r = Ip(), t = zo(), n = cm(), a = Oe(), i = dr(), s = qp();
  function o(u, l, c, d, h) {
    u !== l && t(l, function(p, g) {
      if (h || (h = new e()), a(p))
        n(u, l, g, c, o, d, h);
      else {
        var f = d ? d(s(u, g), p, g + "", u, l, h) : void 0;
        f === void 0 && (f = p), r(u, g, f);
      }
    }, i);
  }
  return Bs = o, Bs;
}
var Us, Af;
function dm() {
  if (Af) return Us;
  Af = 1;
  var e = bt(), r = _t();
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
  return Us = t, Us;
}
var Ks, Of;
function fm() {
  if (Of) return Ks;
  Of = 1;
  var e = lm(), r = dm(), t = r(function(n, a, i) {
    e(n, a, i);
  });
  return Ks = t, Ks;
}
var Hs, Pf;
function Ep() {
  if (Pf) return Hs;
  Pf = 1;
  function e(r, t) {
    return r < t;
  }
  return Hs = e, Hs;
}
var Vs, Nf;
function hm() {
  if (Nf) return Vs;
  Nf = 1;
  var e = Wo(), r = Ep(), t = fr();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return Vs = n, Vs;
}
var Ws, Df;
function pm() {
  if (Df) return Ws;
  Df = 1;
  var e = Wo(), r = Ve(), t = Ep();
  function n(a, i) {
    return a && a.length ? e(a, r(i, 2), t) : void 0;
  }
  return Ws = n, Ws;
}
var Ys, Mf;
function vm() {
  if (Mf) return Ys;
  Mf = 1;
  var e = Ne(), r = function() {
    return e.Date.now();
  };
  return Ys = r, Ys;
}
var Qs, Lf;
function gm() {
  if (Lf) return Qs;
  Lf = 1;
  var e = lt(), r = yt(), t = dt(), n = Oe(), a = Gr();
  function i(s, o, u, l) {
    if (!n(s))
      return s;
    o = r(o, s);
    for (var c = -1, d = o.length, h = d - 1, p = s; p != null && ++c < d; ) {
      var g = a(o[c]), f = u;
      if (g === "__proto__" || g === "constructor" || g === "prototype")
        return s;
      if (c != h) {
        var y = p[g];
        f = l ? l(y, g, p) : void 0, f === void 0 && (f = n(y) ? y : t(o[c + 1]) ? [] : {});
      }
      e(p, g, f), p = p[g];
    }
    return s;
  }
  return Qs = i, Qs;
}
var Js, Ff;
function ym() {
  if (Ff) return Js;
  Ff = 1;
  var e = mt(), r = gm(), t = yt();
  function n(a, i, s) {
    for (var o = -1, u = i.length, l = {}; ++o < u; ) {
      var c = i[o], d = e(a, c);
      s(d, c) && r(l, t(c, a), d);
    }
    return l;
  }
  return Js = n, Js;
}
var Xs, jf;
function mm() {
  if (jf) return Xs;
  jf = 1;
  var e = ym(), r = tp();
  function t(n, a) {
    return e(n, a, function(i, s) {
      return r(n, s);
    });
  }
  return Xs = t, Xs;
}
var Zs, $f;
function bm() {
  if ($f) return Zs;
  $f = 1;
  var e = wp(), r = lp(), t = dp();
  function n(a) {
    return t(r(a, void 0, e), a + "");
  }
  return Zs = n, Zs;
}
var eo, Gf;
function _m() {
  if (Gf) return eo;
  Gf = 1;
  var e = mm(), r = bm(), t = r(function(n, a) {
    return n == null ? {} : e(n, a);
  });
  return eo = t, eo;
}
var ro, zf;
function wm() {
  if (zf) return ro;
  zf = 1;
  var e = Math.ceil, r = Math.max;
  function t(n, a, i, s) {
    for (var o = -1, u = r(e((a - n) / (i || 1)), 0), l = Array(u); u--; )
      l[s ? u : ++o] = n, n += i;
    return l;
  }
  return ro = t, ro;
}
var to, Bf;
function Im() {
  if (Bf) return to;
  Bf = 1;
  var e = wm(), r = _t(), t = _p();
  function n(a) {
    return function(i, s, o) {
      return o && typeof o != "number" && r(i, s, o) && (s = o = void 0), i = t(i), s === void 0 ? (s = i, i = 0) : s = t(s), o = o === void 0 ? i < s ? 1 : -1 : t(o), e(i, s, o, a);
    };
  }
  return to = n, to;
}
var no, Uf;
function qm() {
  if (Uf) return no;
  Uf = 1;
  var e = Im(), r = e();
  return no = r, no;
}
var ao, Kf;
function Em() {
  if (Kf) return ao;
  Kf = 1;
  function e(r, t) {
    var n = r.length;
    for (r.sort(t); n--; )
      r[n] = r[n].value;
    return r;
  }
  return ao = e, ao;
}
var io, Hf;
function Cm() {
  if (Hf) return io;
  Hf = 1;
  var e = Ir();
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
  return io = r, io;
}
var so, Vf;
function Sm() {
  if (Vf) return so;
  Vf = 1;
  var e = Cm();
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
  return so = r, so;
}
var oo, Wf;
function Tm() {
  if (Wf) return oo;
  Wf = 1;
  var e = gt(), r = mt(), t = Ve(), n = op(), a = Em(), i = ft(), s = Sm(), o = fr(), u = he();
  function l(c, d, h) {
    d.length ? d = e(d, function(f) {
      return u(f) ? function(y) {
        return r(y, f.length === 1 ? f[0] : f);
      } : f;
    }) : d = [o];
    var p = -1;
    d = e(d, i(t));
    var g = n(c, function(f, y, I) {
      var S = e(d, function(_) {
        return _(f);
      });
      return { criteria: S, index: ++p, value: f };
    });
    return a(g, function(f, y) {
      return s(f, y, h);
    });
  }
  return oo = l, oo;
}
var uo, Yf;
function Rm() {
  if (Yf) return uo;
  Yf = 1;
  var e = Ho(), r = Tm(), t = bt(), n = _t(), a = t(function(i, s) {
    if (i == null)
      return [];
    var o = s.length;
    return o > 1 && n(i, s[0], s[1]) ? s = [] : o > 2 && n(s[0], s[1], s[2]) && (s = [s[0]]), r(i, e(s, 1), []);
  });
  return uo = a, uo;
}
var co, Qf;
function xm() {
  if (Qf) return co;
  Qf = 1;
  var e = ep(), r = 0;
  function t(n) {
    var a = ++r;
    return e(n) + a;
  }
  return co = t, co;
}
var lo, Jf;
function km() {
  if (Jf) return lo;
  Jf = 1;
  function e(r, t, n) {
    for (var a = -1, i = r.length, s = t.length, o = {}; ++a < i; ) {
      var u = a < s ? t[a] : void 0;
      n(o, r[a], u);
    }
    return o;
  }
  return lo = e, lo;
}
var fo, Xf;
function Am() {
  if (Xf) return fo;
  Xf = 1;
  var e = lt(), r = km();
  function t(n, a) {
    return r(n || [], a || [], e);
  }
  return fo = t, fo;
}
var Zr;
if (typeof Ao == "function")
  try {
    Zr = {
      cloneDeep: Vy(),
      constant: Go(),
      defaults: Wy(),
      each: Vh(),
      filter: ap(),
      find: rm(),
      flatten: wp(),
      forEach: Hh(),
      forIn: tm(),
      has: ip(),
      isUndefined: sp(),
      last: nm(),
      map: up(),
      mapValues: am(),
      max: sm(),
      merge: fm(),
      min: hm(),
      minBy: pm(),
      now: vm(),
      pick: _m(),
      range: qm(),
      reduce: cp(),
      sortBy: Rm(),
      uniqueId: xm(),
      values: pp(),
      zipObject: Am()
    };
  } catch {
  }
Zr || (Zr = window._);
var oe = Zr, Om = wt;
function wt() {
  var e = {};
  e._next = e._prev = e, this._sentinel = e;
}
wt.prototype.dequeue = function() {
  var e = this._sentinel, r = e._prev;
  if (r !== e)
    return Cp(r), r;
};
wt.prototype.enqueue = function(e) {
  var r = this._sentinel;
  e._prev && e._next && Cp(e), e._next = r._next, r._next._prev = e, r._next = e, e._prev = r;
};
wt.prototype.toString = function() {
  for (var e = [], r = this._sentinel, t = r._prev; t !== r; )
    e.push(JSON.stringify(t, Pm)), t = t._prev;
  return "[" + e.join(", ") + "]";
};
function Cp(e) {
  e._prev._next = e._next, e._next._prev = e._prev, delete e._next, delete e._prev;
}
function Pm(e, r) {
  if (e !== "_next" && e !== "_prev")
    return r;
}
var Be = oe, Nm = De.Graph, Dm = Om, Mm = Fm, Lm = Be.constant(1);
function Fm(e, r) {
  if (e.nodeCount() <= 1)
    return [];
  var t = $m(e, r || Lm), n = jm(t.graph, t.buckets, t.zeroIdx);
  return Be.flatten(Be.map(n, function(a) {
    return e.outEdges(a.v, a.w);
  }), !0);
}
function jm(e, r, t) {
  for (var n = [], a = r[r.length - 1], i = r[0], s; e.nodeCount(); ) {
    for (; s = i.dequeue(); )
      ho(e, r, t, s);
    for (; s = a.dequeue(); )
      ho(e, r, t, s);
    if (e.nodeCount()) {
      for (var o = r.length - 2; o > 0; --o)
        if (s = r[o].dequeue(), s) {
          n = n.concat(ho(e, r, t, s, !0));
          break;
        }
    }
  }
  return n;
}
function ho(e, r, t, n, a) {
  var i = a ? [] : void 0;
  return Be.forEach(e.inEdges(n.v), function(s) {
    var o = e.edge(s), u = e.node(s.v);
    a && i.push({ v: s.v, w: s.w }), u.out -= o, So(r, t, u);
  }), Be.forEach(e.outEdges(n.v), function(s) {
    var o = e.edge(s), u = s.w, l = e.node(u);
    l.in -= o, So(r, t, l);
  }), e.removeNode(n.v), i;
}
function $m(e, r) {
  var t = new Nm(), n = 0, a = 0;
  Be.forEach(e.nodes(), function(o) {
    t.setNode(o, { v: o, in: 0, out: 0 });
  }), Be.forEach(e.edges(), function(o) {
    var u = t.edge(o.v, o.w) || 0, l = r(o), c = u + l;
    t.setEdge(o.v, o.w, c), a = Math.max(a, t.node(o.v).out += l), n = Math.max(n, t.node(o.w).in += l);
  });
  var i = Be.range(a + n + 3).map(function() {
    return new Dm();
  }), s = n + 1;
  return Be.forEach(t.nodes(), function(o) {
    So(i, s, t.node(o));
  }), { graph: t, buckets: i, zeroIdx: s };
}
function So(e, r, t) {
  t.out ? t.in ? e[t.out - t.in + r].enqueue(t) : e[e.length - 1].enqueue(t) : e[0].enqueue(t);
}
var or = oe, Gm = Mm, zm = {
  run: Bm,
  undo: Km
};
function Bm(e) {
  var r = e.graph().acyclicer === "greedy" ? Gm(e, t(e)) : Um(e);
  or.forEach(r, function(n) {
    var a = e.edge(n);
    e.removeEdge(n), a.forwardName = n.name, a.reversed = !0, e.setEdge(n.w, n.v, a, or.uniqueId("rev"));
  });
  function t(n) {
    return function(a) {
      return n.edge(a).weight;
    };
  }
}
function Um(e) {
  var r = [], t = {}, n = {};
  function a(i) {
    or.has(n, i) || (n[i] = !0, t[i] = !0, or.forEach(e.outEdges(i), function(s) {
      or.has(t, s.w) ? r.push(s) : a(s.w);
    }), delete t[i]);
  }
  return or.forEach(e.nodes(), a), r;
}
function Km(e) {
  or.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (t.reversed) {
      e.removeEdge(r);
      var n = t.forwardName;
      delete t.reversed, delete t.forwardName, e.setEdge(r.w, r.v, t, n);
    }
  });
}
var J = oe, Sp = De.Graph, Se = {
  addDummyNode: Tp,
  simplify: Hm,
  asNonCompoundGraph: Vm,
  successorWeights: Wm,
  predecessorWeights: Ym,
  intersectRect: Qm,
  buildLayerMatrix: Jm,
  normalizeRanks: Xm,
  removeEmptyRanks: Zm,
  addBorderNode: eb,
  maxRank: Rp,
  partition: rb,
  time: tb,
  notime: nb
};
function Tp(e, r, t, n) {
  var a;
  do
    a = J.uniqueId(n);
  while (e.hasNode(a));
  return t.dummy = r, e.setNode(a, t), a;
}
function Hm(e) {
  var r = new Sp().setGraph(e.graph());
  return J.forEach(e.nodes(), function(t) {
    r.setNode(t, e.node(t));
  }), J.forEach(e.edges(), function(t) {
    var n = r.edge(t.v, t.w) || { weight: 0, minlen: 1 }, a = e.edge(t);
    r.setEdge(t.v, t.w, {
      weight: n.weight + a.weight,
      minlen: Math.max(n.minlen, a.minlen)
    });
  }), r;
}
function Vm(e) {
  var r = new Sp({ multigraph: e.isMultigraph() }).setGraph(e.graph());
  return J.forEach(e.nodes(), function(t) {
    e.children(t).length || r.setNode(t, e.node(t));
  }), J.forEach(e.edges(), function(t) {
    r.setEdge(t, e.edge(t));
  }), r;
}
function Wm(e) {
  var r = J.map(e.nodes(), function(t) {
    var n = {};
    return J.forEach(e.outEdges(t), function(a) {
      n[a.w] = (n[a.w] || 0) + e.edge(a).weight;
    }), n;
  });
  return J.zipObject(e.nodes(), r);
}
function Ym(e) {
  var r = J.map(e.nodes(), function(t) {
    var n = {};
    return J.forEach(e.inEdges(t), function(a) {
      n[a.v] = (n[a.v] || 0) + e.edge(a).weight;
    }), n;
  });
  return J.zipObject(e.nodes(), r);
}
function Qm(e, r) {
  var t = e.x, n = e.y, a = r.x - t, i = r.y - n, s = e.width / 2, o = e.height / 2;
  if (!a && !i)
    throw new Error("Not possible to find intersection inside of the rectangle");
  var u, l;
  return Math.abs(i) * s > Math.abs(a) * o ? (i < 0 && (o = -o), u = o * a / i, l = o) : (a < 0 && (s = -s), u = s, l = s * i / a), { x: t + u, y: n + l };
}
function Jm(e) {
  var r = J.map(J.range(Rp(e) + 1), function() {
    return [];
  });
  return J.forEach(e.nodes(), function(t) {
    var n = e.node(t), a = n.rank;
    J.isUndefined(a) || (r[a][n.order] = t);
  }), r;
}
function Xm(e) {
  var r = J.min(J.map(e.nodes(), function(t) {
    return e.node(t).rank;
  }));
  J.forEach(e.nodes(), function(t) {
    var n = e.node(t);
    J.has(n, "rank") && (n.rank -= r);
  });
}
function Zm(e) {
  var r = J.min(J.map(e.nodes(), function(i) {
    return e.node(i).rank;
  })), t = [];
  J.forEach(e.nodes(), function(i) {
    var s = e.node(i).rank - r;
    t[s] || (t[s] = []), t[s].push(i);
  });
  var n = 0, a = e.graph().nodeRankFactor;
  J.forEach(t, function(i, s) {
    J.isUndefined(i) && s % a !== 0 ? --n : n && J.forEach(i, function(o) {
      e.node(o).rank += n;
    });
  });
}
function eb(e, r, t, n) {
  var a = {
    width: 0,
    height: 0
  };
  return arguments.length >= 4 && (a.rank = t, a.order = n), Tp(e, "border", a, r);
}
function Rp(e) {
  return J.max(J.map(e.nodes(), function(r) {
    var t = e.node(r).rank;
    if (!J.isUndefined(t))
      return t;
  }));
}
function rb(e, r) {
  var t = { lhs: [], rhs: [] };
  return J.forEach(e, function(n) {
    r(n) ? t.lhs.push(n) : t.rhs.push(n);
  }), t;
}
function tb(e, r) {
  var t = J.now();
  try {
    return r();
  } finally {
    console.log(e + " time: " + (J.now() - t) + "ms");
  }
}
function nb(e, r) {
  return r();
}
var xp = oe, ab = Se, ib = {
  run: sb,
  undo: ub
};
function sb(e) {
  e.graph().dummyChains = [], xp.forEach(e.edges(), function(r) {
    ob(e, r);
  });
}
function ob(e, r) {
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
      }, l = ab.addDummyNode(e, "edge", c, "_d"), n === u && (c.width = o.width, c.height = o.height, c.dummy = "edge-label", c.labelpos = o.labelpos), e.setEdge(t, l, { weight: o.weight }, s), d === 0 && e.graph().dummyChains.push(l), t = l;
    e.setEdge(t, a, { weight: o.weight }, s);
  }
}
function ub(e) {
  xp.forEach(e.graph().dummyChains, function(r) {
    var t = e.node(r), n = t.edgeLabel, a;
    for (e.setEdge(t.edgeObj, n); t.dummy; )
      a = e.successors(r)[0], e.removeNode(r), n.points.push({ x: t.x, y: t.y }), t.dummy === "edge-label" && (n.x = t.x, n.y = t.y, n.width = t.width, n.height = t.height), r = a, t = e.node(r);
  });
}
var Vr = oe, It = {
  longestPath: cb,
  slack: lb
};
function cb(e) {
  var r = {};
  function t(n) {
    var a = e.node(n);
    if (Vr.has(r, n))
      return a.rank;
    r[n] = !0;
    var i = Vr.min(Vr.map(e.outEdges(n), function(s) {
      return t(s.w) - e.edge(s).minlen;
    }));
    return (i === Number.POSITIVE_INFINITY || // return value of _.map([]) for Lodash 3
    i === void 0 || // return value of _.map([]) for Lodash 4
    i === null) && (i = 0), a.rank = i;
  }
  Vr.forEach(e.sources(), t);
}
function lb(e, r) {
  return e.node(r.w).rank - e.node(r.v).rank - e.edge(r).minlen;
}
var et = oe, db = De.Graph, rt = It.slack, kp = fb;
function fb(e) {
  var r = new db({ directed: !1 }), t = e.nodes()[0], n = e.nodeCount();
  r.setNode(t, {});
  for (var a, i; hb(r, e) < n; )
    a = pb(r, e), i = r.hasNode(a.v) ? rt(e, a) : -rt(e, a), vb(r, e, i);
  return r;
}
function hb(e, r) {
  function t(n) {
    et.forEach(r.nodeEdges(n), function(a) {
      var i = a.v, s = n === i ? a.w : i;
      !e.hasNode(s) && !rt(r, a) && (e.setNode(s, {}), e.setEdge(n, s, {}), t(s));
    });
  }
  return et.forEach(e.nodes(), t), e.nodeCount();
}
function pb(e, r) {
  return et.minBy(r.edges(), function(t) {
    if (e.hasNode(t.v) !== e.hasNode(t.w))
      return rt(r, t);
  });
}
function vb(e, r, t) {
  et.forEach(e.nodes(), function(n) {
    r.node(n).rank += t;
  });
}
var Ke = oe, gb = kp, yb = It.slack, mb = It.longestPath, bb = De.alg.preorder, _b = De.alg.postorder, wb = Se.simplify, Ib = hr;
hr.initLowLimValues = Qo;
hr.initCutValues = Yo;
hr.calcCutValue = Ap;
hr.leaveEdge = Pp;
hr.enterEdge = Np;
hr.exchangeEdges = Dp;
function hr(e) {
  e = wb(e), mb(e);
  var r = gb(e);
  Qo(r), Yo(r, e);
  for (var t, n; t = Pp(r); )
    n = Np(r, e, t), Dp(r, e, t, n);
}
function Yo(e, r) {
  var t = _b(e, e.nodes());
  t = t.slice(0, t.length - 1), Ke.forEach(t, function(n) {
    qb(e, r, n);
  });
}
function qb(e, r, t) {
  var n = e.node(t), a = n.parent;
  e.edge(t, a).cutvalue = Ap(e, r, t);
}
function Ap(e, r, t) {
  var n = e.node(t), a = n.parent, i = !0, s = r.edge(t, a), o = 0;
  return s || (i = !1, s = r.edge(a, t)), o = s.weight, Ke.forEach(r.nodeEdges(t), function(u) {
    var l = u.v === t, c = l ? u.w : u.v;
    if (c !== a) {
      var d = l === i, h = r.edge(u).weight;
      if (o += d ? h : -h, Cb(e, t, c)) {
        var p = e.edge(t, c).cutvalue;
        o += d ? -p : p;
      }
    }
  }), o;
}
function Qo(e, r) {
  arguments.length < 2 && (r = e.nodes()[0]), Op(e, {}, 1, r);
}
function Op(e, r, t, n, a) {
  var i = t, s = e.node(n);
  return r[n] = !0, Ke.forEach(e.neighbors(n), function(o) {
    Ke.has(r, o) || (t = Op(e, r, t, o, n));
  }), s.low = i, s.lim = t++, a ? s.parent = a : delete s.parent, t;
}
function Pp(e) {
  return Ke.find(e.edges(), function(r) {
    return e.edge(r).cutvalue < 0;
  });
}
function Np(e, r, t) {
  var n = t.v, a = t.w;
  r.hasEdge(n, a) || (n = t.w, a = t.v);
  var i = e.node(n), s = e.node(a), o = i, u = !1;
  i.lim > s.lim && (o = s, u = !0);
  var l = Ke.filter(r.edges(), function(c) {
    return u === Zf(e, e.node(c.v), o) && u !== Zf(e, e.node(c.w), o);
  });
  return Ke.minBy(l, function(c) {
    return yb(r, c);
  });
}
function Dp(e, r, t, n) {
  var a = t.v, i = t.w;
  e.removeEdge(a, i), e.setEdge(n.v, n.w, {}), Qo(e), Yo(e, r), Eb(e, r);
}
function Eb(e, r) {
  var t = Ke.find(e.nodes(), function(a) {
    return !r.node(a).parent;
  }), n = bb(e, t);
  n = n.slice(1), Ke.forEach(n, function(a) {
    var i = e.node(a).parent, s = r.edge(a, i), o = !1;
    s || (s = r.edge(i, a), o = !0), r.node(a).rank = r.node(i).rank + (o ? s.minlen : -s.minlen);
  });
}
function Cb(e, r, t) {
  return e.hasEdge(r, t);
}
function Zf(e, r, t) {
  return t.low <= r.lim && r.lim <= t.lim;
}
var Sb = It, Mp = Sb.longestPath, Tb = kp, Rb = Ib, xb = kb;
function kb(e) {
  switch (e.graph().ranker) {
    case "network-simplex":
      eh(e);
      break;
    case "tight-tree":
      Ob(e);
      break;
    case "longest-path":
      Ab(e);
      break;
    default:
      eh(e);
  }
}
var Ab = Mp;
function Ob(e) {
  Mp(e), Tb(e);
}
function eh(e) {
  Rb(e);
}
var To = oe, Pb = Nb;
function Nb(e) {
  var r = Mb(e);
  To.forEach(e.graph().dummyChains, function(t) {
    for (var n = e.node(t), a = n.edgeObj, i = Db(e, r, a.v, a.w), s = i.path, o = i.lca, u = 0, l = s[u], c = !0; t !== a.w; ) {
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
function Db(e, r, t, n) {
  var a = [], i = [], s = Math.min(r[t].low, r[n].low), o = Math.max(r[t].lim, r[n].lim), u, l;
  u = t;
  do
    u = e.parent(u), a.push(u);
  while (u && (r[u].low > s || o > r[u].lim));
  for (l = u, u = n; (u = e.parent(u)) !== l; )
    i.push(u);
  return { path: a.concat(i.reverse()), lca: l };
}
function Mb(e) {
  var r = {}, t = 0;
  function n(a) {
    var i = t;
    To.forEach(e.children(a), n), r[a] = { low: i, lim: t++ };
  }
  return To.forEach(e.children(), n), r;
}
var Ue = oe, Ro = Se, Lb = {
  run: Fb,
  cleanup: Gb
};
function Fb(e) {
  var r = Ro.addDummyNode(e, "root", {}, "_root"), t = jb(e), n = Ue.max(Ue.values(t)) - 1, a = 2 * n + 1;
  e.graph().nestingRoot = r, Ue.forEach(e.edges(), function(s) {
    e.edge(s).minlen *= a;
  });
  var i = $b(e) + 1;
  Ue.forEach(e.children(), function(s) {
    Lp(e, r, a, i, n, t, s);
  }), e.graph().nodeRankFactor = a;
}
function Lp(e, r, t, n, a, i, s) {
  var o = e.children(s);
  if (!o.length) {
    s !== r && e.setEdge(r, s, { weight: 0, minlen: t });
    return;
  }
  var u = Ro.addBorderNode(e, "_bt"), l = Ro.addBorderNode(e, "_bb"), c = e.node(s);
  e.setParent(u, s), c.borderTop = u, e.setParent(l, s), c.borderBottom = l, Ue.forEach(o, function(d) {
    Lp(e, r, t, n, a, i, d);
    var h = e.node(d), p = h.borderTop ? h.borderTop : d, g = h.borderBottom ? h.borderBottom : d, f = h.borderTop ? n : 2 * n, y = p !== g ? 1 : a - i[s] + 1;
    e.setEdge(u, p, {
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
function jb(e) {
  var r = {};
  function t(n, a) {
    var i = e.children(n);
    i && i.length && Ue.forEach(i, function(s) {
      t(s, a + 1);
    }), r[n] = a;
  }
  return Ue.forEach(e.children(), function(n) {
    t(n, 1);
  }), r;
}
function $b(e) {
  return Ue.reduce(e.edges(), function(r, t) {
    return r + e.edge(t).weight;
  }, 0);
}
function Gb(e) {
  var r = e.graph();
  e.removeNode(r.nestingRoot), delete r.nestingRoot, Ue.forEach(e.edges(), function(t) {
    var n = e.edge(t);
    n.nestingEdge && e.removeEdge(t);
  });
}
var po = oe, zb = Se, Bb = Ub;
function Ub(e) {
  function r(t) {
    var n = e.children(t), a = e.node(t);
    if (n.length && po.forEach(n, r), po.has(a, "minRank")) {
      a.borderLeft = [], a.borderRight = [];
      for (var i = a.minRank, s = a.maxRank + 1; i < s; ++i)
        rh(e, "borderLeft", "_bl", t, a, i), rh(e, "borderRight", "_br", t, a, i);
    }
  }
  po.forEach(e.children(), r);
}
function rh(e, r, t, n, a, i) {
  var s = { width: 0, height: 0, rank: i, borderType: r }, o = a[r][i - 1], u = zb.addDummyNode(e, "border", s, t);
  a[r][i] = u, e.setParent(u, n), o && e.setEdge(o, u, { weight: 1 });
}
var je = oe, Kb = {
  adjust: Hb,
  undo: Vb
};
function Hb(e) {
  var r = e.graph().rankdir.toLowerCase();
  (r === "lr" || r === "rl") && Fp(e);
}
function Vb(e) {
  var r = e.graph().rankdir.toLowerCase();
  (r === "bt" || r === "rl") && Wb(e), (r === "lr" || r === "rl") && (Yb(e), Fp(e));
}
function Fp(e) {
  je.forEach(e.nodes(), function(r) {
    th(e.node(r));
  }), je.forEach(e.edges(), function(r) {
    th(e.edge(r));
  });
}
function th(e) {
  var r = e.width;
  e.width = e.height, e.height = r;
}
function Wb(e) {
  je.forEach(e.nodes(), function(r) {
    vo(e.node(r));
  }), je.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    je.forEach(t.points, vo), je.has(t, "y") && vo(t);
  });
}
function vo(e) {
  e.y = -e.y;
}
function Yb(e) {
  je.forEach(e.nodes(), function(r) {
    go(e.node(r));
  }), je.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    je.forEach(t.points, go), je.has(t, "x") && go(t);
  });
}
function go(e) {
  var r = e.x;
  e.x = e.y, e.y = r;
}
var ze = oe, Qb = Jb;
function Jb(e) {
  var r = {}, t = ze.filter(e.nodes(), function(o) {
    return !e.children(o).length;
  }), n = ze.max(ze.map(t, function(o) {
    return e.node(o).rank;
  })), a = ze.map(ze.range(n + 1), function() {
    return [];
  });
  function i(o) {
    if (!ze.has(r, o)) {
      r[o] = !0;
      var u = e.node(o);
      a[u.rank].push(o), ze.forEach(e.successors(o), i);
    }
  }
  var s = ze.sortBy(t, function(o) {
    return e.node(o).rank;
  });
  return ze.forEach(s, i), a;
}
var er = oe, Xb = Zb;
function Zb(e, r) {
  for (var t = 0, n = 1; n < r.length; ++n)
    t += e_(e, r[n - 1], r[n]);
  return t;
}
function e_(e, r, t) {
  for (var n = er.zipObject(
    t,
    er.map(t, function(l, c) {
      return c;
    })
  ), a = er.flatten(er.map(r, function(l) {
    return er.sortBy(er.map(e.outEdges(l), function(c) {
      return { pos: n[c.w], weight: e.edge(c).weight };
    }), "pos");
  }), !0), i = 1; i < t.length; ) i <<= 1;
  var s = 2 * i - 1;
  i -= 1;
  var o = er.map(new Array(s), function() {
    return 0;
  }), u = 0;
  return er.forEach(a.forEach(function(l) {
    var c = l.pos + i;
    o[c] += l.weight;
    for (var d = 0; c > 0; )
      c % 2 && (d += o[c + 1]), c = c - 1 >> 1, o[c] += l.weight;
    u += l.weight * d;
  })), u;
}
var nh = oe, r_ = t_;
function t_(e, r) {
  return nh.map(r, function(t) {
    var n = e.inEdges(t);
    if (n.length) {
      var a = nh.reduce(n, function(i, s) {
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
var xe = oe, n_ = a_;
function a_(e, r) {
  var t = {};
  xe.forEach(e, function(a, i) {
    var s = t[a.v] = {
      indegree: 0,
      in: [],
      out: [],
      vs: [a.v],
      i
    };
    xe.isUndefined(a.barycenter) || (s.barycenter = a.barycenter, s.weight = a.weight);
  }), xe.forEach(r.edges(), function(a) {
    var i = t[a.v], s = t[a.w];
    !xe.isUndefined(i) && !xe.isUndefined(s) && (s.indegree++, i.out.push(t[a.w]));
  });
  var n = xe.filter(t, function(a) {
    return !a.indegree;
  });
  return i_(n);
}
function i_(e) {
  var r = [];
  function t(i) {
    return function(s) {
      s.merged || (xe.isUndefined(s.barycenter) || xe.isUndefined(i.barycenter) || s.barycenter >= i.barycenter) && s_(i, s);
    };
  }
  function n(i) {
    return function(s) {
      s.in.push(i), --s.indegree === 0 && e.push(s);
    };
  }
  for (; e.length; ) {
    var a = e.pop();
    r.push(a), xe.forEach(a.in.reverse(), t(a)), xe.forEach(a.out, n(a));
  }
  return xe.map(
    xe.filter(r, function(i) {
      return !i.merged;
    }),
    function(i) {
      return xe.pick(i, ["vs", "i", "barycenter", "weight"]);
    }
  );
}
function s_(e, r) {
  var t = 0, n = 0;
  e.weight && (t += e.barycenter * e.weight, n += e.weight), r.weight && (t += r.barycenter * r.weight, n += r.weight), e.vs = r.vs.concat(e.vs), e.barycenter = t / n, e.weight = n, e.i = Math.min(r.i, e.i), r.merged = !0;
}
var Ar = oe, o_ = Se, u_ = c_;
function c_(e, r) {
  var t = o_.partition(e, function(c) {
    return Ar.has(c, "barycenter");
  }), n = t.lhs, a = Ar.sortBy(t.rhs, function(c) {
    return -c.i;
  }), i = [], s = 0, o = 0, u = 0;
  n.sort(l_(!!r)), u = ah(i, a, u), Ar.forEach(n, function(c) {
    u += c.vs.length, i.push(c.vs), s += c.barycenter * c.weight, o += c.weight, u = ah(i, a, u);
  });
  var l = { vs: Ar.flatten(i, !0) };
  return o && (l.barycenter = s / o, l.weight = o), l;
}
function ah(e, r, t) {
  for (var n; r.length && (n = Ar.last(r)).i <= t; )
    r.pop(), e.push(n.vs), t++;
  return t;
}
function l_(e) {
  return function(r, t) {
    return r.barycenter < t.barycenter ? -1 : r.barycenter > t.barycenter ? 1 : e ? t.i - r.i : r.i - t.i;
  };
}
var tr = oe, d_ = r_, f_ = n_, h_ = u_, p_ = jp;
function jp(e, r, t, n) {
  var a = e.children(r), i = e.node(r), s = i ? i.borderLeft : void 0, o = i ? i.borderRight : void 0, u = {};
  s && (a = tr.filter(a, function(g) {
    return g !== s && g !== o;
  }));
  var l = d_(e, a);
  tr.forEach(l, function(g) {
    if (e.children(g.v).length) {
      var f = jp(e, g.v, t, n);
      u[g.v] = f, tr.has(f, "barycenter") && g_(g, f);
    }
  });
  var c = f_(l, t);
  v_(c, u);
  var d = h_(c, n);
  if (s && (d.vs = tr.flatten([s, d.vs, o], !0), e.predecessors(s).length)) {
    var h = e.node(e.predecessors(s)[0]), p = e.node(e.predecessors(o)[0]);
    tr.has(d, "barycenter") || (d.barycenter = 0, d.weight = 0), d.barycenter = (d.barycenter * d.weight + h.order + p.order) / (d.weight + 2), d.weight += 2;
  }
  return d;
}
function v_(e, r) {
  tr.forEach(e, function(t) {
    t.vs = tr.flatten(t.vs.map(function(n) {
      return r[n] ? r[n].vs : n;
    }), !0);
  });
}
function g_(e, r) {
  tr.isUndefined(e.barycenter) ? (e.barycenter = r.barycenter, e.weight = r.weight) : (e.barycenter = (e.barycenter * e.weight + r.barycenter * r.weight) / (e.weight + r.weight), e.weight += r.weight);
}
var Or = oe, y_ = De.Graph, m_ = b_;
function b_(e, r, t) {
  var n = __(e), a = new y_({ compound: !0 }).setGraph({ root: n }).setDefaultNodeLabel(function(i) {
    return e.node(i);
  });
  return Or.forEach(e.nodes(), function(i) {
    var s = e.node(i), o = e.parent(i);
    (s.rank === r || s.minRank <= r && r <= s.maxRank) && (a.setNode(i), a.setParent(i, o || n), Or.forEach(e[t](i), function(u) {
      var l = u.v === i ? u.w : u.v, c = a.edge(l, i), d = Or.isUndefined(c) ? 0 : c.weight;
      a.setEdge(l, i, { weight: e.edge(u).weight + d });
    }), Or.has(s, "minRank") && a.setNode(i, {
      borderLeft: s.borderLeft[r],
      borderRight: s.borderRight[r]
    }));
  }), a;
}
function __(e) {
  for (var r; e.hasNode(r = Or.uniqueId("_root")); ) ;
  return r;
}
var w_ = oe, I_ = q_;
function q_(e, r, t) {
  var n = {}, a;
  w_.forEach(t, function(i) {
    for (var s = e.parent(i), o, u; s; ) {
      if (o = e.parent(s), o ? (u = n[o], n[o] = s) : (u = a, a = s), u && u !== s) {
        r.setEdge(u, s);
        return;
      }
      s = o;
    }
  });
}
var nr = oe, E_ = Qb, C_ = Xb, S_ = p_, T_ = m_, R_ = I_, x_ = De.Graph, ih = Se, k_ = A_;
function A_(e) {
  var r = ih.maxRank(e), t = sh(e, nr.range(1, r + 1), "inEdges"), n = sh(e, nr.range(r - 1, -1, -1), "outEdges"), a = E_(e);
  oh(e, a);
  for (var i = Number.POSITIVE_INFINITY, s, o = 0, u = 0; u < 4; ++o, ++u) {
    O_(o % 2 ? t : n, o % 4 >= 2), a = ih.buildLayerMatrix(e);
    var l = C_(e, a);
    l < i && (u = 0, s = nr.cloneDeep(a), i = l);
  }
  oh(e, s);
}
function sh(e, r, t) {
  return nr.map(r, function(n) {
    return T_(e, n, t);
  });
}
function O_(e, r) {
  var t = new x_();
  nr.forEach(e, function(n) {
    var a = n.graph().root, i = S_(n, a, t, r);
    nr.forEach(i.vs, function(s, o) {
      n.node(s).order = o;
    }), R_(n, t, i.vs);
  });
}
function oh(e, r) {
  nr.forEach(r, function(t) {
    nr.forEach(t, function(n, a) {
      e.node(n).order = a;
    });
  });
}
var U = oe, P_ = De.Graph, N_ = Se, D_ = {
  positionX: H_
};
function M_(e, r) {
  var t = {};
  function n(a, i) {
    var s = 0, o = 0, u = a.length, l = U.last(i);
    return U.forEach(i, function(c, d) {
      var h = F_(e, c), p = h ? e.node(h).order : u;
      (h || c === l) && (U.forEach(i.slice(o, d + 1), function(g) {
        U.forEach(e.predecessors(g), function(f) {
          var y = e.node(f), I = y.order;
          (I < s || p < I) && !(y.dummy && e.node(g).dummy) && $p(t, f, g);
        });
      }), o = d + 1, s = p);
    }), i;
  }
  return U.reduce(r, n), t;
}
function L_(e, r) {
  var t = {};
  function n(i, s, o, u, l) {
    var c;
    U.forEach(U.range(s, o), function(d) {
      c = i[d], e.node(c).dummy && U.forEach(e.predecessors(c), function(h) {
        var p = e.node(h);
        p.dummy && (p.order < u || p.order > l) && $p(t, h, c);
      });
    });
  }
  function a(i, s) {
    var o = -1, u, l = 0;
    return U.forEach(s, function(c, d) {
      if (e.node(c).dummy === "border") {
        var h = e.predecessors(c);
        h.length && (u = e.node(h[0]).order, n(s, l, d, o, u), l = d, o = u);
      }
      n(s, l, s.length, u, i.length);
    }), s;
  }
  return U.reduce(r, a), t;
}
function F_(e, r) {
  if (e.node(r).dummy)
    return U.find(e.predecessors(r), function(t) {
      return e.node(t).dummy;
    });
}
function $p(e, r, t) {
  if (r > t) {
    var n = r;
    r = t, t = n;
  }
  var a = e[r];
  a || (e[r] = a = {}), a[t] = !0;
}
function j_(e, r, t) {
  if (r > t) {
    var n = r;
    r = t, t = n;
  }
  return U.has(e[r], t);
}
function $_(e, r, t, n) {
  var a = {}, i = {}, s = {};
  return U.forEach(r, function(o) {
    U.forEach(o, function(u, l) {
      a[u] = u, i[u] = u, s[u] = l;
    });
  }), U.forEach(r, function(o) {
    var u = -1;
    U.forEach(o, function(l) {
      var c = n(l);
      if (c.length) {
        c = U.sortBy(c, function(f) {
          return s[f];
        });
        for (var d = (c.length - 1) / 2, h = Math.floor(d), p = Math.ceil(d); h <= p; ++h) {
          var g = c[h];
          i[l] === l && u < s[g] && !j_(t, l, g) && (i[g] = l, i[l] = a[l] = a[g], u = s[g]);
        }
      }
    });
  }), { root: a, align: i };
}
function G_(e, r, t, n, a) {
  var i = {}, s = z_(e, r, t, a), o = a ? "borderLeft" : "borderRight";
  function u(d, h) {
    for (var p = s.nodes(), g = p.pop(), f = {}; g; )
      f[g] ? d(g) : (f[g] = !0, p.push(g), p = p.concat(h(g))), g = p.pop();
  }
  function l(d) {
    i[d] = s.inEdges(d).reduce(function(h, p) {
      return Math.max(h, i[p.v] + s.edge(p));
    }, 0);
  }
  function c(d) {
    var h = s.outEdges(d).reduce(function(g, f) {
      return Math.min(g, i[f.w] - s.edge(f));
    }, Number.POSITIVE_INFINITY), p = e.node(d);
    h !== Number.POSITIVE_INFINITY && p.borderType !== o && (i[d] = Math.max(i[d], h));
  }
  return u(l, s.predecessors.bind(s)), u(c, s.successors.bind(s)), U.forEach(n, function(d) {
    i[d] = i[t[d]];
  }), i;
}
function z_(e, r, t, n) {
  var a = new P_(), i = e.graph(), s = V_(i.nodesep, i.edgesep, n);
  return U.forEach(r, function(o) {
    var u;
    U.forEach(o, function(l) {
      var c = t[l];
      if (a.setNode(c), u) {
        var d = t[u], h = a.edge(d, c);
        a.setEdge(d, c, Math.max(s(e, l, u), h || 0));
      }
      u = l;
    });
  }), a;
}
function B_(e, r) {
  return U.minBy(U.values(r), function(t) {
    var n = Number.NEGATIVE_INFINITY, a = Number.POSITIVE_INFINITY;
    return U.forIn(t, function(i, s) {
      var o = W_(e, s) / 2;
      n = Math.max(i + o, n), a = Math.min(i - o, a);
    }), n - a;
  });
}
function U_(e, r) {
  var t = U.values(r), n = U.min(t), a = U.max(t);
  U.forEach(["u", "d"], function(i) {
    U.forEach(["l", "r"], function(s) {
      var o = i + s, u = e[o], l;
      if (u !== r) {
        var c = U.values(u);
        l = s === "l" ? n - U.min(c) : a - U.max(c), l && (e[o] = U.mapValues(u, function(d) {
          return d + l;
        }));
      }
    });
  });
}
function K_(e, r) {
  return U.mapValues(e.ul, function(t, n) {
    if (r)
      return e[r.toLowerCase()][n];
    var a = U.sortBy(U.map(e, n));
    return (a[1] + a[2]) / 2;
  });
}
function H_(e) {
  var r = N_.buildLayerMatrix(e), t = U.merge(
    M_(e, r),
    L_(e, r)
  ), n = {}, a;
  U.forEach(["u", "d"], function(s) {
    a = s === "u" ? r : U.values(r).reverse(), U.forEach(["l", "r"], function(o) {
      o === "r" && (a = U.map(a, function(d) {
        return U.values(d).reverse();
      }));
      var u = (s === "u" ? e.predecessors : e.successors).bind(e), l = $_(e, a, t, u), c = G_(
        e,
        a,
        l.root,
        l.align,
        o === "r"
      );
      o === "r" && (c = U.mapValues(c, function(d) {
        return -d;
      })), n[s + o] = c;
    });
  });
  var i = B_(e, n);
  return U_(n, i), K_(n, e.graph().align);
}
function V_(e, r, t) {
  return function(n, a, i) {
    var s = n.node(a), o = n.node(i), u = 0, l;
    if (u += s.width / 2, U.has(s, "labelpos"))
      switch (s.labelpos.toLowerCase()) {
        case "l":
          l = -s.width / 2;
          break;
        case "r":
          l = s.width / 2;
          break;
      }
    if (l && (u += t ? l : -l), l = 0, u += (s.dummy ? r : e) / 2, u += (o.dummy ? r : e) / 2, u += o.width / 2, U.has(o, "labelpos"))
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
function W_(e, r) {
  return e.node(r).width;
}
var Pr = oe, Gp = Se, Y_ = D_.positionX, Q_ = J_;
function J_(e) {
  e = Gp.asNonCompoundGraph(e), X_(e), Pr.forEach(Y_(e), function(r, t) {
    e.node(t).x = r;
  });
}
function X_(e) {
  var r = Gp.buildLayerMatrix(e), t = e.graph().ranksep, n = 0;
  Pr.forEach(r, function(a) {
    var i = Pr.max(Pr.map(a, function(s) {
      return e.node(s).height;
    }));
    Pr.forEach(a, function(s) {
      e.node(s).y = n + i / 2;
    }), n += i + t;
  });
}
var H = oe, uh = zm, ch = ib, Z_ = xb, ew = Se.normalizeRanks, rw = Pb, tw = Se.removeEmptyRanks, lh = Lb, nw = Bb, dh = Kb, aw = k_, iw = Q_, ar = Se, sw = De.Graph, ow = uw;
function uw(e, r) {
  var t = r && r.debugTiming ? ar.time : ar.notime;
  t("layout", function() {
    var n = t("  buildLayoutGraph", function() {
      return bw(e);
    });
    t("  runLayout", function() {
      cw(n, t);
    }), t("  updateInputGraph", function() {
      lw(e, n);
    });
  });
}
function cw(e, r) {
  r("    makeSpaceForEdgeLabels", function() {
    _w(e);
  }), r("    removeSelfEdges", function() {
    xw(e);
  }), r("    acyclic", function() {
    uh.run(e);
  }), r("    nestingGraph.run", function() {
    lh.run(e);
  }), r("    rank", function() {
    Z_(ar.asNonCompoundGraph(e));
  }), r("    injectEdgeLabelProxies", function() {
    ww(e);
  }), r("    removeEmptyRanks", function() {
    tw(e);
  }), r("    nestingGraph.cleanup", function() {
    lh.cleanup(e);
  }), r("    normalizeRanks", function() {
    ew(e);
  }), r("    assignRankMinMax", function() {
    Iw(e);
  }), r("    removeEdgeLabelProxies", function() {
    qw(e);
  }), r("    normalize.run", function() {
    ch.run(e);
  }), r("    parentDummyChains", function() {
    rw(e);
  }), r("    addBorderSegments", function() {
    nw(e);
  }), r("    order", function() {
    aw(e);
  }), r("    insertSelfEdges", function() {
    kw(e);
  }), r("    adjustCoordinateSystem", function() {
    dh.adjust(e);
  }), r("    position", function() {
    iw(e);
  }), r("    positionSelfEdges", function() {
    Aw(e);
  }), r("    removeBorderNodes", function() {
    Rw(e);
  }), r("    normalize.undo", function() {
    ch.undo(e);
  }), r("    fixupEdgeLabelCoords", function() {
    Sw(e);
  }), r("    undoCoordinateSystem", function() {
    dh.undo(e);
  }), r("    translateGraph", function() {
    Ew(e);
  }), r("    assignNodeIntersects", function() {
    Cw(e);
  }), r("    reversePoints", function() {
    Tw(e);
  }), r("    acyclic.undo", function() {
    uh.undo(e);
  });
}
function lw(e, r) {
  H.forEach(e.nodes(), function(t) {
    var n = e.node(t), a = r.node(t);
    n && (n.x = a.x, n.y = a.y, r.children(t).length && (n.width = a.width, n.height = a.height));
  }), H.forEach(e.edges(), function(t) {
    var n = e.edge(t), a = r.edge(t);
    n.points = a.points, H.has(a, "x") && (n.x = a.x, n.y = a.y);
  }), e.graph().width = r.graph().width, e.graph().height = r.graph().height;
}
var dw = ["nodesep", "edgesep", "ranksep", "marginx", "marginy"], fw = { ranksep: 50, edgesep: 20, nodesep: 50, rankdir: "tb" }, hw = ["acyclicer", "ranker", "rankdir", "align"], pw = ["width", "height"], vw = { width: 0, height: 0 }, gw = ["minlen", "weight", "width", "height", "labeloffset"], yw = {
  minlen: 1,
  weight: 1,
  width: 0,
  height: 0,
  labeloffset: 10,
  labelpos: "r"
}, mw = ["labelpos"];
function bw(e) {
  var r = new sw({ multigraph: !0, compound: !0 }), t = mo(e.graph());
  return r.setGraph(H.merge(
    {},
    fw,
    yo(t, dw),
    H.pick(t, hw)
  )), H.forEach(e.nodes(), function(n) {
    var a = mo(e.node(n));
    r.setNode(n, H.defaults(yo(a, pw), vw)), r.setParent(n, e.parent(n));
  }), H.forEach(e.edges(), function(n) {
    var a = mo(e.edge(n));
    r.setEdge(n, H.merge(
      {},
      yw,
      yo(a, gw),
      H.pick(a, mw)
    ));
  }), r;
}
function _w(e) {
  var r = e.graph();
  r.ranksep /= 2, H.forEach(e.edges(), function(t) {
    var n = e.edge(t);
    n.minlen *= 2, n.labelpos.toLowerCase() !== "c" && (r.rankdir === "TB" || r.rankdir === "BT" ? n.width += n.labeloffset : n.height += n.labeloffset);
  });
}
function ww(e) {
  H.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (t.width && t.height) {
      var n = e.node(r.v), a = e.node(r.w), i = { rank: (a.rank - n.rank) / 2 + n.rank, e: r };
      ar.addDummyNode(e, "edge-proxy", i, "_ep");
    }
  });
}
function Iw(e) {
  var r = 0;
  H.forEach(e.nodes(), function(t) {
    var n = e.node(t);
    n.borderTop && (n.minRank = e.node(n.borderTop).rank, n.maxRank = e.node(n.borderBottom).rank, r = H.max(r, n.maxRank));
  }), e.graph().maxRank = r;
}
function qw(e) {
  H.forEach(e.nodes(), function(r) {
    var t = e.node(r);
    t.dummy === "edge-proxy" && (e.edge(t.e).labelRank = t.rank, e.removeNode(r));
  });
}
function Ew(e) {
  var r = Number.POSITIVE_INFINITY, t = 0, n = Number.POSITIVE_INFINITY, a = 0, i = e.graph(), s = i.marginx || 0, o = i.marginy || 0;
  function u(l) {
    var c = l.x, d = l.y, h = l.width, p = l.height;
    r = Math.min(r, c - h / 2), t = Math.max(t, c + h / 2), n = Math.min(n, d - p / 2), a = Math.max(a, d + p / 2);
  }
  H.forEach(e.nodes(), function(l) {
    u(e.node(l));
  }), H.forEach(e.edges(), function(l) {
    var c = e.edge(l);
    H.has(c, "x") && u(c);
  }), r -= s, n -= o, H.forEach(e.nodes(), function(l) {
    var c = e.node(l);
    c.x -= r, c.y -= n;
  }), H.forEach(e.edges(), function(l) {
    var c = e.edge(l);
    H.forEach(c.points, function(d) {
      d.x -= r, d.y -= n;
    }), H.has(c, "x") && (c.x -= r), H.has(c, "y") && (c.y -= n);
  }), i.width = t - r + s, i.height = a - n + o;
}
function Cw(e) {
  H.forEach(e.edges(), function(r) {
    var t = e.edge(r), n = e.node(r.v), a = e.node(r.w), i, s;
    t.points ? (i = t.points[0], s = t.points[t.points.length - 1]) : (t.points = [], i = a, s = n), t.points.unshift(ar.intersectRect(n, i)), t.points.push(ar.intersectRect(a, s));
  });
}
function Sw(e) {
  H.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (H.has(t, "x"))
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
function Tw(e) {
  H.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    t.reversed && t.points.reverse();
  });
}
function Rw(e) {
  H.forEach(e.nodes(), function(r) {
    if (e.children(r).length) {
      var t = e.node(r), n = e.node(t.borderTop), a = e.node(t.borderBottom), i = e.node(H.last(t.borderLeft)), s = e.node(H.last(t.borderRight));
      t.width = Math.abs(s.x - i.x), t.height = Math.abs(a.y - n.y), t.x = i.x + t.width / 2, t.y = n.y + t.height / 2;
    }
  }), H.forEach(e.nodes(), function(r) {
    e.node(r).dummy === "border" && e.removeNode(r);
  });
}
function xw(e) {
  H.forEach(e.edges(), function(r) {
    if (r.v === r.w) {
      var t = e.node(r.v);
      t.selfEdges || (t.selfEdges = []), t.selfEdges.push({ e: r, label: e.edge(r) }), e.removeEdge(r);
    }
  });
}
function kw(e) {
  var r = ar.buildLayerMatrix(e);
  H.forEach(r, function(t) {
    var n = 0;
    H.forEach(t, function(a, i) {
      var s = e.node(a);
      s.order = i + n, H.forEach(s.selfEdges, function(o) {
        ar.addDummyNode(e, "selfedge", {
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
function Aw(e) {
  H.forEach(e.nodes(), function(r) {
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
function yo(e, r) {
  return H.mapValues(H.pick(e, r), Number);
}
function mo(e) {
  var r = {};
  return H.forEach(e, function(t, n) {
    r[n.toLowerCase()] = t;
  }), r;
}
var Wr = oe, Ow = Se, Pw = De.Graph, Nw = {
  debugOrdering: Dw
};
function Dw(e) {
  var r = Ow.buildLayerMatrix(e), t = new Pw({ compound: !0, multigraph: !0 }).setGraph({});
  return Wr.forEach(e.nodes(), function(n) {
    t.setNode(n, { label: n }), t.setParent(n, "layer" + e.node(n).rank);
  }), Wr.forEach(e.edges(), function(n) {
    t.setEdge(n.v, n.w, {}, n.name);
  }), Wr.forEach(r, function(n, a) {
    var i = "layer" + a;
    t.setNode(i, { rank: "same" }), Wr.reduce(n, function(s, o) {
      return t.setEdge(s, o, { style: "invis" }), o;
    });
  }), t;
}
var Mw = "0.8.5", Lw = {
  graphlib: De,
  layout: ow,
  debug: Nw,
  util: {
    time: Se.time,
    notime: Se.notime
  },
  version: Mw
};
const fh = /* @__PURE__ */ Gv(Lw), Dr = 148, Mr = 46, Jo = 80, Xo = 44;
function Fw(e) {
  if (e.nodes.length === 0) return e;
  const r = new fh.graphlib.Graph();
  r.setDefaultEdgeLabel(() => ({})), r.setGraph({ rankdir: "LR", nodesep: 60, ranksep: 120, marginx: 40, marginy: 40 });
  for (const n of e.nodes) r.setNode(n.id, { width: Dr, height: Mr });
  for (const n of e.edges) r.setEdge(n.source, n.target);
  fh.layout(r);
  const t = {};
  for (const n of e.nodes) {
    const a = r.node(n.id);
    t[n.id] = a ? { x: Math.round(a.x - Dr / 2), y: Math.round(a.y - Mr / 2) } : { x: n.x, y: n.y };
  }
  return {
    ...e,
    nodes: e.nodes.map((n) => ({ ...n, ...t[n.id] })),
    layout: { ...e.layout, positions: t }
  };
}
const zp = (e, r) => Math.abs(e.x - r.x) < Dr + Jo && Math.abs(e.y - r.y) < Mr + Xo;
function jw(e) {
  const r = [], t = e.nodes.map((a) => {
    let i = Number.isFinite(a.x) ? a.x : 0, s = Number.isFinite(a.y) ? a.y : 0, o = 0;
    for (; r.some((u) => zp({ x: i, y: s }, u)) && o++ < 200; )
      i += Dr + Jo, i > 2400 && (i = 80, s += Mr + Xo);
    return r.push({ x: i, y: s }), { ...a, x: i, y: s };
  });
  if (t.every((a, i) => a.x === e.nodes[i].x && a.y === e.nodes[i].y)) return e;
  const n = Object.fromEntries(t.map((a) => [a.id, { x: a.x, y: a.y }]));
  return { ...e, nodes: t, layout: { ...e.layout, positions: n } };
}
function hh(e, r, t) {
  let n = { x: Math.round(r), y: Math.round(t) }, a = 0;
  for (; e.nodes.some((i) => zp(n, i)) && a++ < 200; )
    n = { x: n.x + Dr + Jo, y: n.y }, n.x > 2400 && (n = { x: 80, y: n.y + Mr + Xo });
  return n;
}
const { Header: $w, Content: Gw } = Kp;
function zw({ ctx: e }) {
  const [r, t] = Hp.useMessage(), [n, a] = q([]), [i, s] = q([]), [o, u] = q([]), [l, c] = q([]), [d, h] = q(""), [p, g] = q(1), [f, y] = q(!0), [I, S] = q([]), [_, A] = q(() => Yr()), [k, m] = q(null), [C, O] = q([]), [L, N] = q(!1), [w, F] = q(!0), [j, Y] = q(!1), [X, ae] = q(!1), [P, G] = q(0), Z = be([]), de = be([]), D = be(null), R = be(null), [ue, qr] = q(null), [Ae, Me] = q(!1), [sr, pr] = q(!1), vr = be(1), zr = be(e);
  zr.current = e;
  const Er = be(_.trigger);
  le(() => {
    Er.current = _.trigger;
  }, [_.trigger]), le(() => {
    let E = !0;
    const M = zr.current.hubQuery;
    return Promise.allSettled([
      M("industrial/nodes"),
      M("industrial/workflows"),
      M("industrial/resources")
    ]).then(([ce, ee, re]) => {
      var Le;
      if (!E) return;
      const te = [];
      if (ce.status === "fulfilled" ? a(wh(ce.value)) : te.push(`节点描述: ${String(ce.reason)}`), ee.status === "fulfilled") {
        const ge = Eh(ee.value);
        s(ge);
        const ve = new URLSearchParams(window.location.search).get("workflowId"), Re = ve && ge.some((Fe) => Fe.workflowId === ve) ? ve : (Le = ge[0]) == null ? void 0 : Le.workflowId;
        Re && ne(Re);
      } else te.push(`工作流: ${String(ee.reason)}`);
      if (re.status === "fulfilled") {
        const ge = Ih(re.value);
        c(ge);
        const ve = ge.map((Re) => Re.id);
        u(ve), h((Re) => Re || ve[0] || "");
      } else te.push(`资源: ${String(re.reason)}`);
      S(te);
    }).finally(() => {
      E && y(!1);
    }), () => {
      E = !1;
    };
  }, []);
  const we = tt(
    () => new Map(n.map((E) => [E.nodeTypeId, E])),
    [n]
  ), Cr = _.nodes.find((E) => E.id === k) ?? null, We = z(async (E, M, ce) => {
    N(!0);
    try {
      return await Ee().api.hubCommand(E, M), r.success(ce), !0;
    } catch (ee) {
      return r.error(`${E} failed: ${ee instanceof Error ? ee.message : String(ee)}`), !1;
    } finally {
      N(!1);
    }
  }, [r]), ne = z(async (E) => {
    try {
      const M = await e.hubQuery("industrial/workflow-definitions"), ee = (_e(M) ?? []).find((ge) => {
        const ve = ge;
        return String((ve == null ? void 0 : ve.workflowId) ?? "") === E;
      }), re = ee === void 0 ? { ...Yr(E), name: E } : Tv(ee), te = jw(re);
      A(te), Z.current = [], de.current = [], G((ge) => ge + 1), g(Number(te.layout.lookbackDays ?? 1) || 1);
      const Le = re.nodes.map((ge) => /^n(\d+)$/.exec(ge.id)).filter(Boolean).reduce((ge, ve) => Math.max(ge, Number(ve[1])), 0);
      vr.current = Le + 1, m(null), Y(!1), O([]), ae(!1);
    } catch (M) {
      r.error(`加载工作流失败: ${M instanceof Error ? M.message : String(M)}`);
    }
  }, [e, r]), Ge = z(async () => {
    try {
      const E = Nv(await e.hubQuery("industrial/execution"));
      if (E === null) return;
      qr(E);
    } catch {
    }
  }, [e]), Q = z(async () => {
    var ce;
    const E = new Set(_.resourceReferences ?? []);
    for (const ee of _.nodes)
      for (const re of ["resourceId", "targetResourceId"]) {
        const te = (ce = ee.config) == null ? void 0 : ce[re];
        typeof te == "string" && te.trim() && E.add(te.trim());
      }
    const M = {
      ..._,
      trigger: Er.current,
      resourceReferences: [...E],
      layout: { ..._.layout, lookbackDays: p }
    };
    await We("industrial/save-workflow", M, "已保存") && (A(M), ae(!1));
  }, [We, _, p]), Ye = z(async () => {
    if (X) {
      r.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await We("industrial/publish-workflow", { workflowId: _.workflowId }, "已发布") && await Ge();
  }, [We, _.workflowId, X, r, Ge]), Qe = z(async () => {
    if (X) {
      r.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await We(
      "industrial/run-workflow",
      { workflowId: _.workflowId, resourceId: d },
      "采集已启动"
    ) && await Ge();
  }, [We, _.workflowId, X, r, Ge, d]), pe = z((E) => {
    E !== _ && (Z.current.length >= 50 && Z.current.shift(), Z.current.push(_), de.current = [], G((M) => M + 1), A(E), ae(!0));
  }, [_]), Je = z(() => {
    const E = Z.current.pop();
    E && (de.current.push(_), A(E), ae(!0), G((M) => M + 1));
  }, [_]), Te = z(() => {
    const E = de.current.pop();
    E && (Z.current.push(_), A(E), ae(!0), G((M) => M + 1));
  }, [_]);
  le(() => {
    const E = (M) => {
      const ce = M.target;
      if (ce && /INPUT|TEXTAREA|SELECT/.test(ce.tagName)) return;
      const ee = M.key.toLowerCase();
      if ((M.ctrlKey || M.metaKey) && ee === "z") {
        M.preventDefault(), M.shiftKey ? Te() : Je();
        return;
      }
      if ((M.ctrlKey || M.metaKey) && ee === "y") {
        M.preventDefault(), Te();
        return;
      }
      if (M.key === "Delete" && k) {
        M.preventDefault(), pe(iu(_, k)), m(null);
        return;
      }
      if ((M.ctrlKey || M.metaKey) && ee === "c" && k) {
        R.current = _.nodes.find((re) => re.id === k) ?? null;
        return;
      }
      if ((M.ctrlKey || M.metaKey) && ee === "v" && R.current) {
        M.preventDefault();
        const re = rr(we, R.current.type);
        if (!re) return;
        const te = `n${vr.current++}`, Le = hh(_, R.current.x + 40, R.current.y + 40);
        pe({
          ..._,
          nodes: [..._.nodes, nu(re, te, Le.x, Le.y)]
        }), m(te);
      }
    };
    return window.addEventListener("keydown", E), () => window.removeEventListener("keydown", E);
  }, [we, pe, _, Te, k, Je]);
  const me = z(() => {
    pe(Fw(_)), requestAnimationFrame(() => {
      var E, M;
      return (M = (E = D.current) == null ? void 0 : E.fitView) == null ? void 0 : M.call(E, { when: "always" });
    });
  }, [pe, _]), Xe = z((E, M, ce) => {
    const ee = rr(we, E);
    if (!ee) return;
    const re = `n${vr.current++}`, te = hh(_, M, ce);
    pe({ ..._, nodes: [..._.nodes, nu(ee, re, te.x, te.y)] }), m(re), Y(!0), O([]);
  }, [we, pe, _]), gr = z((E) => {
    const M = vr.current - 1;
    Xe(E, 80 + M % 3 * 190, 80 + Math.floor(M / 3) * 130);
  }, [Xe]), Br = z((E, M) => {
    const ce = _.nodes.find((Fe) => Fe.id === E), ee = _.nodes.find((Fe) => Fe.id === M);
    if (!ce || !ee)
      return r.warning("连线失败：端点不在画布上"), null;
    const re = rr(we, ce.type), te = rr(we, ee.type);
    if (!re || !te) return null;
    let Le = ce, ge = ee, ve = au(re, te);
    if (!ve) {
      const Fe = au(te, re);
      if (!Fe)
        return r.warning(`端口类型不兼容：${re.name} → ${te.name}`), null;
      Le = ee, ge = ce, ve = Fe;
    }
    const Re = av(_, {
      source: Le.id,
      sourcePort: ve.sourcePort,
      target: ge.id,
      targetPort: ve.targetPort
    }, we);
    return Re.issue ? (r.warning(`${Re.issue.code}: ${Re.issue.message}`), O((Fe) => [...Fe, Re.issue]), null) : Re.definition === _ ? (r.info("这条连线已经存在"), null) : (pe(Re.definition), ve);
  }, [we, pe, _, r]), Ur = z(() => {
    const E = lv(_, we, new Set(o));
    O(E), E.length === 0 ? r.success("本地校验通过（发布时服务端仍会复核）") : (r.warning(`发现 ${E.length} 个问题，见属性抽屉定位列表`), Y(!0));
  }, [_, we, o, r]), Ze = z((E, M) => {
    pe({
      ..._,
      edges: _.edges.map((ce, ee) => ee === E ? M : ce)
    });
  }, [pe, _]);
  return f ? /* @__PURE__ */ v(gh, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ T("div", { className: "industrial-designer", children: [
    t,
    /* @__PURE__ */ T($w, { className: "industrial-header", children: [
      /* @__PURE__ */ T("div", { className: "industrial-title", children: [
        /* @__PURE__ */ v($.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ v($.Text, { type: "secondary", children: _.name || _.workflowId }),
        X && /* @__PURE__ */ v(ke, { color: "orange", children: "未保存" }),
        ue && /* @__PURE__ */ v(ke, { color: ue.error ? "error" : "success", children: ue.status })
      ] }),
      /* @__PURE__ */ T("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ v(
          B,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": w,
            onClick: () => F((E) => !E),
            children: w ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ v(
          B,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => Y((E) => !E),
            children: "属性"
          }
        ),
        /* @__PURE__ */ v(B, { size: "small", "data-testid": "open-trigger", onClick: () => Me(!0), children: "触发器" }),
        /* @__PURE__ */ v(
          B,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => pr(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ v(
          B,
          {
            size: "small",
            "data-testid": "designer-undo",
            disabled: Z.current.length === 0,
            onClick: Je,
            children: "撤销"
          }
        ),
        /* @__PURE__ */ v(
          B,
          {
            size: "small",
            "data-testid": "designer-redo",
            disabled: de.current.length === 0,
            onClick: Te,
            children: "重做"
          }
        ),
        /* @__PURE__ */ v(B, { size: "small", "data-testid": "designer-auto-layout", onClick: me, children: "自动布局" }),
        /* @__PURE__ */ v(B, { size: "small", "data-testid": "designer-fit", onClick: () => {
          var E, M;
          return (M = (E = D.current) == null ? void 0 : E.fitView) == null ? void 0 : M.call(E, { when: "always" });
        }, children: "适应画布" }),
        /* @__PURE__ */ v(B, { size: "small", "data-testid": "designer-center", onClick: () => {
          var E, M;
          return (M = (E = D.current) == null ? void 0 : E.fitCenter) == null ? void 0 : M.call(E);
        }, children: "居中" }),
        /* @__PURE__ */ v("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ T(K, { size: 4, children: [
          /* @__PURE__ */ v(B, { size: "small", loading: L, onClick: () => void Q(), children: "保存" }),
          /* @__PURE__ */ v(B, { size: "small", onClick: Ur, children: "校验" }),
          /* @__PURE__ */ v(B, { size: "small", loading: L, onClick: () => void Ye(), children: "发布" }),
          /* @__PURE__ */ v(B, { size: "small", type: "primary", loading: L, onClick: () => void Qe(), children: "运行" })
        ] }),
        /* @__PURE__ */ T(
          "select",
          {
            className: "industrial-resource-select",
            "aria-label": "运行资源",
            value: d,
            onChange: (E) => h(E.target.value),
            children: [
              /* @__PURE__ */ v("option", { value: "", children: "运行资源" }),
              o.map((E) => /* @__PURE__ */ v("option", { value: E, children: E }, E))
            ]
          }
        ),
        /* @__PURE__ */ v(
          xo,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: p,
            onChange: (E) => {
              g(Number(E) || 1), ae(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ T(Gw, { className: `industrial-body${w ? "" : " industrial-body--collapsed"}`, children: [
      w ? /* @__PURE__ */ T("aside", { className: "industrial-palette", children: [
        i.length > 0 && /* @__PURE__ */ v(
          ye,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: _.workflowId,
            placeholder: "选择工作流",
            onChange: (E) => void ne(E),
            options: i.map((E) => ({ value: E.workflowId, label: E.name }))
          }
        ),
        /* @__PURE__ */ v(rv, { descriptors: n, loading: !1, onAdd: gr })
      ] }) : /* @__PURE__ */ v("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ v(B, { type: "text", size: "small", onClick: () => F(!0), children: "节点库" }) }),
      /* @__PURE__ */ T("section", { className: "industrial-center", children: [
        I.length > 0 && /* @__PURE__ */ v(
          Ce,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: I.join("；")
          }
        ),
        /* @__PURE__ */ v(
          ev,
          {
            definition: _,
            catalog: we,
            selectedId: k,
            onSelect: (E) => {
              m(E), E && Y(!0);
            },
            onDropNode: Xe,
            onCreateEdge: Br,
            onPositions: (E) => pe(uv(_, E)),
            onSelectIds: (E) => {
              E.length && m(E[0]);
            },
            onGraphReady: (E) => {
              D.current = E;
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ T(
      Up,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: j,
        mask: !1,
        getContainer: !1,
        onClose: () => Y(!1),
        children: [
          ue && /* @__PURE__ */ v(
            Ce,
            {
              style: { marginBottom: 12 },
              type: ue.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${ue.status}`,
              description: /* @__PURE__ */ T(K, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                ue.error && /* @__PURE__ */ v("span", { children: ue.error }),
                ue.executed.length > 0 && /* @__PURE__ */ T("span", { children: [
                  "已执行节点: ",
                  ue.executed.join(", ")
                ] }),
                ue.nodes.length > 0 && /* @__PURE__ */ v(
                  qe,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: ue.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (E, M) => M.error ?? (M.samples.length > 0 ? M.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ v(
            mv,
            {
              node: Cr,
              descriptor: Cr ? rr(we, Cr.type) ?? null : null,
              nodes: _.nodes,
              edges: _.edges,
              catalog: we,
              resources: l,
              issues: C,
              onApplyConfig: (E) => {
                k && pe(ov(_, k, E));
              },
              onDeleteNode: () => {
                pe(iu(_, k ?? "")), m(null);
              },
              onDeleteEdge: (E) => pe(iv(_, E)),
              onCreateEdge: (E, M) => void Br(E, M),
              onUpdateEdge: Ze,
              onSelectNode: (E) => {
                m(E), Y(!0);
              }
            }
          )
        ]
      }
    ),
    Ae && /* @__PURE__ */ v(
      Lv,
      {
        open: Ae,
        onClose: () => Me(!1),
        trigger: _.trigger,
        resourceIds: o,
        workflowId: _.workflowId,
        ctx: e,
        onSave: (E) => {
          Er.current = E, pe({ ..._, trigger: E });
        }
      }
    ),
    sr && /* @__PURE__ */ v(
      $v,
      {
        open: sr,
        onClose: () => pr(!1),
        ctx: e,
        workflowId: _.workflowId,
        resourceId: d,
        lookbackDays: p
      }
    )
  ] });
}
function Bw({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q(!1), [s, o] = q(""), [u, l] = q(null), [c, d] = q(!1), h = z(async () => {
    try {
      n(Eh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
    } catch (f) {
      l(f instanceof Error ? f.message : String(f));
    }
  }, [e == null ? void 0 : e.query, r]);
  le(() => {
    h();
  }, [h]);
  const p = z(async () => {
    var y;
    const f = s.trim();
    if (!f) {
      l("请输入工作流名称");
      return;
    }
    d(!0), l(null);
    try {
      const S = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await r.hubCommand("industrial/save-workflow", {
        ...Yr(S),
        name: f,
        layout: { lookbackDays: 1 }
      }), i(!1), o(""), await h(), (y = r.navigate) == null || y.call(r, `/industrial/designer?workflowId=${encodeURIComponent(S)}`);
    } catch (I) {
      l(I instanceof Error ? I.message : String(I));
    } finally {
      d(!1);
    }
  }, [r, h, s]), g = z((f) => {
    var y;
    (y = r.navigate) == null || y.call(r, `/industrial/designer?workflowId=${encodeURIComponent(f)}`);
  }, [r]);
  return /* @__PURE__ */ T(K, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ T(K, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ v($.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ v(B, { type: "primary", onClick: () => i(!0), children: "新建工作流" })
    ] }),
    u && /* @__PURE__ */ v($.Text, { type: "danger", children: u }),
    /* @__PURE__ */ v(
      qe,
      {
        rowKey: "workflowId",
        dataSource: t,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (f) => /* @__PURE__ */ v(B, { type: "link", style: { padding: 0 }, onClick: () => g(f), children: f })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ v(
      ur,
      {
        open: a,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: c,
        onCancel: () => i(!1),
        onOk: () => void p(),
        children: /* @__PURE__ */ T(K, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ v($.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: s,
              onChange: (f) => o(f.target.value ?? "")
            }
          ),
          u && /* @__PURE__ */ v($.Text, { type: "danger", children: u })
        ] })
      }
    )
  ] });
}
const bo = {
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
}, Uw = [
  { value: "filesystem.local", label: "本地目录" },
  { value: "filesystem.unc", label: "Windows UNC 共享" },
  { value: "sqlserver", label: "SQL Server" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mysql", label: "MySQL" },
  { value: "sqlite", label: "SQLite" }
];
function Kw(e) {
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
function Hw({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q([]), [u, l] = q(bo), [c, d] = q(!1), [h, p] = q(null), [g, f] = q(!1), y = z(async () => {
    try {
      const [m, C, O] = await Promise.all([
        r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        r.hubQuery("industrial/credentials"),
        r.hubQuery("industrial/resource-tests")
      ]);
      n(Rv(m)), i(Ch(C)), o(xv(O));
    } catch (m) {
      p(m instanceof Error ? m.message : String(m));
    }
  }, [e == null ? void 0 : e.query, r]);
  le(() => {
    y();
  }, [y]);
  const I = z((m) => {
    const C = (...O) => {
      for (const L of O) {
        const N = m.config[L];
        if (N != null && String(N).trim()) return String(N);
      }
      return "";
    };
    l({
      id: m.id,
      name: m.name,
      type: m.type,
      provider: m.provider,
      root: C("root", "path"),
      host: C("server", "host", "dataSource", "data_source"),
      database: C("database", "initialCatalog", "initial_catalog", "path"),
      port: C("port"),
      table: C("table"),
      credentialId: m.credentialId ?? "",
      enabled: m.enabled,
      config: m.config
    }), d(!0);
  }, []), S = z(async () => {
    const m = u.id.trim();
    if (!m) {
      p("请输入资源 ID");
      return;
    }
    f(!0), p(null);
    try {
      await r.hubCommand("industrial/save-resource", {
        id: m,
        name: u.name || m,
        type: u.type,
        provider: u.provider || "manual",
        config: Kw(u),
        credentialId: u.credentialId || null,
        enabled: u.enabled
      }), d(!1), l(bo), await y();
    } catch (C) {
      p(C instanceof Error ? C.message : String(C));
    } finally {
      f(!1);
    }
  }, [r, u, y]), _ = z(async (m) => {
    p(null);
    try {
      await r.hubCommand("industrial/test-resource", { id: m });
    } catch (C) {
      p(C instanceof Error ? C.message : String(C));
    }
    await y();
  }, [r, y]), A = z(async (m) => {
    p(null);
    try {
      await r.hubCommand("industrial/delete-resource", { id: m }), await y();
    } catch (C) {
      p(C instanceof Error ? C.message : String(C));
    }
  }, [r, y]), k = new Map(s.map((m) => [m.resourceId, m]));
  return /* @__PURE__ */ T(K, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ T(K, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ v($.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ v(B, { type: "primary", onClick: () => {
        l(bo), d(!0);
      }, children: "新建资源" })
    ] }),
    h && /* @__PURE__ */ v(Ce, { type: "error", showIcon: !0, message: h }),
    /* @__PURE__ */ v(
      qe,
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
            render: (m, C) => {
              const O = k.get(C.id);
              return O ? /* @__PURE__ */ v($.Text, { type: O.ok ? "success" : "danger", children: O.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (m, C) => /* @__PURE__ */ T(K, { size: 4, children: [
              /* @__PURE__ */ v(B, { size: "small", onClick: () => void _(C.id), children: "测试连接" }),
              /* @__PURE__ */ v(B, { size: "small", onClick: () => I(C), children: "编辑" }),
              /* @__PURE__ */ v(B, { size: "small", danger: !0, onClick: () => void A(C.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ v(
      ur,
      {
        open: c,
        title: u.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: g,
        onCancel: () => d(!1),
        onOk: () => void S(),
        children: /* @__PURE__ */ T(K, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: u.id,
              onChange: (m) => l({ ...u, id: m.target.value })
            }
          ),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: u.name,
              onChange: (m) => l({ ...u, name: m.target.value })
            }
          ),
          /* @__PURE__ */ v($.Text, { children: "资源类型" }),
          /* @__PURE__ */ v(K, { wrap: !0, children: Uw.map((m) => /* @__PURE__ */ v(
            B,
            {
              size: "small",
              type: u.type === m.value ? "primary" : "default",
              onClick: () => l({ ...u, type: m.value }),
              children: m.label
            },
            m.value
          )) }),
          (u.type === "filesystem.local" || u.type === "filesystem.unc") && /* @__PURE__ */ v(
            V,
            {
              "aria-label": "目录路径",
              placeholder: u.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: u.root,
              onChange: (m) => l({ ...u, root: m.target.value })
            }
          ),
          (u.type === "sqlserver" || u.type === "postgresql" || u.type === "mysql") && /* @__PURE__ */ T(K, { children: [
            /* @__PURE__ */ v(
              V,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: u.host,
                onChange: (m) => l({ ...u, host: m.target.value })
              }
            ),
            /* @__PURE__ */ v(
              V,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: u.database,
                onChange: (m) => l({ ...u, database: m.target.value })
              }
            ),
            /* @__PURE__ */ v(
              V,
              {
                "aria-label": "端口",
                placeholder: "端口",
                value: u.port,
                onChange: (m) => l({ ...u, port: m.target.value })
              }
            )
          ] }),
          u.type === "sqlite" && /* @__PURE__ */ v(
            V,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: u.database,
              onChange: (m) => l({ ...u, database: m.target.value })
            }
          ),
          /* @__PURE__ */ v(
            ye,
            {
              "aria-label": "凭证",
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: u.credentialId || void 0,
              options: a.map((m) => ({ value: m.id, label: m.name })),
              onChange: (m) => l({ ...u, credentialId: m ?? "" })
            }
          ),
          /* @__PURE__ */ T(K, { children: [
            /* @__PURE__ */ v(
              nt,
              {
                "aria-label": "启用资源",
                checked: u.enabled,
                onChange: (m) => l({ ...u, enabled: m })
              }
            ),
            /* @__PURE__ */ v($.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
const _o = ["username-password", "domain-account", "api-token"];
function Vw({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q(_o), [s, o] = q(!1), [u, l] = q(""), [c, d] = q(""), [h, p] = q(_o[0]), [g, f] = q(""), [y, I] = q(""), [S, _] = q(""), [A, k] = q(""), [m, C] = q(""), [O, L] = q(!1), [N, w] = q(null), [F, j] = q(!1), Y = z(async () => {
    try {
      n(Ch(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
      const D = await r.hubQuery("industrial/credential-types"), R = Array.isArray(D) ? D : Array.isArray(D == null ? void 0 : D.data) ? D.data : [];
      R.length && i(R.map((ue) => ue.kind));
    } catch (D) {
      w(D instanceof Error ? D.message : String(D));
    }
  }, [e == null ? void 0 : e.query, r]);
  le(() => {
    Y();
  }, [Y]);
  const X = z(() => {
    l(""), d(""), p(_o[0]), f(""), I(""), _(""), k(""), C(""), L(!1), w(null);
  }, []), ae = z(() => {
    X(), o(!0);
  }, [X]), P = z((D) => {
    X(), l(D.id), d(D.name), a.includes(D.kind) && p(D.kind), L(!0), o(!0);
  }, [a, X]), G = z(() => m.trim() ? m.trim() : h === "api-token" ? A.trim() ? JSON.stringify({ token: A.trim() }) : null : !g.trim() || !y.trim() ? null : JSON.stringify(h === "domain-account" ? { username: g.trim(), password: y.trim(), domain: S.trim() } : { username: g.trim(), password: y.trim() }), [S, h, y, m, A, g]), Z = z(async () => {
    if (!u.trim()) {
      w("请输入凭证 ID");
      return;
    }
    const D = G();
    if (!D) {
      w("请填写凭证字段，或在高级内容中提供凭证");
      return;
    }
    j(!0), w(null);
    try {
      O ? await r.hubCommand("industrial/rotate-credential", { id: u, secret: D }) : await r.hubCommand("industrial/save-credential", { id: u, name: c || u, kind: h, secret: D }), o(!1), await Y();
    } catch (R) {
      w(R instanceof Error ? R.message : String(R));
    } finally {
      j(!1);
    }
  }, [G, r, u, h, Y, c, O, m]), de = z(async (D) => {
    w(null);
    try {
      await r.hubCommand("industrial/delete-credential", { id: D.id }), await Y();
    } catch (R) {
      w(R instanceof Error ? R.message : String(R));
    }
  }, [r, Y]);
  return /* @__PURE__ */ T(K, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ T(K, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ v($.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ v(B, { type: "primary", onClick: ae, children: "新建凭证" })
    ] }),
    N && /* @__PURE__ */ v(Ce, { type: "error", showIcon: !0, message: N }),
    /* @__PURE__ */ v(
      qe,
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
            render: (D, R) => /* @__PURE__ */ T(K, { children: [
              /* @__PURE__ */ v(B, { size: "small", onClick: () => P(R), children: "轮换" }),
              /* @__PURE__ */ v(B, { size: "small", danger: !0, onClick: () => void de(R), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ v(
      ur,
      {
        open: s,
        title: O ? "轮换凭证" : "新建凭证",
        okText: O ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: F,
        onCancel: () => o(!1),
        onOk: () => void Z(),
        children: /* @__PURE__ */ T(K, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: u,
              disabled: O,
              onChange: (D) => l(D.target.value)
            }
          ),
          !O && /* @__PURE__ */ v(
            V,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: c,
              onChange: (D) => d(D.target.value)
            }
          ),
          /* @__PURE__ */ v(
            ye,
            {
              "aria-label": "凭证类型",
              style: { width: "100%" },
              value: h,
              onChange: (D) => p(D),
              options: a.map((D) => ({ value: D, label: D }))
            }
          ),
          h === "api-token" ? /* @__PURE__ */ v(
            V,
            {
              "aria-label": "令牌",
              placeholder: "令牌",
              value: A,
              onChange: (D) => k(D.target.value)
            }
          ) : /* @__PURE__ */ T(Nr, { children: [
            /* @__PURE__ */ v(
              V,
              {
                "aria-label": "用户名",
                placeholder: "用户名",
                value: g,
                onChange: (D) => f(D.target.value)
              }
            ),
            /* @__PURE__ */ v(
              V,
              {
                "aria-label": "密码",
                placeholder: "密码",
                value: y,
                onChange: (D) => I(D.target.value)
              }
            ),
            h === "domain-account" && /* @__PURE__ */ v(
              V,
              {
                "aria-label": "域",
                placeholder: "域（可选）",
                value: S,
                onChange: (D) => _(D.target.value)
              }
            )
          ] }),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "凭证内容",
              placeholder: "高级：直接提供凭证内容（优先）",
              value: m,
              onChange: (D) => C(D.target.value)
            }
          ),
          N && /* @__PURE__ */ v($.Text, { type: "danger", children: N })
        ] })
      }
    )
  ] });
}
const ph = (e) => e === "Succeeded" || e === "Completed" ? "success" : e === "Failed" || e === "PartialFailure" ? "error" : e === "Running" || e === "Interrupted" ? "processing" : "default";
function Ww({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q(null), [u, l] = q(null), [c, d] = q(!1), h = z(async () => {
    try {
      const g = Pv(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/execution-center"));
      n(g.runs), i(g.deliveries);
    } catch (g) {
      o(g instanceof Error ? g.message : String(g));
    }
  }, [e == null ? void 0 : e.query, r]);
  le(() => {
    h();
  }, [h]);
  const p = z(async (g) => {
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
  return /* @__PURE__ */ T(K, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ v($.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    s && /* @__PURE__ */ v(Ce, { type: "error", showIcon: !0, message: s }),
    u && /* @__PURE__ */ v(Ce, { type: "success", showIcon: !0, message: u }),
    /* @__PURE__ */ v(
      qe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ v(ke, { color: ph(g), children: g }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (g) => g ?? "—" },
          { title: "错误", dataIndex: "error", render: (g) => g ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ v(
      qe,
      {
        rowKey: "id",
        dataSource: a,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ v(ke, { color: ph(g), children: g }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (g, f) => f.diagnosis ? /* @__PURE__ */ T(K, { direction: "vertical", size: 0, children: [
              /* @__PURE__ */ v($.Text, { children: f.diagnosis.message }),
              /* @__PURE__ */ v($.Text, { type: "secondary", children: f.diagnosis.action })
            ] }) : f.error ?? "—"
          },
          {
            title: "操作",
            key: "actions",
            render: (g, f) => /* @__PURE__ */ v(
              B,
              {
                size: "small",
                disabled: c || f.status !== "Failed" && f.status !== "Interrupted",
                onClick: () => void p(f),
                children: "重试此目标"
              }
            )
          }
        ]
      }
    )
  ] });
}
function Yw({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q(null), s = z(async () => {
    try {
      n(Ov(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (u) {
      i(u instanceof Error ? u.message : String(u));
    }
  }, [e == null ? void 0 : e.query, r]);
  le(() => {
    s();
  }, [s]);
  const o = (u) => /* @__PURE__ */ v(
    qe,
    {
      rowKey: "id",
      dataSource: u.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (l) => /* @__PURE__ */ v(ke, { children: l }) },
        { title: "记录", dataIndex: "records" },
        {
          title: "接受/拒绝/警告",
          key: "counts",
          render: (l, c) => `${c.accepted} / ${c.rejected} / ${c.warnings}`
        },
        {
          title: "原因与下一步",
          key: "diagnosis",
          render: (l, c) => c.diagnosis ? /* @__PURE__ */ T(K, { direction: "vertical", size: 0, children: [
            /* @__PURE__ */ v($.Text, { children: c.diagnosis.message }),
            /* @__PURE__ */ v($.Text, { type: "secondary", children: c.diagnosis.action }),
            c.error && /* @__PURE__ */ v($.Text, { type: "secondary", children: c.error })
          ] }) : c.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ T(K, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ v($.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    a && /* @__PURE__ */ v(Ce, { type: "error", showIcon: !0, message: a }),
    /* @__PURE__ */ v(
      qe,
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
const wo = (e) => {
  const r = _e(e);
  return Array.isArray(r) ? r : [];
}, Qw = () => `machine-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 12) : Date.now().toString(36)}`;
function Jw({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q([]), [u, l] = q(null), [c, d] = q(""), [h, p] = q(""), [g, f] = q(), [y, I] = q(!0), [S, _] = q(null), A = z(async () => {
    try {
      n(wo(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/machines"))), i(wo(await r.hubQuery("industrial/resources"))), o(wo(await r.hubQuery("industrial/credentials")));
    } catch (w) {
      _(w instanceof Error ? w.message : String(w));
    }
  }, [e == null ? void 0 : e.query, r]);
  le(() => {
    A();
  }, [A]);
  const k = z(() => {
    l({ id: Qw(), name: "", endpoint: "", credentialId: null, enabled: !0 }), d(""), p(""), f(void 0), I(!0), _(null);
  }, []), m = z((w) => {
    l(w), d(w.name), p(w.endpoint), f(w.credentialId ?? void 0), I(w.enabled), _(null);
  }, []), C = z(async () => {
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
        }), l(null), await A();
      } catch (w) {
        _(w instanceof Error ? w.message : String(w));
      }
    }
  }, [r, g, u, h, y, A, c]), O = z(async (w) => {
    _(null);
    try {
      await r.hubCommand("industrial/delete-machine", { id: w.id }), await A();
    } catch (F) {
      _(F instanceof Error ? F.message : String(F));
    }
  }, [r, A]), L = z(async (w, F) => {
    _(null);
    try {
      await r.hubCommand("industrial/save-machine", {
        id: w.id,
        name: w.name,
        endpoint: w.endpoint,
        credentialId: w.credentialId,
        enabled: F
      }), await A();
    } catch (j) {
      _(j instanceof Error ? j.message : String(j));
    }
  }, [r, A]), N = z(async (w, F) => {
    _(null);
    try {
      await r.hubCommand("industrial/save-resource", {
        id: w.id,
        machineId: F || null
      }), await A();
    } catch (j) {
      _(j instanceof Error ? j.message : String(j));
    }
  }, [r, A]);
  return /* @__PURE__ */ T(K, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ T(K, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ v($.Title, { level: 4, style: { margin: 0 }, children: "设备与资源" }),
      /* @__PURE__ */ v(B, { type: "primary", onClick: k, children: "新增设备" })
    ] }),
    /* @__PURE__ */ v($.Text, { type: "secondary", children: "设备地址是台账属性；实际连接由数据源资源配置决定。绑定凭证供需要认证的数据源引用。" }),
    S && /* @__PURE__ */ v(Ce, { type: "error", showIcon: !0, message: S }),
    /* @__PURE__ */ v(
      qe,
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
              var F;
              return ((F = s.find((j) => j.id === w)) == null ? void 0 : F.name) ?? "—";
            }
          },
          {
            title: "启用",
            dataIndex: "enabled",
            render: (w, F) => /* @__PURE__ */ v(
              nt,
              {
                "aria-label": `设备 ${F.name} 启用`,
                checked: w,
                onChange: (j) => void L(F, j)
              }
            )
          },
          { title: "操作", key: "actions", render: (w, F) => /* @__PURE__ */ T(K, { children: [
            /* @__PURE__ */ v(B, { size: "small", onClick: () => m(F), children: "编辑" }),
            /* @__PURE__ */ v(B, { size: "small", danger: !0, onClick: () => void O(F), children: "删除" })
          ] }) }
        ]
      }
    ),
    /* @__PURE__ */ v($.Title, { level: 5, style: { margin: 0 }, children: "资源归属" }),
    /* @__PURE__ */ v(
      qe,
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
            render: (w, F) => /* @__PURE__ */ v(
              ye,
              {
                "aria-label": `资源 ${F.name} 的设备`,
                style: { width: 220 },
                value: F.machineId ?? void 0,
                allowClear: !0,
                onChange: (j) => void N(F, j ?? ""),
                options: t.map((j) => ({ value: j.id, label: j.name }))
              }
            )
          }
        ]
      }
    ),
    /* @__PURE__ */ v(
      ur,
      {
        open: u !== null,
        title: u != null && u.name ? "编辑设备" : "新增设备",
        okText: "保存设备",
        cancelText: "取消",
        onCancel: () => l(null),
        onOk: () => void C(),
        children: /* @__PURE__ */ T(K, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "设备名称",
              placeholder: "设备名称",
              value: c,
              onChange: (w) => d(w.target.value)
            }
          ),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "设备地址",
              placeholder: "台账地址 / IP",
              value: h,
              onChange: (w) => p(w.target.value)
            }
          ),
          /* @__PURE__ */ v(
            ye,
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
const Xw = ["filesystem.local", "filesystem.unc"], Zw = ["sink.sqlite", "sink.sqlserver", "sink.postgresql", "sink.mysql"];
function e1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q(""), [u, l] = q(""), [c, d] = q("csv"), [h, p] = q([]), [g, f] = q(null), [y, I] = q(!1);
  le(() => {
    (async () => {
      try {
        n(Ih(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"))), i(wh(await r.hubQuery((e == null ? void 0 : e.nodesQuery) ?? "industrial/nodes")));
      } catch (m) {
        f(m instanceof Error ? m.message : String(m));
      }
    })();
  }, [e == null ? void 0 : e.nodesQuery, e == null ? void 0 : e.query, r]);
  const S = t.filter((m) => Xw.includes(m.type)), _ = t.filter((m) => Zw.some((C) => m.type === C.split(".")[1])), A = tt(
    () => new Map(a.map((m) => [m.nodeTypeId, m])),
    [a]
  ), k = z(async () => {
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
    I(!0), f(null);
    try {
      const O = `quick-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`, N = `industrial.${t.find((G) => G.id === u).type}`, w = `industrial.reader.${c}`, F = (G, Z, de, D, R) => ({ id: G, type: Z, config: R, x: de, y: D }), j = (G, Z) => {
        var R, ue;
        const de = A.get(X[G]), D = A.get(X[Z]);
        return {
          source: G,
          sourcePort: ((R = de.outputs[0]) == null ? void 0 : R.name) ?? "output",
          target: Z,
          targetPort: ((ue = D.inputs[0]) == null ? void 0 : ue.name) ?? "input"
        };
      }, Y = [
        F("src", N, 80, 120, { resourceId: u }),
        F("read", w, 280, 120, {}),
        ...h.map((G, Z) => F(
          `sink${Z + 1}`,
          `industrial.sink.${t.find((de) => de.id === G).type}`,
          500,
          80 + Z * 120,
          { targetResourceId: G }
        ))
      ], X = Object.fromEntries(Y.map((G) => [G.id, G.type])), ae = [j("src", "read"), ...h.map((G, Z) => j("read", `sink${Z + 1}`))], P = {
        ...Yr(O),
        name: s.trim(),
        nodes: Y,
        edges: ae,
        resourceReferences: [u, ...h],
        layout: { lookbackDays: 1 }
      };
      await r.hubCommand("industrial/save-workflow", P), await r.hubCommand("industrial/publish-workflow", { workflowId: O }), (m = r.navigate) == null || m.call(r, `/industrial/designer?workflowId=${encodeURIComponent(O)}`);
    } catch (C) {
      f(C instanceof Error ? C.message : String(C));
    } finally {
      I(!1);
    }
  }, [r, c, A, s, t, u, h]);
  return /* @__PURE__ */ T(K, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ v($.Title, { level: 4, style: { margin: 0 }, children: "快速创建采集任务" }),
    /* @__PURE__ */ v($.Text, { type: "secondary", children: "生成标准工作流，可在设计器中继续修改。" }),
    g && /* @__PURE__ */ v(Ce, { type: "error", showIcon: !0, message: g }),
    /* @__PURE__ */ v(
      V,
      {
        "aria-label": "任务名称",
        placeholder: "任务名称",
        value: s,
        onChange: (m) => o(m.target.value)
      }
    ),
    /* @__PURE__ */ T(K, { wrap: !0, children: [
      /* @__PURE__ */ v(
        ye,
        {
          "aria-label": "数据来源",
          style: { width: 260 },
          placeholder: "数据来源",
          value: u || void 0,
          onChange: (m) => l(m),
          options: S.map((m) => ({ value: m.id, label: `${m.name} (${m.type})` }))
        }
      ),
      /* @__PURE__ */ v(
        ye,
        {
          "aria-label": "数据格式",
          style: { width: 160 },
          value: c,
          onChange: (m) => d(m),
          options: ["csv", "excel", "json"].map((m) => ({ value: m, label: m.toUpperCase() }))
        }
      )
    ] }),
    /* @__PURE__ */ v(
      ye,
      {
        "aria-label": "目标资源",
        mode: "multiple",
        style: { width: 420 },
        placeholder: "目标资源",
        value: h,
        onChange: (m) => p(m),
        options: _.map((m) => ({ value: m.id, label: `${m.name} (${m.type})` }))
      }
    ),
    /* @__PURE__ */ v(B, { type: "primary", loading: y, onClick: () => void k(), children: "创建并发布" })
  ] });
}
const r1 = (e) => {
  const r = _e(e);
  return Array.isArray(r) ? r : [];
}, t1 = () => ({
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
function n1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q(null), [s, o] = q(""), [u, l] = q(""), [c, d] = q(null), h = z(async () => {
    try {
      n(r1(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-profiles")));
    } catch (f) {
      d(f instanceof Error ? f.message : String(f));
    }
  }, [e == null ? void 0 : e.query, r]);
  le(() => {
    h();
  }, [h]);
  const p = z((f) => {
    const y = f ? { ...f } : t1();
    i(y), o(Object.entries(y.mapping ?? {}).map(([I, S]) => `${I}=${S}`).join(",")), l((y.requiredFields ?? []).join(",")), d(null);
  }, []), g = z(async () => {
    if (a) {
      if (!a.name.trim()) {
        d("请输入配置名称");
        return;
      }
      d(null);
      try {
        const f = {};
        for (const y of s.split(",")) {
          const [I, S] = y.split("=").map((_) => _ == null ? void 0 : _.trim());
          I && S && (f[I] = S);
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
  return /* @__PURE__ */ T(K, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ T(K, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ v($.Title, { level: 4, style: { margin: 0 }, children: "文件格式配置" }),
      /* @__PURE__ */ v(B, { type: "primary", onClick: () => p(), children: "新建文件配置" })
    ] }),
    c && /* @__PURE__ */ v(Ce, { type: "error", showIcon: !0, message: c }),
    /* @__PURE__ */ v(
      qe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "名称", dataIndex: "name" },
          { title: "文件匹配", dataIndex: "fileNamePattern" },
          { title: "格式", dataIndex: "format" },
          { title: "版本", dataIndex: "version" },
          { title: "操作", key: "actions", render: (f, y) => /* @__PURE__ */ v(B, { size: "small", onClick: () => p(y), children: "编辑" }) }
        ]
      }
    ),
    /* @__PURE__ */ v(
      ur,
      {
        open: a !== null,
        title: a != null && a.version ? "编辑文件配置" : "新建文件配置",
        okText: "保存配置",
        cancelText: "取消",
        onCancel: () => i(null),
        onOk: () => void g(),
        children: a && /* @__PURE__ */ T(K, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "配置名称",
              placeholder: "配置名称",
              value: a.name,
              onChange: (f) => i({ ...a, name: f.target.value })
            }
          ),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "文件匹配规则",
              placeholder: "文件匹配，例如 *.csv",
              value: a.fileNamePattern,
              onChange: (f) => i({ ...a, fileNamePattern: f.target.value })
            }
          ),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "文件格式",
              placeholder: "csv / excel / json",
              value: a.format,
              onChange: (f) => i({ ...a, format: f.target.value })
            }
          ),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "分隔符",
              placeholder: "分隔符",
              value: a.delimiter,
              onChange: (f) => i({ ...a, delimiter: f.target.value })
            }
          ),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "编码",
              placeholder: "编码",
              value: a.encoding,
              onChange: (f) => i({ ...a, encoding: f.target.value })
            }
          ),
          /* @__PURE__ */ v(
            nt,
            {
              "aria-label": "首行为表头",
              checked: a.hasHeader,
              onChange: (f) => i({ ...a, hasHeader: f })
            }
          ),
          /* @__PURE__ */ v(
            V,
            {
              "aria-label": "字段映射",
              placeholder: "目标字段=来源字段，逗号分隔",
              value: s,
              onChange: (f) => o(f.target.value)
            }
          ),
          /* @__PURE__ */ v(
            V,
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
const Sr = (e) => {
  const r = _e(e);
  return Array.isArray(r) ? r : [];
};
function a1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q([]), [u, l] = q([]), [c, d] = q([]), [h, p] = q([]), [g, f] = q([]), [y, I] = q(), [S, _] = q(1), [A, k] = q("resource-{machine}"), [m, C] = q([]), [O, L] = q(""), [N, w] = q(""), [F, j] = q(null), [Y, X] = q(!1), ae = z(async () => {
    try {
      n(Sr(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/bindings"))), i(Sr(await r.hubQuery("industrial/machines"))), o(Sr(await r.hubQuery("industrial/file-profiles"))), l(Sr(await r.hubQuery("industrial/workflows"))), d(Sr(await r.hubQuery("industrial/resources")));
    } catch (R) {
      j(R instanceof Error ? R.message : String(R));
    }
  }, [e == null ? void 0 : e.query, r]);
  le(() => {
    ae();
  }, [ae]);
  const P = tt(() => {
    const R = [];
    for (const Ae of h) for (const Me of g) {
      const sr = `binding:${y ?? ""}:v${S}:${Ae}:${Me}`;
      t.some((pr) => pr.id === sr) && R.push(sr);
    }
    const ue = m.filter((Ae) => !c.some((Me) => Me.id === Ae)), qr = h.map((Ae) => A.replace("{machine}", Ae)).filter((Ae) => !c.some((Me) => Me.id === Ae));
    return {
      total: h.length * g.length,
      create: h.length * g.length - R.length,
      conflicts: R,
      missing: [...ue, ...qr]
    };
  }, [t, h, g, c, A, m, S, y]), G = z(async () => {
    if (!y || h.length === 0 || g.length === 0 || m.length === 0) {
      j("请选择工作流、机台、文件配置和目标资源");
      return;
    }
    if (P.missing.length > 0) {
      j("存在缺失资源，请先创建后再导入");
      return;
    }
    X(!0), j(null);
    try {
      await r.hubCommand("industrial/create-bindings", {
        machineIds: h,
        fileProfileIds: g,
        workflowId: y,
        workflowVersion: S,
        sourceResourcePattern: A,
        targetResourceIds: m
      }), await ae();
    } catch (R) {
      j(R instanceof Error ? R.message : String(R));
    } finally {
      X(!1);
    }
  }, [r, ae, P.missing.length, g, A, m, S, y, h]), Z = z(async () => {
    X(!0), j(null);
    try {
      await r.hubCommand("industrial/import-machines", { csv: O }), L(""), await ae();
    } catch (R) {
      j(R instanceof Error ? R.message : String(R));
    } finally {
      X(!1);
    }
  }, [r, O, ae]), de = z(async () => {
    j(null);
    try {
      const R = await r.hubQuery("industrial/export-config"), ue = (R == null ? void 0 : R.data) ?? R;
      w(JSON.stringify(ue, null, 2));
    } catch (R) {
      j(R instanceof Error ? R.message : String(R));
    }
  }, [r]), D = z(async () => {
    X(!0), j(null);
    try {
      await r.hubCommand("industrial/import-config", JSON.parse(N)), await ae();
    } catch (R) {
      j(R instanceof Error ? R.message : String(R));
    } finally {
      X(!1);
    }
  }, [r, ae, N]);
  return /* @__PURE__ */ T(K, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ v($.Title, { level: 4, style: { margin: 0 }, children: "采集任务绑定" }),
    F && /* @__PURE__ */ v(Ce, { type: "error", showIcon: !0, message: F }),
    /* @__PURE__ */ T(K, { wrap: !0, children: [
      /* @__PURE__ */ v(
        ye,
        {
          "aria-label": "工作流模板",
          mode: "single",
          style: { width: 220 },
          value: y,
          onChange: (R) => I(R),
          options: u.map((R) => ({ value: R.workflowId, label: R.name }))
        }
      ),
      /* @__PURE__ */ v(
        xo,
        {
          "aria-label": "模板版本",
          min: 1,
          value: S,
          onChange: (R) => _(Number(R) || 1)
        }
      ),
      /* @__PURE__ */ v(
        ye,
        {
          "aria-label": "机台范围",
          mode: "multiple",
          style: { width: 300 },
          value: h,
          onChange: (R) => p(R),
          options: a.map((R) => ({ value: R.id, label: R.name }))
        }
      ),
      /* @__PURE__ */ v(
        ye,
        {
          "aria-label": "文件配置范围",
          mode: "multiple",
          style: { width: 300 },
          value: g,
          onChange: (R) => f(R),
          options: s.map((R) => ({ value: R.id, label: R.name }))
        }
      ),
      /* @__PURE__ */ v(
        ye,
        {
          "aria-label": "目标资源",
          mode: "multiple",
          style: { width: 300 },
          value: m,
          onChange: (R) => C(R),
          options: c.map((R) => ({ value: R.id, label: `${R.name} (${R.type})` }))
        }
      ),
      /* @__PURE__ */ v(
        V,
        {
          "aria-label": "来源资源模板",
          placeholder: "来源资源模板，{machine} 占位符",
          value: A,
          onChange: (R) => k(R.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ v(
      Ce,
      {
        type: "info",
        showIcon: !0,
        message: `预计生成 ${P.total} 条绑定；可创建 ${P.create} 条`,
        description: `冲突 ${P.conflicts.length} 条；缺失资源 ${P.missing.length} 条`
      }
    ),
    /* @__PURE__ */ v(B, { type: "primary", loading: Y, onClick: () => void G(), children: "生成绑定" }),
    /* @__PURE__ */ v($.Title, { level: 5, style: { margin: 0 }, children: "批量导入机台（CSV）" }),
    /* @__PURE__ */ v(
      V.TextArea,
      {
        "aria-label": "机台 CSV",
        rows: 4,
        value: O,
        placeholder: "id,name,endpoint,credentialId",
        onChange: (R) => L(R.target.value)
      }
    ),
    /* @__PURE__ */ v(B, { loading: Y, onClick: () => void Z(), children: "导入机台" }),
    /* @__PURE__ */ v($.Title, { level: 5, style: { margin: 0 }, children: "配置包（不含凭证）" }),
    /* @__PURE__ */ T(K, { wrap: !0, children: [
      /* @__PURE__ */ v(B, { onClick: () => void de(), children: "导出配置" }),
      /* @__PURE__ */ v(B, { loading: Y, onClick: () => void D(), children: "导入配置" })
    ] }),
    /* @__PURE__ */ v(
      V.TextArea,
      {
        "aria-label": "配置包",
        rows: 5,
        value: N,
        placeholder: "导出后可跨环境导入；资源 ID 需自行映射",
        onChange: (R) => w(R.target.value)
      }
    ),
    /* @__PURE__ */ v(
      qe,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "绑定", dataIndex: "name" },
          { title: "机台", dataIndex: "machineId" },
          { title: "文件配置", dataIndex: "fileProfileId" },
          { title: "模板版本", dataIndex: "workflowVersion" },
          { title: "状态", dataIndex: "enabled", render: (R) => R ? "启用" : "停用" }
        ]
      }
    )
  ] });
}
const i1 = `
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
`;
function s1(e) {
  Bp(e);
  const r = typeof document > "u" ? null : document.createElement("style");
  r && (r.dataset.industrialDesigner = "true", r.textContent = i1, document.head.appendChild(r));
  const t = ({ ctx: i }) => Io(zw, { ctx: i });
  t.displayName = "IndustrialWorkflowCanvas";
  const n = (i, s) => {
    const o = ({ block: u, ctx: l }) => Io(s, { block: u, ctx: l });
    return o.displayName = i, e.blocks.register(i, o);
  }, a = [
    e.blocks.register("industrial-workflow-canvas", t),
    n("industrial-workflow-list", Bw),
    n("industrial-resource-console", Hw),
    n("industrial-credential-console", Vw),
    n("industrial-execution-center", Ww),
    n("industrial-file-details", Yw),
    n("industrial-machine-console", Jw),
    n("industrial-collection-wizard", e1),
    n("industrial-file-profiles", n1),
    n("industrial-binding-console", a1)
  ];
  return () => {
    for (const i of a) i == null || i();
    r == null || r.remove();
  };
}
export {
  Gv as g,
  s1 as r
};
