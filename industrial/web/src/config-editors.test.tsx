// @vitest-environment jsdom
import * as React from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("antd", async () => {
  const React = await import("react");
  const Input = Object.assign(
    (props: any) => React.createElement("input", {
      "aria-label": props["aria-label"],
      placeholder: props.placeholder,
      list: props.list,
      value: props.value ?? "",
      onChange: (event: any) => props.onChange?.({ target: { value: event.target.value } }),
    }),
    { TextArea: (props: any) => React.createElement("textarea", { value: props.value ?? "" }) },
  );
  return {
    Button: (props: any) => React.createElement("button", { onClick: props.onClick }, props.children),
    Input,
    Select: (props: any) => React.createElement("select", {
      "aria-label": props["aria-label"],
      value: props.value ?? "",
      onChange: (event: any) => props.onChange?.(event.target.value),
    }, (props.options ?? []).map((option: any) =>
      React.createElement("option", { key: option.value, value: option.value }, option.label))),
    Typography: { Text: ({ children }: any) => React.createElement("span", null, children) },
  };
});

import { KeyValueEditor } from "./KeyValueEditor";
import { StringListEditor } from "./StringListEditor";

afterEach(() => cleanup());

describe("dedicated mapping and validation editors", () => {
  it("builds field mappings as a record without JSON text", () => {
    const changes: Array<Record<string, string>> = [];
    render(React.createElement(KeyValueEditor, {
      value: {}, onChange: (next) => changes.push(next), testId: "field-mapping",
    }));
    fireEvent.click(screen.getByText("添加字段"));

    const keyInputs = screen.getAllByLabelText("目标字段 1");
    const valueInputs = screen.getAllByLabelText("来源字段 1");
    fireEvent.change(keyInputs[0], { target: { value: "value" } });
    fireEvent.change(valueInputs[0], { target: { value: "reading" } });

    expect(changes[changes.length - 1]).toEqual({ value: "reading" });
    expect(document.querySelector("textarea")).toBeNull();
  });

  it("collects validation field names as a string array", () => {
    const changes: Array<string[]> = [];
    render(React.createElement(StringListEditor, {
      value: [" record_id "], onChange: (next) => changes.push(next), testId: "field-required",
    }));
    fireEvent.click(screen.getByText("添加字段"));
    const inputs = screen.getAllByLabelText(/字段 2/);
    fireEvent.change(inputs[0], { target: { value: "value" } });
    expect(changes[changes.length - 1]).toEqual(["record_id", "value"]);
    expect(document.querySelector("textarea")).toBeNull();
  });
});
