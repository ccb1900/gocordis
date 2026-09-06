package runtime

import "fmt"

// FiberState is the lifecycle state of a Fiber.
//
// State transitions are owned exclusively by the Runtime lifecycle
// coordinator. Component code MUST NOT mutate Fiber state directly.
type FiberState uint8

const (
	// StatePending means the Fiber exists but its activation conditions
	// are not (yet) satisfied.
	StatePending FiberState = iota
	// StateLoading means Apply is currently executing.
	StateLoading
	// StateActive means the current activation completed successfully and
	// all mandatory dependencies remain satisfied.
	StateActive
	// StateUnloading means the current activation is being unwound.
	StateUnloading
	// StateFailed means the current activation could not become Active due
	// to an Apply failure (component or Runtime-visible lifecycle failure).
	StateFailed
	// StateGone means the current activation has fully exited.
	StateGone
)

func (s FiberState) String() string {
	switch s {
	case StatePending:
		return "Pending"
	case StateLoading:
		return "Loading"
	case StateActive:
		return "Active"
	case StateUnloading:
		return "Unloading"
	case StateFailed:
		return "Failed"
	case StateGone:
		return "Gone"
	default:
		return fmt.Sprintf("FiberState(%d)", uint8(s))
	}
}

// Intent is the desired lifecycle intent for a Fiber.
//
// Intent expresses a request ("mount" / "unmount"); it is distinct from the
// current State. Intent mutations are idempotent.
type Intent uint8

const (
	// IntentMounted requests that the Fiber be (kept) active.
	IntentMounted Intent = iota
	// IntentUnmounted requests that the Fiber be withdrawn.
	IntentUnmounted
)

func (i Intent) String() string {
	switch i {
	case IntentMounted:
		return "Mounted"
	case IntentUnmounted:
		return "Unmounted"
	default:
		return fmt.Sprintf("Intent(%d)", uint8(i))
	}
}

// RuntimeState is the lifecycle state of the whole Runtime.
type RuntimeState uint8

const (
	// RuntimeRunning accepts lifecycle operations.
	RuntimeRunning RuntimeState = iota
	// RuntimeClosing rejects new Loads and is draining existing Fibers.
	RuntimeClosing
	// RuntimeClosed means the orchestrator has fully drained and stopped.
	RuntimeClosed
)

func (s RuntimeState) String() string {
	switch s {
	case RuntimeRunning:
		return "Running"
	case RuntimeClosing:
		return "Closing"
	case RuntimeClosed:
		return "Closed"
	default:
		return fmt.Sprintf("RuntimeState(%d)", uint8(s))
	}
}
