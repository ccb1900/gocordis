// Package scheduler provides the Runtime Scheduler Extension: controlled task
// execution when a time condition is met.
//
// Scheduler answers "when should something run?". It does not know what a task
// does, why it runs, or how to retry it - that belongs to Components.
//
// Scheduler is an Extension, not part of the Kernel:
//   - this package does not import the runtime package;
//   - a Scheduler never creates Fibers, Activations, Contexts, or Providers;
//   - a Job is not a Kernel lifecycle object (no Pending/Loading/.../Gone);
//   - there is no global singleton, no polling, no goroutine killing, and
//     Task code is never executed while holding the Scheduler mutex.
//
// v0.1 model:
//   - Schedule kinds: Once and Interval only (no cron/calendar/timezone).
//   - Interval uses a theoretical timeline anchored at a fixed Start
//     (Start + k*Every), never "previous completion + Every".
//   - Missed ticks are skipped; an overrun never creates a catch-up storm.
//   - Same Job executions never overlap; different Jobs may run concurrently.
//   - Task errors are not retried and do not remove the Job. Task panics are
//     isolated so the Scheduler keeps running.
//
// Runtime integration follows the standard pattern: a Component creates a
// *Scheduler in Apply, registers scheduler.Close as a reversible ctx.Effect,
// and provides it as a capability; Consumers depend on the Scheduler
// capability, never on individual Jobs.
package scheduler

import (
	"context"
	"errors"
	"sync"
	"time"
)

// Errors returned by Scheduler operations. Compare with errors.Is.
var (
	// ErrSchedulerClosed is returned by Add/Remove after Close.
	ErrSchedulerClosed = errors.New("scheduler closed")
	// ErrJobExists is returned by Add for a duplicate JobID. Add never
	// overwrites the original Job.
	ErrJobExists = errors.New("job already exists")
	// ErrJobNotFound is returned by Remove for an unknown JobID.
	ErrJobNotFound = errors.New("job not found")
	// ErrInvalidSchedule is returned when a Schedule cannot produce a valid
	// future trigger (e.g. Interval.Every <= 0).
	ErrInvalidSchedule = errors.New("invalid schedule")
)

// JobID identifies a Job within one Scheduler instance.
type JobID string

// Schedule computes the theoretical trigger timeline of a Job.
//
// For a valid Schedule, Next(after) returns ok == true and a time strictly
// after `after`. Once Next reports ok == false once the schedule has ended.
type Schedule interface {
	Next(after time.Time) (time.Time, bool)
}

// Once triggers at most once, when the Scheduler time reaches At. If At is
// already in the past when the Job is added, the Job is due immediately and
// triggers exactly once.
type Once struct {
	At time.Time
}

// Next returns At once (when after < At), then ok == false forever after.
func (o Once) Next(after time.Time) (time.Time, bool) {
	if !after.Before(o.At) {
		return time.Time{}, false
	}
	return o.At, true
}

// Interval triggers on a fixed theoretical timeline Start + k*Every (k >= 1),
// independent of actual execution duration.
type Interval struct {
	// Start anchors the theoretical timeline. If zero at Add time, the
	// Scheduler fixes it to the registration time (the Add linearization
	// point).
	Start time.Time
	Every time.Duration
}

// Next returns the first theoretical tick strictly after `after`.
func (i Interval) Next(after time.Time) (time.Time, bool) {
	if i.Every <= 0 {
		return time.Time{}, false
	}
	start := i.Start
	if start.IsZero() {
		// Unanchored interval: nothing to compute until registered.
		return time.Time{}, false
	}
	if !after.After(start) {
		// Before or exactly at the anchor: the first tick is Start + Every.
		return start.Add(i.Every), true
	}
	k := after.Sub(start)/i.Every + 1
	return start.Add(k * i.Every), true
}

// Task is the scheduled unit of work. It must cooperate with cancellation; the
// Scheduler never force-terminates a Task goroutine.
type Task func(context.Context) error

// Job binds a Schedule to a Task.
type Job struct {
	ID       JobID
	Schedule Schedule
	Task     Task
}

