using System;
using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using FluidX.TextBuffers;
using FluidX.TextBeffers.Benchmarks.Utils;

namespace FluidX.TextBeffers.Benchmarks;

[ShortRunJob]
[MemoryDiagnoser]
public class LoadFileBenchmark
{
    [ParamsSource(nameof(FileTypes))]
    public TestFileType FileType { get; set; }
    public static IEnumerable<TestFileType> FileTypes => BenchmarkParams.FileTypes;

    [ParamsSource(nameof(Impls))]
    public BufferImplementation Implementation { get; set; }
    public static IEnumerable<BufferImplementation> Impls => BenchmarkParams.BufferImpls;

    private byte[] _fileData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fileData = TestFileHelper.LoadFileBytes(FileType);
    }

    [Benchmark]
    public IReadOnlyTextBuffer LoadFile()
    {
        using var stream = new MemoryStream(_fileData);
        using var reader = new StreamReader(stream);
        string text = reader.ReadToEnd();
        return BufferFactory.CreateBuffer(Implementation, text);
    }
}
