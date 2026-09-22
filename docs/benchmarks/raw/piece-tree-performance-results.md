# Raw PieceTree benchmark results

These are the latest BenchmarkDotNet result tables used by [the performance report](../piece-tree-performance.md). Values are reproduced without cross-run aggregation.

## Environment

```text
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core i7-9700 CPU 3.00GHz, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
```

Unless noted otherwise, the job configuration is:

```text
Job=ShortRun  IterationCount=3  LaunchCount=1  WarmupCount=3
```

The edit benchmarks additionally use `InvocationCount=1` and `UnrollFactor=1`.

## LoadFileBenchmark

| Method | FileType | Implementation | Mean | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| LoadFile | CheckerTs | PieceTree | 7.412 ms | 4.475 ms | 0.2453 ms | 1695.3125 | 1656.2500 | 664.0625 | 12.78 MB |
| LoadFile | CheckerTs | PersistentPieceTree | 5.606 ms | 2.781 ms | 0.1525 ms | 1695.3125 | 1656.2500 | 664.0625 | 12.76 MB |

## SingleEditBenchmark

| Method | FileType | Implementation | Mean | Error | StdDev | Allocated |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| ApplySingleEdit | CheckerTs | PieceTree | 19.23 us | 2.107 us | 0.115 us | 960 B |
| ApplySingleEdit | CheckerTs | PersistentPieceTree | 11.23 us | 7.595 us | 0.416 us | 8,744 B |

## RandomEditsBenchmark

| Method | EditCount | FileType | Implementation | Mean | Error | StdDev | Gen0 | Allocated |
| --- | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: |
| ApplyRandomEdits | 100 | CheckerTs | PieceTree | 724.8 us | 1,885.7 us | 103.36 us | - | 91.95 KB |
| ApplyRandomEdits | 100 | CheckerTs | PersistentPieceTree | 1,747.9 us | 590.6 us | 32.37 us | - | 938.75 KB |
| ApplyRandomEdits | 1,000 | CheckerTs | PieceTree | 7,104.9 us | 2,308.0 us | 126.51 us | - | 946.62 KB |
| ApplyRandomEdits | 1,000 | CheckerTs | PersistentPieceTree | 23,692.6 us | 2,599.1 us | 142.46 us | 2000.0000 | 12,926.56 KB |

## RandomEditsAfterRandomEditsBenchmark

| Method | FileType | Implementation | Mean | Error | StdDev | Gen0 | Allocated |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| ApplyEditsAfterEdits | CheckerTs | PieceTree | 7.867 ms | 10.585 ms | 0.5802 ms | - | 948.66 KB |
| ApplyEditsAfterEdits | CheckerTs | PersistentPieceTree | 25.859 ms | 281.656 ms | 15.4385 ms | 2000.0000 | 14,929.91 KB |

## SequentialEditsBenchmark

| Method | EditCount | FileType | Implementation | Mean | Error | StdDev | Allocated |
| --- | ---: | --- | --- | ---: | ---: | ---: | ---: |
| ApplySequentialEdits | 100 | CheckerTs | PieceTree | 199.6 us | 541.6 us | 29.69 us | 42.15 KB |
| ApplySequentialEdits | 100 | CheckerTs | PersistentPieceTree | 205.8 us | 210.2 us | 11.52 us | 26.47 KB |
| ApplySequentialEdits | 1,000 | CheckerTs | PieceTree | 1,786.0 us | 1,197.2 us | 65.62 us | 418.38 KB |
| ApplySequentialEdits | 1,000 | CheckerTs | PersistentPieceTree | 1,965.8 us | 1,161.8 us | 63.68 us | 202.25 KB |

## ReadLineBenchmark

