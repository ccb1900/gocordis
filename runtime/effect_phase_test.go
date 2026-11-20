package runtime_test

// 论文 Temporal Composability 的确定性并发验收：
//   不变量  Fiber Gone ⟹ 所有 Effect 逆已完成
//   活性    Orchestrator 在等待慢 install 时仍能调度其他 Fiber
// 用阶段屏障记录真实完成顺序，不依赖 time.Sleep / 先等某一方再观察另一方。
import (
	"context"
	"fmt"
	"strings"
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

// 两阶段握手组件：install 与 inverse 各自可独立阻塞，让测试能在
// "inverse 已进入但尚未返回"时观察 Fiber 状态——这是证明
// "Gone ⟹ 所有 inverse 已完成" 的关键可观测点（联合审查末轮）。
type twoPhaseInstallComp struct {
	entered        chan struct{}
	inverseEntered chan struct{}
	releaseInstall []chan struct{}
	releaseInverse []chan struct{}
	calls          *atomic.Int32
	unwinding      chan struct{} // closed when ctx.Done() fires (= Context entered Unwinding)
}

func newTwoPhaseComp(n int, calls *atomic.Int32) *twoPhaseInstallComp {
	c := &twoPhaseInstallComp{
		entered:        make(chan struct{}, n),
		inverseEntered: make(chan struct{}, n),
		releaseInstall: make([]chan struct{}, n),
		releaseInverse: make([]chan struct{}, n),
		calls:          calls,
		unwinding:      make(chan struct{}),
	}
	for i := 0; i < n; i++ {
		c.releaseInstall[i] = make(chan struct{})
		c.releaseInverse[i] = make(chan struct{})
	}
	return c
}

func (c *twoPhaseInstallComp) Name() string                  { return "two-phase-install" }
func (c *twoPhaseInstallComp) Inject() []runtime.Dependency  { return nil }
func (c *twoPhaseInstallComp) Provide() []runtime.Capability { return nil }
func (c *twoPhaseInstallComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	// 把 Context 的取消信号转发给测试：ctx.Done() 在 beginUnwind 中触发，
	// 收到它即证明 Context 已进入 Unwinding——比轮询 Fiber.State() 更精确。
	go func() {
		<-ctx.Done()
		close(c.unwinding)
	}()
	for i := range c.releaseInstall {
		ri, ro := c.releaseInstall[i], c.releaseInverse[i]
		go func() {
			_ = ctx.Effect(func() (func() error, error) {
				c.entered <- struct{}{} // 阶段 1：install 已进入
				<-ri                    // 阻塞直到测试释放 install
				return func() error {
					c.inverseEntered <- struct{}{} // 阶段 2：inverse 已进入
					<-ro                           // 阻塞直到测试释放 inverse
					c.calls.Add(1)
					return nil
				}, nil
			})
		}()
	}
	// Apply 不返回直到所有 install goroutine 都真正进入 Effect 的 install
	// 阶段——保证 f.Ready() 时没有"还在路上"的 Effect 被 Dispose 拒绝。
	for i := 0; i < len(c.releaseInstall); i++ {
		<-c.entered
	}
	return nil, nil
}

func waitSignals(t *testing.T, ch chan struct{}, n int, phase string) {
	t.Helper()
	deadline := time.Now().Add(3 * time.Second)
	for i := 0; i < n; i++ {
		select {
		case <-ch:
		case <-time.After(time.Until(deadline)):
			t.Fatalf("%s: signal %d/%d never arrived", phase, i+1, n)
		}
	}
}

// 核心不变量：inverse 正在执行时 Fiber 不得进入 Gone；只有 inverse 全部
// 返回后 Gone 才可完成。用 Fiber 的可观察状态断言，而不是 Gone goroutine
// 的非阻塞检查（后者在 goroutine 未获调度时天然通过，证明力不足）。
func TestPhaseGoneImpliesInverseDone(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	calls := &atomic.Int32{}
	c := newTwoPhaseComp(1, calls)
	f, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	// 等 Context.Done 信号（= beginUnwind 已执行）后再释放 install，
	// 确保走自撤销路径——这是屏障要保护的场景。
	select {
	case <-c.unwinding:
	case <-ctx.Done():
		t.Fatal("context never entered unwinding")
	}
	close(c.releaseInstall[0])
	waitSignals(t, c.inverseEntered, 1, "inverse entered") // inverse 正在执行

	// 关键断言：inverse 尚未返回时 Fiber 不得 Gone。
	if st := f.State(); st == runtime.StateGone {
		t.Fatal("Fiber reached Gone while inverse was still executing")
	}
	if calls.Load() != 0 {
		t.Fatalf("inverse completed before release: calls=%d", calls.Load())
	}
	close(c.releaseInverse[0]) // 释放 inverse
	if err := f.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if calls.Load() != 1 {
		t.Fatalf("inverse calls=%d, want 1", calls.Load())
	}
}

// 多并发 Installing 屏障：3 个 install 并发进入并阻塞，Dispose 后释放
// install；在 3 个 inverse 全部进入但都未返回时，Fiber 必须仍未 Gone；
// 全部释放 inverse 后 Gone 才可返回。
func TestConcurrentInstallingBarrier(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	const n = 3
	calls := &atomic.Int32{}
	c := newTwoPhaseComp(n, calls)
	f, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	// ctx.Done 表示 orchestrator 已执行 beginUnwind，
	// 此时释放 install 必走自撤销路径（屏障保护的场景）。
	select {
	case <-c.unwinding:
	case <-ctx.Done():
		t.Fatal("context never entered unwinding")
	}
	for i := 0; i < n; i++ {
		close(c.releaseInstall[i])
	}
	// 三个 inverse 都已进入执行阶段（全部被 releaseInverse 阻塞）。
	// 三个 inverse 必须全部进入执行阶段：完整证明三个并发 Installing
	// 对应的撤销工作都受屏障保护（联合审查 P1）。
	waitSignals(t, c.inverseEntered, n, "all inverses entered")

	// 核心断言：inverse 执行期间 Fiber 不得 Gone，且 inverse 尚未完成。
	if st := f.State(); st == runtime.StateGone {
		t.Fatal("Fiber reached Gone while inverses were still executing")
	}
	if calls.Load() != 0 {
		t.Fatalf("inverses completed before release: calls=%d", calls.Load())
	}

	// 全部释放 inverse → 等 Gone → 断言每个 inverse 恰好执行一次。
	for i := 0; i < n; i++ {
		close(c.releaseInverse[i])
	}
	if err := f.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if got := calls.Load(); got != n {
		t.Fatalf("inverse calls=%d, want %d", got, n)
	}
}

func TestEffectStrictLIFO(t *testing.T) {
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
	// 三个 Effect 在 Apply 内【串行】注册 → seq 顺序确定 a,b,c。
	comp := &orderedEffectsComp{order: &order, mu: &mu, calls: calls}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}

	mu.Lock()
	defer mu.Unlock()
	if calls.Load() != 3 {
		t.Fatalf("inverse calls=%d, want 3", calls.Load())
	}
	// 注册顺序 a,b,c；严格 LIFO 必须是 c,b,a。
	invOrder := inverseTags(order)
	want := []string{"c", "b", "a"}
	if fmt.Sprint(invOrder) != fmt.Sprint(want) {
		t.Fatalf("inverse order = %v, want strict LIFO %v (all=%v)", invOrder, want, order)
	}
}

