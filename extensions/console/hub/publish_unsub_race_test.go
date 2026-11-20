package hub_test

// OnObservation 的 unregister close(ch) 与 Publish 的 send 并发时不得有
// 数据竞争（联合审查 P0：应用 -race 在 proc 插件退出观察路径实测）。
// 用同步屏障控制交错，不依赖 time.Sleep 的时间窗口。
import (
	"sync"
	"testing"

	"dynamic-runtime/extensions/console/hub"
)

func TestPublishConcurrentUnsubscribe(t *testing.T) {
	r := hub.New()
	unsub, err := r.OnObservation(64, func(hub.Observation) {})
	if err != nil {
		t.Fatal(err)
	}

	// 两个 goroutine 都就位后同时放行：send 与 close 真实交错。
	ready := make(chan struct{}, 2)
	goPublish := make(chan struct{})
	var wg sync.WaitGroup
	wg.Add(2)
	go func() {
		defer wg.Done()
		ready <- struct{}{}
		<-goPublish
		for i := 0; i < 5000; i++ {
			r.Publish(hub.Observation{Type: "test"})
		}
	}()
	go func() {
		defer wg.Done()
		ready <- struct{}{}
		<-goPublish
		_ = unsub()
	}()
	<-ready
	<-ready
	close(goPublish)
	wg.Wait()
}