| Method | FileType | Implementation | Preparation | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Allocated | Alloc Ratio |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| SingleLineRead | CheckerTs | PieceTree | None | 2.0830 ns | 0.3822 ns | 0.0209 ns | 1.00 | 0.01 | - | - | NA |
| RepeatedSameLineReads | CheckerTs | PieceTree | None | 0.7672 ns | 0.0699 ns | 0.0038 ns | 0.37 | 0.00 | - | - | NA |
| RandomLineReads | CheckerTs | PieceTree | None | 31.2786 ns | 10.7359 ns | 0.5885 ns | 15.02 | 0.28 | 0.0214 | 134 B | NA |
| RandomRangeReads | CheckerTs | PieceTree | None | 40.7122 ns | 2.8603 ns | 0.1568 ns | 19.55 | 0.18 | 0.0295 | 186 B | NA |
| ContiguousLineReads | CheckerTs | PieceTree | None | 31.2193 ns | 3.8145 ns | 0.2091 ns | 14.99 | 0.16 | 0.0236 | 148 B | NA |
| SingleLineRead | CheckerTs | PieceTree | RandomEdits | 1.9662 ns | 5.3151 ns | 0.2913 ns | 1.01 | 0.18 | - | - | NA |
| RepeatedSameLineReads | CheckerTs | PieceTree | RandomEdits | 0.7866 ns | 0.0599 ns | 0.0033 ns | 0.41 | 0.05 | - | - | NA |
| RandomLineReads | CheckerTs | PieceTree | RandomEdits | 92.4007 ns | 70.2562 ns | 3.8510 ns | 47.64 | 5.90 | 0.0293 | 184 B | NA |
| RandomRangeReads | CheckerTs | PieceTree | RandomEdits | 100.7891 ns | 1.2050 ns | 0.0660 ns | 51.96 | 6.15 | 0.0294 | 186 B | NA |
| ContiguousLineReads | CheckerTs | PieceTree | RandomEdits | 45.3526 ns | 4.1432 ns | 0.2271 ns | 23.38 | 2.77 | 0.0292 | 184 B | NA |
| SingleLineRead | CheckerTs | PieceTree | SequentialEdits | 2.1559 ns | 0.2018 ns | 0.0111 ns | 1.00 | 0.01 | - | - | NA |
| RepeatedSameLineReads | CheckerTs | PieceTree | SequentialEdits | 0.7771 ns | 0.0499 ns | 0.0027 ns | 0.36 | 0.00 | - | - | NA |
| RandomLineReads | CheckerTs | PieceTree | SequentialEdits | 44.1699 ns | 3.9953 ns | 0.2190 ns | 20.49 | 0.13 | 0.0243 | 153 B | NA |
| RandomRangeReads | CheckerTs | PieceTree | SequentialEdits | 55.1443 ns | 36.0658 ns | 1.9769 ns | 25.58 | 0.80 | 0.0295 | 186 B | NA |
| ContiguousLineReads | CheckerTs | PieceTree | SequentialEdits | 37.4255 ns | 16.8875 ns | 0.9257 ns | 17.36 | 0.38 | 0.0236 | 148 B | NA |
| SingleLineRead | CheckerTs | PersistentPieceTree | None | 2.1071 ns | 0.1348 ns | 0.0074 ns | 1.00 | 0.00 | - | - | NA |
| RepeatedSameLineReads | CheckerTs | PersistentPieceTree | None | 1.3630 ns | 0.2337 ns | 0.0128 ns | 0.65 | 0.01 | - | - | NA |
| RandomLineReads | CheckerTs | PersistentPieceTree | None | 87.9402 ns | 10.2550 ns | 0.5621 ns | 41.74 | 0.26 | 0.0278 | 174 B | NA |
| RandomRangeReads | CheckerTs | PersistentPieceTree | None | 102.1297 ns | 1.6710 ns | 0.0916 ns | 48.47 | 0.15 | 0.0192 | 122 B | NA |
| ContiguousLineReads | CheckerTs | PersistentPieceTree | None | 86.0691 ns | 8.6288 ns | 0.4730 ns | 40.85 | 0.23 | 0.0299 | 188 B | NA |
| SingleLineRead | CheckerTs | PersistentPieceTree | RandomEdits | 1.9169 ns | 0.8584 ns | 0.0470 ns | 1.00 | 0.03 | - | - | NA |
| RepeatedSameLineReads | CheckerTs | PersistentPieceTree | RandomEdits | 1.4974 ns | 0.8964 ns | 0.0491 ns | 0.78 | 0.03 | - | - | NA |
| RandomLineReads | CheckerTs | PersistentPieceTree | RandomEdits | 363.8956 ns | 313.6892 ns | 17.1944 ns | 189.91 | 8.75 | 0.0287 | 181 B | NA |
| RandomRangeReads | CheckerTs | PersistentPieceTree | RandomEdits | 419.7279 ns | 32.1537 ns | 1.7625 ns | 219.05 | 4.71 | 0.0189 | 122 B | NA |
| ContiguousLineReads | CheckerTs | PersistentPieceTree | RandomEdits | 180.4627 ns | 18.2806 ns | 1.0020 ns | 94.18 | 2.05 | 0.0305 | 192 B | NA |
| SingleLineRead | CheckerTs | PersistentPieceTree | SequentialEdits | 1.8733 ns | 0.3637 ns | 0.0199 ns | 1.00 | 0.01 | - | - | NA |
| RepeatedSameLineReads | CheckerTs | PersistentPieceTree | SequentialEdits | 1.4028 ns | 0.6610 ns | 0.0362 ns | 0.75 | 0.02 | - | - | NA |
| RandomLineReads | CheckerTs | PersistentPieceTree | SequentialEdits | 108.6589 ns | 51.0807 ns | 2.7999 ns | 58.01 | 1.40 | 0.0278 | 174 B | NA |
| RandomRangeReads | CheckerTs | PersistentPieceTree | SequentialEdits | 126.3248 ns | 61.9842 ns | 3.3976 ns | 67.44 | 1.69 | 0.0192 | 122 B | NA |
| ContiguousLineReads | CheckerTs | PersistentPieceTree | SequentialEdits | 103.0319 ns | 11.8352 ns | 0.6487 ns | 55.01 | 0.59 | 0.0299 | 188 B | NA |

