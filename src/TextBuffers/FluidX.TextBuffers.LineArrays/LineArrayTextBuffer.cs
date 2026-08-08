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

    private LineArrayTextBuffer(LineArrayTextBuffer source)
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
        for (int i = 0; i < lineNumber; i++)
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
        if (rawOperations.Length == 0)
            return new ApplyEditsResult
            {
                ReverseEdits = null,
                Changes = [],
                TrimAutoWhitespaceLineNumbers = null
            };

        var contentChanges = new List<InternalModelContentChange>(rawOperations.Length);

        var sortedOps = rawOperations
            .Select(op => (op, rangeOffset: GetOffsetAt(op.Range.StartLineNumber, op.Range.StartColumn)))
            .OrderByDescending(x => x.rangeOffset)
            .ToList();

        var sb = new StringBuilder(_getFullText());

        foreach (var (op, rangeOffset) in sortedOps)
        {
            int rangeLength = GetValueLengthInRange(op.Range, EndOfLinePreference.TextDefined);
            string insertText = op.Text ?? string.Empty;

            sb.Remove(rangeOffset, rangeLength);
            if (insertText.Length > 0)
                sb.Insert(rangeOffset, insertText);

            contentChanges.Add(new InternalModelContentChange
            {
                Range = op.Range,
                RangeOffset = rangeOffset,
                RangeLength = rangeLength,
                Text = insertText,
                ForceMoveMarkers = op.ForceMoveMarkers
            });
        }

        SetText(sb.ToString());

        OnDidChangeContent?.Invoke(this, EventArgs.Empty);

        return new ApplyEditsResult
        {
            ReverseEdits = null,
            Changes = contentChanges,
            TrimAutoWhitespaceLineNumbers = null
        };
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
