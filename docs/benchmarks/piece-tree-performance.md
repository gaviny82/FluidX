# PieceTree and PersistentPieceTree performance

## Scope

This report compares the mutable `PieceTree` and immutable-versioned `PersistentPieceTree` using the current benchmark suite. File-backed benchmarks use `checker.ts`. The retained-snapshot benchmark uses its own synthetic 100,000-character document.

All results were measured with BenchmarkDotNet 0.15.8 on an Intel Core i7-9700 (8 cores), Windows 11 25H2, .NET 10.0.12 x64 RyuJIT, and the .NET 10.0.401 SDK. The job is `ShortRun` with three warmups, three measurement iterations, and one launch. Short runs are suitable for broad comparisons, but results with large error intervals should be confirmed with the default job before making small ranking claims.

Managed allocation is allocation per benchmark operation, not retained live-heap size. Batched edit benchmarks report the total time and allocation for the entire batch. Read methods that perform 100 reads use `OperationsPerInvoke = 100`, so their reported time and allocation are normalized to one read.

The complete BenchmarkDotNet tables are in [raw/piece-tree-performance-results.md](raw/piece-tree-performance-results.md).

## Summary

The two trees are in the same practical performance class for loading, isolated edits, sequential typing, and single-line access. The persistent tree is faster to load this fixture, is approximately tied for single-line reads, and is only 3–10% slower for sequential edit batches while allocating about half as much. Its isolated replacement is faster in this run, although it allocates more.

Random edits and non-local reads favor the mutable tree by larger ratios. Persistent random-edit batches are roughly 2.4–3.3 times slower, and random or contiguous reads are roughly 2.3–4.2 times slower. In absolute terms, the slowest measured persistent read is about 420 ns and 1,000 random edits complete in about 24–26 ms. These costs remain small enough for many interactive editor workloads, but they are not throughput parity and deserve attention if profiling shows them on a critical path.

Retained snapshots are the decisive advantage of the persistent design. Capturing a version is an allocation-free root reference independent of tree fragmentation. When every edit retains a version, `PersistentPieceTree` is 2.7–74 times faster and allocates 2.8–16.2 times less memory in the measured workloads. This matches the needs of a `TextModel` whose tokenization, diagnostics, search, parsing, or other background services require stable read-only document views while foreground edits continue.

## Loading and editing

| Workload | PieceTree | PersistentPieceTree | Persistent / mutable | Allocation comparison |
| --- | ---: | ---: | ---: | ---: |
| Load `checker.ts` | 7.412 ms | 5.606 ms | 0.76x | 12.78 MB vs 12.76 MB |
| Single random replacement | 19.23 us | 11.23 us | 0.58x | 960 B vs 8,744 B |
| 100 random replacements | 724.8 us | 1,747.9 us | 2.41x | 91.95 KB vs 938.75 KB |
| 1,000 random replacements | 7.105 ms | 23.693 ms | 3.33x | 946.62 KB vs 12,926.56 KB |
| 1,000 replacements after 1,000 random replacements | 7.867 ms | 25.859 ms | 3.29x | 948.66 KB vs 14,929.91 KB |
| 100 sequential inserts | 199.6 us | 205.8 us | 1.03x | 42.15 KB vs 26.47 KB |
| 1,000 sequential inserts | 1.786 ms | 1.966 ms | 1.10x | 418.38 KB vs 202.25 KB |

Sequential typing is the most representative edit path for ordinary text entry. Here the persistent tree is close in time and better in allocation. Random replacements are more demanding because persistence copies and rebuilds search paths as pieces are split, deleted, or inserted. Even so, the persistent result averages about 17.5 us per edit for the 100-edit batch and 23.7–25.9 us per edit for the fragmented 1,000-edit batches.

The `ApplyEditsAfterEdits` persistent measurement has a wide error interval, so its exact ratio is not precise. Its scale and allocation nevertheless agree with the ordinary 1,000-random-edit result.

## Reads

| Preparation | Pattern | PieceTree | PersistentPieceTree | Persistent / mutable |
| --- | --- | ---: | ---: | ---: |
| None | Single line | 2.083 ns | 2.107 ns | 1.01x |
| None | Repeated same line | 0.767 ns | 1.363 ns | 1.78x |
| None | Random lines | 31.279 ns | 87.940 ns | 2.81x |
| None | Random ranges | 40.712 ns | 102.130 ns | 2.51x |
| None | Contiguous lines | 31.219 ns | 86.069 ns | 2.76x |
| 1,000 random edits | Single line | 1.966 ns | 1.917 ns | 0.98x |
| 1,000 random edits | Repeated same line | 0.787 ns | 1.497 ns | 1.90x |
| 1,000 random edits | Random lines | 92.401 ns | 363.896 ns | 3.94x |
| 1,000 random edits | Random ranges | 100.789 ns | 419.728 ns | 4.16x |
| 1,000 random edits | Contiguous lines | 45.353 ns | 180.463 ns | 3.98x |
| 1,000 sequential edits | Single line | 2.156 ns | 1.873 ns | 0.87x |
| 1,000 sequential edits | Repeated same line | 0.777 ns | 1.403 ns | 1.81x |
| 1,000 sequential edits | Random lines | 44.170 ns | 108.659 ns | 2.46x |
| 1,000 sequential edits | Random ranges | 55.144 ns | 126.325 ns | 2.29x |
| 1,000 sequential edits | Contiguous lines | 37.426 ns | 103.032 ns | 2.75x |

