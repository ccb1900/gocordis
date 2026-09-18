package procplugin

import (
	"os"
	"path/filepath"
	"runtime"
	"testing"
)

// 后端可执行名解析：相对路径锚定插件目录取绝对值；Windows 上对缺失的
// 非 .exe 名补 .exe 后缀（清单按 POSIX 习惯写 ./demo，产物是 demo.exe）。
func TestResolveExecutable(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "demo"), []byte{}, 0o755); err != nil {
		t.Fatal(err)
	}
	c := &Component{dir: dir, backend: "./demo"}
	if got, want := c.resolveExecutable(), filepath.Join(dir, "demo"); got != want {
		t.Fatalf("existing backend: got %q, want %q", got, want)
	}

	// 两平台都缺：返回原值（绝对化），让 Start 报出原始错误。
	c2 := &Component{dir: dir, backend: "./missing"}
	if got, want := c2.resolveExecutable(), filepath.Join(dir, "missing"); got != want {
		t.Fatalf("missing backend: got %q, want %q", got, want)
	}

	// Windows 专属：只有 demo.exe 时自动补后缀。
	if runtime.GOOS == "windows" {
		if err := os.WriteFile(filepath.Join(dir, "only.exe"), []byte{}, 0o755); err != nil {
			t.Fatal(err)
		}
		c3 := &Component{dir: dir, backend: "./only"}
		if got, want := c3.resolveExecutable(), filepath.Join(dir, "only.exe"); got != want {
			t.Fatalf("exe fallback: got %q, want %q", got, want)
		}
	}
}
