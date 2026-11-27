import { getFacade } from "./facade";
import { createElement } from "./facade-react";

const component = (name: string) => {
  const Wrapper = (props: any) => {
    const value = name.split(".").reduce<any>((part, key) => part?.[key], getFacade().antd);
    if (!value) throw new Error(`[industrial-designer] antd.${name} is unavailable`);
    return createElement(value, props ?? {});
  };
  Wrapper.displayName = `facade.${name}`;
  return Wrapper as any;
};

export const Alert = component("Alert");
export const Button = component("Button");
export const Divider = component("Divider");
export const Drawer = component("Drawer");
export const Empty = component("Empty");
export const Form = Object.assign(component("Form"), {
  Item: component("Form.Item"),
  useForm: (...args: any[]) => (getFacade().antd.Form.useForm as any)(...args),
}) as any;
export const Input = Object.assign(component("Input"), {
  Number: component("InputNumber"),
  TextArea: component("Input.TextArea"),
}) as any;
export const InputNumber = component("InputNumber");
export const Layout = Object.assign(component("Layout"), {
  Header: component("Layout.Header"),
  Content: component("Layout.Content"),
}) as any;
export const Modal = component("Modal");
export const Select = component("Select");
export const Space = component("Space");
export const Spin = component("Spin");
export const Switch = component("Switch");
export const Table = component("Table");
export const Tag = component("Tag");
export const Typography = Object.assign(component("Typography"), {
  Title: component("Typography.Title"),
  Text: component("Typography.Text"),
  Paragraph: component("Typography.Paragraph"),
}) as any;
export const message = new Proxy({}, {
  get: (_, key: string) => (...args: any[]) => {
    const target = (getFacade().antd as any).message?.[key];
    if (typeof target !== "function") throw new Error(`[industrial-designer] message.${key} unavailable`);
    return target(...args);
  },
}) as any;
