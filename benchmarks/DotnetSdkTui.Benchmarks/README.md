# dsm benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) micro-benchmarks for the CPU-bound hot paths in dsm,
comparing the original implementation (`Baselines.cs`) against the shipped span-based one in a single
run. Each `[GlobalSetup]` asserts the two implementations produce identical results before measuring,
so a regression fails fast rather than silently benchmarking different behaviour.

> Not part of `dotnet-sdk-tui.slnx` — it's an on-demand dev tool, kept out of CI to keep builds lean.

## Run

```bash
# all benchmarks
dotnet run -c Release --project benchmarks/DotnetSdkTui.Benchmarks -- --filter '*'

# one group
dotnet run -c Release --project benchmarks/DotnetSdkTui.Benchmarks -- --filter '*VersionSort*'
```

## What's measured

| Benchmark | Path | Real-world trigger |
|-----------|------|--------------------|
| `VersionSortBenchmarks` | `SdkSearchService.CompareSdkVersions` | sorting search/SDK lists (comparator runs O(n log n) times) |
| `SdkListParseBenchmarks` | `DotnetUpService.ParseDotnetSdkList` | parsing `dotnet --list-sdks` on every refresh |
| `ManagedRootBenchmarks` | `DotnetUpService.IsManagedInstallRoot` | classifying each install as managed/unmanaged |

## Results (baseline → optimized)

Apple M-series, .NET 10, `[ShortRunJob]`. Lower is better; `Ratio`/`Alloc Ratio` are vs baseline.

| Benchmark | Mean (before → after) | Time ratio | Allocated (before → after) | Alloc ratio |
|-----------|----------------------|-----------:|----------------------------|------------:|
| Version sort (~150 versions) | 162.74 µs → **40.79 µs** | **0.25× (4.0× faster)** | 670.57 KB → **1.55 KB** | **0.002× (~430× less)** |
| Parse `--list-sdks` (~16 lines) | 552.9 ns → **449.0 ns** | **0.81× (1.24× faster)** | 4.94 KB → **3.02 KB** | **0.61× (~39% less)** |
| Managed-root classify (6 installs) | 245.0 ns → **84.25 ns** | **0.34× (2.9× faster)** | 2520 B → **240 B** | **0.10× (90% less)** |

### What changed

- **`CompareSdkVersions`** — replaced the `Split('-')` + `Split('.')` + LINQ `Select().ToArray()` parse
  (two `string[]` + one `int[]` per call) with a span walk that allocates nothing. This is the big win
  because it runs as a sort comparator.
- **`ParseDotnetSdkList` / `ParseDotnetRuntimeList`** — iterate with `ReadOnlySpan<char>.EnumerateLines()`
  and slice spans; only the two stored strings per row are materialised (no line array, no intermediate
  trimmed strings).
- **`IsManagedInstallRoot`** — span-based prefix check, dropping the `"<root>/"` probe string allocated
  per candidate root, and a span-returning `NormalizeRoot`.

## Binary size (NativeAOT, `osx-arm64`)

Added AOT feature-switch trims in `DotnetSdkTui.csproj` (stack-trace data, system resource keys,
EventSource/Debugger/MetadataUpdater/HTTP-activity off, reflection-free JSON, identical-method folding):

| | Binary |
|-|-------:|
| Before | 7.53 MB (7,891,488 B) |
| After  | **6.86 MB (7,196,432 B)** |
| Saved  | **0.66 MB (−8.8%)** |

Verified the trimmed binary launches and still fetches release metadata (HTTP + source-gen JSON work
under AOT with reflection disabled).
