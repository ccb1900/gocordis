//go:build windows

package proc

import (
	"os"
	"path/filepath"
	"testing"
)

// Windows 文件系统没有执行位：合法的 .exe 必须通过校验（否则 proc 后端
// 在 Windows 完全不可用），regular file 即契约。
func TestValidateExecutableWindows(t *testing.T) {
	path := filepath.Join(t.TempDir(), "plugin.exe")
	if err := os.WriteFile(path, []byte{}, 0o666); err != nil {
		t.Fatal(err)
	}
	if err := validateExecutable(path); err != nil {
		t.Fatalf("validateExecutable(%q) = %v, want nil", path, err)
	}
}
