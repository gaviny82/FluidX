using FluidX.TextBuffers.LineArray;
using FluidX.TextBuffers.PieceTree;

namespace FluidX.TextBuffers.Tests.Infrastructure;

internal static class TextBufferFactory
{
    public static ITextBuffer PersistentPieceTree(string text) => new FluidX.TextBuffers.PersistentPieceTree.PersistentPieceTreeTextBuffer(text);
    public static ITextBuffer LineArray(string text) => new LineArrayTextBuffer(text);
    public static ITextBuffer PieceTree(string text) => PieceTreeTextBuffer.Create(text);
}

internal sealed class ReferenceText
{
    internal readonly record struct Line(int Start, string Content, string Eol);

    public string Text { get; }
    public IReadOnlyList<Line> Lines { get; }

    public ReferenceText(string text)
    {
        Text = text;
        var lines = new List<Line>();
        int start = 0;
        for (int i = 0; i < text.Length;)
        {
            if (text[i] is not ('\r' or '\n'))
            {
                i++;
                continue;
            }

            int eolLength = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            lines.Add(new Line(start, text[start..i], text.Substring(i, eolLength)));
            i += eolLength;
            start = i;
        }
        lines.Add(new Line(start, text[start..], ""));
        Lines = lines;
    }

    public TextPosition PositionAt(int offset)
    {
        for (int line = 0; line < Lines.Count; line++)
        {
            Line value = Lines[line];
            int end = value.Start + value.Content.Length + value.Eol.Length;
            if (offset < end || line == Lines.Count - 1)
                return new TextPosition(line, offset - value.Start);
        }
        throw new ArgumentOutOfRangeException(nameof(offset));
    }

    public int OffsetAt(TextPosition position) => Lines[position.LineIndex].Start + position.ColumnIndex;

    public string Apply(IReadOnlyList<TextReplacement> replacements)
    {
        var result = new System.Text.StringBuilder();
        int cursor = 0;
        foreach (TextReplacement replacement in replacements)
        {
            int start = OffsetAt(replacement.Range.StartPosition);
            int end = OffsetAt(replacement.Range.EndPosition);
            result.Append(Text, cursor, start - cursor);
            result.Append(replacement.Text);
            cursor = end;
        }
        result.Append(Text, cursor, Text.Length - cursor);
        return result.ToString();
    }
}

internal static class BufferAssertions
{
    public static string ReadAll(IReadOnlyTextBuffer buffer) =>
        buffer.GetTextInRange(buffer.GetRangeAt(0, buffer.Length));

    public static void Matches(string expectedText, IReadOnlyTextBuffer actual)
    {
        var expected = new ReferenceText(expectedText);
        Assert.AreEqual(expectedText.Length, actual.Length);
        Assert.AreEqual(expected.Lines.Count, actual.LineCount);
        Assert.AreEqual(expectedText, ReadAll(actual));
        CollectionAssert.AreEqual(expected.Lines.Select(x => x.Content).ToArray(), actual.GetLinesContent().ToArray());

        for (int line = 0; line < expected.Lines.Count; line++)
        {
            Assert.AreEqual(expected.Lines[line].Content, actual.GetLineContent(line), $"Line {line} content");
            Assert.AreEqual(expected.Lines[line].Content.Length, actual.GetLineLength(line), $"Line {line} length");
            Assert.AreEqual(expected.Lines[line].Eol, actual.GetLineEOL(line), $"Line {line} EOL");
        }

        for (int offset = 0; offset <= expectedText.Length; offset++)
        {
            TextPosition position = expected.PositionAt(offset);
            Assert.AreEqual(position, actual.GetPositionAt(offset), $"Position at offset {offset}");
            Assert.AreEqual(offset, actual.GetOffsetAt(position), $"Offset at position {position}");
            if (offset < expectedText.Length)
            {
                Assert.AreEqual(expectedText[offset], actual.GetChar(offset), $"Character at offset {offset}");
                Assert.AreEqual(expectedText[offset], actual.GetChar(position), $"Character at position {position}");
            }
        }
    }
}
