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
            buffer.ApplyEdits([replacement]);
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
            // Make sure the replacement rage end points do not split a CRLF sequence
            int candidate = offset;
            while (SplitsCrlf(text, candidate) || SplitsCrlf(text, candidate + deleteLen))
            {
                candidate = candidate == maxOffset ? 0 : candidate + 1;
                if (candidate == offset) break;
            }
            offset = candidate;
            if (SplitsCrlf(text, offset)) { offset--; deleteLen++; }
            if (SplitsCrlf(text, offset + deleteLen)) deleteLen++;

            // Create edit
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
        if (SplitsCrlf(text, offset)) offset++;
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

    private static bool SplitsCrlf(string text, int offset) =>
        offset > 0 && offset < text.Length && text[offset - 1] == '\r' && text[offset] == '\n';

    public static string GenerateRandomString(Random random, int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = (char)random.Next(32, 127);
        return new string(chars);
    }
}
