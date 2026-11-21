// Package cli is the command-dispatch extension for dynamic-runtime.
//
// It is an implementation-layer capability: a Component provides a Service
// under ServiceKey; independent components register Commands through reversible
// effects. The service owns no lifecycle authority — registration lifetime is
// exactly the activation that installed it (paper §5 reversible effect).
//
// Parsing is a pure function (parseInput); this package never runs external
// processes or executes anything other than a registered Command.Execute.
package cli

import (
	"context"
	"errors"
	"fmt"
	"sort"
	"strings"
	"sync"
	"unicode"

	"dynamic-runtime/runtime"
)

// Command is one dispatchable command. Name may be a space-separated path
// ("git remote add"); Aliases are alternative paths for the same command.
type Command interface {
	Name() string
	Aliases() []string
	Execute(ctx context.Context, args []string, opts map[string]string) error
}

// Service is the reversible command registry provided by Component.
type Service interface {
	// Register adds a command and returns an idempotent disposer that removes
	// ONLY this registration.
	Register(cmd Command) (func() error, error)
	// Execute parses input, dispatches to the longest-matching registered
	// command path (or alias), and returns a *CliSyntaxError for bad syntax,
	// unknown commands and duplicate flags. Pre-supplied args/opts are merged:
	// args precede parsed positionals, parsed flags override opts on conflict.
	// Neither caller-owned slice nor map is mutated. "help" is built in: it
	// succeeds without invoking a Command; call Help for the text.
	Execute(ctx context.Context, input string, args []string, opts map[string]string) error
	// Help returns the command listing, or details for one command path.
	Help(input string) string
}

// ServiceKey is the capability a cli Component provides while Active.
var ServiceKey = runtime.NewKey[Service]("extension.cli.service")

var errServiceClosed = errors.New("cli: service closed")

// CliSyntaxError reports a parse/dispatch syntax failure (unknown command,
// duplicate flag, unknown option, malformed token). Compare with errors.As.
// Its exported fields give callers machine-readable detail: Input is the
// original input, Reason is a stable category, Option is the offending option
// name when applicable, and Detail is a human-readable explanation.
type CliSyntaxError struct {
	Input  string
	Reason string
	Option string
	Detail string
}

func (e *CliSyntaxError) Error() string {
	detail := e.Detail
	if detail == "" {
		detail = e.Reason
	}
	return fmt.Sprintf("cli: %s: %q", detail, e.Input)
}

func syntaxError(input, reason string) error {
	return &CliSyntaxError{Input: input, Reason: reason, Detail: reason}
}

// unknownOptionError carries the offending option name for a command that
// declared no matching option (including a command declaring none at all).
func unknownOptionError(input, option, command string) error {
	detail := "unknown option --" + option
	if command != "" {
		detail += " for command " + command
	}
	return &CliSyntaxError{
		Input: input, Reason: "unknown option", Option: option, Detail: detail,
	}
}

// ---- pure parser ---------------------------------------------------------

type parsedInput struct {
	head  []string          // non-flag tokens before the first flag (command path + trailing words)
	flags map[string]string // parsed flags; boolean presence is "true"
	args  []string          // non-flag tokens after the first flag
}

// tokenize is a quote-aware replacement for strings.Fields. Both '"' and '\”
// group whitespace into a single token; the quoting characters are removed.
//
// Backslash handling is deliberately context-sensitive (docs/r1.md §4.1):
//   - Outside quotes a backslash is LITERAL (Windows paths such as
//     C:\Program\Files\App must not be mangled). It escapes the next rune only
//     when that rune is whitespace or a quote, preserving shell-style `a\ b`.
//   - Inside double quotes the escapes \", \\, \n and \t are decoded; any
//     other \X sequence keeps both characters so quoted Windows paths survive.
//   - Inside single quotes a backslash escapes just the next rune.
//
// Unterminated quotes and a trailing backslash are syntax errors.
func tokenize(input string) ([]string, error) {
	var tokens []string
	var cur strings.Builder
	var quote rune
	started := false
	runes := []rune(input)
	for i := 0; i < len(runes); i++ {
		r := runes[i]
		switch {
		case quote == '"':
			if r == '"' {
				quote = 0
				started = true
				continue
			}
			if r == '\\' {
				if i+1 >= len(runes) {
					return nil, syntaxError(input, "trailing escape")
				}
				i++
				switch n := runes[i]; n {
				case '"':
					cur.WriteByte('"')
				case '\\':
					cur.WriteByte('\\')
				case 'n':
					cur.WriteByte('\n')
				case 't':
					cur.WriteByte('\t')
				case 'r':
					cur.WriteByte('\r')
				default:
					cur.WriteByte('\\')
					cur.WriteRune(n)
				}
				started = true
				continue
			}
			cur.WriteRune(r)
			started = true
		case quote == '\'':
			if r == '\'' {
				quote = 0
				started = true
				continue
			}
			if r == '\\' {
				if i+1 >= len(runes) {
					return nil, syntaxError(input, "trailing escape")
				}
				i++
				cur.WriteRune(runes[i])
				started = true
				continue
			}
			cur.WriteRune(r)
			started = true
		default:
			if r == '"' || r == '\'' {
				quote = r
				started = true
				continue
			}
			if r == '\\' {
				if i+1 >= len(runes) {
					return nil, syntaxError(input, "trailing escape")
				}
				n := runes[i+1]
				if unicode.IsSpace(n) || n == '"' || n == '\'' {
					i++
					cur.WriteRune(n)
					started = true
					continue
				}
				// Literal backslash outside quotes (Windows path separator).
				cur.WriteRune(r)
				started = true
				continue
			}
			if unicode.IsSpace(r) {
				if started {
					tokens = append(tokens, cur.String())
					cur.Reset()
					started = false
				}
				continue
			}
			cur.WriteRune(r)
			started = true
		}
	}
	if quote != 0 {
		return nil, syntaxError(input, "unterminated quote")
	}
	if started {
		tokens = append(tokens, cur.String())
	}
	return tokens, nil
}

