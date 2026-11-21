package fs

import (
	"dynamic-runtime/extensions/fs/contract"
	"dynamic-runtime/extensions/fs/local"
	"dynamic-runtime/extensions/fs/memory"
)

// Shared provider-neutral types are re-exported as aliases so every existing
// consumer of extensions/fs keeps compiling unchanged.
type (
	FileInfo              = contract.FileInfo
	SymlinkInfo           = contract.SymlinkInfo
	File                  = contract.File
	WatchEvent            = contract.WatchEvent
	Watcher               = contract.Watcher
	StreamWatcher         = contract.StreamWatcher
	Provider              = contract.Provider
	StreamProvider        = contract.StreamProvider
	ExtendedProvider      = contract.ExtendedProvider
	RemoveOptions         = contract.RemoveOptions
	MkdirOptions          = contract.MkdirOptions
	CopyOptions           = contract.CopyOptions
	RemoveOptionsProvider = contract.RemoveOptionsProvider
	MkdirOptionsProvider  = contract.MkdirOptionsProvider
	CopyOptionsProvider   = contract.CopyOptionsProvider
	SymlinkProvider       = contract.SymlinkProvider
	ErrNotSupported       = contract.ErrNotSupported
)

// NewLocalProvider returns a file:// provider confined to root.
func NewLocalProvider(root string) Provider { return local.New(root) }

// NewMemoryProvider returns an in-memory provider.
func NewMemoryProvider() Provider { return memory.New() }
