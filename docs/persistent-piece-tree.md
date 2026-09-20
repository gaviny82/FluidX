# Persistent piece tree

`FluidX.TextBuffers.PersistentPieceTree.PersistentPieceTreeTextBuffer` implements the complete `ITextBuffer` contract. Reference the new project and construct it with raw UTF-16 text:

```csharp
var buffer = new PersistentPieceTreeTextBuffer("one\r\ntwo");
ITextSnapshot version = buffer.CreateSnapshot();
buffer.ApplyEdits([new(buffer.GetRangeAt(0, 3), "ONE")]);
// version still contains "one\r\ntwo", including its original line structure.
```

The existing mutable implementation remains available. This project does not change the default text-model buffer.

## Origin and adaptations

The functional red-black insertion, fusion, and deletion algorithms in `PieceNode.cs` are ported from Cameron DaCamara's [fredbuf](https://github.com/cdacamar/fredbuf), revision `0fe604ab31cd49f96b23551a8e69a946f8e07d19`. See the author's [editor data structures article](https://cdacamar.github.io/data%20structures/algorithms/benchmarking/text%20editors/c++/editor-data-structures/) and the included [MIT license](../src/TextBuffers/FluidX.TextBuffers.PersistentPieceTree/LICENSE.fredbuf). fredbuf credits Bartosz Milewski's functional trees for insertion and dotnwat/persistent-rbtree for deletion; this port follows its non-experimental deletion algorithm.

The tree is deliberately internal to this project: its measures and offset ordering are specific to text pieces. It has no parent pointers, mutable sentinels, or global storage. Every node is immutable, and rebuilding a path recomputes its length and line-break summary. Unchanged subtrees retain their identity.

FluidX-specific additions include:

- Raw UTF-16 addressing, including individual surrogate code units and the boundary inside CRLF for reads.
- CR, LF, and CRLF recognition across arbitrary piece boundaries. Concatenated summaries subtract one break when a trailing CR meets a leading LF. Prefix queries keep the interior CRLF boundary on the preceding line.
- Full range, line, whitespace, search, normalization, and cross-implementation equality behavior.
- Batch edits resolved against the original version, including tied insertions and touching ranges whose edits change CRLF pairing.
- Immutable snapshots with concurrent reads while the source is edited.

Original text and large insertions retain immutable strings with indexed newline starts. Small edits use fixed 1,024-character append-only chunks with fixed newline-index arrays. Published characters/index entries are never overwritten, and arrays never move. Each piece captures its length and its newline-index search bound; snapshot readers never consult mutable append counters or unpublished cells. Consecutive typing extends a piece by copying one tree path until a chunk fills, without copying its characters.

The current version is an immutable snapshot object published through a volatile reference. `CreateSnapshot()` simply returns that object: O(1) time, no traversal, and **zero allocation**. Snapshotting a snapshot returns itself. Writers require external synchronization; readers that need consistency across multiple calls should capture a snapshot. There is no undo/history list retaining otherwise-unused versions.

## Compute and memory costs

These are algorithmic estimates, not timing claims. Let N be document length, P the current piece count, B the newline-index size of a backing store, M inserted length, D deleted pieces, K returned characters, and S retained versions. Logarithms below include a constant for empty/single-element trees.

| Operation | Persistent piece tree | Comparison with mutable PieceTree |
| --- | --- | --- |
| Load a string | O(N) indexing, O(B) index space; original string shared | Same linear indexing class |
| Snapshot capture | O(1) time, zero allocation | Mutable snapshot clones O(P) nodes |
| Length / line count | O(1) | Same class |
| Character by offset | O(log P), no allocation | Same class |
| Offset / line coordinates | O(log P + log B), no allocation | Tree descent plus indexed piece lookup; no full-document scan |
| Read a range or line | O(log P + log B + K), O(K) output space | Same tree lookup/output-copy class |
| Insert / extend typing | O(log P + log B + M) time; O(log P) new tree nodes | Mutable tree avoids path-copy allocations; both index inserted text |
| Delete across D pieces | O((D + 1) log P + log B) time and transient node allocation | Both implementations remove affected pieces; no copying of retained text |
| Normalize EOL | O(N + P), O(N) new storage | Full rewrite; snapshots keep the old version |
| Read all lines / compare unrelated versions | O(N + LineCount * (log P + log B)) | Per-line traversal; identical roots compare in O(1) |

Ordinary local edits retained with a snapshot per keystroke need roughly O(P_initial + S log P) tree nodes plus shared original/inserted text, newline indexes, and O(S) version objects/references. Mutable snapshots instead require the sum of the piece counts of all captured versions. If fragmentation grows with each edit, that sum can grow quadratically with history length. Multi-piece deletions have the larger allocation bound shown above; intermediate unpublished roots can be collected. These are upper bounds, not exact byte counts.

Typing coalescence matters for both implementations. This implementation stores 5,000 consecutive single-character appends in five pieces. When edits do not coalesce, the persistent tree still bounds each local edit's copied path by O(log P). Each edit chunk reserves about 6 KiB of array payload (2 KiB characters and 4 KiB possible newline positions), plus object overhead. A retained slice can keep its whole original string/index or edit chunk alive. Dropping snapshots permits unreachable paths/storage to be collected; the live buffer also retains its current append chunk until it fills or normalization resets it. Repeated normalization with every version retained necessarily retains rewritten text.

Reads and edits have comparable asymptotic costs to the mutable piece tree, but equal or lower wall-clock cost is **not established**. Path copying adds allocation and GC work; the mutable implementation also has read caches this implementation does not share. The primary advantage is frequent retained snapshots, which move work from O(P) per capture to O(log P) per local edit. Workloads without snapshots may favor the mutable implementation. Fixed chunk size and deletion constants are future benchmark tuning candidates.

## Validation and benchmarks

The shared read, mutation, snapshot, search, and equality suites include the new implementation. Additional tests verify red-black colors/black heights, subtree summaries, historical roots, sharing of untouched nodes/backing storage, zero-allocation captures, coalescing across chunk boundaries, and concurrent reads while the writer appends to the same backing storage. Seeded differential tests compare 6,000 edits against raw strings, preserving snapshots throughout.

Validation commands (single-node MSBuild avoids the host's inaccessible compiler-server pipes):

```powershell
dotnet build tests/FluidX.TextBuffers.Tests/FluidX.TextBuffers.Tests.csproj -c Release -m:1 /nr:false /p:UseSharedCompilation=false
dotnet tests/FluidX.TextBuffers.Tests/bin/Release/net10.0/FluidX.TextBuffers.Tests.dll --no-progress
```

Result: 257 tests passed in Release. The existing PieceTree unused-event compiler warning remains.

All benchmark factories enumerate the new implementation, including the separate file-loading path. `RetainedSnapshotsBenchmark` additionally retains a snapshot after every edit, comparing sequential typing with fragmented prepends, without depending on downloaded text fixtures. MemoryDiagnoser reports allocated bytes, not a measurement of the final retained heap. The initial port was validated without running benchmarks because of the benchmark project's network resource problem. After that was fixed, the full comparison and targeted follow-ups were run; see [measured results and methodology](benchmarks/piece-tree-performance.md). The algorithmic discussion above is not a timing prediction.
