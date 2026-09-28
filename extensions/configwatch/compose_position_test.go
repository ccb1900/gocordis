package configwatch

import (
	"strings"
	"testing"
)

// TOML 解码错误必须带行号与出错行：操作员面对"invalid escape character"
// 逐行找配置文件不可接受（真实案例：Windows 路径 \s 写进双引号字符串）。
func TestParseDocumentReportsPosition(t *testing.T) {
	_, err := ParseDocument([]byte("path = \"\\sanling6\\dir\"\nother = 1\n"))
	if err == nil {
		t.Fatal("invalid escape must be rejected")
	}
	for _, want := range []string{"1|", `path = "\sanling6\dir"`, "invalid escape character"} {
		if !strings.Contains(err.Error(), want) {
			t.Fatalf("error %q missing %q", err, want)
		}
	}
}
