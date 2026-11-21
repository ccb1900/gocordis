/// <reference types="vite/client" />
// Logic-level regression guard for the Rules of Hooks: vitest runs in node
// env (no jsdom), so instead of rendering we assert the source shape — the
// conditionally-returning FieldControl must not own hook calls; the always
// mounted JsonControl does.
import { describe, expect, it } from "vitest";

const modules = import.meta.glob("./SchemaForm.tsx", { query: "?raw", import: "default", eager: true });
const source = modules["./SchemaForm.tsx"] as string;

function bodyOf(name: string): string {
  const start = source.indexOf(`function ${name}`);
  if (start < 0) return "";
  const rest = source.slice(start);
  const end = rest.indexOf("\n}\n"); // the function's closing brace at column 0
  return end < 0 ? rest : rest.slice(0, end);
}

describe("FieldControl hook discipline", () => {
  it("delegates JSON editing to a dedicated hook-owning component", () => {
    const field = bodyOf("FieldControl");
    const json = bodyOf("JsonControl");
    expect(field).toContain("JsonControl");
    expect(field).not.toContain("useState");
    expect(field).not.toContain("useEffect");
    expect(json).toContain("useState");
    expect(json).toContain("useEffect");
  });

  it("still branches on every FieldSpec type", () => {
    const field = bodyOf("FieldControl");
    for (const type of ["bool", "number", "select", "json"]) {
      expect(field).toContain(`spec.type === "${type}"`);
    }
  });
});
