import { CordisDesigner } from "./CordisDesigner";
import { WorkflowList } from "./WorkflowList";
import { ResourceConsole } from "./ResourceConsole";
import { CredentialConsole } from "./CredentialConsole";
import { ExecutionCenter } from "./ExecutionCenter";
import { setFacade } from "./facade";
import { createElement } from "./facade-react";
import { DESIGNER_CSS } from "./styles";

/** Cordis client-module contract: the console imports this module and calls
 * register(facade). Registering a BLOCK renderer keeps the page itself a
 * backend-owned declarative contribution, with no second UI shell. */
export function register(api: any) {
  setFacade(api);
  const style = typeof document === "undefined" ? null : document.createElement("style");
  if (style) {
    style.dataset.industrialDesigner = "true";
    style.textContent = DESIGNER_CSS;
    document.head.appendChild(style);
  }
  const IndustrialWorkflowCanvas = ({ ctx }: { ctx: unknown }) =>
    createElement(CordisDesigner, { ctx });
  IndustrialWorkflowCanvas.displayName = "IndustrialWorkflowCanvas";
  const wrap = (name: string, Component: any) => {
    const Renderer = ({ block, ctx }: { block: unknown; ctx: unknown }) =>
      createElement(Component, { block, ctx });
    Renderer.displayName = name;
    return api.blocks.register(name, Renderer);
  };
  const disposers = [
    api.blocks.register("industrial-workflow-canvas", IndustrialWorkflowCanvas),
    wrap("industrial-workflow-list", WorkflowList),
    wrap("industrial-resource-console", ResourceConsole),
    wrap("industrial-credential-console", CredentialConsole),
    wrap("industrial-execution-center", ExecutionCenter),
  ];
  return () => {
    for (const dispose of disposers) dispose?.();
    style?.remove();
  };
}

export default register;
