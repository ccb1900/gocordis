import { describe, expect, it } from "vitest";
import {
  canonicalPortType, parseCollections, parseCommandResult, parseExecution, parseNodeDescriptors,
  parseResourceOptions, parseValidationIssues, parseWorkflow, parseWorkflowSummaries, unwrapData,
} from "./dto";

describe("hub envelope parsing", () => {
  it("parses bounded node execution diagnostics from the same execution payload", () => {
    const state = parseExecution({ data: {
      status: "Finished",
      error: null,
      executed: ["src", "read"],
      instanceId: "instance-1",
      definitionId: "flow:v1",
      nodes: [
        { nodeId: "read", status: "Succeeded", records: 6, files: 0, error: null,
          samples: ["1", "2", "3", "4"] },
        { nodeId: "storeB", status: "Failed", records: 1, files: 0,
          error: "connection reset", samples: [] },
      ],
    } });
    expect(state).not.toBeNull();
    expect(state!.nodes).toHaveLength(2);
    expect(state!.nodes[0]).toMatchObject({ records: 6, status: "Succeeded" });
    expect(state!.nodes[0].samples).toEqual(["1", "2", "3"]);
    expect(state!.nodes[1].error).toBe("connection reset");
    expect(parseExecution(null)).toBeNull();
  });

  it("unwraps {data} and rejects an error envelope", () => {
    expect(unwrapData({ data: { ok: true } })).toEqual({ ok: true });
    expect(() => unwrapData({ code: "not_found", message: "unknown query" })).toThrow(/not_found/);
    // Bare payloads are accepted so pure parsers are node-testable.
    expect(unwrapData([1, 2])).toEqual([1, 2]);
  });

  it("normalizes the live industrial/nodes identity trio and richer descriptors", () => {
    const nodes = parseNodeDescriptors({ data: [
      { nodeTypeId: "industrial.reader.csv", name: "CSV Reader", category: "Parsing", version: "1.0.0" },
      {
        nodeTypeId: "industrial.sink.sqlite", title: "SQLite", group: "Delivery.sink",
        inputPorts: [
          { name: "records", type: "recordset", required: true },
          { name: "records", type: "RecordSet", required: true },
        ],
        outputPorts: [{ name: "result", portType: "DeliveryResult" }],
        schema: { type: "object", properties: { resourceId: { type: "string" } }, required: ["resourceId"] },
        activity_type: "Industrial.Activities.Sink.Sqlite",
      },
    ] });
    expect(nodes).toHaveLength(2);
    expect(nodes[0]).toMatchObject({ nodeTypeId: "industrial.reader.csv", name: "CSV Reader", version: "1.0.0" });
    expect(nodes[0].inputs).toEqual([]); // identity trio stays honest, no invented ports
    expect(nodes[1]).toMatchObject({ name: "SQLite", category: "Delivery.sink", activityType: "Industrial.Activities.Sink.Sqlite" });
    expect(nodes[1].inputs).toEqual([{ name: "records", type: "RecordSet", required: true }]);
    expect(nodes[1].outputs[0].type).toBe("DeliveryResult");
    expect(nodes[1].configurationSchema.required).toEqual(["resourceId"]);
  });

  it("preserves schema hints supplied by a node plugin", () => {
    const [node] = parseNodeDescriptors({ data: [
      {
        nodeTypeId: "industrial.transform.validate",
        schema: {
          type: "object", resourceType: "filesystem.local",
          properties: {
            required: { type: "array", items: { type: "string" }, default: [] },
            types: { type: "object", additionalProperties: { type: "string" } },
            resourceId: { type: "string", format: "resource" },
          },
        },
      },
    ] });
    expect(node.configurationSchema.resourceType).toBe("filesystem.local");
    expect(node.configurationSchema.properties!.required.items).toEqual({ type: "string" });
    expect(node.configurationSchema.properties!.types.additionalProperties).toEqual({ type: "string" });
    expect(node.configurationSchema.properties!.resourceId.format).toBe("resource");
  });

  it("parses resource options with their linked credential for pickers", () => {
    expect(parseResourceOptions({ data: [
      { id: "share", name: "Share", type: "filesystem.local", enabled: true, credentialId: "cred-1", credentialName: "Account" },
      { id: "off", name: "Off", type: "filesystem.local", enabled: false },
      { name: "no id" },
      { id: "share", name: "duplicate" },
    ] })).toEqual([
      {
        id: "share", name: "Share", type: "filesystem.local", enabled: true,
        credentialId: "cred-1", credentialName: "Account",
      },
      { id: "off", name: "Off", type: "filesystem.local", enabled: false, credentialId: null, credentialName: null },
    ]);
  });

  it("canonicalizes port types without silently treating unknown types as compatible", () => {
    expect(canonicalPortType("filebatch")).toBe("FileBatch");
    expect(canonicalPortType("RECORDSET")).toBe("RecordSet");
    expect(canonicalPortType("something-new")).toBe("Unknown");
  });

  it("parses workflow summaries from industrial/workflows", () => {
    const rows = parseWorkflowSummaries({ data: [
      { workflowId: "daily-collection", name: "Daily", version: 3 },
      { workflow_id: "hourly", version: "2" },
      { name: "no id" },
    ] });
    expect(rows).toEqual([
      { workflowId: "daily-collection", name: "Daily", version: 3 },
      { workflowId: "hourly", name: "hourly", version: 2 },
    ]);
  });

  it("parses a full workflow definition with nested positions and snake_case ports", () => {
    const def = parseWorkflow({ data: {
      workflowId: "daily-collection", name: "Daily", version: 2,
      nodes: [
        { id: "source", type: "industrial.filesystem.local", config: { resourceId: "share" }, position: { x: 40, y: 80 } },
        { id: "reader", nodeType: "industrial.reader.csv", configuration: { encoding: "utf-8" }, x: 240 },
      ],
      edges: [{ from: "source", to: "reader", source_port: "files", target_port: "input" }],
      layout: { zoom: 1 },
    } });
    expect(def.workflowId).toBe("daily-collection");
    expect(def.nodes[0]).toMatchObject({ id: "source", type: "industrial.filesystem.local", x: 40, y: 80 });
    expect(def.nodes[1]).toMatchObject({ id: "reader", type: "industrial.reader.csv", x: 240, y: 0 });
    expect(def.edges[0]).toEqual({ source: "source", sourcePort: "files", target: "reader", targetPort: "input" });
    expect(def.layout).toEqual({ zoom: 1 });
  });

  it("parses collection rows and located validation issues", () => {
    expect(parseCollections({ data: [{ id: "f1", relativePath: "a.csv", sourceDate: "2026-01-01", status: "Stored" }] }))
      .toEqual([{ id: "f1", path: "a.csv", sourceDate: "2026-01-01", status: "Stored" }]);
    const issues = parseValidationIssues({ data: { errors: [
      { path: "nodes.source.config.resourceId", code: "RESOURCE_MISSING", message: "gone" },
      "generic failure",
    ] } });
    expect(issues).toEqual([
      { path: "nodes.source.config.resourceId", code: "RESOURCE_MISSING", message: "gone" },
      { path: "workflow", code: "VALIDATION", message: "generic failure" },
    ]);
  });

  it("treats {accepted:false} inside {data} as an unaccepted command", () => {
    expect(parseCommandResult({ data: null }).accepted).toBe(true);
    expect(parseCommandResult({ data: { accepted: false, code: "UNKNOWN_WORKFLOW", error: "no such id" } }))
      .toEqual({ accepted: false, code: "UNKNOWN_WORKFLOW", error: "no such id" });
    expect(parseCommandResult({ data: { ok: true } }).accepted).toBe(true);
  });
});
