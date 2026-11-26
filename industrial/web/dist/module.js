let ue = null;
function nt(e) {
  ue = e;
}
function B() {
  if (!ue && typeof globalThis < "u" && (ue = globalThis.__CORDIS_CONSOLE ?? null), !ue)
    throw new Error("[industrial-designer] console facade is not available");
  return ue;
}
const l = (...e) => B().jsxRuntime.jsx(...e), m = (...e) => B().jsxRuntime.jsxs(...e), he = (e) => B().jsxRuntime.jsx(B().jsxRuntime.Fragment, e), je = (...e) => B().React.createElement(...e), Q = (...e) => B().React.useCallback(...e), Z = (...e) => B().React.useEffect(...e), Fe = (...e) => B().React.useMemo(...e), X = (...e) => B().React.useRef(...e), S = (...e) => B().React.useState(...e), N = (e) => {
  const t = (r) => {
    const n = e.split(".").reduce((o, i) => o == null ? void 0 : o[i], B().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return je(n, r ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, Pe = N("Alert"), G = N("Button"), $e = N("Divider"), ot = N("Drawer"), Le = N("Empty"), be = Object.assign(N("Form"), {
  Item: N("Form.Item"),
  useForm: (...e) => B().antd.Form.useForm(...e)
}), ae = Object.assign(N("Input"), {
  Number: N("InputNumber"),
  TextArea: N("Input.TextArea")
}), Me = N("InputNumber"), st = Object.assign(N("Layout"), {
  Header: N("Layout.Header"),
  Content: N("Layout.Content")
});
N("Modal");
const se = N("Select"), Ee = N("Space"), Ge = N("Spin"), it = N("Switch"), Be = N("Table"), oe = N("Tag"), _ = Object.assign(N("Typography"), {
  Title: N("Typography.Title"),
  Text: N("Typography.Text")
}), at = new Proxy({}, {
  get: (e, t) => (...r) => {
    var o;
    const n = (o = B().antd.message) == null ? void 0 : o[t];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${t} unavailable`);
    return n(...r);
  }
}), We = 148, qe = 46, Ce = {
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
}, Oe = (e) => Ce[e] ?? Ce[e.split(".")[0]] ?? "#4d6bfe";
function lt(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function ct(e, t = /* @__PURE__ */ new Map(), r) {
  return {
    nodes: e.nodes.map((n) => {
      const o = t.get(n.type), i = (o == null ? void 0 : o.category) ?? "General", a = r === n.id;
      return {
        id: n.id,
        data: {
          label: (o == null ? void 0 : o.name) ?? n.type,
          nodeTypeId: n.type,
          category: i,
          icon: (o == null ? void 0 : o.icon) ?? "AppstoreOutlined",
          config: { ...n.config }
        },
        style: {
          x: n.x,
          y: n.y,
          fill: Oe(i),
          stroke: a ? "#111111" : Oe(i),
          lineWidth: a ? 3 : 1.5,
          radius: 10,
          port: !0,
          ports: [
            ...((o == null ? void 0 : o.inputs) ?? []).map((s, c, f) => ({
              key: `in:${s.name}`,
              placement: [0, (c + 1) / (f.length + 1)],
              r: 5,
              stroke: s.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((o == null ? void 0 : o.outputs) ?? []).map((s, c, f) => ({
              key: `out:${s.name}`,
              placement: [1, (c + 1) / (f.length + 1)],
              r: 5,
              stroke: s.type === "Control" ? "#52c41a" : "#1677ff",
              fill: "#fff"
            }))
          ]
        }
      };
    }),
    edges: e.edges.map((n) => ({
      id: lt(n),
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
function ut(e, t, r) {
  const n = r && r.length > 0 ? new Set(r) : null, o = {};
  for (const i of e.nodes) {
    if (!t.has(i.id) || n && !n.has(i.id)) continue;
    const a = i.style.x, s = i.style.y;
    typeof a == "number" && typeof s == "number" && (o[i.id] = { x: a, y: s });
  }
  return o;
}
function dt(e, t) {
  return {
    container: t,
    autoFit: "view",
    data: e,
    node: {
      type: "rect",
      style: {
        labelText: (r) => {
          var n;
          return ((n = r == null ? void 0 : r.data) == null ? void 0 : n.label) ?? "";
        },
        labelPlacement: "bottom",
        size: [We, qe],
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
        labelText: (r) => {
          var n;
          return ((n = r == null ? void 0 : r.data) == null ? void 0 : n.label) ?? "";
        },
        labelFontSize: 10,
        stroke: "#8a93a6",
        endArrow: !0
      }
    },
    behaviors: Ue()
  };
}
const pt = "__industrial_drag_target__", ft = "__industrial_connect_source__", we = (e, t) => {
  var r;
  return (((r = e.isPanModifier) == null ? void 0 : r.call(e)) ?? !1) || t.button === 1 || t.buttons === 4;
}, _e = (e, t) => {
  var g;
  if (t.targetType !== "node" || !((g = t.target) != null && g.id)) return !1;
  const r = t.client ?? t.canvas;
  if (!r || !e.graph) return !1;
  const n = String(t.target.id), o = e.graph.getNodeData().find((w) => String(w.id) === n), i = o == null ? void 0 : o.style;
  if (typeof (i == null ? void 0 : i.x) != "number" || typeof i.y != "number") return !1;
  const [a, s] = e.graph.getCanvasByClient([r.x, r.y]), c = Math.abs(a - i.x), f = Math.abs(s - i.y);
  return Math.abs(c - We / 2) <= 14 && f <= qe / 2 + 10;
};
function Ue(e = {}) {
  const t = e.connectionState ?? { armed: !1 };
  return [
    "zoom-canvas",
    {
      type: "drag-canvas",
      enable: (r) => r.targetType === "canvas" || we(e, r),
      range: 0.9
    },
    {
      type: "drag-element",
      state: pt,
      enable: (r) => (r.targetType === "node" || r.targetType === "combo") && !_e(e, r) && !we(e, r),
      onFinish: (r) => {
        var a, s;
        const n = e.graph;
        if (!n) return;
        const o = Array.isArray(r) ? r : r ? [String(r)] : void 0, i = n.getNodeData().map((c) => ({
          id: String(c.id),
          data: c.data ?? {},
          style: c.style ?? {}
        }));
        (s = e.onPositions) == null || s.call(e, ut(
          { nodes: i },
          ((a = e.allowedIds) == null ? void 0 : a.call(e)) ?? /* @__PURE__ */ new Set(),
          o
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: ft,
      enable: (r) => we(e, r) ? (t.armed = !1, !1) : t.armed ? r.targetType === "node" || r.targetType === "combo" : _e(e, r) ? (t.armed = !0, !0) : !1,
      onCreate: (r) => {
        var a;
        t.armed = !1;
        const n = String(r.source ?? ""), o = String(r.target ?? "");
        if (!n || !o || n === o) return;
        const i = (a = e.onCreateEdge) == null ? void 0 : a.call(e, n, o);
        if (i)
          return {
            ...r,
            sourcePort: `out:${i.sourcePort}`,
            targetPort: `in:${i.targetPort}`
          };
      }
    }
  ];
}
const gt = (e) => {
  var r;
  const t = e;
  return (t == null ? void 0 : t.targetType) === "node" ? (r = t.target) == null ? void 0 : r.id : void 0;
};
function yt(e) {
  const {
    definition: t,
    catalog: r,
    selectedId: n,
    onSelect: o,
    onDropNode: i,
    onCreateEdge: a,
    onPositions: s
  } = e, c = X(null), f = X(null), g = X(!1), w = X(!1), P = X({ armed: !1 }), x = X({ onSelect: o, onDropNode: i, onCreateEdge: a, onPositions: s });
  x.current = { onSelect: o, onDropNode: i, onCreateEdge: a, onPositions: s };
  const I = Fe(
    () => ct(t, r, n),
    [t, r, n]
  ), D = X(I);
  D.current = I;
  const $ = X(/* @__PURE__ */ new Set());
  return $.current = new Set(t.nodes.map((k) => k.id)), Z(() => {
    const k = (R) => {
      R.code === "Space" && !R.repeat && (w.current = !0);
    }, b = (R) => {
      R.code === "Space" && (w.current = !1);
    }, E = () => {
      w.current = !1;
    };
    return window.addEventListener("keydown", k), window.addEventListener("keyup", b), window.addEventListener("blur", E), () => {
      window.removeEventListener("keydown", k), window.removeEventListener("keyup", b), window.removeEventListener("blur", E);
    };
  }, []), Z(() => {
    const k = c.current;
    if (!k || g.current) return;
    let b = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: E }) => {
      if (b || !c.current || g.current) return;
      const R = dt(D.current, k), p = new E({
        ...R,
        behaviors: Ue({
          graph: {
            getNodeData: () => {
              var y;
              return ((y = f.current) == null ? void 0 : y.getNodeData()) ?? [];
            },
            getCanvasByClient: (y) => {
              var K;
              const C = ((K = f.current) == null ? void 0 : K.getCanvasByClient(y)) ?? y;
              return [C[0], C[1]];
            }
          },
          isPanModifier: () => w.current,
          connectionState: P.current,
          allowedIds: () => $.current,
          onPositions: (y) => x.current.onPositions(y),
          onCreateEdge: (y, C) => x.current.onCreateEdge(y, C)
        })
      });
      p.on("node:click", (y) => {
        const C = gt(y);
        C && x.current.onSelect(C);
      }), p.on("edge:click", () => x.current.onSelect(null)), p.on("canvas:click", () => x.current.onSelect(null)), p.on("pointerup", () => {
        P.current.armed = !1;
      }), f.current = p, k.__industrialGraph = p, g.current = !0, p.render();
    }), () => {
      b = !0;
    };
  }, []), Z(() => {
    const k = f.current;
    !k || !g.current || (k.setData(I), k.draw());
  }, [I]), Z(() => () => {
    var k;
    (k = f.current) == null || k.destroy(), f.current = null, g.current = !1;
  }, []), /* @__PURE__ */ l("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ m("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ l(
      "div",
      {
        ref: c,
        className: "industrial-graph-host",
        onDragOver: (k) => k.preventDefault(),
        onDrop: (k) => {
          k.preventDefault();
          const b = k.dataTransfer.getData("application/x-industrial-node") || k.dataTransfer.getData("text/plain");
          if (!b) return;
          const E = f.current, R = c.current;
          if (!E || !R) return;
          const p = R.getBoundingClientRect(), y = E.getCanvasByViewport([
            k.clientX - p.left,
            k.clientY - p.top
          ]);
          x.current.onDropNode(b, y[0], y[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ l("div", { className: "industrial-graph-hint", children: "拖动节点移动 · 从端口拖出连线 · 拖动画布平移（空格或中键可在节点上平移）" })
  ] }) });
}
function mt({ descriptors: e, loading: t, error: r }) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const a = i.category.split(".")[0] || "General", s = n.get(a) ?? [];
    s.push(i), n.set(a, s);
  }
  const o = (i, a) => {
    i.dataTransfer.setData("application/x-industrial-node", a.nodeTypeId), i.dataTransfer.setData("text/plain", a.nodeTypeId), i.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ m("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ l(_.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ l(_.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ l(Ge, { size: "small", style: { display: "block", marginTop: 16 } }),
    r && /* @__PURE__ */ l(_.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: r }),
    !t && !r && e.length === 0 && /* @__PURE__ */ l(Le, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(n.entries()).map(([i, a]) => /* @__PURE__ */ m("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ l(_.Text, { strong: !0, style: { fontSize: 12 }, children: i }),
      a.map((s) => /* @__PURE__ */ m(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          onDragStart: (c) => o(c, s),
          title: s.description || s.nodeTypeId,
          children: [
            /* @__PURE__ */ l("div", { className: "industrial-palette-node-title", children: s.name }),
            /* @__PURE__ */ m("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ l(oe, { bordered: !1, color: "blue", children: s.version }),
              /* @__PURE__ */ l(_.Text, { type: "secondary", style: { fontSize: 11 }, children: s.nodeTypeId })
            ] }),
            /* @__PURE__ */ m("div", { className: "industrial-palette-node-ports", children: [
              s.inputs.map((c) => /* @__PURE__ */ m(oe, { bordered: !1, color: "default", children: [
                "in ",
                c.name,
                ": ",
                c.type
              ] }, `in-${c.name}`)),
              s.outputs.map((c) => /* @__PURE__ */ m(oe, { bordered: !1, color: "green", children: [
                "out ",
                c.name,
                ": ",
                c.type
              ] }, `out-${c.name}`))
            ] })
          ]
        },
        s.nodeTypeId
      ))
    ] }, i))
  ] });
}
const De = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {}
});
function ht(e) {
  const t = {};
  for (const [r, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (t[r] = n.default);
  return t;
}
function wt(e, t, r = 0, n = 0) {
  return { id: t, type: e.nodeTypeId, config: ht(e), x: r, y: n };
}
function ne(e, t) {
  return e.get(t);
}
function ye(e, t, r) {
  return e[r].find((n) => n.name === t);
}
function Ie(e, t) {
  return e !== "Unknown" && t !== "Unknown" && e === t;
}
function Re(e, t) {
  for (const r of e.outputs)
    for (const n of t.inputs)
      if (Ie(r.type, n.type))
        return { sourcePort: r.name, targetPort: n.name };
  return null;
}
const L = (e, t, r) => ({ path: e, code: t, message: r });
function bt(e, t) {
  return e.source === t.source && e.sourcePort === t.sourcePort && e.target === t.target && e.targetPort === t.targetPort;
}
function xt(e, t, r) {
  const n = `edges.${e.edges.length}`, o = e.nodes.find((g) => g.id === t.source), i = e.nodes.find((g) => g.id === t.target);
  if (!o || !i)
    return { definition: e, issue: L(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((g) => bt(g, t)))
    return { definition: e };
  const a = ne(r, o.type), s = ne(r, i.type);
  if (!a || !s)
    return { definition: e, issue: L(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const c = ye(a, t.sourcePort, "outputs"), f = ye(s, t.targetPort, "inputs");
  return !c || !f ? { definition: e, issue: L(n, "PORT_MISSING", "declared port does not exist") } : Ie(c.type, f.type) ? { definition: { ...e, edges: [...e.edges, t] } } : {
    definition: e,
    issue: L(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${c.type} → ${f.type} is not a legal connection`
    )
  };
}
function kt(e, t) {
  return {
    ...e,
    nodes: e.nodes.filter((r) => r.id !== t),
    edges: e.edges.filter((r) => r.source !== t && r.target !== t)
  };
}
function vt(e, t) {
  return { ...e, edges: e.edges.filter((r, n) => n !== t) };
}
function It(e, t) {
  const r = { ...e };
  for (const [n, o] of Object.entries(t))
    o === void 0 ? delete r[n] : r[n] = o;
  return r;
}
function St(e, t, r) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === t ? { ...n, config: r } : n)
  };
}
function Nt(e, t) {
  return {
    ...e,
    nodes: e.nodes.map((r) => {
      const n = t[r.id];
      return n ? { ...r, x: n.x, y: n.y } : r;
    }),
    // One canonical layout field: the domain ships positions with the
    // definition, never as a separate browser-only cache.
    layout: { ...e.layout, positions: { ...t } }
  };
}
function Tt(e) {
  var i;
  const t = /* @__PURE__ */ new Map();
  for (const a of e.nodes) t.set(a.id, []);
  for (const a of e.edges)
    (i = t.get(a.source)) == null || i.push(a.target);
  const r = /* @__PURE__ */ new Map(), n = [], o = (a) => {
    r.set(a, 1), n.push(a);
    for (const s of t.get(a) ?? []) {
      if (r.get(s) === 1)
        return [...n.slice(n.indexOf(s)), s];
      if (r.get(s) !== 2) {
        const c = o(s);
        if (c) return c;
      }
    }
    return n.pop(), r.set(a, 2), null;
  };
  for (const a of e.nodes)
    if (r.get(a.id) !== 2) {
      const s = o(a.id);
      if (s) return s;
    }
  return null;
}
function Pt(e, t, r) {
  const n = [];
  e.workflowId.trim() || n.push(L("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(L("workflow", "INVALID_VERSION", "version must be positive"));
  const o = /* @__PURE__ */ new Map();
  for (const s of e.nodes)
    o.has(s.id) && n.push(L(`nodes.${s.id}`, "DUPLICATE_NODE_ID", `duplicate node '${s.id}'`)), o.set(s.id, s);
  const i = /* @__PURE__ */ new Map();
  for (const s of e.nodes) {
    const c = ne(t, s.type);
    if (!c) {
      n.push(L(
        `nodes.${s.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${s.type}' is not registered`
      ));
      continue;
    }
    i.set(s.id, c);
    const f = c.configurationSchema.required ?? [];
    for (const g of f) {
      const w = s.config[g];
      (w == null || w === "") && n.push(L(
        `nodes.${s.id}.config.${g}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${g}' is missing`
      ));
    }
    r && typeof s.config.resourceId == "string" && (r.has(s.config.resourceId) || n.push(L(
      `nodes.${s.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${s.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((s, c) => {
    const f = o.get(s.source), g = o.get(s.target);
    if (!f || !g) {
      n.push(L(`edges.${c}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const w = i.get(s.source), P = i.get(s.target);
    if (!w || !P) return;
    const x = ye(w, s.sourcePort, "outputs"), I = ye(P, s.targetPort, "inputs");
    if (!x || !I) {
      n.push(L(`edges.${c}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    Ie(x.type, I.type) || n.push(L(
      `edges.${c}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${x.type} → ${I.type} is not a legal connection`
    ));
  });
  for (const s of e.nodes) {
    const c = i.get(s.id);
    if (c)
      for (const f of c.inputs.filter((g) => g.required))
        e.edges.some((g) => g.target === s.id && g.targetPort === f.name && o.has(g.source)) || n.push(L(
          `nodes.${s.id}.inputs.${f.name}`,
          "MISSING_INPUT",
          `required input '${f.name}' is not connected`
        ));
  }
  const a = Tt(e);
  return a && n.push(L(`nodes.${a[0]}`, "GRAPH_CYCLE", `cycle: ${a.join(" → ")}`)), n;
}
const xe = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function $t(e) {
  const t = xe(e.additionalProperties).enum;
  if (!Array.isArray(t)) return null;
  const r = t.filter((n) => typeof n == "string");
  return r.length > 0 ? r : null;
}
function Je(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && xe(e.items).type === "string" ? "string-list" : e.type === "object" && xe(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function ke(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, t]) => t != null && typeof t != "object").map(([t, r]) => ({ key: t, value: String(r) }));
}
function Ae(e) {
  const t = {}, r = [];
  for (const n of e) {
    const o = n.key.trim();
    o && (Object.prototype.hasOwnProperty.call(t, o) && r.push(o), t[o] = n.value);
  }
  return { record: t, duplicateKeys: r };
}
function ge(e) {
  if (!Array.isArray(e)) return [];
  const t = /* @__PURE__ */ new Set(), r = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const o = n.trim();
    !o || t.has(o) || (t.add(o), r.push(o));
  }
  return r;
}
function Et(e, t) {
  const r = /* @__PURE__ */ new Set();
  for (const [n, o] of Object.entries(e))
    if (Je(o) === "key-value")
      for (const i of ke(t[n]))
        i.key.trim() && r.add(i.key.trim());
  return [...r];
}
function Ct({
  value: e,
  onChange: t,
  valueOptions: r,
  keyTitle: n = "目标字段",
  valueTitle: o = "来源字段",
  testId: i
}) {
  const a = JSON.stringify(e ?? {}), [s, c] = S(() => ke(e)), [f, g] = S(a);
  Z(() => {
    a !== f && (c(ke(e)), g(a));
  }, [a, f, e]);
  const w = (x) => {
    c(x), t == null || t(Ae(x).record);
  }, { duplicateKeys: P } = Ae(s);
  return /* @__PURE__ */ m("div", { "data-testid": i, className: "industrial-kv-editor", children: [
    s.map((x, I) => /* @__PURE__ */ m("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ l(
        ae,
        {
          "aria-label": `${n} ${I + 1}`,
          value: x.key,
          placeholder: n,
          onChange: (D) => w(s.map(($, J) => J === I ? { ...$, key: D.target.value } : $))
        }
      ),
      r && r.length > 0 ? /* @__PURE__ */ l(
        se,
        {
          "aria-label": `${o} ${I + 1}`,
          value: x.value || void 0,
          placeholder: o,
          options: r.map((D) => ({ label: D, value: D })),
          onChange: (D) => w(s.map(($, J) => J === I ? { ...$, value: D ?? "" } : $))
        }
      ) : /* @__PURE__ */ l(
        ae,
        {
          "aria-label": `${o} ${I + 1}`,
          value: x.value,
          placeholder: o,
          onChange: (D) => w(s.map(($, J) => J === I ? { ...$, value: D.target.value } : $))
        }
      ),
      /* @__PURE__ */ l(
        G,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${I + 1}`,
          onClick: () => w(s.filter((D, $) => $ !== I)),
          children: "删"
        }
      )
    ] }, I)),
    /* @__PURE__ */ l(
      G,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => w([...s, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    P.length > 0 && /* @__PURE__ */ m(_.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      P.join("、")
    ] })
  ] });
}
function Ot({ value: e, onChange: t, suggestions: r = [], testId: n }) {
  const o = JSON.stringify(ge(e)), [i, a] = S(() => ge(e)), [s, c] = S(o);
  Z(() => {
    o !== s && (a(ge(e)), c(o));
  }, [o, s, e]);
  const f = (g) => {
    a(g), t == null || t(ge(g));
  };
  return /* @__PURE__ */ m("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ l("datalist", { id: `${n ?? "list"}-suggestions`, children: r.map((g) => /* @__PURE__ */ l("option", { value: g }, g)) }),
    i.map((g, w) => /* @__PURE__ */ m("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ l(
        ae,
        {
          "aria-label": `字段 ${w + 1}`,
          value: g,
          list: `${n ?? "list"}-suggestions`,
          onChange: (P) => f(i.map((x, I) => I === w ? P.target.value : x))
        }
      ),
      /* @__PURE__ */ l(
        G,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${w + 1}`,
          onClick: () => f(i.filter((P, x) => x !== w)),
          children: "删"
        }
      )
    ] }, w)),
    /* @__PURE__ */ l(G, { size: "small", type: "dashed", block: !0, onClick: () => f([...i, ""]), children: "添加字段" }),
    i.length === 0 && /* @__PURE__ */ l(_.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function _t({ value: e, onChange: t }) {
  const r = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, o] = S(r), [i, a] = S(null);
  Z(() => {
    o(r), a(null);
  }, [r]);
  const s = (c) => {
    o(c);
    try {
      const f = JSON.parse(c);
      a(null), t == null || t(f);
    } catch (f) {
      a(f.message);
    }
  };
  return /* @__PURE__ */ m("div", { children: [
    /* @__PURE__ */ l(ae.TextArea, { rows: 5, value: n, onChange: (c) => s(c.target.value) }),
    i && /* @__PURE__ */ l(_.Text, { type: "danger", style: { fontSize: 11 }, children: i })
  ] });
}
function Dt({ value: e, onChange: t, resources: r, resourceType: n }) {
  const o = r.filter((a) => a.enabled !== !1 && (!n || a.type === n)).map((a) => ({
    value: a.id,
    label: `${a.name} (${a.type})`
  })), i = r.find((a) => a.id === e);
  return /* @__PURE__ */ m("div", { children: [
    /* @__PURE__ */ l(se, { allowClear: !0, value: e, onChange: t, options: o }),
    (i == null ? void 0 : i.credentialId) && /* @__PURE__ */ m(_.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      i.credentialName ?? i.credentialId
    ] })
  ] });
}
function Rt({ name: e, schema: t, required: r, resources: n, resourceType: o, properties: i, config: a }) {
  let s, c = "value";
  switch (Je(t)) {
    case "resource":
      s = /* @__PURE__ */ l(Dt, { resources: n, resourceType: o });
      break;
    case "enum":
      s = /* @__PURE__ */ l(
        se,
        {
          allowClear: !0,
          options: (t.enum ?? []).map((f) => ({ label: String(f), value: f }))
        }
      );
      break;
    case "number":
      s = /* @__PURE__ */ l(Me, { style: { width: "100%" } });
      break;
    case "boolean":
      s = /* @__PURE__ */ l(it, {}), c = "checked";
      break;
    case "key-value":
      s = /* @__PURE__ */ l(
        Ct,
        {
          testId: `field-${e}`,
          valueOptions: $t(t) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      s = /* @__PURE__ */ l(Ot, { testId: `field-${e}`, suggestions: Et(i, a) });
      break;
    case "json":
      s = /* @__PURE__ */ l(_t, {});
      break;
    default:
      s = /* @__PURE__ */ l(ae, { placeholder: t.description });
      break;
  }
  return /* @__PURE__ */ l(
    be.Item,
    {
      name: e,
      label: t.title ?? e,
      tooltip: t.description,
      valuePropName: c,
      rules: r && c === "value" ? [{ required: !0, message: `请填写 ${t.title ?? e}` }] : [],
      children: s
    },
    e
  );
}
function At(e) {
  const {
    node: t,
    descriptor: r,
    edges: n,
    catalog: o,
    resources: i,
    issues: a,
    onApplyConfig: s,
    onDeleteNode: c,
    onDeleteEdge: f,
    onUpdateEdge: g,
    onSelectNode: w
  } = e, [P] = be.useForm(), [x, I] = S("{}"), [D, $] = S(null);
  Z(() => {
    P.setFieldsValue((t == null ? void 0 : t.config) ?? {}), I(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), $(null);
  }, [P, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const J = new Set((r == null ? void 0 : r.configurationSchema.required) ?? []), k = Object.entries((r == null ? void 0 : r.configurationSchema.properties) ?? {}), b = () => {
    if (k.length > 0) {
      P.validateFields().then((p) => s(It((t == null ? void 0 : t.config) ?? {}, p)));
      return;
    }
    try {
      const p = JSON.parse(x);
      if (p === null || typeof p != "object" || Array.isArray(p))
        throw new Error("配置必须是 JSON 对象");
      $(null), s(p);
    } catch (p) {
      $(p.message);
    }
  }, E = n.map((p, y) => ({
    ...p,
    index: y,
    key: `${y}:${p.source}->${p.target}`
  })), R = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (p, y) => /* @__PURE__ */ m(_.Text, { style: { fontSize: 11 }, children: [
        y.source,
        " → ",
        y.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (p, y) => {
        const C = o.get(y.source), K = o.get(y.target);
        return /* @__PURE__ */ m("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ l(
            se,
            {
              size: "small",
              value: y.sourcePort,
              style: { width: 110 },
              onChange: (M) => g(y.index, { ...y, sourcePort: M }),
              options: ((C == null ? void 0 : C.outputs) ?? []).map((M) => ({ label: `${M.name} (${M.type})`, value: M.name }))
            }
          ),
          /* @__PURE__ */ l(
            se,
            {
              size: "small",
              value: y.targetPort,
              style: { width: 110 },
              onChange: (M) => g(y.index, { ...y, targetPort: M }),
              options: ((K == null ? void 0 : K.inputs) ?? []).map((M) => ({ label: `${M.name} (${M.type})`, value: M.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (p, y) => /* @__PURE__ */ l(G, { danger: !0, size: "small", type: "text", onClick: () => f(y.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ m("div", { className: "industrial-properties-body", children: [
    !t && /* @__PURE__ */ l(Le, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ m(he, { children: [
      /* @__PURE__ */ l(_.Text, { strong: !0, children: (r == null ? void 0 : r.name) ?? t.type }),
      /* @__PURE__ */ l(oe, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ l(be, { form: P, layout: "vertical", size: "small", style: { marginTop: 12 }, children: k.map(([p, y]) => /* @__PURE__ */ l(
        Rt,
        {
          name: p,
          schema: y,
          required: J.has(p),
          resources: i,
          resourceType: typeof (r == null ? void 0 : r.configurationSchema.resourceType) == "string" ? r.configurationSchema.resourceType : void 0,
          properties: (r == null ? void 0 : r.configurationSchema.properties) ?? {},
          config: (t == null ? void 0 : t.config) ?? {}
        },
        p
      )) }),
      k.length === 0 && /* @__PURE__ */ m(he, { children: [
        /* @__PURE__ */ l(_.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ l(
          ae.TextArea,
          {
            rows: 8,
            value: x,
            style: { marginTop: 6 },
            onChange: (p) => I(p.target.value)
          }
        ),
        D && /* @__PURE__ */ l(_.Text, { type: "danger", style: { fontSize: 12 }, children: D })
      ] }),
      /* @__PURE__ */ l(G, { type: "primary", block: !0, onClick: b, children: "应用配置" }),
      /* @__PURE__ */ l(G, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: c, children: "删除节点" }),
      (r == null ? void 0 : r.activityType) && /* @__PURE__ */ l(_.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: r.activityType })
    ] }),
    /* @__PURE__ */ m($e, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      n.length,
      ")"
    ] }),
    n.length === 0 ? /* @__PURE__ */ l(_.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ l(Be, { size: "small", columns: R, dataSource: E, pagination: !1 }),
    a.length > 0 && /* @__PURE__ */ m(he, { children: [
      /* @__PURE__ */ m($e, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        a.length,
        ")"
      ] }),
      a.map((p, y) => /* @__PURE__ */ m("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ l(oe, { color: "red", children: p.code }),
        /* @__PURE__ */ m(
          _.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const C = /^nodes\.([^.]+)/.exec(p.path);
              C && w(C[1]);
            },
            children: [
              p.path,
              ": ",
              p.message
            ]
          }
        )
      ] }, `${p.path}-${y}`))
    ] })
  ] });
}
const zt = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class jt extends Error {
  constructor(t) {
    super(t), this.name = "DtoError";
  }
}
const A = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, H = (e) => Array.isArray(e) ? e : [], h = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", de = (e, t = 0) => {
  const r = typeof e == "string" ? Number(e) : e;
  return typeof r == "number" && Number.isFinite(r) ? r : t;
}, u = (e, ...t) => {
  for (const r of t)
    if (e[r] !== void 0 && e[r] !== null) return e[r];
};
function le(e) {
  const t = A(e);
  if (Object.prototype.hasOwnProperty.call(t, "data")) return t.data;
  if (typeof t.code == "string" && typeof t.message == "string")
    throw new jt(`${t.code}: ${t.message}`);
  return e;
}
function Ft(e) {
  const t = h(e).trim().toLowerCase();
  return zt.find((n) => n.toLowerCase() === t) ?? "Unknown";
}
function Lt(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const t = A(e), r = h(u(t, "name", "port", "id")).trim();
  return r ? {
    name: r,
    type: Ft(u(t, "type", "portType", "dataType")),
    required: u(t, "required", "isRequired") === !0
  } : null;
}
function ze(e) {
  const t = H(e).map(Lt).filter((n) => n !== null), r = /* @__PURE__ */ new Set();
  return t.filter((n) => r.has(n.name) ? !1 : (r.add(n.name), !0));
}
const Mt = (e) => {
  const t = A(e);
  return Object.keys(t).length === 0 ? { type: "object", properties: {} } : t;
};
function Gt(e) {
  const t = A(e), r = h(u(t, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return r ? {
    nodeTypeId: r,
    name: h(u(t, "name", "title"), r),
    description: h(u(t, "description", "desc")),
    category: h(u(t, "category", "group"), "General"),
    icon: h(u(t, "icon"), "AppstoreOutlined"),
    version: h(u(t, "version"), "1.0.0"),
    inputs: ze(u(t, "inputs", "inputPorts", "input_ports")),
    outputs: ze(u(t, "outputs", "outputPorts", "output_ports")),
    configurationSchema: Mt(u(t, "configurationSchema", "configSchema", "schema")),
    activityType: h(u(t, "activityType", "activity_type")),
    dependencies: H(u(t, "dependencies")).map((n) => h(n)).filter(Boolean)
  } : null;
}
function Bt(e) {
  const t = le(e), r = Array.isArray(t) ? t : H(u(A(t), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return r.map(Gt).filter((o) => o !== null).filter((o) => n.has(o.nodeTypeId) ? !1 : (n.add(o.nodeTypeId), !0));
}
function Wt(e) {
  const t = le(e), r = Array.isArray(t) ? t : H(u(A(t), "resources", "items")), n = /* @__PURE__ */ new Set();
  return r.map((o) => {
    const i = A(o), a = h(u(i, "id")).trim();
    return !a || n.has(a) ? null : (n.add(a), {
      id: a,
      name: h(u(i, "name"), a),
      type: h(u(i, "type"), "unknown"),
      enabled: i.enabled !== !1,
      credentialId: typeof i.credentialId == "string" ? i.credentialId : null,
      credentialName: typeof i.credentialName == "string" ? i.credentialName : null
    });
  }).filter((o) => o !== null);
}
function Ye(e) {
  const t = A(e), r = h(u(t, "workflowId", "workflow_id", "id")).trim();
  return r ? {
    workflowId: r,
    name: h(u(t, "name", "title"), r),
    version: de(u(t, "version"), 1)
  } : null;
}
function qt(e) {
  const t = le(e);
  return (Array.isArray(t) ? t : H(u(A(t), "workflows", "items"))).map(Ye).filter((n) => n !== null);
}
function ve(e) {
  const t = A(e), r = {};
  for (const [n, o] of Object.entries(t))
    o !== void 0 && (r[n] = o);
  return r;
}
function Ut(e) {
  const t = A(e), r = h(u(t, "id", "nodeId", "node_id")).trim();
  if (!r) return null;
  const n = A(u(t, "position", "layout"));
  return {
    id: r,
    type: h(u(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: ve(u(t, "config", "configuration")),
    x: de(u(t, "x") ?? u(n, "x"), 0),
    y: de(u(t, "y") ?? u(n, "y"), 0)
  };
}
function Jt(e) {
  const t = A(e), r = h(u(t, "source", "from", "sourceId")).trim(), n = h(u(t, "target", "to", "targetId")).trim();
  return !r || !n ? null : {
    source: r,
    sourcePort: h(u(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: h(u(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function Yt(e) {
  const t = le(e), r = A(t), n = Ye(r) ?? { workflowId: "", name: "", version: 1 }, o = H(u(r, "nodes")).map(Ut).filter((c) => c !== null), i = H(u(r, "edges")).map(Jt).filter((c) => c !== null), a = A(u(r, "trigger")), s = {
    kind: h(u(a, "kind", "type"), "manual"),
    cron: u(a, "cron") === void 0 ? null : h(a.cron),
    eventName: u(a, "eventName", "event_name") === void 0 ? null : h(u(a, "eventName", "event_name")),
    config: ve(u(a, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: o,
    edges: i,
    trigger: s,
    resourceReferences: H(u(r, "resourceReferences", "resource_references")).map((c) => h(c)).filter(Boolean),
    layout: ve(u(r, "layout"))
  };
}
function Ht(e) {
  if (e == null) return null;
  const t = le(e), r = A(t);
  if (Object.keys(r).length === 0) return null;
  const n = H(u(r, "nodes")).slice(0, 64).map((o) => {
    const i = A(o);
    return {
      nodeId: h(u(i, "nodeId", "node_id"), "—"),
      status: h(u(i, "status"), "—"),
      records: de(u(i, "records")),
      files: de(u(i, "files")),
      error: u(i, "error") == null ? null : h(u(i, "error"), void 0),
      samples: H(u(i, "samples")).slice(0, 3).map((a) => h(a))
    };
  });
  return {
    status: h(u(r, "status"), "—"),
    error: u(r, "error") == null ? null : h(u(r, "error"), void 0),
    executed: H(u(r, "executed")).map((o) => h(o)),
    instanceId: u(r, "instanceId") == null ? null : h(u(r, "instanceId"), void 0),
    definitionId: u(r, "definitionId") == null ? null : h(u(r, "definitionId"), void 0),
    nodes: n
  };
}
const { Header: Kt, Content: Vt } = st;
function Qt({ ctx: e }) {
  const [t, r] = at.useMessage(), [n, o] = S([]), [i, a] = S([]), [s, c] = S([]), [f, g] = S([]), [w, P] = S(""), [x, I] = S(1), [D, $] = S(!0), [J, k] = S([]), [b, E] = S(() => De()), [R, p] = S(null), [y, C] = S([]), [K, M] = S(!1), [pe, Se] = S(!0), [He, re] = S(!1), [ce, V] = S(!1), [W, Ke] = S(null), Ne = X(1);
  Z(() => {
    let d = !0;
    return Promise.allSettled([
      e.hubQuery("industrial/nodes"),
      e.hubQuery("industrial/workflows"),
      e.hubQuery("industrial/resources")
    ]).then(([v, z, T]) => {
      var q;
      if (!d) return;
      const O = [];
      if (v.status === "fulfilled" ? o(Bt(v.value)) : O.push(`节点描述: ${String(v.reason)}`), z.status === "fulfilled") {
        const j = qt(z.value);
        a(j);
        const F = new URLSearchParams(window.location.search).get("workflowId"), U = F && j.some((ee) => ee.workflowId === F) ? F : (q = j[0]) == null ? void 0 : q.workflowId;
        U && Te(U);
      } else O.push(`工作流: ${String(z.reason)}`);
      if (T.status === "fulfilled") {
        const j = Wt(T.value);
        g(j);
        const F = j.map((U) => U.id);
        c(F), P((U) => U || F[0] || "");
      } else O.push(`资源: ${String(T.reason)}`);
      k(O);
    }).finally(() => {
      d && $(!1);
    }), () => {
      d = !1;
    };
  }, [e]);
  const Y = Fe(
    () => new Map(n.map((d) => [d.nodeTypeId, d])),
    [n]
  ), me = b.nodes.find((d) => d.id === R) ?? null, ie = Q(async (d, v, z) => {
    M(!0);
    try {
      return await B().api.hubCommand(d, v), t.success(z), !0;
    } catch (T) {
      return t.error(`${d} failed: ${T instanceof Error ? T.message : String(T)}`), !1;
    } finally {
      M(!1);
    }
  }, [t]), Te = Q(async (d) => {
    try {
      const v = await e.hubQuery("industrial/workflow-definitions"), T = (le(v) ?? []).find((j) => {
        const F = j;
        return String((F == null ? void 0 : F.workflowId) ?? "") === d;
      }), O = T === void 0 ? { ...De(d), name: d } : Yt(T);
      E(O), I(Number(O.layout.lookbackDays ?? 1) || 1);
      const q = O.nodes.map((j) => /^n(\d+)$/.exec(j.id)).filter(Boolean).reduce((j, F) => Math.max(j, Number(F[1])), 0);
      Ne.current = q + 1, p(null), re(!1), C([]), V(!1);
    } catch (v) {
      t.error(`加载工作流失败: ${v instanceof Error ? v.message : String(v)}`);
    }
  }, [e, t]), fe = Q(async () => {
    try {
      const d = Ht(await e.hubQuery("industrial/execution"));
      if (d === null) return;
      Ke(d);
    } catch {
    }
  }, [e]), Ve = Q(async () => {
    const d = {
      ...b,
      layout: { ...b.layout, lookbackDays: x }
    };
    await ie("industrial/save-workflow", d, "已保存") && (E(d), V(!1));
  }, [ie, b, x]), Qe = Q(async () => {
    if (ce) {
      t.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await ie("industrial/publish-workflow", { workflowId: b.workflowId }, "已发布") && await fe();
  }, [ie, b.workflowId, ce, t, fe]), Xe = Q(async () => {
    if (ce) {
      t.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await ie(
      "industrial/run-workflow",
      { workflowId: b.workflowId, resourceId: w },
      "采集已启动"
    ) && await fe();
  }, [ie, b.workflowId, ce, t, fe, w]), Ze = Q((d, v, z) => {
    const T = ne(Y, d);
    if (!T) return;
    const O = `n${Ne.current++}`;
    E((q) => ({
      ...q,
      nodes: [...q.nodes, wt(T, O, v, z)]
    })), p(O), re(!0), C([]), V(!0);
  }, [Y]), et = Q((d, v) => {
    const z = b.nodes.find((te) => te.id === d), T = b.nodes.find((te) => te.id === v);
    if (!z || !T)
      return t.warning("连线失败：端点不在画布上"), null;
    const O = ne(Y, z.type), q = ne(Y, T.type);
    if (!O || !q) return null;
    let j = z, F = T, U = Re(O, q);
    if (!U) {
      const te = Re(q, O);
      if (!te)
        return t.warning(`端口类型不兼容：${O.name} → ${q.name}`), null;
      j = T, F = z, U = te;
    }
    const ee = xt(b, {
      source: j.id,
      sourcePort: U.sourcePort,
      target: F.id,
      targetPort: U.targetPort
    }, Y);
    return ee.issue ? (t.warning(`${ee.issue.code}: ${ee.issue.message}`), C((te) => [...te, ee.issue]), null) : ee.definition === b ? (t.info("这条连线已经存在"), null) : (E(ee.definition), V(!0), U);
  }, [Y, b, t]), tt = Q(() => {
    const d = Pt(b, Y, new Set(s));
    C(d), d.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : (t.warning(`发现 ${d.length} 个问题，见属性抽屉定位列表`), re(!0));
  }, [b, Y, s, t]), rt = Q((d, v) => {
    E((z) => ({
      ...z,
      edges: z.edges.map((T, O) => O === d ? v : T)
    })), V(!0);
  }, []);
  return D ? /* @__PURE__ */ l(Ge, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ m("div", { className: "industrial-designer", children: [
    r,
    /* @__PURE__ */ m(Kt, { className: "industrial-header", children: [
      /* @__PURE__ */ m("div", { className: "industrial-title", children: [
        /* @__PURE__ */ l(_.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ l(_.Text, { type: "secondary", children: b.name || b.workflowId }),
        ce && /* @__PURE__ */ l(oe, { color: "orange", children: "未保存" }),
        W && /* @__PURE__ */ l(oe, { color: W.error ? "error" : "success", children: W.status })
      ] }),
      /* @__PURE__ */ m("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ l(
          G,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": pe,
            onClick: () => Se((d) => !d),
            children: pe ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ l(
          G,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => re((d) => !d),
            children: "属性"
          }
        ),
        /* @__PURE__ */ l("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ m(Ee, { size: 4, children: [
          /* @__PURE__ */ l(G, { size: "small", loading: K, onClick: () => void Ve(), children: "保存" }),
          /* @__PURE__ */ l(G, { size: "small", onClick: tt, children: "校验" }),
          /* @__PURE__ */ l(G, { size: "small", loading: K, onClick: () => void Qe(), children: "发布" }),
          /* @__PURE__ */ l(G, { size: "small", type: "primary", loading: K, onClick: () => void Xe(), children: "运行" })
        ] }),
        /* @__PURE__ */ l(
          se,
          {
            className: "industrial-resource-select",
            size: "small",
            value: w,
            onChange: P,
            placeholder: "运行资源",
            options: s.map((d) => ({ value: d, label: d }))
          }
        ),
        /* @__PURE__ */ l(
          Me,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: x,
            onChange: (d) => {
              I(Number(d) || 1), V(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ m(Vt, { className: `industrial-body${pe ? "" : " industrial-body--collapsed"}`, children: [
      pe ? /* @__PURE__ */ m("aside", { className: "industrial-palette", children: [
        i.length > 0 && /* @__PURE__ */ l(
          se,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: b.workflowId,
            placeholder: "选择工作流",
            onChange: (d) => void Te(d),
            options: i.map((d) => ({ value: d.workflowId, label: d.name }))
          }
        ),
        /* @__PURE__ */ l(mt, { descriptors: n, loading: !1 })
      ] }) : /* @__PURE__ */ l("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ l(G, { type: "text", size: "small", onClick: () => Se(!0), children: "节点库" }) }),
      /* @__PURE__ */ m("section", { className: "industrial-center", children: [
        J.length > 0 && /* @__PURE__ */ l(
          Pe,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: J.join("；")
          }
        ),
        /* @__PURE__ */ l(
          yt,
          {
            definition: b,
            catalog: Y,
            selectedId: R,
            onSelect: (d) => {
              p(d), d && re(!0);
            },
            onDropNode: Ze,
            onCreateEdge: et,
            onPositions: (d) => {
              E((v) => Nt(v, d)), V(!0);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ m(
      ot,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: He,
        mask: !1,
        getContainer: !1,
        onClose: () => re(!1),
        children: [
          W && /* @__PURE__ */ l(
            Pe,
            {
              style: { marginBottom: 12 },
              type: W.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${W.status}`,
              description: /* @__PURE__ */ m(Ee, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                W.error && /* @__PURE__ */ l("span", { children: W.error }),
                W.executed.length > 0 && /* @__PURE__ */ m("span", { children: [
                  "已执行节点: ",
                  W.executed.join(", ")
                ] }),
                W.nodes.length > 0 && /* @__PURE__ */ l(
                  Be,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: W.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (d, v) => v.error ?? (v.samples.length > 0 ? v.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ l(
            At,
            {
              node: me,
              descriptor: me ? ne(Y, me.type) ?? null : null,
              edges: b.edges,
              catalog: Y,
              resources: f,
              issues: y,
              onApplyConfig: (d) => {
                R && (E((v) => St(v, R, d)), V(!0));
              },
              onDeleteNode: () => {
                E((d) => kt(d, R ?? "")), p(null), V(!0);
              },
              onDeleteEdge: (d) => {
                E((v) => vt(v, d)), V(!0);
              },
              onUpdateEdge: rt,
              onSelectNode: (d) => {
                p(d), re(!0);
              }
            }
          )
        ]
      }
    )
  ] });
}
const Xt = `
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
function Zt(e) {
  nt(e);
  const t = typeof document > "u" ? null : document.createElement("style");
  t && (t.dataset.industrialDesigner = "true", t.textContent = Xt, document.head.appendChild(t));
  const r = ({ ctx: o }) => je(Qt, { ctx: o });
  r.displayName = "IndustrialWorkflowCanvas";
  const n = e.blocks.register("industrial-workflow-canvas", r);
  return () => {
    n == null || n(), t == null || t.remove();
  };
}
export {
  Zt as default,
  Zt as register
};
