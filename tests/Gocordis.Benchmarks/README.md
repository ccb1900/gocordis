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
| ColdApply (new registry, all fibers activated) | ~4.7 ms | ~306 ms | ~39.8 s / ~55.8 GB allocated |
| SteadyApply (unchanged declarations, existing fibers) | ~2.6 ms | ~4.3 ms | ~17.3 ms / ~9.6 MB |

Interpretation: the **full dependency sort is not the bottleneck** — an unchanged
10k-plugin apply is ~17 ms. The cold path is dominated by per-command snapshot
publication in Core (`PublishSnapshot` runs after every load command, making cold
startup effectively O(N²) in fiber snapshots). That is the next measurement-driven
target, not `SortByTypedDependencies`.
