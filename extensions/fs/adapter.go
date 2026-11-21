package fs

import (
	"context"
	"errors"
	"io"
	iofs "io/fs"
	"strings"
	"sync"
	"time"
)

// NewFS adapts a gocordis FS Service to the standard io/fs.FS interface. Names
// must be valid io/fs paths (no leading slash, no "..", no scheme); they are
// resolved through the Service's mount table, so a plain name is served by the
// empty-prefix fallback mount. The returned FS also implements ReadFileFS and
// ReadDirFS, and its files implement ReadDirFile and Seek when the underlying
// provider supports it.
func NewFS(svc Service, ctx context.Context) iofs.FS {
	return &adapter{svc: svc, ctx: ctx}
}

type adapter struct {
	svc Service
	ctx context.Context
}

func validateFSPath(name, op string) error {
	if strings.Contains(name, "://") || !iofs.ValidPath(name) {
		return &iofs.PathError{Op: op, Path: name, Err: iofs.ErrInvalid}
	}
	return nil
}

func (a *adapter) Open(name string) (iofs.File, error) {
	if err := validateFSPath(name, "open"); err != nil {
		return nil, err
	}
	// Directories need a ReadDirFile wrapper; probing Stat lets one wrapper
	// serve both providers (local can open a directory, memory cannot).
	if fi, err := a.svc.Stat(a.ctx, name); err == nil && fi.IsDir() {
		return &adapterFile{
			ctx: a.ctx, svc: a.svc, name: name,
			info: adaptFileInfo(fi), dir: true,
		}, nil
	}
	f, err := a.svc.Open(a.ctx, name)
	if err != nil {
		return nil, &iofs.PathError{Op: "open", Path: name, Err: err}
	}
	return &adapterFile{ctx: a.ctx, svc: a.svc, name: name, file: f}, nil
}

func (a *adapter) Stat(name string) (iofs.FileInfo, error) {
	if err := validateFSPath(name, "stat"); err != nil {
		return nil, err
	}
	fi, err := a.svc.Stat(a.ctx, name)
	if err != nil {
		return nil, &iofs.PathError{Op: "stat", Path: name, Err: err}
	}
	return adaptFileInfo(fi), nil
}

func (a *adapter) ReadFile(name string) ([]byte, error) {
	if err := validateFSPath(name, "readfile"); err != nil {
		return nil, err
	}
	data, err := a.svc.ReadFile(a.ctx, name)
	if err != nil {
		return nil, &iofs.PathError{Op: "readfile", Path: name, Err: err}
	}
	return data, nil
}

func (a *adapter) ReadDir(name string) ([]iofs.DirEntry, error) {
	if err := validateFSPath(name, "readdir"); err != nil {
		return nil, err
	}
	return a.readDir(name)
}

func (a *adapter) readDir(name string) ([]iofs.DirEntry, error) {
	infos, err := a.svc.ReadDir(a.ctx, name)
	if err != nil {
		return nil, &iofs.PathError{Op: "readdir", Path: name, Err: err}
	}
	out := make([]iofs.DirEntry, 0, len(infos))
	for _, fi := range infos {
		out = append(out, dirEntry{info: adaptFileInfo(fi)})
	}
	return out, nil
}

// adapterFile wraps a gocordis File into io/fs.File. A directory wrapper has no
// underlying file; Read returns EOF and ReadDir serves a stable snapshot.
type adapterFile struct {
	ctx     context.Context
	svc     Service
	name    string
	file    File
	info    iofs.FileInfo
	dir     bool
	once    sync.Once
	entries []iofs.DirEntry
	readErr error
	pos     int
}

func (f *adapterFile) Close() error {
	if f.file != nil {
		return f.file.Close()
	}
	return nil
}

func (f *adapterFile) Read(p []byte) (int, error) {
	if f.dir || f.file == nil {
		return 0, io.EOF
	}
	return f.file.Read(p)
}

func (f *adapterFile) Seek(offset int64, whence int) (int64, error) {
	if f.file == nil {
		return 0, &iofs.PathError{Op: "seek", Path: f.name, Err: errors.New("fs: seek on directory")}
	}
	if s, ok := f.file.(io.Seeker); ok {
		return s.Seek(offset, whence)
	}
	return 0, &iofs.PathError{Op: "seek", Path: f.name, Err: errors.New("fs: seek not supported")}
}

func (f *adapterFile) Stat() (iofs.FileInfo, error) {
	if f.info != nil {
		return f.info, nil
	}
	fi, err := f.file.Stat()
	if err != nil {
		return nil, err
	}
	return adaptFileInfo(fi), nil
}

func (f *adapterFile) ReadDir(n int) ([]iofs.DirEntry, error) {
	if !f.dir && f.info == nil {
		fi, err := f.file.Stat()
		if err != nil {
			return nil, err
		}
		f.info = adaptFileInfo(fi)
	}
	if f.info != nil && !f.info.IsDir() {
		return nil, &iofs.PathError{Op: "readdir", Path: f.name, Err: errors.New("fs: not a directory")}
	}
	f.once.Do(func() {
		infos, err := f.svc.ReadDir(f.ctx, f.name)
		if err != nil {
			f.readErr = err
			return
		}
		f.entries = make([]iofs.DirEntry, 0, len(infos))
		for _, fi := range infos {
			f.entries = append(f.entries, dirEntry{info: adaptFileInfo(fi)})
		}
	})
	if f.readErr != nil {
		return nil, &iofs.PathError{Op: "readdir", Path: f.name, Err: f.readErr}
	}
	if n <= 0 {
		out := append([]iofs.DirEntry(nil), f.entries...)
		f.pos = len(f.entries)
		return out, nil
	}
	if f.pos >= len(f.entries) {
		return nil, io.EOF
	}
	end := f.pos + n
	if end > len(f.entries) {
		end = len(f.entries)
	}
	out := append([]iofs.DirEntry(nil), f.entries[f.pos:end]...)
	f.pos = end
	return out, nil
}

type dirEntry struct{ info iofs.FileInfo }

func (d dirEntry) Name() string                 { return d.info.Name() }
func (d dirEntry) IsDir() bool                  { return d.info.IsDir() }
func (d dirEntry) Type() iofs.FileMode          { return d.info.Mode().Type() }
func (d dirEntry) Info() (iofs.FileInfo, error) { return d.info, nil }

// osInfo adapts provider-neutral metadata (which has no Mode/Sys) into io/fs.
type osInfo struct {
	fi   FileInfo
	mode iofs.FileMode
}

func adaptFileInfo(fi FileInfo) iofs.FileInfo {
	mode := iofs.FileMode(0o644)
	if fi.IsDir() {
		mode = iofs.ModeDir | 0o755
	}
	if si, ok := fi.(SymlinkInfo); ok && si.IsSymlink() {
		mode |= iofs.ModeSymlink
	}
	return osInfo{fi: fi, mode: mode}
}

func (o osInfo) Name() string        { return o.fi.Name() }
func (o osInfo) Size() int64         { return o.fi.Size() }
func (o osInfo) Mode() iofs.FileMode { return o.mode }
func (o osInfo) ModTime() time.Time  { return o.fi.ModTime() }
func (o osInfo) IsDir() bool         { return o.fi.IsDir() }
func (o osInfo) Sys() any            { return nil }
