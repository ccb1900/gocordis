package proc

import (
	"bufio"
	"context"
	"encoding/json"
	"fmt"
	"os"
	"os/signal"
	"syscall"
)

// Handler serves one plugin method. params is the raw JSON of the request's
// "params"; the returned value is marshaled as the response "result". Return
// an error (or *RPCError for a specific code) to fail the request.
type Handler func(ctx context.Context, params json.RawMessage) (any, error)

// RPCError is a JSON-RPC error response with an explicit code.
type RPCError struct {
	Code    int
	Message string
}

func (e *RPCError) Error() string { return fmt.Sprintf("plugin rpc error %d: %s", e.Code, e.Message) }

// Serve runs the plugin side of the protocol on stdin/stdout: it writes the
// handshake header, then serves JSON-RPC methods until stdin is closed or the
// process is signaled. It returns when the plugin should exit; a plugin main
// is therefore:
//
//	func main() {
//	    methods := map[string]proc.Handler{
//	        "encode": func(_ context.Context, params json.RawMessage) (any, error) { ... },
//	    }
//	    if err := proc.Serve(methods); err != nil {
//	        os.Exit(1)
//	    }
//	}
//
// Serve handles the "shutdown" method itself (it stops the serve loop, letting
// main return). SIGINT/SIGTERM also stop the loop. Serve writes nothing to
// stdout except protocol messages.
func Serve(methods map[string]Handler) error {
	return ServeContext(context.Background(), methods)
}

// ServeContext is Serve with an explicit base context (canceled by SIGINT/
// SIGTERM regardless).
func ServeContext(ctx context.Context, methods map[string]Handler) error {
	w := &lineWriter{bw: bufio.NewWriter(os.Stdout)}
	if _, err := fmt.Fprintln(w, HandshakeHeader); err != nil {
		return err
	}
	if err := w.Flush(); err != nil {
		return err
	}
	enc := json.NewEncoder(w)

	ctx, cancel := context.WithCancel(ctx)
	defer cancel()
	sigs := make(chan os.Signal, 1)
	signal.Notify(sigs, syscall.SIGINT, syscall.SIGTERM)
	done := make(chan struct{})
	go func() {
		select {
		case <-sigs:
			cancel()
		case <-done:
		}
		signal.Stop(sigs)
	}()
	defer close(done)

	reader := bufio.NewReader(os.Stdin)
	for {
		if err := ctx.Err(); err != nil {
			return nil
		}
		line, err := reader.ReadString('\n')
		if err != nil {
			if line == "" {
				return nil // stdin closed: host is gone; exit cleanly
			}
			// fall through: try to parse a trailing partial line? No — the
			// protocol is line-delimited; a partial line is a violation.
			return nil
		}
		line = trimCR(line)
		if line == "" {
			continue
		}
		var msg struct {
			JSONRPC string          `json:"jsonrpc"`
			ID      *int            `json:"id"`
			Method  string          `json:"method"`
			Params  json.RawMessage `json:"params"`
		}
		if err := json.Unmarshal([]byte(line), &msg); err != nil {
			if werr := writeError(w, enc, nil, -32700, "parse error"); werr != nil {
				return werr
			}
			continue
		}
		if msg.Method == MethodShutdown {
			_ = writeResult(w, enc, msg.ID, struct{}{})
			return nil
		}
		h, ok := methods[msg.Method]
		if !ok {
			if msg.ID != nil {
				if werr := writeError(w, enc, msg.ID, -32601, "unknown method "+msg.Method); werr != nil {
					return werr
				}
			}
			continue
		}
		// Method handlers run sequentially (v1 contract: one in-flight call
		// per connection); long methods should manage their own deadlines.
		result, herr := h(ctx, msg.Params)
		if msg.ID == nil {
			continue // notification: no response
		}
		if herr != nil {
			var re *RPCError
			if asErr(herr, &re) {
				if werr := writeError(w, enc, msg.ID, re.Code, re.Message); werr != nil {
					return werr
				}
				continue
			}
			if werr := writeError(w, enc, msg.ID, -32000, herr.Error()); werr != nil {
				return werr
			}
			continue
		}
		if err := writeResult(w, enc, msg.ID, result); err != nil {
			return err
		}
	}
}

func trimCR(s string) string {
	for len(s) > 0 && (s[len(s)-1] == '\n' || s[len(s)-1] == '\r') {
		s = s[:len(s)-1]
	}
	return s
}

// lineWriter flushes after every protocol message: a hanging response must
// never sit in a buffer while the host blocks on it.
type lineWriter struct{ bw *bufio.Writer }

func (w *lineWriter) Write(p []byte) (int, error) {
	n, err := w.bw.Write(p)
	if err == nil {
		err = w.bw.Flush()
	}
	return n, err
}

func (w *lineWriter) Flush() error { return w.bw.Flush() }

func writeResult(w *lineWriter, enc *json.Encoder, id *int, result any) error {
	return enc.Encode(map[string]any{"jsonrpc": "2.0", "id": deref(id), "result": result})
}

func writeError(w *lineWriter, enc *json.Encoder, id *int, code int, message string) error {
	return enc.Encode(map[string]any{
		"jsonrpc": "2.0", "id": deref(id),
		"error": map[string]any{"code": code, "message": message},
	})
}

func deref(id *int) int {
	if id == nil {
		return 0
	}
	return *id
}

// asErr is a local errors.As for *RPCError (kept dependency-light).
func asErr(err error, target **RPCError) bool {
	for err != nil {
		if e, ok := err.(*RPCError); ok {
			*target = e
			return true
		}
		u, ok := err.(interface{ Unwrap() error })
		if !ok {
			return false
		}
		err = u.Unwrap()
	}
	return false
}
