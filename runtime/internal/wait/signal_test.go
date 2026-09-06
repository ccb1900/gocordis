package wait_test

import (
	"sync"
	"testing"
	"time"

	"dynamic-runtime/runtime/internal/wait"
)

func TestSignalBroadcastWakesWaiters(t *testing.T) {
	s := wait.New()

	const n = 32
	var wg sync.WaitGroup
	wg.Add(n)
	start := make(chan struct{})
	for i := 0; i < n; i++ {
		go func() {
			defer wg.Done()
			<-start
			ch := s.Channel()
			<-ch
		}()
	}
	close(start)
	// Let every goroutine capture the pre-broadcast channel.
	time.Sleep(20 * time.Millisecond)
	s.Broadcast()

	done := make(chan struct{})
	go func() { wg.Wait(); close(done) }()
	select {
	case <-done:
	case <-time.After(2 * time.Second):
		t.Fatal("not all waiters woke after Broadcast")
	}
}

func TestSignalGenerationsAdvance(t *testing.T) {
	s := wait.New()
	v0 := s.Version()
	s.Broadcast()
	v1 := s.Version()
	if v1 != v0+1 {
		t.Fatalf("version = %d after one broadcast, want %d", v1, v0+1)
	}
}

func TestSignalChannelReplacedAfterBroadcast(t *testing.T) {
	s := wait.New()
	old := s.Channel()
	s.Broadcast()
	cur := s.Channel()
	if old == cur {
		t.Fatal("channel must be replaced after Broadcast")
	}
	// The old channel must be closed.
	select {
	case <-old:
	default:
		t.Fatal("old channel not closed after Broadcast")
	}
}
