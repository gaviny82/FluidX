using System.IO;
using BenchmarkDotNet.Attributes;
using FluidX.TextBuffers;
using FluidX.TextBeffers.Benchmarks.Utils;

namespace FluidX.TextBeffers.Benchmarks.Load;

[ShortRunJob]
[MemoryDiagnoser]
public class LoadFileBenchmark
{
    [ParamsAllValues]
    public TestFileType FileType { get; set; }

    [ParamsAllValues]
    public BufferImplementation Implementation { get; set; }

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
