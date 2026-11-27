let Se = null;
function bt(e) {
  Se = e;
}
function re() {
  if (!Se && typeof globalThis < "u" && (Se = globalThis.__CORDIS_CONSOLE ?? null), !Se)
    throw new Error("[industrial-designer] console facade is not available");
  return Se;
}
const i = (...e) => re().jsxRuntime.jsx(...e), h = (...e) => re().jsxRuntime.jsxs(...e), Ee = (e) => re().jsxRuntime.jsx(re().jsxRuntime.Fragment, e), Fe = (...e) => re().React.createElement(...e), z = (...e) => re().React.useCallback(...e), W = (...e) => re().React.useEffect(...e), Me = (...e) => re().React.useMemo(...e), ce = (...e) => re().React.useRef(...e), m = (...e) => re().React.useState(...e), L = (e) => {
  const t = (r) => {
    const n = e.split(".").reduce((a, d) => a == null ? void 0 : a[d], re().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Fe(n, r ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, fe = L("Alert"), R = L("Button"), Qe = L("Divider"), xt = L("Drawer"), tt = L("Empty"), je = Object.assign(L("Form"), {
  Item: L("Form.Item"),
  useForm: (...e) => re().antd.Form.useForm(...e)
}), M = Object.assign(L("Input"), {
  Number: L("InputNumber"),
  TextArea: L("Input.TextArea")
}), rt = L("InputNumber"), kt = Object.assign(L("Layout"), {
  Header: L("Layout.Header"),
  Content: L("Layout.Content")
}), Te = L("Modal"), pe = L("Select"), O = L("Space"), nt = L("Spin"), at = L("Switch"), ie = L("Table"), ye = L("Tag"), E = Object.assign(L("Typography"), {
  Title: L("Typography.Title"),
  Text: L("Typography.Text")
}), St = new Proxy({}, {
  get: (e, t) => (...r) => {
    var a;
    const n = (a = re().antd.message) == null ? void 0 : a[t];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${t} unavailable`);
    return n(...r);
  }
}), it = 148, st = 46, We = {
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
}, Je = (e) => We[e] ?? We[e.split(".")[0]] ?? "#4d6bfe";
function Tt(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function Ct(e, t = /* @__PURE__ */ new Map(), r) {
  return {
    nodes: e.nodes.map((n) => {
      const a = t.get(n.type), d = (a == null ? void 0 : a.category) ?? "General", s = r === n.id;
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
          fill: Je(d),
          stroke: s ? "#111111" : Je(d),
          lineWidth: s ? 3 : 1.5,
          radius: 10,
          port: !0,
          ports: [
            ...((a == null ? void 0 : a.inputs) ?? []).map((o, c, f) => ({
              key: `in:${o.name}`,
              placement: [0, (c + 1) / (f.length + 1)],
              r: 5,
              stroke: o.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((a == null ? void 0 : a.outputs) ?? []).map((o, c, f) => ({
              key: `out:${o.name}`,
              placement: [1, (c + 1) / (f.length + 1)],
              r: 5,
              stroke: o.type === "Control" ? "#52c41a" : "#1677ff",
              fill: "#fff"
            }))
          ]
        }
      };
    }),
    edges: e.edges.map((n) => ({
      id: Tt(n),
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
function Nt(e, t, r) {
  const n = r && r.length > 0 ? new Set(r) : null, a = {};
  for (const d of e.nodes) {
    if (!t.has(d.id) || n && !n.has(d.id)) continue;
    const s = d.style.x, o = d.style.y;
    typeof s == "number" && typeof o == "number" && (a[d.id] = { x: s, y: o });
  }
  return a;
}
function Et(e, t) {
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
        size: [it, st],
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
const $t = "__industrial_drag_target__", Dt = "__industrial_connect_source__", ze = (e, t) => {
  var r;
  return (((r = e.isPanModifier) == null ? void 0 : r.call(e)) ?? !1) || t.button === 1 || t.buttons === 4;
}, Ye = (e, t) => {
  var w, b;
  if (t.targetType !== "node" || !((w = t.target) != null && w.id)) return !1;
  const r = ((b = e.gestureOrigin) == null ? void 0 : b.call(e)) ?? t.client ?? t.canvas;
  if (!r || !e.graph) return !1;
  const n = String(t.target.id), a = e.graph.getNodeData().find((k) => String(k.id) === n), d = a == null ? void 0 : a.style;
  if (typeof (d == null ? void 0 : d.x) != "number" || typeof d.y != "number") return !1;
  const [s, o] = e.graph.getCanvasByClient([r.x, r.y]), c = Math.abs(s - d.x), f = Math.abs(o - d.y);
  return Math.abs(c - it / 2) <= 14 && f <= st / 2 + 10;
};
function ot(e = {}) {
  const t = e.connectionState ?? { armed: !1 };
  return [
    "zoom-canvas",
    {
      type: "drag-canvas",
      enable: (r) => r.targetType === "canvas" || ze(e, r),
      range: 0.9
    },
    {
      type: "drag-element",
      state: $t,
      enable: (r) => (r.targetType === "node" || r.targetType === "combo") && !Ye(e, r) && !ze(e, r),
      onFinish: (r) => {
        var s, o;
        const n = e.graph;
        if (!n) return;
        const a = Array.isArray(r) ? r : r ? [String(r)] : void 0, d = n.getNodeData().map((c) => ({
          id: String(c.id),
          data: c.data ?? {},
          style: c.style ?? {}
        }));
        (o = e.onPositions) == null || o.call(e, Nt(
          { nodes: d },
          ((s = e.allowedIds) == null ? void 0 : s.call(e)) ?? /* @__PURE__ */ new Set(),
          a
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: Dt,
      enable: (r) => ze(e, r) ? (t.armed = !1, !1) : t.armed ? r.targetType === "node" || r.targetType === "combo" : Ye(e, r) ? (t.armed = !0, !0) : !1,
      onCreate: (r) => {
        var s;
        t.armed = !1;
        const n = String(r.source ?? ""), a = String(r.target ?? "");
        if (!n || !a || n === a) return;
        const d = (s = e.onCreateEdge) == null ? void 0 : s.call(e, n, a);
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
const Pt = (e) => {
  var r;
  const t = e;
  return (t == null ? void 0 : t.targetType) === "node" ? (r = t.target) == null ? void 0 : r.id : void 0;
};
function Rt(e) {
  const {
    definition: t,
    catalog: r,
    selectedId: n,
    onSelect: a,
    onDropNode: d,
    onCreateEdge: s,
    onPositions: o
  } = e, c = ce(null), f = ce(null), w = ce(!1), b = ce(!1), k = ce({ armed: !1 }), x = ce(null), y = ce({ onSelect: a, onDropNode: d, onCreateEdge: s, onPositions: o });
  y.current = { onSelect: a, onDropNode: d, onCreateEdge: s, onPositions: o };
  const u = Me(
    () => Ct(t, r, n),
    [t, r, n]
  ), S = ce(u);
  S.current = u;
  const $ = ce(/* @__PURE__ */ new Set());
  return $.current = new Set(t.nodes.map((I) => I.id)), W(() => {
    const I = (p) => {
      p.code === "Space" && !p.repeat && (b.current = !0);
    }, _ = (p) => {
      p.code === "Space" && (b.current = !1);
    }, A = () => {
      b.current = !1;
    };
    return window.addEventListener("keydown", I), window.addEventListener("keyup", _), window.addEventListener("blur", A), () => {
      window.removeEventListener("keydown", I), window.removeEventListener("keyup", _), window.removeEventListener("blur", A);
    };
  }, []), W(() => {
    const I = c.current;
    if (!I || w.current) return;
    let _ = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: A }) => {
      if (_ || !c.current || w.current) return;
      const p = Et(S.current, I), C = new A({
        ...p,
        behaviors: ot({
          graph: {
            getNodeData: () => {
              var T;
              return ((T = f.current) == null ? void 0 : T.getNodeData()) ?? [];
            },
            getCanvasByClient: (T) => {
              var ee;
              const U = ((ee = f.current) == null ? void 0 : ee.getCanvasByClient(T)) ?? T;
              return [U[0], U[1]];
            }
          },
          isPanModifier: () => b.current,
          gestureOrigin: () => x.current,
          connectionState: k.current,
          allowedIds: () => $.current,
          onPositions: (T) => y.current.onPositions(T),
          onCreateEdge: (T, U) => y.current.onCreateEdge(T, U)
        })
      });
      C.on("node:click", (T) => {
        const U = Pt(T);
        U && y.current.onSelect(U);
      }), C.on("edge:click", () => y.current.onSelect(null)), C.on("canvas:click", () => y.current.onSelect(null)), C.on("pointerdown", (T) => {
        const U = T;
        x.current = U.client ? { x: U.client.x, y: U.client.y } : null;
      }), C.on("pointerup", () => {
        queueMicrotask(() => {
          k.current.armed = !1, x.current = null;
        });
      }), f.current = C, I.__industrialGraph = C, w.current = !0, C.render();
    }), () => {
      _ = !0;
    };
  }, []), W(() => {
    const I = f.current;
    !I || !w.current || (I.setData(u), I.draw());
  }, [u]), W(() => () => {
    var I;
    (I = f.current) == null || I.destroy(), f.current = null, w.current = !1;
  }, []), /* @__PURE__ */ i("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ h("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ i(
      "div",
      {
        ref: c,
        className: "industrial-graph-host",
        onDragOver: (I) => I.preventDefault(),
        onDrop: (I) => {
          I.preventDefault();
          const _ = I.dataTransfer.getData("application/x-industrial-node") || I.dataTransfer.getData("text/plain");
          if (!_) return;
          const A = f.current, p = c.current;
          if (!A || !p) return;
          const C = p.getBoundingClientRect(), T = A.getCanvasByViewport([
            I.clientX - C.left,
            I.clientY - C.top
          ]);
          y.current.onDropNode(_, T[0], T[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ i("div", { className: "industrial-graph-hint", children: "拖动节点移动 · 从端口拖出连线 · 拖动画布平移（空格或中键可在节点上平移）" })
  ] }) });
}
function _t({ descriptors: e, loading: t, error: r, onAdd: n }) {
  const a = /* @__PURE__ */ new Map();
  for (const s of e) {
    const o = s.category.split(".")[0] || "General", c = a.get(o) ?? [];
    c.push(s), a.set(o, c);
  }
  const d = (s, o) => {
    s.dataTransfer.setData("application/x-industrial-node", o.nodeTypeId), s.dataTransfer.setData("text/plain", o.nodeTypeId), s.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ h("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ i(E.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ i(nt, { size: "small", style: { display: "block", marginTop: 16 } }),
    r && /* @__PURE__ */ i(E.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: r }),
    !t && !r && e.length === 0 && /* @__PURE__ */ i(tt, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(a.entries()).map(([s, o]) => /* @__PURE__ */ h("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ i(E.Text, { strong: !0, style: { fontSize: 12 }, children: s }),
      o.map((c) => /* @__PURE__ */ h(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          role: "button",
          tabIndex: 0,
          onDragStart: (f) => d(f, c),
          onClick: () => n == null ? void 0 : n(c.nodeTypeId),
          onKeyDown: (f) => {
            (f.key === "Enter" || f.key === " ") && (n == null || n(c.nodeTypeId));
          },
          title: c.description || c.nodeTypeId,
          children: [
            /* @__PURE__ */ i("div", { className: "industrial-palette-node-title", children: c.name }),
            /* @__PURE__ */ h("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ i(ye, { bordered: !1, color: "blue", children: c.version }),
              /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 11 }, children: c.nodeTypeId })
            ] }),
            /* @__PURE__ */ h("div", { className: "industrial-palette-node-ports", children: [
              c.inputs.map((f) => /* @__PURE__ */ h(ye, { bordered: !1, color: "default", children: [
                "in ",
                f.name,
                ": ",
                f.type
              ] }, `in-${f.name}`)),
              c.outputs.map((f) => /* @__PURE__ */ h(ye, { bordered: !1, color: "green", children: [
                "out ",
                f.name,
                ": ",
                f.type
              ] }, `out-${f.name}`))
            ] })
          ]
        },
        c.nodeTypeId
      ))
    ] }, s))
  ] });
}
const $e = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {}
});
function Ot(e) {
  const t = {};
  for (const [r, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (t[r] = n.default);
  return t;
}
function zt(e, t, r = 0, n = 0) {
  return { id: t, type: e.nodeTypeId, config: Ot(e), x: r, y: n };
}
function ve(e, t) {
  return e.get(t);
}
function De(e, t, r) {
  return e[r].find((n) => n.name === t);
}
function Ue(e, t) {
  return e !== "Unknown" && t !== "Unknown" && e === t;
}
function He(e, t) {
  for (const r of e.outputs)
    for (const n of t.inputs)
      if (Ue(r.type, n.type))
        return { sourcePort: r.name, targetPort: n.name };
  return null;
}
const Z = (e, t, r) => ({ path: e, code: t, message: r });
function At(e, t) {
  return e.source === t.source && e.sourcePort === t.sourcePort && e.target === t.target && e.targetPort === t.targetPort;
}
function Ft(e, t, r) {
  const n = `edges.${e.edges.length}`, a = e.nodes.find((w) => w.id === t.source), d = e.nodes.find((w) => w.id === t.target);
  if (!a || !d)
    return { definition: e, issue: Z(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((w) => At(w, t)))
    return { definition: e };
  const s = ve(r, a.type), o = ve(r, d.type);
  if (!s || !o)
    return { definition: e, issue: Z(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const c = De(s, t.sourcePort, "outputs"), f = De(o, t.targetPort, "inputs");
  return !c || !f ? { definition: e, issue: Z(n, "PORT_MISSING", "declared port does not exist") } : Ue(c.type, f.type) ? { definition: { ...e, edges: [...e.edges, t] } } : {
    definition: e,
    issue: Z(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${c.type} → ${f.type} is not a legal connection`
    )
  };
}
function jt(e, t) {
  return {
    ...e,
    nodes: e.nodes.filter((r) => r.id !== t),
    edges: e.edges.filter((r) => r.source !== t && r.target !== t)
  };
}
function qt(e, t) {
  return { ...e, edges: e.edges.filter((r, n) => n !== t) };
}
function Lt(e, t) {
  const r = { ...e };
  for (const [n, a] of Object.entries(t))
    a === void 0 ? delete r[n] : r[n] = a;
  return r;
}
function Mt(e, t, r) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === t ? { ...n, config: r } : n)
  };
}
function Ut(e, t) {
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
function Bt(e) {
  var d;
  const t = /* @__PURE__ */ new Map();
  for (const s of e.nodes) t.set(s.id, []);
  for (const s of e.edges)
    (d = t.get(s.source)) == null || d.push(s.target);
  const r = /* @__PURE__ */ new Map(), n = [], a = (s) => {
    r.set(s, 1), n.push(s);
    for (const o of t.get(s) ?? []) {
      if (r.get(o) === 1)
        return [...n.slice(n.indexOf(o)), o];
      if (r.get(o) !== 2) {
        const c = a(o);
        if (c) return c;
      }
    }
    return n.pop(), r.set(s, 2), null;
  };
  for (const s of e.nodes)
    if (r.get(s.id) !== 2) {
      const o = a(s.id);
      if (o) return o;
    }
  return null;
}
function Kt(e, t, r) {
  const n = [];
  e.workflowId.trim() || n.push(Z("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(Z("workflow", "INVALID_VERSION", "version must be positive"));
  const a = /* @__PURE__ */ new Map();
  for (const o of e.nodes)
    a.has(o.id) && n.push(Z(`nodes.${o.id}`, "DUPLICATE_NODE_ID", `duplicate node '${o.id}'`)), a.set(o.id, o);
  const d = /* @__PURE__ */ new Map();
  for (const o of e.nodes) {
    const c = ve(t, o.type);
    if (!c) {
      n.push(Z(
        `nodes.${o.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${o.type}' is not registered`
      ));
      continue;
    }
    d.set(o.id, c);
    const f = c.configurationSchema.required ?? [];
    for (const w of f) {
      const b = o.config[w];
      (b == null || b === "") && n.push(Z(
        `nodes.${o.id}.config.${w}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${w}' is missing`
      ));
    }
    r && typeof o.config.resourceId == "string" && (r.has(o.config.resourceId) || n.push(Z(
      `nodes.${o.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${o.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((o, c) => {
    const f = a.get(o.source), w = a.get(o.target);
    if (!f || !w) {
      n.push(Z(`edges.${c}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const b = d.get(o.source), k = d.get(o.target);
    if (!b || !k) return;
    const x = De(b, o.sourcePort, "outputs"), y = De(k, o.targetPort, "inputs");
    if (!x || !y) {
      n.push(Z(`edges.${c}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    Ue(x.type, y.type) || n.push(Z(
      `edges.${c}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${x.type} → ${y.type} is not a legal connection`
    ));
  });
  for (const o of e.nodes) {
    const c = d.get(o.id);
    if (c)
      for (const f of c.inputs.filter((w) => w.required))
        e.edges.some((w) => w.target === o.id && w.targetPort === f.name && a.has(w.source)) || n.push(Z(
          `nodes.${o.id}.inputs.${f.name}`,
          "MISSING_INPUT",
          `required input '${f.name}' is not connected`
        ));
  }
  const s = Bt(e);
  return s && n.push(Z(`nodes.${s[0]}`, "GRAPH_CYCLE", `cycle: ${s.join(" → ")}`)), n;
}
const qe = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function Gt(e) {
  const t = qe(e.additionalProperties).enum;
  if (!Array.isArray(t)) return null;
  const r = t.filter((n) => typeof n == "string");
  return r.length > 0 ? r : null;
}
function lt(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && qe(e.items).type === "string" ? "string-list" : e.type === "object" && qe(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function Le(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, t]) => t != null && typeof t != "object").map(([t, r]) => ({ key: t, value: String(r) }));
}
function Ve(e) {
  const t = {}, r = [];
  for (const n of e) {
    const a = n.key.trim();
    a && (Object.prototype.hasOwnProperty.call(t, a) && r.push(a), t[a] = n.value);
  }
  return { record: t, duplicateKeys: r };
}
function Ne(e) {
  if (!Array.isArray(e)) return [];
  const t = /* @__PURE__ */ new Set(), r = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const a = n.trim();
    !a || t.has(a) || (t.add(a), r.push(a));
  }
  return r;
}
function Qt(e, t) {
  const r = /* @__PURE__ */ new Set();
  for (const [n, a] of Object.entries(e))
    if (lt(a) === "key-value")
      for (const d of Le(t[n]))
        d.key.trim() && r.add(d.key.trim());
  return [...r];
}
function Wt({
  value: e,
  onChange: t,
  valueOptions: r,
  keyTitle: n = "目标字段",
  valueTitle: a = "来源字段",
  testId: d
}) {
  const s = JSON.stringify(e ?? {}), [o, c] = m(() => Le(e)), [f, w] = m(s);
  W(() => {
    s !== f && (c(Le(e)), w(s));
  }, [s, f, e]);
  const b = (x) => {
    c(x), t == null || t(Ve(x).record);
  }, { duplicateKeys: k } = Ve(o);
  return /* @__PURE__ */ h("div", { "data-testid": d, className: "industrial-kv-editor", children: [
    o.map((x, y) => /* @__PURE__ */ h("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ i(
        M,
        {
          "aria-label": `${n} ${y + 1}`,
          value: x.key,
          placeholder: n,
          onChange: (u) => b(o.map((S, $) => $ === y ? { ...S, key: u.target.value } : S))
        }
      ),
      r && r.length > 0 ? /* @__PURE__ */ i(
        pe,
        {
          "aria-label": `${a} ${y + 1}`,
          value: x.value || void 0,
          placeholder: a,
          options: r.map((u) => ({ label: u, value: u })),
          onChange: (u) => b(o.map((S, $) => $ === y ? { ...S, value: u ?? "" } : S))
        }
      ) : /* @__PURE__ */ i(
        M,
        {
          "aria-label": `${a} ${y + 1}`,
          value: x.value,
          placeholder: a,
          onChange: (u) => b(o.map((S, $) => $ === y ? { ...S, value: u.target.value } : S))
        }
      ),
      /* @__PURE__ */ i(
        R,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${y + 1}`,
          onClick: () => b(o.filter((u, S) => S !== y)),
          children: "删"
        }
      )
    ] }, y)),
    /* @__PURE__ */ i(
      R,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => b([...o, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    k.length > 0 && /* @__PURE__ */ h(E.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      k.join("、")
    ] })
  ] });
}
function Jt({ value: e, onChange: t, suggestions: r = [], testId: n }) {
  const a = JSON.stringify(Ne(e)), [d, s] = m(() => Ne(e)), [o, c] = m(a);
  W(() => {
    a !== o && (s(Ne(e)), c(a));
  }, [a, o, e]);
  const f = (w) => {
    s(w), t == null || t(Ne(w));
  };
  return /* @__PURE__ */ h("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ i("datalist", { id: `${n ?? "list"}-suggestions`, children: r.map((w) => /* @__PURE__ */ i("option", { value: w }, w)) }),
    d.map((w, b) => /* @__PURE__ */ h("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ i(
        M,
        {
          "aria-label": `字段 ${b + 1}`,
          value: w,
          list: `${n ?? "list"}-suggestions`,
          onChange: (k) => f(d.map((x, y) => y === b ? k.target.value : x))
        }
      ),
      /* @__PURE__ */ i(
        R,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${b + 1}`,
          onClick: () => f(d.filter((k, x) => x !== b)),
          children: "删"
        }
      )
    ] }, b)),
    /* @__PURE__ */ i(R, { size: "small", type: "dashed", block: !0, onClick: () => f([...d, ""]), children: "添加字段" }),
    d.length === 0 && /* @__PURE__ */ i(E.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Yt({ value: e, onChange: t }) {
  const r = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, a] = m(r), [d, s] = m(null);
  W(() => {
    a(r), s(null);
  }, [r]);
  const o = (c) => {
    a(c);
    try {
      const f = JSON.parse(c);
      s(null), t == null || t(f);
    } catch (f) {
      s(f.message);
    }
  };
  return /* @__PURE__ */ h("div", { children: [
    /* @__PURE__ */ i(M.TextArea, { rows: 5, value: n, onChange: (c) => o(c.target.value) }),
    d && /* @__PURE__ */ i(E.Text, { type: "danger", style: { fontSize: 11 }, children: d })
  ] });
}
function Ht({ value: e, onChange: t, resources: r, resourceType: n, label: a }) {
  const d = r.filter((o) => o.enabled !== !1 && (!n || o.type === n)).map((o) => ({
    value: o.id,
    label: `${o.name} (${o.type})`
  })), s = r.find((o) => o.id === e);
  return /* @__PURE__ */ h("div", { children: [
    /* @__PURE__ */ h(
      "select",
      {
        "aria-label": a,
        value: e ?? "",
        onChange: (o) => t == null ? void 0 : t(o.target.value || void 0),
        children: [
          /* @__PURE__ */ i("option", { value: "", children: "未选择" }),
          d.map((o) => /* @__PURE__ */ i("option", { value: o.value, children: o.label }, o.value))
        ]
      }
    ),
    (s == null ? void 0 : s.credentialId) && /* @__PURE__ */ h(E.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      s.credentialName ?? s.credentialId
    ] })
  ] });
}
function Vt({ name: e, schema: t, required: r, resources: n, resourceType: a, properties: d, config: s }) {
  let o, c = "value";
  switch (lt(t)) {
    case "resource":
      o = /* @__PURE__ */ i(
        Ht,
        {
          resources: n,
          resourceType: a,
          label: String(t.title ?? e)
        }
      );
      break;
    case "enum":
      o = /* @__PURE__ */ i(
        pe,
        {
          allowClear: !0,
          options: (t.enum ?? []).map((f) => ({ label: String(f), value: f }))
        }
      );
      break;
    case "number":
      o = /* @__PURE__ */ i(rt, { style: { width: "100%" } });
      break;
    case "boolean":
      o = /* @__PURE__ */ i(at, {}), c = "checked";
      break;
    case "key-value":
      o = /* @__PURE__ */ i(
        Wt,
        {
          testId: `field-${e}`,
          valueOptions: Gt(t) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      o = /* @__PURE__ */ i(Jt, { testId: `field-${e}`, suggestions: Qt(d, s) });
      break;
    case "json":
      o = /* @__PURE__ */ i(Yt, {});
      break;
    default:
      o = /* @__PURE__ */ i(M, { placeholder: t.description });
      break;
  }
  return /* @__PURE__ */ i(
    je.Item,
    {
      name: e,
      label: t.title ?? e,
      tooltip: t.description,
      valuePropName: c,
      rules: r && c === "value" ? [{ required: !0, message: `请填写 ${t.title ?? e}` }] : [],
      children: o
    },
    e
  );
}
function Xt(e) {
  const {
    node: t,
    descriptor: r,
    nodes: n,
    edges: a,
    catalog: d,
    resources: s,
    issues: o,
    onApplyConfig: c,
    onDeleteNode: f,
    onDeleteEdge: w,
    onCreateEdge: b,
    onUpdateEdge: k,
    onSelectNode: x
  } = e, [y] = je.useForm(), [u, S] = m("{}"), [$, H] = m(null), [I, _] = m(), [A, p] = m();
  W(() => {
    y.setFieldsValue((t == null ? void 0 : t.config) ?? {}), S(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), H(null);
  }, [y, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const C = new Set((r == null ? void 0 : r.configurationSchema.required) ?? []), T = Object.entries((r == null ? void 0 : r.configurationSchema.properties) ?? {}), U = () => {
    if (T.length > 0) {
      y.validateFields().then((N) => c(Lt((t == null ? void 0 : t.config) ?? {}, N)));
      return;
    }
    try {
      const N = JSON.parse(u);
      if (N === null || typeof N != "object" || Array.isArray(N))
        throw new Error("配置必须是 JSON 对象");
      H(null), c(N);
    } catch (N) {
      H(N.message);
    }
  }, ee = a.map((N, F) => ({
    ...N,
    index: F,
    key: `${F}:${N.source}->${N.target}`
  })), me = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (N, F) => /* @__PURE__ */ h(E.Text, { style: { fontSize: 11 }, children: [
        F.source,
        " → ",
        F.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (N, F) => {
        const Y = d.get(F.source), se = d.get(F.target);
        return /* @__PURE__ */ h("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ i(
            pe,
            {
              size: "small",
              value: F.sourcePort,
              style: { width: 110 },
              onChange: (j) => k(F.index, { ...F, sourcePort: j }),
              options: ((Y == null ? void 0 : Y.outputs) ?? []).map((j) => ({ label: `${j.name} (${j.type})`, value: j.name }))
            }
          ),
          /* @__PURE__ */ i(
            pe,
            {
              size: "small",
              value: F.targetPort,
              style: { width: 110 },
              onChange: (j) => k(F.index, { ...F, targetPort: j }),
              options: ((se == null ? void 0 : se.inputs) ?? []).map((j) => ({ label: `${j.name} (${j.type})`, value: j.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (N, F) => /* @__PURE__ */ i(R, { danger: !0, size: "small", type: "text", onClick: () => w(F.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ h("div", { className: "industrial-properties-body", children: [
    !t && /* @__PURE__ */ i(tt, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ h(Ee, { children: [
      /* @__PURE__ */ i(E.Text, { strong: !0, children: (r == null ? void 0 : r.name) ?? t.type }),
      /* @__PURE__ */ i(ye, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ i(je, { form: y, layout: "vertical", size: "small", style: { marginTop: 12 }, children: T.map(([N, F]) => /* @__PURE__ */ i(
        Vt,
        {
          name: N,
          schema: F,
          required: C.has(N),
          resources: s,
          resourceType: typeof (r == null ? void 0 : r.configurationSchema.resourceType) == "string" ? r.configurationSchema.resourceType : void 0,
          properties: (r == null ? void 0 : r.configurationSchema.properties) ?? {},
          config: (t == null ? void 0 : t.config) ?? {}
        },
        N
      )) }),
      T.length === 0 && /* @__PURE__ */ h(Ee, { children: [
        /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ i(
          M.TextArea,
          {
            rows: 8,
            value: u,
            style: { marginTop: 6 },
            onChange: (N) => S(N.target.value)
          }
        ),
        $ && /* @__PURE__ */ i(E.Text, { type: "danger", style: { fontSize: 12 }, children: $ })
      ] }),
      /* @__PURE__ */ i(R, { type: "primary", block: !0, onClick: U, children: "应用配置" }),
      /* @__PURE__ */ i(R, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: f, children: "删除节点" }),
      (r == null ? void 0 : r.activityType) && /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: r.activityType })
    ] }),
    /* @__PURE__ */ h(Qe, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      a.length,
      ")"
    ] }),
    n.length > 1 && /* @__PURE__ */ h(O, { wrap: !0, style: { marginBottom: 8 }, children: [
      /* @__PURE__ */ h(
        "select",
        {
          "aria-label": "连线起点",
          value: I ?? "",
          onChange: (N) => _(N.target.value || void 0),
          children: [
            /* @__PURE__ */ i("option", { value: "", children: "起点节点" }),
            n.map((N) => /* @__PURE__ */ i("option", { value: N.id, children: N.id }, N.id))
          ]
        }
      ),
      /* @__PURE__ */ h(
        "select",
        {
          "aria-label": "连线终点",
          value: A ?? "",
          onChange: (N) => p(N.target.value || void 0),
          children: [
            /* @__PURE__ */ i("option", { value: "", children: "终点节点" }),
            n.map((N) => /* @__PURE__ */ i("option", { value: N.id, children: N.id }, N.id))
          ]
        }
      ),
      /* @__PURE__ */ i(
        R,
        {
          size: "small",
          disabled: !I || !A || I === A,
          onClick: () => {
            I && A && b(I, A);
          },
          children: "添加连线"
        }
      )
    ] }),
    a.length === 0 ? /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ i(ie, { size: "small", columns: me, dataSource: ee, pagination: !1 }),
    o.length > 0 && /* @__PURE__ */ h(Ee, { children: [
      /* @__PURE__ */ h(Qe, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        o.length,
        ")"
      ] }),
      o.map((N, F) => /* @__PURE__ */ h("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ i(ye, { color: "red", children: N.code }),
        /* @__PURE__ */ h(
          E.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const Y = /^nodes\.([^.]+)/.exec(N.path);
              Y && x(Y[1]);
            },
            children: [
              N.path,
              ": ",
              N.message
            ]
          }
        )
      ] }, `${N.path}-${F}`))
    ] })
  ] });
}
const Zt = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class er extends Error {
  constructor(t) {
    super(t), this.name = "DtoError";
  }
}
const P = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, K = (e) => Array.isArray(e) ? e : [], g = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", q = (e, t = 0) => {
  const r = typeof e == "string" ? Number(e) : e;
  return typeof r == "number" && Number.isFinite(r) ? r : t;
}, l = (e, ...t) => {
  for (const r of t)
    if (e[r] !== void 0 && e[r] !== null) return e[r];
};
function ne(e) {
  const t = P(e);
  if (Object.prototype.hasOwnProperty.call(t, "data")) return t.data;
  if (typeof t.code == "string" && typeof t.message == "string")
    throw new er(`${t.code}: ${t.message}`);
  return e;
}
function tr(e) {
  const t = g(e).trim().toLowerCase();
  return Zt.find((n) => n.toLowerCase() === t) ?? "Unknown";
}
function rr(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const t = P(e), r = g(l(t, "name", "port", "id")).trim();
  return r ? {
    name: r,
    type: tr(l(t, "type", "portType", "dataType")),
    required: l(t, "required", "isRequired") === !0
  } : null;
}
function Xe(e) {
  const t = K(e).map(rr).filter((n) => n !== null), r = /* @__PURE__ */ new Set();
  return t.filter((n) => r.has(n.name) ? !1 : (r.add(n.name), !0));
}
const nr = (e) => {
  const t = P(e);
  return Object.keys(t).length === 0 ? { type: "object", properties: {} } : t;
};
function ar(e) {
  const t = P(e), r = g(l(t, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return r ? {
    nodeTypeId: r,
    name: g(l(t, "name", "title"), r),
    description: g(l(t, "description", "desc")),
    category: g(l(t, "category", "group"), "General"),
    icon: g(l(t, "icon"), "AppstoreOutlined"),
    version: g(l(t, "version"), "1.0.0"),
    inputs: Xe(l(t, "inputs", "inputPorts", "input_ports")),
    outputs: Xe(l(t, "outputs", "outputPorts", "output_ports")),
    configurationSchema: nr(l(t, "configurationSchema", "configSchema", "schema")),
    activityType: g(l(t, "activityType", "activity_type")),
    dependencies: K(l(t, "dependencies")).map((n) => g(n)).filter(Boolean)
  } : null;
}
function ct(e) {
  const t = ne(e), r = Array.isArray(t) ? t : K(l(P(t), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return r.map(ar).filter((a) => a !== null).filter((a) => n.has(a.nodeTypeId) ? !1 : (n.add(a.nodeTypeId), !0));
}
function dt(e) {
  const t = ne(e), r = Array.isArray(t) ? t : K(l(P(t), "resources", "items")), n = /* @__PURE__ */ new Set();
  return r.map((a) => {
    const d = P(a), s = g(l(d, "id")).trim();
    return !s || n.has(s) ? null : (n.add(s), {
      id: s,
      name: g(l(d, "name"), s),
      type: g(l(d, "type"), "unknown"),
      enabled: d.enabled !== !1,
      credentialId: typeof d.credentialId == "string" ? d.credentialId : null,
      credentialName: typeof d.credentialName == "string" ? d.credentialName : null
    });
  }).filter((a) => a !== null);
}
function ut(e) {
  const t = P(e), r = g(l(t, "workflowId", "workflow_id", "id")).trim();
  return r ? {
    workflowId: r,
    name: g(l(t, "name", "title"), r),
    version: q(l(t, "version"), 1)
  } : null;
}
function pt(e) {
  const t = ne(e);
  return (Array.isArray(t) ? t : K(l(P(t), "workflows", "items"))).map(ut).filter((n) => n !== null);
}
function Pe(e) {
  const t = P(e), r = {};
  for (const [n, a] of Object.entries(t))
    a !== void 0 && (r[n] = a);
  return r;
}
function ir(e) {
  const t = P(e), r = g(l(t, "id", "nodeId", "node_id")).trim();
  if (!r) return null;
  const n = P(l(t, "position", "layout"));
  return {
    id: r,
    type: g(l(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: Pe(l(t, "config", "configuration")),
    x: q(l(t, "x") ?? l(n, "x"), 0),
    y: q(l(t, "y") ?? l(n, "y"), 0)
  };
}
function sr(e) {
  const t = P(e), r = g(l(t, "source", "from", "sourceId")).trim(), n = g(l(t, "target", "to", "targetId")).trim();
  return !r || !n ? null : {
    source: r,
    sourcePort: g(l(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: g(l(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function or(e) {
  const t = ne(e), r = P(t), n = ut(r) ?? { workflowId: "", name: "", version: 1 }, a = K(l(r, "nodes")).map(ir).filter((c) => c !== null), d = K(l(r, "edges")).map(sr).filter((c) => c !== null), s = P(l(r, "trigger")), o = {
    kind: g(l(s, "kind", "type"), "manual"),
    cron: l(s, "cron") === void 0 ? null : g(s.cron),
    eventName: l(s, "eventName", "event_name") === void 0 ? null : g(l(s, "eventName", "event_name")),
    config: Pe(l(s, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: a,
    edges: d,
    trigger: o,
    resourceReferences: K(l(r, "resourceReferences", "resource_references")).map((c) => g(c)).filter(Boolean),
    layout: Pe(l(r, "layout"))
  };
}
const ke = (e) => e === !0 || typeof e == "string" && e.toLowerCase() === "true";
function lr(e) {
  const t = ne(e);
  return (Array.isArray(t) ? t : K(l(P(t), "resources", "items"))).map((n) => {
    const a = P(n), d = l(a, "credentialId", "credential_id");
    return {
      id: g(l(a, "id"), "—"),
      name: g(l(a, "name"), "—"),
      type: g(l(a, "type"), "—"),
      provider: g(l(a, "provider"), "—"),
      enabled: ke(l(a, "enabled")),
      credentialId: d == null ? null : g(d),
      credentialName: l(a, "credentialName") == null ? null : g(l(a, "credentialName")),
      config: Pe(l(a, "config", "configuration"))
    };
  });
}
function ft(e) {
  const t = ne(e);
  return (Array.isArray(t) ? t : K(l(P(t), "credentials", "items"))).map((n) => {
    const a = P(n);
    return {
      id: g(l(a, "id"), "—"),
      name: g(l(a, "name"), "—"),
      kind: g(l(a, "kind"), "—"),
      version: q(l(a, "version"))
    };
  });
}
function cr(e) {
  const t = ne(e);
  return (Array.isArray(t) ? t : K(l(P(t), "tests", "items"))).map((n) => {
    const a = P(n);
    return {
      resourceId: g(l(a, "resourceId", "resource_id"), "—"),
      ok: ke(l(a, "ok")),
      detail: g(l(a, "detail"), "")
    };
  });
}
const gt = (e) => {
  if (e == null) return null;
  const t = P(e);
  return Object.keys(t).length === 0 ? null : {
    code: g(l(t, "code"), "OTHER"),
    message: g(l(t, "message"), ""),
    action: g(l(t, "action"), "")
  };
};
function dr(e) {
  const t = ne(e);
  return K(l(P(t), "triggers", "items")).map((n) => {
    const a = P(n), d = (s) => l(a, s) == null ? null : g(l(a, s));
    return {
      workflowId: g(l(a, "workflowId", "workflow_id"), "—"),
      version: q(l(a, "version")),
      kind: g(l(a, "kind"), "manual"),
      cron: d("cron"),
      eventName: d("eventName"),
      timezone: d("timezone"),
      resourceId: d("resourceId"),
      nextRun: d("nextRun"),
      lastStatus: d("lastStatus"),
      lastStartedAt: d("lastStartedAt"),
      lastFireStatus: d("lastFireStatus"),
      lastFireDueAt: d("lastFireDueAt"),
      lastFireAttempts: l(a, "lastFireAttempts") == null ? null : q(l(a, "lastFireAttempts")),
      skippedFires: l(a, "skippedFires") == null ? null : q(l(a, "skippedFires")),
      lastError: d("lastError")
    };
  });
}
function ur(e) {
  if (e == null) return null;
  const t = ne(e), r = P(t);
  if (Object.keys(r).length === 0) return null;
  const n = K(l(r, "files")).map((d) => {
    const s = P(d);
    return {
      path: g(l(s, "path"), "—"),
      sourceDate: g(l(s, "sourceDate"), ""),
      inLedger: ke(l(s, "inLedger")),
      expectedTargets: q(l(s, "expectedTargets")),
      succeededTargets: q(l(s, "succeededTargets")),
      incompleteTargets: q(l(s, "incompleteTargets")),
      missingTargets: g(l(s, "missingTargets"), ""),
      wouldSkip: ke(l(s, "wouldSkip")),
      wouldCollect: ke(l(s, "wouldCollect")),
      duplicateRisk: ke(l(s, "duplicateRisk"))
    };
  }), a = P(l(r, "summary"));
  return {
    resourceId: g(l(r, "resourceId"), "—"),
    from: g(l(r, "from"), ""),
    to: g(l(r, "to"), ""),
    files: n,
    summary: {
      discovered: q(l(a, "discovered")),
      alreadyComplete: q(l(a, "alreadyComplete")),
      needsCollection: q(l(a, "needsCollection")),
      duplicateRisk: q(l(a, "duplicateRisk"))
    }
  };
}
function pr(e) {
  const t = ne(e);
  return (Array.isArray(t) ? t : K(l(P(t), "files", "items"))).map((n) => {
    const a = P(n);
    return {
      id: g(l(a, "id"), "—"),
      path: g(l(a, "path"), "—"),
      identityKey: g(l(a, "identityKey"), ""),
      sourceResourceId: g(l(a, "sourceResourceId"), ""),
      length: q(l(a, "length")),
      sourceDate: g(l(a, "sourceDate"), ""),
      collectedAt: g(l(a, "collectedAt"), ""),
      status: g(l(a, "status"), "—"),
      deliveries: K(l(a, "deliveries")).map((d) => {
        const s = P(d);
        return {
          id: g(l(s, "id"), "—"),
          collectionFileId: g(l(s, "collectionFileId"), ""),
          targetId: g(l(s, "targetId"), "—"),
          targetKind: g(l(s, "targetKind"), "record"),
          status: g(l(s, "status"), "—"),
          attempts: q(l(s, "attempts")),
          records: q(l(s, "records")),
          error: l(s, "error") == null ? null : g(l(s, "error")),
          accepted: q(l(s, "accepted")),
          rejected: q(l(s, "rejected")),
          warnings: q(l(s, "warnings")),
          diagnosis: gt(l(s, "diagnosis"))
        };
      })
    };
  });
}
function fr(e) {
  const t = ne(e), r = P(t), n = K(l(r, "runs")).map((d) => {
    const s = P(d);
    return {
      id: g(l(s, "id"), "—"),
      workflowId: g(l(s, "workflowId", "workflow_id"), "—"),
      status: g(l(s, "status"), "—"),
      startedAt: g(l(s, "startedAt", "started_at"), ""),
      finishedAt: l(s, "finishedAt", "finished_at") == null ? null : g(l(s, "finishedAt", "finished_at")),
      error: l(s, "error") == null ? null : g(l(s, "error"))
    };
  }), a = K(l(r, "deliveries")).map((d) => {
    const s = P(d);
    return {
      id: g(l(s, "id"), "—"),
      collectionFileId: g(l(s, "collectionFileId", "collection_file_id"), ""),
      targetId: g(l(s, "targetId", "target_id"), "—"),
      targetKind: g(l(s, "targetKind", "target_kind"), "record"),
      status: g(l(s, "status"), "—"),
      attempts: q(l(s, "attempts")),
      records: q(l(s, "records")),
      error: l(s, "error") == null ? null : g(l(s, "error")),
      diagnosis: gt(l(s, "diagnosis"))
    };
  });
  return { runs: n, deliveries: a };
}
function gr(e) {
  if (e == null) return null;
  const t = ne(e), r = P(t);
  if (Object.keys(r).length === 0) return null;
  const n = K(l(r, "nodes")).slice(0, 64).map((a) => {
    const d = P(a);
    return {
      nodeId: g(l(d, "nodeId", "node_id"), "—"),
      status: g(l(d, "status"), "—"),
      records: q(l(d, "records")),
      files: q(l(d, "files")),
      error: l(d, "error") == null ? null : g(l(d, "error"), void 0),
      samples: K(l(d, "samples")).slice(0, 3).map((s) => g(s))
    };
  });
  return {
    status: g(l(r, "status"), "—"),
    error: l(r, "error") == null ? null : g(l(r, "error"), void 0),
    executed: K(l(r, "executed")).map((a) => g(a)),
    instanceId: l(r, "instanceId") == null ? null : g(l(r, "instanceId"), void 0),
    definitionId: l(r, "definitionId") == null ? null : g(l(r, "definitionId"), void 0),
    nodes: n
  };
}
const yr = [
  { value: "manual", label: "手动运行" },
  { value: "schedule", label: "定时运行" },
  { value: "event", label: "事件触发" }
], mr = [
  { label: "每天 02:00", cron: "0 2 * * *" },
  { label: "每小时", cron: "0 * * * *" },
  { label: "每 5 分钟", cron: "*/5 * * * *" },
  { label: "工作日 08:30", cron: "30 8 * * 1-5" }
];
function hr({
  open: e,
  onClose: t,
  trigger: r,
  resourceIds: n,
  onSave: a,
  ctx: d,
  workflowId: s
}) {
  const [o, c] = m(r), [f, w] = m([]), [b, k] = m(null);
  W(() => {
    c(r);
  }, [r]), W(() => {
    e && d.hubQuery("industrial/trigger-status").then((u) => w(dr(u))).catch((u) => k(u instanceof Error ? u.message : String(u)));
  }, [d, e]);
  const x = f.find((u) => u.workflowId === s);
  return /* @__PURE__ */ i(
    Te,
    {
      open: e,
      title: "触发器配置",
      okText: "保存触发器",
      cancelText: "取消",
      onCancel: t,
      onOk: () => {
        var u, S;
        if (o.kind === "schedule" && !((u = o.cron) != null && u.trim())) {
          k("定时运行需要填写 cron 表达式");
          return;
        }
        if (o.kind === "event" && !((S = o.eventName) != null && S.trim())) {
          k("事件触发需要填写事件名称");
          return;
        }
        k(null), a(o), t();
      },
      width: 720,
      children: /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ h(O, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(E.Text, { children: "触发方式" }),
          /* @__PURE__ */ i(O, { wrap: !0, children: yr.map((u) => /* @__PURE__ */ i(
            R,
            {
              type: o.kind === u.value ? "primary" : "default",
              onClick: () => c({ ...o, kind: u.value }),
              children: u.label
            },
            u.value
          )) })
        ] }),
        /* @__PURE__ */ i(O, { wrap: !0, children: /* @__PURE__ */ i(
          pe,
          {
            "aria-label": "运行资源",
            allowClear: !0,
            placeholder: "运行资源",
            value: wr(o),
            options: n.map((u) => ({ value: u, label: u })),
            onChange: (u) => c({
              ...o,
              config: { ...o.config, resourceId: u ?? "" }
            })
          }
        ) }),
        /* @__PURE__ */ h(O, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(E.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ h(O, { wrap: !0, children: [
            /* @__PURE__ */ i(E.Text, { children: "常用" }),
            mr.map((u) => /* @__PURE__ */ i(
              R,
              {
                size: "small",
                onClick: () => c({ ...o, cron: u.cron }),
                children: u.label
              },
              u.cron
            ))
          ] }),
          /* @__PURE__ */ i(
            M,
            {
              "aria-label": "Cron 表达式",
              placeholder: "分 时 日 月 周，例如 0 2 * * *",
              value: o.cron ?? "",
              onChange: (u) => c({ ...o, cron: u.target.value })
            }
          ),
          /* @__PURE__ */ i(
            M,
            {
              "aria-label": "调度时区",
              placeholder: "时区（默认 UTC，例如 Asia/Shanghai）",
              value: vr(o),
              onChange: (u) => c({
                ...o,
                config: { ...o.config, timezone: u.target.value }
              })
            }
          ),
          /* @__PURE__ */ i(
            M,
            {
              "aria-label": "事件名称",
              placeholder: "事件名称",
              value: o.eventName ?? "",
              onChange: (u) => c({ ...o, eventName: u.target.value })
            }
          ),
          /* @__PURE__ */ h(E.Text, { type: "secondary", children: [
            "下次运行：",
            (x == null ? void 0 : x.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        x && /* @__PURE__ */ i(
          ie,
          {
            rowKey: "workflowId",
            dataSource: [x],
            pagination: !1,
            columns: [
              { title: "时区", dataIndex: "timezone", render: (u) => u ?? "UTC" },
              { title: "状态", dataIndex: "lastStatus", render: (u) => u ?? "未运行" },
              { title: "上次运行", dataIndex: "lastStartedAt", render: (u) => u ?? "—" },
              {
                title: "触发点",
                dataIndex: "lastFireStatus",
                render: (u, S) => S.lastFireStatus ? `${S.lastFireStatus} · ${S.lastFireDueAt ?? "—"} · ${S.lastFireAttempts ?? 0} 次` + (S.skippedFires ? ` · 超限 ${S.skippedFires}` : "") : "—"
              },
              { title: "错误", dataIndex: "lastError", render: (u) => u ?? "—" }
            ]
          }
        ),
        b && /* @__PURE__ */ i(fe, { type: "error", showIcon: !0, message: b })
      ] })
    }
  );
}
function wr(e) {
  const t = e.config.resourceId;
  return typeof t == "string" && t.trim() ? t : void 0;
}
function vr(e) {
  const t = e.config.timezone;
  return typeof t == "string" && t.trim() ? t : "UTC";
}
function Ir({
  open: e,
  onClose: t,
  ctx: r,
  workflowId: n,
  resourceId: a,
  lookbackDays: d
}) {
  const [s, o] = m(null), [c, f] = m(null), [w, b] = m(!1), k = z(async () => {
    b(!0), f(null);
    try {
      await r.hubCommand("industrial/preview-backfill", {
        workflowId: n,
        resourceId: a,
        lookbackDays: d
      }), o(ur(await r.hubQuery("industrial/backfill-preview")));
    } catch (x) {
      f(x instanceof Error ? x.message : String(x));
    } finally {
      b(!1);
    }
  }, [r, d, a, n]);
  return /* @__PURE__ */ i(
    Te,
    {
      open: e,
      title: "补采预览",
      onCancel: t,
      width: 820,
      footer: [
        /* @__PURE__ */ i(R, { loading: w, onClick: () => void k(), children: "重新扫描" }, "refresh"),
        /* @__PURE__ */ i(R, { type: "primary", onClick: t, children: "关闭" }, "close")
      ],
      children: /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ h(O, { children: [
          /* @__PURE__ */ h(R, { type: "primary", loading: w, onClick: () => void k(), children: [
            "扫描最近 ",
            d,
            " 天"
          ] }),
          /* @__PURE__ */ h(E.Text, { type: "secondary", children: [
            "资源：",
            a || "未选择"
          ] })
        ] }),
        c && /* @__PURE__ */ i(fe, { type: "error", showIcon: !0, message: c }),
        s && /* @__PURE__ */ h(Ee, { children: [
          /* @__PURE__ */ h(E.Text, { children: [
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
          /* @__PURE__ */ i(
            ie,
            {
              rowKey: "path",
              dataSource: s.files,
              pagination: !1,
              size: "small",
              columns: [
                { title: "文件", dataIndex: "path" },
                { title: "源日期", dataIndex: "sourceDate" },
                { title: "账本", dataIndex: "inLedger", render: (x) => x ? "已登记" : "新文件" },
                { title: "当前目标", dataIndex: "expectedTargets" },
                { title: "成功目标", dataIndex: "succeededTargets" },
                { title: "待补目标", dataIndex: "missingTargets", render: (x) => x || "—" },
                { title: "未完成目标", dataIndex: "incompleteTargets" },
                {
                  title: "预计动作",
                  render: (x, y) => y.wouldSkip ? "跳过" : y.duplicateRisk ? "仅补缺失目标" : "采集"
                }
              ]
            }
          )
        ] })
      ] })
    }
  );
}
const { Header: br, Content: xr } = kt;
function kr({ ctx: e }) {
  const [t, r] = St.useMessage(), [n, a] = m([]), [d, s] = m([]), [o, c] = m([]), [f, w] = m([]), [b, k] = m(""), [x, y] = m(1), [u, S] = m(!0), [$, H] = m([]), [I, _] = m(() => $e()), [A, p] = m(null), [C, T] = m([]), [U, ee] = m(!1), [me, N] = m(!0), [F, Y] = m(!1), [se, j] = m(!1), [te, V] = m(null), [oe, he] = m(!1), [Ie, we] = m(!1), be = ce(1), Be = ce(e);
  Be.current = e;
  const Re = ce(I.trigger);
  W(() => {
    Re.current = I.trigger;
  }, [I.trigger]), W(() => {
    let v = !0;
    const D = Be.current.hubQuery;
    return Promise.allSettled([
      D("industrial/nodes"),
      D("industrial/workflows"),
      D("industrial/resources")
    ]).then(([G, B, Q]) => {
      var ue;
      if (!v) return;
      const J = [];
      if (G.status === "fulfilled" ? a(ct(G.value)) : J.push(`节点描述: ${String(G.reason)}`), B.status === "fulfilled") {
        const X = pt(B.value);
        s(X);
        const le = new URLSearchParams(window.location.search).get("workflowId"), ae = le && X.some((ge) => ge.workflowId === le) ? le : (ue = X[0]) == null ? void 0 : ue.workflowId;
        ae && Ke(ae);
      } else J.push(`工作流: ${String(B.reason)}`);
      if (Q.status === "fulfilled") {
        const X = dt(Q.value);
        w(X);
        const le = X.map((ae) => ae.id);
        c(le), k((ae) => ae || le[0] || "");
      } else J.push(`资源: ${String(Q.reason)}`);
      H(J);
    }).finally(() => {
      v && S(!1);
    }), () => {
      v = !1;
    };
  }, []);
  const de = Me(
    () => new Map(n.map((v) => [v.nodeTypeId, v])),
    [n]
  ), _e = I.nodes.find((v) => v.id === A) ?? null, xe = z(async (v, D, G) => {
    ee(!0);
    try {
      return await re().api.hubCommand(v, D), t.success(G), !0;
    } catch (B) {
      return t.error(`${v} failed: ${B instanceof Error ? B.message : String(B)}`), !1;
    } finally {
      ee(!1);
    }
  }, [t]), Ke = z(async (v) => {
    try {
      const D = await e.hubQuery("industrial/workflow-definitions"), B = (ne(D) ?? []).find((ue) => {
        const X = ue;
        return String((X == null ? void 0 : X.workflowId) ?? "") === v;
      }), Q = B === void 0 ? { ...$e(v), name: v } : or(B);
      _(Q), y(Number(Q.layout.lookbackDays ?? 1) || 1);
      const J = Q.nodes.map((ue) => /^n(\d+)$/.exec(ue.id)).filter(Boolean).reduce((ue, X) => Math.max(ue, Number(X[1])), 0);
      be.current = J + 1, p(null), Y(!1), T([]), j(!1);
    } catch (D) {
      t.error(`加载工作流失败: ${D instanceof Error ? D.message : String(D)}`);
    }
  }, [e, t]), Ce = z(async () => {
    try {
      const v = gr(await e.hubQuery("industrial/execution"));
      if (v === null) return;
      V(v);
    } catch {
    }
  }, [e]), yt = z(async () => {
    var G;
    const v = new Set(I.resourceReferences ?? []);
    for (const B of I.nodes)
      for (const Q of ["resourceId", "targetResourceId"]) {
        const J = (G = B.config) == null ? void 0 : G[Q];
        typeof J == "string" && J.trim() && v.add(J.trim());
      }
    const D = {
      ...I,
      trigger: Re.current,
      resourceReferences: [...v],
      layout: { ...I.layout, lookbackDays: x }
    };
    await xe("industrial/save-workflow", D, "已保存") && (_(D), j(!1));
  }, [xe, I, x]), mt = z(async () => {
    if (se) {
      t.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await xe("industrial/publish-workflow", { workflowId: I.workflowId }, "已发布") && await Ce();
  }, [xe, I.workflowId, se, t, Ce]), ht = z(async () => {
    if (se) {
      t.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await xe(
      "industrial/run-workflow",
      { workflowId: I.workflowId, resourceId: b },
      "采集已启动"
    ) && await Ce();
  }, [xe, I.workflowId, se, t, Ce, b]), Oe = z((v, D, G) => {
    const B = ve(de, v);
    if (!B) return;
    const Q = `n${be.current++}`;
    _((J) => ({
      ...J,
      nodes: [...J.nodes, zt(B, Q, D, G)]
    })), p(Q), Y(!0), T([]), j(!0);
  }, [de]), wt = z((v) => {
    const D = be.current - 1;
    Oe(v, 80 + D % 3 * 190, 80 + Math.floor(D / 3) * 130);
  }, [Oe]), Ge = z((v, D) => {
    const G = I.nodes.find((ge) => ge.id === v), B = I.nodes.find((ge) => ge.id === D);
    if (!G || !B)
      return t.warning("连线失败：端点不在画布上"), null;
    const Q = ve(de, G.type), J = ve(de, B.type);
    if (!Q || !J) return null;
    let ue = G, X = B, le = He(Q, J);
    if (!le) {
      const ge = He(J, Q);
      if (!ge)
        return t.warning(`端口类型不兼容：${Q.name} → ${J.name}`), null;
      ue = B, X = G, le = ge;
    }
    const ae = Ft(I, {
      source: ue.id,
      sourcePort: le.sourcePort,
      target: X.id,
      targetPort: le.targetPort
    }, de);
    return ae.issue ? (t.warning(`${ae.issue.code}: ${ae.issue.message}`), T((ge) => [...ge, ae.issue]), null) : ae.definition === I ? (t.info("这条连线已经存在"), null) : (_(ae.definition), j(!0), le);
  }, [de, I, t]), vt = z(() => {
    const v = Kt(I, de, new Set(o));
    T(v), v.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : (t.warning(`发现 ${v.length} 个问题，见属性抽屉定位列表`), Y(!0));
  }, [I, de, o, t]), It = z((v, D) => {
    _((G) => ({
      ...G,
      edges: G.edges.map((B, Q) => Q === v ? D : B)
    })), j(!0);
  }, []);
  return u ? /* @__PURE__ */ i(nt, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ h("div", { className: "industrial-designer", children: [
    r,
    /* @__PURE__ */ h(br, { className: "industrial-header", children: [
      /* @__PURE__ */ h("div", { className: "industrial-title", children: [
        /* @__PURE__ */ i(E.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ i(E.Text, { type: "secondary", children: I.name || I.workflowId }),
        se && /* @__PURE__ */ i(ye, { color: "orange", children: "未保存" }),
        te && /* @__PURE__ */ i(ye, { color: te.error ? "error" : "success", children: te.status })
      ] }),
      /* @__PURE__ */ h("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ i(
          R,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": me,
            onClick: () => N((v) => !v),
            children: me ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ i(
          R,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => Y((v) => !v),
            children: "属性"
          }
        ),
        /* @__PURE__ */ i(R, { size: "small", "data-testid": "open-trigger", onClick: () => he(!0), children: "触发器" }),
        /* @__PURE__ */ i(
          R,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => we(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ i("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ h(O, { size: 4, children: [
          /* @__PURE__ */ i(R, { size: "small", loading: U, onClick: () => void yt(), children: "保存" }),
          /* @__PURE__ */ i(R, { size: "small", onClick: vt, children: "校验" }),
          /* @__PURE__ */ i(R, { size: "small", loading: U, onClick: () => void mt(), children: "发布" }),
          /* @__PURE__ */ i(R, { size: "small", type: "primary", loading: U, onClick: () => void ht(), children: "运行" })
        ] }),
        /* @__PURE__ */ h(
          "select",
          {
            className: "industrial-resource-select",
            "aria-label": "运行资源",
            value: b,
            onChange: (v) => k(v.target.value),
            children: [
              /* @__PURE__ */ i("option", { value: "", children: "运行资源" }),
              o.map((v) => /* @__PURE__ */ i("option", { value: v, children: v }, v))
            ]
          }
        ),
        /* @__PURE__ */ i(
          rt,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: x,
            onChange: (v) => {
              y(Number(v) || 1), j(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ h(xr, { className: `industrial-body${me ? "" : " industrial-body--collapsed"}`, children: [
      me ? /* @__PURE__ */ h("aside", { className: "industrial-palette", children: [
        d.length > 0 && /* @__PURE__ */ i(
          pe,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: I.workflowId,
            placeholder: "选择工作流",
            onChange: (v) => void Ke(v),
            options: d.map((v) => ({ value: v.workflowId, label: v.name }))
          }
        ),
        /* @__PURE__ */ i(_t, { descriptors: n, loading: !1, onAdd: wt })
      ] }) : /* @__PURE__ */ i("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ i(R, { type: "text", size: "small", onClick: () => N(!0), children: "节点库" }) }),
      /* @__PURE__ */ h("section", { className: "industrial-center", children: [
        $.length > 0 && /* @__PURE__ */ i(
          fe,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: $.join("；")
          }
        ),
        /* @__PURE__ */ i(
          Rt,
          {
            definition: I,
            catalog: de,
            selectedId: A,
            onSelect: (v) => {
              p(v), v && Y(!0);
            },
            onDropNode: Oe,
            onCreateEdge: Ge,
            onPositions: (v) => {
              _((D) => Ut(D, v)), j(!0);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ h(
      xt,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: F,
        mask: !1,
        getContainer: !1,
        onClose: () => Y(!1),
        children: [
          te && /* @__PURE__ */ i(
            fe,
            {
              style: { marginBottom: 12 },
              type: te.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${te.status}`,
              description: /* @__PURE__ */ h(O, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                te.error && /* @__PURE__ */ i("span", { children: te.error }),
                te.executed.length > 0 && /* @__PURE__ */ h("span", { children: [
                  "已执行节点: ",
                  te.executed.join(", ")
                ] }),
                te.nodes.length > 0 && /* @__PURE__ */ i(
                  ie,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: te.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (v, D) => D.error ?? (D.samples.length > 0 ? D.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ i(
            Xt,
            {
              node: _e,
              descriptor: _e ? ve(de, _e.type) ?? null : null,
              nodes: I.nodes,
              edges: I.edges,
              catalog: de,
              resources: f,
              issues: C,
              onApplyConfig: (v) => {
                A && (_((D) => Mt(D, A, v)), j(!0));
              },
              onDeleteNode: () => {
                _((v) => jt(v, A ?? "")), p(null), j(!0);
              },
              onDeleteEdge: (v) => {
                _((D) => qt(D, v)), j(!0);
              },
              onCreateEdge: (v, D) => void Ge(v, D),
              onUpdateEdge: It,
              onSelectNode: (v) => {
                p(v), Y(!0);
              }
            }
          )
        ]
      }
    ),
    oe && /* @__PURE__ */ i(
      hr,
      {
        open: oe,
        onClose: () => he(!1),
        trigger: I.trigger,
        resourceIds: o,
        workflowId: I.workflowId,
        ctx: e,
        onSave: (v) => {
          Re.current = v, _((D) => ({ ...D, trigger: v })), j(!0);
        }
      }
    ),
    Ie && /* @__PURE__ */ i(
      Ir,
      {
        open: Ie,
        onClose: () => we(!1),
        ctx: e,
        workflowId: I.workflowId,
        resourceId: b,
        lookbackDays: x
      }
    )
  ] });
}
function Sr({ block: e, ctx: t }) {
  const [r, n] = m([]), [a, d] = m(!1), [s, o] = m(""), [c, f] = m(null), [w, b] = m(!1), k = z(async () => {
    try {
      n(pt(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
    } catch (u) {
      f(u instanceof Error ? u.message : String(u));
    }
  }, [e == null ? void 0 : e.query, t]);
  W(() => {
    k();
  }, [k]);
  const x = z(async () => {
    var S;
    const u = s.trim();
    if (!u) {
      f("请输入工作流名称");
      return;
    }
    b(!0), f(null);
    try {
      const H = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await t.hubCommand("industrial/save-workflow", {
        ...$e(H),
        name: u,
        layout: { lookbackDays: 1 }
      }), d(!1), o(""), await k(), (S = t.navigate) == null || S.call(t, `/industrial/designer?workflowId=${encodeURIComponent(H)}`);
    } catch ($) {
      f($ instanceof Error ? $.message : String($));
    } finally {
      b(!1);
    }
  }, [t, k, s]), y = z((u) => {
    var S;
    (S = t.navigate) == null || S.call(t, `/industrial/designer?workflowId=${encodeURIComponent(u)}`);
  }, [t]);
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(O, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ i(R, { type: "primary", onClick: () => d(!0), children: "新建工作流" })
    ] }),
    c && /* @__PURE__ */ i(E.Text, { type: "danger", children: c }),
    /* @__PURE__ */ i(
      ie,
      {
        rowKey: "workflowId",
        dataSource: r,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (u) => /* @__PURE__ */ i(R, { type: "link", style: { padding: 0 }, onClick: () => y(u), children: u })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ i(
      Te,
      {
        open: a,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: w,
        onCancel: () => d(!1),
        onOk: () => void x(),
        children: /* @__PURE__ */ h(O, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(E.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ i(
            M,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: s,
              onChange: (u) => o(u.target.value ?? "")
            }
          ),
          c && /* @__PURE__ */ i(E.Text, { type: "danger", children: c })
        ] })
      }
    )
  ] });
}
const Ae = {
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
}, Tr = [
  { value: "filesystem.local", label: "本地目录" },
  { value: "filesystem.unc", label: "Windows UNC 共享" },
  { value: "sqlserver", label: "SQL Server" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mysql", label: "MySQL" },
  { value: "sqlite", label: "SQLite" }
];
function Cr(e) {
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
function Nr({ block: e, ctx: t }) {
  const [r, n] = m([]), [a, d] = m([]), [s, o] = m([]), [c, f] = m(Ae), [w, b] = m(!1), [k, x] = m(null), [y, u] = m(!1), S = z(async () => {
    try {
      const [p, C, T] = await Promise.all([
        t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        t.hubQuery("industrial/credentials"),
        t.hubQuery("industrial/resource-tests")
      ]);
      n(lr(p)), d(ft(C)), o(cr(T));
    } catch (p) {
      x(p instanceof Error ? p.message : String(p));
    }
  }, [e == null ? void 0 : e.query, t]);
  W(() => {
    S();
  }, [S]);
  const $ = z((p) => {
    const C = (...T) => {
      for (const U of T) {
        const ee = p.config[U];
        if (ee != null && String(ee).trim()) return String(ee);
      }
      return "";
    };
    f({
      id: p.id,
      name: p.name,
      type: p.type,
      provider: p.provider,
      root: C("root", "path"),
      host: C("server", "host", "dataSource", "data_source"),
      database: C("database", "initialCatalog", "initial_catalog", "path"),
      port: C("port"),
      table: C("table"),
      credentialId: p.credentialId ?? "",
      enabled: p.enabled,
      config: p.config
    }), b(!0);
  }, []), H = z(async () => {
    const p = c.id.trim();
    if (!p) {
      x("请输入资源 ID");
      return;
    }
    u(!0), x(null);
    try {
      await t.hubCommand("industrial/save-resource", {
        id: p,
        name: c.name || p,
        type: c.type,
        provider: c.provider || "manual",
        config: Cr(c),
        credentialId: c.credentialId || null,
        enabled: c.enabled
      }), b(!1), f(Ae), await S();
    } catch (C) {
      x(C instanceof Error ? C.message : String(C));
    } finally {
      u(!1);
    }
  }, [t, c, S]), I = z(async (p) => {
    x(null);
    try {
      await t.hubCommand("industrial/test-resource", { id: p });
    } catch (C) {
      x(C instanceof Error ? C.message : String(C));
    }
    await S();
  }, [t, S]), _ = z(async (p) => {
    x(null);
    try {
      await t.hubCommand("industrial/delete-resource", { id: p }), await S();
    } catch (C) {
      x(C instanceof Error ? C.message : String(C));
    }
  }, [t, S]), A = new Map(s.map((p) => [p.resourceId, p]));
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(O, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ i(R, { type: "primary", onClick: () => {
        f(Ae), b(!0);
      }, children: "新建资源" })
    ] }),
    k && /* @__PURE__ */ i(fe, { type: "error", showIcon: !0, message: k }),
    /* @__PURE__ */ i(
      ie,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        columns: [
          { title: "ID", dataIndex: "id" },
          { title: "名称", dataIndex: "name" },
          { title: "类型", dataIndex: "type" },
          { title: "凭证", dataIndex: "credentialName", render: (p) => p ?? "—" },
          {
            title: "连接测试",
            key: "test",
            render: (p, C) => {
              const T = A.get(C.id);
              return T ? /* @__PURE__ */ i(E.Text, { type: T.ok ? "success" : "danger", children: T.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (p, C) => /* @__PURE__ */ h(O, { size: 4, children: [
              /* @__PURE__ */ i(R, { size: "small", onClick: () => void I(C.id), children: "测试连接" }),
              /* @__PURE__ */ i(R, { size: "small", onClick: () => $(C), children: "编辑" }),
              /* @__PURE__ */ i(R, { size: "small", danger: !0, onClick: () => void _(C.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ i(
      Te,
      {
        open: w,
        title: c.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: y,
        onCancel: () => b(!1),
        onOk: () => void H(),
        children: /* @__PURE__ */ h(O, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(
            M,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: c.id,
              onChange: (p) => f({ ...c, id: p.target.value })
            }
          ),
          /* @__PURE__ */ i(
            M,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: c.name,
              onChange: (p) => f({ ...c, name: p.target.value })
            }
          ),
          /* @__PURE__ */ i(E.Text, { children: "资源类型" }),
          /* @__PURE__ */ i(O, { wrap: !0, children: Tr.map((p) => /* @__PURE__ */ i(
            R,
            {
              size: "small",
              type: c.type === p.value ? "primary" : "default",
              onClick: () => f({ ...c, type: p.value }),
              children: p.label
            },
            p.value
          )) }),
          (c.type === "filesystem.local" || c.type === "filesystem.unc") && /* @__PURE__ */ i(
            M,
            {
              "aria-label": "目录路径",
              placeholder: c.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: c.root,
              onChange: (p) => f({ ...c, root: p.target.value })
            }
          ),
          (c.type === "sqlserver" || c.type === "postgresql" || c.type === "mysql") && /* @__PURE__ */ h(O, { children: [
            /* @__PURE__ */ i(
              M,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: c.host,
                onChange: (p) => f({ ...c, host: p.target.value })
              }
            ),
            /* @__PURE__ */ i(
              M,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: c.database,
                onChange: (p) => f({ ...c, database: p.target.value })
              }
            ),
            /* @__PURE__ */ i(
              M,
              {
                "aria-label": "端口",
                placeholder: "端口",
                value: c.port,
                onChange: (p) => f({ ...c, port: p.target.value })
              }
            )
          ] }),
          c.type === "sqlite" && /* @__PURE__ */ i(
            M,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: c.database,
              onChange: (p) => f({ ...c, database: p.target.value })
            }
          ),
          /* @__PURE__ */ i(
            pe,
            {
              "aria-label": "凭证",
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: c.credentialId || void 0,
              options: a.map((p) => ({ value: p.id, label: p.name })),
              onChange: (p) => f({ ...c, credentialId: p ?? "" })
            }
          ),
          /* @__PURE__ */ h(O, { children: [
            /* @__PURE__ */ i(
              at,
              {
                "aria-label": "启用资源",
                checked: c.enabled,
                onChange: (p) => f({ ...c, enabled: p })
              }
            ),
            /* @__PURE__ */ i(E.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
function Er({ block: e, ctx: t }) {
  const [r, n] = m([]), [a, d] = m(!1), [s, o] = m(""), [c, f] = m(""), [w, b] = m("username-password"), [k, x] = m(""), [y, u] = m(!1), [S, $] = m(null), [H, I] = m(!1), _ = z(async () => {
    try {
      n(ft(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
    } catch (T) {
      $(T instanceof Error ? T.message : String(T));
    }
  }, [e == null ? void 0 : e.query, t]);
  W(() => {
    _();
  }, [_]);
  const A = z(() => {
    o(""), f(""), b("username-password"), x(""), u(!1), d(!0);
  }, []), p = z((T) => {
    o(T.id), f(T.name), b(T.kind), x(""), u(!0), d(!0);
  }, []), C = z(async () => {
    if (!s.trim()) {
      $("请输入凭证 ID");
      return;
    }
    if (!k.trim()) {
      $("请输入凭证内容");
      return;
    }
    I(!0), $(null);
    try {
      y ? await t.hubCommand("industrial/rotate-credential", { id: s, secret: k }) : await t.hubCommand("industrial/save-credential", { id: s, name: c || s, kind: w, secret: k }), d(!1), await _();
    } catch (T) {
      $(T instanceof Error ? T.message : String(T));
    } finally {
      I(!1);
    }
  }, [t, s, w, _, c, y, k]);
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(O, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ i(R, { type: "primary", onClick: A, children: "新建凭证" })
    ] }),
    S && /* @__PURE__ */ i(fe, { type: "error", showIcon: !0, message: S }),
    /* @__PURE__ */ i(
      ie,
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
            render: (T, U) => /* @__PURE__ */ i(R, { size: "small", onClick: () => p(U), children: "轮换" })
          }
        ]
      }
    ),
    /* @__PURE__ */ i(
      Te,
      {
        open: a,
        title: y ? "轮换凭证" : "新建凭证",
        okText: y ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: H,
        onCancel: () => d(!1),
        onOk: () => void C(),
        children: /* @__PURE__ */ h(O, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(
            M,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: s,
              disabled: y,
              onChange: (T) => o(T.target.value)
            }
          ),
          !y && /* @__PURE__ */ i(
            M,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: c,
              onChange: (T) => f(T.target.value)
            }
          ),
          /* @__PURE__ */ i(
            M,
            {
              "aria-label": "凭证内容",
              placeholder: y ? "新凭证内容" : "凭证内容",
              value: k,
              onChange: (T) => x(T.target.value)
            }
          ),
          S && /* @__PURE__ */ i(E.Text, { type: "danger", children: S })
        ] })
      }
    )
  ] });
}
const Ze = (e) => e === "Succeeded" || e === "Completed" ? "success" : e === "Failed" || e === "PartialFailure" ? "error" : e === "Running" || e === "Interrupted" ? "processing" : "default";
function $r({ block: e, ctx: t }) {
  const [r, n] = m([]), [a, d] = m([]), [s, o] = m(null), [c, f] = m(null), [w, b] = m(!1), k = z(async () => {
    try {
      const y = fr(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/execution-center"));
      n(y.runs), d(y.deliveries);
    } catch (y) {
      o(y instanceof Error ? y.message : String(y));
    }
  }, [e == null ? void 0 : e.query, t]);
  W(() => {
    k();
  }, [k]);
  const x = z(async (y) => {
    b(!0), o(null), f(null);
    try {
      await t.hubCommand("industrial/retry-failed", {
        deliveryId: y.id,
        targetId: y.targetId
      }), f(`已提交目标 ${y.targetId} 的重试`), await k();
    } catch (u) {
      o(u instanceof Error ? u.message : String(u));
    } finally {
      b(!1);
    }
  }, [t, k]);
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    s && /* @__PURE__ */ i(fe, { type: "error", showIcon: !0, message: s }),
    c && /* @__PURE__ */ i(fe, { type: "success", showIcon: !0, message: c }),
    /* @__PURE__ */ i(
      ie,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (y) => /* @__PURE__ */ i(ye, { color: Ze(y), children: y }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (y) => y ?? "—" },
          { title: "错误", dataIndex: "error", render: (y) => y ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ i(
      ie,
      {
        rowKey: "id",
        dataSource: a,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (y) => /* @__PURE__ */ i(ye, { color: Ze(y), children: y }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (y, u) => u.diagnosis ? /* @__PURE__ */ h(O, { direction: "vertical", size: 0, children: [
              /* @__PURE__ */ i(E.Text, { children: u.diagnosis.message }),
              /* @__PURE__ */ i(E.Text, { type: "secondary", children: u.diagnosis.action })
            ] }) : u.error ?? "—"
          },
          {
            title: "操作",
            key: "actions",
            render: (y, u) => /* @__PURE__ */ i(
              R,
              {
                size: "small",
                disabled: w || u.status !== "Failed" && u.status !== "Interrupted",
                onClick: () => void x(u),
                children: "重试此目标"
              }
            )
          }
        ]
      }
    )
  ] });
}
function Dr({ block: e, ctx: t }) {
  const [r, n] = m([]), [a, d] = m(null), s = z(async () => {
    try {
      n(pr(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (c) {
      d(c instanceof Error ? c.message : String(c));
    }
  }, [e == null ? void 0 : e.query, t]);
  W(() => {
    s();
  }, [s]);
  const o = (c) => /* @__PURE__ */ i(
    ie,
    {
      rowKey: "id",
      dataSource: c.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (f) => /* @__PURE__ */ i(ye, { children: f }) },
        { title: "记录", dataIndex: "records" },
        {
          title: "接受/拒绝/警告",
          key: "counts",
          render: (f, w) => `${w.accepted} / ${w.rejected} / ${w.warnings}`
        },
        {
          title: "原因与下一步",
          key: "diagnosis",
          render: (f, w) => w.diagnosis ? /* @__PURE__ */ h(O, { direction: "vertical", size: 0, children: [
            /* @__PURE__ */ i(E.Text, { children: w.diagnosis.message }),
            /* @__PURE__ */ i(E.Text, { type: "secondary", children: w.diagnosis.action }),
            w.error && /* @__PURE__ */ i(E.Text, { type: "secondary", children: w.error })
          ] }) : w.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    a && /* @__PURE__ */ i(fe, { type: "error", showIcon: !0, message: a }),
    /* @__PURE__ */ i(
      ie,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        expandable: { expandedRowRender: o },
        columns: [
          { title: "文件", dataIndex: "path" },
          { title: "源日期", dataIndex: "sourceDate" },
          { title: "状态", dataIndex: "status" },
          { title: "大小", dataIndex: "length", render: (c) => `${c} B` },
          {
            title: "目标数",
            key: "targets",
            render: (c, f) => f.deliveries.length
          }
        ]
      }
    )
  ] });
}
const et = (e) => {
  const t = ne(e);
  return Array.isArray(t) ? t : [];
};
function Pr({ block: e, ctx: t }) {
  const [r, n] = m([]), [a, d] = m([]), [s, o] = m(""), [c, f] = m(""), [w, b] = m(null), k = z(async () => {
    try {
      n(et(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/machines")));
      const u = et(await t.hubQuery("industrial/resources"));
      d(u);
    } catch (u) {
      b(u instanceof Error ? u.message : String(u));
    }
  }, [e == null ? void 0 : e.query, t]);
  W(() => {
    k();
  }, [k]);
  const x = z(async () => {
    if (!s.trim()) {
      b("请输入设备名称");
      return;
    }
    b(null);
    try {
      await t.hubCommand("industrial/save-machine", {
        id: `machine-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 12) : Date.now().toString(36)}`,
        name: s.trim(),
        endpoint: c.trim(),
        enabled: !0
      }), o(""), f(""), await k();
    } catch (u) {
      b(u instanceof Error ? u.message : String(u));
    }
  }, [t, c, k, s]), y = z(async (u, S) => {
    b(null);
    try {
      await t.hubCommand("industrial/save-resource", {
        id: u.id,
        machineId: S || null
      }), await k();
    } catch ($) {
      b($ instanceof Error ? $.message : String($));
    }
  }, [t, k]);
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "设备与资源" }),
    w && /* @__PURE__ */ i(fe, { type: "error", showIcon: !0, message: w }),
    /* @__PURE__ */ h(O, { wrap: !0, children: [
      /* @__PURE__ */ i(
        M,
        {
          "aria-label": "设备名称",
          placeholder: "设备名称",
          value: s,
          onChange: (u) => o(u.target.value)
        }
      ),
      /* @__PURE__ */ i(
        M,
        {
          "aria-label": "设备地址",
          placeholder: "地址 / IP",
          value: c,
          onChange: (u) => f(u.target.value)
        }
      ),
      /* @__PURE__ */ i(R, { type: "primary", onClick: () => void x(), children: "新增设备" })
    ] }),
    /* @__PURE__ */ i(
      ie,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        columns: [
          { title: "设备", dataIndex: "name" },
          { title: "地址", dataIndex: "endpoint" },
          { title: "状态", dataIndex: "enabled", render: (u) => u ? "启用" : "停用" }
        ]
      }
    ),
    /* @__PURE__ */ i(E.Title, { level: 5, style: { margin: 0 }, children: "资源归属" }),
    /* @__PURE__ */ i(
      ie,
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
            render: (u, S) => /* @__PURE__ */ i(
              pe,
              {
                "aria-label": `资源 ${S.name} 的设备`,
                style: { width: 220 },
                value: S.machineId ?? void 0,
                allowClear: !0,
                onChange: ($) => void y(S, $ ?? ""),
                options: r.map(($) => ({ value: $.id, label: $.name }))
              }
            )
          }
        ]
      }
    )
  ] });
}
const Rr = ["filesystem.local", "filesystem.unc"], _r = ["sink.sqlite", "sink.sqlserver", "sink.postgresql", "sink.mysql"];
function Or({ block: e, ctx: t }) {
  const [r, n] = m([]), [a, d] = m([]), [s, o] = m(""), [c, f] = m(""), [w, b] = m("csv"), [k, x] = m([]), [y, u] = m(null), [S, $] = m(!1);
  W(() => {
    (async () => {
      try {
        n(dt(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"))), d(ct(await t.hubQuery((e == null ? void 0 : e.nodesQuery) ?? "industrial/nodes")));
      } catch (p) {
        u(p instanceof Error ? p.message : String(p));
      }
    })();
  }, [e == null ? void 0 : e.nodesQuery, e == null ? void 0 : e.query, t]);
  const H = r.filter((p) => Rr.includes(p.type)), I = r.filter((p) => _r.some((C) => p.type === C.split(".")[1])), _ = Me(
    () => new Map(a.map((p) => [p.nodeTypeId, p])),
    [a]
  ), A = z(async () => {
    var p;
    if (!s.trim()) {
      u("请输入任务名称");
      return;
    }
    if (!c) {
      u("请选择数据来源");
      return;
    }
    if (k.length === 0) {
      u("请至少选择一个目标");
      return;
    }
    $(!0), u(null);
    try {
      const T = `quick-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`, ee = `industrial.${r.find((V) => V.id === c).type}`, me = `industrial.reader.${w}`, N = (V, oe, he, Ie, we) => ({ id: V, type: oe, config: we, x: he, y: Ie }), F = (V, oe) => {
        var we, be;
        const he = _.get(se[V]), Ie = _.get(se[oe]);
        return {
          source: V,
          sourcePort: ((we = he.outputs[0]) == null ? void 0 : we.name) ?? "output",
          target: oe,
          targetPort: ((be = Ie.inputs[0]) == null ? void 0 : be.name) ?? "input"
        };
      }, Y = [
        N("src", ee, 80, 120, { resourceId: c }),
        N("read", me, 280, 120, {}),
        ...k.map((V, oe) => N(
          `sink${oe + 1}`,
          `industrial.sink.${r.find((he) => he.id === V).type}`,
          500,
          80 + oe * 120,
          { targetResourceId: V }
        ))
      ], se = Object.fromEntries(Y.map((V) => [V.id, V.type])), j = [F("src", "read"), ...k.map((V, oe) => F("read", `sink${oe + 1}`))], te = {
        ...$e(T),
        name: s.trim(),
        nodes: Y,
        edges: j,
        resourceReferences: [c, ...k],
        layout: { lookbackDays: 1 }
      };
      await t.hubCommand("industrial/save-workflow", te), await t.hubCommand("industrial/publish-workflow", { workflowId: T }), (p = t.navigate) == null || p.call(t, `/industrial/designer?workflowId=${encodeURIComponent(T)}`);
    } catch (C) {
      u(C instanceof Error ? C.message : String(C));
    } finally {
      $(!1);
    }
  }, [t, w, _, s, r, c, k]);
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "快速创建采集任务" }),
    /* @__PURE__ */ i(E.Text, { type: "secondary", children: "生成标准工作流，可在设计器中继续修改。" }),
    y && /* @__PURE__ */ i(fe, { type: "error", showIcon: !0, message: y }),
    /* @__PURE__ */ i(
      M,
      {
        "aria-label": "任务名称",
        placeholder: "任务名称",
        value: s,
        onChange: (p) => o(p.target.value)
      }
    ),
    /* @__PURE__ */ h(O, { wrap: !0, children: [
      /* @__PURE__ */ i(
        pe,
        {
          "aria-label": "数据来源",
          style: { width: 260 },
          placeholder: "数据来源",
          value: c || void 0,
          onChange: (p) => f(p),
          options: H.map((p) => ({ value: p.id, label: `${p.name} (${p.type})` }))
        }
      ),
      /* @__PURE__ */ i(
        pe,
        {
          "aria-label": "数据格式",
          style: { width: 160 },
          value: w,
          onChange: (p) => b(p),
          options: ["csv", "excel", "json"].map((p) => ({ value: p, label: p.toUpperCase() }))
        }
      )
    ] }),
    /* @__PURE__ */ i(
      pe,
      {
        "aria-label": "目标资源",
        mode: "multiple",
        style: { width: 420 },
        placeholder: "目标资源",
        value: k,
        onChange: (p) => x(p),
        options: I.map((p) => ({ value: p.id, label: `${p.name} (${p.type})` }))
      }
    ),
    /* @__PURE__ */ i(R, { type: "primary", loading: S, onClick: () => void A(), children: "创建并发布" })
  ] });
}
const zr = `
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
function Ar(e) {
  bt(e);
  const t = typeof document > "u" ? null : document.createElement("style");
  t && (t.dataset.industrialDesigner = "true", t.textContent = zr, document.head.appendChild(t));
  const r = ({ ctx: d }) => Fe(kr, { ctx: d });
  r.displayName = "IndustrialWorkflowCanvas";
  const n = (d, s) => {
    const o = ({ block: c, ctx: f }) => Fe(s, { block: c, ctx: f });
    return o.displayName = d, e.blocks.register(d, o);
  }, a = [
    e.blocks.register("industrial-workflow-canvas", r),
    n("industrial-workflow-list", Sr),
    n("industrial-resource-console", Nr),
    n("industrial-credential-console", Er),
    n("industrial-execution-center", $r),
    n("industrial-file-details", Dr),
    n("industrial-machine-console", Pr),
    n("industrial-collection-wizard", Or)
  ];
  return () => {
    for (const d of a) d == null || d();
    t == null || t.remove();
  };
}
export {
  Ar as default,
  Ar as register
};
