import { getFacade } from "./facade";

export const createElement = (...args: any[]) => (getFacade().React.createElement as any)(...args);
export const Fragment = ((props: any) => createElement(getFacade().React.Fragment, props)) as any;
export const useCallback = (...args: any[]) => (getFacade().React.useCallback as any)(...args);
export const useEffect = (...args: any[]) => (getFacade().React.useEffect as any)(...args);
export const useMemo = (...args: any[]) => (getFacade().React.useMemo as any)(...args);
export const useRef = (...args: any[]) => (getFacade().React.useRef as any)(...args);
export const useState = (...args: any[]) => (getFacade().React.useState as any)(...args);
export const useContext = (...args: any[]) => (getFacade().React.useContext as any)(...args);
