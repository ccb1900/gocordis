package runtime_test

// 论文 Temporal Composability 的确定性并发验收：
//   不变量  Fiber Gone ⟹ 所有 Effect 逆已完成
//   活性    Orchestrator 在等待慢 install 时仍能调度其他 Fiber
// 用阶段屏障记录真实完成顺序，不依赖 time.Sleep / 先等某一方再观察另一方。
import (
	"context"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

type slowInstallComp struct {
	entered chan struct{}
	release chan struct{}
	order   *[]string
	mu      *sync.Mutex
	tag     string
	panics  bool
	calls   *atomic.Int32
}

func (c *slowInstallComp) Name() string                  { return "slow-install" }
func (c *slowInstallComp) Inject() []runtime.Dependency  { return nil }
func (c *slowInstallComp) Provide() []runtime.Capability { return nil }
func (c *slowInstallComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	go func() {
		_ = ctx.Effect(func() (func() error, error) {
			c.mu.Lock()
			*c.order = append(*c.order, "install-enter:"+c.tag)
			c.mu.Unlock()
			close(c.entered)
			<-c.release
			return func() error {
				c.calls.Add(1)
				c.mu.Lock()
				*c.order = append(*c.order, "inverse:"+c.tag)
				c.mu.Unlock()
				if c.panics {
					panic("inverse boom")
				}
				return nil
			}, nil
		})
	}()
	return nil, nil
}

// Gone 与 inverse 的真实完成顺序：当 Gone 返回时 inverse 必须已完成。
// 关键：先等 Gone，再检查 inverse 是否已完成——而不是反过来。
func TestPhaseGoneImpliesInverseDone(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	var order []string
	var mu sync.Mutex
	calls := &atomic.Int32{}
	c := &slowInstallComp{
		entered: make(chan struct{}), release: make(chan struct{}),
		order: &order, mu: &mu, tag: "late", calls: calls,
	}
	f, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	<-c.entered // install 正在阻塞

	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	// 阶段屏障：先释放 install，再从 Gone 的返回点反查 inverse 是否
	// 已完成。Gone 返回时 inverse 必须已在 order 中（不是先等 inverse）。
	close(c.release)
	if err := f.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	mu.Lock()
	inverseDone := false
	for _, e := range order {
		if e == "inverse:late" {
			inverseDone = true
		}
	}
	mu.Unlock()
	if !inverseDone {
		t.Fatalf("Gone returned before inverse completed (order=%v)", order)
	}
	if calls.Load() != 1 {
		t.Fatalf("inverse calls=%d, want 1", calls.Load())
	}
}

// 多个并发 Installing 全部完成后才开始最终 LIFO 撤销。
func TestPhaseMultipleInstallingAllDoneBeforeLIFO(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	var order []string
	var mu sync.Mutex
	calls := &atomic.Int32{}
	comp := &multiSlowComp{order: &order, mu: &mu, calls: calls, releases: []chan struct{}{
		make(chan struct{}), make(chan struct{}), make(chan struct{}),
	}, entered: make(chan struct{}, 3)}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	for i := 0; i < 3; i++ {
		<-comp.entered
	}
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}

	// 交错释放：每次释放一个 install，其逆完成后 installing 屏障才递减。
	// Gone 必须等到三个逆全部完成（阶段屏障：从 Gone 返回点反查）。
	close(comp.releases[0])
	<-time.After(50 * time.Millisecond)
	close(comp.releases[1])
	close(comp.releases[2])
	if err := f.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	mu.Lock()
	invCount := 0
	for _, e := range order {
		if len(e) >= 8 && e[:8] == "inverse:" {
			invCount++
		}
	}
	mu.Unlock()
	if invCount != 3 {
		t.Fatalf("inverse count=%d at Gone, want 3 (order=%v)", invCount, order)
	}
	if calls.Load() != 3 {
		t.Fatalf("inverse calls=%d, want 3", calls.Load())
	}
}

type multiSlowComp struct {
	order    *[]string
	mu       *sync.Mutex
	calls    *atomic.Int32
	releases []chan struct{}
	entered  chan struct{}
}

