//go:build go1.25

package local

import "context"

// rename uses the stdlib confined primitive. The narrow interface keeps the
// module's go1.24 directive compatible with go vet's stdversion check while
// still calling Root.Rename on toolchains that provide it (Go 1.25+).
func (p *Provider) rename(ctx context.Context, a, b string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	var renamer interface{ Rename(string, string) error } = p.root
	if err := renamer.Rename(a, b); err != nil {
		return err
	}
	return ctx.Err()
}
