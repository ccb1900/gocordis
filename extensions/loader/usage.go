package loader

import "sync"

// usageTracker is the default ModuleUsage implementation: ModuleID -> owner set.
// Every Acquire/Release has a single linearization point on the tracker mutex.
type usageTracker struct {
	mu     sync.Mutex
	owners map[string]map[string]struct{}
}

// NewModuleUsage returns an empty ModuleUsage tracker.
func NewModuleUsage() ModuleUsage {
	return &usageTracker{owners: make(map[string]map[string]struct{})}
}

// Acquire records that ownerID uses moduleID. A duplicate (moduleID, ownerID)
// fails with ErrModuleUseExists.
func (u *usageTracker) Acquire(moduleID, ownerID string) error {
	u.mu.Lock()
	defer u.mu.Unlock()
	set := u.owners[moduleID]
	if set == nil {
		set = make(map[string]struct{})
		u.owners[moduleID] = set
	}
	if _, dup := set[ownerID]; dup {
		return ErrModuleUseExists
	}
	set[ownerID] = struct{}{}
	return nil
}

// Release removes ownerID from moduleID. Releasing a non-existent ownership
// fails with ErrModuleUseNotFound.
func (u *usageTracker) Release(moduleID, ownerID string) error {
	u.mu.Lock()
	defer u.mu.Unlock()
	set := u.owners[moduleID]
	if set == nil {
		return ErrModuleUseNotFound
	}
	if _, ok := set[ownerID]; !ok {
		return ErrModuleUseNotFound
	}
	delete(set, ownerID)
	if len(set) == 0 {
		delete(u.owners, moduleID)
	}
	return nil
}

// InUse reports whether moduleID currently has at least one owner.
func (u *usageTracker) InUse(moduleID string) bool {
	u.mu.Lock()
	defer u.mu.Unlock()
	set := u.owners[moduleID]
	return len(set) > 0
}

var _ ModuleUsage = (*usageTracker)(nil)
