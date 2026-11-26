let we = null;
function wt(e) {
  we = e;
}
function X() {
  if (!we && typeof globalThis < "u" && (we = globalThis.__CORDIS_CONSOLE ?? null), !we)
    throw new Error("[industrial-designer] console facade is not available");
  return we;
}
const o = (...e) => X().jsxRuntime.jsx(...e), h = (...e) => X().jsxRuntime.jsxs(...e), xe = (e) => X().jsxRuntime.jsx(X().jsxRuntime.Fragment, e), Re = (...e) => X().React.createElement(...e), z = (...e) => X().React.useCallback(...e), J = (...e) => X().React.useEffect(...e), Xe = (...e) => X().React.useMemo(...e), oe = (...e) => X().React.useRef(...e), b = (...e) => X().React.useState(...e), A = (e) => {
  const t = (r) => {
    const n = e.split(".").reduce((i, d) => i == null ? void 0 : i[d], X().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Re(n, r ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, pe = A("Alert"), R = A("Button"), Ue = A("Divider"), vt = A("Drawer"), Ze = A("Empty"), $e = Object.assign(A("Form"), {
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
    var i;
    const n = (i = X().antd.message) == null ? void 0 : i[t];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${t} unavailable`);
    return n(...r);
  }
}), nt = 148, it = 46, Ge = {
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
      const i = t.get(n.type), d = (i == null ? void 0 : i.category) ?? "General", a = r === n.id;
      return {
        id: n.id,
        data: {
          label: (i == null ? void 0 : i.name) ?? n.type,
          nodeTypeId: n.type,
          category: d,
          icon: (i == null ? void 0 : i.icon) ?? "AppstoreOutlined",
          config: { ...n.config }
        },
        style: {
          x: n.x,
          y: n.y,
          fill: We(d),
          stroke: a ? "#111111" : We(d),
          lineWidth: a ? 3 : 1.5,
          radius: 10,
          port: !0,
          ports: [
            ...((i == null ? void 0 : i.inputs) ?? []).map((s, c, u) => ({
              key: `in:${s.name}`,
              placement: [0, (c + 1) / (u.length + 1)],
              r: 5,
              stroke: s.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((i == null ? void 0 : i.outputs) ?? []).map((s, c, u) => ({
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
  const n = r && r.length > 0 ? new Set(r) : null, i = {};
  for (const d of e.nodes) {
    if (!t.has(d.id) || n && !n.has(d.id)) continue;
    const a = d.style.x, s = d.style.y;
    typeof a == "number" && typeof s == "number" && (i[d.id] = { x: a, y: s });
  }
  return i;
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
        size: [nt, it],
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
  var w, x;
  if (t.targetType !== "node" || !((w = t.target) != null && w.id)) return !1;
  const r = ((x = e.gestureOrigin) == null ? void 0 : x.call(e)) ?? t.client ?? t.canvas;
  if (!r || !e.graph) return !1;
  const n = String(t.target.id), i = e.graph.getNodeData().find((S) => String(S.id) === n), d = i == null ? void 0 : i.style;
  if (typeof (d == null ? void 0 : d.x) != "number" || typeof d.y != "number") return !1;
  const [a, s] = e.graph.getCanvasByClient([r.x, r.y]), c = Math.abs(a - d.x), u = Math.abs(s - d.y);
  return Math.abs(c - nt / 2) <= 14 && u <= it / 2 + 10;
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
        var a, s;
        const n = e.graph;
        if (!n) return;
        const i = Array.isArray(r) ? r : r ? [String(r)] : void 0, d = n.getNodeData().map((c) => ({
          id: String(c.id),
          data: c.data ?? {},
          style: c.style ?? {}
        }));
        (s = e.onPositions) == null || s.call(e, St(
          { nodes: d },
          ((a = e.allowedIds) == null ? void 0 : a.call(e)) ?? /* @__PURE__ */ new Set(),
          i
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: Nt,
      enable: (r) => Pe(e, r) ? (t.armed = !1, !1) : t.armed ? r.targetType === "node" || r.targetType === "combo" : Qe(e, r) ? (t.armed = !0, !0) : !1,
      onCreate: (r) => {
        var a;
        t.armed = !1;
        const n = String(r.source ?? ""), i = String(r.target ?? "");
        if (!n || !i || n === i) return;
        const d = (a = e.onCreateEdge) == null ? void 0 : a.call(e, n, i);
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
    onSelect: i,
    onDropNode: d,
    onCreateEdge: a,
    onPositions: s
  } = e, c = oe(null), u = oe(null), w = oe(!1), x = oe(!1), S = oe({ armed: !1 }), I = oe(null), y = oe({ onSelect: i, onDropNode: d, onCreateEdge: a, onPositions: s });
  y.current = { onSelect: i, onDropNode: d, onCreateEdge: a, onPositions: s };
  const p = Xe(
    () => kt(t, r, n),
    [t, r, n]
  ), N = oe(p);
  N.current = p;
  const O = oe(/* @__PURE__ */ new Set());
  return O.current = new Set(t.nodes.map((v) => v.id)), J(() => {
    const v = (g) => {
      g.code === "Space" && !g.repeat && (x.current = !0);
    }, $ = (g) => {
      g.code === "Space" && (x.current = !1);
    }, j = () => {
      x.current = !1;
    };
    return window.addEventListener("keydown", v), window.addEventListener("keyup", $), window.addEventListener("blur", j), () => {
      window.removeEventListener("keydown", v), window.removeEventListener("keyup", $), window.removeEventListener("blur", j);
    };
  }, []), J(() => {
    const v = c.current;
    if (!v || w.current) return;
    let $ = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: j }) => {
      if ($ || !c.current || w.current) return;
      const g = Tt(N.current, v), T = new j({
        ...g,
        behaviors: ot({
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
      T.on("node:click", (k) => {
        const B = Et(k);
        B && y.current.onSelect(B);
      }), T.on("edge:click", () => y.current.onSelect(null)), T.on("canvas:click", () => y.current.onSelect(null)), T.on("pointerdown", (k) => {
        const B = k;
        I.current = B.client ? { x: B.client.x, y: B.client.y } : null;
      }), T.on("pointerup", () => {
        queueMicrotask(() => {
          S.current.armed = !1, I.current = null;
        });
      }), u.current = T, v.__industrialGraph = T, w.current = !0, T.render();
    }), () => {
      $ = !0;
    };
  }, []), J(() => {
    const v = u.current;
    !v || !w.current || (v.setData(p), v.draw());
  }, [p]), J(() => () => {
    var v;
    (v = u.current) == null || v.destroy(), u.current = null, w.current = !1;
  }, []), /* @__PURE__ */ o("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ h("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ o(
      "div",
      {
        ref: c,
        className: "industrial-graph-host",
        onDragOver: (v) => v.preventDefault(),
        onDrop: (v) => {
          v.preventDefault();
          const $ = v.dataTransfer.getData("application/x-industrial-node") || v.dataTransfer.getData("text/plain");
          if (!$) return;
          const j = u.current, g = c.current;
          if (!j || !g) return;
          const T = g.getBoundingClientRect(), k = j.getCanvasByViewport([
            v.clientX - T.left,
            v.clientY - T.top
          ]);
          y.current.onDropNode($, k[0], k[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ o("div", { className: "industrial-graph-hint", children: "拖动节点移动 · 从端口拖出连线 · 拖动画布平移（空格或中键可在节点上平移）" })
  ] }) });
}
function Dt({ descriptors: e, loading: t, error: r, onAdd: n }) {
  const i = /* @__PURE__ */ new Map();
  for (const a of e) {
    const s = a.category.split(".")[0] || "General", c = i.get(s) ?? [];
    c.push(a), i.set(s, c);
  }
  const d = (a, s) => {
    a.dataTransfer.setData("application/x-industrial-node", s.nodeTypeId), a.dataTransfer.setData("text/plain", s.nodeTypeId), a.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ h("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ o(E.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ o(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ o(tt, { size: "small", style: { display: "block", marginTop: 16 } }),
    r && /* @__PURE__ */ o(E.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: r }),
    !t && !r && e.length === 0 && /* @__PURE__ */ o(Ze, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(i.entries()).map(([a, s]) => /* @__PURE__ */ h("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ o(E.Text, { strong: !0, style: { fontSize: 12 }, children: a }),
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
            /* @__PURE__ */ o("div", { className: "industrial-palette-node-title", children: c.name }),
            /* @__PURE__ */ h("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ o(de, { bordered: !1, color: "blue", children: c.version }),
              /* @__PURE__ */ o(E.Text, { type: "secondary", style: { fontSize: 11 }, children: c.nodeTypeId })
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
function Rt(e) {
  const t = {};
  for (const [r, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (t[r] = n.default);
  return t;
}
function $t(e, t, r = 0, n = 0) {
  return { id: t, type: e.nodeTypeId, config: Rt(e), x: r, y: n };
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
  const n = `edges.${e.edges.length}`, i = e.nodes.find((w) => w.id === t.source), d = e.nodes.find((w) => w.id === t.target);
  if (!i || !d)
    return { definition: e, issue: H(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((w) => _t(w, t)))
    return { definition: e };
  const a = fe(r, i.type), s = fe(r, d.type);
  if (!a || !s)
    return { definition: e, issue: H(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const c = ke(a, t.sourcePort, "outputs"), u = ke(s, t.targetPort, "inputs");
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
  for (const [n, i] of Object.entries(t))
    i === void 0 ? delete r[n] : r[n] = i;
  return r;
}
function Lt(e, t, r) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === t ? { ...n, config: r } : n)
  };
}
function qt(e, t) {
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
function Ft(e) {
  var d;
  const t = /* @__PURE__ */ new Map();
  for (const a of e.nodes) t.set(a.id, []);
  for (const a of e.edges)
    (d = t.get(a.source)) == null || d.push(a.target);
  const r = /* @__PURE__ */ new Map(), n = [], i = (a) => {
    r.set(a, 1), n.push(a);
    for (const s of t.get(a) ?? []) {
      if (r.get(s) === 1)
        return [...n.slice(n.indexOf(s)), s];
      if (r.get(s) !== 2) {
        const c = i(s);
        if (c) return c;
      }
    }
    return n.pop(), r.set(a, 2), null;
  };
  for (const a of e.nodes)
    if (r.get(a.id) !== 2) {
      const s = i(a.id);
      if (s) return s;
    }
  return null;
}
function Mt(e, t, r) {
  const n = [];
  e.workflowId.trim() || n.push(H("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(H("workflow", "INVALID_VERSION", "version must be positive"));
  const i = /* @__PURE__ */ new Map();
  for (const s of e.nodes)
    i.has(s.id) && n.push(H(`nodes.${s.id}`, "DUPLICATE_NODE_ID", `duplicate node '${s.id}'`)), i.set(s.id, s);
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
    const u = i.get(s.source), w = i.get(s.target);
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
        e.edges.some((w) => w.target === s.id && w.targetPort === u.name && i.has(w.source)) || n.push(H(
          `nodes.${s.id}.inputs.${u.name}`,
          "MISSING_INPUT",
          `required input '${u.name}' is not connected`
        ));
  }
  const a = Ft(e);
  return a && n.push(H(`nodes.${a[0]}`, "GRAPH_CYCLE", `cycle: ${a.join(" → ")}`)), n;
}
const Oe = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function Bt(e) {
  const t = Oe(e.additionalProperties).enum;
  if (!Array.isArray(t)) return null;
  const r = t.filter((n) => typeof n == "string");
  return r.length > 0 ? r : null;
}
function at(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && Oe(e.items).type === "string" ? "string-list" : e.type === "object" && Oe(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function ze(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, t]) => t != null && typeof t != "object").map(([t, r]) => ({ key: t, value: String(r) }));
}
function Ye(e) {
  const t = {}, r = [];
  for (const n of e) {
    const i = n.key.trim();
    i && (Object.prototype.hasOwnProperty.call(t, i) && r.push(i), t[i] = n.value);
  }
  return { record: t, duplicateKeys: r };
}
function Ie(e) {
  if (!Array.isArray(e)) return [];
  const t = /* @__PURE__ */ new Set(), r = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const i = n.trim();
    !i || t.has(i) || (t.add(i), r.push(i));
  }
  return r;
}
function Kt(e, t) {
  const r = /* @__PURE__ */ new Set();
  for (const [n, i] of Object.entries(e))
    if (at(i) === "key-value")
      for (const d of ze(t[n]))
        d.key.trim() && r.add(d.key.trim());
  return [...r];
}
function Ut({
  value: e,
  onChange: t,
  valueOptions: r,
  keyTitle: n = "目标字段",
  valueTitle: i = "来源字段",
  testId: d
}) {
  const a = JSON.stringify(e ?? {}), [s, c] = b(() => ze(e)), [u, w] = b(a);
  J(() => {
    a !== u && (c(ze(e)), w(a));
  }, [a, u, e]);
  const x = (I) => {
    c(I), t == null || t(Ye(I).record);
  }, { duplicateKeys: S } = Ye(s);
  return /* @__PURE__ */ h("div", { "data-testid": d, className: "industrial-kv-editor", children: [
    s.map((I, y) => /* @__PURE__ */ h("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ o(
        K,
        {
          "aria-label": `${n} ${y + 1}`,
          value: I.key,
          placeholder: n,
          onChange: (p) => x(s.map((N, O) => O === y ? { ...N, key: p.target.value } : N))
        }
      ),
      r && r.length > 0 ? /* @__PURE__ */ o(
        ge,
        {
          "aria-label": `${i} ${y + 1}`,
          value: I.value || void 0,
          placeholder: i,
          options: r.map((p) => ({ label: p, value: p })),
          onChange: (p) => x(s.map((N, O) => O === y ? { ...N, value: p ?? "" } : N))
        }
      ) : /* @__PURE__ */ o(
        K,
        {
          "aria-label": `${i} ${y + 1}`,
          value: I.value,
          placeholder: i,
          onChange: (p) => x(s.map((N, O) => O === y ? { ...N, value: p.target.value } : N))
        }
      ),
      /* @__PURE__ */ o(
        R,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${y + 1}`,
          onClick: () => x(s.filter((p, N) => N !== y)),
          children: "删"
        }
      )
    ] }, y)),
    /* @__PURE__ */ o(
      R,
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
  const i = JSON.stringify(Ie(e)), [d, a] = b(() => Ie(e)), [s, c] = b(i);
  J(() => {
    i !== s && (a(Ie(e)), c(i));
  }, [i, s, e]);
  const u = (w) => {
    a(w), t == null || t(Ie(w));
  };
  return /* @__PURE__ */ h("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ o("datalist", { id: `${n ?? "list"}-suggestions`, children: r.map((w) => /* @__PURE__ */ o("option", { value: w }, w)) }),
    d.map((w, x) => /* @__PURE__ */ h("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ o(
        K,
        {
          "aria-label": `字段 ${x + 1}`,
          value: w,
          list: `${n ?? "list"}-suggestions`,
          onChange: (S) => u(d.map((I, y) => y === x ? S.target.value : I))
        }
      ),
      /* @__PURE__ */ o(
        R,
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
    /* @__PURE__ */ o(R, { size: "small", type: "dashed", block: !0, onClick: () => u([...d, ""]), children: "添加字段" }),
    d.length === 0 && /* @__PURE__ */ o(E.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Wt({ value: e, onChange: t }) {
  const r = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, i] = b(r), [d, a] = b(null);
  J(() => {
    i(r), a(null);
  }, [r]);
  const s = (c) => {
    i(c);
    try {
      const u = JSON.parse(c);
      a(null), t == null || t(u);
    } catch (u) {
      a(u.message);
    }
  };
  return /* @__PURE__ */ h("div", { children: [
    /* @__PURE__ */ o(K.TextArea, { rows: 5, value: n, onChange: (c) => s(c.target.value) }),
    d && /* @__PURE__ */ o(E.Text, { type: "danger", style: { fontSize: 11 }, children: d })
  ] });
}
function Qt({ value: e, onChange: t, resources: r, resourceType: n, label: i }) {
  const d = r.filter((s) => s.enabled !== !1 && (!n || s.type === n)).map((s) => ({
    value: s.id,
    label: `${s.name} (${s.type})`
  })), a = r.find((s) => s.id === e);
  return /* @__PURE__ */ h("div", { children: [
    /* @__PURE__ */ h(
      "select",
      {
        "aria-label": i,
        value: e ?? "",
        onChange: (s) => t == null ? void 0 : t(s.target.value || void 0),
        children: [
          /* @__PURE__ */ o("option", { value: "", children: "未选择" }),
          d.map((s) => /* @__PURE__ */ o("option", { value: s.value, children: s.label }, s.value))
        ]
      }
    ),
    (a == null ? void 0 : a.credentialId) && /* @__PURE__ */ h(E.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      a.credentialName ?? a.credentialId
    ] })
  ] });
}
function Jt({ name: e, schema: t, required: r, resources: n, resourceType: i, properties: d, config: a }) {
  let s, c = "value";
  switch (at(t)) {
    case "resource":
      s = /* @__PURE__ */ o(
        Qt,
        {
          resources: n,
          resourceType: i,
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
      s = /* @__PURE__ */ o(Gt, { testId: `field-${e}`, suggestions: Kt(d, a) });
      break;
    case "json":
      s = /* @__PURE__ */ o(Wt, {});
      break;
    default:
      s = /* @__PURE__ */ o(K, { placeholder: t.description });
      break;
  }
  return /* @__PURE__ */ o(
    $e.Item,
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
    edges: i,
    catalog: d,
    resources: a,
    issues: s,
    onApplyConfig: c,
    onDeleteNode: u,
    onDeleteEdge: w,
    onCreateEdge: x,
    onUpdateEdge: S,
    onSelectNode: I
  } = e, [y] = $e.useForm(), [p, N] = b("{}"), [O, Z] = b(null), [v, $] = b(), [j, g] = b();
  J(() => {
    y.setFieldsValue((t == null ? void 0 : t.config) ?? {}), N(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), Z(null);
  }, [y, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const T = new Set((r == null ? void 0 : r.configurationSchema.required) ?? []), k = Object.entries((r == null ? void 0 : r.configurationSchema.properties) ?? {}), B = () => {
    if (k.length > 0) {
      y.validateFields().then((C) => c(jt((t == null ? void 0 : t.config) ?? {}, C)));
      return;
    }
    try {
      const C = JSON.parse(p);
      if (C === null || typeof C != "object" || Array.isArray(C))
        throw new Error("配置必须是 JSON 对象");
      Z(null), c(C);
    } catch (C) {
      Z(C.message);
    }
  }, re = i.map((C, q) => ({
    ...C,
    index: q,
    key: `${q}:${C.source}->${C.target}`
  })), ye = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (C, q) => /* @__PURE__ */ h(E.Text, { style: { fontSize: 11 }, children: [
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
        return /* @__PURE__ */ h("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ o(
            ge,
            {
              size: "small",
              value: q.sourcePort,
              style: { width: 110 },
              onChange: (L) => S(q.index, { ...q, sourcePort: L }),
              options: ((V == null ? void 0 : V.outputs) ?? []).map((L) => ({ label: `${L.name} (${L.type})`, value: L.name }))
            }
          ),
          /* @__PURE__ */ o(
            ge,
            {
              size: "small",
              value: q.targetPort,
              style: { width: 110 },
              onChange: (L) => S(q.index, { ...q, targetPort: L }),
              options: ((ue == null ? void 0 : ue.inputs) ?? []).map((L) => ({ label: `${L.name} (${L.type})`, value: L.name }))
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
  return /* @__PURE__ */ h("div", { className: "industrial-properties-body", children: [
    !t && /* @__PURE__ */ o(Ze, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ h(xe, { children: [
      /* @__PURE__ */ o(E.Text, { strong: !0, children: (r == null ? void 0 : r.name) ?? t.type }),
      /* @__PURE__ */ o(de, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ o($e, { form: y, layout: "vertical", size: "small", style: { marginTop: 12 }, children: k.map(([C, q]) => /* @__PURE__ */ o(
        Jt,
        {
          name: C,
          schema: q,
          required: T.has(C),
          resources: a,
          resourceType: typeof (r == null ? void 0 : r.configurationSchema.resourceType) == "string" ? r.configurationSchema.resourceType : void 0,
          properties: (r == null ? void 0 : r.configurationSchema.properties) ?? {},
          config: (t == null ? void 0 : t.config) ?? {}
        },
        C
      )) }),
      k.length === 0 && /* @__PURE__ */ h(xe, { children: [
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
        O && /* @__PURE__ */ o(E.Text, { type: "danger", style: { fontSize: 12 }, children: O })
      ] }),
      /* @__PURE__ */ o(R, { type: "primary", block: !0, onClick: B, children: "应用配置" }),
      /* @__PURE__ */ o(R, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: u, children: "删除节点" }),
      (r == null ? void 0 : r.activityType) && /* @__PURE__ */ o(E.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: r.activityType })
    ] }),
    /* @__PURE__ */ h(Ue, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      i.length,
      ")"
    ] }),
    n.length > 1 && /* @__PURE__ */ h(_, { wrap: !0, style: { marginBottom: 8 }, children: [
      /* @__PURE__ */ h(
        "select",
        {
          "aria-label": "连线起点",
          value: v ?? "",
          onChange: (C) => $(C.target.value || void 0),
          children: [
            /* @__PURE__ */ o("option", { value: "", children: "起点节点" }),
            n.map((C) => /* @__PURE__ */ o("option", { value: C.id, children: C.id }, C.id))
          ]
        }
      ),
      /* @__PURE__ */ h(
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
          disabled: !v || !j || v === j,
          onClick: () => {
            v && j && x(v, j);
          },
          children: "添加连线"
        }
      )
    ] }),
    i.length === 0 ? /* @__PURE__ */ o(E.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ o(le, { size: "small", columns: ye, dataSource: re, pagination: !1 }),
    s.length > 0 && /* @__PURE__ */ h(xe, { children: [
      /* @__PURE__ */ h(Ue, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        s.length,
        ")"
      ] }),
      s.map((C, q) => /* @__PURE__ */ h("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ o(de, { color: "red", children: C.code }),
        /* @__PURE__ */ h(
          E.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const V = /^nodes\.([^.]+)/.exec(C.path);
              V && I(V[1]);
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
const D = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, U = (e) => Array.isArray(e) ? e : [], f = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", M = (e, t = 0) => {
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
  return r.map(tr).filter((i) => i !== null).filter((i) => n.has(i.nodeTypeId) ? !1 : (n.add(i.nodeTypeId), !0));
}
function nr(e) {
  const t = te(e), r = Array.isArray(t) ? t : U(l(D(t), "resources", "items")), n = /* @__PURE__ */ new Set();
  return r.map((i) => {
    const d = D(i), a = f(l(d, "id")).trim();
    return !a || n.has(a) ? null : (n.add(a), {
      id: a,
      name: f(l(d, "name"), a),
      type: f(l(d, "type"), "unknown"),
      enabled: d.enabled !== !1,
      credentialId: typeof d.credentialId == "string" ? d.credentialId : null,
      credentialName: typeof d.credentialName == "string" ? d.credentialName : null
    });
  }).filter((i) => i !== null);
}
function st(e) {
  const t = D(e), r = f(l(t, "workflowId", "workflow_id", "id")).trim();
  return r ? {
    workflowId: r,
    name: f(l(t, "name", "title"), r),
    version: M(l(t, "version"), 1)
  } : null;
}
function lt(e) {
  const t = te(e);
  return (Array.isArray(t) ? t : U(l(D(t), "workflows", "items"))).map(st).filter((n) => n !== null);
}
function Se(e) {
  const t = D(e), r = {};
  for (const [n, i] of Object.entries(t))
    i !== void 0 && (r[n] = i);
  return r;
}
function ir(e) {
  const t = D(e), r = f(l(t, "id", "nodeId", "node_id")).trim();
  if (!r) return null;
  const n = D(l(t, "position", "layout"));
  return {
    id: r,
    type: f(l(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: Se(l(t, "config", "configuration")),
    x: M(l(t, "x") ?? l(n, "x"), 0),
    y: M(l(t, "y") ?? l(n, "y"), 0)
  };
}
function or(e) {
  const t = D(e), r = f(l(t, "source", "from", "sourceId")).trim(), n = f(l(t, "target", "to", "targetId")).trim();
  return !r || !n ? null : {
    source: r,
    sourcePort: f(l(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: f(l(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function ar(e) {
  const t = te(e), r = D(t), n = st(r) ?? { workflowId: "", name: "", version: 1 }, i = U(l(r, "nodes")).map(ir).filter((c) => c !== null), d = U(l(r, "edges")).map(or).filter((c) => c !== null), a = D(l(r, "trigger")), s = {
    kind: f(l(a, "kind", "type"), "manual"),
    cron: l(a, "cron") === void 0 ? null : f(a.cron),
    eventName: l(a, "eventName", "event_name") === void 0 ? null : f(l(a, "eventName", "event_name")),
    config: Se(l(a, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: i,
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
    const i = D(n), d = l(i, "credentialId", "credential_id");
    return {
      id: f(l(i, "id"), "—"),
      name: f(l(i, "name"), "—"),
      type: f(l(i, "type"), "—"),
      provider: f(l(i, "provider"), "—"),
      enabled: he(l(i, "enabled")),
      credentialId: d == null ? null : f(d),
      credentialName: l(i, "credentialName") == null ? null : f(l(i, "credentialName")),
      config: Se(l(i, "config", "configuration"))
    };
  });
}
function ct(e) {
  const t = te(e);
  return (Array.isArray(t) ? t : U(l(D(t), "credentials", "items"))).map((n) => {
    const i = D(n);
    return {
      id: f(l(i, "id"), "—"),
      name: f(l(i, "name"), "—"),
      kind: f(l(i, "kind"), "—"),
      version: M(l(i, "version"))
    };
  });
}
function lr(e) {
  const t = te(e);
  return (Array.isArray(t) ? t : U(l(D(t), "tests", "items"))).map((n) => {
    const i = D(n);
    return {
      resourceId: f(l(i, "resourceId", "resource_id"), "—"),
      ok: he(l(i, "ok")),
      detail: f(l(i, "detail"), "")
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
    const i = D(n), d = (a) => l(i, a) == null ? null : f(l(i, a));
    return {
      workflowId: f(l(i, "workflowId", "workflow_id"), "—"),
      version: M(l(i, "version")),
      kind: f(l(i, "kind"), "manual"),
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
  const t = te(e), r = D(t);
  if (Object.keys(r).length === 0) return null;
  const n = U(l(r, "files")).map((d) => {
    const a = D(d);
    return {
      path: f(l(a, "path"), "—"),
      sourceDate: f(l(a, "sourceDate"), ""),
      inLedger: he(l(a, "inLedger")),
      expectedTargets: M(l(a, "expectedTargets")),
      succeededTargets: M(l(a, "succeededTargets")),
      incompleteTargets: M(l(a, "incompleteTargets")),
      missingTargets: f(l(a, "missingTargets"), ""),
      wouldSkip: he(l(a, "wouldSkip")),
      wouldCollect: he(l(a, "wouldCollect")),
      duplicateRisk: he(l(a, "duplicateRisk"))
    };
  }), i = D(l(r, "summary"));
  return {
    resourceId: f(l(r, "resourceId"), "—"),
    from: f(l(r, "from"), ""),
    to: f(l(r, "to"), ""),
    files: n,
    summary: {
      discovered: M(l(i, "discovered")),
      alreadyComplete: M(l(i, "alreadyComplete")),
      needsCollection: M(l(i, "needsCollection")),
      duplicateRisk: M(l(i, "duplicateRisk"))
    }
  };
}
function ur(e) {
  const t = te(e);
  return (Array.isArray(t) ? t : U(l(D(t), "files", "items"))).map((n) => {
    const i = D(n);
    return {
      id: f(l(i, "id"), "—"),
      path: f(l(i, "path"), "—"),
      identityKey: f(l(i, "identityKey"), ""),
      sourceResourceId: f(l(i, "sourceResourceId"), ""),
      length: M(l(i, "length")),
      sourceDate: f(l(i, "sourceDate"), ""),
      collectedAt: f(l(i, "collectedAt"), ""),
      status: f(l(i, "status"), "—"),
      deliveries: U(l(i, "deliveries")).map((d) => {
        const a = D(d);
        return {
          id: f(l(a, "id"), "—"),
          collectionFileId: f(l(a, "collectionFileId"), ""),
          targetId: f(l(a, "targetId"), "—"),
          targetKind: f(l(a, "targetKind"), "record"),
          status: f(l(a, "status"), "—"),
          attempts: M(l(a, "attempts")),
          records: M(l(a, "records")),
          error: l(a, "error") == null ? null : f(l(a, "error")),
          accepted: M(l(a, "accepted")),
          rejected: M(l(a, "rejected")),
          warnings: M(l(a, "warnings")),
          diagnosis: dt(l(a, "diagnosis"))
        };
      })
    };
  });
}
function pr(e) {
  const t = te(e), r = D(t), n = U(l(r, "runs")).map((d) => {
    const a = D(d);
    return {
      id: f(l(a, "id"), "—"),
      workflowId: f(l(a, "workflowId", "workflow_id"), "—"),
      status: f(l(a, "status"), "—"),
      startedAt: f(l(a, "startedAt", "started_at"), ""),
      finishedAt: l(a, "finishedAt", "finished_at") == null ? null : f(l(a, "finishedAt", "finished_at")),
      error: l(a, "error") == null ? null : f(l(a, "error"))
    };
  }), i = U(l(r, "deliveries")).map((d) => {
    const a = D(d);
    return {
      id: f(l(a, "id"), "—"),
      collectionFileId: f(l(a, "collectionFileId", "collection_file_id"), ""),
      targetId: f(l(a, "targetId", "target_id"), "—"),
      targetKind: f(l(a, "targetKind", "target_kind"), "record"),
      status: f(l(a, "status"), "—"),
      attempts: M(l(a, "attempts")),
      records: M(l(a, "records")),
      error: l(a, "error") == null ? null : f(l(a, "error")),
      diagnosis: dt(l(a, "diagnosis"))
    };
  });
  return { runs: n, deliveries: i };
}
function fr(e) {
  if (e == null) return null;
  const t = te(e), r = D(t);
  if (Object.keys(r).length === 0) return null;
  const n = U(l(r, "nodes")).slice(0, 64).map((i) => {
    const d = D(i);
    return {
      nodeId: f(l(d, "nodeId", "node_id"), "—"),
      status: f(l(d, "status"), "—"),
      records: M(l(d, "records")),
      files: M(l(d, "files")),
      error: l(d, "error") == null ? null : f(l(d, "error"), void 0),
      samples: U(l(d, "samples")).slice(0, 3).map((a) => f(a))
    };
  });
  return {
    status: f(l(r, "status"), "—"),
    error: l(r, "error") == null ? null : f(l(r, "error"), void 0),
    executed: U(l(r, "executed")).map((i) => f(i)),
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
  onSave: i,
  ctx: d,
  workflowId: a
}) {
  const [s, c] = b(r), [u, w] = b([]), [x, S] = b(null);
  J(() => {
    c(r);
  }, [r]), J(() => {
    e && d.hubQuery("industrial/trigger-status").then((p) => w(cr(p))).catch((p) => S(p instanceof Error ? p.message : String(p)));
  }, [d, e]);
  const I = u.find((p) => p.workflowId === a);
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
        S(null), i(s), t();
      },
      width: 720,
      children: /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ h(_, { direction: "vertical", style: { width: "100%" }, children: [
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
        /* @__PURE__ */ h(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(E.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ h(_, { wrap: !0, children: [
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
          /* @__PURE__ */ h(E.Text, { type: "secondary", children: [
            "下次运行：",
            (I == null ? void 0 : I.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        I && /* @__PURE__ */ o(
          le,
          {
            rowKey: "workflowId",
            dataSource: [I],
            pagination: !1,
            columns: [
              { title: "时区", dataIndex: "timezone", render: (p) => p ?? "UTC" },
              { title: "状态", dataIndex: "lastStatus", render: (p) => p ?? "未运行" },
              { title: "上次运行", dataIndex: "lastStartedAt", render: (p) => p ?? "—" },
              { title: "错误", dataIndex: "lastError", render: (p) => p ?? "—" }
            ]
          }
        ),
        x && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: x })
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
  resourceId: i,
  lookbackDays: d
}) {
  const [a, s] = b(null), [c, u] = b(null), [w, x] = b(!1), S = z(async () => {
    x(!0), u(null);
    try {
      await r.hubCommand("industrial/preview-backfill", {
        workflowId: n,
        resourceId: i,
        lookbackDays: d
      }), s(dr(await r.hubQuery("industrial/backfill-preview")));
    } catch (I) {
      u(I instanceof Error ? I.message : String(I));
    } finally {
      x(!1);
    }
  }, [r, d, i, n]);
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
      children: /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ h(_, { children: [
          /* @__PURE__ */ h(R, { type: "primary", loading: w, onClick: () => void S(), children: [
            "扫描最近 ",
            d,
            " 天"
          ] }),
          /* @__PURE__ */ h(E.Text, { type: "secondary", children: [
            "资源：",
            i || "未选择"
          ] })
        ] }),
        c && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: c }),
        a && /* @__PURE__ */ h(xe, { children: [
          /* @__PURE__ */ h(E.Text, { children: [
            "发现 ",
            a.summary.discovered,
            " 个文件；已完成 ",
            a.summary.alreadyComplete,
            "； 待采集 ",
            a.summary.needsCollection,
            "；重复风险 ",
            a.summary.duplicateRisk,
            "。"
          ] }),
          /* @__PURE__ */ o(
            le,
            {
              rowKey: "path",
              dataSource: a.files,
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
  const [t, r] = It.useMessage(), [n, i] = b([]), [d, a] = b([]), [s, c] = b([]), [u, w] = b([]), [x, S] = b(""), [I, y] = b(1), [p, N] = b(!0), [O, Z] = b([]), [v, $] = b(() => _e()), [j, g] = b(null), [T, k] = b([]), [B, re] = b(!1), [ye, C] = b(!0), [q, V] = b(!1), [ue, L] = b(!1), [ne, ut] = b(null), [je, Le] = b(!1), [qe, Fe] = b(!1), Te = oe(1), Me = oe(e);
  Me.current = e;
  const Ce = oe(v.trigger);
  J(() => {
    Ce.current = v.trigger;
  }, [v.trigger]), J(() => {
    let m = !0;
    const P = Me.current.hubQuery;
    return Promise.allSettled([
      P("industrial/nodes"),
      P("industrial/workflows"),
      P("industrial/resources")
    ]).then(([G, F, W]) => {
      var se;
      if (!m) return;
      const Q = [];
      if (G.status === "fulfilled" ? i(rr(G.value)) : Q.push(`节点描述: ${String(G.reason)}`), F.status === "fulfilled") {
        const Y = lt(F.value);
        a(Y);
        const ie = new URLSearchParams(window.location.search).get("workflowId"), ee = ie && Y.some((ce) => ce.workflowId === ie) ? ie : (se = Y[0]) == null ? void 0 : se.workflowId;
        ee && Be(ee);
      } else Q.push(`工作流: ${String(F.reason)}`);
      if (W.status === "fulfilled") {
        const Y = nr(W.value);
        w(Y);
        const ie = Y.map((ee) => ee.id);
        c(ie), S((ee) => ee || ie[0] || "");
      } else Q.push(`资源: ${String(W.reason)}`);
      Z(Q);
    }).finally(() => {
      m && N(!1);
    }), () => {
      m = !1;
    };
  }, []);
  const ae = Xe(
    () => new Map(n.map((m) => [m.nodeTypeId, m])),
    [n]
  ), Ne = v.nodes.find((m) => m.id === j) ?? null, me = z(async (m, P, G) => {
    re(!0);
    try {
      return await X().api.hubCommand(m, P), t.success(G), !0;
    } catch (F) {
      return t.error(`${m} failed: ${F instanceof Error ? F.message : String(F)}`), !1;
    } finally {
      re(!1);
    }
  }, [t]), Be = z(async (m) => {
    try {
      const P = await e.hubQuery("industrial/workflow-definitions"), F = (te(P) ?? []).find((se) => {
        const Y = se;
        return String((Y == null ? void 0 : Y.workflowId) ?? "") === m;
      }), W = F === void 0 ? { ..._e(m), name: m } : ar(F);
      $(W), y(Number(W.layout.lookbackDays ?? 1) || 1);
      const Q = W.nodes.map((se) => /^n(\d+)$/.exec(se.id)).filter(Boolean).reduce((se, Y) => Math.max(se, Number(Y[1])), 0);
      Te.current = Q + 1, g(null), V(!1), k([]), L(!1);
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
    for (const F of v.nodes)
      for (const W of ["resourceId", "targetResourceId"]) {
        const Q = (G = F.config) == null ? void 0 : G[W];
        typeof Q == "string" && Q.trim() && m.add(Q.trim());
      }
    const P = {
      ...v,
      trigger: Ce.current,
      resourceReferences: [...m],
      layout: { ...v.layout, lookbackDays: I }
    };
    await me("industrial/save-workflow", P, "已保存") && ($(P), L(!1));
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
    const F = fe(ae, m);
    if (!F) return;
    const W = `n${Te.current++}`;
    $((Q) => ({
      ...Q,
      nodes: [...Q.nodes, $t(F, W, P, G)]
    })), g(W), V(!0), k([]), L(!0);
  }, [ae]), yt = z((m) => {
    const P = Te.current - 1;
    Ee(m, 80 + P % 3 * 190, 80 + Math.floor(P / 3) * 130);
  }, [Ee]), Ke = z((m, P) => {
    const G = v.nodes.find((ce) => ce.id === m), F = v.nodes.find((ce) => ce.id === P);
    if (!G || !F)
      return t.warning("连线失败：端点不在画布上"), null;
    const W = fe(ae, G.type), Q = fe(ae, F.type);
    if (!W || !Q) return null;
    let se = G, Y = F, ie = Je(W, Q);
    if (!ie) {
      const ce = Je(Q, W);
      if (!ce)
        return t.warning(`端口类型不兼容：${W.name} → ${Q.name}`), null;
      se = F, Y = G, ie = ce;
    }
    const ee = Ot(v, {
      source: se.id,
      sourcePort: ie.sourcePort,
      target: Y.id,
      targetPort: ie.targetPort
    }, ae);
    return ee.issue ? (t.warning(`${ee.issue.code}: ${ee.issue.message}`), k((ce) => [...ce, ee.issue]), null) : ee.definition === v ? (t.info("这条连线已经存在"), null) : ($(ee.definition), L(!0), ie);
  }, [ae, v, t]), mt = z(() => {
    const m = Mt(v, ae, new Set(s));
    k(m), m.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : (t.warning(`发现 ${m.length} 个问题，见属性抽屉定位列表`), V(!0));
  }, [v, ae, s, t]), ht = z((m, P) => {
    $((G) => ({
      ...G,
      edges: G.edges.map((F, W) => W === m ? P : F)
    })), L(!0);
  }, []);
  return p ? /* @__PURE__ */ o(tt, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ h("div", { className: "industrial-designer", children: [
    r,
    /* @__PURE__ */ h(br, { className: "industrial-header", children: [
      /* @__PURE__ */ h("div", { className: "industrial-title", children: [
        /* @__PURE__ */ o(E.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ o(E.Text, { type: "secondary", children: v.name || v.workflowId }),
        ue && /* @__PURE__ */ o(de, { color: "orange", children: "未保存" }),
        ne && /* @__PURE__ */ o(de, { color: ne.error ? "error" : "success", children: ne.status })
      ] }),
      /* @__PURE__ */ h("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ o(
          R,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": ye,
            onClick: () => C((m) => !m),
            children: ye ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ o(
          R,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => V((m) => !m),
            children: "属性"
          }
        ),
        /* @__PURE__ */ o(R, { size: "small", "data-testid": "open-trigger", onClick: () => Le(!0), children: "触发器" }),
        /* @__PURE__ */ o(
          R,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => Fe(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ o("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ h(_, { size: 4, children: [
          /* @__PURE__ */ o(R, { size: "small", loading: B, onClick: () => void pt(), children: "保存" }),
          /* @__PURE__ */ o(R, { size: "small", onClick: mt, children: "校验" }),
          /* @__PURE__ */ o(R, { size: "small", loading: B, onClick: () => void ft(), children: "发布" }),
          /* @__PURE__ */ o(R, { size: "small", type: "primary", loading: B, onClick: () => void gt(), children: "运行" })
        ] }),
        /* @__PURE__ */ h(
          "select",
          {
            className: "industrial-resource-select",
            "aria-label": "运行资源",
            value: x,
            onChange: (m) => S(m.target.value),
            children: [
              /* @__PURE__ */ o("option", { value: "", children: "运行资源" }),
              s.map((m) => /* @__PURE__ */ o("option", { value: m, children: m }, m))
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
            value: I,
            onChange: (m) => {
              y(Number(m) || 1), L(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ h(Ir, { className: `industrial-body${ye ? "" : " industrial-body--collapsed"}`, children: [
      ye ? /* @__PURE__ */ h("aside", { className: "industrial-palette", children: [
        d.length > 0 && /* @__PURE__ */ o(
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
        /* @__PURE__ */ o(Dt, { descriptors: n, loading: !1, onAdd: yt })
      ] }) : /* @__PURE__ */ o("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ o(R, { type: "text", size: "small", onClick: () => C(!0), children: "节点库" }) }),
      /* @__PURE__ */ h("section", { className: "industrial-center", children: [
        O.length > 0 && /* @__PURE__ */ o(
          pe,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: O.join("；")
          }
        ),
        /* @__PURE__ */ o(
          Pt,
          {
            definition: v,
            catalog: ae,
            selectedId: j,
            onSelect: (m) => {
              g(m), m && V(!0);
            },
            onDropNode: Ee,
            onCreateEdge: Ke,
            onPositions: (m) => {
              $((P) => qt(P, m)), L(!0);
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
          ne && /* @__PURE__ */ o(
            pe,
            {
              style: { marginBottom: 12 },
              type: ne.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${ne.status}`,
              description: /* @__PURE__ */ h(_, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                ne.error && /* @__PURE__ */ o("span", { children: ne.error }),
                ne.executed.length > 0 && /* @__PURE__ */ h("span", { children: [
                  "已执行节点: ",
                  ne.executed.join(", ")
                ] }),
                ne.nodes.length > 0 && /* @__PURE__ */ o(
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
          /* @__PURE__ */ o(
            Yt,
            {
              node: Ne,
              descriptor: Ne ? fe(ae, Ne.type) ?? null : null,
              nodes: v.nodes,
              edges: v.edges,
              catalog: ae,
              resources: u,
              issues: T,
              onApplyConfig: (m) => {
                j && ($((P) => Lt(P, j, m)), L(!0));
              },
              onDeleteNode: () => {
                $((m) => zt(m, j ?? "")), g(null), L(!0);
              },
              onDeleteEdge: (m) => {
                $((P) => At(P, m)), L(!0);
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
    je && /* @__PURE__ */ o(
      mr,
      {
        open: je,
        onClose: () => Le(!1),
        trigger: v.trigger,
        resourceIds: s,
        workflowId: v.workflowId,
        ctx: e,
        onSave: (m) => {
          Ce.current = m, $((P) => ({ ...P, trigger: m })), L(!0);
        }
      }
    ),
    qe && /* @__PURE__ */ o(
      vr,
      {
        open: qe,
        onClose: () => Fe(!1),
        ctx: e,
        workflowId: v.workflowId,
        resourceId: x,
        lookbackDays: I
      }
    )
  ] });
}
function kr({ block: e, ctx: t }) {
  const [r, n] = b([]), [i, d] = b(!1), [a, s] = b(""), [c, u] = b(null), [w, x] = b(!1), S = z(async () => {
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
    var N;
    const p = a.trim();
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
      }), d(!1), s(""), await S(), (N = t.navigate) == null || N.call(t, `/industrial/designer?workflowId=${encodeURIComponent(Z)}`);
    } catch (O) {
      u(O instanceof Error ? O.message : String(O));
    } finally {
      x(!1);
    }
  }, [t, S, a]), y = z((p) => {
    var N;
    (N = t.navigate) == null || N.call(t, `/industrial/designer?workflowId=${encodeURIComponent(p)}`);
  }, [t]);
  return /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ o(E.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ o(R, { type: "primary", onClick: () => d(!0), children: "新建工作流" })
    ] }),
    c && /* @__PURE__ */ o(E.Text, { type: "danger", children: c }),
    /* @__PURE__ */ o(
      le,
      {
        rowKey: "workflowId",
        dataSource: r,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (p) => /* @__PURE__ */ o(R, { type: "link", style: { padding: 0 }, onClick: () => y(p), children: p })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ o(
      ve,
      {
        open: i,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: w,
        onCancel: () => d(!1),
        onOk: () => void I(),
        children: /* @__PURE__ */ h(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(E.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ o(
            K,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: a,
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
  const t = { ...e.config }, r = (n, i) => {
    i.trim() && (t[n] = i);
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
  const [r, n] = b([]), [i, d] = b([]), [a, s] = b([]), [c, u] = b(De), [w, x] = b(!1), [S, I] = b(null), [y, p] = b(!1), N = z(async () => {
    try {
      const [g, T, k] = await Promise.all([
        t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        t.hubQuery("industrial/credentials"),
        t.hubQuery("industrial/resource-tests")
      ]);
      n(sr(g)), d(ct(T)), s(lr(k));
    } catch (g) {
      I(g instanceof Error ? g.message : String(g));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    N();
  }, [N]);
  const O = z((g) => {
    const T = (...k) => {
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
      root: T("root", "path"),
      host: T("server", "host", "dataSource", "data_source"),
      database: T("database", "initialCatalog", "initial_catalog", "path"),
      port: T("port"),
      table: T("table"),
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
      }), x(!1), u(De), await N();
    } catch (T) {
      I(T instanceof Error ? T.message : String(T));
    } finally {
      p(!1);
    }
  }, [t, c, N]), v = z(async (g) => {
    I(null);
    try {
      await t.hubCommand("industrial/test-resource", { id: g });
    } catch (T) {
      I(T instanceof Error ? T.message : String(T));
    }
    await N();
  }, [t, N]), $ = z(async (g) => {
    I(null);
    try {
      await t.hubCommand("industrial/delete-resource", { id: g }), await N();
    } catch (T) {
      I(T instanceof Error ? T.message : String(T));
    }
  }, [t, N]), j = new Map(a.map((g) => [g.resourceId, g]));
  return /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ o(E.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ o(R, { type: "primary", onClick: () => {
        u(De), x(!0);
      }, children: "新建资源" })
    ] }),
    S && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: S }),
    /* @__PURE__ */ o(
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
            render: (g, T) => {
              const k = j.get(T.id);
              return k ? /* @__PURE__ */ o(E.Text, { type: k.ok ? "success" : "danger", children: k.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (g, T) => /* @__PURE__ */ h(_, { size: 4, children: [
              /* @__PURE__ */ o(R, { size: "small", onClick: () => void v(T.id), children: "测试连接" }),
              /* @__PURE__ */ o(R, { size: "small", onClick: () => O(T), children: "编辑" }),
              /* @__PURE__ */ o(R, { size: "small", danger: !0, onClick: () => void $(T.id), children: "删除" })
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
        confirmLoading: y,
        onCancel: () => x(!1),
        onOk: () => void Z(),
        children: /* @__PURE__ */ h(_, { direction: "vertical", style: { width: "100%" }, children: [
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
          (c.type === "sqlserver" || c.type === "postgresql" || c.type === "mysql") && /* @__PURE__ */ h(_, { children: [
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
              options: i.map((g) => ({ value: g.id, label: g.name })),
              onChange: (g) => u({ ...c, credentialId: g ?? "" })
            }
          ),
          /* @__PURE__ */ h(_, { children: [
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
  const [r, n] = b([]), [i, d] = b(!1), [a, s] = b(""), [c, u] = b(""), [w, x] = b("username-password"), [S, I] = b(""), [y, p] = b(!1), [N, O] = b(null), [Z, v] = b(!1), $ = z(async () => {
    try {
      n(ct(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
    } catch (k) {
      O(k instanceof Error ? k.message : String(k));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    $();
  }, [$]);
  const j = z(() => {
    s(""), u(""), x("username-password"), I(""), p(!1), d(!0);
  }, []), g = z((k) => {
    s(k.id), u(k.name), x(k.kind), I(""), p(!0), d(!0);
  }, []), T = z(async () => {
    if (!a.trim()) {
      O("请输入凭证 ID");
      return;
    }
    if (!S.trim()) {
      O("请输入凭证内容");
      return;
    }
    v(!0), O(null);
    try {
      y ? await t.hubCommand("industrial/rotate-credential", { id: a, secret: S }) : await t.hubCommand("industrial/save-credential", { id: a, name: c || a, kind: w, secret: S }), d(!1), await $();
    } catch (k) {
      O(k instanceof Error ? k.message : String(k));
    } finally {
      v(!1);
    }
  }, [t, a, w, $, c, y, S]);
  return /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ o(E.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ o(R, { type: "primary", onClick: j, children: "新建凭证" })
    ] }),
    N && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: N }),
    /* @__PURE__ */ o(
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
            render: (k, B) => /* @__PURE__ */ o(R, { size: "small", onClick: () => g(B), children: "轮换" })
          }
        ]
      }
    ),
    /* @__PURE__ */ o(
      ve,
      {
        open: i,
        title: y ? "轮换凭证" : "新建凭证",
        okText: y ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: Z,
        onCancel: () => d(!1),
        onOk: () => void T(),
        children: /* @__PURE__ */ h(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(
            K,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: a,
              disabled: y,
              onChange: (k) => s(k.target.value)
            }
          ),
          !y && /* @__PURE__ */ o(
            K,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: c,
              onChange: (k) => u(k.target.value)
            }
          ),
          /* @__PURE__ */ o(
            K,
            {
              "aria-label": "凭证内容",
              placeholder: y ? "新凭证内容" : "凭证内容",
              value: S,
              onChange: (k) => I(k.target.value)
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
  const [r, n] = b([]), [i, d] = b([]), [a, s] = b(null), [c, u] = b(null), [w, x] = b(!1), S = z(async () => {
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
    /* @__PURE__ */ o(E.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    a && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: a }),
    c && /* @__PURE__ */ o(pe, { type: "success", showIcon: !0, message: c }),
    /* @__PURE__ */ o(
      le,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (y) => /* @__PURE__ */ o(de, { color: Ve(y), children: y }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (y) => y ?? "—" },
          { title: "错误", dataIndex: "error", render: (y) => y ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ o(
      le,
      {
        rowKey: "id",
        dataSource: i,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (y) => /* @__PURE__ */ o(de, { color: Ve(y), children: y }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (y, p) => p.diagnosis ? /* @__PURE__ */ h(_, { direction: "vertical", size: 0, children: [
              /* @__PURE__ */ o(E.Text, { children: p.diagnosis.message }),
              /* @__PURE__ */ o(E.Text, { type: "secondary", children: p.diagnosis.action })
            ] }) : p.error ?? "—"
          },
          {
            title: "操作",
            key: "actions",
            render: (y, p) => /* @__PURE__ */ o(
              R,
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
  const [r, n] = b([]), [i, d] = b(null), a = z(async () => {
    try {
      n(ur(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (c) {
      d(c instanceof Error ? c.message : String(c));
    }
  }, [e == null ? void 0 : e.query, t]);
  J(() => {
    a();
  }, [a]);
  const s = (c) => /* @__PURE__ */ o(
    le,
    {
      rowKey: "id",
      dataSource: c.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (u) => /* @__PURE__ */ o(de, { children: u }) },
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
            /* @__PURE__ */ o(E.Text, { children: w.diagnosis.message }),
            /* @__PURE__ */ o(E.Text, { type: "secondary", children: w.diagnosis.action }),
            w.error && /* @__PURE__ */ o(E.Text, { type: "secondary", children: w.error })
          ] }) : w.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ h(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ o(E.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    i && /* @__PURE__ */ o(pe, { type: "error", showIcon: !0, message: i }),
    /* @__PURE__ */ o(
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
function Rr(e) {
  wt(e);
  const t = typeof document > "u" ? null : document.createElement("style");
  t && (t.dataset.industrialDesigner = "true", t.textContent = Dr, document.head.appendChild(t));
  const r = ({ ctx: d }) => Re(xr, { ctx: d });
  r.displayName = "IndustrialWorkflowCanvas";
  const n = (d, a) => {
    const s = ({ block: c, ctx: u }) => Re(a, { block: c, ctx: u });
    return s.displayName = d, e.blocks.register(d, s);
  }, i = [
    e.blocks.register("industrial-workflow-canvas", r),
    n("industrial-workflow-list", kr),
    n("industrial-resource-console", Cr),
    n("industrial-credential-console", Nr),
    n("industrial-execution-center", Er),
    n("industrial-file-details", Pr)
  ];
  return () => {
    for (const d of i) d == null || d();
    t == null || t.remove();
  };
}
export {
  Rr as default,
  Rr as register
};
