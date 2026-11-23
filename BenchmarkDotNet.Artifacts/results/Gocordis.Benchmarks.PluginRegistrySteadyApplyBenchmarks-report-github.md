```

BenchmarkDotNet v0.14.0, Ubuntu 26.04 (Resolute Raccoon) WSL
Intel Core i5-14400, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.115
  [Host] : .NET 10.0.15 (10.0.1526.53008), X64 RyuJIT AVX2
  Dry    : .NET 10.0.15 (10.0.1526.53008), X64 RyuJIT AVX2

Job=Dry  IterationCount=1  LaunchCount=1  
RunStrategy=ColdStart  UnrollFactor=1  WarmupCount=1  

```
| Method      | PluginCount | Mean      | Error | Allocated  |
|------------ |------------ |----------:|------:|-----------:|
| **SteadyApply** | **100**         |  **2.588 ms** |    **NA** |  **101.15 KB** |
| **SteadyApply** | **1000**        |  **4.304 ms** |    **NA** |  **972.25 KB** |
| **SteadyApply** | **10000**       | **17.333 ms** |    **NA** | **9566.49 KB** |
