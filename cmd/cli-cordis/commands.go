// Command package for cli-cordis. Build and VersionCommand are pure
// constructors; the writer-carrying variants keep tests free of global stdout.
package main

import (
	"context"
	"fmt"
	"io"
	"os"

	"dynamic-runtime/extensions/cli"
)

// versionValue is the build version reported by the CLI. A real build can
// override it with -ldflags -X main.versionValue=...
var versionValue = "dev"

type runCommand struct {
	ver string
	out io.Writer
}

func (c *runCommand) Name() string      { return "run" }
func (c *runCommand) Aliases() []string { return []string{"r"} }
func (c *runCommand) Execute(_ context.Context, _ []string, opts map[string]string) error {
	if v, ok := opts["version"]; ok && v != "false" && v != "" {
		printVersion(c.out, c.ver)
	}
	return nil
}

type versionCommand struct {
	ver string
	out io.Writer
}

func (c *versionCommand) Name() string      { return "version" }
func (c *versionCommand) Aliases() []string { return []string{"v"} }
func (c *versionCommand) Execute(context.Context, []string, map[string]string) error {
	printVersion(c.out, c.ver)
	return nil
}

func printVersion(out io.Writer, ver string) {
	if out == nil {
		out = os.Stdout
	}
	fmt.Fprintf(out, "cli-cordis %s\n", ver)
}

func buildWithWriter(ver string, out io.Writer) cli.Command {
	return &runCommand{ver: ver, out: out}
}

func versionWithWriter(ver string, out io.Writer) cli.Command {
	return &versionCommand{ver: ver, out: out}
}

// Build returns the "run" command. Its Execute prints the version when the
// parsed --version flag is present.
func Build(ver string) cli.Command { return buildWithWriter(ver, os.Stdout) }

// VersionCommand returns the "version" command that main dispatches for the
// --version flag.
func VersionCommand(ver string) cli.Command { return versionWithWriter(ver, os.Stdout) }
