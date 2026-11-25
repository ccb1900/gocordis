let Q = null;
function ve(e) {
  Q = e;
}
function D() {
  if (!Q && typeof globalThis < "u" && (Q = globalThis.__CORDIS_CONSOLE ?? null), !Q)
    throw new Error("[industrial-designer] console facade is not available");
  return Q;
}
const i = (...e) => D().jsxRuntime.jsx(...e), T = (...e) => D().jsxRuntime.jsxs(...e), ne = (e) => D().jsxRuntime.jsx(D().jsxRuntime.Fragment, e), ue = (...e) => D().React.createElement(...e), B = (...e) => D().React.useCallback(...e), V = (...e) => D().React.useEffect(...e), de = (...e) => D().React.useMemo(...e), J = (...e) => D().React.useRef(...e), R = (...e) => D().React.useState(...e), w = (e) => {
  const t = (n) => {
    const r = e.split(".").reduce((s, l) => s == null ? void 0 : s[l], D().antd);
    if (!r) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return ue(r, n ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, ke = w("Alert"), W = w("Button"), ie = w("Divider"), pe = w("Empty"), re = Object.assign(w("Form"), {
  Item: w("Form.Item"),
  useForm: (...e) => D().antd.Form.useForm(...e)
}), fe = Object.assign(w("Input"), {
  Number: w("InputNumber"),
  TextArea: w("Input.TextArea")
}), Ne = w("InputNumber"), ge = Object.assign(w("Layout"), {
  Header: w("Layout.Header"),
  Content: w("Layout.Content")
});
w("Modal");
const K = w("Select"), Pe = w("Space"), ye = w("Spin"), xe = w("Switch"), Ee = w("Table"), q = w("Tag"), E = Object.assign(w("Typography"), {
  Title: w("Typography.Title"),
  Text: w("Typography.Text")
}), Ce = new Proxy({}, {
  get: (e, t) => (...n) => {
    var s;
    const r = (s = D().antd.message) == null ? void 0 : s[t];
    if (typeof r != "function") throw new Error(`[industrial-designer] message.${t} unavailable`);
    return r(...n);
  }
}), ae = {
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
}, ce = (e) => ae[e] ?? ae[e.split(".")[0]] ?? "#4d6bfe";
function $e(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function De(e, t = /* @__PURE__ */ new Map(), n) {
  return {
    nodes: e.nodes.map((r) => {
      const s = t.get(r.type), l = (s == null ? void 0 : s.category) ?? "General", c = n === r.id;
      return {
        id: r.id,
        data: {
          label: (s == null ? void 0 : s.name) ?? r.type,
          nodeTypeId: r.type,
          category: l,
          icon: (s == null ? void 0 : s.icon) ?? "AppstoreOutlined",
          // Domain config travels with the node so selection can edit it
          // without a second lookup; it is never written back from G6.
          config: { ...r.config }
        },
        style: {
          x: r.x,
          y: r.y,
          fill: ce(l),
          stroke: c ? "#111111" : ce(l),
          lineWidth: c ? 3 : 1.5,
          radius: 8
        }
      };
    }),
    edges: e.edges.map((r) => ({
      id: $e(r),
      source: r.source,
      target: r.target,
      data: {
        label: `${r.sourcePort} → ${r.targetPort}`,
        sourcePort: r.sourcePort,
        targetPort: r.targetPort
      },
      style: { stroke: "#8a93a6", lineWidth: 1.5, endArrow: !0 }
    }))
  };
}
function Oe(e, t) {
  const n = {};
  for (const r of e.nodes) {
    if (!t.has(r.id)) continue;
    const s = r.style.x, l = r.style.y;
    typeof s == "number" && typeof l == "number" && (n[r.id] = { x: s, y: l });
  }
  return n;
}
function _e(e, t) {
  return {
    container: t,
    autoFit: "view",
    data: e,
    node: {
      type: "rect",
      style: {
        labelText: (n) => {
          var r;
          return ((r = n == null ? void 0 : n.data) == null ? void 0 : r.label) ?? "";
        },
        labelPlacement: "bottom",
        size: [140, 42],
        radius: 8,
        fill: "#e6f0ff",
        stroke: "#4d6bfe",
        lineWidth: 1.5,
        labelFill: "#1f1f1f",
        labelFontSize: 11
      }
    },
    edge: {
      style: {
        labelText: (n) => {
          var r;
          return ((r = n == null ? void 0 : n.data) == null ? void 0 : r.label) ?? "";
        },
        labelFontSize: 10,
        stroke: "#8a93a6",
        endArrow: !0
      }
    },
    behaviors: [
      "drag-canvas",
      "zoom-canvas",
      { type: "drag-element", onFinish: () => {
      } },
      { type: "create-edge", trigger: "drag", onCreate: (n) => n }
    ]
  };
}
const Re = (e) => {
  var n;
  const t = e;
  return (t == null ? void 0 : t.targetType) === "node" ? (n = t.target) == null ? void 0 : n.id : void 0;
};
function Ae(e) {
  const { definition: t, catalog: n, selectedId: r, onSelect: s, onDropNode: l, onCreateEdge: c, onPositions: o } = e, d = J(null), m = J(null), y = J(!1), S = J({ onSelect: s, onDropNode: l, onCreateEdge: c, onPositions: o });
  S.current = { onSelect: s, onDropNode: l, onCreateEdge: c, onPositions: o };
  const O = de(
    () => De(t, n, r),
    [t, n, r]
  ), C = J(O);
  return C.current = O, V(() => {
    const p = d.current;
    if (!p || y.current) return;
    let b = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: _ }) => {
      if (b || !d.current || y.current) return;
      const F = _e(C.current, p), j = new _({
        ...F,
        behaviors: [
          "drag-canvas",
          "zoom-canvas",
          {
            type: "drag-element",
            onFinish: () => {
              const a = m.current;
              if (!a) return;
              const g = new Set(C.current.nodes.map((I) => String(I.id))), N = a.getNodeData().map((I) => ({
                id: String(I.id),
                data: I.data ?? {},
                style: I.style ?? {}
              }));
              S.current.onPositions(Oe({ nodes: N }, g));
            }
          },
          {
            type: "create-edge",
            trigger: "drag",
            onCreate: (a) => {
              const g = String(a.source ?? ""), N = String(a.target ?? "");
              if (!(!g || !N || g === N))
                return S.current.onCreateEdge(g, N) ? a : void 0;
            }
          }
        ]
      });
      j.on("node:click", (a) => {
        const g = Re(a);
        g && S.current.onSelect(g);
      }), j.on("canvas:click", () => S.current.onSelect(null)), m.current = j, y.current = !0, j.render();
    }), () => {
      b = !0;
    };
  }, []), V(() => {
    const p = m.current;
    !p || !y.current || (p.setData(O), p.render());
  }, [O]), V(() => () => {
    var p;
    (p = m.current) == null || p.destroy(), m.current = null, y.current = !1;
  }, []), /* @__PURE__ */ i(
    "div",
    {
      ref: d,
      className: "industrial-graph",
      onDragOver: (p) => p.preventDefault(),
      onDrop: (p) => {
        p.preventDefault();
        const b = p.dataTransfer.getData("application/x-industrial-node") || p.dataTransfer.getData("text/plain");
        if (!b) return;
        const _ = m.current, F = d.current;
        if (!_ || !F) return;
        const j = F.getBoundingClientRect(), a = _.getCanvasByViewport([
          p.clientX - j.left,
          p.clientY - j.top
        ]);
        S.current.onDropNode(b, a[0], a[1]);
      },
      "data-testid": "industrial-graph"
    }
  );
}
function Fe({ descriptors: e, loading: t, error: n }) {
  const r = /* @__PURE__ */ new Map();
  for (const l of e) {
    const c = l.category.split(".")[0] || "General", o = r.get(c) ?? [];
    o.push(l), r.set(c, o);
  }
  const s = (l, c) => {
    l.dataTransfer.setData("application/x-industrial-node", c.nodeTypeId), l.dataTransfer.setData("text/plain", c.nodeTypeId), l.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ T("aside", { className: "industrial-palette", children: [
    /* @__PURE__ */ i(E.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ i(ye, { size: "small", style: { display: "block", marginTop: 16 } }),
    n && /* @__PURE__ */ i(E.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: n }),
    !t && !n && e.length === 0 && /* @__PURE__ */ i(pe, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(r.entries()).map(([l, c]) => /* @__PURE__ */ T("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ i(E.Text, { strong: !0, style: { fontSize: 12 }, children: l }),
      c.map((o) => /* @__PURE__ */ T(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          onDragStart: (d) => s(d, o),
          title: o.description || o.nodeTypeId,
          children: [
            /* @__PURE__ */ i("div", { className: "industrial-palette-node-title", children: o.name }),
            /* @__PURE__ */ T("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ i(q, { bordered: !1, color: "blue", children: o.version }),
              /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 11 }, children: o.nodeTypeId })
            ] }),
            /* @__PURE__ */ T("div", { className: "industrial-palette-node-ports", children: [
              o.inputs.map((d) => /* @__PURE__ */ T(q, { bordered: !1, color: "default", children: [
                "in ",
                d.name,
                ": ",
                d.type
              ] }, `in-${d.name}`)),
              o.outputs.map((d) => /* @__PURE__ */ T(q, { bordered: !1, color: "green", children: [
                "out ",
                d.name,
                ": ",
                d.type
              ] }, `out-${d.name}`))
            ] })
          ]
        },
        o.nodeTypeId
      ))
    ] }, l))
  ] });
}
function je({ name: e, schema: t }) {
  let n;
  return t.enum && t.enum.length > 0 ? n = /* @__PURE__ */ i(
    K,
    {
      allowClear: !0,
      options: t.enum.map((r) => ({ label: String(r), value: String(r) }))
    }
  ) : t.type === "number" || t.type === "integer" ? n = /* @__PURE__ */ i(Ne, { style: { width: "100%" } }) : t.type === "boolean" ? n = /* @__PURE__ */ i(xe, {}) : n = /* @__PURE__ */ i(fe, { placeholder: t.description }), /* @__PURE__ */ i(re.Item, { name: e, label: t.title ?? e, tooltip: t.description, children: n }, e);
}
function ze(e) {
  const {
    node: t,
    descriptor: n,
    edges: r,
    catalog: s,
    issues: l,
    onApplyConfig: c,
    onDeleteNode: o,
    onDeleteEdge: d,
    onUpdateEdge: m,
    onSelectNode: y
  } = e, [S] = re.useForm(), [O, C] = R("{}"), [M, p] = R(null);
  V(() => {
    S.setFieldsValue((t == null ? void 0 : t.config) ?? {}), C(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), p(null);
  }, [S, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const b = Object.entries((n == null ? void 0 : n.configurationSchema.properties) ?? {}), _ = () => {
    if (b.length > 0) {
      S.validateFields().then((a) => c(a));
      return;
    }
    try {
      const a = JSON.parse(O);
      if (a === null || typeof a != "object" || Array.isArray(a))
        throw new Error("配置必须是 JSON 对象");
      p(null), c(a);
    } catch (a) {
      p(a.message);
    }
  }, F = r.map((a, g) => ({
    ...a,
    index: g,
    key: `${g}:${a.source}->${a.target}`
  })), j = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (a, g) => /* @__PURE__ */ T(E.Text, { style: { fontSize: 11 }, children: [
        g.source,
        " → ",
        g.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (a, g) => {
        const N = s.get(g.source), I = s.get(g.target);
        return /* @__PURE__ */ T("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ i(
            K,
            {
              size: "small",
              value: g.sourcePort,
              style: { width: 110 },
              onChange: (P) => m(g.index, { ...g, sourcePort: P }),
              options: ((N == null ? void 0 : N.outputs) ?? []).map((P) => ({ label: `${P.name} (${P.type})`, value: P.name }))
            }
          ),
          /* @__PURE__ */ i(
            K,
            {
              size: "small",
              value: g.targetPort,
              style: { width: 110 },
              onChange: (P) => m(g.index, { ...g, targetPort: P }),
              options: ((I == null ? void 0 : I.inputs) ?? []).map((P) => ({ label: `${P.name} (${P.type})`, value: P.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (a, g) => /* @__PURE__ */ i(W, { danger: !0, size: "small", type: "text", onClick: () => d(g.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ T("aside", { className: "industrial-properties", children: [
    /* @__PURE__ */ i(E.Title, { level: 5, style: { marginTop: 0 }, children: "节点属性" }),
    !t && /* @__PURE__ */ i(pe, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ T(ne, { children: [
      /* @__PURE__ */ i(E.Text, { strong: !0, children: (n == null ? void 0 : n.name) ?? t.type }),
      /* @__PURE__ */ i(q, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ i(re, { form: S, layout: "vertical", size: "small", style: { marginTop: 12 }, children: b.map(([a, g]) => /* @__PURE__ */ i(je, { name: a, schema: g }, a)) }),
      b.length === 0 && /* @__PURE__ */ T(ne, { children: [
        /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ i(
          fe.TextArea,
          {
            rows: 8,
            value: O,
            style: { marginTop: 6 },
            onChange: (a) => C(a.target.value)
          }
        ),
        M && /* @__PURE__ */ i(E.Text, { type: "danger", style: { fontSize: 12 }, children: M })
      ] }),
      /* @__PURE__ */ i(W, { type: "primary", block: !0, onClick: _, children: "应用配置" }),
      /* @__PURE__ */ i(W, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: o, children: "删除节点" }),
      /* @__PURE__ */ T(ie, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "连线 (",
        r.length,
        ")"
      ] }),
      r.length === 0 ? /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点输出拖到另一节点输入即可连线。" }) : /* @__PURE__ */ i(Ee, { size: "small", columns: j, dataSource: F, pagination: !1 }),
      (n == null ? void 0 : n.activityType) && /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: n.activityType })
    ] }),
    l.length > 0 && /* @__PURE__ */ T(ne, { children: [
      /* @__PURE__ */ T(ie, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        l.length,
        ")"
      ] }),
      l.map((a, g) => /* @__PURE__ */ T("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ i(q, { color: "red", children: a.code }),
        /* @__PURE__ */ T(
          E.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const N = /^nodes\.([^.]+)/.exec(a.path);
              N && y(N[1]);
            },
            children: [
              a.path,
              ": ",
              a.message
            ]
          }
        )
      ] }, `${a.path}-${g}`))
    ] })
  ] });
}
const Me = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class Le extends Error {
  constructor(t) {
    super(t), this.name = "DtoError";
  }
}
const A = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, Y = (e) => Array.isArray(e) ? e : [], v = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", oe = (e, t = 0) => {
  const n = typeof e == "string" ? Number(e) : e;
  return typeof n == "number" && Number.isFinite(n) ? n : t;
}, f = (e, ...t) => {
  for (const n of t)
    if (e[n] !== void 0 && e[n] !== null) return e[n];
};
function X(e) {
  const t = A(e);
  if (Object.prototype.hasOwnProperty.call(t, "data")) return t.data;
  if (typeof t.code == "string" && typeof t.message == "string")
    throw new Le(`${t.code}: ${t.message}`);
  return e;
}
function Ge(e) {
  const t = v(e).trim().toLowerCase();
  return Me.find((r) => r.toLowerCase() === t) ?? "Unknown";
}
function We(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const t = A(e), n = v(f(t, "name", "port", "id")).trim();
  return n ? {
    name: n,
    type: Ge(f(t, "type", "portType", "dataType")),
    required: f(t, "required", "isRequired") === !0
  } : null;
}
function le(e) {
  const t = Y(e).map(We).filter((r) => r !== null), n = /* @__PURE__ */ new Set();
  return t.filter((r) => n.has(r.name) ? !1 : (n.add(r.name), !0));
}
const Ue = (e) => {
  const t = A(e);
  return Object.keys(t).length === 0 ? { type: "object", properties: {} } : t;
};
function Be(e) {
  const t = A(e), n = v(f(t, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return n ? {
    nodeTypeId: n,
    name: v(f(t, "name", "title"), n),
    description: v(f(t, "description", "desc")),
    category: v(f(t, "category", "group"), "General"),
    icon: v(f(t, "icon"), "AppstoreOutlined"),
    version: v(f(t, "version"), "1.0.0"),
    inputs: le(f(t, "inputs", "inputPorts", "input_ports")),
    outputs: le(f(t, "outputs", "outputPorts", "output_ports")),
    configurationSchema: Ue(f(t, "configurationSchema", "configSchema", "schema")),
    activityType: v(f(t, "activityType", "activity_type")),
    dependencies: Y(f(t, "dependencies")).map((r) => v(r)).filter(Boolean)
  } : null;
}
function qe(e) {
  const t = X(e), n = Array.isArray(t) ? t : Y(f(A(t), "nodes", "items")), r = /* @__PURE__ */ new Set();
  return n.map(Be).filter((s) => s !== null).filter((s) => r.has(s.nodeTypeId) ? !1 : (r.add(s.nodeTypeId), !0));
}
function me(e) {
  const t = A(e), n = v(f(t, "workflowId", "workflow_id", "id")).trim();
  return n ? {
    workflowId: n,
    name: v(f(t, "name", "title"), n),
    version: oe(f(t, "version"), 1)
  } : null;
}
function Ye(e) {
  const t = X(e);
  return (Array.isArray(t) ? t : Y(f(A(t), "workflows", "items"))).map(me).filter((r) => r !== null);
}
function he(e) {
  const t = A(e), n = {};
  for (const [r, s] of Object.entries(t))
    s !== void 0 && (n[r] = s);
  return n;
}
function He(e) {
  const t = A(e), n = v(f(t, "id", "nodeId", "node_id")).trim();
  if (!n) return null;
  const r = A(f(t, "position", "layout"));
  return {
    id: n,
    type: v(f(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: he(f(t, "config", "configuration")),
    x: oe(f(t, "x") ?? f(r, "x"), 0),
    y: oe(f(t, "y") ?? f(r, "y"), 0)
  };
}
function Je(e) {
  const t = A(e), n = v(f(t, "source", "from", "sourceId")).trim(), r = v(f(t, "target", "to", "targetId")).trim();
  return !n || !r ? null : {
    source: n,
    sourcePort: v(f(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: r,
    targetPort: v(f(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function Qe(e) {
  const t = X(e), n = A(t), r = me(n) ?? { workflowId: "", name: "", version: 1 }, s = Y(f(n, "nodes")).map(He).filter((c) => c !== null), l = Y(f(n, "edges")).map(Je).filter((c) => c !== null);
  return {
    workflowId: r.workflowId,
    name: r.name,
    version: r.version,
    nodes: s,
    edges: l,
    layout: he(f(n, "layout"))
  };
}
const Ve = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  layout: {}
});
function Ke(e) {
  const t = new Set(e.map((n) => n.id));
  for (let n = 1; ; n++) {
    const r = `n${n}`;
    if (!t.has(r)) return r;
  }
}
function Xe(e) {
  const t = {};
  for (const [n, r] of Object.entries(e.configurationSchema.properties ?? {}))
    r.default !== void 0 && (t[n] = r.default);
  return t;
}
function Ze(e, t, n = 0, r = 0) {
  return { id: t, type: e.nodeTypeId, config: Xe(e), x: n, y: r };
}
function U(e, t) {
  return e.get(t);
}
function ee(e, t, n) {
  return e[n].find((r) => r.name === t);
}
function se(e, t) {
  return e !== "Unknown" && t !== "Unknown" && e === t;
}
function et(e, t) {
  for (const n of e.outputs)
    for (const r of t.inputs)
      if (se(n.type, r.type))
        return { sourcePort: n.name, targetPort: r.name };
  return null;
}
const x = (e, t, n) => ({ path: e, code: t, message: n });
function tt(e, t) {
  return e.source === t.source && e.sourcePort === t.sourcePort && e.target === t.target && e.targetPort === t.targetPort;
}
function nt(e, t, n) {
  const r = `edges.${e.edges.length}`, s = e.nodes.find((y) => y.id === t.source), l = e.nodes.find((y) => y.id === t.target);
  if (!s || !l)
    return { definition: e, issue: x(r, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((y) => tt(y, t)))
    return { definition: e };
  const c = U(n, s.type), o = U(n, l.type);
  if (!c || !o)
    return { definition: e, issue: x(r, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const d = ee(c, t.sourcePort, "outputs"), m = ee(o, t.targetPort, "inputs");
  return !d || !m ? { definition: e, issue: x(r, "PORT_MISSING", "declared port does not exist") } : se(d.type, m.type) ? { definition: { ...e, edges: [...e.edges, t] } } : {
    definition: e,
    issue: x(
      r,
      "PORT_TYPE_INCOMPATIBLE",
      `${d.type} → ${m.type} is not a legal connection`
    )
  };
}
function rt(e, t) {
  return {
    ...e,
    nodes: e.nodes.filter((n) => n.id !== t),
    edges: e.edges.filter((n) => n.source !== t && n.target !== t)
  };
}
function ot(e, t) {
  return { ...e, edges: e.edges.filter((n, r) => r !== t) };
}
function st(e, t, n) {
  return {
    ...e,
    nodes: e.nodes.map((r) => r.id === t ? { ...r, config: n } : r)
  };
}
function it(e, t) {
  return {
    ...e,
    nodes: e.nodes.map((n) => {
      const r = t[n.id];
      return r ? { ...n, x: r.x, y: r.y } : n;
    })
  };
}
function at(e) {
  var l;
  const t = /* @__PURE__ */ new Map();
  for (const c of e.nodes) t.set(c.id, []);
  for (const c of e.edges)
    (l = t.get(c.source)) == null || l.push(c.target);
  const n = /* @__PURE__ */ new Map(), r = [], s = (c) => {
    n.set(c, 1), r.push(c);
    for (const o of t.get(c) ?? []) {
      if (n.get(o) === 1)
        return [...r.slice(r.indexOf(o)), o];
      if (n.get(o) !== 2) {
        const d = s(o);
        if (d) return d;
      }
    }
    return r.pop(), n.set(c, 2), null;
  };
  for (const c of e.nodes)
    if (n.get(c.id) !== 2) {
      const o = s(c.id);
      if (o) return o;
    }
  return null;
}
function ct(e, t, n) {
  const r = [];
  e.workflowId.trim() || r.push(x("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && r.push(x("workflow", "INVALID_VERSION", "version must be positive"));
  const s = /* @__PURE__ */ new Map();
  for (const o of e.nodes)
    s.has(o.id) && r.push(x(`nodes.${o.id}`, "DUPLICATE_NODE_ID", `duplicate node '${o.id}'`)), s.set(o.id, o);
  const l = /* @__PURE__ */ new Map();
  for (const o of e.nodes) {
    const d = U(t, o.type);
    if (!d) {
      r.push(x(
        `nodes.${o.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${o.type}' is not registered`
      ));
      continue;
    }
    l.set(o.id, d);
    const m = d.configurationSchema.required ?? [];
    for (const y of m) {
      const S = o.config[y];
      (S == null || S === "") && r.push(x(
        `nodes.${o.id}.config.${y}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${y}' is missing`
      ));
    }
    n && typeof o.config.resourceId == "string" && (n.has(o.config.resourceId) || r.push(x(
      `nodes.${o.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${o.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((o, d) => {
    const m = s.get(o.source), y = s.get(o.target);
    if (!m || !y) {
      r.push(x(`edges.${d}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const S = l.get(o.source), O = l.get(o.target);
    if (!S || !O) return;
    const C = ee(S, o.sourcePort, "outputs"), M = ee(O, o.targetPort, "inputs");
    if (!C || !M) {
      r.push(x(`edges.${d}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    se(C.type, M.type) || r.push(x(
      `edges.${d}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${C.type} → ${M.type} is not a legal connection`
    ));
  });
  for (const o of e.nodes) {
    const d = l.get(o.id);
    if (d)
      for (const m of d.inputs.filter((y) => y.required))
        e.edges.some((y) => y.target === o.id && y.targetPort === m.name && s.has(y.source)) || r.push(x(
          `nodes.${o.id}.inputs.${m.name}`,
          "MISSING_INPUT",
          `required input '${m.name}' is not connected`
        ));
  }
  const c = at(e);
  return c && r.push(x(`nodes.${c[0]}`, "GRAPH_CYCLE", `cycle: ${c.join(" → ")}`)), r;
}
const { Header: lt, Content: ut } = ge;
function dt({ ctx: e }) {
  const [t, n] = Ce.useMessage(), [r, s] = R([]), [l, c] = R([]), [o, d] = R([]), [m, y] = R(""), [S, O] = R(!0), [C, M] = R(null), [p, b] = R(() => Ve()), [_, F] = R(null), [j, a] = R([]), [g, N] = R(!1);
  V(() => {
    let u = !0;
    return Promise.allSettled([
      e.hubQuery("industrial/nodes"),
      e.hubQuery("industrial/workflows"),
      e.hubQuery("industrial/resources")
    ]).then(([h, $, k]) => {
      if (u && (h.status === "fulfilled" ? s(qe(h.value)) : M(`节点描述加载失败: ${String(h.reason)}`), $.status === "fulfilled" && c(Ye($.value)), k.status === "fulfilled")) {
        const L = (X(k.value) ?? []).map((G) => String((G == null ? void 0 : G.id) ?? "")).filter(Boolean);
        d(L), y((G) => G || L[0] || "");
      }
    }).finally(() => {
      u && O(!1);
    }), () => {
      u = !1;
    };
  }, [e]);
  const I = de(
    () => new Map(r.map((u) => [u.nodeTypeId, u])),
    [r]
  ), P = p.nodes.find((u) => u.id === _) ?? null, te = B(async (u, h, $) => {
    N(!0);
    try {
      await e.hubCommand(u, h), t.success($);
    } catch (k) {
      t.error(`${u} failed: ${k instanceof Error ? k.message : String(k)}`);
    } finally {
      N(!1);
    }
  }, [e, t]), we = B(async (u) => {
    try {
      const h = await e.hubQuery("industrial/workflow-definitions"), k = (X(h) ?? []).find((z) => {
        const L = z;
        return String((L == null ? void 0 : L.workflowId) ?? "") === u;
      });
      if (k === void 0) return;
      b(Qe(k)), F(null), a([]);
    } catch (h) {
      t.error(`加载工作流失败: ${h instanceof Error ? h.message : String(h)}`);
    }
  }, [e, t]), Te = B((u, h, $) => {
    const k = U(I, u);
    k && (b((z) => ({
      ...z,
      nodes: [...z.nodes, Ze(k, Ke(z.nodes), h, $)]
    })), a([]));
  }, [I]), Ie = B((u, h) => {
    const $ = p.nodes.find((H) => H.id === u), k = p.nodes.find((H) => H.id === h);
    if (!$ || !k) return !1;
    const z = U(I, $.type), L = U(I, k.type);
    if (!z || !L) return !1;
    const G = et(z, L);
    if (!G) return !1;
    const Z = nt(p, {
      source: u,
      sourcePort: G.sourcePort,
      target: h,
      targetPort: G.targetPort
    }, I);
    return Z.issue ? a((H) => [...H, Z.issue]) : b(Z.definition), !Z.issue;
  }, [p, I]), Se = B(() => {
    const u = ct(p, I, new Set(o));
    a(u), u.length === 0 ? t.success("校验通过") : t.warning(`发现 ${u.length} 个问题，见右侧定位列表`);
  }, [p, I, o, t]), be = B((u, h) => {
    b(($) => ({
      ...$,
      edges: $.edges.map((k, z) => z === u ? h : k)
    }));
  }, []);
  return S ? /* @__PURE__ */ i(ye, { tip: "加载节点描述与资源…" }) : C ? /* @__PURE__ */ i(ke, { type: "error", message: C }) : /* @__PURE__ */ T(ge, { style: { minHeight: 520, border: "1px solid #f0f0f0" }, children: [
    n,
    /* @__PURE__ */ T(lt, { style: { background: "#001529", display: "flex", alignItems: "center", gap: 12 }, children: [
      /* @__PURE__ */ i(E.Text, { strong: !0, style: { color: "#fff" }, children: "Industrial Designer" }),
      /* @__PURE__ */ i(q, { color: "blue", children: "Cordis block renderer" }),
      /* @__PURE__ */ T(Pe, { children: [
        /* @__PURE__ */ i(W, { loading: g, onClick: () => void te("industrial/save-workflow", p, "已保存"), children: "保存" }),
        /* @__PURE__ */ i(W, { onClick: Se, children: "校验" }),
        /* @__PURE__ */ i(W, { loading: g, onClick: () => void te(
          "industrial/publish-workflow",
          { workflowId: p.workflowId },
          "已发布"
        ), children: "发布" }),
        /* @__PURE__ */ i(W, { type: "primary", loading: g, onClick: () => void te(
          "industrial/run-workflow",
          { workflowId: p.workflowId, resourceId: m },
          "采集已启动"
        ), children: "运行" }),
        /* @__PURE__ */ i(
          K,
          {
            style: { width: 180 },
            value: m,
            onChange: y,
            options: o.map((u) => ({ value: u, label: u }))
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ T(ut, { style: { display: "grid", gridTemplateColumns: "220px 1fr 300px", gap: 12, padding: 12 }, children: [
      /* @__PURE__ */ i("div", { style: { borderRight: "1px solid #f0f0f0", paddingRight: 12 }, children: /* @__PURE__ */ i(Fe, { descriptors: r, loading: !1 }) }),
      /* @__PURE__ */ i(
        Ae,
        {
          definition: p,
          catalog: I,
          selectedId: _,
          onSelect: F,
          onDropNode: Te,
          onCreateEdge: Ie,
          onPositions: (u) => b((h) => it(h, u))
        }
      ),
      /* @__PURE__ */ T("div", { style: { borderLeft: "1px solid #f0f0f0", paddingLeft: 12 }, children: [
        l.length > 0 && /* @__PURE__ */ i(
          K,
          {
            style: { width: "100%", marginBottom: 12 },
            placeholder: "选择工作流",
            onChange: (u) => void we(u),
            options: l.map((u) => ({ value: u.workflowId, label: u.name }))
          }
        ),
        /* @__PURE__ */ i(
          ze,
          {
            node: P,
            descriptor: P ? U(I, P.type) ?? null : null,
            edges: p.edges,
            catalog: I,
            issues: j,
            onApplyConfig: (u) => {
              _ && b((h) => st(h, _, u));
            },
            onDeleteNode: () => {
              b((u) => rt(u, _ ?? "")), F(null);
            },
            onDeleteEdge: (u) => b((h) => ot(h, u)),
            onUpdateEdge: be,
            onSelectNode: F
          }
        )
      ] })
    ] })
  ] });
}
function pt(e) {
  ve(e);
  const t = ({ ctx: n }) => ue(dt, { ctx: n });
  return t.displayName = "IndustrialWorkflowCanvas", e.blocks.register("industrial-workflow-canvas", t);
}
export {
  pt as default,
  pt as register
};
