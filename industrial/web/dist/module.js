let ye = null;
function mt(e) {
  ye = e;
}
function H() {
  if (!ye && typeof globalThis < "u" && (ye = globalThis.__CORDIS_CONSOLE ?? null), !ye)
    throw new Error("[industrial-designer] console facade is not available");
  return ye;
}
const a = (...e) => H().jsxRuntime.jsx(...e), h = (...e) => H().jsxRuntime.jsxs(...e), be = (e) => H().jsxRuntime.jsx(H().jsxRuntime.Fragment, e), Ce = (...e) => H().React.createElement(...e), _ = (...e) => H().React.useCallback(...e), G = (...e) => H().React.useEffect(...e), Je = (...e) => H().React.useMemo(...e), se = (...e) => H().React.useRef(...e), b = (...e) => H().React.useState(...e), z = (e) => {
  const t = (r) => {
    const n = e.split(".").reduce((s, l) => s == null ? void 0 : s[l], H().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return Ce(n, r ?? {});
  };
  return t.displayName = `facade.${e}`, t;
}, ie = z("Alert"), $ = z("Button"), Fe = z("Divider"), ht = z("Drawer"), Ye = z("Empty"), Ne = Object.assign(z("Form"), {
  Item: z("Form.Item"),
  useForm: (...e) => H().antd.Form.useForm(...e)
}), K = Object.assign(z("Input"), {
  Number: z("InputNumber"),
  TextArea: z("Input.TextArea")
}), He = z("InputNumber"), wt = Object.assign(z("Layout"), {
  Header: z("Layout.Header"),
  Content: z("Layout.Content")
}), me = z("Modal"), ae = z("Select"), O = z("Space"), Ve = z("Spin"), Xe = z("Switch"), ne = z("Table"), oe = z("Tag"), N = Object.assign(z("Typography"), {
  Title: z("Typography.Title"),
  Text: z("Typography.Text")
}), vt = new Proxy({}, {
  get: (e, t) => (...r) => {
    var s;
    const n = (s = H().antd.message) == null ? void 0 : s[t];
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
      const s = t.get(n.type), l = (s == null ? void 0 : s.category) ?? "General", o = r === n.id;
      return {
        id: n.id,
        data: {
          label: (s == null ? void 0 : s.name) ?? n.type,
          nodeTypeId: n.type,
          category: l,
          icon: (s == null ? void 0 : s.icon) ?? "AppstoreOutlined",
          config: { ...n.config }
        },
        style: {
          x: n.x,
          y: n.y,
          fill: Be(l),
          stroke: o ? "#111111" : Be(l),
          lineWidth: o ? 3 : 1.5,
          radius: 10,
          port: !0,
          ports: [
            ...((s == null ? void 0 : s.inputs) ?? []).map((c, d, f) => ({
              key: `in:${c.name}`,
              placement: [0, (d + 1) / (f.length + 1)],
              r: 5,
              stroke: c.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((s == null ? void 0 : s.outputs) ?? []).map((c, d, f) => ({
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
  const n = r && r.length > 0 ? new Set(r) : null, s = {};
  for (const l of e.nodes) {
    if (!t.has(l.id) || n && !n.has(l.id)) continue;
    const o = l.style.x, c = l.style.y;
    typeof o == "number" && typeof c == "number" && (s[l.id] = { x: o, y: c });
  }
  return s;
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
const St = "__industrial_drag_target__", Tt = "__industrial_connect_source__", Se = (e, t) => {
  var r;
  return (((r = e.isPanModifier) == null ? void 0 : r.call(e)) ?? !1) || t.button === 1 || t.buttons === 4;
}, Ke = (e, t) => {
  var y;
  if (t.targetType !== "node" || !((y = t.target) != null && y.id)) return !1;
  const r = t.client ?? t.canvas;
  if (!r || !e.graph) return !1;
  const n = String(t.target.id), s = e.graph.getNodeData().find((x) => String(x.id) === n), l = s == null ? void 0 : s.style;
  if (typeof (l == null ? void 0 : l.x) != "number" || typeof l.y != "number") return !1;
  const [o, c] = e.graph.getCanvasByClient([r.x, r.y]), d = Math.abs(o - l.x), f = Math.abs(c - l.y);
  return Math.abs(d - Ze / 2) <= 14 && f <= et / 2 + 10;
};
function tt(e = {}) {
  const t = e.connectionState ?? { armed: !1 };
  return [
    "zoom-canvas",
    {
      type: "drag-canvas",
      enable: (r) => r.targetType === "canvas" || Se(e, r),
      range: 0.9
    },
    {
      type: "drag-element",
      state: St,
      enable: (r) => (r.targetType === "node" || r.targetType === "combo") && !Ke(e, r) && !Se(e, r),
      onFinish: (r) => {
        var o, c;
        const n = e.graph;
        if (!n) return;
        const s = Array.isArray(r) ? r : r ? [String(r)] : void 0, l = n.getNodeData().map((d) => ({
          id: String(d.id),
          data: d.data ?? {},
          style: d.style ?? {}
        }));
        (c = e.onPositions) == null || c.call(e, xt(
          { nodes: l },
          ((o = e.allowedIds) == null ? void 0 : o.call(e)) ?? /* @__PURE__ */ new Set(),
          s
        ));
      }
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: Tt,
      enable: (r) => Se(e, r) ? (t.armed = !1, !1) : t.armed ? r.targetType === "node" || r.targetType === "combo" : Ke(e, r) ? (t.armed = !0, !0) : !1,
      onCreate: (r) => {
        var o;
        t.armed = !1;
        const n = String(r.source ?? ""), s = String(r.target ?? "");
        if (!n || !s || n === s) return;
        const l = (o = e.onCreateEdge) == null ? void 0 : o.call(e, n, s);
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
    onSelect: s,
    onDropNode: l,
    onCreateEdge: o,
    onPositions: c
  } = e, d = se(null), f = se(null), y = se(!1), x = se(!1), k = se({ armed: !1 }), v = se({ onSelect: s, onDropNode: l, onCreateEdge: o, onPositions: c });
  v.current = { onSelect: s, onDropNode: l, onCreateEdge: o, onPositions: c };
  const m = Je(
    () => It(t, r, n),
    [t, r, n]
  ), p = se(m);
  p.current = m;
  const C = se(/* @__PURE__ */ new Set());
  return C.current = new Set(t.nodes.map((E) => E.id)), G(() => {
    const E = (L) => {
      L.code === "Space" && !L.repeat && (x.current = !0);
    }, S = (L) => {
      L.code === "Space" && (x.current = !1);
    }, R = () => {
      x.current = !1;
    };
    return window.addEventListener("keydown", E), window.addEventListener("keyup", S), window.addEventListener("blur", R), () => {
      window.removeEventListener("keydown", E), window.removeEventListener("keyup", S), window.removeEventListener("blur", R);
    };
  }, []), G(() => {
    const E = d.current;
    if (!E || y.current) return;
    let S = !1;
    return import("./esm-iA-ueG44.js").then(({ Graph: R }) => {
      if (S || !d.current || y.current) return;
      const L = kt(p.current, E), u = new R({
        ...L,
        behaviors: tt({
          graph: {
            getNodeData: () => {
              var I;
              return ((I = f.current) == null ? void 0 : I.getNodeData()) ?? [];
            },
            getCanvasByClient: (I) => {
              var X;
              const T = ((X = f.current) == null ? void 0 : X.getCanvasByClient(I)) ?? I;
              return [T[0], T[1]];
            }
          },
          isPanModifier: () => x.current,
          connectionState: k.current,
          allowedIds: () => C.current,
          onPositions: (I) => v.current.onPositions(I),
          onCreateEdge: (I, T) => v.current.onCreateEdge(I, T)
        })
      });
      u.on("node:click", (I) => {
        const T = Ct(I);
        T && v.current.onSelect(T);
      }), u.on("edge:click", () => v.current.onSelect(null)), u.on("canvas:click", () => v.current.onSelect(null)), u.on("pointerup", () => {
        k.current.armed = !1;
      }), f.current = u, E.__industrialGraph = u, y.current = !0, u.render();
    }), () => {
      S = !0;
    };
  }, []), G(() => {
    const E = f.current;
    !E || !y.current || (E.setData(m), E.draw());
  }, [m]), G(() => () => {
    var E;
    (E = f.current) == null || E.destroy(), f.current = null, y.current = !1;
  }, []), /* @__PURE__ */ a("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ h("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ a(
      "div",
      {
        ref: d,
        className: "industrial-graph-host",
        onDragOver: (E) => E.preventDefault(),
        onDrop: (E) => {
          E.preventDefault();
          const S = E.dataTransfer.getData("application/x-industrial-node") || E.dataTransfer.getData("text/plain");
          if (!S) return;
          const R = f.current, L = d.current;
          if (!R || !L) return;
          const u = L.getBoundingClientRect(), I = R.getCanvasByViewport([
            E.clientX - u.left,
            E.clientY - u.top
          ]);
          v.current.onDropNode(S, I[0], I[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    /* @__PURE__ */ a("div", { className: "industrial-graph-hint", children: "拖动节点移动 · 从端口拖出连线 · 拖动画布平移（空格或中键可在节点上平移）" })
  ] }) });
}
function Et({ descriptors: e, loading: t, error: r }) {
  const n = /* @__PURE__ */ new Map();
  for (const l of e) {
    const o = l.category.split(".")[0] || "General", c = n.get(o) ?? [];
    c.push(l), n.set(o, c);
  }
  const s = (l, o) => {
    l.dataTransfer.setData("application/x-industrial-node", o.nodeTypeId), l.dataTransfer.setData("text/plain", o.nodeTypeId), l.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ h("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ a(N.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ a(N.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    t && /* @__PURE__ */ a(Ve, { size: "small", style: { display: "block", marginTop: 16 } }),
    r && /* @__PURE__ */ a(N.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: r }),
    !t && !r && e.length === 0 && /* @__PURE__ */ a(Ye, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(n.entries()).map(([l, o]) => /* @__PURE__ */ h("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ a(N.Text, { strong: !0, style: { fontSize: 12 }, children: l }),
      o.map((c) => /* @__PURE__ */ h(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          onDragStart: (d) => s(d, c),
          title: c.description || c.nodeTypeId,
          children: [
            /* @__PURE__ */ a("div", { className: "industrial-palette-node-title", children: c.name }),
            /* @__PURE__ */ h("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ a(oe, { bordered: !1, color: "blue", children: c.version }),
              /* @__PURE__ */ a(N.Text, { type: "secondary", style: { fontSize: 11 }, children: c.nodeTypeId })
            ] }),
            /* @__PURE__ */ h("div", { className: "industrial-palette-node-ports", children: [
              c.inputs.map((d) => /* @__PURE__ */ h(oe, { bordered: !1, color: "default", children: [
                "in ",
                d.name,
                ": ",
                d.type
              ] }, `in-${d.name}`)),
              c.outputs.map((d) => /* @__PURE__ */ h(oe, { bordered: !1, color: "green", children: [
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
const Ee = (e = "daily-collection") => ({
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
function Ge(e, t) {
  for (const r of e.outputs)
    for (const n of t.inputs)
      if (Re(r.type, n.type))
        return { sourcePort: r.name, targetPort: n.name };
  return null;
}
const J = (e, t, r) => ({ path: e, code: t, message: r });
function $t(e, t) {
  return e.source === t.source && e.sourcePort === t.sourcePort && e.target === t.target && e.targetPort === t.targetPort;
}
function Rt(e, t, r) {
  const n = `edges.${e.edges.length}`, s = e.nodes.find((y) => y.id === t.source), l = e.nodes.find((y) => y.id === t.target);
  if (!s || !l)
    return { definition: e, issue: J(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((y) => $t(y, t)))
    return { definition: e };
  const o = ue(r, s.type), c = ue(r, l.type);
  if (!o || !c)
    return { definition: e, issue: J(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const d = Ie(o, t.sourcePort, "outputs"), f = Ie(c, t.targetPort, "inputs");
  return !d || !f ? { definition: e, issue: J(n, "PORT_MISSING", "declared port does not exist") } : Re(d.type, f.type) ? { definition: { ...e, edges: [...e.edges, t] } } : {
    definition: e,
    issue: J(
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
function At(e, t) {
  const r = { ...e };
  for (const [n, s] of Object.entries(t))
    s === void 0 ? delete r[n] : r[n] = s;
  return r;
}
function zt(e, t, r) {
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
  for (const o of e.nodes) t.set(o.id, []);
  for (const o of e.edges)
    (l = t.get(o.source)) == null || l.push(o.target);
  const r = /* @__PURE__ */ new Map(), n = [], s = (o) => {
    r.set(o, 1), n.push(o);
    for (const c of t.get(o) ?? []) {
      if (r.get(c) === 1)
        return [...n.slice(n.indexOf(c)), c];
      if (r.get(c) !== 2) {
        const d = s(c);
        if (d) return d;
      }
    }
    return n.pop(), r.set(o, 2), null;
  };
  for (const o of e.nodes)
    if (r.get(o.id) !== 2) {
      const c = s(o.id);
      if (c) return c;
    }
  return null;
}
function qt(e, t, r) {
  const n = [];
  e.workflowId.trim() || n.push(J("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(J("workflow", "INVALID_VERSION", "version must be positive"));
  const s = /* @__PURE__ */ new Map();
  for (const c of e.nodes)
    s.has(c.id) && n.push(J(`nodes.${c.id}`, "DUPLICATE_NODE_ID", `duplicate node '${c.id}'`)), s.set(c.id, c);
  const l = /* @__PURE__ */ new Map();
  for (const c of e.nodes) {
    const d = ue(t, c.type);
    if (!d) {
      n.push(J(
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
      (x == null || x === "") && n.push(J(
        `nodes.${c.id}.config.${y}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${y}' is missing`
      ));
    }
    r && typeof c.config.resourceId == "string" && (r.has(c.config.resourceId) || n.push(J(
      `nodes.${c.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${c.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((c, d) => {
    const f = s.get(c.source), y = s.get(c.target);
    if (!f || !y) {
      n.push(J(`edges.${d}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const x = l.get(c.source), k = l.get(c.target);
    if (!x || !k) return;
    const v = Ie(x, c.sourcePort, "outputs"), m = Ie(k, c.targetPort, "inputs");
    if (!v || !m) {
      n.push(J(`edges.${d}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    Re(v.type, m.type) || n.push(J(
      `edges.${d}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${v.type} → ${m.type} is not a legal connection`
    ));
  });
  for (const c of e.nodes) {
    const d = l.get(c.id);
    if (d)
      for (const f of d.inputs.filter((y) => y.required))
        e.edges.some((y) => y.target === c.id && y.targetPort === f.name && s.has(y.source)) || n.push(J(
          `nodes.${c.id}.inputs.${f.name}`,
          "MISSING_INPUT",
          `required input '${f.name}' is not connected`
        ));
  }
  const o = Lt(e);
  return o && n.push(J(`nodes.${o[0]}`, "GRAPH_CYCLE", `cycle: ${o.join(" → ")}`)), n;
}
const Pe = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function Ft(e) {
  const t = Pe(e.additionalProperties).enum;
  if (!Array.isArray(t)) return null;
  const r = t.filter((n) => typeof n == "string");
  return r.length > 0 ? r : null;
}
function rt(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && Pe(e.items).type === "string" ? "string-list" : e.type === "object" && Pe(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function De(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, t]) => t != null && typeof t != "object").map(([t, r]) => ({ key: t, value: String(r) }));
}
function Ue(e) {
  const t = {}, r = [];
  for (const n of e) {
    const s = n.key.trim();
    s && (Object.prototype.hasOwnProperty.call(t, s) && r.push(s), t[s] = n.value);
  }
  return { record: t, duplicateKeys: r };
}
function ve(e) {
  if (!Array.isArray(e)) return [];
  const t = /* @__PURE__ */ new Set(), r = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const s = n.trim();
    !s || t.has(s) || (t.add(s), r.push(s));
  }
  return r;
}
function Mt(e, t) {
  const r = /* @__PURE__ */ new Set();
  for (const [n, s] of Object.entries(e))
    if (rt(s) === "key-value")
      for (const l of De(t[n]))
        l.key.trim() && r.add(l.key.trim());
  return [...r];
}
function Bt({
  value: e,
  onChange: t,
  valueOptions: r,
  keyTitle: n = "目标字段",
  valueTitle: s = "来源字段",
  testId: l
}) {
  const o = JSON.stringify(e ?? {}), [c, d] = b(() => De(e)), [f, y] = b(o);
  G(() => {
    o !== f && (d(De(e)), y(o));
  }, [o, f, e]);
  const x = (v) => {
    d(v), t == null || t(Ue(v).record);
  }, { duplicateKeys: k } = Ue(c);
  return /* @__PURE__ */ h("div", { "data-testid": l, className: "industrial-kv-editor", children: [
    c.map((v, m) => /* @__PURE__ */ h("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ a(
        K,
        {
          "aria-label": `${n} ${m + 1}`,
          value: v.key,
          placeholder: n,
          onChange: (p) => x(c.map((C, A) => A === m ? { ...C, key: p.target.value } : C))
        }
      ),
      r && r.length > 0 ? /* @__PURE__ */ a(
        ae,
        {
          "aria-label": `${s} ${m + 1}`,
          value: v.value || void 0,
          placeholder: s,
          options: r.map((p) => ({ label: p, value: p })),
          onChange: (p) => x(c.map((C, A) => A === m ? { ...C, value: p ?? "" } : C))
        }
      ) : /* @__PURE__ */ a(
        K,
        {
          "aria-label": `${s} ${m + 1}`,
          value: v.value,
          placeholder: s,
          onChange: (p) => x(c.map((C, A) => A === m ? { ...C, value: p.target.value } : C))
        }
      ),
      /* @__PURE__ */ a(
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
    /* @__PURE__ */ a(
      $,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => x([...c, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    k.length > 0 && /* @__PURE__ */ h(N.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      k.join("、")
    ] })
  ] });
}
function Kt({ value: e, onChange: t, suggestions: r = [], testId: n }) {
  const s = JSON.stringify(ve(e)), [l, o] = b(() => ve(e)), [c, d] = b(s);
  G(() => {
    s !== c && (o(ve(e)), d(s));
  }, [s, c, e]);
  const f = (y) => {
    o(y), t == null || t(ve(y));
  };
  return /* @__PURE__ */ h("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ a("datalist", { id: `${n ?? "list"}-suggestions`, children: r.map((y) => /* @__PURE__ */ a("option", { value: y }, y)) }),
    l.map((y, x) => /* @__PURE__ */ h("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ a(
        K,
        {
          "aria-label": `字段 ${x + 1}`,
          value: y,
          list: `${n ?? "list"}-suggestions`,
          onChange: (k) => f(l.map((v, m) => m === x ? k.target.value : v))
        }
      ),
      /* @__PURE__ */ a(
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
    /* @__PURE__ */ a($, { size: "small", type: "dashed", block: !0, onClick: () => f([...l, ""]), children: "添加字段" }),
    l.length === 0 && /* @__PURE__ */ a(N.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Gt({ value: e, onChange: t }) {
  const r = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, s] = b(r), [l, o] = b(null);
  G(() => {
    s(r), o(null);
  }, [r]);
  const c = (d) => {
    s(d);
    try {
      const f = JSON.parse(d);
      o(null), t == null || t(f);
    } catch (f) {
      o(f.message);
    }
  };
  return /* @__PURE__ */ h("div", { children: [
    /* @__PURE__ */ a(K.TextArea, { rows: 5, value: n, onChange: (d) => c(d.target.value) }),
    l && /* @__PURE__ */ a(N.Text, { type: "danger", style: { fontSize: 11 }, children: l })
  ] });
}
function Ut({ value: e, onChange: t, resources: r, resourceType: n }) {
  const s = r.filter((o) => o.enabled !== !1 && (!n || o.type === n)).map((o) => ({
    value: o.id,
    label: `${o.name} (${o.type})`
  })), l = r.find((o) => o.id === e);
  return /* @__PURE__ */ h("div", { children: [
    /* @__PURE__ */ a(ae, { allowClear: !0, value: e, onChange: t, options: s }),
    (l == null ? void 0 : l.credentialId) && /* @__PURE__ */ h(N.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
      "凭证：",
      l.credentialName ?? l.credentialId
    ] })
  ] });
}
function Wt({ name: e, schema: t, required: r, resources: n, resourceType: s, properties: l, config: o }) {
  let c, d = "value";
  switch (rt(t)) {
    case "resource":
      c = /* @__PURE__ */ a(Ut, { resources: n, resourceType: s });
      break;
    case "enum":
      c = /* @__PURE__ */ a(
        ae,
        {
          allowClear: !0,
          options: (t.enum ?? []).map((f) => ({ label: String(f), value: f }))
        }
      );
      break;
    case "number":
      c = /* @__PURE__ */ a(He, { style: { width: "100%" } });
      break;
    case "boolean":
      c = /* @__PURE__ */ a(Xe, {}), d = "checked";
      break;
    case "key-value":
      c = /* @__PURE__ */ a(
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
      c = /* @__PURE__ */ a(Kt, { testId: `field-${e}`, suggestions: Mt(l, o) });
      break;
    case "json":
      c = /* @__PURE__ */ a(Gt, {});
      break;
    default:
      c = /* @__PURE__ */ a(K, { placeholder: t.description });
      break;
  }
  return /* @__PURE__ */ a(
    Ne.Item,
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
    catalog: s,
    resources: l,
    issues: o,
    onApplyConfig: c,
    onDeleteNode: d,
    onDeleteEdge: f,
    onUpdateEdge: y,
    onSelectNode: x
  } = e, [k] = Ne.useForm(), [v, m] = b("{}"), [p, C] = b(null);
  G(() => {
    k.setFieldsValue((t == null ? void 0 : t.config) ?? {}), m(JSON.stringify((t == null ? void 0 : t.config) ?? {}, null, 2)), C(null);
  }, [k, t == null ? void 0 : t.id, t == null ? void 0 : t.config]);
  const A = new Set((r == null ? void 0 : r.configurationSchema.required) ?? []), E = Object.entries((r == null ? void 0 : r.configurationSchema.properties) ?? {}), S = () => {
    if (E.length > 0) {
      k.validateFields().then((u) => c(At((t == null ? void 0 : t.config) ?? {}, u)));
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
  }, R = n.map((u, I) => ({
    ...u,
    index: I,
    key: `${I}:${u.source}->${u.target}`
  })), L = [
    {
      title: "连线",
      dataIndex: "source",
      key: "source",
      width: 110,
      render: (u, I) => /* @__PURE__ */ h(N.Text, { style: { fontSize: 11 }, children: [
        I.source,
        " → ",
        I.target
      ] })
    },
    {
      title: "端口",
      key: "ports",
      render: (u, I) => {
        const T = s.get(I.source), X = s.get(I.target);
        return /* @__PURE__ */ h("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ a(
            ae,
            {
              size: "small",
              value: I.sourcePort,
              style: { width: 110 },
              onChange: (Y) => y(I.index, { ...I, sourcePort: Y }),
              options: ((T == null ? void 0 : T.outputs) ?? []).map((Y) => ({ label: `${Y.name} (${Y.type})`, value: Y.name }))
            }
          ),
          /* @__PURE__ */ a(
            ae,
            {
              size: "small",
              value: I.targetPort,
              style: { width: 110 },
              onChange: (Y) => y(I.index, { ...I, targetPort: Y }),
              options: ((X == null ? void 0 : X.inputs) ?? []).map((Y) => ({ label: `${Y.name} (${Y.type})`, value: Y.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (u, I) => /* @__PURE__ */ a($, { danger: !0, size: "small", type: "text", onClick: () => f(I.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ h("div", { className: "industrial-properties-body", children: [
    !t && /* @__PURE__ */ a(Ye, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    t && /* @__PURE__ */ h(be, { children: [
      /* @__PURE__ */ a(N.Text, { strong: !0, children: (r == null ? void 0 : r.name) ?? t.type }),
      /* @__PURE__ */ a(oe, { color: "blue", style: { marginLeft: 8 }, children: t.id }),
      /* @__PURE__ */ a(Ne, { form: k, layout: "vertical", size: "small", style: { marginTop: 12 }, children: E.map(([u, I]) => /* @__PURE__ */ a(
        Wt,
        {
          name: u,
          schema: I,
          required: A.has(u),
          resources: l,
          resourceType: typeof (r == null ? void 0 : r.configurationSchema.resourceType) == "string" ? r.configurationSchema.resourceType : void 0,
          properties: (r == null ? void 0 : r.configurationSchema.properties) ?? {},
          config: (t == null ? void 0 : t.config) ?? {}
        },
        u
      )) }),
      E.length === 0 && /* @__PURE__ */ h(be, { children: [
        /* @__PURE__ */ a(N.Text, { type: "secondary", style: { fontSize: 12 }, children: "配置 (JSON)" }),
        /* @__PURE__ */ a(
          K.TextArea,
          {
            rows: 8,
            value: v,
            style: { marginTop: 6 },
            onChange: (u) => m(u.target.value)
          }
        ),
        p && /* @__PURE__ */ a(N.Text, { type: "danger", style: { fontSize: 12 }, children: p })
      ] }),
      /* @__PURE__ */ a($, { type: "primary", block: !0, onClick: S, children: "应用配置" }),
      /* @__PURE__ */ a($, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: d, children: "删除节点" }),
      (r == null ? void 0 : r.activityType) && /* @__PURE__ */ a(N.Text, { type: "secondary", style: { fontSize: 11, display: "block", marginTop: 8 }, children: r.activityType })
    ] }),
    /* @__PURE__ */ h(Fe, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "连线 (",
      n.length,
      ")"
    ] }),
    n.length === 0 ? /* @__PURE__ */ a(N.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ a(ne, { size: "small", columns: L, dataSource: R, pagination: !1 }),
    o.length > 0 && /* @__PURE__ */ h(be, { children: [
      /* @__PURE__ */ h(Fe, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        o.length,
        ")"
      ] }),
      o.map((u, I) => /* @__PURE__ */ h("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ a(oe, { color: "red", children: u.code }),
        /* @__PURE__ */ h(
          N.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const T = /^nodes\.([^.]+)/.exec(u.path);
              T && x(T[1]);
            },
            children: [
              u.path,
              ": ",
              u.message
            ]
          }
        )
      ] }, `${u.path}-${I}`))
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
const P = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, M = (e) => Array.isArray(e) ? e : [], g = (e, t = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : t ?? "", q = (e, t = 0) => {
  const r = typeof e == "string" ? Number(e) : e;
  return typeof r == "number" && Number.isFinite(r) ? r : t;
}, i = (e, ...t) => {
  for (const r of t)
    if (e[r] !== void 0 && e[r] !== null) return e[r];
};
function V(e) {
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
  const t = M(e).map(Vt).filter((n) => n !== null), r = /* @__PURE__ */ new Set();
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
    dependencies: M(i(t, "dependencies")).map((n) => g(n)).filter(Boolean)
  } : null;
}
function er(e) {
  const t = V(e), r = Array.isArray(t) ? t : M(i(P(t), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return r.map(Zt).filter((s) => s !== null).filter((s) => n.has(s.nodeTypeId) ? !1 : (n.add(s.nodeTypeId), !0));
}
function tr(e) {
  const t = V(e), r = Array.isArray(t) ? t : M(i(P(t), "resources", "items")), n = /* @__PURE__ */ new Set();
  return r.map((s) => {
    const l = P(s), o = g(i(l, "id")).trim();
    return !o || n.has(o) ? null : (n.add(o), {
      id: o,
      name: g(i(l, "name"), o),
      type: g(i(l, "type"), "unknown"),
      enabled: l.enabled !== !1,
      credentialId: typeof l.credentialId == "string" ? l.credentialId : null,
      credentialName: typeof l.credentialName == "string" ? l.credentialName : null
    });
  }).filter((s) => s !== null);
}
function nt(e) {
  const t = P(e), r = g(i(t, "workflowId", "workflow_id", "id")).trim();
  return r ? {
    workflowId: r,
    name: g(i(t, "name", "title"), r),
    version: q(i(t, "version"), 1)
  } : null;
}
function st(e) {
  const t = V(e);
  return (Array.isArray(t) ? t : M(i(P(t), "workflows", "items"))).map(nt).filter((n) => n !== null);
}
function $e(e) {
  const t = P(e), r = {};
  for (const [n, s] of Object.entries(t))
    s !== void 0 && (r[n] = s);
  return r;
}
function rr(e) {
  const t = P(e), r = g(i(t, "id", "nodeId", "node_id")).trim();
  if (!r) return null;
  const n = P(i(t, "position", "layout"));
  return {
    id: r,
    type: g(i(t, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: $e(i(t, "config", "configuration")),
    x: q(i(t, "x") ?? i(n, "x"), 0),
    y: q(i(t, "y") ?? i(n, "y"), 0)
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
function sr(e) {
  const t = V(e), r = P(t), n = nt(r) ?? { workflowId: "", name: "", version: 1 }, s = M(i(r, "nodes")).map(rr).filter((d) => d !== null), l = M(i(r, "edges")).map(nr).filter((d) => d !== null), o = P(i(r, "trigger")), c = {
    kind: g(i(o, "kind", "type"), "manual"),
    cron: i(o, "cron") === void 0 ? null : g(o.cron),
    eventName: i(o, "eventName", "event_name") === void 0 ? null : g(i(o, "eventName", "event_name")),
    config: $e(i(o, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: s,
    edges: l,
    trigger: c,
    resourceReferences: M(i(r, "resourceReferences", "resource_references")).map((d) => g(d)).filter(Boolean),
    layout: $e(i(r, "layout"))
  };
}
const fe = (e) => e === !0 || typeof e == "string" && e.toLowerCase() === "true";
function or(e) {
  const t = V(e);
  return (Array.isArray(t) ? t : M(i(P(t), "resources", "items"))).map((n) => {
    const s = P(n), l = i(s, "credentialId", "credential_id");
    return {
      id: g(i(s, "id"), "—"),
      name: g(i(s, "name"), "—"),
      type: g(i(s, "type"), "—"),
      provider: g(i(s, "provider"), "—"),
      enabled: fe(i(s, "enabled")),
      credentialId: l == null ? null : g(l),
      credentialName: i(s, "credentialName") == null ? null : g(i(s, "credentialName"))
    };
  });
}
function ot(e) {
  const t = V(e);
  return (Array.isArray(t) ? t : M(i(P(t), "credentials", "items"))).map((n) => {
    const s = P(n);
    return {
      id: g(i(s, "id"), "—"),
      name: g(i(s, "name"), "—"),
      kind: g(i(s, "kind"), "—"),
      version: q(i(s, "version"))
    };
  });
}
function ar(e) {
  const t = V(e);
  return (Array.isArray(t) ? t : M(i(P(t), "tests", "items"))).map((n) => {
    const s = P(n);
    return {
      resourceId: g(i(s, "resourceId", "resource_id"), "—"),
      ok: fe(i(s, "ok")),
      detail: g(i(s, "detail"), "")
    };
  });
}
const at = (e) => {
  if (e == null) return null;
  const t = P(e);
  return Object.keys(t).length === 0 ? null : {
    code: g(i(t, "code"), "OTHER"),
    message: g(i(t, "message"), ""),
    action: g(i(t, "action"), "")
  };
};
function ir(e) {
  const t = V(e);
  return M(i(P(t), "triggers", "items")).map((n) => {
    const s = P(n), l = (o) => i(s, o) == null ? null : g(i(s, o));
    return {
      workflowId: g(i(s, "workflowId", "workflow_id"), "—"),
      version: q(i(s, "version")),
      kind: g(i(s, "kind"), "manual"),
      cron: l("cron"),
      eventName: l("eventName"),
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
  const t = V(e), r = P(t);
  if (Object.keys(r).length === 0) return null;
  const n = M(i(r, "files")).map((l) => {
    const o = P(l);
    return {
      path: g(i(o, "path"), "—"),
      sourceDate: g(i(o, "sourceDate"), ""),
      inLedger: fe(i(o, "inLedger")),
      succeededTargets: q(i(o, "succeededTargets")),
      incompleteTargets: q(i(o, "incompleteTargets")),
      wouldSkip: fe(i(o, "wouldSkip")),
      wouldCollect: fe(i(o, "wouldCollect")),
      duplicateRisk: fe(i(o, "duplicateRisk"))
    };
  }), s = P(i(r, "summary"));
  return {
    resourceId: g(i(r, "resourceId"), "—"),
    from: g(i(r, "from"), ""),
    to: g(i(r, "to"), ""),
    files: n,
    summary: {
      discovered: q(i(s, "discovered")),
      alreadyComplete: q(i(s, "alreadyComplete")),
      needsCollection: q(i(s, "needsCollection")),
      duplicateRisk: q(i(s, "duplicateRisk"))
    }
  };
}
function cr(e) {
  const t = V(e);
  return (Array.isArray(t) ? t : M(i(P(t), "files", "items"))).map((n) => {
    const s = P(n);
    return {
      id: g(i(s, "id"), "—"),
      path: g(i(s, "path"), "—"),
      identityKey: g(i(s, "identityKey"), ""),
      sourceResourceId: g(i(s, "sourceResourceId"), ""),
      length: q(i(s, "length")),
      sourceDate: g(i(s, "sourceDate"), ""),
      collectedAt: g(i(s, "collectedAt"), ""),
      status: g(i(s, "status"), "—"),
      deliveries: M(i(s, "deliveries")).map((l) => {
        const o = P(l);
        return {
          id: g(i(o, "id"), "—"),
          collectionFileId: g(i(o, "collectionFileId"), ""),
          targetId: g(i(o, "targetId"), "—"),
          targetKind: g(i(o, "targetKind"), "record"),
          status: g(i(o, "status"), "—"),
          attempts: q(i(o, "attempts")),
          records: q(i(o, "records")),
          error: i(o, "error") == null ? null : g(i(o, "error")),
          accepted: q(i(o, "accepted")),
          rejected: q(i(o, "rejected")),
          warnings: q(i(o, "warnings")),
          diagnosis: at(i(o, "diagnosis"))
        };
      })
    };
  });
}
function dr(e) {
  const t = V(e), r = P(t), n = M(i(r, "runs")).map((l) => {
    const o = P(l);
    return {
      id: g(i(o, "id"), "—"),
      workflowId: g(i(o, "workflowId", "workflow_id"), "—"),
      status: g(i(o, "status"), "—"),
      startedAt: g(i(o, "startedAt", "started_at"), ""),
      finishedAt: i(o, "finishedAt", "finished_at") == null ? null : g(i(o, "finishedAt", "finished_at")),
      error: i(o, "error") == null ? null : g(i(o, "error"))
    };
  }), s = M(i(r, "deliveries")).map((l) => {
    const o = P(l);
    return {
      id: g(i(o, "id"), "—"),
      collectionFileId: g(i(o, "collectionFileId", "collection_file_id"), ""),
      targetId: g(i(o, "targetId", "target_id"), "—"),
      targetKind: g(i(o, "targetKind", "target_kind"), "record"),
      status: g(i(o, "status"), "—"),
      attempts: q(i(o, "attempts")),
      records: q(i(o, "records")),
      error: i(o, "error") == null ? null : g(i(o, "error")),
      diagnosis: at(i(o, "diagnosis"))
    };
  });
  return { runs: n, deliveries: s };
}
function ur(e) {
  if (e == null) return null;
  const t = V(e), r = P(t);
  if (Object.keys(r).length === 0) return null;
  const n = M(i(r, "nodes")).slice(0, 64).map((s) => {
    const l = P(s);
    return {
      nodeId: g(i(l, "nodeId", "node_id"), "—"),
      status: g(i(l, "status"), "—"),
      records: q(i(l, "records")),
      files: q(i(l, "files")),
      error: i(l, "error") == null ? null : g(i(l, "error"), void 0),
      samples: M(i(l, "samples")).slice(0, 3).map((o) => g(o))
    };
  });
  return {
    status: g(i(r, "status"), "—"),
    error: i(r, "error") == null ? null : g(i(r, "error"), void 0),
    executed: M(i(r, "executed")).map((s) => g(s)),
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
  onSave: s,
  ctx: l,
  workflowId: o
}) {
  const [c, d] = b(r), [f, y] = b([]), [x, k] = b(null);
  G(() => {
    d(r);
  }, [r]), G(() => {
    e && l.hubQuery("industrial/trigger-status").then((p) => y(ir(p))).catch((p) => k(p instanceof Error ? p.message : String(p)));
  }, [l, e]);
  const v = f.find((p) => p.workflowId === o);
  return /* @__PURE__ */ a(
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
        k(null), s(c), t();
      },
      width: 720,
      children: /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ h(O, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ a(N.Text, { children: "触发方式" }),
          /* @__PURE__ */ a(O, { wrap: !0, children: pr.map((p) => /* @__PURE__ */ a(
            $,
            {
              type: c.kind === p.value ? "primary" : "default",
              onClick: () => d({ ...c, kind: p.value }),
              children: p.label
            },
            p.value
          )) })
        ] }),
        /* @__PURE__ */ a(O, { wrap: !0, children: /* @__PURE__ */ a(
          ae,
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
        /* @__PURE__ */ h(O, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ a(N.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ h(O, { wrap: !0, children: [
            /* @__PURE__ */ a(N.Text, { children: "常用" }),
            fr.map((p) => /* @__PURE__ */ a(
              $,
              {
                size: "small",
                onClick: () => d({ ...c, cron: p.cron }),
                children: p.label
              },
              p.cron
            ))
          ] }),
          /* @__PURE__ */ a(
            K,
            {
              "aria-label": "Cron 表达式",
              placeholder: "分 时 日 月 周，例如 0 2 * * *",
              value: c.cron ?? "",
              onChange: (p) => d({ ...c, cron: p.target.value })
            }
          ),
          /* @__PURE__ */ a(
            K,
            {
              "aria-label": "事件名称",
              placeholder: "事件名称",
              value: c.eventName ?? "",
              onChange: (p) => d({ ...c, eventName: p.target.value })
            }
          ),
          /* @__PURE__ */ h(N.Text, { type: "secondary", children: [
            "下次运行：",
            (v == null ? void 0 : v.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        v && /* @__PURE__ */ a(
          ne,
          {
            rowKey: "workflowId",
            dataSource: [v],
            pagination: !1,
            columns: [
              { title: "状态", dataIndex: "lastStatus", render: (p) => p ?? "未运行" },
              { title: "上次运行", dataIndex: "lastStartedAt", render: (p) => p ?? "—" },
              { title: "错误", dataIndex: "lastError", render: (p) => p ?? "—" }
            ]
          }
        ),
        x && /* @__PURE__ */ a(ie, { type: "error", showIcon: !0, message: x })
      ] })
    }
  );
}
function yr(e) {
  const t = e.config.resourceId;
  return typeof t == "string" && t.trim() ? t : void 0;
}
function mr({
  open: e,
  onClose: t,
  ctx: r,
  workflowId: n,
  resourceId: s,
  lookbackDays: l
}) {
  const [o, c] = b(null), [d, f] = b(null), [y, x] = b(!1), k = _(async () => {
    x(!0), f(null);
    try {
      await r.hubCommand("industrial/preview-backfill", {
        workflowId: n,
        resourceId: s,
        lookbackDays: l
      }), c(lr(await r.hubQuery("industrial/backfill-preview")));
    } catch (v) {
      f(v instanceof Error ? v.message : String(v));
    } finally {
      x(!1);
    }
  }, [r, l, s, n]);
  return /* @__PURE__ */ a(
    me,
    {
      open: e,
      title: "补采预览",
      onCancel: t,
      width: 820,
      footer: [
        /* @__PURE__ */ a($, { loading: y, onClick: () => void k(), children: "重新扫描" }, "refresh"),
        /* @__PURE__ */ a($, { type: "primary", onClick: t, children: "关闭" }, "close")
      ],
      children: /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ h(O, { children: [
          /* @__PURE__ */ h($, { type: "primary", loading: y, onClick: () => void k(), children: [
            "扫描最近 ",
            l,
            " 天"
          ] }),
          /* @__PURE__ */ h(N.Text, { type: "secondary", children: [
            "资源：",
            s || "未选择"
          ] })
        ] }),
        d && /* @__PURE__ */ a(ie, { type: "error", showIcon: !0, message: d }),
        o && /* @__PURE__ */ h(be, { children: [
          /* @__PURE__ */ h(N.Text, { children: [
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
          /* @__PURE__ */ a(
            ne,
            {
              rowKey: "path",
              dataSource: o.files,
              pagination: !1,
              size: "small",
              columns: [
                { title: "文件", dataIndex: "path" },
                { title: "源日期", dataIndex: "sourceDate" },
                { title: "账本", dataIndex: "inLedger", render: (v) => v ? "已登记" : "新文件" },
                { title: "成功目标", dataIndex: "succeededTargets" },
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
const { Header: hr, Content: wr } = wt;
function vr({ ctx: e }) {
  const [t, r] = vt.useMessage(), [n, s] = b([]), [l, o] = b([]), [c, d] = b([]), [f, y] = b([]), [x, k] = b(""), [v, m] = b(1), [p, C] = b(!0), [A, E] = b([]), [S, R] = b(() => Ee()), [L, u] = b(null), [I, T] = b([]), [X, Y] = b(!1), [he, _e] = b(!0), [it, de] = b(!1), [ge, te] = b(!1), [Z, lt] = b(null), [Oe, Ae] = b(!1), [ze, je] = b(!1), Le = se(1), xe = se(S.trigger);
  G(() => {
    xe.current = S.trigger;
  }, [S.trigger]), G(() => {
    let w = !0;
    return Promise.allSettled([
      e.hubQuery("industrial/nodes"),
      e.hubQuery("industrial/workflows"),
      e.hubQuery("industrial/resources")
    ]).then(([D, B, j]) => {
      var U;
      if (!w) return;
      const F = [];
      if (D.status === "fulfilled" ? s(er(D.value)) : F.push(`节点描述: ${String(D.reason)}`), B.status === "fulfilled") {
        const W = st(B.value);
        o(W);
        const Q = new URLSearchParams(window.location.search).get("workflowId"), ee = Q && W.some((le) => le.workflowId === Q) ? Q : (U = W[0]) == null ? void 0 : U.workflowId;
        ee && qe(ee);
      } else F.push(`工作流: ${String(B.reason)}`);
      if (j.status === "fulfilled") {
        const W = tr(j.value);
        y(W);
        const Q = W.map((ee) => ee.id);
        d(Q), k((ee) => ee || Q[0] || "");
      } else F.push(`资源: ${String(j.reason)}`);
      E(F);
    }).finally(() => {
      w && C(!1);
    }), () => {
      w = !1;
    };
  }, [e]);
  const re = Je(
    () => new Map(n.map((w) => [w.nodeTypeId, w])),
    [n]
  ), ke = S.nodes.find((w) => w.id === L) ?? null, pe = _(async (w, D, B) => {
    Y(!0);
    try {
      return await H().api.hubCommand(w, D), t.success(B), !0;
    } catch (j) {
      return t.error(`${w} failed: ${j instanceof Error ? j.message : String(j)}`), !1;
    } finally {
      Y(!1);
    }
  }, [t]), qe = _(async (w) => {
    try {
      const D = await e.hubQuery("industrial/workflow-definitions"), j = (V(D) ?? []).find((W) => {
        const Q = W;
        return String((Q == null ? void 0 : Q.workflowId) ?? "") === w;
      }), F = j === void 0 ? { ...Ee(w), name: w } : sr(j);
      R(F), m(Number(F.layout.lookbackDays ?? 1) || 1);
      const U = F.nodes.map((W) => /^n(\d+)$/.exec(W.id)).filter(Boolean).reduce((W, Q) => Math.max(W, Number(Q[1])), 0);
      Le.current = U + 1, u(null), de(!1), T([]), te(!1);
    } catch (D) {
      t.error(`加载工作流失败: ${D instanceof Error ? D.message : String(D)}`);
    }
  }, [e, t]), we = _(async () => {
    try {
      const w = ur(await e.hubQuery("industrial/execution"));
      if (w === null) return;
      lt(w);
    } catch {
    }
  }, [e]), ct = _(async () => {
    var B;
    const w = new Set(S.resourceReferences ?? []);
    for (const j of S.nodes)
      for (const F of ["resourceId", "targetResourceId"]) {
        const U = (B = j.config) == null ? void 0 : B[F];
        typeof U == "string" && U.trim() && w.add(U.trim());
      }
    const D = {
      ...S,
      trigger: xe.current,
      resourceReferences: [...w],
      layout: { ...S.layout, lookbackDays: v }
    };
    await pe("industrial/save-workflow", D, "已保存") && (R(D), te(!1));
  }, [pe, S, v]), dt = _(async () => {
    if (ge) {
      t.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await pe("industrial/publish-workflow", { workflowId: S.workflowId }, "已发布") && await we();
  }, [pe, S.workflowId, ge, t, we]), ut = _(async () => {
    if (ge) {
      t.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await pe(
      "industrial/run-workflow",
      { workflowId: S.workflowId, resourceId: x },
      "采集已启动"
    ) && await we();
  }, [pe, S.workflowId, ge, t, we, x]), pt = _((w, D, B) => {
    const j = ue(re, w);
    if (!j) return;
    const F = `n${Le.current++}`;
    R((U) => ({
      ...U,
      nodes: [...U.nodes, Dt(j, F, D, B)]
    })), u(F), de(!0), T([]), te(!0);
  }, [re]), ft = _((w, D) => {
    const B = S.nodes.find((ce) => ce.id === w), j = S.nodes.find((ce) => ce.id === D);
    if (!B || !j)
      return t.warning("连线失败：端点不在画布上"), null;
    const F = ue(re, B.type), U = ue(re, j.type);
    if (!F || !U) return null;
    let W = B, Q = j, ee = Ge(F, U);
    if (!ee) {
      const ce = Ge(U, F);
      if (!ce)
        return t.warning(`端口类型不兼容：${F.name} → ${U.name}`), null;
      W = j, Q = B, ee = ce;
    }
    const le = Rt(S, {
      source: W.id,
      sourcePort: ee.sourcePort,
      target: Q.id,
      targetPort: ee.targetPort
    }, re);
    return le.issue ? (t.warning(`${le.issue.code}: ${le.issue.message}`), T((ce) => [...ce, le.issue]), null) : le.definition === S ? (t.info("这条连线已经存在"), null) : (R(le.definition), te(!0), ee);
  }, [re, S, t]), gt = _(() => {
    const w = qt(S, re, new Set(c));
    T(w), w.length === 0 ? t.success("本地校验通过（发布时服务端仍会复核）") : (t.warning(`发现 ${w.length} 个问题，见属性抽屉定位列表`), de(!0));
  }, [S, re, c, t]), yt = _((w, D) => {
    R((B) => ({
      ...B,
      edges: B.edges.map((j, F) => F === w ? D : j)
    })), te(!0);
  }, []);
  return p ? /* @__PURE__ */ a(Ve, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ h("div", { className: "industrial-designer", children: [
    r,
    /* @__PURE__ */ h(hr, { className: "industrial-header", children: [
      /* @__PURE__ */ h("div", { className: "industrial-title", children: [
        /* @__PURE__ */ a(N.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ a(N.Text, { type: "secondary", children: S.name || S.workflowId }),
        ge && /* @__PURE__ */ a(oe, { color: "orange", children: "未保存" }),
        Z && /* @__PURE__ */ a(oe, { color: Z.error ? "error" : "success", children: Z.status })
      ] }),
      /* @__PURE__ */ h("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ a(
          $,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": he,
            onClick: () => _e((w) => !w),
            children: he ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ a(
          $,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => de((w) => !w),
            children: "属性"
          }
        ),
        /* @__PURE__ */ a($, { size: "small", "data-testid": "open-trigger", onClick: () => Ae(!0), children: "触发器" }),
        /* @__PURE__ */ a(
          $,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => je(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ a("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ h(O, { size: 4, children: [
          /* @__PURE__ */ a($, { size: "small", loading: X, onClick: () => void ct(), children: "保存" }),
          /* @__PURE__ */ a($, { size: "small", onClick: gt, children: "校验" }),
          /* @__PURE__ */ a($, { size: "small", loading: X, onClick: () => void dt(), children: "发布" }),
          /* @__PURE__ */ a($, { size: "small", type: "primary", loading: X, onClick: () => void ut(), children: "运行" })
        ] }),
        /* @__PURE__ */ a(
          ae,
          {
            className: "industrial-resource-select",
            size: "small",
            value: x,
            onChange: k,
            placeholder: "运行资源",
            options: c.map((w) => ({ value: w, label: w }))
          }
        ),
        /* @__PURE__ */ a(
          He,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: v,
            onChange: (w) => {
              m(Number(w) || 1), te(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ h(wr, { className: `industrial-body${he ? "" : " industrial-body--collapsed"}`, children: [
      he ? /* @__PURE__ */ h("aside", { className: "industrial-palette", children: [
        l.length > 0 && /* @__PURE__ */ a(
          ae,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: S.workflowId,
            placeholder: "选择工作流",
            onChange: (w) => void qe(w),
            options: l.map((w) => ({ value: w.workflowId, label: w.name }))
          }
        ),
        /* @__PURE__ */ a(Et, { descriptors: n, loading: !1 })
      ] }) : /* @__PURE__ */ a("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ a($, { type: "text", size: "small", onClick: () => _e(!0), children: "节点库" }) }),
      /* @__PURE__ */ h("section", { className: "industrial-center", children: [
        A.length > 0 && /* @__PURE__ */ a(
          ie,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: A.join("；")
          }
        ),
        /* @__PURE__ */ a(
          Nt,
          {
            definition: S,
            catalog: re,
            selectedId: L,
            onSelect: (w) => {
              u(w), w && de(!0);
            },
            onDropNode: pt,
            onCreateEdge: ft,
            onPositions: (w) => {
              R((D) => jt(D, w)), te(!0);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ h(
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
          Z && /* @__PURE__ */ a(
            ie,
            {
              style: { marginBottom: 12 },
              type: Z.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${Z.status}`,
              description: /* @__PURE__ */ h(O, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                Z.error && /* @__PURE__ */ a("span", { children: Z.error }),
                Z.executed.length > 0 && /* @__PURE__ */ h("span", { children: [
                  "已执行节点: ",
                  Z.executed.join(", ")
                ] }),
                Z.nodes.length > 0 && /* @__PURE__ */ a(
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
                        render: (w, D) => D.error ?? (D.samples.length > 0 ? D.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ a(
            Qt,
            {
              node: ke,
              descriptor: ke ? ue(re, ke.type) ?? null : null,
              edges: S.edges,
              catalog: re,
              resources: f,
              issues: I,
              onApplyConfig: (w) => {
                L && (R((D) => zt(D, L, w)), te(!0));
              },
              onDeleteNode: () => {
                R((w) => _t(w, L ?? "")), u(null), te(!0);
              },
              onDeleteEdge: (w) => {
                R((D) => Ot(D, w)), te(!0);
              },
              onUpdateEdge: yt,
              onSelectNode: (w) => {
                u(w), de(!0);
              }
            }
          )
        ]
      }
    ),
    Oe && /* @__PURE__ */ a(
      gr,
      {
        open: Oe,
        onClose: () => Ae(!1),
        trigger: S.trigger,
        resourceIds: c,
        workflowId: S.workflowId,
        ctx: e,
        onSave: (w) => {
          xe.current = w, R((D) => ({ ...D, trigger: w })), te(!0);
        }
      }
    ),
    ze && /* @__PURE__ */ a(
      mr,
      {
        open: ze,
        onClose: () => je(!1),
        ctx: e,
        workflowId: S.workflowId,
        resourceId: x,
        lookbackDays: v
      }
    )
  ] });
}
function br({ block: e, ctx: t }) {
  const [r, n] = b([]), [s, l] = b(!1), [o, c] = b(""), [d, f] = b(null), [y, x] = b(!1), k = _(async () => {
    try {
      n(st(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
    } catch (p) {
      f(p instanceof Error ? p.message : String(p));
    }
  }, [e == null ? void 0 : e.query, t]);
  G(() => {
    k();
  }, [k]);
  const v = _(async () => {
    var C;
    const p = o.trim();
    if (!p) {
      f("请输入工作流名称");
      return;
    }
    x(!0), f(null);
    try {
      const E = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await t.hubCommand("industrial/save-workflow", {
        ...Ee(E),
        name: p,
        layout: { lookbackDays: 1 }
      }), l(!1), c(""), await k(), (C = t.navigate) == null || C.call(t, `/industrial/designer?workflowId=${encodeURIComponent(E)}`);
    } catch (A) {
      f(A instanceof Error ? A.message : String(A));
    } finally {
      x(!1);
    }
  }, [t, k, o]), m = _((p) => {
    var C;
    (C = t.navigate) == null || C.call(t, `/industrial/designer?workflowId=${encodeURIComponent(p)}`);
  }, [t]);
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(O, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ a(N.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ a($, { type: "primary", onClick: () => l(!0), children: "新建工作流" })
    ] }),
    d && /* @__PURE__ */ a(N.Text, { type: "danger", children: d }),
    /* @__PURE__ */ a(
      ne,
      {
        rowKey: "workflowId",
        dataSource: r,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (p) => /* @__PURE__ */ a($, { type: "link", style: { padding: 0 }, onClick: () => m(p), children: p })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ a(
      me,
      {
        open: s,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: y,
        onCancel: () => l(!1),
        onOk: () => void v(),
        children: /* @__PURE__ */ h(O, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ a(N.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ a(
            K,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: o,
              onChange: (p) => c(p.target.value ?? "")
            }
          ),
          d && /* @__PURE__ */ a(N.Text, { type: "danger", children: d })
        ] })
      }
    )
  ] });
}
const Te = {
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
}, Ir = [
  { value: "filesystem.local", label: "本地目录" },
  { value: "filesystem.unc", label: "Windows UNC 共享" },
  { value: "sqlserver", label: "SQL Server" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mysql", label: "MySQL" },
  { value: "sqlite", label: "SQLite" }
];
function xr(e) {
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
function kr({ block: e, ctx: t }) {
  const [r, n] = b([]), [s, l] = b([]), [o, c] = b([]), [d, f] = b(Te), [y, x] = b(!1), [k, v] = b(null), [m, p] = b(!1), C = _(async () => {
    try {
      const [u, I, T] = await Promise.all([
        t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        t.hubQuery("industrial/credentials"),
        t.hubQuery("industrial/resource-tests")
      ]);
      n(or(u)), l(ot(I)), c(ar(T));
    } catch (u) {
      v(u instanceof Error ? u.message : String(u));
    }
  }, [e == null ? void 0 : e.query, t]);
  G(() => {
    C();
  }, [C]);
  const A = _((u) => {
    f({
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
    }), x(!0);
  }, []), E = _(async () => {
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
        config: xr(d),
        credentialId: d.credentialId || null,
        enabled: d.enabled
      }), x(!1), f(Te), await C();
    } catch (I) {
      v(I instanceof Error ? I.message : String(I));
    } finally {
      p(!1);
    }
  }, [t, d, C]), S = _(async (u) => {
    v(null);
    try {
      await t.hubCommand("industrial/test-resource", { id: u });
    } catch (I) {
      v(I instanceof Error ? I.message : String(I));
    }
    await C();
  }, [t, C]), R = _(async (u) => {
    v(null);
    try {
      await t.hubCommand("industrial/delete-resource", { id: u }), await C();
    } catch (I) {
      v(I instanceof Error ? I.message : String(I));
    }
  }, [t, C]), L = new Map(o.map((u) => [u.resourceId, u]));
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(O, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ a(N.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ a($, { type: "primary", onClick: () => {
        f(Te), x(!0);
      }, children: "新建资源" })
    ] }),
    k && /* @__PURE__ */ a(ie, { type: "error", showIcon: !0, message: k }),
    /* @__PURE__ */ a(
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
            render: (u, I) => {
              const T = L.get(I.id);
              return T ? /* @__PURE__ */ a(N.Text, { type: T.ok ? "success" : "danger", children: T.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (u, I) => /* @__PURE__ */ h(O, { size: 4, children: [
              /* @__PURE__ */ a($, { size: "small", onClick: () => void S(I.id), children: "测试连接" }),
              /* @__PURE__ */ a($, { size: "small", onClick: () => A(I), children: "编辑" }),
              /* @__PURE__ */ a($, { size: "small", danger: !0, onClick: () => void R(I.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ a(
      me,
      {
        open: y,
        title: d.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: m,
        onCancel: () => x(!1),
        onOk: () => void E(),
        children: /* @__PURE__ */ h(O, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ a(
            K,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: d.id,
              onChange: (u) => f({ ...d, id: u.target.value })
            }
          ),
          /* @__PURE__ */ a(
            K,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: d.name,
              onChange: (u) => f({ ...d, name: u.target.value })
            }
          ),
          /* @__PURE__ */ a(
            ae,
            {
              "aria-label": "资源类型",
              value: d.type,
              options: Ir,
              onChange: (u) => f({ ...d, type: u })
            }
          ),
          (d.type === "filesystem.local" || d.type === "filesystem.unc") && /* @__PURE__ */ a(
            K,
            {
              "aria-label": "目录路径",
              placeholder: d.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: d.root,
              onChange: (u) => f({ ...d, root: u.target.value })
            }
          ),
          (d.type === "sqlserver" || d.type === "postgresql" || d.type === "mysql") && /* @__PURE__ */ h(O, { children: [
            /* @__PURE__ */ a(
              K,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: d.host,
                onChange: (u) => f({ ...d, host: u.target.value })
              }
            ),
            /* @__PURE__ */ a(
              K,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: d.database,
                onChange: (u) => f({ ...d, database: u.target.value })
              }
            ),
            /* @__PURE__ */ a(
              K,
              {
                "aria-label": "端口",
                placeholder: "端口",
                value: d.port,
                onChange: (u) => f({ ...d, port: u.target.value })
              }
            )
          ] }),
          d.type === "sqlite" && /* @__PURE__ */ a(
            K,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: d.database,
              onChange: (u) => f({ ...d, database: u.target.value })
            }
          ),
          /* @__PURE__ */ a(
            ae,
            {
              "aria-label": "凭证",
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: d.credentialId || void 0,
              options: s.map((u) => ({ value: u.id, label: u.name })),
              onChange: (u) => f({ ...d, credentialId: u ?? "" })
            }
          ),
          /* @__PURE__ */ h(O, { children: [
            /* @__PURE__ */ a(
              Xe,
              {
                "aria-label": "启用资源",
                checked: d.enabled,
                onChange: (u) => f({ ...d, enabled: u })
              }
            ),
            /* @__PURE__ */ a(N.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
function Sr({ block: e, ctx: t }) {
  const [r, n] = b([]), [s, l] = b(!1), [o, c] = b(""), [d, f] = b(""), [y, x] = b("username-password"), [k, v] = b(""), [m, p] = b(!1), [C, A] = b(null), [E, S] = b(!1), R = _(async () => {
    try {
      n(ot(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
    } catch (T) {
      A(T instanceof Error ? T.message : String(T));
    }
  }, [e == null ? void 0 : e.query, t]);
  G(() => {
    R();
  }, [R]);
  const L = _(() => {
    c(""), f(""), x("username-password"), v(""), p(!1), l(!0);
  }, []), u = _((T) => {
    c(T.id), f(T.name), x(T.kind), v(""), p(!0), l(!0);
  }, []), I = _(async () => {
    if (!o.trim()) {
      A("请输入凭证 ID");
      return;
    }
    if (!k.trim()) {
      A("请输入凭证内容");
      return;
    }
    S(!0), A(null);
    try {
      m ? await t.hubCommand("industrial/rotate-credential", { id: o, secret: k }) : await t.hubCommand("industrial/save-credential", { id: o, name: d || o, kind: y, secret: k }), l(!1), await R();
    } catch (T) {
      A(T instanceof Error ? T.message : String(T));
    } finally {
      S(!1);
    }
  }, [t, o, y, R, d, m, k]);
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ h(O, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ a(N.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ a($, { type: "primary", onClick: L, children: "新建凭证" })
    ] }),
    C && /* @__PURE__ */ a(ie, { type: "error", showIcon: !0, message: C }),
    /* @__PURE__ */ a(
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
            render: (T, X) => /* @__PURE__ */ a($, { size: "small", onClick: () => u(X), children: "轮换" })
          }
        ]
      }
    ),
    /* @__PURE__ */ a(
      me,
      {
        open: s,
        title: m ? "轮换凭证" : "新建凭证",
        okText: m ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: E,
        onCancel: () => l(!1),
        onOk: () => void I(),
        children: /* @__PURE__ */ h(O, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ a(
            K,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: o,
              disabled: m,
              onChange: (T) => c(T.target.value)
            }
          ),
          !m && /* @__PURE__ */ a(
            K,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: d,
              onChange: (T) => f(T.target.value)
            }
          ),
          /* @__PURE__ */ a(
            K,
            {
              "aria-label": "凭证内容",
              placeholder: m ? "新凭证内容" : "凭证内容",
              value: k,
              onChange: (T) => v(T.target.value)
            }
          ),
          C && /* @__PURE__ */ a(N.Text, { type: "danger", children: C })
        ] })
      }
    )
  ] });
}
const Qe = (e) => e === "Succeeded" || e === "Completed" ? "success" : e === "Failed" || e === "PartialFailure" ? "error" : e === "Running" || e === "Interrupted" ? "processing" : "default";
function Tr({ block: e, ctx: t }) {
  const [r, n] = b([]), [s, l] = b([]), [o, c] = b(null), [d, f] = b(null), [y, x] = b(!1), k = _(async () => {
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
  const v = _(async (m) => {
    x(!0), c(null), f(null);
    try {
      await t.hubCommand("industrial/retry-failed", { targetId: m.targetId }), f(`已提交目标 ${m.targetId} 的重试`), await k();
    } catch (p) {
      c(p instanceof Error ? p.message : String(p));
    } finally {
      x(!1);
    }
  }, [t, k]);
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ a(N.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    o && /* @__PURE__ */ a(ie, { type: "error", showIcon: !0, message: o }),
    d && /* @__PURE__ */ a(ie, { type: "success", showIcon: !0, message: d }),
    /* @__PURE__ */ a(
      ne,
      {
        rowKey: "id",
        dataSource: r,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (m) => /* @__PURE__ */ a(oe, { color: Qe(m), children: m }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (m) => m ?? "—" },
          { title: "错误", dataIndex: "error", render: (m) => m ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ a(
      ne,
      {
        rowKey: "id",
        dataSource: s,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (m) => /* @__PURE__ */ a(oe, { color: Qe(m), children: m }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (m, p) => p.diagnosis ? /* @__PURE__ */ h(O, { direction: "vertical", size: 0, children: [
              /* @__PURE__ */ a(N.Text, { children: p.diagnosis.message }),
              /* @__PURE__ */ a(N.Text, { type: "secondary", children: p.diagnosis.action })
            ] }) : p.error ?? "—"
          },
          {
            title: "操作",
            key: "actions",
            render: (m, p) => /* @__PURE__ */ a(
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
function Cr({ block: e, ctx: t }) {
  const [r, n] = b([]), [s, l] = b(null), o = _(async () => {
    try {
      n(cr(await t.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (d) {
      l(d instanceof Error ? d.message : String(d));
    }
  }, [e == null ? void 0 : e.query, t]);
  G(() => {
    o();
  }, [o]);
  const c = (d) => /* @__PURE__ */ a(
    ne,
    {
      rowKey: "id",
      dataSource: d.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (f) => /* @__PURE__ */ a(oe, { children: f }) },
        { title: "记录", dataIndex: "records" },
        {
          title: "接受/拒绝/警告",
          key: "counts",
          render: (f, y) => `${y.accepted} / ${y.rejected} / ${y.warnings}`
        },
        {
          title: "原因与下一步",
          key: "diagnosis",
          render: (f, y) => y.diagnosis ? /* @__PURE__ */ h(O, { direction: "vertical", size: 0, children: [
            /* @__PURE__ */ a(N.Text, { children: y.diagnosis.message }),
            /* @__PURE__ */ a(N.Text, { type: "secondary", children: y.diagnosis.action }),
            y.error && /* @__PURE__ */ a(N.Text, { type: "secondary", children: y.error })
          ] }) : y.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ h(O, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ a(N.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    s && /* @__PURE__ */ a(ie, { type: "error", showIcon: !0, message: s }),
    /* @__PURE__ */ a(
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
const Nr = `
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
function Er(e) {
  mt(e);
  const t = typeof document > "u" ? null : document.createElement("style");
  t && (t.dataset.industrialDesigner = "true", t.textContent = Nr, document.head.appendChild(t));
  const r = ({ ctx: l }) => Ce(vr, { ctx: l });
  r.displayName = "IndustrialWorkflowCanvas";
  const n = (l, o) => {
    const c = ({ block: d, ctx: f }) => Ce(o, { block: d, ctx: f });
    return c.displayName = l, e.blocks.register(l, c);
  }, s = [
    e.blocks.register("industrial-workflow-canvas", r),
    n("industrial-workflow-list", br),
    n("industrial-resource-console", kr),
    n("industrial-credential-console", Sr),
    n("industrial-execution-center", Tr),
    n("industrial-file-details", Cr)
  ];
  return () => {
    for (const l of s) l == null || l();
    t == null || t.remove();
  };
}
export {
  Er as default,
  Er as register
};
