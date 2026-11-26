let we = null;
function wt(e) {
  we = e;
}
function X() {
  if (!we && typeof globalThis < "u" && (we = globalThis.__CORDIS_CONSOLE ?? null), !we)
    throw new Error("[industrial-designer] console facade is not available");
  return we;
}
const o = (...e) => X().jsxRuntime.jsx(...e), m = (...e) => X().jsxRuntime.jsxs(...e), xe = (e) => X().jsxRuntime.jsx(X().jsxRuntime.Fragment, e), $e = (...e) => X().React.createElement(...e), z = (...e) => X().React.useCallback(...e), J = (...e) => X().React.useEffect(...e), Xe = (...e) => X().React.useMemo(...e), ie = (...e) => X().React.useRef(...e), v = (...e) => X().React.useState(...e), L = (e) => {
  const t = (r) => {
    const n = e.split(".").reduce((a, d) => a == null ? void 0 : a[d], X().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return $e(n, r ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, pe = L("Alert"), R = L("Button"), Ue = L("Divider"), vt = L("Drawer"), Ze = L("Empty"), Re = Object.assign(L("Form"), {
  Item: L("Form.Item"),
  useForm: (...e) => X().antd.Form.useForm(...e)
}), K = Object.assign(L("Input"), {
  Number: L("InputNumber"),
  TextArea: L("Input.TextArea")
}), et = L("InputNumber"), bt = Object.assign(L("Layout"), {
  Header: L("Layout.Header"),
  Content: L("Layout.Content")
}), ve = L("Modal"), ge = L("Select"), _ = L("Space"), tt = L("Spin"), rt = L("Switch"), se = L("Table"), ce = L("Tag"), E = Object.assign(L("Typography"), {
  Title: L("Typography.Title"),
  Text: L("Typography.Text")
}), It = new Proxy({}, {
  get: (e, t) => (...r) => {
    var a;
    const n = (a = X().antd.message) == null ? void 0 : a[t];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${t} unavailable`);
    return n(...r);
  }
}), nt = 148, at = 46, Ge = {
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
}, We = (e) => Ge[e] ?? Ge[e.split(".")[0]] ?? "#4d6bfe";
function xt(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function kt(e, t = /* @__PURE__ */ new Map(), r) {
  return {
    nodes: e.nodes.map((n) => {
      const a = t.get(n.type), d = (a == null ? void 0 : a.category) ?? "General", i = r === n.id;
      return {
        id: n.id,
        data: {
          label: (a == null ? void 0 : a.name) ?? n.type,
          nodeTypeId: n.type,
          category: d,
          icon: (a == null ? void 0 : a.icon) ?? "AppstoreOutlined",
          config: { ...n.config }
        },
        style: {
          x: n.x,
          y: n.y,
          fill: We(d),
          stroke: i ? "#111111" : We(d),
          lineWidth: i ? 3 : 1.5,
          radius: 10,
          port: !0,
          ports: [
            ...((a == null ? void 0 : a.inputs) ?? []).map((s, c, u) => ({
              key: `in:${s.name}`,
              placement: [0, (c + 1) / (u.length + 1)],
              r: 5,
              stroke: s.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((a == null ? void 0 : a.outputs) ?? []).map((s, c, u) => ({
              key: `out:${s.name}`,
              placement: [1, (c + 1) / (u.length + 1)],
              r: 5,
              stroke: s.type === "Control" ? "#52c41a" : "#1677ff",
              fill: "#fff"
            }))
          ]
        }
      };
    }),
    edges: e.edges.map((n) => ({
      id: xt(n),
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
function St(e, t, r) {
  const n = r && r.length > 0 ? new Set(r) : null, a = {};
  for (const d of e.nodes) {
    if (!t.has(d.id) || n && !n.has(d.id)) continue;
    const i = d.style.x, s = d.style.y;
    typeof i == "number" && typeof s == "number" && (a[d.id] = { x: i, y: s });
  }
  return a;
}
function Tt(e, t) {
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
        size: [nt, at],
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
    behaviors: ot()
  };
}
const Ct = "__industrial_drag_target__", Nt = "__industrial_connect_source__", Pe = (e, t) => {
  var r;
  return (((r = e.isPanModifier) == null ? void 0 : r.call(e)) ?? !1) || t.button === 1 || t.buttons === 4;
}, Qe = (e, t) => {
  var w;
  if (t.targetType !== "node" || !((w = t.target) != null && w.id)) return !1;
  const r = t.client ?? t.canvas;
  if (!r || !e.graph) return !1;
  const n = String(t.target.id), a = e.graph.getNodeData().find((I) => String(I.id) === n), d = a == null ? void 0 : a.style;
  if (typeof (d == null ? void 0 : d.x) != "number" || typeof d.y != "number") return !1;
  const [i, s] = e.graph.getCanvasByClient([r.x, r.y]), c = Math.abs(i - d.x), u = Math.abs(s - d.y);
  return Math.abs(c - nt / 2) <= 14 && u <= at / 2 + 10;
};
function ot(e = {}) {
  const t = e.connectionState ?? { armed: !1 };
  return [
    "zoom-canvas",
    {
      type: "drag-canvas",
      enable: (r) => r.targetType === "canvas" || Pe(e, r),
      range: 0.9
    },
    {
      type: "drag-element",
      state: Ct,
      enable: (r) => (r.targetType === "node" || r.targetType === "combo") && !Qe(e, r) && !Pe(e, r),
      onFinish: (r) => {
        var i, s;
        const n = e.graph;
        if (!n) return;
        const a = Array.isArray(r) ? r : r ? [String(r)] : void 0, d = n.getNodeData().map((c) => ({
          id: String(c.id),
          data: c.data ?? {},
          style: c.style ?? {}
        }));
        (s = e.onPositions) == null || s.call(e, St(
          { nodes: d },
          ((i = e.allowedIds) == null ? void 0 : i.call(e)) ?? /* @__PURE__ */ new Set(),
          a
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: Nt,
      enable: (r) => Pe(e, r) ? (t.armed = !1, !1) : t.armed ? r.targetType === "node" || r.targetType === "combo" : Qe(e, r) ? (t.armed = !0, !0) : !1,
      onCreate: (r) => {
        var i;
        t.armed = !1;
        const n = String(r.source ?? ""), a = String(r.target ?? "");
        if (!n || !a || n === a) return;
        const d = (i = e.onCreateEdge) == null ? void 0 : i.call(e, n, a);
        if (d)
          return {
            ...r,
            sourcePort: `out:${d.sourcePort}`,
            targetPort: `in:${d.targetPort}`
          };
      }
    }
  ];
}
const Et = (e) => {
  var r;
  const t = e;
  return (t == null ? void 0 : t.targetType) === "node" ? (r = t.target) == null ? void 0 : r.id : void 0;
};
function Pt(e) {
  const {
    definition: t,
    catalog: r,
    selectedId: n,
    onSelect: a,
    onDropNode: d,
    onCreateEdge: i,
    onPositions: s
  } = e, c = ie(null), u = ie(null), w = ie(!1), I = ie(!1), S = ie({ armed: !1 }), b = ie({ onSelect: a, onDropNode: d, onCreateEdge: i, onPositions: s });
  b.current = { onSelect: a, onDropNode: d, onCreateEdge: i, onPositions: s };
  const h = Xe(
    () => kt(t, r, n),
    [t, r, n]
  ), p = ie(h);
  p.current = h;
  const N = ie(/* @__PURE__ */ new Set());
  return N.current = new Set(t.nodes.map((D) => D.id)), J(() => {
    const D = (j) => {
      j.code === "Space" && !j.repeat && (I.current = !0);
    }, x = (j) => {
      j.code === "Space" && (I.current = !1);
    }, O = () => {
      I.current = !1;
    };
    return window.addEventListener("keydown", D), window.addEventListener("keyup", x), window.addEventListener("blur", O), () => {
      window.removeEventListener("keydown", D), window.removeEventListener("keyup", x), window.removeEventListener("blur", O);
    };
  }, []), J(() => {
    const D = c.current;
    if (!D || w.current) return;
    let x = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: O }) => {
      if (x || !c.current || w.current) return;
      const j = Tt(p.current, D), g = new O({
        ...j,
        behaviors: ot({
          graph: {
            getNodeData: () => {
              var k;
              return ((k = u.current) == null ? void 0 : k.getNodeData()) ?? [];
            },
            getCanvasByClient: (k) => {
              var te;
              const T = ((te = u.current) == null ? void 0 : te.getCanvasByClient(k)) ?? k;
              return [T[0], T[1]];
            }
          },
          isPanModifier: () => I.current,
          connectionState: S.current,
          allowedIds: () => N.current,
          onPositions: (k) => b.current.onPositions(k),
          onCreateEdge: (k, T) => b.current.onCreateEdge(k, T)
        })
      });
      g.on("node:click", (k) => {
        const T = Et(k);
        T && b.current.onSelect(T);
      }), g.on("edge:click", () => b.current.onSelect(null)), g.on("canvas:click", () => b.current.onSelect(null)), g.on("pointerup", () => {
        S.current.armed = !1;
      }), u.current = g, D.__industrialGraph = g, w.current = !0, g.render();
    }), () => {
      x = !0;
    };
  }, []), J(() => {
    const D = u.current;
    !D || !w.current || (D.setData(h), D.draw());
  }, [h]), J(() => () => {
    var D;
    (D = u.current) == null || D.destroy(), u.current = null, w.current = !1;
  }, []), /* @__PURE__ */ o("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ m("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ o(
      "div",
      {
        ref: c,
        className: "industrial-graph-host",
        onDragOver: (D) => D.preventDefault(),
        onDrop: (D) => {
          D.preventDefault();
          const x = D.dataTransfer.getData("application/x-industrial-node") || D.dataTransfer.getData("text/plain");
          if (!x) return;
          const O = u.current, j = c.current;
          if (!O || !j) return;
          const g = j.getBoundingClientRect(), k = O.getCanvasByViewport([
            D.clientX - g.left,
            D.clientY - g.top
          ]);
          b.current.onDropNode(x, k[0], k[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ o("div", { className: "industrial-graph-hint", children: "拖动节点移动 · 从端口拖出连线 · 拖动画布平移（空格或中键可在节点上平移）" })
  ] }) });
}
function Dt({ descriptors: e, loading: t, error: r, onAdd: n }) {
  const a = /* @__PURE__ */ new Map();
  for (const i of e) {
    const s = i.category.split(".")[0] || "General", c = a.get(s) ?? [];
    c.push(i), a.set(s, c);
  }
  const d = (i, s) => {
    i.dataTransfer.setData("application/x-industrial-node", s.nodeTypeId), i.dataTransfer.setData("text/plain", s.nodeTypeId), i.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ m("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ o(E.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ o(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ o(tt, { size: "small", style: { display: "block", marginTop: 16 } }),
    r && /* @__PURE__ */ o(E.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: r }),
    !t && !r && e.length === 0 && /* @__PURE__ */ o(Ze, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(a.entries()).map(([i, s]) => /* @__PURE__ */ m("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ o(E.Text, { strong: !0, style: { fontSize: 12 }, children: i }),
      s.map((c) => /* @__PURE__ */ m(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          role: "button",
          tabIndex: 0,
          onDragStart: (u) => d(u, c),
          onClick: () => n == null ? void 0 : n(c.nodeTypeId),
          onKeyDown: (u) => {
            (u.key === "Enter" || u.key === " ") && (n == null || n(c.nodeTypeId));
          },
          title: c.description || c.nodeTypeId,
          children: [
            /* @__PURE__ */ o("div", { className: "industrial-palette-node-title", children: c.name }),
            /* @__PURE__ */ m("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ o(ce, { bordered: !1, color: "blue", children: c.version }),
              /* @__PURE__ */ o(E.Text, { type: "secondary", style: { fontSize: 11 }, children: c.nodeTypeId })
            ] }),
            /* @__PURE__ */ m("div", { className: "industrial-palette-node-ports", children: [
              c.inputs.map((u) => /* @__PURE__ */ m(ce, { bordered: !1, color: "default", children: [
                "in ",
                u.name,
                ": ",
                u.type
              ] }, `in-${u.name}`)),
              c.outputs.map((u) => /* @__PURE__ */ m(ce, { bordered: !1, color: "green", children: [
                "out ",
                u.name,
                ": ",
                u.type
              ] }, `out-${u.name}`))
            ] })
          ]
        },
        c.nodeTypeId
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
function $t(e) {
  const t = {};
  for (const [r, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (t[r] = n.default);
  return t;
}
function Rt(e, t, r = 0, n = 0) {
  return { id: t, type: e.nodeTypeId, config: $t(e), x: r, y: n };
}
function fe(e, t) {
  return e.get(t);
}
function ke(e, t, r) {
  return e[r].find((n) => n.name === t);
}
function Ae(e, t) {
  return e !== "Unknown" && t !== "Unknown" && e === t;
}
function Je(e, t) {
  for (const r of e.outputs)
    for (const n of t.inputs)
      if (Ae(r.type, n.type))
        return { sourcePort: r.name, targetPort: n.name };
  return null;
}
const H = (e, t, r) => ({ path: e, code: t, message: r });
function _t(e, t) {
  return e.source === t.source && e.sourcePort === t.sourcePort && e.target === t.target && e.targetPort === t.targetPort;
}
function Ot(e, t, r) {
  const n = `edges.${e.edges.length}`, a = e.nodes.find((w) => w.id === t.source), d = e.nodes.find((w) => w.id === t.target);
  if (!a || !d)
    return { definition: e, issue: H(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((w) => _t(w, t)))
    return { definition: e };
  const i = fe(r, a.type), s = fe(r, d.type);
  if (!i || !s)
    return { definition: e, issue: H(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const c = ke(i, t.sourcePort, "outputs"), u = ke(s, t.targetPort, "inputs");
  return !c || !u ? { definition: e, issue: H(n, "PORT_MISSING", "declared port does not exist") } : Ae(c.type, u.type) ? { definition: { ...e, edges: [...e.edges, t] } } : {
    definition: e,
    issue: H(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${c.type} → ${u.type} is not a legal connection`
    )
  };
}
function zt(e, t) {
  return {
    ...e,
    nodes: e.nodes.filter((r) => r.id !== t),
    edges: e.edges.filter((r) => r.source !== t && r.target !== t)
  };
}
function At(e, t) {
  return { ...e, edges: e.edges.filter((r, n) => n !== t) };
}
function jt(e, t) {
  const r = { ...e };
  for (const [n, a] of Object.entries(t))
    a === void 0 ? delete r[n] : r[n] = a;
  return r;
}
function Lt(e, t, r) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === t ? { ...n, config: r } : n)
  };
}
function Ft(e, t) {
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
function qt(e) {
  var d;
  const t = /* @__PURE__ */ new Map();
  for (const i of e.nodes) t.set(i.id, []);
  for (const i of e.edges)
    (d = t.get(i.source)) == null || d.push(i.target);
  const r = /* @__PURE__ */ new Map(), n = [], a = (i) => {
    r.set(i, 1), n.push(i);
    for (const s of t.get(i) ?? []) {
      if (r.get(s) === 1)
        return [...n.slice(n.indexOf(s)), s];
      if (r.get(s) !== 2) {
        const c = a(s);
        if (c) return c;
      }
    }
    return n.pop(), r.set(i, 2), null;
  };
  for (const i of e.nodes)
    if (r.get(i.id) !== 2) {
      const s = a(i.id);
      if (s) return s;
    }
  return null;
}
function Mt(e, t, r) {
  const n = [];
  e.workflowId.trim() || n.push(H("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(H("workflow", "INVALID_VERSION", "version must be positive"));
  const a = /* @__PURE__ */ new Map();
  for (const s of e.nodes)
    a.has(s.id) && n.push(H(`nodes.${s.id}`, "DUPLICATE_NODE_ID", `duplicate node '${s.id}'`)), a.set(s.id, s);
  const d = /* @__PURE__ */ new Map();
  for (const s of e.nodes) {
    const c = fe(t, s.type);
    if (!c) {
      n.push(H(
        `nodes.${s.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${s.type}' is not registered`
      ));
      continue;
    }
    d.set(s.id, c);
    const u = c.configurationSchema.required ?? [];
    for (const w of u) {
      const I = s.config[w];
      (I == null || I === "") && n.push(H(
        `nodes.${s.id}.config.${w}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${w}' is missing`
      ));
    }
    r && typeof s.config.resourceId == "string" && (r.has(s.config.resourceId) || n.push(H(
      `nodes.${s.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${s.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((s, c) => {
    const u = a.get(s.source), w = a.get(s.target);
    if (!u || !w) {
      n.push(H(`edges.${c}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const I = d.get(s.source), S = d.get(s.target);
    if (!I || !S) return;
    const b = ke(I, s.sourcePort, "outputs"), h = ke(S, s.targetPort, "inputs");
    if (!b || !h) {
      n.push(H(`edges.${c}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    Ae(b.type, h.type) || n.push(H(
      `edges.${c}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${b.type} → ${h.type} is not a legal connection`
    ));
  });
  for (const s of e.nodes) {
    const c = d.get(s.id);
    if (c)
      for (const u of c.inputs.filter((w) => w.required))
        e.edges.some((w) => w.target === s.id && w.targetPort === u.name && a.has(w.source)) || n.push(H(
          `nodes.${s.id}.inputs.${u.name}`,
          "MISSING_INPUT",
          `required input '${u.name}' is not connected`
        ));
  }
  const i = qt(e);
  return i && n.push(H(`nodes.${i[0]}`, "GRAPH_CYCLE", `cycle: ${i.join(" → ")}`)), n;
}
const Oe = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function Bt(e) {
  const t = Oe(e.additionalProperties).enum;
  if (!Array.isArray(t)) return null;
  const r = t.filter((n) => typeof n == "string");
  return r.length > 0 ? r : null;
}
function it(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && Oe(e.items).type === "string" ? "string-list" : e.type === "object" && Oe(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function ze(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, t]) => t != null && typeof t != "object").map(([t, r]) => ({ key: t, value: String(r) }));
}
function Ye(e) {
  const t = {}, r = [];
  for (const n of e) {
    const a = n.key.trim();
    a && (Object.prototype.hasOwnProperty.call(t, a) && r.push(a), t[a] = n.value);
  }
  return { record: t, duplicateKeys: r };
}
function Ie(e) {
  if (!Array.isArray(e)) return [];
  const t = /* @__PURE__ */ new Set(), r = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const a = n.trim();
    !a || t.has(a) || (t.add(a), r.push(a));
  }
  return r;
}
function Kt(e, t) {
  const r = /* @__PURE__ */ new Set();
  for (const [n, a] of Object.entries(e))
    if (it(a) === "key-value")
      for (const d of ze(t[n]))
        d.key.trim() && r.add(d.key.trim());
  return [...r];
}
function Ut({
  value: e,
  onChange: t,
  valueOptions: r,
  keyTitle: n = "目标字段",
  valueTitle: a = "来源字段",
  testId: d
}) {
  const i = JSON.stringify(e ?? {}), [s, c] = v(() => ze(e)), [u, w] = v(i);
  J(() => {
    i !== u && (c(ze(e)), w(i));
  }, [i, u, e]);
  const I = (b) => {
    c(b), t == null || t(Ye(b).record);
  }, { duplicateKeys: S } = Ye(s);
  return /* @__PURE__ */ m("div", { "data-testid": d, className: "industrial-kv-editor", children: [
    s.map((b, h) => /* @__PURE__ */ m("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ o(
        K,
        {
          "aria-label": `${n} ${h + 1}`,
          value: b.key,
          placeholder: n,
          onChange: (p) => I(s.map((N, A) => A === h ? { ...N, key: p.target.value } : N))
        }
      ),
      r && r.length > 0 ? /* @__PURE__ */ o(
        ge,
        {
          "aria-label": `${a} ${h + 1}`,
          value: b.value || void 0,
          placeholder: a,
          options: r.map((p) => ({ label: p, value: p })),
          onChange: (p) => I(s.map((N, A) => A === h ? { ...N, value: p ?? "" } : N))
        }
      ) : /* @__PURE__ */ o(
        K,
        {
          "aria-label": `${a} ${h + 1}`,
          value: b.value,
          placeholder: a,
          onChange: (p) => I(s.map((N, A) => A === h ? { ...N, value: p.target.value } : N))
        }
      ),
      /* @__PURE__ */ o(
        R,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${h + 1}`,
          onClick: () => I(s.filter((p, N) => N !== h)),
          children: "删"
        }
      )
    ] }, h)),
    /* @__PURE__ */ o(
      R,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => I([...s, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    S.length > 0 && /* @__PURE__ */ m(E.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      S.join("、")
    ] })
  ] });
}
function Gt({ value: e, onChange: t, suggestions: r = [], testId: n }) {
  const a = JSON.stringify(Ie(e)), [d, i] = v(() => Ie(e)), [s, c] = v(a);
  J(() => {
    a !== s && (i(Ie(e)), c(a));
  }, [a, s, e]);
  const u = (w) => {
    i(w), t == null || t(Ie(w));
  };
  return /* @__PURE__ */ m("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ o("datalist", { id: `${n ?? "list"}-suggestions`, children: r.map((w) => /* @__PURE__ */ o("option", { value: w }, w)) }),
    d.map((w, I) => /* @__PURE__ */ m("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ o(
        K,
        {
          "aria-label": `字段 ${I + 1}`,
          value: w,
          list: `${n ?? "list"}-suggestions`,
          onChange: (S) => u(d.map((b, h) => h === I ? S.target.value : b))
        }
      ),
      /* @__PURE__ */ o(
        R,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${I + 1}`,
          onClick: () => u(d.filter((S, b) => b !== I)),
          children: "删"
        }
      )
    ] }, I)),
    /* @__PURE__ */ o(R, { size: "small", type: "dashed", block: !0, onClick: () => u([...d, ""]), children: "添加字段" }),
    d.length === 0 && /* @__PURE__ */ o(E.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Wt({ value: e, onChange: t }) {
  const r = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, a] = v(r), [d, i] = v(null);
  J(() => {
    a(r), i(null);
  }, [r]);
  const s = (c) => {
    a(c);
    try {
      const u = JSON.parse(c);
      i(null), t == null || t(u);
    } catch (u) {
      i(u.message);
    }
  };
  return /* @__PURE__ */ m("div", { children: [
    /* @__PURE__ */ o(K.TextArea, { rows: 5, value: n, onChange: (c) => s(c.target.value) }),
    d && /* @__PURE__ */ o(E.Text, { type: "danger", style: { fontSize: 11 }, children: d })
  ] });
}
function Qt({ value: e, onChange: t, resources: r, resourceType: n, label: a }) {
  const d = r.filter((s) => s.enabled !== !1 && (!n || s.type === n)).map((s) => ({
    value: s.id,
    label: `${s.name} (${s.type})`
  })), i = r.find((s) => s.id === e);
  return /* @__PURE__ */ m("div", { children: [
    /* @__PURE__ */ m(
      "select",
      {
        "aria-label": a,
        value: e ?? "",
        onChange: (s) => t == null ? void 0 : t(s.target.value || void 0),
        children: [
          /* @__PURE__ */ o("option", { value: "", children: "未选择" }),
          d.map((s) => /* @__PURE__ */ o("option", { value: s.value, children: s.label }, s.value))
        ]
      }
    ),
    (i == null ? void 0 : i.credentialId) && /* @__PURE__ */ m(E.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      i.credentialName ?? i.credentialId
    ] })
  ] });
}
function Jt({ name: e, schema: t, required: r, resources: n, resourceType: a, properties: d, config: i }) {
  let s, c = "value";
  switch (it(t)) {
    case "resource":
      s = /* @__PURE__ */ o(
        Qt,
        {
          resources: n,
          resourceType: a,
          label: String(t.title ?? e)
        }
      );
      break;
    case "enum":
      s = /* @__PURE__ */ o(
        ge,
        {
          allowClear: !0,
          options: (t.enum ?? []).map((u) => ({ label: String(u), value: u }))
        }
      );
      break;
    case "number":
      s = /* @__PURE__ */ o(et, { style: { width: "100%" } });
      break;
    case "boolean":
      s = /* @__PURE__ */ o(rt, {}), c = "checked";
      break;
    case "key-value":
      s = /* @__PURE__ */ o(
        Ut,
        {
          testId: `field-${e}`,
          valueOptions: Bt(t) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      s = /* @__PURE__ */ o(Gt, { testId: `field-${e}`, suggestions: Kt(d, i) });
      break;
    case "json":
      s = /* @__PURE__ */ o(Wt, {});
      break;
    default:
      s = /* @__PURE__ */ o(K, { placeholder: t.description });
      break;
  }
  return /* @__PURE__ */ o(
    Re.Item,
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
function Yt(e) {
  const {
    node: t,
    descriptor: r,
    nodes: n,
    edges: a,
    catalog: d,
    resources: i,
    issues: s,
    onApplyConfig: c,
    onDeleteNode: u,
    onDeleteEdge: w,
    onCreateEdge: I,
    onUpdateEdge: S,
    onSelectNode: b
  } = e, [h] = Re.useForm(), [p, N] = v("{}"), [A, D] = v(null), [x, O] = v(), [j, g] = v();
  J(() => {
    h.setFieldsValue((t == null ? void 0 : t.config) ?? {}), N(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), D(null);
  }, [h, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const k = new Set((r == null ? void 0 : r.configurationSchema.required) ?? []), T = Object.entries((r == null ? void 0 : r.configurationSchema.properties) ?? {}), te = () => {
    if (T.length > 0) {
      h.validateFields().then((C) => c(jt((t == null ? void 0 : t.config) ?? {}, C)));
      return;
    }
    try {
      const C = JSON.parse(p);
      if (C === null || typeof C != "object" || Array.isArray(C))
        throw new Error("配置必须是 JSON 对象");
      D(null), c(C);
    } catch (C) {
      D(C.message);
    }
  }, de = a.map((C, q) => ({
    ...C,
    index: q,
    key: `${q}:${C.source}->${C.target}`
  })), ye = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (C, q) => /* @__PURE__ */ m(E.Text, { style: { fontSize: 11 }, children: [
        q.source,
        " → ",
        q.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (C, q) => {
        const V = d.get(q.source), ue = d.get(q.target);
        return /* @__PURE__ */ m("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ o(
            ge,
            {
              size: "small",
              value: q.sourcePort,
              style: { width: 110 },
              onChange: (F) => S(q.index, { ...q, sourcePort: F }),
              options: ((V == null ? void 0 : V.outputs) ?? []).map((F) => ({ label: `${F.name} (${F.type})`, value: F.name }))
            }
          ),
          /* @__PURE__ */ o(
            ge,
            {
              size: "small",
              value: q.targetPort,
              style: { width: 110 },
              onChange: (F) => S(q.index, { ...q, targetPort: F }),
              options: ((ue == null ? void 0 : ue.inputs) ?? []).map((F) => ({ label: `${F.name} (${F.type})`, value: F.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (C, q) => /* @__PURE__ */ o(R, { danger: !0, size: "small", type: "text", onClick: () => w(q.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ m("div", { className: "industrial-properties-body", children: [
    !t && /* @__PURE__ */ o(Ze, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ m(xe, { children: [
      /* @__PURE__ */ o(E.Text, { strong: !0, children: (r == null ? void 0 : r.name) ?? t.type }),
      /* @__PURE__ */ o(ce, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ o(Re, { form: h, layout: "vertical", size: "small", style: { marginTop: 12 }, children: T.map(([C, q]) => /* @__PURE__ */ o(
        Jt,
        {
          name: C,
          schema: q,
          required: k.has(C),
          resources: i,
          resourceType: typeof (r == null ? void 0 : r.configurationSchema.resourceType) == "string" ? r.configurationSchema.resourceType : void 0,
          properties: (r == null ? void 0 : r.configurationSchema.properties) ?? {},
          config: (t == null ? void 0 : t.config) ?? {}
        },
        C
      )) }),
      T.length === 0 && /* @__PURE__ */ m(xe, { children: [
        /* @__PURE__ */ o(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ o(
          K.TextArea,
          {
            rows: 8,
            value: p,
            style: { marginTop: 6 },
            onChange: (C) => N(C.target.value)
          }
        ),
        A && /* @__PURE__ */ o(E.Text, { type: "danger", style: { fontSize: 12 }, children: A })
      ] }),
      /* @__PURE__ */ o(R, { type: "primary", block: !0, onClick: te, children: "应用配置" }),
      /* @__PURE__ */ o(R, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: u, children: "删除节点" }),
      (r == null ? void 0 : r.activityType) && /* @__PURE__ */ o(E.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: r.activityType })
    ] }),
    /* @__PURE__ */ m(Ue, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      a.length,
      ")"
    ] }),
    n.length > 1 && /* @__PURE__ */ m(_, { wrap: !0, style: { marginBottom: 8 }, children: [
      /* @__PURE__ */ m(
        "select",
        {
          "aria-label": "连线起点",
          value: x ?? "",
          onChange: (C) => O(C.target.value || void 0),
          children: [
            /* @__PURE__ */ o("option", { value: "", children: "起点节点" }),
            n.map((C) => /* @__PURE__ */ o("option", { value: C.id, children: C.id }, C.id))
          ]
        }
      ),
      /* @__PURE__ */ m(
        "select",
        {
          "aria-label": "连线终点",
          value: j ?? "",
          onChange: (C) => g(C.target.value || void 0),
          children: [
            /* @__PURE__ */ o("option", { value: "", children: "终点节点" }),
            n.map((C) => /* @__PURE__ */ o("option", { value: C.id, children: C.id }, C.id))
          ]
        }
      ),
      /* @__PURE__ */ o(
        R,
        {
          size: "small",
          disabled: !x || !j || x === j,
          onClick: () => {
            x && j && I(x, j);
          },
          children: "添加连线"
        }
      )
    ] }),
    a.length === 0 ? /* @__PURE__ */ o(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ o(se, { size: "small", columns: ye, dataSource: de, pagination: !1 }),
    s.length > 0 && /* @__PURE__ */ m(xe, { children: [
      /* @__PURE__ */ m(Ue, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        s.length,
        ")"
      ] }),
      s.map((C, q) => /* @__PURE__ */ m("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ o(ce, { color: "red", children: C.code }),
        /* @__PURE__ */ m(
          E.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const V = /^nodes\.([^.]+)/.exec(C.path);
              V && b(V[1]);
            },
            children: [
              C.path,
              ": ",
              C.message
            ]
          }
        )
      ] }, `${C.path}-${q}`))
    ] })
  ] });
}
const Ht = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class Vt extends Error {
  constructor(t) {
    super(t), this.name = "DtoError";
  }
}
const $ = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, U = (e) => Array.isArray(e) ? e : [], f = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", B = (e, t = 0) => {
  const r = typeof e == "string" ? Number(e) : e;
  return typeof r == "number" && Number.isFinite(r) ? r : t;
}, l = (e, ...t) => {
  for (const r of t)
    if (e[r] !== void 0 && e[r] !== null) return e[r];
};
function ee(e) {
  const t = $(e);
  if (Object.prototype.hasOwnProperty.call(t, "data")) return t.data;
  if (typeof t.code == "string" && typeof t.message == "string")
    throw new Vt(`${t.code}: ${t.message}`);
  return e;
}
function Xt(e) {
  const t = f(e).trim().toLowerCase();
  return Ht.find((n) => n.toLowerCase() === t) ?? "Unknown";
}
function Zt(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const t = $(e), r = f(l(t, "name", "port", "id")).trim();
  return r ? {
    name: r,
    type: Xt(l(t, "type", "portType", "dataType")),
    required: l(t, "required", "isRequired") === !0
  } : null;
}
function He(e) {
  const t = U(e).map(Zt).filter((n) => n !== null), r = /* @__PURE__ */ new Set();
  return t.filter((n) => r.has(n.name) ? !1 : (r.add(n.name), !0));
}
const er = (e) => {
  const t = $(e);
  return Object.keys(t).length === 0 ? { type: "object", properties: {} } : t;
};
function tr(e) {
  const t = $(e), r = f(l(t, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return r ? {
    nodeTypeId: r,
    name: f(l(t, "name", "title"), r),
    description: f(l(t, "description", "desc")),
    category: f(l(t, "category", "group"), "General"),
    icon: f(l(t, "icon"), "AppstoreOutlined"),
    version: f(l(t, "version"), "1.0.0"),
    inputs: He(l(t, "inputs", "inputPorts", "input_ports")),
    outputs: He(l(t, "outputs", "outputPorts", "output_ports")),
    configurationSchema: er(l(t, "configurationSchema", "configSchema", "schema")),
    activityType: f(l(t, "activityType", "activity_type")),
    dependencies: U(l(t, "dependencies")).map((n) => f(n)).filter(Boolean)
  } : null;
}
function rr(e) {
  const t = ee(e), r = Array.isArray(t) ? t : U(l($(t), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return r.map(tr).filter((a) => a !== null).filter((a) => n.has(a.nodeTypeId) ? !1 : (n.add(a.nodeTypeId), !0));
}
function nr(e) {
  const t = ee(e), r = Array.isArray(t) ? t : U(l($(t), "resources", "items")), n = /* @__PURE__ */ new Set();
  return r.map((a) => {
    const d = $(a), i = f(l(d, "id")).trim();
    return !i || n.has(i) ? null : (n.add(i), {
      id: i,
      name: f(l(d, "name"), i),
      type: f(l(d, "type"), "unknown"),
      enabled: d.enabled !== !1,
      credentialId: typeof d.credentialId == "string" ? d.credentialId : null,
      credentialName: typeof d.credentialName == "string" ? d.credentialName : null
    });
  }).filter((a) => a !== null);
}
function st(e) {
  const t = $(e), r = f(l(t, "workflowId", "workflow_id", "id")).trim();
  return r ? {
    workflowId: r,
    name: f(l(t, "name", "title"), r),
    version: B(l(t, "version"), 1)
  } : null;
}
function lt(e) {
  const t = ee(e);
  return (Array.isArray(t) ? t : U(l($(t), "workflows", "items"))).map(st).filter((n) => n !== null);
}
function Se(e) {
  const t = $(e), r = {};
  for (const [n, a] of Object.entries(t))
    a !== void 0 && (r[n] = a);
  return r;
}
function ar(e) {
  const t = $(e), r = f(l(t, "id", "nodeId", "node_id")).trim();
  if (!r) return null;
  const n = $(l(t, "position", "layout"));
  return {
    id: r,
    type: f(l(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: Se(l(t, "config", "configuration")),
    x: B(l(t, "x") ?? l(n, "x"), 0),
    y: B(l(t, "y") ?? l(n, "y"), 0)
  };
}
function or(e) {
  const t = $(e), r = f(l(t, "source", "from", "sourceId")).trim(), n = f(l(t, "target", "to", "targetId")).trim();
  return !r || !n ? null : {
    source: r,
    sourcePort: f(l(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: f(l(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function ir(e) {
  const t = ee(e), r = $(t), n = st(r) ?? { workflowId: "", name: "", version: 1 }, a = U(l(r, "nodes")).map(ar).filter((c) => c !== null), d = U(l(r, "edges")).map(or).filter((c) => c !== null), i = $(l(r, "trigger")), s = {
    kind: f(l(i, "kind", "type"), "manual"),
    cron: l(i, "cron") === void 0 ? null : f(i.cron),
    eventName: l(i, "eventName", "event_name") === void 0 ? null : f(l(i, "eventName", "event_name")),
    config: Se(l(i, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: a,
    edges: d,
    trigger: s,
    resourceReferences: U(l(r, "resourceReferences", "resource_references")).map((c) => f(c)).filter(Boolean),
    layout: Se(l(r, "layout"))
  };
}
const he = (e) => e === !0 || typeof e == "string" && e.toLowerCase() === "true";
function sr(e) {
  const t = ee(e);
  return (Array.isArray(t) ? t : U(l($(t), "resources", "items"))).map((n) => {
    const a = $(n), d = l(a, "credentialId", "credential_id");
    return {
      id: f(l(a, "id"), "—"),
      name: f(l(a, "name"), "—"),
      type: f(l(a, "type"), "—"),
      provider: f(l(a, "provider"), "—"),
      enabled: he(l(a, "enabled")),
      credentialId: d == null ? null : f(d),
      credentialName: l(a, "credentialName") == null ? null : f(l(a, "credentialName")),
      config: Se(l(a, "config", "configuration"))
    };
  });
}
function ct(e) {
  const t = ee(e);
  return (Array.isArray(t) ? t : U(l($(t), "credentials", "items"))).map((n) => {
    const a = $(n);
    return {
      id: f(l(a, "id"), "—"),
      name: f(l(a, "name"), "—"),
      kind: f(l(a, "kind"), "—"),
      version: B(l(a, "version"))
    };
  });
}
function lr(e) {
  const t = ee(e);
  return (Array.isArray(t) ? t : U(l($(t), "tests", "items"))).map((n) => {
    const a = $(n);
    return {
      resourceId: f(l(a, "resourceId", "resource_id"), "—"),
      ok: he(l(a, "ok")),
      detail: f(l(a, "detail"), "")
    };
  });
}
const dt = (e) => {
  if (e == null) return null;
  const t = $(e);
  return Object.keys(t).length === 0 ? null : {
    code: f(l(t, "code"), "OTHER"),
    message: f(l(t, "message"), ""),
    action: f(l(t, "action"), "")
  };
};
function cr(e) {
  const t = ee(e);
  return U(l($(t), "triggers", "items")).map((n) => {
    const a = $(n), d = (i) => l(a, i) == null ? null : f(l(a, i));
    return {
      workflowId: f(l(a, "workflowId", "workflow_id"), "—"),
      version: B(l(a, "version")),
      kind: f(l(a, "kind"), "manual"),
      cron: d("cron"),
      eventName: d("eventName"),
      timezone: d("timezone"),
      resourceId: d("resourceId"),
      nextRun: d("nextRun"),
      lastStatus: d("lastStatus"),
      lastStartedAt: d("lastStartedAt"),
      lastError: d("lastError")
    };
  });
}
function dr(e) {
  if (e == null) return null;
  const t = ee(e), r = $(t);
  if (Object.keys(r).length === 0) return null;
  const n = U(l(r, "files")).map((d) => {
    const i = $(d);
    return {
      path: f(l(i, "path"), "—"),
      sourceDate: f(l(i, "sourceDate"), ""),
      inLedger: he(l(i, "inLedger")),
      expectedTargets: B(l(i, "expectedTargets")),
      succeededTargets: B(l(i, "succeededTargets")),
      incompleteTargets: B(l(i, "incompleteTargets")),
      missingTargets: f(l(i, "missingTargets"), ""),
      wouldSkip: he(l(i, "wouldSkip")),
      wouldCollect: he(l(i, "wouldCollect")),
      duplicateRisk: he(l(i, "duplicateRisk"))
    };
  }), a = $(l(r, "summary"));
  return {
    resourceId: f(l(r, "resourceId"), "—"),
    from: f(l(r, "from"), ""),
    to: f(l(r, "to"), ""),
    files: n,
    summary: {
      discovered: B(l(a, "discovered")),
      alreadyComplete: B(l(a, "alreadyComplete")),
      needsCollection: B(l(a, "needsCollection")),
      duplicateRisk: B(l(a, "duplicateRisk"))
    }
  };
}
function ur(e) {
  const t = ee(e);
  return (Array.isArray(t) ? t : U(l($(t), "files", "items"))).map((n) => {
    const a = $(n);
    return {
      id: f(l(a, "id"), "—"),
      path: f(l(a, "path"), "—"),
      identityKey: f(l(a, "identityKey"), ""),
      sourceResourceId: f(l(a, "sourceResourceId"), ""),
      length: B(l(a, "length")),
      sourceDate: f(l(a, "sourceDate"), ""),
      collectedAt: f(l(a, "collectedAt"), ""),
      status: f(l(a, "status"), "—"),
      deliveries: U(l(a, "deliveries")).map((d) => {
        const i = $(d);
        return {
          id: f(l(i, "id"), "—"),
          collectionFileId: f(l(i, "collectionFileId"), ""),
          targetId: f(l(i, "targetId"), "—"),
          targetKind: f(l(i, "targetKind"), "record"),
          status: f(l(i, "status"), "—"),
          attempts: B(l(i, "attempts")),
          records: B(l(i, "records")),
          error: l(i, "error") == null ? null : f(l(i, "error")),
          accepted: B(l(i, "accepted")),
          rejected: B(l(i, "rejected")),
          warnings: B(l(i, "warnings")),
          diagnosis: dt(l(i, "diagnosis"))
        };
      })
    };
  });
}
function pr(e) {
  const t = ee(e), r = $(t), n = U(l(r, "runs")).map((d) => {
    const i = $(d);
    return {
      id: f(l(i, "id"), "—"),
      workflowId: f(l(i, "workflowId", "workflow_id"), "—"),
      status: f(l(i, "status"), "—"),
      startedAt: f(l(i, "startedAt", "started_at"), ""),
      finishedAt: l(i, "finishedAt", "finished_at") == null ? null : f(l(i, "finishedAt", "finished_at")),
      error: l(i, "error") == null ? null : f(l(i, "error"))
    };
  }), a = U(l(r, "deliveries")).map((d) => {
    const i = $(d);
    return {
      id: f(l(i, "id"), "—"),
      collectionFileId: f(l(i, "collectionFileId", "collection_file_id"), ""),
      targetId: f(l(i, "targetId", "target_id"), "—"),
      targetKind: f(l(i, "targetKind", "target_kind"), "record"),
      status: f(l(i, "status"), "—"),
      attempts: B(l(i, "attempts")),
      records: B(l(i, "records")),
      error: l(i, "error") == null ? null : f(l(i, "error")),
      diagnosis: dt(l(i, "diagnosis"))
    };
  });
  return { runs: n, deliveries: a };
}
function fr(e) {
  if (e == null) return null;
  const t = ee(e), r = $(t);
  if (Object.keys(r).length === 0) return null;
  const n = U(l(r, "nodes")).slice(0, 64).map((a) => {
    const d = $(a);
    return {
      nodeId: f(l(d, "nodeId", "node_id"), "—"),
      status: f(l(d, "status"), "—"),
      records: B(l(d, "records")),
      files: B(l(d, "files")),
      error: l(d, "error") == null ? null : f(l(d, "error"), void 0),
      samples: U(l(d, "samples")).slice(0, 3).map((i) => f(i))
    };
  });
  return {
    status: f(l(r, "status"), "—"),
    error: l(r, "error") == null ? null : f(l(r, "error"), void 0),
    executed: U(l(r, "executed")).map((a) => f(a)),
    instanceId: l(r, "instanceId") == null ? null : f(l(r, "instanceId"), void 0),
    definitionId: l(r, "definitionId") == null ? null : f(l(r, "definitionId"), void 0),
    nodes: n
  };
}
const gr = [
  { value: "manual", label: "手动运行" },
  { value: "schedule", label: "定时运行" },
  { value: "event", label: "事件触发" }
], yr = [
  { label: "每天 02:00", cron: "0 2 * * *" },
  { label: "每小时", cron: "0 * * * *" },
  { label: "每 5 分钟", cron: "*/5 * * * *" },
  { label: "工作日 08:30", cron: "30 8 * * 1-5" }
];
function mr({
  open: e,
  onClose: t,
  trigger: r,
  resourceIds: n,
  onSave: a,
  ctx: d,
  workflowId: i
}) {
  const [s, c] = v(r), [u, w] = v([]), [I, S] = v(null);
  J(() => {
    c(r);
  }, [r]), J(() => {
    e && d.hubQuery("industrial/trigger-status").then((p) => w(cr(p))).catch((p) => S(p instanceof Error ? p.message : String(p)));
  }, [d, e]);
  const b = u.find((p) => p.workflowId === i);
  return /* @__PURE__ */ o(
    ve,
    {
      open: e,
      title: "触发器配置",
      okText: "保存触发器",
      cancelText: "取消",
      onCancel: t,
      onOk: () => {
        var p, N;
        if (s.kind === "schedule" && !((p = s.cron) != null && p.trim())) {
          S("定时运行需要填写 cron 表达式");
          return;
        }
        if (s.kind === "event" && !((N = s.eventName) != null && N.trim())) {
          S("事件触发需要填写事件名称");
          return;
        }
        S(null), a(s), t();
      },
      width: 720,
      children: /* @__PURE__ */ m(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ m(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(E.Text, { children: "触发方式" }),
          /* @__PURE__ */ o(_, { wrap: !0, children: gr.map((p) => /* @__PURE__ */ o(
            R,
            {
              type: s.kind === p.value ? "primary" : "default",
              onClick: () => c({ ...s, kind: p.value }),
              children: p.label
            },
            p.value
          )) })
        ] }),
        /* @__PURE__ */ o(_, { wrap: !0, children: /* @__PURE__ */ o(
          ge,
          {
            "aria-label": "运行资源",
            allowClear: !0,
            placeholder: "运行资源",
            value: hr(s),
            options: n.map((p) => ({ value: p, label: p })),
            onChange: (p) => c({
              ...s,
              config: { ...s.config, resourceId: p ?? "" }
            })
          }
        ) }),
        /* @__PURE__ */ m(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(E.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ m(_, { wrap: !0, children: [
            /* @__PURE__ */ o(E.Text, { children: "常用" }),
            yr.map((p) => /* @__PURE__ */ o(
              R,
              {
                size: "small",
                onClick: () => c({ ...s, cron: p.cron }),
                children: p.label
              },
              p.cron
            ))
          ] }),
          /* @__PURE__ */ o(
            K,
            {
              "aria-label": "Cron 表达式",
              placeholder: "分 时 日 月 周，例如 0 2 * * *",
              value: s.cron ?? "",
              onChange: (p) => c({ ...s, cron: p.target.value })
            }
          ),
          /* @__PURE__ */ o(
            K,
            {
              "aria-label": "调度时区",
              placeholder: "时区（默认 UTC，例如 Asia/Shanghai）",
              value: wr(s),
              onChange: (p) => c({
                ...s,
                config: { ...s.config, timezone: p.target.value }
              })
            }
          ),
          /* @__PURE__ */ o(
            K,
            {
              "aria-label": "事件名称",
              placeholder: "事件名称",
              value: s.eventName ?? "",
              onChange: (p) => c({ ...s, eventName: p.target.value })
            }
          ),
          /* @__PURE__ */ m(E.Text, { type: "secondary", children: [
            "下次运行：",
            (b == null ? void 0 : b.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        b && /* @__PURE__ */ o(
          se,
          {
            rowKey: "workflowId",
            dataSource: [b],
            pagination: !1,
            columns: [
              { title: "时区", dataIndex: "timezone", render: (p) => p ?? "UTC" },
              { title: "状态", dataIndex: "lastStatus", render: (p) => p ?? "未运行" },
              { title: "上次运行", dataIndex: "lastStartedAt", render: (p) => p ?? "—" },
              { title: "错误", dataIndex: "lastError", render: (p) => p ?? "—" }
            ]
          }
        ),
        I && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: I })
      ] })
    }
  );
}
function hr(e) {
  const t = e.config.resourceId;
  return typeof t == "string" && t.trim() ? t : void 0;
}
function wr(e) {
  const t = e.config.timezone;
  return typeof t == "string" && t.trim() ? t : "UTC";
}
function vr({
  open: e,
  onClose: t,
  ctx: r,
  workflowId: n,
  resourceId: a,
  lookbackDays: d
}) {
  const [i, s] = v(null), [c, u] = v(null), [w, I] = v(!1), S = z(async () => {
    I(!0), u(null);
    try {
      await r.hubCommand("industrial/preview-backfill", {
        workflowId: n,
        resourceId: a,
        lookbackDays: d
      }), s(dr(await r.hubQuery("industrial/backfill-preview")));
    } catch (b) {
      u(b instanceof Error ? b.message : String(b));
    } finally {
      I(!1);
    }
  }, [r, d, a, n]);
  return /* @__PURE__ */ o(
    ve,
    {
      open: e,
      title: "补采预览",
      onCancel: t,
      width: 820,
      footer: [
        /* @__PURE__ */ o(R, { loading: w, onClick: () => void S(), children: "重新扫描" }, "refresh"),
        /* @__PURE__ */ o(R, { type: "primary", onClick: t, children: "关闭" }, "close")
      ],
      children: /* @__PURE__ */ m(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ m(_, { children: [
          /* @__PURE__ */ m(R, { type: "primary", loading: w, onClick: () => void S(), children: [
            "扫描最近 ",
            d,
            " 天"
          ] }),
          /* @__PURE__ */ m(E.Text, { type: "secondary", children: [
            "资源：",
            a || "未选择"
          ] })
        ] }),
        c && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: c }),
        i && /* @__PURE__ */ m(xe, { children: [
          /* @__PURE__ */ m(E.Text, { children: [
            "发现 ",
            i.summary.discovered,
            " 个文件；已完成 ",
            i.summary.alreadyComplete,
            "； 待采集 ",
            i.summary.needsCollection,
            "；重复风险 ",
            i.summary.duplicateRisk,
            "。"
          ] }),
          /* @__PURE__ */ o(
            se,
            {
              rowKey: "path",
              dataSource: i.files,
              pagination: !1,
              size: "small",
              columns: [
                { title: "文件", dataIndex: "path" },
                { title: "源日期", dataIndex: "sourceDate" },
                { title: "账本", dataIndex: "inLedger", render: (b) => b ? "已登记" : "新文件" },
                { title: "当前目标", dataIndex: "expectedTargets" },
                { title: "成功目标", dataIndex: "succeededTargets" },
                { title: "待补目标", dataIndex: "missingTargets", render: (b) => b || "—" },
                { title: "未完成目标", dataIndex: "incompleteTargets" },
                {
                  title: "预计动作",
                  render: (b, h) => h.wouldSkip ? "跳过" : h.duplicateRisk ? "仅补缺失目标" : "采集"
                }
              ]
            }
          )
        ] })
      ] })
    }
  );
}
const { Header: br, Content: Ir } = bt;
function xr({ ctx: e }) {
  const [t, r] = It.useMessage(), [n, a] = v([]), [d, i] = v([]), [s, c] = v([]), [u, w] = v([]), [I, S] = v(""), [b, h] = v(1), [p, N] = v(!0), [A, D] = v([]), [x, O] = v(() => _e()), [j, g] = v(null), [k, T] = v([]), [te, de] = v(!1), [ye, C] = v(!0), [q, V] = v(!1), [ue, F] = v(!1), [re, ut] = v(null), [je, Le] = v(!1), [Fe, qe] = v(!1), Te = ie(1), Me = ie(e);
  Me.current = e;
  const Ce = ie(x.trigger);
  J(() => {
    Ce.current = x.trigger;
  }, [x.trigger]), J(() => {
    let y = !0;
    const P = Me.current.hubQuery;
    return Promise.allSettled([
      P("industrial/nodes"),
      P("industrial/workflows"),
      P("industrial/resources")
    ]).then(([G, M, W]) => {
      var oe;
      if (!y) return;
      const Q = [];
      if (G.status === "fulfilled" ? a(rr(G.value)) : Q.push(`节点描述: ${String(G.reason)}`), M.status === "fulfilled") {
        const Y = lt(M.value);
        i(Y);
        const ne = new URLSearchParams(window.location.search).get("workflowId"), Z = ne && Y.some((le) => le.workflowId === ne) ? ne : (oe = Y[0]) == null ? void 0 : oe.workflowId;
        Z && Be(Z);
      } else Q.push(`工作流: ${String(M.reason)}`);
      if (W.status === "fulfilled") {
        const Y = nr(W.value);
        w(Y);
        const ne = Y.map((Z) => Z.id);
        c(ne), S((Z) => Z || ne[0] || "");
      } else Q.push(`资源: ${String(W.reason)}`);
      D(Q);
    }).finally(() => {
      y && N(!1);
    }), () => {
      y = !1;
    };
  }, []);
  const ae = Xe(
    () => new Map(n.map((y) => [y.nodeTypeId, y])),
    [n]
  ), Ne = x.nodes.find((y) => y.id === j) ?? null, me = z(async (y, P, G) => {
    de(!0);
    try {
      return await X().api.hubCommand(y, P), t.success(G), !0;
    } catch (M) {
      return t.error(`${y} failed: ${M instanceof Error ? M.message : String(M)}`), !1;
    } finally {
      de(!1);
    }
  }, [t]), Be = z(async (y) => {
    try {
      const P = await e.hubQuery("industrial/workflow-definitions"), M = (ee(P) ?? []).find((oe) => {
        const Y = oe;
        return String((Y == null ? void 0 : Y.workflowId) ?? "") === y;
      }), W = M === void 0 ? { ..._e(y), name: y } : ir(M);
      O(W), h(Number(W.layout.lookbackDays ?? 1) || 1);
      const Q = W.nodes.map((oe) => /^n(\d+)$/.exec(oe.id)).filter(Boolean).reduce((oe, Y) => Math.max(oe, Number(Y[1])), 0);
      Te.current = Q + 1, g(null), V(!1), T([]), F(!1);
    } catch (P) {
      t.error(`加载工作流失败: ${P instanceof Error ? P.message : String(P)}`);
    }
  }, [e, t]), be = z(async () => {
    try {
      const y = fr(await e.hubQuery("industrial/execution"));
      if (y === null) return;
      ut(y);
    } catch {
    }
  }, [e]), pt = z(async () => {
    var G;
    const y = new Set(x.resourceReferences ?? []);
    for (const M of x.nodes)
      for (const W of ["resourceId", "targetResourceId"]) {
        const Q = (G = M.config) == null ? void 0 : G[W];
        typeof Q == "string" && Q.trim() && y.add(Q.trim());
      }
    const P = {
      ...x,
      trigger: Ce.current,
      resourceReferences: [...y],
      layout: { ...x.layout, lookbackDays: b }
    };
    await me("industrial/save-workflow", P, "已保存") && (O(P), F(!1));
  }, [me, x, b]), ft = z(async () => {
    if (ue) {
      t.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await me("industrial/publish-workflow", { workflowId: x.workflowId }, "已发布") && await be();
  }, [me, x.workflowId, ue, t, be]), gt = z(async () => {
    if (ue) {
      t.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await me(
      "industrial/run-workflow",
      { workflowId: x.workflowId, resourceId: I },
      "采集已启动"
    ) && await be();
  }, [me, x.workflowId, ue, t, be, I]), Ee = z((y, P, G) => {
    const M = fe(ae, y);
    if (!M) return;
    const W = `n${Te.current++}`;
    O((Q) => ({
      ...Q,
      nodes: [...Q.nodes, Rt(M, W, P, G)]
    })), g(W), V(!0), T([]), F(!0);
  }, [ae]), yt = z((y) => {
    const P = Te.current - 1;
    Ee(y, 80 + P % 3 * 190, 80 + Math.floor(P / 3) * 130);
  }, [Ee]), Ke = z((y, P) => {
    const G = x.nodes.find((le) => le.id === y), M = x.nodes.find((le) => le.id === P);
    if (!G || !M)
      return t.warning("连线失败：端点不在画布上"), null;
    const W = fe(ae, G.type), Q = fe(ae, M.type);
    if (!W || !Q) return null;
    let oe = G, Y = M, ne = Je(W, Q);
    if (!ne) {
      const le = Je(Q, W);
      if (!le)
        return t.warning(`端口类型不兼容：${W.name} → ${Q.name}`), null;
      oe = M, Y = G, ne = le;
    }
    const Z = Ot(x, {
      source: oe.id,
      sourcePort: ne.sourcePort,
      target: Y.id,
      targetPort: ne.targetPort
    }, ae);
    return Z.issue ? (t.warning(`${Z.issue.code}: ${Z.issue.message}`), T((le) => [...le, Z.issue]), null) : Z.definition === x ? (t.info("这条连线已经存在"), null) : (O(Z.definition), F(!0), ne);
  }, [ae, x, t]), mt = z(() => {
    const y = Mt(x, ae, new Set(s));
    T(y), y.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : (t.warning(`发现 ${y.length} 个问题，见属性抽屉定位列表`), V(!0));
  }, [x, ae, s, t]), ht = z((y, P) => {
    O((G) => ({
      ...G,
      edges: G.edges.map((M, W) => W === y ? P : M)
    })), F(!0);
  }, []);
  return p ? /* @__PURE__ */ o(tt, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ m("div", { className: "industrial-designer", children: [
    r,
    /* @__PURE__ */ m(br, { className: "industrial-header", children: [
      /* @__PURE__ */ m("div", { className: "industrial-title", children: [
        /* @__PURE__ */ o(E.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ o(E.Text, { type: "secondary", children: x.name || x.workflowId }),
        ue && /* @__PURE__ */ o(ce, { color: "orange", children: "未保存" }),
        re && /* @__PURE__ */ o(ce, { color: re.error ? "error" : "success", children: re.status })
      ] }),
      /* @__PURE__ */ m("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ o(
          R,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": ye,
            onClick: () => C((y) => !y),
            children: ye ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ o(
          R,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => V((y) => !y),
            children: "属性"
          }
        ),
        /* @__PURE__ */ o(R, { size: "small", "data-testid": "open-trigger", onClick: () => Le(!0), children: "触发器" }),
        /* @__PURE__ */ o(
          R,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => qe(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ o("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ m(_, { size: 4, children: [
          /* @__PURE__ */ o(R, { size: "small", loading: te, onClick: () => void pt(), children: "保存" }),
          /* @__PURE__ */ o(R, { size: "small", onClick: mt, children: "校验" }),
          /* @__PURE__ */ o(R, { size: "small", loading: te, onClick: () => void ft(), children: "发布" }),
          /* @__PURE__ */ o(R, { size: "small", type: "primary", loading: te, onClick: () => void gt(), children: "运行" })
        ] }),
        /* @__PURE__ */ m(
          "select",
          {
            className: "industrial-resource-select",
            "aria-label": "运行资源",
            value: I,
            onChange: (y) => S(y.target.value),
            children: [
              /* @__PURE__ */ o("option", { value: "", children: "运行资源" }),
              s.map((y) => /* @__PURE__ */ o("option", { value: y, children: y }, y))
            ]
          }
        ),
        /* @__PURE__ */ o(
          et,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: b,
            onChange: (y) => {
              h(Number(y) || 1), F(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ m(Ir, { className: `industrial-body${ye ? "" : " industrial-body--collapsed"}`, children: [
      ye ? /* @__PURE__ */ m("aside", { className: "industrial-palette", children: [
        d.length > 0 && /* @__PURE__ */ o(
          ge,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: x.workflowId,
            placeholder: "选择工作流",
            onChange: (y) => void Be(y),
            options: d.map((y) => ({ value: y.workflowId, label: y.name }))
          }
        ),
        /* @__PURE__ */ o(Dt, { descriptors: n, loading: !1, onAdd: yt })
      ] }) : /* @__PURE__ */ o("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ o(R, { type: "text", size: "small", onClick: () => C(!0), children: "节点库" }) }),
      /* @__PURE__ */ m("section", { className: "industrial-center", children: [
        A.length > 0 && /* @__PURE__ */ o(
          pe,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: A.join("；")
          }
        ),
        /* @__PURE__ */ o(
          Pt,
          {
            definition: x,
            catalog: ae,
            selectedId: j,
            onSelect: (y) => {
              g(y), y && V(!0);
            },
            onDropNode: Ee,
            onCreateEdge: Ke,
            onPositions: (y) => {
              O((P) => Ft(P, y)), F(!0);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ m(
      vt,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: q,
        mask: !1,
        getContainer: !1,
        onClose: () => V(!1),
        children: [
          re && /* @__PURE__ */ o(
            pe,
            {
              style: { marginBottom: 12 },
              type: re.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${re.status}`,
              description: /* @__PURE__ */ m(_, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                re.error && /* @__PURE__ */ o("span", { children: re.error }),
                re.executed.length > 0 && /* @__PURE__ */ m("span", { children: [
                  "已执行节点: ",
                  re.executed.join(", ")
                ] }),
                re.nodes.length > 0 && /* @__PURE__ */ o(
                  se,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: re.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (y, P) => P.error ?? (P.samples.length > 0 ? P.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ o(
            Yt,
            {
              node: Ne,
              descriptor: Ne ? fe(ae, Ne.type) ?? null : null,
              nodes: x.nodes,
              edges: x.edges,
              catalog: ae,
              resources: u,
              issues: k,
              onApplyConfig: (y) => {
                j && (O((P) => Lt(P, j, y)), F(!0));
              },
              onDeleteNode: () => {
                O((y) => zt(y, j ?? "")), g(null), F(!0);
              },
              onDeleteEdge: (y) => {
                O((P) => At(P, y)), F(!0);
              },
              onCreateEdge: (y, P) => void Ke(y, P),
              onUpdateEdge: ht,
              onSelectNode: (y) => {
                g(y), V(!0);
              }
            }
          )
        ]
      }
    ),
    je && /* @__PURE__ */ o(
      mr,
      {
        open: je,
        onClose: () => Le(!1),
        trigger: x.trigger,
        resourceIds: s,
        workflowId: x.workflowId,
        ctx: e,
        onSave: (y) => {
          Ce.current = y, O((P) => ({ ...P, trigger: y })), F(!0);
        }
      }
    ),
    Fe && /* @__PURE__ */ o(
      vr,
      {
        open: Fe,
        onClose: () => qe(!1),
        ctx: e,
        workflowId: x.workflowId,
        resourceId: I,
        lookbackDays: b
      }
    )
  ] });
}
function kr({ block: e, ctx: t }) {
  const [r, n] = v([]), [a, d] = v(!1), [i, s] = v(""), [c, u] = v(null), [w, I] = v(!1), S = z(async () => {
    try {
      n(lt(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
    } catch (p) {
      u(p instanceof Error ? p.message : String(p));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    S();
  }, [S]);
  const b = z(async () => {
    var N;
    const p = i.trim();
    if (!p) {
      u("请输入工作流名称");
      return;
    }
    I(!0), u(null);
    try {
      const D = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await t.hubCommand("industrial/save-workflow", {
        ..._e(D),
        name: p,
        layout: { lookbackDays: 1 }
      }), d(!1), s(""), await S(), (N = t.navigate) == null || N.call(t, `/industrial/designer?workflowId=${encodeURIComponent(D)}`);
    } catch (A) {
      u(A instanceof Error ? A.message : String(A));
    } finally {
      I(!1);
    }
  }, [t, S, i]), h = z((p) => {
    var N;
    (N = t.navigate) == null || N.call(t, `/industrial/designer?workflowId=${encodeURIComponent(p)}`);
  }, [t]);
  return /* @__PURE__ */ m(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ m(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ o(E.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ o(R, { type: "primary", onClick: () => d(!0), children: "新建工作流" })
    ] }),
    c && /* @__PURE__ */ o(E.Text, { type: "danger", children: c }),
    /* @__PURE__ */ o(
      se,
      {
        rowKey: "workflowId",
        dataSource: r,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (p) => /* @__PURE__ */ o(R, { type: "link", style: { padding: 0 }, onClick: () => h(p), children: p })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ o(
      ve,
      {
        open: a,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: w,
        onCancel: () => d(!1),
        onOk: () => void b(),
        children: /* @__PURE__ */ m(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(E.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ o(
            K,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: i,
              onChange: (p) => s(p.target.value ?? "")
            }
          ),
          c && /* @__PURE__ */ o(E.Text, { type: "danger", children: c })
        ] })
      }
    )
  ] });
}
const De = {
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
}, Sr = [
  { value: "filesystem.local", label: "本地目录" },
  { value: "filesystem.unc", label: "Windows UNC 共享" },
  { value: "sqlserver", label: "SQL Server" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mysql", label: "MySQL" },
  { value: "sqlite", label: "SQLite" }
];
function Tr(e) {
  const t = { ...e.config }, r = (n, a) => {
    a.trim() && (t[n] = a);
  };
  switch (e.type) {
    case "filesystem.local":
    case "filesystem.unc":
      r("root", e.root);
      break;
    case "sqlserver":
      r("server", e.host), r("host", e.host), r("database", e.database), r("table", e.table || "IndustrialRecords");
      break;
    case "postgresql":
    case "mysql":
      r("host", e.host), r("database", e.database), r("port", e.port || (e.type === "mysql" ? "3306" : "5432"));
      break;
    case "sqlite":
      r("database", e.database);
      break;
  }
  return t;
}
function Cr({ block: e, ctx: t }) {
  const [r, n] = v([]), [a, d] = v([]), [i, s] = v([]), [c, u] = v(De), [w, I] = v(!1), [S, b] = v(null), [h, p] = v(!1), N = z(async () => {
    try {
      const [g, k, T] = await Promise.all([
        t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        t.hubQuery("industrial/credentials"),
        t.hubQuery("industrial/resource-tests")
      ]);
      n(sr(g)), d(ct(k)), s(lr(T));
    } catch (g) {
      b(g instanceof Error ? g.message : String(g));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    N();
  }, [N]);
  const A = z((g) => {
    const k = (...T) => {
      for (const te of T) {
        const de = g.config[te];
        if (de != null && String(de).trim()) return String(de);
      }
      return "";
    };
    u({
      id: g.id,
      name: g.name,
      type: g.type,
      provider: g.provider,
      root: k("root", "path"),
      host: k("server", "host", "dataSource", "data_source"),
      database: k("database", "initialCatalog", "initial_catalog", "path"),
      port: k("port"),
      table: k("table"),
      credentialId: g.credentialId ?? "",
      enabled: g.enabled,
      config: g.config
    }), I(!0);
  }, []), D = z(async () => {
    const g = c.id.trim();
    if (!g) {
      b("请输入资源 ID");
      return;
    }
    p(!0), b(null);
    try {
      await t.hubCommand("industrial/save-resource", {
        id: g,
        name: c.name || g,
        type: c.type,
        provider: c.provider || "manual",
        config: Tr(c),
        credentialId: c.credentialId || null,
        enabled: c.enabled
      }), I(!1), u(De), await N();
    } catch (k) {
      b(k instanceof Error ? k.message : String(k));
    } finally {
      p(!1);
    }
  }, [t, c, N]), x = z(async (g) => {
    b(null);
    try {
      await t.hubCommand("industrial/test-resource", { id: g });
    } catch (k) {
      b(k instanceof Error ? k.message : String(k));
    }
    await N();
  }, [t, N]), O = z(async (g) => {
    b(null);
    try {
      await t.hubCommand("industrial/delete-resource", { id: g }), await N();
    } catch (k) {
      b(k instanceof Error ? k.message : String(k));
    }
  }, [t, N]), j = new Map(i.map((g) => [g.resourceId, g]));
  return /* @__PURE__ */ m(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ m(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ o(E.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ o(R, { type: "primary", onClick: () => {
        u(De), I(!0);
      }, children: "新建资源" })
    ] }),
    S && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: S }),
    /* @__PURE__ */ o(
      se,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        columns: [
          { title: "ID", dataIndex: "id" },
          { title: "名称", dataIndex: "name" },
          { title: "类型", dataIndex: "type" },
          { title: "凭证", dataIndex: "credentialName", render: (g) => g ?? "—" },
          {
            title: "连接测试",
            key: "test",
            render: (g, k) => {
              const T = j.get(k.id);
              return T ? /* @__PURE__ */ o(E.Text, { type: T.ok ? "success" : "danger", children: T.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (g, k) => /* @__PURE__ */ m(_, { size: 4, children: [
              /* @__PURE__ */ o(R, { size: "small", onClick: () => void x(k.id), children: "测试连接" }),
              /* @__PURE__ */ o(R, { size: "small", onClick: () => A(k), children: "编辑" }),
              /* @__PURE__ */ o(R, { size: "small", danger: !0, onClick: () => void O(k.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ o(
      ve,
      {
        open: w,
        title: c.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: h,
        onCancel: () => I(!1),
        onOk: () => void D(),
        children: /* @__PURE__ */ m(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(
            K,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: c.id,
              onChange: (g) => u({ ...c, id: g.target.value })
            }
          ),
          /* @__PURE__ */ o(
            K,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: c.name,
              onChange: (g) => u({ ...c, name: g.target.value })
            }
          ),
          /* @__PURE__ */ o(E.Text, { children: "资源类型" }),
          /* @__PURE__ */ o(_, { wrap: !0, children: Sr.map((g) => /* @__PURE__ */ o(
            R,
            {
              size: "small",
              type: c.type === g.value ? "primary" : "default",
              onClick: () => u({ ...c, type: g.value }),
              children: g.label
            },
            g.value
          )) }),
          (c.type === "filesystem.local" || c.type === "filesystem.unc") && /* @__PURE__ */ o(
            K,
            {
              "aria-label": "目录路径",
              placeholder: c.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: c.root,
              onChange: (g) => u({ ...c, root: g.target.value })
            }
          ),
          (c.type === "sqlserver" || c.type === "postgresql" || c.type === "mysql") && /* @__PURE__ */ m(_, { children: [
            /* @__PURE__ */ o(
              K,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: c.host,
                onChange: (g) => u({ ...c, host: g.target.value })
              }
            ),
            /* @__PURE__ */ o(
              K,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: c.database,
                onChange: (g) => u({ ...c, database: g.target.value })
              }
            ),
            /* @__PURE__ */ o(
              K,
              {
                "aria-label": "端口",
                placeholder: "端口",
                value: c.port,
                onChange: (g) => u({ ...c, port: g.target.value })
              }
            )
          ] }),
          c.type === "sqlite" && /* @__PURE__ */ o(
            K,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: c.database,
              onChange: (g) => u({ ...c, database: g.target.value })
            }
          ),
          /* @__PURE__ */ o(
            ge,
            {
              "aria-label": "凭证",
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: c.credentialId || void 0,
              options: a.map((g) => ({ value: g.id, label: g.name })),
              onChange: (g) => u({ ...c, credentialId: g ?? "" })
            }
          ),
          /* @__PURE__ */ m(_, { children: [
            /* @__PURE__ */ o(
              rt,
              {
                "aria-label": "启用资源",
                checked: c.enabled,
                onChange: (g) => u({ ...c, enabled: g })
              }
            ),
            /* @__PURE__ */ o(E.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
function Nr({ block: e, ctx: t }) {
  const [r, n] = v([]), [a, d] = v(!1), [i, s] = v(""), [c, u] = v(""), [w, I] = v("username-password"), [S, b] = v(""), [h, p] = v(!1), [N, A] = v(null), [D, x] = v(!1), O = z(async () => {
    try {
      n(ct(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
    } catch (T) {
      A(T instanceof Error ? T.message : String(T));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    O();
  }, [O]);
  const j = z(() => {
    s(""), u(""), I("username-password"), b(""), p(!1), d(!0);
  }, []), g = z((T) => {
    s(T.id), u(T.name), I(T.kind), b(""), p(!0), d(!0);
  }, []), k = z(async () => {
    if (!i.trim()) {
      A("请输入凭证 ID");
      return;
    }
    if (!S.trim()) {
      A("请输入凭证内容");
      return;
    }
    x(!0), A(null);
    try {
      h ? await t.hubCommand("industrial/rotate-credential", { id: i, secret: S }) : await t.hubCommand("industrial/save-credential", { id: i, name: c || i, kind: w, secret: S }), d(!1), await O();
    } catch (T) {
      A(T instanceof Error ? T.message : String(T));
    } finally {
      x(!1);
    }
  }, [t, i, w, O, c, h, S]);
  return /* @__PURE__ */ m(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ m(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ o(E.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ o(R, { type: "primary", onClick: j, children: "新建凭证" })
    ] }),
    N && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: N }),
    /* @__PURE__ */ o(
      se,
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
            render: (T, te) => /* @__PURE__ */ o(R, { size: "small", onClick: () => g(te), children: "轮换" })
          }
        ]
      }
    ),
    /* @__PURE__ */ o(
      ve,
      {
        open: a,
        title: h ? "轮换凭证" : "新建凭证",
        okText: h ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: D,
        onCancel: () => d(!1),
        onOk: () => void k(),
        children: /* @__PURE__ */ m(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(
            K,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: i,
              disabled: h,
              onChange: (T) => s(T.target.value)
            }
          ),
          !h && /* @__PURE__ */ o(
            K,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: c,
              onChange: (T) => u(T.target.value)
            }
          ),
          /* @__PURE__ */ o(
            K,
            {
              "aria-label": "凭证内容",
              placeholder: h ? "新凭证内容" : "凭证内容",
              value: S,
              onChange: (T) => b(T.target.value)
            }
          ),
          N && /* @__PURE__ */ o(E.Text, { type: "danger", children: N })
        ] })
      }
    )
  ] });
}
const Ve = (e) => e === "Succeeded" || e === "Completed" ? "success" : e === "Failed" || e === "PartialFailure" ? "error" : e === "Running" || e === "Interrupted" ? "processing" : "default";
function Er({ block: e, ctx: t }) {
  const [r, n] = v([]), [a, d] = v([]), [i, s] = v(null), [c, u] = v(null), [w, I] = v(!1), S = z(async () => {
    try {
      const h = pr(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/execution-center"));
      n(h.runs), d(h.deliveries);
    } catch (h) {
      s(h instanceof Error ? h.message : String(h));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    S();
  }, [S]);
  const b = z(async (h) => {
    I(!0), s(null), u(null);
    try {
      await t.hubCommand("industrial/retry-failed", {
        deliveryId: h.id,
        targetId: h.targetId
      }), u(`已提交目标 ${h.targetId} 的重试`), await S();
    } catch (p) {
      s(p instanceof Error ? p.message : String(p));
    } finally {
      I(!1);
    }
  }, [t, S]);
  return /* @__PURE__ */ m(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ o(E.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    i && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: i }),
    c && /* @__PURE__ */ o(pe, { type: "success", showIcon: !0, message: c }),
    /* @__PURE__ */ o(
      se,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (h) => /* @__PURE__ */ o(ce, { color: Ve(h), children: h }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (h) => h ?? "—" },
          { title: "错误", dataIndex: "error", render: (h) => h ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ o(
      se,
      {
        rowKey: "id",
        dataSource: a,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (h) => /* @__PURE__ */ o(ce, { color: Ve(h), children: h }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (h, p) => p.diagnosis ? /* @__PURE__ */ m(_, { direction: "vertical", size: 0, children: [
              /* @__PURE__ */ o(E.Text, { children: p.diagnosis.message }),
              /* @__PURE__ */ o(E.Text, { type: "secondary", children: p.diagnosis.action })
            ] }) : p.error ?? "—"
          },
          {
            title: "操作",
            key: "actions",
            render: (h, p) => /* @__PURE__ */ o(
              R,
              {
                size: "small",
                disabled: w || p.status !== "Failed" && p.status !== "Interrupted",
                onClick: () => void b(p),
                children: "重试此目标"
              }
            )
          }
        ]
      }
    )
  ] });
}
function Pr({ block: e, ctx: t }) {
  const [r, n] = v([]), [a, d] = v(null), i = z(async () => {
    try {
      n(ur(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (c) {
      d(c instanceof Error ? c.message : String(c));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    i();
  }, [i]);
  const s = (c) => /* @__PURE__ */ o(
    se,
    {
      rowKey: "id",
      dataSource: c.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (u) => /* @__PURE__ */ o(ce, { children: u }) },
        { title: "记录", dataIndex: "records" },
        {
          title: "接受/拒绝/警告",
          key: "counts",
          render: (u, w) => `${w.accepted} / ${w.rejected} / ${w.warnings}`
        },
        {
          title: "原因与下一步",
          key: "diagnosis",
          render: (u, w) => w.diagnosis ? /* @__PURE__ */ m(_, { direction: "vertical", size: 0, children: [
            /* @__PURE__ */ o(E.Text, { children: w.diagnosis.message }),
            /* @__PURE__ */ o(E.Text, { type: "secondary", children: w.diagnosis.action }),
            w.error && /* @__PURE__ */ o(E.Text, { type: "secondary", children: w.error })
          ] }) : w.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ m(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ o(E.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    a && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: a }),
    /* @__PURE__ */ o(
      se,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        expandable: { expandedRowRender: s },
        columns: [
          { title: "文件", dataIndex: "path" },
          { title: "源日期", dataIndex: "sourceDate" },
          { title: "状态", dataIndex: "status" },
          { title: "大小", dataIndex: "length", render: (c) => `${c} B` },
          {
            title: "目标数",
            key: "targets",
            render: (c, u) => u.deliveries.length
          }
        ]
      }
    )
  ] });
}
const Dr = `
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
function $r(e) {
  wt(e);
  const t = typeof document > "u" ? null : document.createElement("style");
  t && (t.dataset.industrialDesigner = "true", t.textContent = Dr, document.head.appendChild(t));
  const r = ({ ctx: d }) => $e(xr, { ctx: d });
  r.displayName = "IndustrialWorkflowCanvas";
  const n = (d, i) => {
    const s = ({ block: c, ctx: u }) => $e(i, { block: c, ctx: u });
    return s.displayName = d, e.blocks.register(d, s);
  }, a = [
    e.blocks.register("industrial-workflow-canvas", r),
    n("industrial-workflow-list", kr),
    n("industrial-resource-console", Cr),
    n("industrial-credential-console", Nr),
    n("industrial-execution-center", Er),
    n("industrial-file-details", Pr)
  ];
  return () => {
    for (const d of a) d == null || d();
    t == null || t.remove();
  };
}
export {
  $r as default,
  $r as register
};
