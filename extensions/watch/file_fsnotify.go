package watch

import (
	"context"
	"errors"
	"path/filepath"
	"runtime"
	"strings"

	"github.com/fsnotify/fsnotify"
)

// runFileSession drives the native (fsnotify) observation for one file.
//
// The target file's parent directory is watched; only events whose path is the
// target file wake the shared engine, which re-probes the file revision
// (content hash) and deduplicates. This covers direct writes, atomic saves
// (rename over target), removal and recreation. fsnotify maps to the platform
// notification API (ReadDirectoryChangesW on Windows, kqueue on macOS), so the
// same semantics hold on the v0.1 target platforms.
func runFileSession(ctx context.Context, fs *fileSub) {
	probe := func() Revision { return fileRevision(fs.path) }

	watcher, err := fsnotify.NewWatcher()
	if err != nil {
		fs.sub.finish(err)
		return
	}
	defer watcher.Close()

	dir := filepath.Dir(fs.path)
	if err := watcher.Add(dir); err != nil {
		if ctx.Err() != nil {
			fs.sub.finish(ctx.Err())
		} else {
			fs.sub.finish(err)
		}
		return
	}

	// Attach before the baseline so a steady-state watcher is fully registered
	// before the first observation; any event between Add and the baseline is
	// still buffered and caught by the first wait.
	baseline := probe()

	wait := func(ctx context.Context, stopped <-chan struct{}) (sessionAction, error) {
		for {
			select {
			case <-stopped:
				return actionStop, nil
			case <-ctx.Done():
				return actionCtx, nil
			case ev, ok := <-watcher.Events:
				if !ok {
					return channelClosedAction(stopped, ctx)
				}
				if sameFilePath(ev.Name, fs.path) {
					return actionEvent, nil
				}
			case err, ok := <-watcher.Errors:
				if !ok {
					return channelClosedAction(stopped, ctx)
				}
				if err == nil {
					continue
				}
				// A watcher error may mean an event was missed (e.g. an
				// overflow): force a re-probe; the engine deduplicates by
				// revision. Do not treat it as fatal for the subscription.
				select {
				case <-stopped:
					return actionStop, nil
				case <-ctx.Done():
					return actionCtx, nil
				default:
					return actionEvent, nil
				}
			}
		}
	}

	runSessionFrom(ctx, fs.sub, fs.source, baseline, probe, wait)
}

// channelClosedAction reports the terminal reason when an fsnotify channel
// closed without a stop/ctx (should not happen because watcher.Close is
// deferred until after the session exits).
func channelClosedAction(stopped <-chan struct{}, ctx context.Context) (sessionAction, error) {
	select {
	case <-stopped:
		return actionStop, nil
	case <-ctx.Done():
		return actionCtx, nil
	default:
		return actionFatal, errors.New("watch event stream closed unexpectedly")
	}
}

// sameFilePath reports whether the fsnotify event name refers to the watched
// file. Paths are compared case-insensitively on Windows (the filesystem is
// case-insensitive there).
func sameFilePath(eventName, target string) bool {
	eventName = filepath.Clean(eventName)
	target = filepath.Clean(target)
	if runtime.GOOS == "windows" {
		return strings.EqualFold(eventName, target)
	}
	return eventName == target
}
