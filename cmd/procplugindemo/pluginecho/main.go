// Command pluginecho is a gocordis process-external plugin: an independent
// executable that speaks the proc protocol (handshake + JSON-RPC over
// stdin/stdout) via proc.Serve.
//
// Build (from the repo root):
//
//	go build -o build/plugin-echo ./cmd/procplugindemo/pluginecho
//
// It serves one method, "encode" (uppercases the text and adds "!"). The host
// reaches it through the Codec contract — the plugin never imports the host.
package main

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"strings"

	"dynamic-runtime/extensions/loader/proc"
)

func main() {
	methods := map[string]proc.Handler{
		"encode": func(_ context.Context, params json.RawMessage) (any, error) {
			var in struct {
				Text string `json:"text"`
			}
			if err := json.Unmarshal(params, &in); err != nil {
				return nil, &proc.RPCError{Code: -32602, Message: "bad params: " + err.Error()}
			}
			if in.Text == "" {
				return nil, &proc.RPCError{Code: -32000, Message: "empty text"}
			}
			return map[string]string{"encoded": strings.ToUpper(in.Text) + "!"}, nil
		},
	}
	if err := proc.Serve(methods); err != nil {
		fmt.Fprintln(os.Stderr, "plugin:", err)
		os.Exit(1)
	}
	// "shutdown" received or stdin closed: exit cleanly (the host has already
	// stopped the fiber's effects in order).
}
