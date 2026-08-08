using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FluidX.TextBeffers.Benchmarks.Utils;

public enum TestFileType
{
    CheckerTs,
    SqliteC,
    BilingualDictionary
}

public static class TestFileHelper
{
    public static string BaseDir =>
        Environment.GetEnvironmentVariable("FLUIDX_BENCHMARK_DATA_DIR")
        ?? Environment.CurrentDirectory;

    private static readonly Dictionary<TestFileType, (string url, string filename)> FileMap = new()
    {
        [TestFileType.CheckerTs] = (
            "https://raw.githubusercontent.com/microsoft/TypeScript/main/src/compiler/checker.ts",
            "checker.ts"),
        [TestFileType.SqliteC] = (
            "https://raw.githubusercontent.com/clibs/sqlite/refs/heads/master/sqlite3.c",
            "sqlite3.c"),
        [TestFileType.BilingualDictionary] = (
            "https://raw.githubusercontent.com/titoBouzout/Dictionaries/master/Russian-English%20Bilingual.dic",
            "Russian-English Bilingual.dic")
    };

    public static IEnumerable<(TestFileType Type, string Url, string Filename)> GetFileMap() => FileMap.Select(kv => (kv.Key, kv.Value.url, kv.Value.filename));

    public static string GetFilePath(TestFileType fileType)
    {
        var (_, filename) = FileMap[fileType];
        return Path.Combine(BaseDir, filename);
    }

    public static byte[] LoadFileBytes(TestFileType fileType)
    {
        return File.ReadAllBytes(GetFilePath(fileType));
    }

    public static string LoadFileText(TestFileType fileType)
    {
        return File.ReadAllText(GetFilePath(fileType));
    }

    public static MemoryStream LoadFileStream(TestFileType fileType)
    {
        var bytes = LoadFileBytes(fileType);
        return new MemoryStream(bytes);
    }
}
