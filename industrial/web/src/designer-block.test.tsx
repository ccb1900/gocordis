// @vitest-environment jsdom
import * as React from "react";
import * as jsxRuntime from "react/jsx-runtime";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

// G6 needs a real canvas; this test proves the Cordis integration boundary,
// not the canvas. GraphCanvas itself has its own rendering tests.
vi.mock("./GraphCanvas", () => ({
  GraphCanvas: (props: any) => React.createElement("button", {
    "data-testid": "drop-node",
    onClick: () => props.onDropNode("industrial.reader.json", 10, 10),
  }),
}));

import { register } from "./module";

// antd uses ResizeObserver in jsdom; the console supplies it in a browser.
(globalThis as any).ResizeObserver = class {
  observe() { /* no-op */ }
  unobserve() { /* no-op */ }
  disconnect() { /* no-op */ }
};

const Box = ({ children }: { children?: React.ReactNode }) =>
  React.createElement("div", null, children);
const Text = ({ children }: { children?: React.ReactNode }) =>
  React.createElement("span", null, children);
const antd = {
  Alert: Box, Button: Box, Divider: Box, Drawer: Box, Empty: Box, Spin: Box, Switch: Box, Table: Box,
  Tag: Box, Space: Box, Select: Box,
  Layout: Object.assign(Box, { Header: Box, Content: Box }),
  Typography: Object.assign(Box, { Title: Box, Text }),
  Form: Object.assign(Box, {
    Item: Box,
    useForm: () => [{ setFieldsValue: () => undefined, validateFields: async () => ({}) }],
  }),
  Input: Object.assign(Box, { TextArea: Box, Number: Box }),
  InputNumber: Box,
  message: {
    useMessage: () => [{ success: () => undefined, error: () => undefined, warning: () => undefined }, null],
  },
};

describe("industrial-workflow-canvas inside the console shell", () => {
  it("renders palette nodes from the hub query without an iframe or a second app shell", async () => {
    let canvas: any;
    const descriptors = [{
      nodeTypeId: "industrial.reader.json", name: "JSON Reader", category: "reader.Parsing",
      version: "1.0.0", inputs: [], outputs: [], configurationSchema: { properties: {} },
      activityType: "Industrial.Activities.Reader.Json", dependencies: [],
    }];
    const api = {
      React, jsxRuntime, antd,
      api: {
        hubQuery: async (name: string) => name === "industrial/nodes"
          ? descriptors
          : name === "industrial/workflows" ? [] : [{ id: "source-1" }],
        hubCommand: async () => undefined,
      },
      blocks: { register: (_kind: string, component: unknown) => { canvas = component; return () => undefined; } },
    };

    register(api);
    expect(canvas).toBeTypeOf("function");
    const ctx = {
      hubQuery: api.api.hubQuery,
      hubCommand: api.api.hubCommand,
      focus: null, onFocus: () => undefined, busy: false, generation: 0,
    };
    const { container } = render(React.createElement(canvas, { ctx }));
    expect(container.querySelector("iframe")).toBeNull();
    await waitFor(() => expect(screen.getByText("JSON Reader")).toBeTruthy());
    expect(screen.getByText("Industrial Workflow")).toBeTruthy();

    // Drop a palette node onto the canvas; the property panel must bind to it.
    fireEvent.click(screen.getByTestId("drop-node"));
    await waitFor(() => expect(screen.getByText("n1")).toBeTruthy());
  });
});
