using System;
using FluidX.TextBuffers;

namespace FluidX.TextBeffers.Benchmarks.Utils;

public struct PreGeneratedEdit
{
    public int InsertOffset;
    public int DeleteLength;
    public string Text;
}

public static class EditHelper
{
    private const int DefaultEditCount = 1000;
    private const int DefaultEditLength = 10;

    public static void ApplyEdits(ITextBuffer buffer, PreGeneratedEdit[] edits)
    {
        foreach (var edit in edits)
        {
            var startPos = buffer.GetPositionAt(edit.InsertOffset);
            var endPos = buffer.GetPositionAt(edit.InsertOffset + edit.DeleteLength);
            var range = new TextRange(startPos, endPos);
            var replacement = new TextReplacement(range, edit.Text);
            buffer.ApplyEdits([replacement], false);
        }
    }

    public static PreGeneratedEdit[] PreGenerateRandomEdits(string text, Random random, int editCount = DefaultEditCount, int editLength = DefaultEditLength)
    {
        int docLength = text.Length;
        var edits = new PreGeneratedEdit[editCount];

        for (int i = 0; i < editCount; i++)
        {
            int maxOffset = Math.Max(0, docLength - editLength);
            int offset = random.Next(0, maxOffset + 1);
            int deleteLen = Math.Min(editLength, docLength - offset);
            string insertText = GenerateRandomString(random, deleteLen);

            edits[i] = new PreGeneratedEdit
            {
                InsertOffset = offset,
                DeleteLength = deleteLen,
                Text = insertText
            };
        }
        return edits;
    }

    public static PreGeneratedEdit[] PreGenerateSequentialEdits(string text, Random random, int editCount = DefaultEditCount)
    {
        int offset = text.Length / 2;
        var edits = new PreGeneratedEdit[editCount];

        for (int i = 0; i < editCount; i++)
        {
            char c = (char)random.Next(32, 127);

            edits[i] = new PreGeneratedEdit
            {
                InsertOffset = offset,
                DeleteLength = 0,
                Text = c.ToString()
            };
            offset++; // Move the offset forward for the next character insertion
        }
        return edits;
    }

    public static string GenerateRandomString(Random random, int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = (char)random.Next(32, 127);
        return new string(chars);
    }
}
