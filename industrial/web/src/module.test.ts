import { describe, expect, it } from "vitest";
import { getFacade } from "./facade";
import { register } from "./module";

describe("industrial console client module", () => {
  it("registers a block renderer through the console facade only", () => {
    const registrations: Array<{ kind: string; component: unknown }> = [];
    const dispose = () => undefined;
    const api = {
      blocks: {
        register: (kind: string, component: unknown) => {
          registrations.push({ kind, component });
          return dispose;
        },
      },
    };

    const unregister = register(api);
    expect(typeof unregister).toBe("function");
    unregister();
    expect(registrations.map((entry) => entry.kind)).toEqual([
      "industrial-workflow-canvas",
      "industrial-workflow-list",
      "industrial-resource-console",
      "industrial-credential-console",
      "industrial-execution-center",
      "industrial-file-details",
      "industrial-machine-console",
      "industrial-collection-wizard",
      "industrial-file-profiles",
      "industrial-binding-console",
    ]);
    expect(registrations.every((entry) => typeof entry.component === "function")).toBe(true);
    expect(getFacade()).toBe(api);
  });
});
