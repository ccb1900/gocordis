```

BenchmarkDotNet v0.14.0, Ubuntu 26.04 (Resolute Raccoon) WSL
Intel Core i5-14400, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.115
  [Host]   : .NET 10.0.15 (10.0.1526.53008), X64 RyuJIT AVX2
  ShortRun : .NET 10.0.15 (10.0.1526.53008), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | PluginCount | Mean          | Error         | StdDev      | Gen0         | Gen1         | Gen2        | Allocated   |
|------------ |------------ |--------------:|--------------:|------------:|-------------:|-------------:|------------:|------------:|
| **ColdApply**   | **100**         |      **4.730 ms** |      **3.156 ms** |   **0.1730 ms** |     **625.0000** |     **320.3125** |           **-** |     **6.23 MB** |
| SteadyApply | 100         |      4.744 ms |      7.090 ms |   0.3886 ms |     632.8125 |     320.3125 |           - |     6.32 MB |
| **ColdApply**   | **1000**        |    **305.790 ms** |    **109.374 ms** |   **5.9951 ms** |   **56000.0000** |    **1000.0000** |           **-** |   **559.22 MB** |
| SteadyApply | 1000        |    347.965 ms |     52.115 ms |   2.8566 ms |   56000.0000 |    1000.0000 |           - |   560.11 MB |
| **ColdApply**   | **10000**       | **39,751.649 ms** |  **8,314.023 ms** | **455.7197 ms** | **5458000.0000** | **5118000.0000** | **454000.0000** | **55818.13 MB** |
| SteadyApply | 10000       | 39,899.109 ms | 17,094.183 ms | 936.9898 ms | 5462000.0000 | 5117000.0000 | 457000.0000 | 55827.02 MB |
