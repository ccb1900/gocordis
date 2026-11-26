let ye = null;
function mt(e) {
  ye = e;
}
function V() {
  if (!ye && typeof globalThis < "u" && (ye = globalThis.__CORDIS_CONSOLE ?? null), !ye)
    throw new Error("[industrial-designer] console facade is not available");
  return ye;
}
const o = (...e) => V().jsxRuntime.jsx(...e), w = (...e) => V().jsxRuntime.jsxs(...e), be = (e) => V().jsxRuntime.jsx(V().jsxRuntime.Fragment, e), Ne = (...e) => V().React.createElement(...e), O = (...e) => V().React.useCallback(...e), G = (...e) => V().React.useEffect(...e), Je = (...e) => V().React.useMemo(...e), ae = (...e) => V().React.useRef(...e), I = (...e) => V().React.useState(...e), A = (e) => {
  const t = (r) => {
    const n = e.split(".").reduce((a, l) => a == null ? void 0 : a[l], V().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Ne(n, r ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, oe = A("Alert"), $ = A("Button"), Fe = A("Divider"), ht = A("Drawer"), Ye = A("Empty"), Ee = Object.assign(A("Form"), {
  Item: A("Form.Item"),
  useForm: (...e) => V().antd.Form.useForm(...e)
}), M = Object.assign(A("Input"), {
  Number: A("InputNumber"),
  TextArea: A("Input.TextArea")
}), He = A("InputNumber"), wt = Object.assign(A("Layout"), {
  Header: A("Layout.Header"),
  Content: A("Layout.Content")
}), me = A("Modal"), ie = A("Select"), _ = A("Space"), Ve = A("Spin"), Xe = A("Switch"), ne = A("Table"), se = A("Tag"), N = Object.assign(A("Typography"), {
  Title: A("Typography.Title"),
  Text: A("Typography.Text")
}), vt = new Proxy({}, {
  get: (e, t) => (...r) => {
    var a;
    const n = (a = V().antd.message) == null ? void 0 : a[t];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${t} unavailable`);
    return n(...r);
  }
}), Ze = 148, et = 46, Me = {
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
}, Be = (e) => Me[e] ?? Me[e.split(".")[0]] ?? "#4d6bfe";
function bt(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function It(e, t = /* @__PURE__ */ new Map(), r) {
  return {
    nodes: e.nodes.map((n) => {
      const a = t.get(n.type), l = (a == null ? void 0 : a.category) ?? "General", s = r === n.id;
      return {
        id: n.id,
        data: {
          label: (a == null ? void 0 : a.name) ?? n.type,
          nodeTypeId: n.type,
          category: l,
          icon: (a == null ? void 0 : a.icon) ?? "AppstoreOutlined",
          config: { ...n.config }
        },
        style: {
          x: n.x,
          y: n.y,
          fill: Be(l),
          stroke: s ? "#111111" : Be(l),
          lineWidth: s ? 3 : 1.5,
          radius: 10,
          port: !0,
          ports: [
            ...((a == null ? void 0 : a.inputs) ?? []).map((c, d, f) => ({
              key: `in:${c.name}`,
              placement: [0, (d + 1) / (f.length + 1)],
              r: 5,
              stroke: c.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((a == null ? void 0 : a.outputs) ?? []).map((c, d, f) => ({
              key: `out:${c.name}`,
              placement: [1, (d + 1) / (f.length + 1)],
              r: 5,
              stroke: c.type === "Control" ? "#52c41a" : "#1677ff",
              fill: "#fff"
            }))
          ]
        }
      };
    }),
    edges: e.edges.map((n) => ({
      id: bt(n),
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
function xt(e, t, r) {
  const n = r && r.length > 0 ? new Set(r) : null, a = {};
  for (const l of e.nodes) {
    if (!t.has(l.id) || n && !n.has(l.id)) continue;
    const s = l.style.x, c = l.style.y;
    typeof s == "number" && typeof c == "number" && (a[l.id] = { x: s, y: c });
  }
  return a;
}
function kt(e, t) {
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
        size: [Ze, et],
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
    behaviors: tt()
  };
}
const St = "__industrial_drag_target__", Tt = "__industrial_connect_source__", Te = (e, t) => {
  var r;
  return (((r = e.isPanModifier) == null ? void 0 : r.call(e)) ?? !1) || t.button === 1 || t.buttons === 4;
}, Ke = (e, t) => {
  var y;
  if (t.targetType !== "node" || !((y = t.target) != null && y.id)) return !1;
  const r = t.client ?? t.canvas;
  if (!r || !e.graph) return !1;
  const n = String(t.target.id), a = e.graph.getNodeData().find((x) => String(x.id) === n), l = a == null ? void 0 : a.style;
  if (typeof (l == null ? void 0 : l.x) != "number" || typeof l.y != "number") return !1;
  const [s, c] = e.graph.getCanvasByClient([r.x, r.y]), d = Math.abs(s - l.x), f = Math.abs(c - l.y);
  return Math.abs(d - Ze / 2) <= 14 && f <= et / 2 + 10;
};
function tt(e = {}) {
  const t = e.connectionState ?? { armed: !1 };
  return [
    "zoom-canvas",
    {
      type: "drag-canvas",
      enable: (r) => r.targetType === "canvas" || Te(e, r),
      range: 0.9
    },
    {
      type: "drag-element",
      state: St,
      enable: (r) => (r.targetType === "node" || r.targetType === "combo") && !Ke(e, r) && !Te(e, r),
      onFinish: (r) => {
        var s, c;
        const n = e.graph;
        if (!n) return;
        const a = Array.isArray(r) ? r : r ? [String(r)] : void 0, l = n.getNodeData().map((d) => ({
          id: String(d.id),
          data: d.data ?? {},
          style: d.style ?? {}
        }));
        (c = e.onPositions) == null || c.call(e, xt(
          { nodes: l },
          ((s = e.allowedIds) == null ? void 0 : s.call(e)) ?? /* @__PURE__ */ new Set(),
          a
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: Tt,
      enable: (r) => Te(e, r) ? (t.armed = !1, !1) : t.armed ? r.targetType === "node" || r.targetType === "combo" : Ke(e, r) ? (t.armed = !0, !0) : !1,
      onCreate: (r) => {
        var s;
        t.armed = !1;
        const n = String(r.source ?? ""), a = String(r.target ?? "");
        if (!n || !a || n === a) return;
        const l = (s = e.onCreateEdge) == null ? void 0 : s.call(e, n, a);
        if (l)
          return {
            ...r,
            sourcePort: `out:${l.sourcePort}`,
            targetPort: `in:${l.targetPort}`
          };
      }
    }
  ];
}
const Ct = (e) => {
  var r;
  const t = e;
  return (t == null ? void 0 : t.targetType) === "node" ? (r = t.target) == null ? void 0 : r.id : void 0;
};
function Nt(e) {
  const {
    definition: t,
    catalog: r,
    selectedId: n,
    onSelect: a,
    onDropNode: l,
    onCreateEdge: s,
    onPositions: c
  } = e, d = ae(null), f = ae(null), y = ae(!1), x = ae(!1), k = ae({ armed: !1 }), v = ae({ onSelect: a, onDropNode: l, onCreateEdge: s, onPositions: c });
  v.current = { onSelect: a, onDropNode: l, onCreateEdge: s, onPositions: c };
  const m = Je(
    () => It(t, r, n),
    [t, r, n]
  ), p = ae(m);
  p.current = m;
  const C = ae(/* @__PURE__ */ new Set());
  return C.current = new Set(t.nodes.map((E) => E.id)), G(() => {
    const E = (q) => {
      q.code === "Space" && !q.repeat && (x.current = !0);
    }, T = (q) => {
      q.code === "Space" && (x.current = !1);
    }, R = () => {
      x.current = !1;
    };
    return window.addEventListener("keydown", E), window.addEventListener("keyup", T), window.addEventListener("blur", R), () => {
      window.removeEventListener("keydown", E), window.removeEventListener("keyup", T), window.removeEventListener("blur", R);
    };
  }, []), G(() => {
    const E = d.current;
    if (!E || y.current) return;
    let T = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: R }) => {
      if (T || !d.current || y.current) return;
      const q = kt(p.current, E), u = new R({
        ...q,
        behaviors: tt({
          graph: {
            getNodeData: () => {
              var h;
              return ((h = f.current) == null ? void 0 : h.getNodeData()) ?? [];
            },
            getCanvasByClient: (h) => {
              var H;
              const S = ((H = f.current) == null ? void 0 : H.getCanvasByClient(h)) ?? h;
              return [S[0], S[1]];
            }
          },
          isPanModifier: () => x.current,
          connectionState: k.current,
          allowedIds: () => C.current,
          onPositions: (h) => v.current.onPositions(h),
          onCreateEdge: (h, S) => v.current.onCreateEdge(h, S)
        })
      });
      u.on("node:click", (h) => {
        const S = Ct(h);
        S && v.current.onSelect(S);
      }), u.on("edge:click", () => v.current.onSelect(null)), u.on("canvas:click", () => v.current.onSelect(null)), u.on("pointerup", () => {
        k.current.armed = !1;
      }), f.current = u, E.__industrialGraph = u, y.current = !0, u.render();
    }), () => {
      T = !0;
    };
  }, []), G(() => {
    const E = f.current;
    !E || !y.current || (E.setData(m), E.draw());
  }, [m]), G(() => () => {
    var E;
    (E = f.current) == null || E.destroy(), f.current = null, y.current = !1;
  }, []), /* @__PURE__ */ o("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ w("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ o(
      "div",
      {
        ref: d,
        className: "industrial-graph-host",
        onDragOver: (E) => E.preventDefault(),
        onDrop: (E) => {
          E.preventDefault();
          const T = E.dataTransfer.getData("application/x-industrial-node") || E.dataTransfer.getData("text/plain");
          if (!T) return;
          const R = f.current, q = d.current;
          if (!R || !q) return;
          const u = q.getBoundingClientRect(), h = R.getCanvasByViewport([
            E.clientX - u.left,
            E.clientY - u.top
          ]);
          v.current.onDropNode(T, h[0], h[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ o("div", { className: "industrial-graph-hint", children: "拖动节点移动 · 从端口拖出连线 · 拖动画布平移（空格或中键可在节点上平移）" })
  ] }) });
}
function Et({ descriptors: e, loading: t, error: r }) {
  const n = /* @__PURE__ */ new Map();
  for (const l of e) {
    const s = l.category.split(".")[0] || "General", c = n.get(s) ?? [];
    c.push(l), n.set(s, c);
  }
  const a = (l, s) => {
    l.dataTransfer.setData("application/x-industrial-node", s.nodeTypeId), l.dataTransfer.setData("text/plain", s.nodeTypeId), l.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ w("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ o(N.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ o(N.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ o(Ve, { size: "small", style: { display: "block", marginTop: 16 } }),
    r && /* @__PURE__ */ o(N.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: r }),
    !t && !r && e.length === 0 && /* @__PURE__ */ o(Ye, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(n.entries()).map(([l, s]) => /* @__PURE__ */ w("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ o(N.Text, { strong: !0, style: { fontSize: 12 }, children: l }),
      s.map((c) => /* @__PURE__ */ w(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          onDragStart: (d) => a(d, c),
          title: c.description || c.nodeTypeId,
          children: [
            /* @__PURE__ */ o("div", { className: "industrial-palette-node-title", children: c.name }),
            /* @__PURE__ */ w("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ o(se, { bordered: !1, color: "blue", children: c.version }),
              /* @__PURE__ */ o(N.Text, { type: "secondary", style: { fontSize: 11 }, children: c.nodeTypeId })
            ] }),
            /* @__PURE__ */ w("div", { className: "industrial-palette-node-ports", children: [
              c.inputs.map((d) => /* @__PURE__ */ w(se, { bordered: !1, color: "default", children: [
                "in ",
                d.name,
                ": ",
                d.type
              ] }, `in-${d.name}`)),
              c.outputs.map((d) => /* @__PURE__ */ w(se, { bordered: !1, color: "green", children: [
                "out ",
                d.name,
                ": ",
                d.type
              ] }, `out-${d.name}`))
            ] })
          ]
        },
        c.nodeTypeId
      ))
    ] }, l))
  ] });
}
const Pe = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {}
});
function Pt(e) {
  const t = {};
  for (const [r, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (t[r] = n.default);
  return t;
}
function Dt(e, t, r = 0, n = 0) {
  return { id: t, type: e.nodeTypeId, config: Pt(e), x: r, y: n };
}
function ue(e, t) {
  return e.get(t);
}
function Ie(e, t, r) {
  return e[r].find((n) => n.name === t);
}
function Re(e, t) {
  return e !== "Unknown" && t !== "Unknown" && e === t;
}
function Ue(e, t) {
  for (const r of e.outputs)
    for (const n of t.inputs)
      if (Re(r.type, n.type))
        return { sourcePort: r.name, targetPort: n.name };
  return null;
}
const Y = (e, t, r) => ({ path: e, code: t, message: r });
function $t(e, t) {
  return e.source === t.source && e.sourcePort === t.sourcePort && e.target === t.target && e.targetPort === t.targetPort;
}
function Rt(e, t, r) {
  const n = `edges.${e.edges.length}`, a = e.nodes.find((y) => y.id === t.source), l = e.nodes.find((y) => y.id === t.target);
  if (!a || !l)
    return { definition: e, issue: Y(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((y) => $t(y, t)))
    return { definition: e };
  const s = ue(r, a.type), c = ue(r, l.type);
  if (!s || !c)
    return { definition: e, issue: Y(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const d = Ie(s, t.sourcePort, "outputs"), f = Ie(c, t.targetPort, "inputs");
  return !d || !f ? { definition: e, issue: Y(n, "PORT_MISSING", "declared port does not exist") } : Re(d.type, f.type) ? { definition: { ...e, edges: [...e.edges, t] } } : {
    definition: e,
    issue: Y(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${d.type} → ${f.type} is not a legal connection`
    )
  };
}
function _t(e, t) {
  return {
    ...e,
    nodes: e.nodes.filter((r) => r.id !== t),
    edges: e.edges.filter((r) => r.source !== t && r.target !== t)
  };
}
function Ot(e, t) {
  return { ...e, edges: e.edges.filter((r, n) => n !== t) };
}
function zt(e, t) {
  const r = { ...e };
  for (const [n, a] of Object.entries(t))
    a === void 0 ? delete r[n] : r[n] = a;
  return r;
}
function At(e, t, r) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === t ? { ...n, config: r } : n)
  };
}
function jt(e, t) {
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
function Lt(e) {
  var l;
  const t = /* @__PURE__ */ new Map();
  for (const s of e.nodes) t.set(s.id, []);
  for (const s of e.edges)
    (l = t.get(s.source)) == null || l.push(s.target);
  const r = /* @__PURE__ */ new Map(), n = [], a = (s) => {
    r.set(s, 1), n.push(s);
    for (const c of t.get(s) ?? []) {
      if (r.get(c) === 1)
        return [...n.slice(n.indexOf(c)), c];
      if (r.get(c) !== 2) {
        const d = a(c);
        if (d) return d;
      }
    }
    return n.pop(), r.set(s, 2), null;
  };
  for (const s of e.nodes)
    if (r.get(s.id) !== 2) {
      const c = a(s.id);
      if (c) return c;
    }
  return null;
}
function qt(e, t, r) {
  const n = [];
  e.workflowId.trim() || n.push(Y("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(Y("workflow", "INVALID_VERSION", "version must be positive"));
  const a = /* @__PURE__ */ new Map();
  for (const c of e.nodes)
    a.has(c.id) && n.push(Y(`nodes.${c.id}`, "DUPLICATE_NODE_ID", `duplicate node '${c.id}'`)), a.set(c.id, c);
  const l = /* @__PURE__ */ new Map();
  for (const c of e.nodes) {
    const d = ue(t, c.type);
    if (!d) {
      n.push(Y(
        `nodes.${c.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${c.type}' is not registered`
      ));
      continue;
    }
    l.set(c.id, d);
    const f = d.configurationSchema.required ?? [];
    for (const y of f) {
      const x = c.config[y];
      (x == null || x === "") && n.push(Y(
        `nodes.${c.id}.config.${y}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${y}' is missing`
      ));
    }
    r && typeof c.config.resourceId == "string" && (r.has(c.config.resourceId) || n.push(Y(
      `nodes.${c.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${c.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((c, d) => {
    const f = a.get(c.source), y = a.get(c.target);
    if (!f || !y) {
      n.push(Y(`edges.${d}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const x = l.get(c.source), k = l.get(c.target);
    if (!x || !k) return;
    const v = Ie(x, c.sourcePort, "outputs"), m = Ie(k, c.targetPort, "inputs");
    if (!v || !m) {
      n.push(Y(`edges.${d}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    Re(v.type, m.type) || n.push(Y(
      `edges.${d}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${v.type} → ${m.type} is not a legal connection`
    ));
  });
  for (const c of e.nodes) {
    const d = l.get(c.id);
    if (d)
      for (const f of d.inputs.filter((y) => y.required))
        e.edges.some((y) => y.target === c.id && y.targetPort === f.name && a.has(y.source)) || n.push(Y(
          `nodes.${c.id}.inputs.${f.name}`,
          "MISSING_INPUT",
          `required input '${f.name}' is not connected`
        ));
  }
  const s = Lt(e);
  return s && n.push(Y(`nodes.${s[0]}`, "GRAPH_CYCLE", `cycle: ${s.join(" → ")}`)), n;
}
const De = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function Ft(e) {
  const t = De(e.additionalProperties).enum;
  if (!Array.isArray(t)) return null;
  const r = t.filter((n) => typeof n == "string");
  return r.length > 0 ? r : null;
}
function rt(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && De(e.items).type === "string" ? "string-list" : e.type === "object" && De(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function $e(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, t]) => t != null && typeof t != "object").map(([t, r]) => ({ key: t, value: String(r) }));
}
function Ge(e) {
  const t = {}, r = [];
  for (const n of e) {
    const a = n.key.trim();
    a && (Object.prototype.hasOwnProperty.call(t, a) && r.push(a), t[a] = n.value);
  }
  return { record: t, duplicateKeys: r };
}
function ve(e) {
  if (!Array.isArray(e)) return [];
  const t = /* @__PURE__ */ new Set(), r = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const a = n.trim();
    !a || t.has(a) || (t.add(a), r.push(a));
  }
  return r;
}
function Mt(e, t) {
  const r = /* @__PURE__ */ new Set();
  for (const [n, a] of Object.entries(e))
    if (rt(a) === "key-value")
      for (const l of $e(t[n]))
        l.key.trim() && r.add(l.key.trim());
  return [...r];
}
function Bt({
  value: e,
  onChange: t,
  valueOptions: r,
  keyTitle: n = "目标字段",
  valueTitle: a = "来源字段",
  testId: l
}) {
  const s = JSON.stringify(e ?? {}), [c, d] = I(() => $e(e)), [f, y] = I(s);
  G(() => {
    s !== f && (d($e(e)), y(s));
  }, [s, f, e]);
  const x = (v) => {
    d(v), t == null || t(Ge(v).record);
  }, { duplicateKeys: k } = Ge(c);
  return /* @__PURE__ */ w("div", { "data-testid": l, className: "industrial-kv-editor", children: [
    c.map((v, m) => /* @__PURE__ */ w("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ o(
        M,
        {
          "aria-label": `${n} ${m + 1}`,
          value: v.key,
          placeholder: n,
          onChange: (p) => x(c.map((C, z) => z === m ? { ...C, key: p.target.value } : C))
        }
      ),
      r && r.length > 0 ? /* @__PURE__ */ o(
        ie,
        {
          "aria-label": `${a} ${m + 1}`,
          value: v.value || void 0,
          placeholder: a,
          options: r.map((p) => ({ label: p, value: p })),
          onChange: (p) => x(c.map((C, z) => z === m ? { ...C, value: p ?? "" } : C))
        }
      ) : /* @__PURE__ */ o(
        M,
        {
          "aria-label": `${a} ${m + 1}`,
          value: v.value,
          placeholder: a,
          onChange: (p) => x(c.map((C, z) => z === m ? { ...C, value: p.target.value } : C))
        }
      ),
      /* @__PURE__ */ o(
        $,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${m + 1}`,
          onClick: () => x(c.filter((p, C) => C !== m)),
          children: "删"
        }
      )
    ] }, m)),
    /* @__PURE__ */ o(
      $,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => x([...c, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    k.length > 0 && /* @__PURE__ */ w(N.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      k.join("、")
    ] })
  ] });
}
function Kt({ value: e, onChange: t, suggestions: r = [], testId: n }) {
  const a = JSON.stringify(ve(e)), [l, s] = I(() => ve(e)), [c, d] = I(a);
  G(() => {
    a !== c && (s(ve(e)), d(a));
  }, [a, c, e]);
  const f = (y) => {
    s(y), t == null || t(ve(y));
  };
  return /* @__PURE__ */ w("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ o("datalist", { id: `${n ?? "list"}-suggestions`, children: r.map((y) => /* @__PURE__ */ o("option", { value: y }, y)) }),
    l.map((y, x) => /* @__PURE__ */ w("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ o(
        M,
        {
          "aria-label": `字段 ${x + 1}`,
          value: y,
          list: `${n ?? "list"}-suggestions`,
          onChange: (k) => f(l.map((v, m) => m === x ? k.target.value : v))
        }
      ),
      /* @__PURE__ */ o(
        $,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${x + 1}`,
          onClick: () => f(l.filter((k, v) => v !== x)),
          children: "删"
        }
      )
    ] }, x)),
    /* @__PURE__ */ o($, { size: "small", type: "dashed", block: !0, onClick: () => f([...l, ""]), children: "添加字段" }),
    l.length === 0 && /* @__PURE__ */ o(N.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Ut({ value: e, onChange: t }) {
  const r = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, a] = I(r), [l, s] = I(null);
  G(() => {
    a(r), s(null);
  }, [r]);
  const c = (d) => {
    a(d);
    try {
      const f = JSON.parse(d);
      s(null), t == null || t(f);
    } catch (f) {
      s(f.message);
    }
  };
  return /* @__PURE__ */ w("div", { children: [
    /* @__PURE__ */ o(M.TextArea, { rows: 5, value: n, onChange: (d) => c(d.target.value) }),
    l && /* @__PURE__ */ o(N.Text, { type: "danger", style: { fontSize: 11 }, children: l })
  ] });
}
function Gt({ value: e, onChange: t, resources: r, resourceType: n }) {
  const a = r.filter((s) => s.enabled !== !1 && (!n || s.type === n)).map((s) => ({
    value: s.id,
    label: `${s.name} (${s.type})`
  })), l = r.find((s) => s.id === e);
  return /* @__PURE__ */ w("div", { children: [
    /* @__PURE__ */ o(ie, { allowClear: !0, value: e, onChange: t, options: a }),
    (l == null ? void 0 : l.credentialId) && /* @__PURE__ */ w(N.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      l.credentialName ?? l.credentialId
    ] })
  ] });
}
function Wt({ name: e, schema: t, required: r, resources: n, resourceType: a, properties: l, config: s }) {
  let c, d = "value";
  switch (rt(t)) {
    case "resource":
      c = /* @__PURE__ */ o(Gt, { resources: n, resourceType: a });
      break;
    case "enum":
      c = /* @__PURE__ */ o(
        ie,
        {
          allowClear: !0,
          options: (t.enum ?? []).map((f) => ({ label: String(f), value: f }))
        }
      );
      break;
    case "number":
      c = /* @__PURE__ */ o(He, { style: { width: "100%" } });
      break;
    case "boolean":
      c = /* @__PURE__ */ o(Xe, {}), d = "checked";
      break;
    case "key-value":
      c = /* @__PURE__ */ o(
        Bt,
        {
          testId: `field-${e}`,
          valueOptions: Ft(t) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      c = /* @__PURE__ */ o(Kt, { testId: `field-${e}`, suggestions: Mt(l, s) });
      break;
    case "json":
      c = /* @__PURE__ */ o(Ut, {});
      break;
    default:
      c = /* @__PURE__ */ o(M, { placeholder: t.description });
      break;
  }
  return /* @__PURE__ */ o(
    Ee.Item,
    {
      name: e,
      label: t.title ?? e,
      tooltip: t.description,
      valuePropName: d,
      rules: r && d === "value" ? [{ required: !0, message: `请填写 ${t.title ?? e}` }] : [],
      children: c
    },
    e
  );
}
function Qt(e) {
  const {
    node: t,
    descriptor: r,
    edges: n,
    catalog: a,
    resources: l,
    issues: s,
    onApplyConfig: c,
    onDeleteNode: d,
    onDeleteEdge: f,
    onUpdateEdge: y,
    onSelectNode: x
  } = e, [k] = Ee.useForm(), [v, m] = I("{}"), [p, C] = I(null);
  G(() => {
    k.setFieldsValue((t == null ? void 0 : t.config) ?? {}), m(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), C(null);
  }, [k, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const z = new Set((r == null ? void 0 : r.configurationSchema.required) ?? []), E = Object.entries((r == null ? void 0 : r.configurationSchema.properties) ?? {}), T = () => {
    if (E.length > 0) {
      k.validateFields().then((u) => c(zt((t == null ? void 0 : t.config) ?? {}, u)));
      return;
    }
    try {
      const u = JSON.parse(v);
      if (u === null || typeof u != "object" || Array.isArray(u))
        throw new Error("配置必须是 JSON 对象");
      C(null), c(u);
    } catch (u) {
      C(u.message);
    }
  }, R = n.map((u, h) => ({
    ...u,
    index: h,
    key: `${h}:${u.source}->${u.target}`
  })), q = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (u, h) => /* @__PURE__ */ w(N.Text, { style: { fontSize: 11 }, children: [
        h.source,
        " → ",
        h.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (u, h) => {
        const S = a.get(h.source), H = a.get(h.target);
        return /* @__PURE__ */ w("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ o(
            ie,
            {
              size: "small",
              value: h.sourcePort,
              style: { width: 110 },
              onChange: (K) => y(h.index, { ...h, sourcePort: K }),
              options: ((S == null ? void 0 : S.outputs) ?? []).map((K) => ({ label: `${K.name} (${K.type})`, value: K.name }))
            }
          ),
          /* @__PURE__ */ o(
            ie,
            {
              size: "small",
              value: h.targetPort,
              style: { width: 110 },
              onChange: (K) => y(h.index, { ...h, targetPort: K }),
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
      render: (u, h) => /* @__PURE__ */ o($, { danger: !0, size: "small", type: "text", onClick: () => f(h.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ w("div", { className: "industrial-properties-body", children: [
    !t && /* @__PURE__ */ o(Ye, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ w(be, { children: [
      /* @__PURE__ */ o(N.Text, { strong: !0, children: (r == null ? void 0 : r.name) ?? t.type }),
      /* @__PURE__ */ o(se, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ o(Ee, { form: k, layout: "vertical", size: "small", style: { marginTop: 12 }, children: E.map(([u, h]) => /* @__PURE__ */ o(
        Wt,
        {
          name: u,
          schema: h,
          required: z.has(u),
          resources: l,
          resourceType: typeof (r == null ? void 0 : r.configurationSchema.resourceType) == "string" ? r.configurationSchema.resourceType : void 0,
          properties: (r == null ? void 0 : r.configurationSchema.properties) ?? {},
          config: (t == null ? void 0 : t.config) ?? {}
        },
        u
      )) }),
      E.length === 0 && /* @__PURE__ */ w(be, { children: [
        /* @__PURE__ */ o(N.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ o(
          M.TextArea,
          {
            rows: 8,
            value: v,
            style: { marginTop: 6 },
            onChange: (u) => m(u.target.value)
          }
        ),
        p && /* @__PURE__ */ o(N.Text, { type: "danger", style: { fontSize: 12 }, children: p })
      ] }),
      /* @__PURE__ */ o($, { type: "primary", block: !0, onClick: T, children: "应用配置" }),
      /* @__PURE__ */ o($, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: d, children: "删除节点" }),
      (r == null ? void 0 : r.activityType) && /* @__PURE__ */ o(N.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: r.activityType })
    ] }),
    /* @__PURE__ */ w(Fe, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      n.length,
      ")"
    ] }),
    n.length === 0 ? /* @__PURE__ */ o(N.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ o(ne, { size: "small", columns: q, dataSource: R, pagination: !1 }),
    s.length > 0 && /* @__PURE__ */ w(be, { children: [
      /* @__PURE__ */ w(Fe, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        s.length,
        ")"
      ] }),
      s.map((u, h) => /* @__PURE__ */ w("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ o(se, { color: "red", children: u.code }),
        /* @__PURE__ */ w(
          N.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const S = /^nodes\.([^.]+)/.exec(u.path);
              S && x(S[1]);
            },
            children: [
              u.path,
              ": ",
              u.message
            ]
          }
        )
      ] }, `${u.path}-${h}`))
    ] })
  ] });
}
const Jt = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class Yt extends Error {
  constructor(t) {
    super(t), this.name = "DtoError";
  }
}
const P = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, B = (e) => Array.isArray(e) ? e : [], g = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", L = (e, t = 0) => {
  const r = typeof e == "string" ? Number(e) : e;
  return typeof r == "number" && Number.isFinite(r) ? r : t;
}, i = (e, ...t) => {
  for (const r of t)
    if (e[r] !== void 0 && e[r] !== null) return e[r];
};
function X(e) {
  const t = P(e);
  if (Object.prototype.hasOwnProperty.call(t, "data")) return t.data;
  if (typeof t.code == "string" && typeof t.message == "string")
    throw new Yt(`${t.code}: ${t.message}`);
  return e;
}
function Ht(e) {
  const t = g(e).trim().toLowerCase();
  return Jt.find((n) => n.toLowerCase() === t) ?? "Unknown";
}
function Vt(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const t = P(e), r = g(i(t, "name", "port", "id")).trim();
  return r ? {
    name: r,
    type: Ht(i(t, "type", "portType", "dataType")),
    required: i(t, "required", "isRequired") === !0
  } : null;
}
function We(e) {
  const t = B(e).map(Vt).filter((n) => n !== null), r = /* @__PURE__ */ new Set();
  return t.filter((n) => r.has(n.name) ? !1 : (r.add(n.name), !0));
}
const Xt = (e) => {
  const t = P(e);
  return Object.keys(t).length === 0 ? { type: "object", properties: {} } : t;
};
function Zt(e) {
  const t = P(e), r = g(i(t, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return r ? {
    nodeTypeId: r,
    name: g(i(t, "name", "title"), r),
    description: g(i(t, "description", "desc")),
    category: g(i(t, "category", "group"), "General"),
    icon: g(i(t, "icon"), "AppstoreOutlined"),
    version: g(i(t, "version"), "1.0.0"),
    inputs: We(i(t, "inputs", "inputPorts", "input_ports")),
    outputs: We(i(t, "outputs", "outputPorts", "output_ports")),
    configurationSchema: Xt(i(t, "configurationSchema", "configSchema", "schema")),
    activityType: g(i(t, "activityType", "activity_type")),
    dependencies: B(i(t, "dependencies")).map((n) => g(n)).filter(Boolean)
  } : null;
}
function er(e) {
  const t = X(e), r = Array.isArray(t) ? t : B(i(P(t), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return r.map(Zt).filter((a) => a !== null).filter((a) => n.has(a.nodeTypeId) ? !1 : (n.add(a.nodeTypeId), !0));
}
function tr(e) {
  const t = X(e), r = Array.isArray(t) ? t : B(i(P(t), "resources", "items")), n = /* @__PURE__ */ new Set();
  return r.map((a) => {
    const l = P(a), s = g(i(l, "id")).trim();
    return !s || n.has(s) ? null : (n.add(s), {
      id: s,
      name: g(i(l, "name"), s),
      type: g(i(l, "type"), "unknown"),
      enabled: l.enabled !== !1,
      credentialId: typeof l.credentialId == "string" ? l.credentialId : null,
      credentialName: typeof l.credentialName == "string" ? l.credentialName : null
    });
  }).filter((a) => a !== null);
}
function nt(e) {
  const t = P(e), r = g(i(t, "workflowId", "workflow_id", "id")).trim();
  return r ? {
    workflowId: r,
    name: g(i(t, "name", "title"), r),
    version: L(i(t, "version"), 1)
  } : null;
}
function at(e) {
  const t = X(e);
  return (Array.isArray(t) ? t : B(i(P(t), "workflows", "items"))).map(nt).filter((n) => n !== null);
}
function xe(e) {
  const t = P(e), r = {};
  for (const [n, a] of Object.entries(t))
    a !== void 0 && (r[n] = a);
  return r;
}
function rr(e) {
  const t = P(e), r = g(i(t, "id", "nodeId", "node_id")).trim();
  if (!r) return null;
  const n = P(i(t, "position", "layout"));
  return {
    id: r,
    type: g(i(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: xe(i(t, "config", "configuration")),
    x: L(i(t, "x") ?? i(n, "x"), 0),
    y: L(i(t, "y") ?? i(n, "y"), 0)
  };
}
function nr(e) {
  const t = P(e), r = g(i(t, "source", "from", "sourceId")).trim(), n = g(i(t, "target", "to", "targetId")).trim();
  return !r || !n ? null : {
    source: r,
    sourcePort: g(i(t, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: g(i(t, "targetPort", "target_port", "toPort"), "in")
  };
}
function ar(e) {
  const t = X(e), r = P(t), n = nt(r) ?? { workflowId: "", name: "", version: 1 }, a = B(i(r, "nodes")).map(rr).filter((d) => d !== null), l = B(i(r, "edges")).map(nr).filter((d) => d !== null), s = P(i(r, "trigger")), c = {
    kind: g(i(s, "kind", "type"), "manual"),
    cron: i(s, "cron") === void 0 ? null : g(s.cron),
    eventName: i(s, "eventName", "event_name") === void 0 ? null : g(i(s, "eventName", "event_name")),
    config: xe(i(s, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: a,
    edges: l,
    trigger: c,
    resourceReferences: B(i(r, "resourceReferences", "resource_references")).map((d) => g(d)).filter(Boolean),
    layout: xe(i(r, "layout"))
  };
}
const fe = (e) => e === !0 || typeof e == "string" && e.toLowerCase() === "true";
function sr(e) {
  const t = X(e);
  return (Array.isArray(t) ? t : B(i(P(t), "resources", "items"))).map((n) => {
    const a = P(n), l = i(a, "credentialId", "credential_id");
    return {
      id: g(i(a, "id"), "—"),
      name: g(i(a, "name"), "—"),
      type: g(i(a, "type"), "—"),
      provider: g(i(a, "provider"), "—"),
      enabled: fe(i(a, "enabled")),
      credentialId: l == null ? null : g(l),
      credentialName: i(a, "credentialName") == null ? null : g(i(a, "credentialName")),
      config: xe(i(a, "config", "configuration"))
    };
  });
}
function st(e) {
  const t = X(e);
  return (Array.isArray(t) ? t : B(i(P(t), "credentials", "items"))).map((n) => {
    const a = P(n);
    return {
      id: g(i(a, "id"), "—"),
      name: g(i(a, "name"), "—"),
      kind: g(i(a, "kind"), "—"),
      version: L(i(a, "version"))
    };
  });
}
function or(e) {
  const t = X(e);
  return (Array.isArray(t) ? t : B(i(P(t), "tests", "items"))).map((n) => {
    const a = P(n);
    return {
      resourceId: g(i(a, "resourceId", "resource_id"), "—"),
      ok: fe(i(a, "ok")),
      detail: g(i(a, "detail"), "")
    };
  });
}
const ot = (e) => {
  if (e == null) return null;
  const t = P(e);
  return Object.keys(t).length === 0 ? null : {
    code: g(i(t, "code"), "OTHER"),
    message: g(i(t, "message"), ""),
    action: g(i(t, "action"), "")
  };
};
function ir(e) {
  const t = X(e);
  return B(i(P(t), "triggers", "items")).map((n) => {
    const a = P(n), l = (s) => i(a, s) == null ? null : g(i(a, s));
    return {
      workflowId: g(i(a, "workflowId", "workflow_id"), "—"),
      version: L(i(a, "version")),
      kind: g(i(a, "kind"), "manual"),
      cron: l("cron"),
      eventName: l("eventName"),
      timezone: l("timezone"),
      resourceId: l("resourceId"),
      nextRun: l("nextRun"),
      lastStatus: l("lastStatus"),
      lastStartedAt: l("lastStartedAt"),
      lastError: l("lastError")
    };
  });
}
function lr(e) {
  if (e == null) return null;
  const t = X(e), r = P(t);
  if (Object.keys(r).length === 0) return null;
  const n = B(i(r, "files")).map((l) => {
    const s = P(l);
    return {
      path: g(i(s, "path"), "—"),
      sourceDate: g(i(s, "sourceDate"), ""),
      inLedger: fe(i(s, "inLedger")),
      expectedTargets: L(i(s, "expectedTargets")),
      succeededTargets: L(i(s, "succeededTargets")),
      incompleteTargets: L(i(s, "incompleteTargets")),
      missingTargets: g(i(s, "missingTargets"), ""),
      wouldSkip: fe(i(s, "wouldSkip")),
      wouldCollect: fe(i(s, "wouldCollect")),
      duplicateRisk: fe(i(s, "duplicateRisk"))
    };
  }), a = P(i(r, "summary"));
  return {
    resourceId: g(i(r, "resourceId"), "—"),
    from: g(i(r, "from"), ""),
    to: g(i(r, "to"), ""),
    files: n,
    summary: {
      discovered: L(i(a, "discovered")),
      alreadyComplete: L(i(a, "alreadyComplete")),
      needsCollection: L(i(a, "needsCollection")),
      duplicateRisk: L(i(a, "duplicateRisk"))
    }
  };
}
function cr(e) {
  const t = X(e);
  return (Array.isArray(t) ? t : B(i(P(t), "files", "items"))).map((n) => {
    const a = P(n);
    return {
      id: g(i(a, "id"), "—"),
      path: g(i(a, "path"), "—"),
      identityKey: g(i(a, "identityKey"), ""),
      sourceResourceId: g(i(a, "sourceResourceId"), ""),
      length: L(i(a, "length")),
      sourceDate: g(i(a, "sourceDate"), ""),
      collectedAt: g(i(a, "collectedAt"), ""),
      status: g(i(a, "status"), "—"),
      deliveries: B(i(a, "deliveries")).map((l) => {
        const s = P(l);
        return {
          id: g(i(s, "id"), "—"),
          collectionFileId: g(i(s, "collectionFileId"), ""),
          targetId: g(i(s, "targetId"), "—"),
          targetKind: g(i(s, "targetKind"), "record"),
          status: g(i(s, "status"), "—"),
          attempts: L(i(s, "attempts")),
          records: L(i(s, "records")),
          error: i(s, "error") == null ? null : g(i(s, "error")),
          accepted: L(i(s, "accepted")),
          rejected: L(i(s, "rejected")),
          warnings: L(i(s, "warnings")),
          diagnosis: ot(i(s, "diagnosis"))
        };
      })
    };
  });
}
function dr(e) {
  const t = X(e), r = P(t), n = B(i(r, "runs")).map((l) => {
    const s = P(l);
    return {
      id: g(i(s, "id"), "—"),
      workflowId: g(i(s, "workflowId", "workflow_id"), "—"),
      status: g(i(s, "status"), "—"),
      startedAt: g(i(s, "startedAt", "started_at"), ""),
      finishedAt: i(s, "finishedAt", "finished_at") == null ? null : g(i(s, "finishedAt", "finished_at")),
      error: i(s, "error") == null ? null : g(i(s, "error"))
    };
  }), a = B(i(r, "deliveries")).map((l) => {
    const s = P(l);
    return {
      id: g(i(s, "id"), "—"),
      collectionFileId: g(i(s, "collectionFileId", "collection_file_id"), ""),
      targetId: g(i(s, "targetId", "target_id"), "—"),
      targetKind: g(i(s, "targetKind", "target_kind"), "record"),
      status: g(i(s, "status"), "—"),
      attempts: L(i(s, "attempts")),
      records: L(i(s, "records")),
      error: i(s, "error") == null ? null : g(i(s, "error")),
      diagnosis: ot(i(s, "diagnosis"))
    };
  });
  return { runs: n, deliveries: a };
}
function ur(e) {
  if (e == null) return null;
  const t = X(e), r = P(t);
  if (Object.keys(r).length === 0) return null;
  const n = B(i(r, "nodes")).slice(0, 64).map((a) => {
    const l = P(a);
    return {
      nodeId: g(i(l, "nodeId", "node_id"), "—"),
      status: g(i(l, "status"), "—"),
      records: L(i(l, "records")),
      files: L(i(l, "files")),
      error: i(l, "error") == null ? null : g(i(l, "error"), void 0),
      samples: B(i(l, "samples")).slice(0, 3).map((s) => g(s))
    };
  });
  return {
    status: g(i(r, "status"), "—"),
    error: i(r, "error") == null ? null : g(i(r, "error"), void 0),
    executed: B(i(r, "executed")).map((a) => g(a)),
    instanceId: i(r, "instanceId") == null ? null : g(i(r, "instanceId"), void 0),
    definitionId: i(r, "definitionId") == null ? null : g(i(r, "definitionId"), void 0),
    nodes: n
  };
}
const pr = [
  { value: "manual", label: "手动运行" },
  { value: "schedule", label: "定时运行" },
  { value: "event", label: "事件触发" }
], fr = [
  { label: "每天 02:00", cron: "0 2 * * *" },
  { label: "每小时", cron: "0 * * * *" },
  { label: "每 5 分钟", cron: "*/5 * * * *" },
  { label: "工作日 08:30", cron: "30 8 * * 1-5" }
];
function gr({
  open: e,
  onClose: t,
  trigger: r,
  resourceIds: n,
  onSave: a,
  ctx: l,
  workflowId: s
}) {
  const [c, d] = I(r), [f, y] = I([]), [x, k] = I(null);
  G(() => {
    d(r);
  }, [r]), G(() => {
    e && l.hubQuery("industrial/trigger-status").then((p) => y(ir(p))).catch((p) => k(p instanceof Error ? p.message : String(p)));
  }, [l, e]);
  const v = f.find((p) => p.workflowId === s);
  return /* @__PURE__ */ o(
    me,
    {
      open: e,
      title: "触发器配置",
      okText: "保存触发器",
      cancelText: "取消",
      onCancel: t,
      onOk: () => {
        var p, C;
        if (c.kind === "schedule" && !((p = c.cron) != null && p.trim())) {
          k("定时运行需要填写 cron 表达式");
          return;
        }
        if (c.kind === "event" && !((C = c.eventName) != null && C.trim())) {
          k("事件触发需要填写事件名称");
          return;
        }
        k(null), a(c), t();
      },
      width: 720,
      children: /* @__PURE__ */ w(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ w(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(N.Text, { children: "触发方式" }),
          /* @__PURE__ */ o(_, { wrap: !0, children: pr.map((p) => /* @__PURE__ */ o(
            $,
            {
              type: c.kind === p.value ? "primary" : "default",
              onClick: () => d({ ...c, kind: p.value }),
              children: p.label
            },
            p.value
          )) })
        ] }),
        /* @__PURE__ */ o(_, { wrap: !0, children: /* @__PURE__ */ o(
          ie,
          {
            "aria-label": "运行资源",
            allowClear: !0,
            placeholder: "运行资源",
            value: yr(c),
            options: n.map((p) => ({ value: p, label: p })),
            onChange: (p) => d({
              ...c,
              config: { ...c.config, resourceId: p ?? "" }
            })
          }
        ) }),
        /* @__PURE__ */ w(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(N.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ w(_, { wrap: !0, children: [
            /* @__PURE__ */ o(N.Text, { children: "常用" }),
            fr.map((p) => /* @__PURE__ */ o(
              $,
              {
                size: "small",
                onClick: () => d({ ...c, cron: p.cron }),
                children: p.label
              },
              p.cron
            ))
          ] }),
          /* @__PURE__ */ o(
            M,
            {
              "aria-label": "Cron 表达式",
              placeholder: "分 时 日 月 周，例如 0 2 * * *",
              value: c.cron ?? "",
              onChange: (p) => d({ ...c, cron: p.target.value })
            }
          ),
          /* @__PURE__ */ o(
            M,
            {
              "aria-label": "调度时区",
              placeholder: "时区（默认 UTC，例如 Asia/Shanghai）",
              value: mr(c),
              onChange: (p) => d({
                ...c,
                config: { ...c.config, timezone: p.target.value }
              })
            }
          ),
          /* @__PURE__ */ o(
            M,
            {
              "aria-label": "事件名称",
              placeholder: "事件名称",
              value: c.eventName ?? "",
              onChange: (p) => d({ ...c, eventName: p.target.value })
            }
          ),
          /* @__PURE__ */ w(N.Text, { type: "secondary", children: [
            "下次运行：",
            (v == null ? void 0 : v.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        v && /* @__PURE__ */ o(
          ne,
          {
            rowKey: "workflowId",
            dataSource: [v],
            pagination: !1,
            columns: [
              { title: "时区", dataIndex: "timezone", render: (p) => p ?? "UTC" },
              { title: "状态", dataIndex: "lastStatus", render: (p) => p ?? "未运行" },
              { title: "上次运行", dataIndex: "lastStartedAt", render: (p) => p ?? "—" },
              { title: "错误", dataIndex: "lastError", render: (p) => p ?? "—" }
            ]
          }
        ),
        x && /* @__PURE__ */ o(oe, { type: "error", showIcon: !0, message: x })
      ] })
    }
  );
}
function yr(e) {
  const t = e.config.resourceId;
  return typeof t == "string" && t.trim() ? t : void 0;
}
function mr(e) {
  const t = e.config.timezone;
  return typeof t == "string" && t.trim() ? t : "UTC";
}
function hr({
  open: e,
  onClose: t,
  ctx: r,
  workflowId: n,
  resourceId: a,
  lookbackDays: l
}) {
  const [s, c] = I(null), [d, f] = I(null), [y, x] = I(!1), k = O(async () => {
    x(!0), f(null);
    try {
      await r.hubCommand("industrial/preview-backfill", {
        workflowId: n,
        resourceId: a,
        lookbackDays: l
      }), c(lr(await r.hubQuery("industrial/backfill-preview")));
    } catch (v) {
      f(v instanceof Error ? v.message : String(v));
    } finally {
      x(!1);
    }
  }, [r, l, a, n]);
  return /* @__PURE__ */ o(
    me,
    {
      open: e,
      title: "补采预览",
      onCancel: t,
      width: 820,
      footer: [
        /* @__PURE__ */ o($, { loading: y, onClick: () => void k(), children: "重新扫描" }, "refresh"),
        /* @__PURE__ */ o($, { type: "primary", onClick: t, children: "关闭" }, "close")
      ],
      children: /* @__PURE__ */ w(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ w(_, { children: [
          /* @__PURE__ */ w($, { type: "primary", loading: y, onClick: () => void k(), children: [
            "扫描最近 ",
            l,
            " 天"
          ] }),
          /* @__PURE__ */ w(N.Text, { type: "secondary", children: [
            "资源：",
            a || "未选择"
          ] })
        ] }),
        d && /* @__PURE__ */ o(oe, { type: "error", showIcon: !0, message: d }),
        s && /* @__PURE__ */ w(be, { children: [
          /* @__PURE__ */ w(N.Text, { children: [
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
          /* @__PURE__ */ o(
            ne,
            {
              rowKey: "path",
              dataSource: s.files,
              pagination: !1,
              size: "small",
              columns: [
                { title: "文件", dataIndex: "path" },
                { title: "源日期", dataIndex: "sourceDate" },
                { title: "账本", dataIndex: "inLedger", render: (v) => v ? "已登记" : "新文件" },
                { title: "当前目标", dataIndex: "expectedTargets" },
                { title: "成功目标", dataIndex: "succeededTargets" },
                { title: "待补目标", dataIndex: "missingTargets", render: (v) => v || "—" },
                { title: "未完成目标", dataIndex: "incompleteTargets" },
                {
                  title: "预计动作",
                  render: (v, m) => m.wouldSkip ? "跳过" : m.duplicateRisk ? "仅补缺失目标" : "采集"
                }
              ]
            }
          )
        ] })
      ] })
    }
  );
}
const { Header: wr, Content: vr } = wt;
function br({ ctx: e }) {
  const [t, r] = vt.useMessage(), [n, a] = I([]), [l, s] = I([]), [c, d] = I([]), [f, y] = I([]), [x, k] = I(""), [v, m] = I(1), [p, C] = I(!0), [z, E] = I([]), [T, R] = I(() => Pe()), [q, u] = I(null), [h, S] = I([]), [H, K] = I(!1), [he, _e] = I(!0), [it, de] = I(!1), [ge, te] = I(!1), [Z, lt] = I(null), [Oe, ze] = I(!1), [Ae, je] = I(!1), Le = ae(1), ke = ae(T.trigger);
  G(() => {
    ke.current = T.trigger;
  }, [T.trigger]), G(() => {
    let b = !0;
    return Promise.allSettled([
      e.hubQuery("industrial/nodes"),
      e.hubQuery("industrial/workflows"),
      e.hubQuery("industrial/resources")
    ]).then(([D, U, j]) => {
      var W;
      if (!b) return;
      const F = [];
      if (D.status === "fulfilled" ? a(er(D.value)) : F.push(`节点描述: ${String(D.reason)}`), U.status === "fulfilled") {
        const Q = at(U.value);
        s(Q);
        const J = new URLSearchParams(window.location.search).get("workflowId"), ee = J && Q.some((le) => le.workflowId === J) ? J : (W = Q[0]) == null ? void 0 : W.workflowId;
        ee && qe(ee);
      } else F.push(`工作流: ${String(U.reason)}`);
      if (j.status === "fulfilled") {
        const Q = tr(j.value);
        y(Q);
        const J = Q.map((ee) => ee.id);
        d(J), k((ee) => ee || J[0] || "");
      } else F.push(`资源: ${String(j.reason)}`);
      E(F);
    }).finally(() => {
      b && C(!1);
    }), () => {
      b = !1;
    };
  }, [e]);
  const re = Je(
    () => new Map(n.map((b) => [b.nodeTypeId, b])),
    [n]
  ), Se = T.nodes.find((b) => b.id === q) ?? null, pe = O(async (b, D, U) => {
    K(!0);
    try {
      return await V().api.hubCommand(b, D), t.success(U), !0;
    } catch (j) {
      return t.error(`${b} failed: ${j instanceof Error ? j.message : String(j)}`), !1;
    } finally {
      K(!1);
    }
  }, [t]), qe = O(async (b) => {
    try {
      const D = await e.hubQuery("industrial/workflow-definitions"), j = (X(D) ?? []).find((Q) => {
        const J = Q;
        return String((J == null ? void 0 : J.workflowId) ?? "") === b;
      }), F = j === void 0 ? { ...Pe(b), name: b } : ar(j);
      R(F), m(Number(F.layout.lookbackDays ?? 1) || 1);
      const W = F.nodes.map((Q) => /^n(\d+)$/.exec(Q.id)).filter(Boolean).reduce((Q, J) => Math.max(Q, Number(J[1])), 0);
      Le.current = W + 1, u(null), de(!1), S([]), te(!1);
    } catch (D) {
      t.error(`加载工作流失败: ${D instanceof Error ? D.message : String(D)}`);
    }
  }, [e, t]), we = O(async () => {
    try {
      const b = ur(await e.hubQuery("industrial/execution"));
      if (b === null) return;
      lt(b);
    } catch {
    }
  }, [e]), ct = O(async () => {
    var U;
    const b = new Set(T.resourceReferences ?? []);
    for (const j of T.nodes)
      for (const F of ["resourceId", "targetResourceId"]) {
        const W = (U = j.config) == null ? void 0 : U[F];
        typeof W == "string" && W.trim() && b.add(W.trim());
      }
    const D = {
      ...T,
      trigger: ke.current,
      resourceReferences: [...b],
      layout: { ...T.layout, lookbackDays: v }
    };
    await pe("industrial/save-workflow", D, "已保存") && (R(D), te(!1));
  }, [pe, T, v]), dt = O(async () => {
    if (ge) {
      t.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await pe("industrial/publish-workflow", { workflowId: T.workflowId }, "已发布") && await we();
  }, [pe, T.workflowId, ge, t, we]), ut = O(async () => {
    if (ge) {
      t.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await pe(
      "industrial/run-workflow",
      { workflowId: T.workflowId, resourceId: x },
      "采集已启动"
    ) && await we();
  }, [pe, T.workflowId, ge, t, we, x]), pt = O((b, D, U) => {
    const j = ue(re, b);
    if (!j) return;
    const F = `n${Le.current++}`;
    R((W) => ({
      ...W,
      nodes: [...W.nodes, Dt(j, F, D, U)]
    })), u(F), de(!0), S([]), te(!0);
  }, [re]), ft = O((b, D) => {
    const U = T.nodes.find((ce) => ce.id === b), j = T.nodes.find((ce) => ce.id === D);
    if (!U || !j)
      return t.warning("连线失败：端点不在画布上"), null;
    const F = ue(re, U.type), W = ue(re, j.type);
    if (!F || !W) return null;
    let Q = U, J = j, ee = Ue(F, W);
    if (!ee) {
      const ce = Ue(W, F);
      if (!ce)
        return t.warning(`端口类型不兼容：${F.name} → ${W.name}`), null;
      Q = j, J = U, ee = ce;
    }
    const le = Rt(T, {
      source: Q.id,
      sourcePort: ee.sourcePort,
      target: J.id,
      targetPort: ee.targetPort
    }, re);
    return le.issue ? (t.warning(`${le.issue.code}: ${le.issue.message}`), S((ce) => [...ce, le.issue]), null) : le.definition === T ? (t.info("这条连线已经存在"), null) : (R(le.definition), te(!0), ee);
  }, [re, T, t]), gt = O(() => {
    const b = qt(T, re, new Set(c));
    S(b), b.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : (t.warning(`发现 ${b.length} 个问题，见属性抽屉定位列表`), de(!0));
  }, [T, re, c, t]), yt = O((b, D) => {
    R((U) => ({
      ...U,
      edges: U.edges.map((j, F) => F === b ? D : j)
    })), te(!0);
  }, []);
  return p ? /* @__PURE__ */ o(Ve, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ w("div", { className: "industrial-designer", children: [
    r,
    /* @__PURE__ */ w(wr, { className: "industrial-header", children: [
      /* @__PURE__ */ w("div", { className: "industrial-title", children: [
        /* @__PURE__ */ o(N.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ o(N.Text, { type: "secondary", children: T.name || T.workflowId }),
        ge && /* @__PURE__ */ o(se, { color: "orange", children: "未保存" }),
        Z && /* @__PURE__ */ o(se, { color: Z.error ? "error" : "success", children: Z.status })
      ] }),
      /* @__PURE__ */ w("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ o(
          $,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": he,
            onClick: () => _e((b) => !b),
            children: he ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ o(
          $,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => de((b) => !b),
            children: "属性"
          }
        ),
        /* @__PURE__ */ o($, { size: "small", "data-testid": "open-trigger", onClick: () => ze(!0), children: "触发器" }),
        /* @__PURE__ */ o(
          $,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => je(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ o("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ w(_, { size: 4, children: [
          /* @__PURE__ */ o($, { size: "small", loading: H, onClick: () => void ct(), children: "保存" }),
          /* @__PURE__ */ o($, { size: "small", onClick: gt, children: "校验" }),
          /* @__PURE__ */ o($, { size: "small", loading: H, onClick: () => void dt(), children: "发布" }),
          /* @__PURE__ */ o($, { size: "small", type: "primary", loading: H, onClick: () => void ut(), children: "运行" })
        ] }),
        /* @__PURE__ */ o(
          ie,
          {
            className: "industrial-resource-select",
            size: "small",
            value: x,
            onChange: k,
            placeholder: "运行资源",
            options: c.map((b) => ({ value: b, label: b }))
          }
        ),
        /* @__PURE__ */ o(
          He,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: v,
            onChange: (b) => {
              m(Number(b) || 1), te(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ w(vr, { className: `industrial-body${he ? "" : " industrial-body--collapsed"}`, children: [
      he ? /* @__PURE__ */ w("aside", { className: "industrial-palette", children: [
        l.length > 0 && /* @__PURE__ */ o(
          ie,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: T.workflowId,
            placeholder: "选择工作流",
            onChange: (b) => void qe(b),
            options: l.map((b) => ({ value: b.workflowId, label: b.name }))
          }
        ),
        /* @__PURE__ */ o(Et, { descriptors: n, loading: !1 })
      ] }) : /* @__PURE__ */ o("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ o($, { type: "text", size: "small", onClick: () => _e(!0), children: "节点库" }) }),
      /* @__PURE__ */ w("section", { className: "industrial-center", children: [
        z.length > 0 && /* @__PURE__ */ o(
          oe,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: z.join("；")
          }
        ),
        /* @__PURE__ */ o(
          Nt,
          {
            definition: T,
            catalog: re,
            selectedId: q,
            onSelect: (b) => {
              u(b), b && de(!0);
            },
            onDropNode: pt,
            onCreateEdge: ft,
            onPositions: (b) => {
              R((D) => jt(D, b)), te(!0);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ w(
      ht,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: it,
        mask: !1,
        getContainer: !1,
        onClose: () => de(!1),
        children: [
          Z && /* @__PURE__ */ o(
            oe,
            {
              style: { marginBottom: 12 },
              type: Z.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${Z.status}`,
              description: /* @__PURE__ */ w(_, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                Z.error && /* @__PURE__ */ o("span", { children: Z.error }),
                Z.executed.length > 0 && /* @__PURE__ */ w("span", { children: [
                  "已执行节点: ",
                  Z.executed.join(", ")
                ] }),
                Z.nodes.length > 0 && /* @__PURE__ */ o(
                  ne,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: Z.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (b, D) => D.error ?? (D.samples.length > 0 ? D.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ o(
            Qt,
            {
              node: Se,
              descriptor: Se ? ue(re, Se.type) ?? null : null,
              edges: T.edges,
              catalog: re,
              resources: f,
              issues: h,
              onApplyConfig: (b) => {
                q && (R((D) => At(D, q, b)), te(!0));
              },
              onDeleteNode: () => {
                R((b) => _t(b, q ?? "")), u(null), te(!0);
              },
              onDeleteEdge: (b) => {
                R((D) => Ot(D, b)), te(!0);
              },
              onUpdateEdge: yt,
              onSelectNode: (b) => {
                u(b), de(!0);
              }
            }
          )
        ]
      }
    ),
    Oe && /* @__PURE__ */ o(
      gr,
      {
        open: Oe,
        onClose: () => ze(!1),
        trigger: T.trigger,
        resourceIds: c,
        workflowId: T.workflowId,
        ctx: e,
        onSave: (b) => {
          ke.current = b, R((D) => ({ ...D, trigger: b })), te(!0);
        }
      }
    ),
    Ae && /* @__PURE__ */ o(
      hr,
      {
        open: Ae,
        onClose: () => je(!1),
        ctx: e,
        workflowId: T.workflowId,
        resourceId: x,
        lookbackDays: v
      }
    )
  ] });
}
function Ir({ block: e, ctx: t }) {
  const [r, n] = I([]), [a, l] = I(!1), [s, c] = I(""), [d, f] = I(null), [y, x] = I(!1), k = O(async () => {
    try {
      n(at(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
    } catch (p) {
      f(p instanceof Error ? p.message : String(p));
    }
  }, [e == null ? void 0 : e.query, t]);
  G(() => {
    k();
  }, [k]);
  const v = O(async () => {
    var C;
    const p = s.trim();
    if (!p) {
      f("请输入工作流名称");
      return;
    }
    x(!0), f(null);
    try {
      const E = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await t.hubCommand("industrial/save-workflow", {
        ...Pe(E),
        name: p,
        layout: { lookbackDays: 1 }
      }), l(!1), c(""), await k(), (C = t.navigate) == null || C.call(t, `/industrial/designer?workflowId=${encodeURIComponent(E)}`);
    } catch (z) {
      f(z instanceof Error ? z.message : String(z));
    } finally {
      x(!1);
    }
  }, [t, k, s]), m = O((p) => {
    var C;
    (C = t.navigate) == null || C.call(t, `/industrial/designer?workflowId=${encodeURIComponent(p)}`);
  }, [t]);
  return /* @__PURE__ */ w(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ w(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ o(N.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ o($, { type: "primary", onClick: () => l(!0), children: "新建工作流" })
    ] }),
    d && /* @__PURE__ */ o(N.Text, { type: "danger", children: d }),
    /* @__PURE__ */ o(
      ne,
      {
        rowKey: "workflowId",
        dataSource: r,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (p) => /* @__PURE__ */ o($, { type: "link", style: { padding: 0 }, onClick: () => m(p), children: p })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ o(
      me,
      {
        open: a,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: y,
        onCancel: () => l(!1),
        onOk: () => void v(),
        children: /* @__PURE__ */ w(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(N.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ o(
            M,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: s,
              onChange: (p) => c(p.target.value ?? "")
            }
          ),
          d && /* @__PURE__ */ o(N.Text, { type: "danger", children: d })
        ] })
      }
    )
  ] });
}
const Ce = {
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
}, xr = [
  { value: "filesystem.local", label: "本地目录" },
  { value: "filesystem.unc", label: "Windows UNC 共享" },
  { value: "sqlserver", label: "SQL Server" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mysql", label: "MySQL" },
  { value: "sqlite", label: "SQLite" }
];
function kr(e) {
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
function Sr({ block: e, ctx: t }) {
  const [r, n] = I([]), [a, l] = I([]), [s, c] = I([]), [d, f] = I(Ce), [y, x] = I(!1), [k, v] = I(null), [m, p] = I(!1), C = O(async () => {
    try {
      const [u, h, S] = await Promise.all([
        t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        t.hubQuery("industrial/credentials"),
        t.hubQuery("industrial/resource-tests")
      ]);
      n(sr(u)), l(st(h)), c(or(S));
    } catch (u) {
      v(u instanceof Error ? u.message : String(u));
    }
  }, [e == null ? void 0 : e.query, t]);
  G(() => {
    C();
  }, [C]);
  const z = O((u) => {
    const h = (...S) => {
      for (const H of S) {
        const K = u.config[H];
        if (K != null && String(K).trim()) return String(K);
      }
      return "";
    };
    f({
      id: u.id,
      name: u.name,
      type: u.type,
      provider: u.provider,
      root: h("root", "path"),
      host: h("server", "host", "dataSource", "data_source"),
      database: h("database", "initialCatalog", "initial_catalog", "path"),
      port: h("port"),
      table: h("table"),
      credentialId: u.credentialId ?? "",
      enabled: u.enabled,
      config: u.config
    }), x(!0);
  }, []), E = O(async () => {
    const u = d.id.trim();
    if (!u) {
      v("请输入资源 ID");
      return;
    }
    p(!0), v(null);
    try {
      await t.hubCommand("industrial/save-resource", {
        id: u,
        name: d.name || u,
        type: d.type,
        provider: d.provider || "manual",
        config: kr(d),
        credentialId: d.credentialId || null,
        enabled: d.enabled
      }), x(!1), f(Ce), await C();
    } catch (h) {
      v(h instanceof Error ? h.message : String(h));
    } finally {
      p(!1);
    }
  }, [t, d, C]), T = O(async (u) => {
    v(null);
    try {
      await t.hubCommand("industrial/test-resource", { id: u });
    } catch (h) {
      v(h instanceof Error ? h.message : String(h));
    }
    await C();
  }, [t, C]), R = O(async (u) => {
    v(null);
    try {
      await t.hubCommand("industrial/delete-resource", { id: u }), await C();
    } catch (h) {
      v(h instanceof Error ? h.message : String(h));
    }
  }, [t, C]), q = new Map(s.map((u) => [u.resourceId, u]));
  return /* @__PURE__ */ w(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ w(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ o(N.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ o($, { type: "primary", onClick: () => {
        f(Ce), x(!0);
      }, children: "新建资源" })
    ] }),
    k && /* @__PURE__ */ o(oe, { type: "error", showIcon: !0, message: k }),
    /* @__PURE__ */ o(
      ne,
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
            render: (u, h) => {
              const S = q.get(h.id);
              return S ? /* @__PURE__ */ o(N.Text, { type: S.ok ? "success" : "danger", children: S.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (u, h) => /* @__PURE__ */ w(_, { size: 4, children: [
              /* @__PURE__ */ o($, { size: "small", onClick: () => void T(h.id), children: "测试连接" }),
              /* @__PURE__ */ o($, { size: "small", onClick: () => z(h), children: "编辑" }),
              /* @__PURE__ */ o($, { size: "small", danger: !0, onClick: () => void R(h.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ o(
      me,
      {
        open: y,
        title: d.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: m,
        onCancel: () => x(!1),
        onOk: () => void E(),
        children: /* @__PURE__ */ w(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(
            M,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: d.id,
              onChange: (u) => f({ ...d, id: u.target.value })
            }
          ),
          /* @__PURE__ */ o(
            M,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: d.name,
              onChange: (u) => f({ ...d, name: u.target.value })
            }
          ),
          /* @__PURE__ */ o(N.Text, { children: "资源类型" }),
          /* @__PURE__ */ o(_, { wrap: !0, children: xr.map((u) => /* @__PURE__ */ o(
            $,
            {
              size: "small",
              type: d.type === u.value ? "primary" : "default",
              onClick: () => f({ ...d, type: u.value }),
              children: u.label
            },
            u.value
          )) }),
          (d.type === "filesystem.local" || d.type === "filesystem.unc") && /* @__PURE__ */ o(
            M,
            {
              "aria-label": "目录路径",
              placeholder: d.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: d.root,
              onChange: (u) => f({ ...d, root: u.target.value })
            }
          ),
          (d.type === "sqlserver" || d.type === "postgresql" || d.type === "mysql") && /* @__PURE__ */ w(_, { children: [
            /* @__PURE__ */ o(
              M,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: d.host,
                onChange: (u) => f({ ...d, host: u.target.value })
              }
            ),
            /* @__PURE__ */ o(
              M,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: d.database,
                onChange: (u) => f({ ...d, database: u.target.value })
              }
            ),
            /* @__PURE__ */ o(
              M,
              {
                "aria-label": "端口",
                placeholder: "端口",
                value: d.port,
                onChange: (u) => f({ ...d, port: u.target.value })
              }
            )
          ] }),
          d.type === "sqlite" && /* @__PURE__ */ o(
            M,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: d.database,
              onChange: (u) => f({ ...d, database: u.target.value })
            }
          ),
          /* @__PURE__ */ o(
            ie,
            {
              "aria-label": "凭证",
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: d.credentialId || void 0,
              options: a.map((u) => ({ value: u.id, label: u.name })),
              onChange: (u) => f({ ...d, credentialId: u ?? "" })
            }
          ),
          /* @__PURE__ */ w(_, { children: [
            /* @__PURE__ */ o(
              Xe,
              {
                "aria-label": "启用资源",
                checked: d.enabled,
                onChange: (u) => f({ ...d, enabled: u })
              }
            ),
            /* @__PURE__ */ o(N.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
function Tr({ block: e, ctx: t }) {
  const [r, n] = I([]), [a, l] = I(!1), [s, c] = I(""), [d, f] = I(""), [y, x] = I("username-password"), [k, v] = I(""), [m, p] = I(!1), [C, z] = I(null), [E, T] = I(!1), R = O(async () => {
    try {
      n(st(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
    } catch (S) {
      z(S instanceof Error ? S.message : String(S));
    }
  }, [e == null ? void 0 : e.query, t]);
  G(() => {
    R();
  }, [R]);
  const q = O(() => {
    c(""), f(""), x("username-password"), v(""), p(!1), l(!0);
  }, []), u = O((S) => {
    c(S.id), f(S.name), x(S.kind), v(""), p(!0), l(!0);
  }, []), h = O(async () => {
    if (!s.trim()) {
      z("请输入凭证 ID");
      return;
    }
    if (!k.trim()) {
      z("请输入凭证内容");
      return;
    }
    T(!0), z(null);
    try {
      m ? await t.hubCommand("industrial/rotate-credential", { id: s, secret: k }) : await t.hubCommand("industrial/save-credential", { id: s, name: d || s, kind: y, secret: k }), l(!1), await R();
    } catch (S) {
      z(S instanceof Error ? S.message : String(S));
    } finally {
      T(!1);
    }
  }, [t, s, y, R, d, m, k]);
  return /* @__PURE__ */ w(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ w(_, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ o(N.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ o($, { type: "primary", onClick: q, children: "新建凭证" })
    ] }),
    C && /* @__PURE__ */ o(oe, { type: "error", showIcon: !0, message: C }),
    /* @__PURE__ */ o(
      ne,
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
            render: (S, H) => /* @__PURE__ */ o($, { size: "small", onClick: () => u(H), children: "轮换" })
          }
        ]
      }
    ),
    /* @__PURE__ */ o(
      me,
      {
        open: a,
        title: m ? "轮换凭证" : "新建凭证",
        okText: m ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: E,
        onCancel: () => l(!1),
        onOk: () => void h(),
        children: /* @__PURE__ */ w(_, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ o(
            M,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: s,
              disabled: m,
              onChange: (S) => c(S.target.value)
            }
          ),
          !m && /* @__PURE__ */ o(
            M,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: d,
              onChange: (S) => f(S.target.value)
            }
          ),
          /* @__PURE__ */ o(
            M,
            {
              "aria-label": "凭证内容",
              placeholder: m ? "新凭证内容" : "凭证内容",
              value: k,
              onChange: (S) => v(S.target.value)
            }
          ),
          C && /* @__PURE__ */ o(N.Text, { type: "danger", children: C })
        ] })
      }
    )
  ] });
}
const Qe = (e) => e === "Succeeded" || e === "Completed" ? "success" : e === "Failed" || e === "PartialFailure" ? "error" : e === "Running" || e === "Interrupted" ? "processing" : "default";
function Cr({ block: e, ctx: t }) {
  const [r, n] = I([]), [a, l] = I([]), [s, c] = I(null), [d, f] = I(null), [y, x] = I(!1), k = O(async () => {
    try {
      const m = dr(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/execution-center"));
      n(m.runs), l(m.deliveries);
    } catch (m) {
      c(m instanceof Error ? m.message : String(m));
    }
  }, [e == null ? void 0 : e.query, t]);
  G(() => {
    k();
  }, [k]);
  const v = O(async (m) => {
    x(!0), c(null), f(null);
    try {
      await t.hubCommand("industrial/retry-failed", {
        deliveryId: m.id,
        targetId: m.targetId
      }), f(`已提交目标 ${m.targetId} 的重试`), await k();
    } catch (p) {
      c(p instanceof Error ? p.message : String(p));
    } finally {
      x(!1);
    }
  }, [t, k]);
  return /* @__PURE__ */ w(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ o(N.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    s && /* @__PURE__ */ o(oe, { type: "error", showIcon: !0, message: s }),
    d && /* @__PURE__ */ o(oe, { type: "success", showIcon: !0, message: d }),
    /* @__PURE__ */ o(
      ne,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (m) => /* @__PURE__ */ o(se, { color: Qe(m), children: m }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (m) => m ?? "—" },
          { title: "错误", dataIndex: "error", render: (m) => m ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ o(
      ne,
      {
        rowKey: "id",
        dataSource: a,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (m) => /* @__PURE__ */ o(se, { color: Qe(m), children: m }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (m, p) => p.diagnosis ? /* @__PURE__ */ w(_, { direction: "vertical", size: 0, children: [
              /* @__PURE__ */ o(N.Text, { children: p.diagnosis.message }),
              /* @__PURE__ */ o(N.Text, { type: "secondary", children: p.diagnosis.action })
            ] }) : p.error ?? "—"
          },
          {
            title: "操作",
            key: "actions",
            render: (m, p) => /* @__PURE__ */ o(
              $,
              {
                size: "small",
                disabled: y || p.status !== "Failed" && p.status !== "Interrupted",
                onClick: () => void v(p),
                children: "重试此目标"
              }
            )
          }
        ]
      }
    )
  ] });
}
function Nr({ block: e, ctx: t }) {
  const [r, n] = I([]), [a, l] = I(null), s = O(async () => {
    try {
      n(cr(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (d) {
      l(d instanceof Error ? d.message : String(d));
    }
  }, [e == null ? void 0 : e.query, t]);
  G(() => {
    s();
  }, [s]);
  const c = (d) => /* @__PURE__ */ o(
    ne,
    {
      rowKey: "id",
      dataSource: d.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (f) => /* @__PURE__ */ o(se, { children: f }) },
        { title: "记录", dataIndex: "records" },
        {
          title: "接受/拒绝/警告",
          key: "counts",
          render: (f, y) => `${y.accepted} / ${y.rejected} / ${y.warnings}`
        },
        {
          title: "原因与下一步",
          key: "diagnosis",
          render: (f, y) => y.diagnosis ? /* @__PURE__ */ w(_, { direction: "vertical", size: 0, children: [
            /* @__PURE__ */ o(N.Text, { children: y.diagnosis.message }),
            /* @__PURE__ */ o(N.Text, { type: "secondary", children: y.diagnosis.action }),
            y.error && /* @__PURE__ */ o(N.Text, { type: "secondary", children: y.error })
          ] }) : y.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ w(_, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ o(N.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    a && /* @__PURE__ */ o(oe, { type: "error", showIcon: !0, message: a }),
    /* @__PURE__ */ o(
      ne,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        expandable: { expandedRowRender: c },
        columns: [
          { title: "文件", dataIndex: "path" },
          { title: "源日期", dataIndex: "sourceDate" },
          { title: "状态", dataIndex: "status" },
          { title: "大小", dataIndex: "length", render: (d) => `${d} B` },
          {
            title: "目标数",
            key: "targets",
            render: (d, f) => f.deliveries.length
          }
        ]
      }
    )
  ] });
}
const Er = `
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
function Pr(e) {
  mt(e);
  const t = typeof document > "u" ? null : document.createElement("style");
  t && (t.dataset.industrialDesigner = "true", t.textContent = Er, document.head.appendChild(t));
  const r = ({ ctx: l }) => Ne(br, { ctx: l });
  r.displayName = "IndustrialWorkflowCanvas";
  const n = (l, s) => {
    const c = ({ block: d, ctx: f }) => Ne(s, { block: d, ctx: f });
    return c.displayName = l, e.blocks.register(l, c);
  }, a = [
    e.blocks.register("industrial-workflow-canvas", r),
    n("industrial-workflow-list", Ir),
    n("industrial-resource-console", Sr),
    n("industrial-credential-console", Tr),
    n("industrial-execution-center", Cr),
    n("industrial-file-details", Nr)
  ];
  return () => {
    for (const l of a) l == null || l();
    t == null || t.remove();
  };
}
export {
  Pr as default,
  Pr as register
};
