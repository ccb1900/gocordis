package main

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"

	"dynamic-runtime/extensions/devtools"
	"dynamic-runtime/extensions/pluginmeta"
)

// codeParseError is a CLI-level diagnostic for input that cannot be decoded.
const codeParseError devtools.Code = "PARSE_ERROR"

func printDiagnostic(w io.Writer, d devtools.Diagnostic) {
	fmt.Fprintln(w, d.String())
}

func detectFormat(path string) (string, error) {
	switch strings.ToLower(filepath.Ext(path)) {
	case ".toml":
		return "toml", nil
	case ".json":
		return "json", nil
	default:
		return "", fmt.Errorf("plugin: unsupported manifest extension %q (want .toml or .json)", filepath.Ext(path))
	}
}

func readManifest(path string) (pluginmeta.Manifest, error) {
	format, err := detectFormat(path)
	if err != nil {
		return pluginmeta.Manifest{}, err
	}
	data, err := os.ReadFile(path)
	if err != nil {
		return pluginmeta.Manifest{}, err
	}
	return pluginmeta.Parse(data, format)
}

// doctor parses a manifest, prints every diagnostic and returns 1 if any has
// error severity (including a parse failure), otherwise 0.
func doctor(path string, w io.Writer) int {
	m, err := readManifest(path)
	if err != nil {
		printDiagnostic(w, devtools.Diagnostic{
			Severity: devtools.SeverityError,
			Code:     codeParseError,
			Message:  err.Error(),
		})
		return 1
	}

	diags := devtools.Diagnose(m)
	var errors, warnings, infos int
	for _, d := range diags {
		printDiagnostic(w, d)
		switch d.Severity {
		case devtools.SeverityError:
			errors++
		case devtools.SeverityWarn:
			warnings++
		case devtools.SeverityInfo:
			infos++
		}
	}
	fmt.Fprintf(w, "%d diagnostics: %d errors, %d warnings, %d info\n", len(diags), errors, warnings, infos)
	if errors > 0 {
		return 1
	}
	return 0
}
