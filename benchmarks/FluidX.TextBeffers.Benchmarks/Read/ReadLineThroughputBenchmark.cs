using System;
using BenchmarkDotNet.Attributes;
using FluidX.TextBuffers;
using FluidX.TextBeffers.Benchmarks.Utils;

namespace FluidX.TextBeffers.Benchmarks.Read;

public enum ReadPreparation { None, SingleEdit, RandomEdits, SequentialEdits }

// Complement the original cold, single-invocation ReadLineBenchmark. GlobalSetup
// allows BDN to calibrate many reads per iteration, above the timer/noise floor.
[MemoryDiagnoser]
public class ReadLineThroughputBenchmark
{
    [ParamsAllValues]
    public TestFileType FileType { get; set; }

    [ParamsAllValues]
    public BufferImplementation Implementation { get; set; }

    [ParamsAllValues]
    public ReadPreparation Preparation { get; set; }

    private ITextBuffer _buffer = null!;
    private int _lineIndex;

    [GlobalSetup]
    public void Setup()
    {
        string text = TestFileHelper.LoadFileText(FileType);
        _buffer = BufferFactory.CreateBuffer(Implementation, text);
        var random = new Random(42);
        if (Preparation == ReadPreparation.SingleEdit)
            EditHelper.ApplyEdits(_buffer, EditHelper.PreGenerateRandomEdits(text, random, 1));
        else if (Preparation == ReadPreparation.RandomEdits)
            EditHelper.ApplyEdits(_buffer, EditHelper.PreGenerateRandomEdits(text, random, 1000));
        else if (Preparation == ReadPreparation.SequentialEdits)
            EditHelper.ApplyEdits(_buffer, EditHelper.PreGenerateSequentialEdits(text, random, 1000));
        _lineIndex = _buffer.LineCount / 2;
    }

    [Benchmark]
    public string ReadLineSteadyState() => _buffer.GetLineContent(_lineIndex);
}
