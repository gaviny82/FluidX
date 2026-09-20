# PieceTree versus PersistentPieceTree: measured performance

Measured on 2026-09-20, from feature branch revision `d780117a6026c47ff68a2e59a1399fcf84cc9ef0` plus the benchmark-only changes in this report. Neither text-buffer implementation was changed during measurement.

## Conclusion

PersistentPieceTree delivers the intended constant-time, allocation-free snapshot capture and wins when every edit retains a version. It does **not** yet meet the goal of read/edit performance matching the mutable PieceTree in general. Warmed random replacements are substantially slower and allocate much more; sequential typing is modestly slower but allocates less. Loading is somewhat faster. Repeated reading of the same line exposes the mutable implementation's last-line cache, which the persistent implementation currently lacks.

## Methodology and accuracy

Hardware: Intel Core i7-9700, 8 cores/8 logical processors; Windows 11 25H2; .NET 10.0.12, x64 RyuJIT, concurrent workstation GC; SDK 10.0.401; BenchmarkDotNet 0.15.8; Release builds. BenchmarkDotNet selected its high-performance power plan. Benchmark processes ran sequentially, without a debugger.

The complete existing suite ran with ShortRun for both tree implementations: **92 cases**, including every fixture, edit count and retained-history variant. LineArray was excluded because this comparison concerns the two tree implementations.

Follow-ups:

1. Standard jobs for 34 cases where either side's ShortRun standard deviation exceeded 10% of its mean. Sub-10-ns snapshot returns were excluded from this trigger because their relative noise is not a useful reason for long reruns.
2. Standard jobs for the other 24 edit cases after the first follow-ups exposed a large runtime warmup effect.
3. All 36 edit cases with the standard job and **150 warmup iterations**. Raw samples had shown a sequential-edit workload moving from about 2 ms to about 0.29 ms during measurement. These longer-warmup runs take precedence in the final edit comparison. Ordinary ShortRun variability alone would not have detected that issue reliably.
4. A benchmark-input correction and reruns of affected TypeScript cases, described below.
5. A separate 24-case **steady-state same-line read** ShortRun, using GlobalSetup so BenchmarkDotNet can calibrate many invocations per iteration. The original read benchmarks reset the buffer in IterationSetup and measure only one tiny read per iteration; several remain noisy even with about 100 standard-job samples. Both workloads are retained and labeled separately.

The original single-edit and cold-read cases represent isolated operations immediately after fresh setup, not a calibrated throughput loop. Their remaining variability is reported in the CSVs. Small differences in those cases should not be interpreted as precise rankings. The added read test deliberately measures repeated access to the same line, including cache benefits; it does not measure a tokenizer scanning different lines.

`AllocatedBytes` means total managed allocation per benchmark operation, **not live retained heap size**. Editing batches report allocation/time for the entire batch. Retained-history cases include buffer creation, edits, snapshots, and the returned snapshot array. Capture-only cases exclude their setup edits. Loading includes file decoding; the mutable buffer uses its stream factory and the persistent buffer uses StreamReader plus its string constructor, as configured in the existing benchmark.

### Corrected CRLF inputs

`checker.ts` contains CRLF, unlike the other two fixtures. The original random generator produced **66 invalid CRLF endpoint edits in its 2,000-edit sequence**; the first occurred at edit index 53. Those violate `ITextBuffer.ApplyEdits` and cannot support a valid comparison.

The generator now selects a window with valid endpoints while preserving replacement length; if a requested length cannot fit any valid window, it expands the replacement to include the whole CRLF and generates equal-length replacement text. Sequential generation also avoids a midpoint inside CRLF. Inserted text has no line breaks and replacements preserve length, so valid original boundaries remain valid throughout these generated sequences.

The corrected runs supersede TypeScript random-edit, pre-edited random-edit, random-edited read, and fragmented snapshot results. Initial invalid-input results are preserved only as historical raw data. SQLite/dictionary inputs, initial/single/sequential TypeScript edits, and the retained-history workload were unaffected. Generator tests include empty text, repeated CRLF, mixed line endings, and edit lengths 1–10. **260 tests pass in Release** after the change.

## Results

See the generated summary tables below. Ratios are **PersistentPieceTree / PieceTree**: lower is better for time and allocated bytes. They compare matched fixture/workload pairs, not averages across unlike operations.

The final comparison covers **116 distinct cases / 58 matched pairs**, including the 24 added steady-state read cases. There are 222 archived measurements across the initial run and all follow-ups.

| Workload | Time ratio (persistent / mutable) | Allocation comparison |
| --- | ---: | --- |
| Load/decode file | 0.78–0.91x | Essentially equal |
| 100 random replacements | 3.86–6.13x | 10.20x more |
| 1,000 random replacements | 6.27–7.85x | 13.65–13.66x more |
| 1,000 replacements after 1,000 earlier replacements | 6.86–7.92x | 15.73x more |
| Sequential typing, 100/1,000 edits | 1.04–1.62x | 0.46–0.61x as much |
| Isolated single replacement | 1.07–1.55x | 9.10x more; TypeScript timing remains noisy |
| Capture snapshot | See table below | 0 bytes for persistent |
| Edit and retain every version | 0.015–0.353x | 0.061–0.348x as much |
| Repeated same-line read, warmed | 33–106x | Mutable: 0 B; persistent: 112–528 B |

The original cold-read cases range from 0.77–1.43x in mean time, but most remain too variable to support a general read-speed conclusion. The separate same-line test has much smaller variability (maximum CV about 6.2%) and specifically measures the missing last-line cache. Its absolute times are **1.68–2.53 ns** for PieceTree versus **72.7–193.6 ns** for PersistentPieceTree.

