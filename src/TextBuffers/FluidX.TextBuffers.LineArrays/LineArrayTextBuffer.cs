using System.Runtime.InteropServices;
using System.Text;

namespace FluidX.TextBuffers.LineArray;

/// <summary>
/// A simple text buffer used by tests and benchmarks. The document remains a
/// list of character lists, with each non-final line retaining its original
/// end-of-line characters.
/// </summary>
public class LineArrayTextBuffer : ITextBuffer
{
    private readonly List<List<char>> _lines = [];

    public LineArrayTextBuffer() : this(string.Empty) { }

    public LineArrayTextBuffer(string text)
    {
        _lines.AddRange(SplitTextIntoLines(text.AsSpan()));
    }

    public LineArrayTextBuffer(LineArrayTextBuffer source)
    {
        foreach (List<char> line in source._lines)
            _lines.Add([.. line]);
    }

    public int Length => _lines.Sum(line => line.Count);

    public int LineCount => _lines.Count;

    public event EventHandler? ContentChanged;

    public bool Equals(IReadOnlyTextBuffer? other)
    {
        if (other is not LineArrayTextBuffer buffer || LineCount != buffer.LineCount)
            return false;

        if (ReferenceEquals(this, buffer))
            return true;

        for (int i = 0; i < _lines.Count; i++)
        {
            var currentLineSpan = CollectionsMarshal.AsSpan(_lines[i]);
            var otherLineSpan = CollectionsMarshal.AsSpan(buffer._lines[i]);
            if (!currentLineSpan.SequenceEqual(otherLineSpan))
                return false;
        }

        return true;
    }

    public int GetOffsetAt(TextPosition position)
    {
        int lineIndex = GetLineIndex(position.LineNumber);
        int relativeOffset = position.Column - 1;
        if (relativeOffset < 0 || relativeOffset > _lines[lineIndex].Count)
            throw new ArgumentOutOfRangeException(nameof(position));

        int offset = relativeOffset;
        for (int i = 0; i < lineIndex; i++)
            offset += _lines[i].Count;
        return offset;
    }

    public TextPosition GetPositionAt(int offset)
    {
        if (offset < 0 || offset > Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int lineStart = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            int lineEnd = lineStart + _lines[i].Count;
            if (offset < lineEnd || i == _lines.Count - 1)
                return new TextPosition(i + 1, offset - lineStart + 1);
            lineStart = lineEnd;
        }

        return new TextPosition(1, 1);
    }

    public TextRange GetRangeAt(int offset, int length)
    {
        if (length < 0 || offset < 0 || offset > Length - length)
            throw new ArgumentOutOfRangeException(nameof(length));
        return new TextRange(GetPositionAt(offset), GetPositionAt(offset + length));
    }

