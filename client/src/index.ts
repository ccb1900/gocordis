// @gocordis/console-client — reusable console shell for gocordis applications.
//
// Applications assemble the shell with their own renderers and domain state.
// Everything exported here is application-agnostic: the composition-aware
// sidebar, the observation stream and boundary status, the plugin inventory
// console, shared presentation primitives and the design system.

export * from "./api";
export * as Icons from "./components/Icons";
export { useComposition } from "./hooks/useComposition";
export { Sidebar } from "./components/Sidebar";
export {
  Chip,
  EmptyState,
  ErrorNote,
  EventFeed,
  LoadingState,
  MetadataTable,
  Progress,
  StatusChip,
} from "./components/Lists";
