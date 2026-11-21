# Reproducible verification (Phase 1). Usage:
#   make verify            # fmt + vet + test + race (fresh)
# Override GO with the pinned 1.26.0 binary and GOCACHE with an isolated cache:
#   make verify GO=/path/to/go1.26.0 GOCACHE=/tmp/gocache
GO ?= go
GOCACHE ?=

# Export GOCACHE only when explicitly overridden — exporting an empty value
# would break Go's default cache resolution.
ifneq ($(strip $(GOCACHE)),)
export GOCACHE
endif

verify: fmt-check vet test race

fmt-check:
	@unformatted=$$(gofmt -l $$(find . -name '*.go' -not -path './vendor/*')); \
	if [ -n "$$unformatted" ]; then \
		echo "gofmt required on:"; echo "$$unformatted"; exit 1; \
	fi

vet:
	$(GO) vet ./...

test:
	$(GO) test -count=1 ./...

race:
	$(GO) test -race -count=1 ./...

.PHONY: verify fmt-check vet test race
