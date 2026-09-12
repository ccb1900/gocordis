// Build-time shim (never import this from app code): esbuild aliases
// "react" here so a bundled plugin binds to the CONSOLE's React instance —
// a page must have exactly one React or hooks break.
const R = globalThis.__CORDIS_CONSOLE.React;

export default R;
export const {
  Children, Component, Fragment, PureComponent, StrictMode, Suspense,
  createContext, createElement, cloneElement, createRef, forwardRef,
  isValidElement, lazy, memo, startTransition, useCallback, useContext,
  useDebugValue, useDeferredValue, useEffect, useId, useImperativeHandle,
  useInsertionEffect, useLayoutEffect, useMemo, useOptimistic, useReducer,
  useRef, useState, useSyncExternalStore, useTransition,
} = R;
