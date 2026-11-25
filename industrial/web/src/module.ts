import { CordisDesigner } from "./CordisDesigner";
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
  const dispose = api.blocks.register("industrial-workflow-canvas", IndustrialWorkflowCanvas);
  return () => {
    dispose?.();
    style?.remove();
  };
}

export default register;
