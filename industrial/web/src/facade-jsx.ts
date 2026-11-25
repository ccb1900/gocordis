import { getFacade } from "./facade";

export const jsx = (...args: any[]) => (getFacade().jsxRuntime.jsx as any)(...args);
export const jsxs = (...args: any[]) => (getFacade().jsxRuntime.jsxs as any)(...args);
export const jsxDEV = (...args: any[]) =>
  ((getFacade().jsxRuntime as any).jsxDEV ?? getFacade().jsxRuntime.jsx)(...args);
export const Fragment = ((props: any) =>
  (getFacade().jsxRuntime.jsx as any)(getFacade().jsxRuntime.Fragment, props)) as any;
