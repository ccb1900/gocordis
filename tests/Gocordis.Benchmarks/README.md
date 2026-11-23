# Gocordis.Benchmarks

Control-plane benchmarks for declaration reconciliation. These are NOT product
tests and are not part of `dotnet test Gocordis.sln`; run them explicitly:

```bash
dotnet run --project tests/Gocordis.Benchmarks -c Release -- \
  --filter '*PluginRegistryApplyBenchmarks*' --job Short
dotnet run --project tests/Gocordis.Benchmarks -c Release -- \
  --filter '*SteadyApply*' --job Short
```

## First measurements (this machine, Short/Dry jobs)

| scenario | 100 | 1,000 | 10,000 |
| --- | --- | --- | --- |
| ColdApply before batch snapshot | ~4.7 ms | ~306 ms | ~39.8 s / ~55.8 GB allocated |
| ColdApply after batch snapshot | ~3.1 ms | ~220 ms | ~27.4 s / ~44.4 GB allocated |
| SteadyApply (unchanged declarations, existing fibers) | ~2.6 ms | ~4.3 ms | ~17.3 ms / ~9.6 MB |

Interpretation: the **full dependency sort is not the bottleneck** — an unchanged
10k-plugin apply is ~17 ms. Batch snapshot publication cut cold 10k from ~39.8 s
to ~27.4 s, but the cold path is still dominated by work that runs once per load:
`FindCycle()` walks all fibers on every `HandleLoad`, and `Reconcile()` traverses
all fibers after every command. Those are the next measurement-driven targets.
Do not remove correctness checks to chase the number — batch the checks at the
declaration-boundary instead.
