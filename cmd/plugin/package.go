package main

import (
	"archive/tar"
	"compress/gzip"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"

	toml "github.com/pelletier/go-toml/v2"

	"dynamic-runtime/extensions/pluginmeta"
)

const pluginManifestName = "plugin.toml"

// packageValue lists the flags for `plugin package` that consume a value.
var packageValue = map[string]bool{"out": true}

// bundleFile is one artifact to store in the catalog bundle. name is a
// slash-separated path relative to the plugin directory root.
type bundleFile struct {
	name string
	path string
}

// Package verifies a plugin manifest, computes a SHA-256 digest over the
// plugin's artifacts (every regular file except plugin.toml and the output
// bundle), writes that digest to Source.Checksum via go-toml/v2, and produces
// a deterministic tar.gz bundle containing the updated manifest plus all
// artifacts. The default output is <id>.tar.gz inside the plugin directory.
// It refuses invalid manifests, symlinks and any path that could escape the
// bundle root.
func Package(dir, out string) (string, error) {
	if strings.TrimSpace(dir) == "" {
		return "", errors.New("plugin: empty plugin directory")
	}
	root, err := filepath.Abs(dir)
	if err != nil {
		return "", err
	}
	info, err := os.Stat(root)
	if err != nil {
		return "", err
	}
	if !info.IsDir() {
		return "", fmt.Errorf("plugin: %s is not a directory", dir)
	}

	manifestPath := filepath.Join(root, pluginManifestName)
	manifestInfo, err := os.Stat(manifestPath)
	if err != nil {
		return "", fmt.Errorf("plugin: stat manifest: %w", err)
	}
	raw, err := os.ReadFile(manifestPath)
	if err != nil {
		return "", fmt.Errorf("plugin: read manifest: %w", err)
	}
	m, err := pluginmeta.Parse(raw, "toml")
	if err != nil {
		return "", fmt.Errorf("plugin: parse manifest: %w", err)
	}
	// A stale (or malformed) checksum is exactly what packaging replaces, so
	// validate the declaration with that field blanked.
	previousChecksum := m.Source.Checksum
	m.Source.Checksum = ""
	if err := pluginmeta.Validate(m); err != nil {
		return "", fmt.Errorf("plugin: invalid manifest: %w", err)
	}
	m.Source.Checksum = previousChecksum

	if out == "" {
		out = filepath.Join(dir, packageName(m.ID)+".tar.gz")
	}
	outAbs, err := filepath.Abs(out)
	if err != nil {
		return "", err
	}

	files, err := collectBundleFiles(root, outAbs)
	if err != nil {
		return "", err
	}
	sum, err := checksumArtifacts(files)
	if err != nil {
		return "", err
	}
	m.Source.Checksum = sum
	updated, err := toml.Marshal(m)
	if err != nil {
		return "", fmt.Errorf("plugin: encode manifest: %w", err)
	}

	// Write the bundle to a temporary file first so a failed bundle never
	// leaves a corrupt artifact at the caller's output path.
	if err := writeBundle(outAbs, updated, files); err != nil {
		return "", err
	}
	if err := os.WriteFile(manifestPath, updated, manifestInfo.Mode().Perm()); err != nil {
		return "", fmt.Errorf("plugin: write manifest checksum: %w", err)
	}
	return out, nil
}

// collectBundleFiles lists artifacts in deterministic order. The output
// bundle itself (if it lives inside the plugin directory) and plugin.toml are
// excluded; the manifest is added explicitly by writeBundle.
func collectBundleFiles(root, outAbs string) ([]bundleFile, error) {
	var files []bundleFile
	err := filepath.WalkDir(root, func(path string, d os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if path == root {
			return nil
		}
		if d.IsDir() {
			return nil
		}
		rel, err := filepath.Rel(root, path)
		if err != nil {
			return err
		}
		name := filepath.ToSlash(rel)
		if err := validateBundleName(name); err != nil {
			return err
		}
		abs, err := filepath.Abs(path)
		if err != nil {
			return err
		}
		if abs == outAbs {
			return nil
		}
		if d.Type()&os.ModeSymlink != 0 {
			return fmt.Errorf("plugin: refusing symlink %q: bundle paths must not escape the plugin directory", name)
		}
		if !d.Type().IsRegular() {
			return fmt.Errorf("plugin: refusing non-regular file %q", name)
		}
		if name == pluginManifestName {
			return nil
		}
		files = append(files, bundleFile{name: name, path: path})
		return nil
	})
	if err != nil {
		return nil, err
	}
	sort.Slice(files, func(i, j int) bool { return files[i].name < files[j].name })
	return files, nil
}

