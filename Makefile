# Reproducible verification (Phase 1). Usage:
#   make verify            # fmt + vet + test + race (fresh)
# Override GO with the pinned 1.26.0 binary and GOCACHE with an isolated cache:
#   make verify GO=/path/to/go1.26.0 GOCACHE=/tmp/gocache
GO ?= go
GOCACHE ?=

verify: fmt-check vet test race

fmt-check:
	gofmt -l $$(find . -name '*.go' -not -path './vendor/*')

vet:
	$(GO) vet ./...

test:
	$(GO) test -count=1 ./...

race:
	$(GO) test -race -count=1 ./...

.PHONY: verify fmt-check vet test race