// parseInput is pure: no state, no side effects, no external process.
//
// Accepted tokens: --name=value, --name (true), --no-name (false),
// -a (true), -a=value, combined short flags (-ab = -a -b, -ab=42 = -a -b=42),
// quoted/escaped words, and bare positional words. A repeated flag key is a
// syntax error. Words before the first flag form the command path candidate;
// remaining bare words are positional arguments.
func parseInput(input string) (parsedInput, error) {
	tokens, err := tokenize(input)
	if err != nil {
		return parsedInput{}, err
	}
	out := parsedInput{flags: make(map[string]string)}
	addFlag := func(key, value string) error {
		if key == "" {
			return syntaxError(input, "empty flag name")
		}
		if _, dup := out.flags[key]; dup {
			return syntaxError(input, "duplicate flag "+key)
		}
		out.flags[key] = value
		return nil
	}
	for _, tok := range tokens {
		switch {
		case strings.HasPrefix(tok, "--"):
			body := tok[2:]
			key, value, hasValue := strings.Cut(body, "=")
			if !hasValue && strings.HasPrefix(key, "no-") && len(key) > len("no-") {
				key = key[len("no-"):]
				value, hasValue = "false", true
			}
			if !hasValue {
				value = "true"
			}
			if err := addFlag(key, value); err != nil {
				return out, err
			}
		case strings.HasPrefix(tok, "-") && len(tok) > 1:
			key, value, hasValue := strings.Cut(tok[1:], "=")
			if runes := []rune(key); len(runes) > 1 {
				// Combined short flags: every character is a flag; an attached
				// value belongs to the last one (-ab=42 -> -a -b=42).
				for i, k := range runes {
					v := "true"
					if i == len(runes)-1 && hasValue {
						v = value
					}
					if err := addFlag(string(k), v); err != nil {
						return out, err
					}
				}
				continue
			}
			if !hasValue {
				value = "true"
			}
			if err := addFlag(key, value); err != nil {
				return out, err
			}
		default:
			if len(out.flags) == 0 && len(out.args) == 0 {
				out.head = append(out.head, tok)
			} else {
				out.args = append(out.args, tok)
			}
		}
	}
	return out, nil
}

// ---- service -------------------------------------------------------------

type registration struct {
	id    uint64
	cmd   Command
	names []string // first is the canonical Name; the rest are aliases
}

type service struct {
	mu     sync.RWMutex
	nextID uint64
	regs   []*registration
	closed bool
}

// newService builds an empty, open registry. Unexported on purpose: the only
// public way to obtain a Service is Component + ServiceKey. It returns the
// concrete type so the Component can register its Close as the inverse; Close
// is deliberately NOT part of the public Service interface.
func newService() *service { return &service{} }

var _ Service = (*service)(nil)

func canonical(s string) string { return strings.Join(strings.Fields(s), " ") }

func (s *service) Register(cmd Command) (func() error, error) {
	if cmd == nil {
		return nil, errors.New("cli: nil command")
	}
	name := canonical(cmd.Name())
	if name == "" {
		return nil, errors.New("cli: empty command name")
	}
	names := []string{name}
	for _, alias := range cmd.Aliases() {
		if a := canonical(alias); a != "" {
			names = append(names, a)
		}
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return nil, errServiceClosed
	}
	for _, n := range names {
		if n == "help" {
			return nil, fmt.Errorf("cli: reserved command name %q", n)
		}
		for _, reg := range s.regs {
			for _, existing := range reg.names {
				if existing == n {
					return nil, fmt.Errorf("cli: duplicate command name or alias %q", n)
				}
			}
		}
	}
	s.nextID++
	id := s.nextID
	reg := &registration{id: id, cmd: cmd, names: names}
	s.regs = append(s.regs, reg)
	return func() error {
		s.mu.Lock()
		defer s.mu.Unlock()
		for i, existing := range s.regs {
			if existing.id == id {
				s.regs = append(s.regs[:i], s.regs[i+1:]...)
				break
			}
		}
		return nil
	}, nil
}