// validateBundleName rejects absolute paths, backslashes and any "." or ".."
// component so no entry can escape the tar.gz root on extraction.
func validateBundleName(name string) error {
	if name == "" {
		return errors.New("plugin: empty bundle path")
	}
	if strings.HasPrefix(name, "/") || strings.Contains(name, "\\") {
		return fmt.Errorf("plugin: invalid bundle path %q", name)
	}
	for _, part := range strings.Split(name, "/") {
		if part == ".." || part == "." || part == "" {
			return fmt.Errorf("plugin: bundle path %q escapes the plugin root", name)
		}
	}
	return nil
}

// checksumArtifacts hashes a length-framed, sorted list of artifact names and
// contents so the digest is deterministic and unambiguous.
func checksumArtifacts(files []bundleFile) (string, error) {
	h := sha256.New()
	for _, f := range files {
		data, err := os.ReadFile(f.path)
		if err != nil {
			return "", fmt.Errorf("plugin: hash %s: %w", f.name, err)
		}
		if _, err := fmt.Fprintf(h, "%d:%s\x00", len(f.name), f.name); err != nil {
			return "", err
		}
		if _, err := h.Write(data); err != nil {
			return "", err
		}
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}

// writeBundle writes manifest + artifacts as a reproducible tar.gz. The
// temporary file is renamed into place only after every entry is written.
func writeBundle(outAbs string, manifestData []byte, files []bundleFile) error {
	dir := filepath.Dir(outAbs)
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return err
	}
	tmp, err := os.CreateTemp(dir, ".plugin-bundle-*.tar.gz")
	if err != nil {
		return err
	}
	tmpName := tmp.Name()
	defer os.Remove(tmpName)

	gz := gzip.NewWriter(tmp)
	gz.Header.ModTime = time.Time{} // reproducible gzip header
	tw := tar.NewWriter(gz)
	fixedTime := time.Unix(0, 0).UTC()

	writeEntry := func(name string, data []byte) error {
		hdr := &tar.Header{
			Name:     name,
			Mode:     0o644,
			Size:     int64(len(data)),
			ModTime:  fixedTime,
			Typeflag: tar.TypeReg,
			Format:   tar.FormatPAX,
		}
		if err := tw.WriteHeader(hdr); err != nil {
			return err
		}
		_, err := tw.Write(data)
		return err
	}

	// Manifest first, then artifacts in sorted order: one stable layout.
	err = writeEntry(pluginManifestName, manifestData)
	for i := 0; err == nil && i < len(files); i++ {
		var data []byte
		data, err = os.ReadFile(files[i].path)
		if err == nil {
			err = writeEntry(files[i].name, data)
		}
	}
	if err == nil {
		err = errors.Join(tw.Close(), gz.Close(), tmp.Close())
		if err == nil {
			err = os.Rename(tmpName, outAbs)
		}
	}
	if err != nil {
		_ = tw.Close()
		_ = gz.Close()
		_ = tmp.Close()
		return fmt.Errorf("plugin: write bundle: %w", err)
	}
	return nil
}

func runPackage(args []string, out, errOut io.Writer) int {
	positional, flags := normalizeValueArgs(args, packageValue)
	fs := flagSet("package", errOut)
	outPath := fs.String("out", "", "output tar.gz bundle path (default: <id>.tar.gz in the plugin directory)")
	if err := fs.Parse(flags); err != nil {
		return 2
	}
	if len(positional) != 1 {
		fmt.Fprintln(errOut, "usage: plugin package <dir> [--out <file>]")
		return 2
	}
	bundle, err := Package(positional[0], *outPath)
	if err != nil {
		fmt.Fprintln(errOut, err)
		return 1
	}
	fmt.Fprintf(out, "packaged plugin bundle: %s\n", bundle)
	return 0
}
