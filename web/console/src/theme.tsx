import React, { createContext, useCallback, useContext, useEffect, useState } from "react";

// Dark (default) / light, persisted; custom classes follow via the
// data-theme variable overrides in styles.css.
const ThemeContext = createContext<{ isDark: boolean; toggle: () => void }>({
  isDark: true,
  toggle: () => undefined,
});

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [isDark, setIsDark] = useState<boolean>(() => localStorage.getItem("gc-theme") !== "light");
  useEffect(() => {
    document.documentElement.dataset.theme = isDark ? "dark" : "light";
    localStorage.setItem("gc-theme", isDark ? "dark" : "light");
  }, [isDark]);
  const toggle = useCallback(() => setIsDark((v) => !v), []);
  return <ThemeContext.Provider value={{ isDark, toggle }}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  return useContext(ThemeContext);
}
