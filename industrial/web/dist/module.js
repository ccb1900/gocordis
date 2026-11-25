let le = null;
function Qe(e) {
  le = e;
}
function M() {
  if (!le && typeof globalThis < "u" && (le = globalThis.__CORDIS_CONSOLE ?? null), !le)
    throw new Error("[industrial-designer] console facade is not available");
  return le;
}
const l = (...e) => M().jsxRuntime.jsx(...e), y = (...e) => M().jsxRuntime.jsxs(...e), ge = (e) => M().jsxRuntime.jsx(M().jsxRuntime.Fragment, e), Ae = (...e) => M().React.createElement(...e), Y = (...e) => M().React.useCallback(...e), Q = (...e) => M().React.useEffect(...e), Re = (...e) => M().React.useMemo(...e), K = (...e) => M().React.useRef(...e), S = (...e) => M().React.useState(...e), T = (e) => {
  const t = (r) => {
    const n = e.split(".").reduce((o, a) => o == null ? void 0 : o[a], M().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Ae(n, r ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, Te = T("Alert"), W = T("Button"), Ie = T("Divider"), je = T("Empty"), ye = Object.assign(T("Form"), {
  Item: T("Form.Item"),
  useForm: (...e) => M().antd.Form.useForm(...e)
}), se = Object.assign(T("Input"), {
  Number: T("InputNumber"),
  TextArea: T("Input.TextArea")
}), ze = T("InputNumber"), Xe = Object.assign(T("Layout"), {
  Header: T("Layout.Header"),
  Content: T("Layout.Content")
});
T("Modal");
const re = T("Select"), Pe = T("Space"), Fe = T("Spin"), Ze = T("Switch"), et = T("Table"), te = T("Tag"), E = Object.assign(T("Typography"), {
  Title: T("Typography.Title"),
  Text: T("Typography.Text")
}), tt = new Proxy({}, {
  get: (e, t) => (...r) => {
    var o;
    const n = (o = M().antd.message) == null ? void 0 : o[t];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${t} unavailable`);
    return n(...r);
  }
}), $e = {
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
}, Ee = (e) => $e[e] ?? $e[e.split(".")[0]] ?? "#4d6bfe";
function rt(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function nt(e, t = /* @__PURE__ */ new Map(), r) {
  return {
    nodes: e.nodes.map((n) => {
      const o = t.get(n.type), a = (o == null ? void 0 : o.category) ?? "General", i = r === n.id;
      return {
        id: n.id,
        data: {
          label: (o == null ? void 0 : o.name) ?? n.type,
          nodeTypeId: n.type,
          category: a,
          icon: (o == null ? void 0 : o.icon) ?? "AppstoreOutlined",
          config: { ...n.config }
        },
        style: {
          x: n.x,
          y: n.y,
          fill: Ee(a),
          stroke: i ? "#111111" : Ee(a),
          lineWidth: i ? 3 : 1.5,
          radius: 10,
          port: !0,
          ports: [
            ...((o == null ? void 0 : o.inputs) ?? []).map((s, c, g) => ({
              key: `in:${s.name}`,
              placement: [0, (c + 1) / (g.length + 1)],
              r: 5,
              stroke: s.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((o == null ? void 0 : o.outputs) ?? []).map((s, c, g) => ({
              key: `out:${s.name}`,
              placement: [1, (c + 1) / (g.length + 1)],
              r: 5,
              stroke: s.type === "Control" ? "#52c41a" : "#1677ff",
              fill: "#fff"
            }))
          ]
        }
      };
    }),
    edges: e.edges.map((n) => ({
      id: rt(n),
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
function ot(e, t, r) {
  const n = r && r.length > 0 ? new Set(r) : null, o = {};
  for (const a of e.nodes) {
    if (!t.has(a.id) || n && !n.has(a.id)) continue;
    const i = a.style.x, s = a.style.y;
    typeof i == "number" && typeof s == "number" && (o[a.id] = { x: i, y: s });
  }
  return o;
}
function st(e, t) {
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
        size: [148, 46],
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
    behaviors: Me()
  };
}
const it = "__industrial_drag_target__", at = "__industrial_connect_source__";
function Me(e = {}) {
  const t = () => {
    var r;
    return ((r = e.mode) == null ? void 0 : r.call(e)) ?? "select";
  };
  return [
    "zoom-canvas",
    {
      type: "drag-canvas",
      enable: (r) => t() === "pan" && r.targetType === "canvas",
      range: 0.9
    },
    {
      type: "drag-element",
      state: it,
      enable: (r) => t() === "select" && (r.targetType === "node" || r.targetType === "combo"),
      onFinish: (r) => {
        var i, s;
        const n = e.graph;
        if (!n) return;
        const o = Array.isArray(r) ? r : r ? [String(r)] : void 0, a = n.getNodeData().map((c) => ({
          id: String(c.id),
          data: c.data ?? {},
          style: c.style ?? {}
        }));
        (s = e.onPositions) == null || s.call(e, ot(
          { nodes: a },
          ((i = e.allowedIds) == null ? void 0 : i.call(e)) ?? /* @__PURE__ */ new Set(),
          o
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: at,
      enable: () => t() === "connect",
      onCreate: (r) => {
        var i;
        if (t() !== "connect") return;
        const n = String(r.source ?? ""), o = String(r.target ?? "");
        if (!n || !o || n === o) return;
        const a = (i = e.onCreateEdge) == null ? void 0 : i.call(e, n, o);
        if (a)
          return {
            ...r,
            sourcePort: `out:${a.sourcePort}`,
            targetPort: `in:${a.targetPort}`
          };
      }
    }
  ];
}
const lt = (e) => {
  var r;
  const t = e;
  return (t == null ? void 0 : t.targetType) === "node" ? (r = t.target) == null ? void 0 : r.id : void 0;
}, ct = {
  select: "选择：拖动节点改变位置，点击节点编辑属性",
  connect: "连线：从节点拖到另一个节点，自动匹配兼容端口",
  pan: "平移：拖动画布移动视口（不移动节点）"
};
function ut(e) {
  const {
    definition: t,
    catalog: r,
    selectedId: n,
    mode: o,
    onSelect: a,
    onDropNode: i,
    onCreateEdge: s,
    onPositions: c
  } = e, g = K(null), f = K(null), h = K(!1), I = K(o);
  I.current = o;
  const b = K({ onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: c });
  b.current = { onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: c };
  const v = Re(
    () => nt(t, r, n),
    [t, r, n]
  ), O = K(v);
  O.current = v;
  const $ = K(/* @__PURE__ */ new Set());
  $.current = new Set(t.nodes.map((k) => k.id)), Q(() => {
    const k = g.current;
    if (!k || h.current) return;
    let w = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: C }) => {
      if (w || !g.current || h.current) return;
      const G = st(O.current, k), p = new C({
        ...G,
        behaviors: Me({
          graph: { getNodeData: () => {
            var m;
            return ((m = f.current) == null ? void 0 : m.getNodeData()) ?? [];
          } },
          mode: () => I.current,
          allowedIds: () => $.current,
          onPositions: (m) => b.current.onPositions(m),
          onCreateEdge: (m, D) => b.current.onCreateEdge(m, D)
        })
      });
      p.on("node:click", (m) => {
        const D = lt(m);
        D && I.current === "select" && b.current.onSelect(D);
      }), p.on("edge:click", () => {
        b.current.onSelect(null);
      }), p.on("canvas:click", () => b.current.onSelect(null)), f.current = p, k.__industrialGraph = p, h.current = !0, p.render();
    }), () => {
      w = !0;
    };
  }, []), Q(() => {
    const k = f.current;
    !k || !h.current || (k.setData(v), k.draw());
  }, [v]), Q(() => () => {
    var k;
    (k = f.current) == null || k.destroy(), f.current = null, h.current = !1;
  }, []);
  const L = (k) => {
    if (k.preventDefault(), I.current !== "select") return;
    const w = k.dataTransfer.getData("application/x-industrial-node") || k.dataTransfer.getData("text/plain");
    if (!w) return;
    const C = f.current, G = g.current;
    if (!C || !G) return;
    const p = G.getBoundingClientRect(), m = C.getCanvasByViewport([
      k.clientX - p.left,
      k.clientY - p.top
    ]);
    b.current.onDropNode(w, m[0], m[1]);
  };
  return /* @__PURE__ */ l("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ y("div", { className: `industrial-graph industrial-graph--${o}`, children: [
    /* @__PURE__ */ l(
      "div",
      {
        ref: g,
        className: "industrial-graph-host",
        onDragOver: (k) => k.preventDefault(),
        onDrop: L,
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ l("div", { className: "industrial-graph-hint", children: ct[o] })
  ] }) });
}
function dt({ descriptors: e, loading: t, error: r }) {
  const n = /* @__PURE__ */ new Map();
  for (const a of e) {
    const i = a.category.split(".")[0] || "General", s = n.get(i) ?? [];
    s.push(a), n.set(i, s);
  }
  const o = (a, i) => {
    a.dataTransfer.setData("application/x-industrial-node", i.nodeTypeId), a.dataTransfer.setData("text/plain", i.nodeTypeId), a.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ y("aside", { className: "industrial-palette", children: [
    /* @__PURE__ */ l(E.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ l(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ l(Fe, { size: "small", style: { display: "block", marginTop: 16 } }),
    r && /* @__PURE__ */ l(E.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: r }),
    !t && !r && e.length === 0 && /* @__PURE__ */ l(je, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(n.entries()).map(([a, i]) => /* @__PURE__ */ y("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ l(E.Text, { strong: !0, style: { fontSize: 12 }, children: a }),
      i.map((s) => /* @__PURE__ */ y(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          onDragStart: (c) => o(c, s),
          title: s.description || s.nodeTypeId,
          children: [
            /* @__PURE__ */ l("div", { className: "industrial-palette-node-title", children: s.name }),
            /* @__PURE__ */ y("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ l(te, { bordered: !1, color: "blue", children: s.version }),
              /* @__PURE__ */ l(E.Text, { type: "secondary", style: { fontSize: 11 }, children: s.nodeTypeId })
            ] }),
            /* @__PURE__ */ y("div", { className: "industrial-palette-node-ports", children: [
              s.inputs.map((c) => /* @__PURE__ */ y(te, { bordered: !1, color: "default", children: [
                "in ",
                c.name,
                ": ",
                c.type
              ] }, `in-${c.name}`)),
              s.outputs.map((c) => /* @__PURE__ */ y(te, { bordered: !1, color: "green", children: [
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
    ] }, a))
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
function pt(e) {
  const t = {};
  for (const [r, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (t[r] = n.default);
  return t;
}
function ft(e, t, r = 0, n = 0) {
  return { id: t, type: e.nodeTypeId, config: pt(e), x: r, y: n };
}
function ee(e, t) {
  return e.get(t);
}
function pe(e, t, r) {
  return e[r].find((n) => n.name === t);
}
function xe(e, t) {
  return e !== "Unknown" && t !== "Unknown" && e === t;
}
function Oe(e, t) {
  for (const r of e.outputs)
    for (const n of t.inputs)
      if (xe(r.type, n.type))
        return { sourcePort: r.name, targetPort: n.name };
  return null;
}
const j = (e, t, r) => ({ path: e, code: t, message: r });
function gt(e, t) {
  return e.source === t.source && e.sourcePort === t.sourcePort && e.target === t.target && e.targetPort === t.targetPort;
}
function yt(e, t, r) {
  const n = `edges.${e.edges.length}`, o = e.nodes.find((f) => f.id === t.source), a = e.nodes.find((f) => f.id === t.target);
  if (!o || !a)
    return { definition: e, issue: j(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((f) => gt(f, t)))
    return { definition: e };
  const i = ee(r, o.type), s = ee(r, a.type);
  if (!i || !s)
    return { definition: e, issue: j(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const c = pe(i, t.sourcePort, "outputs"), g = pe(s, t.targetPort, "inputs");
  return !c || !g ? { definition: e, issue: j(n, "PORT_MISSING", "declared port does not exist") } : xe(c.type, g.type) ? { definition: { ...e, edges: [...e.edges, t] } } : {
    definition: e,
    issue: j(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${c.type} → ${g.type} is not a legal connection`
    )
  };
}
function mt(e, t) {
  return {
    ...e,
    nodes: e.nodes.filter((r) => r.id !== t),
    edges: e.edges.filter((r) => r.source !== t && r.target !== t)
  };
}
function ht(e, t) {
  return { ...e, edges: e.edges.filter((r, n) => n !== t) };
}
function bt(e, t) {
  const r = { ...e };
  for (const [n, o] of Object.entries(t))
    o === void 0 ? delete r[n] : r[n] = o;
  return r;
}
function wt(e, t, r) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === t ? { ...n, config: r } : n)
  };
}
function xt(e, t) {
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
function vt(e) {
  var a;
  const t = /* @__PURE__ */ new Map();
  for (const i of e.nodes) t.set(i.id, []);
  for (const i of e.edges)
    (a = t.get(i.source)) == null || a.push(i.target);
  const r = /* @__PURE__ */ new Map(), n = [], o = (i) => {
    r.set(i, 1), n.push(i);
    for (const s of t.get(i) ?? []) {
      if (r.get(s) === 1)
        return [...n.slice(n.indexOf(s)), s];
      if (r.get(s) !== 2) {
        const c = o(s);
        if (c) return c;
      }
    }
    return n.pop(), r.set(i, 2), null;
  };
  for (const i of e.nodes)
    if (r.get(i.id) !== 2) {
      const s = o(i.id);
      if (s) return s;
    }
  return null;
}
function kt(e, t, r) {
  const n = [];
  e.workflowId.trim() || n.push(j("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(j("workflow", "INVALID_VERSION", "version must be positive"));
  const o = /* @__PURE__ */ new Map();
  for (const s of e.nodes)
    o.has(s.id) && n.push(j(`nodes.${s.id}`, "DUPLICATE_NODE_ID", `duplicate node '${s.id}'`)), o.set(s.id, s);
  const a = /* @__PURE__ */ new Map();
  for (const s of e.nodes) {
    const c = ee(t, s.type);
    if (!c) {
      n.push(j(
        `nodes.${s.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${s.type}' is not registered`
      ));
      continue;
    }
    a.set(s.id, c);
    const g = c.configurationSchema.required ?? [];
    for (const f of g) {
      const h = s.config[f];
      (h == null || h === "") && n.push(j(
        `nodes.${s.id}.config.${f}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${f}' is missing`
      ));
    }
    r && typeof s.config.resourceId == "string" && (r.has(s.config.resourceId) || n.push(j(
      `nodes.${s.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${s.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((s, c) => {
    const g = o.get(s.source), f = o.get(s.target);
    if (!g || !f) {
      n.push(j(`edges.${c}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const h = a.get(s.source), I = a.get(s.target);
    if (!h || !I) return;
    const b = pe(h, s.sourcePort, "outputs"), v = pe(I, s.targetPort, "inputs");
    if (!b || !v) {
      n.push(j(`edges.${c}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    xe(b.type, v.type) || n.push(j(
      `edges.${c}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${b.type} → ${v.type} is not a legal connection`
    ));
  });
  for (const s of e.nodes) {
    const c = a.get(s.id);
    if (c)
      for (const g of c.inputs.filter((f) => f.required))
        e.edges.some((f) => f.target === s.id && f.targetPort === g.name && o.has(f.source)) || n.push(j(
          `nodes.${s.id}.inputs.${g.name}`,
          "MISSING_INPUT",
          `required input '${g.name}' is not connected`
        ));
  }
  const i = vt(e);
  return i && n.push(j(`nodes.${i[0]}`, "GRAPH_CYCLE", `cycle: ${i.join(" → ")}`)), n;
}
const me = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function Nt(e) {
  const t = me(e.additionalProperties).enum;
  if (!Array.isArray(t)) return null;
  const r = t.filter((n) => typeof n == "string");
  return r.length > 0 ? r : null;
}
function Le(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && me(e.items).type === "string" ? "string-list" : e.type === "object" && me(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function he(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, t]) => t != null && typeof t != "object").map(([t, r]) => ({ key: t, value: String(r) }));
}
function Ce(e) {
  const t = {}, r = [];
  for (const n of e) {
    const o = n.key.trim();
    o && (Object.prototype.hasOwnProperty.call(t, o) && r.push(o), t[o] = n.value);
  }
  return { record: t, duplicateKeys: r };
}
function de(e) {
  if (!Array.isArray(e)) return [];
  const t = /* @__PURE__ */ new Set(), r = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const o = n.trim();
    !o || t.has(o) || (t.add(o), r.push(o));
  }
  return r;
}
function St(e, t) {
  const r = /* @__PURE__ */ new Set();
  for (const [n, o] of Object.entries(e))
    if (Le(o) === "key-value")
      for (const a of he(t[n]))
        a.key.trim() && r.add(a.key.trim());
  return [...r];
}
function Tt({
  value: e,
  onChange: t,
  valueOptions: r,
  keyTitle: n = "目标字段",
  valueTitle: o = "来源字段",
  testId: a
}) {
  const i = JSON.stringify(e ?? {}), [s, c] = S(() => he(e)), [g, f] = S(i);
  Q(() => {
    i !== g && (c(he(e)), f(i));
  }, [i, g, e]);
  const h = (b) => {
    c(b), t == null || t(Ce(b).record);
  }, { duplicateKeys: I } = Ce(s);
  return /* @__PURE__ */ y("div", { "data-testid": a, className: "industrial-kv-editor", children: [
    s.map((b, v) => /* @__PURE__ */ y("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ l(
        se,
        {
          "aria-label": `${n} ${v + 1}`,
          value: b.key,
          placeholder: n,
          onChange: (O) => h(s.map(($, L) => L === v ? { ...$, key: O.target.value } : $))
        }
      ),
      r && r.length > 0 ? /* @__PURE__ */ l(
        re,
        {
          "aria-label": `${o} ${v + 1}`,
          value: b.value || void 0,
          placeholder: o,
          options: r.map((O) => ({ label: O, value: O })),
          onChange: (O) => h(s.map(($, L) => L === v ? { ...$, value: O ?? "" } : $))
        }
      ) : /* @__PURE__ */ l(
        se,
        {
          "aria-label": `${o} ${v + 1}`,
          value: b.value,
          placeholder: o,
          onChange: (O) => h(s.map(($, L) => L === v ? { ...$, value: O.target.value } : $))
        }
      ),
      /* @__PURE__ */ l(
        W,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${v + 1}`,
          onClick: () => h(s.filter((O, $) => $ !== v)),
          children: "删"
        }
      )
    ] }, v)),
    /* @__PURE__ */ l(
      W,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => h([...s, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    I.length > 0 && /* @__PURE__ */ y(E.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      I.join("、")
    ] })
  ] });
}
function It({ value: e, onChange: t, suggestions: r = [], testId: n }) {
  const o = JSON.stringify(de(e)), [a, i] = S(() => de(e)), [s, c] = S(o);
  Q(() => {
    o !== s && (i(de(e)), c(o));
  }, [o, s, e]);
  const g = (f) => {
    i(f), t == null || t(de(f));
  };
  return /* @__PURE__ */ y("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ l("datalist", { id: `${n ?? "list"}-suggestions`, children: r.map((f) => /* @__PURE__ */ l("option", { value: f }, f)) }),
    a.map((f, h) => /* @__PURE__ */ y("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ l(
        se,
        {
          "aria-label": `字段 ${h + 1}`,
          value: f,
          list: `${n ?? "list"}-suggestions`,
          onChange: (I) => g(a.map((b, v) => v === h ? I.target.value : b))
        }
      ),
      /* @__PURE__ */ l(
        W,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${h + 1}`,
          onClick: () => g(a.filter((I, b) => b !== h)),
          children: "删"
        }
      )
    ] }, h)),
    /* @__PURE__ */ l(W, { size: "small", type: "dashed", block: !0, onClick: () => g([...a, ""]), children: "添加字段" }),
    a.length === 0 && /* @__PURE__ */ l(E.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Pt({ value: e, onChange: t }) {
  const r = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, o] = S(r), [a, i] = S(null);
  Q(() => {
    o(r), i(null);
  }, [r]);
  const s = (c) => {
    o(c);
    try {
      const g = JSON.parse(c);
      i(null), t == null || t(g);
    } catch (g) {
      i(g.message);
    }
  };
  return /* @__PURE__ */ y("div", { children: [
    /* @__PURE__ */ l(se.TextArea, { rows: 5, value: n, onChange: (c) => s(c.target.value) }),
    a && /* @__PURE__ */ l(E.Text, { type: "danger", style: { fontSize: 11 }, children: a })
  ] });
}
function $t({ value: e, onChange: t, resources: r, resourceType: n }) {
  const o = r.filter((i) => i.enabled !== !1 && (!n || i.type === n)).map((i) => ({
    value: i.id,
    label: `${i.name} (${i.type})`
  })), a = r.find((i) => i.id === e);
  return /* @__PURE__ */ y("div", { children: [
    /* @__PURE__ */ l(re, { allowClear: !0, value: e, onChange: t, options: o }),
    (a == null ? void 0 : a.credentialId) && /* @__PURE__ */ y(E.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      a.credentialName ?? a.credentialId
    ] })
  ] });
}
function Et({ name: e, schema: t, required: r, resources: n, resourceType: o, properties: a, config: i }) {
  let s, c = "value";
  switch (Le(t)) {
    case "resource":
      s = /* @__PURE__ */ l($t, { resources: n, resourceType: o });
      break;
    case "enum":
      s = /* @__PURE__ */ l(
        re,
        {
          allowClear: !0,
          options: (t.enum ?? []).map((g) => ({ label: String(g), value: g }))
        }
      );
      break;
    case "number":
      s = /* @__PURE__ */ l(ze, { style: { width: "100%" } });
      break;
    case "boolean":
      s = /* @__PURE__ */ l(Ze, {}), c = "checked";
      break;
    case "key-value":
      s = /* @__PURE__ */ l(
        Tt,
        {
          testId: `field-${e}`,
          valueOptions: Nt(t) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      s = /* @__PURE__ */ l(It, { testId: `field-${e}`, suggestions: St(a, i) });
      break;
    case "json":
      s = /* @__PURE__ */ l(Pt, {});
      break;
    default:
      s = /* @__PURE__ */ l(se, { placeholder: t.description });
      break;
  }
  return /* @__PURE__ */ l(
    ye.Item,
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
function _t(e) {
  const {
    node: t,
    descriptor: r,
    edges: n,
    catalog: o,
    resources: a,
    issues: i,
    onApplyConfig: s,
    onDeleteNode: c,
    onDeleteEdge: g,
    onUpdateEdge: f,
    onSelectNode: h
  } = e, [I] = ye.useForm(), [b, v] = S("{}"), [O, $] = S(null);
  Q(() => {
    I.setFieldsValue((t == null ? void 0 : t.config) ?? {}), v(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), $(null);
  }, [I, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const L = new Set((r == null ? void 0 : r.configurationSchema.required) ?? []), k = Object.entries((r == null ? void 0 : r.configurationSchema.properties) ?? {}), w = () => {
    if (k.length > 0) {
      I.validateFields().then((p) => s(bt((t == null ? void 0 : t.config) ?? {}, p)));
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
  }, C = n.map((p, m) => ({
    ...p,
    index: m,
    key: `${m}:${p.source}->${p.target}`
  })), G = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (p, m) => /* @__PURE__ */ y(E.Text, { style: { fontSize: 11 }, children: [
        m.source,
        " → ",
        m.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (p, m) => {
        const D = o.get(m.source), Z = o.get(m.target);
        return /* @__PURE__ */ y("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ l(
            re,
            {
              size: "small",
              value: m.sourcePort,
              style: { width: 110 },
              onChange: (F) => f(m.index, { ...m, sourcePort: F }),
              options: ((D == null ? void 0 : D.outputs) ?? []).map((F) => ({ label: `${F.name} (${F.type})`, value: F.name }))
            }
          ),
          /* @__PURE__ */ l(
            re,
            {
              size: "small",
              value: m.targetPort,
              style: { width: 110 },
              onChange: (F) => f(m.index, { ...m, targetPort: F }),
              options: ((Z == null ? void 0 : Z.inputs) ?? []).map((F) => ({ label: `${F.name} (${F.type})`, value: F.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (p, m) => /* @__PURE__ */ l(W, { danger: !0, size: "small", type: "text", onClick: () => g(m.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ y("aside", { className: "industrial-properties", children: [
    /* @__PURE__ */ l(E.Title, { level: 5, style: { marginTop: 0 }, children: "节点属性" }),
    !t && /* @__PURE__ */ l(je, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ y(ge, { children: [
      /* @__PURE__ */ l(E.Text, { strong: !0, children: (r == null ? void 0 : r.name) ?? t.type }),
      /* @__PURE__ */ l(te, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ l(ye, { form: I, layout: "vertical", size: "small", style: { marginTop: 12 }, children: k.map(([p, m]) => /* @__PURE__ */ l(
        Et,
        {
          name: p,
          schema: m,
          required: L.has(p),
          resources: a,
          resourceType: typeof (r == null ? void 0 : r.configurationSchema.resourceType) == "string" ? r.configurationSchema.resourceType : void 0,
          properties: (r == null ? void 0 : r.configurationSchema.properties) ?? {},
          config: (t == null ? void 0 : t.config) ?? {}
        },
        p
      )) }),
      k.length === 0 && /* @__PURE__ */ y(ge, { children: [
        /* @__PURE__ */ l(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ l(
          se.TextArea,
          {
            rows: 8,
            value: b,
            style: { marginTop: 6 },
            onChange: (p) => v(p.target.value)
          }
        ),
        O && /* @__PURE__ */ l(E.Text, { type: "danger", style: { fontSize: 12 }, children: O })
      ] }),
      /* @__PURE__ */ l(W, { type: "primary", block: !0, onClick: w, children: "应用配置" }),
      /* @__PURE__ */ l(W, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: c, children: "删除节点" }),
      (r == null ? void 0 : r.activityType) && /* @__PURE__ */ l(E.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: r.activityType })
    ] }),
    /* @__PURE__ */ y(Ie, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      n.length,
      ")"
    ] }),
    n.length === 0 ? /* @__PURE__ */ l(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "切换到“连线”模式，从节点拖到另一个节点；也可以在这里编辑端口。" }) : /* @__PURE__ */ l(et, { size: "small", columns: G, dataSource: C, pagination: !1 }),
    i.length > 0 && /* @__PURE__ */ y(ge, { children: [
      /* @__PURE__ */ y(Ie, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        i.length,
        ")"
      ] }),
      i.map((p, m) => /* @__PURE__ */ y("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ l(te, { color: "red", children: p.code }),
        /* @__PURE__ */ y(
          E.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const D = /^nodes\.([^.]+)/.exec(p.path);
              D && h(D[1]);
            },
            children: [
              p.path,
              ": ",
              p.message
            ]
          }
        )
      ] }, `${p.path}-${m}`))
    ] })
  ] });
}
const Ot = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class Ct extends Error {
  constructor(t) {
    super(t), this.name = "DtoError";
  }
}
const z = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, X = (e) => Array.isArray(e) ? e : [], N = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", be = (e, t = 0) => {
  const r = typeof e == "string" ? Number(e) : e;
  return typeof r == "number" && Number.isFinite(r) ? r : t;
}, d = (e, ...t) => {
  for (const r of t)
    if (e[r] !== void 0 && e[r] !== null) return e[r];
};
function ce(e) {
  const t = z(e);
  if (Object.prototype.hasOwnProperty.call(t, "data")) return t.data;
  if (typeof t.code == "string" && typeof t.message == "string")
    throw new Ct(`${t.code}: ${t.message}`);
  return e;
}
function Dt(e) {
  const t = N(e).trim().toLowerCase();
  return Ot.find((n) => n.toLowerCase() === t) ?? "Unknown";
}
function At(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const t = z(e), r = N(d(t, "name", "port", "id")).trim();
  return r ? {
    name: r,
    type: Dt(d(t, "type", "portType", "dataType")),
    required: d(t, "required", "isRequired") === !0
  } : null;
}
function De(e) {
  const t = X(e).map(At).filter((n) => n !== null), r = /* @__PURE__ */ new Set();
  return t.filter((n) => r.has(n.name) ? !1 : (r.add(n.name), !0));
}
const Rt = (e) => {
  const t = z(e);
  return Object.keys(t).length === 0 ? { type: "object", properties: {} } : t;
};
function jt(e) {
  const t = z(e), r = N(d(t, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return r ? {
    nodeTypeId: r,
    name: N(d(t, "name", "title"), r),
    description: N(d(t, "description", "desc")),
    category: N(d(t, "category", "group"), "General"),
    icon: N(d(t, "icon"), "AppstoreOutlined"),
    version: N(d(t, "version"), "1.0.0"),
    inputs: De(d(t, "inputs", "inputPorts", "input_ports")),
    outputs: De(d(t, "outputs", "outputPorts", "output_ports")),
    configurationSchema: Rt(d(t, "configurationSchema", "configSchema", "schema")),
    activityType: N(d(t, "activityType", "activity_type")),
    dependencies: X(d(t, "dependencies")).map((n) => N(n)).filter(Boolean)
  } : null;
}
function zt(e) {
  const t = ce(e), r = Array.isArray(t) ? t : X(d(z(t), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return r.map(jt).filter((o) => o !== null).filter((o) => n.has(o.nodeTypeId) ? !1 : (n.add(o.nodeTypeId), !0));
}
function Ft(e) {
  const t = ce(e), r = Array.isArray(t) ? t : X(d(z(t), "resources", "items")), n = /* @__PURE__ */ new Set();
  return r.map((o) => {
    const a = z(o), i = N(d(a, "id")).trim();
    return !i || n.has(i) ? null : (n.add(i), {
      id: i,
      name: N(d(a, "name"), i),
      type: N(d(a, "type"), "unknown"),
      enabled: a.enabled !== !1,
      credentialId: typeof a.credentialId == "string" ? a.credentialId : null,
      credentialName: typeof a.credentialName == "string" ? a.credentialName : null
    });
  }).filter((o) => o !== null);
}
function Ge(e) {
  const t = z(e), r = N(d(t, "workflowId", "workflow_id", "id")).trim();
  return r ? {
    workflowId: r,
    name: N(d(t, "name", "title"), r),
    version: be(d(t, "version"), 1)
  } : null;
}
function Mt(e) {
  const t = ce(e);
  return (Array.isArray(t) ? t : X(d(z(t), "workflows", "items"))).map(Ge).filter((n) => n !== null);
}
function we(e) {
  const t = z(e), r = {};
  for (const [n, o] of Object.entries(t))
    o !== void 0 && (r[n] = o);
  return r;
}
function Lt(e) {
  const t = z(e), r = N(d(t, "id", "nodeId", "node_id")).trim();
  if (!r) return null;
  const n = z(d(t, "position", "layout"));
  return {
    id: r,
    type: N(d(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: we(d(t, "config", "configuration")),
    x: be(d(t, "x") ?? d(n, "x"), 0),
    y: be(d(t, "y") ?? d(n, "y"), 0)
  };
}
function Gt(e) {
  const t = z(e), r = N(d(t, "source", "from", "sourceId")).trim(), n = N(d(t, "target", "to", "targetId")).trim();
  return !r || !n ? null : {
    source: r,
    sourcePort: N(d(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: N(d(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function Bt(e) {
  const t = ce(e), r = z(t), n = Ge(r) ?? { workflowId: "", name: "", version: 1 }, o = X(d(r, "nodes")).map(Lt).filter((c) => c !== null), a = X(d(r, "edges")).map(Gt).filter((c) => c !== null), i = z(d(r, "trigger")), s = {
    kind: N(d(i, "kind", "type"), "manual"),
    cron: d(i, "cron") === void 0 ? null : N(i.cron),
    eventName: d(i, "eventName", "event_name") === void 0 ? null : N(d(i, "eventName", "event_name")),
    config: we(d(i, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: o,
    edges: a,
    trigger: s,
    resourceReferences: X(d(r, "resourceReferences", "resource_references")).map((c) => N(c)).filter(Boolean),
    layout: we(d(r, "layout"))
  };
}
const { Header: Wt, Content: qt } = Xe, Ut = {
  select: "选择",
  connect: "连线",
  pan: "平移"
};
function Jt({ ctx: e }) {
  var Se;
  const [t, r] = tt.useMessage(), [n, o] = S([]), [a, i] = S([]), [s, c] = S([]), [g, f] = S([]), [h, I] = S(""), [b, v] = S(1), [O, $] = S(!0), [L, k] = S([]), [w, C] = S(() => _e()), [G, p] = S(null), [m, D] = S([]), [Z, F] = S(!1), [ve, Be] = S("select"), [ie, J] = S(!1), [V, We] = S(null), ke = K(1);
  Q(() => {
    let u = !0;
    return Promise.allSettled([
      e.hubQuery("industrial/nodes"),
      e.hubQuery("industrial/workflows"),
      e.hubQuery("industrial/resources")
    ]).then(([x, A, P]) => {
      if (!u) return;
      const _ = [];
      if (x.status === "fulfilled" ? o(zt(x.value)) : _.push(`节点描述: ${String(x.reason)}`), A.status === "fulfilled") {
        const R = Mt(A.value);
        i(R), R.length > 0 && Ne(R[0].workflowId);
      } else _.push(`工作流: ${String(A.reason)}`);
      if (P.status === "fulfilled") {
        const R = Ft(P.value);
        f(R);
        const U = R.map((B) => B.id);
        c(U), I((B) => B || U[0] || "");
      } else _.push(`资源: ${String(P.reason)}`);
      k(_);
    }).finally(() => {
      u && $(!1);
    }), () => {
      u = !1;
    };
  }, [e]);
  const q = Re(
    () => new Map(n.map((u) => [u.nodeTypeId, u])),
    [n]
  ), fe = w.nodes.find((u) => u.id === G) ?? null, ne = Y(async (u, x, A) => {
    F(!0);
    try {
      return await M().api.hubCommand(u, x), t.success(A), !0;
    } catch (P) {
      return t.error(`${u} failed: ${P instanceof Error ? P.message : String(P)}`), !1;
    } finally {
      F(!1);
    }
  }, [t]), Ne = Y(async (u) => {
    try {
      const x = await e.hubQuery("industrial/workflow-definitions"), P = (ce(x) ?? []).find((U) => {
        const B = U;
        return String((B == null ? void 0 : B.workflowId) ?? "") === u;
      }), _ = P === void 0 ? { ..._e(u), name: u } : Bt(P);
      C(_), v(Number(_.layout.lookbackDays ?? 1) || 1);
      const R = _.nodes.map((U) => /^n(\d+)$/.exec(U.id)).filter(Boolean).reduce((U, B) => Math.max(U, Number(B[1])), 0);
      ke.current = R + 1, p(null), D([]), J(!1);
    } catch (x) {
      t.error(`加载工作流失败: ${x instanceof Error ? x.message : String(x)}`);
    }
  }, [e, t]), ue = Y(async () => {
    try {
      const u = await e.hubQuery("industrial/execution");
      if (u == null) return;
      const x = u;
      We({ status: String(x.status ?? ""), error: x.error, executed: x.executed ?? [] });
    } catch {
    }
  }, [e]), qe = Y(async () => {
    const u = {
      ...w,
      layout: { ...w.layout, lookbackDays: b }
    };
    await ne("industrial/save-workflow", u, "已保存") && (C(u), J(!1));
  }, [ne, w, b]), Ue = Y(async () => {
    if (ie) {
      t.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await ne("industrial/publish-workflow", { workflowId: w.workflowId }, "已发布") && await ue();
  }, [ne, w.workflowId, ie, t, ue]), Je = Y(async () => {
    if (ie) {
      t.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await ne(
      "industrial/run-workflow",
      { workflowId: w.workflowId, resourceId: h },
      "采集已启动"
    ) && await ue();
  }, [ne, w.workflowId, ie, t, ue, h]), Ye = Y((u, x, A) => {
    const P = ee(q, u);
    if (!P) return;
    const _ = `n${ke.current++}`;
    C((R) => ({
      ...R,
      nodes: [...R.nodes, ft(P, _, x, A)]
    })), p(_), D([]), J(!0);
  }, [q]), Ve = Y((u, x) => {
    const A = w.nodes.find((H) => H.id === u), P = w.nodes.find((H) => H.id === x);
    if (!A || !P)
      return t.warning("连线失败：端点不在画布上"), null;
    const _ = ee(q, A.type), R = ee(q, P.type);
    if (!_ || !R) return null;
    let U = A, B = P, ae = Oe(_, R);
    if (!ae) {
      const H = Oe(R, _);
      if (!H)
        return t.warning(`端口类型不兼容：${_.name} → ${R.name}`), null;
      U = P, B = A, ae = H;
    }
    const oe = yt(w, {
      source: U.id,
      sourcePort: ae.sourcePort,
      target: B.id,
      targetPort: ae.targetPort
    }, q);
    return oe.issue ? (t.warning(`${oe.issue.code}: ${oe.issue.message}`), D((H) => [...H, oe.issue]), null) : oe.definition === w ? (t.info("这条连线已经存在"), null) : (C(oe.definition), J(!0), ae);
  }, [q, w, t]), He = Y(() => {
    const u = kt(w, q, new Set(s));
    D(u), u.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : t.warning(`发现 ${u.length} 个问题，见右侧定位列表`);
  }, [w, q, s, t]), Ke = Y((u, x) => {
    C((A) => ({
      ...A,
      edges: A.edges.map((P, _) => _ === u ? x : P)
    })), J(!0);
  }, []);
  return O ? /* @__PURE__ */ l(Fe, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ y("div", { className: "industrial-designer", children: [
    r,
    /* @__PURE__ */ y(Wt, { className: "industrial-header", children: [
      /* @__PURE__ */ y("div", { className: "industrial-title", children: [
        /* @__PURE__ */ l(E.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ l(E.Text, { type: "secondary", children: w.name || w.workflowId }),
        ie && /* @__PURE__ */ l(te, { color: "orange", children: "未保存" }),
        V && /* @__PURE__ */ l(te, { color: V.error ? "error" : "success", children: V.status })
      ] }),
      /* @__PURE__ */ y("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ l(Pe, { size: 4, children: ["select", "connect", "pan"].map((u) => /* @__PURE__ */ l(
          W,
          {
            type: ve === u ? "primary" : "default",
            onClick: () => Be(u),
            children: Ut[u]
          },
          u
        )) }),
        /* @__PURE__ */ l("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ y(Pe, { size: 4, children: [
          /* @__PURE__ */ l(W, { loading: Z, onClick: () => void qe(), children: "保存" }),
          /* @__PURE__ */ l(W, { onClick: He, children: "校验" }),
          /* @__PURE__ */ l(W, { loading: Z, onClick: () => void Ue(), children: "发布" }),
          /* @__PURE__ */ l(W, { type: "primary", loading: Z, onClick: () => void Je(), children: "运行" })
        ] }),
        /* @__PURE__ */ l(
          re,
          {
            className: "industrial-resource-select",
            value: h,
            onChange: I,
            placeholder: "运行资源",
            options: s.map((u) => ({ value: u, label: u }))
          }
        ),
        /* @__PURE__ */ l(
          ze,
          {
            className: "industrial-backfill-input",
            min: 1,
            max: 365,
            value: b,
            onChange: (u) => {
              v(Number(u) || 1), J(!0);
            },
            addonBefore: "补采天数"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ y(qt, { className: "industrial-body", children: [
      /* @__PURE__ */ y("aside", { className: "industrial-palette", children: [
        a.length > 0 && /* @__PURE__ */ l(
          re,
          {
            className: "industrial-workflow-select",
            value: w.workflowId,
            placeholder: "选择工作流",
            onChange: (u) => void Ne(u),
            options: a.map((u) => ({ value: u.workflowId, label: u.name }))
          }
        ),
        /* @__PURE__ */ l(dt, { descriptors: n, loading: !1 })
      ] }),
      /* @__PURE__ */ y("section", { className: "industrial-center", children: [
        L.length > 0 && /* @__PURE__ */ l(
          Te,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 10 },
            message: "部分设计器数据未加载",
            description: L.join("；")
          }
        ),
        /* @__PURE__ */ l(
          ut,
          {
            definition: w,
            catalog: q,
            selectedId: G,
            mode: ve,
            onSelect: p,
            onDropNode: Ye,
            onCreateEdge: Ve,
            onPositions: (u) => {
              C((x) => xt(x, u)), J(!0);
            }
          }
        )
      ] }),
      /* @__PURE__ */ y("aside", { className: "industrial-properties", children: [
        V && /* @__PURE__ */ l(
          Te,
          {
            style: { marginBottom: 12 },
            type: V.error ? "error" : "success",
            showIcon: !0,
            message: `执行状态: ${V.status}`,
            description: V.error ?? ((Se = V.executed) != null && Se.length ? `已执行节点: ${V.executed.join(", ")}` : void 0)
          }
        ),
        /* @__PURE__ */ l(
          _t,
          {
            node: fe,
            descriptor: fe ? ee(q, fe.type) ?? null : null,
            edges: w.edges,
            catalog: q,
            resources: g,
            issues: m,
            onApplyConfig: (u) => {
              G && (C((x) => wt(x, G, u)), J(!0));
            },
            onDeleteNode: () => {
              C((u) => mt(u, G ?? "")), p(null), J(!0);
            },
            onDeleteEdge: (u) => {
              C((x) => ht(x, u)), J(!0);
            },
            onUpdateEdge: Ke,
            onSelectNode: p
          }
        )
      ] })
    ] })
  ] });
}
const Yt = `
.industrial-designer{display:flex;flex-direction:column;height:min(86vh,900px);min-height:620px;background:#f4f7fb;border:1px solid #e6ebf2;border-radius:12px;overflow:hidden;box-shadow:0 8px 28px rgba(16,37,66,.08)}
.industrial-header{height:auto;min-height:58px;padding:10px 16px;background:#102542;display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap;line-height:normal}
.industrial-title{display:flex;align-items:center;gap:8px;min-width:0;color:#fff}
.industrial-title .ant-typography{color:#fff!important}
.industrial-title .ant-typography-secondary{color:#9db2d0!important}
.industrial-toolbar{display:flex;align-items:center;gap:10px;flex-wrap:wrap}
.industrial-toolbar-divider{width:1px;height:22px;background:rgba(255,255,255,.24)}
.industrial-resource-select{width:180px}
.industrial-backfill-input{width:170px}
.industrial-body{display:grid;grid-template-columns:268px minmax(0,1fr) 348px;gap:0;flex:1;min-height:0;background:#f4f7fb}
.industrial-palette,.industrial-properties{height:100%;overflow:auto;padding:14px;background:#fff}
.industrial-palette{border-right:1px solid #e8edf4}
.industrial-properties{border-left:1px solid #e8edf4}
.industrial-workflow-select{width:100%;margin-bottom:12px}
.industrial-center{display:flex;flex-direction:column;min-width:0;min-height:0;padding:12px;background:#f4f7fb}
.industrial-palette-group{margin-top:14px}
.industrial-palette-group>.ant-typography{display:block;margin-bottom:6px;color:#5c6b80;font-size:11px;letter-spacing:.08em;text-transform:uppercase}
.industrial-palette-node{margin-top:8px;padding:9px 11px;border:1px solid #dae3f0;border-radius:9px;background:#f9fbff;cursor:grab;transition:border-color .12s ease,box-shadow .12s ease,transform .12s ease}
.industrial-palette-node:hover{border-color:#4d6bfe;box-shadow:0 3px 12px rgba(77,107,254,.14);transform:translateY(-1px)}
.industrial-palette-node-title{font-size:13px;font-weight:600;color:#1f2b3d}
.industrial-palette-node-meta,.industrial-palette-node-ports{display:flex;flex-wrap:wrap;gap:4px;margin-top:5px}
.industrial-canvas-wrap{position:relative;flex:1;min-width:0;min-height:0;border:1px solid #dfe7f2;border-radius:10px;background:#fff;overflow:hidden}
.industrial-graph{position:relative;width:100%;height:100%}
.industrial-graph-host{width:100%;height:100%}
.industrial-graph--connect .industrial-graph-host{cursor:crosshair}
.industrial-graph--pan .industrial-graph-host{cursor:grab}
.industrial-graph-hint{position:absolute;left:10px;top:10px;z-index:2;pointer-events:none;padding:3px 9px;border-radius:999px;background:rgba(16,37,66,.78);color:#fff;font-size:11px}
.industrial-edge-ports{display:flex;flex-direction:column;gap:4px}
.industrial-issue{display:flex;flex-direction:column;gap:2px;margin-bottom:8px;font-size:12px}
.industrial-kv-editor,.industrial-list-editor{display:flex;flex-direction:column;gap:5px}
.industrial-kv-row,.industrial-list-row{display:flex;align-items:center;gap:4px}
.industrial-kv-row>*:first-child,.industrial-list-row>*:first-child{flex:1;min-width:0}
.industrial-editor-error{display:block;font-size:11px}
@media(max-width:1100px){.industrial-body{grid-template-columns:220px minmax(0,1fr) 280px}.industrial-resource-select{width:140px}}
`;
function Vt(e) {
  Qe(e);
  const t = typeof document > "u" ? null : document.createElement("style");
  t && (t.dataset.industrialDesigner = "true", t.textContent = Yt, document.head.appendChild(t));
  const r = ({ ctx: o }) => Ae(Jt, { ctx: o });
  r.displayName = "IndustrialWorkflowCanvas";
  const n = e.blocks.register("industrial-workflow-canvas", r);
  return () => {
    n == null || n(), t == null || t.remove();
  };
}
export {
  Vt as default,
  Vt as register
};
