// Client module of the protected builtin control-plane UI plugin. It owns
// only the control surface renderer and event-feed panel; the control-plane
// SERVICE is a separate protected Fiber capability.
import React from "react";
import { bindFacade, type BuiltinFacade } from "./facade";
import type { PageRenderer, PanelRenderer } from "./types";
import { PluginExplorer } from "./components/Explorer";
import { EventFeed } from "./components/EventFeed";

const ExplorerPage: PageRenderer = ({ ctx }) => <PluginExplorer ctx={ctx} />;
const EventFeedPanel: PanelRenderer = ({ generation }) => <EventFeed generation={generation} />;

export default function register(api: BuiltinFacade) {
  bindFacade(api);
  const disposers: Array<() => void> = [];
  disposers.push(api.pages.register({ name: "plugin-explorer", component: ExplorerPage }) as never);
  disposers.push(api.pages.register({ name: "plugin-control-plane", component: ExplorerPage }) as never);
  disposers.push(api.panels.register({ name: "event-feed", component: EventFeedPanel }) as never);
  return () => { for (const dispose of disposers.reverse()) dispose(); };
}
