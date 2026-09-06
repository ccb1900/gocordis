package registry_test

import (
	"errors"
	"sync"
	"testing"

	"dynamic-runtime/extensions/registry"
)

// R3 — Duplicate member: second Add fails, registry keeps the original.
func TestR3DuplicateMemberRejected(t *testing.T) {
	reg := registry.New[string]()
	if err := reg.Add("A", "v1"); err != nil {
		t.Fatalf("Add A: %v", err)
	}
	err := reg.Add("A", "v2")
	if !errors.Is(err, registry.ErrMemberExists) {
		t.Fatalf("second Add = %v, want ErrMemberExists", err)
	}
	if got, ok := reg.Get("A"); !ok || got != "v1" {
		t.Fatalf("Get(A) = %q,%v; want original v1", got, ok)
	}
	if reg.Len() != 1 {
		t.Fatalf("Len = %d, want 1", reg.Len())
	}
}

// R4 — Remove: member becomes absent; removing an absent member is an explicit
// error and never removes a different member.
func TestR4Remove(t *testing.T) {
	reg := registry.New[string]()
	_ = reg.Add("A", "v1")
	_ = reg.Add("B", "v2")

	if err := reg.Remove("A"); err != nil {
		t.Fatalf("Remove A: %v", err)
	}
	if reg.Has("A") {
		t.Fatal("A still present after Remove")
	}
	if got, ok := reg.Get("B"); !ok || got != "v2" {
		t.Fatalf("B must be untouched: %q,%v", got, ok)
	}

	err := reg.Remove("A")
	if !errors.Is(err, registry.ErrMemberNotFound) {
		t.Fatalf("Remove(missing) = %v, want ErrMemberNotFound", err)
	}
	if err := reg.Remove("B"); err != nil {
		t.Fatalf("Remove B: %v", err)
	}
	if reg.Len() != 0 {
		t.Fatalf("Len = %d, want 0", reg.Len())
	}
}

// R5 — Replace atomicity: old -> new with no observable absent state.
func TestR5ReplaceAtomicity(t *testing.T) {
	reg := registry.New[int]()
	if err := reg.Replace("A", 1); !errors.Is(err, registry.ErrMemberNotFound) {
		t.Fatalf("Replace on missing member = %v, want ErrMemberNotFound", err)
	}
	_ = reg.Add("A", 1)

	before := reg.Snapshot()
	if v, ok := before.Get("A"); !ok || v != 1 {
		t.Fatalf("before Replace snapshot = %v,%v; want 1", v, ok)
	}
	if err := reg.Replace("A", 2); err != nil {
		t.Fatalf("Replace A: %v", err)
	}
	after := reg.Snapshot()
	if v, ok := after.Get("A"); !ok || v != 2 {
		t.Fatalf("after Replace snapshot = %v,%v; want 2", v, ok)
	}
	if reg.Has("A") == false {
		t.Fatal("A must never be absent")
	}
}

// R5 (property) — concurrent Replace + Snapshot: every snapshot sees A with one
// of the written values, never absent.
func TestR5PropertyReplaceNeverAbsent(t *testing.T) {
	reg := registry.New[int]()
	_ = reg.Add("A", 0)

	stop := make(chan struct{})
	var wg sync.WaitGroup
	wg.Add(2)

	go func() { // replacer
		defer wg.Done()
		for i := 0; ; i++ {
			select {
			case <-stop:
				return
			default:
			}
			if err := reg.Replace("A", i); err != nil {
				t.Errorf("Replace: %v", err)
				return
			}
		}
	}()
	go func() { // snapshot observer
		defer wg.Done()
		for {
			select {
			case <-stop:
				return
			default:
			}
			snap := reg.Snapshot()
			if _, ok := snap.Get("A"); !ok {
				t.Error("snapshot observed A absent during Replace storm")
				return
			}
		}
	}()

	// Let both goroutines run briefly, then stop.
	for i := 0; i < 20000; i++ {
		if v, ok := reg.Get("A"); !ok {
			t.Fatalf("Get observed A absent")
			return
		} else {
			_ = v
		}
	}
	close(stop)
	wg.Wait()
}

