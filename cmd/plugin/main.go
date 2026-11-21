// Package main implements the plugin developer CLI: create, doctor and sandbox.
// It uses only the standard library plus existing gocordis packages.
package main

import (
	"flag"
	"fmt"
	"io"
	"os"
)

func main() {
	os.Exit(run(os.Args[1:], os.Stdout, os.Stderr))
}

func usage(w io.Writer) {
	fmt.Fprintln(w, "usage: plugin <command> [args]")
	fmt.Fprintln(w, "  create <dir> --id <id> --name <name> --backend native|wasm|proc [--force]")
	fmt.Fprintln(w, "  doctor <manifest path>")
	fmt.Fprintln(w, "  sandbox <manifest path>")
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
		if len(args) != 2 {
			fmt.Fprintln(errOut, "usage: plugin sandbox <manifest path>")
			return 2
		}
		return sandbox(args[1], out)
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
	fs := flag.NewFlagSet("create", flag.ContinueOnError)
	fs.SetOutput(errOut)
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
