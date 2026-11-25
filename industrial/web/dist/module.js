let te = null;
function ze(e) {
  te = e;
}
function j() {
  if (!te && typeof globalThis < "u" && (te = globalThis.__CORDIS_CONSOLE ?? null), !te)
    throw new Error("[industrial-designer] console facade is not available");
  return te;
}
const i = (...e) => j().jsxRuntime.jsx(...e), T = (...e) => j().jsxRuntime.jsxs(...e), ce = (e) => j().jsxRuntime.jsx(j().jsxRuntime.Fragment, e), Se = (...e) => j().React.createElement(...e), q = (...e) => j().React.useCallback(...e), ne = (...e) => j().React.useEffect(...e), ke = (...e) => j().React.useMemo(...e), H = (...e) => j().React.useRef(...e), C = (...e) => j().React.useState(...e), v = (e) => {
  const t = (r) => {
    const n = e.split(".").reduce((o, a) => o == null ? void 0 : o[a], j().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Se(n, r ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, me = v("Alert"), J = v("Button"), he = v("Divider"), Ne = v("Empty"), le = Object.assign(v("Form"), {
  Item: v("Form.Item"),
  useForm: (...e) => j().antd.Form.useForm(...e)
}), Pe = Object.assign(v("Input"), {
  Number: v("InputNumber"),
  TextArea: v("Input.TextArea")
}), Le = v("InputNumber"), xe = Object.assign(v("Layout"), {
  Header: v("Layout.Header"),
  Content: v("Layout.Content")
});
v("Modal");
const re = v("Select"), Ge = v("Space"), $e = v("Spin"), Be = v("Switch"), We = v("Table"), V = v("Tag"), A = Object.assign(v("Typography"), {
  Title: v("Typography.Title"),
  Text: v("Typography.Text")
}), qe = new Proxy({}, {
  get: (e, t) => (...r) => {
    var o;
    const n = (o = j().antd.message) == null ? void 0 : o[t];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${t} unavailable`);
    return n(...r);
  }
}), we = {
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
}, Ie = (e) => we[e] ?? we[e.split(".")[0]] ?? "#4d6bfe";
function Ue(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function Ye(e, t = /* @__PURE__ */ new Map(), r) {
  return {
    nodes: e.nodes.map((n) => {
      const o = t.get(n.type), a = (o == null ? void 0 : o.category) ?? "General", l = r === n.id;
      return {
        id: n.id,
        data: {
          label: (o == null ? void 0 : o.name) ?? n.type,
          nodeTypeId: n.type,
          category: a,
          icon: (o == null ? void 0 : o.icon) ?? "AppstoreOutlined",
          // Domain config travels with the node so selection can edit it
          // without a second lookup; it is never written back from G6.
          config: { ...n.config }
        },
        style: {
          x: n.x,
          y: n.y,
          fill: Ie(a),
          stroke: l ? "#111111" : Ie(a),
          lineWidth: l ? 3 : 1.5,
          radius: 8,
          // Typed anchors: the user can see which side accepts which port,
          // and G6 binds edges to these keys instead of node centers.
          port: !0,
          ports: [
            ...((o == null ? void 0 : o.inputs) ?? []).map((s, u, h) => ({
              key: `in:${s.name}`,
              placement: [0, (u + 1) / (h.length + 1)],
              r: 4,
              stroke: s.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((o == null ? void 0 : o.outputs) ?? []).map((s, u, h) => ({
              key: `out:${s.name}`,
              placement: [1, (u + 1) / (h.length + 1)],
              r: 4,
              stroke: "#1677ff",
              fill: "#fff"
            }))
          ]
        }
      };
    }),
    edges: e.edges.map((n) => ({
      id: Ue(n),
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
function He(e, t) {
  const r = {};
  for (const n of e.nodes) {
    if (!t.has(n.id)) continue;
    const o = n.style.x, a = n.style.y;
    typeof o == "number" && typeof a == "number" && (r[n.id] = { x: o, y: a });
  }
  return r;
}
function Je(e, t) {
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
        labelText: (r) => {
          var n;
          return ((n = r == null ? void 0 : r.data) == null ? void 0 : n.label) ?? "";
        },
        labelFontSize: 10,
        stroke: "#8a93a6",
        endArrow: !0
      }
    },
    behaviors: Ee()
  };
}
function Ee(e = {}) {
  return [
    "drag-canvas",
    "zoom-canvas",
    {
      type: "drag-element",
      enable: () => {
        var t;
        return !(((t = e.connectMode) == null ? void 0 : t.call(e)) ?? !1);
      },
      onFinish: () => {
        var n, o;
        const t = e.graph;
        if (!t) return;
        const r = t.getNodeData().map((a) => ({
          id: String(a.id),
          data: a.data ?? {},
          style: a.style ?? {}
        }));
        (o = e.onPositions) == null || o.call(e, He(
          { nodes: r },
          ((n = e.allowedIds) == null ? void 0 : n.call(e)) ?? /* @__PURE__ */ new Set()
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      enable: () => {
        var t;
        return ((t = e.connectMode) == null ? void 0 : t.call(e)) ?? !1;
      },
      onCreate: (t) => {
        var a;
        const r = String(t.source ?? ""), n = String(t.target ?? "");
        if (!r || !n || r === n) return;
        const o = (a = e.onCreateEdge) == null ? void 0 : a.call(e, r, n);
        if (o)
          return {
            ...t,
            sourcePort: `out:${o.sourcePort}`,
            targetPort: `in:${o.targetPort}`
          };
      }
    }
  ];
}
const Qe = (e) => {
  var r;
  const t = e;
  return (t == null ? void 0 : t.targetType) === "node" ? (r = t.target) == null ? void 0 : r.id : void 0;
};
function Ve(e) {
  const {
    definition: t,
    catalog: r,
    selectedId: n,
    connectMode: o,
    onSelect: a,
    onDropNode: l,
    onCreateEdge: s,
    onPositions: u
  } = e, h = H(null), g = H(null), k = H(!1), L = H(o);
  L.current = o;
  const N = H({ onSelect: a, onDropNode: l, onCreateEdge: s, onPositions: u });
  N.current = { onSelect: a, onDropNode: l, onCreateEdge: s, onPositions: u };
  const F = ke(
    () => Ye(t, r, n),
    [t, r, n]
  ), I = H(F);
  I.current = F;
  const O = H(/* @__PURE__ */ new Set());
  return O.current = new Set(t.nodes.map((w) => w.id)), ne(() => {
    const w = h.current;
    if (!w || k.current) return;
    let U = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: G }) => {
      if (U || !h.current || k.current) return;
      const f = Je(I.current, w), p = new G({
        ...f,
        behaviors: Ee({
          graph: {
            getNodeData: () => {
              var y;
              return ((y = g.current) == null ? void 0 : y.getNodeData()) ?? [];
            }
          },
          allowedIds: () => O.current,
          connectMode: () => L.current,
          onSelect: (y) => N.current.onSelect(y),
          onDropNode: (y, $, b) => N.current.onDropNode(y, $, b),
          onCreateEdge: (y, $) => N.current.onCreateEdge(y, $),
          onPositions: (y) => N.current.onPositions(y)
        })
      });
      p.on("node:click", (y) => {
        const $ = Qe(y);
        $ && !L.current && N.current.onSelect($);
      }), p.on("edge:click", (y) => {
        var b;
        const $ = y;
        (b = $ == null ? void 0 : $.target) != null && b.id && N.current.onSelect($.target.id);
      }), p.on("canvas:click", () => N.current.onSelect(null)), g.current = p, k.current = !0, p.render();
    }), () => {
      U = !0;
    };
  }, []), ne(() => {
    const w = g.current;
    !w || !k.current || (w.setData(F), w.draw());
  }, [F]), ne(() => () => {
    var w;
    (w = g.current) == null || w.destroy(), g.current = null, k.current = !1;
  }, []), /* @__PURE__ */ T("div", { style: { position: "relative", height: "100%", minHeight: 360 }, children: [
    /* @__PURE__ */ i(
      "div",
      {
        ref: h,
        className: "industrial-graph",
        onDragOver: (w) => w.preventDefault(),
        onDrop: (w) => {
          w.preventDefault();
          const U = w.dataTransfer.getData("application/x-industrial-node") || w.dataTransfer.getData("text/plain");
          if (!U) return;
          const G = g.current, f = h.current;
          if (!G || !f) return;
          const p = f.getBoundingClientRect(), y = G.getCanvasByViewport([
            w.clientX - p.left,
            w.clientY - p.top
          ]);
          N.current.onDropNode(U, y[0], y[1]);
        },
        "data-testid": "industrial-graph",
        style: {
          height: "100%",
          minHeight: 360,
          border: o ? "1px dashed #1677ff" : "1px solid transparent",
          cursor: o ? "crosshair" : "default"
        }
      }
    ),
    o && /* @__PURE__ */ i("div", { style: { position: "absolute", left: 8, top: 8, pointerEvents: "none" }, children: /* @__PURE__ */ i("span", { style: { background: "#1677ff", color: "#fff", borderRadius: 4, padding: "2px 8px", fontSize: 12 }, children: "连线模式：从一个节点拖到另一个节点" }) })
  ] });
}
function Ke({ descriptors: e, loading: t, error: r }) {
  const n = /* @__PURE__ */ new Map();
  for (const a of e) {
    const l = a.category.split(".")[0] || "General", s = n.get(l) ?? [];
    s.push(a), n.set(l, s);
  }
  const o = (a, l) => {
    a.dataTransfer.setData("application/x-industrial-node", l.nodeTypeId), a.dataTransfer.setData("text/plain", l.nodeTypeId), a.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ T("aside", { className: "industrial-palette", children: [
    /* @__PURE__ */ i(A.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ i(A.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ i($e, { size: "small", style: { display: "block", marginTop: 16 } }),
    r && /* @__PURE__ */ i(A.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: r }),
    !t && !r && e.length === 0 && /* @__PURE__ */ i(Ne, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(n.entries()).map(([a, l]) => /* @__PURE__ */ T("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ i(A.Text, { strong: !0, style: { fontSize: 12 }, children: a }),
      l.map((s) => /* @__PURE__ */ T(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          onDragStart: (u) => o(u, s),
          title: s.description || s.nodeTypeId,
          children: [
            /* @__PURE__ */ i("div", { className: "industrial-palette-node-title", children: s.name }),
            /* @__PURE__ */ T("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ i(V, { bordered: !1, color: "blue", children: s.version }),
              /* @__PURE__ */ i(A.Text, { type: "secondary", style: { fontSize: 11 }, children: s.nodeTypeId })
            ] }),
            /* @__PURE__ */ T("div", { className: "industrial-palette-node-ports", children: [
              s.inputs.map((u) => /* @__PURE__ */ T(V, { bordered: !1, color: "default", children: [
                "in ",
                u.name,
                ": ",
                u.type
              ] }, `in-${u.name}`)),
              s.outputs.map((u) => /* @__PURE__ */ T(V, { bordered: !1, color: "green", children: [
                "out ",
                u.name,
                ": ",
                u.type
              ] }, `out-${u.name}`))
            ] })
          ]
        },
        s.nodeTypeId
      ))
    ] }, a))
  ] });
}
function Xe({ name: e, schema: t, required: r }) {
  let n;
  return t.enum && t.enum.length > 0 ? n = /* @__PURE__ */ i(
    re,
    {
      allowClear: !0,
      options: t.enum.map((o) => ({ label: String(o), value: o }))
    }
  ) : t.type === "number" || t.type === "integer" ? n = /* @__PURE__ */ i(Le, { style: { width: "100%" } }) : t.type === "boolean" ? n = /* @__PURE__ */ i(Be, {}) : n = /* @__PURE__ */ i(Pe, { placeholder: t.description }), /* @__PURE__ */ i(
    le.Item,
    {
      name: e,
      label: t.title ?? e,
      tooltip: t.description,
      rules: r ? [{ required: !0, message: `请填写 ${t.title ?? e}` }] : [],
      children: n
    },
    e
  );
}
function Ze(e) {
  const {
    node: t,
    descriptor: r,
    edges: n,
    catalog: o,
    issues: a,
    onApplyConfig: l,
    onDeleteNode: s,
    onDeleteEdge: u,
    onUpdateEdge: h,
    onSelectNode: g
  } = e, [k] = le.useForm(), [L, N] = C("{}"), [F, I] = C(null);
  ne(() => {
    k.setFieldsValue((t == null ? void 0 : t.config) ?? {}), N(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), I(null);
  }, [k, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const O = new Set((r == null ? void 0 : r.configurationSchema.required) ?? []), W = Object.entries((r == null ? void 0 : r.configurationSchema.properties) ?? {}), w = () => {
    if (W.length > 0) {
      k.validateFields().then((f) => l(f));
      return;
    }
    try {
      const f = JSON.parse(L);
      if (f === null || typeof f != "object" || Array.isArray(f))
        throw new Error("配置必须是 JSON 对象");
      I(null), l(f);
    } catch (f) {
      I(f.message);
    }
  }, U = n.map((f, p) => ({
    ...f,
    index: p,
    key: `${p}:${f.source}->${f.target}`
  })), G = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (f, p) => /* @__PURE__ */ T(A.Text, { style: { fontSize: 11 }, children: [
        p.source,
        " → ",
        p.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (f, p) => {
        const y = o.get(p.source), $ = o.get(p.target);
        return /* @__PURE__ */ T("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ i(
            re,
            {
              size: "small",
              value: p.sourcePort,
              style: { width: 110 },
              onChange: (b) => h(p.index, { ...p, sourcePort: b }),
              options: ((y == null ? void 0 : y.outputs) ?? []).map((b) => ({ label: `${b.name} (${b.type})`, value: b.name }))
            }
          ),
          /* @__PURE__ */ i(
            re,
            {
              size: "small",
              value: p.targetPort,
              style: { width: 110 },
              onChange: (b) => h(p.index, { ...p, targetPort: b }),
              options: (($ == null ? void 0 : $.inputs) ?? []).map((b) => ({ label: `${b.name} (${b.type})`, value: b.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (f, p) => /* @__PURE__ */ i(J, { danger: !0, size: "small", type: "text", onClick: () => u(p.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ T("aside", { className: "industrial-properties", children: [
    /* @__PURE__ */ i(A.Title, { level: 5, style: { marginTop: 0 }, children: "节点属性" }),
    !t && /* @__PURE__ */ i(Ne, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ T(ce, { children: [
      /* @__PURE__ */ i(A.Text, { strong: !0, children: (r == null ? void 0 : r.name) ?? t.type }),
      /* @__PURE__ */ i(V, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ i(le, { form: k, layout: "vertical", size: "small", style: { marginTop: 12 }, children: W.map(([f, p]) => /* @__PURE__ */ i(Xe, { name: f, schema: p, required: O.has(f) }, f)) }),
      W.length === 0 && /* @__PURE__ */ T(ce, { children: [
        /* @__PURE__ */ i(A.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ i(
          Pe.TextArea,
          {
            rows: 8,
            value: L,
            style: { marginTop: 6 },
            onChange: (f) => N(f.target.value)
          }
        ),
        F && /* @__PURE__ */ i(A.Text, { type: "danger", style: { fontSize: 12 }, children: F })
      ] }),
      /* @__PURE__ */ i(J, { type: "primary", block: !0, onClick: w, children: "应用配置" }),
      /* @__PURE__ */ i(J, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: s, children: "删除节点" }),
      (r == null ? void 0 : r.activityType) && /* @__PURE__ */ i(A.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: r.activityType })
    ] }),
    /* @__PURE__ */ T(he, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      n.length,
      ")"
    ] }),
    n.length === 0 ? /* @__PURE__ */ i(A.Text, { type: "secondary", style: { fontSize: 12 }, children: "开启“连线模式”，从节点拖到另一个节点。" }) : /* @__PURE__ */ i(We, { size: "small", columns: G, dataSource: U, pagination: !1 }),
    a.length > 0 && /* @__PURE__ */ T(ce, { children: [
      /* @__PURE__ */ T(he, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        a.length,
        ")"
      ] }),
      a.map((f, p) => /* @__PURE__ */ T("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ i(V, { color: "red", children: f.code }),
        /* @__PURE__ */ T(
          A.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const y = /^nodes\.([^.]+)/.exec(f.path);
              y && g(y[1]);
            },
            children: [
              f.path,
              ": ",
              f.message
            ]
          }
        )
      ] }, `${f.path}-${p}`))
    ] })
  ] });
}
const et = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class tt extends Error {
  constructor(t) {
    super(t), this.name = "DtoError";
  }
}
const z = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, K = (e) => Array.isArray(e) ? e : [], S = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", ue = (e, t = 0) => {
  const r = typeof e == "string" ? Number(e) : e;
  return typeof r == "number" && Number.isFinite(r) ? r : t;
}, d = (e, ...t) => {
  for (const r of t)
    if (e[r] !== void 0 && e[r] !== null) return e[r];
};
function oe(e) {
  const t = z(e);
  if (Object.prototype.hasOwnProperty.call(t, "data")) return t.data;
  if (typeof t.code == "string" && typeof t.message == "string")
    throw new tt(`${t.code}: ${t.message}`);
  return e;
}
function nt(e) {
  const t = S(e).trim().toLowerCase();
  return et.find((n) => n.toLowerCase() === t) ?? "Unknown";
}
function rt(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const t = z(e), r = S(d(t, "name", "port", "id")).trim();
  return r ? {
    name: r,
    type: nt(d(t, "type", "portType", "dataType")),
    required: d(t, "required", "isRequired") === !0
  } : null;
}
function Te(e) {
  const t = K(e).map(rt).filter((n) => n !== null), r = /* @__PURE__ */ new Set();
  return t.filter((n) => r.has(n.name) ? !1 : (r.add(n.name), !0));
}
const ot = (e) => {
  const t = z(e);
  return Object.keys(t).length === 0 ? { type: "object", properties: {} } : t;
};
function st(e) {
  const t = z(e), r = S(d(t, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return r ? {
    nodeTypeId: r,
    name: S(d(t, "name", "title"), r),
    description: S(d(t, "description", "desc")),
    category: S(d(t, "category", "group"), "General"),
    icon: S(d(t, "icon"), "AppstoreOutlined"),
    version: S(d(t, "version"), "1.0.0"),
    inputs: Te(d(t, "inputs", "inputPorts", "input_ports")),
    outputs: Te(d(t, "outputs", "outputPorts", "output_ports")),
    configurationSchema: ot(d(t, "configurationSchema", "configSchema", "schema")),
    activityType: S(d(t, "activityType", "activity_type")),
    dependencies: K(d(t, "dependencies")).map((n) => S(n)).filter(Boolean)
  } : null;
}
function it(e) {
  const t = oe(e), r = Array.isArray(t) ? t : K(d(z(t), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return r.map(st).filter((o) => o !== null).filter((o) => n.has(o.nodeTypeId) ? !1 : (n.add(o.nodeTypeId), !0));
}
function Ce(e) {
  const t = z(e), r = S(d(t, "workflowId", "workflow_id", "id")).trim();
  return r ? {
    workflowId: r,
    name: S(d(t, "name", "title"), r),
    version: ue(d(t, "version"), 1)
  } : null;
}
function at(e) {
  const t = oe(e);
  return (Array.isArray(t) ? t : K(d(z(t), "workflows", "items"))).map(Ce).filter((n) => n !== null);
}
function de(e) {
  const t = z(e), r = {};
  for (const [n, o] of Object.entries(t))
    o !== void 0 && (r[n] = o);
  return r;
}
function ct(e) {
  const t = z(e), r = S(d(t, "id", "nodeId", "node_id")).trim();
  if (!r) return null;
  const n = z(d(t, "position", "layout"));
  return {
    id: r,
    type: S(d(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: de(d(t, "config", "configuration")),
    x: ue(d(t, "x") ?? d(n, "x"), 0),
    y: ue(d(t, "y") ?? d(n, "y"), 0)
  };
}
function lt(e) {
  const t = z(e), r = S(d(t, "source", "from", "sourceId")).trim(), n = S(d(t, "target", "to", "targetId")).trim();
  return !r || !n ? null : {
    source: r,
    sourcePort: S(d(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: S(d(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function ve(e) {
  const t = oe(e), r = z(t), n = Ce(r) ?? { workflowId: "", name: "", version: 1 }, o = K(d(r, "nodes")).map(ct).filter((u) => u !== null), a = K(d(r, "edges")).map(lt).filter((u) => u !== null), l = z(d(r, "trigger")), s = {
    kind: S(d(l, "kind", "type"), "manual"),
    cron: d(l, "cron") === void 0 ? null : S(l.cron),
    eventName: d(l, "eventName", "event_name") === void 0 ? null : S(d(l, "eventName", "event_name")),
    config: de(d(l, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: o,
    edges: a,
    trigger: s,
    resourceReferences: K(d(r, "resourceReferences", "resource_references")).map((u) => S(u)).filter(Boolean),
    layout: de(d(r, "layout"))
  };
}
const be = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {}
});
function ut(e) {
  const t = {};
  for (const [r, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (t[r] = n.default);
  return t;
}
function dt(e, t, r = 0, n = 0) {
  return { id: t, type: e.nodeTypeId, config: ut(e), x: r, y: n };
}
function Q(e, t) {
  return e.get(t);
}
function ie(e, t, r) {
  return e[r].find((n) => n.name === t);
}
function fe(e, t) {
  return e !== "Unknown" && t !== "Unknown" && e === t;
}
function ft(e, t) {
  for (const r of e.outputs)
    for (const n of t.inputs)
      if (fe(r.type, n.type))
        return { sourcePort: r.name, targetPort: n.name };
  return null;
}
const D = (e, t, r) => ({ path: e, code: t, message: r });
function pt(e, t) {
  return e.source === t.source && e.sourcePort === t.sourcePort && e.target === t.target && e.targetPort === t.targetPort;
}
function gt(e, t, r) {
  const n = `edges.${e.edges.length}`, o = e.nodes.find((g) => g.id === t.source), a = e.nodes.find((g) => g.id === t.target);
  if (!o || !a)
    return { definition: e, issue: D(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((g) => pt(g, t)))
    return { definition: e };
  const l = Q(r, o.type), s = Q(r, a.type);
  if (!l || !s)
    return { definition: e, issue: D(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const u = ie(l, t.sourcePort, "outputs"), h = ie(s, t.targetPort, "inputs");
  return !u || !h ? { definition: e, issue: D(n, "PORT_MISSING", "declared port does not exist") } : fe(u.type, h.type) ? { definition: { ...e, edges: [...e.edges, t] } } : {
    definition: e,
    issue: D(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${u.type} → ${h.type} is not a legal connection`
    )
  };
}
function yt(e, t) {
  return {
    ...e,
    nodes: e.nodes.filter((r) => r.id !== t),
    edges: e.edges.filter((r) => r.source !== t && r.target !== t)
  };
}
function mt(e, t) {
  return { ...e, edges: e.edges.filter((r, n) => n !== t) };
}
function ht(e, t, r) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === t ? { ...n, config: r } : n)
  };
}
function wt(e, t) {
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
function It(e) {
  var a;
  const t = /* @__PURE__ */ new Map();
  for (const l of e.nodes) t.set(l.id, []);
  for (const l of e.edges)
    (a = t.get(l.source)) == null || a.push(l.target);
  const r = /* @__PURE__ */ new Map(), n = [], o = (l) => {
    r.set(l, 1), n.push(l);
    for (const s of t.get(l) ?? []) {
      if (r.get(s) === 1)
        return [...n.slice(n.indexOf(s)), s];
      if (r.get(s) !== 2) {
        const u = o(s);
        if (u) return u;
      }
    }
    return n.pop(), r.set(l, 2), null;
  };
  for (const l of e.nodes)
    if (r.get(l.id) !== 2) {
      const s = o(l.id);
      if (s) return s;
    }
  return null;
}
function Tt(e, t, r) {
  const n = [];
  e.workflowId.trim() || n.push(D("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(D("workflow", "INVALID_VERSION", "version must be positive"));
  const o = /* @__PURE__ */ new Map();
  for (const s of e.nodes)
    o.has(s.id) && n.push(D(`nodes.${s.id}`, "DUPLICATE_NODE_ID", `duplicate node '${s.id}'`)), o.set(s.id, s);
  const a = /* @__PURE__ */ new Map();
  for (const s of e.nodes) {
    const u = Q(t, s.type);
    if (!u) {
      n.push(D(
        `nodes.${s.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${s.type}' is not registered`
      ));
      continue;
    }
    a.set(s.id, u);
    const h = u.configurationSchema.required ?? [];
    for (const g of h) {
      const k = s.config[g];
      (k == null || k === "") && n.push(D(
        `nodes.${s.id}.config.${g}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${g}' is missing`
      ));
    }
    r && typeof s.config.resourceId == "string" && (r.has(s.config.resourceId) || n.push(D(
      `nodes.${s.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${s.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((s, u) => {
    const h = o.get(s.source), g = o.get(s.target);
    if (!h || !g) {
      n.push(D(`edges.${u}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const k = a.get(s.source), L = a.get(s.target);
    if (!k || !L) return;
    const N = ie(k, s.sourcePort, "outputs"), F = ie(L, s.targetPort, "inputs");
    if (!N || !F) {
      n.push(D(`edges.${u}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    fe(N.type, F.type) || n.push(D(
      `edges.${u}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${N.type} → ${F.type} is not a legal connection`
    ));
  });
  for (const s of e.nodes) {
    const u = a.get(s.id);
    if (u)
      for (const h of u.inputs.filter((g) => g.required))
        e.edges.some((g) => g.target === s.id && g.targetPort === h.name && o.has(g.source)) || n.push(D(
          `nodes.${s.id}.inputs.${h.name}`,
          "MISSING_INPUT",
          `required input '${h.name}' is not connected`
        ));
  }
  const l = It(e);
  return l && n.push(D(`nodes.${l[0]}`, "GRAPH_CYCLE", `cycle: ${l.join(" → ")}`)), n;
}
const { Header: vt, Content: bt } = xe;
function St({ ctx: e }) {
  var ye;
  const [t, r] = qe.useMessage(), [n, o] = C([]), [a, l] = C([]), [s, u] = C([]), [h, g] = C(""), [k, L] = C(!0), [N, F] = C([]), [I, O] = C(() => be()), [W, w] = C(null), [U, G] = C([]), [f, p] = C(!1), [y, $] = C(!1), [b, Y] = C(!1), [X, De] = C(null), pe = H(1);
  ne(() => {
    let c = !0;
    return Promise.allSettled([
      e.hubQuery("industrial/nodes"),
      e.hubQuery("industrial/workflows"),
      e.hubQuery("industrial/resources")
    ]).then(([m, R, P]) => {
      if (!c) return;
      const _ = [];
      if (m.status === "fulfilled" ? o(it(m.value)) : _.push(`节点描述: ${String(m.reason)}`), R.status === "fulfilled") {
        const M = at(R.value);
        l(M), M.length > 0 && ge(M[0].workflowId);
      } else _.push(`工作流: ${String(R.reason)}`);
      if (P.status === "fulfilled") {
        const E = (oe(P.value) ?? []).map((x) => String((x == null ? void 0 : x.id) ?? "")).filter(Boolean);
        u(E), g((x) => x || E[0] || "");
      } else _.push(`资源: ${String(P.reason)}`);
      F(_);
    }).finally(() => {
      c && L(!1);
    }), () => {
      c = !1;
    };
  }, [e]);
  const B = ke(
    () => new Map(n.map((c) => [c.nodeTypeId, c])),
    [n]
  ), ae = I.nodes.find((c) => c.id === W) ?? null, Z = q(async (c, m, R) => {
    p(!0);
    try {
      return await j().api.hubCommand(c, m), t.success(R), !0;
    } catch (P) {
      return t.error(`${c} failed: ${P instanceof Error ? P.message : String(P)}`), !1;
    } finally {
      p(!1);
    }
  }, [t]), ge = q(async (c) => {
    try {
      const m = await e.hubQuery("industrial/workflow-definitions"), P = (oe(m) ?? []).find((E) => {
        const x = E;
        return String((x == null ? void 0 : x.workflowId) ?? "") === c;
      });
      if (P === void 0) {
        O({ ...be(c), name: c });
        return;
      }
      O(ve(P));
      const M = ve(P).nodes.map((E) => E.id).map((E) => /^n(\d+)$/.exec(E)).filter(Boolean).reduce((E, x) => Math.max(E, Number(x[1])), 0);
      pe.current = M + 1, w(null), G([]), Y(!1);
    } catch (m) {
      t.error(`加载工作流失败: ${m instanceof Error ? m.message : String(m)}`);
    }
  }, [e, t]), se = q(async () => {
    try {
      const c = await e.hubQuery("industrial/execution");
      if (c == null) return;
      const m = c;
      De({ status: String(m.status ?? ""), error: m.error, executed: m.executed ?? [] });
    } catch {
    }
  }, [e]), Oe = q(async () => {
    await Z("industrial/save-workflow", I, "已保存") && Y(!1);
  }, [Z, I]), Re = q(async () => {
    if (b) {
      t.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await Z("industrial/publish-workflow", { workflowId: I.workflowId }, "已发布") && await se();
  }, [Z, I.workflowId, b, t, se]), _e = q(async () => {
    if (b) {
      t.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await Z(
      "industrial/run-workflow",
      { workflowId: I.workflowId, resourceId: h },
      "采集已启动"
    ) && await se();
  }, [Z, I.workflowId, b, t, se, h]), Ae = q((c, m, R) => {
    const P = Q(B, c);
    if (!P) return;
    const _ = `n${pe.current++}`;
    O((M) => ({
      ...M,
      nodes: [...M.nodes, dt(P, _, m, R)]
    })), w(_), G([]), Y(!0);
  }, [B]), je = q((c, m) => {
    const R = I.nodes.find((ee) => ee.id === c), P = I.nodes.find((ee) => ee.id === m);
    if (!R || !P)
      return t.warning("连线失败：端点不在画布上"), null;
    const _ = Q(B, R.type), M = Q(B, P.type);
    if (!_ || !M) return null;
    const E = ft(_, M);
    if (!E)
      return t.warning(`端口类型不兼容：${_.name} → ${M.name}`), null;
    const x = gt(I, {
      source: c,
      sourcePort: E.sourcePort,
      target: m,
      targetPort: E.targetPort
    }, B);
    return x.issue ? (t.warning(`${x.issue.code}: ${x.issue.message}`), G((ee) => [...ee, x.issue]), null) : x.definition === I ? (t.info("这条连线已经存在"), E) : (O(x.definition), Y(!0), E);
  }, [B, I, t]), Fe = q(() => {
    const c = Tt(I, B, new Set(s));
    G(c), c.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : t.warning(`发现 ${c.length} 个问题，见右侧定位列表`);
  }, [I, B, s, t]), Me = q((c, m) => {
    O((R) => ({
      ...R,
      edges: R.edges.map((P, _) => _ === c ? m : P)
    })), Y(!0);
  }, []);
  return k ? /* @__PURE__ */ i($e, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ T(xe, { style: { minHeight: 520, border: "1px solid #f0f0f0" }, children: [
    r,
    /* @__PURE__ */ T(vt, { style: { background: "#001529", display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }, children: [
      /* @__PURE__ */ i(A.Text, { strong: !0, style: { color: "#fff" }, children: "Industrial Designer" }),
      /* @__PURE__ */ i(V, { color: "blue", children: "Cordis block renderer" }),
      b && /* @__PURE__ */ i(V, { color: "orange", children: "未保存" }),
      /* @__PURE__ */ T(Ge, { children: [
        /* @__PURE__ */ i(J, { type: y ? "primary" : "default", onClick: () => $((c) => !c), children: y ? "退出连线" : "连线模式" }),
        /* @__PURE__ */ i(J, { loading: f, onClick: () => void Oe(), children: "保存" }),
        /* @__PURE__ */ i(J, { onClick: Fe, children: "校验" }),
        /* @__PURE__ */ i(J, { loading: f, onClick: () => void Re(), children: "发布" }),
        /* @__PURE__ */ i(J, { type: "primary", loading: f, onClick: () => void _e(), children: "运行" }),
        /* @__PURE__ */ i(
          re,
          {
            style: { width: 180 },
            value: h,
            onChange: g,
            options: s.map((c) => ({ value: c, label: c }))
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ T(bt, { style: { display: "grid", gridTemplateColumns: "220px 1fr 300px", gap: 12, padding: 12 }, children: [
      /* @__PURE__ */ i("div", { style: { borderRight: "1px solid #f0f0f0", paddingRight: 12 }, children: /* @__PURE__ */ i(Ke, { descriptors: n, loading: !1 }) }),
      /* @__PURE__ */ i(
        Ve,
        {
          definition: I,
          catalog: B,
          selectedId: W,
          connectMode: y,
          onSelect: w,
          onDropNode: Ae,
          onCreateEdge: je,
          onPositions: (c) => {
            O((m) => wt(m, c)), Y(!0);
          }
        }
      ),
      /* @__PURE__ */ T("div", { style: { borderLeft: "1px solid #f0f0f0", paddingLeft: 12 }, children: [
        N.length > 0 && /* @__PURE__ */ i(
          me,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 12 },
            message: "部分设计器数据未加载",
            description: N.join("；")
          }
        ),
        a.length > 0 && /* @__PURE__ */ i(
          re,
          {
            style: { width: "100%", marginBottom: 12 },
            value: I.workflowId,
            placeholder: "选择工作流",
            onChange: (c) => void ge(c),
            options: a.map((c) => ({ value: c.workflowId, label: c.name }))
          }
        ),
        X && /* @__PURE__ */ i(
          me,
          {
            style: { marginBottom: 12 },
            type: X.error ? "error" : "success",
            showIcon: !0,
            message: `执行状态: ${X.status}`,
            description: X.error ?? ((ye = X.executed) != null && ye.length ? `已执行节点: ${X.executed.join(", ")}` : void 0)
          }
        ),
        /* @__PURE__ */ i(
          Ze,
          {
            node: ae,
            descriptor: ae ? Q(B, ae.type) ?? null : null,
            edges: I.edges,
            catalog: B,
            issues: U,
            onApplyConfig: (c) => {
              W && (O((m) => ht(m, W, c)), Y(!0));
            },
            onDeleteNode: () => {
              O((c) => yt(c, W ?? "")), w(null), Y(!0);
            },
            onDeleteEdge: (c) => {
              O((m) => mt(m, c)), Y(!0);
            },
            onUpdateEdge: Me,
            onSelectNode: w
          }
        )
      ] })
    ] })
  ] });
}
function kt(e) {
  ze(e);
  const t = ({ ctx: r }) => Se(St, { ctx: r });
  return t.displayName = "IndustrialWorkflowCanvas", e.blocks.register("industrial-workflow-canvas", t);
}
export {
  kt as default,
  kt as register
};
