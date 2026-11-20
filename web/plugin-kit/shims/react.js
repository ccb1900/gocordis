// Build-time shim (never import this from app code): esbuild aliases
// "react" here so a bundled plugin binds to the CONSOLE's React instance —
// a page must have exactly one React or hooks break.
const F = globalThis.__CORDIS_CONSOLE;
if (!F) throw new Error("gocordis plugin: console facade missing — a bundled plugin must be loaded by a GoCordis console (webui/host), never standalone");
const R = F.React;

export default R;
// Note: useOptimistic (React 19) is deliberately NOT re-exported — the
// console pins React ^18.3.1, where the binding would be `undefined`, and a
// plugin calling it would crash. Re-add only with a facade version bump.
export const {
  Children, Component, Fragment, PureComponent, StrictMode, Suspense,
  createContext, createElement, cloneElement, createRef, forwardRef,
  isValidElement, lazy, memo, startTransition, useCallback, useContext,
  useDebugValue, useDeferredValue, useEffect, useId, useImperativeHandle,
  useInsertionEffect, useLayoutEffect, useMemo, useReducer,
  useRef, useState, useSyncExternalStore, useTransition,
} = R;
