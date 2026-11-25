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
    expect(registrations).toEqual([
      { kind: "industrial-workflow-canvas", component: expect.any(Function) },
    ]);
    expect(getFacade()).toBe(api);
  });
});