// matchHead matches a (possibly quoted) command path against a registered
// name. It returns how many HEAD TOKENS were consumed, positional words left
// over from a token that completed the match mid-token, and whether it matched.
// Quoted positional tokens stay intact as single arguments.
func matchHead(head []string, name string) (int, []string, bool) {
	words := strings.Fields(name)
	wi := 0
	for ti, tok := range head {
		tokWords := strings.Fields(tok)
		for j, w := range tokWords {
			if wi >= len(words) {
				return ti + 1, tokWords[j:], true
			}
			if w != words[wi] {
				return 0, nil, false
			}
			wi++
		}
		if wi == len(words) {
			return ti + 1, nil, true
		}
	}
	return 0, nil, false
}

// validateOptions rejects options a command does not declare. A command may
// optionally declare accepted names via DeclaredOptions() or Options(); a
// declaration of none makes every supplied option unknown. Commands without
// either method accept options as before (additive, backward compatible).
func validateOptions(input string, cmd Command, flags map[string]string) error {
	if len(flags) == 0 {
		return nil
	}
	var declared []string
	knowsOptions := false
	switch c := cmd.(type) {
	case interface{ DeclaredOptions() []string }:
		declared, knowsOptions = c.DeclaredOptions(), true
	case interface{ Options() []string }:
		declared, knowsOptions = c.Options(), true
	}
	if !knowsOptions {
		return nil
	}
	allowed := make(map[string]struct{}, len(declared))
	for _, name := range declared {
		if name = strings.TrimLeft(name, "-"); name != "" {
			allowed[name] = struct{}{}
		}
	}
	names := make([]string, 0, len(flags))
	for name := range flags {
		names = append(names, name)
	}
	sort.Strings(names) // deterministic error when several options are unknown
	for _, name := range names {
		if _, ok := allowed[name]; !ok {
			return unknownOptionError(input, name, cmd.Name())
		}
	}
	return nil
}

func (s *service) Execute(ctx context.Context, input string, args []string, opts map[string]string) error {
	parsed, err := parseInput(input)
	if err != nil {
		return err
	}
	head := parsed.head

	s.mu.RLock()
	if s.closed {
		s.mu.RUnlock()
		return errServiceClosed
	}
	regs := append([]*registration(nil), s.regs...)
	s.mu.RUnlock()

	if len(head) > 0 && head[0] == "help" {
		return nil
	}

	// Longest registered command path (or alias) wins, so "git remote add"
	// takes precedence over "git".
	bestIdx, bestConsumed := -1, 0
	var bestExtra []string
	for i, reg := range regs {
		for _, name := range reg.names {
			consumed, extra, ok := matchHead(head, name)
			if ok && consumed > bestConsumed {
				bestIdx, bestConsumed, bestExtra = i, consumed, extra
			}
		}
	}
	if bestIdx < 0 {
		return syntaxError(input, "unknown command")
	}
	if err := validateOptions(input, regs[bestIdx].cmd, parsed.flags); err != nil {
		return err
	}

	finalArgs := append([]string(nil), args...)
	finalArgs = append(finalArgs, bestExtra...)
	finalArgs = append(finalArgs, head[bestConsumed:]...)
	finalArgs = append(finalArgs, parsed.args...)
	finalOpts := make(map[string]string, len(opts)+len(parsed.flags))
	for k, v := range opts {
		finalOpts[k] = v
	}
	for k, v := range parsed.flags {
		finalOpts[k] = v
	}
	return regs[bestIdx].cmd.Execute(ctx, finalArgs, finalOpts)
}

func (s *service) Help(input string) string {
	s.mu.RLock()
	defer s.mu.RUnlock()

	var b strings.Builder
	b.WriteString("Available commands:\n")
	b.WriteString("  help                     list commands (or describe one)\n")
	write := func(n []string) {
		b.WriteString("  " + n[0])
		if len(n) > 1 {
			b.WriteString(" (aliases: " + strings.Join(n[1:], ", ") + ")")
		}
		b.WriteString("\n")
	}
	if target := canonical(input); target != "" && target != "help" {
		for _, reg := range s.regs {
			for _, n := range reg.names {
				if n == target {
					write(reg.names)
					return b.String()
				}
			}
		}
		b.WriteString("  unknown command: " + target + "\n")
		return b.String()
	}
	for _, reg := range s.regs {
		write(reg.names)
	}
	return b.String()
}

func (s *service) Close() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return nil
	}
	s.closed = true
	s.regs = nil
	return nil
}

// ---- Component -----------------------------------------------------------

// Component provides one Service under ServiceKey. It holds no configuration:
// commands are registered by consuming components through reversible effects.
type Component struct{}

func (c *Component) Name() string                 { return "extension.cli" }
func (c *Component) Inject() []runtime.Dependency { return nil }
func (c *Component) Provide() []runtime.Capability {
	return []runtime.Capability{ServiceKey.Capability()}
}

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc := newService()
	// Inverse registered before the provider so LIFO unwind withdraws the
	// capability first and closes the registry last.
	if err := ctx.Effect(func() (func() error, error) {
		return func() error { return svc.Close() }, nil
	}); err != nil {
		return nil, err
	}
	if err := runtime.Provide(ctx, ServiceKey, Service(svc)); err != nil {
		return nil, err
	}
	return nil, nil
}
