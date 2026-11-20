package runtime_test

// 论文 Temporal Composability：Fiber Gone 必须意味着所有已安装 effect 的
// inverse 已完成。Installing 与 Unwinding 并发时，旧实现只收集 Committed
// slot，Installing 的 inverse 在 Gone 之后才跑（联合审查 P0-02）。
import (
	"context"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

type effectUnwindComp struct {
	installEntered chan struct{}
	releaseInstall chan struct{}
	inverseDone    chan struct{}
	calls          *atomic.Int32
	panicInverse   bool
}

func (c *effectUnwindComp) Name() string                  { return "effect-unwind-race" }
func (c *effectUnwindComp) Inject() []runtime.Dependency  { return nil }
func (c *effectUnwindComp) Provide() []runtime.Capability { return nil }
func (c *effectUnwindComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	// 在独立 goroutine 里安装一个慢 effect；Apply 不等待它完成。
	go func() {
		_ = ctx.Effect(func() (func() error, error) {
			close(c.installEntered)
			<-c.releaseInstall
			return func() error {
				c.calls.Add(1)
				if c.panicInverse {
					panic("inverse boom")
				}
				close(c.inverseDone)
				return nil
			}, nil
		})
	}()
	return nil, nil
}

func TestInstallingInverseCompletesBeforeGone(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	c := &effectUnwindComp{
		installEntered: make(chan struct{}),
		releaseInstall: make(chan struct{}),
		inverseDone:    make(chan struct{}),
		calls:          &atomic.Int32{},
	}
	f, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	<-c.installEntered // install 正在阻塞，slot 处于 Installing

	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	gone := make(chan error, 1)
	go func() { gone <- f.Gone(ctx) }()

	select {
	case err := <-gone:
		t.Fatalf("fiber reached Gone before installing inverse completed: %v", err)
	case <-time.After(200 * time.Millisecond):
	}

	close(c.releaseInstall) // 让 install 完成；此时 Context 已 Unwinding
	select {
	case <-c.inverseDone:
	case <-time.After(2 * time.Second):
		t.Fatal("installing effect inverse never ran")
	}
	select {
	case err := <-gone:
		if err != nil {
			t.Fatalf("gone: %v", err)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("fiber did not reach Gone after inverse completed")
	}
	if got := c.calls.Load(); got != 1 {
		t.Fatalf("inverse calls = %d, want exactly once", got)
	}
}

// inverse panic 必须被防护（与正常撤销路径一致），不得逃逸为进程 panic。
func TestInstallingInversePanicIsGuarded(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	c := &effectUnwindComp{
		installEntered: make(chan struct{}),
		releaseInstall: make(chan struct{}),
		inverseDone:    make(chan struct{}),
		calls:          &atomic.Int32{},
		panicInverse:   true,
	}
	f, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	<-c.installEntered
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	close(c.releaseInstall)
	// Gone 返回 cleanup error（包含被防护的 inverse panic），但绝不 panic。
	if err := f.Gone(ctx); err == nil {
		t.Fatal("guarded inverse panic must surface as a cleanup error")
	}
	if got := c.calls.Load(); got != 1 {
		t.Fatalf("inverse calls = %d, want 1", got)
	}
}
