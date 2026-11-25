// Client modules cannot rely on the host loading a CSS asset. The designer
// injects its own scoped styles once and removes them with the module.
export const DESIGNER_CSS = `
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
