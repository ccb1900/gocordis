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

    expect(register(api)).toBe(dispose);
    expect(registrations).toEqual([
      { kind: "industrial-workflow-canvas", component: expect.any(Function) },
    ]);
    expect(getFacade()).toBe(api);
  });
});
