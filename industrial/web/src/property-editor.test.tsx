// @vitest-environment jsdom
import * as React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

const { formItems, selects } = vi.hoisted(() => ({
  formItems: [] as Array<Record<string, any>>,
  selects: [] as Array<Record<string, any>>,
}));

vi.mock("antd", async () => {
  const React = await import("react");
  const Box = ({ children }: { children?: React.ReactNode }) => React.createElement("div", null, children);
  const Text = ({ children }: { children?: React.ReactNode }) => React.createElement("span", null, children);
  return {
  Button: ({ children, onClick }: any) => React.createElement("button", { onClick }, children),
  Divider: Box,
  Space: Box,
  Empty: Box,
  Form: Object.assign(Box, {
    Item: (props: any) => {
      formItems.push(props);
      const value = props.name === "resourceId" ? "share" : undefined;
      return React.createElement("div", null,
        React.isValidElement(props.children)
          ? React.cloneElement(props.children as React.ReactElement<any>, { value })
          : props.children);
    },
    useForm: () => [{ setFieldsValue: () => undefined, validateFields: async () => ({ resourceId: "new", enabled: false }) }],
  }),
  Input: Object.assign(Box, { TextArea: Box }),
  InputNumber: Box,
  Select: (props: any) => {
    selects.push(props);
    return React.createElement("div");
  },
  Switch: Box,
  Table: Box,
  Tag: Box,
  Typography: Object.assign(Box, { Title: Box, Text, Paragraph: Box }),
  };
});

import { PropertyEditor } from "./PropertyEditor";
import type { NodeDescriptor } from "./types";

const descriptor: NodeDescriptor = {
  nodeTypeId: "probe", name: "Probe", description: "", category: "probe", icon: "", version: "1.0.0",
  inputs: [], outputs: [],
  configurationSchema: {
    type: "object", resourceType: "filesystem.local", required: ["resourceId"],
    properties: {
      resourceId: { type: "string", format: "resource", title: "Source" },
      enabled: { type: "boolean", title: "Enabled" },
      mapping: { type: "object", title: "Mapping" },
    },
  },
  activityType: "Industrial.Activities.Probe", dependencies: [],
};

describe("schema-driven property form", () => {
  it("binds booleans as checked, filters resource pickers and preserves unknown config", async () => {
    formItems.length = 0;
    selects.length = 0;
    const applied: Array<Record<string, unknown>> = [];
    render(React.createElement(PropertyEditor, {
      node: { id: "n1", type: "probe", config: { targetResourceId: "target" }, x: 0, y: 0 },
      descriptor, nodes: [{ id: "n1", type: "probe", config: {}, x: 0, y: 0 }],
      edges: [], catalog: new Map(), resources: [
        { id: "share", name: "Share", type: "filesystem.local", enabled: true, credentialId: "cred-1", credentialName: "Account" },
        { id: "other", name: "Other", type: "database", enabled: true },
        { id: "off", name: "Disabled", type: "filesystem.local", enabled: false },
      ],
      issues: [],
      onApplyConfig: (config) => applied.push(config),
      onDeleteNode: () => undefined,
      onDeleteEdge: () => undefined,
      onCreateEdge: () => undefined,
      onUpdateEdge: () => undefined,
      onSelectNode: () => undefined,
    }));

    expect(formItems.find((item) => item.name === "enabled")?.valuePropName).toBe("checked");
    const picker = screen.getByLabelText("Source") as HTMLSelectElement;
    expect(Array.from(picker.options).map((option) => option.value)).toEqual(["", "share"]);
    expect(screen.getByText(/凭证 Account/)).toBeTruthy();

    fireEvent.click(screen.getByText("应用配置"));
    await waitFor(() => expect(applied).toEqual([
      { targetResourceId: "target", resourceId: "new", enabled: false },
    ]));
  });
});
