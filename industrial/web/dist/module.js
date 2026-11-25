let re = null;
function Le(e) {
  re = e;
}
function R() {
  if (!re && typeof globalThis < "u" && (re = globalThis.__CORDIS_CONSOLE ?? null), !re)
    throw new Error("[industrial-designer] console facade is not available");
  return re;
}
const a = (...e) => R().jsxRuntime.jsx(...e), w = (...e) => R().jsxRuntime.jsxs(...e), ue = (e) => R().jsxRuntime.jsx(R().jsxRuntime.Fragment, e), Ne = (...e) => R().React.createElement(...e), q = (...e) => R().React.useCallback(...e), oe = (...e) => R().React.useEffect(...e), Pe = (...e) => R().React.useMemo(...e), Q = (...e) => R().React.useRef(...e), $ = (...e) => R().React.useState(...e), b = (e) => {
  const t = (n) => {
    const r = e.split(".").reduce((s, l) => s == null ? void 0 : s[l], R().antd);
    if (!r) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Ne(r, n ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, we = b("Alert"), V = b("Button"), xe = b("Divider"), Ee = b("Empty"), de = Object.assign(b("Form"), {
  Item: b("Form.Item"),
  useForm: (...e) => R().antd.Form.useForm(...e)
}), $e = Object.assign(b("Input"), {
  Number: b("InputNumber"),
  TextArea: b("Input.TextArea")
}), Be = b("InputNumber"), We = Object.assign(b("Layout"), {
  Header: b("Layout.Header"),
  Content: b("Layout.Content")
});
b("Modal");
const se = b("Select"), be = b("Space"), Ce = b("Spin"), qe = b("Switch"), Ue = b("Table"), X = b("Tag"), _ = Object.assign(b("Typography"), {
  Title: b("Typography.Title"),
  Text: b("Typography.Text")
}), Ye = new Proxy({}, {
  get: (e, t) => (...n) => {
    var s;
    const r = (s = R().antd.message) == null ? void 0 : s[t];
    if (typeof r != "function") throw new Error(`[industrial-designer] message.${t} unavailable`);
    return r(...n);
  }
}), ve = {
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
}, Te = (e) => ve[e] ?? ve[e.split(".")[0]] ?? "#4d6bfe";
function He(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function Je(e, t = /* @__PURE__ */ new Map(), n) {
  return {
    nodes: e.nodes.map((r) => {
      const s = t.get(r.type), l = (s == null ? void 0 : s.category) ?? "General", i = n === r.id;
      return {
        id: r.id,
        data: {
          label: (s == null ? void 0 : s.name) ?? r.type,
          nodeTypeId: r.type,
          category: l,
          icon: (s == null ? void 0 : s.icon) ?? "AppstoreOutlined",
          config: { ...r.config }
        },
        style: {
          x: r.x,
          y: r.y,
          fill: Te(l),
          stroke: i ? "#111111" : Te(l),
          lineWidth: i ? 3 : 1.5,
          radius: 10,
          port: !0,
          ports: [
            ...((s == null ? void 0 : s.inputs) ?? []).map((o, u, y) => ({
              key: `in:${o.name}`,
              placement: [0, (u + 1) / (y.length + 1)],
              r: 5,
              stroke: o.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((s == null ? void 0 : s.outputs) ?? []).map((o, u, y) => ({
              key: `out:${o.name}`,
              placement: [1, (u + 1) / (y.length + 1)],
              r: 5,
              stroke: o.type === "Control" ? "#52c41a" : "#1677ff",
              fill: "#fff"
            }))
          ]
        }
      };
    }),
    edges: e.edges.map((r) => ({
      id: He(r),
      source: r.source,
      sourcePort: `out:${r.sourcePort}`,
      target: r.target,
      targetPort: `in:${r.targetPort}`,
      data: {
        label: `${r.sourcePort} → ${r.targetPort}`,
        sourcePort: r.sourcePort,
        targetPort: r.targetPort
      },
      style: { stroke: "#8a93a6", lineWidth: 1.5, endArrow: !0 }
    }))
  };
}
function Qe(e, t, n) {
  const r = n && n.length > 0 ? new Set(n) : null, s = {};
  for (const l of e.nodes) {
    if (!t.has(l.id) || r && !r.has(l.id)) continue;
    const i = l.style.x, o = l.style.y;
    typeof i == "number" && typeof o == "number" && (s[l.id] = { x: i, y: o });
  }
  return s;
}
function Ve(e, t) {
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
        labelText: (n) => {
          var r;
          return ((r = n == null ? void 0 : n.data) == null ? void 0 : r.label) ?? "";
        },
        labelFontSize: 10,
        stroke: "#8a93a6",
        endArrow: !0
      }
    },
    behaviors: _e()
  };
}
const Ke = "__industrial_drag_target__", Xe = "__industrial_connect_source__";
function _e(e = {}) {
  const t = () => {
    var n;
    return ((n = e.mode) == null ? void 0 : n.call(e)) ?? "select";
  };
  return [
    "zoom-canvas",
    {
      type: "drag-canvas",
      enable: (n) => t() === "pan" && n.targetType === "canvas",
      range: 0.9
    },
    {
      type: "drag-element",
      state: Ke,
      enable: (n) => t() === "select" && (n.targetType === "node" || n.targetType === "combo"),
      onFinish: (n) => {
        var i, o;
        const r = e.graph;
        if (!r) return;
        const s = Array.isArray(n) ? n : n ? [String(n)] : void 0, l = r.getNodeData().map((u) => ({
          id: String(u.id),
          data: u.data ?? {},
          style: u.style ?? {}
        }));
        (o = e.onPositions) == null || o.call(e, Qe(
          { nodes: l },
          ((i = e.allowedIds) == null ? void 0 : i.call(e)) ?? /* @__PURE__ */ new Set(),
          s
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: Xe,
      enable: () => t() === "connect",
      onCreate: (n) => {
        var i;
        if (t() !== "connect") return;
        const r = String(n.source ?? ""), s = String(n.target ?? "");
        if (!r || !s || r === s) return;
        const l = (i = e.onCreateEdge) == null ? void 0 : i.call(e, r, s);
        if (l)
          return {
            ...n,
            sourcePort: `out:${l.sourcePort}`,
            targetPort: `in:${l.targetPort}`
          };
      }
    }
  ];
}
const Ze = (e) => {
  var n;
  const t = e;
  return (t == null ? void 0 : t.targetType) === "node" ? (n = t.target) == null ? void 0 : n.id : void 0;
}, et = {
  select: "选择：拖动节点改变位置，点击节点编辑属性",
  connect: "连线：从节点拖到另一个节点，自动匹配兼容端口",
  pan: "平移：拖动画布移动视口（不移动节点）"
};
function tt(e) {
  const {
    definition: t,
    catalog: n,
    selectedId: r,
    mode: s,
    onSelect: l,
    onDropNode: i,
    onCreateEdge: o,
    onPositions: u
  } = e, y = Q(null), g = Q(null), I = Q(!1), z = Q(s);
  z.current = s;
  const S = Q({ onSelect: l, onDropNode: i, onCreateEdge: o, onPositions: u });
  S.current = { onSelect: l, onDropNode: i, onCreateEdge: o, onPositions: u };
  const A = Pe(
    () => Je(t, n, r),
    [t, n, r]
  ), x = Q(A);
  x.current = A;
  const j = Q(/* @__PURE__ */ new Set());
  j.current = new Set(t.nodes.map((m) => m.id)), oe(() => {
    const m = y.current;
    if (!m || I.current) return;
    let U = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: G }) => {
      if (U || !y.current || I.current) return;
      const p = Ve(x.current, m), f = new G({
        ...p,
        behaviors: _e({
          graph: { getNodeData: () => {
            var T;
            return ((T = g.current) == null ? void 0 : T.getNodeData()) ?? [];
          } },
          mode: () => z.current,
          allowedIds: () => j.current,
          onPositions: (T) => S.current.onPositions(T),
          onCreateEdge: (T, W) => S.current.onCreateEdge(T, W)
        })
      });
      f.on("node:click", (T) => {
        const W = Ze(T);
        W && z.current === "select" && S.current.onSelect(W);
      }), f.on("edge:click", () => {
        S.current.onSelect(null);
      }), f.on("canvas:click", () => S.current.onSelect(null)), g.current = f, I.current = !0, f.render();
    }), () => {
      U = !0;
    };
  }, []), oe(() => {
    const m = g.current;
    !m || !I.current || (m.setData(A), m.draw());
  }, [A]), oe(() => () => {
    var m;
    (m = g.current) == null || m.destroy(), g.current = null, I.current = !1;
  }, []);
  const M = (m) => {
    if (m.preventDefault(), z.current !== "select") return;
    const U = m.dataTransfer.getData("application/x-industrial-node") || m.dataTransfer.getData("text/plain");
    if (!U) return;
    const G = g.current, p = y.current;
    if (!G || !p) return;
    const f = p.getBoundingClientRect(), T = G.getCanvasByViewport([
      m.clientX - f.left,
      m.clientY - f.top
    ]);
    S.current.onDropNode(U, T[0], T[1]);
  };
  return /* @__PURE__ */ a("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ w("div", { className: `industrial-graph industrial-graph--${s}`, children: [
    /* @__PURE__ */ a(
      "div",
      {
        ref: y,
        className: "industrial-graph-host",
        onDragOver: (m) => m.preventDefault(),
        onDrop: M,
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ a("div", { className: "industrial-graph-hint", children: et[s] })
  ] }) });
}
function nt({ descriptors: e, loading: t, error: n }) {
  const r = /* @__PURE__ */ new Map();
  for (const l of e) {
    const i = l.category.split(".")[0] || "General", o = r.get(i) ?? [];
    o.push(l), r.set(i, o);
  }
  const s = (l, i) => {
    l.dataTransfer.setData("application/x-industrial-node", i.nodeTypeId), l.dataTransfer.setData("text/plain", i.nodeTypeId), l.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ w("aside", { className: "industrial-palette", children: [
    /* @__PURE__ */ a(_.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ a(_.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ a(Ce, { size: "small", style: { display: "block", marginTop: 16 } }),
    n && /* @__PURE__ */ a(_.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: n }),
    !t && !n && e.length === 0 && /* @__PURE__ */ a(Ee, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(r.entries()).map(([l, i]) => /* @__PURE__ */ w("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ a(_.Text, { strong: !0, style: { fontSize: 12 }, children: l }),
      i.map((o) => /* @__PURE__ */ w(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          onDragStart: (u) => s(u, o),
          title: o.description || o.nodeTypeId,
          children: [
            /* @__PURE__ */ a("div", { className: "industrial-palette-node-title", children: o.name }),
            /* @__PURE__ */ w("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ a(X, { bordered: !1, color: "blue", children: o.version }),
              /* @__PURE__ */ a(_.Text, { type: "secondary", style: { fontSize: 11 }, children: o.nodeTypeId })
            ] }),
            /* @__PURE__ */ w("div", { className: "industrial-palette-node-ports", children: [
              o.inputs.map((u) => /* @__PURE__ */ w(X, { bordered: !1, color: "default", children: [
                "in ",
                u.name,
                ": ",
                u.type
              ] }, `in-${u.name}`)),
              o.outputs.map((u) => /* @__PURE__ */ w(X, { bordered: !1, color: "green", children: [
                "out ",
                u.name,
                ": ",
                u.type
              ] }, `out-${u.name}`))
            ] })
          ]
        },
        o.nodeTypeId
      ))
    ] }, l))
  ] });
}
function rt({ name: e, schema: t, required: n }) {
  let r;
  return t.enum && t.enum.length > 0 ? r = /* @__PURE__ */ a(
    se,
    {
      allowClear: !0,
      options: t.enum.map((s) => ({ label: String(s), value: s }))
    }
  ) : t.type === "number" || t.type === "integer" ? r = /* @__PURE__ */ a(Be, { style: { width: "100%" } }) : t.type === "boolean" ? r = /* @__PURE__ */ a(qe, {}) : r = /* @__PURE__ */ a($e, { placeholder: t.description }), /* @__PURE__ */ a(
    de.Item,
    {
      name: e,
      label: t.title ?? e,
      tooltip: t.description,
      rules: n ? [{ required: !0, message: `请填写 ${t.title ?? e}` }] : [],
      children: r
    },
    e
  );
}
function ot(e) {
  const {
    node: t,
    descriptor: n,
    edges: r,
    catalog: s,
    issues: l,
    onApplyConfig: i,
    onDeleteNode: o,
    onDeleteEdge: u,
    onUpdateEdge: y,
    onSelectNode: g
  } = e, [I] = de.useForm(), [z, S] = $("{}"), [A, x] = $(null);
  oe(() => {
    I.setFieldsValue((t == null ? void 0 : t.config) ?? {}), S(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), x(null);
  }, [I, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const j = new Set((n == null ? void 0 : n.configurationSchema.required) ?? []), M = Object.entries((n == null ? void 0 : n.configurationSchema.properties) ?? {}), m = () => {
    if (M.length > 0) {
      I.validateFields().then((p) => i(p));
      return;
    }
    try {
      const p = JSON.parse(z);
      if (p === null || typeof p != "object" || Array.isArray(p))
        throw new Error("配置必须是 JSON 对象");
      x(null), i(p);
    } catch (p) {
      x(p.message);
    }
  }, U = r.map((p, f) => ({
    ...p,
    index: f,
    key: `${f}:${p.source}->${p.target}`
  })), G = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (p, f) => /* @__PURE__ */ w(_.Text, { style: { fontSize: 11 }, children: [
        f.source,
        " → ",
        f.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (p, f) => {
        const T = s.get(f.source), W = s.get(f.target);
        return /* @__PURE__ */ w("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ a(
            se,
            {
              size: "small",
              value: f.sourcePort,
              style: { width: 110 },
              onChange: (N) => y(f.index, { ...f, sourcePort: N }),
              options: ((T == null ? void 0 : T.outputs) ?? []).map((N) => ({ label: `${N.name} (${N.type})`, value: N.name }))
            }
          ),
          /* @__PURE__ */ a(
            se,
            {
              size: "small",
              value: f.targetPort,
              style: { width: 110 },
              onChange: (N) => y(f.index, { ...f, targetPort: N }),
              options: ((W == null ? void 0 : W.inputs) ?? []).map((N) => ({ label: `${N.name} (${N.type})`, value: N.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (p, f) => /* @__PURE__ */ a(V, { danger: !0, size: "small", type: "text", onClick: () => u(f.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ w("aside", { className: "industrial-properties", children: [
    /* @__PURE__ */ a(_.Title, { level: 5, style: { marginTop: 0 }, children: "节点属性" }),
    !t && /* @__PURE__ */ a(Ee, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ w(ue, { children: [
      /* @__PURE__ */ a(_.Text, { strong: !0, children: (n == null ? void 0 : n.name) ?? t.type }),
      /* @__PURE__ */ a(X, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ a(de, { form: I, layout: "vertical", size: "small", style: { marginTop: 12 }, children: M.map(([p, f]) => /* @__PURE__ */ a(rt, { name: p, schema: f, required: j.has(p) }, p)) }),
      M.length === 0 && /* @__PURE__ */ w(ue, { children: [
        /* @__PURE__ */ a(_.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ a(
          $e.TextArea,
          {
            rows: 8,
            value: z,
            style: { marginTop: 6 },
            onChange: (p) => S(p.target.value)
          }
        ),
        A && /* @__PURE__ */ a(_.Text, { type: "danger", style: { fontSize: 12 }, children: A })
      ] }),
      /* @__PURE__ */ a(V, { type: "primary", block: !0, onClick: m, children: "应用配置" }),
      /* @__PURE__ */ a(V, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: o, children: "删除节点" }),
      (n == null ? void 0 : n.activityType) && /* @__PURE__ */ a(_.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: n.activityType })
    ] }),
    /* @__PURE__ */ w(xe, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      r.length,
      ")"
    ] }),
    r.length === 0 ? /* @__PURE__ */ a(_.Text, { type: "secondary", style: { fontSize: 12 }, children: "切换到“连线”模式，从节点拖到另一个节点；也可以在这里编辑端口。" }) : /* @__PURE__ */ a(Ue, { size: "small", columns: G, dataSource: U, pagination: !1 }),
    l.length > 0 && /* @__PURE__ */ w(ue, { children: [
      /* @__PURE__ */ w(xe, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        l.length,
        ")"
      ] }),
      l.map((p, f) => /* @__PURE__ */ w("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ a(X, { color: "red", children: p.code }),
        /* @__PURE__ */ w(
          _.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const T = /^nodes\.([^.]+)/.exec(p.path);
              T && g(T[1]);
            },
            children: [
              p.path,
              ": ",
              p.message
            ]
          }
        )
      ] }, `${p.path}-${f}`))
    ] })
  ] });
}
const st = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class it extends Error {
  constructor(t) {
    super(t), this.name = "DtoError";
  }
}
const F = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, Z = (e) => Array.isArray(e) ? e : [], v = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", pe = (e, t = 0) => {
  const n = typeof e == "string" ? Number(e) : e;
  return typeof n == "number" && Number.isFinite(n) ? n : t;
}, d = (e, ...t) => {
  for (const n of t)
    if (e[n] !== void 0 && e[n] !== null) return e[n];
};
function ie(e) {
  const t = F(e);
  if (Object.prototype.hasOwnProperty.call(t, "data")) return t.data;
  if (typeof t.code == "string" && typeof t.message == "string")
    throw new it(`${t.code}: ${t.message}`);
  return e;
}
function at(e) {
  const t = v(e).trim().toLowerCase();
  return st.find((r) => r.toLowerCase() === t) ?? "Unknown";
}
function lt(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const t = F(e), n = v(d(t, "name", "port", "id")).trim();
  return n ? {
    name: n,
    type: at(d(t, "type", "portType", "dataType")),
    required: d(t, "required", "isRequired") === !0
  } : null;
}
function ke(e) {
  const t = Z(e).map(lt).filter((r) => r !== null), n = /* @__PURE__ */ new Set();
  return t.filter((r) => n.has(r.name) ? !1 : (n.add(r.name), !0));
}
const ct = (e) => {
  const t = F(e);
  return Object.keys(t).length === 0 ? { type: "object", properties: {} } : t;
};
function ut(e) {
  const t = F(e), n = v(d(t, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return n ? {
    nodeTypeId: n,
    name: v(d(t, "name", "title"), n),
    description: v(d(t, "description", "desc")),
    category: v(d(t, "category", "group"), "General"),
    icon: v(d(t, "icon"), "AppstoreOutlined"),
    version: v(d(t, "version"), "1.0.0"),
    inputs: ke(d(t, "inputs", "inputPorts", "input_ports")),
    outputs: ke(d(t, "outputs", "outputPorts", "output_ports")),
    configurationSchema: ct(d(t, "configurationSchema", "configSchema", "schema")),
    activityType: v(d(t, "activityType", "activity_type")),
    dependencies: Z(d(t, "dependencies")).map((r) => v(r)).filter(Boolean)
  } : null;
}
function dt(e) {
  const t = ie(e), n = Array.isArray(t) ? t : Z(d(F(t), "nodes", "items")), r = /* @__PURE__ */ new Set();
  return n.map(ut).filter((s) => s !== null).filter((s) => r.has(s.nodeTypeId) ? !1 : (r.add(s.nodeTypeId), !0));
}
function De(e) {
  const t = F(e), n = v(d(t, "workflowId", "workflow_id", "id")).trim();
  return n ? {
    workflowId: n,
    name: v(d(t, "name", "title"), n),
    version: pe(d(t, "version"), 1)
  } : null;
}
function pt(e) {
  const t = ie(e);
  return (Array.isArray(t) ? t : Z(d(F(t), "workflows", "items"))).map(De).filter((r) => r !== null);
}
function fe(e) {
  const t = F(e), n = {};
  for (const [r, s] of Object.entries(t))
    s !== void 0 && (n[r] = s);
  return n;
}
function ft(e) {
  const t = F(e), n = v(d(t, "id", "nodeId", "node_id")).trim();
  if (!n) return null;
  const r = F(d(t, "position", "layout"));
  return {
    id: n,
    type: v(d(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: fe(d(t, "config", "configuration")),
    x: pe(d(t, "x") ?? d(r, "x"), 0),
    y: pe(d(t, "y") ?? d(r, "y"), 0)
  };
}
function gt(e) {
  const t = F(e), n = v(d(t, "source", "from", "sourceId")).trim(), r = v(d(t, "target", "to", "targetId")).trim();
  return !n || !r ? null : {
    source: n,
    sourcePort: v(d(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: r,
    targetPort: v(d(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function yt(e) {
  const t = ie(e), n = F(t), r = De(n) ?? { workflowId: "", name: "", version: 1 }, s = Z(d(n, "nodes")).map(ft).filter((u) => u !== null), l = Z(d(n, "edges")).map(gt).filter((u) => u !== null), i = F(d(n, "trigger")), o = {
    kind: v(d(i, "kind", "type"), "manual"),
    cron: d(i, "cron") === void 0 ? null : v(i.cron),
    eventName: d(i, "eventName", "event_name") === void 0 ? null : v(d(i, "eventName", "event_name")),
    config: fe(d(i, "config"))
  };
  return {
    workflowId: r.workflowId,
    name: r.name,
    version: r.version,
    nodes: s,
    edges: l,
    trigger: o,
    resourceReferences: Z(d(n, "resourceReferences", "resource_references")).map((u) => v(u)).filter(Boolean),
    layout: fe(d(n, "layout"))
  };
}
const Ie = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {}
});
function mt(e) {
  const t = {};
  for (const [n, r] of Object.entries(e.configurationSchema.properties ?? {}))
    r.default !== void 0 && (t[n] = r.default);
  return t;
}
function ht(e, t, n = 0, r = 0) {
  return { id: t, type: e.nodeTypeId, config: mt(e), x: n, y: r };
}
function K(e, t) {
  return e.get(t);
}
function le(e, t, n) {
  return e[n].find((r) => r.name === t);
}
function ge(e, t) {
  return e !== "Unknown" && t !== "Unknown" && e === t;
}
function Se(e, t) {
  for (const n of e.outputs)
    for (const r of t.inputs)
      if (ge(n.type, r.type))
        return { sourcePort: n.name, targetPort: r.name };
  return null;
}
const C = (e, t, n) => ({ path: e, code: t, message: n });
function wt(e, t) {
  return e.source === t.source && e.sourcePort === t.sourcePort && e.target === t.target && e.targetPort === t.targetPort;
}
function xt(e, t, n) {
  const r = `edges.${e.edges.length}`, s = e.nodes.find((g) => g.id === t.source), l = e.nodes.find((g) => g.id === t.target);
  if (!s || !l)
    return { definition: e, issue: C(r, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((g) => wt(g, t)))
    return { definition: e };
  const i = K(n, s.type), o = K(n, l.type);
  if (!i || !o)
    return { definition: e, issue: C(r, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const u = le(i, t.sourcePort, "outputs"), y = le(o, t.targetPort, "inputs");
  return !u || !y ? { definition: e, issue: C(r, "PORT_MISSING", "declared port does not exist") } : ge(u.type, y.type) ? { definition: { ...e, edges: [...e.edges, t] } } : {
    definition: e,
    issue: C(
      r,
      "PORT_TYPE_INCOMPATIBLE",
      `${u.type} → ${y.type} is not a legal connection`
    )
  };
}
function bt(e, t) {
  return {
    ...e,
    nodes: e.nodes.filter((n) => n.id !== t),
    edges: e.edges.filter((n) => n.source !== t && n.target !== t)
  };
}
function vt(e, t) {
  return { ...e, edges: e.edges.filter((n, r) => r !== t) };
}
function Tt(e, t, n) {
  return {
    ...e,
    nodes: e.nodes.map((r) => r.id === t ? { ...r, config: n } : r)
  };
}
function kt(e, t) {
  return {
    ...e,
    nodes: e.nodes.map((n) => {
      const r = t[n.id];
      return r ? { ...n, x: r.x, y: r.y } : n;
    }),
    // One canonical layout field: the domain ships positions with the
    // definition, never as a separate browser-only cache.
    layout: { ...e.layout, positions: { ...t } }
  };
}
function It(e) {
  var l;
  const t = /* @__PURE__ */ new Map();
  for (const i of e.nodes) t.set(i.id, []);
  for (const i of e.edges)
    (l = t.get(i.source)) == null || l.push(i.target);
  const n = /* @__PURE__ */ new Map(), r = [], s = (i) => {
    n.set(i, 1), r.push(i);
    for (const o of t.get(i) ?? []) {
      if (n.get(o) === 1)
        return [...r.slice(r.indexOf(o)), o];
      if (n.get(o) !== 2) {
        const u = s(o);
        if (u) return u;
      }
    }
    return r.pop(), n.set(i, 2), null;
  };
  for (const i of e.nodes)
    if (n.get(i.id) !== 2) {
      const o = s(i.id);
      if (o) return o;
    }
  return null;
}
function St(e, t, n) {
  const r = [];
  e.workflowId.trim() || r.push(C("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && r.push(C("workflow", "INVALID_VERSION", "version must be positive"));
  const s = /* @__PURE__ */ new Map();
  for (const o of e.nodes)
    s.has(o.id) && r.push(C(`nodes.${o.id}`, "DUPLICATE_NODE_ID", `duplicate node '${o.id}'`)), s.set(o.id, o);
  const l = /* @__PURE__ */ new Map();
  for (const o of e.nodes) {
    const u = K(t, o.type);
    if (!u) {
      r.push(C(
        `nodes.${o.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${o.type}' is not registered`
      ));
      continue;
    }
    l.set(o.id, u);
    const y = u.configurationSchema.required ?? [];
    for (const g of y) {
      const I = o.config[g];
      (I == null || I === "") && r.push(C(
        `nodes.${o.id}.config.${g}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${g}' is missing`
      ));
    }
    n && typeof o.config.resourceId == "string" && (n.has(o.config.resourceId) || r.push(C(
      `nodes.${o.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${o.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((o, u) => {
    const y = s.get(o.source), g = s.get(o.target);
    if (!y || !g) {
      r.push(C(`edges.${u}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const I = l.get(o.source), z = l.get(o.target);
    if (!I || !z) return;
    const S = le(I, o.sourcePort, "outputs"), A = le(z, o.targetPort, "inputs");
    if (!S || !A) {
      r.push(C(`edges.${u}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    ge(S.type, A.type) || r.push(C(
      `edges.${u}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${S.type} → ${A.type} is not a legal connection`
    ));
  });
  for (const o of e.nodes) {
    const u = l.get(o.id);
    if (u)
      for (const y of u.inputs.filter((g) => g.required))
        e.edges.some((g) => g.target === o.id && g.targetPort === y.name && s.has(g.source)) || r.push(C(
          `nodes.${o.id}.inputs.${y.name}`,
          "MISSING_INPUT",
          `required input '${y.name}' is not connected`
        ));
  }
  const i = It(e);
  return i && r.push(C(`nodes.${i[0]}`, "GRAPH_CYCLE", `cycle: ${i.join(" → ")}`)), r;
}
const { Header: Nt, Content: Pt } = We, Et = {
  select: "选择",
  connect: "连线",
  pan: "平移"
};
function $t({ ctx: e }) {
  var he;
  const [t, n] = Ye.useMessage(), [r, s] = $([]), [l, i] = $([]), [o, u] = $([]), [y, g] = $(""), [I, z] = $(!0), [S, A] = $([]), [x, j] = $(() => Ie()), [M, m] = $(null), [U, G] = $([]), [p, f] = $(!1), [T, W] = $("select"), [N, Y] = $(!1), [H, Oe] = $(null), ye = Q(1);
  oe(() => {
    let c = !0;
    return Promise.allSettled([
      e.hubQuery("industrial/nodes"),
      e.hubQuery("industrial/workflows"),
      e.hubQuery("industrial/resources")
    ]).then(([h, E, k]) => {
      if (!c) return;
      const P = [];
      if (h.status === "fulfilled" ? s(dt(h.value)) : P.push(`节点描述: ${String(h.reason)}`), E.status === "fulfilled") {
        const D = pt(E.value);
        i(D), D.length > 0 && me(D[0].workflowId);
      } else P.push(`工作流: ${String(E.reason)}`);
      if (k.status === "fulfilled") {
        const B = (ie(k.value) ?? []).map((O) => String((O == null ? void 0 : O.id) ?? "")).filter(Boolean);
        u(B), g((O) => O || B[0] || "");
      } else P.push(`资源: ${String(k.reason)}`);
      A(P);
    }).finally(() => {
      c && z(!1);
    }), () => {
      c = !1;
    };
  }, [e]);
  const L = Pe(
    () => new Map(r.map((c) => [c.nodeTypeId, c])),
    [r]
  ), ce = x.nodes.find((c) => c.id === M) ?? null, ee = q(async (c, h, E) => {
    f(!0);
    try {
      return await R().api.hubCommand(c, h), t.success(E), !0;
    } catch (k) {
      return t.error(`${c} failed: ${k instanceof Error ? k.message : String(k)}`), !1;
    } finally {
      f(!1);
    }
  }, [t]), me = q(async (c) => {
    try {
      const h = await e.hubQuery("industrial/workflow-definitions"), k = (ie(h) ?? []).find((B) => {
        const O = B;
        return String((O == null ? void 0 : O.workflowId) ?? "") === c;
      }), P = k === void 0 ? { ...Ie(c), name: c } : yt(k);
      j(P);
      const D = P.nodes.map((B) => /^n(\d+)$/.exec(B.id)).filter(Boolean).reduce((B, O) => Math.max(B, Number(O[1])), 0);
      ye.current = D + 1, m(null), G([]), Y(!1);
    } catch (h) {
      t.error(`加载工作流失败: ${h instanceof Error ? h.message : String(h)}`);
    }
  }, [e, t]), ae = q(async () => {
    try {
      const c = await e.hubQuery("industrial/execution");
      if (c == null) return;
      const h = c;
      Oe({ status: String(h.status ?? ""), error: h.error, executed: h.executed ?? [] });
    } catch {
    }
  }, [e]), Re = q(async () => {
    await ee("industrial/save-workflow", x, "已保存") && Y(!1);
  }, [ee, x]), Ae = q(async () => {
    if (N) {
      t.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await ee("industrial/publish-workflow", { workflowId: x.workflowId }, "已发布") && await ae();
  }, [ee, x.workflowId, N, t, ae]), je = q(async () => {
    if (N) {
      t.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await ee(
      "industrial/run-workflow",
      { workflowId: x.workflowId, resourceId: y },
      "采集已启动"
    ) && await ae();
  }, [ee, x.workflowId, N, t, ae, y]), Fe = q((c, h, E) => {
    const k = K(L, c);
    if (!k) return;
    const P = `n${ye.current++}`;
    j((D) => ({
      ...D,
      nodes: [...D.nodes, ht(k, P, h, E)]
    })), m(P), G([]), Y(!0);
  }, [L]), ze = q((c, h) => {
    const E = x.nodes.find((J) => J.id === c), k = x.nodes.find((J) => J.id === h);
    if (!E || !k)
      return t.warning("连线失败：端点不在画布上"), null;
    const P = K(L, E.type), D = K(L, k.type);
    if (!P || !D) return null;
    let B = E, O = k, ne = Se(P, D);
    if (!ne) {
      const J = Se(D, P);
      if (!J)
        return t.warning(`端口类型不兼容：${P.name} → ${D.name}`), null;
      B = k, O = E, ne = J;
    }
    const te = xt(x, {
      source: B.id,
      sourcePort: ne.sourcePort,
      target: O.id,
      targetPort: ne.targetPort
    }, L);
    return te.issue ? (t.warning(`${te.issue.code}: ${te.issue.message}`), G((J) => [...J, te.issue]), null) : te.definition === x ? (t.info("这条连线已经存在"), null) : (j(te.definition), Y(!0), ne);
  }, [L, x, t]), Me = q(() => {
    const c = St(x, L, new Set(o));
    G(c), c.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : t.warning(`发现 ${c.length} 个问题，见右侧定位列表`);
  }, [x, L, o, t]), Ge = q((c, h) => {
    j((E) => ({
      ...E,
      edges: E.edges.map((k, P) => P === c ? h : k)
    })), Y(!0);
  }, []);
  return I ? /* @__PURE__ */ a(Ce, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ w("div", { className: "industrial-designer", children: [
    n,
    /* @__PURE__ */ w(Nt, { className: "industrial-header", children: [
      /* @__PURE__ */ w("div", { className: "industrial-title", children: [
        /* @__PURE__ */ a(_.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ a(_.Text, { type: "secondary", children: x.name || x.workflowId }),
        N && /* @__PURE__ */ a(X, { color: "orange", children: "未保存" }),
        H && /* @__PURE__ */ a(X, { color: H.error ? "error" : "success", children: H.status })
      ] }),
      /* @__PURE__ */ w("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ a(be, { size: 4, children: ["select", "connect", "pan"].map((c) => /* @__PURE__ */ a(
          V,
          {
            type: T === c ? "primary" : "default",
            onClick: () => W(c),
            children: Et[c]
          },
          c
        )) }),
        /* @__PURE__ */ a("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ w(be, { size: 4, children: [
          /* @__PURE__ */ a(V, { loading: p, onClick: () => void Re(), children: "保存" }),
          /* @__PURE__ */ a(V, { onClick: Me, children: "校验" }),
          /* @__PURE__ */ a(V, { loading: p, onClick: () => void Ae(), children: "发布" }),
          /* @__PURE__ */ a(V, { type: "primary", loading: p, onClick: () => void je(), children: "运行" })
        ] }),
        /* @__PURE__ */ a(
          se,
          {
            className: "industrial-resource-select",
            value: y,
            onChange: g,
            placeholder: "运行资源",
            options: o.map((c) => ({ value: c, label: c }))
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ w(Pt, { className: "industrial-body", children: [
      /* @__PURE__ */ w("aside", { className: "industrial-palette", children: [
        l.length > 0 && /* @__PURE__ */ a(
          se,
          {
            className: "industrial-workflow-select",
            value: x.workflowId,
            placeholder: "选择工作流",
            onChange: (c) => void me(c),
            options: l.map((c) => ({ value: c.workflowId, label: c.name }))
          }
        ),
        /* @__PURE__ */ a(nt, { descriptors: r, loading: !1 })
      ] }),
      /* @__PURE__ */ w("section", { className: "industrial-center", children: [
        S.length > 0 && /* @__PURE__ */ a(
          we,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 10 },
            message: "部分设计器数据未加载",
            description: S.join("；")
          }
        ),
        /* @__PURE__ */ a(
          tt,
          {
            definition: x,
            catalog: L,
            selectedId: M,
            mode: T,
            onSelect: m,
            onDropNode: Fe,
            onCreateEdge: ze,
            onPositions: (c) => {
              j((h) => kt(h, c)), Y(!0);
            }
          }
        )
      ] }),
      /* @__PURE__ */ w("aside", { className: "industrial-properties", children: [
        H && /* @__PURE__ */ a(
          we,
          {
            style: { marginBottom: 12 },
            type: H.error ? "error" : "success",
            showIcon: !0,
            message: `执行状态: ${H.status}`,
            description: H.error ?? ((he = H.executed) != null && he.length ? `已执行节点: ${H.executed.join(", ")}` : void 0)
          }
        ),
        /* @__PURE__ */ a(
          ot,
          {
            node: ce,
            descriptor: ce ? K(L, ce.type) ?? null : null,
            edges: x.edges,
            catalog: L,
            issues: U,
            onApplyConfig: (c) => {
              M && (j((h) => Tt(h, M, c)), Y(!0));
            },
            onDeleteNode: () => {
              j((c) => bt(c, M ?? "")), m(null), Y(!0);
            },
            onDeleteEdge: (c) => {
              j((h) => vt(h, c)), Y(!0);
            },
            onUpdateEdge: Ge,
            onSelectNode: m
          }
        )
      ] })
    ] })
  ] });
}
const Ct = `
.industrial-designer{display:flex;flex-direction:column;height:min(86vh,900px);min-height:620px;background:#f4f7fb;border:1px solid #e6ebf2;border-radius:12px;overflow:hidden;box-shadow:0 8px 28px rgba(16,37,66,.08)}
.industrial-header{height:auto;min-height:58px;padding:10px 16px;background:#102542;display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap;line-height:normal}
.industrial-title{display:flex;align-items:center;gap:8px;min-width:0;color:#fff}
.industrial-title .ant-typography{color:#fff!important}
.industrial-title .ant-typography-secondary{color:#9db2d0!important}
.industrial-toolbar{display:flex;align-items:center;gap:10px;flex-wrap:wrap}
.industrial-toolbar-divider{width:1px;height:22px;background:rgba(255,255,255,.24)}
.industrial-resource-select{width:180px}
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
@media(max-width:1100px){.industrial-body{grid-template-columns:220px minmax(0,1fr) 280px}.industrial-resource-select{width:140px}}
`;
function _t(e) {
  Le(e);
  const t = typeof document > "u" ? null : document.createElement("style");
  t && (t.dataset.industrialDesigner = "true", t.textContent = Ct, document.head.appendChild(t));
  const n = ({ ctx: s }) => Ne($t, { ctx: s });
  n.displayName = "IndustrialWorkflowCanvas";
  const r = e.blocks.register("industrial-workflow-canvas", n);
  return () => {
    r == null || r(), t == null || t.remove();
  };
}
export {
  _t as default,
  _t as register
};