    public string GetTextInRange(TextRange range)
    {
        int start = GetOffsetAt(range.StartPosition);
        int length = GetOffsetAt(range.EndPosition) - start;
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(range));
        return GetTextAt(start, length);
    }

    public int GetTextLengthInRange(TextRange range)
        => GetOffsetAt(range.EndPosition) - GetOffsetAt(range.StartPosition);

    public IReadOnlyList<string> GetLinesContent()
    {
        string[] result = new string[_lines.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = GetLineContent(i + 1);
        return result;
    }

    public string GetLineContent(int lineNumber)
    {
        List<char> line = _lines[GetLineIndex(lineNumber)];
        int contentLength = line.Count - GetEOLLength(line);
        return new string(CollectionsMarshal.AsSpan(line)[..contentLength]);
    }

    public string GetLineEOL(int lineNumber)
    {
        List<char> line = _lines[GetLineIndex(lineNumber)];
        int eolLength = GetEOLLength(line);
        return eolLength == 0
            ? string.Empty
            : new string(CollectionsMarshal.AsSpan(line)[^eolLength..]);
    }

    public int GetLineLength(int lineNumber)
    {
        List<char> line = _lines[GetLineIndex(lineNumber)];
        return line.Count - GetEOLLength(line);
    }

    public int GetLineFirstNonWhitespaceColumn(int lineNumber)
    {
        List<char> line = _lines[GetLineIndex(lineNumber)];
        ReadOnlySpan<char> content = CollectionsMarshal.AsSpan(line)[..(line.Count - GetEOLLength(line))];
        for (int i = 0; i < content.Length; i++)
        {
            if (!char.IsWhiteSpace(content[i]))
                return i + 1;
        }
        return 0;
    }

    public int GetLineLastNonWhitespaceColumn(int lineNumber)
    {
        List<char> line = _lines[GetLineIndex(lineNumber)];
        ReadOnlySpan<char> content = CollectionsMarshal.AsSpan(line)[..(line.Count - GetEOLLength(line))];
        for (int i = content.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(content[i]))
                return i + 2;
        }
        return 0;
    }

    public char GetChar(TextPosition position)
    {
        int lineIndex = GetLineIndex(position.LineNumber);
        int relativeOffset = position.Column - 1;
        if (relativeOffset < 0 || relativeOffset >= _lines[lineIndex].Count)
            throw new ArgumentOutOfRangeException(nameof(position));
        return _lines[lineIndex][relativeOffset];
    }

    public char GetChar(int offset)
    {
        if (offset < 0 || offset >= Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int lineStart = 0;
        foreach (List<char> line in _lines)
        {
            if (offset < lineStart + line.Count)
                return line[offset - lineStart];
            lineStart += line.Count;
        }
        throw new ArgumentOutOfRangeException(nameof(offset));
    }

    public void NormalizeEOL(string eol)
    {
        if (eol is not ("\n" or "\r\n"))
            throw new ArgumentException("Invalid EOL value", nameof(eol));

        for (int i = 0; i < _lines.Count - 1; i++)
        {
            List<char> line = _lines[i];
            int eolLength = GetEOLLength(line);
            line.RemoveRange(line.Count - eolLength, eolLength);
            line.AddRange(eol);
        }
    }

    public ITextSnapshot CreateSnapshot(bool preserveBOM)
        => new Snapshot(GetTextAt(0, Length));

    public IReadOnlyList<FindMatch> FindMatchesLineByLine(
        TextRange searchRange,
        SearchData searchData,
        bool captureMatches,
        int limitResultCount)
    {
        List<FindMatch> result = [];
        var searcher = new Searcher(searchData.WordSeparators, searchData.Regex);

        for (int lineNumber = searchRange.StartLineNumber;
            lineNumber <= searchRange.EndLineNumber && result.Count < limitResultCount;
            lineNumber++)
        {
            string line = GetLineContent(lineNumber);
            int start = lineNumber == searchRange.StartLineNumber ? searchRange.StartColumn - 1 : 0;
            int end = lineNumber == searchRange.EndLineNumber ? searchRange.EndColumn - 1 : line.Length;
            start = Math.Min(start, line.Length);
            end = Math.Min(end, line.Length);
            string searchedText = line[start..end];

            searcher.Reset(0);
            System.Text.RegularExpressions.Match? match;
            while ((match = searcher.Next(searchedText)) is not null && result.Count < limitResultCount)
            {
                result.Add(SearchUtils.CreateFindMatch(
                    new TextRange(
                        lineNumber,
                        start + match.Index + 1,
                        lineNumber,
                        start + match.Index + match.Length + 1),
                    [match],
                    captureMatches));
            }
        }

        return result;
    }

    public ApplyEditsResult ApplyEdits(TextReplacement[] replacements, bool computeUndoEdits)
    {
        EditInfo[] edits = replacements
            .Select((replacement, index) =>
            {
                int offset = GetOffsetAt(replacement.Range.StartPosition);
                int length = GetTextLengthInRange(replacement.Range);
                return new EditInfo(index, replacement, offset, length, GetTextAt(offset, length));
            })
            .OrderBy(edit => edit.Offset + edit.Length)
            .ThenBy(edit => edit.SortIndex)
            .ToArray();

        bool hasTouchingRanges = false;
        for (int i = 0; i < edits.Length - 1; i++)
        {
            int end = edits[i].Offset + edits[i].Length;
            if (end > edits[i + 1].Offset)
                throw new ArgumentException("Overlapping ranges are not allowed.", nameof(replacements));
            if (end == edits[i + 1].Offset)
                hasTouchingRanges = true;
        }

        LineArrayTextBuffer? resultingBuffer = computeUndoEdits ? CreateEditedCopy(edits) : null;
        ReverseSingleEditOperation[]? reverseEdits = null;
        if (computeUndoEdits)
        {
            reverseEdits = new ReverseSingleEditOperation[edits.Length];
            int delta = 0;
            for (int i = 0; i < edits.Length; i++)
            {
                EditInfo edit = edits[i];
                int newOffset = edit.Offset + delta;
                reverseEdits[i] = new ReverseSingleEditOperation
                {
                    SortIndex = edit.SortIndex,
                    Range = resultingBuffer!.GetRangeAt(newOffset, edit.Replacement.Text.Length),
                    Text = edit.OldText,
                    TextChange = new TextChange(edit.Offset, edit.OldText, newOffset, edit.Replacement.Text)
                };
                delta += edit.Replacement.Text.Length - edit.Length;
            }
            if (!hasTouchingRanges)
                Array.Sort(reverseEdits, (a, b) => a.SortIndex - b.SortIndex);
        }

        List<InternalModelContentChange> changes = [];
        for (int i = edits.Length - 1; i >= 0; i--)
        {
            EditInfo edit = edits[i];
            if (edit.Length == 0 && edit.Replacement.Text.Length == 0)
                continue;

            ReplaceText(edit.Offset, edit.Length, edit.Replacement.Text);
            changes.Add(new InternalModelContentChange
            {
                SortIndex = edit.SortIndex,
                Range = edit.Replacement.Range,
                RangeOffset = edit.Offset,
                RangeLength = edit.Length,
                Text = edit.Replacement.Text
            });
        }

        if (changes.Count > 0)
            ContentChanged?.Invoke(this, EventArgs.Empty);

        return new ApplyEditsResult { ReverseEdits = reverseEdits, Changes = changes };
    }

    private LineArrayTextBuffer CreateEditedCopy(EditInfo[] edits)
    {
        var result = new LineArrayTextBuffer(this);
        for (int i = edits.Length - 1; i >= 0; i--)
            result.ReplaceText(edits[i].Offset, edits[i].Length, edits[i].Replacement.Text);
        return result;
    }

    private void ReplaceText(int offset, int length, string text)
    {
        BufferLocation start = GetLocation(offset);
        BufferLocation end = GetLocation(offset + length);
        List<char> startLine = _lines[start.LineIndex];
        List<char> endLine = _lines[end.LineIndex];

        var replacementText = new List<char>(start.IndexInLine + text.Length + endLine.Count - end.IndexInLine);
        replacementText.AddRange(CollectionsMarshal.AsSpan(startLine)[..start.IndexInLine]);
        replacementText.AddRange(text);
        replacementText.AddRange(CollectionsMarshal.AsSpan(endLine)[end.IndexInLine..]);

        List<List<char>> replacementLines = SplitTextIntoLines(CollectionsMarshal.AsSpan(replacementText));
        _lines.RemoveRange(start.LineIndex, end.LineIndex - start.LineIndex + 1);
        _lines.InsertRange(start.LineIndex, replacementLines);
    }

    private string GetTextAt(int offset, int length)
    {
        if (length == 0)
            return string.Empty;
        if (offset < 0 || length < 0 || offset > Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        BufferLocation start = GetLocation(offset);
        int remaining = length;
        var result = new StringBuilder(length);
        for (int i = start.LineIndex; i < _lines.Count && remaining > 0; i++)
        {
            ReadOnlySpan<char> line = CollectionsMarshal.AsSpan(_lines[i]);
            int lineOffset = i == start.LineIndex ? start.IndexInLine : 0;
            int count = Math.Min(line.Length - lineOffset, remaining);
            result.Append(line.Slice(lineOffset, count));
            remaining -= count;
        }
        return result.ToString();
    }

    private BufferLocation GetLocation(int offset)
    {
        if (offset < 0 || offset > Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int lineStart = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            int lineEnd = lineStart + _lines[i].Count;
            if (offset < lineEnd || i == _lines.Count - 1)
                return new BufferLocation(i, offset - lineStart);
            lineStart = lineEnd;
        }
        return new BufferLocation(0, 0);
    }

    private int GetLineIndex(int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > _lines.Count)
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        return lineNumber - 1;
    }

    private static List<List<char>> SplitTextIntoLines(ReadOnlySpan<char> text)
    {
        List<List<char>> lines = [];
        int lineStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int eolLength = text[i] switch
            {
                '\r' when i + 1 < text.Length && text[i + 1] == '\n' => 2,
                '\r' or '\n' => 1,
                _ => 0
            };
            if (eolLength == 0)
                continue;

            int lineEnd = i + eolLength;
            lines.Add([.. text[lineStart..lineEnd]]);
            i = lineEnd - 1;
            lineStart = lineEnd;
        }
        lines.Add([.. text[lineStart..]]);
        return lines;
    }

    private static int GetEOLLength(List<char> line)
    {
        if (line.Count == 0)
            return 0;
        if (line[^1] == '\n')
            return line.Count > 1 && line[^2] == '\r' ? 2 : 1;
        return line[^1] == '\r' ? 1 : 0;
    }

    private readonly record struct BufferLocation(int LineIndex, int IndexInLine);

    private readonly record struct EditInfo(
        int SortIndex,
        TextReplacement Replacement,
        int Offset,
        int Length,
        string OldText);

    private sealed class Snapshot(string text) : ITextSnapshot
    {
        private bool _read;

        public string? Read()
        {
            if (_read)
                return null;
            _read = true;
            return text;
        }
    }
}