### Representative warmed random-edit batches

Each operation below applies 1,000 replacements. Both sides use the standard job with 150 warmups; the TypeScript row uses corrected CRLF inputs.

| Fixture | PieceTree time | Persistent time | Ratio | PieceTree allocation | Persistent allocation |
| --- | ---: | ---: | ---: | ---: | ---: |
| TypeScript | 0.982 ms | 7.714 ms | 7.85x | 0.969 MB | 13.229 MB |
| SQLite | 1.065 ms | 7.906 ms | 7.42x | 0.969 MB | 13.238 MB |
| Dictionary | 1.290 ms | 8.083 ms | 6.27x | 0.969 MB | 13.238 MB |

Standard deviations for these six measurements are 1.4–5.1% of the mean. This is a clear regression in editing without snapshots, not merely ShortRun noise.

### Snapshot capture versus retained history

Capture-only results across the three files:

| Prior edits | PieceTree capture | Persistent capture | PieceTree allocation | Persistent allocation |
| --- | ---: | ---: | ---: | ---: |
| 0 | 42.9–46.9 ns | About 1 ns | 184 B | 0 B |
| 1,000 | 32.1–37.4 us | About 1 ns | About 96 KB | 0 B |
| 2,000 | 79.8–84.4 us | About 1 ns | About 192 KB | 0 B |

The end-to-end retained-history benchmark uses a 100,000-character initial string. Each edit inserts one character and retains that version. These are total operation times, including all edits and captures:

| Edits / location | PieceTree | Persistent | Speedup | PieceTree allocated | Persistent allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| 100 / prepend (fragmented) | 305.59 us | 88.33 us | 3.46x | 0.540 MB | 0.113 MB |
| 100 / append (coalesced typing) | 196.32 us | 60.51 us | 3.24x | 0.268 MB | 0.036 MB |
| 1,000 / prepend (fragmented) | 53.013 ms | 0.806 ms | 65.74x | 25.190 MB | 1.543 MB |
| 1,000 / append (coalesced typing) | 476.19 us | 168.22 us | 2.83x | 0.869 MB | 0.302 MB |

All retained-history CVs are at most 3.3%. This workload demonstrates the intended benefit while including editing overhead. It does not establish the same net gain for random replacements with retained versions; that exact combined workload is not in this suite. All MB/KB above are decimal units.

[Full per-case tables](piece-tree-detailed-results.md) include the original cold reads, all edit counts, allocations, CVs, and selected jobs.

## Interpretation and next work

- Snapshot capture returns an existing immutable version; its measured sub-nanosecond/nanosecond scale is near the harness floor. Zero allocation and independence from fragmentation are the useful conclusions; exact huge speedup ratios are not.
- Random editing pays for persistent path copying and intermediate trees during piece splitting, deletion, and insertion. The measured allocation increase is consistent with that design. Profiling would be needed to divide time between tree rebuilding, indexing, and GC.
- Sequential edits benefit from append-only storage and piece coalescing, reducing allocations, but still reconstruct paths and recompute metadata.
- The mutable buffer caches the last line's returned string. The persistent buffer recomputes bounds and creates a StringBuilder/string for nonempty line reads, making repeated same-line reads a particularly unfavorable workload. A version-scoped, concurrency-safe cache is a candidate improvement.
- Highest-priority edit improvements are reducing intermediate path copies during replacement and avoiding redundant piece searches/splits. The large random-edit allocation gap makes this more consequential than tuning snapshot capture, which is already essentially free.

## Reproduce

The runner now defaults to ShortRun when no `--job`/`-j` is supplied; explicit `--job Default` selects only the standard job. Hardcoded class-level ShortRun attributes were removed so selecting Default does not silently run two jobs.

```powershell
dotnet build benchmarks/FluidX.TextBeffers.Benchmarks/FluidX.TextBeffers.Benchmarks.csproj -c Release -m:1 /nr:false /p:UseSharedCompilation=false
$exe = 'benchmarks/FluidX.TextBeffers.Benchmarks/bin/Release/net10.0/FluidX.TextBeffers.Benchmarks.dll'
# The new read-throughput cases are also included in this full-suite filter.
dotnet $exe --job Short --filter '*PieceTree*' --exporters json --artifacts BenchmarkDotNet.Artifacts/reproduction-short
dotnet $exe --job Default --warmupCount 150 --filter '*Benchmarks.Edit.*PieceTree*' --exporters json --artifacts BenchmarkDotNet.Artifacts/reproduction-edits
```

Original selected-case lists are saved in `piece-tree-standard-filters.json`, `piece-tree-standard-extra-filters.json`, and `piece-tree-corrected-standard-filters.json`. Full BenchmarkDotNet traces remain under `BenchmarkDotNet.Artifacts/persistent-comparison-*`. Compact original result samples, statistics, environment information and allocations are checked in under [raw](raw/). The named follow-up directories document which results superseded earlier results; no outliers or slow samples were manually removed (BenchmarkDotNet retained its default outlier policy).

Run `./scripts/Compare-PieceTreeBenchmarks.ps1` to regenerate the CSV comparisons. It reads local BenchmarkDotNet JSON exports if available, otherwise the checked-in compact results, applying the documented precedence. [Individual measurements](piece-tree-measurements.csv) include mean, standard deviation, sample count, coefficient of variation and allocation. [Matched comparisons](piece-tree-comparison.csv) include both implementations and their ratios. [Fixture metadata](piece-tree-fixtures.json) records input sizes and CR counts.
