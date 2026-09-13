package logstore

import (
	"context"
	"net/url"
	"strconv"

	consolehub "dynamic-runtime/extensions/console/hub"
)

// RegisterLogsQuery registers the standard "logs" hub query on the given hub
// registry: limit (default/max 200), level (minimum severity) and contains
// (substring filter). This is the contract the console's log panel consumes;
// registering it here keeps that contract next to its implementation.
func RegisterLogsQuery(hub *consolehub.Registry, store *Store) (func() error, error) {
	return hub.RegisterQuery("logs", "console:logstore", func(_ context.Context, values url.Values) (any, *consolehub.Error) {
		limit := 200
		if n, err := strconv.Atoi(values.Get("limit")); err == nil && n > 0 && n <= 1000 {
			limit = n
		}
		return store.Latest(limit, values.Get("level"), values.Get("contains")), nil
	})
}
