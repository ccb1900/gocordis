// Cordis client-module entry. The console dynamic-imports this module and
// calls register(facade). The facade's React instance is used so there is one
// React per page; the designer itself runs in a same-origin iframe document.
export function register(api) {
  const facade = api ?? globalThis.__CORDIS_CONSOLE;
  if (!facade?.React?.createElement) {
    console.warn("[industrial-designer] console facade has no React; renderer skipped");
    return () => {};
  }

  const React = facade.React;
  function IndustrialDesignerPage() {
    return React.createElement("iframe", {
      src: "/client-modules/industrial-designer/index.html",
      title: "Industrial workflow designer",
      style: { width: "100%", height: "calc(100vh - 140px)", border: "0", background: "#fff" },
    });
  }

  return facade.pages.register({
    name: "industrial-designer",
    component: IndustrialDesignerPage,
  });
}

export default register;
