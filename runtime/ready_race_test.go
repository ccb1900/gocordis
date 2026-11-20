package runtime_test

// Fiber.Ready 与 Load/Dispose 并发时不得有数据竞争：Ready 曾在 RUnlock
// 之后读 f.intent，而 Load/Dispose 在锁内写 intent（联合审查 P0-01）。
import (
	"context"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

type readyRaceComp struct{}

func (c *readyRaceComp) Name() string                                    { return "ready-race" }
func (c *readyRaceComp) Inject() []runtime.Dependency                    { return nil }
func (c *readyRaceComp) Provide() []runtime.Capability                   { return nil }
func (c *readyRaceComp) Apply(*runtime.Context) (runtime.Cleanup, error) { return nil, nil }

func TestReadyConcurrentLoadDispose(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	f, err := rt.Load(&readyRaceComp{})
	if err != nil {
		t.Fatal(err)
	}
	var wg sync.WaitGroup
	wg.Add(2)
	go func() {
		defer wg.Done()
		for i := 0; i < 2000; i++ {
			_ = f.Ready(ctx)
		}
	}()
	go func() {
		defer wg.Done()
		for i := 0; i < 150; i++ {
			_ = f.Dispose()
			_ = f.Gone(ctx)
			_ = f.Load() // SAME fiber: writes f.intent under f.mu
		}
	}()
	wg.Wait()
}