// R6 — Snapshot immutability: later mutations never modify an earlier snapshot.
func TestR6SnapshotImmutability(t *testing.T) {
	reg := registry.New[string]()
	_ = reg.Add("pre", "p")
	s1 := reg.Snapshot()

	_ = reg.Add("A", "a")
	_ = reg.Replace("pre", "p2")
	_ = reg.Remove("pre")

	if s1.Has("A") {
		t.Fatal("snapshot S1 mutated by Add(A)")
	}
	if v, ok := s1.Get("pre"); !ok || v != "p" {
		t.Fatalf("snapshot S1 mutated by Replace/Remove: %q,%v", v, ok)
	}
	if s1.Len() != 1 {
		t.Fatalf("S1.Len = %d, want 1", s1.Len())
	}

	// Snapshot value semantics: independent copies.
	s2 := reg.Snapshot()
	if s2.Has("pre") {
		t.Fatal("S2 must not contain removed member")
	}
}

// R7 — Concurrent mutations settle to a state equivalent to a legal serial
// execution; membership stays consistent (at most one value per ID).
func TestR7ConcurrentMutationConsistency(t *testing.T) {
	reg := registry.New[int]()

	const workers = 8
	const idsPerWorker = 16
	var wg sync.WaitGroup
	for w := 0; w < workers; w++ {
		wg.Add(1)
		go func(w int) {
			defer wg.Done()
			base := w * idsPerWorker
			for i := 0; i < idsPerWorker; i++ {
				id := registry.MemberID(string(rune('a' + i))) // same IDs across workers -> contention
				_ = reg.Add(id, base+i)
				_ = reg.Replace(id, base+i+1000)
				_ = reg.Remove(id)
				_ = reg.Add(id, base+i+2000)
			}
		}(w)
	}
	wg.Wait()

	snap := reg.Snapshot()
	if snap.Len() != idsPerWorker {
		t.Fatalf("final Len = %d, want %d (each worker re-added its ids)", snap.Len(), idsPerWorker)
	}
	// At most one member per ID and no torn value: every present value must be
	// one that some worker wrote for that ID.
	snap.Range(func(id registry.MemberID, v int) bool {
		lo := int(id[0]-'a') % idsPerWorker
		valid := false
		for w := 0; w < workers; w++ {
			base := w * idsPerWorker
			if v == base+lo || v == base+lo+1000 || v == base+lo+2000 {
				valid = true
			}
		}
		if !valid {
			t.Errorf("id %q has torn/foreign value %d", id, v)
			return false
		}
		return true
	})
}

// R11 — Watch: ordered events for Add/Remove/Replace, initial snapshot at the
// subscription point, and idempotent unsubscribe cancellation.
func TestR11WatchOrderAndCancel(t *testing.T) {
	reg := registry.New[string]()

	sub, initial := reg.Subscribe()
	if initial.Len() != 0 {
		t.Fatalf("initial snapshot Len = %d, want 0", initial.Len())
	}

	_ = reg.Add("A", "a1")
	_ = reg.Add("B", "b1")
	_ = reg.Replace("A", "a2")
	_ = reg.Remove("B")

	// Drain events in order.
	type ev struct {
		kind registry.ChangeKind
		id   registry.MemberID
	}
	var got []ev
	expect := []ev{
		{registry.MemberAdded, "A"},
		{registry.MemberAdded, "B"},
		{registry.MemberReplaced, "A"},
		{registry.MemberRemoved, "B"},
	}
	for len(got) < len(expect) {
		select {
		case c := <-sub.Changes():
			got = append(got, ev{c.Kind, c.ID})
		default:
			t.Fatalf("missing events: got %v want %v", got, expect)
		}
	}
	for i := range expect {
		if got[i] != expect[i] {
			t.Fatalf("event %d = %+v, want %+v", i, got[i], expect[i])
		}
	}

	// Initial snapshot is a consistent prefix: at subscribe time the registry
	// was empty, so the stream contains exactly the mutations after that point.
	if got := reg.Snapshot().Len(); got != 1 { // only A remains
		t.Fatalf("registry Len = %d, want 1", got)
	}

	// Unsubscribe: idempotent, and later mutations must not be delivered. After
	// Unsubscribe the channel is closed, so the only receivable value is the
	// closed-channel zero (open == false) - never a real event.
	sub.Unsubscribe()
	sub.Unsubscribe()
	_ = reg.Add("C", "c1")
	select {
	case c, open := <-sub.Changes():
		if open {
			t.Fatalf("event after Unsubscribe delivered: %+v", c)
		}
	default:
		t.Fatal("Changes channel not closed after Unsubscribe")
	}
}