type orderedEffectsComp struct {
	order *[]string
	mu    *sync.Mutex
	calls *atomic.Int32
}

func (c *orderedEffectsComp) Name() string                  { return "ordered-effects" }
func (c *orderedEffectsComp) Inject() []runtime.Dependency  { return nil }
func (c *orderedEffectsComp) Provide() []runtime.Capability { return nil }
func (c *orderedEffectsComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	for _, tag := range []string{"a", "b", "c"} {
		tag := tag
		err := ctx.Effect(func() (func() error, error) {
			c.mu.Lock()
			*c.order = append(*c.order, "install:"+tag)
			c.mu.Unlock()
			return func() error {
				c.calls.Add(1)
				c.mu.Lock()
				*c.order = append(*c.order, "inverse:"+tag)
				c.mu.Unlock()
				return nil
			}, nil
		})
		if err != nil {
			return nil, err
		}
	}
	return nil, nil
}

func inverseTags(order []string) []string {
	var out []string
	for _, e := range order {
		if strings.HasPrefix(e, "inverse:") {
			out = append(out, strings.TrimPrefix(e, "inverse:"))
		}
	}
	return out
}

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

// Effect 事件是生命周期状态的一部分：observers 收到 Committed 之后才能
// 收到 Undoing/Undone，且 Gone 不得早于对应的终态事件（联合审查 P1）。
// 用 EventSink 记录真实事件顺序，不用 sleep。
type effectEventSink struct {
	mu      sync.Mutex
	events  []string
	eventCh chan string // stage signal: every effect event delivered here (buffered)
}

func newEffectEventSink() *effectEventSink {
	return &effectEventSink{eventCh: make(chan string, 64)}
}

