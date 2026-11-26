let we = null;
function wt(e) {
  we = e;
}
function X() {
  if (!we && typeof globalThis < "u" && (we = globalThis.__CORDIS_CONSOLE ?? null), !we)
    throw new Error("[industrial-designer] console facade is not available");
  return we;
}
const i = (...e) => X().jsxRuntime.jsx(...e), h = (...e) => X().jsxRuntime.jsxs(...e), xe = (e) => X().jsxRuntime.jsx(X().jsxRuntime.Fragment, e), $e = (...e) => X().React.createElement(...e), z = (...e) => X().React.useCallback(...e), J = (...e) => X().React.useEffect(...e), Xe = (...e) => X().React.useMemo(...e), ie = (...e) => X().React.useRef(...e), b = (...e) => X().React.useState(...e), A = (e) => {
  const t = (r) => {
    const n = e.split(".").reduce((a, d) => a == null ? void 0 : a[d], X().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return $e(n, r ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, pe = A("Alert"), $ = A("Button"), Ue = A("Divider"), vt = A("Drawer"), Ze = A("Empty"), Re = Object.assign(A("Form"), {
  Item: A("Form.Item"),
  useForm: (...e) => X().antd.Form.useForm(...e)
}), K = Object.assign(A("Input"), {
  Number: A("InputNumber"),
  TextArea: A("Input.TextArea")
}), et = A("InputNumber"), bt = Object.assign(A("Layout"), {
  Header: A("Layout.Header"),
  Content: A("Layout.Content")
}), ve = A("Modal"), ge = A("Select"), _ = A("Space"), tt = A("Spin"), rt = A("Switch"), le = A("Table"), de = A("Tag"), E = Object.assign(A("Typography"), {
  Title: A("Typography.Title"),
  Text: A("Typography.Text")
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
      const a = t.get(n.type), d = (a == null ? void 0 : a.category) ?? "General", o = r === n.id;
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
          stroke: o ? "#111111" : We(d),
          lineWidth: o ? 3 : 1.5,
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
    const o = d.style.x, s = d.style.y;
    typeof o == "number" && typeof s == "number" && (a[d.id] = { x: o, y: s });
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
    behaviors: it()
  };
}
const Ct = "__industrial_drag_target__", Nt = "__industrial_connect_source__", Pe = (e, t) => {
  var r;
  return (((r = e.isPanModifier) == null ? void 0 : r.call(e)) ?? !1) || t.button === 1 || t.buttons === 4;
}, Qe = (e, t) => {
  var w, x;
  if (t.targetType !== "node" || !((w = t.target) != null && w.id)) return !1;
  const r = ((x = e.gestureOrigin) == null ? void 0 : x.call(e)) ?? t.client ?? t.canvas;
  if (!r || !e.graph) return !1;
  const n = String(t.target.id), a = e.graph.getNodeData().find((S) => String(S.id) === n), d = a == null ? void 0 : a.style;
  if (typeof (d == null ? void 0 : d.x) != "number" || typeof d.y != "number") return !1;
  const [o, s] = e.graph.getCanvasByClient([r.x, r.y]), c = Math.abs(o - d.x), u = Math.abs(s - d.y);
  return Math.abs(c - nt / 2) <= 14 && u <= at / 2 + 10;
};
function it(e = {}) {
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
        var o, s;
        const n = e.graph;
        if (!n) return;
        const a = Array.isArray(r) ? r : r ? [String(r)] : void 0, d = n.getNodeData().map((c) => ({
          id: String(c.id),
          data: c.data ?? {},
          style: c.style ?? {}
        }));
        (s = e.onPositions) == null || s.call(e, St(
          { nodes: d },
          ((o = e.allowedIds) == null ? void 0 : o.call(e)) ?? /* @__PURE__ */ new Set(),
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
        var o;
        t.armed = !1;
        const n = String(r.source ?? ""), a = String(r.target ?? "");
        if (!n || !a || n === a) return;
        const d = (o = e.onCreateEdge) == null ? void 0 : o.call(e, n, a);
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
    onCreateEdge: o,
    onPositions: s
  } = e, c = ie(null), u = ie(null), w = ie(!1), x = ie(!1), S = ie({ armed: !1 }), I = ie(null), y = ie({ onSelect: a, onDropNode: d, onCreateEdge: o, onPositions: s });
  y.current = { onSelect: a, onDropNode: d, onCreateEdge: o, onPositions: s };
  const p = Xe(
    () => kt(t, r, n),
    [t, r, n]
  ), T = ie(p);
  T.current = p;
  const O = ie(/* @__PURE__ */ new Set());
  return O.current = new Set(t.nodes.map((v) => v.id)), J(() => {
    const v = (g) => {
      g.code === "Space" && !g.repeat && (x.current = !0);
    }, R = (g) => {
      g.code === "Space" && (x.current = !1);
    }, j = () => {
      x.current = !1;
    };
    return window.addEventListener("keydown", v), window.addEventListener("keyup", R), window.addEventListener("blur", j), () => {
      window.removeEventListener("keydown", v), window.removeEventListener("keyup", R), window.removeEventListener("blur", j);
    };
  }, []), J(() => {
    const v = c.current;
    if (!v || w.current) return;
    let R = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: j }) => {
      if (R || !c.current || w.current) return;
      const g = Tt(T.current, v), C = new j({
        ...g,
        behaviors: it({
          graph: {
            getNodeData: () => {
              var k;
              return ((k = u.current) == null ? void 0 : k.getNodeData()) ?? [];
            },
            getCanvasByClient: (k) => {
              var re;
              const B = ((re = u.current) == null ? void 0 : re.getCanvasByClient(k)) ?? k;
              return [B[0], B[1]];
            }
          },
          isPanModifier: () => x.current,
          gestureOrigin: () => I.current,
          connectionState: S.current,
          allowedIds: () => O.current,
          onPositions: (k) => y.current.onPositions(k),
          onCreateEdge: (k, B) => y.current.onCreateEdge(k, B)
        })
      });
      C.on("node:click", (k) => {
        const B = Et(k);
        B && y.current.onSelect(B);
      }), C.on("edge:click", () => y.current.onSelect(null)), C.on("canvas:click", () => y.current.onSelect(null)), C.on("pointerdown", (k) => {
        const B = k;
        I.current = B.client ? { x: B.client.x, y: B.client.y } : null;
      }), C.on("pointerup", () => {
        queueMicrotask(() => {
          S.current.armed = !1, I.current = null;
        });
      }), u.current = C, v.__industrialGraph = C, w.current = !0, C.render();
    }), () => {
      R = !0;
    };
  }, []), J(() => {
    const v = u.current;
    !v || !w.current || (v.setData(p), v.draw());
  }, [p]), J(() => () => {
    var v;
    (v = u.current) == null || v.destroy(), u.current = null, w.current = !1;
  }, []), /* @__PURE__ */ i("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ h("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ i(
      "div",
      {
        ref: c,
        className: "industrial-graph-host",
        onDragOver: (v) => v.preventDefault(),
        onDrop: (v) => {
          v.preventDefault();
          const R = v.dataTransfer.getData("application/x-industrial-node") || v.dataTransfer.getData("text/plain");
          if (!R) return;
          const j = u.current, g = c.current;
          if (!j || !g) return;
          const C = g.getBoundingClientRect(), k = j.getCanvasByViewport([
            v.clientX - C.left,
            v.clientY - C.top
          ]);
          y.current.onDropNode(R, k[0], k[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ i("div", { className: "industrial-graph-hint", children: "拖动节点移动 · 从端口拖出连线 · 拖动画布平移（空格或中键可在节点上平移）" })
  ] }) });
}
function Dt({ descriptors: e, loading: t, error: r, onAdd: n }) {
  const a = /* @__PURE__ */ new Map();
  for (const o of e) {
    const s = o.category.split(".")[0] || "General", c = a.get(s) ?? [];
    c.push(o), a.set(s, c);
  }
  const d = (o, s) => {
    o.dataTransfer.setData("application/x-industrial-node", s.nodeTypeId), o.dataTransfer.setData("text/plain", s.nodeTypeId), o.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ h("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ i(E.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ i(tt, { size: "small", style: { display: "block", marginTop: 16 } }),
    r && /* @__PURE__ */ i(E.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: r }),
    !t && !r && e.length === 0 && /* @__PURE__ */ i(Ze, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(a.entries()).map(([o, s]) => /* @__PURE__ */ h("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ i(E.Text, { strong: !0, style: { fontSize: 12 }, children: o }),
      s.map((c) => /* @__PURE__ */ h(
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
            /* @__PURE__ */ i("div", { className: "industrial-palette-node-title", children: c.name }),
            /* @__PURE__ */ h("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ i(de, { bordered: !1, color: "blue", children: c.version }),
              /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 11 }, children: c.nodeTypeId })
            ] }),
            /* @__PURE__ */ h("div", { className: "industrial-palette-node-ports", children: [
              c.inputs.map((u) => /* @__PURE__ */ h(de, { bordered: !1, color: "default", children: [
                "in ",
                u.name,
                ": ",
                u.type
              ] }, `in-${u.name}`)),
              c.outputs.map((u) => /* @__PURE__ */ h(de, { bordered: !1, color: "green", children: [
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
    ] }, o))
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
  const o = fe(r, a.type), s = fe(r, d.type);
  if (!o || !s)
    return { definition: e, issue: H(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const c = ke(o, t.sourcePort, "outputs"), u = ke(s, t.targetPort, "inputs");
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
function Ft(e, t, r) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === t ? { ...n, config: r } : n)
  };
}
function Lt(e, t) {
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
  for (const o of e.nodes) t.set(o.id, []);
  for (const o of e.edges)
    (d = t.get(o.source)) == null || d.push(o.target);
  const r = /* @__PURE__ */ new Map(), n = [], a = (o) => {
    r.set(o, 1), n.push(o);
    for (const s of t.get(o) ?? []) {
      if (r.get(s) === 1)
        return [...n.slice(n.indexOf(s)), s];
      if (r.get(s) !== 2) {
        const c = a(s);
        if (c) return c;
      }
    }
    return n.pop(), r.set(o, 2), null;
  };
  for (const o of e.nodes)
    if (r.get(o.id) !== 2) {
      const s = a(o.id);
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
      const x = s.config[w];
      (x == null || x === "") && n.push(H(
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
    const x = d.get(s.source), S = d.get(s.target);
    if (!x || !S) return;
    const I = ke(x, s.sourcePort, "outputs"), y = ke(S, s.targetPort, "inputs");
    if (!I || !y) {
      n.push(H(`edges.${c}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    Ae(I.type, y.type) || n.push(H(
      `edges.${c}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${I.type} → ${y.type} is not a legal connection`
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
  const o = qt(e);
  return o && n.push(H(`nodes.${o[0]}`, "GRAPH_CYCLE", `cycle: ${o.join(" → ")}`)), n;
}
const Oe = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function Bt(e) {
  const t = Oe(e.additionalProperties).enum;
  if (!Array.isArray(t)) return null;
  const r = t.filter((n) => typeof n == "string");
  return r.length > 0 ? r : null;
}
function ot(e) {
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
    if (ot(a) === "key-value")
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
  const o = JSON.stringify(e ?? {}), [s, c] = b(() => ze(e)), [u, w] = b(o);
  J(() => {
    o !== u && (c(ze(e)), w(o));
  }, [o, u, e]);
  const x = (I) => {
    c(I), t == null || t(Ye(I).record);
  }, { duplicateKeys: S } = Ye(s);
  return /* @__PURE__ */ h("div", { "data-testid": d, className: "industrial-kv-editor", children: [
    s.map((I, y) => /* @__PURE__ */ h("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ i(
        K,
        {
          "aria-label": `${n} ${y + 1}`,
          value: I.key,
          placeholder: n,
          onChange: (p) => x(s.map((T, O) => O === y ? { ...T, key: p.target.value } : T))
        }
      ),
      r && r.length > 0 ? /* @__PURE__ */ i(
        ge,
        {
          "aria-label": `${a} ${y + 1}`,
          value: I.value || void 0,
          placeholder: a,
          options: r.map((p) => ({ label: p, value: p })),
          onChange: (p) => x(s.map((T, O) => O === y ? { ...T, value: p ?? "" } : T))
        }
      ) : /* @__PURE__ */ i(
        K,
        {
          "aria-label": `${a} ${y + 1}`,
          value: I.value,
          placeholder: a,
          onChange: (p) => x(s.map((T, O) => O === y ? { ...T, value: p.target.value } : T))
        }
      ),
      /* @__PURE__ */ i(
        $,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${y + 1}`,
          onClick: () => x(s.filter((p, T) => T !== y)),
          children: "删"
        }
      )
    ] }, y)),
    /* @__PURE__ */ i(
      $,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => x([...s, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    S.length > 0 && /* @__PURE__ */ h(E.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      S.join("、")
    ] })
  ] });
}
function Gt({ value: e, onChange: t, suggestions: r = [], testId: n }) {
  const a = JSON.stringify(Ie(e)), [d, o] = b(() => Ie(e)), [s, c] = b(a);
  J(() => {
    a !== s && (o(Ie(e)), c(a));
  }, [a, s, e]);
  const u = (w) => {
    o(w), t == null || t(Ie(w));
  };
  return /* @__PURE__ */ h("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ i("datalist", { id: `${n ?? "list"}-suggestions`, children: r.map((w) => /* @__PURE__ */ i("option", { value: w }, w)) }),
    d.map((w, x) => /* @__PURE__ */ h("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ i(
        K,
        {
          "aria-label": `字段 ${x + 1}`,
          value: w,
          list: `${n ?? "list"}-suggestions`,
          onChange: (S) => u(d.map((I, y) => y === x ? S.target.value : I))
        }
      ),
      /* @__PURE__ */ i(
        $,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${x + 1}`,
          onClick: () => u(d.filter((S, I) => I !== x)),
          children: "删"
        }
      )
    ] }, x)),
    /* @__PURE__ */ i($, { size: "small", type: "dashed", block: !0, onClick: () => u([...d, ""]), children: "添加字段" }),
    d.length === 0 && /* @__PURE__ */ i(E.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Wt({ value: e, onChange: t }) {
  const r = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, a] = b(r), [d, o] = b(null);
  J(() => {
    a(r), o(null);
  }, [r]);
  const s = (c) => {
    a(c);
    try {
      const u = JSON.parse(c);
      o(null), t == null || t(u);
    } catch (u) {
      o(u.message);
    }
  };
  return /* @__PURE__ */ h("div", { children: [
    /* @__PURE__ */ i(K.TextArea, { rows: 5, value: n, onChange: (c) => s(c.target.value) }),
    d && /* @__PURE__ */ i(E.Text, { type: "danger", style: { fontSize: 11 }, children: d })
  ] });
}
function Qt({ value: e, onChange: t, resources: r, resourceType: n, label: a }) {
  const d = r.filter((s) => s.enabled !== !1 && (!n || s.type === n)).map((s) => ({
    value: s.id,
    label: `${s.name} (${s.type})`
  })), o = r.find((s) => s.id === e);
  return /* @__PURE__ */ h("div", { children: [
    /* @__PURE__ */ h(
      "select",
      {
        "aria-label": a,
        value: e ?? "",
        onChange: (s) => t == null ? void 0 : t(s.target.value || void 0),
        children: [
          /* @__PURE__ */ i("option", { value: "", children: "未选择" }),
          d.map((s) => /* @__PURE__ */ i("option", { value: s.value, children: s.label }, s.value))
        ]
      }
    ),
    (o == null ? void 0 : o.credentialId) && /* @__PURE__ */ h(E.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      o.credentialName ?? o.credentialId
    ] })
  ] });
}
function Jt({ name: e, schema: t, required: r, resources: n, resourceType: a, properties: d, config: o }) {
  let s, c = "value";
  switch (ot(t)) {
    case "resource":
      s = /* @__PURE__ */ i(
        Qt,
        {
          resources: n,
          resourceType: a,
          label: String(t.title ?? e)
        }
      );
      break;
    case "enum":
      s = /* @__PURE__ */ i(
        ge,
        {
          allowClear: !0,
          options: (t.enum ?? []).map((u) => ({ label: String(u), value: u }))
        }
      );
      break;
    case "number":
      s = /* @__PURE__ */ i(et, { style: { width: "100%" } });
      break;
    case "boolean":
      s = /* @__PURE__ */ i(rt, {}), c = "checked";
      break;
    case "key-value":
      s = /* @__PURE__ */ i(
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
      s = /* @__PURE__ */ i(Gt, { testId: `field-${e}`, suggestions: Kt(d, o) });
      break;
    case "json":
      s = /* @__PURE__ */ i(Wt, {});
      break;
    default:
      s = /* @__PURE__ */ i(K, { placeholder: t.description });
      break;
  }
  return /* @__PURE__ */ i(
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
    resources: o,
    issues: s,
    onApplyConfig: c,
    onDeleteNode: u,
    onDeleteEdge: w,
    onCreateEdge: x,
    onUpdateEdge: S,
    onSelectNode: I
  } = e, [y] = Re.useForm(), [p, T] = b("{}"), [O, Z] = b(null), [v, R] = b(), [j, g] = b();
  J(() => {
    y.setFieldsValue((t == null ? void 0 : t.config) ?? {}), T(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), Z(null);
  }, [y, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const C = new Set((r == null ? void 0 : r.configurationSchema.required) ?? []), k = Object.entries((r == null ? void 0 : r.configurationSchema.properties) ?? {}), B = () => {
    if (k.length > 0) {
      y.validateFields().then((N) => c(jt((t == null ? void 0 : t.config) ?? {}, N)));
      return;
    }
    try {
      const N = JSON.parse(p);
      if (N === null || typeof N != "object" || Array.isArray(N))
        throw new Error("配置必须是 JSON 对象");
      Z(null), c(N);
    } catch (N) {
      Z(N.message);
    }
  }, re = a.map((N, q) => ({
    ...N,
    index: q,
    key: `${q}:${N.source}->${N.target}`
  })), ye = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (N, q) => /* @__PURE__ */ h(E.Text, { style: { fontSize: 11 }, children: [
        q.source,
        " → ",
        q.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (N, q) => {
        const V = d.get(q.source), ue = d.get(q.target);
        return /* @__PURE__ */ h("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ i(
            ge,
            {
              size: "small",
              value: q.sourcePort,
              style: { width: 110 },
              onChange: (F) => S(q.index, { ...q, sourcePort: F }),
              options: ((V == null ? void 0 : V.outputs) ?? []).map((F) => ({ label: `${F.name} (${F.type})`, value: F.name }))
            }
          ),
          /* @__PURE__ */ i(
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
      render: (N, q) => /* @__PURE__ */ i($, { danger: !0, size: "small", type: "text", onClick: () => w(q.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ h("div", { className: "industrial-properties-body", children: [
    !t && /* @__PURE__ */ i(Ze, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ h(xe, { children: [
      /* @__PURE__ */ i(E.Text, { strong: !0, children: (r == null ? void 0 : r.name) ?? t.type }),
      /* @__PURE__ */ i(de, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ i(Re, { form: y, layout: "vertical", size: "small", style: { marginTop: 12 }, children: k.map(([N, q]) => /* @__PURE__ */ i(
        Jt,
        {
          name: N,
          schema: q,
          required: C.has(N),
          resources: o,
          resourceType: typeof (r == null ? void 0 : r.configurationSchema.resourceType) == "string" ? r.configurationSchema.resourceType : void 0,
          properties: (r == null ? void 0 : r.configurationSchema.properties) ?? {},
          config: (t == null ? void 0 : t.config) ?? {}
        },
        N
      )) }),
      k.length === 0 && /* @__PURE__ */ h(xe, { children: [
        /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ i(
          K.TextArea,
          {
            rows: 8,
            value: p,
            style: { marginTop: 6 },
            onChange: (N) => T(N.target.value)
          }
        ),
        O && /* @__PURE__ */ i(E.Text, { type: "danger", style: { fontSize: 12 }, children: O })
      ] }),
      /* @__PURE__ */ i($, { type: "primary", block: !0, onClick: B, children: "应用配置" }),
      /* @__PURE__ */ i($, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: u, children: "删除节点" }),
      (r == null ? void 0 : r.activityType) && /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: r.activityType })
    ] }),
    /* @__PURE__ */ h(Ue, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      a.length,
      ")"
    ] }),
    n.length > 1 && /* @__PURE__ */ h(_, { wrap: !0, style: { marginBottom: 8 }, children: [
      /* @__PURE__ */ h(
        "select",
        {
          "aria-label": "连线起点",
          value: v ?? "",
          onChange: (N) => R(N.target.value || void 0),
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
          value: j ?? "",
          onChange: (N) => g(N.target.value || void 0),
          children: [
            /* @__PURE__ */ i("option", { value: "", children: "终点节点" }),
            n.map((N) => /* @__PURE__ */ i("option", { value: N.id, children: N.id }, N.id))
          ]
        }
      ),
      /* @__PURE__ */ i(
        $,
        {
          size: "small",
          disabled: !v || !j || v === j,
          onClick: () => {
            v && j && x(v, j);
          },
          children: "添加连线"
        }
      )
    ] }),
    a.length === 0 ? /* @__PURE__ */ i(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ i(le, { size: "small", columns: ye, dataSource: re, pagination: !1 }),
    s.length > 0 && /* @__PURE__ */ h(xe, { children: [
      /* @__PURE__ */ h(Ue, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        s.length,
        ")"
      ] }),
      s.map((N, q) => /* @__PURE__ */ h("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ i(de, { color: "red", children: N.code }),
        /* @__PURE__ */ h(
          E.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const V = /^nodes\.([^.]+)/.exec(N.path);
              V && I(V[1]);
            },
            children: [
              N.path,
              ": ",
              N.message
            ]
          }
        )
      ] }, `${N.path}-${q}`))
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
const D = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, U = (e) => Array.isArray(e) ? e : [], f = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", L = (e, t = 0) => {
  const r = typeof e == "string" ? Number(e) : e;
  return typeof r == "number" && Number.isFinite(r) ? r : t;
}, l = (e, ...t) => {
  for (const r of t)
    if (e[r] !== void 0 && e[r] !== null) return e[r];
};
function te(e) {
  const t = D(e);
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
  const t = D(e), r = f(l(t, "name", "port", "id")).trim();
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
  const t = D(e);
  return Object.keys(t).length === 0 ? { type: "object", properties: {} } : t;
};
function tr(e) {
  const t = D(e), r = f(l(t, "nodeTypeId", "node_type_id", "type", "id")).trim();
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
  const t = te(e), r = Array.isArray(t) ? t : U(l(D(t), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return r.map(tr).filter((a) => a !== null).filter((a) => n.has(a.nodeTypeId) ? !1 : (n.add(a.nodeTypeId), !0));
}
function nr(e) {
  const t = te(e), r = Array.isArray(t) ? t : U(l(D(t), "resources", "items")), n = /* @__PURE__ */ new Set();
  return r.map((a) => {
    const d = D(a), o = f(l(d, "id")).trim();
    return !o || n.has(o) ? null : (n.add(o), {
      id: o,
      name: f(l(d, "name"), o),
      type: f(l(d, "type"), "unknown"),
      enabled: d.enabled !== !1,
      credentialId: typeof d.credentialId == "string" ? d.credentialId : null,
      credentialName: typeof d.credentialName == "string" ? d.credentialName : null
    });
  }).filter((a) => a !== null);
}
function st(e) {
  const t = D(e), r = f(l(t, "workflowId", "workflow_id", "id")).trim();
  return r ? {
    workflowId: r,
    name: f(l(t, "name", "title"), r),
    version: L(l(t, "version"), 1)
  } : null;
}
function lt(e) {
  const t = te(e);
  return (Array.isArray(t) ? t : U(l(D(t), "workflows", "items"))).map(st).filter((n) => n !== null);
}
function Se(e) {
  const t = D(e), r = {};
  for (const [n, a] of Object.entries(t))
    a !== void 0 && (r[n] = a);
  return r;
}
function ar(e) {
  const t = D(e), r = f(l(t, "id", "nodeId", "node_id")).trim();
  if (!r) return null;
  const n = D(l(t, "position", "layout"));
  return {
    id: r,
    type: f(l(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: Se(l(t, "config", "configuration")),
    x: L(l(t, "x") ?? l(n, "x"), 0),
    y: L(l(t, "y") ?? l(n, "y"), 0)
  };
}
function ir(e) {
  const t = D(e), r = f(l(t, "source", "from", "sourceId")).trim(), n = f(l(t, "target", "to", "targetId")).trim();
  return !r || !n ? null : {
    source: r,
    sourcePort: f(l(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: f(l(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function or(e) {
  const t = te(e), r = D(t), n = st(r) ?? { workflowId: "", name: "", version: 1 }, a = U(l(r, "nodes")).map(ar).filter((c) => c !== null), d = U(l(r, "edges")).map(ir).filter((c) => c !== null), o = D(l(r, "trigger")), s = {
    kind: f(l(o, "kind", "type"), "manual"),
    cron: l(o, "cron") === void 0 ? null : f(o.cron),
    eventName: l(o, "eventName", "event_name") === void 0 ? null : f(l(o, "eventName", "event_name")),
    config: Se(l(o, "config"))
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
  const t = te(e);
  return (Array.isArray(t) ? t : U(l(D(t), "resources", "items"))).map((n) => {
    const a = D(n), d = l(a, "credentialId", "credential_id");
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
  const t = te(e);
  return (Array.isArray(t) ? t : U(l(D(t), "credentials", "items"))).map((n) => {
    const a = D(n);
    return {
      id: f(l(a, "id"), "—"),
      name: f(l(a, "name"), "—"),
      kind: f(l(a, "kind"), "—"),
      version: L(l(a, "version"))
    };
  });
}
function lr(e) {
  const t = te(e);
  return (Array.isArray(t) ? t : U(l(D(t), "tests", "items"))).map((n) => {
    const a = D(n);
    return {
      resourceId: f(l(a, "resourceId", "resource_id"), "—"),
      ok: he(l(a, "ok")),
      detail: f(l(a, "detail"), "")
    };
  });
}
const dt = (e) => {
  if (e == null) return null;
  const t = D(e);
  return Object.keys(t).length === 0 ? null : {
    code: f(l(t, "code"), "OTHER"),
    message: f(l(t, "message"), ""),
    action: f(l(t, "action"), "")
  };
};
function cr(e) {
  const t = te(e);
  return U(l(D(t), "triggers", "items")).map((n) => {
    const a = D(n), d = (o) => l(a, o) == null ? null : f(l(a, o));
    return {
      workflowId: f(l(a, "workflowId", "workflow_id"), "—"),
      version: L(l(a, "version")),
      kind: f(l(a, "kind"), "manual"),
      cron: d("cron"),
      eventName: d("eventName"),
      timezone: d("timezone"),
      resourceId: d("resourceId"),
      nextRun: d("nextRun"),
      lastStatus: d("lastStatus"),
      lastStartedAt: d("lastStartedAt"),
      lastFireStatus: d("lastFireStatus"),
      lastFireDueAt: d("lastFireDueAt"),
      lastFireAttempts: l(a, "lastFireAttempts") == null ? null : L(l(a, "lastFireAttempts")),
      lastError: d("lastError")
    };
  });
}
function dr(e) {
  if (e == null) return null;
  const t = te(e), r = D(t);
  if (Object.keys(r).length === 0) return null;
  const n = U(l(r, "files")).map((d) => {
    const o = D(d);
    return {
      path: f(l(o, "path"), "—"),
      sourceDate: f(l(o, "sourceDate"), ""),
      inLedger: he(l(o, "inLedger")),
      expectedTargets: L(l(o, "expectedTargets")),
      succeededTargets: L(l(o, "succeededTargets")),
      incompleteTargets: L(l(o, "incompleteTargets")),
      missingTargets: f(l(o, "missingTargets"), ""),
      wouldSkip: he(l(o, "wouldSkip")),
      wouldCollect: he(l(o, "wouldCollect")),
      duplicateRisk: he(l(o, "duplicateRisk"))
    };
  }), a = D(l(r, "summary"));
  return {
    resourceId: f(l(r, "resourceId"), "—"),
    from: f(l(r, "from"), ""),
    to: f(l(r, "to"), ""),
    files: n,
    summary: {
      discovered: L(l(a, "discovered")),
      alreadyComplete: L(l(a, "alreadyComplete")),
      needsCollection: L(l(a, "needsCollection")),
      duplicateRisk: L(l(a, "duplicateRisk"))
    }
  };
}
function ur(e) {
  const t = te(e);
  return (Array.isArray(t) ? t : U(l(D(t), "files", "items"))).map((n) => {
    const a = D(n);
    return {
      id: f(l(a, "id"), "—"),
      path: f(l(a, "path"), "—"),
      identityKey: f(l(a, "identityKey"), ""),
      sourceResourceId: f(l(a, "sourceResourceId"), ""),
      length: L(l(a, "length")),
      sourceDate: f(l(a, "sourceDate"), ""),
      collectedAt: f(l(a, "collectedAt"), ""),
      status: f(l(a, "status"), "—"),
      deliveries: U(l(a, "deliveries")).map((d) => {
        const o = D(d);
        return {
          id: f(l(o, "id"), "—"),
          collectionFileId: f(l(o, "collectionFileId"), ""),
          targetId: f(l(o, "targetId"), "—"),
          targetKind: f(l(o, "targetKind"), "record"),
          status: f(l(o, "status"), "—"),
          attempts: L(l(o, "attempts")),
          records: L(l(o, "records")),
          error: l(o, "error") == null ? null : f(l(o, "error")),
          accepted: L(l(o, "accepted")),
          rejected: L(l(o, "rejected")),
          warnings: L(l(o, "warnings")),
          diagnosis: dt(l(o, "diagnosis"))
        };
      })
    };
  });
}
function pr(e) {
  const t = te(e), r = D(t), n = U(l(r, "runs")).map((d) => {
    const o = D(d);
    return {
      id: f(l(o, "id"), "—"),
      workflowId: f(l(o, "workflowId", "workflow_id"), "—"),
      status: f(l(o, "status"), "—"),
      startedAt: f(l(o, "startedAt", "started_at"), ""),
      finishedAt: l(o, "finishedAt", "finished_at") == null ? null : f(l(o, "finishedAt", "finished_at")),
      error: l(o, "error") == null ? null : f(l(o, "error"))
    };
  }), a = U(l(r, "deliveries")).map((d) => {
    const o = D(d);
    return {
      id: f(l(o, "id"), "—"),
      collectionFileId: f(l(o, "collectionFileId", "collection_file_id"), ""),
      targetId: f(l(o, "targetId", "target_id"), "—"),
      targetKind: f(l(o, "targetKind", "target_kind"), "record"),
      status: f(l(o, "status"), "—"),
      attempts: L(l(o, "attempts")),
      records: L(l(o, "records")),
      error: l(o, "error") == null ? null : f(l(o, "error")),
      diagnosis: dt(l(o, "diagnosis"))
    };
  });
  return { runs: n, deliveries: a };
}
function fr(e) {
  if (e == null) return null;
  const t = te(e), r = D(t);
  if (Object.keys(r).length === 0) return null;
  const n = U(l(r, "nodes")).slice(0, 64).map((a) => {
    const d = D(a);
    return {
      nodeId: f(l(d, "nodeId", "node_id"), "—"),
      status: f(l(d, "status"), "—"),
      records: L(l(d, "records")),
      files: L(l(d, "files")),
      error: l(d, "error") == null ? null : f(l(d, "error"), void 0),
      samples: U(l(d, "samples")).slice(0, 3).map((o) => f(o))
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
  workflowId: o
}) {
  const [s, c] = b(r), [u, w] = b([]), [x, S] = b(null);
  J(() => {
    c(r);
  }, [r]), J(() => {
    e && d.hubQuery("industrial/trigger-status").then((p) => w(cr(p))).catch((p) => S(p instanceof Error ? p.message : String(p)));
  }, [d, e]);
  const I = u.find((p) => p.workflowId === o);
  return /* @__PURE__ */ i(
    ve,
    {
      open: e,
      title: "触发器配置",
      okText: "保存触发器",
      cancelText: "取消",
      onCancel: t,
      onOk: () => {
        var p, T;
        if (s.kind === "schedule" && !((p = s.cron) != null && p.trim())) {
          S("定时运行需要填写 cron 表达式");
          return;
        }
        if (s.kind === "event" && !((T = s.eventName) != null && T.trim())) {
          S("事件触发需要填写事件名称");
          return;
        }
        S(null), a(s), t();
      },
      width: 720,
      children: /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ h(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(E.Text, { children: "触发方式" }),
          /* @__PURE__ */ i(_, { wrap: !0, children: gr.map((p) => /* @__PURE__ */ i(
            $,
            {
              type: s.kind === p.value ? "primary" : "default",
              onClick: () => c({ ...s, kind: p.value }),
              children: p.label
            },
            p.value
          )) })
        ] }),
        /* @__PURE__ */ i(_, { wrap: !0, children: /* @__PURE__ */ i(
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
        /* @__PURE__ */ h(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(E.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ h(_, { wrap: !0, children: [
            /* @__PURE__ */ i(E.Text, { children: "常用" }),
            yr.map((p) => /* @__PURE__ */ i(
              $,
              {
                size: "small",
                onClick: () => c({ ...s, cron: p.cron }),
                children: p.label
              },
              p.cron
            ))
          ] }),
          /* @__PURE__ */ i(
            K,
            {
              "aria-label": "Cron 表达式",
              placeholder: "分 时 日 月 周，例如 0 2 * * *",
              value: s.cron ?? "",
              onChange: (p) => c({ ...s, cron: p.target.value })
            }
          ),
          /* @__PURE__ */ i(
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
          /* @__PURE__ */ i(
            K,
            {
              "aria-label": "事件名称",
              placeholder: "事件名称",
              value: s.eventName ?? "",
              onChange: (p) => c({ ...s, eventName: p.target.value })
            }
          ),
          /* @__PURE__ */ h(E.Text, { type: "secondary", children: [
            "下次运行：",
            (I == null ? void 0 : I.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        I && /* @__PURE__ */ i(
          le,
          {
            rowKey: "workflowId",
            dataSource: [I],
            pagination: !1,
            columns: [
              { title: "时区", dataIndex: "timezone", render: (p) => p ?? "UTC" },
              { title: "状态", dataIndex: "lastStatus", render: (p) => p ?? "未运行" },
              { title: "上次运行", dataIndex: "lastStartedAt", render: (p) => p ?? "—" },
              {
                title: "触发点",
                dataIndex: "lastFireStatus",
                render: (p, T) => T.lastFireStatus ? `${T.lastFireStatus} · ${T.lastFireDueAt ?? "—"} · ${T.lastFireAttempts ?? 0} 次` : "—"
              },
              { title: "错误", dataIndex: "lastError", render: (p) => p ?? "—" }
            ]
          }
        ),
        x && /* @__PURE__ */ i(pe, { type: "error", showIcon: !0, message: x })
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
  const [o, s] = b(null), [c, u] = b(null), [w, x] = b(!1), S = z(async () => {
    x(!0), u(null);
    try {
      await r.hubCommand("industrial/preview-backfill", {
        workflowId: n,
        resourceId: a,
        lookbackDays: d
      }), s(dr(await r.hubQuery("industrial/backfill-preview")));
    } catch (I) {
      u(I instanceof Error ? I.message : String(I));
    } finally {
      x(!1);
    }
  }, [r, d, a, n]);
  return /* @__PURE__ */ i(
    ve,
    {
      open: e,
      title: "补采预览",
      onCancel: t,
      width: 820,
      footer: [
        /* @__PURE__ */ i($, { loading: w, onClick: () => void S(), children: "重新扫描" }, "refresh"),
        /* @__PURE__ */ i($, { type: "primary", onClick: t, children: "关闭" }, "close")
      ],
      children: /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ h(_, { children: [
          /* @__PURE__ */ h($, { type: "primary", loading: w, onClick: () => void S(), children: [
            "扫描最近 ",
            d,
            " 天"
          ] }),
          /* @__PURE__ */ h(E.Text, { type: "secondary", children: [
            "资源：",
            a || "未选择"
          ] })
        ] }),
        c && /* @__PURE__ */ i(pe, { type: "error", showIcon: !0, message: c }),
        o && /* @__PURE__ */ h(xe, { children: [
          /* @__PURE__ */ h(E.Text, { children: [
            "发现 ",
            o.summary.discovered,
            " 个文件；已完成 ",
            o.summary.alreadyComplete,
            "； 待采集 ",
            o.summary.needsCollection,
            "；重复风险 ",
            o.summary.duplicateRisk,
            "。"
          ] }),
          /* @__PURE__ */ i(
            le,
            {
              rowKey: "path",
              dataSource: o.files,
              pagination: !1,
              size: "small",
              columns: [
                { title: "文件", dataIndex: "path" },
                { title: "源日期", dataIndex: "sourceDate" },
                { title: "账本", dataIndex: "inLedger", render: (I) => I ? "已登记" : "新文件" },
                { title: "当前目标", dataIndex: "expectedTargets" },
                { title: "成功目标", dataIndex: "succeededTargets" },
                { title: "待补目标", dataIndex: "missingTargets", render: (I) => I || "—" },
                { title: "未完成目标", dataIndex: "incompleteTargets" },
                {
                  title: "预计动作",
                  render: (I, y) => y.wouldSkip ? "跳过" : y.duplicateRisk ? "仅补缺失目标" : "采集"
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
  const [t, r] = It.useMessage(), [n, a] = b([]), [d, o] = b([]), [s, c] = b([]), [u, w] = b([]), [x, S] = b(""), [I, y] = b(1), [p, T] = b(!0), [O, Z] = b([]), [v, R] = b(() => _e()), [j, g] = b(null), [C, k] = b([]), [B, re] = b(!1), [ye, N] = b(!0), [q, V] = b(!1), [ue, F] = b(!1), [ne, ut] = b(null), [je, Fe] = b(!1), [Le, qe] = b(!1), Te = ie(1), Me = ie(e);
  Me.current = e;
  const Ce = ie(v.trigger);
  J(() => {
    Ce.current = v.trigger;
  }, [v.trigger]), J(() => {
    let m = !0;
    const P = Me.current.hubQuery;
    return Promise.allSettled([
      P("industrial/nodes"),
      P("industrial/workflows"),
      P("industrial/resources")
    ]).then(([G, M, W]) => {
      var se;
      if (!m) return;
      const Q = [];
      if (G.status === "fulfilled" ? a(rr(G.value)) : Q.push(`节点描述: ${String(G.reason)}`), M.status === "fulfilled") {
        const Y = lt(M.value);
        o(Y);
        const ae = new URLSearchParams(window.location.search).get("workflowId"), ee = ae && Y.some((ce) => ce.workflowId === ae) ? ae : (se = Y[0]) == null ? void 0 : se.workflowId;
        ee && Be(ee);
      } else Q.push(`工作流: ${String(M.reason)}`);
      if (W.status === "fulfilled") {
        const Y = nr(W.value);
        w(Y);
        const ae = Y.map((ee) => ee.id);
        c(ae), S((ee) => ee || ae[0] || "");
      } else Q.push(`资源: ${String(W.reason)}`);
      Z(Q);
    }).finally(() => {
      m && T(!1);
    }), () => {
      m = !1;
    };
  }, []);
  const oe = Xe(
    () => new Map(n.map((m) => [m.nodeTypeId, m])),
    [n]
  ), Ne = v.nodes.find((m) => m.id === j) ?? null, me = z(async (m, P, G) => {
    re(!0);
    try {
      return await X().api.hubCommand(m, P), t.success(G), !0;
    } catch (M) {
      return t.error(`${m} failed: ${M instanceof Error ? M.message : String(M)}`), !1;
    } finally {
      re(!1);
    }
  }, [t]), Be = z(async (m) => {
    try {
      const P = await e.hubQuery("industrial/workflow-definitions"), M = (te(P) ?? []).find((se) => {
        const Y = se;
        return String((Y == null ? void 0 : Y.workflowId) ?? "") === m;
      }), W = M === void 0 ? { ..._e(m), name: m } : or(M);
      R(W), y(Number(W.layout.lookbackDays ?? 1) || 1);
      const Q = W.nodes.map((se) => /^n(\d+)$/.exec(se.id)).filter(Boolean).reduce((se, Y) => Math.max(se, Number(Y[1])), 0);
      Te.current = Q + 1, g(null), V(!1), k([]), F(!1);
    } catch (P) {
      t.error(`加载工作流失败: ${P instanceof Error ? P.message : String(P)}`);
    }
  }, [e, t]), be = z(async () => {
    try {
      const m = fr(await e.hubQuery("industrial/execution"));
      if (m === null) return;
      ut(m);
    } catch {
    }
  }, [e]), pt = z(async () => {
    var G;
    const m = new Set(v.resourceReferences ?? []);
    for (const M of v.nodes)
      for (const W of ["resourceId", "targetResourceId"]) {
        const Q = (G = M.config) == null ? void 0 : G[W];
        typeof Q == "string" && Q.trim() && m.add(Q.trim());
      }
    const P = {
      ...v,
      trigger: Ce.current,
      resourceReferences: [...m],
      layout: { ...v.layout, lookbackDays: I }
    };
    await me("industrial/save-workflow", P, "已保存") && (R(P), F(!1));
  }, [me, v, I]), ft = z(async () => {
    if (ue) {
      t.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await me("industrial/publish-workflow", { workflowId: v.workflowId }, "已发布") && await be();
  }, [me, v.workflowId, ue, t, be]), gt = z(async () => {
    if (ue) {
      t.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await me(
      "industrial/run-workflow",
      { workflowId: v.workflowId, resourceId: x },
      "采集已启动"
    ) && await be();
  }, [me, v.workflowId, ue, t, be, x]), Ee = z((m, P, G) => {
    const M = fe(oe, m);
    if (!M) return;
    const W = `n${Te.current++}`;
    R((Q) => ({
      ...Q,
      nodes: [...Q.nodes, Rt(M, W, P, G)]
    })), g(W), V(!0), k([]), F(!0);
  }, [oe]), yt = z((m) => {
    const P = Te.current - 1;
    Ee(m, 80 + P % 3 * 190, 80 + Math.floor(P / 3) * 130);
  }, [Ee]), Ke = z((m, P) => {
    const G = v.nodes.find((ce) => ce.id === m), M = v.nodes.find((ce) => ce.id === P);
    if (!G || !M)
      return t.warning("连线失败：端点不在画布上"), null;
    const W = fe(oe, G.type), Q = fe(oe, M.type);
    if (!W || !Q) return null;
    let se = G, Y = M, ae = Je(W, Q);
    if (!ae) {
      const ce = Je(Q, W);
      if (!ce)
        return t.warning(`端口类型不兼容：${W.name} → ${Q.name}`), null;
      se = M, Y = G, ae = ce;
    }
    const ee = Ot(v, {
      source: se.id,
      sourcePort: ae.sourcePort,
      target: Y.id,
      targetPort: ae.targetPort
    }, oe);
    return ee.issue ? (t.warning(`${ee.issue.code}: ${ee.issue.message}`), k((ce) => [...ce, ee.issue]), null) : ee.definition === v ? (t.info("这条连线已经存在"), null) : (R(ee.definition), F(!0), ae);
  }, [oe, v, t]), mt = z(() => {
    const m = Mt(v, oe, new Set(s));
    k(m), m.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : (t.warning(`发现 ${m.length} 个问题，见属性抽屉定位列表`), V(!0));
  }, [v, oe, s, t]), ht = z((m, P) => {
    R((G) => ({
      ...G,
      edges: G.edges.map((M, W) => W === m ? P : M)
    })), F(!0);
  }, []);
  return p ? /* @__PURE__ */ i(tt, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ h("div", { className: "industrial-designer", children: [
    r,
    /* @__PURE__ */ h(br, { className: "industrial-header", children: [
      /* @__PURE__ */ h("div", { className: "industrial-title", children: [
        /* @__PURE__ */ i(E.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ i(E.Text, { type: "secondary", children: v.name || v.workflowId }),
        ue && /* @__PURE__ */ i(de, { color: "orange", children: "未保存" }),
        ne && /* @__PURE__ */ i(de, { color: ne.error ? "error" : "success", children: ne.status })
      ] }),
      /* @__PURE__ */ h("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ i(
          $,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": ye,
            onClick: () => N((m) => !m),
            children: ye ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ i(
          $,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => V((m) => !m),
            children: "属性"
          }
        ),
        /* @__PURE__ */ i($, { size: "small", "data-testid": "open-trigger", onClick: () => Fe(!0), children: "触发器" }),
        /* @__PURE__ */ i(
          $,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => qe(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ i("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ h(_, { size: 4, children: [
          /* @__PURE__ */ i($, { size: "small", loading: B, onClick: () => void pt(), children: "保存" }),
          /* @__PURE__ */ i($, { size: "small", onClick: mt, children: "校验" }),
          /* @__PURE__ */ i($, { size: "small", loading: B, onClick: () => void ft(), children: "发布" }),
          /* @__PURE__ */ i($, { size: "small", type: "primary", loading: B, onClick: () => void gt(), children: "运行" })
        ] }),
        /* @__PURE__ */ h(
          "select",
          {
            className: "industrial-resource-select",
            "aria-label": "运行资源",
            value: x,
            onChange: (m) => S(m.target.value),
            children: [
              /* @__PURE__ */ i("option", { value: "", children: "运行资源" }),
              s.map((m) => /* @__PURE__ */ i("option", { value: m, children: m }, m))
            ]
          }
        ),
        /* @__PURE__ */ i(
          et,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: I,
            onChange: (m) => {
              y(Number(m) || 1), F(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ h(Ir, { className: `industrial-body${ye ? "" : " industrial-body--collapsed"}`, children: [
      ye ? /* @__PURE__ */ h("aside", { className: "industrial-palette", children: [
        d.length > 0 && /* @__PURE__ */ i(
          ge,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: v.workflowId,
            placeholder: "选择工作流",
            onChange: (m) => void Be(m),
            options: d.map((m) => ({ value: m.workflowId, label: m.name }))
          }
        ),
        /* @__PURE__ */ i(Dt, { descriptors: n, loading: !1, onAdd: yt })
      ] }) : /* @__PURE__ */ i("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ i($, { type: "text", size: "small", onClick: () => N(!0), children: "节点库" }) }),
      /* @__PURE__ */ h("section", { className: "industrial-center", children: [
        O.length > 0 && /* @__PURE__ */ i(
          pe,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: O.join("；")
          }
        ),
        /* @__PURE__ */ i(
          Pt,
          {
            definition: v,
            catalog: oe,
            selectedId: j,
            onSelect: (m) => {
              g(m), m && V(!0);
            },
            onDropNode: Ee,
            onCreateEdge: Ke,
            onPositions: (m) => {
              R((P) => Lt(P, m)), F(!0);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ h(
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
          ne && /* @__PURE__ */ i(
            pe,
            {
              style: { marginBottom: 12 },
              type: ne.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${ne.status}`,
              description: /* @__PURE__ */ h(_, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                ne.error && /* @__PURE__ */ i("span", { children: ne.error }),
                ne.executed.length > 0 && /* @__PURE__ */ h("span", { children: [
                  "已执行节点: ",
                  ne.executed.join(", ")
                ] }),
                ne.nodes.length > 0 && /* @__PURE__ */ i(
                  le,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: ne.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (m, P) => P.error ?? (P.samples.length > 0 ? P.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ i(
            Yt,
            {
              node: Ne,
              descriptor: Ne ? fe(oe, Ne.type) ?? null : null,
              nodes: v.nodes,
              edges: v.edges,
              catalog: oe,
              resources: u,
              issues: C,
              onApplyConfig: (m) => {
                j && (R((P) => Ft(P, j, m)), F(!0));
              },
              onDeleteNode: () => {
                R((m) => zt(m, j ?? "")), g(null), F(!0);
              },
              onDeleteEdge: (m) => {
                R((P) => At(P, m)), F(!0);
              },
              onCreateEdge: (m, P) => void Ke(m, P),
              onUpdateEdge: ht,
              onSelectNode: (m) => {
                g(m), V(!0);
              }
            }
          )
        ]
      }
    ),
    je && /* @__PURE__ */ i(
      mr,
      {
        open: je,
        onClose: () => Fe(!1),
        trigger: v.trigger,
        resourceIds: s,
        workflowId: v.workflowId,
        ctx: e,
        onSave: (m) => {
          Ce.current = m, R((P) => ({ ...P, trigger: m })), F(!0);
        }
      }
    ),
    Le && /* @__PURE__ */ i(
      vr,
      {
        open: Le,
        onClose: () => qe(!1),
        ctx: e,
        workflowId: v.workflowId,
        resourceId: x,
        lookbackDays: I
      }
    )
  ] });
}
function kr({ block: e, ctx: t }) {
  const [r, n] = b([]), [a, d] = b(!1), [o, s] = b(""), [c, u] = b(null), [w, x] = b(!1), S = z(async () => {
    try {
      n(lt(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
    } catch (p) {
      u(p instanceof Error ? p.message : String(p));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    S();
  }, [S]);
  const I = z(async () => {
    var T;
    const p = o.trim();
    if (!p) {
      u("请输入工作流名称");
      return;
    }
    x(!0), u(null);
    try {
      const Z = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await t.hubCommand("industrial/save-workflow", {
        ..._e(Z),
        name: p,
        layout: { lookbackDays: 1 }
      }), d(!1), s(""), await S(), (T = t.navigate) == null || T.call(t, `/industrial/designer?workflowId=${encodeURIComponent(Z)}`);
    } catch (O) {
      u(O instanceof Error ? O.message : String(O));
    } finally {
      x(!1);
    }
  }, [t, S, o]), y = z((p) => {
    var T;
    (T = t.navigate) == null || T.call(t, `/industrial/designer?workflowId=${encodeURIComponent(p)}`);
  }, [t]);
  return /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ i($, { type: "primary", onClick: () => d(!0), children: "新建工作流" })
    ] }),
    c && /* @__PURE__ */ i(E.Text, { type: "danger", children: c }),
    /* @__PURE__ */ i(
      le,
      {
        rowKey: "workflowId",
        dataSource: r,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (p) => /* @__PURE__ */ i($, { type: "link", style: { padding: 0 }, onClick: () => y(p), children: p })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ i(
      ve,
      {
        open: a,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: w,
        onCancel: () => d(!1),
        onOk: () => void I(),
        children: /* @__PURE__ */ h(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(E.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ i(
            K,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: o,
              onChange: (p) => s(p.target.value ?? "")
            }
          ),
          c && /* @__PURE__ */ i(E.Text, { type: "danger", children: c })
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
  const [r, n] = b([]), [a, d] = b([]), [o, s] = b([]), [c, u] = b(De), [w, x] = b(!1), [S, I] = b(null), [y, p] = b(!1), T = z(async () => {
    try {
      const [g, C, k] = await Promise.all([
        t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        t.hubQuery("industrial/credentials"),
        t.hubQuery("industrial/resource-tests")
      ]);
      n(sr(g)), d(ct(C)), s(lr(k));
    } catch (g) {
      I(g instanceof Error ? g.message : String(g));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    T();
  }, [T]);
  const O = z((g) => {
    const C = (...k) => {
      for (const B of k) {
        const re = g.config[B];
        if (re != null && String(re).trim()) return String(re);
      }
      return "";
    };
    u({
      id: g.id,
      name: g.name,
      type: g.type,
      provider: g.provider,
      root: C("root", "path"),
      host: C("server", "host", "dataSource", "data_source"),
      database: C("database", "initialCatalog", "initial_catalog", "path"),
      port: C("port"),
      table: C("table"),
      credentialId: g.credentialId ?? "",
      enabled: g.enabled,
      config: g.config
    }), x(!0);
  }, []), Z = z(async () => {
    const g = c.id.trim();
    if (!g) {
      I("请输入资源 ID");
      return;
    }
    p(!0), I(null);
    try {
      await t.hubCommand("industrial/save-resource", {
        id: g,
        name: c.name || g,
        type: c.type,
        provider: c.provider || "manual",
        config: Tr(c),
        credentialId: c.credentialId || null,
        enabled: c.enabled
      }), x(!1), u(De), await T();
    } catch (C) {
      I(C instanceof Error ? C.message : String(C));
    } finally {
      p(!1);
    }
  }, [t, c, T]), v = z(async (g) => {
    I(null);
    try {
      await t.hubCommand("industrial/test-resource", { id: g });
    } catch (C) {
      I(C instanceof Error ? C.message : String(C));
    }
    await T();
  }, [t, T]), R = z(async (g) => {
    I(null);
    try {
      await t.hubCommand("industrial/delete-resource", { id: g }), await T();
    } catch (C) {
      I(C instanceof Error ? C.message : String(C));
    }
  }, [t, T]), j = new Map(o.map((g) => [g.resourceId, g]));
  return /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ i($, { type: "primary", onClick: () => {
        u(De), x(!0);
      }, children: "新建资源" })
    ] }),
    S && /* @__PURE__ */ i(pe, { type: "error", showIcon: !0, message: S }),
    /* @__PURE__ */ i(
      le,
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
            render: (g, C) => {
              const k = j.get(C.id);
              return k ? /* @__PURE__ */ i(E.Text, { type: k.ok ? "success" : "danger", children: k.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (g, C) => /* @__PURE__ */ h(_, { size: 4, children: [
              /* @__PURE__ */ i($, { size: "small", onClick: () => void v(C.id), children: "测试连接" }),
              /* @__PURE__ */ i($, { size: "small", onClick: () => O(C), children: "编辑" }),
              /* @__PURE__ */ i($, { size: "small", danger: !0, onClick: () => void R(C.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ i(
      ve,
      {
        open: w,
        title: c.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: y,
        onCancel: () => x(!1),
        onOk: () => void Z(),
        children: /* @__PURE__ */ h(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(
            K,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: c.id,
              onChange: (g) => u({ ...c, id: g.target.value })
            }
          ),
          /* @__PURE__ */ i(
            K,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: c.name,
              onChange: (g) => u({ ...c, name: g.target.value })
            }
          ),
          /* @__PURE__ */ i(E.Text, { children: "资源类型" }),
          /* @__PURE__ */ i(_, { wrap: !0, children: Sr.map((g) => /* @__PURE__ */ i(
            $,
            {
              size: "small",
              type: c.type === g.value ? "primary" : "default",
              onClick: () => u({ ...c, type: g.value }),
              children: g.label
            },
            g.value
          )) }),
          (c.type === "filesystem.local" || c.type === "filesystem.unc") && /* @__PURE__ */ i(
            K,
            {
              "aria-label": "目录路径",
              placeholder: c.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: c.root,
              onChange: (g) => u({ ...c, root: g.target.value })
            }
          ),
          (c.type === "sqlserver" || c.type === "postgresql" || c.type === "mysql") && /* @__PURE__ */ h(_, { children: [
            /* @__PURE__ */ i(
              K,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: c.host,
                onChange: (g) => u({ ...c, host: g.target.value })
              }
            ),
            /* @__PURE__ */ i(
              K,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: c.database,
                onChange: (g) => u({ ...c, database: g.target.value })
              }
            ),
            /* @__PURE__ */ i(
              K,
              {
                "aria-label": "端口",
                placeholder: "端口",
                value: c.port,
                onChange: (g) => u({ ...c, port: g.target.value })
              }
            )
          ] }),
          c.type === "sqlite" && /* @__PURE__ */ i(
            K,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: c.database,
              onChange: (g) => u({ ...c, database: g.target.value })
            }
          ),
          /* @__PURE__ */ i(
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
          /* @__PURE__ */ h(_, { children: [
            /* @__PURE__ */ i(
              rt,
              {
                "aria-label": "启用资源",
                checked: c.enabled,
                onChange: (g) => u({ ...c, enabled: g })
              }
            ),
            /* @__PURE__ */ i(E.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
function Nr({ block: e, ctx: t }) {
  const [r, n] = b([]), [a, d] = b(!1), [o, s] = b(""), [c, u] = b(""), [w, x] = b("username-password"), [S, I] = b(""), [y, p] = b(!1), [T, O] = b(null), [Z, v] = b(!1), R = z(async () => {
    try {
      n(ct(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
    } catch (k) {
      O(k instanceof Error ? k.message : String(k));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    R();
  }, [R]);
  const j = z(() => {
    s(""), u(""), x("username-password"), I(""), p(!1), d(!0);
  }, []), g = z((k) => {
    s(k.id), u(k.name), x(k.kind), I(""), p(!0), d(!0);
  }, []), C = z(async () => {
    if (!o.trim()) {
      O("请输入凭证 ID");
      return;
    }
    if (!S.trim()) {
      O("请输入凭证内容");
      return;
    }
    v(!0), O(null);
    try {
      y ? await t.hubCommand("industrial/rotate-credential", { id: o, secret: S }) : await t.hubCommand("industrial/save-credential", { id: o, name: c || o, kind: w, secret: S }), d(!1), await R();
    } catch (k) {
      O(k instanceof Error ? k.message : String(k));
    } finally {
      v(!1);
    }
  }, [t, o, w, R, c, y, S]);
  return /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ i($, { type: "primary", onClick: j, children: "新建凭证" })
    ] }),
    T && /* @__PURE__ */ i(pe, { type: "error", showIcon: !0, message: T }),
    /* @__PURE__ */ i(
      le,
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
            render: (k, B) => /* @__PURE__ */ i($, { size: "small", onClick: () => g(B), children: "轮换" })
          }
        ]
      }
    ),
    /* @__PURE__ */ i(
      ve,
      {
        open: a,
        title: y ? "轮换凭证" : "新建凭证",
        okText: y ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: Z,
        onCancel: () => d(!1),
        onOk: () => void C(),
        children: /* @__PURE__ */ h(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ i(
            K,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: o,
              disabled: y,
              onChange: (k) => s(k.target.value)
            }
          ),
          !y && /* @__PURE__ */ i(
            K,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: c,
              onChange: (k) => u(k.target.value)
            }
          ),
          /* @__PURE__ */ i(
            K,
            {
              "aria-label": "凭证内容",
              placeholder: y ? "新凭证内容" : "凭证内容",
              value: S,
              onChange: (k) => I(k.target.value)
            }
          ),
          T && /* @__PURE__ */ i(E.Text, { type: "danger", children: T })
        ] })
      }
    )
  ] });
}
const Ve = (e) => e === "Succeeded" || e === "Completed" ? "success" : e === "Failed" || e === "PartialFailure" ? "error" : e === "Running" || e === "Interrupted" ? "processing" : "default";
function Er({ block: e, ctx: t }) {
  const [r, n] = b([]), [a, d] = b([]), [o, s] = b(null), [c, u] = b(null), [w, x] = b(!1), S = z(async () => {
    try {
      const y = pr(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/execution-center"));
      n(y.runs), d(y.deliveries);
    } catch (y) {
      s(y instanceof Error ? y.message : String(y));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    S();
  }, [S]);
  const I = z(async (y) => {
    x(!0), s(null), u(null);
    try {
      await t.hubCommand("industrial/retry-failed", {
        deliveryId: y.id,
        targetId: y.targetId
      }), u(`已提交目标 ${y.targetId} 的重试`), await S();
    } catch (p) {
      s(p instanceof Error ? p.message : String(p));
    } finally {
      x(!1);
    }
  }, [t, S]);
  return /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    o && /* @__PURE__ */ i(pe, { type: "error", showIcon: !0, message: o }),
    c && /* @__PURE__ */ i(pe, { type: "success", showIcon: !0, message: c }),
    /* @__PURE__ */ i(
      le,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (y) => /* @__PURE__ */ i(de, { color: Ve(y), children: y }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (y) => y ?? "—" },
          { title: "错误", dataIndex: "error", render: (y) => y ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ i(
      le,
      {
        rowKey: "id",
        dataSource: a,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (y) => /* @__PURE__ */ i(de, { color: Ve(y), children: y }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (y, p) => p.diagnosis ? /* @__PURE__ */ h(_, { direction: "vertical", size: 0, children: [
              /* @__PURE__ */ i(E.Text, { children: p.diagnosis.message }),
              /* @__PURE__ */ i(E.Text, { type: "secondary", children: p.diagnosis.action })
            ] }) : p.error ?? "—"
          },
          {
            title: "操作",
            key: "actions",
            render: (y, p) => /* @__PURE__ */ i(
              $,
              {
                size: "small",
                disabled: w || p.status !== "Failed" && p.status !== "Interrupted",
                onClick: () => void I(p),
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
  const [r, n] = b([]), [a, d] = b(null), o = z(async () => {
    try {
      n(ur(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (c) {
      d(c instanceof Error ? c.message : String(c));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    o();
  }, [o]);
  const s = (c) => /* @__PURE__ */ i(
    le,
    {
      rowKey: "id",
      dataSource: c.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (u) => /* @__PURE__ */ i(de, { children: u }) },
        { title: "记录", dataIndex: "records" },
        {
          title: "接受/拒绝/警告",
          key: "counts",
          render: (u, w) => `${w.accepted} / ${w.rejected} / ${w.warnings}`
        },
        {
          title: "原因与下一步",
          key: "diagnosis",
          render: (u, w) => w.diagnosis ? /* @__PURE__ */ h(_, { direction: "vertical", size: 0, children: [
            /* @__PURE__ */ i(E.Text, { children: w.diagnosis.message }),
            /* @__PURE__ */ i(E.Text, { type: "secondary", children: w.diagnosis.action }),
            w.error && /* @__PURE__ */ i(E.Text, { type: "secondary", children: w.error })
          ] }) : w.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ i(E.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    a && /* @__PURE__ */ i(pe, { type: "error", showIcon: !0, message: a }),
    /* @__PURE__ */ i(
      le,
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
  const n = (d, o) => {
    const s = ({ block: c, ctx: u }) => $e(o, { block: c, ctx: u });
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