// R11 — subscription point snapshot: no gap between the returned snapshot and
// the first delivered event.
func TestR11SubscribeSnapshotConsistency(t *testing.T) {
	reg := registry.New[int]()
	_ = reg.Add("existing", 7)

	sub, snap := reg.Subscribe()
	if v, ok := snap.Get("existing"); !ok || v != 7 {
		t.Fatalf("initial snapshot missing existing member: %v,%v", v, ok)
	}
	_ = reg.Add("after", 8)

	first := <-sub.Changes()
	if first.Kind != registry.MemberAdded || first.ID != "after" {
		t.Fatalf("first event = %+v, want Added(after) — no gap/duplication", first)
	}
	sub.Unsubscribe()
}

// Slow consumer: registry mutations never block on a full watcher buffer.
func TestSlowConsumerDoesNotBlockMutations(t *testing.T) {
	reg := registry.New[int]()
	sub, _ := reg.Subscribe()
	defer sub.Unsubscribe()

	// Do not drain; push far more events than the buffer.
	for i := 0; i < 200; i++ {
		id := registry.MemberID(string(rune('a'+i%26)) + "_" + itoa(i))
		if err := reg.Add(id, i); err != nil {
			if errors.Is(err, registry.ErrMemberExists) {
				// duplicate from wraparound: fine, just replace it instead
				_ = reg.Replace(id, i)
				continue
			}
			t.Fatalf("Add blocked/failed: %v", err)
		}
	}
	if reg.Len() != 200 {
		t.Fatalf("Len = %d, want 200", reg.Len())
	}
	// Buffer is bounded: at most subscriptionBuffer events are queued.
	if n := len(sub.Changes()); n > 64 {
		t.Fatalf("buffered %d events, want <= 64", n)
	}
}

func itoa(i int) string {
	if i == 0 {
		return "0"
	}
	var b []byte
	for i > 0 {
		b = append([]byte{byte('0' + i%10)}, b...)
		i /= 10
	}
	return string(b)
}

// Snapshot Range visits every member exactly once (order unspecified).
func TestSnapshotRangeCoversAll(t *testing.T) {
	reg := registry.New[int]()
	for i := 0; i < 10; i++ {
		_ = reg.Add(registry.MemberID(itoa(i)), i)
	}
	snap := reg.Snapshot()
	seen := map[registry.MemberID]int{}
	snap.Range(func(id registry.MemberID, v int) bool {
		seen[id] = v
		return true
	})
	if len(seen) != 10 {
		t.Fatalf("Range visited %d members, want 10", len(seen))
	}
	// early stop
	count := 0
	snap.Range(func(id registry.MemberID, v int) bool {
		count++
		return count < 3
	})
	if count != 3 {
		t.Fatalf("early-stop Range ran %d iterations, want 3", count)
	}
}

// Property: at any moment a MemberID maps to at most one value (no duplicates,
// no torn writes), even under heavy concurrent Add/Replace/Remove + readers.
func TestPropertyMembershipConsistencyConcurrent(t *testing.T) {
	reg := registry.New[int]()
	_ = reg.Add("X", 0)

	var wg sync.WaitGroup
	for w := 0; w < 4; w++ {
		wg.Add(1)
		go func(w int) {
			defer wg.Done()
			for i := 0; i < 5000; i++ {
				switch w % 3 {
				case 0:
					_ = reg.Replace("X", i)
				case 1:
					_ = reg.Remove("X")
					_ = reg.Add("X", i)
				case 2:
					_, _ = reg.Get("X")
					_ = reg.Snapshot()
				}
			}
		}(w)
	}
	wg.Wait()

	// Final state is one of the two legal outcomes of the Add/Remove races:
	// present or absent — never duplicated, never torn.
	if n := reg.Len(); n > 1 {
		t.Fatalf("Len = %d, want 0 or 1", n)
	}
	if reg.Has("X") {
		if _, ok := reg.Get("X"); !ok {
			t.Fatal("Has true but Get false")
		}
	}
}
