using System;
using BenchmarkDotNet.Attributes;
using FluidX.TextBuffers;
using FluidX.TextBeffers.Benchmarks.Utils;

namespace FluidX.TextBeffers.Benchmarks.Read;

public enum ReadPreparation
{
    None,
    RandomEdits,
    SequentialEdits
}

[MemoryDiagnoser]
public class ReadLineBenchmark
{
    private const int EditCount = 1000;
    private const int ReadCount = 100;
    private const int MaximumRangeLength = 100;

    [ParamsAllValues]
    public TestFileType FileType { get; set; }

    [ParamsAllValues]
    public BufferImplementation Implementation { get; set; }

    [ParamsAllValues]
    public ReadPreparation Preparation { get; set; }

    private ITextBuffer _buffer = null!;
    private int _lineIndex;
    private int[] _randomLineIndices = null!;
    private TextRange[] _randomRanges = null!;
    private int[] _contiguousLineIndices = null!;

    [GlobalSetup]
    public void Setup()
    {
        string text = TestFileHelper.LoadFileText(FileType);
        _buffer = BufferFactory.CreateBuffer(Implementation, text);

        var random = new Random(42);
        if (Preparation == ReadPreparation.RandomEdits)
        {
            EditHelper.ApplyEdits(
                _buffer,
                EditHelper.PreGenerateRandomEdits(text, random, EditCount));
        }
        else if (Preparation == ReadPreparation.SequentialEdits)
        {
            EditHelper.ApplyEdits(
                _buffer,
                EditHelper.PreGenerateSequentialEdits(text, random, EditCount));
        }

        random = new Random(43);
        _lineIndex = _buffer.LineCount / 2;
        _randomLineIndices = CreateRandomLineIndices(random);
        _randomRanges = CreateRandomRanges(random);
        _contiguousLineIndices = CreateContiguousLineIndices();
    }

    private int[] CreateRandomLineIndices(Random random)
    {
        var indices = new int[ReadCount];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = random.Next(_buffer.LineCount);
        return indices;
    }

    private TextRange[] CreateRandomRanges(Random random)
    {
        var ranges = new TextRange[ReadCount];
        for (int i = 0; i < ranges.Length; i++)
        {
            int offset = random.Next(_buffer.Length);
            int maximumLength = Math.Min(MaximumRangeLength, _buffer.Length - offset);
            int length = random.Next(1, maximumLength + 1);
            ranges[i] = _buffer.GetRangeAt(offset, length);
        }
        return ranges;
    }

    private int[] CreateContiguousLineIndices()
    {
        var indices = new int[ReadCount];
        int start = Math.Clamp(
            _lineIndex - ReadCount / 2,
            0,
            Math.Max(0, _buffer.LineCount - ReadCount));

        for (int i = 0; i < indices.Length; i++)
            indices[i] = (start + i) % _buffer.LineCount;
        return indices;
    }

    [Benchmark(Baseline = true)]
    public string SingleLineRead() => _buffer.GetLineContent(_lineIndex);

    [Benchmark(OperationsPerInvoke = ReadCount)]
    public int RepeatedSameLineReads()
    {
        int totalLength = 0;
        for (int i = 0; i < ReadCount; i++)
            totalLength += _buffer.GetLineContent(_lineIndex).Length;
        return totalLength;
    }

    [Benchmark(OperationsPerInvoke = ReadCount)]
    public int RandomLineReads()
    {
        int totalLength = 0;
        foreach (int lineIndex in _randomLineIndices)
            totalLength += _buffer.GetLineContent(lineIndex).Length;
        return totalLength;
    }

    [Benchmark(OperationsPerInvoke = ReadCount)]
    public int RandomRangeReads()
    {
        int totalLength = 0;
        foreach (TextRange range in _randomRanges)
            totalLength += _buffer.GetTextInRange(range).Length;
        return totalLength;
    }

    [Benchmark(OperationsPerInvoke = ReadCount)]
    public int ContiguousLineReads()
    {
        int totalLength = 0;
        foreach (int lineIndex in _contiguousLineIndices)
            totalLength += _buffer.GetLineContent(lineIndex).Length;
        return totalLength;
    }
}
