using System;
using System.Collections.Generic;
using System.IO;

namespace FluidX.TextBeffers.Benchmarks.Utils;

public enum TestFileType
{
    CheckerTs,
    SqliteC,
    BilingualDictionary
}

public static class TestFileHelper
{
    private static readonly string BaseDir = Path.Combine(AppContext.BaseDirectory, "TestFiles");

    private static readonly Dictionary<TestFileType, string> FileNames = new()
    {
        [TestFileType.CheckerTs] = "checker.ts",
        [TestFileType.SqliteC] = "sqlite3.c",
        [TestFileType.BilingualDictionary] = "Russian-English Bilingual.dic"
    };

    public static string GetFilePath(TestFileType fileType)
    {
        return Path.Combine(BaseDir, FileNames[fileType]);
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
