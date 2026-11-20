package scheduler

import (
	"context"
	"errors"
	"testing"
	"time"
)

// EXT-04: Schedule.Next is external code. It must never run while the
// scheduler lock is held — a re-entrant Next (calling Add/Remove) or a
// panicking Next must not deadlock or crash the scheduler.

// reentrantSchedule calls back into the scheduler from Next.
type reentrantSchedule struct {
	s      *Scheduler
	jobID  JobID
	fired  bool
	panics bool
}

func (r *reentrantSchedule) Next(after time.Time) (time.Time, bool) {
	if r.panics {
		panic("schedule exploded")
	}
	if !r.fired {
		r.fired = true
		// Re-entrant mutation: Remove our own job from inside Next.
		_ = r.s.Remove(r.jobID)
	}
	return after.Add(time.Hour), true
}

func TestReentrantScheduleNextDoesNotDeadlock(t *testing.T) {
	s := New()
	defer func() { _ = s.Close() }()

	sched := &reentrantSchedule{s: s, jobID: JobID("reent")}
	done := make(chan error, 1)
	go func() {
		done <- s.Add(Job{ID: JobID("reent"), Schedule: sched, Task: func(context.Context) error { return nil }})
	}()
	select {
	case err := <-done:
		// The re-entrant Remove may or may not win the race; the Add outcome
		// is either success or ErrJobExists-less cleanup — the essential
		// property is that it RETURNS (no deadlock).
		if err != nil && !errors.Is(err, ErrJobExists) && !errors.Is(err, ErrSchedulerClosed) {
			t.Fatalf("unexpected add error: %v", err)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("Add deadlocked — Schedule.Next ran under the scheduler lock")
	}
}

func TestPanickingScheduleNextContained(t *testing.T) {
	s := New()
	defer func() { _ = s.Close() }()

	done := make(chan error, 1)
	go func() {
		done <- s.Add(Job{ID: JobID("boom"), Schedule: &reentrantSchedule{panics: true}, Task: func(context.Context) error { return nil }})
	}()
	select {
	case err := <-done:
		if !errors.Is(err, ErrInvalidSchedule) {
			t.Fatalf("panicking Next not contained as ErrInvalidSchedule: %v", err)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("Add did not return after a panicking Next")
	}
}