// jobState is the internal per-Job execution state. It is a resource state
// (scheduled/running/next), NOT a second Kernel Fiber lifecycle.
type jobState struct {
	id       JobID
	schedule Schedule
	task     Task

	next    time.Time // next pending theoretical trigger; zero if none
	running bool
	cancel  context.CancelFunc // cancels the current execution's context
}

// Scheduler runs Jobs on their Schedule. New() returns a running Scheduler
// (there is no separate Start step).
//
// Internal lifecycle (not public): Running -> Closing/Closed. Public behavior:
// once Close is called, Add/Remove return ErrSchedulerClosed and no future
// execution starts.
type Scheduler struct {
	mu     sync.Mutex
	closed bool
	jobs   map[JobID]*jobState

	active  int           // number of running executions
	drained chan struct{} // closed exactly once when closed && active == 0

	wake     chan struct{} // capacity-1 signal to the loop: recompute
	loopDone chan struct{}
}

// New creates a running Scheduler.
func New() *Scheduler {
	s := &Scheduler{
		jobs:     make(map[JobID]*jobState),
		drained:  make(chan struct{}),
		wake:     make(chan struct{}, 1),
		loopDone: make(chan struct{}),
	}
	go s.run()
	return s
}

// Add registers a Job. Add returns after the Job is in the scheduling set;
// future triggers are processed in linearization order. Add fails with
// ErrJobExists (without changing anything) for a duplicate ID, ErrInvalidSchedule
// for an invalid Schedule, and ErrSchedulerClosed after Close.
func (s *Scheduler) Add(job Job) error {
	if job.Schedule == nil {
		return ErrInvalidSchedule
	}
	if job.Task == nil {
		return ErrInvalidSchedule
	}

	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return ErrSchedulerClosed
	}
	if _, exists := s.jobs[job.ID]; exists {
		s.mu.Unlock()
		return ErrJobExists
	}

	now := time.Now()
	var sched Schedule
	var next time.Time
	switch sc := job.Schedule.(type) {
	case Once:
		if sc.At.IsZero() {
			s.mu.Unlock()
			return ErrInvalidSchedule
		}
		sched = sc
		if sc.At.After(now) {
			next = sc.At
		} else {
			// Already reached: due immediately, exactly once.
			next = now
		}
	case Interval:
		if sc.Every <= 0 {
			s.mu.Unlock()
			return ErrInvalidSchedule
		}
		if sc.Start.IsZero() {
			sc.Start = now // anchor at the Add linearization point
		}
		sched = sc
		t, ok := sc.Next(now)
		if !ok || !t.After(now) {
			s.mu.Unlock()
			return ErrInvalidSchedule
		}
		next = t
	default:
		t, ok := job.Schedule.Next(now)
		if !ok || !t.After(now) {
			s.mu.Unlock()
			return ErrInvalidSchedule
		}
		sched = job.Schedule
		next = t
	}

	s.jobs[job.ID] = &jobState{
		id:       job.ID,
		schedule: sched,
		task:     job.Task,
		next:     next,
	}
	s.mu.Unlock()

	s.wakeLoop()
	return nil
}

// Remove removes a Job from the future scheduling set. A running execution of
// the Job is NOT force-killed; its execution context is canceled cooperatively
// (it continues until it honors the cancellation). Remove returns
// ErrJobNotFound for an unknown ID and ErrSchedulerClosed after Close.
func (s *Scheduler) Remove(id JobID) error {
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return ErrSchedulerClosed
	}
	j := s.jobs[id]
	if j == nil {
		s.mu.Unlock()
		return ErrJobNotFound
	}
	delete(s.jobs, id)
	if j.running && j.cancel != nil {
		j.cancel()
	}
	s.mu.Unlock()

	s.wakeLoop()
	return nil
}

// Close shuts the Scheduler down: it stops future triggers, cancels every
// running Task context, and waits for running Tasks to return. Close is
// idempotent. Tasks that ignore cancellation make Close block; use
// CloseContext with a deadline for a bounded wait.
func (s *Scheduler) Close() error {
	return s.CloseContext(context.Background())
}