func (c *multiSlowComp) Name() string                  { return "multi-slow" }
func (c *multiSlowComp) Inject() []runtime.Dependency  { return nil }
func (c *multiSlowComp) Provide() []runtime.Capability { return nil }
func (c *multiSlowComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	for i, rel := range c.releases {
		tag := string(rune('a' + i))
		go func(tag string, rel chan struct{}) {
			_ = ctx.Effect(func() (func() error, error) {
				c.mu.Lock()
				*c.order = append(*c.order, "install:"+tag)
				c.mu.Unlock()
				c.entered <- struct{}{}
				<-rel
				return func() error {
					c.calls.Add(1)
					c.mu.Lock()
					*c.order = append(*c.order, "inverse:"+tag)
					c.mu.Unlock()
					return nil
				}, nil
			})
		}(tag, rel)
	}
	return nil, nil
}

// Orchestrator 活性：一个 Fiber 的慢 install 正在等待时，另一个 Fiber
// 仍必须能被调度到 Active。旧实现 beginUnwind 在决策线程 Cond.Wait，
// 会阻塞整个 Runtime（联合审查 P0-02 架构风险）。
func TestPhaseOrchestratorStaysLiveDuringSlowInstall(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	var order []string
	var mu sync.Mutex
	calls := &atomic.Int32{}
	a := &slowInstallComp{
		entered: make(chan struct{}), release: make(chan struct{}),
		order: &order, mu: &mu, tag: "slow", calls: calls,
	}
	fa, err := rt.Load(a)
	if err != nil {
		t.Fatal(err)
	}
	if err := fa.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	<-a.entered
	if err := fa.Dispose(); err != nil { // 触发 beginUnwind（旧实现在此阻塞）
		t.Fatal(err)
	}

	// B 的 Apply 应立即被调度——若 Orchestrator 卡在 Cond.Wait，这里超时。
	b := &plainComp{}
	fb, err := rt.Load(b)
	if err != nil {
		t.Fatal(err)
	}
	if err := fb.Ready(ctx); err != nil {
		t.Fatalf("orchestrator blocked by slow install: %v", err)
	}

	close(a.release)
	if err := fa.Gone(ctx); err != nil {
		t.Fatal(err)
	}
}

type plainComp struct{}

func (c *plainComp) Name() string                                    { return "plain" }
func (c *plainComp) Inject() []runtime.Dependency                    { return nil }
func (c *plainComp) Provide() []runtime.Capability                   { return nil }
func (c *plainComp) Apply(*runtime.Context) (runtime.Cleanup, error) { return nil, nil }

// inverse panic 必须被防护，且不影响其他 inverse 执行（Exactly-once + 继续撤销）。
func TestPhaseInversePanicDoesNotBlockOthers(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	var order []string
	var mu sync.Mutex
	calls := &atomic.Int32{}
	comp := &panicMixComp{order: &order, mu: &mu, calls: calls, entered: make(chan struct{}, 2)}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	<-comp.entered
	<-comp.entered
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctx); err == nil {
		t.Fatal("panicking inverse must surface as cleanup error")
	}
	if calls.Load() != 2 {
		t.Fatalf("inverse calls=%d, want 2 (panic must not skip sibling)", calls.Load())
	}
	mu.Lock()
	defer mu.Unlock()
	if len(order) != 2 {
		t.Fatalf("order=%v, want both inverses", order)
	}
}

type panicMixComp struct {
	order   *[]string
	mu      *sync.Mutex
	calls   *atomic.Int32
	entered chan struct{}
}

func (c *panicMixComp) Name() string                  { return "panic-mix" }
func (c *panicMixComp) Inject() []runtime.Dependency  { return nil }
func (c *panicMixComp) Provide() []runtime.Capability { return nil }
func (c *panicMixComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	go func() {
		_ = ctx.Effect(func() (func() error, error) {
			c.entered <- struct{}{}
			return func() error {
				c.calls.Add(1)
				c.mu.Lock()
				*c.order = append(*c.order, "panic-inverse")
				c.mu.Unlock()
				panic("boom")
			}, nil
		})
	}()
	go func() {
		_ = ctx.Effect(func() (func() error, error) {
			c.entered <- struct{}{}
			return func() error {
				c.calls.Add(1)
				c.mu.Lock()
				*c.order = append(*c.order, "normal-inverse")
				c.mu.Unlock()
				return nil
			}, nil
		})
	}()
	return nil, nil
}
