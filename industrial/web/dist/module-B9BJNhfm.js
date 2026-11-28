let Ur = null;
function dv(e) {
  Ur = e;
}
function De() {
  if (!Ur && typeof globalThis < "u" && (Ur = globalThis.__CORDIS_CONSOLE ?? null), !Ur)
    throw new Error("[industrial-designer] console facade is not available");
  return Ur;
}
const f = (...e) => De().jsxRuntime.jsx(...e), S = (...e) => De().jsxRuntime.jsxs(...e), br = (e) => De().jsxRuntime.jsx(De().jsxRuntime.Fragment, e), zo = (...e) => De().React.createElement(...e), L = (...e) => De().React.useCallback(...e), we = (...e) => De().React.useEffect(...e), Qo = (...e) => De().React.useMemo(...e), Ae = (...e) => De().React.useRef(...e), q = (...e) => De().React.useState(...e), ge = (e) => {
  const r = (t) => {
    const n = e.split(".").reduce((a, i) => a == null ? void 0 : a[i], De().antd);
    if (!n) throw new Error(`[industrial-designer] antd.${e} is unavailable`);
    return zo(n, t ?? {});
  };
  return r.displayName = `facade.${e}`, r;
}, Se = ge("Alert"), j = ge("Button"), _u = ge("Divider"), fv = ge("Drawer"), Mh = ge("Empty"), Bo = Object.assign(ge("Form"), {
  Item: ge("Form.Item"),
  useForm: (...e) => De().antd.Form.useForm(...e)
}), K = Object.assign(ge("Input"), {
  Number: ge("InputNumber"),
  TextArea: ge("Input.TextArea"),
  Password: ge("Input.Password")
}), ht = ge("InputNumber"), hv = Object.assign(ge("Layout"), {
  Header: ge("Layout.Header"),
  Content: ge("Layout.Content")
}), Er = ge("Modal"), Ke = ge("Select"), Y = ge("Space"), $h = ge("Spin"), Xr = ge("Switch"), Ne = ge("Table"), Be = ge("Tag"), B = Object.assign(ge("Typography"), {
  Title: ge("Typography.Title"),
  Text: ge("Typography.Text"),
  Paragraph: ge("Typography.Paragraph")
}), pv = new Proxy({}, {
  get: (e, r) => (...t) => {
    var a;
    const n = (a = De().antd.message) == null ? void 0 : a[r];
    if (typeof n != "function") throw new Error(`[industrial-designer] message.${r} unavailable`);
    return n(...t);
  }
}), Lh = 148, jh = 46, wu = {
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
}, Iu = (e) => wu[e] ?? wu[e.split(".")[0]] ?? "#4d6bfe";
function vv(e) {
  return `e:${e.source}:${e.sourcePort}>${e.target}:${e.targetPort}`;
}
function gv(e, r = /* @__PURE__ */ new Map(), t) {
  return {
    nodes: e.nodes.map((n) => {
      const a = r.get(n.type), i = (a == null ? void 0 : a.category) ?? "General", s = t === n.id;
      return {
        id: n.id,
        data: {
          label: (a == null ? void 0 : a.name) ?? n.type,
          nodeTypeId: n.type,
          category: i,
          icon: (a == null ? void 0 : a.icon) ?? "AppstoreOutlined",
          config: { ...n.config }
        },
        style: {
          x: n.x,
          y: n.y,
          fill: Iu(i),
          stroke: s ? "#111111" : Iu(i),
          lineWidth: s ? 3 : 1.5,
          radius: 10,
          port: !0,
          ports: [
            ...((a == null ? void 0 : a.inputs) ?? []).map((o, u, c) => ({
              key: `in:${o.name}`,
              placement: [0, (u + 1) / (c.length + 1)],
              r: 5,
              stroke: o.required ? "#fa541c" : "#8a93a6",
              fill: "#fff"
            })),
            ...((a == null ? void 0 : a.outputs) ?? []).map((o, u, c) => ({
              key: `out:${o.name}`,
              placement: [1, (u + 1) / (c.length + 1)],
              r: 5,
              stroke: o.type === "Control" ? "#52c41a" : "#1677ff",
              fill: "#fff"
            }))
          ]
        }
      };
    }),
    edges: e.edges.map((n) => ({
      id: vv(n),
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
const Kr = 20;
function Fh(e) {
  const r = Object.values(e), t = /* @__PURE__ */ new Set();
  for (let n = 0; n < r.length; n++)
    for (let a = n + 1; a < r.length; a++)
      r[n].x === r[a].x && t.add(`x=${r[n].x}`), r[n].y === r[a].y && t.add(`y=${r[n].y}`);
  return [...t];
}
function Uo(e, r, t) {
  const n = t && t.length > 0 ? new Set(t) : null, a = {};
  for (const i of e.nodes) {
    if (!r.has(i.id) || n && !n.has(i.id)) continue;
    const s = i.style.x, o = i.style.y;
    typeof s == "number" && typeof o == "number" && (a[i.id] = {
      x: Math.round(s / Kr) * Kr,
      y: Math.round(o / Kr) * Kr
    });
  }
  return a;
}
function yv(e, r) {
  return {
    container: r,
    autoFit: "view",
    data: e,
    node: {
      type: "rect",
      style: {
        labelText: (t) => {
          var n;
          return ((n = t == null ? void 0 : t.data) == null ? void 0 : n.label) ?? "";
        },
        labelPlacement: "bottom",
        size: [Lh, jh],
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
        labelText: (t) => {
          var n;
          return ((n = t == null ? void 0 : t.data) == null ? void 0 : n.label) ?? "";
        },
        labelFontSize: 10,
        stroke: "#8a93a6",
        endArrow: !0,
        // Orthogonal routing keeps edges out of node bodies and reduces
        // crossings in left-to-right industrial pipelines.
        router: { type: "orth" }
      }
    },
    plugins: [
      { type: "grid-line", size: Kr, stroke: "#e8edf4" },
      { type: "minimap", size: [180, 110], position: "right-bottom" },
      { type: "snapline", lineWidth: 1, stroke: "#4d6bfe" }
    ],
    behaviors: Gh()
  };
}
const mv = "__industrial_drag_target__", bv = "__industrial_connect_source__", Gt = (e, r) => {
  var t;
  return (((t = e.isPanModifier) == null ? void 0 : t.call(e)) ?? !1) || r.button === 1 || r.buttons === 4 || r.shiftKey === !0;
}, qu = (e, r) => {
  var l, d;
  if (r.targetType !== "node" || !((l = r.target) != null && l.id)) return !1;
  const t = ((d = e.gestureOrigin) == null ? void 0 : d.call(e)) ?? r.client ?? r.canvas;
  if (!t || !e.graph) return !1;
  const n = String(r.target.id), a = e.graph.getNodeData().find((h) => String(h.id) === n), i = a == null ? void 0 : a.style;
  if (typeof (i == null ? void 0 : i.x) != "number" || typeof i.y != "number") return !1;
  const [s, o] = e.graph.getCanvasByClient([t.x, t.y]), u = Math.abs(s - i.x), c = Math.abs(o - i.y);
  return Math.abs(u - Lh / 2) <= 14 && c <= jh / 2 + 10;
};
function Gh(e = {}) {
  const r = e.connectionState ?? { armed: !1 }, t = (n) => {
    var c, l, d;
    const a = e.graph;
    if (!a || !e.onPositions) return;
    const i = Array.isArray(n) ? n : n ? [String(n)] : void 0, s = a.getNodeData().map((h) => ({
      id: String(h.id),
      data: h.data ?? {},
      style: h.style ?? {}
    })), o = Uo(
      { nodes: s },
      ((c = e.allowedIds) == null ? void 0 : c.call(e)) ?? /* @__PURE__ */ new Set(),
      i
    );
    e.onPositions(o);
    const u = Uo(
      { nodes: s },
      ((l = e.allowedIds) == null ? void 0 : l.call(e)) ?? /* @__PURE__ */ new Set()
    );
    (d = e.onAlign) == null || d.call(e, Fh(u));
  };
  return [
    "zoom-canvas",
    {
      type: "click-select",
      multiple: !0,
      trigger: ["shift"],
      state: "__industrial_selected__"
    },
    {
      type: "brush-select",
      trigger: ["shift"],
      state: "__industrial_selected__",
      onSelect: (n) => {
        var a;
        return (a = e.onSelectIds) == null ? void 0 : a.call(e, Object.keys(n ?? {}));
      }
    },
    {
      type: "drag-canvas",
      enable: (n) => n.targetType === "canvas" || Gt(e, n),
      range: 0.9
    },
    {
      type: "drag-element",
      state: mv,
      enable: (n) => (n.targetType === "node" || n.targetType === "combo") && !qu(e, n) && !Gt(e, n),
      onFinish: t
    },
    {
      type: "drag-element",
      state: "__industrial_selected__",
      enable: (n) => n.targetType === "node" && n.shiftKey === !0,
      onFinish: t
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: bv,
      enable: (n) => Gt(e, n) ? (r.armed = !1, !1) : r.armed ? n.targetType === "node" || n.targetType === "combo" : qu(e, n) ? (r.armed = !0, !0) : !1,
      onCreate: (n) => {
        var o;
        r.armed = !1;
        const a = String(n.source ?? ""), i = String(n.target ?? "");
        if (!a || !i || a === i) return;
        const s = (o = e.onCreateEdge) == null ? void 0 : o.call(e, a, i);
        if (s)
          return {
            ...n,
            sourcePort: `out:${s.sourcePort}`,
            targetPort: `in:${s.targetPort}`
          };
      }
    }
  ];
}
const _v = (e) => {
  var t;
  const r = e;
  return (r == null ? void 0 : r.targetType) === "node" ? (t = r.target) == null ? void 0 : t.id : void 0;
};
function wv(e) {
  const {
    definition: r,
    catalog: t,
    selectedId: n,
    onSelect: a,
    onDropNode: i,
    onCreateEdge: s,
    onPositions: o,
    onSelectIds: u,
    onGraphReady: c,
    onSelectEdge: l,
    onDeleteNodeById: d,
    onDuplicateNode: h,
    onDeleteEdgeById: v
  } = e, g = Ae(null), p = Ae(null), m = Ae(!1), I = Ae(!1), k = Ae({ armed: !1 }), b = Ae(null), [A, M] = q(null), [y, x] = q([]), O = Ae({ onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o });
  O.current = { onSelect: a, onDropNode: i, onCreateEdge: s, onPositions: o, onSelectEdge: l };
  const V = Qo(
    () => gv(r, t, n),
    [r, t, n]
  ), F = Ae(V);
  F.current = V;
  const R = Ae(/* @__PURE__ */ new Set());
  R.current = new Set(r.nodes.map((z) => z.id)), we(() => {
    const z = (de) => {
      de.code === "Space" && !de.repeat && (I.current = !0);
    }, ae = (de) => {
      de.code === "Space" && (I.current = !1);
    }, te = () => {
      I.current = !1;
    };
    return window.addEventListener("keydown", z), window.addEventListener("keyup", ae), window.addEventListener("blur", te), () => {
      window.removeEventListener("keydown", z), window.removeEventListener("keyup", ae), window.removeEventListener("blur", te);
    };
  }, []), we(() => {
    const z = g.current;
    if (!z || m.current) return;
    let ae = !1;
    return import("./esm-Gxy29MLw.js").then(({ Graph: te }) => {
      if (ae || !g.current || m.current) return;
      const de = yv(F.current, z), U = new te({
        ...de,
        behaviors: Gh({
          graph: {
            getNodeData: () => {
              var Q;
              return ((Q = p.current) == null ? void 0 : Q.getNodeData()) ?? [];
            },
            getCanvasByClient: (Q) => {
              var T;
              const E = ((T = p.current) == null ? void 0 : T.getCanvasByClient(Q)) ?? Q;
              return [E[0], E[1]];
            }
          },
          isPanModifier: () => I.current,
          gestureOrigin: () => b.current,
          connectionState: k.current,
          allowedIds: () => R.current,
          onPositions: (Q) => O.current.onPositions(Q),
          onAlign: (Q) => x((E) => Q.length > 0 ? Q : E),
          onSelectIds: (Q) => u == null ? void 0 : u(Q),
          onCreateEdge: (Q, E) => O.current.onCreateEdge(Q, E)
        })
      });
      U.on("node:click", (Q) => {
        const E = _v(Q);
        E && O.current.onSelect(E);
      }), U.on("edge:click", (Q) => {
        var T, $, pe;
        const E = Q;
        (pe = ($ = O.current).onSelectEdge) == null || pe.call($, ((T = E.target) == null ? void 0 : T.id) ?? null);
      }), U.on("node:contextmenu", (Q) => {
        var T, $, pe;
        const E = Q;
        (T = E.target) != null && T.id && M({
          x: (($ = E.client) == null ? void 0 : $.x) ?? 0,
          y: ((pe = E.client) == null ? void 0 : pe.y) ?? 0,
          type: "node",
          id: E.target.id
        });
      }), U.on("edge:contextmenu", (Q) => {
        var T, $, pe;
        const E = Q;
        (T = E.target) != null && T.id && M({
          x: (($ = E.client) == null ? void 0 : $.x) ?? 0,
          y: ((pe = E.client) == null ? void 0 : pe.y) ?? 0,
          type: "edge",
          id: E.target.id
        });
      }), U.on("canvas:click", () => O.current.onSelect(null)), U.on("pointerdown", (Q) => {
        const E = Q;
        b.current = E.client ? { x: E.client.x, y: E.client.y } : null;
      }), U.on("pointerup", () => {
        queueMicrotask(() => {
          k.current.armed = !1, b.current = null;
        });
      }), p.current = U, z.__industrialGraph = U, c == null || c(U), m.current = !0, U.render();
    }), () => {
      ae = !0;
    };
  }, []), we(() => {
    const z = p.current;
    !z || !m.current || (z.setData(V), z.draw());
  }, [V]), we(() => () => {
    var z;
    (z = p.current) == null || z.destroy(), p.current = null, m.current = !1;
  }, []);
  const G = () => {
    const z = p.current;
    if (!z) return;
    const ae = z.getNodeData().map((U) => ({
      id: String(U.id),
      data: U.data ?? {},
      style: U.style ?? {}
    })), te = Uo({ nodes: ae }, R.current);
    O.current.onPositions(te);
    const de = Fh(te);
    x(de.length > 0 ? de : ["已对齐到网格"]);
  };
  return /* @__PURE__ */ f("div", { className: "industrial-canvas-wrap", children: /* @__PURE__ */ S("div", { className: "industrial-graph", children: [
    /* @__PURE__ */ f(
      "div",
      {
        ref: g,
        className: "industrial-graph-host",
        onDragOver: (z) => z.preventDefault(),
        onDrop: (z) => {
          z.preventDefault();
          const ae = z.dataTransfer.getData("application/x-industrial-node") || z.dataTransfer.getData("text/plain");
          if (!ae) return;
          const te = p.current, de = g.current;
          if (!te || !de) return;
          const U = de.getBoundingClientRect(), Q = te.getCanvasByViewport([
            z.clientX - U.left,
            z.clientY - U.top
          ]);
          O.current.onDropNode(ae, Q[0], Q[1]);
        },
        "data-testid": "industrial-graph"
      }
    ),
    A && /* @__PURE__ */ f("div", { className: "industrial-context-menu", style: { left: A.x, top: A.y }, children: A.type === "node" ? /* @__PURE__ */ S(br, { children: [
      /* @__PURE__ */ f("button", { onClick: () => {
        h == null || h(A.id), M(null);
      }, children: "复制节点" }),
      /* @__PURE__ */ f("button", { onClick: () => {
        d == null || d(A.id), M(null);
      }, children: "删除节点" })
    ] }) : /* @__PURE__ */ S(br, { children: [
      /* @__PURE__ */ f("button", { onClick: () => {
        l == null || l(A.id), M(null);
      }, children: "编辑连线" }),
      /* @__PURE__ */ f("button", { onClick: () => {
        v == null || v(A.id), M(null);
      }, children: "删除连线" })
    ] }) }),
    y.length > 0 && /* @__PURE__ */ S("div", { className: "industrial-align-guide", "data-testid": "align-guide", children: [
      "已对齐 ",
      y.join(" · ")
    ] }),
    /* @__PURE__ */ S("div", { className: "industrial-graph-hint", children: [
      "Shift+拖拽框选 · 拖节点移动 · 端口拖拽连线 · 滚轮缩放 · 空格/中键平移",
      /* @__PURE__ */ f("button", { type: "button", "data-testid": "designer-snap", onClick: G, children: "对齐到网格" })
    ] })
  ] }) });
}
function Iv({ descriptors: e, loading: r, error: t, onAdd: n }) {
  const a = /* @__PURE__ */ new Map();
  for (const s of e) {
    const o = s.category.split(".")[0] || "General", u = a.get(o) ?? [];
    u.push(s), a.set(o, u);
  }
  const i = (s, o) => {
    s.dataTransfer.setData("application/x-industrial-node", o.nodeTypeId), s.dataTransfer.setData("text/plain", o.nodeTypeId), s.dataTransfer.effectAllowed = "copy";
  };
  return /* @__PURE__ */ S("div", { className: "industrial-palette-content", children: [
    /* @__PURE__ */ f(B.Title, { level: 5, style: { marginTop: 0 }, children: "节点面板" }),
    /* @__PURE__ */ f(B.Text, { type: "secondary", style: { fontSize: 12 }, children: "拖入画布 · 连线按端口类型校验" }),
    r && /* @__PURE__ */ f($h, { size: "small", style: { display: "block", marginTop: 16 } }),
    t && /* @__PURE__ */ f(B.Text, { type: "danger", style: { display: "block", marginTop: 12 }, children: t }),
    !r && !t && e.length === 0 && /* @__PURE__ */ f(Mh, { description: "industrial/nodes 暂无节点描述", style: { marginTop: 24 } }),
    Array.from(a.entries()).map(([s, o]) => /* @__PURE__ */ S("section", { className: "industrial-palette-group", children: [
      /* @__PURE__ */ f(B.Text, { strong: !0, style: { fontSize: 12 }, children: s }),
      o.map((u) => /* @__PURE__ */ S(
        "div",
        {
          className: "industrial-palette-node",
          draggable: !0,
          role: "button",
          tabIndex: 0,
          onDragStart: (c) => i(c, u),
          onClick: () => n == null ? void 0 : n(u.nodeTypeId),
          onKeyDown: (c) => {
            (c.key === "Enter" || c.key === " ") && (n == null || n(u.nodeTypeId));
          },
          title: u.description || u.nodeTypeId,
          children: [
            /* @__PURE__ */ f("div", { className: "industrial-palette-node-title", children: u.name }),
            /* @__PURE__ */ S("div", { className: "industrial-palette-node-meta", children: [
              /* @__PURE__ */ f(Be, { bordered: !1, color: "blue", children: u.version }),
              /* @__PURE__ */ f(B.Text, { type: "secondary", style: { fontSize: 11 }, children: u.nodeTypeId })
            ] }),
            /* @__PURE__ */ S("div", { className: "industrial-palette-node-ports", children: [
              u.inputs.map((c) => /* @__PURE__ */ S(Be, { bordered: !1, color: "default", children: [
                "in ",
                c.name,
                ": ",
                c.type
              ] }, `in-${c.name}`)),
              u.outputs.map((c) => /* @__PURE__ */ S(Be, { bordered: !1, color: "green", children: [
                "out ",
                c.name,
                ": ",
                c.type
              ] }, `out-${c.name}`))
            ] })
          ]
        },
        u.nodeTypeId
      ))
    ] }, s))
  ] });
}
const pt = (e = "daily-collection") => ({
  workflowId: e,
  name: e,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {}
});
function qv(e) {
  const r = {};
  for (const [t, n] of Object.entries(e.configurationSchema.properties ?? {}))
    n.default !== void 0 && (r[t] = n.default);
  return r;
}
function zt(e, r, t = 0, n = 0) {
  return { id: r, type: e.nodeTypeId, config: qv(e), x: t, y: n };
}
function ur(e, r) {
  return e.get(r);
}
function vt(e, r, t) {
  return e[t].find((n) => n.name === r);
}
function Jo(e, r) {
  return e !== "Unknown" && r !== "Unknown" && e === r;
}
function Cu(e, r) {
  for (const t of e.outputs)
    for (const n of r.inputs)
      if (Jo(t.type, n.type))
        return { sourcePort: t.name, targetPort: n.name };
  return null;
}
const Oe = (e, r, t) => ({ path: e, code: r, message: t });
function Cv(e, r) {
  return e.source === r.source && e.sourcePort === r.sourcePort && e.target === r.target && e.targetPort === r.targetPort;
}
function Ev(e, r, t) {
  const n = `edges.${e.edges.length}`, a = e.nodes.find((l) => l.id === r.source), i = e.nodes.find((l) => l.id === r.target);
  if (!a || !i)
    return { definition: e, issue: Oe(n, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  if (e.edges.some((l) => Cv(l, r)))
    return { definition: e };
  const s = ur(t, a.type), o = ur(t, i.type);
  if (!s || !o)
    return { definition: e, issue: Oe(n, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  const u = vt(s, r.sourcePort, "outputs"), c = vt(o, r.targetPort, "inputs");
  return !u || !c ? { definition: e, issue: Oe(n, "PORT_MISSING", "declared port does not exist") } : Jo(u.type, c.type) ? { definition: { ...e, edges: [...e.edges, r] } } : {
    definition: e,
    issue: Oe(
      n,
      "PORT_TYPE_INCOMPATIBLE",
      `${u.type} → ${c.type} is not a legal connection`
    )
  };
}
function Bt(e, r) {
  return {
    ...e,
    nodes: e.nodes.filter((t) => t.id !== r),
    edges: e.edges.filter((t) => t.source !== r && t.target !== r)
  };
}
function Eu(e, r) {
  return { ...e, edges: e.edges.filter((t, n) => n !== r) };
}
function Sv(e, r) {
  const t = { ...e };
  for (const [n, a] of Object.entries(r))
    a === void 0 ? delete t[n] : t[n] = a;
  return t;
}
function Rv(e, r, t) {
  return {
    ...e,
    nodes: e.nodes.map((n) => n.id === r ? { ...n, config: t } : n)
  };
}
function Tv(e, r) {
  return {
    ...e,
    nodes: e.nodes.map((t) => {
      const n = r[t.id];
      return n ? { ...t, x: n.x, y: n.y } : t;
    }),
    // One canonical layout field: the domain ships positions with the
    // definition, never as a separate browser-only cache.
    layout: { ...e.layout, positions: { ...r } }
  };
}
function xv(e) {
  var i;
  const r = /* @__PURE__ */ new Map();
  for (const s of e.nodes) r.set(s.id, []);
  for (const s of e.edges)
    (i = r.get(s.source)) == null || i.push(s.target);
  const t = /* @__PURE__ */ new Map(), n = [], a = (s) => {
    t.set(s, 1), n.push(s);
    for (const o of r.get(s) ?? []) {
      if (t.get(o) === 1)
        return [...n.slice(n.indexOf(o)), o];
      if (t.get(o) !== 2) {
        const u = a(o);
        if (u) return u;
      }
    }
    return n.pop(), t.set(s, 2), null;
  };
  for (const s of e.nodes)
    if (t.get(s.id) !== 2) {
      const o = a(s.id);
      if (o) return o;
    }
  return null;
}
function Su(e, r, t) {
  const n = [];
  e.workflowId.trim() || n.push(Oe("workflow", "INVALID_IDENTITY", "workflowId is required")), e.version < 1 && n.push(Oe("workflow", "INVALID_VERSION", "version must be positive"));
  const a = /* @__PURE__ */ new Map();
  for (const o of e.nodes)
    a.has(o.id) && n.push(Oe(`nodes.${o.id}`, "DUPLICATE_NODE_ID", `duplicate node '${o.id}'`)), a.set(o.id, o);
  const i = /* @__PURE__ */ new Map();
  for (const o of e.nodes) {
    const u = ur(r, o.type);
    if (!u) {
      n.push(Oe(
        `nodes.${o.id}`,
        "UNKNOWN_NODE_TYPE",
        `node type '${o.type}' is not registered`
      ));
      continue;
    }
    i.set(o.id, u);
    const c = u.configurationSchema.required ?? [];
    for (const l of c) {
      const d = o.config[l];
      (d == null || d === "") && n.push(Oe(
        `nodes.${o.id}.config.${l}`,
        "REQUIRED_CONFIG_MISSING",
        `required configuration '${l}' is missing`
      ));
    }
    t && typeof o.config.resourceId == "string" && (t.has(o.config.resourceId) || n.push(Oe(
      `nodes.${o.id}.config.resourceId`,
      "RESOURCE_MISSING",
      `resource '${o.config.resourceId}' does not exist`
    )));
  }
  e.edges.forEach((o, u) => {
    const c = a.get(o.source), l = a.get(o.target);
    if (!c || !l) {
      n.push(Oe(`edges.${u}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const d = i.get(o.source), h = i.get(o.target);
    if (!d || !h) return;
    const v = vt(d, o.sourcePort, "outputs"), g = vt(h, o.targetPort, "inputs");
    if (!v || !g) {
      n.push(Oe(`edges.${u}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    Jo(v.type, g.type) || n.push(Oe(
      `edges.${u}`,
      "PORT_TYPE_INCOMPATIBLE",
      `${v.type} → ${g.type} is not a legal connection`
    ));
  });
  for (const o of e.nodes) {
    const u = i.get(o.id);
    if (u)
      for (const c of u.inputs.filter((l) => l.required))
        e.edges.some((l) => l.target === o.id && l.targetPort === c.name && a.has(l.source)) || n.push(Oe(
          `nodes.${o.id}.inputs.${c.name}`,
          "MISSING_INPUT",
          `required input '${c.name}' is not connected`
        ));
  }
  const s = xv(e);
  return s && n.push(Oe(`nodes.${s[0]}`, "GRAPH_CYCLE", `cycle: ${s.join(" → ")}`)), n;
}
const Ko = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {};
function kv(e) {
  const r = Ko(e.additionalProperties).enum;
  if (!Array.isArray(r)) return null;
  const t = r.filter((n) => typeof n == "string");
  return t.length > 0 ? t : null;
}
function zh(e) {
  return e.format === "resource" ? "resource" : Array.isArray(e.enum) && e.enum.length > 0 ? "enum" : e.type === "number" || e.type === "integer" ? "number" : e.type === "boolean" ? "boolean" : e.type === "array" && Ko(e.items).type === "string" ? "string-list" : e.type === "object" && Ko(e.additionalProperties).type === "string" ? "key-value" : e.type === "object" || e.type === "array" ? "json" : "text";
}
function Vo(e) {
  return e === null || typeof e != "object" || Array.isArray(e) ? [] : Object.entries(e).filter(([, r]) => r != null && typeof r != "object").map(([r, t]) => ({ key: r, value: String(t) }));
}
function Ru(e) {
  const r = {}, t = [];
  for (const n of e) {
    const a = n.key.trim();
    a && (Object.prototype.hasOwnProperty.call(r, a) && t.push(a), r[a] = n.value);
  }
  return { record: r, duplicateKeys: t };
}
function ot(e) {
  if (!Array.isArray(e)) return [];
  const r = /* @__PURE__ */ new Set(), t = [];
  for (const n of e) {
    if (typeof n != "string") continue;
    const a = n.trim();
    !a || r.has(a) || (r.add(a), t.push(a));
  }
  return t;
}
function Av(e, r) {
  const t = /* @__PURE__ */ new Set();
  for (const [n, a] of Object.entries(e))
    if (zh(a) === "key-value")
      for (const i of Vo(r[n]))
        i.key.trim() && t.add(i.key.trim());
  return [...t];
}
function Pv({
  value: e,
  onChange: r,
  valueOptions: t,
  keyTitle: n = "目标字段",
  valueTitle: a = "来源字段",
  testId: i
}) {
  const s = JSON.stringify(e ?? {}), [o, u] = q(() => Vo(e)), [c, l] = q(s);
  we(() => {
    s !== c && (u(Vo(e)), l(s));
  }, [s, c, e]);
  const d = (v) => {
    u(v), r == null || r(Ru(v).record);
  }, { duplicateKeys: h } = Ru(o);
  return /* @__PURE__ */ S("div", { "data-testid": i, className: "industrial-kv-editor", children: [
    o.map((v, g) => /* @__PURE__ */ S("div", { className: "industrial-kv-row", children: [
      /* @__PURE__ */ f(
        K,
        {
          "aria-label": `${n} ${g + 1}`,
          value: v.key,
          placeholder: n,
          onChange: (p) => d(o.map((m, I) => I === g ? { ...m, key: p.target.value } : m))
        }
      ),
      t && t.length > 0 ? /* @__PURE__ */ f(
        Ke,
        {
          "aria-label": `${a} ${g + 1}`,
          value: v.value || void 0,
          placeholder: a,
          options: t.map((p) => ({ label: p, value: p })),
          onChange: (p) => d(o.map((m, I) => I === g ? { ...m, value: p ?? "" } : m))
        }
      ) : /* @__PURE__ */ f(
        K,
        {
          "aria-label": `${a} ${g + 1}`,
          value: v.value,
          placeholder: a,
          onChange: (p) => d(o.map((m, I) => I === g ? { ...m, value: p.target.value } : m))
        }
      ),
      /* @__PURE__ */ f(
        j,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${g + 1}`,
          onClick: () => d(o.filter((p, m) => m !== g)),
          children: "删"
        }
      )
    ] }, g)),
    /* @__PURE__ */ f(
      j,
      {
        size: "small",
        type: "dashed",
        block: !0,
        onClick: () => d([...o, { key: "", value: "" }]),
        children: "添加字段"
      }
    ),
    h.length > 0 && /* @__PURE__ */ S(B.Text, { type: "danger", className: "industrial-editor-error", children: [
      "重复字段：",
      h.join("、")
    ] })
  ] });
}
function Ov({ value: e, onChange: r, suggestions: t = [], testId: n }) {
  const a = JSON.stringify(ot(e)), [i, s] = q(() => ot(e)), [o, u] = q(a);
  we(() => {
    a !== o && (s(ot(e)), u(a));
  }, [a, o, e]);
  const c = (l) => {
    s(l), r == null || r(ot(l));
  };
  return /* @__PURE__ */ S("div", { "data-testid": n, className: "industrial-list-editor", children: [
    /* @__PURE__ */ f("datalist", { id: `${n ?? "list"}-suggestions`, children: t.map((l) => /* @__PURE__ */ f("option", { value: l }, l)) }),
    i.map((l, d) => /* @__PURE__ */ S("div", { className: "industrial-list-row", children: [
      /* @__PURE__ */ f(
        K,
        {
          "aria-label": `字段 ${d + 1}`,
          value: l,
          list: `${n ?? "list"}-suggestions`,
          onChange: (h) => c(i.map((v, g) => g === d ? h.target.value : v))
        }
      ),
      /* @__PURE__ */ f(
        j,
        {
          size: "small",
          type: "text",
          danger: !0,
          "aria-label": `删除 ${d + 1}`,
          onClick: () => c(i.filter((h, v) => v !== d)),
          children: "删"
        }
      )
    ] }, d)),
    /* @__PURE__ */ f(j, { size: "small", type: "dashed", block: !0, onClick: () => c([...i, ""]), children: "添加字段" }),
    i.length === 0 && /* @__PURE__ */ f(B.Text, { type: "secondary", className: "industrial-editor-error", children: "尚未添加字段" })
  ] });
}
function Nv({ value: e, onChange: r }) {
  const t = e === void 0 ? "{}" : JSON.stringify(e, null, 2), [n, a] = q(t), [i, s] = q(null);
  we(() => {
    a(t), s(null);
  }, [t]);
  const o = (u) => {
    a(u);
    try {
      const c = JSON.parse(u);
      s(null), r == null || r(c);
    } catch (c) {
      s(c.message);
    }
  };
  return /* @__PURE__ */ S("div", { children: [
    /* @__PURE__ */ f(K.TextArea, { rows: 5, value: n, onChange: (u) => o(u.target.value) }),
    i && /* @__PURE__ */ f(B.Text, { type: "danger", style: { fontSize: 11 }, children: i })
  ] });
}
function Dv({ value: e, onChange: r, resources: t, resourceType: n, label: a, onCreate: i }) {
  const s = t.filter((u) => u.enabled !== !1 && (!n || u.type === n)).map((u) => ({
    value: u.id,
    label: `${u.name} (${u.type})`
  })), o = t.find((u) => u.id === e);
  return /* @__PURE__ */ S("div", { children: [
    /* @__PURE__ */ S(
      "select",
      {
        "aria-label": a,
        value: e ?? "",
        onChange: (u) => r == null ? void 0 : r(u.target.value || void 0),
        children: [
          /* @__PURE__ */ f("option", { value: "", children: "未选择" }),
          s.map((u) => /* @__PURE__ */ f("option", { value: u.value, children: u.label }, u.value))
        ]
      }
    ),
    o && /* @__PURE__ */ S(
      B.Text,
      {
        type: o.enabled === !1 ? "danger" : "success",
        "data-testid": "resource-picker-status",
        style: { fontSize: 11, display: "block" },
        children: [
          o.name,
          " · ",
          o.type,
          " · ",
          o.enabled === !1 ? "已停用" : "已启用",
          o.credentialId ? ` · 凭证 ${o.credentialName ?? o.credentialId}` : ""
        ]
      }
    ),
    /* @__PURE__ */ f(j, { type: "link", size: "small", "data-testid": "resource-picker-create", onClick: i, children: "新建资源" })
  ] });
}
function Mv({ name: e, schema: r, required: t, resources: n, resourceType: a, properties: i, config: s, onCreateResource: o }) {
  let u, c = "value";
  switch (zh(r)) {
    case "resource":
      u = /* @__PURE__ */ f(
        Dv,
        {
          resources: n,
          resourceType: a,
          label: String(r.title ?? e),
          onCreate: o
        }
      );
      break;
    case "enum":
      u = /* @__PURE__ */ f(
        Ke,
        {
          allowClear: !0,
          options: (r.enum ?? []).map((l) => ({ label: String(l), value: l }))
        }
      );
      break;
    case "number":
      u = /* @__PURE__ */ f(ht, { style: { width: "100%" } });
      break;
    case "boolean":
      u = /* @__PURE__ */ f(Xr, {}), c = "checked";
      break;
    case "key-value":
      u = /* @__PURE__ */ f(
        Pv,
        {
          testId: `field-${e}`,
          valueOptions: kv(r) ?? void 0,
          keyTitle: e === "mapping" ? "目标字段" : "字段名",
          valueTitle: e === "mapping" ? "来源字段" : "类型"
        }
      );
      break;
    case "string-list":
      u = /* @__PURE__ */ f(Ov, { testId: `field-${e}`, suggestions: Av(i, s) });
      break;
    case "json":
      u = /* @__PURE__ */ f(Nv, {});
      break;
    default:
      u = /* @__PURE__ */ f(K, { placeholder: r.description });
      break;
  }
  return /* @__PURE__ */ f(
    Bo.Item,
    {
      name: e,
      label: r.title ?? e,
      tooltip: r.description,
      extra: (() => {
        const l = r.examples, d = [
          r.description,
          r.default === void 0 ? null : `默认：${JSON.stringify(r.default)}`,
          Array.isArray(l) && l.length > 0 ? `示例：${l.map((h) => String(h)).join("、")}` : null
        ].filter((h) => typeof h == "string" && h.length > 0);
        return d.length > 0 ? /* @__PURE__ */ f("span", { "data-testid": `field-hint-${e}`, children: d.join(" · ") }) : void 0;
      })(),
      valuePropName: c,
      rules: t && c === "value" ? [{ required: !0, message: `请填写 ${r.title ?? e}` }] : [],
      children: u
    },
    e
  );
}
function $v(e) {
  const {
    node: r,
    descriptor: t,
    nodes: n,
    edges: a,
    catalog: i,
    resources: s,
    issues: o,
    onApplyConfig: u,
    onDeleteNode: c,
    onDeleteEdge: l,
    onCreateEdge: d,
    onUpdateEdge: h,
    onSelectNode: v,
    onCreateResource: g,
    selectedEdgeId: p,
    sampleLines: m
  } = e, [I] = Bo.useForm(), [k, b] = q("{}"), [A, M] = q(null), [y, x] = q(), [O, V] = q(), [F, R] = q(!1), [G, H] = q(!1);
  we(() => {
    I.setFieldsValue((r == null ? void 0 : r.config) ?? {}), b(JSON.stringify((r == null ? void 0 : r.config) ?? {}, null, 2)), M(null);
  }, [I, r == null ? void 0 : r.id, r == null ? void 0 : r.config]);
  const z = new Set((t == null ? void 0 : t.configurationSchema.required) ?? []), ae = Object.entries((t == null ? void 0 : t.configurationSchema.properties) ?? {}), te = () => {
    if (ae.length > 0) {
      I.validateFields().then((E) => u(Sv((r == null ? void 0 : r.config) ?? {}, E)));
      return;
    }
    try {
      const E = JSON.parse(k);
      if (E === null || typeof E != "object" || Array.isArray(E))
        throw new Error("配置必须是 JSON 对象");
      M(null), u(E);
    } catch (E) {
      M(E.message);
    }
  }, U = (r && !F ? a.map((E, T) => ({ edge: E, index: T })).filter(({ edge: E }) => E.source === r.id || E.target === r.id) : a.map((E, T) => ({ edge: E, index: T }))).map(({ edge: E, index: T }) => ({
    ...E,
    index: T,
    key: `${T}:${E.source}->${E.target}`
  })), Q = [
    {
      title: "起点节点",
      key: "reconnect-source",
      width: 108,
      render: (E, T) => /* @__PURE__ */ f(
        "select",
        {
          "aria-label": `连线 ${T.index} 起点节点`,
          value: T.source,
          onChange: ($) => h(T.index, { ...T, source: $.target.value }),
          children: n.map(($) => /* @__PURE__ */ f("option", { value: $.id, children: $.id }, $.id))
        }
      )
    },
    {
      title: "终点节点",
      key: "reconnect-target",
      width: 108,
      render: (E, T) => /* @__PURE__ */ f(
        "select",
        {
          "aria-label": `连线 ${T.index} 终点节点`,
          value: T.target,
          onChange: ($) => h(T.index, { ...T, target: $.target.value }),
          children: n.map(($) => /* @__PURE__ */ f("option", { value: $.id, children: $.id }, $.id))
        }
      )
    },
    {
      title: "端口",
      key: "ports",
      render: (E, T) => {
        const $ = i.get(T.source), pe = i.get(T.target);
        return /* @__PURE__ */ S("div", { className: "industrial-edge-ports", children: [
          /* @__PURE__ */ f(
            Ke,
            {
              size: "small",
              value: T.sourcePort,
              style: { width: 110 },
              onChange: (Ie) => h(T.index, { ...T, sourcePort: Ie }),
              options: (($ == null ? void 0 : $.outputs) ?? []).map((Ie) => ({ label: `${Ie.name} (${Ie.type})`, value: Ie.name }))
            }
          ),
          /* @__PURE__ */ f(
            Ke,
            {
              size: "small",
              value: T.targetPort,
              style: { width: 110 },
              onChange: (Ie) => h(T.index, { ...T, targetPort: Ie }),
              options: ((pe == null ? void 0 : pe.inputs) ?? []).map((Ie) => ({ label: `${Ie.name} (${Ie.type})`, value: Ie.name }))
            }
          )
        ] });
      }
    },
    {
      title: "",
      key: "actions",
      width: 44,
      render: (E, T) => /* @__PURE__ */ f(j, { danger: !0, size: "small", type: "text", onClick: () => l(T.index), children: "删" })
    }
  ];
  return /* @__PURE__ */ S("div", { className: "industrial-properties-body", children: [
    !r && /* @__PURE__ */ f(Mh, { description: "在画布中选中节点以编辑配置", style: { marginTop: 24 } }),
    r && /* @__PURE__ */ S(br, { children: [
      /* @__PURE__ */ f(B.Title, { level: 5, style: { margin: 0 }, children: (t == null ? void 0 : t.name) ?? r.type }),
      /* @__PURE__ */ f(B.Text, { type: "secondary", style: { fontSize: 11 }, children: r.id }),
      /* @__PURE__ */ f(B.Paragraph, { type: "secondary", style: { fontSize: 12, marginTop: 6 }, children: (t == null ? void 0 : t.description) ?? "请按业务含义配置该节点。" }),
      /* @__PURE__ */ S(Y, { wrap: !0, size: 4, style: { marginBottom: 8 }, children: [
        /* @__PURE__ */ f(Be, { color: "blue", children: (t == null ? void 0 : t.category) ?? "采集节点" }),
        /* @__PURE__ */ S(Be, { children: [
          "版本 ",
          (t == null ? void 0 : t.version) ?? "1.0.0"
        ] }),
        t == null ? void 0 : t.inputs.map((E) => /* @__PURE__ */ S(Be, { color: E.required ? "red" : "default", children: [
          "输入 ",
          E.name,
          " · ",
          E.type
        ] }, `in-${E.name}`)),
        t == null ? void 0 : t.outputs.map((E) => /* @__PURE__ */ S(Be, { color: "green", children: [
          "输出 ",
          E.name,
          " · ",
          E.type
        ] }, `out-${E.name}`))
      ] }),
      (r.type.includes("reader") || r.type.includes("mapping") || r.type.includes("source") || r.type.includes("filesystem")) && m && m.length > 0 && /* @__PURE__ */ S(br, { children: [
        /* @__PURE__ */ f(B.Text, { strong: !0, style: { fontSize: 12 }, children: "样本预览" }),
        /* @__PURE__ */ f("pre", { className: "industrial-sample-preview", children: m.join(`
`) })
      ] }),
      /* @__PURE__ */ f(Bo, { form: I, layout: "vertical", size: "small", style: { marginTop: 12 }, children: ae.map(([E, T]) => /* @__PURE__ */ f(
        Mv,
        {
          name: E,
          schema: T,
          required: z.has(E),
          resources: s,
          resourceType: typeof (t == null ? void 0 : t.configurationSchema.resourceType) == "string" ? t.configurationSchema.resourceType : void 0,
          properties: (t == null ? void 0 : t.configurationSchema.properties) ?? {},
          config: (r == null ? void 0 : r.config) ?? {},
          onCreateResource: g
        },
        E
      )) }),
      ae.length === 0 && /* @__PURE__ */ f(B.Text, { type: "secondary", style: { fontSize: 12 }, children: "此节点无需填写业务参数；保留默认配置即可。" }),
      /* @__PURE__ */ f(j, { type: "link", size: "small", onClick: () => H((E) => !E), children: G ? "收起高级配置" : "高级配置" }),
      G && /* @__PURE__ */ S(br, { children: [
        /* @__PURE__ */ f(B.Text, { type: "secondary", style: { fontSize: 12 }, children: "高级配置直接编辑节点 JSON，仅在需要复杂表达式时使用。" }),
        /* @__PURE__ */ f(
          K.TextArea,
          {
            rows: 8,
            value: k,
            style: { marginTop: 6 },
            onChange: (E) => b(E.target.value)
          }
        ),
        A && /* @__PURE__ */ f(B.Text, { type: "danger", style: { fontSize: 12 }, children: A }),
        (t == null ? void 0 : t.activityType) && /* @__PURE__ */ S(B.Text, { type: "secondary", style: { fontSize: 11, display: "block" }, children: [
          "运行时 Activity：",
          t.activityType
        ] })
      ] }),
      /* @__PURE__ */ f(j, { type: "primary", block: !0, onClick: te, children: "应用配置" }),
      /* @__PURE__ */ f(j, { danger: !0, block: !0, style: { marginTop: 8 }, onClick: c, children: "删除节点" })
    ] }),
    p && (() => {
      const E = a.findIndex(($) => `e:${$.source}:${$.sourcePort}>${$.target}:${$.targetPort}` === p);
      if (E < 0) return null;
      const T = a[E];
      return /* @__PURE__ */ f(
        Se,
        {
          type: "info",
          showIcon: !0,
          style: { marginTop: 8 },
          message: `当前连线：${T.source}.${T.sourcePort} → ${T.target}.${T.targetPort}`,
          description: /* @__PURE__ */ f(j, { size: "small", danger: !0, onClick: () => l(E), children: "删除此连线" })
        }
      );
    })(),
    /* @__PURE__ */ S(_u, { plain: !0, style: { margin: "16px 0 8px" }, children: [
      "相关连线 (",
      U.length,
      " / ",
      a.length,
      ")"
    ] }),
    r && /* @__PURE__ */ f(j, { type: "link", size: "small", onClick: () => R((E) => !E), children: F ? "仅看当前节点" : "显示全部连线" }),
    n.length > 1 && /* @__PURE__ */ S(Y, { wrap: !0, style: { marginBottom: 8 }, children: [
      /* @__PURE__ */ S(
        "select",
        {
          "aria-label": "连线起点",
          value: y ?? "",
          onChange: (E) => x(E.target.value || void 0),
          children: [
            /* @__PURE__ */ f("option", { value: "", children: "起点节点" }),
            n.map((E) => /* @__PURE__ */ f("option", { value: E.id, children: E.id }, E.id))
          ]
        }
      ),
      /* @__PURE__ */ S(
        "select",
        {
          "aria-label": "连线终点",
          value: O ?? "",
          onChange: (E) => V(E.target.value || void 0),
          children: [
            /* @__PURE__ */ f("option", { value: "", children: "终点节点" }),
            n.map((E) => /* @__PURE__ */ f("option", { value: E.id, children: E.id }, E.id))
          ]
        }
      ),
      /* @__PURE__ */ f(
        j,
        {
          size: "small",
          disabled: !y || !O || y === O,
          onClick: () => {
            y && O && d(y, O);
          },
          children: "添加连线"
        }
      )
    ] }),
    a.length === 0 ? /* @__PURE__ */ f(B.Text, { type: "secondary", style: { fontSize: 12 }, children: "从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。" }) : /* @__PURE__ */ f(Ne, { size: "small", columns: Q, dataSource: U, pagination: !1 }),
    o.length > 0 && /* @__PURE__ */ S(br, { children: [
      /* @__PURE__ */ S(_u, { plain: !0, style: { margin: "16px 0 8px" }, children: [
        "校验定位 (",
        o.length,
        ")"
      ] }),
      o.map((E, T) => /* @__PURE__ */ S("div", { className: "industrial-issue", children: [
        /* @__PURE__ */ f(Be, { color: "red", children: E.code }),
        /* @__PURE__ */ S(
          B.Text,
          {
            style: { fontSize: 12, cursor: "pointer" },
            onClick: () => {
              const $ = /^nodes\.([^.]+)/.exec(E.path);
              $ && v($[1]);
            },
            children: [
              E.path,
              ": ",
              E.message
            ]
          }
        )
      ] }, `${E.path}-${T}`))
    ] })
  ] });
}
const Lv = [
  "Control",
  "File",
  "FileBatch",
  "Dataset",
  "RecordSet",
  "DeliveryResult",
  "Text",
  "Unknown"
];
class jv extends Error {
  constructor(r) {
    super(r), this.name = "DtoError";
  }
}
const ne = (e) => e !== null && typeof e == "object" && !Array.isArray(e) ? e : {}, Ce = (e) => Array.isArray(e) ? e : [], P = (e, r = "") => typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : r ?? "", me = (e, r = 0) => {
  const t = typeof e == "string" ? Number(e) : e;
  return typeof t == "number" && Number.isFinite(t) ? t : r;
}, _ = (e, ...r) => {
  for (const t of r)
    if (e[t] !== void 0 && e[t] !== null) return e[t];
};
function Pe(e) {
  const r = ne(e);
  if (Object.prototype.hasOwnProperty.call(r, "data")) return r.data;
  if (typeof r.code == "string" && typeof r.message == "string")
    throw new jv(`${r.code}: ${r.message}`);
  return e;
}
function Fv(e) {
  const r = P(e).trim().toLowerCase();
  return Lv.find((n) => n.toLowerCase() === r) ?? "Unknown";
}
function Gv(e) {
  if (typeof e == "string")
    return { name: e, type: "Unknown", required: !1 };
  const r = ne(e), t = P(_(r, "name", "port", "id")).trim();
  return t ? {
    name: t,
    type: Fv(_(r, "type", "portType", "dataType")),
    required: _(r, "required", "isRequired") === !0
  } : null;
}
function Tu(e) {
  const r = Ce(e).map(Gv).filter((n) => n !== null), t = /* @__PURE__ */ new Set();
  return r.filter((n) => t.has(n.name) ? !1 : (t.add(n.name), !0));
}
const zv = (e) => {
  const r = ne(e);
  return Object.keys(r).length === 0 ? { type: "object", properties: {} } : r;
};
function Bv(e) {
  const r = ne(e), t = P(_(r, "nodeTypeId", "node_type_id", "type", "id")).trim();
  return t ? {
    nodeTypeId: t,
    name: P(_(r, "name", "title"), t),
    description: P(_(r, "description", "desc")),
    category: P(_(r, "category", "group"), "General"),
    icon: P(_(r, "icon"), "AppstoreOutlined"),
    version: P(_(r, "version"), "1.0.0"),
    inputs: Tu(_(r, "inputs", "inputPorts", "input_ports")),
    outputs: Tu(_(r, "outputs", "outputPorts", "output_ports")),
    configurationSchema: zv(_(r, "configurationSchema", "configSchema", "schema")),
    activityType: P(_(r, "activityType", "activity_type")),
    dependencies: Ce(_(r, "dependencies")).map((n) => P(n)).filter(Boolean)
  } : null;
}
function Bh(e) {
  const r = Pe(e), t = Array.isArray(r) ? r : Ce(_(ne(r), "nodes", "items")), n = /* @__PURE__ */ new Set();
  return t.map(Bv).filter((a) => a !== null).filter((a) => n.has(a.nodeTypeId) ? !1 : (n.add(a.nodeTypeId), !0));
}
function Uh(e) {
  const r = Pe(e), t = Array.isArray(r) ? r : Ce(_(ne(r), "resources", "items")), n = /* @__PURE__ */ new Set();
  return t.map((a) => {
    const i = ne(a), s = P(_(i, "id")).trim();
    return !s || n.has(s) ? null : (n.add(s), {
      id: s,
      name: P(_(i, "name"), s),
      type: P(_(i, "type"), "unknown"),
      enabled: i.enabled !== !1,
      credentialId: typeof i.credentialId == "string" ? i.credentialId : null,
      credentialName: typeof i.credentialName == "string" ? i.credentialName : null
    });
  }).filter((a) => a !== null);
}
function Kh(e) {
  const r = ne(e), t = P(_(r, "workflowId", "workflow_id", "id")).trim();
  return t ? {
    workflowId: t,
    name: P(_(r, "name", "title"), t),
    version: me(_(r, "version"), 1)
  } : null;
}
function Vh(e) {
  const r = Pe(e);
  return (Array.isArray(r) ? r : Ce(_(ne(r), "workflows", "items"))).map(Kh).filter((n) => n !== null);
}
function gt(e) {
  const r = ne(e), t = {};
  for (const [n, a] of Object.entries(r))
    a !== void 0 && (t[n] = a);
  return t;
}
function Uv(e) {
  const r = ne(e), t = P(_(r, "id", "nodeId", "node_id")).trim();
  if (!t) return null;
  const n = ne(_(r, "position", "layout"));
  return {
    id: t,
    type: P(_(r, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: gt(_(r, "config", "configuration")),
    x: me(_(r, "x") ?? _(n, "x"), 0),
    y: me(_(r, "y") ?? _(n, "y"), 0)
  };
}
function Kv(e) {
  const r = ne(e), t = P(_(r, "source", "from", "sourceId")).trim(), n = P(_(r, "target", "to", "targetId")).trim();
  return !t || !n ? null : {
    source: t,
    sourcePort: P(_(r, "sourcePort", "source_port", "fromPort"), "out"),
    target: n,
    targetPort: P(_(r, "targetPort", "target_port", "toPort"), "in")
  };
}
function Vv(e) {
  const r = Pe(e), t = ne(r), n = Kh(t) ?? { workflowId: "", name: "", version: 1 }, a = Ce(_(t, "nodes")).map(Uv).filter((u) => u !== null), i = Ce(_(t, "edges")).map(Kv).filter((u) => u !== null), s = ne(_(t, "trigger")), o = {
    kind: P(_(s, "kind", "type"), "manual"),
    cron: _(s, "cron") === void 0 ? null : P(s.cron),
    eventName: _(s, "eventName", "event_name") === void 0 ? null : P(_(s, "eventName", "event_name")),
    config: gt(_(s, "config"))
  };
  return {
    workflowId: n.workflowId,
    name: n.name,
    version: n.version,
    nodes: a,
    edges: i,
    trigger: o,
    resourceReferences: Ce(_(t, "resourceReferences", "resource_references")).map((u) => P(u)).filter(Boolean),
    layout: gt(_(t, "layout"))
  };
}
const Mr = (e) => e === !0 || typeof e == "string" && e.toLowerCase() === "true";
function Hv(e) {
  const r = Pe(e);
  return (Array.isArray(r) ? r : Ce(_(ne(r), "resources", "items"))).map((n) => {
    const a = ne(n), i = _(a, "credentialId", "credential_id");
    return {
      id: P(_(a, "id"), "—"),
      name: P(_(a, "name"), "—"),
      type: P(_(a, "type"), "—"),
      provider: P(_(a, "provider"), "—"),
      enabled: Mr(_(a, "enabled")),
      credentialId: i == null ? null : P(i),
      credentialName: _(a, "credentialName") == null ? null : P(_(a, "credentialName")),
      config: gt(_(a, "config", "configuration"))
    };
  });
}
function Hh(e) {
  const r = Pe(e);
  return (Array.isArray(r) ? r : Ce(_(ne(r), "credentials", "items"))).map((n) => {
    const a = ne(n);
    return {
      id: P(_(a, "id"), "—"),
      name: P(_(a, "name"), "—"),
      kind: P(_(a, "kind"), "—"),
      version: me(_(a, "version"))
    };
  });
}
function Wv(e) {
  const r = Pe(e);
  return (Array.isArray(r) ? r : Ce(_(ne(r), "tests", "items"))).map((n) => {
    const a = ne(n);
    return {
      resourceId: P(_(a, "resourceId", "resource_id"), "—"),
      ok: Mr(_(a, "ok")),
      detail: P(_(a, "detail"), "")
    };
  });
}
const Wh = (e) => {
  if (e == null) return null;
  const r = ne(e);
  return Object.keys(r).length === 0 ? null : {
    code: P(_(r, "code"), "OTHER"),
    message: P(_(r, "message"), ""),
    action: P(_(r, "action"), "")
  };
};
function Yv(e) {
  const r = Pe(e);
  return Ce(_(ne(r), "triggers", "items")).map((n) => {
    const a = ne(n), i = (s) => _(a, s) == null ? null : P(_(a, s));
    return {
      workflowId: P(_(a, "workflowId", "workflow_id"), "—"),
      version: me(_(a, "version")),
      kind: P(_(a, "kind"), "manual"),
      cron: i("cron"),
      eventName: i("eventName"),
      timezone: i("timezone"),
      resourceId: i("resourceId"),
      nextRun: i("nextRun"),
      lastStatus: i("lastStatus"),
      lastStartedAt: i("lastStartedAt"),
      lastFireStatus: i("lastFireStatus"),
      lastFireDueAt: i("lastFireDueAt"),
      lastFireAttempts: _(a, "lastFireAttempts") == null ? null : me(_(a, "lastFireAttempts")),
      skippedFires: _(a, "skippedFires") == null ? null : me(_(a, "skippedFires")),
      backlogTruncated: _(a, "backlogTruncated") == null ? null : _(a, "backlogTruncated") === !0,
      lastError: i("lastError")
    };
  });
}
function Qv(e) {
  if (e == null) return null;
  const r = Pe(e), t = ne(r);
  if (Object.keys(t).length === 0) return null;
  const n = Ce(_(t, "files")).map((i) => {
    const s = ne(i);
    return {
      path: P(_(s, "path"), "—"),
      sourceDate: P(_(s, "sourceDate"), ""),
      inLedger: Mr(_(s, "inLedger")),
      expectedTargets: me(_(s, "expectedTargets")),
      succeededTargets: me(_(s, "succeededTargets")),
      incompleteTargets: me(_(s, "incompleteTargets")),
      missingTargets: P(_(s, "missingTargets"), ""),
      wouldSkip: Mr(_(s, "wouldSkip")),
      wouldCollect: Mr(_(s, "wouldCollect")),
      duplicateRisk: Mr(_(s, "duplicateRisk"))
    };
  }), a = ne(_(t, "summary"));
  return {
    resourceId: P(_(t, "resourceId"), "—"),
    from: P(_(t, "from"), ""),
    to: P(_(t, "to"), ""),
    files: n,
    summary: {
      discovered: me(_(a, "discovered")),
      alreadyComplete: me(_(a, "alreadyComplete")),
      needsCollection: me(_(a, "needsCollection")),
      duplicateRisk: me(_(a, "duplicateRisk"))
    }
  };
}
function Jv(e) {
  const r = Pe(e);
  return (Array.isArray(r) ? r : Ce(_(ne(r), "files", "items"))).map((n) => {
    const a = ne(n);
    return {
      id: P(_(a, "id"), "—"),
      path: P(_(a, "path"), "—"),
      identityKey: P(_(a, "identityKey"), ""),
      sourceResourceId: P(_(a, "sourceResourceId"), ""),
      length: me(_(a, "length")),
      sourceDate: P(_(a, "sourceDate"), ""),
      collectedAt: P(_(a, "collectedAt"), ""),
      status: P(_(a, "status"), "—"),
      deliveries: Ce(_(a, "deliveries")).map((i) => {
        const s = ne(i);
        return {
          id: P(_(s, "id"), "—"),
          collectionFileId: P(_(s, "collectionFileId"), ""),
          targetId: P(_(s, "targetId"), "—"),
          targetKind: P(_(s, "targetKind"), "record"),
          status: P(_(s, "status"), "—"),
          attempts: me(_(s, "attempts")),
          records: me(_(s, "records")),
          error: _(s, "error") == null ? null : P(_(s, "error")),
          accepted: me(_(s, "accepted")),
          rejected: me(_(s, "rejected")),
          warnings: me(_(s, "warnings")),
          diagnosis: Wh(_(s, "diagnosis"))
        };
      })
    };
  });
}
function Xv(e) {
  const r = Pe(e), t = ne(r), n = Ce(_(t, "runs")).map((i) => {
    const s = ne(i);
    return {
      id: P(_(s, "id"), "—"),
      workflowId: P(_(s, "workflowId", "workflow_id"), "—"),
      status: P(_(s, "status"), "—"),
      startedAt: P(_(s, "startedAt", "started_at"), ""),
      finishedAt: _(s, "finishedAt", "finished_at") == null ? null : P(_(s, "finishedAt", "finished_at")),
      error: _(s, "error") == null ? null : P(_(s, "error"))
    };
  }), a = Ce(_(t, "deliveries")).map((i) => {
    const s = ne(i);
    return {
      id: P(_(s, "id"), "—"),
      collectionFileId: P(_(s, "collectionFileId", "collection_file_id"), ""),
      targetId: P(_(s, "targetId", "target_id"), "—"),
      targetKind: P(_(s, "targetKind", "target_kind"), "record"),
      status: P(_(s, "status"), "—"),
      attempts: me(_(s, "attempts")),
      records: me(_(s, "records")),
      error: _(s, "error") == null ? null : P(_(s, "error")),
      diagnosis: Wh(_(s, "diagnosis"))
    };
  });
  return { runs: n, deliveries: a };
}
function Zv(e) {
  if (e == null) return null;
  const r = Pe(e), t = ne(r);
  if (Object.keys(t).length === 0) return null;
  const n = Ce(_(t, "nodes")).slice(0, 64).map((a) => {
    const i = ne(a);
    return {
      nodeId: P(_(i, "nodeId", "node_id"), "—"),
      status: P(_(i, "status"), "—"),
      records: me(_(i, "records")),
      files: me(_(i, "files")),
      error: _(i, "error") == null ? null : P(_(i, "error"), void 0),
      samples: Ce(_(i, "samples")).slice(0, 3).map((s) => P(s))
    };
  });
  return {
    status: P(_(t, "status"), "—"),
    error: _(t, "error") == null ? null : P(_(t, "error"), void 0),
    executed: Ce(_(t, "executed")).map((a) => P(a)),
    instanceId: _(t, "instanceId") == null ? null : P(_(t, "instanceId"), void 0),
    definitionId: _(t, "definitionId") == null ? null : P(_(t, "definitionId"), void 0),
    nodes: n
  };
}
const eg = [
  { value: "manual", label: "手动运行" },
  { value: "schedule", label: "定时运行" },
  { value: "event", label: "事件触发" }
], rg = [
  { label: "每天 02:00", cron: "0 2 * * *" },
  { label: "每小时", cron: "0 * * * *" },
  { label: "每 5 分钟", cron: "*/5 * * * *" },
  { label: "工作日 08:30", cron: "30 8 * * 1-5" }
];
function tg({
  open: e,
  onClose: r,
  trigger: t,
  resourceIds: n,
  onSave: a,
  ctx: i,
  workflowId: s
}) {
  const [o, u] = q(t), [c, l] = q([]), [d, h] = q(null);
  we(() => {
    u(t);
  }, [t]), we(() => {
    e && i.hubQuery("industrial/trigger-status").then((p) => l(Yv(p))).catch((p) => h(p instanceof Error ? p.message : String(p)));
  }, [i, e]);
  const v = c.find((p) => p.workflowId === s);
  return /* @__PURE__ */ f(
    Er,
    {
      open: e,
      title: "触发器配置",
      okText: "保存触发器",
      cancelText: "取消",
      onCancel: r,
      onOk: () => {
        var p, m;
        if (o.kind === "schedule" && !((p = o.cron) != null && p.trim())) {
          h("定时运行需要填写 cron 表达式");
          return;
        }
        if (o.kind === "event" && !((m = o.eventName) != null && m.trim())) {
          h("事件触发需要填写事件名称");
          return;
        }
        h(null), a(o), r();
      },
      width: 720,
      children: /* @__PURE__ */ S(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ S(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(B.Text, { children: "触发方式" }),
          /* @__PURE__ */ f(Y, { wrap: !0, children: eg.map((p) => /* @__PURE__ */ f(
            j,
            {
              type: o.kind === p.value ? "primary" : "default",
              onClick: () => u({ ...o, kind: p.value }),
              children: p.label
            },
            p.value
          )) })
        ] }),
        /* @__PURE__ */ f(Y, { wrap: !0, children: /* @__PURE__ */ f(
          Ke,
          {
            "aria-label": "运行资源",
            allowClear: !0,
            placeholder: "运行资源",
            value: ng(o),
            options: n.map((p) => ({ value: p, label: p })),
            onChange: (p) => u({
              ...o,
              config: { ...o.config, resourceId: p ?? "" }
            })
          }
        ) }),
        /* @__PURE__ */ S(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(B.Text, { type: "secondary", children: "Cron 与事件名称始终可编辑；只有所选触发方式会生效。" }),
          /* @__PURE__ */ S(Y, { wrap: !0, children: [
            /* @__PURE__ */ f(B.Text, { children: "常用" }),
            rg.map((p) => /* @__PURE__ */ f(
              j,
              {
                size: "small",
                onClick: () => u({ ...o, cron: p.cron }),
                children: p.label
              },
              p.cron
            ))
          ] }),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "Cron 表达式",
              placeholder: "分 时 日 月 周，例如 0 2 * * *",
              value: o.cron ?? "",
              onChange: (p) => u({ ...o, cron: p.target.value })
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "调度时区",
              placeholder: "时区（默认 UTC，例如 Asia/Shanghai）",
              value: ag(o),
              onChange: (p) => u({
                ...o,
                config: { ...o.config, timezone: p.target.value }
              })
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "事件名称",
              placeholder: "事件名称",
              value: o.eventName ?? "",
              onChange: (p) => u({ ...o, eventName: p.target.value })
            }
          ),
          /* @__PURE__ */ S(B.Text, { type: "secondary", children: [
            "下次运行：",
            (v == null ? void 0 : v.nextRun) ?? "尚未发布定时触发器"
          ] })
        ] }),
        v && /* @__PURE__ */ f(
          Ne,
          {
            rowKey: "workflowId",
            dataSource: [v],
            pagination: !1,
            columns: [
              { title: "时区", dataIndex: "timezone", render: (p) => p ?? "UTC" },
              { title: "状态", dataIndex: "lastStatus", render: (p) => p ?? "未运行" },
              { title: "上次运行", dataIndex: "lastStartedAt", render: (p) => p ?? "—" },
              {
                title: "触发点",
                dataIndex: "lastFireStatus",
                render: (p, m) => m.lastFireStatus ? `${m.lastFireStatus} · ${m.lastFireDueAt ?? "—"} · ${m.lastFireAttempts ?? 0} 次` + (m.skippedFires ? ` · 跳过≥${m.skippedFires}` : "") + (m.backlogTruncated ? " · 积压扫描截断" : "") : "—"
              },
              { title: "错误", dataIndex: "lastError", render: (p) => p ?? "—" }
            ]
          }
        ),
        d && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: d })
      ] })
    }
  );
}
function ng(e) {
  const r = e.config.resourceId;
  return typeof r == "string" && r.trim() ? r : void 0;
}
function ag(e) {
  const r = e.config.timezone;
  return typeof r == "string" && r.trim() ? r : "UTC";
}
function ig({
  open: e,
  onClose: r,
  ctx: t,
  workflowId: n,
  resourceId: a,
  lookbackDays: i
}) {
  const [s, o] = q(null), [u, c] = q(null), [l, d] = q(!1), h = L(async () => {
    d(!0), c(null);
    try {
      await t.hubCommand("industrial/preview-backfill", {
        workflowId: n,
        resourceId: a,
        lookbackDays: i
      }), o(Qv(await t.hubQuery("industrial/backfill-preview")));
    } catch (v) {
      c(v instanceof Error ? v.message : String(v));
    } finally {
      d(!1);
    }
  }, [t, i, a, n]);
  return /* @__PURE__ */ f(
    Er,
    {
      open: e,
      title: "补采预览",
      onCancel: r,
      width: 820,
      footer: [
        /* @__PURE__ */ f(j, { loading: l, onClick: () => void h(), children: "重新扫描" }, "refresh"),
        /* @__PURE__ */ f(j, { type: "primary", onClick: r, children: "关闭" }, "close")
      ],
      children: /* @__PURE__ */ S(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
        /* @__PURE__ */ S(Y, { children: [
          /* @__PURE__ */ S(j, { type: "primary", loading: l, onClick: () => void h(), children: [
            "扫描最近 ",
            i,
            " 天"
          ] }),
          /* @__PURE__ */ S(B.Text, { type: "secondary", children: [
            "资源：",
            a || "未选择"
          ] })
        ] }),
        u && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: u }),
        s && /* @__PURE__ */ S(br, { children: [
          /* @__PURE__ */ S(B.Text, { children: [
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
          /* @__PURE__ */ f(
            Ne,
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
                  render: (v, g) => g.wouldSkip ? "跳过" : g.duplicateRisk ? "仅补缺失目标" : "采集"
                }
              ]
            }
          )
        ] })
      ] })
    }
  );
}
var ut = typeof globalThis < "u" ? globalThis : typeof window < "u" ? window : typeof global < "u" ? global : typeof self < "u" ? self : {};
function sg(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
function Xo(e) {
  throw new Error('Could not dynamically require "' + e + '". Please configure the dynamicRequireTargets or/and ignoreDynamicRequires option of @rollup/plugin-commonjs appropriately for this require call to work.');
}
var Ut, xu;
function og() {
  if (xu) return Ut;
  xu = 1;
  function e() {
    this.__data__ = [], this.size = 0;
  }
  return Ut = e, Ut;
}
var Kt, ku;
function $r() {
  if (ku) return Kt;
  ku = 1;
  function e(r, t) {
    return r === t || r !== r && t !== t;
  }
  return Kt = e, Kt;
}
var Vt, Au;
function wt() {
  if (Au) return Vt;
  Au = 1;
  var e = $r();
  function r(t, n) {
    for (var a = t.length; a--; )
      if (e(t[a][0], n))
        return a;
    return -1;
  }
  return Vt = r, Vt;
}
var Ht, Pu;
function ug() {
  if (Pu) return Ht;
  Pu = 1;
  var e = wt(), r = Array.prototype, t = r.splice;
  function n(a) {
    var i = this.__data__, s = e(i, a);
    if (s < 0)
      return !1;
    var o = i.length - 1;
    return s == o ? i.pop() : t.call(i, s, 1), --this.size, !0;
  }
  return Ht = n, Ht;
}
var Wt, Ou;
function lg() {
  if (Ou) return Wt;
  Ou = 1;
  var e = wt();
  function r(t) {
    var n = this.__data__, a = e(n, t);
    return a < 0 ? void 0 : n[a][1];
  }
  return Wt = r, Wt;
}
var Yt, Nu;
function cg() {
  if (Nu) return Yt;
  Nu = 1;
  var e = wt();
  function r(t) {
    return e(this.__data__, t) > -1;
  }
  return Yt = r, Yt;
}
var Qt, Du;
function dg() {
  if (Du) return Qt;
  Du = 1;
  var e = wt();
  function r(t, n) {
    var a = this.__data__, i = e(a, t);
    return i < 0 ? (++this.size, a.push([t, n])) : a[i][1] = n, this;
  }
  return Qt = r, Qt;
}
var Jt, Mu;
function It() {
  if (Mu) return Jt;
  Mu = 1;
  var e = og(), r = ug(), t = lg(), n = cg(), a = dg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, Jt = i, Jt;
}
var Xt, $u;
function fg() {
  if ($u) return Xt;
  $u = 1;
  var e = It();
  function r() {
    this.__data__ = new e(), this.size = 0;
  }
  return Xt = r, Xt;
}
var Zt, Lu;
function hg() {
  if (Lu) return Zt;
  Lu = 1;
  function e(r) {
    var t = this.__data__, n = t.delete(r);
    return this.size = t.size, n;
  }
  return Zt = e, Zt;
}
var en, ju;
function pg() {
  if (ju) return en;
  ju = 1;
  function e(r) {
    return this.__data__.get(r);
  }
  return en = e, en;
}
var rn, Fu;
function vg() {
  if (Fu) return rn;
  Fu = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return rn = e, rn;
}
var tn, Gu;
function Yh() {
  if (Gu) return tn;
  Gu = 1;
  var e = typeof ut == "object" && ut && ut.Object === Object && ut;
  return tn = e, tn;
}
var nn, zu;
function Xe() {
  if (zu) return nn;
  zu = 1;
  var e = Yh(), r = typeof self == "object" && self && self.Object === Object && self, t = e || r || Function("return this")();
  return nn = t, nn;
}
var an, Bu;
function Lr() {
  if (Bu) return an;
  Bu = 1;
  var e = Xe(), r = e.Symbol;
  return an = r, an;
}
var sn, Uu;
function gg() {
  if (Uu) return sn;
  Uu = 1;
  var e = Lr(), r = Object.prototype, t = r.hasOwnProperty, n = r.toString, a = e ? e.toStringTag : void 0;
  function i(s) {
    var o = t.call(s, a), u = s[a];
    try {
      s[a] = void 0;
      var c = !0;
    } catch {
    }
    var l = n.call(s);
    return c && (o ? s[a] = u : delete s[a]), l;
  }
  return sn = i, sn;
}
var on, Ku;
function yg() {
  if (Ku) return on;
  Ku = 1;
  var e = Object.prototype, r = e.toString;
  function t(n) {
    return r.call(n);
  }
  return on = t, on;
}
var un, Vu;
function Sr() {
  if (Vu) return un;
  Vu = 1;
  var e = Lr(), r = gg(), t = yg(), n = "[object Null]", a = "[object Undefined]", i = e ? e.toStringTag : void 0;
  function s(o) {
    return o == null ? o === void 0 ? a : n : i && i in Object(o) ? r(o) : t(o);
  }
  return un = s, un;
}
var ln, Hu;
function Ve() {
  if (Hu) return ln;
  Hu = 1;
  function e(r) {
    var t = typeof r;
    return r != null && (t == "object" || t == "function");
  }
  return ln = e, ln;
}
var cn, Wu;
function rt() {
  if (Wu) return cn;
  Wu = 1;
  var e = Sr(), r = Ve(), t = "[object AsyncFunction]", n = "[object Function]", a = "[object GeneratorFunction]", i = "[object Proxy]";
  function s(o) {
    if (!r(o))
      return !1;
    var u = e(o);
    return u == n || u == a || u == t || u == i;
  }
  return cn = s, cn;
}
var dn, Yu;
function mg() {
  if (Yu) return dn;
  Yu = 1;
  var e = Xe(), r = e["__core-js_shared__"];
  return dn = r, dn;
}
var fn, Qu;
function bg() {
  if (Qu) return fn;
  Qu = 1;
  var e = mg(), r = function() {
    var n = /[^.]+$/.exec(e && e.keys && e.keys.IE_PROTO || "");
    return n ? "Symbol(src)_1." + n : "";
  }();
  function t(n) {
    return !!r && r in n;
  }
  return fn = t, fn;
}
var hn, Ju;
function Qh() {
  if (Ju) return hn;
  Ju = 1;
  var e = Function.prototype, r = e.toString;
  function t(n) {
    if (n != null) {
      try {
        return r.call(n);
      } catch {
      }
      try {
        return n + "";
      } catch {
      }
    }
    return "";
  }
  return hn = t, hn;
}
var pn, Xu;
function _g() {
  if (Xu) return pn;
  Xu = 1;
  var e = rt(), r = bg(), t = Ve(), n = Qh(), a = /[\\^$.*+?()[\]{}|]/g, i = /^\[object .+?Constructor\]$/, s = Function.prototype, o = Object.prototype, u = s.toString, c = o.hasOwnProperty, l = RegExp(
    "^" + u.call(c).replace(a, "\\$&").replace(/hasOwnProperty|(function).*?(?=\\\()| for .+?(?=\\\])/g, "$1.*?") + "$"
  );
  function d(h) {
    if (!t(h) || r(h))
      return !1;
    var v = e(h) ? l : i;
    return v.test(n(h));
  }
  return pn = d, pn;
}
var vn, Zu;
function wg() {
  if (Zu) return vn;
  Zu = 1;
  function e(r, t) {
    return r == null ? void 0 : r[t];
  }
  return vn = e, vn;
}
var gn, el;
function Rr() {
  if (el) return gn;
  el = 1;
  var e = _g(), r = wg();
  function t(n, a) {
    var i = r(n, a);
    return e(i) ? i : void 0;
  }
  return gn = t, gn;
}
var yn, rl;
function Zo() {
  if (rl) return yn;
  rl = 1;
  var e = Rr(), r = Xe(), t = e(r, "Map");
  return yn = t, yn;
}
var mn, tl;
function qt() {
  if (tl) return mn;
  tl = 1;
  var e = Rr(), r = e(Object, "create");
  return mn = r, mn;
}
var bn, nl;
function Ig() {
  if (nl) return bn;
  nl = 1;
  var e = qt();
  function r() {
    this.__data__ = e ? e(null) : {}, this.size = 0;
  }
  return bn = r, bn;
}
var _n, al;
function qg() {
  if (al) return _n;
  al = 1;
  function e(r) {
    var t = this.has(r) && delete this.__data__[r];
    return this.size -= t ? 1 : 0, t;
  }
  return _n = e, _n;
}
var wn, il;
function Cg() {
  if (il) return wn;
  il = 1;
  var e = qt(), r = "__lodash_hash_undefined__", t = Object.prototype, n = t.hasOwnProperty;
  function a(i) {
    var s = this.__data__;
    if (e) {
      var o = s[i];
      return o === r ? void 0 : o;
    }
    return n.call(s, i) ? s[i] : void 0;
  }
  return wn = a, wn;
}
var In, sl;
function Eg() {
  if (sl) return In;
  sl = 1;
  var e = qt(), r = Object.prototype, t = r.hasOwnProperty;
  function n(a) {
    var i = this.__data__;
    return e ? i[a] !== void 0 : t.call(i, a);
  }
  return In = n, In;
}
var qn, ol;
function Sg() {
  if (ol) return qn;
  ol = 1;
  var e = qt(), r = "__lodash_hash_undefined__";
  function t(n, a) {
    var i = this.__data__;
    return this.size += this.has(n) ? 0 : 1, i[n] = e && a === void 0 ? r : a, this;
  }
  return qn = t, qn;
}
var Cn, ul;
function Rg() {
  if (ul) return Cn;
  ul = 1;
  var e = Ig(), r = qg(), t = Cg(), n = Eg(), a = Sg();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, Cn = i, Cn;
}
var En, ll;
function Tg() {
  if (ll) return En;
  ll = 1;
  var e = Rg(), r = It(), t = Zo();
  function n() {
    this.size = 0, this.__data__ = {
      hash: new e(),
      map: new (t || r)(),
      string: new e()
    };
  }
  return En = n, En;
}
var Sn, cl;
function xg() {
  if (cl) return Sn;
  cl = 1;
  function e(r) {
    var t = typeof r;
    return t == "string" || t == "number" || t == "symbol" || t == "boolean" ? r !== "__proto__" : r === null;
  }
  return Sn = e, Sn;
}
var Rn, dl;
function Ct() {
  if (dl) return Rn;
  dl = 1;
  var e = xg();
  function r(t, n) {
    var a = t.__data__;
    return e(n) ? a[typeof n == "string" ? "string" : "hash"] : a.map;
  }
  return Rn = r, Rn;
}
var Tn, fl;
function kg() {
  if (fl) return Tn;
  fl = 1;
  var e = Ct();
  function r(t) {
    var n = e(this, t).delete(t);
    return this.size -= n ? 1 : 0, n;
  }
  return Tn = r, Tn;
}
var xn, hl;
function Ag() {
  if (hl) return xn;
  hl = 1;
  var e = Ct();
  function r(t) {
    return e(this, t).get(t);
  }
  return xn = r, xn;
}
var kn, pl;
function Pg() {
  if (pl) return kn;
  pl = 1;
  var e = Ct();
  function r(t) {
    return e(this, t).has(t);
  }
  return kn = r, kn;
}
var An, vl;
function Og() {
  if (vl) return An;
  vl = 1;
  var e = Ct();
  function r(t, n) {
    var a = e(this, t), i = a.size;
    return a.set(t, n), this.size += a.size == i ? 0 : 1, this;
  }
  return An = r, An;
}
var Pn, gl;
function eu() {
  if (gl) return Pn;
  gl = 1;
  var e = Tg(), r = kg(), t = Ag(), n = Pg(), a = Og();
  function i(s) {
    var o = -1, u = s == null ? 0 : s.length;
    for (this.clear(); ++o < u; ) {
      var c = s[o];
      this.set(c[0], c[1]);
    }
  }
  return i.prototype.clear = e, i.prototype.delete = r, i.prototype.get = t, i.prototype.has = n, i.prototype.set = a, Pn = i, Pn;
}
var On, yl;
function Ng() {
  if (yl) return On;
  yl = 1;
  var e = It(), r = Zo(), t = eu(), n = 200;
  function a(i, s) {
    var o = this.__data__;
    if (o instanceof e) {
      var u = o.__data__;
      if (!r || u.length < n - 1)
        return u.push([i, s]), this.size = ++o.size, this;
      o = this.__data__ = new t(u);
    }
    return o.set(i, s), this.size = o.size, this;
  }
  return On = a, On;
}
var Nn, ml;
function Et() {
  if (ml) return Nn;
  ml = 1;
  var e = It(), r = fg(), t = hg(), n = pg(), a = vg(), i = Ng();
  function s(o) {
    var u = this.__data__ = new e(o);
    this.size = u.size;
  }
  return s.prototype.clear = r, s.prototype.delete = t, s.prototype.get = n, s.prototype.has = a, s.prototype.set = i, Nn = s, Nn;
}
var Dn, bl;
function ru() {
  if (bl) return Dn;
  bl = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a && t(r[n], n, r) !== !1; )
      ;
    return r;
  }
  return Dn = e, Dn;
}
var Mn, _l;
function Jh() {
  if (_l) return Mn;
  _l = 1;
  var e = Rr(), r = function() {
    try {
      var t = e(Object, "defineProperty");
      return t({}, "", {}), t;
    } catch {
    }
  }();
  return Mn = r, Mn;
}
var $n, wl;
function St() {
  if (wl) return $n;
  wl = 1;
  var e = Jh();
  function r(t, n, a) {
    n == "__proto__" && e ? e(t, n, {
      configurable: !0,
      enumerable: !0,
      value: a,
      writable: !0
    }) : t[n] = a;
  }
  return $n = r, $n;
}
var Ln, Il;
function Rt() {
  if (Il) return Ln;
  Il = 1;
  var e = St(), r = $r(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o) {
    var u = i[s];
    (!(n.call(i, s) && r(u, o)) || o === void 0 && !(s in i)) && e(i, s, o);
  }
  return Ln = a, Ln;
}
var jn, ql;
function tt() {
  if (ql) return jn;
  ql = 1;
  var e = Rt(), r = St();
  function t(n, a, i, s) {
    var o = !i;
    i || (i = {});
    for (var u = -1, c = a.length; ++u < c; ) {
      var l = a[u], d = s ? s(i[l], n[l], l, i, n) : void 0;
      d === void 0 && (d = n[l]), o ? r(i, l, d) : e(i, l, d);
    }
    return i;
  }
  return jn = t, jn;
}
var Fn, Cl;
function Dg() {
  if (Cl) return Fn;
  Cl = 1;
  function e(r, t) {
    for (var n = -1, a = Array(r); ++n < r; )
      a[n] = t(n);
    return a;
  }
  return Fn = e, Fn;
}
var Gn, El;
function tr() {
  if (El) return Gn;
  El = 1;
  function e(r) {
    return r != null && typeof r == "object";
  }
  return Gn = e, Gn;
}
var zn, Sl;
function Mg() {
  if (Sl) return zn;
  Sl = 1;
  var e = Sr(), r = tr(), t = "[object Arguments]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return zn = n, zn;
}
var Bn, Rl;
function nt() {
  if (Rl) return Bn;
  Rl = 1;
  var e = Mg(), r = tr(), t = Object.prototype, n = t.hasOwnProperty, a = t.propertyIsEnumerable, i = e(/* @__PURE__ */ function() {
    return arguments;
  }()) ? e : function(s) {
    return r(s) && n.call(s, "callee") && !a.call(s, "callee");
  };
  return Bn = i, Bn;
}
var Un, Tl;
function Ee() {
  if (Tl) return Un;
  Tl = 1;
  var e = Array.isArray;
  return Un = e, Un;
}
var Vr = { exports: {} }, Kn, xl;
function $g() {
  if (xl) return Kn;
  xl = 1;
  function e() {
    return !1;
  }
  return Kn = e, Kn;
}
Vr.exports;
var kl;
function jr() {
  return kl || (kl = 1, function(e, r) {
    var t = Xe(), n = $g(), a = r && !r.nodeType && r, i = a && !0 && e && !e.nodeType && e, s = i && i.exports === a, o = s ? t.Buffer : void 0, u = o ? o.isBuffer : void 0, c = u || n;
    e.exports = c;
  }(Vr, Vr.exports)), Vr.exports;
}
var Vn, Al;
function Tt() {
  if (Al) return Vn;
  Al = 1;
  var e = 9007199254740991, r = /^(?:0|[1-9]\d*)$/;
  function t(n, a) {
    var i = typeof n;
    return a = a ?? e, !!a && (i == "number" || i != "symbol" && r.test(n)) && n > -1 && n % 1 == 0 && n < a;
  }
  return Vn = t, Vn;
}
var Hn, Pl;
function tu() {
  if (Pl) return Hn;
  Pl = 1;
  var e = 9007199254740991;
  function r(t) {
    return typeof t == "number" && t > -1 && t % 1 == 0 && t <= e;
  }
  return Hn = r, Hn;
}
var Wn, Ol;
function Lg() {
  if (Ol) return Wn;
  Ol = 1;
  var e = Sr(), r = tu(), t = tr(), n = "[object Arguments]", a = "[object Array]", i = "[object Boolean]", s = "[object Date]", o = "[object Error]", u = "[object Function]", c = "[object Map]", l = "[object Number]", d = "[object Object]", h = "[object RegExp]", v = "[object Set]", g = "[object String]", p = "[object WeakMap]", m = "[object ArrayBuffer]", I = "[object DataView]", k = "[object Float32Array]", b = "[object Float64Array]", A = "[object Int8Array]", M = "[object Int16Array]", y = "[object Int32Array]", x = "[object Uint8Array]", O = "[object Uint8ClampedArray]", V = "[object Uint16Array]", F = "[object Uint32Array]", R = {};
  R[k] = R[b] = R[A] = R[M] = R[y] = R[x] = R[O] = R[V] = R[F] = !0, R[n] = R[a] = R[m] = R[i] = R[I] = R[s] = R[o] = R[u] = R[c] = R[l] = R[d] = R[h] = R[v] = R[g] = R[p] = !1;
  function G(H) {
    return t(H) && r(H.length) && !!R[e(H)];
  }
  return Wn = G, Wn;
}
var Yn, Nl;
function xt() {
  if (Nl) return Yn;
  Nl = 1;
  function e(r) {
    return function(t) {
      return r(t);
    };
  }
  return Yn = e, Yn;
}
var Hr = { exports: {} };
Hr.exports;
var Dl;
function nu() {
  return Dl || (Dl = 1, function(e, r) {
    var t = Yh(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i && t.process, o = function() {
      try {
        var u = a && a.require && a.require("util").types;
        return u || s && s.binding && s.binding("util");
      } catch {
      }
    }();
    e.exports = o;
  }(Hr, Hr.exports)), Hr.exports;
}
var Qn, Ml;
function at() {
  if (Ml) return Qn;
  Ml = 1;
  var e = Lg(), r = xt(), t = nu(), n = t && t.isTypedArray, a = n ? r(n) : e;
  return Qn = a, Qn;
}
var Jn, $l;
function Xh() {
  if ($l) return Jn;
  $l = 1;
  var e = Dg(), r = nt(), t = Ee(), n = jr(), a = Tt(), i = at(), s = Object.prototype, o = s.hasOwnProperty;
  function u(c, l) {
    var d = t(c), h = !d && r(c), v = !d && !h && n(c), g = !d && !h && !v && i(c), p = d || h || v || g, m = p ? e(c.length, String) : [], I = m.length;
    for (var k in c)
      (l || o.call(c, k)) && !(p && // Safari 9 has enumerable `arguments.length` in strict mode.
      (k == "length" || // Node.js 0.10 has enumerable non-index properties on buffers.
      v && (k == "offset" || k == "parent") || // PhantomJS 2 has enumerable non-index properties on typed arrays.
      g && (k == "buffer" || k == "byteLength" || k == "byteOffset") || // Skip index properties.
      a(k, I))) && m.push(k);
    return m;
  }
  return Jn = u, Jn;
}
var Xn, Ll;
function kt() {
  if (Ll) return Xn;
  Ll = 1;
  var e = Object.prototype;
  function r(t) {
    var n = t && t.constructor, a = typeof n == "function" && n.prototype || e;
    return t === a;
  }
  return Xn = r, Xn;
}
var Zn, jl;
function Zh() {
  if (jl) return Zn;
  jl = 1;
  function e(r, t) {
    return function(n) {
      return r(t(n));
    };
  }
  return Zn = e, Zn;
}
var ea, Fl;
function jg() {
  if (Fl) return ea;
  Fl = 1;
  var e = Zh(), r = e(Object.keys, Object);
  return ea = r, ea;
}
var ra, Gl;
function au() {
  if (Gl) return ra;
  Gl = 1;
  var e = kt(), r = jg(), t = Object.prototype, n = t.hasOwnProperty;
  function a(i) {
    if (!e(i))
      return r(i);
    var s = [];
    for (var o in Object(i))
      n.call(i, o) && o != "constructor" && s.push(o);
    return s;
  }
  return ra = a, ra;
}
var ta, zl;
function fr() {
  if (zl) return ta;
  zl = 1;
  var e = rt(), r = tu();
  function t(n) {
    return n != null && r(n.length) && !e(n);
  }
  return ta = t, ta;
}
var na, Bl;
function Ir() {
  if (Bl) return na;
  Bl = 1;
  var e = Xh(), r = au(), t = fr();
  function n(a) {
    return t(a) ? e(a) : r(a);
  }
  return na = n, na;
}
var aa, Ul;
function Fg() {
  if (Ul) return aa;
  Ul = 1;
  var e = tt(), r = Ir();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return aa = t, aa;
}
var ia, Kl;
function Gg() {
  if (Kl) return ia;
  Kl = 1;
  function e(r) {
    var t = [];
    if (r != null)
      for (var n in Object(r))
        t.push(n);
    return t;
  }
  return ia = e, ia;
}
var sa, Vl;
function zg() {
  if (Vl) return sa;
  Vl = 1;
  var e = Ve(), r = kt(), t = Gg(), n = Object.prototype, a = n.hasOwnProperty;
  function i(s) {
    if (!e(s))
      return t(s);
    var o = r(s), u = [];
    for (var c in s)
      c == "constructor" && (o || !a.call(s, c)) || u.push(c);
    return u;
  }
  return sa = i, sa;
}
var oa, Hl;
function Tr() {
  if (Hl) return oa;
  Hl = 1;
  var e = Xh(), r = zg(), t = fr();
  function n(a) {
    return t(a) ? e(a, !0) : r(a);
  }
  return oa = n, oa;
}
var ua, Wl;
function Bg() {
  if (Wl) return ua;
  Wl = 1;
  var e = tt(), r = Tr();
  function t(n, a) {
    return n && e(a, r(a), n);
  }
  return ua = t, ua;
}
var Wr = { exports: {} };
Wr.exports;
var Yl;
function ep() {
  return Yl || (Yl = 1, function(e, r) {
    var t = Xe(), n = r && !r.nodeType && r, a = n && !0 && e && !e.nodeType && e, i = a && a.exports === n, s = i ? t.Buffer : void 0, o = s ? s.allocUnsafe : void 0;
    function u(c, l) {
      if (l)
        return c.slice();
      var d = c.length, h = o ? o(d) : new c.constructor(d);
      return c.copy(h), h;
    }
    e.exports = u;
  }(Wr, Wr.exports)), Wr.exports;
}
var la, Ql;
function rp() {
  if (Ql) return la;
  Ql = 1;
  function e(r, t) {
    var n = -1, a = r.length;
    for (t || (t = Array(a)); ++n < a; )
      t[n] = r[n];
    return t;
  }
  return la = e, la;
}
var ca, Jl;
function tp() {
  if (Jl) return ca;
  Jl = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = 0, s = []; ++n < a; ) {
      var o = r[n];
      t(o, n, r) && (s[i++] = o);
    }
    return s;
  }
  return ca = e, ca;
}
var da, Xl;
function np() {
  if (Xl) return da;
  Xl = 1;
  function e() {
    return [];
  }
  return da = e, da;
}
var fa, Zl;
function iu() {
  if (Zl) return fa;
  Zl = 1;
  var e = tp(), r = np(), t = Object.prototype, n = t.propertyIsEnumerable, a = Object.getOwnPropertySymbols, i = a ? function(s) {
    return s == null ? [] : (s = Object(s), e(a(s), function(o) {
      return n.call(s, o);
    }));
  } : r;
  return fa = i, fa;
}
var ha, ec;
function Ug() {
  if (ec) return ha;
  ec = 1;
  var e = tt(), r = iu();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return ha = t, ha;
}
var pa, rc;
function su() {
  if (rc) return pa;
  rc = 1;
  function e(r, t) {
    for (var n = -1, a = t.length, i = r.length; ++n < a; )
      r[i + n] = t[n];
    return r;
  }
  return pa = e, pa;
}
var va, tc;
function At() {
  if (tc) return va;
  tc = 1;
  var e = Zh(), r = e(Object.getPrototypeOf, Object);
  return va = r, va;
}
var ga, nc;
function ap() {
  if (nc) return ga;
  nc = 1;
  var e = su(), r = At(), t = iu(), n = np(), a = Object.getOwnPropertySymbols, i = a ? function(s) {
    for (var o = []; s; )
      e(o, t(s)), s = r(s);
    return o;
  } : n;
  return ga = i, ga;
}
var ya, ac;
function Kg() {
  if (ac) return ya;
  ac = 1;
  var e = tt(), r = ap();
  function t(n, a) {
    return e(n, r(n), a);
  }
  return ya = t, ya;
}
var ma, ic;
function ip() {
  if (ic) return ma;
  ic = 1;
  var e = su(), r = Ee();
  function t(n, a, i) {
    var s = a(n);
    return r(n) ? s : e(s, i(n));
  }
  return ma = t, ma;
}
var ba, sc;
function sp() {
  if (sc) return ba;
  sc = 1;
  var e = ip(), r = iu(), t = Ir();
  function n(a) {
    return e(a, t, r);
  }
  return ba = n, ba;
}
var _a, oc;
function Vg() {
  if (oc) return _a;
  oc = 1;
  var e = ip(), r = ap(), t = Tr();
  function n(a) {
    return e(a, t, r);
  }
  return _a = n, _a;
}
var wa, uc;
function Hg() {
  if (uc) return wa;
  uc = 1;
  var e = Rr(), r = Xe(), t = e(r, "DataView");
  return wa = t, wa;
}
var Ia, lc;
function Wg() {
  if (lc) return Ia;
  lc = 1;
  var e = Rr(), r = Xe(), t = e(r, "Promise");
  return Ia = t, Ia;
}
var qa, cc;
function op() {
  if (cc) return qa;
  cc = 1;
  var e = Rr(), r = Xe(), t = e(r, "Set");
  return qa = t, qa;
}
var Ca, dc;
function Yg() {
  if (dc) return Ca;
  dc = 1;
  var e = Rr(), r = Xe(), t = e(r, "WeakMap");
  return Ca = t, Ca;
}
var Ea, fc;
function Fr() {
  if (fc) return Ea;
  fc = 1;
  var e = Hg(), r = Zo(), t = Wg(), n = op(), a = Yg(), i = Sr(), s = Qh(), o = "[object Map]", u = "[object Object]", c = "[object Promise]", l = "[object Set]", d = "[object WeakMap]", h = "[object DataView]", v = s(e), g = s(r), p = s(t), m = s(n), I = s(a), k = i;
  return (e && k(new e(new ArrayBuffer(1))) != h || r && k(new r()) != o || t && k(t.resolve()) != c || n && k(new n()) != l || a && k(new a()) != d) && (k = function(b) {
    var A = i(b), M = A == u ? b.constructor : void 0, y = M ? s(M) : "";
    if (y)
      switch (y) {
        case v:
          return h;
        case g:
          return o;
        case p:
          return c;
        case m:
          return l;
        case I:
          return d;
      }
    return A;
  }), Ea = k, Ea;
}
var Sa, hc;
function Qg() {
  if (hc) return Sa;
  hc = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n) {
    var a = n.length, i = new n.constructor(a);
    return a && typeof n[0] == "string" && r.call(n, "index") && (i.index = n.index, i.input = n.input), i;
  }
  return Sa = t, Sa;
}
var Ra, pc;
function up() {
  if (pc) return Ra;
  pc = 1;
  var e = Xe(), r = e.Uint8Array;
  return Ra = r, Ra;
}
var Ta, vc;
function ou() {
  if (vc) return Ta;
  vc = 1;
  var e = up();
  function r(t) {
    var n = new t.constructor(t.byteLength);
    return new e(n).set(new e(t)), n;
  }
  return Ta = r, Ta;
}
var xa, gc;
function Jg() {
  if (gc) return xa;
  gc = 1;
  var e = ou();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.byteLength);
  }
  return xa = r, xa;
}
var ka, yc;
function Xg() {
  if (yc) return ka;
  yc = 1;
  var e = /\w*$/;
  function r(t) {
    var n = new t.constructor(t.source, e.exec(t));
    return n.lastIndex = t.lastIndex, n;
  }
  return ka = r, ka;
}
var Aa, mc;
function Zg() {
  if (mc) return Aa;
  mc = 1;
  var e = Lr(), r = e ? e.prototype : void 0, t = r ? r.valueOf : void 0;
  function n(a) {
    return t ? Object(t.call(a)) : {};
  }
  return Aa = n, Aa;
}
var Pa, bc;
function lp() {
  if (bc) return Pa;
  bc = 1;
  var e = ou();
  function r(t, n) {
    var a = n ? e(t.buffer) : t.buffer;
    return new t.constructor(a, t.byteOffset, t.length);
  }
  return Pa = r, Pa;
}
var Oa, _c;
function ey() {
  if (_c) return Oa;
  _c = 1;
  var e = ou(), r = Jg(), t = Xg(), n = Zg(), a = lp(), i = "[object Boolean]", s = "[object Date]", o = "[object Map]", u = "[object Number]", c = "[object RegExp]", l = "[object Set]", d = "[object String]", h = "[object Symbol]", v = "[object ArrayBuffer]", g = "[object DataView]", p = "[object Float32Array]", m = "[object Float64Array]", I = "[object Int8Array]", k = "[object Int16Array]", b = "[object Int32Array]", A = "[object Uint8Array]", M = "[object Uint8ClampedArray]", y = "[object Uint16Array]", x = "[object Uint32Array]";
  function O(V, F, R) {
    var G = V.constructor;
    switch (F) {
      case v:
        return e(V);
      case i:
      case s:
        return new G(+V);
      case g:
        return r(V, R);
      case p:
      case m:
      case I:
      case k:
      case b:
      case A:
      case M:
      case y:
      case x:
        return a(V, R);
      case o:
        return new G();
      case u:
      case d:
        return new G(V);
      case c:
        return t(V);
      case l:
        return new G();
      case h:
        return n(V);
    }
  }
  return Oa = O, Oa;
}
var Na, wc;
function cp() {
  if (wc) return Na;
  wc = 1;
  var e = Ve(), r = Object.create, t = /* @__PURE__ */ function() {
    function n() {
    }
    return function(a) {
      if (!e(a))
        return {};
      if (r)
        return r(a);
      n.prototype = a;
      var i = new n();
      return n.prototype = void 0, i;
    };
  }();
  return Na = t, Na;
}
var Da, Ic;
function dp() {
  if (Ic) return Da;
  Ic = 1;
  var e = cp(), r = At(), t = kt();
  function n(a) {
    return typeof a.constructor == "function" && !t(a) ? e(r(a)) : {};
  }
  return Da = n, Da;
}
var Ma, qc;
function ry() {
  if (qc) return Ma;
  qc = 1;
  var e = Fr(), r = tr(), t = "[object Map]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return Ma = n, Ma;
}
var $a, Cc;
function ty() {
  if (Cc) return $a;
  Cc = 1;
  var e = ry(), r = xt(), t = nu(), n = t && t.isMap, a = n ? r(n) : e;
  return $a = a, $a;
}
var La, Ec;
function ny() {
  if (Ec) return La;
  Ec = 1;
  var e = Fr(), r = tr(), t = "[object Set]";
  function n(a) {
    return r(a) && e(a) == t;
  }
  return La = n, La;
}
var ja, Sc;
function ay() {
  if (Sc) return ja;
  Sc = 1;
  var e = ny(), r = xt(), t = nu(), n = t && t.isSet, a = n ? r(n) : e;
  return ja = a, ja;
}
var Fa, Rc;
function fp() {
  if (Rc) return Fa;
  Rc = 1;
  var e = Et(), r = ru(), t = Rt(), n = Fg(), a = Bg(), i = ep(), s = rp(), o = Ug(), u = Kg(), c = sp(), l = Vg(), d = Fr(), h = Qg(), v = ey(), g = dp(), p = Ee(), m = jr(), I = ty(), k = Ve(), b = ay(), A = Ir(), M = Tr(), y = 1, x = 2, O = 4, V = "[object Arguments]", F = "[object Array]", R = "[object Boolean]", G = "[object Date]", H = "[object Error]", z = "[object Function]", ae = "[object GeneratorFunction]", te = "[object Map]", de = "[object Number]", U = "[object Object]", Q = "[object RegExp]", E = "[object Set]", T = "[object String]", $ = "[object Symbol]", pe = "[object WeakMap]", Ie = "[object ArrayBuffer]", Re = "[object DataView]", nr = "[object Float32Array]", Ar = "[object Float64Array]", qr = "[object Int8Array]", Pr = "[object Int16Array]", pr = "[object Int32Array]", Or = "[object Uint8Array]", We = "[object Uint8ClampedArray]", ar = "[object Uint16Array]", Ye = "[object Uint32Array]", ie = {};
  ie[V] = ie[F] = ie[Ie] = ie[Re] = ie[R] = ie[G] = ie[nr] = ie[Ar] = ie[qr] = ie[Pr] = ie[pr] = ie[te] = ie[de] = ie[U] = ie[Q] = ie[E] = ie[T] = ie[$] = ie[Or] = ie[We] = ie[ar] = ie[Ye] = !0, ie[H] = ie[z] = ie[pe] = !1;
  function ce(X, Te, Qe, ir, vr, $e) {
    var W, ee = Te & y, oe = Te & x, ye = Te & O;
    if (Qe && (W = vr ? Qe(X, ir, vr, $e) : Qe(X)), W !== void 0)
      return W;
    if (!k(X))
      return X;
    var gr = p(X);
    if (gr) {
      if (W = h(X), !ee)
        return s(X, W);
    } else {
      var Le = d(X), sr = Le == z || Le == ae;
      if (m(X))
        return i(X, ee);
      if (Le == U || Le == V || sr && !vr) {
        if (W = oe || sr ? {} : g(X), !ee)
          return oe ? u(X, a(W, X)) : o(X, n(W, X));
      } else {
        if (!ie[Le])
          return vr ? X : {};
        W = v(X, Le, ee);
      }
    }
    $e || ($e = new e());
    var er = $e.get(X);
    if (er)
      return er;
    $e.set(X, W), b(X) ? X.forEach(function(je) {
      W.add(ce(je, Te, Qe, je, X, $e));
    }) : I(X) && X.forEach(function(je, Je) {
      W.set(Je, ce(je, Te, Qe, Je, X, $e));
    });
    var zr = ye ? oe ? l : c : oe ? M : A, Nr = gr ? void 0 : zr(X);
    return r(Nr || X, function(je, Je) {
      Nr && (Je = je, je = X[Je]), t(W, Je, ce(je, Te, Qe, Je, X, $e));
    }), W;
  }
  return Fa = ce, Fa;
}
var Ga, Tc;
function iy() {
  if (Tc) return Ga;
  Tc = 1;
  var e = fp(), r = 4;
  function t(n) {
    return e(n, r);
  }
  return Ga = t, Ga;
}
var za, xc;
function uu() {
  if (xc) return za;
  xc = 1;
  function e(r) {
    return function() {
      return r;
    };
  }
  return za = e, za;
}
var Ba, kc;
function sy() {
  if (kc) return Ba;
  kc = 1;
  function e(r) {
    return function(t, n, a) {
      for (var i = -1, s = Object(t), o = a(t), u = o.length; u--; ) {
        var c = o[r ? u : ++i];
        if (n(s[c], c, s) === !1)
          break;
      }
      return t;
    };
  }
  return Ba = e, Ba;
}
var Ua, Ac;
function lu() {
  if (Ac) return Ua;
  Ac = 1;
  var e = sy(), r = e();
  return Ua = r, Ua;
}
var Ka, Pc;
function cu() {
  if (Pc) return Ka;
  Pc = 1;
  var e = lu(), r = Ir();
  function t(n, a) {
    return n && e(n, a, r);
  }
  return Ka = t, Ka;
}
var Va, Oc;
function oy() {
  if (Oc) return Va;
  Oc = 1;
  var e = fr();
  function r(t, n) {
    return function(a, i) {
      if (a == null)
        return a;
      if (!e(a))
        return t(a, i);
      for (var s = a.length, o = n ? s : -1, u = Object(a); (n ? o-- : ++o < s) && i(u[o], o, u) !== !1; )
        ;
      return a;
    };
  }
  return Va = r, Va;
}
var Ha, Nc;
function Pt() {
  if (Nc) return Ha;
  Nc = 1;
  var e = cu(), r = oy(), t = r(e);
  return Ha = t, Ha;
}
var Wa, Dc;
function xr() {
  if (Dc) return Wa;
  Dc = 1;
  function e(r) {
    return r;
  }
  return Wa = e, Wa;
}
var Ya, Mc;
function hp() {
  if (Mc) return Ya;
  Mc = 1;
  var e = xr();
  function r(t) {
    return typeof t == "function" ? t : e;
  }
  return Ya = r, Ya;
}
var Qa, $c;
function pp() {
  if ($c) return Qa;
  $c = 1;
  var e = ru(), r = Pt(), t = hp(), n = Ee();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s));
  }
  return Qa = a, Qa;
}
var Ja, Lc;
function vp() {
  return Lc || (Lc = 1, Ja = pp()), Ja;
}
var Xa, jc;
function uy() {
  if (jc) return Xa;
  jc = 1;
  var e = Pt();
  function r(t, n) {
    var a = [];
    return e(t, function(i, s, o) {
      n(i, s, o) && a.push(i);
    }), a;
  }
  return Xa = r, Xa;
}
var Za, Fc;
function ly() {
  if (Fc) return Za;
  Fc = 1;
  var e = "__lodash_hash_undefined__";
  function r(t) {
    return this.__data__.set(t, e), this;
  }
  return Za = r, Za;
}
var ei, Gc;
function cy() {
  if (Gc) return ei;
  Gc = 1;
  function e(r) {
    return this.__data__.has(r);
  }
  return ei = e, ei;
}
var ri, zc;
function gp() {
  if (zc) return ri;
  zc = 1;
  var e = eu(), r = ly(), t = cy();
  function n(a) {
    var i = -1, s = a == null ? 0 : a.length;
    for (this.__data__ = new e(); ++i < s; )
      this.add(a[i]);
  }
  return n.prototype.add = n.prototype.push = r, n.prototype.has = t, ri = n, ri;
}
var ti, Bc;
function dy() {
  if (Bc) return ti;
  Bc = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length; ++n < a; )
      if (t(r[n], n, r))
        return !0;
    return !1;
  }
  return ti = e, ti;
}
var ni, Uc;
function yp() {
  if (Uc) return ni;
  Uc = 1;
  function e(r, t) {
    return r.has(t);
  }
  return ni = e, ni;
}
var ai, Kc;
function mp() {
  if (Kc) return ai;
  Kc = 1;
  var e = gp(), r = dy(), t = yp(), n = 1, a = 2;
  function i(s, o, u, c, l, d) {
    var h = u & n, v = s.length, g = o.length;
    if (v != g && !(h && g > v))
      return !1;
    var p = d.get(s), m = d.get(o);
    if (p && m)
      return p == o && m == s;
    var I = -1, k = !0, b = u & a ? new e() : void 0;
    for (d.set(s, o), d.set(o, s); ++I < v; ) {
      var A = s[I], M = o[I];
      if (c)
        var y = h ? c(M, A, I, o, s, d) : c(A, M, I, s, o, d);
      if (y !== void 0) {
        if (y)
          continue;
        k = !1;
        break;
      }
      if (b) {
        if (!r(o, function(x, O) {
          if (!t(b, O) && (A === x || l(A, x, u, c, d)))
            return b.push(O);
        })) {
          k = !1;
          break;
        }
      } else if (!(A === M || l(A, M, u, c, d))) {
        k = !1;
        break;
      }
    }
    return d.delete(s), d.delete(o), k;
  }
  return ai = i, ai;
}
var ii, Vc;
function fy() {
  if (Vc) return ii;
  Vc = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a, i) {
      n[++t] = [i, a];
    }), n;
  }
  return ii = e, ii;
}
var si, Hc;
function du() {
  if (Hc) return si;
  Hc = 1;
  function e(r) {
    var t = -1, n = Array(r.size);
    return r.forEach(function(a) {
      n[++t] = a;
    }), n;
  }
  return si = e, si;
}
var oi, Wc;
function hy() {
  if (Wc) return oi;
  Wc = 1;
  var e = Lr(), r = up(), t = $r(), n = mp(), a = fy(), i = du(), s = 1, o = 2, u = "[object Boolean]", c = "[object Date]", l = "[object Error]", d = "[object Map]", h = "[object Number]", v = "[object RegExp]", g = "[object Set]", p = "[object String]", m = "[object Symbol]", I = "[object ArrayBuffer]", k = "[object DataView]", b = e ? e.prototype : void 0, A = b ? b.valueOf : void 0;
  function M(y, x, O, V, F, R, G) {
    switch (O) {
      case k:
        if (y.byteLength != x.byteLength || y.byteOffset != x.byteOffset)
          return !1;
        y = y.buffer, x = x.buffer;
      case I:
        return !(y.byteLength != x.byteLength || !R(new r(y), new r(x)));
      case u:
      case c:
      case h:
        return t(+y, +x);
      case l:
        return y.name == x.name && y.message == x.message;
      case v:
      case p:
        return y == x + "";
      case d:
        var H = a;
      case g:
        var z = V & s;
        if (H || (H = i), y.size != x.size && !z)
          return !1;
        var ae = G.get(y);
        if (ae)
          return ae == x;
        V |= o, G.set(y, x);
        var te = n(H(y), H(x), V, F, R, G);
        return G.delete(y), te;
      case m:
        if (A)
          return A.call(y) == A.call(x);
    }
    return !1;
  }
  return oi = M, oi;
}
var ui, Yc;
function py() {
  if (Yc) return ui;
  Yc = 1;
  var e = sp(), r = 1, t = Object.prototype, n = t.hasOwnProperty;
  function a(i, s, o, u, c, l) {
    var d = o & r, h = e(i), v = h.length, g = e(s), p = g.length;
    if (v != p && !d)
      return !1;
    for (var m = v; m--; ) {
      var I = h[m];
      if (!(d ? I in s : n.call(s, I)))
        return !1;
    }
    var k = l.get(i), b = l.get(s);
    if (k && b)
      return k == s && b == i;
    var A = !0;
    l.set(i, s), l.set(s, i);
    for (var M = d; ++m < v; ) {
      I = h[m];
      var y = i[I], x = s[I];
      if (u)
        var O = d ? u(x, y, I, s, i, l) : u(y, x, I, i, s, l);
      if (!(O === void 0 ? y === x || c(y, x, o, u, l) : O)) {
        A = !1;
        break;
      }
      M || (M = I == "constructor");
    }
    if (A && !M) {
      var V = i.constructor, F = s.constructor;
      V != F && "constructor" in i && "constructor" in s && !(typeof V == "function" && V instanceof V && typeof F == "function" && F instanceof F) && (A = !1);
    }
    return l.delete(i), l.delete(s), A;
  }
  return ui = a, ui;
}
var li, Qc;
function vy() {
  if (Qc) return li;
  Qc = 1;
  var e = Et(), r = mp(), t = hy(), n = py(), a = Fr(), i = Ee(), s = jr(), o = at(), u = 1, c = "[object Arguments]", l = "[object Array]", d = "[object Object]", h = Object.prototype, v = h.hasOwnProperty;
  function g(p, m, I, k, b, A) {
    var M = i(p), y = i(m), x = M ? l : a(p), O = y ? l : a(m);
    x = x == c ? d : x, O = O == c ? d : O;
    var V = x == d, F = O == d, R = x == O;
    if (R && s(p)) {
      if (!s(m))
        return !1;
      M = !0, V = !1;
    }
    if (R && !V)
      return A || (A = new e()), M || o(p) ? r(p, m, I, k, b, A) : t(p, m, x, I, k, b, A);
    if (!(I & u)) {
      var G = V && v.call(p, "__wrapped__"), H = F && v.call(m, "__wrapped__");
      if (G || H) {
        var z = G ? p.value() : p, ae = H ? m.value() : m;
        return A || (A = new e()), b(z, ae, I, k, A);
      }
    }
    return R ? (A || (A = new e()), n(p, m, I, k, b, A)) : !1;
  }
  return li = g, li;
}
var ci, Jc;
function bp() {
  if (Jc) return ci;
  Jc = 1;
  var e = vy(), r = tr();
  function t(n, a, i, s, o) {
    return n === a ? !0 : n == null || a == null || !r(n) && !r(a) ? n !== n && a !== a : e(n, a, i, s, t, o);
  }
  return ci = t, ci;
}
var di, Xc;
function gy() {
  if (Xc) return di;
  Xc = 1;
  var e = Et(), r = bp(), t = 1, n = 2;
  function a(i, s, o, u) {
    var c = o.length, l = c, d = !u;
    if (i == null)
      return !l;
    for (i = Object(i); c--; ) {
      var h = o[c];
      if (d && h[2] ? h[1] !== i[h[0]] : !(h[0] in i))
        return !1;
    }
    for (; ++c < l; ) {
      h = o[c];
      var v = h[0], g = i[v], p = h[1];
      if (d && h[2]) {
        if (g === void 0 && !(v in i))
          return !1;
      } else {
        var m = new e();
        if (u)
          var I = u(g, p, v, i, s, m);
        if (!(I === void 0 ? r(p, g, t | n, u, m) : I))
          return !1;
      }
    }
    return !0;
  }
  return di = a, di;
}
var fi, Zc;
function _p() {
  if (Zc) return fi;
  Zc = 1;
  var e = Ve();
  function r(t) {
    return t === t && !e(t);
  }
  return fi = r, fi;
}
var hi, ed;
function yy() {
  if (ed) return hi;
  ed = 1;
  var e = _p(), r = Ir();
  function t(n) {
    for (var a = r(n), i = a.length; i--; ) {
      var s = a[i], o = n[s];
      a[i] = [s, o, e(o)];
    }
    return a;
  }
  return hi = t, hi;
}
var pi, rd;
function wp() {
  if (rd) return pi;
  rd = 1;
  function e(r, t) {
    return function(n) {
      return n == null ? !1 : n[r] === t && (t !== void 0 || r in Object(n));
    };
  }
  return pi = e, pi;
}
var vi, td;
function my() {
  if (td) return vi;
  td = 1;
  var e = gy(), r = yy(), t = wp();
  function n(a) {
    var i = r(a);
    return i.length == 1 && i[0][2] ? t(i[0][0], i[0][1]) : function(s) {
      return s === a || e(s, a, i);
    };
  }
  return vi = n, vi;
}
var gi, nd;
function Gr() {
  if (nd) return gi;
  nd = 1;
  var e = Sr(), r = tr(), t = "[object Symbol]";
  function n(a) {
    return typeof a == "symbol" || r(a) && e(a) == t;
  }
  return gi = n, gi;
}
var yi, ad;
function fu() {
  if (ad) return yi;
  ad = 1;
  var e = Ee(), r = Gr(), t = /\.|\[(?:[^[\]]*|(["'])(?:(?!\1)[^\\]|\\.)*?\1)\]/, n = /^\w*$/;
  function a(i, s) {
    if (e(i))
      return !1;
    var o = typeof i;
    return o == "number" || o == "symbol" || o == "boolean" || i == null || r(i) ? !0 : n.test(i) || !t.test(i) || s != null && i in Object(s);
  }
  return yi = a, yi;
}
var mi, id;
function by() {
  if (id) return mi;
  id = 1;
  var e = eu(), r = "Expected a function";
  function t(n, a) {
    if (typeof n != "function" || a != null && typeof a != "function")
      throw new TypeError(r);
    var i = function() {
      var s = arguments, o = a ? a.apply(this, s) : s[0], u = i.cache;
      if (u.has(o))
        return u.get(o);
      var c = n.apply(this, s);
      return i.cache = u.set(o, c) || u, c;
    };
    return i.cache = new (t.Cache || e)(), i;
  }
  return t.Cache = e, mi = t, mi;
}
var bi, sd;
function _y() {
  if (sd) return bi;
  sd = 1;
  var e = by(), r = 500;
  function t(n) {
    var a = e(n, function(s) {
      return i.size === r && i.clear(), s;
    }), i = a.cache;
    return a;
  }
  return bi = t, bi;
}
var _i, od;
function wy() {
  if (od) return _i;
  od = 1;
  var e = _y(), r = /[^.[\]]+|\[(?:(-?\d+(?:\.\d+)?)|(["'])((?:(?!\2)[^\\]|\\.)*?)\2)\]|(?=(?:\.|\[\])(?:\.|\[\]|$))/g, t = /\\(\\)?/g, n = e(function(a) {
    var i = [];
    return a.charCodeAt(0) === 46 && i.push(""), a.replace(r, function(s, o, u, c) {
      i.push(u ? c.replace(t, "$1") : o || s);
    }), i;
  });
  return _i = n, _i;
}
var wi, ud;
function Ot() {
  if (ud) return wi;
  ud = 1;
  function e(r, t) {
    for (var n = -1, a = r == null ? 0 : r.length, i = Array(a); ++n < a; )
      i[n] = t(r[n], n, r);
    return i;
  }
  return wi = e, wi;
}
var Ii, ld;
function Iy() {
  if (ld) return Ii;
  ld = 1;
  var e = Lr(), r = Ot(), t = Ee(), n = Gr(), a = e ? e.prototype : void 0, i = a ? a.toString : void 0;
  function s(o) {
    if (typeof o == "string")
      return o;
    if (t(o))
      return r(o, s) + "";
    if (n(o))
      return i ? i.call(o) : "";
    var u = o + "";
    return u == "0" && 1 / o == -1 / 0 ? "-0" : u;
  }
  return Ii = s, Ii;
}
var qi, cd;
function Ip() {
  if (cd) return qi;
  cd = 1;
  var e = Iy();
  function r(t) {
    return t == null ? "" : e(t);
  }
  return qi = r, qi;
}
var Ci, dd;
function Nt() {
  if (dd) return Ci;
  dd = 1;
  var e = Ee(), r = fu(), t = wy(), n = Ip();
  function a(i, s) {
    return e(i) ? i : r(i, s) ? [i] : t(n(i));
  }
  return Ci = a, Ci;
}
var Ei, fd;
function it() {
  if (fd) return Ei;
  fd = 1;
  var e = Gr();
  function r(t) {
    if (typeof t == "string" || e(t))
      return t;
    var n = t + "";
    return n == "0" && 1 / t == -1 / 0 ? "-0" : n;
  }
  return Ei = r, Ei;
}
var Si, hd;
function Dt() {
  if (hd) return Si;
  hd = 1;
  var e = Nt(), r = it();
  function t(n, a) {
    a = e(a, n);
    for (var i = 0, s = a.length; n != null && i < s; )
      n = n[r(a[i++])];
    return i && i == s ? n : void 0;
  }
  return Si = t, Si;
}
var Ri, pd;
function qy() {
  if (pd) return Ri;
  pd = 1;
  var e = Dt();
  function r(t, n, a) {
    var i = t == null ? void 0 : e(t, n);
    return i === void 0 ? a : i;
  }
  return Ri = r, Ri;
}
var Ti, vd;
function Cy() {
  if (vd) return Ti;
  vd = 1;
  function e(r, t) {
    return r != null && t in Object(r);
  }
  return Ti = e, Ti;
}
var xi, gd;
function qp() {
  if (gd) return xi;
  gd = 1;
  var e = Nt(), r = nt(), t = Ee(), n = Tt(), a = tu(), i = it();
  function s(o, u, c) {
    u = e(u, o);
    for (var l = -1, d = u.length, h = !1; ++l < d; ) {
      var v = i(u[l]);
      if (!(h = o != null && c(o, v)))
        break;
      o = o[v];
    }
    return h || ++l != d ? h : (d = o == null ? 0 : o.length, !!d && a(d) && n(v, d) && (t(o) || r(o)));
  }
  return xi = s, xi;
}
var ki, yd;
function Cp() {
  if (yd) return ki;
  yd = 1;
  var e = Cy(), r = qp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return ki = t, ki;
}
var Ai, md;
function Ey() {
  if (md) return Ai;
  md = 1;
  var e = bp(), r = qy(), t = Cp(), n = fu(), a = _p(), i = wp(), s = it(), o = 1, u = 2;
  function c(l, d) {
    return n(l) && a(d) ? i(s(l), d) : function(h) {
      var v = r(h, l);
      return v === void 0 && v === d ? t(h, l) : e(d, v, o | u);
    };
  }
  return Ai = c, Ai;
}
var Pi, bd;
function Ep() {
  if (bd) return Pi;
  bd = 1;
  function e(r) {
    return function(t) {
      return t == null ? void 0 : t[r];
    };
  }
  return Pi = e, Pi;
}
var Oi, _d;
function Sy() {
  if (_d) return Oi;
  _d = 1;
  var e = Dt();
  function r(t) {
    return function(n) {
      return e(n, t);
    };
  }
  return Oi = r, Oi;
}
var Ni, wd;
function Ry() {
  if (wd) return Ni;
  wd = 1;
  var e = Ep(), r = Sy(), t = fu(), n = it();
  function a(i) {
    return t(i) ? e(n(i)) : r(i);
  }
  return Ni = a, Ni;
}
var Di, Id;
function hr() {
  if (Id) return Di;
  Id = 1;
  var e = my(), r = Ey(), t = xr(), n = Ee(), a = Ry();
  function i(s) {
    return typeof s == "function" ? s : s == null ? t : typeof s == "object" ? n(s) ? r(s[0], s[1]) : e(s) : a(s);
  }
  return Di = i, Di;
}
var Mi, qd;
function Sp() {
  if (qd) return Mi;
  qd = 1;
  var e = tp(), r = uy(), t = hr(), n = Ee();
  function a(i, s) {
    var o = n(i) ? e : r;
    return o(i, t(s, 3));
  }
  return Mi = a, Mi;
}
var $i, Cd;
function Ty() {
  if (Cd) return $i;
  Cd = 1;
  var e = Object.prototype, r = e.hasOwnProperty;
  function t(n, a) {
    return n != null && r.call(n, a);
  }
  return $i = t, $i;
}
var Li, Ed;
function Rp() {
  if (Ed) return Li;
  Ed = 1;
  var e = Ty(), r = qp();
  function t(n, a) {
    return n != null && r(n, a, e);
  }
  return Li = t, Li;
}
var ji, Sd;
function xy() {
  if (Sd) return ji;
  Sd = 1;
  var e = au(), r = Fr(), t = nt(), n = Ee(), a = fr(), i = jr(), s = kt(), o = at(), u = "[object Map]", c = "[object Set]", l = Object.prototype, d = l.hasOwnProperty;
  function h(v) {
    if (v == null)
      return !0;
    if (a(v) && (n(v) || typeof v == "string" || typeof v.splice == "function" || i(v) || o(v) || t(v)))
      return !v.length;
    var g = r(v);
    if (g == u || g == c)
      return !v.size;
    if (s(v))
      return !e(v).length;
    for (var p in v)
      if (d.call(v, p))
        return !1;
    return !0;
  }
  return ji = h, ji;
}
var Fi, Rd;
function Tp() {
  if (Rd) return Fi;
  Rd = 1;
  function e(r) {
    return r === void 0;
  }
  return Fi = e, Fi;
}
var Gi, Td;
function xp() {
  if (Td) return Gi;
  Td = 1;
  var e = Pt(), r = fr();
  function t(n, a) {
    var i = -1, s = r(n) ? Array(n.length) : [];
    return e(n, function(o, u, c) {
      s[++i] = a(o, u, c);
    }), s;
  }
  return Gi = t, Gi;
}
var zi, xd;
function kp() {
  if (xd) return zi;
  xd = 1;
  var e = Ot(), r = hr(), t = xp(), n = Ee();
  function a(i, s) {
    var o = n(i) ? e : t;
    return o(i, r(s, 3));
  }
  return zi = a, zi;
}
var Bi, kd;
function ky() {
  if (kd) return Bi;
  kd = 1;
  function e(r, t, n, a) {
    var i = -1, s = r == null ? 0 : r.length;
    for (a && s && (n = r[++i]); ++i < s; )
      n = t(n, r[i], i, r);
    return n;
  }
  return Bi = e, Bi;
}
var Ui, Ad;
function Ay() {
  if (Ad) return Ui;
  Ad = 1;
  function e(r, t, n, a, i) {
    return i(r, function(s, o, u) {
      n = a ? (a = !1, s) : t(n, s, o, u);
    }), n;
  }
  return Ui = e, Ui;
}
var Ki, Pd;
function Ap() {
  if (Pd) return Ki;
  Pd = 1;
  var e = ky(), r = Pt(), t = hr(), n = Ay(), a = Ee();
  function i(s, o, u) {
    var c = a(s) ? e : n, l = arguments.length < 3;
    return c(s, t(o, 4), u, l, r);
  }
  return Ki = i, Ki;
}
var Vi, Od;
function Py() {
  if (Od) return Vi;
  Od = 1;
  var e = Sr(), r = Ee(), t = tr(), n = "[object String]";
  function a(i) {
    return typeof i == "string" || !r(i) && t(i) && e(i) == n;
  }
  return Vi = a, Vi;
}
var Hi, Nd;
function Oy() {
  if (Nd) return Hi;
  Nd = 1;
  var e = Ep(), r = e("length");
  return Hi = r, Hi;
}
var Wi, Dd;
function Ny() {
  if (Dd) return Wi;
  Dd = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "\\u200d", o = RegExp("[" + s + e + a + i + "]");
  function u(c) {
    return o.test(c);
  }
  return Wi = u, Wi;
}
var Yi, Md;
function Dy() {
  if (Md) return Yi;
  Md = 1;
  var e = "\\ud800-\\udfff", r = "\\u0300-\\u036f", t = "\\ufe20-\\ufe2f", n = "\\u20d0-\\u20ff", a = r + t + n, i = "\\ufe0e\\ufe0f", s = "[" + e + "]", o = "[" + a + "]", u = "\\ud83c[\\udffb-\\udfff]", c = "(?:" + o + "|" + u + ")", l = "[^" + e + "]", d = "(?:\\ud83c[\\udde6-\\uddff]){2}", h = "[\\ud800-\\udbff][\\udc00-\\udfff]", v = "\\u200d", g = c + "?", p = "[" + i + "]?", m = "(?:" + v + "(?:" + [l, d, h].join("|") + ")" + p + g + ")*", I = p + g + m, k = "(?:" + [l + o + "?", o, d, h, s].join("|") + ")", b = RegExp(u + "(?=" + u + ")|" + k + I, "g");
  function A(M) {
    for (var y = b.lastIndex = 0; b.test(M); )
      ++y;
    return y;
  }
  return Yi = A, Yi;
}
var Qi, $d;
function My() {
  if ($d) return Qi;
  $d = 1;
  var e = Oy(), r = Ny(), t = Dy();
  function n(a) {
    return r(a) ? t(a) : e(a);
  }
  return Qi = n, Qi;
}
var Ji, Ld;
function $y() {
  if (Ld) return Ji;
  Ld = 1;
  var e = au(), r = Fr(), t = fr(), n = Py(), a = My(), i = "[object Map]", s = "[object Set]";
  function o(u) {
    if (u == null)
      return 0;
    if (t(u))
      return n(u) ? a(u) : u.length;
    var c = r(u);
    return c == i || c == s ? u.size : e(u).length;
  }
  return Ji = o, Ji;
}
var Xi, jd;
function Ly() {
  if (jd) return Xi;
  jd = 1;
  var e = ru(), r = cp(), t = cu(), n = hr(), a = At(), i = Ee(), s = jr(), o = rt(), u = Ve(), c = at();
  function l(d, h, v) {
    var g = i(d), p = g || s(d) || c(d);
    if (h = n(h, 4), v == null) {
      var m = d && d.constructor;
      p ? v = g ? new m() : [] : u(d) ? v = o(m) ? r(a(d)) : {} : v = {};
    }
    return (p ? e : t)(d, function(I, k, b) {
      return h(v, I, k, b);
    }), v;
  }
  return Xi = l, Xi;
}
var Zi, Fd;
function jy() {
  if (Fd) return Zi;
  Fd = 1;
  var e = Lr(), r = nt(), t = Ee(), n = e ? e.isConcatSpreadable : void 0;
  function a(i) {
    return t(i) || r(i) || !!(n && i && i[n]);
  }
  return Zi = a, Zi;
}
var es, Gd;
function hu() {
  if (Gd) return es;
  Gd = 1;
  var e = su(), r = jy();
  function t(n, a, i, s, o) {
    var u = -1, c = n.length;
    for (i || (i = r), o || (o = []); ++u < c; ) {
      var l = n[u];
      a > 0 && i(l) ? a > 1 ? t(l, a - 1, i, s, o) : e(o, l) : s || (o[o.length] = l);
    }
    return o;
  }
  return es = t, es;
}
var rs, zd;
function Fy() {
  if (zd) return rs;
  zd = 1;
  function e(r, t, n) {
    switch (n.length) {
      case 0:
        return r.call(t);
      case 1:
        return r.call(t, n[0]);
      case 2:
        return r.call(t, n[0], n[1]);
      case 3:
        return r.call(t, n[0], n[1], n[2]);
    }
    return r.apply(t, n);
  }
  return rs = e, rs;
}
var ts, Bd;
function Pp() {
  if (Bd) return ts;
  Bd = 1;
  var e = Fy(), r = Math.max;
  function t(n, a, i) {
    return a = r(a === void 0 ? n.length - 1 : a, 0), function() {
      for (var s = arguments, o = -1, u = r(s.length - a, 0), c = Array(u); ++o < u; )
        c[o] = s[a + o];
      o = -1;
      for (var l = Array(a + 1); ++o < a; )
        l[o] = s[o];
      return l[a] = i(c), e(n, this, l);
    };
  }
  return ts = t, ts;
}
var ns, Ud;
function Gy() {
  if (Ud) return ns;
  Ud = 1;
  var e = uu(), r = Jh(), t = xr(), n = r ? function(a, i) {
    return r(a, "toString", {
      configurable: !0,
      enumerable: !1,
      value: e(i),
      writable: !0
    });
  } : t;
  return ns = n, ns;
}
var as, Kd;
function zy() {
  if (Kd) return as;
  Kd = 1;
  var e = 800, r = 16, t = Date.now;
  function n(a) {
    var i = 0, s = 0;
    return function() {
      var o = t(), u = r - (o - s);
      if (s = o, u > 0) {
        if (++i >= e)
          return arguments[0];
      } else
        i = 0;
      return a.apply(void 0, arguments);
    };
  }
  return as = n, as;
}
var is, Vd;
function Op() {
  if (Vd) return is;
  Vd = 1;
  var e = Gy(), r = zy(), t = r(e);
  return is = t, is;
}
var ss, Hd;
function Mt() {
  if (Hd) return ss;
  Hd = 1;
  var e = xr(), r = Pp(), t = Op();
  function n(a, i) {
    return t(r(a, i, e), a + "");
  }
  return ss = n, ss;
}
var os, Wd;
function Np() {
  if (Wd) return os;
  Wd = 1;
  function e(r, t, n, a) {
    for (var i = r.length, s = n + (a ? 1 : -1); a ? s-- : ++s < i; )
      if (t(r[s], s, r))
        return s;
    return -1;
  }
  return os = e, os;
}
var us, Yd;
function By() {
  if (Yd) return us;
  Yd = 1;
  function e(r) {
    return r !== r;
  }
  return us = e, us;
}
var ls, Qd;
function Uy() {
  if (Qd) return ls;
  Qd = 1;
  function e(r, t, n) {
    for (var a = n - 1, i = r.length; ++a < i; )
      if (r[a] === t)
        return a;
    return -1;
  }
  return ls = e, ls;
}
var cs, Jd;
function Ky() {
  if (Jd) return cs;
  Jd = 1;
  var e = Np(), r = By(), t = Uy();
  function n(a, i, s) {
    return i === i ? t(a, i, s) : e(a, r, s);
  }
  return cs = n, cs;
}
var ds, Xd;
function Vy() {
  if (Xd) return ds;
  Xd = 1;
  var e = Ky();
  function r(t, n) {
    var a = t == null ? 0 : t.length;
    return !!a && e(t, n, 0) > -1;
  }
  return ds = r, ds;
}
var fs, Zd;
function Hy() {
  if (Zd) return fs;
  Zd = 1;
  function e(r, t, n) {
    for (var a = -1, i = r == null ? 0 : r.length; ++a < i; )
      if (n(t, r[a]))
        return !0;
    return !1;
  }
  return fs = e, fs;
}
var hs, ef;
function Wy() {
  if (ef) return hs;
  ef = 1;
  function e() {
  }
  return hs = e, hs;
}
var ps, rf;
function Yy() {
  if (rf) return ps;
  rf = 1;
  var e = op(), r = Wy(), t = du(), n = 1 / 0, a = e && 1 / t(new e([, -0]))[1] == n ? function(i) {
    return new e(i);
  } : r;
  return ps = a, ps;
}
var vs, tf;
function Qy() {
  if (tf) return vs;
  tf = 1;
  var e = gp(), r = Vy(), t = Hy(), n = yp(), a = Yy(), i = du(), s = 200;
  function o(u, c, l) {
    var d = -1, h = r, v = u.length, g = !0, p = [], m = p;
    if (l)
      g = !1, h = t;
    else if (v >= s) {
      var I = c ? null : a(u);
      if (I)
        return i(I);
      g = !1, h = n, m = new e();
    } else
      m = c ? [] : p;
    e:
      for (; ++d < v; ) {
        var k = u[d], b = c ? c(k) : k;
        if (k = l || k !== 0 ? k : 0, g && b === b) {
          for (var A = m.length; A--; )
            if (m[A] === b)
              continue e;
          c && m.push(b), p.push(k);
        } else h(m, b, l) || (m !== p && m.push(b), p.push(k));
      }
    return p;
  }
  return vs = o, vs;
}
var gs, nf;
function Dp() {
  if (nf) return gs;
  nf = 1;
  var e = fr(), r = tr();
  function t(n) {
    return r(n) && e(n);
  }
  return gs = t, gs;
}
var ys, af;
function Jy() {
  if (af) return ys;
  af = 1;
  var e = hu(), r = Mt(), t = Qy(), n = Dp(), a = r(function(i) {
    return t(e(i, 1, n, !0));
  });
  return ys = a, ys;
}
var ms, sf;
function Xy() {
  if (sf) return ms;
  sf = 1;
  var e = Ot();
  function r(t, n) {
    return e(n, function(a) {
      return t[a];
    });
  }
  return ms = r, ms;
}
var bs, of;
function Mp() {
  if (of) return bs;
  of = 1;
  var e = Xy(), r = Ir();
  function t(n) {
    return n == null ? [] : e(n, r(n));
  }
  return bs = t, bs;
}
var _s, uf;
function He() {
  if (uf) return _s;
  uf = 1;
  var e;
  if (typeof Xo == "function")
    try {
      e = {
        clone: iy(),
        constant: uu(),
        each: vp(),
        filter: Sp(),
        has: Rp(),
        isArray: Ee(),
        isEmpty: xy(),
        isFunction: rt(),
        isUndefined: Tp(),
        keys: Ir(),
        map: kp(),
        reduce: Ap(),
        size: $y(),
        transform: Ly(),
        union: Jy(),
        values: Mp()
      };
    } catch {
    }
  return e || (e = window._), _s = e, _s;
}
var ws, lf;
function pu() {
  if (lf) return ws;
  lf = 1;
  var e = He();
  ws = a;
  var r = "\0", t = "\0", n = "";
  function a(l) {
    this._isDirected = e.has(l, "directed") ? l.directed : !0, this._isMultigraph = e.has(l, "multigraph") ? l.multigraph : !1, this._isCompound = e.has(l, "compound") ? l.compound : !1, this._label = void 0, this._defaultNodeLabelFn = e.constant(void 0), this._defaultEdgeLabelFn = e.constant(void 0), this._nodes = {}, this._isCompound && (this._parent = {}, this._children = {}, this._children[t] = {}), this._in = {}, this._preds = {}, this._out = {}, this._sucs = {}, this._edgeObjs = {}, this._edgeLabels = {};
  }
  a.prototype._nodeCount = 0, a.prototype._edgeCount = 0, a.prototype.isDirected = function() {
    return this._isDirected;
  }, a.prototype.isMultigraph = function() {
    return this._isMultigraph;
  }, a.prototype.isCompound = function() {
    return this._isCompound;
  }, a.prototype.setGraph = function(l) {
    return this._label = l, this;
  }, a.prototype.graph = function() {
    return this._label;
  }, a.prototype.setDefaultNodeLabel = function(l) {
    return e.isFunction(l) || (l = e.constant(l)), this._defaultNodeLabelFn = l, this;
  }, a.prototype.nodeCount = function() {
    return this._nodeCount;
  }, a.prototype.nodes = function() {
    return e.keys(this._nodes);
  }, a.prototype.sources = function() {
    var l = this;
    return e.filter(this.nodes(), function(d) {
      return e.isEmpty(l._in[d]);
    });
  }, a.prototype.sinks = function() {
    var l = this;
    return e.filter(this.nodes(), function(d) {
      return e.isEmpty(l._out[d]);
    });
  }, a.prototype.setNodes = function(l, d) {
    var h = arguments, v = this;
    return e.each(l, function(g) {
      h.length > 1 ? v.setNode(g, d) : v.setNode(g);
    }), this;
  }, a.prototype.setNode = function(l, d) {
    return e.has(this._nodes, l) ? (arguments.length > 1 && (this._nodes[l] = d), this) : (this._nodes[l] = arguments.length > 1 ? d : this._defaultNodeLabelFn(l), this._isCompound && (this._parent[l] = t, this._children[l] = {}, this._children[t][l] = !0), this._in[l] = {}, this._preds[l] = {}, this._out[l] = {}, this._sucs[l] = {}, ++this._nodeCount, this);
  }, a.prototype.node = function(l) {
    return this._nodes[l];
  }, a.prototype.hasNode = function(l) {
    return e.has(this._nodes, l);
  }, a.prototype.removeNode = function(l) {
    var d = this;
    if (e.has(this._nodes, l)) {
      var h = function(v) {
        d.removeEdge(d._edgeObjs[v]);
      };
      delete this._nodes[l], this._isCompound && (this._removeFromParentsChildList(l), delete this._parent[l], e.each(this.children(l), function(v) {
        d.setParent(v);
      }), delete this._children[l]), e.each(e.keys(this._in[l]), h), delete this._in[l], delete this._preds[l], e.each(e.keys(this._out[l]), h), delete this._out[l], delete this._sucs[l], --this._nodeCount;
    }
    return this;
  }, a.prototype.setParent = function(l, d) {
    if (!this._isCompound)
      throw new Error("Cannot set parent in a non-compound graph");
    if (e.isUndefined(d))
      d = t;
    else {
      d += "";
      for (var h = d; !e.isUndefined(h); h = this.parent(h))
        if (h === l)
          throw new Error("Setting " + d + " as parent of " + l + " would create a cycle");
      this.setNode(d);
    }
    return this.setNode(l), this._removeFromParentsChildList(l), this._parent[l] = d, this._children[d][l] = !0, this;
  }, a.prototype._removeFromParentsChildList = function(l) {
    delete this._children[this._parent[l]][l];
  }, a.prototype.parent = function(l) {
    if (this._isCompound) {
      var d = this._parent[l];
      if (d !== t)
        return d;
    }
  }, a.prototype.children = function(l) {
    if (e.isUndefined(l) && (l = t), this._isCompound) {
      var d = this._children[l];
      if (d)
        return e.keys(d);
    } else {
      if (l === t)
        return this.nodes();
      if (this.hasNode(l))
        return [];
    }
  }, a.prototype.predecessors = function(l) {
    var d = this._preds[l];
    if (d)
      return e.keys(d);
  }, a.prototype.successors = function(l) {
    var d = this._sucs[l];
    if (d)
      return e.keys(d);
  }, a.prototype.neighbors = function(l) {
    var d = this.predecessors(l);
    if (d)
      return e.union(d, this.successors(l));
  }, a.prototype.isLeaf = function(l) {
    var d;
    return this.isDirected() ? d = this.successors(l) : d = this.neighbors(l), d.length === 0;
  }, a.prototype.filterNodes = function(l) {
    var d = new this.constructor({
      directed: this._isDirected,
      multigraph: this._isMultigraph,
      compound: this._isCompound
    });
    d.setGraph(this.graph());
    var h = this;
    e.each(this._nodes, function(p, m) {
      l(m) && d.setNode(m, p);
    }), e.each(this._edgeObjs, function(p) {
      d.hasNode(p.v) && d.hasNode(p.w) && d.setEdge(p, h.edge(p));
    });
    var v = {};
    function g(p) {
      var m = h.parent(p);
      return m === void 0 || d.hasNode(m) ? (v[p] = m, m) : m in v ? v[m] : g(m);
    }
    return this._isCompound && e.each(d.nodes(), function(p) {
      d.setParent(p, g(p));
    }), d;
  }, a.prototype.setDefaultEdgeLabel = function(l) {
    return e.isFunction(l) || (l = e.constant(l)), this._defaultEdgeLabelFn = l, this;
  }, a.prototype.edgeCount = function() {
    return this._edgeCount;
  }, a.prototype.edges = function() {
    return e.values(this._edgeObjs);
  }, a.prototype.setPath = function(l, d) {
    var h = this, v = arguments;
    return e.reduce(l, function(g, p) {
      return v.length > 1 ? h.setEdge(g, p, d) : h.setEdge(g, p), p;
    }), this;
  }, a.prototype.setEdge = function() {
    var l, d, h, v, g = !1, p = arguments[0];
    typeof p == "object" && p !== null && "v" in p ? (l = p.v, d = p.w, h = p.name, arguments.length === 2 && (v = arguments[1], g = !0)) : (l = p, d = arguments[1], h = arguments[3], arguments.length > 2 && (v = arguments[2], g = !0)), l = "" + l, d = "" + d, e.isUndefined(h) || (h = "" + h);
    var m = o(this._isDirected, l, d, h);
    if (e.has(this._edgeLabels, m))
      return g && (this._edgeLabels[m] = v), this;
    if (!e.isUndefined(h) && !this._isMultigraph)
      throw new Error("Cannot set a named edge when isMultigraph = false");
    this.setNode(l), this.setNode(d), this._edgeLabels[m] = g ? v : this._defaultEdgeLabelFn(l, d, h);
    var I = u(this._isDirected, l, d, h);
    return l = I.v, d = I.w, Object.freeze(I), this._edgeObjs[m] = I, i(this._preds[d], l), i(this._sucs[l], d), this._in[d][m] = I, this._out[l][m] = I, this._edgeCount++, this;
  }, a.prototype.edge = function(l, d, h) {
    var v = arguments.length === 1 ? c(this._isDirected, arguments[0]) : o(this._isDirected, l, d, h);
    return this._edgeLabels[v];
  }, a.prototype.hasEdge = function(l, d, h) {
    var v = arguments.length === 1 ? c(this._isDirected, arguments[0]) : o(this._isDirected, l, d, h);
    return e.has(this._edgeLabels, v);
  }, a.prototype.removeEdge = function(l, d, h) {
    var v = arguments.length === 1 ? c(this._isDirected, arguments[0]) : o(this._isDirected, l, d, h), g = this._edgeObjs[v];
    return g && (l = g.v, d = g.w, delete this._edgeLabels[v], delete this._edgeObjs[v], s(this._preds[d], l), s(this._sucs[l], d), delete this._in[d][v], delete this._out[l][v], this._edgeCount--), this;
  }, a.prototype.inEdges = function(l, d) {
    var h = this._in[l];
    if (h) {
      var v = e.values(h);
      return d ? e.filter(v, function(g) {
        return g.v === d;
      }) : v;
    }
  }, a.prototype.outEdges = function(l, d) {
    var h = this._out[l];
    if (h) {
      var v = e.values(h);
      return d ? e.filter(v, function(g) {
        return g.w === d;
      }) : v;
    }
  }, a.prototype.nodeEdges = function(l, d) {
    var h = this.inEdges(l, d);
    if (h)
      return h.concat(this.outEdges(l, d));
  };
  function i(l, d) {
    l[d] ? l[d]++ : l[d] = 1;
  }
  function s(l, d) {
    --l[d] || delete l[d];
  }
  function o(l, d, h, v) {
    var g = "" + d, p = "" + h;
    if (!l && g > p) {
      var m = g;
      g = p, p = m;
    }
    return g + n + p + n + (e.isUndefined(v) ? r : v);
  }
  function u(l, d, h, v) {
    var g = "" + d, p = "" + h;
    if (!l && g > p) {
      var m = g;
      g = p, p = m;
    }
    var I = { v: g, w: p };
    return v && (I.name = v), I;
  }
  function c(l, d) {
    return o(l, d.v, d.w, d.name);
  }
  return ws;
}
var Is, cf;
function Zy() {
  return cf || (cf = 1, Is = "2.1.8"), Is;
}
var qs, df;
function em() {
  return df || (df = 1, qs = {
    Graph: pu(),
    version: Zy()
  }), qs;
}
var Cs, ff;
function rm() {
  if (ff) return Cs;
  ff = 1;
  var e = He(), r = pu();
  Cs = {
    write: t,
    read: i
  };
  function t(s) {
    var o = {
      options: {
        directed: s.isDirected(),
        multigraph: s.isMultigraph(),
        compound: s.isCompound()
      },
      nodes: n(s),
      edges: a(s)
    };
    return e.isUndefined(s.graph()) || (o.value = e.clone(s.graph())), o;
  }
  function n(s) {
    return e.map(s.nodes(), function(o) {
      var u = s.node(o), c = s.parent(o), l = { v: o };
      return e.isUndefined(u) || (l.value = u), e.isUndefined(c) || (l.parent = c), l;
    });
  }
  function a(s) {
    return e.map(s.edges(), function(o) {
      var u = s.edge(o), c = { v: o.v, w: o.w };
      return e.isUndefined(o.name) || (c.name = o.name), e.isUndefined(u) || (c.value = u), c;
    });
  }
  function i(s) {
    var o = new r(s.options).setGraph(s.value);
    return e.each(s.nodes, function(u) {
      o.setNode(u.v, u.value), u.parent && o.setParent(u.v, u.parent);
    }), e.each(s.edges, function(u) {
      o.setEdge({ v: u.v, w: u.w, name: u.name }, u.value);
    }), o;
  }
  return Cs;
}
var Es, hf;
function tm() {
  if (hf) return Es;
  hf = 1;
  var e = He();
  Es = r;
  function r(t) {
    var n = {}, a = [], i;
    function s(o) {
      e.has(n, o) || (n[o] = !0, i.push(o), e.each(t.successors(o), s), e.each(t.predecessors(o), s));
    }
    return e.each(t.nodes(), function(o) {
      i = [], s(o), i.length && a.push(i);
    }), a;
  }
  return Es;
}
var Ss, pf;
function $p() {
  if (pf) return Ss;
  pf = 1;
  var e = He();
  Ss = r;
  function r() {
    this._arr = [], this._keyIndices = {};
  }
  return r.prototype.size = function() {
    return this._arr.length;
  }, r.prototype.keys = function() {
    return this._arr.map(function(t) {
      return t.key;
    });
  }, r.prototype.has = function(t) {
    return e.has(this._keyIndices, t);
  }, r.prototype.priority = function(t) {
    var n = this._keyIndices[t];
    if (n !== void 0)
      return this._arr[n].priority;
  }, r.prototype.min = function() {
    if (this.size() === 0)
      throw new Error("Queue underflow");
    return this._arr[0].key;
  }, r.prototype.add = function(t, n) {
    var a = this._keyIndices;
    if (t = String(t), !e.has(a, t)) {
      var i = this._arr, s = i.length;
      return a[t] = s, i.push({ key: t, priority: n }), this._decrease(s), !0;
    }
    return !1;
  }, r.prototype.removeMin = function() {
    this._swap(0, this._arr.length - 1);
    var t = this._arr.pop();
    return delete this._keyIndices[t.key], this._heapify(0), t.key;
  }, r.prototype.decrease = function(t, n) {
    var a = this._keyIndices[t];
    if (n > this._arr[a].priority)
      throw new Error("New priority is greater than current priority. Key: " + t + " Old: " + this._arr[a].priority + " New: " + n);
    this._arr[a].priority = n, this._decrease(a);
  }, r.prototype._heapify = function(t) {
    var n = this._arr, a = 2 * t, i = a + 1, s = t;
    a < n.length && (s = n[a].priority < n[s].priority ? a : s, i < n.length && (s = n[i].priority < n[s].priority ? i : s), s !== t && (this._swap(t, s), this._heapify(s)));
  }, r.prototype._decrease = function(t) {
    for (var n = this._arr, a = n[t].priority, i; t !== 0 && (i = t >> 1, !(n[i].priority < a)); )
      this._swap(t, i), t = i;
  }, r.prototype._swap = function(t, n) {
    var a = this._arr, i = this._keyIndices, s = a[t], o = a[n];
    a[t] = o, a[n] = s, i[o.key] = t, i[s.key] = n;
  }, Ss;
}
var Rs, vf;
function Lp() {
  if (vf) return Rs;
  vf = 1;
  var e = He(), r = $p();
  Rs = n;
  var t = e.constant(1);
  function n(i, s, o, u) {
    return a(
      i,
      String(s),
      o || t,
      u || function(c) {
        return i.outEdges(c);
      }
    );
  }
  function a(i, s, o, u) {
    var c = {}, l = new r(), d, h, v = function(g) {
      var p = g.v !== d ? g.v : g.w, m = c[p], I = o(g), k = h.distance + I;
      if (I < 0)
        throw new Error("dijkstra does not allow negative edge weights. Bad edge: " + g + " Weight: " + I);
      k < m.distance && (m.distance = k, m.predecessor = d, l.decrease(p, k));
    };
    for (i.nodes().forEach(function(g) {
      var p = g === s ? 0 : Number.POSITIVE_INFINITY;
      c[g] = { distance: p }, l.add(g, p);
    }); l.size() > 0 && (d = l.removeMin(), h = c[d], h.distance !== Number.POSITIVE_INFINITY); )
      u(d).forEach(v);
    return c;
  }
  return Rs;
}
var Ts, gf;
function nm() {
  if (gf) return Ts;
  gf = 1;
  var e = Lp(), r = He();
  Ts = t;
  function t(n, a, i) {
    return r.transform(n.nodes(), function(s, o) {
      s[o] = e(n, o, a, i);
    }, {});
  }
  return Ts;
}
var xs, yf;
function jp() {
  if (yf) return xs;
  yf = 1;
  var e = He();
  xs = r;
  function r(t) {
    var n = 0, a = [], i = {}, s = [];
    function o(u) {
      var c = i[u] = {
        onStack: !0,
        lowlink: n,
        index: n++
      };
      if (a.push(u), t.successors(u).forEach(function(h) {
        e.has(i, h) ? i[h].onStack && (c.lowlink = Math.min(c.lowlink, i[h].index)) : (o(h), c.lowlink = Math.min(c.lowlink, i[h].lowlink));
      }), c.lowlink === c.index) {
        var l = [], d;
        do
          d = a.pop(), i[d].onStack = !1, l.push(d);
        while (u !== d);
        s.push(l);
      }
    }
    return t.nodes().forEach(function(u) {
      e.has(i, u) || o(u);
    }), s;
  }
  return xs;
}
var ks, mf;
function am() {
  if (mf) return ks;
  mf = 1;
  var e = He(), r = jp();
  ks = t;
  function t(n) {
    return e.filter(r(n), function(a) {
      return a.length > 1 || a.length === 1 && n.hasEdge(a[0], a[0]);
    });
  }
  return ks;
}
var As, bf;
function im() {
  if (bf) return As;
  bf = 1;
  var e = He();
  As = t;
  var r = e.constant(1);
  function t(a, i, s) {
    return n(
      a,
      i || r,
      s || function(o) {
        return a.outEdges(o);
      }
    );
  }
  function n(a, i, s) {
    var o = {}, u = a.nodes();
    return u.forEach(function(c) {
      o[c] = {}, o[c][c] = { distance: 0 }, u.forEach(function(l) {
        c !== l && (o[c][l] = { distance: Number.POSITIVE_INFINITY });
      }), s(c).forEach(function(l) {
        var d = l.v === c ? l.w : l.v, h = i(l);
        o[c][d] = { distance: h, predecessor: c };
      });
    }), u.forEach(function(c) {
      var l = o[c];
      u.forEach(function(d) {
        var h = o[d];
        u.forEach(function(v) {
          var g = h[c], p = l[v], m = h[v], I = g.distance + p.distance;
          I < m.distance && (m.distance = I, m.predecessor = p.predecessor);
        });
      });
    }), o;
  }
  return As;
}
var Ps, _f;
function Fp() {
  if (_f) return Ps;
  _f = 1;
  var e = He();
  Ps = r, r.CycleException = t;
  function r(n) {
    var a = {}, i = {}, s = [];
    function o(u) {
      if (e.has(i, u))
        throw new t();
      e.has(a, u) || (i[u] = !0, a[u] = !0, e.each(n.predecessors(u), o), delete i[u], s.push(u));
    }
    if (e.each(n.sinks(), o), e.size(a) !== n.nodeCount())
      throw new t();
    return s;
  }
  function t() {
  }
  return t.prototype = new Error(), Ps;
}
var Os, wf;
function sm() {
  if (wf) return Os;
  wf = 1;
  var e = Fp();
  Os = r;
  function r(t) {
    try {
      e(t);
    } catch (n) {
      if (n instanceof e.CycleException)
        return !1;
      throw n;
    }
    return !0;
  }
  return Os;
}
var Ns, If;
function Gp() {
  if (If) return Ns;
  If = 1;
  var e = He();
  Ns = r;
  function r(n, a, i) {
    e.isArray(a) || (a = [a]);
    var s = (n.isDirected() ? n.successors : n.neighbors).bind(n), o = [], u = {};
    return e.each(a, function(c) {
      if (!n.hasNode(c))
        throw new Error("Graph does not have node: " + c);
      t(n, c, i === "post", u, s, o);
    }), o;
  }
  function t(n, a, i, s, o, u) {
    e.has(s, a) || (s[a] = !0, i || u.push(a), e.each(o(a), function(c) {
      t(n, c, i, s, o, u);
    }), i && u.push(a));
  }
  return Ns;
}
var Ds, qf;
function om() {
  if (qf) return Ds;
  qf = 1;
  var e = Gp();
  Ds = r;
  function r(t, n) {
    return e(t, n, "post");
  }
  return Ds;
}
var Ms, Cf;
function um() {
  if (Cf) return Ms;
  Cf = 1;
  var e = Gp();
  Ms = r;
  function r(t, n) {
    return e(t, n, "pre");
  }
  return Ms;
}
var $s, Ef;
function lm() {
  if (Ef) return $s;
  Ef = 1;
  var e = He(), r = pu(), t = $p();
  $s = n;
  function n(a, i) {
    var s = new r(), o = {}, u = new t(), c;
    function l(h) {
      var v = h.v === c ? h.w : h.v, g = u.priority(v);
      if (g !== void 0) {
        var p = i(h);
        p < g && (o[v] = c, u.decrease(v, p));
      }
    }
    if (a.nodeCount() === 0)
      return s;
    e.each(a.nodes(), function(h) {
      u.add(h, Number.POSITIVE_INFINITY), s.setNode(h);
    }), u.decrease(a.nodes()[0], 0);
    for (var d = !1; u.size() > 0; ) {
      if (c = u.removeMin(), e.has(o, c))
        s.setEdge(c, o[c]);
      else {
        if (d)
          throw new Error("Input graph is not connected: " + a);
        d = !0;
      }
      a.nodeEdges(c).forEach(l);
    }
    return s;
  }
  return $s;
}
var Ls, Sf;
function cm() {
  return Sf || (Sf = 1, Ls = {
    components: tm(),
    dijkstra: Lp(),
    dijkstraAll: nm(),
    findCycles: am(),
    floydWarshall: im(),
    isAcyclic: sm(),
    postorder: om(),
    preorder: um(),
    prim: lm(),
    tarjan: jp(),
    topsort: Fp()
  }), Ls;
}
var js, Rf;
function dm() {
  if (Rf) return js;
  Rf = 1;
  var e = em();
  return js = {
    Graph: e.Graph,
    json: rm(),
    alg: cm(),
    version: e.version
  }, js;
}
var yt;
if (typeof Xo == "function")
  try {
    yt = dm();
  } catch {
  }
yt || (yt = window.graphlib);
var Ze = yt, Fs, Tf;
function fm() {
  if (Tf) return Fs;
  Tf = 1;
  var e = fp(), r = 1, t = 4;
  function n(a) {
    return e(a, r | t);
  }
  return Fs = n, Fs;
}
var Gs, xf;
function $t() {
  if (xf) return Gs;
  xf = 1;
  var e = $r(), r = fr(), t = Tt(), n = Ve();
  function a(i, s, o) {
    if (!n(o))
      return !1;
    var u = typeof s;
    return (u == "number" ? r(o) && t(s, o.length) : u == "string" && s in o) ? e(o[s], i) : !1;
  }
  return Gs = a, Gs;
}
var zs, kf;
function hm() {
  if (kf) return zs;
  kf = 1;
  var e = Mt(), r = $r(), t = $t(), n = Tr(), a = Object.prototype, i = a.hasOwnProperty, s = e(function(o, u) {
    o = Object(o);
    var c = -1, l = u.length, d = l > 2 ? u[2] : void 0;
    for (d && t(u[0], u[1], d) && (l = 1); ++c < l; )
      for (var h = u[c], v = n(h), g = -1, p = v.length; ++g < p; ) {
        var m = v[g], I = o[m];
        (I === void 0 || r(I, a[m]) && !i.call(o, m)) && (o[m] = h[m]);
      }
    return o;
  });
  return zs = s, zs;
}
var Bs, Af;
function pm() {
  if (Af) return Bs;
  Af = 1;
  var e = hr(), r = fr(), t = Ir();
  function n(a) {
    return function(i, s, o) {
      var u = Object(i);
      if (!r(i)) {
        var c = e(s, 3);
        i = t(i), s = function(d) {
          return c(u[d], d, u);
        };
      }
      var l = a(i, s, o);
      return l > -1 ? u[c ? i[l] : l] : void 0;
    };
  }
  return Bs = n, Bs;
}
var Us, Pf;
function vm() {
  if (Pf) return Us;
  Pf = 1;
  var e = /\s/;
  function r(t) {
    for (var n = t.length; n-- && e.test(t.charAt(n)); )
      ;
    return n;
  }
  return Us = r, Us;
}
var Ks, Of;
function gm() {
  if (Of) return Ks;
  Of = 1;
  var e = vm(), r = /^\s+/;
  function t(n) {
    return n && n.slice(0, e(n) + 1).replace(r, "");
  }
  return Ks = t, Ks;
}
var Vs, Nf;
function ym() {
  if (Nf) return Vs;
  Nf = 1;
  var e = gm(), r = Ve(), t = Gr(), n = NaN, a = /^[-+]0x[0-9a-f]+$/i, i = /^0b[01]+$/i, s = /^0o[0-7]+$/i, o = parseInt;
  function u(c) {
    if (typeof c == "number")
      return c;
    if (t(c))
      return n;
    if (r(c)) {
      var l = typeof c.valueOf == "function" ? c.valueOf() : c;
      c = r(l) ? l + "" : l;
    }
    if (typeof c != "string")
      return c === 0 ? c : +c;
    c = e(c);
    var d = i.test(c);
    return d || s.test(c) ? o(c.slice(2), d ? 2 : 8) : a.test(c) ? n : +c;
  }
  return Vs = u, Vs;
}
var Hs, Df;
function zp() {
  if (Df) return Hs;
  Df = 1;
  var e = ym(), r = 1 / 0, t = 17976931348623157e292;
  function n(a) {
    if (!a)
      return a === 0 ? a : 0;
    if (a = e(a), a === r || a === -r) {
      var i = a < 0 ? -1 : 1;
      return i * t;
    }
    return a === a ? a : 0;
  }
  return Hs = n, Hs;
}
var Ws, Mf;
function mm() {
  if (Mf) return Ws;
  Mf = 1;
  var e = zp();
  function r(t) {
    var n = e(t), a = n % 1;
    return n === n ? a ? n - a : n : 0;
  }
  return Ws = r, Ws;
}
var Ys, $f;
function bm() {
  if ($f) return Ys;
  $f = 1;
  var e = Np(), r = hr(), t = mm(), n = Math.max;
  function a(i, s, o) {
    var u = i == null ? 0 : i.length;
    if (!u)
      return -1;
    var c = o == null ? 0 : t(o);
    return c < 0 && (c = n(u + c, 0)), e(i, r(s, 3), c);
  }
  return Ys = a, Ys;
}
var Qs, Lf;
function _m() {
  if (Lf) return Qs;
  Lf = 1;
  var e = pm(), r = bm(), t = e(r);
  return Qs = t, Qs;
}
var Js, jf;
function Bp() {
  if (jf) return Js;
  jf = 1;
  var e = hu();
  function r(t) {
    var n = t == null ? 0 : t.length;
    return n ? e(t, 1) : [];
  }
  return Js = r, Js;
}
var Xs, Ff;
function wm() {
  if (Ff) return Xs;
  Ff = 1;
  var e = lu(), r = hp(), t = Tr();
  function n(a, i) {
    return a == null ? a : e(a, r(i), t);
  }
  return Xs = n, Xs;
}
var Zs, Gf;
function Im() {
  if (Gf) return Zs;
  Gf = 1;
  function e(r) {
    var t = r == null ? 0 : r.length;
    return t ? r[t - 1] : void 0;
  }
  return Zs = e, Zs;
}
var eo, zf;
function qm() {
  if (zf) return eo;
  zf = 1;
  var e = St(), r = cu(), t = hr();
  function n(a, i) {
    var s = {};
    return i = t(i, 3), r(a, function(o, u, c) {
      e(s, u, i(o, u, c));
    }), s;
  }
  return eo = n, eo;
}
var ro, Bf;
function vu() {
  if (Bf) return ro;
  Bf = 1;
  var e = Gr();
  function r(t, n, a) {
    for (var i = -1, s = t.length; ++i < s; ) {
      var o = t[i], u = n(o);
      if (u != null && (c === void 0 ? u === u && !e(u) : a(u, c)))
        var c = u, l = o;
    }
    return l;
  }
  return ro = r, ro;
}
var to, Uf;
function Cm() {
  if (Uf) return to;
  Uf = 1;
  function e(r, t) {
    return r > t;
  }
  return to = e, to;
}
var no, Kf;
function Em() {
  if (Kf) return no;
  Kf = 1;
  var e = vu(), r = Cm(), t = xr();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return no = n, no;
}
var ao, Vf;
function Up() {
  if (Vf) return ao;
  Vf = 1;
  var e = St(), r = $r();
  function t(n, a, i) {
    (i !== void 0 && !r(n[a], i) || i === void 0 && !(a in n)) && e(n, a, i);
  }
  return ao = t, ao;
}
var io, Hf;
function Sm() {
  if (Hf) return io;
  Hf = 1;
  var e = Sr(), r = At(), t = tr(), n = "[object Object]", a = Function.prototype, i = Object.prototype, s = a.toString, o = i.hasOwnProperty, u = s.call(Object);
  function c(l) {
    if (!t(l) || e(l) != n)
      return !1;
    var d = r(l);
    if (d === null)
      return !0;
    var h = o.call(d, "constructor") && d.constructor;
    return typeof h == "function" && h instanceof h && s.call(h) == u;
  }
  return io = c, io;
}
var so, Wf;
function Kp() {
  if (Wf) return so;
  Wf = 1;
  function e(r, t) {
    if (!(t === "constructor" && typeof r[t] == "function") && t != "__proto__")
      return r[t];
  }
  return so = e, so;
}
var oo, Yf;
function Rm() {
  if (Yf) return oo;
  Yf = 1;
  var e = tt(), r = Tr();
  function t(n) {
    return e(n, r(n));
  }
  return oo = t, oo;
}
var uo, Qf;
function Tm() {
  if (Qf) return uo;
  Qf = 1;
  var e = Up(), r = ep(), t = lp(), n = rp(), a = dp(), i = nt(), s = Ee(), o = Dp(), u = jr(), c = rt(), l = Ve(), d = Sm(), h = at(), v = Kp(), g = Rm();
  function p(m, I, k, b, A, M, y) {
    var x = v(m, k), O = v(I, k), V = y.get(O);
    if (V) {
      e(m, k, V);
      return;
    }
    var F = M ? M(x, O, k + "", m, I, y) : void 0, R = F === void 0;
    if (R) {
      var G = s(O), H = !G && u(O), z = !G && !H && h(O);
      F = O, G || H || z ? s(x) ? F = x : o(x) ? F = n(x) : H ? (R = !1, F = r(O, !0)) : z ? (R = !1, F = t(O, !0)) : F = [] : d(O) || i(O) ? (F = x, i(x) ? F = g(x) : (!l(x) || c(x)) && (F = a(O))) : R = !1;
    }
    R && (y.set(O, F), A(F, O, b, M, y), y.delete(O)), e(m, k, F);
  }
  return uo = p, uo;
}
var lo, Jf;
function xm() {
  if (Jf) return lo;
  Jf = 1;
  var e = Et(), r = Up(), t = lu(), n = Tm(), a = Ve(), i = Tr(), s = Kp();
  function o(u, c, l, d, h) {
    u !== c && t(c, function(v, g) {
      if (h || (h = new e()), a(v))
        n(u, c, g, l, o, d, h);
      else {
        var p = d ? d(s(u, g), v, g + "", u, c, h) : void 0;
        p === void 0 && (p = v), r(u, g, p);
      }
    }, i);
  }
  return lo = o, lo;
}
var co, Xf;
function km() {
  if (Xf) return co;
  Xf = 1;
  var e = Mt(), r = $t();
  function t(n) {
    return e(function(a, i) {
      var s = -1, o = i.length, u = o > 1 ? i[o - 1] : void 0, c = o > 2 ? i[2] : void 0;
      for (u = n.length > 3 && typeof u == "function" ? (o--, u) : void 0, c && r(i[0], i[1], c) && (u = o < 3 ? void 0 : u, o = 1), a = Object(a); ++s < o; ) {
        var l = i[s];
        l && n(a, l, s, u);
      }
      return a;
    });
  }
  return co = t, co;
}
var fo, Zf;
function Am() {
  if (Zf) return fo;
  Zf = 1;
  var e = xm(), r = km(), t = r(function(n, a, i) {
    e(n, a, i);
  });
  return fo = t, fo;
}
var ho, eh;
function Vp() {
  if (eh) return ho;
  eh = 1;
  function e(r, t) {
    return r < t;
  }
  return ho = e, ho;
}
var po, rh;
function Pm() {
  if (rh) return po;
  rh = 1;
  var e = vu(), r = Vp(), t = xr();
  function n(a) {
    return a && a.length ? e(a, t, r) : void 0;
  }
  return po = n, po;
}
var vo, th;
function Om() {
  if (th) return vo;
  th = 1;
  var e = vu(), r = hr(), t = Vp();
  function n(a, i) {
    return a && a.length ? e(a, r(i, 2), t) : void 0;
  }
  return vo = n, vo;
}
var go, nh;
function Nm() {
  if (nh) return go;
  nh = 1;
  var e = Xe(), r = function() {
    return e.Date.now();
  };
  return go = r, go;
}
var yo, ah;
function Dm() {
  if (ah) return yo;
  ah = 1;
  var e = Rt(), r = Nt(), t = Tt(), n = Ve(), a = it();
  function i(s, o, u, c) {
    if (!n(s))
      return s;
    o = r(o, s);
    for (var l = -1, d = o.length, h = d - 1, v = s; v != null && ++l < d; ) {
      var g = a(o[l]), p = u;
      if (g === "__proto__" || g === "constructor" || g === "prototype")
        return s;
      if (l != h) {
        var m = v[g];
        p = c ? c(m, g, v) : void 0, p === void 0 && (p = n(m) ? m : t(o[l + 1]) ? [] : {});
      }
      e(v, g, p), v = v[g];
    }
    return s;
  }
  return yo = i, yo;
}
var mo, ih;
function Mm() {
  if (ih) return mo;
  ih = 1;
  var e = Dt(), r = Dm(), t = Nt();
  function n(a, i, s) {
    for (var o = -1, u = i.length, c = {}; ++o < u; ) {
      var l = i[o], d = e(a, l);
      s(d, l) && r(c, t(l, a), d);
    }
    return c;
  }
  return mo = n, mo;
}
var bo, sh;
function $m() {
  if (sh) return bo;
  sh = 1;
  var e = Mm(), r = Cp();
  function t(n, a) {
    return e(n, a, function(i, s) {
      return r(n, s);
    });
  }
  return bo = t, bo;
}
var _o, oh;
function Lm() {
  if (oh) return _o;
  oh = 1;
  var e = Bp(), r = Pp(), t = Op();
  function n(a) {
    return t(r(a, void 0, e), a + "");
  }
  return _o = n, _o;
}
var wo, uh;
function jm() {
  if (uh) return wo;
  uh = 1;
  var e = $m(), r = Lm(), t = r(function(n, a) {
    return n == null ? {} : e(n, a);
  });
  return wo = t, wo;
}
var Io, lh;
function Fm() {
  if (lh) return Io;
  lh = 1;
  var e = Math.ceil, r = Math.max;
  function t(n, a, i, s) {
    for (var o = -1, u = r(e((a - n) / (i || 1)), 0), c = Array(u); u--; )
      c[s ? u : ++o] = n, n += i;
    return c;
  }
  return Io = t, Io;
}
var qo, ch;
function Gm() {
  if (ch) return qo;
  ch = 1;
  var e = Fm(), r = $t(), t = zp();
  function n(a) {
    return function(i, s, o) {
      return o && typeof o != "number" && r(i, s, o) && (s = o = void 0), i = t(i), s === void 0 ? (s = i, i = 0) : s = t(s), o = o === void 0 ? i < s ? 1 : -1 : t(o), e(i, s, o, a);
    };
  }
  return qo = n, qo;
}
var Co, dh;
function zm() {
  if (dh) return Co;
  dh = 1;
  var e = Gm(), r = e();
  return Co = r, Co;
}
var Eo, fh;
function Bm() {
  if (fh) return Eo;
  fh = 1;
  function e(r, t) {
    var n = r.length;
    for (r.sort(t); n--; )
      r[n] = r[n].value;
    return r;
  }
  return Eo = e, Eo;
}
var So, hh;
function Um() {
  if (hh) return So;
  hh = 1;
  var e = Gr();
  function r(t, n) {
    if (t !== n) {
      var a = t !== void 0, i = t === null, s = t === t, o = e(t), u = n !== void 0, c = n === null, l = n === n, d = e(n);
      if (!c && !d && !o && t > n || o && u && l && !c && !d || i && u && l || !a && l || !s)
        return 1;
      if (!i && !o && !d && t < n || d && a && s && !i && !o || c && a && s || !u && s || !l)
        return -1;
    }
    return 0;
  }
  return So = r, So;
}
var Ro, ph;
function Km() {
  if (ph) return Ro;
  ph = 1;
  var e = Um();
  function r(t, n, a) {
    for (var i = -1, s = t.criteria, o = n.criteria, u = s.length, c = a.length; ++i < u; ) {
      var l = e(s[i], o[i]);
      if (l) {
        if (i >= c)
          return l;
        var d = a[i];
        return l * (d == "desc" ? -1 : 1);
      }
    }
    return t.index - n.index;
  }
  return Ro = r, Ro;
}
var To, vh;
function Vm() {
  if (vh) return To;
  vh = 1;
  var e = Ot(), r = Dt(), t = hr(), n = xp(), a = Bm(), i = xt(), s = Km(), o = xr(), u = Ee();
  function c(l, d, h) {
    d.length ? d = e(d, function(p) {
      return u(p) ? function(m) {
        return r(m, p.length === 1 ? p[0] : p);
      } : p;
    }) : d = [o];
    var v = -1;
    d = e(d, i(t));
    var g = n(l, function(p, m, I) {
      var k = e(d, function(b) {
        return b(p);
      });
      return { criteria: k, index: ++v, value: p };
    });
    return a(g, function(p, m) {
      return s(p, m, h);
    });
  }
  return To = c, To;
}
var xo, gh;
function Hm() {
  if (gh) return xo;
  gh = 1;
  var e = hu(), r = Vm(), t = Mt(), n = $t(), a = t(function(i, s) {
    if (i == null)
      return [];
    var o = s.length;
    return o > 1 && n(i, s[0], s[1]) ? s = [] : o > 2 && n(s[0], s[1], s[2]) && (s = [s[0]]), r(i, e(s, 1), []);
  });
  return xo = a, xo;
}
var ko, yh;
function Wm() {
  if (yh) return ko;
  yh = 1;
  var e = Ip(), r = 0;
  function t(n) {
    var a = ++r;
    return e(n) + a;
  }
  return ko = t, ko;
}
var Ao, mh;
function Ym() {
  if (mh) return Ao;
  mh = 1;
  function e(r, t, n) {
    for (var a = -1, i = r.length, s = t.length, o = {}; ++a < i; ) {
      var u = a < s ? t[a] : void 0;
      n(o, r[a], u);
    }
    return o;
  }
  return Ao = e, Ao;
}
var Po, bh;
function Qm() {
  if (bh) return Po;
  bh = 1;
  var e = Rt(), r = Ym();
  function t(n, a) {
    return r(n || [], a || [], e);
  }
  return Po = t, Po;
}
var mt;
if (typeof Xo == "function")
  try {
    mt = {
      cloneDeep: fm(),
      constant: uu(),
      defaults: hm(),
      each: vp(),
      filter: Sp(),
      find: _m(),
      flatten: Bp(),
      forEach: pp(),
      forIn: wm(),
      has: Rp(),
      isUndefined: Tp(),
      last: Im(),
      map: kp(),
      mapValues: qm(),
      max: Em(),
      merge: Am(),
      min: Pm(),
      minBy: Om(),
      now: Nm(),
      pick: jm(),
      range: zm(),
      reduce: Ap(),
      sortBy: Hm(),
      uniqueId: Wm(),
      values: Mp(),
      zipObject: Qm()
    };
  } catch {
  }
mt || (mt = window._);
var be = mt, Jm = Lt;
function Lt() {
  var e = {};
  e._next = e._prev = e, this._sentinel = e;
}
Lt.prototype.dequeue = function() {
  var e = this._sentinel, r = e._prev;
  if (r !== e)
    return Hp(r), r;
};
Lt.prototype.enqueue = function(e) {
  var r = this._sentinel;
  e._prev && e._next && Hp(e), e._next = r._next, r._next._prev = e, r._next = e, e._prev = r;
};
Lt.prototype.toString = function() {
  for (var e = [], r = this._sentinel, t = r._prev; t !== r; )
    e.push(JSON.stringify(t, Xm)), t = t._prev;
  return "[" + e.join(", ") + "]";
};
function Hp(e) {
  e._prev._next = e._next, e._next._prev = e._prev, delete e._next, delete e._prev;
}
function Xm(e, r) {
  if (e !== "_next" && e !== "_prev")
    return r;
}
var lr = be, Zm = Ze.Graph, eb = Jm, rb = nb, tb = lr.constant(1);
function nb(e, r) {
  if (e.nodeCount() <= 1)
    return [];
  var t = ib(e, r || tb), n = ab(t.graph, t.buckets, t.zeroIdx);
  return lr.flatten(lr.map(n, function(a) {
    return e.outEdges(a.v, a.w);
  }), !0);
}
function ab(e, r, t) {
  for (var n = [], a = r[r.length - 1], i = r[0], s; e.nodeCount(); ) {
    for (; s = i.dequeue(); )
      Oo(e, r, t, s);
    for (; s = a.dequeue(); )
      Oo(e, r, t, s);
    if (e.nodeCount()) {
      for (var o = r.length - 2; o > 0; --o)
        if (s = r[o].dequeue(), s) {
          n = n.concat(Oo(e, r, t, s, !0));
          break;
        }
    }
  }
  return n;
}
function Oo(e, r, t, n, a) {
  var i = a ? [] : void 0;
  return lr.forEach(e.inEdges(n.v), function(s) {
    var o = e.edge(s), u = e.node(s.v);
    a && i.push({ v: s.v, w: s.w }), u.out -= o, Ho(r, t, u);
  }), lr.forEach(e.outEdges(n.v), function(s) {
    var o = e.edge(s), u = s.w, c = e.node(u);
    c.in -= o, Ho(r, t, c);
  }), e.removeNode(n.v), i;
}
function ib(e, r) {
  var t = new Zm(), n = 0, a = 0;
  lr.forEach(e.nodes(), function(o) {
    t.setNode(o, { v: o, in: 0, out: 0 });
  }), lr.forEach(e.edges(), function(o) {
    var u = t.edge(o.v, o.w) || 0, c = r(o), l = u + c;
    t.setEdge(o.v, o.w, l), a = Math.max(a, t.node(o.v).out += c), n = Math.max(n, t.node(o.w).in += c);
  });
  var i = lr.range(a + n + 3).map(function() {
    return new eb();
  }), s = n + 1;
  return lr.forEach(t.nodes(), function(o) {
    Ho(i, s, t.node(o));
  }), { graph: t, buckets: i, zeroIdx: s };
}
function Ho(e, r, t) {
  t.out ? t.in ? e[t.out - t.in + r].enqueue(t) : e[e.length - 1].enqueue(t) : e[0].enqueue(t);
}
var Cr = be, sb = rb, ob = {
  run: ub,
  undo: cb
};
function ub(e) {
  var r = e.graph().acyclicer === "greedy" ? sb(e, t(e)) : lb(e);
  Cr.forEach(r, function(n) {
    var a = e.edge(n);
    e.removeEdge(n), a.forwardName = n.name, a.reversed = !0, e.setEdge(n.w, n.v, a, Cr.uniqueId("rev"));
  });
  function t(n) {
    return function(a) {
      return n.edge(a).weight;
    };
  }
}
function lb(e) {
  var r = [], t = {}, n = {};
  function a(i) {
    Cr.has(n, i) || (n[i] = !0, t[i] = !0, Cr.forEach(e.outEdges(i), function(s) {
      Cr.has(t, s.w) ? r.push(s) : a(s.w);
    }), delete t[i]);
  }
  return Cr.forEach(e.nodes(), a), r;
}
function cb(e) {
  Cr.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (t.reversed) {
      e.removeEdge(r);
      var n = t.forwardName;
      delete t.reversed, delete t.forwardName, e.setEdge(r.w, r.v, t, n);
    }
  });
}
var le = be, Wp = Ze.Graph, Me = {
  addDummyNode: Yp,
  simplify: db,
  asNonCompoundGraph: fb,
  successorWeights: hb,
  predecessorWeights: pb,
  intersectRect: vb,
  buildLayerMatrix: gb,
  normalizeRanks: yb,
  removeEmptyRanks: mb,
  addBorderNode: bb,
  maxRank: Qp,
  partition: _b,
  time: wb,
  notime: Ib
};
function Yp(e, r, t, n) {
  var a;
  do
    a = le.uniqueId(n);
  while (e.hasNode(a));
  return t.dummy = r, e.setNode(a, t), a;
}
function db(e) {
  var r = new Wp().setGraph(e.graph());
  return le.forEach(e.nodes(), function(t) {
    r.setNode(t, e.node(t));
  }), le.forEach(e.edges(), function(t) {
    var n = r.edge(t.v, t.w) || { weight: 0, minlen: 1 }, a = e.edge(t);
    r.setEdge(t.v, t.w, {
      weight: n.weight + a.weight,
      minlen: Math.max(n.minlen, a.minlen)
    });
  }), r;
}
function fb(e) {
  var r = new Wp({ multigraph: e.isMultigraph() }).setGraph(e.graph());
  return le.forEach(e.nodes(), function(t) {
    e.children(t).length || r.setNode(t, e.node(t));
  }), le.forEach(e.edges(), function(t) {
    r.setEdge(t, e.edge(t));
  }), r;
}
function hb(e) {
  var r = le.map(e.nodes(), function(t) {
    var n = {};
    return le.forEach(e.outEdges(t), function(a) {
      n[a.w] = (n[a.w] || 0) + e.edge(a).weight;
    }), n;
  });
  return le.zipObject(e.nodes(), r);
}
function pb(e) {
  var r = le.map(e.nodes(), function(t) {
    var n = {};
    return le.forEach(e.inEdges(t), function(a) {
      n[a.v] = (n[a.v] || 0) + e.edge(a).weight;
    }), n;
  });
  return le.zipObject(e.nodes(), r);
}
function vb(e, r) {
  var t = e.x, n = e.y, a = r.x - t, i = r.y - n, s = e.width / 2, o = e.height / 2;
  if (!a && !i)
    throw new Error("Not possible to find intersection inside of the rectangle");
  var u, c;
  return Math.abs(i) * s > Math.abs(a) * o ? (i < 0 && (o = -o), u = o * a / i, c = o) : (a < 0 && (s = -s), u = s, c = s * i / a), { x: t + u, y: n + c };
}
function gb(e) {
  var r = le.map(le.range(Qp(e) + 1), function() {
    return [];
  });
  return le.forEach(e.nodes(), function(t) {
    var n = e.node(t), a = n.rank;
    le.isUndefined(a) || (r[a][n.order] = t);
  }), r;
}
function yb(e) {
  var r = le.min(le.map(e.nodes(), function(t) {
    return e.node(t).rank;
  }));
  le.forEach(e.nodes(), function(t) {
    var n = e.node(t);
    le.has(n, "rank") && (n.rank -= r);
  });
}
function mb(e) {
  var r = le.min(le.map(e.nodes(), function(i) {
    return e.node(i).rank;
  })), t = [];
  le.forEach(e.nodes(), function(i) {
    var s = e.node(i).rank - r;
    t[s] || (t[s] = []), t[s].push(i);
  });
  var n = 0, a = e.graph().nodeRankFactor;
  le.forEach(t, function(i, s) {
    le.isUndefined(i) && s % a !== 0 ? --n : n && le.forEach(i, function(o) {
      e.node(o).rank += n;
    });
  });
}
function bb(e, r, t, n) {
  var a = {
    width: 0,
    height: 0
  };
  return arguments.length >= 4 && (a.rank = t, a.order = n), Yp(e, "border", a, r);
}
function Qp(e) {
  return le.max(le.map(e.nodes(), function(r) {
    var t = e.node(r).rank;
    if (!le.isUndefined(t))
      return t;
  }));
}
function _b(e, r) {
  var t = { lhs: [], rhs: [] };
  return le.forEach(e, function(n) {
    r(n) ? t.lhs.push(n) : t.rhs.push(n);
  }), t;
}
function wb(e, r) {
  var t = le.now();
  try {
    return r();
  } finally {
    console.log(e + " time: " + (le.now() - t) + "ms");
  }
}
function Ib(e, r) {
  return r();
}
var Jp = be, qb = Me, Cb = {
  run: Eb,
  undo: Rb
};
function Eb(e) {
  e.graph().dummyChains = [], Jp.forEach(e.edges(), function(r) {
    Sb(e, r);
  });
}
function Sb(e, r) {
  var t = r.v, n = e.node(t).rank, a = r.w, i = e.node(a).rank, s = r.name, o = e.edge(r), u = o.labelRank;
  if (i !== n + 1) {
    e.removeEdge(r);
    var c, l, d;
    for (d = 0, ++n; n < i; ++d, ++n)
      o.points = [], l = {
        width: 0,
        height: 0,
        edgeLabel: o,
        edgeObj: r,
        rank: n
      }, c = qb.addDummyNode(e, "edge", l, "_d"), n === u && (l.width = o.width, l.height = o.height, l.dummy = "edge-label", l.labelpos = o.labelpos), e.setEdge(t, c, { weight: o.weight }, s), d === 0 && e.graph().dummyChains.push(c), t = c;
    e.setEdge(t, a, { weight: o.weight }, s);
  }
}
function Rb(e) {
  Jp.forEach(e.graph().dummyChains, function(r) {
    var t = e.node(r), n = t.edgeLabel, a;
    for (e.setEdge(t.edgeObj, n); t.dummy; )
      a = e.successors(r)[0], e.removeNode(r), n.points.push({ x: t.x, y: t.y }), t.dummy === "edge-label" && (n.x = t.x, n.y = t.y, n.width = t.width, n.height = t.height), r = a, t = e.node(r);
  });
}
var lt = be, jt = {
  longestPath: Tb,
  slack: xb
};
function Tb(e) {
  var r = {};
  function t(n) {
    var a = e.node(n);
    if (lt.has(r, n))
      return a.rank;
    r[n] = !0;
    var i = lt.min(lt.map(e.outEdges(n), function(s) {
      return t(s.w) - e.edge(s).minlen;
    }));
    return (i === Number.POSITIVE_INFINITY || // return value of _.map([]) for Lodash 3
    i === void 0 || // return value of _.map([]) for Lodash 4
    i === null) && (i = 0), a.rank = i;
  }
  lt.forEach(e.sources(), t);
}
function xb(e, r) {
  return e.node(r.w).rank - e.node(r.v).rank - e.edge(r).minlen;
}
var bt = be, kb = Ze.Graph, _t = jt.slack, Xp = Ab;
function Ab(e) {
  var r = new kb({ directed: !1 }), t = e.nodes()[0], n = e.nodeCount();
  r.setNode(t, {});
  for (var a, i; Pb(r, e) < n; )
    a = Ob(r, e), i = r.hasNode(a.v) ? _t(e, a) : -_t(e, a), Nb(r, e, i);
  return r;
}
function Pb(e, r) {
  function t(n) {
    bt.forEach(r.nodeEdges(n), function(a) {
      var i = a.v, s = n === i ? a.w : i;
      !e.hasNode(s) && !_t(r, a) && (e.setNode(s, {}), e.setEdge(n, s, {}), t(s));
    });
  }
  return bt.forEach(e.nodes(), t), e.nodeCount();
}
function Ob(e, r) {
  return bt.minBy(r.edges(), function(t) {
    if (e.hasNode(t.v) !== e.hasNode(t.w))
      return _t(r, t);
  });
}
function Nb(e, r, t) {
  bt.forEach(e.nodes(), function(n) {
    r.node(n).rank += t;
  });
}
var dr = be, Db = Xp, Mb = jt.slack, $b = jt.longestPath, Lb = Ze.alg.preorder, jb = Ze.alg.postorder, Fb = Me.simplify, Gb = kr;
kr.initLowLimValues = yu;
kr.initCutValues = gu;
kr.calcCutValue = Zp;
kr.leaveEdge = rv;
kr.enterEdge = tv;
kr.exchangeEdges = nv;
function kr(e) {
  e = Fb(e), $b(e);
  var r = Db(e);
  yu(r), gu(r, e);
  for (var t, n; t = rv(r); )
    n = tv(r, e, t), nv(r, e, t, n);
}
function gu(e, r) {
  var t = jb(e, e.nodes());
  t = t.slice(0, t.length - 1), dr.forEach(t, function(n) {
    zb(e, r, n);
  });
}
function zb(e, r, t) {
  var n = e.node(t), a = n.parent;
  e.edge(t, a).cutvalue = Zp(e, r, t);
}
function Zp(e, r, t) {
  var n = e.node(t), a = n.parent, i = !0, s = r.edge(t, a), o = 0;
  return s || (i = !1, s = r.edge(a, t)), o = s.weight, dr.forEach(r.nodeEdges(t), function(u) {
    var c = u.v === t, l = c ? u.w : u.v;
    if (l !== a) {
      var d = c === i, h = r.edge(u).weight;
      if (o += d ? h : -h, Ub(e, t, l)) {
        var v = e.edge(t, l).cutvalue;
        o += d ? -v : v;
      }
    }
  }), o;
}
function yu(e, r) {
  arguments.length < 2 && (r = e.nodes()[0]), ev(e, {}, 1, r);
}
function ev(e, r, t, n, a) {
  var i = t, s = e.node(n);
  return r[n] = !0, dr.forEach(e.neighbors(n), function(o) {
    dr.has(r, o) || (t = ev(e, r, t, o, n));
  }), s.low = i, s.lim = t++, a ? s.parent = a : delete s.parent, t;
}
function rv(e) {
  return dr.find(e.edges(), function(r) {
    return e.edge(r).cutvalue < 0;
  });
}
function tv(e, r, t) {
  var n = t.v, a = t.w;
  r.hasEdge(n, a) || (n = t.w, a = t.v);
  var i = e.node(n), s = e.node(a), o = i, u = !1;
  i.lim > s.lim && (o = s, u = !0);
  var c = dr.filter(r.edges(), function(l) {
    return u === _h(e, e.node(l.v), o) && u !== _h(e, e.node(l.w), o);
  });
  return dr.minBy(c, function(l) {
    return Mb(r, l);
  });
}
function nv(e, r, t, n) {
  var a = t.v, i = t.w;
  e.removeEdge(a, i), e.setEdge(n.v, n.w, {}), yu(e), gu(e, r), Bb(e, r);
}
function Bb(e, r) {
  var t = dr.find(e.nodes(), function(a) {
    return !r.node(a).parent;
  }), n = Lb(e, t);
  n = n.slice(1), dr.forEach(n, function(a) {
    var i = e.node(a).parent, s = r.edge(a, i), o = !1;
    s || (s = r.edge(i, a), o = !0), r.node(a).rank = r.node(i).rank + (o ? s.minlen : -s.minlen);
  });
}
function Ub(e, r, t) {
  return e.hasEdge(r, t);
}
function _h(e, r, t) {
  return t.low <= r.lim && r.lim <= t.lim;
}
var Kb = jt, av = Kb.longestPath, Vb = Xp, Hb = Gb, Wb = Yb;
function Yb(e) {
  switch (e.graph().ranker) {
    case "network-simplex":
      wh(e);
      break;
    case "tight-tree":
      Jb(e);
      break;
    case "longest-path":
      Qb(e);
      break;
    default:
      wh(e);
  }
}
var Qb = av;
function Jb(e) {
  av(e), Vb(e);
}
function wh(e) {
  Hb(e);
}
var Wo = be, Xb = Zb;
function Zb(e) {
  var r = r_(e);
  Wo.forEach(e.graph().dummyChains, function(t) {
    for (var n = e.node(t), a = n.edgeObj, i = e_(e, r, a.v, a.w), s = i.path, o = i.lca, u = 0, c = s[u], l = !0; t !== a.w; ) {
      if (n = e.node(t), l) {
        for (; (c = s[u]) !== o && e.node(c).maxRank < n.rank; )
          u++;
        c === o && (l = !1);
      }
      if (!l) {
        for (; u < s.length - 1 && e.node(c = s[u + 1]).minRank <= n.rank; )
          u++;
        c = s[u];
      }
      e.setParent(t, c), t = e.successors(t)[0];
    }
  });
}
function e_(e, r, t, n) {
  var a = [], i = [], s = Math.min(r[t].low, r[n].low), o = Math.max(r[t].lim, r[n].lim), u, c;
  u = t;
  do
    u = e.parent(u), a.push(u);
  while (u && (r[u].low > s || o > r[u].lim));
  for (c = u, u = n; (u = e.parent(u)) !== c; )
    i.push(u);
  return { path: a.concat(i.reverse()), lca: c };
}
function r_(e) {
  var r = {}, t = 0;
  function n(a) {
    var i = t;
    Wo.forEach(e.children(a), n), r[a] = { low: i, lim: t++ };
  }
  return Wo.forEach(e.children(), n), r;
}
var cr = be, Yo = Me, t_ = {
  run: n_,
  cleanup: s_
};
function n_(e) {
  var r = Yo.addDummyNode(e, "root", {}, "_root"), t = a_(e), n = cr.max(cr.values(t)) - 1, a = 2 * n + 1;
  e.graph().nestingRoot = r, cr.forEach(e.edges(), function(s) {
    e.edge(s).minlen *= a;
  });
  var i = i_(e) + 1;
  cr.forEach(e.children(), function(s) {
    iv(e, r, a, i, n, t, s);
  }), e.graph().nodeRankFactor = a;
}
function iv(e, r, t, n, a, i, s) {
  var o = e.children(s);
  if (!o.length) {
    s !== r && e.setEdge(r, s, { weight: 0, minlen: t });
    return;
  }
  var u = Yo.addBorderNode(e, "_bt"), c = Yo.addBorderNode(e, "_bb"), l = e.node(s);
  e.setParent(u, s), l.borderTop = u, e.setParent(c, s), l.borderBottom = c, cr.forEach(o, function(d) {
    iv(e, r, t, n, a, i, d);
    var h = e.node(d), v = h.borderTop ? h.borderTop : d, g = h.borderBottom ? h.borderBottom : d, p = h.borderTop ? n : 2 * n, m = v !== g ? 1 : a - i[s] + 1;
    e.setEdge(u, v, {
      weight: p,
      minlen: m,
      nestingEdge: !0
    }), e.setEdge(g, c, {
      weight: p,
      minlen: m,
      nestingEdge: !0
    });
  }), e.parent(s) || e.setEdge(r, u, { weight: 0, minlen: a + i[s] });
}
function a_(e) {
  var r = {};
  function t(n, a) {
    var i = e.children(n);
    i && i.length && cr.forEach(i, function(s) {
      t(s, a + 1);
    }), r[n] = a;
  }
  return cr.forEach(e.children(), function(n) {
    t(n, 1);
  }), r;
}
function i_(e) {
  return cr.reduce(e.edges(), function(r, t) {
    return r + e.edge(t).weight;
  }, 0);
}
function s_(e) {
  var r = e.graph();
  e.removeNode(r.nestingRoot), delete r.nestingRoot, cr.forEach(e.edges(), function(t) {
    var n = e.edge(t);
    n.nestingEdge && e.removeEdge(t);
  });
}
var No = be, o_ = Me, u_ = l_;
function l_(e) {
  function r(t) {
    var n = e.children(t), a = e.node(t);
    if (n.length && No.forEach(n, r), No.has(a, "minRank")) {
      a.borderLeft = [], a.borderRight = [];
      for (var i = a.minRank, s = a.maxRank + 1; i < s; ++i)
        Ih(e, "borderLeft", "_bl", t, a, i), Ih(e, "borderRight", "_br", t, a, i);
    }
  }
  No.forEach(e.children(), r);
}
function Ih(e, r, t, n, a, i) {
  var s = { width: 0, height: 0, rank: i, borderType: r }, o = a[r][i - 1], u = o_.addDummyNode(e, "border", s, t);
  a[r][i] = u, e.setParent(u, n), o && e.setEdge(o, u, { weight: 1 });
}
var rr = be, c_ = {
  adjust: d_,
  undo: f_
};
function d_(e) {
  var r = e.graph().rankdir.toLowerCase();
  (r === "lr" || r === "rl") && sv(e);
}
function f_(e) {
  var r = e.graph().rankdir.toLowerCase();
  (r === "bt" || r === "rl") && h_(e), (r === "lr" || r === "rl") && (p_(e), sv(e));
}
function sv(e) {
  rr.forEach(e.nodes(), function(r) {
    qh(e.node(r));
  }), rr.forEach(e.edges(), function(r) {
    qh(e.edge(r));
  });
}
function qh(e) {
  var r = e.width;
  e.width = e.height, e.height = r;
}
function h_(e) {
  rr.forEach(e.nodes(), function(r) {
    Do(e.node(r));
  }), rr.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    rr.forEach(t.points, Do), rr.has(t, "y") && Do(t);
  });
}
function Do(e) {
  e.y = -e.y;
}
function p_(e) {
  rr.forEach(e.nodes(), function(r) {
    Mo(e.node(r));
  }), rr.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    rr.forEach(t.points, Mo), rr.has(t, "x") && Mo(t);
  });
}
function Mo(e) {
  var r = e.x;
  e.x = e.y, e.y = r;
}
var or = be, v_ = g_;
function g_(e) {
  var r = {}, t = or.filter(e.nodes(), function(o) {
    return !e.children(o).length;
  }), n = or.max(or.map(t, function(o) {
    return e.node(o).rank;
  })), a = or.map(or.range(n + 1), function() {
    return [];
  });
  function i(o) {
    if (!or.has(r, o)) {
      r[o] = !0;
      var u = e.node(o);
      a[u.rank].push(o), or.forEach(e.successors(o), i);
    }
  }
  var s = or.sortBy(t, function(o) {
    return e.node(o).rank;
  });
  return or.forEach(s, i), a;
}
var yr = be, y_ = m_;
function m_(e, r) {
  for (var t = 0, n = 1; n < r.length; ++n)
    t += b_(e, r[n - 1], r[n]);
  return t;
}
function b_(e, r, t) {
  for (var n = yr.zipObject(
    t,
    yr.map(t, function(c, l) {
      return l;
    })
  ), a = yr.flatten(yr.map(r, function(c) {
    return yr.sortBy(yr.map(e.outEdges(c), function(l) {
      return { pos: n[l.w], weight: e.edge(l).weight };
    }), "pos");
  }), !0), i = 1; i < t.length; ) i <<= 1;
  var s = 2 * i - 1;
  i -= 1;
  var o = yr.map(new Array(s), function() {
    return 0;
  }), u = 0;
  return yr.forEach(a.forEach(function(c) {
    var l = c.pos + i;
    o[l] += c.weight;
    for (var d = 0; l > 0; )
      l % 2 && (d += o[l + 1]), l = l - 1 >> 1, o[l] += c.weight;
    u += c.weight * d;
  })), u;
}
var Ch = be, __ = w_;
function w_(e, r) {
  return Ch.map(r, function(t) {
    var n = e.inEdges(t);
    if (n.length) {
      var a = Ch.reduce(n, function(i, s) {
        var o = e.edge(s), u = e.node(s.v);
        return {
          sum: i.sum + o.weight * u.order,
          weight: i.weight + o.weight
        };
      }, { sum: 0, weight: 0 });
      return {
        v: t,
        barycenter: a.sum / a.weight,
        weight: a.weight
      };
    } else
      return { v: t };
  });
}
var ze = be, I_ = q_;
function q_(e, r) {
  var t = {};
  ze.forEach(e, function(a, i) {
    var s = t[a.v] = {
      indegree: 0,
      in: [],
      out: [],
      vs: [a.v],
      i
    };
    ze.isUndefined(a.barycenter) || (s.barycenter = a.barycenter, s.weight = a.weight);
  }), ze.forEach(r.edges(), function(a) {
    var i = t[a.v], s = t[a.w];
    !ze.isUndefined(i) && !ze.isUndefined(s) && (s.indegree++, i.out.push(t[a.w]));
  });
  var n = ze.filter(t, function(a) {
    return !a.indegree;
  });
  return C_(n);
}
function C_(e) {
  var r = [];
  function t(i) {
    return function(s) {
      s.merged || (ze.isUndefined(s.barycenter) || ze.isUndefined(i.barycenter) || s.barycenter >= i.barycenter) && E_(i, s);
    };
  }
  function n(i) {
    return function(s) {
      s.in.push(i), --s.indegree === 0 && e.push(s);
    };
  }
  for (; e.length; ) {
    var a = e.pop();
    r.push(a), ze.forEach(a.in.reverse(), t(a)), ze.forEach(a.out, n(a));
  }
  return ze.map(
    ze.filter(r, function(i) {
      return !i.merged;
    }),
    function(i) {
      return ze.pick(i, ["vs", "i", "barycenter", "weight"]);
    }
  );
}
function E_(e, r) {
  var t = 0, n = 0;
  e.weight && (t += e.barycenter * e.weight, n += e.weight), r.weight && (t += r.barycenter * r.weight, n += r.weight), e.vs = r.vs.concat(e.vs), e.barycenter = t / n, e.weight = n, e.i = Math.min(r.i, e.i), r.merged = !0;
}
var Yr = be, S_ = Me, R_ = T_;
function T_(e, r) {
  var t = S_.partition(e, function(l) {
    return Yr.has(l, "barycenter");
  }), n = t.lhs, a = Yr.sortBy(t.rhs, function(l) {
    return -l.i;
  }), i = [], s = 0, o = 0, u = 0;
  n.sort(x_(!!r)), u = Eh(i, a, u), Yr.forEach(n, function(l) {
    u += l.vs.length, i.push(l.vs), s += l.barycenter * l.weight, o += l.weight, u = Eh(i, a, u);
  });
  var c = { vs: Yr.flatten(i, !0) };
  return o && (c.barycenter = s / o, c.weight = o), c;
}
function Eh(e, r, t) {
  for (var n; r.length && (n = Yr.last(r)).i <= t; )
    r.pop(), e.push(n.vs), t++;
  return t;
}
function x_(e) {
  return function(r, t) {
    return r.barycenter < t.barycenter ? -1 : r.barycenter > t.barycenter ? 1 : e ? t.i - r.i : r.i - t.i;
  };
}
var mr = be, k_ = __, A_ = I_, P_ = R_, O_ = ov;
function ov(e, r, t, n) {
  var a = e.children(r), i = e.node(r), s = i ? i.borderLeft : void 0, o = i ? i.borderRight : void 0, u = {};
  s && (a = mr.filter(a, function(g) {
    return g !== s && g !== o;
  }));
  var c = k_(e, a);
  mr.forEach(c, function(g) {
    if (e.children(g.v).length) {
      var p = ov(e, g.v, t, n);
      u[g.v] = p, mr.has(p, "barycenter") && D_(g, p);
    }
  });
  var l = A_(c, t);
  N_(l, u);
  var d = P_(l, n);
  if (s && (d.vs = mr.flatten([s, d.vs, o], !0), e.predecessors(s).length)) {
    var h = e.node(e.predecessors(s)[0]), v = e.node(e.predecessors(o)[0]);
    mr.has(d, "barycenter") || (d.barycenter = 0, d.weight = 0), d.barycenter = (d.barycenter * d.weight + h.order + v.order) / (d.weight + 2), d.weight += 2;
  }
  return d;
}
function N_(e, r) {
  mr.forEach(e, function(t) {
    t.vs = mr.flatten(t.vs.map(function(n) {
      return r[n] ? r[n].vs : n;
    }), !0);
  });
}
function D_(e, r) {
  mr.isUndefined(e.barycenter) ? (e.barycenter = r.barycenter, e.weight = r.weight) : (e.barycenter = (e.barycenter * e.weight + r.barycenter * r.weight) / (e.weight + r.weight), e.weight += r.weight);
}
var Qr = be, M_ = Ze.Graph, $_ = L_;
function L_(e, r, t) {
  var n = j_(e), a = new M_({ compound: !0 }).setGraph({ root: n }).setDefaultNodeLabel(function(i) {
    return e.node(i);
  });
  return Qr.forEach(e.nodes(), function(i) {
    var s = e.node(i), o = e.parent(i);
    (s.rank === r || s.minRank <= r && r <= s.maxRank) && (a.setNode(i), a.setParent(i, o || n), Qr.forEach(e[t](i), function(u) {
      var c = u.v === i ? u.w : u.v, l = a.edge(c, i), d = Qr.isUndefined(l) ? 0 : l.weight;
      a.setEdge(c, i, { weight: e.edge(u).weight + d });
    }), Qr.has(s, "minRank") && a.setNode(i, {
      borderLeft: s.borderLeft[r],
      borderRight: s.borderRight[r]
    }));
  }), a;
}
function j_(e) {
  for (var r; e.hasNode(r = Qr.uniqueId("_root")); ) ;
  return r;
}
var F_ = be, G_ = z_;
function z_(e, r, t) {
  var n = {}, a;
  F_.forEach(t, function(i) {
    for (var s = e.parent(i), o, u; s; ) {
      if (o = e.parent(s), o ? (u = n[o], n[o] = s) : (u = a, a = s), u && u !== s) {
        r.setEdge(u, s);
        return;
      }
      s = o;
    }
  });
}
var _r = be, B_ = v_, U_ = y_, K_ = O_, V_ = $_, H_ = G_, W_ = Ze.Graph, Sh = Me, Y_ = Q_;
function Q_(e) {
  var r = Sh.maxRank(e), t = Rh(e, _r.range(1, r + 1), "inEdges"), n = Rh(e, _r.range(r - 1, -1, -1), "outEdges"), a = B_(e);
  Th(e, a);
  for (var i = Number.POSITIVE_INFINITY, s, o = 0, u = 0; u < 4; ++o, ++u) {
    J_(o % 2 ? t : n, o % 4 >= 2), a = Sh.buildLayerMatrix(e);
    var c = U_(e, a);
    c < i && (u = 0, s = _r.cloneDeep(a), i = c);
  }
  Th(e, s);
}
function Rh(e, r, t) {
  return _r.map(r, function(n) {
    return V_(e, n, t);
  });
}
function J_(e, r) {
  var t = new W_();
  _r.forEach(e, function(n) {
    var a = n.graph().root, i = K_(n, a, t, r);
    _r.forEach(i.vs, function(s, o) {
      n.node(s).order = o;
    }), H_(n, t, i.vs);
  });
}
function Th(e, r) {
  _r.forEach(r, function(t) {
    _r.forEach(t, function(n, a) {
      e.node(n).order = a;
    });
  });
}
var J = be, X_ = Ze.Graph, Z_ = Me, ew = {
  positionX: dw
};
function rw(e, r) {
  var t = {};
  function n(a, i) {
    var s = 0, o = 0, u = a.length, c = J.last(i);
    return J.forEach(i, function(l, d) {
      var h = nw(e, l), v = h ? e.node(h).order : u;
      (h || l === c) && (J.forEach(i.slice(o, d + 1), function(g) {
        J.forEach(e.predecessors(g), function(p) {
          var m = e.node(p), I = m.order;
          (I < s || v < I) && !(m.dummy && e.node(g).dummy) && uv(t, p, g);
        });
      }), o = d + 1, s = v);
    }), i;
  }
  return J.reduce(r, n), t;
}
function tw(e, r) {
  var t = {};
  function n(i, s, o, u, c) {
    var l;
    J.forEach(J.range(s, o), function(d) {
      l = i[d], e.node(l).dummy && J.forEach(e.predecessors(l), function(h) {
        var v = e.node(h);
        v.dummy && (v.order < u || v.order > c) && uv(t, h, l);
      });
    });
  }
  function a(i, s) {
    var o = -1, u, c = 0;
    return J.forEach(s, function(l, d) {
      if (e.node(l).dummy === "border") {
        var h = e.predecessors(l);
        h.length && (u = e.node(h[0]).order, n(s, c, d, o, u), c = d, o = u);
      }
      n(s, c, s.length, u, i.length);
    }), s;
  }
  return J.reduce(r, a), t;
}
function nw(e, r) {
  if (e.node(r).dummy)
    return J.find(e.predecessors(r), function(t) {
      return e.node(t).dummy;
    });
}
function uv(e, r, t) {
  if (r > t) {
    var n = r;
    r = t, t = n;
  }
  var a = e[r];
  a || (e[r] = a = {}), a[t] = !0;
}
function aw(e, r, t) {
  if (r > t) {
    var n = r;
    r = t, t = n;
  }
  return J.has(e[r], t);
}
function iw(e, r, t, n) {
  var a = {}, i = {}, s = {};
  return J.forEach(r, function(o) {
    J.forEach(o, function(u, c) {
      a[u] = u, i[u] = u, s[u] = c;
    });
  }), J.forEach(r, function(o) {
    var u = -1;
    J.forEach(o, function(c) {
      var l = n(c);
      if (l.length) {
        l = J.sortBy(l, function(p) {
          return s[p];
        });
        for (var d = (l.length - 1) / 2, h = Math.floor(d), v = Math.ceil(d); h <= v; ++h) {
          var g = l[h];
          i[c] === c && u < s[g] && !aw(t, c, g) && (i[g] = c, i[c] = a[c] = a[g], u = s[g]);
        }
      }
    });
  }), { root: a, align: i };
}
function sw(e, r, t, n, a) {
  var i = {}, s = ow(e, r, t, a), o = a ? "borderLeft" : "borderRight";
  function u(d, h) {
    for (var v = s.nodes(), g = v.pop(), p = {}; g; )
      p[g] ? d(g) : (p[g] = !0, v.push(g), v = v.concat(h(g))), g = v.pop();
  }
  function c(d) {
    i[d] = s.inEdges(d).reduce(function(h, v) {
      return Math.max(h, i[v.v] + s.edge(v));
    }, 0);
  }
  function l(d) {
    var h = s.outEdges(d).reduce(function(g, p) {
      return Math.min(g, i[p.w] - s.edge(p));
    }, Number.POSITIVE_INFINITY), v = e.node(d);
    h !== Number.POSITIVE_INFINITY && v.borderType !== o && (i[d] = Math.max(i[d], h));
  }
  return u(c, s.predecessors.bind(s)), u(l, s.successors.bind(s)), J.forEach(n, function(d) {
    i[d] = i[t[d]];
  }), i;
}
function ow(e, r, t, n) {
  var a = new X_(), i = e.graph(), s = fw(i.nodesep, i.edgesep, n);
  return J.forEach(r, function(o) {
    var u;
    J.forEach(o, function(c) {
      var l = t[c];
      if (a.setNode(l), u) {
        var d = t[u], h = a.edge(d, l);
        a.setEdge(d, l, Math.max(s(e, c, u), h || 0));
      }
      u = c;
    });
  }), a;
}
function uw(e, r) {
  return J.minBy(J.values(r), function(t) {
    var n = Number.NEGATIVE_INFINITY, a = Number.POSITIVE_INFINITY;
    return J.forIn(t, function(i, s) {
      var o = hw(e, s) / 2;
      n = Math.max(i + o, n), a = Math.min(i - o, a);
    }), n - a;
  });
}
function lw(e, r) {
  var t = J.values(r), n = J.min(t), a = J.max(t);
  J.forEach(["u", "d"], function(i) {
    J.forEach(["l", "r"], function(s) {
      var o = i + s, u = e[o], c;
      if (u !== r) {
        var l = J.values(u);
        c = s === "l" ? n - J.min(l) : a - J.max(l), c && (e[o] = J.mapValues(u, function(d) {
          return d + c;
        }));
      }
    });
  });
}
function cw(e, r) {
  return J.mapValues(e.ul, function(t, n) {
    if (r)
      return e[r.toLowerCase()][n];
    var a = J.sortBy(J.map(e, n));
    return (a[1] + a[2]) / 2;
  });
}
function dw(e) {
  var r = Z_.buildLayerMatrix(e), t = J.merge(
    rw(e, r),
    tw(e, r)
  ), n = {}, a;
  J.forEach(["u", "d"], function(s) {
    a = s === "u" ? r : J.values(r).reverse(), J.forEach(["l", "r"], function(o) {
      o === "r" && (a = J.map(a, function(d) {
        return J.values(d).reverse();
      }));
      var u = (s === "u" ? e.predecessors : e.successors).bind(e), c = iw(e, a, t, u), l = sw(
        e,
        a,
        c.root,
        c.align,
        o === "r"
      );
      o === "r" && (l = J.mapValues(l, function(d) {
        return -d;
      })), n[s + o] = l;
    });
  });
  var i = uw(e, n);
  return lw(n, i), cw(n, e.graph().align);
}
function fw(e, r, t) {
  return function(n, a, i) {
    var s = n.node(a), o = n.node(i), u = 0, c;
    if (u += s.width / 2, J.has(s, "labelpos"))
      switch (s.labelpos.toLowerCase()) {
        case "l":
          c = -s.width / 2;
          break;
        case "r":
          c = s.width / 2;
          break;
      }
    if (c && (u += t ? c : -c), c = 0, u += (s.dummy ? r : e) / 2, u += (o.dummy ? r : e) / 2, u += o.width / 2, J.has(o, "labelpos"))
      switch (o.labelpos.toLowerCase()) {
        case "l":
          c = o.width / 2;
          break;
        case "r":
          c = -o.width / 2;
          break;
      }
    return c && (u += t ? c : -c), c = 0, u;
  };
}
function hw(e, r) {
  return e.node(r).width;
}
var Jr = be, lv = Me, pw = ew.positionX, vw = gw;
function gw(e) {
  e = lv.asNonCompoundGraph(e), yw(e), Jr.forEach(pw(e), function(r, t) {
    e.node(t).x = r;
  });
}
function yw(e) {
  var r = lv.buildLayerMatrix(e), t = e.graph().ranksep, n = 0;
  Jr.forEach(r, function(a) {
    var i = Jr.max(Jr.map(a, function(s) {
      return e.node(s).height;
    }));
    Jr.forEach(a, function(s) {
      e.node(s).y = n + i / 2;
    }), n += i + t;
  });
}
var Z = be, xh = ob, kh = Cb, mw = Wb, bw = Me.normalizeRanks, _w = Xb, ww = Me.removeEmptyRanks, Ah = t_, Iw = u_, Ph = c_, qw = Y_, Cw = vw, wr = Me, Ew = Ze.Graph, Sw = Rw;
function Rw(e, r) {
  var t = r && r.debugTiming ? wr.time : wr.notime;
  t("layout", function() {
    var n = t("  buildLayoutGraph", function() {
      return Lw(e);
    });
    t("  runLayout", function() {
      Tw(n, t);
    }), t("  updateInputGraph", function() {
      xw(e, n);
    });
  });
}
function Tw(e, r) {
  r("    makeSpaceForEdgeLabels", function() {
    jw(e);
  }), r("    removeSelfEdges", function() {
    Ww(e);
  }), r("    acyclic", function() {
    xh.run(e);
  }), r("    nestingGraph.run", function() {
    Ah.run(e);
  }), r("    rank", function() {
    mw(wr.asNonCompoundGraph(e));
  }), r("    injectEdgeLabelProxies", function() {
    Fw(e);
  }), r("    removeEmptyRanks", function() {
    ww(e);
  }), r("    nestingGraph.cleanup", function() {
    Ah.cleanup(e);
  }), r("    normalizeRanks", function() {
    bw(e);
  }), r("    assignRankMinMax", function() {
    Gw(e);
  }), r("    removeEdgeLabelProxies", function() {
    zw(e);
  }), r("    normalize.run", function() {
    kh.run(e);
  }), r("    parentDummyChains", function() {
    _w(e);
  }), r("    addBorderSegments", function() {
    Iw(e);
  }), r("    order", function() {
    qw(e);
  }), r("    insertSelfEdges", function() {
    Yw(e);
  }), r("    adjustCoordinateSystem", function() {
    Ph.adjust(e);
  }), r("    position", function() {
    Cw(e);
  }), r("    positionSelfEdges", function() {
    Qw(e);
  }), r("    removeBorderNodes", function() {
    Hw(e);
  }), r("    normalize.undo", function() {
    kh.undo(e);
  }), r("    fixupEdgeLabelCoords", function() {
    Kw(e);
  }), r("    undoCoordinateSystem", function() {
    Ph.undo(e);
  }), r("    translateGraph", function() {
    Bw(e);
  }), r("    assignNodeIntersects", function() {
    Uw(e);
  }), r("    reversePoints", function() {
    Vw(e);
  }), r("    acyclic.undo", function() {
    xh.undo(e);
  });
}
function xw(e, r) {
  Z.forEach(e.nodes(), function(t) {
    var n = e.node(t), a = r.node(t);
    n && (n.x = a.x, n.y = a.y, r.children(t).length && (n.width = a.width, n.height = a.height));
  }), Z.forEach(e.edges(), function(t) {
    var n = e.edge(t), a = r.edge(t);
    n.points = a.points, Z.has(a, "x") && (n.x = a.x, n.y = a.y);
  }), e.graph().width = r.graph().width, e.graph().height = r.graph().height;
}
var kw = ["nodesep", "edgesep", "ranksep", "marginx", "marginy"], Aw = { ranksep: 50, edgesep: 20, nodesep: 50, rankdir: "tb" }, Pw = ["acyclicer", "ranker", "rankdir", "align"], Ow = ["width", "height"], Nw = { width: 0, height: 0 }, Dw = ["minlen", "weight", "width", "height", "labeloffset"], Mw = {
  minlen: 1,
  weight: 1,
  width: 0,
  height: 0,
  labeloffset: 10,
  labelpos: "r"
}, $w = ["labelpos"];
function Lw(e) {
  var r = new Ew({ multigraph: !0, compound: !0 }), t = Lo(e.graph());
  return r.setGraph(Z.merge(
    {},
    Aw,
    $o(t, kw),
    Z.pick(t, Pw)
  )), Z.forEach(e.nodes(), function(n) {
    var a = Lo(e.node(n));
    r.setNode(n, Z.defaults($o(a, Ow), Nw)), r.setParent(n, e.parent(n));
  }), Z.forEach(e.edges(), function(n) {
    var a = Lo(e.edge(n));
    r.setEdge(n, Z.merge(
      {},
      Mw,
      $o(a, Dw),
      Z.pick(a, $w)
    ));
  }), r;
}
function jw(e) {
  var r = e.graph();
  r.ranksep /= 2, Z.forEach(e.edges(), function(t) {
    var n = e.edge(t);
    n.minlen *= 2, n.labelpos.toLowerCase() !== "c" && (r.rankdir === "TB" || r.rankdir === "BT" ? n.width += n.labeloffset : n.height += n.labeloffset);
  });
}
function Fw(e) {
  Z.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (t.width && t.height) {
      var n = e.node(r.v), a = e.node(r.w), i = { rank: (a.rank - n.rank) / 2 + n.rank, e: r };
      wr.addDummyNode(e, "edge-proxy", i, "_ep");
    }
  });
}
function Gw(e) {
  var r = 0;
  Z.forEach(e.nodes(), function(t) {
    var n = e.node(t);
    n.borderTop && (n.minRank = e.node(n.borderTop).rank, n.maxRank = e.node(n.borderBottom).rank, r = Z.max(r, n.maxRank));
  }), e.graph().maxRank = r;
}
function zw(e) {
  Z.forEach(e.nodes(), function(r) {
    var t = e.node(r);
    t.dummy === "edge-proxy" && (e.edge(t.e).labelRank = t.rank, e.removeNode(r));
  });
}
function Bw(e) {
  var r = Number.POSITIVE_INFINITY, t = 0, n = Number.POSITIVE_INFINITY, a = 0, i = e.graph(), s = i.marginx || 0, o = i.marginy || 0;
  function u(c) {
    var l = c.x, d = c.y, h = c.width, v = c.height;
    r = Math.min(r, l - h / 2), t = Math.max(t, l + h / 2), n = Math.min(n, d - v / 2), a = Math.max(a, d + v / 2);
  }
  Z.forEach(e.nodes(), function(c) {
    u(e.node(c));
  }), Z.forEach(e.edges(), function(c) {
    var l = e.edge(c);
    Z.has(l, "x") && u(l);
  }), r -= s, n -= o, Z.forEach(e.nodes(), function(c) {
    var l = e.node(c);
    l.x -= r, l.y -= n;
  }), Z.forEach(e.edges(), function(c) {
    var l = e.edge(c);
    Z.forEach(l.points, function(d) {
      d.x -= r, d.y -= n;
    }), Z.has(l, "x") && (l.x -= r), Z.has(l, "y") && (l.y -= n);
  }), i.width = t - r + s, i.height = a - n + o;
}
function Uw(e) {
  Z.forEach(e.edges(), function(r) {
    var t = e.edge(r), n = e.node(r.v), a = e.node(r.w), i, s;
    t.points ? (i = t.points[0], s = t.points[t.points.length - 1]) : (t.points = [], i = a, s = n), t.points.unshift(wr.intersectRect(n, i)), t.points.push(wr.intersectRect(a, s));
  });
}
function Kw(e) {
  Z.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    if (Z.has(t, "x"))
      switch ((t.labelpos === "l" || t.labelpos === "r") && (t.width -= t.labeloffset), t.labelpos) {
        case "l":
          t.x -= t.width / 2 + t.labeloffset;
          break;
        case "r":
          t.x += t.width / 2 + t.labeloffset;
          break;
      }
  });
}
function Vw(e) {
  Z.forEach(e.edges(), function(r) {
    var t = e.edge(r);
    t.reversed && t.points.reverse();
  });
}
function Hw(e) {
  Z.forEach(e.nodes(), function(r) {
    if (e.children(r).length) {
      var t = e.node(r), n = e.node(t.borderTop), a = e.node(t.borderBottom), i = e.node(Z.last(t.borderLeft)), s = e.node(Z.last(t.borderRight));
      t.width = Math.abs(s.x - i.x), t.height = Math.abs(a.y - n.y), t.x = i.x + t.width / 2, t.y = n.y + t.height / 2;
    }
  }), Z.forEach(e.nodes(), function(r) {
    e.node(r).dummy === "border" && e.removeNode(r);
  });
}
function Ww(e) {
  Z.forEach(e.edges(), function(r) {
    if (r.v === r.w) {
      var t = e.node(r.v);
      t.selfEdges || (t.selfEdges = []), t.selfEdges.push({ e: r, label: e.edge(r) }), e.removeEdge(r);
    }
  });
}
function Yw(e) {
  var r = wr.buildLayerMatrix(e);
  Z.forEach(r, function(t) {
    var n = 0;
    Z.forEach(t, function(a, i) {
      var s = e.node(a);
      s.order = i + n, Z.forEach(s.selfEdges, function(o) {
        wr.addDummyNode(e, "selfedge", {
          width: o.label.width,
          height: o.label.height,
          rank: s.rank,
          order: i + ++n,
          e: o.e,
          label: o.label
        }, "_se");
      }), delete s.selfEdges;
    });
  });
}
function Qw(e) {
  Z.forEach(e.nodes(), function(r) {
    var t = e.node(r);
    if (t.dummy === "selfedge") {
      var n = e.node(t.e.v), a = n.x + n.width / 2, i = n.y, s = t.x - a, o = n.height / 2;
      e.setEdge(t.e, t.label), e.removeNode(r), t.label.points = [
        { x: a + 2 * s / 3, y: i - o },
        { x: a + 5 * s / 6, y: i - o },
        { x: a + s, y: i },
        { x: a + 5 * s / 6, y: i + o },
        { x: a + 2 * s / 3, y: i + o }
      ], t.label.x = t.x, t.label.y = t.y;
    }
  });
}
function $o(e, r) {
  return Z.mapValues(Z.pick(e, r), Number);
}
function Lo(e) {
  var r = {};
  return Z.forEach(e, function(t, n) {
    r[n.toLowerCase()] = t;
  }), r;
}
var ct = be, Jw = Me, Xw = Ze.Graph, Zw = {
  debugOrdering: e1
};
function e1(e) {
  var r = Jw.buildLayerMatrix(e), t = new Xw({ compound: !0, multigraph: !0 }).setGraph({});
  return ct.forEach(e.nodes(), function(n) {
    t.setNode(n, { label: n }), t.setParent(n, "layer" + e.node(n).rank);
  }), ct.forEach(e.edges(), function(n) {
    t.setEdge(n.v, n.w, {}, n.name);
  }), ct.forEach(r, function(n, a) {
    var i = "layer" + a;
    t.setNode(i, { rank: "same" }), ct.reduce(n, function(s, o) {
      return t.setEdge(s, o, { style: "invis" }), o;
    });
  }), t;
}
var r1 = "0.8.5", t1 = {
  graphlib: Ze,
  layout: Sw,
  debug: Zw,
  util: {
    time: Me.time,
    notime: Me.notime
  },
  version: r1
};
const Oh = /* @__PURE__ */ sg(t1), Zr = 148, et = 46, mu = 80, bu = 44;
function n1(e) {
  if (e.nodes.length === 0) return e;
  const r = new Oh.graphlib.Graph();
  r.setDefaultEdgeLabel(() => ({})), r.setGraph({ rankdir: "LR", nodesep: 60, ranksep: 120, marginx: 40, marginy: 40 });
  for (const n of e.nodes) r.setNode(n.id, { width: Zr, height: et });
  for (const n of e.edges) r.setEdge(n.source, n.target);
  Oh.layout(r);
  const t = {};
  for (const n of e.nodes) {
    const a = r.node(n.id);
    t[n.id] = a ? { x: Math.round(a.x - Zr / 2), y: Math.round(a.y - et / 2) } : { x: n.x, y: n.y };
  }
  return {
    ...e,
    nodes: e.nodes.map((n) => ({ ...n, ...t[n.id] })),
    layout: { ...e.layout, positions: t }
  };
}
const cv = (e, r) => Math.abs(e.x - r.x) < Zr + mu && Math.abs(e.y - r.y) < et + bu;
function a1(e) {
  const r = [], t = e.nodes.map((a) => {
    let i = Number.isFinite(a.x) ? a.x : 0, s = Number.isFinite(a.y) ? a.y : 0, o = 0;
    for (; r.some((u) => cv({ x: i, y: s }, u)) && o++ < 200; )
      i += Zr + mu, i > 2400 && (i = 80, s += et + bu);
    return r.push({ x: i, y: s }), { ...a, x: i, y: s };
  });
  if (t.every((a, i) => a.x === e.nodes[i].x && a.y === e.nodes[i].y)) return e;
  const n = Object.fromEntries(t.map((a) => [a.id, { x: a.x, y: a.y }]));
  return { ...e, nodes: t, layout: { ...e.layout, positions: n } };
}
function jo(e, r, t) {
  let n = { x: Math.round(r), y: Math.round(t) }, a = 0;
  for (; e.nodes.some((i) => cv(n, i)) && a++ < 200; )
    n = { x: n.x + Zr + mu, y: n.y }, n.x > 2400 && (n = { x: 80, y: n.y + et + bu });
  return n;
}
const { Header: i1, Content: s1 } = hv;
function o1({ ctx: e }) {
  var Dr, st;
  const [r, t] = pv.useMessage(), [n, a] = q([]), [i, s] = q([]), [o, u] = q([]), [c, l] = q([]), [d, h] = q(""), [v, g] = q(1), [p, m] = q(!0), [I, k] = q([]), [b, A] = q(() => pt()), [M, y] = q(null), [x, O] = q(null), [V, F] = q([]), [R, G] = q(!1), [H, z] = q(!0), [ae, te] = q(!1), [de, U] = q(!1), [Q, E] = q(0), T = Ae([]), $ = Ae([]), pe = Ae(null), Ie = Ae(null), [Re, nr] = q(null), [Ar, qr] = q({}), [Pr, pr] = q(!1), [Or, We] = q(!1), ar = Ae(1), Ye = Ae(e);
  Ye.current = e;
  const ie = Ae(b.trigger);
  we(() => {
    ie.current = b.trigger;
  }, [b.trigger]), we(() => {
    let C = !0;
    const D = Ye.current.hubQuery;
    return Promise.allSettled([
      D("industrial/nodes"),
      D("industrial/workflows"),
      D("industrial/resources")
    ]).then(async ([ue, se, he]) => {
      var Fe;
      if (!C) return;
      const ve = [];
      if (ue.status === "fulfilled" ? a(Bh(ue.value)) : ve.push(`节点描述: ${String(ue.reason)}`), se.status === "fulfilled") {
        const fe = Vh(se.value);
        s(fe);
        const _e = new URLSearchParams(window.location.search).get("workflowId"), xe = _e && fe.some((Ge) => Ge.workflowId === _e) ? _e : (Fe = fe[0]) == null ? void 0 : Fe.workflowId;
        xe && Qe(xe);
      } else ve.push(`工作流: ${String(se.reason)}`);
      if (he.status === "fulfilled") {
        const fe = Uh(he.value);
        l(fe);
        const _e = fe.map((xe) => xe.id);
        u(_e), h((xe) => xe || _e[0] || "");
      } else ve.push(`资源: ${String(he.reason)}`);
      k(ve);
      try {
        const fe = await Ye.current.hubQuery("industrial/file-samples");
        C && qr((fe == null ? void 0 : fe.data) ?? fe ?? {});
      } catch {
      }
    }).finally(() => {
      C && m(!1);
    }), () => {
      C = !1;
    };
  }, []);
  const ce = Qo(
    () => new Map(n.map((C) => [C.nodeTypeId, C])),
    [n]
  ), X = b.nodes.find((C) => C.id === M) ?? null, Te = L(async (C, D, ue) => {
    G(!0);
    try {
      return await De().api.hubCommand(C, D), r.success(ue), !0;
    } catch (se) {
      return r.error(`${C} failed: ${se instanceof Error ? se.message : String(se)}`), !1;
    } finally {
      G(!1);
    }
  }, [r]), Qe = L(async (C) => {
    try {
      const D = await e.hubQuery("industrial/workflow-definitions"), se = (Pe(D) ?? []).find((fe) => {
        const _e = fe;
        return String((_e == null ? void 0 : _e.workflowId) ?? "") === C;
      }), he = se === void 0 ? { ...pt(C), name: C } : Vv(se), ve = a1(he);
      A(ve), T.current = [], $.current = [], E((fe) => fe + 1), g(Number(ve.layout.lookbackDays ?? 1) || 1);
      const Fe = he.nodes.map((fe) => /^n(\d+)$/.exec(fe.id)).filter(Boolean).reduce((fe, _e) => Math.max(fe, Number(_e[1])), 0);
      ar.current = Fe + 1, y(null), te(!1), F([]), U(!1);
    } catch (D) {
      r.error(`加载工作流失败: ${D instanceof Error ? D.message : String(D)}`);
    }
  }, [e, r]);
  we(() => {
    b.workflowId && F(Su(b, ce, new Set(c.map((C) => C.id))));
  }, [ce, b, c]);
  const ir = L(async () => {
    try {
      const C = Zv(await e.hubQuery("industrial/execution"));
      if (C === null) return;
      nr(C);
    } catch {
    }
  }, [e]), vr = L(async () => {
    var ue;
    const C = new Set(b.resourceReferences ?? []);
    for (const se of b.nodes)
      for (const he of ["resourceId", "targetResourceId"]) {
        const ve = (ue = se.config) == null ? void 0 : ue[he];
        typeof ve == "string" && ve.trim() && C.add(ve.trim());
      }
    const D = {
      ...b,
      trigger: ie.current,
      resourceReferences: [...C],
      layout: { ...b.layout, lookbackDays: v }
    };
    await Te("industrial/save-workflow", D, "已保存") && (A(D), U(!1));
  }, [Te, b, v]), $e = L(async () => {
    if (de) {
      r.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    await Te("industrial/publish-workflow", { workflowId: b.workflowId }, "已发布") && await ir();
  }, [Te, b.workflowId, de, r, ir]), W = L(async () => {
    if (de) {
      r.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    await Te(
      "industrial/run-workflow",
      { workflowId: b.workflowId, resourceId: d },
      "采集已启动"
    ) && await ir();
  }, [Te, b.workflowId, de, r, ir, d]), ee = L((C) => {
    C !== b && (T.current.length >= 50 && T.current.shift(), T.current.push(b), $.current = [], E((D) => D + 1), A(C), U(!0));
  }, [b]), oe = L(() => {
    const C = T.current.pop();
    C && ($.current.push(b), A(C), U(!0), E((D) => D + 1));
  }, [b]), ye = L(() => {
    const C = $.current.pop();
    C && (T.current.push(b), A(C), U(!0), E((D) => D + 1));
  }, [b]);
  we(() => {
    const C = (D) => {
      const ue = D.target;
      if (ue && /INPUT|TEXTAREA|SELECT/.test(ue.tagName)) return;
      const se = D.key.toLowerCase();
      if ((D.ctrlKey || D.metaKey) && se === "z") {
        D.preventDefault(), D.shiftKey ? ye() : oe();
        return;
      }
      if ((D.ctrlKey || D.metaKey) && se === "y") {
        D.preventDefault(), ye();
        return;
      }
      if (D.key === "Delete" && M) {
        D.preventDefault(), ee(Bt(b, M)), y(null);
        return;
      }
      if ((D.ctrlKey || D.metaKey) && se === "c" && M) {
        Ie.current = b.nodes.find((he) => he.id === M) ?? null;
        return;
      }
      if ((D.ctrlKey || D.metaKey) && se === "v" && Ie.current) {
        D.preventDefault();
        const he = ur(ce, Ie.current.type);
        if (!he) return;
        const ve = `n${ar.current++}`, Fe = Ie.current, fe = jo(b, Fe.x + 40, Fe.y + 40), _e = zt(he, ve, fe.x, fe.y);
        ee({
          ...b,
          nodes: [...b.nodes, { ..._e, config: { ...Fe.config } }]
        }), y(ve);
      }
    };
    return window.addEventListener("keydown", C), () => window.removeEventListener("keydown", C);
  }, [ce, ee, b, ye, M, oe]);
  const gr = L(() => {
    ee(n1(b)), requestAnimationFrame(() => {
      var C, D;
      return (D = (C = pe.current) == null ? void 0 : C.fitView) == null ? void 0 : D.call(C, { when: "always" });
    });
  }, [ee, b]), Le = L((C, D, ue) => {
    const se = ur(ce, C);
    if (!se) return;
    const he = `n${ar.current++}`, ve = jo(b, D, ue);
    ee({ ...b, nodes: [...b.nodes, zt(se, he, ve.x, ve.y)] }), y(he), te(!0), F([]);
  }, [ce, ee, b]), sr = L((C) => {
    const D = ar.current - 1;
    Le(C, 80 + D % 3 * 190, 80 + Math.floor(D / 3) * 130);
  }, [Le]), er = L((C, D) => {
    const ue = b.nodes.find((Ge) => Ge.id === C), se = b.nodes.find((Ge) => Ge.id === D);
    if (!ue || !se)
      return r.warning("连线失败：端点不在画布上"), null;
    const he = ur(ce, ue.type), ve = ur(ce, se.type);
    if (!he || !ve) return null;
    let Fe = ue, fe = se, _e = Cu(he, ve);
    if (!_e) {
      const Ge = Cu(ve, he);
      if (!Ge)
        return r.warning(`端口类型不兼容：${he.name} → ${ve.name}`), null;
      Fe = se, fe = ue, _e = Ge;
    }
    const xe = Ev(b, {
      source: Fe.id,
      sourcePort: _e.sourcePort,
      target: fe.id,
      targetPort: _e.targetPort
    }, ce);
    return xe.issue ? (r.warning(`${xe.issue.code}: ${xe.issue.message}`), F((Ge) => [...Ge, xe.issue]), null) : xe.definition === b ? (r.info("这条连线已经存在"), null) : (ee(xe.definition), _e);
  }, [ce, ee, b, r]), zr = L(() => {
    const C = Su(b, ce, new Set(o));
    F(C), C.length === 0 ? r.success("本地校验通过（发布时服务端仍会复核）") : (r.warning(`发现 ${C.length} 个问题，见属性抽屉定位列表`), te(!0));
  }, [b, ce, o, r]), Nr = L((C) => {
    ee(Bt(b, C)), M === C && y(null);
  }, [ee, b, M]), je = L((C) => {
    const D = b.nodes.find((ve) => ve.id === C);
    if (!D) return;
    const ue = ur(ce, D.type);
    if (!ue) return;
    const se = `n${ar.current++}`, he = jo(b, D.x + 40, D.y + 40);
    ee({ ...b, nodes: [
      ...b.nodes,
      { ...zt(ue, se, he.x, he.y), config: { ...D.config } }
    ] }), y(se);
  }, [ce, ee, b]), Je = L((C) => {
    const D = b.edges.findIndex((ue) => `e:${ue.source}:${ue.sourcePort}>${ue.target}:${ue.targetPort}` === C);
    D >= 0 && ee(Eu(b, D));
  }, [ee, b]), Ft = L((C, D) => {
    ee({
      ...b,
      edges: b.edges.map((ue, se) => se === C ? D : ue)
    });
  }, [ee, b]);
  return p ? /* @__PURE__ */ f($h, { tip: "加载节点描述与资源…" }) : /* @__PURE__ */ S("div", { className: "industrial-designer", children: [
    t,
    /* @__PURE__ */ S(i1, { className: "industrial-header", children: [
      /* @__PURE__ */ S("div", { className: "industrial-title", children: [
        /* @__PURE__ */ f(B.Text, { strong: !0, children: "Industrial Workflow" }),
        /* @__PURE__ */ f(B.Text, { type: "secondary", children: b.name || b.workflowId }),
        de && /* @__PURE__ */ f(Be, { color: "orange", children: "未保存" }),
        Re && /* @__PURE__ */ f(Be, { color: Re.error ? "error" : "success", children: Re.status })
      ] }),
      /* @__PURE__ */ S("div", { className: "industrial-toolbar", children: [
        /* @__PURE__ */ f(
          j,
          {
            size: "small",
            "data-testid": "toggle-library",
            "aria-expanded": H,
            onClick: () => z((C) => !C),
            children: H ? "收起节点库" : "节点库"
          }
        ),
        /* @__PURE__ */ f(
          j,
          {
            size: "small",
            "data-testid": "toggle-properties",
            onClick: () => te((C) => !C),
            children: "属性"
          }
        ),
        /* @__PURE__ */ f(j, { size: "small", "data-testid": "open-trigger", onClick: () => pr(!0), children: "触发器" }),
        /* @__PURE__ */ f(
          j,
          {
            size: "small",
            "data-testid": "open-backfill-preview",
            onClick: () => We(!0),
            children: "补采预览"
          }
        ),
        /* @__PURE__ */ f(
          j,
          {
            size: "small",
            "data-testid": "designer-undo",
            disabled: T.current.length === 0,
            onClick: oe,
            children: "撤销"
          }
        ),
        /* @__PURE__ */ f(
          j,
          {
            size: "small",
            "data-testid": "designer-redo",
            disabled: $.current.length === 0,
            onClick: ye,
            children: "重做"
          }
        ),
        /* @__PURE__ */ f(j, { size: "small", "data-testid": "designer-auto-layout", onClick: gr, children: "自动布局" }),
        /* @__PURE__ */ f(j, { size: "small", "data-testid": "designer-fit", onClick: () => {
          var C, D;
          return (D = (C = pe.current) == null ? void 0 : C.fitView) == null ? void 0 : D.call(C, { when: "always" });
        }, children: "适应画布" }),
        /* @__PURE__ */ f(j, { size: "small", "data-testid": "designer-center", onClick: () => {
          var C, D;
          return (D = (C = pe.current) == null ? void 0 : C.fitCenter) == null ? void 0 : D.call(C);
        }, children: "居中" }),
        /* @__PURE__ */ f("span", { className: "industrial-toolbar-divider" }),
        /* @__PURE__ */ S(Y, { size: 4, children: [
          /* @__PURE__ */ f(j, { size: "small", loading: R, onClick: () => void vr(), children: "保存" }),
          /* @__PURE__ */ f(j, { size: "small", onClick: zr, children: "校验" }),
          /* @__PURE__ */ f(j, { size: "small", loading: R, onClick: () => void $e(), children: "发布" }),
          /* @__PURE__ */ f(j, { size: "small", type: "primary", loading: R, onClick: () => void W(), children: "运行" })
        ] }),
        /* @__PURE__ */ S(
          "select",
          {
            className: "industrial-resource-select",
            "aria-label": "运行资源",
            value: d,
            onChange: (C) => h(C.target.value),
            children: [
              /* @__PURE__ */ f("option", { value: "", children: "运行资源" }),
              o.map((C) => /* @__PURE__ */ f("option", { value: C, children: C }, C))
            ]
          }
        ),
        /* @__PURE__ */ f(
          ht,
          {
            className: "industrial-backfill-input",
            size: "small",
            min: 1,
            max: 365,
            value: v,
            onChange: (C) => {
              g(Number(C) || 1), U(!0);
            },
            addonBefore: "补采"
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ S(s1, { className: `industrial-body${H ? "" : " industrial-body--collapsed"}`, children: [
      H ? /* @__PURE__ */ S("aside", { className: "industrial-palette", children: [
        i.length > 0 && /* @__PURE__ */ f(
          Ke,
          {
            className: "industrial-workflow-select",
            size: "small",
            value: b.workflowId,
            placeholder: "选择工作流",
            onChange: (C) => void Qe(C),
            options: i.map((C) => ({ value: C.workflowId, label: C.name }))
          }
        ),
        /* @__PURE__ */ f(Iv, { descriptors: n, loading: !1, onAdd: sr })
      ] }) : /* @__PURE__ */ f("div", { className: "industrial-palette-rail", children: /* @__PURE__ */ f(j, { type: "text", size: "small", onClick: () => z(!0), children: "节点库" }) }),
      /* @__PURE__ */ S("section", { className: "industrial-center", children: [
        I.length > 0 && /* @__PURE__ */ f(
          Se,
          {
            type: "warning",
            showIcon: !0,
            style: { marginBottom: 8 },
            message: "部分设计器数据未加载",
            description: I.join("；")
          }
        ),
        /* @__PURE__ */ f(
          wv,
          {
            definition: b,
            catalog: ce,
            selectedId: M,
            onSelect: (C) => {
              y(C), C && te(!0);
            },
            onDropNode: Le,
            onCreateEdge: er,
            onPositions: (C) => ee(Tv(b, C)),
            onSelectIds: (C) => {
              C.length && y(C[0]);
            },
            onGraphReady: (C) => {
              pe.current = C;
            },
            onSelectEdge: (C) => O(C),
            onDeleteNodeById: Nr,
            onDuplicateNode: je,
            onDeleteEdgeById: Je
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ S(
      fv,
      {
        className: "industrial-properties-drawer",
        title: "节点属性",
        placement: "right",
        width: 420,
        open: ae,
        mask: !1,
        getContainer: !1,
        onClose: () => te(!1),
        children: [
          Re && /* @__PURE__ */ f(
            Se,
            {
              style: { marginBottom: 12 },
              type: Re.error ? "error" : "success",
              showIcon: !0,
              message: `执行状态: ${Re.status}`,
              description: /* @__PURE__ */ S(Y, { direction: "vertical", size: 8, style: { width: "100%" }, children: [
                Re.error && /* @__PURE__ */ f("span", { children: Re.error }),
                Re.executed.length > 0 && /* @__PURE__ */ S("span", { children: [
                  "已执行节点: ",
                  Re.executed.join(", ")
                ] }),
                Re.nodes.length > 0 && /* @__PURE__ */ f(
                  Ne,
                  {
                    size: "small",
                    pagination: !1,
                    rowKey: "nodeId",
                    dataSource: Re.nodes,
                    columns: [
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (C, D) => D.error ?? (D.samples.length > 0 ? D.samples.join(", ") : "—")
                      }
                    ]
                  }
                )
              ] })
            }
          ),
          /* @__PURE__ */ f(
            $v,
            {
              node: X,
              descriptor: X ? ur(ce, X.type) ?? null : null,
              nodes: b.nodes,
              edges: b.edges,
              catalog: ce,
              resources: c,
              issues: V,
              onApplyConfig: (C) => {
                M && ee(Rv(b, M, C));
              },
              onDeleteNode: () => {
                ee(Bt(b, M ?? "")), y(null);
              },
              onDeleteEdge: (C) => ee(Eu(b, C)),
              onCreateEdge: (C, D) => void er(C, D),
              onUpdateEdge: Ft,
              onCreateResource: () => {
                var C;
                return (C = e.navigate) == null ? void 0 : C.call(e, "/industrial/resources");
              },
              selectedEdgeId: x,
              sampleLines: X ? Ar[String(((Dr = X.config) == null ? void 0 : Dr.resourceId) ?? ((st = X.config) == null ? void 0 : st.targetResourceId) ?? "")] ?? [] : [],
              onSelectNode: (C) => {
                y(C), te(!0);
              }
            }
          )
        ]
      }
    ),
    Pr && /* @__PURE__ */ f(
      tg,
      {
        open: Pr,
        onClose: () => pr(!1),
        trigger: b.trigger,
        resourceIds: o,
        workflowId: b.workflowId,
        ctx: e,
        onSave: (C) => {
          ie.current = C, ee({ ...b, trigger: C });
        }
      }
    ),
    Or && /* @__PURE__ */ f(
      ig,
      {
        open: Or,
        onClose: () => We(!1),
        ctx: e,
        workflowId: b.workflowId,
        resourceId: d,
        lookbackDays: v
      }
    )
  ] });
}
function u1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q(!1), [s, o] = q(""), [u, c] = q(null), [l, d] = q(!1), h = L(async () => {
    try {
      n(Vh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/workflows")));
    } catch (p) {
      c(p instanceof Error ? p.message : String(p));
    }
  }, [e == null ? void 0 : e.query, r]);
  we(() => {
    h();
  }, [h]);
  const v = L(async () => {
    var m;
    const p = s.trim();
    if (!p) {
      c("请输入工作流名称");
      return;
    }
    d(!0), c(null);
    try {
      const k = `workflow-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`;
      await r.hubCommand("industrial/save-workflow", {
        ...pt(k),
        name: p,
        layout: { lookbackDays: 1 }
      }), i(!1), o(""), await h(), (m = r.navigate) == null || m.call(r, `/industrial/designer?workflowId=${encodeURIComponent(k)}`);
    } catch (I) {
      c(I instanceof Error ? I.message : String(I));
    } finally {
      d(!1);
    }
  }, [r, h, s]), g = L((p) => {
    var m;
    (m = r.navigate) == null || m.call(r, `/industrial/designer?workflowId=${encodeURIComponent(p)}`);
  }, [r]);
  return /* @__PURE__ */ S(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ S(Y, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(B.Title, { level: 4, style: { margin: 0 }, children: "采集工作流" }),
      /* @__PURE__ */ f(j, { type: "primary", onClick: () => i(!0), children: "新建工作流" })
    ] }),
    u && /* @__PURE__ */ f(B.Text, { type: "danger", children: u }),
    /* @__PURE__ */ f(
      Ne,
      {
        rowKey: "workflowId",
        dataSource: t,
        pagination: !1,
        columns: [
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (p) => /* @__PURE__ */ f(j, { type: "link", style: { padding: 0 }, onClick: () => g(p), children: p })
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" }
        ]
      }
    ),
    /* @__PURE__ */ f(
      Er,
      {
        open: a,
        title: "新建采集工作流",
        okText: "创建",
        cancelText: "取消",
        confirmLoading: l,
        onCancel: () => i(!1),
        onOk: () => void v(),
        children: /* @__PURE__ */ S(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(B.Text, { children: "输入名称后会生成唯一 ID，并直接进入设计器。" }),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "工作流名称",
              placeholder: "例如：每日设备数据采集",
              value: s,
              onChange: (p) => o(p.target.value ?? "")
            }
          ),
          u && /* @__PURE__ */ f(B.Text, { type: "danger", children: u })
        ] })
      }
    )
  ] });
}
const Br = {
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
}, l1 = [
  { value: "filesystem.local", label: "本地目录" },
  { value: "filesystem.unc", label: "Windows UNC 共享" },
  { value: "sqlserver", label: "SQL Server" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mysql", label: "MySQL" },
  { value: "sqlite", label: "SQLite" }
], dt = {
  "filesystem.local": "local",
  "filesystem.unc": "local",
  sqlserver: "sqlserver",
  postgresql: "postgresql",
  mysql: "mysql",
  sqlite: "sqlite"
}, Nh = {
  sqlserver: { table: "IndustrialRecords" },
  postgresql: { port: "5432" },
  mysql: { port: "3306" }
};
function c1(e) {
  const r = { ...e.config }, t = (n, a) => {
    r[n] = a.trim();
  };
  switch (e.type) {
    case "filesystem.local":
    case "filesystem.unc":
      t("root", e.root);
      break;
    case "sqlserver":
      t("server", e.host), t("host", e.host), t("database", e.database), t("table", e.table);
      break;
    case "postgresql":
    case "mysql":
      t("host", e.host), t("database", e.database), t("port", e.port);
      break;
    case "sqlite":
      t("database", e.database);
      break;
  }
  return r;
}
function d1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q([]), [u, c] = q(Br), [l, d] = q(!1), [h, v] = q(null), [g, p] = q(!1), m = L(async () => {
    try {
      const [y, x, O] = await Promise.all([
        r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"),
        r.hubQuery("industrial/credentials"),
        r.hubQuery("industrial/resource-tests")
      ]);
      n(Hv(y)), i(Hh(x)), o(Wv(O));
    } catch (y) {
      v(y instanceof Error ? y.message : String(y));
    }
  }, [e == null ? void 0 : e.query, r]);
  we(() => {
    m();
  }, [m]);
  const I = L((y) => {
    const x = (...O) => {
      for (const V of O) {
        const F = y.config[V];
        if (F != null && String(F).trim()) return String(F);
      }
      return "";
    };
    c({
      id: y.id,
      name: y.name,
      type: y.type,
      provider: dt[y.type] ?? y.provider,
      root: x("root", "path"),
      host: x("server", "host", "dataSource", "data_source"),
      database: x("database", "initialCatalog", "initial_catalog", "path"),
      port: x("port"),
      table: x("table"),
      credentialId: y.credentialId ?? "",
      enabled: y.enabled,
      config: y.config
    }), d(!0);
  }, []), k = L(async () => {
    const y = u.id.trim();
    if (!y) {
      v("请输入资源 ID");
      return;
    }
    p(!0), v(null);
    try {
      await r.hubCommand("industrial/save-resource", {
        id: y,
        name: u.name || y,
        type: u.type,
        provider: dt[u.type] ?? "manual",
        config: c1(u),
        // An empty Select is an explicit unbind: null is sent, not omitted.
        credentialId: u.credentialId || null,
        enabled: u.enabled
      }), o((x) => x.filter((O) => O.resourceId !== y)), d(!1), c(Br), await m();
    } catch (x) {
      v(x instanceof Error ? x.message : String(x));
    } finally {
      p(!1);
    }
  }, [r, u, m]), b = L(async (y) => {
    v(null);
    try {
      await r.hubCommand("industrial/test-resource", { id: y });
    } catch (x) {
      v(x instanceof Error ? x.message : String(x));
    }
    await m();
  }, [r, m]), A = L(async (y) => {
    v(null);
    try {
      await r.hubCommand("industrial/delete-resource", { id: y }), await m();
    } catch (x) {
      v(x instanceof Error ? x.message : String(x));
    }
  }, [r, m]), M = new Map(s.map((y) => [y.resourceId, y]));
  return /* @__PURE__ */ S(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ S(Y, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(B.Title, { level: 4, style: { margin: 0 }, children: "数据源与目标" }),
      /* @__PURE__ */ S(Y, { children: [
        /* @__PURE__ */ f(j, { onClick: () => {
          var y;
          return (y = r.navigate) == null ? void 0 : y.call(r, "/industrial/designer");
        }, children: "返回设计器" }),
        /* @__PURE__ */ f(j, { type: "primary", onClick: () => {
          const y = Nh[Br.type] ?? {};
          c({ ...Br, ...y, provider: dt[Br.type] ?? "manual" }), d(!0);
        }, children: "新建资源" })
      ] })
    ] }),
    h && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: h }),
    /* @__PURE__ */ f(
      Ne,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "ID", dataIndex: "id" },
          { title: "名称", dataIndex: "name" },
          { title: "类型", dataIndex: "type" },
          { title: "凭证", dataIndex: "credentialName", render: (y) => y ?? "—" },
          {
            title: "连接测试",
            key: "test",
            render: (y, x) => {
              const O = M.get(x.id);
              return O ? /* @__PURE__ */ f(B.Text, { type: O.ok ? "success" : "danger", children: O.detail }) : "未测试";
            }
          },
          {
            title: "操作",
            key: "actions",
            render: (y, x) => /* @__PURE__ */ S(Y, { size: 4, children: [
              /* @__PURE__ */ f(j, { size: "small", onClick: () => void b(x.id), children: "测试连接" }),
              /* @__PURE__ */ f(j, { size: "small", onClick: () => I(x), children: "编辑" }),
              /* @__PURE__ */ f(j, { size: "small", danger: !0, onClick: () => void A(x.id), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ f(
      Er,
      {
        open: l,
        title: u.id ? "编辑资源" : "新建资源",
        okText: "保存资源",
        cancelText: "取消",
        confirmLoading: g,
        onCancel: () => d(!1),
        onOk: () => void k(),
        children: /* @__PURE__ */ S(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "资源 ID",
              placeholder: "资源 ID（唯一）",
              value: u.id,
              onChange: (y) => c({ ...u, id: y.target.value })
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "资源名称",
              placeholder: "显示名称",
              value: u.name,
              onChange: (y) => c({ ...u, name: y.target.value })
            }
          ),
          /* @__PURE__ */ f(B.Text, { children: "资源类型" }),
          /* @__PURE__ */ f(Y, { wrap: !0, children: l1.map((y) => /* @__PURE__ */ f(
            j,
            {
              size: "small",
              type: u.type === y.value ? "primary" : "default",
              onClick: () => {
                const x = Nh[y.value] ?? {};
                c({
                  ...u,
                  type: y.value,
                  provider: dt[y.value] ?? "manual",
                  root: "",
                  host: "",
                  database: "",
                  port: "",
                  table: "",
                  ...x
                });
              },
              children: y.label
            },
            y.value
          )) }),
          (u.type === "filesystem.local" || u.type === "filesystem.unc") && /* @__PURE__ */ f(
            K,
            {
              "aria-label": "目录路径",
              placeholder: u.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv",
              value: u.root,
              onChange: (y) => c({ ...u, root: y.target.value })
            }
          ),
          (u.type === "sqlserver" || u.type === "postgresql" || u.type === "mysql") && /* @__PURE__ */ S(Y, { children: [
            /* @__PURE__ */ f(
              K,
              {
                "aria-label": "主机或服务器",
                placeholder: "主机 / 服务器",
                value: u.host,
                onChange: (y) => c({ ...u, host: y.target.value })
              }
            ),
            /* @__PURE__ */ f(
              K,
              {
                "aria-label": "数据库",
                placeholder: "数据库",
                value: u.database,
                onChange: (y) => c({ ...u, database: y.target.value })
              }
            ),
            /* @__PURE__ */ f(
              K,
              {
                "aria-label": "端口",
                placeholder: u.type === "mysql" ? "默认 3306" : "默认 5432",
                value: u.port,
                onChange: (y) => c({ ...u, port: y.target.value })
              }
            )
          ] }),
          u.type === "sqlite" && /* @__PURE__ */ f(
            K,
            {
              "aria-label": "数据库文件",
              placeholder: "数据库文件路径",
              value: u.database,
              onChange: (y) => c({ ...u, database: y.target.value })
            }
          ),
          /* @__PURE__ */ f(
            Ke,
            {
              "aria-label": "凭证",
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: u.credentialId || void 0,
              options: a.map((y) => ({ value: y.id, label: y.name })),
              onChange: (y) => c({ ...u, credentialId: y ?? "" })
            }
          ),
          /* @__PURE__ */ S(Y, { children: [
            /* @__PURE__ */ f(
              Xr,
              {
                "aria-label": "启用资源",
                checked: u.enabled,
                onChange: (y) => c({ ...u, enabled: y })
              }
            ),
            /* @__PURE__ */ f(B.Text, { children: "启用" })
          ] })
        ] })
      }
    )
  ] });
}
const Fo = ["username-password", "domain-account", "api-token"];
function f1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q(Fo), [s, o] = q(!1), [u, c] = q(""), [l, d] = q(""), [h, v] = q(Fo[0]), [g, p] = q(""), [m, I] = q(""), [k, b] = q(""), [A, M] = q(""), [y, x] = q(""), [O, V] = q(!1), [F, R] = q(null), [G, H] = q(!1), z = L(async () => {
    try {
      n(Hh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/credentials")));
      const T = await r.hubQuery("industrial/credential-types"), $ = Array.isArray(T) ? T : Array.isArray(T == null ? void 0 : T.data) ? T.data : [];
      $.length && i($.map((pe) => pe.kind));
    } catch (T) {
      R(T instanceof Error ? T.message : String(T));
    }
  }, [e == null ? void 0 : e.query, r]);
  we(() => {
    z();
  }, [z]);
  const ae = L(() => {
    c(""), d(""), v(Fo[0]), p(""), I(""), b(""), M(""), x(""), V(!1), R(null);
  }, []), te = L(() => {
    ae(), o(!0);
  }, [ae]), de = L((T) => {
    ae(), c(T.id), d(T.name), a.includes(T.kind) && v(T.kind), V(!0), o(!0);
  }, [a, ae]), U = L(() => y.trim() ? y.trim() : h === "api-token" ? A.trim() ? JSON.stringify({ token: A.trim() }) : null : !g.trim() || !m.trim() ? null : JSON.stringify(h === "domain-account" ? { username: g.trim(), password: m.trim(), domain: k.trim() } : { username: g.trim(), password: m.trim() }), [k, h, m, y, A, g]), Q = L(async () => {
    if (!u.trim()) {
      R("请输入凭证 ID");
      return;
    }
    const T = U();
    if (!T) {
      R("请填写凭证字段，或在高级内容中提供凭证");
      return;
    }
    H(!0), R(null);
    try {
      O ? await r.hubCommand("industrial/rotate-credential", { id: u, secret: T }) : await r.hubCommand("industrial/save-credential", { id: u, name: l || u, kind: h, secret: T }), o(!1), await z();
    } catch ($) {
      R($ instanceof Error ? $.message : String($));
    } finally {
      H(!1);
    }
  }, [U, r, u, h, z, l, O, y]), E = L(async (T) => {
    R(null);
    try {
      await r.hubCommand("industrial/delete-credential", { id: T.id }), await z();
    } catch ($) {
      R($ instanceof Error ? $.message : String($));
    }
  }, [r, z]);
  return /* @__PURE__ */ S(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ S(Y, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(B.Title, { level: 4, style: { margin: 0 }, children: "凭证" }),
      /* @__PURE__ */ f(j, { type: "primary", onClick: te, children: "新建凭证" })
    ] }),
    F && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: F }),
    /* @__PURE__ */ f(
      Ne,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "ID", dataIndex: "id" },
          { title: "名称", dataIndex: "name" },
          { title: "类型", dataIndex: "kind" },
          { title: "版本", dataIndex: "version" },
          {
            title: "操作",
            key: "actions",
            render: (T, $) => /* @__PURE__ */ S(Y, { children: [
              /* @__PURE__ */ f(j, { size: "small", onClick: () => de($), children: "轮换" }),
              /* @__PURE__ */ f(j, { size: "small", danger: !0, onClick: () => void E($), children: "删除" })
            ] })
          }
        ]
      }
    ),
    /* @__PURE__ */ f(
      Er,
      {
        open: s,
        title: O ? "轮换凭证" : "新建凭证",
        okText: O ? "轮换" : "保存凭证",
        cancelText: "取消",
        confirmLoading: G,
        onCancel: () => o(!1),
        onOk: () => void Q(),
        children: /* @__PURE__ */ S(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "凭证 ID",
              placeholder: "凭证 ID",
              value: u,
              disabled: O,
              onChange: (T) => c(T.target.value)
            }
          ),
          !O && /* @__PURE__ */ f(
            K,
            {
              "aria-label": "凭证名称",
              placeholder: "显示名称",
              value: l,
              onChange: (T) => d(T.target.value)
            }
          ),
          /* @__PURE__ */ f(
            Ke,
            {
              "aria-label": "凭证类型",
              style: { width: "100%" },
              value: h,
              onChange: (T) => v(T),
              options: a.map((T) => ({ value: T, label: T }))
            }
          ),
          h === "api-token" ? /* @__PURE__ */ f(
            K.Password,
            {
              "aria-label": "令牌",
              placeholder: "令牌",
              value: A,
              onChange: (T) => M(T.target.value)
            }
          ) : /* @__PURE__ */ S(br, { children: [
            /* @__PURE__ */ f(
              K,
              {
                "aria-label": "用户名",
                placeholder: "用户名",
                value: g,
                onChange: (T) => p(T.target.value)
              }
            ),
            /* @__PURE__ */ f(
              K.Password,
              {
                "aria-label": "密码",
                placeholder: "密码",
                value: m,
                onChange: (T) => I(T.target.value)
              }
            ),
            h === "domain-account" && /* @__PURE__ */ f(
              K,
              {
                "aria-label": "域",
                placeholder: "域（可选）",
                value: k,
                onChange: (T) => b(T.target.value)
              }
            )
          ] }),
          /* @__PURE__ */ f(
            K.Password,
            {
              "aria-label": "凭证内容",
              placeholder: "高级：直接提供凭证内容（优先）",
              value: y,
              onChange: (T) => x(T.target.value)
            }
          ),
          F && /* @__PURE__ */ f(B.Text, { type: "danger", children: F })
        ] })
      }
    )
  ] });
}
const Dh = (e) => e === "Succeeded" || e === "Completed" ? "success" : e === "Failed" || e === "PartialFailure" ? "error" : e === "Running" || e === "Interrupted" ? "processing" : "default";
function h1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q(null), [u, c] = q(null), [l, d] = q(!1), h = L(async () => {
    try {
      const g = Xv(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/execution-center"));
      n(g.runs), i(g.deliveries);
    } catch (g) {
      o(g instanceof Error ? g.message : String(g));
    }
  }, [e == null ? void 0 : e.query, r]);
  we(() => {
    h();
  }, [h]);
  const v = L(async (g) => {
    d(!0), o(null), c(null);
    try {
      await r.hubCommand("industrial/retry-failed", {
        deliveryId: g.id,
        targetId: g.targetId
      }), c(`已提交目标 ${g.targetId} 的重试`), await h();
    } catch (p) {
      o(p instanceof Error ? p.message : String(p));
    } finally {
      d(!1);
    }
  }, [r, h]);
  return /* @__PURE__ */ S(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(B.Title, { level: 4, style: { margin: 0 }, children: "执行记录" }),
    s && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: s }),
    u && /* @__PURE__ */ f(Se, { type: "success", showIcon: !0, message: u }),
    /* @__PURE__ */ f(
      Ne,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        title: () => "采集运行",
        columns: [
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ f(Be, { color: Dh(g), children: g }) },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (g) => g ?? "—" },
          { title: "错误", dataIndex: "error", render: (g) => g ?? "—" }
        ]
      }
    ),
    /* @__PURE__ */ f(
      Ne,
      {
        rowKey: "id",
        dataSource: a,
        pagination: !1,
        title: () => "交付目标",
        columns: [
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (g) => /* @__PURE__ */ f(Be, { color: Dh(g), children: g }) },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (g, p) => p.diagnosis ? /* @__PURE__ */ S(Y, { direction: "vertical", size: 0, children: [
              /* @__PURE__ */ f(B.Text, { children: p.diagnosis.message }),
              /* @__PURE__ */ f(B.Text, { type: "secondary", children: p.diagnosis.action })
            ] }) : p.error ?? "—"
          },
          {
            title: "操作",
            key: "actions",
            render: (g, p) => /* @__PURE__ */ f(
              j,
              {
                size: "small",
                disabled: l || p.status !== "Failed" && p.status !== "Interrupted",
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
function p1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q(null), s = L(async () => {
    try {
      n(Jv(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-details")));
    } catch (u) {
      i(u instanceof Error ? u.message : String(u));
    }
  }, [e == null ? void 0 : e.query, r]);
  we(() => {
    s();
  }, [s]);
  const o = (u) => /* @__PURE__ */ f(
    Ne,
    {
      rowKey: "id",
      dataSource: u.deliveries,
      pagination: !1,
      size: "small",
      columns: [
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (c) => /* @__PURE__ */ f(Be, { children: c }) },
        { title: "记录", dataIndex: "records" },
        {
          title: "接受/拒绝/警告",
          key: "counts",
          render: (c, l) => `${l.accepted} / ${l.rejected} / ${l.warnings}`
        },
        {
          title: "原因与下一步",
          key: "diagnosis",
          render: (c, l) => l.diagnosis ? /* @__PURE__ */ S(Y, { direction: "vertical", size: 0, children: [
            /* @__PURE__ */ f(B.Text, { children: l.diagnosis.message }),
            /* @__PURE__ */ f(B.Text, { type: "secondary", children: l.diagnosis.action }),
            l.error && /* @__PURE__ */ f(B.Text, { type: "secondary", children: l.error })
          ] }) : l.error ?? "—"
        }
      ]
    }
  );
  return /* @__PURE__ */ S(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(B.Title, { level: 4, style: { margin: 0 }, children: "文件处理详情" }),
    a && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: a }),
    /* @__PURE__ */ f(
      Ne,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        expandable: { expandedRowRender: o },
        columns: [
          { title: "文件", dataIndex: "path" },
          { title: "源日期", dataIndex: "sourceDate" },
          { title: "状态", dataIndex: "status" },
          { title: "大小", dataIndex: "length", render: (u) => `${u} B` },
          {
            title: "目标数",
            key: "targets",
            render: (u, c) => c.deliveries.length
          }
        ]
      }
    )
  ] });
}
const Go = (e) => {
  const r = Pe(e);
  return Array.isArray(r) ? r : [];
}, v1 = () => `machine-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 12) : Date.now().toString(36)}`;
function g1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q([]), [u, c] = q(null), [l, d] = q(""), [h, v] = q(""), [g, p] = q(), [m, I] = q(!0), [k, b] = q(null), A = L(async () => {
    try {
      n(Go(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/machines"))), i(Go(await r.hubQuery("industrial/resources"))), o(Go(await r.hubQuery("industrial/credentials")));
    } catch (R) {
      b(R instanceof Error ? R.message : String(R));
    }
  }, [e == null ? void 0 : e.query, r]);
  we(() => {
    A();
  }, [A]);
  const M = L(() => {
    c({ id: v1(), name: "", endpoint: "", credentialId: null, enabled: !0 }), d(""), v(""), p(void 0), I(!0), b(null);
  }, []), y = L((R) => {
    c(R), d(R.name), v(R.endpoint), p(R.credentialId ?? void 0), I(R.enabled), b(null);
  }, []), x = L(async () => {
    if (u) {
      if (!l.trim()) {
        b("请输入设备名称");
        return;
      }
      b(null);
      try {
        await r.hubCommand("industrial/save-machine", {
          id: u.id,
          name: l.trim(),
          endpoint: h.trim(),
          credentialId: g ?? null,
          enabled: m
        }), c(null), await A();
      } catch (R) {
        b(R instanceof Error ? R.message : String(R));
      }
    }
  }, [r, g, u, h, m, A, l]), O = L(async (R) => {
    b(null);
    try {
      await r.hubCommand("industrial/delete-machine", { id: R.id }), await A();
    } catch (G) {
      b(G instanceof Error ? G.message : String(G));
    }
  }, [r, A]), V = L(async (R, G) => {
    b(null);
    try {
      await r.hubCommand("industrial/save-machine", {
        id: R.id,
        name: R.name,
        endpoint: R.endpoint,
        credentialId: R.credentialId,
        enabled: G
      }), await A();
    } catch (H) {
      b(H instanceof Error ? H.message : String(H));
    }
  }, [r, A]), F = L(async (R, G) => {
    b(null);
    try {
      await r.hubCommand("industrial/save-resource", {
        id: R.id,
        machineId: G || null
      }), await A();
    } catch (H) {
      b(H instanceof Error ? H.message : String(H));
    }
  }, [r, A]);
  return /* @__PURE__ */ S(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ S(Y, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(B.Title, { level: 4, style: { margin: 0 }, children: "设备与资源" }),
      /* @__PURE__ */ f(j, { type: "primary", onClick: M, children: "新增设备" })
    ] }),
    /* @__PURE__ */ f(B.Text, { type: "secondary", children: "设备地址是台账属性；实际连接由数据源资源配置决定。绑定凭证供需要认证的数据源引用。" }),
    k && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: k }),
    /* @__PURE__ */ f(
      Ne,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "设备", dataIndex: "name" },
          { title: "台账地址", dataIndex: "endpoint" },
          {
            title: "凭证",
            dataIndex: "credentialId",
            render: (R) => {
              var G;
              return ((G = s.find((H) => H.id === R)) == null ? void 0 : G.name) ?? "—";
            }
          },
          {
            title: "启用",
            dataIndex: "enabled",
            render: (R, G) => /* @__PURE__ */ f(
              Xr,
              {
                "aria-label": `设备 ${G.name} 启用`,
                checked: R,
                onChange: (H) => void V(G, H)
              }
            )
          },
          { title: "操作", key: "actions", render: (R, G) => /* @__PURE__ */ S(Y, { children: [
            /* @__PURE__ */ f(j, { size: "small", onClick: () => y(G), children: "编辑" }),
            /* @__PURE__ */ f(j, { size: "small", danger: !0, onClick: () => void O(G), children: "删除" })
          ] }) }
        ]
      }
    ),
    /* @__PURE__ */ f(B.Title, { level: 5, style: { margin: 0 }, children: "资源归属" }),
    /* @__PURE__ */ f(
      Ne,
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
            render: (R, G) => /* @__PURE__ */ f(
              Ke,
              {
                "aria-label": `资源 ${G.name} 的设备`,
                style: { width: 220 },
                value: G.machineId ?? void 0,
                allowClear: !0,
                onChange: (H) => void F(G, H ?? ""),
                options: t.map((H) => ({ value: H.id, label: H.name }))
              }
            )
          }
        ]
      }
    ),
    /* @__PURE__ */ f(
      Er,
      {
        open: u !== null,
        title: u != null && u.name ? "编辑设备" : "新增设备",
        okText: "保存设备",
        cancelText: "取消",
        onCancel: () => c(null),
        onOk: () => void x(),
        children: /* @__PURE__ */ S(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "设备名称",
              placeholder: "设备名称",
              value: l,
              onChange: (R) => d(R.target.value)
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "设备地址",
              placeholder: "台账地址 / IP",
              value: h,
              onChange: (R) => v(R.target.value)
            }
          ),
          /* @__PURE__ */ f(
            Ke,
            {
              "aria-label": "设备凭证",
              style: { width: "100%" },
              allowClear: !0,
              placeholder: "凭证（可选）",
              value: g,
              onChange: (R) => p(R),
              options: s.map((R) => ({
                value: R.id,
                label: `${R.name} (${R.kind})`
              }))
            }
          )
        ] })
      }
    )
  ] });
}
const y1 = ["filesystem.local", "filesystem.unc"], m1 = ["sink.sqlite", "sink.sqlserver", "sink.postgresql", "sink.mysql"];
function b1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q(""), [u, c] = q(""), [l, d] = q("csv"), [h, v] = q([]), [g, p] = q(null), [m, I] = q(!1);
  we(() => {
    (async () => {
      try {
        n(Uh(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/resources"))), i(Bh(await r.hubQuery((e == null ? void 0 : e.nodesQuery) ?? "industrial/nodes")));
      } catch (y) {
        p(y instanceof Error ? y.message : String(y));
      }
    })();
  }, [e == null ? void 0 : e.nodesQuery, e == null ? void 0 : e.query, r]);
  const k = t.filter((y) => y1.includes(y.type)), b = t.filter((y) => m1.some((x) => y.type === x.split(".")[1])), A = Qo(
    () => new Map(a.map((y) => [y.nodeTypeId, y])),
    [a]
  ), M = L(async () => {
    var y;
    if (!s.trim()) {
      p("请输入任务名称");
      return;
    }
    if (!u) {
      p("请选择数据来源");
      return;
    }
    if (h.length === 0) {
      p("请至少选择一个目标");
      return;
    }
    I(!0), p(null);
    try {
      const O = `quick-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`, F = `industrial.${t.find((U) => U.id === u).type}`, R = `industrial.reader.${l}`, G = (U, Q, E, T, $) => ({ id: U, type: Q, config: $, x: E, y: T }), H = (U, Q) => {
        var $, pe;
        const E = A.get(ae[U]), T = A.get(ae[Q]);
        return {
          source: U,
          sourcePort: (($ = E.outputs[0]) == null ? void 0 : $.name) ?? "output",
          target: Q,
          targetPort: ((pe = T.inputs[0]) == null ? void 0 : pe.name) ?? "input"
        };
      }, z = [
        G("src", F, 80, 120, { resourceId: u }),
        G("read", R, 280, 120, {}),
        ...h.map((U, Q) => G(
          `sink${Q + 1}`,
          `industrial.sink.${t.find((E) => E.id === U).type}`,
          500,
          80 + Q * 120,
          { targetResourceId: U }
        ))
      ], ae = Object.fromEntries(z.map((U) => [U.id, U.type])), te = [H("src", "read"), ...h.map((U, Q) => H("read", `sink${Q + 1}`))], de = {
        ...pt(O),
        name: s.trim(),
        nodes: z,
        edges: te,
        resourceReferences: [u, ...h],
        layout: { lookbackDays: 1 }
      };
      await r.hubCommand("industrial/save-workflow", de), await r.hubCommand("industrial/publish-workflow", { workflowId: O }), (y = r.navigate) == null || y.call(r, `/industrial/designer?workflowId=${encodeURIComponent(O)}`);
    } catch (x) {
      p(x instanceof Error ? x.message : String(x));
    } finally {
      I(!1);
    }
  }, [r, l, A, s, t, u, h]);
  return /* @__PURE__ */ S(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(B.Title, { level: 4, style: { margin: 0 }, children: "快速创建采集任务" }),
    /* @__PURE__ */ f(B.Text, { type: "secondary", children: "生成标准工作流，可在设计器中继续修改。" }),
    g && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: g }),
    /* @__PURE__ */ f(
      K,
      {
        "aria-label": "任务名称",
        placeholder: "任务名称",
        value: s,
        onChange: (y) => o(y.target.value)
      }
    ),
    /* @__PURE__ */ S(Y, { wrap: !0, children: [
      /* @__PURE__ */ f(
        Ke,
        {
          "aria-label": "数据来源",
          style: { width: 260 },
          placeholder: "数据来源",
          value: u || void 0,
          onChange: (y) => c(y),
          options: k.map((y) => ({ value: y.id, label: `${y.name} (${y.type})` }))
        }
      ),
      /* @__PURE__ */ f(
        Ke,
        {
          "aria-label": "数据格式",
          style: { width: 160 },
          value: l,
          onChange: (y) => d(y),
          options: ["csv", "excel", "json"].map((y) => ({ value: y, label: y.toUpperCase() }))
        }
      )
    ] }),
    /* @__PURE__ */ f(
      "select",
      {
        "aria-label": "目标资源 ID 列表",
        multiple: !0,
        style: { width: 420, minHeight: 72 },
        value: h,
        onChange: (y) => v(Array.from(y.target.selectedOptions).map((x) => x.value)),
        children: b.map((y) => /* @__PURE__ */ S("option", { value: y.id, children: [
          y.name,
          " (",
          y.type,
          ")"
        ] }, y.id))
      }
    ),
    /* @__PURE__ */ f(j, { type: "primary", loading: m, onClick: () => void M(), children: "创建并发布" })
  ] });
}
const _1 = (e) => {
  const r = Pe(e);
  return Array.isArray(r) ? r : [];
}, w1 = () => ({
  id: `profile-${typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`,
  name: "",
  version: 0,
  fileNamePattern: "*.csv",
  format: "csv",
  delimiter: ",",
  hasHeader: !0,
  encoding: "utf-8",
  enabled: !0,
  mapping: {},
  requiredFields: [],
  directoryPattern: "",
  datePattern: "",
  flat: !1,
  metadataRules: []
});
function I1({ block: e, ctx: r }) {
  const [t, n] = q([]), [a, i] = q(null), [s, o] = q(""), [u, c] = q(""), [l, d] = q(""), [h, v] = q(null), g = L(async () => {
    try {
      n(_1(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/file-profiles")));
    } catch (I) {
      v(I instanceof Error ? I.message : String(I));
    }
  }, [e == null ? void 0 : e.query, r]);
  we(() => {
    g();
  }, [g]);
  const p = L((I) => {
    const k = I ? { ...I } : w1();
    i(k), o(Object.entries(k.mapping ?? {}).map(([b, A]) => `${b}=${A}`).join(",")), c((k.requiredFields ?? []).join(",")), d((k.metadataRules ?? []).map((b) => `${b.from}|${b.pattern}|${b.key}|${b.required}`).join(`
`)), v(null);
  }, []), m = L(async () => {
    if (a) {
      if (!a.name.trim()) {
        v("请输入配置名称");
        return;
      }
      v(null);
      try {
        const I = {};
        for (const b of s.split(/[,;]/)) {
          const [A, M] = b.split("=").map((y) => y == null ? void 0 : y.trim());
          A && M && (I[A] = M);
        }
        const k = l.split(/\r?\n/).map((b) => b.split("|")).filter((b) => b.length >= 3 && b[0].trim() && b[1].trim() && b[2].trim()).map((b, A) => {
          var y;
          const M = b[1].trim().replace(/\(\?P</g, "(?<");
          try {
            new RegExp(M);
          } catch (x) {
            throw new Error(`元数据规则第 ${A + 1} 行正则无效：${x instanceof Error ? x.message : String(x)}`);
          }
          return {
            from: b[0].trim(),
            pattern: M,
            key: b[2].trim(),
            required: ((y = b[3]) == null ? void 0 : y.trim()) !== "false"
          };
        });
        await r.hubCommand("industrial/save-file-profile", {
          id: a.id,
          name: a.name.trim(),
          fileNamePattern: a.fileNamePattern,
          format: a.format,
          delimiter: a.delimiter,
          hasHeader: a.hasHeader,
          encoding: a.encoding,
          enabled: a.enabled,
          mapping: I,
          required: u.split(/[,;]/).map((b) => b.trim()).filter(Boolean),
          directoryPattern: a.directoryPattern,
          datePattern: a.datePattern,
          flat: a.flat,
          metadataRules: k
        }), i(null), await g();
      } catch (I) {
        v(I instanceof Error ? I.message : String(I));
      }
    }
  }, [r, a, g, s, l, u]);
  return /* @__PURE__ */ S(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ S(Y, { style: { width: "100%", justifyContent: "space-between" }, children: [
      /* @__PURE__ */ f(B.Title, { level: 4, style: { margin: 0 }, children: "文件格式配置" }),
      /* @__PURE__ */ f(j, { type: "primary", onClick: () => p(), children: "新建文件配置" })
    ] }),
    h && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: h }),
    /* @__PURE__ */ f(
      Ne,
      {
        rowKey: "id",
        dataSource: t,
        pagination: !1,
        columns: [
          { title: "名称", dataIndex: "name" },
          { title: "文件匹配", dataIndex: "fileNamePattern" },
          { title: "格式", dataIndex: "format" },
          { title: "版本", dataIndex: "version" },
          { title: "操作", key: "actions", render: (I, k) => /* @__PURE__ */ f(j, { size: "small", onClick: () => p(k), children: "编辑" }) }
        ]
      }
    ),
    /* @__PURE__ */ f(
      Er,
      {
        open: a !== null,
        title: a != null && a.version ? "编辑文件配置" : "新建文件配置",
        okText: "保存配置",
        cancelText: "取消",
        onCancel: () => i(null),
        onOk: () => void m(),
        children: a && /* @__PURE__ */ S(Y, { direction: "vertical", style: { width: "100%" }, children: [
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "配置名称",
              placeholder: "配置名称",
              value: a.name,
              onChange: (I) => i({ ...a, name: I.target.value })
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "文件匹配规则",
              placeholder: "文件匹配，例如 *.csv",
              value: a.fileNamePattern,
              onChange: (I) => i({ ...a, fileNamePattern: I.target.value })
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "文件格式",
              placeholder: "csv / excel / json",
              value: a.format,
              onChange: (I) => i({ ...a, format: I.target.value })
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "分隔符",
              placeholder: "分隔符",
              value: a.delimiter,
              onChange: (I) => i({ ...a, delimiter: I.target.value })
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "编码",
              placeholder: "编码",
              value: a.encoding,
              onChange: (I) => i({ ...a, encoding: I.target.value })
            }
          ),
          /* @__PURE__ */ f(
            Xr,
            {
              "aria-label": "首行为表头",
              checked: a.hasHeader,
              onChange: (I) => i({ ...a, hasHeader: I })
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "字段映射",
              placeholder: "目标字段=来源字段，逗号分隔",
              value: s,
              onChange: (I) => o(I.target.value)
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "必填字段",
              placeholder: "必填字段，逗号分隔",
              value: u,
              onChange: (I) => c(I.target.value)
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "日期目录模板",
              placeholder: "YYYY-MM-DD / 留空按递归扫描",
              value: a.directoryPattern,
              onChange: (I) => i({ ...a, directoryPattern: I.target.value })
            }
          ),
          /* @__PURE__ */ f(
            K,
            {
              "aria-label": "日期文件模板",
              placeholder: "yyyyMMdd-*.csv / 留空按文件名解析",
              value: a.datePattern,
              onChange: (I) => i({ ...a, datePattern: I.target.value })
            }
          ),
          /* @__PURE__ */ f(
            Xr,
            {
              "aria-label": "单文件模式",
              checked: a.flat,
              onChange: (I) => i({ ...a, flat: I })
            }
          ),
          /* @__PURE__ */ f(
            K.TextArea,
            {
              "aria-label": "元数据规则",
              rows: 3,
              placeholder: "每行：filename|正则(?P<value>...)|字段名|required",
              value: l,
              onChange: (I) => d(I.target.value)
            }
          )
        ] })
      }
    )
  ] });
}
const ft = (e) => {
  const r = Pe(e);
  return Array.isArray(r) ? r : [];
}, q1 = (e) => new Promise((r, t) => {
  const n = new FileReader();
  n.onload = () => r(String(n.result).split(",")[1] ?? ""), n.onerror = () => t(n.error ?? new Error("无法读取 Excel 文件")), n.readAsDataURL(e);
});
function C1({ block: e, ctx: r }) {
  var fe, _e, xe, Ge;
  const [t, n] = q([]), [a, i] = q([]), [s, o] = q([]), [u, c] = q([]), [l, d] = q([]), [h, v] = q([]), [g, p] = q(), [m, I] = q(1), [k, b] = q("resource-{machine}"), [A, M] = q([]), [y, x] = q(""), [O, V] = q(""), [F, R] = q(null), [G, H] = q(""), [z, ae] = q(""), [te, de] = q(null), [U, Q] = q("manual"), [E, T] = q(""), [$, pe] = q(""), [Ie, Re] = q(""), [nr, Ar] = q(""), [qr, Pr] = q(""), [pr, Or] = q(""), [We, ar] = q(null), [Ye, ie] = q(null), [ce, X] = q(null), [Te, Qe] = q(1), [ir, vr] = q(""), [$e, W] = q(null), [ee, oe] = q(!1), ye = L(async () => {
    try {
      n(ft(await r.hubQuery((e == null ? void 0 : e.query) ?? "industrial/bindings"))), i(ft(await r.hubQuery("industrial/machines"))), o(ft(await r.hubQuery("industrial/file-profiles"))), c(ft(await r.hubQuery("industrial/workflows")));
      try {
        de(await r.hubQuery("industrial/binding-status"));
      } catch {
      }
    } catch (w) {
      W(w instanceof Error ? w.message : String(w));
    }
  }, [e == null ? void 0 : e.query, r]);
  we(() => {
    ye();
  }, [ye]);
  const gr = () => typeof crypto < "u" && "randomUUID" in crypto ? crypto.randomUUID() : `r-${Date.now()}-${Math.random().toString(36).slice(2)}`, Le = () => {
    if (U !== "schedule") return null;
    const w = E.trim().split(/\s+/).filter(Boolean);
    if (w.length < 5 || w.length > 7)
      return "定时策略需要 5–7 段的有效 cron 表达式";
    try {
      $.trim() && new Intl.DateTimeFormat("en-US", { timeZone: $.trim() });
    } catch {
      return `无效的时区：${$}`;
    }
    return null;
  }, sr = L(() => ({
    machineIds: l,
    fileProfileIds: h,
    workflowId: g,
    workflowVersion: m,
    sourceResourcePattern: k,
    targetResourceIds: A,
    triggerKind: U,
    cron: E || null,
    timezone: $ || null,
    parameters: Object.fromEntries(qr.split(";").map((w) => w.split("=")).filter((w) => w.length === 2 && w[0].trim() && w[1].trim()).map((w) => [w[0].trim(), w[1].trim()])),
    exclusions: pr.split(/[,\n;]/).map((w) => w.trim()).filter(Boolean)
  }), [E, pr, l, h, qr, k, A, $, U, m, g]), er = L(async (w) => {
    const N = gr();
    await r.hubCommand("industrial/preview-bindings", { ...w, requestId: N });
    const re = await r.hubQuery(`industrial/preview-result/${N}`);
    return ar(re), re;
  }, [r]), zr = L(async () => {
    var re, ke, Ue;
    if (!g || l.length === 0 || h.length === 0 || A.length === 0) {
      W("请选择工作流、机台、文件配置和目标资源");
      return;
    }
    const w = Le();
    if (w) {
      W(w);
      return;
    }
    const N = sr();
    oe(!0), W(null);
    try {
      const qe = await er(N);
      if ((((re = qe == null ? void 0 : qe.errors) == null ? void 0 : re.length) ?? 0) > 0 || (((ke = qe == null ? void 0 : qe.missing) == null ? void 0 : ke.length) ?? 0) > 0 || (((Ue = qe == null ? void 0 : qe.conflicts) == null ? void 0 : Ue.length) ?? 0) > 0) {
        W("服务端预览发现问题，请检查冲突、缺失资源或例外规则后再创建");
        return;
      }
      await r.hubCommand("industrial/create-bindings", N), await ye();
    } catch (qe) {
      W(qe instanceof Error ? qe.message : String(qe));
    } finally {
      oe(!1);
    }
  }, [sr, r, ye, l, h, er, A, g]), Nr = L(async (w, N) => {
    oe(!0), W(null);
    try {
      await r.hubCommand("industrial/set-binding-enabled", { id: w.id, enabled: N }), await ye();
    } catch (re) {
      W(re instanceof Error ? re.message : String(re));
    } finally {
      oe(!1);
    }
  }, [r, ye]), je = L(async (w, N) => {
    oe(!0), W(null);
    try {
      await r.hubCommand("industrial/run-binding", {
        bindingId: w.id,
        lookbackDays: N
      }), de(await r.hubQuery("industrial/binding-status"));
    } catch (re) {
      W(re instanceof Error ? re.message : String(re));
    } finally {
      oe(!1);
    }
  }, [r]), Je = L(async () => {
    oe(!0), W(null);
    try {
      await r.hubCommand("industrial/import-file-profiles", { csv: Ie }), Re(""), await ye();
    } catch (w) {
      W(w instanceof Error ? w.message : String(w));
    } finally {
      oe(!1);
    }
  }, [r, ye, Ie]), Ft = L(async () => {
    oe(!0), W(null);
    try {
      await r.hubCommand("industrial/import-bindings", { csv: nr }), Ar(""), await ye();
    } catch (w) {
      W(w instanceof Error ? w.message : String(w));
    } finally {
      oe(!1);
    }
  }, [r, ye, nr]), Dr = L(async (w, N) => {
    if (N) {
      oe(!0), W(null);
      try {
        const re = w === "profiles" ? "industrial/import-file-profiles" : `industrial/import-${w}`;
        await r.hubCommand(re, { xlsx: await q1(N) }), await ye();
      } catch (re) {
        W(re instanceof Error ? re.message : String(re));
      } finally {
        oe(!1);
      }
    }
  }, [r, ye]), st = L(async () => {
    const w = Le();
    if (w) {
      W(w);
      return;
    }
    oe(!0), W(null);
    try {
      await er(sr());
    } catch (N) {
      W(N instanceof Error ? N.message : String(N));
    } finally {
      oe(!1);
    }
  }, [sr, er]), C = L(async () => {
    oe(!0), W(null);
    try {
      const w = gr();
      await r.hubCommand("industrial/preview-import", { csv: nr, requestId: w }), ie(await r.hubQuery(`industrial/preview-result/${w}`));
    } catch (w) {
      W(w instanceof Error ? w.message : String(w));
    } finally {
      oe(!1);
    }
  }, [r, nr]), D = L(async () => {
    oe(!0), W(null);
    try {
      const w = t.filter((N) => N.workflowId === g).map((N) => N.id);
      await r.hubCommand("industrial/upgrade-binding-version", { ids: w, version: Te }), await ye();
    } catch (w) {
      W(w instanceof Error ? w.message : String(w));
    } finally {
      oe(!1);
    }
  }, [t, r, ye, Te, g]), ue = L(async () => {
    oe(!0), W(null);
    try {
      await r.hubCommand("industrial/import-machines", { csv: y }), x(""), await ye();
    } catch (w) {
      W(w instanceof Error ? w.message : String(w));
    } finally {
      oe(!1);
    }
  }, [r, y, ye]), se = L(async () => {
    W(null);
    try {
      const w = await r.hubQuery("industrial/export-config"), N = (w == null ? void 0 : w.data) ?? w, re = JSON.stringify(N, null, 2);
      V(re), R(N);
      const ke = new Blob([re], { type: "application/json" }), Ue = window.URL.createObjectURL(ke), qe = document.createElement("a");
      qe.href = Ue, qe.download = `gocordis-industrial-config-v${(N == null ? void 0 : N.formatVersion) ?? 1}.json`, qe.click(), window.URL.revokeObjectURL(Ue);
    } catch (w) {
      W(w instanceof Error ? w.message : String(w));
    }
  }, [r]), he = (w) => {
    const N = {};
    for (const re of w.split(/[\n,;]/)) {
      const [ke, Ue] = re.split("=").map((qe) => qe == null ? void 0 : qe.trim());
      ke && Ue && (N[ke] = Ue);
    }
    return N;
  }, ve = L(async (w) => {
    if (w) {
      W(null);
      try {
        const N = await w.text();
        V(N), R(JSON.parse(N));
      } catch (N) {
        W(N instanceof Error ? N.message : String(N));
      }
    }
  }, []), Fe = L(async () => {
    oe(!0), W(null), X(null);
    try {
      const w = JSON.parse(O), N = he(G), re = he(z);
      Object.keys(N).length > 0 && (w.resourceMap = { ...w.resourceMap ?? {}, ...N }), Object.keys(re).length > 0 && (w.credentialMap = { ...w.credentialMap ?? {}, ...re });
      const ke = gr();
      await r.hubCommand("industrial/import-config", { ...w, requestId: ke }), X(await r.hubQuery(`industrial/preview-result/${ke}`)), await ye();
    } catch (w) {
      W(w instanceof Error ? w.message : String(w));
    } finally {
      oe(!1);
    }
  }, [r, z, ye, O, G]);
  return /* @__PURE__ */ S(Y, { direction: "vertical", size: 12, style: { width: "100%" }, children: [
    /* @__PURE__ */ f(B.Title, { level: 4, style: { margin: 0 }, children: "采集任务绑定" }),
    $e && /* @__PURE__ */ f(Se, { type: "error", showIcon: !0, message: $e }),
    /* @__PURE__ */ S(Y, { wrap: !0, children: [
      /* @__PURE__ */ f(
        K,
        {
          "aria-label": "工作流 ID",
          placeholder: "工作流 ID",
          value: g ?? "",
          onChange: (w) => p(w.target.value)
        }
      ),
      /* @__PURE__ */ f(
        ht,
        {
          "aria-label": "模板版本",
          min: 1,
          value: m,
          onChange: (w) => I(Number(w) || 1)
        }
      ),
      /* @__PURE__ */ f(
        K,
        {
          "aria-label": "机台 ID 列表",
          placeholder: "机台 ID，逗号分隔",
          value: l.join(","),
          onChange: (w) => d(w.target.value.split(",").map((N) => N.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ f(
        K,
        {
          "aria-label": "文件配置 ID 列表",
          placeholder: "文件配置 ID，逗号分隔",
          value: h.join(","),
          onChange: (w) => v(w.target.value.split(",").map((N) => N.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ f(
        K,
        {
          "aria-label": "目标资源 ID 列表",
          placeholder: "目标资源 ID，逗号分隔",
          value: A.join(","),
          onChange: (w) => M(w.target.value.split(",").map((N) => N.trim()).filter(Boolean))
        }
      ),
      /* @__PURE__ */ S(
        "select",
        {
          "aria-label": "绑定触发策略",
          value: U,
          onChange: (w) => Q(w.target.value),
          children: [
            /* @__PURE__ */ f("option", { value: "manual", children: "手动" }),
            /* @__PURE__ */ f("option", { value: "schedule", children: "定时" })
          ]
        }
      ),
      /* @__PURE__ */ f(
        K,
        {
          "aria-label": "绑定 Cron",
          placeholder: "cron（定时策略）",
          value: E,
          onChange: (w) => T(w.target.value)
        }
      ),
      /* @__PURE__ */ f(
        K,
        {
          "aria-label": "绑定时区",
          placeholder: "时区（可选，如 Asia/Shanghai）",
          value: $,
          onChange: (w) => pe(w.target.value)
        }
      ),
      /* @__PURE__ */ f(
        K,
        {
          "aria-label": "来源资源模板",
          placeholder: "来源资源模板，{machine} 占位符",
          value: k,
          onChange: (w) => b(w.target.value)
        }
      ),
      /* @__PURE__ */ f(
        K,
        {
          "aria-label": "运行参数",
          placeholder: "运行参数，如 lookbackDays=7;shift=day",
          value: qr,
          onChange: (w) => Pr(w.target.value)
        }
      ),
      /* @__PURE__ */ f(
        K,
        {
          "aria-label": "例外规则",
          placeholder: "例外规则：机台:文件配置，逗号或换行分隔",
          value: pr,
          onChange: (w) => Or(w.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ S(Y, { wrap: !0, children: [
      /* @__PURE__ */ f(j, { size: "small", onClick: () => d(a.map((w) => w.id)), children: "全选机台" }),
      /* @__PURE__ */ f(j, { size: "small", onClick: () => v(s.map((w) => w.id)), children: "全选文件配置" }),
      /* @__PURE__ */ f(j, { size: "small", onClick: () => {
        d([]), v([]);
      }, children: "清空范围" })
    ] }),
    /* @__PURE__ */ S(Y, { wrap: !0, children: [
      /* @__PURE__ */ f(j, { type: "primary", loading: ee, onClick: () => void zr(), children: "生成绑定" }),
      /* @__PURE__ */ f(j, { loading: ee, onClick: () => void st(), children: "预览绑定" }),
      /* @__PURE__ */ f(j, { loading: ee, onClick: () => void C(), children: "预览导入" })
    ] }),
    We && /* @__PURE__ */ f(
      Se,
      {
        type: "info",
        showIcon: !0,
        "data-testid": "binding-preview-report",
        message: `批量预览：${We.total} 组合，创建 ${We.create}`,
        description: `冲突 ${((fe = We.conflicts) == null ? void 0 : fe.length) ?? 0}；缺失 ${((_e = We.missing) == null ? void 0 : _e.length) ?? 0}；错误 ${((xe = We.errors) == null ? void 0 : xe.length) ?? 0}`
      }
    ),
    Ye && /* @__PURE__ */ f(
      Se,
      {
        type: (Ge = Ye.errors) != null && Ge.length ? "warning" : "success",
        showIcon: !0,
        "data-testid": "import-preview-report",
        message: `导入预览：${Ye.valid ?? 0}/${Ye.total ?? 0} 行有效`,
        description: (Ye.errors ?? []).map((w, N) => /* @__PURE__ */ S("div", { "data-testid": `import-error-row-${w.row}`, children: [
          "第 ",
          w.row,
          " 行：",
          w.message
        ] }, N))
      }
    ),
    /* @__PURE__ */ S(Y, { wrap: !0, children: [
      /* @__PURE__ */ f(
        ht,
        {
          "aria-label": "升级到模板版本",
          min: 1,
          value: Te,
          onChange: (w) => Qe(Number(w) || 1)
        }
      ),
      /* @__PURE__ */ f(j, { loading: ee, onClick: () => void D(), children: "升级当前工作流绑定版本" })
    ] }),
    /* @__PURE__ */ f(B.Title, { level: 5, style: { margin: 0 }, children: "批量导入机台（CSV）" }),
    /* @__PURE__ */ f(
      K.TextArea,
      {
        "aria-label": "机台 CSV",
        rows: 4,
        value: y,
        placeholder: "id,name,endpoint,credentialId",
        onChange: (w) => x(w.target.value)
      }
    ),
    /* @__PURE__ */ f(j, { loading: ee, onClick: () => void ue(), children: "导入机台" }),
    /* @__PURE__ */ f(
      K.TextArea,
      {
        "aria-label": "文件配置 CSV",
        rows: 3,
        value: Ie,
        placeholder: "id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required",
        onChange: (w) => Re(w.target.value)
      }
    ),
    /* @__PURE__ */ f(j, { loading: ee, onClick: () => void Je(), children: "导入文件配置" }),
    /* @__PURE__ */ f(
      K.TextArea,
      {
        "aria-label": "绑定 CSV",
        rows: 3,
        value: nr,
        placeholder: "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId,targetResourceIds,triggerKind,cron",
        onChange: (w) => Ar(w.target.value)
      }
    ),
    /* @__PURE__ */ f(j, { loading: ee, onClick: () => void Ft(), children: "导入采集绑定" }),
    /* @__PURE__ */ f(B.Title, { level: 5, style: { margin: 0 }, children: "Excel 批量导入（.xlsx，选择即导入）" }),
    /* @__PURE__ */ S(Y, { wrap: !0, children: [
      /* @__PURE__ */ S("label", { children: [
        "机台 Excel",
        /* @__PURE__ */ f(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "机台 Excel 文件",
            onChange: (w) => {
              var N;
              return void Dr("machines", (N = w.target.files) == null ? void 0 : N[0]);
            }
          }
        )
      ] }),
      /* @__PURE__ */ S("label", { children: [
        "文件配置 Excel",
        /* @__PURE__ */ f(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "文件配置 Excel 文件",
            onChange: (w) => {
              var N;
              return void Dr("profiles", (N = w.target.files) == null ? void 0 : N[0]);
            }
          }
        )
      ] }),
      /* @__PURE__ */ S("label", { children: [
        "采集绑定 Excel",
        /* @__PURE__ */ f(
          "input",
          {
            type: "file",
            accept: ".xlsx",
            "aria-label": "绑定 Excel 文件",
            onChange: (w) => {
              var N;
              return void Dr("bindings", (N = w.target.files) == null ? void 0 : N[0]);
            }
          }
        )
      ] })
    ] }),
    /* @__PURE__ */ f(B.Title, { level: 5, style: { margin: 0 }, children: "配置包（不含凭证）" }),
    /* @__PURE__ */ S(Y, { wrap: !0, children: [
      /* @__PURE__ */ f(j, { onClick: () => void se(), children: "导出配置" }),
      /* @__PURE__ */ f(j, { loading: ee, onClick: () => void Fe(), children: "导入配置" }),
      /* @__PURE__ */ S("label", { children: [
        "上传配置文件",
        /* @__PURE__ */ f(
          "input",
          {
            type: "file",
            accept: ".json,application/json",
            "aria-label": "上传配置文件",
            onChange: (w) => {
              var N;
              return void ve((N = w.target.files) == null ? void 0 : N[0]);
            }
          }
        )
      ] })
    ] }),
    ce && /* @__PURE__ */ f(
      Se,
      {
        type: "warning",
        showIcon: !0,
        "data-testid": "config-import-report",
        message: `配置导入：工作流 ${ce.workflows ?? 0} · 机台 ${ce.machines ?? 0} · 资源 ${ce.resources ?? 0} · 绑定 ${ce.bindings ?? 0}`,
        description: ce.publishRequired ? "导入的工作流保存为草稿，必须在设计器中发布到声明版本后绑定才能运行。" : ce.message
      }
    ),
    (F == null ? void 0 : F.dependencies) && /* @__PURE__ */ f(
      Se,
      {
        type: "info",
        showIcon: !0,
        "data-testid": "package-dependencies",
        message: `配置包 v${F.formatVersion ?? 1} · ${F.dependencies.length} 项依赖`,
        description: F.dependencies.map((w, N) => /* @__PURE__ */ S("div", { children: [
          w.kind,
          ": ",
          w.id,
          w.version ? ` @ ${w.version}` : ""
        ] }, N))
      }
    ),
    /* @__PURE__ */ f(
      K,
      {
        "aria-label": "资源 ID 映射",
        placeholder: "旧资源ID=新资源ID，逗号或换行分隔",
        value: G,
        onChange: (w) => H(w.target.value)
      }
    ),
    /* @__PURE__ */ f(
      K,
      {
        "aria-label": "凭证别名映射",
        placeholder: "旧凭证ID=新凭证ID",
        value: z,
        onChange: (w) => ae(w.target.value)
      }
    ),
    /* @__PURE__ */ f(
      K.TextArea,
      {
        "aria-label": "配置包",
        rows: 5,
        value: O,
        placeholder: "导出后可跨环境导入；资源 ID 需自行映射",
        onChange: (w) => V(w.target.value)
      }
    ),
    /* @__PURE__ */ f(
      K,
      {
        "aria-label": "绑定筛选",
        placeholder: "按绑定名称/ID筛选",
        value: ir,
        onChange: (w) => vr(w.target.value)
      }
    ),
    /* @__PURE__ */ f(
      Ne,
      {
        rowKey: "id",
        dataSource: t.filter((w) => {
          const N = ir.trim().toLocaleLowerCase();
          return N ? [
            w.id,
            w.name,
            w.machineId,
            w.fileProfileId,
            w.workflowId,
            w.workflowVersion,
            w.sourceResourceId,
            ...w.targetResourceIds ?? [],
            ...Object.entries(w.parameters ?? {}).flatMap(([re, ke]) => [re, String(ke)]),
            w.enabled ? "恢复" : "暂停"
          ].some((re) => String(re ?? "").toLocaleLowerCase().includes(N)) : !0;
        }),
        pagination: { pageSize: 20 },
        columns: [
          { title: "绑定", dataIndex: "name" },
          { title: "机台", dataIndex: "machineId" },
          { title: "文件配置", dataIndex: "fileProfileId" },
          { title: "模板版本", dataIndex: "workflowVersion" },
          { title: "运行参数", key: "parameters", render: (w, N) => /* @__PURE__ */ f("span", { "data-testid": `binding-parameters-${N.id}`, children: Object.entries(N.parameters ?? {}).map(([re, ke]) => `${re}=${ke}`).join(";") || "—" }) },
          { title: "状态", dataIndex: "enabled", render: (w, N) => /* @__PURE__ */ S(Y, { children: [
            /* @__PURE__ */ f(j, { size: "small", onClick: () => void Nr(N, !N.enabled), children: N.enabled ? "暂停" : "恢复" }),
            /* @__PURE__ */ f(j, { size: "small", onClick: () => void je(N, 7), children: "补采 7 天" }),
            /* @__PURE__ */ f(j, { size: "small", onClick: () => void je(N, 1), children: "运行" })
          ] }) },
          { title: "排障", key: "diagnostics", render: (w, N) => {
            const re = ((te == null ? void 0 : te.runs) ?? []).filter((Ue) => Ue.workflowId === N.workflowId), ke = ((te == null ? void 0 : te.deliveries) ?? []).filter((Ue) => Ue.status === "Failed");
            return /* @__PURE__ */ S(B.Text, { style: { fontSize: 11 }, children: [
              re.length ? `最近运行 ${re[0].status}` : "暂无运行",
              " · 失败交付 ",
              ke.length
            ] });
          } }
        ]
      }
    )
  ] });
}
const E1 = `
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
.industrial-align-guide{position:absolute;left:50%;top:10px;transform:translateX(-50%);z-index:3;pointer-events:none;padding:3px 9px;border-radius:999px;background:#4d6bfe;color:#fff;font-size:11px}
.industrial-graph-hint button{pointer-events:auto;margin-left:8px;border:0;border-radius:999px;background:#fff;color:#102548;padding:1px 8px;font-size:11px;cursor:pointer}
.industrial-properties-drawer .ant-drawer-body{padding:14px 18px}
.industrial-properties-body{min-width:0}
.industrial-edge-ports{display:flex;flex-direction:column;gap:4px}
.industrial-issue{display:flex;flex-direction:column;gap:2px;margin-bottom:8px;font-size:12px}
.industrial-kv-editor,.industrial-list-editor{display:flex;flex-direction:column;gap:5px}
.industrial-kv-row,.industrial-list-row{display:flex;align-items:center;gap:4px}
.industrial-kv-row>*:first-child,.industrial-list-row>*:first-child{flex:1;min-width:0}
.industrial-editor-error{display:block;font-size:11px}
@media(max-width:1180px){.industrial-body{grid-template-columns:224px minmax(0,1fr)}.industrial-resource-select{width:104px}.industrial-backfill-input{width:104px}}
.industrial-context-menu{position:absolute;z-index:30;display:flex;flex-direction:column;min-width:120px;padding:4px;background:#fff;border:1px solid #dfe7f2;border-radius:8px;box-shadow:0 6px 18px rgba(20,40,80,.16)}
.industrial-context-menu button{border:0;background:transparent;padding:6px 8px;text-align:left;font-size:12px;color:#1f2b3d;cursor:pointer;border-radius:4px}
.industrial-context-menu button:hover{background:#f0f4ff}
.industrial-sample-preview{max-height:110px;overflow:auto;padding:6px 8px;background:#f7f9fc;border:1px solid #e8edf4;border-radius:6px;font-size:11px;margin:4px 0 8px}
`;
function S1(e) {
  dv(e);
  const r = typeof document > "u" ? null : document.createElement("style");
  r && (r.dataset.industrialDesigner = "true", r.textContent = E1, document.head.appendChild(r));
  const t = ({ ctx: i }) => zo(o1, { ctx: i });
  t.displayName = "IndustrialWorkflowCanvas";
  const n = (i, s) => {
    const o = ({ block: u, ctx: c }) => zo(s, { block: u, ctx: c });
    return o.displayName = i, e.blocks.register(i, o);
  }, a = [
    e.blocks.register("industrial-workflow-canvas", t),
    n("industrial-workflow-list", u1),
    n("industrial-resource-console", d1),
    n("industrial-credential-console", f1),
    n("industrial-execution-center", h1),
    n("industrial-file-details", p1),
    n("industrial-machine-console", g1),
    n("industrial-collection-wizard", b1),
    n("industrial-file-profiles", I1),
    n("industrial-binding-console", C1)
  ];
  return () => {
    for (const i of a) i == null || i();
    r == null || r.remove();
  };
}
export {
  sg as g,
  S1 as r
};
