//go:build go1.25

package local

// symlink uses the confined stdlib primitive. Root.Symlink resolves both names
// against the Root, so the link can never be created outside the root even if
// an attacker swaps a parent directory between validation and creation. The
// narrow interface keeps the module's go1.24 directive vet-clean.
func (p *Provider) symlink(stored, linkRel string) error {
	var linker interface{ Symlink(string, string) error } = p.root
	return linker.Symlink(stored, linkRel)
}
