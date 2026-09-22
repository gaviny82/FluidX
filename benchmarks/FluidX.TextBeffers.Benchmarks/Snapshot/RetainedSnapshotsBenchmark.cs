using BenchmarkDotNet.Attributes;
using FluidX.TextBuffers;
using FluidX.TextBeffers.Benchmarks.Utils;

namespace FluidX.TextBeffers.Benchmarks.Snapshot;

// Includes editing and retaining every version, unlike the capture-only benchmark.
[MemoryDiagnoser]
public class RetainedSnapshotsBenchmark
{
    [ParamsAllValues]
    public BufferImplementation Implementation { get; set; }

    [Params(100, 1000)]
    public int EditCount { get; set; }

    [Params(false, true)]
    public bool Sequential { get; set; }

    private readonly string _text = new('x', 100000);

    [Benchmark]
    public ITextSnapshot[] EditAndRetainEveryVersion()
    {
        ITextBuffer buffer = BufferFactory.CreateBuffer(Implementation, _text);
        var snapshots = new ITextSnapshot[EditCount];
        for (int i = 0; i < EditCount; i++)
        {
            // Sequential typing coalesces pieces; prepending forces fragmentation.
            int offset = Sequential ? buffer.Length : 0;
            buffer.ApplyEdits([new(buffer.GetRangeAt(offset, 0), "x")]);
            snapshots[i] = buffer.CreateSnapshot();
        }
        return snapshots;
    }
}