// CloseContext shuts the Scheduler down with a bounded wait. It behaves like
// Close, but returns ctx.Err() if running Tasks have not returned by the time
// ctx is done. After CloseContext has been called (even on timeout) Add/Remove
// return ErrSchedulerClosed and no new execution starts; the internal shutdown
// completes on its own once the remaining Tasks return.
func (s *Scheduler) CloseContext(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}

	s.mu.Lock()
	if !s.closed {
		s.closed = true
		for _, j := range s.jobs {
			if j.running && j.cancel != nil {
				j.cancel()
			}
		}
		s.jobs = make(map[JobID]*jobState) // release non-running Job references
		s.checkDrainedLocked()
	}
	s.mu.Unlock()

	s.wakeLoop()

	select {
	case <-s.drained:
		return nil
	case <-ctx.Done():
		return ctx.Err()
	}
}

// wakeLoop asks the loop to recompute. It is non-blocking and safe to call
// without holding the lock.
func (s *Scheduler) wakeLoop() {
	select {
	case s.wake <- struct{}{}:
	default:
	}
}

// checkDrainedLocked closes drained exactly once when the Scheduler is closed
// and no executions remain. Must hold s.mu.
func (s *Scheduler) checkDrainedLocked() {
	if s.closed && s.active == 0 {
		select {
		case <-s.drained:
		default:
			close(s.drained)
		}
	}
}

// run is the single Scheduler loop: it sleeps until the earliest next trigger
// (or a wake signal) and launches due executions. Task code never runs here.
func (s *Scheduler) run() {
	defer close(s.loopDone)
	for {
		s.mu.Lock()
		if s.closed {
			s.mu.Unlock()
			return
		}
		now := time.Now()
		var earliest time.Time
		for _, j := range s.jobs {
			if j.running || j.next.IsZero() {
				continue
			}
			if earliest.IsZero() || j.next.Before(earliest) {
				earliest = j.next
			}
		}
		var wait time.Duration
		hasWait := false
		if !earliest.IsZero() {
			d := earliest.Sub(now)
			if d < 0 {
				d = 0
			}
			wait = d
			hasWait = true
		}
		s.mu.Unlock()

		if !hasWait {
			// Nothing scheduled (or everything running): wait for a wake.
			<-s.wake
			s.fireDue()
			continue
		}

		timer := time.NewTimer(wait)
		select {
		case <-timer.C:
		case <-s.wake:
			if !timer.Stop() {
				select {
				case <-timer.C:
				default:
				}
			}
		}
		s.fireDue()
	}
}

// fireDue launches every due, non-running execution. Must not be called with
// the lock held.
func (s *Scheduler) fireDue() {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return
	}
	now := time.Now()
	for _, j := range s.jobs {
		if j.running || j.next.IsZero() || j.next.After(now) {
			continue
		}
		ctx, cancel := context.WithCancel(context.Background())
		j.running = true
		j.cancel = cancel
		j.next = time.Time{} // recomputed when the execution finishes
		s.active++
		go s.runTask(j, ctx, cancel)
	}
}

// runTask executes one Task on its own goroutine with a fresh, per-execution
// context. Task errors are ignored (no retry, no removal). A Task panic is
// recovered so the Scheduler keeps running.
func (s *Scheduler) runTask(j *jobState, ctx context.Context, cancel context.CancelFunc) {
	// Defer order (LIFO): on panic, recover runs first, then finishExecution;
	// on normal return, both run in the same order.
	defer s.finishExecution(j, cancel)
	defer func() { _ = recover() }()
	_ = j.task(ctx)
}

// finishExecution updates bookkeeping after a Task returns or panics.
func (s *Scheduler) finishExecution(j *jobState, cancel context.CancelFunc) {
	s.mu.Lock()
	j.running = false
	j.cancel = nil
	if !s.closed {
		if _, present := s.jobs[j.id]; present {
			now := time.Now()
			if t, ok := j.schedule.Next(now); ok {
				j.next = t
			} else {
				j.next = time.Time{} // e.g. Once already fired: no more triggers
			}
		} else {
			j.next = time.Time{} // Job removed while running
		}
	} else {
		j.next = time.Time{}
	}
	s.active--
	s.checkDrainedLocked()
	wake := !s.closed
	s.mu.Unlock()

	cancel() // release the per-execution context after the Task returned

	if wake {
		s.wakeLoop()
	}
}
