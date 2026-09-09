package configutil

import (
	"fmt"
	"strconv"

	"dynamic-runtime/extensions/config"
)

func OptionalString(cc config.ComponentConfig, key, def string) string {
	if v, ok := cc.Config[key]; ok && v != nil {
		if s, ok := v.(string); ok {
			return s
		}
		return fmt.Sprint(v)
	}
	return def
}

func RequiredString(cc config.ComponentConfig, key string) (string, error) {
	if v, ok := cc.Config[key]; ok && v != nil {
		if s, ok := v.(string); ok && s != "" {
			return s, nil
		}
	}
	return "", fmt.Errorf("console: component %q missing string config %q", cc.ID, key)
}

func OptionalInt(cc config.ComponentConfig, key string, def int) int {
	v, ok := cc.Config[key]
	if !ok {
		return def
	}
	switch n := v.(type) {
	case int:
		return n
	case int64:
		return int(n)
	case int32:
		return int(n)
	case uint64:
		return int(n)
	case float64:
		return int(n)
	case string:
		if i, err := strconv.Atoi(n); err == nil {
			return i
		}
	}
	return def
}

func OptionalBool(cc config.ComponentConfig, key string, def bool) bool {
	v, ok := cc.Config[key]
	if !ok {
		return def
	}
	switch b := v.(type) {
	case bool:
		return b
	case string:
		return b == "true"
	}
	return def
}

// OptionalStringSlice reads a string-array config key, tolerating scalar
// entries promoted via Sprint. Missing keys return def.
func OptionalStringSlice(cc config.ComponentConfig, key string, def []string) []string {
	v, ok := cc.Config[key]
	if !ok || v == nil {
		return def
	}
	switch t := v.(type) {
	case []string:
		return append([]string(nil), t...)
	case []any:
		out := make([]string, 0, len(t))
		for _, item := range t {
			if item == nil {
				continue
			}
			out = append(out, fmt.Sprint(item))
		}
		return out
	case string:
		if t == "" {
			return def
		}
		return []string{t}
	default:
		return def
	}
}
