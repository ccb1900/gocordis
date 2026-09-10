// Ambient host surfaces the console core may use. Wails desktop injects
// window.runtime; browsers use the SSE transport instead (see api.ts).
interface WailsRuntime {
  EventsOn(name: string, callback: (...args: unknown[]) => void): void;
  EventsOff(name: string): void;
}

interface Window {
  runtime?: WailsRuntime;
}
