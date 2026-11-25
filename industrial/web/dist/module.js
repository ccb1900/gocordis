let ce = null;
function rt(e) {
  ce = e;
}
function B() {
  if (!ce && typeof globalThis < "u" && (ce = globalThis.__CORDIS_CONSOLE ?? null), !ce)
    throw new Error("[industrial-designer] console facade is not available");
  return ce;
}
const l = (...e) => B().jsxRuntime.jsx(...e), m = (...e) => B().jsxRuntime.jsxs(...e), me = (e) => B().jsxRuntime.jsx(B().jsxRuntime.Fragment, e), je = (...e) => B().React.createElement(...e), V = (...e) => B().React.useCallback(...e), X = (...e) => B().React.useEffect(...e), Fe = (...e) => B().React.useMemo(...e), Q = (...e) => B().React.useRef(...e), N = (...e) => B().React.useState(...e), T = (e) => {
  const t = (r) => {
    const n = e.split(".").reduce((o, i) => o == null ? void 0 : o[i], B().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return je(n, r ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, $e = T("Alert"), G = T("Button"), Ee = T("Divider"), nt = T("Drawer"), Le = T("Empty"), we = Object.assign(T("Form"), {
  Item: T("Form.Item"),
  useForm: (...e) => B().antd.Form.useForm(...e)
}), ae = Object.assign(T("Input"), {
  Number: T("InputNumber"),
  TextArea: T("Input.TextArea")
}), Me = T("InputNumber"), ot = Object.assign(T("Layout"), {
  Header: T("Layout.Header"),
  Content: T("Layout.Content")
});
T("Modal");
const se = T("Select"), st = T("Space"), Ge = T("Spin"), it = T("Switch"), at = T("Table"), oe = T("Tag"), D = Object.assign(T("Typography"), {
  Title: T("Typography.Title"),
  Text: T("Typography.Text")
}), lt = new Proxy({}, {
  get: (e, t) => (...r) => {
    var o;
    const n = (o = B().antd.message) == null ? void 0 : o[t];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${t} unavailable`);
    return n(...r);
  }
}), Be = 148, We = 46, Ce = {
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
function ct(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function ut(e, t = /* @__PURE__ */ new Map(), r) {
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
      id: ct(n),
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
function dt(e, t, r) {
  const n = r && r.length > 0 ? new Set(r) : null, o = {};
  for (const i of e.nodes) {
    if (!t.has(i.id) || n && !n.has(i.id)) continue;
    const a = i.style.x, s = i.style.y;
    typeof a == "number" && typeof s == "number" && (o[i.id] = { x: a, y: s });
  }
  return o;
}
function pt(e, t) {
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
        size: [Be, We],
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
    behaviors: qe()
  };
}
const ft = "__industrial_drag_target__", gt = "__industrial_connect_source__", he = (e, t) => {
  var r;
  return (((r = e.isPanModifier) == null ? void 0 : r.call(e)) ?? !1) || t.button === 1 || t.buttons === 4;
}, De = (e, t) => {
  var g;
  if (t.targetType !== "node" || !((g = t.target) != null && g.id)) return !1;
  const r = t.client ?? t.canvas;
  if (!r || !e.graph) return !1;
  const n = String(t.target.id), o = e.graph.getNodeData().find((h) => String(h.id) === n), i = o == null ? void 0 : o.style;
  if (typeof (i == null ? void 0 : i.x) != "number" || typeof i.y != "number") return !1;
  const [a, s] = e.graph.getCanvasByClient([r.x, r.y]), c = Math.abs(a - i.x), f = Math.abs(s - i.y);
  return Math.abs(c - Be / 2) <= 14 && f <= We / 2 + 10;
};
function qe(e = {}) {
  const t = e.connectionState ?? { armed: !1 };
  return [
    "zoom-canvas",
    {
      type: "drag-canvas",
      enable: (r) => r.targetType === "canvas" || he(e, r),
      range: 0.9
    },
    {
      type: "drag-element",
      state: ft,
      enable: (r) => (r.targetType === "node" || r.targetType === "combo") && !De(e, r) && !he(e, r),
      onFinish: (r) => {
        var a, s;
        const n = e.graph;
        if (!n) return;
        const o = Array.isArray(r) ? r : r ? [String(r)] : void 0, i = n.getNodeData().map((c) => ({
          id: String(c.id),
          data: c.data ?? {},
          style: c.style ?? {}
        }));
        (s = e.onPositions) == null || s.call(e, dt(
          { nodes: i },
          ((a = e.allowedIds) == null ? void 0 : a.call(e)) ?? /* @__PURE__ */ new Set(),
          o
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: gt,
      enable: (r) => he(e, r) ? (t.armed = !1, !1) : t.armed ? r.targetType === "node" || r.targetType === "combo" : De(e, r) ? (t.armed = !0, !0) : !1,
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
const yt = (e) => {
  var r;
  const t = e;
  return (t == null ? void 0 : t.targetType) === "node" ? (r = t.target) == null ? void 0 : r.id : void 0;
};
function mt(e) {
  const {
    definition: t,
    catalog: r,
    selectedId: n,
    onSelect: o,
    onDropNode: i,
    onCreateEdge: a,
    onPositions: s
  } = e, c = Q(null), f = Q(null), g = Q(!1), h = Q(!1), P = Q({ armed: !1 }), b = Q({ onSelect: o, onDropNode: i, onCreateEdge: a, onPositions: s });
  b.current = { onSelect: o, onDropNode: i, onCreateEdge: a, onPositions: s };
  const v = Fe(
    () => ut(t, r, n),
    [t, r, n]
  ), _ = Q(v);
  _.current = v;
  const $ = Q(/* @__PURE__ */ new Set());
  return $.current = new Set(t.nodes.map((x) => x.id)), X(() => {
    const x = (R) => {
      R.code === "Space" && !R.repeat && (h.current = !0);
    }, w = (R) => {
      R.code === "Space" && (h.current = !1);
    }, E = () => {
      h.current = !1;
    };
    return window.addEventListener("keydown", x), window.addEventListener("keyup", w), window.addEventListener("blur", E), () => {
      window.removeEventListener("keydown", x), window.removeEventListener("keyup", w), window.removeEventListener("blur", E);
    };
  }, []), X(() => {
    const x = c.current;
    if (!x || g.current) return;
    let w = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: E }) => {
      if (w || !c.current || g.current) return;
      const R = pt(_.current, x), p = new E({
        ...R,
        behaviors: qe({
          graph: {
            getNodeData: () => {
              var y;
              return ((y = f.current) == null ? void 0 : y.getNodeData()) ?? [];
            },
            getCanvasByClient: (y) => {
              var Y;
              const C = ((Y = f.current) == null ? void 0 : Y.getCanvasByClient(y)) ?? y;
              return [C[0], C[1]];
            }
          },
          isPanModifier: () => h.current,
          connectionState: P.current,
          allowedIds: () => $.current,
          onPositions: (y) => b.current.onPositions(y),
          onCreateEdge: (y, C) => b.current.onCreateEdge(y, C)
        })
      });
      p.on("node:click", (y) => {
        const C = yt(y);
        C && b.current.onSelect(C);
      }), p.on("edge:click", () => b.current.onSelect(null)), p.on("canvas:click", () => b.current.onSelect(null)), p.on("pointerup", () => {
        P.current.armed = !1;
      }), f.current = p, x.__industrialGraph = p, g.current = !0, p.render();
    }), () => {
      w = !0;
    };
  }, []), X(() => {
    const x = f.current;
    !x || !g.current || (x.setData(v), x.draw());
  }, [v]), X(() => () => {
    var x;
    (x = f.current) == null || x.destroy(), f.current = null, g.current = !1;
  }, []), /* @__PURE__ */ l("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ m("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ l(
      "div",
      {
        ref: c,
        className: "industrial-graph-host",
        onDragOver: (x) => x.preventDefault(),
        onDrop: (x) => {
          x.preventDefault();
          const w = x.dataTransfer.getData("application/x-industrial-node") || x.dataTransfer.getData("text/plain");
          if (!w) return;
          const E = f.current, R = c.current;
          if (!E || !R) return;
          const p = R.getBoundingClientRect(), y = E.getCanvasByViewport([
            x.clientX - p.left,
            x.clientY - p.top
          ]);
          b.current.onDropNode(w, y[0], y[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ l("div", { className: "industrial-graph-hint", children: "拖动节点移动 · 从端口拖出连线 · 拖动画布平移（空格或中键可在节点上平移）" })
  ] }) });
}
function ht({ descriptors: e, loading: t, error: r }) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const a = i.category.split(".")[0] || "General", s = n.get(a) ?? [];
    s.push(i), n.set(a, s);
  }
  const o = (i, a) => {
    i.dataTransfer.setData("application/x-industrial-node", a.nodeTypeId), i.dataTransfer.setData("text/plain", a.nodeTypeId), i.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ m("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ l(D.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ l(D.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ l(Ge, { size: "small", style: { display: "block", marginTop: 16 } }),
    r && /* @__PURE__ */ l(D.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: r }),
    !t && !r && e.length === 0 && /* @__PURE__ */ l(Le, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(n.entries()).map(([i, a]) => /* @__PURE__ */ m("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ l(D.Text, { strong: !0, style: { fontSize: 12 }, children: i }),
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
              /* @__PURE__ */ l(D.Text, { type: "secondary", style: { fontSize: 11 }, children: s.nodeTypeId })
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
const _e = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {}
});
function wt(e) {
  const t = {};
  for (const [r, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (t[r] = n.default);
  return t;
}
function bt(e, t, r = 0, n = 0) {
  return { id: t, type: e.nodeTypeId, config: wt(e), x: r, y: n };
}
function ne(e, t) {
  return e.get(t);
}
function ge(e, t, r) {
  return e[r].find((n) => n.name === t);
}
function Se(e, t) {
  return e !== "Unknown" && t !== "Unknown" && e === t;
}
function Re(e, t) {
  for (const r of e.outputs)
    for (const n of t.inputs)
      if (Se(r.type, n.type))
        return { sourcePort: r.name, targetPort: n.name };
  return null;
}
const F = (e, t, r) => ({ path: e, code: t, message: r });
function xt(e, t) {
  return e.source === t.source && e.sourcePort === t.sourcePort && e.target === t.target && e.targetPort === t.targetPort;
}
function kt(e, t, r) {
  const n = `edges.${e.edges.length}`, o = e.nodes.find((g) => g.id === t.source), i = e.nodes.find((g) => g.id === t.target);
  if (!o || !i)
    return { definition: e, issue: F(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((g) => xt(g, t)))
    return { definition: e };
  const a = ne(r, o.type), s = ne(r, i.type);
  if (!a || !s)
    return { definition: e, issue: F(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const c = ge(a, t.sourcePort, "outputs"), f = ge(s, t.targetPort, "inputs");
  return !c || !f ? { definition: e, issue: F(n, "PORT_MISSING", "declared port does not exist") } : Se(c.type, f.type) ? { definition: { ...e, edges: [...e.edges, t] } } : {
    definition: e,
    issue: F(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${c.type} → ${f.type} is not a legal connection`
    )
  };
}
function vt(e, t) {
  return {
    ...e,
    nodes: e.nodes.filter((r) => r.id !== t),
    edges: e.edges.filter((r) => r.source !== t && r.target !== t)
  };
}
function St(e, t) {
  return { ...e, edges: e.edges.filter((r, n) => n !== t) };
}
function Nt(e, t) {
  const r = { ...e };
  for (const [n, o] of Object.entries(t))
    o === void 0 ? delete r[n] : r[n] = o;
  return r;
}
function Tt(e, t, r) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === t ? { ...n, config: r } : n)
  };
}
function It(e, t) {
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
function Pt(e) {
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
function $t(e, t, r) {
  const n = [];
  e.workflowId.trim() || n.push(F("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(F("workflow", "INVALID_VERSION", "version must be positive"));
  const o = /* @__PURE__ */ new Map();
  for (const s of e.nodes)
    o.has(s.id) && n.push(F(`nodes.${s.id}`, "DUPLICATE_NODE_ID", `duplicate node '${s.id}'`)), o.set(s.id, s);
  const i = /* @__PURE__ */ new Map();
  for (const s of e.nodes) {
    const c = ne(t, s.type);
    if (!c) {
      n.push(F(
        `nodes.${s.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${s.type}' is not registered`
      ));
      continue;
    }
    i.set(s.id, c);
    const f = c.configurationSchema.required ?? [];
    for (const g of f) {
      const h = s.config[g];
      (h == null || h === "") && n.push(F(
        `nodes.${s.id}.config.${g}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${g}' is missing`
      ));
    }
    r && typeof s.config.resourceId == "string" && (r.has(s.config.resourceId) || n.push(F(
      `nodes.${s.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${s.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((s, c) => {
    const f = o.get(s.source), g = o.get(s.target);
    if (!f || !g) {
      n.push(F(`edges.${c}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const h = i.get(s.source), P = i.get(s.target);
    if (!h || !P) return;
    const b = ge(h, s.sourcePort, "outputs"), v = ge(P, s.targetPort, "inputs");
    if (!b || !v) {
      n.push(F(`edges.${c}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    Se(b.type, v.type) || n.push(F(
      `edges.${c}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${b.type} → ${v.type} is not a legal connection`
    ));
  });
  for (const s of e.nodes) {
    const c = i.get(s.id);
    if (c)
      for (const f of c.inputs.filter((g) => g.required))
        e.edges.some((g) => g.target === s.id && g.targetPort === f.name && o.has(g.source)) || n.push(F(
          `nodes.${s.id}.inputs.${f.name}`,
          "MISSING_INPUT",
          `required input '${f.name}' is not connected`
        ));
  }
  const a = Pt(e);
  return a && n.push(F(`nodes.${a[0]}`, "GRAPH_CYCLE", `cycle: ${a.join(" → ")}`)), n;
}
const be = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function Et(e) {
  const t = be(e.additionalProperties).enum;
  if (!Array.isArray(t)) return null;
  const r = t.filter((n) => typeof n == "string");
  return r.length > 0 ? r : null;
}
function Ue(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && be(e.items).type === "string" ? "string-list" : e.type === "object" && be(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function xe(e) {
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
function fe(e) {
  if (!Array.isArray(e)) return [];
  const t = /* @__PURE__ */ new Set(), r = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const o = n.trim();
    !o || t.has(o) || (t.add(o), r.push(o));
  }
  return r;
}
function Ct(e, t) {
  const r = /* @__PURE__ */ new Set();
  for (const [n, o] of Object.entries(e))
    if (Ue(o) === "key-value")
      for (const i of xe(t[n]))
        i.key.trim() && r.add(i.key.trim());
  return [...r];
}
function Ot({
  value: e,
  onChange: t,
  valueOptions: r,
  keyTitle: n = "目标字段",
  valueTitle: o = "来源字段",
  testId: i
}) {
  const a = JSON.stringify(e ?? {}), [s, c] = N(() => xe(e)), [f, g] = N(a);
  X(() => {
    a !== f && (c(xe(e)), g(a));
  }, [a, f, e]);
  const h = (b) => {
    c(b), t == null || t(Ae(b).record);
  }, { duplicateKeys: P } = Ae(s);
  return /* @__PURE__ */ m("div", { "data-testid": i, className: "industrial-kv-editor", children: [
    s.map((b, v) => /* @__PURE__ */ m("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ l(
        ae,
        {
          "aria-label": `${n} ${v + 1}`,
          value: b.key,
          placeholder: n,
          onChange: (_) => h(s.map(($, U) => U === v ? { ...$, key: _.target.value } : $))
        }
      ),
      r && r.length > 0 ? /* @__PURE__ */ l(
        se,
        {
          "aria-label": `${o} ${v + 1}`,
          value: b.value || void 0,
          placeholder: o,
          options: r.map((_) => ({ label: _, value: _ })),
          onChange: (_) => h(s.map(($, U) => U === v ? { ...$, value: _ ?? "" } : $))
        }
      ) : /* @__PURE__ */ l(
        ae,
        {
          "aria-label": `${o} ${v + 1}`,
          value: b.value,
          placeholder: o,
          onChange: (_) => h(s.map(($, U) => U === v ? { ...$, value: _.target.value } : $))
        }
      ),
      /* @__PURE__ */ l(
        G,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${v + 1}`,
          onClick: () => h(s.filter((_, $) => $ !== v)),
          children: "删"
        }
      )
    ] }, v)),
    /* @__PURE__ */ l(
      G,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => h([...s, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    P.length > 0 && /* @__PURE__ */ m(D.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      P.join("、")
    ] })
  ] });
}
function Dt({ value: e, onChange: t, suggestions: r = [], testId: n }) {
  const o = JSON.stringify(fe(e)), [i, a] = N(() => fe(e)), [s, c] = N(o);
  X(() => {
    o !== s && (a(fe(e)), c(o));
  }, [o, s, e]);
  const f = (g) => {
    a(g), t == null || t(fe(g));
  };
  return /* @__PURE__ */ m("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ l("datalist", { id: `${n ?? "list"}-suggestions`, children: r.map((g) => /* @__PURE__ */ l("option", { value: g }, g)) }),
    i.map((g, h) => /* @__PURE__ */ m("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ l(
        ae,
        {
          "aria-label": `字段 ${h + 1}`,
          value: g,
          list: `${n ?? "list"}-suggestions`,
          onChange: (P) => f(i.map((b, v) => v === h ? P.target.value : b))
        }
      ),
      /* @__PURE__ */ l(
        G,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${h + 1}`,
          onClick: () => f(i.filter((P, b) => b !== h)),
          children: "删"
        }
      )
    ] }, h)),
    /* @__PURE__ */ l(G, { size: "small", type: "dashed", block: !0, onClick: () => f([...i, ""]), children: "添加字段" }),
    i.length === 0 && /* @__PURE__ */ l(D.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function _t({ value: e, onChange: t }) {
  const r = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, o] = N(r), [i, a] = N(null);
  X(() => {
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
    i && /* @__PURE__ */ l(D.Text, { type: "danger", style: { fontSize: 11 }, children: i })
  ] });
}
function Rt({ value: e, onChange: t, resources: r, resourceType: n }) {
  const o = r.filter((a) => a.enabled !== !1 && (!n || a.type === n)).map((a) => ({
    value: a.id,
    label: `${a.name} (${a.type})`
  })), i = r.find((a) => a.id === e);
  return /* @__PURE__ */ m("div", { children: [
    /* @__PURE__ */ l(se, { allowClear: !0, value: e, onChange: t, options: o }),
    (i == null ? void 0 : i.credentialId) && /* @__PURE__ */ m(D.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      i.credentialName ?? i.credentialId
    ] })
  ] });
}
function At({ name: e, schema: t, required: r, resources: n, resourceType: o, properties: i, config: a }) {
  let s, c = "value";
  switch (Ue(t)) {
    case "resource":
      s = /* @__PURE__ */ l(Rt, { resources: n, resourceType: o });
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
        Ot,
        {
          testId: `field-${e}`,
          valueOptions: Et(t) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      s = /* @__PURE__ */ l(Dt, { testId: `field-${e}`, suggestions: Ct(i, a) });
      break;
    case "json":
      s = /* @__PURE__ */ l(_t, {});
      break;
    default:
      s = /* @__PURE__ */ l(ae, { placeholder: t.description });
      break;
  }
  return /* @__PURE__ */ l(
    we.Item,
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
function zt(e) {
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
    onSelectNode: h
  } = e, [P] = we.useForm(), [b, v] = N("{}"), [_, $] = N(null);
  X(() => {
    P.setFieldsValue((t == null ? void 0 : t.config) ?? {}), v(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), $(null);
  }, [P, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const U = new Set((r == null ? void 0 : r.configurationSchema.required) ?? []), x = Object.entries((r == null ? void 0 : r.configurationSchema.properties) ?? {}), w = () => {
    if (x.length > 0) {
      P.validateFields().then((p) => s(Nt((t == null ? void 0 : t.config) ?? {}, p)));
      return;
    }
    try {
      const p = JSON.parse(b);
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
      render: (p, y) => /* @__PURE__ */ m(D.Text, { style: { fontSize: 11 }, children: [
        y.source,
        " → ",
        y.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (p, y) => {
        const C = o.get(y.source), Y = o.get(y.target);
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
              options: ((Y == null ? void 0 : Y.inputs) ?? []).map((M) => ({ label: `${M.name} (${M.type})`, value: M.name }))
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
    t && /* @__PURE__ */ m(me, { children: [
      /* @__PURE__ */ l(D.Text, { strong: !0, children: (r == null ? void 0 : r.name) ?? t.type }),
      /* @__PURE__ */ l(oe, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ l(we, { form: P, layout: "vertical", size: "small", style: { marginTop: 12 }, children: x.map(([p, y]) => /* @__PURE__ */ l(
        At,
        {
          name: p,
          schema: y,
          required: U.has(p),
          resources: i,
          resourceType: typeof (r == null ? void 0 : r.configurationSchema.resourceType) == "string" ? r.configurationSchema.resourceType : void 0,
          properties: (r == null ? void 0 : r.configurationSchema.properties) ?? {},
          config: (t == null ? void 0 : t.config) ?? {}
        },
        p
      )) }),
      x.length === 0 && /* @__PURE__ */ m(me, { children: [
        /* @__PURE__ */ l(D.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ l(
          ae.TextArea,
          {
            rows: 8,
            value: b,
            style: { marginTop: 6 },
            onChange: (p) => v(p.target.value)
          }
        ),
        _ && /* @__PURE__ */ l(D.Text, { type: "danger", style: { fontSize: 12 }, children: _ })
      ] }),
      /* @__PURE__ */ l(G, { type: "primary", block: !0, onClick: w, children: "应用配置" }),
      /* @__PURE__ */ l(G, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: c, children: "删除节点" }),
      (r == null ? void 0 : r.activityType) && /* @__PURE__ */ l(D.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: r.activityType })
    ] }),
    /* @__PURE__ */ m(Ee, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      n.length,
      ")"
    ] }),
    n.length === 0 ? /* @__PURE__ */ l(D.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ l(at, { size: "small", columns: R, dataSource: E, pagination: !1 }),
    a.length > 0 && /* @__PURE__ */ m(me, { children: [
      /* @__PURE__ */ m(Ee, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        a.length,
        ")"
      ] }),
      a.map((p, y) => /* @__PURE__ */ m("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ l(oe, { color: "red", children: p.code }),
        /* @__PURE__ */ m(
          D.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const C = /^nodes\.([^.]+)/.exec(p.path);
              C && h(C[1]);
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
const jt = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class Ft extends Error {
  constructor(t) {
    super(t), this.name = "DtoError";
  }
}
const L = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, te = (e) => Array.isArray(e) ? e : [], S = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", ke = (e, t = 0) => {
  const r = typeof e == "string" ? Number(e) : e;
  return typeof r == "number" && Number.isFinite(r) ? r : t;
}, d = (e, ...t) => {
  for (const r of t)
    if (e[r] !== void 0 && e[r] !== null) return e[r];
};
function ue(e) {
  const t = L(e);
  if (Object.prototype.hasOwnProperty.call(t, "data")) return t.data;
  if (typeof t.code == "string" && typeof t.message == "string")
    throw new Ft(`${t.code}: ${t.message}`);
  return e;
}
function Lt(e) {
  const t = S(e).trim().toLowerCase();
  return jt.find((n) => n.toLowerCase() === t) ?? "Unknown";
}
function Mt(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const t = L(e), r = S(d(t, "name", "port", "id")).trim();
  return r ? {
    name: r,
    type: Lt(d(t, "type", "portType", "dataType")),
    required: d(t, "required", "isRequired") === !0
  } : null;
}
function ze(e) {
  const t = te(e).map(Mt).filter((n) => n !== null), r = /* @__PURE__ */ new Set();
  return t.filter((n) => r.has(n.name) ? !1 : (r.add(n.name), !0));
}
const Gt = (e) => {
  const t = L(e);
  return Object.keys(t).length === 0 ? { type: "object", properties: {} } : t;
};
function Bt(e) {
  const t = L(e), r = S(d(t, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return r ? {
    nodeTypeId: r,
    name: S(d(t, "name", "title"), r),
    description: S(d(t, "description", "desc")),
    category: S(d(t, "category", "group"), "General"),
    icon: S(d(t, "icon"), "AppstoreOutlined"),
    version: S(d(t, "version"), "1.0.0"),
    inputs: ze(d(t, "inputs", "inputPorts", "input_ports")),
    outputs: ze(d(t, "outputs", "outputPorts", "output_ports")),
    configurationSchema: Gt(d(t, "configurationSchema", "configSchema", "schema")),
    activityType: S(d(t, "activityType", "activity_type")),
    dependencies: te(d(t, "dependencies")).map((n) => S(n)).filter(Boolean)
  } : null;
}
function Wt(e) {
  const t = ue(e), r = Array.isArray(t) ? t : te(d(L(t), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return r.map(Bt).filter((o) => o !== null).filter((o) => n.has(o.nodeTypeId) ? !1 : (n.add(o.nodeTypeId), !0));
}
function qt(e) {
  const t = ue(e), r = Array.isArray(t) ? t : te(d(L(t), "resources", "items")), n = /* @__PURE__ */ new Set();
  return r.map((o) => {
    const i = L(o), a = S(d(i, "id")).trim();
    return !a || n.has(a) ? null : (n.add(a), {
      id: a,
      name: S(d(i, "name"), a),
      type: S(d(i, "type"), "unknown"),
      enabled: i.enabled !== !1,
      credentialId: typeof i.credentialId == "string" ? i.credentialId : null,
      credentialName: typeof i.credentialName == "string" ? i.credentialName : null
    });
  }).filter((o) => o !== null);
}
function Je(e) {
  const t = L(e), r = S(d(t, "workflowId", "workflow_id", "id")).trim();
  return r ? {
    workflowId: r,
    name: S(d(t, "name", "title"), r),
    version: ke(d(t, "version"), 1)
  } : null;
}
function Ut(e) {
  const t = ue(e);
  return (Array.isArray(t) ? t : te(d(L(t), "workflows", "items"))).map(Je).filter((n) => n !== null);
}
function ve(e) {
  const t = L(e), r = {};
  for (const [n, o] of Object.entries(t))
    o !== void 0 && (r[n] = o);
  return r;
}
function Jt(e) {
  const t = L(e), r = S(d(t, "id", "nodeId", "node_id")).trim();
  if (!r) return null;
  const n = L(d(t, "position", "layout"));
  return {
    id: r,
    type: S(d(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: ve(d(t, "config", "configuration")),
    x: ke(d(t, "x") ?? d(n, "x"), 0),
    y: ke(d(t, "y") ?? d(n, "y"), 0)
  };
}
function Yt(e) {
  const t = L(e), r = S(d(t, "source", "from", "sourceId")).trim(), n = S(d(t, "target", "to", "targetId")).trim();
  return !r || !n ? null : {
    source: r,
    sourcePort: S(d(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: S(d(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function Ht(e) {
  const t = ue(e), r = L(t), n = Je(r) ?? { workflowId: "", name: "", version: 1 }, o = te(d(r, "nodes")).map(Jt).filter((c) => c !== null), i = te(d(r, "edges")).map(Yt).filter((c) => c !== null), a = L(d(r, "trigger")), s = {
    kind: S(d(a, "kind", "type"), "manual"),
    cron: d(a, "cron") === void 0 ? null : S(a.cron),
    eventName: d(a, "eventName", "event_name") === void 0 ? null : S(d(a, "eventName", "event_name")),
    config: ve(d(a, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: o,
    edges: i,
    trigger: s,
    resourceReferences: te(d(r, "resourceReferences", "resource_references")).map((c) => S(c)).filter(Boolean),
    layout: ve(d(r, "layout"))
  };
}
const { Header: Vt, Content: Kt } = ot;
function Qt({ ctx: e }) {
  var Pe;
  const [t, r] = lt.useMessage(), [n, o] = N([]), [i, a] = N([]), [s, c] = N([]), [f, g] = N([]), [h, P] = N(""), [b, v] = N(1), [_, $] = N(!0), [U, x] = N([]), [w, E] = N(() => _e()), [R, p] = N(null), [y, C] = N([]), [Y, M] = N(!1), [de, Ne] = N(!0), [Ye, re] = N(!1), [le, H] = N(!1), [K, He] = N(null), Te = Q(1);
  X(() => {
    let u = !0;
    return Promise.allSettled([
      e.hubQuery("industrial/nodes"),
      e.hubQuery("industrial/workflows"),
      e.hubQuery("industrial/resources")
    ]).then(([k, A, I]) => {
      var W;
      if (!u) return;
      const O = [];
      if (k.status === "fulfilled" ? o(Wt(k.value)) : O.push(`节点描述: ${String(k.reason)}`), A.status === "fulfilled") {
        const z = Ut(A.value);
        a(z);
        const j = new URLSearchParams(window.location.search).get("workflowId"), q = j && z.some((Z) => Z.workflowId === j) ? j : (W = z[0]) == null ? void 0 : W.workflowId;
        q && Ie(q);
      } else O.push(`工作流: ${String(A.reason)}`);
      if (I.status === "fulfilled") {
        const z = qt(I.value);
        g(z);
        const j = z.map((q) => q.id);
        c(j), P((q) => q || j[0] || "");
      } else O.push(`资源: ${String(I.reason)}`);
      x(O);
    }).finally(() => {
      u && $(!1);
    }), () => {
      u = !1;
    };
  }, [e]);
  const J = Fe(
    () => new Map(n.map((u) => [u.nodeTypeId, u])),
    [n]
  ), ye = w.nodes.find((u) => u.id === R) ?? null, ie = V(async (u, k, A) => {
    M(!0);
    try {
      return await B().api.hubCommand(u, k), t.success(A), !0;
    } catch (I) {
      return t.error(`${u} failed: ${I instanceof Error ? I.message : String(I)}`), !1;
    } finally {
      M(!1);
    }
  }, [t]), Ie = V(async (u) => {
    try {
      const k = await e.hubQuery("industrial/workflow-definitions"), I = (ue(k) ?? []).find((z) => {
        const j = z;
        return String((j == null ? void 0 : j.workflowId) ?? "") === u;
      }), O = I === void 0 ? { ..._e(u), name: u } : Ht(I);
      E(O), v(Number(O.layout.lookbackDays ?? 1) || 1);
      const W = O.nodes.map((z) => /^n(\d+)$/.exec(z.id)).filter(Boolean).reduce((z, j) => Math.max(z, Number(j[1])), 0);
      Te.current = W + 1, p(null), re(!1), C([]), H(!1);
    } catch (k) {
      t.error(`加载工作流失败: ${k instanceof Error ? k.message : String(k)}`);
    }
  }, [e, t]), pe = V(async () => {
    try {
      const u = await e.hubQuery("industrial/execution");
      if (u == null) return;
      const k = u;
      He({ status: String(k.status ?? ""), error: k.error, executed: k.executed ?? [] });
    } catch {
    }
  }, [e]), Ve = V(async () => {
    const u = {
      ...w,
      layout: { ...w.layout, lookbackDays: b }
    };
    await ie("industrial/save-workflow", u, "已保存") && (E(u), H(!1));
  }, [ie, w, b]), Ke = V(async () => {
    if (le) {
      t.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await ie("industrial/publish-workflow", { workflowId: w.workflowId }, "已发布") && await pe();
  }, [ie, w.workflowId, le, t, pe]), Qe = V(async () => {
    if (le) {
      t.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await ie(
      "industrial/run-workflow",
      { workflowId: w.workflowId, resourceId: h },
      "采集已启动"
    ) && await pe();
  }, [ie, w.workflowId, le, t, pe, h]), Xe = V((u, k, A) => {
    const I = ne(J, u);
    if (!I) return;
    const O = `n${Te.current++}`;
    E((W) => ({
      ...W,
      nodes: [...W.nodes, bt(I, O, k, A)]
    })), p(O), re(!0), C([]), H(!0);
  }, [J]), Ze = V((u, k) => {
    const A = w.nodes.find((ee) => ee.id === u), I = w.nodes.find((ee) => ee.id === k);
    if (!A || !I)
      return t.warning("连线失败：端点不在画布上"), null;
    const O = ne(J, A.type), W = ne(J, I.type);
    if (!O || !W) return null;
    let z = A, j = I, q = Re(O, W);
    if (!q) {
      const ee = Re(W, O);
      if (!ee)
        return t.warning(`端口类型不兼容：${O.name} → ${W.name}`), null;
      z = I, j = A, q = ee;
    }
    const Z = kt(w, {
      source: z.id,
      sourcePort: q.sourcePort,
      target: j.id,
      targetPort: q.targetPort
    }, J);
    return Z.issue ? (t.warning(`${Z.issue.code}: ${Z.issue.message}`), C((ee) => [...ee, Z.issue]), null) : Z.definition === w ? (t.info("这条连线已经存在"), null) : (E(Z.definition), H(!0), q);
  }, [J, w, t]), et = V(() => {
    const u = $t(w, J, new Set(s));
    C(u), u.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : (t.warning(`发现 ${u.length} 个问题，见属性抽屉定位列表`), re(!0));
  }, [w, J, s, t]), tt = V((u, k) => {
    E((A) => ({
      ...A,
      edges: A.edges.map((I, O) => O === u ? k : I)
    })), H(!0);
  }, []);
  return _ ? /* @__PURE__ */ l(Ge, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ m("div", { className: "industrial-designer", children: [
    r,
    /* @__PURE__ */ m(Vt, { className: "industrial-header", children: [
      /* @__PURE__ */ m("div", { className: "industrial-title", children: [
        /* @__PURE__ */ l(D.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ l(D.Text, { type: "secondary", children: w.name || w.workflowId }),
        le && /* @__PURE__ */ l(oe, { color: "orange", children: "未保存" }),
        K && /* @__PURE__ */ l(oe, { color: K.error ? "error" : "success", children: K.status })
      ] }),
      /* @__PURE__ */ m("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ l(
          G,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": de,
            onClick: () => Ne((u) => !u),
            children: de ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ l(
          G,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => re((u) => !u),
            children: "属性"
          }
        ),
        /* @__PURE__ */ l("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ m(st, { size: 4, children: [
          /* @__PURE__ */ l(G, { size: "small", loading: Y, onClick: () => void Ve(), children: "保存" }),
          /* @__PURE__ */ l(G, { size: "small", onClick: et, children: "校验" }),
          /* @__PURE__ */ l(G, { size: "small", loading: Y, onClick: () => void Ke(), children: "发布" }),
          /* @__PURE__ */ l(G, { size: "small", type: "primary", loading: Y, onClick: () => void Qe(), children: "运行" })
        ] }),
        /* @__PURE__ */ l(
          se,
          {
            className: "industrial-resource-select",
            size: "small",
            value: h,
            onChange: P,
            placeholder: "运行资源",
            options: s.map((u) => ({ value: u, label: u }))
          }
        ),
        /* @__PURE__ */ l(
          Me,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: b,
            onChange: (u) => {
              v(Number(u) || 1), H(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ m(Kt, { className: `industrial-body${de ? "" : " industrial-body--collapsed"}`, children: [
      de ? /* @__PURE__ */ m("aside", { className: "industrial-palette", children: [
        i.length > 0 && /* @__PURE__ */ l(
          se,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: w.workflowId,
            placeholder: "选择工作流",
            onChange: (u) => void Ie(u),
            options: i.map((u) => ({ value: u.workflowId, label: u.name }))
          }
        ),
        /* @__PURE__ */ l(ht, { descriptors: n, loading: !1 })
      ] }) : /* @__PURE__ */ l("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ l(G, { type: "text", size: "small", onClick: () => Ne(!0), children: "节点库" }) }),
      /* @__PURE__ */ m("section", { className: "industrial-center", children: [
        U.length > 0 && /* @__PURE__ */ l(
          $e,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: U.join("；")
          }
        ),
        /* @__PURE__ */ l(
          mt,
          {
            definition: w,
            catalog: J,
            selectedId: R,
            onSelect: (u) => {
              p(u), u && re(!0);
            },
            onDropNode: Xe,
            onCreateEdge: Ze,
            onPositions: (u) => {
              E((k) => It(k, u)), H(!0);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ m(
      nt,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: Ye,
        mask: !1,
        getContainer: !1,
        onClose: () => re(!1),
        children: [
          K && /* @__PURE__ */ l(
            $e,
            {
              style: { marginBottom: 12 },
              type: K.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${K.status}`,
              description: K.error ?? ((Pe = K.executed) != null && Pe.length ? `已执行节点: ${K.executed.join(", ")}` : void 0)
            }
          ),
          /* @__PURE__ */ l(
            zt,
            {
              node: ye,
              descriptor: ye ? ne(J, ye.type) ?? null : null,
              edges: w.edges,
              catalog: J,
              resources: f,
              issues: y,
              onApplyConfig: (u) => {
                R && (E((k) => Tt(k, R, u)), H(!0));
              },
              onDeleteNode: () => {
                E((u) => vt(u, R ?? "")), p(null), H(!0);
              },
              onDeleteEdge: (u) => {
                E((k) => St(k, u)), H(!0);
              },
              onUpdateEdge: tt,
              onSelectNode: (u) => {
                p(u), re(!0);
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
  rt(e);
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