func (s *effectEventSink) Emit(ev runtime.RuntimeEvent) {
	if ev.Type != "EffectCommitted" && ev.Type != "EffectUndoing" && ev.Type != "EffectUndone" {
		return
	}
	s.mu.Lock()
	s.events = append(s.events, string(ev.Type))
	s.mu.Unlock()
	select {
	case s.eventCh <- string(ev.Type):
	default: // never block: EventSink contract
	}
}

func (s *effectEventSink) snapshot() []string {
	s.mu.Lock()
	defer s.mu.Unlock()
	return append([]string(nil), s.events...)
}

func TestPhaseEffectEventsOrderedBeforeGone(t *testing.T) {
	sink := newEffectEventSink()
	rt, err := runtime.New(runtime.WithEventSink(sink))
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
		order: &order, mu: &mu, tag: "events", calls: calls,
	}
	f, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	<-c.entered // install 阻塞中
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	close(c.release)
	if err := f.Gone(ctx); err != nil {
		t.Fatal(err)
	}

	// 核心不变量：Gone 返回时，终态事件 EffectUndone 必须已发布，
	// 且 Committed（若有）早于 Undoing——正是"emit 纳入 Installing 屏障"
	// 要保证的（旧实现 Gone 可早于 emit、Undoing 可早于 Committed）。
	events := sink.snapshot()
	if len(events) < 2 {
		t.Fatalf("effect events = %v, want at least Undoing+Undone before Gone", events)
	}
	if events[len(events)-1] != "EffectUndone" {
		t.Fatalf("last effect event = %s, want EffectUndone (got %v)", events[len(events)-1], events)
	}
	firstUndo := -1
	for i, e := range events {
		if e == "EffectUndoing" {
			firstUndo = i
			break
		}
	}
	if firstUndo > 0 && events[0] != "EffectCommitted" {
		t.Fatalf("EffectUndoing at %d but no leading EffectCommitted: %v", firstUndo, events)
	}
}

// 第二个场景：install 完成（Committed 已发布）之后再 Dispose，
// 事件顺序必须是 Committed → Undoing → Undone。
func TestPhaseEffectEventsAfterCommit(t *testing.T) {
	sink := newEffectEventSink()
	rt, err := runtime.New(runtime.WithEventSink(sink))
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
		order: &order, mu: &mu, tag: "commit", calls: calls,
	}
	f, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	<-c.entered
	// 先让 install 完成 → Committed 事件应发布
	close(c.release)
	// 纯 channel 阶段同步：Committed 事件一到就继续，不用 sleep/轮询。
	select {
	case ev := <-sink.eventCh:
		if ev != "EffectCommitted" {
			t.Fatalf("first effect event = %s, want EffectCommitted", ev)
		}
	case <-ctx.Done():
		t.Fatal("Committed event never published")
	}
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	events := sink.snapshot()
	want := []string{"EffectCommitted", "EffectUndoing", "EffectUndone"}
	if fmt.Sprint(events) != fmt.Sprint(want) {
		t.Fatalf("effect events = %v, want %v", events, want)
	}
}

// 重复调度实验：Gone 返回时 EffectUndone 必须已经发布。旧实现把 emit
// 放在 installing-- 之后，collectInverses 可越过未发布的终态事件，在
// 高重复调度下产生 Gone 早于 EffectUndone 的可观测违规。
func TestPhaseEffectUndoneBeforeGoneRepeated(t *testing.T) {
	for i := 0; i < 200; i++ {
		sink := newEffectEventSink()
		rt, err := runtime.New(runtime.WithEventSink(sink))
		if err != nil {
			t.Fatal(err)
		}
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		var order []string
		var mu sync.Mutex
		calls := &atomic.Int32{}
		c := &slowInstallComp{
			entered: make(chan struct{}), release: make(chan struct{}),
			order: &order, mu: &mu, tag: fmt.Sprintf("r%d", i), calls: calls,
		}
		f, err := rt.Load(c)
		if err != nil {
			cancel()
			t.Fatal(err)
		}
		if err := f.Ready(ctx); err != nil {
			cancel()
			t.Fatal(err)
		}
		<-c.entered
		close(c.release)
		if err := f.Dispose(); err != nil {
			cancel()
			t.Fatal(err)
		}
		if err := f.Gone(ctx); err != nil {
			cancel()
			t.Fatal(err)
		}
		events := sink.snapshot()
		if len(events) == 0 || events[len(events)-1] != "EffectUndone" {
			cancel()
			t.Fatalf("iter %d: Gone before EffectUndone; events=%v", i, events)
		}
		_ = rt.Close(ctx)
		cancel()
	}
}
