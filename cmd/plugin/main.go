// Package main implements the plugin developer CLI: create, doctor, sandbox
// and package. It uses only the standard library plus existing gocordis
// packages.
package main

import (
	"flag"
	"fmt"
	"io"
	"os"
	"strings"
)

func main() {
	os.Exit(run(os.Args[1:], os.Stdout, os.Stderr))
}

func usage(w io.Writer) {
	fmt.Fprintln(w, "usage: plugin <command> [args]")
	fmt.Fprintln(w, "  create <dir> --id <id> --name <name> --backend native|wasm|proc [--force]")
	fmt.Fprintln(w, "  doctor <manifest path>")
	fmt.Fprintln(w, "  sandbox <manifest path> [--factory <name>]")
	fmt.Fprintln(w, "  package <dir> [--out <file>]")
}

func run(args []string, out, errOut io.Writer) int {
	if len(args) == 0 {
		usage(errOut)
		return 2
	}
	switch args[0] {
	case "create":
		return runCreate(args[1:], out, errOut)
	case "doctor":
		if len(args) != 2 {
			fmt.Fprintln(errOut, "usage: plugin doctor <manifest path>")
			return 2
		}
		return doctor(args[1], out)
	case "sandbox":
		return runSandbox(args[1:], out, errOut)
	case "package":
		return runPackage(args[1:], out, errOut)
	case "help", "-h", "--help":
		usage(out)
		return 0
	default:
		fmt.Fprintf(errOut, "plugin: unknown command %q\n", args[0])
		usage(errOut)
		return 2
	}
}

func runCreate(args []string, out, errOut io.Writer) int {
	positional, flags := normalizeCreateArgs(args)
	fs := flagSet("create", errOut)
	id := fs.String("id", "", "plugin id (stable, machine-readable)")
	name := fs.String("name", "", "human-readable plugin name")
	backend := fs.String("backend", "native", "loading backend: native, wasm or proc")
	force := fs.Bool("force", false, "overwrite existing scaffold files")
	if err := fs.Parse(flags); err != nil {
		return 2
	}
	if len(positional) != 1 {
		fmt.Fprintln(errOut, "usage: plugin create <dir> --id <id> --name <name> --backend native|wasm|proc [--force]")
		return 2
	}
	dir := positional[0]
	opts := CreateOptions{ID: *id, Name: *name, Backend: *backend, Force: *force}
	if err := Generate(dir, opts); err != nil {
		fmt.Fprintln(errOut, err)
		return 1
	}
	fmt.Fprintf(out, "created plugin project in %s\n", dir)
	return 0
}

// flagSet builds a ContinueOnError FlagSet that writes parse errors to errOut.
func flagSet(name string, errOut io.Writer) *flag.FlagSet {
	fs := flag.NewFlagSet(name, flag.ContinueOnError)
	fs.SetOutput(errOut)
	return fs
}

// normalizeValueArgs lets the positional argument appear before or after the
// flags, since Go's flag package stops parsing at the first non-flag argument.
// valueFlags names the flags that consume a following argument.
func normalizeValueArgs(args []string, valueFlags map[string]bool) (positional, flags []string) {
	for i := 0; i < len(args); i++ {
		a := args[i]
		if !strings.HasPrefix(a, "-") {
			positional = append(positional, a)
			continue
		}
		flags = append(flags, a)
		name := strings.TrimLeft(a, "-")
		if strings.Contains(name, "=") || !valueFlags[name] {
			continue
		}
		if i+1 < len(args) {
			i++
			flags = append(flags, args[i])
		}
	}
	return positional, flags
}
