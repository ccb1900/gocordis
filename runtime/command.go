package runtime

// command is a serialized lifecycle decision request. All lifecycle-changing
// operations (Load, Dispose, Replace, ApplyFinished, UnwindFinished,
// DependencyChanged, Close) are expressed as commands and linearized by the
// orchestrator's single command queue.
type command interface {
	apply(*orchestrator)
}

// cmdLoad registers a freshly created Fiber (intent is already Mounted).
type cmdLoad struct {
	fiber *Fiber
}

// cmdLoadIntent expresses Intent = Mounted for an existing Fiber.
type cmdLoadIntent struct {
	fiber *Fiber
}

// cmdDispose expresses Intent = Unmounted for an existing Fiber.
type cmdDispose struct {
	fiber *Fiber
}

// cmdApplyDone reports that an activation's Apply finished.
type cmdApplyDone struct {
	fiberID      FiberID
	activationID ActivationID
	cleanup      Cleanup
	err          error
}

// cmdUnwindDone reports that an activation's unwind finished.
type cmdUnwindDone struct {
	fiberID      FiberID
	activationID ActivationID
	err          error
}

// cmdSpawnChild requests creation of an owned child Fiber. The declarations
// are cached by the caller so the orchestrator never runs Component code.
type cmdSpawnChild struct {
	ctx       *Context
	component Component
	inject    []Dependency
	provide   []Capability
	newScope  bool
	// keyRealms assigns per-key isolation realms for the new fiber (nil when
	// the child has no isolated key; WithScope-derived entry is pre-built).
	keyRealms map[CapabilityKey]*realm
	reply     chan spawnChildReply
}

type spawnChildReply struct {
	fiber *Fiber
	err   error
}

// cmdClose requests Runtime shutdown.
type cmdClose struct {
	ack chan error
}
