package hub_test

// OnObservation 的 unregister close(ch) 与 Publish 的 send 并发时不得有
// 数据竞争（联合审查 P0：应用 -race 在 proc 插件退出观察路径实测）。
import (
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/console/hub"
)

func TestPublishConcurrentUnsubscribe(t *testing.T) {
	r := hub.New()
	unsub, err := r.OnObservation(64, func(hub.Observation) {})
	if err != nil {
		t.Fatal(err)
	}
	var wg sync.WaitGroup
	wg.Add(2)
	go func() {
		defer wg.Done()
		for i := 0; i < 5000; i++ {
			r.Publish(hub.Observation{Type: "test"})
		}
	}()
	go func() {
		defer wg.Done()
		time.Sleep(time.Millisecond)
		_ = unsub()
	}()
	wg.Wait()
}
