// SSE reconnect must invalidate read models: events emitted while the socket
// is down are never delivered, so the first open is passive but every later
// open forces plugin/composition/all consumers to re-query.
import { afterEach, describe, expect, it, vi } from "vitest";
import { onStreamStatus } from "./stream";
import { ALL, COMPOSITION, PLUGIN, domainVersion, testFlush } from "./lib/projections";

type Listener = (e: Event) => void;

class FakeEventSource {
  static instances: FakeEventSource[] = [];
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSED = 2;

  readyState = FakeEventSource.CONNECTING;
  onopen: Listener | null = null;
  onerror: Listener | null = null;
  private readonly listeners = new Map<string, Set<Listener>>();

  constructor(readonly url: string) {
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, fn: Listener) {
    let set = this.listeners.get(type);
    if (!set) this.listeners.set(type, (set = new Set()));
    set.add(fn);
  }

  open() {
    this.readyState = FakeEventSource.OPEN;
    this.onopen?.(new Event("open"));
  }

  error() {
    this.readyState = FakeEventSource.CONNECTING;
    this.onerror?.(new Event("error"));
  }
}

afterEach(() => {
  vi.unstubAllGlobals();
  FakeEventSource.instances = [];
});

describe("SSE reconnect", () => {
  it("invalidates plugin/composition/all only after a dropped connection", () => {
    vi.stubGlobal("EventSource", FakeEventSource);
    const stop = onStreamStatus(() => {});
    const source = FakeEventSource.instances[0];
    expect(source.url).toBe("/api/stream");

    const before = {
      plugin: domainVersion(PLUGIN),
      composition: domainVersion(COMPOSITION),
      all: domainVersion(ALL),
    };

    source.open();
    testFlush();
    expect(domainVersion(PLUGIN)).toBe(before.plugin);
    expect(domainVersion(COMPOSITION)).toBe(before.composition);
    expect(domainVersion(ALL)).toBe(before.all);

    // Connection dropped, events were missed, then EventSource reconnects.
    source.error();
    source.open();
    testFlush();
    expect(domainVersion(PLUGIN)).toBe(before.plugin + 1);
    expect(domainVersion(COMPOSITION)).toBe(before.composition + 1);
    expect(domainVersion(ALL)).toBe(before.all + 1);

    stop();
  });
});