Single-line lookup is effectively tied. The sub-2-ns same-line numbers are close to the measurement floor and mainly demonstrate the benefit of repeatedly accessing cached or already-hot state. The random and contiguous patterns give a more useful view of traversal cost: the persistent tree is slower, especially after fragmentation, but every measured read remains below half a microsecond.

Line reads allocate strings according to the selected line length. Random range reads show an interesting tradeoff: `PersistentPieceTree` allocates 122 B per range versus 186 B for `PieceTree`, despite taking longer. Other random and contiguous line allocations are broadly similar, varying with the selected lines.

## Snapshots and retained versions

Snapshot capture itself is constant-time and allocation-free for `PersistentPieceTree`:

| Prior edits | PieceTree time | Persistent time | PieceTree allocation | Persistent allocation |
| ---: | ---: | ---: | ---: | ---: |
| 0 | 38.214 ns | 0.978 ns | 184 B | 0 B |
| 1,000 | 30.995 us | 0.736 ns | 96,088 B | 0 B |
| 2,000 | 72.984 us | 0.733 ns | 191,704 B | 0 B |

The persistent timings are at the benchmark harness floor; the significant observations are O(1) capture, zero allocation, and no dependence on the number of pieces. The mutable tree must copy its current representation, so both time and allocation grow with fragmentation.

The end-to-end retained-version benchmark includes initial buffer creation, every edit, every snapshot, and the returned snapshot array:

| Edits | Pattern | PieceTree | PersistentPieceTree | Persistent speedup | PieceTree allocation | Persistent allocation |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 100 | Fragmented prepend | 288.48 us | 106.95 us | 2.70x | 527.22 KB | 110.76 KB |
| 100 | Sequential append | 186.81 us | 57.19 us | 3.27x | 261.87 KB | 36.04 KB |
| 1,000 | Fragmented prepend | 54.223 ms | 730.17 us | 74.26x | 24,599.25 KB | 1,514.48 KB |
| 1,000 | Sequential append | 412.46 us | 150.85 us | 2.73x | 849.04 KB | 303.23 KB |

This workload captures the architectural value more directly than a capture-only microbenchmark. A background service can hold a version for as long as needed without blocking foreground mutation or forcing an eager full copy. Multiple services can independently retain the exact document version they started with, which simplifies concurrency and makes results reproducible against a stable text view.

## Assessment

`PersistentPieceTree` is already performant enough to be a credible default for a snapshot-oriented text model:

- Loading, normal typing, and direct line lookup are close to or better than the mutable implementation.
- Non-local reads are slower by a few hundred nanoseconds, not by milliseconds.
- Random editing has a material throughput and allocation penalty, but individual edit cost remains in the tens of microseconds.
- Snapshot capture and retained-version workloads improve by orders of magnitude as fragmentation and version count grow.

The appropriate choice depends on the workload. A single-threaded editor core that rarely retains versions can prefer the mutable tree. A `TextModel` coordinating several asynchronous consumers benefits much more from cheap immutable versions, and the retained-snapshot results outweigh the moderate read/edit overhead for that architecture.

## Future directions

### Improve the current persistent red-black tree

The random-edit and fragmented-read results indicate that node count, pointer chasing, and path copying are the main areas worth profiling. Possible improvements include:

- reducing temporary trees and repeated searches during replace operations;
- combining split, delete, and insert work into one traversal where practical;
- storing or caching version-safe line-location information without introducing mutable cross-version state;
- coalescing adjacent compatible pieces more aggressively;
- using transient ownership during an edit batch so newly created, unshared nodes can be updated in place before publishing the next immutable root.

Transient ownership is especially attractive for `ApplyEdits`: snapshots keep immutable roots, while the active edit transaction can avoid repeatedly copying nodes that were created earlier in the same transaction.

### Persistent B+ tree or wide B-tree

A persistent B+ tree with multiple children per internal node is a strong alternative architecture. Compared with a binary red-black tree, higher fan-out reduces tree height, pointer indirections, metadata checks, and the number of nodes copied on an edit path. Packed child and summary arrays improve cache locality, while leaf nodes can hold several adjacent pieces and scan them cheaply for nearby or contiguous reads.

Snapshots retain the same core advantage: capturing a version stores one immutable root reference, and an edit copies only the root-to-leaf path. With fan-out `B`, that path is O(log_B n), often only a few nodes for editor-sized documents. A B+ layout also makes sequential traversal and range extraction natural because text pieces live together in leaves.

The tradeoffs are larger copied nodes, more complicated split/merge and occupancy rules, and potential wasted capacity. Practical variants could use modest fixed fan-out, relaxed minimum occupancy, compact arrays, and transient mutable builders for edit batches. Benchmarking should compare random edits, sequential typing, random line/range reads, retained versions, and live retained memory—not snapshot capture alone.

## Reproduce

Build once, then run the complete selected benchmark set in one invocation:

```powershell
dotnet build benchmarks/FluidX.TextBeffers.Benchmarks/FluidX.TextBeffers.Benchmarks.csproj -c Release

dotnet run --no-build --project benchmarks/FluidX.TextBeffers.Benchmarks/FluidX.TextBeffers.Benchmarks.csproj -c Release -- --job Short --test-files CheckerTs --buffer-implementations PieceTree,PersistentPieceTree --filter "*"
```

The second command runs the read, edit, load, snapshot, and retained-snapshot benchmarks. `--test-files CheckerTs` applies to file-backed benchmarks; `RetainedSnapshotsBenchmark` uses its built-in synthetic document. Run on an otherwise idle machine without a debugger for comparable results.
