package configwatch_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
)

// WithTxGuard 把应用的声明事务边界提升到整个 Parse→Reconcile→Post 过程：
// 只锁 Parse 会让应用在 Parse 返回后、Controller.Reconcile 之前插入一次
// 控制台写入，导致运行时提交过期快照（联合审查 Iteration 9）。
func TestTxGuardCoversReconcileAndPost(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	var order []string
	mark := func(s string) { order = append(order, s) } // worker 单线程，无需锁

	guarded := false
	opts := []configwatch.Option{
		configwatch.WithTxGuard(func(ctx context.Context, fn func() error) error {
			guarded = true
			mark("guard-enter")
			defer mark("guard-exit")
			return fn()
		}),
		configwatch.WithPostReconcile(func(ctx context.Context, cfg config.Config) error {
			mark("post")
			return nil
		}),
	}
	e := newEnv(t, `[[components]]
id = "c"
type = "camera"
`, opts...)
	if err := e.adapter.Sync(ctx); err != nil {
		t.Fatal(err)
	}
	if !guarded {
		t.Fatal("tx guard must wrap the whole reconcile transaction")
	}
	if len(order) == 0 || order[0] != "guard-enter" || order[len(order)-1] != "guard-exit" {
		t.Fatalf("guard order = %v", order)
	}
	foundPost := false
	for _, s := range order {
		if s == "post" {
			foundPost = true
		}
	}
	if !foundPost {
		t.Fatalf("post-reconcile must run inside the guard, order=%v", order)
	}
}

// 失败路径也要退出守卫（defer 语义），否则应用锁永久卡死。
func TestTxGuardReleasedOnError(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	var guardDepth int
	guard := func(ctx context.Context, fn func() error) error {
		guardDepth++
		defer func() { guardDepth-- }()
		return fn()
	}
	e := newEnv(t, `not valid toml = `, configwatch.WithTxGuard(guard))
	if err := e.adapter.Sync(ctx); err == nil {
		t.Fatal("expected parse failure")
	}
	if guardDepth != 0 {
		t.Fatalf("guard leaked after failure: depth=%d", guardDepth)
	}
	// 修正内容后第二次 sync 必须成功（守卫没有把应用锁永久占住）。
	e.reader.set(`[[components]]
id = "c"
type = "camera"
`)
	if err := e.adapter.Sync(ctx); err != nil {
		t.Fatalf("second sync after failure: %v", err)
	}
	if guardDepth != 0 {
		t.Fatalf("guard leaked after success: depth=%d", guardDepth)
	}
}

var _ = errors.New // keep errors import meaningful if trimmed
