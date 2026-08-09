using System.Runtime.InteropServices;
using System.Text;

namespace FluidX.TextBuffers.LineArray;

public class LineArrayTextBuffer : ITextBuffer
{
    private readonly List<List<char>> _lines = [];
    private string _eol;
    private string _bom;

    public LineArrayTextBuffer() : this(string.Empty, "\n", string.Empty) { }

    public LineArrayTextBuffer(string text, string eol = "\n", string bom = "")
    {
        _eol = eol;
        _bom = bom;
        SetText(text);
    }

    public LineArrayTextBuffer(LineArrayTextBuffer source)
    {
        _eol = source._eol;
        _bom = source._bom;
        foreach (var line in source._lines)
            _lines.Add([.. line]);
    }

    private void SetText(string text)
    {
        _lines.Clear();
        if (string.IsNullOrEmpty(text))
        {
            _lines.Add([]);
            return;
        }

        int lineStart = 0; // offset in the string
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                ReadOnlySpan<char> lineText;
                if (i > 0 && text[i - 1] == '\r')
                    lineText = text.AsSpan(lineStart, i - lineStart - 1); // \r\n
                else
                    lineText = text.AsSpan(lineStart, i - lineStart); // \n
                
                _lines.Add([.. lineText]);
                lineStart = i + 1;
            }
        }

        if (lineStart <= text.Length)
            _lines.Add([.. text[lineStart..]]);
    }

    private string _getFullText()
    {
        if (_lines.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < _lines.Count; i++)
        {
            if (i > 0)
                sb.Append(_eol);
            sb.Append(CollectionsMarshal.AsSpan(_lines[i]));
        }
        return sb.ToString();
    }

    public void Dispose()
    {
    }

    public bool Equals(IReadOnlyTextBuffer? other)
    {
        if (other is not LineArrayTextBuffer lineArrayTextBuffer)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (_lines.Count != lineArrayTextBuffer._lines.Count)
            return false;

        for (int i = 0; i < _lines.Count; i++)
        {
            ReadOnlySpan<char> thisLineSpan = CollectionsMarshal.AsSpan(_lines[i]);
            ReadOnlySpan<char> otherLineSpan = CollectionsMarshal.AsSpan(lineArrayTextBuffer._lines[i]);

            if (!thisLineSpan.SequenceEqual(otherLineSpan))
                return false;
        }

        return true;
    }

    #region IReadOnlyTextBuffer Members

    public string BOM => _bom;

    public bool MightContainRTL => false;

    public bool MightContainUnusualLineTerminators => false;

    public bool MightContainNonBasicASCII => false;

    public int Length
    {
        get
        {
            if (_lines.Count == 0) return 0;
            if (_lines.Count == 1) return _lines[0].Count;

            int total = 0;
            int eolLen = _eol.Length;
            int lastIndex = _lines.Count - 1;
            for (int i = 0; i < lastIndex; i++)
                total += _lines[i].Count + eolLen;
            total += _lines[lastIndex].Count;
            return total;
        }
    }

    public int LineCount => _lines.Count;

    public event EventHandler? OnDpiChangeContent;

    public ITextSnapshot CreateSnapshot(bool preserveBOM)
    {
        var copy = new LineArrayTextBuffer(this);
        return new Snapshot(copy, preserveBOM ? _bom : string.Empty);
    }

    public int GetCharacterCountInRange(TextRange range, EndOfLinePreference eol)
    {
        return GetValueLengthInRange(range, eol);
    }

    public char GetCharCode(int offset)
    {
        int currentOffset = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            int lineLen = _lines[i].Count;
            if (currentOffset + lineLen > offset)
                return _lines[i][offset - currentOffset];

            currentOffset += lineLen + _eol.Length;
        }
        return '\0';
    }

    public char GetLineCharCode(int lineNumber, int index)
    {
        return _lines[lineNumber][index];
    }

    public string GetLineContent(int lineNumber)
    {
        return new string(CollectionsMarshal.AsSpan(_lines[lineNumber]));
    }

    public int GetLineFirstNonWhitespaceColumn(int lineNumber)
    {
        var line = CollectionsMarshal.AsSpan(_lines[lineNumber]);
        for (int i = 0; i < line.Length; i++)
        {
            if (!char.IsWhiteSpace(line[i]))
                return i + 1;
        }
        return 0;
    }

    public int GetLineLastNonWhitespaceColumn(int lineNumber)
    {
        var line = CollectionsMarshal.AsSpan(_lines[lineNumber]);
        for (int i = line.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(line[i]))
                return i + 2;
        }
        return 0;
    }

    public int GetLineLength(int lineNumber)
    {
        return _lines[lineNumber].Count;
    }

    public int GetLineMaxColumn(int lineNumber)
    {
        return GetLineLength(lineNumber) + 1;
    }

    public int GetLineMinColumn(int lineNumber)
    {
        return 1;
    }

    public IReadOnlyList<string> GetLinesContent()
    {
        List<string> lines = [];
        for (int i = 0; i < _lines.Count; i++)
        {
            string line = GetLineContent(i);
            lines.Add(line);
        }
        return lines;
    }

    public int GetOffsetAt(int lineNumber, int column)
    {
        int offset = 0;
        for (int i = 0; i < lineNumber - 1; i++)
        {
            offset += _lines[i].Count;
            if (i < _lines.Count - 1)
                offset += _eol.Length;
        }
        offset += column - 1;
        return offset;
    }

    public TextPosition GetPositionAt(int offset)
    {
        int currentOffset = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            int lineLen = _lines[i].Count;
            if (currentOffset + lineLen >= offset)
            {
                int column = offset - currentOffset + 1;
                return new TextPosition(i + 1, column);
            }
            currentOffset += lineLen + _eol.Length;
        }
        return new TextPosition(_lines.Count, _lines[^1].Count + 1);
    }

    public TextRange GetRangeAt(int offset, int length)
    {
        var start = GetPositionAt(offset);
        var end = GetPositionAt(offset + length);
        return new TextRange(start.LineNumber, start.Column, end.LineNumber, end.Column);
    }

    public string GetValueInRange(TextRange range, EndOfLinePreference eol)
    {
        if (range.IsEmpty)
            return string.Empty;

        string lineEnding = eol switch
        {
            EndOfLinePreference.LF => "\n",
            EndOfLinePreference.CRLF => "\r\n",
            _ => _eol
        };

        var sb = new StringBuilder();
        for (int line = range.StartLineNumber; line <= range.EndLineNumber; line++)
        {
            int lineIndex = line - 1;
            if (line > range.StartLineNumber)
                sb.Append(lineEnding);

            int startCol = (line == range.StartLineNumber) ? range.StartColumn - 1 : 0;
            int endCol = (line == range.EndLineNumber) ? range.EndColumn - 1 : _lines[lineIndex].Count;
            int len = endCol - startCol;
            if (len > 0)
                sb.Append(CollectionsMarshal.AsSpan(_lines[lineIndex]).Slice(startCol, len));
        }
        return sb.ToString();
    }

    public int GetValueLengthInRange(TextRange range, EndOfLinePreference eol)
    {
        if (range.IsEmpty)
            return 0;

        if (range.StartLineNumber == range.EndLineNumber)
            return range.EndColumn - range.StartColumn;

        int length = 0;
        string desiredEOL = eol switch
        {
            EndOfLinePreference.LF => "\n",
            EndOfLinePreference.CRLF => "\r\n",
            _ => _eol
        };

        for (int line = range.StartLineNumber; line <= range.EndLineNumber; line++)
        {
            int lineIndex = line - 1;
            if (line > range.StartLineNumber)
                length += desiredEOL.Length;

            int startCol = (line == range.StartLineNumber) ? range.StartColumn - 1 : 0;
            int endCol = (line == range.EndLineNumber) ? range.EndColumn - 1 : _lines[lineIndex].Count;
            length += endCol - startCol;
        }
        return length;
    }

    public void ResetMightContainUnusualLineTerminators()
    {
    }

    #endregion

    #region ITextBuffer Members

    public void SetEOL(string eol)
    {
        _eol = eol;
    }

    public string GetEOL()
    {
        return _eol;
    }

    public ApplyEditsResult ApplyEdits(EditOperation[] rawOperations, bool recordTrimAutoWhitespace, bool computeUndoEdits)
    {
        // no-op
        if (rawOperations.Length == 0)
            return new ApplyEditsResult
            {
                ReverseEdits = null,
                Changes = [],
                TrimAutoWhitespaceLineNumbers = null
            };

        var contentChanges = new List<InternalModelContentChange>(rawOperations.Length);

        foreach (var op in rawOperations)
            contentChanges.Add(ApplySingleEdit(op));

        OnDidChangeContent?.Invoke(this, EventArgs.Empty);

        return new ApplyEditsResult
        {
            ReverseEdits = null,
            Changes = contentChanges,
            TrimAutoWhitespaceLineNumbers = null
        };
    }

    private InternalModelContentChange ApplySingleEdit(EditOperation op)
    {
        var range = op.Range;
        string insertText = op.Text ?? string.Empty;

        int startLineIndex = range.StartLineNumber - 1;
        int endLineIndex = range.EndLineNumber - 1;
        int startCol0 = range.StartColumn - 1;
        int endCol0 = range.EndColumn - 1;

        // Compute range offset/length before modification
        int rangeOffset = 0;
        for (int i = 0; i < startLineIndex; i++)
            rangeOffset += _lines[i].Count + _eol.Length;
        rangeOffset += startCol0;

        int rangeLength;
        if (startLineIndex == endLineIndex)
        {
            rangeLength = endCol0 - startCol0;
        }
        else
        {
            rangeLength = _lines[startLineIndex].Count - startCol0 + _eol.Length;
            for (int i = startLineIndex + 1; i < endLineIndex; i++)
                rangeLength += _lines[i].Count + _eol.Length;
            rangeLength += endCol0;
        }

        // Get prefix of start line and suffix of end line
        var startLine = _lines[startLineIndex];
        var endLine = _lines[endLineIndex];
        ReadOnlySpan<char> startSpan = CollectionsMarshal.AsSpan(startLine);
        ReadOnlySpan<char> endSpan = CollectionsMarshal.AsSpan(endLine);

        // Split insert text into lines
        var insertLines = SplitTextIntoLines(insertText);

        // Build replacement lines and splice into _lines
        int deleteCount = endLineIndex - startLineIndex + 1;

        if (insertLines.Count == 1)
        {
            // Single line insert (or empty): prefix + insert + suffix on one line
            _lines[startLineIndex] = [.. startSpan[..startCol0], .. CollectionsMarshal.AsSpan(insertLines[0]), .. endSpan[endCol0..]];
            if (deleteCount > 1)
                _lines.RemoveRange(startLineIndex + 1, deleteCount - 1);
        }
        else
        {
            // Multi-line insert
            var replacementLines = new List<List<char>>(insertLines.Count);

            // First line: prefix + first insert line
            replacementLines.Add([.. startSpan[..startCol0], .. CollectionsMarshal.AsSpan(insertLines[0])]);

            // Middle lines (moved directly, no copy needed)
            for (int i = 1; i < insertLines.Count - 1; i++)
                replacementLines.Add(insertLines[i]);

            // Last line: last insert line + suffix
            replacementLines.Add([.. CollectionsMarshal.AsSpan(insertLines[^1]), .. endSpan[endCol0..]]);

            // Replace the affected lines
            _lines.RemoveRange(startLineIndex, deleteCount);
            _lines.InsertRange(startLineIndex, replacementLines);
        }

        return new InternalModelContentChange
        {
            Range = range,
            RangeOffset = rangeOffset,
            RangeLength = rangeLength,
            Text = insertText,
            ForceMoveMarkers = op.ForceMoveMarkers
        };
    }

    private static List<List<char>> SplitTextIntoLines(string text)
    {
        var lines = new List<List<char>>();
        int lineStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                int lineEnd = (i > 0 && text[i - 1] == '\r') ? i - 1 : i;
                lines.Add([.. text.AsSpan(lineStart, lineEnd - lineStart)]);
                lineStart = i + 1;
            }
        }
        if (lineStart <= text.Length)
            lines.Add([.. text.AsSpan(lineStart)]);
        return lines;
    }

    public IReadOnlyList<FindMatch> FindMatchesLineByLine(TextRange searchRange, SearchData searchData, bool captureMatches, int limitResultCount)
    {
        throw new NotImplementedException();
    }

    public event EventHandler? OnDidChangeContent;

    #endregion

    private sealed class Snapshot : ITextSnapshot
    {
        private readonly LineArrayTextBuffer _copy;
        private readonly string _bom;
        private bool _read;

        public Snapshot(LineArrayTextBuffer copy, string bom)
        {
            _copy = copy;
            _bom = bom;
        }

        public string? Read()
        {
            if (_read) return null;
            _read = true;
            return _bom + _copy._getFullText();
        }
    }
}
