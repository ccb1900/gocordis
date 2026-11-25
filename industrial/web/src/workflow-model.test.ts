import { describe, expect, it } from "vitest";
import type { NodeDescriptor, WorkflowDefinition } from "./types";
import {
  addEdge, applyPositions, findCycle, makeNode, mergeAppliedConfig, nextNodeId, portTypesCompatible,
  removeNode, sameEdge, validateWorkflow,
} from "./workflow-model";

const descriptor = (
  id: string, inputs: Array<[string, string, boolean]> = [],
  outputs: Array<[string, string]> = [], required: string[] = [],
): NodeDescriptor => ({
  nodeTypeId: id, name: id, description: "", category: "General", icon: "", version: "1.0.0",
  inputs: inputs.map(([name, type, need]) => ({ name, type: type as NodeDescriptor["inputs"][number]["type"], required: need })),
  outputs: outputs.map(([name, type]) => ({ name, type: type as NodeDescriptor["outputs"][number]["type"], required: false })),
  configurationSchema: { type: "object", properties: { resourceId: { type: "string" } }, required },
  activityType: "", dependencies: [],
});

const catalog = new Map([
  ["source", descriptor("source", [], [["files", "FileBatch"]])],
  ["reader", descriptor("reader", [["input", "FileBatch", true]], [["records", "RecordSet"]], ["resourceId"])],
  ["sink", descriptor("sink", [["records", "RecordSet", true]], [])],
]);

const base = (): WorkflowDefinition => ({
  workflowId: "wf", name: "WF", version: 1, nodes: [], edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [], layout: {},
});

describe("port compatibility (§7.4)", () => {
  it("allows identical known types and rejects mismatches / Unknown", () => {
    expect(portTypesCompatible("FileBatch", "FileBatch")).toBe(true);
    expect(portTypesCompatible("FileBatch", "RecordSet")).toBe(false);
    expect(portTypesCompatible("Control", "File")).toBe(false);
    expect(portTypesCompatible("Unknown", "Unknown")).toBe(false);
  });
});

describe("pure workflow operations", () => {
  it("creates deterministic node ids and applies schema defaults", () => {
    const first = makeNode(catalog.get("reader")!, "n1");
    expect(first.id).toBe("n1");
    expect(first.config).toEqual({});
    expect(nextNodeId([first])).toBe("n2");
    expect(nextNodeId([first, makeNode(catalog.get("sink")!, "n2")])).toBe("n3");
  });

  it("schema applies preserve unknown config and explicit falsy values", () => {
    const current = { resourceId: "share", targetResourceId: "target", enabled: true };
    const merged = mergeAppliedConfig(current, { resourceId: "new", enabled: false, count: 0, note: "" });
    expect(current).toEqual({ resourceId: "share", targetResourceId: "target", enabled: true });
    expect(merged).toEqual({
      resourceId: "new", targetResourceId: "target", enabled: false, count: 0, note: "",
    });
    expect(mergeAppliedConfig(current, { note: undefined })).toEqual({
      resourceId: "share", targetResourceId: "target", enabled: true,
    });
  });

  it("adds only type-compatible edges, idempotently", () => {
    let def = base();
    def = { ...def, nodes: [
      makeNode(catalog.get("source")!, "src", 10, 20),
      makeNode(catalog.get("reader")!, "rd", 200, 20),
      makeNode(catalog.get("sink")!, "sk", 400, 20),
    ] };
    const good = { source: "src", sourcePort: "files", target: "rd", targetPort: "input" };
    const first = addEdge(def, good, catalog);
    expect(first.issue).toBeUndefined();
    expect(first.definition.edges).toEqual([good]);
    const second = addEdge(first.definition, good, catalog);
    expect(second.definition.edges).toHaveLength(1); // idempotent

    const bad = addEdge(def, { source: "rd", sourcePort: "records", target: "src", targetPort: "files" }, catalog);
    expect(bad.definition).toBe(def);
    expect(bad.issue?.code).toBe("PORT_MISSING");
    const typeMismatch = addEdge(def, { source: "rd", sourcePort: "records", target: "rd", targetPort: "input" }, catalog);
    expect(typeMismatch.issue?.code).toBe("PORT_TYPE_INCOMPATIBLE");
  });

  it("detects cycles and drops attached edges when a node is removed", () => {
    // Cycle detection is structural and independent of port validity.
    const def: WorkflowDefinition = {
      ...base(),
      nodes: [makeNode(catalog.get("source")!, "a"), makeNode(catalog.get("source")!, "b")],
      edges: [
        { source: "a", sourcePort: "files", target: "b", targetPort: "out" },
        { source: "b", sourcePort: "files", target: "a", targetPort: "out" },
      ],
    };
    expect(findCycle(def)).toEqual(["a", "b", "a"]);
    expect(removeNode(def, "a").edges).toEqual([]);
  });

  it("applies only known positions and keeps the rest of the definition intact", () => {
    const def: WorkflowDefinition = { ...base(), nodes: [makeNode(catalog.get("source")!, "a")] };
    const next = applyPositions(def, { a: { x: 33, y: -7 }, unknown: { x: 1, y: 2 } });
    expect(next.nodes[0]).toMatchObject({ x: 33, y: -7 });
    expect(def.nodes[0]).toMatchObject({ x: 0, y: 0 }); // immutable
    expect(sameEdge(
      { source: "a", sourcePort: "p", target: "b", targetPort: "q" },
      { source: "a", sourcePort: "p", target: "b", targetPort: "q" },
    )).toBe(true);
  });
});

describe("located validation (§7.7)", () => {
  it("reports node, config, port and cycle errors at their own paths", () => {
    const def: WorkflowDefinition = {
      ...base(),
      nodes: [
        makeNode(catalog.get("reader")!, "rd"), // required config + required input missing
        makeNode(catalog.get("source")!, "ghost", 0, 0),
      ],
      edges: [{ source: "missing", sourcePort: "files", target: "rd", targetPort: "input" }],
    };
    const errors = validateWorkflow(def, catalog);
    expect(errors.map((error) => error.code)).toContain("REQUIRED_CONFIG_MISSING");
    expect(errors.map((error) => error.code)).toContain("ENDPOINT_MISSING");
    expect(errors.map((error) => error.code)).toContain("MISSING_INPUT");
    expect(errors.find((error) => error.code === "REQUIRED_CONFIG_MISSING")?.path)
      .toBe("nodes.rd.config.resourceId");

    const withResource = validateWorkflow({
      ...def, nodes: def.nodes.map((node) => node.id === "rd"
        ? { ...node, config: { resourceId: "r-1" } } : node),
    }, catalog, new Set(["other"]));
    expect(withResource.map((error) => error.code)).toContain("RESOURCE_MISSING");
  });

  it("accepts a well-formed workflow with zero errors", () => {
    const def: WorkflowDefinition = {
      ...base(),
      nodes: [
        { ...makeNode(catalog.get("source")!, "src"), x: 0, y: 0 },
        { ...makeNode(catalog.get("reader")!, "rd"), config: { resourceId: "r-1" } },
        makeNode(catalog.get("sink")!, "sk"),
      ],
      edges: [
        { source: "src", sourcePort: "files", target: "rd", targetPort: "input" },
        { source: "rd", sourcePort: "records", target: "sk", targetPort: "records" },
      ],
    };
    expect(validateWorkflow(def, catalog, new Set(["r-1"]))).toEqual([]);
  });
});
