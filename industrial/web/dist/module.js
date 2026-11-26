let ge = null;
function dt(e) {
  ge = e;
}
function J() {
  if (!ge && typeof globalThis < "u" && (ge = globalThis.__CORDIS_CONSOLE ?? null), !ge)
    throw new Error("[industrial-designer] console facade is not available");
  return ge;
}
const i = (...e) => J().jsxRuntime.jsx(...e), b = (...e) => J().jsxRuntime.jsxs(...e), ve = (e) => J().jsxRuntime.jsx(J().jsxRuntime.Fragment, e), ke = (...e) => J().React.createElement(...e), O = (...e) => J().React.useCallback(...e), Y = (...e) => J().React.useEffect(...e), Be = (...e) => J().React.useMemo(...e), re = (...e) => J().React.useRef(...e), w = (...e) => J().React.useState(...e), R = (e) => {
  const t = (r) => {
    const n = e.split(".").reduce((o, a) => o == null ? void 0 : o[a], J().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return ke(n, r ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, pe = R("Alert"), A = R("Button"), Re = R("Divider"), ut = R("Drawer"), Ge = R("Empty"), Se = Object.assign(R("Form"), {
  Item: R("Form.Item"),
  useForm: (...e) => J().antd.Form.useForm(...e)
}), F = Object.assign(R("Input"), {
  Number: R("InputNumber"),
  TextArea: R("Input.TextArea")
}), Ue = R("InputNumber"), pt = Object.assign(R("Layout"), {
  Header: R("Layout.Header"),
  Content: R("Layout.Content")
}), Pe = R("Modal"), oe = R("Select"), Q = R("Space"), We = R("Spin"), Qe = R("Switch"), de = R("Table"), ne = R("Tag"), P = Object.assign(R("Typography"), {
  Title: R("Typography.Title"),
  Text: R("Typography.Text")
}), ft = new Proxy({}, {
  get: (e, t) => (...r) => {
    var o;
    const n = (o = J().antd.message) == null ? void 0 : o[t];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${t} unavailable`);
    return n(...r);
  }
}), Ke = 148, Je = 46, Ae = {
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
}, ze = (e) => Ae[e] ?? Ae[e.split(".")[0]] ?? "#4d6bfe";
function gt(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function yt(e, t = /* @__PURE__ */ new Map(), r) {
  return {
    nodes: e.nodes.map((n) => {
      const o = t.get(n.type), a = (o == null ? void 0 : o.category) ?? "General", s = r === n.id;
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
          fill: ze(a),
          stroke: s ? "#111111" : ze(a),
          lineWidth: s ? 3 : 1.5,
          radius: 10,
          port: !0,
          ports: [
            ...((o == null ? void 0 : o.inputs) ?? []).map((l, c, p) => ({
              key: `in:${l.name}`,
              placement: [0, (c + 1) / (p.length + 1)],
              r: 5,
              stroke: l.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((o == null ? void 0 : o.outputs) ?? []).map((l, c, p) => ({
              key: `out:${l.name}`,
              placement: [1, (c + 1) / (p.length + 1)],
              r: 5,
              stroke: l.type === "Control" ? "#52c41a" : "#1677ff",
              fill: "#fff"
            }))
          ]
        }
      };
    }),
    edges: e.edges.map((n) => ({
      id: gt(n),
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
function mt(e, t, r) {
  const n = r && r.length > 0 ? new Set(r) : null, o = {};
  for (const a of e.nodes) {
    if (!t.has(a.id) || n && !n.has(a.id)) continue;
    const s = a.style.x, l = a.style.y;
    typeof s == "number" && typeof l == "number" && (o[a.id] = { x: s, y: l });
  }
  return o;
}
function ht(e, t) {
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
        size: [Ke, Je],
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
    behaviors: Ye()
  };
}
const wt = "__industrial_drag_target__", bt = "__industrial_connect_source__", Ie = (e, t) => {
  var r;
  return (((r = e.isPanModifier) == null ? void 0 : r.call(e)) ?? !1) || t.button === 1 || t.buttons === 4;
}, je = (e, t) => {
  var h;
  if (t.targetType !== "node" || !((h = t.target) != null && h.id)) return !1;
  const r = t.client ?? t.canvas;
  if (!r || !e.graph) return !1;
  const n = String(t.target.id), o = e.graph.getNodeData().find((I) => String(I.id) === n), a = o == null ? void 0 : o.style;
  if (typeof (a == null ? void 0 : a.x) != "number" || typeof a.y != "number") return !1;
  const [s, l] = e.graph.getCanvasByClient([r.x, r.y]), c = Math.abs(s - a.x), p = Math.abs(l - a.y);
  return Math.abs(c - Ke / 2) <= 14 && p <= Je / 2 + 10;
};
function Ye(e = {}) {
  const t = e.connectionState ?? { armed: !1 };
  return [
    "zoom-canvas",
    {
      type: "drag-canvas",
      enable: (r) => r.targetType === "canvas" || Ie(e, r),
      range: 0.9
    },
    {
      type: "drag-element",
      state: wt,
      enable: (r) => (r.targetType === "node" || r.targetType === "combo") && !je(e, r) && !Ie(e, r),
      onFinish: (r) => {
        var s, l;
        const n = e.graph;
        if (!n) return;
        const o = Array.isArray(r) ? r : r ? [String(r)] : void 0, a = n.getNodeData().map((c) => ({
          id: String(c.id),
          data: c.data ?? {},
          style: c.style ?? {}
        }));
        (l = e.onPositions) == null || l.call(e, mt(
          { nodes: a },
          ((s = e.allowedIds) == null ? void 0 : s.call(e)) ?? /* @__PURE__ */ new Set(),
          o
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: bt,
      enable: (r) => Ie(e, r) ? (t.armed = !1, !1) : t.armed ? r.targetType === "node" || r.targetType === "combo" : je(e, r) ? (t.armed = !0, !0) : !1,
      onCreate: (r) => {
        var s;
        t.armed = !1;
        const n = String(r.source ?? ""), o = String(r.target ?? "");
        if (!n || !o || n === o) return;
        const a = (s = e.onCreateEdge) == null ? void 0 : s.call(e, n, o);
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
const vt = (e) => {
  var r;
  const t = e;
  return (t == null ? void 0 : t.targetType) === "node" ? (r = t.target) == null ? void 0 : r.id : void 0;
};
function It(e) {
  const {
    definition: t,
    catalog: r,
    selectedId: n,
    onSelect: o,
    onDropNode: a,
    onCreateEdge: s,
    onPositions: l
  } = e, c = re(null), p = re(null), h = re(!1), I = re(!1), T = re({ armed: !1 }), v = re({ onSelect: o, onDropNode: a, onCreateEdge: s, onPositions: l });
  v.current = { onSelect: o, onDropNode: a, onCreateEdge: s, onPositions: l };
  const f = Be(
    () => yt(t, r, n),
    [t, r, n]
  ), x = re(f);
  x.current = f;
  const S = re(/* @__PURE__ */ new Set());
  return S.current = new Set(t.nodes.map((C) => C.id)), Y(() => {
    const C = (j) => {
      j.code === "Space" && !j.repeat && (I.current = !0);
    }, N = (j) => {
      j.code === "Space" && (I.current = !1);
    }, $ = () => {
      I.current = !1;
    };
    return window.addEventListener("keydown", C), window.addEventListener("keyup", N), window.addEventListener("blur", $), () => {
      window.removeEventListener("keydown", C), window.removeEventListener("keyup", N), window.removeEventListener("blur", $);
    };
  }, []), Y(() => {
    const C = c.current;
    if (!C || h.current) return;
    let N = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: $ }) => {
      if (N || !c.current || h.current) return;
      const j = ht(x.current, C), u = new $({
        ...j,
        behaviors: Ye({
          graph: {
            getNodeData: () => {
              var g;
              return ((g = p.current) == null ? void 0 : g.getNodeData()) ?? [];
            },
            getCanvasByClient: (g) => {
              var H;
              const k = ((H = p.current) == null ? void 0 : H.getCanvasByClient(g)) ?? g;
              return [k[0], k[1]];
            }
          },
          isPanModifier: () => I.current,
          connectionState: T.current,
          allowedIds: () => S.current,
          onPositions: (g) => v.current.onPositions(g),
          onCreateEdge: (g, k) => v.current.onCreateEdge(g, k)
        })
      });
      u.on("node:click", (g) => {
        const k = vt(g);
        k && v.current.onSelect(k);
      }), u.on("edge:click", () => v.current.onSelect(null)), u.on("canvas:click", () => v.current.onSelect(null)), u.on("pointerup", () => {
        T.current.armed = !1;
      }), p.current = u, C.__industrialGraph = u, h.current = !0, u.render();
    }), () => {
      N = !0;
    };
  }, []), Y(() => {
    const C = p.current;
    !C || !h.current || (C.setData(f), C.draw());
  }, [f]), Y(() => () => {
    var C;
    (C = p.current) == null || C.destroy(), p.current = null, h.current = !1;
  }, []), /* @__PURE__ */ i("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ b("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ i(
      "div",
      {
        ref: c,
        className: "industrial-graph-host",
        onDragOver: (C) => C.preventDefault(),
        onDrop: (C) => {
          C.preventDefault();
          const N = C.dataTransfer.getData("application/x-industrial-node") || C.dataTransfer.getData("text/plain");
          if (!N) return;
          const $ = p.current, j = c.current;
          if (!$ || !j) return;
          const u = j.getBoundingClientRect(), g = $.getCanvasByViewport([
            C.clientX - u.left,
            C.clientY - u.top
          ]);
          v.current.onDropNode(N, g[0], g[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ i("div", { className: "industrial-graph-hint", children: "拖动节点移动 · 从端口拖出连线 · 拖动画布平移（空格或中键可在节点上平移）" })
  ] }) });
}
function xt({ descriptors: e, loading: t, error: r }) {
  const n = /* @__PURE__ */ new Map();
  for (const a of e) {
    const s = a.category.split(".")[0] || "General", l = n.get(s) ?? [];
    l.push(a), n.set(s, l);
  }
  const o = (a, s) => {
    a.dataTransfer.setData("application/x-industrial-node", s.nodeTypeId), a.dataTransfer.setData("text/plain", s.nodeTypeId), a.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ b("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ i(P.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ i(P.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ i(We, { size: "small", style: { display: "block", marginTop: 16 } }),
    r && /* @__PURE__ */ i(P.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: r }),
    !t && !r && e.length === 0 && /* @__PURE__ */ i(Ge, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(n.entries()).map(([a, s]) => /* @__PURE__ */ b("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ i(P.Text, { strong: !0, style: { fontSize: 12 }, children: a }),
      s.map((l) => /* @__PURE__ */ b(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          onDragStart: (c) => o(c, l),
          title: l.description || l.nodeTypeId,
          children: [
            /* @__PURE__ */ i("div", { className: "industrial-palette-node-title", children: l.name }),
            /* @__PURE__ */ b("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ i(ne, { bordered: !1, color: "blue", children: l.version }),
              /* @__PURE__ */ i(P.Text, { type: "secondary", style: { fontSize: 11 }, children: l.nodeTypeId })
            ] }),
            /* @__PURE__ */ b("div", { className: "industrial-palette-node-ports", children: [
              l.inputs.map((c) => /* @__PURE__ */ b(ne, { bordered: !1, color: "default", children: [
                "in ",
                c.name,
                ": ",
                c.type
              ] }, `in-${c.name}`)),
              l.outputs.map((c) => /* @__PURE__ */ b(ne, { bordered: !1, color: "green", children: [
                "out ",
                c.name,
                ": ",
                c.type
              ] }, `out-${c.name}`))
            ] })
          ]
        },
        l.nodeTypeId
      ))
    ] }, a))
  ] });
}
const Te = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {}
});
function kt(e) {
  const t = {};
  for (const [r, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (t[r] = n.default);
  return t;
}
function St(e, t, r = 0, n = 0) {
  return { id: t, type: e.nodeTypeId, config: kt(e), x: r, y: n };
}
function ce(e, t) {
  return e.get(t);
}
function we(e, t, r) {
  return e[r].find((n) => n.name === t);
}
function $e(e, t) {
  return e !== "Unknown" && t !== "Unknown" && e === t;
}
function Le(e, t) {
  for (const r of e.outputs)
    for (const n of t.inputs)
      if ($e(r.type, n.type))
        return { sourcePort: r.name, targetPort: n.name };
  return null;
}
const W = (e, t, r) => ({ path: e, code: t, message: r });
function Tt(e, t) {
  return e.source === t.source && e.sourcePort === t.sourcePort && e.target === t.target && e.targetPort === t.targetPort;
}
function Nt(e, t, r) {
  const n = `edges.${e.edges.length}`, o = e.nodes.find((h) => h.id === t.source), a = e.nodes.find((h) => h.id === t.target);
  if (!o || !a)
    return { definition: e, issue: W(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((h) => Tt(h, t)))
    return { definition: e };
  const s = ce(r, o.type), l = ce(r, a.type);
  if (!s || !l)
    return { definition: e, issue: W(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const c = we(s, t.sourcePort, "outputs"), p = we(l, t.targetPort, "inputs");
  return !c || !p ? { definition: e, issue: W(n, "PORT_MISSING", "declared port does not exist") } : $e(c.type, p.type) ? { definition: { ...e, edges: [...e.edges, t] } } : {
    definition: e,
    issue: W(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${c.type} → ${p.type} is not a legal connection`
    )
  };
}
function Ct(e, t) {
  return {
    ...e,
    nodes: e.nodes.filter((r) => r.id !== t),
    edges: e.edges.filter((r) => r.source !== t && r.target !== t)
  };
}
function Et(e, t) {
  return { ...e, edges: e.edges.filter((r, n) => n !== t) };
}
function Pt(e, t) {
  const r = { ...e };
  for (const [n, o] of Object.entries(t))
    o === void 0 ? delete r[n] : r[n] = o;
  return r;
}
function $t(e, t, r) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === t ? { ...n, config: r } : n)
  };
}
function Dt(e, t) {
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
function _t(e) {
  var a;
  const t = /* @__PURE__ */ new Map();
  for (const s of e.nodes) t.set(s.id, []);
  for (const s of e.edges)
    (a = t.get(s.source)) == null || a.push(s.target);
  const r = /* @__PURE__ */ new Map(), n = [], o = (s) => {
    r.set(s, 1), n.push(s);
    for (const l of t.get(s) ?? []) {
      if (r.get(l) === 1)
        return [...n.slice(n.indexOf(l)), l];
      if (r.get(l) !== 2) {
        const c = o(l);
        if (c) return c;
      }
    }
    return n.pop(), r.set(s, 2), null;
  };
  for (const s of e.nodes)
    if (r.get(s.id) !== 2) {
      const l = o(s.id);
      if (l) return l;
    }
  return null;
}
function Ot(e, t, r) {
  const n = [];
  e.workflowId.trim() || n.push(W("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(W("workflow", "INVALID_VERSION", "version must be positive"));
  const o = /* @__PURE__ */ new Map();
  for (const l of e.nodes)
    o.has(l.id) && n.push(W(`nodes.${l.id}`, "DUPLICATE_NODE_ID", `duplicate node '${l.id}'`)), o.set(l.id, l);
  const a = /* @__PURE__ */ new Map();
  for (const l of e.nodes) {
    const c = ce(t, l.type);
    if (!c) {
      n.push(W(
        `nodes.${l.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${l.type}' is not registered`
      ));
      continue;
    }
    a.set(l.id, c);
    const p = c.configurationSchema.required ?? [];
    for (const h of p) {
      const I = l.config[h];
      (I == null || I === "") && n.push(W(
        `nodes.${l.id}.config.${h}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${h}' is missing`
      ));
    }
    r && typeof l.config.resourceId == "string" && (r.has(l.config.resourceId) || n.push(W(
      `nodes.${l.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${l.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((l, c) => {
    const p = o.get(l.source), h = o.get(l.target);
    if (!p || !h) {
      n.push(W(`edges.${c}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const I = a.get(l.source), T = a.get(l.target);
    if (!I || !T) return;
    const v = we(I, l.sourcePort, "outputs"), f = we(T, l.targetPort, "inputs");
    if (!v || !f) {
      n.push(W(`edges.${c}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    $e(v.type, f.type) || n.push(W(
      `edges.${c}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${v.type} → ${f.type} is not a legal connection`
    ));
  });
  for (const l of e.nodes) {
    const c = a.get(l.id);
    if (c)
      for (const p of c.inputs.filter((h) => h.required))
        e.edges.some((h) => h.target === l.id && h.targetPort === p.name && o.has(h.source)) || n.push(W(
          `nodes.${l.id}.inputs.${p.name}`,
          "MISSING_INPUT",
          `required input '${p.name}' is not connected`
        ));
  }
  const s = _t(e);
  return s && n.push(W(`nodes.${s[0]}`, "GRAPH_CYCLE", `cycle: ${s.join(" → ")}`)), n;
}
const Ne = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function Rt(e) {
  const t = Ne(e.additionalProperties).enum;
  if (!Array.isArray(t)) return null;
  const r = t.filter((n) => typeof n == "string");
  return r.length > 0 ? r : null;
}
function He(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && Ne(e.items).type === "string" ? "string-list" : e.type === "object" && Ne(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function Ce(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, t]) => t != null && typeof t != "object").map(([t, r]) => ({ key: t, value: String(r) }));
}
function qe(e) {
  const t = {}, r = [];
  for (const n of e) {
    const o = n.key.trim();
    o && (Object.prototype.hasOwnProperty.call(t, o) && r.push(o), t[o] = n.value);
  }
  return { record: t, duplicateKeys: r };
}
function he(e) {
  if (!Array.isArray(e)) return [];
  const t = /* @__PURE__ */ new Set(), r = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const o = n.trim();
    !o || t.has(o) || (t.add(o), r.push(o));
  }
  return r;
}
function At(e, t) {
  const r = /* @__PURE__ */ new Set();
  for (const [n, o] of Object.entries(e))
    if (He(o) === "key-value")
      for (const a of Ce(t[n]))
        a.key.trim() && r.add(a.key.trim());
  return [...r];
}
function zt({
  value: e,
  onChange: t,
  valueOptions: r,
  keyTitle: n = "目标字段",
  valueTitle: o = "来源字段",
  testId: a
}) {
  const s = JSON.stringify(e ?? {}), [l, c] = w(() => Ce(e)), [p, h] = w(s);
  Y(() => {
    s !== p && (c(Ce(e)), h(s));
  }, [s, p, e]);
  const I = (v) => {
    c(v), t == null || t(qe(v).record);
  }, { duplicateKeys: T } = qe(l);
  return /* @__PURE__ */ b("div", { "data-testid": a, className: "industrial-kv-editor", children: [
    l.map((v, f) => /* @__PURE__ */ b("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ i(
        F,
        {
          "aria-label": `${n} ${f + 1}`,
          value: v.key,
          placeholder: n,
          onChange: (x) => I(l.map((S, _) => _ === f ? { ...S, key: x.target.value } : S))
        }
      ),
      r && r.length > 0 ? /* @__PURE__ */ i(
        oe,
        {
          "aria-label": `${o} ${f + 1}`,
          value: v.value || void 0,
          placeholder: o,
          options: r.map((x) => ({ label: x, value: x })),
          onChange: (x) => I(l.map((S, _) => _ === f ? { ...S, value: x ?? "" } : S))
        }
      ) : /* @__PURE__ */ i(
        F,
        {
          "aria-label": `${o} ${f + 1}`,
          value: v.value,
          placeholder: o,
          onChange: (x) => I(l.map((S, _) => _ === f ? { ...S, value: x.target.value } : S))
        }
      ),
      /* @__PURE__ */ i(
        A,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${f + 1}`,
          onClick: () => I(l.filter((x, S) => S !== f)),
          children: "删"
        }
      )
    ] }, f)),
    /* @__PURE__ */ i(
      A,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => I([...l, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    T.length > 0 && /* @__PURE__ */ b(P.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      T.join("、")
    ] })
  ] });
}
function jt({ value: e, onChange: t, suggestions: r = [], testId: n }) {
  const o = JSON.stringify(he(e)), [a, s] = w(() => he(e)), [l, c] = w(o);
  Y(() => {
    o !== l && (s(he(e)), c(o));
  }, [o, l, e]);
  const p = (h) => {
    s(h), t == null || t(he(h));
  };
  return /* @__PURE__ */ b("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ i("datalist", { id: `${n ?? "list"}-suggestions`, children: r.map((h) => /* @__PURE__ */ i("option", { value: h }, h)) }),
    a.map((h, I) => /* @__PURE__ */ b("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ i(
        F,
        {
          "aria-label": `字段 ${I + 1}`,
          value: h,
          list: `${n ?? "list"}-suggestions`,
          onChange: (T) => p(a.map((v, f) => f === I ? T.target.value : v))
        }
      ),
      /* @__PURE__ */ i(
        A,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${I + 1}`,
          onClick: () => p(a.filter((T, v) => v !== I)),
          children: "删"
        }
      )
    ] }, I)),
    /* @__PURE__ */ i(A, { size: "small", type: "dashed", block: !0, onClick: () => p([...a, ""]), children: "添加字段" }),
    a.length === 0 && /* @__PURE__ */ i(P.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Lt({ value: e, onChange: t }) {
  const r = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, o] = w(r), [a, s] = w(null);
  Y(() => {
    o(r), s(null);
  }, [r]);
  const l = (c) => {
    o(c);
    try {
      const p = JSON.parse(c);
      s(null), t == null || t(p);
    } catch (p) {
      s(p.message);
    }
  };
  return /* @__PURE__ */ b("div", { children: [
    /* @__PURE__ */ i(F.TextArea, { rows: 5, value: n, onChange: (c) => l(c.target.value) }),
    a && /* @__PURE__ */ i(P.Text, { type: "danger", style: { fontSize: 11 }, children: a })
  ] });
}
function qt({ value: e, onChange: t, resources: r, resourceType: n }) {
  const o = r.filter((s) => s.enabled !== !1 && (!n || s.type === n)).map((s) => ({
    value: s.id,
    label: `${s.name} (${s.type})`
  })), a = r.find((s) => s.id === e);
  return /* @__PURE__ */ b("div", { children: [
    /* @__PURE__ */ i(oe, { allowClear: !0, value: e, onChange: t, options: o }),
    (a == null ? void 0 : a.credentialId) && /* @__PURE__ */ b(P.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      a.credentialName ?? a.credentialId
    ] })
  ] });
}
function Ft({ name: e, schema: t, required: r, resources: n, resourceType: o, properties: a, config: s }) {
  let l, c = "value";
  switch (He(t)) {
    case "resource":
      l = /* @__PURE__ */ i(qt, { resources: n, resourceType: o });
      break;
    case "enum":
      l = /* @__PURE__ */ i(
        oe,
        {
          allowClear: !0,
          options: (t.enum ?? []).map((p) => ({ label: String(p), value: p }))
        }
      );
      break;
    case "number":
      l = /* @__PURE__ */ i(Ue, { style: { width: "100%" } });
      break;
    case "boolean":
      l = /* @__PURE__ */ i(Qe, {}), c = "checked";
      break;
    case "key-value":
      l = /* @__PURE__ */ i(
        zt,
        {
          testId: `field-${e}`,
          valueOptions: Rt(t) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      l = /* @__PURE__ */ i(jt, { testId: `field-${e}`, suggestions: At(a, s) });
      break;
    case "json":
      l = /* @__PURE__ */ i(Lt, {});
      break;
    default:
      l = /* @__PURE__ */ i(F, { placeholder: t.description });
      break;
  }
  return /* @__PURE__ */ i(
    Se.Item,
    {
      name: e,
      label: t.title ?? e,
      tooltip: t.description,
      valuePropName: c,
      rules: r && c === "value" ? [{ required: !0, message: `请填写 ${t.title ?? e}` }] : [],
      children: l
    },
    e
  );
}
function Mt(e) {
  const {
    node: t,
    descriptor: r,
    edges: n,
    catalog: o,
    resources: a,
    issues: s,
    onApplyConfig: l,
    onDeleteNode: c,
    onDeleteEdge: p,
    onUpdateEdge: h,
    onSelectNode: I
  } = e, [T] = Se.useForm(), [v, f] = w("{}"), [x, S] = w(null);
  Y(() => {
    T.setFieldsValue((t == null ? void 0 : t.config) ?? {}), f(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), S(null);
  }, [T, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const _ = new Set((r == null ? void 0 : r.configurationSchema.required) ?? []), C = Object.entries((r == null ? void 0 : r.configurationSchema.properties) ?? {}), N = () => {
    if (C.length > 0) {
      T.validateFields().then((u) => l(Pt((t == null ? void 0 : t.config) ?? {}, u)));
      return;
    }
    try {
      const u = JSON.parse(v);
      if (u === null || typeof u != "object" || Array.isArray(u))
        throw new Error("配置必须是 JSON 对象");
      S(null), l(u);
    } catch (u) {
      S(u.message);
    }
  }, $ = n.map((u, g) => ({
    ...u,
    index: g,
    key: `${g}:${u.source}->${u.target}`
  })), j = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (u, g) => /* @__PURE__ */ b(P.Text, { style: { fontSize: 11 }, children: [
        g.source,
        " → ",
        g.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (u, g) => {
        const k = o.get(g.source), H = o.get(g.target);
        return /* @__PURE__ */ b("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ i(
            oe,
            {
              size: "small",
              value: g.sourcePort,
              style: { width: 110 },
              onChange: (K) => h(g.index, { ...g, sourcePort: K }),
              options: ((k == null ? void 0 : k.outputs) ?? []).map((K) => ({ label: `${K.name} (${K.type})`, value: K.name }))
            }
          ),
          /* @__PURE__ */ i(
            oe,
            {
              size: "small",
              value: g.targetPort,
              style: { width: 110 },
              onChange: (K) => h(g.index, { ...g, targetPort: K }),
              options: ((H == null ? void 0 : H.inputs) ?? []).map((K) => ({ label: `${K.name} (${K.type})`, value: K.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (u, g) => /* @__PURE__ */ i(A, { danger: !0, size: "small", type: "text", onClick: () => p(g.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ b("div", { className: "industrial-properties-body", children: [
    !t && /* @__PURE__ */ i(Ge, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ b(ve, { children: [
      /* @__PURE__ */ i(P.Text, { strong: !0, children: (r == null ? void 0 : r.name) ?? t.type }),
      /* @__PURE__ */ i(ne, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ i(Se, { form: T, layout: "vertical", size: "small", style: { marginTop: 12 }, children: C.map(([u, g]) => /* @__PURE__ */ i(
        Ft,
        {
          name: u,
          schema: g,
          required: _.has(u),
          resources: a,
          resourceType: typeof (r == null ? void 0 : r.configurationSchema.resourceType) == "string" ? r.configurationSchema.resourceType : void 0,
          properties: (r == null ? void 0 : r.configurationSchema.properties) ?? {},
          config: (t == null ? void 0 : t.config) ?? {}
        },
        u
      )) }),
      C.length === 0 && /* @__PURE__ */ b(ve, { children: [
        /* @__PURE__ */ i(P.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ i(
          F.TextArea,
          {
            rows: 8,
            value: v,
            style: { marginTop: 6 },
            onChange: (u) => f(u.target.value)
          }
        ),
        x && /* @__PURE__ */ i(P.Text, { type: "danger", style: { fontSize: 12 }, children: x })
      ] }),
      /* @__PURE__ */ i(A, { type: "primary", block: !0, onClick: N, children: "应用配置" }),
      /* @__PURE__ */ i(A, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: c, children: "删除节点" }),
      (r == null ? void 0 : r.activityType) && /* @__PURE__ */ i(P.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: r.activityType })
    ] }),
    /* @__PURE__ */ b(Re, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      n.length,
      ")"
    ] }),
    n.length === 0 ? /* @__PURE__ */ i(P.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ i(de, { size: "small", columns: j, dataSource: $, pagination: !1 }),
    s.length > 0 && /* @__PURE__ */ b(ve, { children: [
      /* @__PURE__ */ b(Re, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        s.length,
        ")"
      ] }),
      s.map((u, g) => /* @__PURE__ */ b("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ i(ne, { color: "red", children: u.code }),
        /* @__PURE__ */ b(
          P.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const k = /^nodes\.([^.]+)/.exec(u.path);
              k && I(k[1]);
            },
            children: [
              u.path,
              ": ",
              u.message
            ]
          }
        )
      ] }, `${u.path}-${g}`))
    ] })
  ] });
}
const Bt = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class Gt extends Error {
  constructor(t) {
    super(t), this.name = "DtoError";
  }
}
const D = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, B = (e) => Array.isArray(e) ? e : [], m = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", ie = (e, t = 0) => {
  const r = typeof e == "string" ? Number(e) : e;
  return typeof r == "number" && Number.isFinite(r) ? r : t;
}, d = (e, ...t) => {
  for (const r of t)
    if (e[r] !== void 0 && e[r] !== null) return e[r];
};
function te(e) {
  const t = D(e);
  if (Object.prototype.hasOwnProperty.call(t, "data")) return t.data;
  if (typeof t.code == "string" && typeof t.message == "string")
    throw new Gt(`${t.code}: ${t.message}`);
  return e;
}
function Ut(e) {
  const t = m(e).trim().toLowerCase();
  return Bt.find((n) => n.toLowerCase() === t) ?? "Unknown";
}
function Wt(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const t = D(e), r = m(d(t, "name", "port", "id")).trim();
  return r ? {
    name: r,
    type: Ut(d(t, "type", "portType", "dataType")),
    required: d(t, "required", "isRequired") === !0
  } : null;
}
function Fe(e) {
  const t = B(e).map(Wt).filter((n) => n !== null), r = /* @__PURE__ */ new Set();
  return t.filter((n) => r.has(n.name) ? !1 : (r.add(n.name), !0));
}
const Qt = (e) => {
  const t = D(e);
  return Object.keys(t).length === 0 ? { type: "object", properties: {} } : t;
};
function Kt(e) {
  const t = D(e), r = m(d(t, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return r ? {
    nodeTypeId: r,
    name: m(d(t, "name", "title"), r),
    description: m(d(t, "description", "desc")),
    category: m(d(t, "category", "group"), "General"),
    icon: m(d(t, "icon"), "AppstoreOutlined"),
    version: m(d(t, "version"), "1.0.0"),
    inputs: Fe(d(t, "inputs", "inputPorts", "input_ports")),
    outputs: Fe(d(t, "outputs", "outputPorts", "output_ports")),
    configurationSchema: Qt(d(t, "configurationSchema", "configSchema", "schema")),
    activityType: m(d(t, "activityType", "activity_type")),
    dependencies: B(d(t, "dependencies")).map((n) => m(n)).filter(Boolean)
  } : null;
}
function Jt(e) {
  const t = te(e), r = Array.isArray(t) ? t : B(d(D(t), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return r.map(Kt).filter((o) => o !== null).filter((o) => n.has(o.nodeTypeId) ? !1 : (n.add(o.nodeTypeId), !0));
}
function Yt(e) {
  const t = te(e), r = Array.isArray(t) ? t : B(d(D(t), "resources", "items")), n = /* @__PURE__ */ new Set();
  return r.map((o) => {
    const a = D(o), s = m(d(a, "id")).trim();
    return !s || n.has(s) ? null : (n.add(s), {
      id: s,
      name: m(d(a, "name"), s),
      type: m(d(a, "type"), "unknown"),
      enabled: a.enabled !== !1,
      credentialId: typeof a.credentialId == "string" ? a.credentialId : null,
      credentialName: typeof a.credentialName == "string" ? a.credentialName : null
    });
  }).filter((o) => o !== null);
}
function Ve(e) {
  const t = D(e), r = m(d(t, "workflowId", "workflow_id", "id")).trim();
  return r ? {
    workflowId: r,
    name: m(d(t, "name", "title"), r),
    version: ie(d(t, "version"), 1)
  } : null;
}
function Xe(e) {
  const t = te(e);
  return (Array.isArray(t) ? t : B(d(D(t), "workflows", "items"))).map(Ve).filter((n) => n !== null);
}
function Ee(e) {
  const t = D(e), r = {};
  for (const [n, o] of Object.entries(t))
    o !== void 0 && (r[n] = o);
  return r;
}
function Ht(e) {
  const t = D(e), r = m(d(t, "id", "nodeId", "node_id")).trim();
  if (!r) return null;
  const n = D(d(t, "position", "layout"));
  return {
    id: r,
    type: m(d(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: Ee(d(t, "config", "configuration")),
    x: ie(d(t, "x") ?? d(n, "x"), 0),
    y: ie(d(t, "y") ?? d(n, "y"), 0)
  };
}
function Vt(e) {
  const t = D(e), r = m(d(t, "source", "from", "sourceId")).trim(), n = m(d(t, "target", "to", "targetId")).trim();
  return !r || !n ? null : {
    source: r,
    sourcePort: m(d(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: m(d(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function Xt(e) {
  const t = te(e), r = D(t), n = Ve(r) ?? { workflowId: "", name: "", version: 1 }, o = B(d(r, "nodes")).map(Ht).filter((c) => c !== null), a = B(d(r, "edges")).map(Vt).filter((c) => c !== null), s = D(d(r, "trigger")), l = {
    kind: m(d(s, "kind", "type"), "manual"),
    cron: d(s, "cron") === void 0 ? null : m(s.cron),
    eventName: d(s, "eventName", "event_name") === void 0 ? null : m(d(s, "eventName", "event_name")),
    config: Ee(d(s, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: o,
    edges: a,
    trigger: l,
    resourceReferences: B(d(r, "resourceReferences", "resource_references")).map((c) => m(c)).filter(Boolean),
    layout: Ee(d(r, "layout"))
  };
}
const Ze = (e) => e === !0 || typeof e == "string" && e.toLowerCase() === "true";
function Zt(e) {
  const t = te(e);
  return (Array.isArray(t) ? t : B(d(D(t), "resources", "items"))).map((n) => {
    const o = D(n), a = d(o, "credentialId", "credential_id");
    return {
      id: m(d(o, "id"), "—"),
      name: m(d(o, "name"), "—"),
      type: m(d(o, "type"), "—"),
      provider: m(d(o, "provider"), "—"),
      enabled: Ze(d(o, "enabled")),
      credentialId: a == null ? null : m(a),
      credentialName: d(o, "credentialName") == null ? null : m(d(o, "credentialName"))
    };
  });
}
function et(e) {
  const t = te(e);
  return (Array.isArray(t) ? t : B(d(D(t), "credentials", "items"))).map((n) => {
    const o = D(n);
    return {
      id: m(d(o, "id"), "—"),
      name: m(d(o, "name"), "—"),
      kind: m(d(o, "kind"), "—"),
      version: ie(d(o, "version"))
    };
  });
}
function er(e) {
  const t = te(e);
  return (Array.isArray(t) ? t : B(d(D(t), "tests", "items"))).map((n) => {
    const o = D(n);
    return {
      resourceId: m(d(o, "resourceId", "resource_id"), "—"),
      ok: Ze(d(o, "ok")),
      detail: m(d(o, "detail"), "")
    };
  });
}
function tr(e) {
  const t = te(e), r = D(t), n = B(d(r, "runs")).map((a) => {
    const s = D(a);
    return {
      id: m(d(s, "id"), "—"),
      workflowId: m(d(s, "workflowId", "workflow_id"), "—"),
      status: m(d(s, "status"), "—"),
      startedAt: m(d(s, "startedAt", "started_at"), ""),
      finishedAt: d(s, "finishedAt", "finished_at") == null ? null : m(d(s, "finishedAt", "finished_at")),
      error: d(s, "error") == null ? null : m(d(s, "error"))
    };
  }), o = B(d(r, "deliveries")).map((a) => {
    const s = D(a);
    return {
      id: m(d(s, "id"), "—"),
      collectionFileId: m(d(s, "collectionFileId", "collection_file_id"), ""),
      targetId: m(d(s, "targetId", "target_id"), "—"),
      targetKind: m(d(s, "targetKind", "target_kind"), "record"),
      status: m(d(s, "status"), "—"),
      attempts: ie(d(s, "attempts")),
      records: ie(d(s, "records")),
      error: d(s, "error") == null ? null : m(d(s, "error"))
    };
  });
  return { runs: n, deliveries: o };
}
function rr(e) {
  if (e == null) return null;
  const t = te(e), r = D(t);
  if (Object.keys(r).length === 0) return null;
  const n = B(d(r, "nodes")).slice(0, 64).map((o) => {
    const a = D(o);
    return {
      nodeId: m(d(a, "nodeId", "node_id"), "—"),
      status: m(d(a, "status"), "—"),
      records: ie(d(a, "records")),
      files: ie(d(a, "files")),
      error: d(a, "error") == null ? null : m(d(a, "error"), void 0),
      samples: B(d(a, "samples")).slice(0, 3).map((s) => m(s))
    };
  });
  return {
    status: m(d(r, "status"), "—"),
    error: d(r, "error") == null ? null : m(d(r, "error"), void 0),
    executed: B(d(r, "executed")).map((o) => m(o)),
    instanceId: d(r, "instanceId") == null ? null : m(d(r, "instanceId"), void 0),
    definitionId: d(r, "definitionId") == null ? null : m(d(r, "definitionId"), void 0),
    nodes: n
  };
}
const { Header: nr, Content: or } = pt;
function sr({ ctx: e }) {
  const [t, r] = ft.useMessage(), [n, o] = w([]), [a, s] = w([]), [l, c] = w([]), [p, h] = w([]), [I, T] = w(""), [v, f] = w(1), [x, S] = w(!0), [_, C] = w([]), [N, $] = w(() => Te()), [j, u] = w(null), [g, k] = w([]), [H, K] = w(!1), [ye, De] = w(!0), [tt, le] = w(!1), [fe, ee] = w(!1), [V, rt] = w(null), _e = re(1);
  Y(() => {
    let y = !0;
    return Promise.allSettled([
      e.hubQuery("industrial/nodes"),
      e.hubQuery("industrial/workflows"),
      e.hubQuery("industrial/resources")
    ]).then(([E, q, z]) => {
      var M;
      if (!y) return;
      const L = [];
      if (E.status === "fulfilled" ? o(Jt(E.value)) : L.push(`节点描述: ${String(E.reason)}`), q.status === "fulfilled") {
        const G = Xe(q.value);
        s(G);
        const U = new URLSearchParams(window.location.search).get("workflowId"), X = U && G.some((se) => se.workflowId === U) ? U : (M = G[0]) == null ? void 0 : M.workflowId;
        X && Oe(X);
      } else L.push(`工作流: ${String(q.reason)}`);
      if (z.status === "fulfilled") {
        const G = Yt(z.value);
        h(G);
        const U = G.map((X) => X.id);
        c(U), T((X) => X || U[0] || "");
      } else L.push(`资源: ${String(z.reason)}`);
      C(L);
    }).finally(() => {
      y && S(!1);
    }), () => {
      y = !1;
    };
  }, [e]);
  const Z = Be(
    () => new Map(n.map((y) => [y.nodeTypeId, y])),
    [n]
  ), be = N.nodes.find((y) => y.id === j) ?? null, ue = O(async (y, E, q) => {
    K(!0);
    try {
      return await J().api.hubCommand(y, E), t.success(q), !0;
    } catch (z) {
      return t.error(`${y} failed: ${z instanceof Error ? z.message : String(z)}`), !1;
    } finally {
      K(!1);
    }
  }, [t]), Oe = O(async (y) => {
    try {
      const E = await e.hubQuery("industrial/workflow-definitions"), z = (te(E) ?? []).find((G) => {
        const U = G;
        return String((U == null ? void 0 : U.workflowId) ?? "") === y;
      }), L = z === void 0 ? { ...Te(y), name: y } : Xt(z);
      $(L), f(Number(L.layout.lookbackDays ?? 1) || 1);
      const M = L.nodes.map((G) => /^n(\d+)$/.exec(G.id)).filter(Boolean).reduce((G, U) => Math.max(G, Number(U[1])), 0);
      _e.current = M + 1, u(null), le(!1), k([]), ee(!1);
    } catch (E) {
      t.error(`加载工作流失败: ${E instanceof Error ? E.message : String(E)}`);
    }
  }, [e, t]), me = O(async () => {
    try {
      const y = rr(await e.hubQuery("industrial/execution"));
      if (y === null) return;
      rt(y);
    } catch {
    }
  }, [e]), nt = O(async () => {
    var q;
    const y = new Set(N.resourceReferences ?? []);
    for (const z of N.nodes)
      for (const L of ["resourceId", "targetResourceId"]) {
        const M = (q = z.config) == null ? void 0 : q[L];
        typeof M == "string" && M.trim() && y.add(M.trim());
      }
    const E = {
      ...N,
      resourceReferences: [...y],
      layout: { ...N.layout, lookbackDays: v }
    };
    await ue("industrial/save-workflow", E, "已保存") && ($(E), ee(!1));
  }, [ue, N, v]), ot = O(async () => {
    if (fe) {
      t.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await ue("industrial/publish-workflow", { workflowId: N.workflowId }, "已发布") && await me();
  }, [ue, N.workflowId, fe, t, me]), st = O(async () => {
    if (fe) {
      t.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await ue(
      "industrial/run-workflow",
      { workflowId: N.workflowId, resourceId: I },
      "采集已启动"
    ) && await me();
  }, [ue, N.workflowId, fe, t, me, I]), at = O((y, E, q) => {
    const z = ce(Z, y);
    if (!z) return;
    const L = `n${_e.current++}`;
    $((M) => ({
      ...M,
      nodes: [...M.nodes, St(z, L, E, q)]
    })), u(L), le(!0), k([]), ee(!0);
  }, [Z]), it = O((y, E) => {
    const q = N.nodes.find((ae) => ae.id === y), z = N.nodes.find((ae) => ae.id === E);
    if (!q || !z)
      return t.warning("连线失败：端点不在画布上"), null;
    const L = ce(Z, q.type), M = ce(Z, z.type);
    if (!L || !M) return null;
    let G = q, U = z, X = Le(L, M);
    if (!X) {
      const ae = Le(M, L);
      if (!ae)
        return t.warning(`端口类型不兼容：${L.name} → ${M.name}`), null;
      G = z, U = q, X = ae;
    }
    const se = Nt(N, {
      source: G.id,
      sourcePort: X.sourcePort,
      target: U.id,
      targetPort: X.targetPort
    }, Z);
    return se.issue ? (t.warning(`${se.issue.code}: ${se.issue.message}`), k((ae) => [...ae, se.issue]), null) : se.definition === N ? (t.info("这条连线已经存在"), null) : ($(se.definition), ee(!0), X);
  }, [Z, N, t]), lt = O(() => {
    const y = Ot(N, Z, new Set(l));
    k(y), y.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : (t.warning(`发现 ${y.length} 个问题，见属性抽屉定位列表`), le(!0));
  }, [N, Z, l, t]), ct = O((y, E) => {
    $((q) => ({
      ...q,
      edges: q.edges.map((z, L) => L === y ? E : z)
    })), ee(!0);
  }, []);
  return x ? /* @__PURE__ */ i(We, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ b("div", { className: "industrial-designer", children: [
    r,
    /* @__PURE__ */ b(nr, { className: "industrial-header", children: [
      /* @__PURE__ */ b("div", { className: "industrial-title", children: [
        /* @__PURE__ */ i(P.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ i(P.Text, { type: "secondary", children: N.name || N.workflowId }),
        fe && /* @__PURE__ */ i(ne, { color: "orange", children: "未保存" }),
        V && /* @__PURE__ */ i(ne, { color: V.error ? "error" : "success", children: V.status })
      ] }),
      /* @__PURE__ */ b("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ i(
          A,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": ye,
            onClick: () => De((y) => !y),
            children: ye ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ i(
          A,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => le((y) => !y),
            children: "属性"
          }
        ),
        /* @__PURE__ */ i("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ b(Q, { size: 4, children: [
          /* @__PURE__ */ i(A, { size: "small", loading: H, onClick: () => void nt(), children: "保存" }),
          /* @__PURE__ */ i(A, { size: "small", onClick: lt, children: "校验" }),
          /* @__PURE__ */ i(A, { size: "small", loading: H, onClick: () => void ot(), children: "发布" }),
          /* @__PURE__ */ i(A, { size: "small", type: "primary", loading: H, onClick: () => void st(), children: "运行" })
        ] }),
        /* @__PURE__ */ i(
          oe,
          {
            className: "industrial-resource-select",
            size: "small",
            value: I,
            onChange: T,
            placeholder: "运行资源",
            options: l.map((y) => ({ value: y, label: y }))
          }
        ),
        /* @__PURE__ */ i(
          Ue,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: v,
            onChange: (y) => {
              f(Number(y) || 1), ee(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ b(or, { className: `industrial-body${ye ? "" : " industrial-body--collapsed"}`, children: [
      ye ? /* @__PURE__ */ b("aside", { className: "industrial-palette", children: [
        a.length > 0 && /* @__PURE__ */ i(
          oe,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: N.workflowId,
            placeholder: "选择工作流",
            onChange: (y) => void Oe(y),
            options: a.map((y) => ({ value: y.workflowId, label: y.name }))
          }
        ),
        /* @__PURE__ */ i(xt, { descriptors: n, loading: !1 })
      ] }) : /* @__PURE__ */ i("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ i(A, { type: "text", size: "small", onClick: () => De(!0), children: "节点库" }) }),
      /* @__PURE__ */ b("section", { className: "industrial-center", children: [
        _.length > 0 && /* @__PURE__ */ i(
          pe,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: _.join("；")
          }
        ),
        /* @__PURE__ */ i(
          It,
          {
            definition: N,
            catalog: Z,
            selectedId: j,
            onSelect: (y) => {
              u(y), y && le(!0);
            },
            onDropNode: at,
            onCreateEdge: it,
            onPositions: (y) => {
              $((E) => Dt(E, y)), ee(!0);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ b(
      ut,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: tt,
        mask: !1,
        getContainer: !1,
        onClose: () => le(!1),
        children: [
          V && /* @__PURE__ */ i(
            pe,
            {
              style: { marginBottom: 12 },
              type: V.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${V.status}`,
              description: /* @__PURE__ */ b(Q, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                V.error && /* @__PURE__ */ i("span", { children: V.error }),
                V.executed.length > 0 && /* @__PURE__ */ b("span", { children: [
                  "已执行节点: ",
                  V.executed.join(", ")
                ] }),
                V.nodes.length > 0 && /* @__PURE__ */ i(
                  de,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: V.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (y, E) => E.error ?? (E.samples.length > 0 ? E.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ i(
            Mt,
            {
              node: be,
              descriptor: be ? ce(Z, be.type) ?? null : null,
              edges: N.edges,
              catalog: Z,
              resources: p,
              issues: g,
              onApplyConfig: (y) => {
                j && ($((E) => $t(E, j, y)), ee(!0));
              },
              onDeleteNode: () => {
                $((y) => Ct(y, j ?? "")), u(null), ee(!0);
              },
              onDeleteEdge: (y) => {
                $((E) => Et(E, y)), ee(!0);
              },
              onUpdateEdge: ct,
              onSelectNode: (y) => {
                u(y), le(!0);
              }
            }
          )
        ]
      }
    )
  ] });
}
function ar({ block: e, ctx: t }) {
  const [r, n] = w([]), [o, a] = w(!1), [s, l] = w(""), [c, p] = w(null), [h, I] = w(!1), T = O(async () => {
    try {
      n(Xe(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
    } catch (x) {
      p(x instanceof Error ? x.message : String(x));
    }
  }, [e == null ? void 0 : e.query, t]);
  Y(() => {
    T();
  }, [T]);
  const v = O(async () => {
    var S;
    const x = s.trim();
    if (!x) {
      p("请输入工作流名称");
      return;
    }
    I(!0), p(null);
    try {
      const C = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await t.hubCommand("industrial/save-workflow", {
        ...Te(C),
        name: x,
        layout: { lookbackDays: 1 }
      }), a(!1), l(""), await T(), (S = t.navigate) == null || S.call(t, `/industrial/designer?workflowId=${encodeURIComponent(C)}`);
    } catch (_) {
      p(_ instanceof Error ? _.message : String(_));
    } finally {
      I(!1);
    }
  }, [t, T, s]), f = O((x) => {
    var S;
    (S = t.navigate) == null || S.call(t, `/industrial/designer?workflowId=${encodeURIComponent(x)}`);
  }, [t]);
  return /* @__PURE__ */ b(Q, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ b(Q, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ i(P.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ i(A, { type: "primary", onClick: () => a(!0), children: "新建工作流" })
    ] }),
    c && /* @__PURE__ */ i(P.Text, { type: "danger", children: c }),
    /* @__PURE__ */ i(
      de,
      {
        rowKey: "workflowId",
        dataSource: r,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (x) => /* @__PURE__ */ i(A, { type: "link", style: { padding: 0 }, onClick: () => f(x), children: x })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ i(
      Pe,
      {
        open: o,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: h,
        onCancel: () => a(!1),
        onOk: () => void v(),
        children: /* @__PURE__ */ b(Q, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(P.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ i(
            F,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: s,
              onChange: (x) => l(x.target.value ?? "")
            }
          ),
          c && /* @__PURE__ */ i(P.Text, { type: "danger", children: c })
        ] })
      }
    )
  ] });
}
const xe = {
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
  enabled: !0
}, ir = [
  { value: "filesystem.local", label: "本地目录" },
  { value: "filesystem.unc", label: "Windows UNC 共享" },
  { value: "sqlserver", label: "SQL Server" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mysql", label: "MySQL" },
  { value: "sqlite", label: "SQLite" }
];
function lr(e) {
  switch (e.type) {
    case "filesystem.local":
    case "filesystem.unc":
      return { root: e.root };
    case "sqlserver":
      return { server: e.host, database: e.database, table: e.table || "IndustrialRecords" };
    case "postgresql":
    case "mysql":
      return { host: e.host, database: e.database, port: e.port || (e.type === "mysql" ? "3306" : "5432") };
    case "sqlite":
      return { database: e.database };
    default:
      return {};
  }
}
function cr({ block: e, ctx: t }) {
  const [r, n] = w([]), [o, a] = w([]), [s, l] = w([]), [c, p] = w(xe), [h, I] = w(!1), [T, v] = w(null), [f, x] = w(!1), S = O(async () => {
    try {
      const [u, g, k] = await Promise.all([
        t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        t.hubQuery("industrial/credentials"),
        t.hubQuery("industrial/resource-tests")
      ]);
      n(Zt(u)), a(et(g)), l(er(k));
    } catch (u) {
      v(u instanceof Error ? u.message : String(u));
    }
  }, [e == null ? void 0 : e.query, t]);
  Y(() => {
    S();
  }, [S]);
  const _ = O((u) => {
    p({
      id: u.id,
      name: u.name,
      type: u.type,
      provider: u.provider,
      root: "",
      host: "",
      database: "",
      port: "",
      table: "",
      credentialId: u.credentialId ?? "",
      enabled: u.enabled
    }), I(!0);
  }, []), C = O(async () => {
    const u = c.id.trim();
    if (!u) {
      v("请输入资源 ID");
      return;
    }
    x(!0), v(null);
    try {
      await t.hubCommand("industrial/save-resource", {
        id: u,
        name: c.name || u,
        type: c.type,
        provider: c.provider || "manual",
        config: lr(c),
        credentialId: c.credentialId || null,
        enabled: c.enabled
      }), I(!1), p(xe), await S();
    } catch (g) {
      v(g instanceof Error ? g.message : String(g));
    } finally {
      x(!1);
    }
  }, [t, c, S]), N = O(async (u) => {
    v(null);
    try {
      await t.hubCommand("industrial/test-resource", { id: u });
    } catch (g) {
      v(g instanceof Error ? g.message : String(g));
    }
    await S();
  }, [t, S]), $ = O(async (u) => {
    v(null);
    try {
      await t.hubCommand("industrial/delete-resource", { id: u }), await S();
    } catch (g) {
      v(g instanceof Error ? g.message : String(g));
    }
  }, [t, S]), j = new Map(s.map((u) => [u.resourceId, u]));
  return /* @__PURE__ */ b(Q, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ b(Q, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ i(P.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ i(A, { type: "primary", onClick: () => {
        p(xe), I(!0);
      }, children: "新建资源" })
    ] }),
    T && /* @__PURE__ */ i(pe, { type: "error", showIcon: !0, message: T }),
    /* @__PURE__ */ i(
      de,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        columns: [
          { title: "ID", dataIndex: "id" },
          { title: "名称", dataIndex: "name" },
          { title: "类型", dataIndex: "type" },
          { title: "凭证", dataIndex: "credentialName", render: (u) => u ?? "—" },
          {
            title: "连接测试",
            key: "test",
            render: (u, g) => {
              const k = j.get(g.id);
              return k ? /* @__PURE__ */ i(P.Text, { type: k.ok ? "success" : "danger", children: k.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (u, g) => /* @__PURE__ */ b(Q, { size: 4, children: [
              /* @__PURE__ */ i(A, { size: "small", onClick: () => void N(g.id), children: "测试连接" }),
              /* @__PURE__ */ i(A, { size: "small", onClick: () => _(g), children: "编辑" }),
              /* @__PURE__ */ i(A, { size: "small", danger: !0, onClick: () => void $(g.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ i(
      Pe,
      {
        open: h,
        title: c.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: f,
        onCancel: () => I(!1),
        onOk: () => void C(),
        children: /* @__PURE__ */ b(Q, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(
            F,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: c.id,
              onChange: (u) => p({ ...c, id: u.target.value })
            }
          ),
          /* @__PURE__ */ i(
            F,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: c.name,
              onChange: (u) => p({ ...c, name: u.target.value })
            }
          ),
          /* @__PURE__ */ i(
            oe,
            {
              "aria-label": "资源类型",
              value: c.type,
              options: ir,
              onChange: (u) => p({ ...c, type: u })
            }
          ),
          (c.type === "filesystem.local" || c.type === "filesystem.unc") && /* @__PURE__ */ i(
            F,
            {
              "aria-label": "目录路径",
              placeholder: c.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: c.root,
              onChange: (u) => p({ ...c, root: u.target.value })
            }
          ),
          (c.type === "sqlserver" || c.type === "postgresql" || c.type === "mysql") && /* @__PURE__ */ b(Q, { children: [
            /* @__PURE__ */ i(
              F,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: c.host,
                onChange: (u) => p({ ...c, host: u.target.value })
              }
            ),
            /* @__PURE__ */ i(
              F,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: c.database,
                onChange: (u) => p({ ...c, database: u.target.value })
              }
            ),
            /* @__PURE__ */ i(
              F,
              {
                "aria-label": "端口",
                placeholder: "端口",
                value: c.port,
                onChange: (u) => p({ ...c, port: u.target.value })
              }
            )
          ] }),
          c.type === "sqlite" && /* @__PURE__ */ i(
            F,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: c.database,
              onChange: (u) => p({ ...c, database: u.target.value })
            }
          ),
          /* @__PURE__ */ i(
            oe,
            {
              "aria-label": "凭证",
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: c.credentialId || void 0,
              options: o.map((u) => ({ value: u.id, label: u.name })),
              onChange: (u) => p({ ...c, credentialId: u ?? "" })
            }
          ),
          /* @__PURE__ */ b(Q, { children: [
            /* @__PURE__ */ i(
              Qe,
              {
                "aria-label": "启用资源",
                checked: c.enabled,
                onChange: (u) => p({ ...c, enabled: u })
              }
            ),
            /* @__PURE__ */ i(P.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
function dr({ block: e, ctx: t }) {
  const [r, n] = w([]), [o, a] = w(!1), [s, l] = w(""), [c, p] = w(""), [h, I] = w("username-password"), [T, v] = w(""), [f, x] = w(!1), [S, _] = w(null), [C, N] = w(!1), $ = O(async () => {
    try {
      n(et(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
    } catch (k) {
      _(k instanceof Error ? k.message : String(k));
    }
  }, [e == null ? void 0 : e.query, t]);
  Y(() => {
    $();
  }, [$]);
  const j = O(() => {
    l(""), p(""), I("username-password"), v(""), x(!1), a(!0);
  }, []), u = O((k) => {
    l(k.id), p(k.name), I(k.kind), v(""), x(!0), a(!0);
  }, []), g = O(async () => {
    if (!s.trim()) {
      _("请输入凭证 ID");
      return;
    }
    if (!T.trim()) {
      _("请输入凭证内容");
      return;
    }
    N(!0), _(null);
    try {
      f ? await t.hubCommand("industrial/rotate-credential", { id: s, secret: T }) : await t.hubCommand("industrial/save-credential", { id: s, name: c || s, kind: h, secret: T }), a(!1), await $();
    } catch (k) {
      _(k instanceof Error ? k.message : String(k));
    } finally {
      N(!1);
    }
  }, [t, s, h, $, c, f, T]);
  return /* @__PURE__ */ b(Q, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ b(Q, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ i(P.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ i(A, { type: "primary", onClick: j, children: "新建凭证" })
    ] }),
    S && /* @__PURE__ */ i(pe, { type: "error", showIcon: !0, message: S }),
    /* @__PURE__ */ i(
      de,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        columns: [
          { title: "ID", dataIndex: "id" },
          { title: "名称", dataIndex: "name" },
          { title: "类型", dataIndex: "kind" },
          { title: "版本", dataIndex: "version" },
          {
            title: "操作",
            key: "actions",
            render: (k, H) => /* @__PURE__ */ i(A, { size: "small", onClick: () => u(H), children: "轮换" })
          }
        ]
      }
    ),
    /* @__PURE__ */ i(
      Pe,
      {
        open: o,
        title: f ? "轮换凭证" : "新建凭证",
        okText: f ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: C,
        onCancel: () => a(!1),
        onOk: () => void g(),
        children: /* @__PURE__ */ b(Q, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(
            F,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: s,
              disabled: f,
              onChange: (k) => l(k.target.value)
            }
          ),
          !f && /* @__PURE__ */ i(
            F,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: c,
              onChange: (k) => p(k.target.value)
            }
          ),
          /* @__PURE__ */ i(
            F,
            {
              "aria-label": "凭证内容",
              placeholder: f ? "新凭证内容" : "凭证内容",
              value: T,
              onChange: (k) => v(k.target.value)
            }
          ),
          S && /* @__PURE__ */ i(P.Text, { type: "danger", children: S })
        ] })
      }
    )
  ] });
}
const Me = (e) => e === "Succeeded" || e === "Completed" ? "success" : e === "Failed" || e === "PartialFailure" ? "error" : e === "Running" || e === "Interrupted" ? "processing" : "default";
function ur({ block: e, ctx: t }) {
  const [r, n] = w([]), [o, a] = w([]), [s, l] = w(null), [c, p] = w(null), [h, I] = w(!1), T = O(async () => {
    try {
      const f = tr(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/execution-center"));
      n(f.runs), a(f.deliveries);
    } catch (f) {
      l(f instanceof Error ? f.message : String(f));
    }
  }, [e == null ? void 0 : e.query, t]);
  Y(() => {
    T();
  }, [T]);
  const v = O(async (f) => {
    I(!0), l(null), p(null);
    try {
      await t.hubCommand("industrial/retry-failed", { targetId: f.targetId }), p(`已提交目标 ${f.targetId} 的重试`), await T();
    } catch (x) {
      l(x instanceof Error ? x.message : String(x));
    } finally {
      I(!1);
    }
  }, [t, T]);
  return /* @__PURE__ */ b(Q, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ i(P.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    s && /* @__PURE__ */ i(pe, { type: "error", showIcon: !0, message: s }),
    c && /* @__PURE__ */ i(pe, { type: "success", showIcon: !0, message: c }),
    /* @__PURE__ */ i(
      de,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (f) => /* @__PURE__ */ i(ne, { color: Me(f), children: f }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (f) => f ?? "—" },
          { title: "错误", dataIndex: "error", render: (f) => f ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ i(
      de,
      {
        rowKey: "id",
        dataSource: o,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (f) => /* @__PURE__ */ i(ne, { color: Me(f), children: f }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          { title: "错误", dataIndex: "error", render: (f) => f ?? "—" },
          {
            title: "操作",
            key: "actions",
            render: (f, x) => /* @__PURE__ */ i(
              A,
              {
                size: "small",
                disabled: h || x.status !== "Failed" && x.status !== "Interrupted",
                onClick: () => void v(x),
                children: "重试此目标"
              }
            )
          }
        ]
      }
    )
  ] });
}
const pr = `
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
function fr(e) {
  dt(e);
  const t = typeof document > "u" ? null : document.createElement("style");
  t && (t.dataset.industrialDesigner = "true", t.textContent = pr, document.head.appendChild(t));
  const r = ({ ctx: a }) => ke(sr, { ctx: a });
  r.displayName = "IndustrialWorkflowCanvas";
  const n = (a, s) => {
    const l = ({ block: c, ctx: p }) => ke(s, { block: c, ctx: p });
    return l.displayName = a, e.blocks.register(a, l);
  }, o = [
    e.blocks.register("industrial-workflow-canvas", r),
    n("industrial-workflow-list", ar),
    n("industrial-resource-console", cr),
    n("industrial-credential-console", dr),
    n("industrial-execution-center", ur)
  ];
  return () => {
    for (const a of o) a == null || a();
    t == null || t.remove();
  };
}
export {
  fr as default,
  fr as register
};