## SnapshotBenchmark

| Method | EditCount | FileType | Implementation | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
| --- | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CreateSnapshot | 0 | CheckerTs | PieceTree | 38.2138 ns | 2.9544 ns | 0.1619 ns | 0.0293 | - | 184 B |
| CreateSnapshot | 0 | CheckerTs | PersistentPieceTree | 0.9783 ns | 0.2314 ns | 0.0127 ns | - | - | - |
| CreateSnapshot | 1,000 | CheckerTs | PieceTree | 30,995.1721 ns | 5,877.8293 ns | 322.1837 ns | 15.2588 | 3.2959 | 96,088 B |
| CreateSnapshot | 1,000 | CheckerTs | PersistentPieceTree | 0.7360 ns | 0.1993 ns | 0.0109 ns | - | - | - |
| CreateSnapshot | 2,000 | CheckerTs | PieceTree | 72,984.0047 ns | 15,446.8647 ns | 846.6948 ns | 30.5176 | 8.5449 | 191,704 B |
| CreateSnapshot | 2,000 | CheckerTs | PersistentPieceTree | 0.7333 ns | 0.0576 ns | 0.0032 ns | - | - | - |

## RetainedSnapshotsBenchmark

This benchmark uses its own synthetic 100,000-character starting document rather than `checker.ts`. `Sequential=False` prepends edits to create fragmentation; `Sequential=True` appends edits to model coalesced typing.

| Method | EditCount | Sequential | Implementation | Mean | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
| --- | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| EditAndRetainEveryVersion | 100 | False | PieceTree | 288.48 us | 103.256 us | 5.660 us | 62.0117 | 62.0117 | 62.0117 | 527.22 KB |
| EditAndRetainEveryVersion | 100 | False | PersistentPieceTree | 106.95 us | 41.041 us | 2.250 us | 18.0664 | 4.3945 | - | 110.76 KB |
| EditAndRetainEveryVersion | 100 | True | PieceTree | 186.81 us | 5.187 us | 0.284 us | 62.2559 | 62.2559 | 62.2559 | 261.87 KB |
| EditAndRetainEveryVersion | 100 | True | PersistentPieceTree | 57.19 us | 3.358 us | 0.184 us | 5.8594 | 0.5493 | - | 36.04 KB |
| EditAndRetainEveryVersion | 1,000 | False | PieceTree | 54,223.40 us | 17,806.078 us | 976.011 us | 4800.0000 | 3700.0000 | 1500.0000 | 24,599.25 KB |
| EditAndRetainEveryVersion | 1,000 | False | PersistentPieceTree | 730.17 us | 131.658 us | 7.217 us | 247.0703 | 214.8438 | - | 1,514.48 KB |
| EditAndRetainEveryVersion | 1,000 | True | PieceTree | 412.46 us | 24.073 us | 1.320 us | 124.5117 | 62.0117 | 62.0117 | 849.04 KB |
| EditAndRetainEveryVersion | 1,000 | True | PersistentPieceTree | 150.85 us | 22.366 us | 1.226 us | 49.3164 | 0.4883 | - | 303.23 KB |
