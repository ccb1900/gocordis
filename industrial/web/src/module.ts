import { CordisDesigner } from "./CordisDesigner";
import { setFacade } from "./facade";
import { createElement } from "./facade-react";

/** Cordis client-module contract: the console imports this module and calls
 * register(facade). Registering a BLOCK renderer keeps the page itself a
 * backend-owned declarative contribution, with no second UI shell. */
export function register(api: any) {
  setFacade(api);
  // The shell owns page layout; a block only receives the ViewContext.
  const IndustrialWorkflowCanvas = ({ ctx }: { ctx: unknown }) =>
    createElement(CordisDesigner, { ctx });
  IndustrialWorkflowCanvas.displayName = "IndustrialWorkflowCanvas";
  return api.blocks.register("industrial-workflow-canvas", IndustrialWorkflowCanvas);
}

export default register;
