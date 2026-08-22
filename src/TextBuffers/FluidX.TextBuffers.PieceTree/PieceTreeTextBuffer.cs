using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FluidX.Common.DataStructures.RbTrees;
using FluidX.TextBuffers.PieceTree.Buffers;

namespace FluidX.TextBuffers.PieceTree;

using TreeNode = RedBlackTree<PieceNodeData>.TreeNode;

public class PieceTreeTextBuffer : ITextBuffer
{
    private const int AverageBufferSize = 65535; // 64 * 1024

    internal readonly PieceTree _pieceTree = new();

    protected List<InlineStringBuffer> _buffers = null!; // 0 is change buffer, others are readonly original buffer.
    protected int _lineCount;
    protected int _length;
    protected string _EOL = "\n"; // Either "\r\n" or "\n"
    protected bool _EOLNormalized;

    private BufferCursor _lastChangeBufferPos;
    private PieceTreeSearchCache _searchCache = null!;
    private (int LineNumber, string Value) _lastVisitedLine;

    private readonly string _BOM;
    private bool _mightContainRTL;
    private bool _mightContainUnusualLineTerminators;
    private bool _mightContainNonBasicASCII;

    public PieceTreeTextBuffer(
        IList<InlineStringBuffer> chunks,
        string bom,
        string eol,
        bool containsRTL,
        bool containsUnusualLineTerminators,
        bool isBasicASCII,
        bool eolNormalized)
    {
        _BOM = bom;
        _mightContainNonBasicASCII = !isBasicASCII;
        _mightContainRTL = containsRTL;
        _mightContainUnusualLineTerminators = containsUnusualLineTerminators;
        Create(chunks, eol, eolNormalized);
    }

    private void Create(IList<InlineStringBuffer> chunks, string eol, bool eolNormalized)
    {
        _buffers = [new InlineStringBuffer()];
        _lastChangeBufferPos = new BufferCursor { Line = 0, Column = 0 };
        _lineCount = 1;
        _length = 0;
        _EOL = eol;
        _EOLNormalized = eolNormalized;

        TreeNode? lastNode = null;
        for (int i = 0, len = chunks.Count; i < len; i++)
        {
            if (chunks[i].Text.Length > 0)
            {
                var ithChunkLineStarts = chunks[i].LineStarts;

                var piece = new Piece(
                    i + 1,
                    new BufferCursor { Line = 0, Column = 0 },
                    new BufferCursor
                    {
                        Line = ithChunkLineStarts.Length - 1,
                        Column = chunks[i].Text.Length - ithChunkLineStarts[^1]
                    },
                    ithChunkLineStarts.Length - 1,
                    chunks[i].Text.Length
                );
                _buffers.Add(chunks[i]);
                lastNode = _pieceTree.InsertRight(lastNode, new PieceNodeData(piece));
            }
        }

        _searchCache = new PieceTreeSearchCache(1);
        _lastVisitedLine = (0, "");
        ComputeBufferMetadata();
    }

    private void NormalizeEOL(string eol)
    {
        int averageBufferSize = AverageBufferSize;
        int min = averageBufferSize - averageBufferSize / 3;
        int max = min * 2;

        string tempChunk = "";
        int tempChunkLen = 0;
        List<InlineStringBuffer> chunks = [];

        for (var iterNode = _pieceTree.Root.LeftMost(); iterNode is not null; iterNode = iterNode.Next())
        {
            string str = GetNodeContent(iterNode);
            int len = str.Length;
            if (tempChunkLen <= min || tempChunkLen + len < max)
            {
                tempChunk += str;
                tempChunkLen += len;
                continue;
            }

            // Flush anyways
            string text = StringExtensions.EndOfLinesRegex.Replace(tempChunk, eol);
            chunks.Add(new InlineStringBuffer(text, LineStarts.CreateFast(text)));
            tempChunk = str;
            tempChunkLen = len;
        }

        if (tempChunkLen > 0)
        {
            string text = StringExtensions.EndOfLinesRegex.Replace(tempChunk, eol);
            chunks.Add(new InlineStringBuffer(text, LineStarts.CreateFast(text)));
        }

        Create(chunks, eol, true);
    }

    #region IReadOnlyTextBuffer Members

    public event EventHandler? OnDpiChangeContent;

    public bool MightContainRTL { get => _mightContainRTL; }

    public bool MightContainUnusualLineTerminators { get => _mightContainUnusualLineTerminators; }

    public void ResetMightContainUnusualLineTerminators()
    {
        _mightContainUnusualLineTerminators = false;
    }

    public bool MightContainNonBasicASCII { get => _mightContainNonBasicASCII; }

    public string BOM { get => _BOM; }

    public string GetEOL() => EOL;

    public ITextSnapshot CreateSnapshot(bool preserveBOM)
        => new PieceTreeSnapshot(this, preserveBOM ? _BOM : "");

    public bool Equals(IReadOnlyTextBuffer? other)
    {
        if (other is not PieceTreeTextBuffer otherBuffer)
            return false;

        return Equals(otherBuffer);
    }

    public TextRange GetRangeAt(int start, int length)
    {
        int end = start + length;
        var startPosition = GetPositionAt(start);
        var endPosition = GetPositionAt(end);
        return new TextRange(startPosition.LineNumber, startPosition.Column, endPosition.LineNumber, endPosition.Column);
    }

    public string GetValueInRange(TextRange range, EndOfLinePreference eol = EndOfLinePreference.TextDefined)
    {
        if (range.IsEmpty)
            return "";

        string lineEnding = GetEndOfLine(eol);
        return GetValueInRange(range, lineEnding);
    }

    public int GetValueLengthInRange(TextRange range, EndOfLinePreference eol = EndOfLinePreference.TextDefined)
    {
        if (range.IsEmpty)
            return 0;

        if (range.StartLineNumber == range.EndLineNumber)
        {
            return (range.EndColumn - range.StartColumn);
        }

        int startOffset = GetOffsetAt(range.StartLineNumber, range.StartColumn);
        int endOffset = GetOffsetAt(range.EndLineNumber, range.EndColumn);

        // offsets use the text EOL, so we need to compensate for length differences
        // if the requested EOL doesn't match the text EOL
        int eolOffsetCompensation = 0;
        string desiredEOL = GetEndOfLine(eol);
        string actualEOL = GetEOL();
        if (desiredEOL.Length != actualEOL.Length)
        {
            int delta = desiredEOL.Length - actualEOL.Length;
            int eolCount = range.EndLineNumber - range.StartLineNumber;
            eolOffsetCompensation = delta * eolCount;
        }

        return endOffset - startOffset + eolOffsetCompensation;
    }

    public int GetCharacterCountInRange(TextRange range, EndOfLinePreference eol)
    {
        if (_mightContainNonBasicASCII)
        {
            // we must count by iterating
            int result = 0;

            int fromLineNumber = range.StartLineNumber;
            int toLineNumber = range.EndLineNumber;
            for (int lineNumber = fromLineNumber; lineNumber <= toLineNumber; lineNumber++)
            {
                string lineContent = GetLineContent(lineNumber);
                int fromOffset = (lineNumber == fromLineNumber ? range.StartColumn - 1 : 0);
                int toOffset = (lineNumber == toLineNumber ? range.EndColumn - 1 : lineContent.Length);

                for (int offset = fromOffset; offset < toOffset; offset++)
                {
                    if (char.IsHighSurrogate(lineContent[offset]))
                    {
                        result++;
                        offset++;
                    }
                    else
                    {
                        result++;
                    }
                }
            }

            result += GetEndOfLine(eol).Length * (toLineNumber - fromLineNumber);

            return result;
        }

        return GetValueLengthInRange(range, eol);
    }

    public int GetLineMinColumn(int lineNumber) => 1;

    public int GetLineMaxColumn(int lineNumber) => GetLineLength(lineNumber) + 1;

    public int GetLineFirstNonWhitespaceColumn(int lineNumber)
    {
        int result = GetLineContent(lineNumber).FirstNonWhitespaceIndex();
        if (result == -1)
            return 0;
        return result + 1;
    }

    public int GetLineLastNonWhitespaceColumn(int lineNumber)
    {
        int result = GetLineContent(lineNumber).LastNonWhitespaceIndex();
        if (result == -1)
            return 0;
        return result + 2;
    }

    private string GetEndOfLine(EndOfLinePreference eol) => eol switch
    {
        EndOfLinePreference.LF => "\n",
        EndOfLinePreference.CRLF => "\r\n",
        EndOfLinePreference.TextDefined => GetEOL(),
        _ => throw new Exception("Unknown EOL preference"),
    };

    #endregion

    #region Editing (ITextBuffer Members)

    public event EventHandler? OnDidChangeContent;

    public void SetEOL(string eol) => EOL = eol;

    public ApplyEditsResult ApplyEdits(EditOperation[] rawOperations, bool recordTrimAutoWhitespace, bool computeUndoEdits)
    {
        bool mightContainRTL = _mightContainRTL;
        bool mightContainUnusualLineTerminators = _mightContainUnusualLineTerminators;
        bool mightContainNonBasicASCII = _mightContainNonBasicASCII;
        bool canReduceOperations = true;

        var operations = new ValidatedEditOperation[rawOperations.Length];
        for (int i = 0; i < rawOperations.Length; i++)
        {
            EditOperation op = rawOperations[i];
            if (canReduceOperations && op.IsTracked)
                canReduceOperations = false;

            TextRange validatedRange = op.Range;
            if (!string.IsNullOrEmpty(op.Text))
            {
                bool textMightContainNonBasicASCII = true;
                if (!mightContainNonBasicASCII)
                {
                    textMightContainNonBasicASCII = !op.Text.IsBasicASCII();
                    mightContainNonBasicASCII = textMightContainNonBasicASCII;
                }
                if (!mightContainRTL && textMightContainNonBasicASCII)
                {
                    // check if the new inserted text contains RTL
                    mightContainRTL = op.Text.ContainsRTL();
                }
                if (!mightContainUnusualLineTerminators && textMightContainNonBasicASCII)
                {
                    // check if the new inserted text contains unusual line terminators
                    mightContainUnusualLineTerminators = op.Text.ContainsUnusualLineTerminators();
                }
            }

            string validText = "";
            int eolCount = 0;
            int firstLineLength = 0;
            int lastLineLength = 0;
            if (!string.IsNullOrEmpty(op.Text))
            {
                (eolCount, firstLineLength, lastLineLength, StringEndOfLine strEOL) =
                    EOLCounter.CountEOL(op.Text);

                string bufferEOL = GetEOL();
                StringEndOfLine expectedStrEOL = (bufferEOL == "\r\n" ? StringEndOfLine.CRLF : StringEndOfLine.LF);
                if (strEOL == StringEndOfLine.Unknown || strEOL == expectedStrEOL)
                    validText = op.Text;
                else
                    validText = StringExtensions.EndOfLinesRegex.Replace(op.Text, bufferEOL);
            }
            operations[i] = new ValidatedEditOperation
            {
                SortIndex = i,
                Range = validatedRange,
                RangeOffset = GetOffsetAt(validatedRange.StartLineNumber, validatedRange.StartColumn),
                RangeLength = GetValueLengthInRange(validatedRange),
                Text = validText,
                EOLCount = eolCount,
                FirstLineLength = firstLineLength,
                LastLineLength = lastLineLength,
                ForceMoveMarkers = op.ForceMoveMarkers,
                IsAutoWhitespaceEdit = op.IsAutoWhitespaceEdit
            };
        }

        // Sort operations ascending
        Array.Sort(operations, SortOpsAscending);

        bool hasTouchingRanges = false;
        for (int i = 0, count = operations.Length - 1; i < count; i++)
        {
            var rangeEnd = operations[i].Range.EndPosition;
            var nextRangeStart = operations[i + 1].Range.StartPosition;

            if (nextRangeStart.IsBeforeOrEqual(rangeEnd))
            {
                if (nextRangeStart.IsBefore(rangeEnd))
                {
                    // overlapping ranges
                    throw new Exception("Overlapping ranges are not allowed.");
                }
                hasTouchingRanges = true;
            }
        }

        if (canReduceOperations)
            operations = ReduceOperations(operations);

        // WithLineDelta encode operations
        TextRange[] reverseRanges = computeUndoEdits || recordTrimAutoWhitespace
            ? GetInverseEditRanges(operations)
            : [];
        List<(int lineNumber, string oldContent)> newTrimAutoWhitespaceCandidates = [];
        if (recordTrimAutoWhitespace)
        {
            for (int i = 0; i < operations.Length; i++)
            {
                var op = operations[i];
                var reverseRange = reverseRanges[i];

                if (op.IsAutoWhitespaceEdit && op.Range.IsEmpty)
                {
                    // Record already the future line numbers that might be auto whitespace removal candidates on next edit
                    for (int lineNumber = reverseRange.StartLineNumber; lineNumber < reverseRange.EndLineNumber; lineNumber++)
                    {
                        string currentLineContent = "";
                        if (lineNumber == reverseRange.StartLineNumber)
                        {
                            currentLineContent = GetLineContent(op.Range.StartLineNumber);
                            if (currentLineContent.FirstNonWhitespaceIndex() != -1)
                                continue;
                        }
                        newTrimAutoWhitespaceCandidates.Add((lineNumber, currentLineContent));
                    }
                }
            }
        }

        ReverseSingleEditOperation[]? reverseOperations = null;
        if (computeUndoEdits)
        {
            int reverseRangeDeltaOffset = 0;
            reverseOperations = new ReverseSingleEditOperation[operations.Length];
            for (int i = 0; i < operations.Length; i++)
            {
                ValidatedEditOperation op = operations[i];
                TextRange reverseRange = reverseRanges[i];
                string bufferText = GetValueInRange(op.Range);
                int reverseRangeOffset = op.RangeOffset + reverseRangeDeltaOffset;
                reverseRangeDeltaOffset += op.Text.Length - bufferText.Length;

                reverseOperations[i] = new ReverseSingleEditOperation
                {
                    SortIndex = op.SortIndex,
                    Range = reverseRange,
                    Text = bufferText,
                    TextChange = new TextChange(op.RangeOffset, bufferText, reverseRangeOffset, op.Text)
                };
            }

            // Can only sort reverse operations when the order is not significant
            if (!hasTouchingRanges)
            {
                Array.Sort(reverseOperations, (a, b) => a.SortIndex - b.SortIndex);
            }
        }

        _mightContainRTL = mightContainRTL;
        _mightContainUnusualLineTerminators = mightContainUnusualLineTerminators;
        _mightContainNonBasicASCII = mightContainNonBasicASCII;

        var contentChanges = DoApplyEdits(operations);

        List<int>? trimAutoWhitespaceLineNumbers = null;
        if (recordTrimAutoWhitespace && newTrimAutoWhitespaceCandidates.Count > 0)
        {
            // sort line numbers auto whitespace removal candidates for next edit descending
            newTrimAutoWhitespaceCandidates.Sort((a, b) => b.lineNumber - a.lineNumber);

            trimAutoWhitespaceLineNumbers = [];
            for (int i = 0, len = newTrimAutoWhitespaceCandidates.Count; i < len; i++)
            {
                int lineNumber = newTrimAutoWhitespaceCandidates[i].lineNumber;
                if (i > 0 && newTrimAutoWhitespaceCandidates[i - 1].lineNumber == lineNumber)
                    continue; // Do not have the same line number twice

                string prevContent = newTrimAutoWhitespaceCandidates[i].oldContent;
                string lineContent = GetLineContent(lineNumber);

                if (lineContent.Length == 0 || lineContent == prevContent || lineContent.FirstNonWhitespaceIndex() != -1)
                    continue;

                trimAutoWhitespaceLineNumbers.Add(lineNumber);
            }
        }

        OnDidChangeContent?.Invoke(this, EventArgs.Empty);

        return new ApplyEditsResult
        {
            ReverseEdits = reverseOperations,
            Changes = contentChanges,
            TrimAutoWhitespaceLineNumbers = trimAutoWhitespaceLineNumbers
        };
    }

    /**
     * Transform operations such that they represent the same logic edit,
     * but that they also do not cause OOM crashes.
     */
    private ValidatedEditOperation[] ReduceOperations(ValidatedEditOperation[] operations)
    {
        if (operations.Length < 1000)
            return operations; // We know from empirical testing that a thousand edits work fine regardless of their shape.

        bool forceMoveMarkers = false;
        TextRange firstEditRange = operations[0].Range;
        TextRange lastEditRange = operations[^1].Range;
        TextRange entireEditRange = new(firstEditRange.StartLineNumber, firstEditRange.StartColumn, lastEditRange.EndLineNumber, lastEditRange.EndColumn);
        int lastEndLineNumber = firstEditRange.StartLineNumber;
        int lastEndColumn = firstEditRange.StartColumn;
        List<string> result = [];

        for (int i = 0, len = operations.Length; i < len; i++)
        {
            ValidatedEditOperation operation = operations[i];
            TextRange range = operation.Range;

            forceMoveMarkers = forceMoveMarkers || operation.ForceMoveMarkers;

            // (1) -- Push old text
            result.Add(GetValueInRange(new TextRange(lastEndLineNumber, lastEndColumn, range.StartLineNumber, range.StartColumn)));

            // (2) -- Push new text
            if (operation.Text.Length > 0)
                result.Add(operation.Text);

            lastEndLineNumber = range.EndLineNumber;
            lastEndColumn = range.EndColumn;
        }

        string text = string.Concat(result);
        var (eolCount, firstLineLength, lastLineLength, _) = EOLCounter.CountEOL(text);

        var combinedOperation = new ValidatedEditOperation
        {
            SortIndex = 0,
            Range = entireEditRange,
            RangeOffset = GetOffsetAt(entireEditRange.StartLineNumber, entireEditRange.StartColumn),
            RangeLength = GetValueLengthInRange(entireEditRange, EndOfLinePreference.TextDefined),
            Text = text,
            EOLCount = eolCount,
            FirstLineLength = firstLineLength,
            LastLineLength = lastLineLength,
            ForceMoveMarkers = forceMoveMarkers,
            IsAutoWhitespaceEdit = false
        };

        // At one point, due to how events are emitted and how each operation is handled,
        // some operations can trigger a high amount of temporary string allocations,
        // that will immediately get edited again.
        // e.g. a formatter inserting ridiculous amounts of \n on a model with a single line
        // Therefore, the strategy is to collapse all the operations into a huge single edit operation
        return [combinedOperation];
    }

    private List<InternalModelContentChange> DoApplyEdits(ValidatedEditOperation[] operations)
    {
        Array.Sort(operations, SortOpsDescending);
        List<InternalModelContentChange> contentChanges = [];

        // operations are from bottom to top
        for (int i = 0; i < operations.Length; i++)
        {
            ValidatedEditOperation op = operations[i];

            if (op.Range.StartLineNumber == op.Range.EndLineNumber
                && op.Range.StartColumn == op.Range.EndColumn
                && op.Text.Length == 0)
                continue; // no-op

            if (!string.IsNullOrEmpty(op.Text))
            {
                // replacement
                Delete(op.RangeOffset, op.RangeLength);
                Insert(op.RangeOffset, op.Text, true);
            }
            else
            {
                // deletion
                Delete(op.RangeOffset, op.RangeLength);
            }

            contentChanges.Add(new InternalModelContentChange
            {
                Range = op.Range,
                RangeLength = op.RangeLength,
                Text = op.Text,
                RangeOffset = op.RangeOffset,
                ForceMoveMarkers = op.ForceMoveMarkers
            });
        }
        return contentChanges;
    }

    #endregion

    #region Buffer API

    public string EOL
    {
        get => _EOL;
        set
        {
            if (value != "\n" && value != "\r\n")
                throw new ArgumentException("Invalid EOL value");

            _EOL = value;
            NormalizeEOL(value);
        }
    }

    public bool Equals(PieceTreeTextBuffer? other)
    {
        if (other is null)
            return false;

        if (_BOM != other._BOM)
            return false;

        if (EOL != other.EOL)
            return false;

        if (Length != other.Length || LineCount != other.LineCount)
            return false;

        int offset = 0;
        return PieceTree.IterateInOrder(_pieceTree.Root, node =>
        {
            string str = GetNodeContent(node);
            int len = str.Length;
            NodePosition startPosition = other.NodeAt(offset);
            NodePosition endPosition = other.NodeAt(offset + len);
            string val = other.GetValueInRange2(startPosition, endPosition);

            offset += len;
            return str == val;
        });
    }

    public int GetOffsetAt(int lineNumber, int column)
    {
        int leftLen = 0; // inorder
        TreeNode x = _pieceTree.Root;

        while (!x.IsSentinel)
        {
            if (!x.Left.IsSentinel && x.LfLeft + 1 >= lineNumber)
            {
                x = x.Left;
            }
            else if (x.LfLeft + x.Piece.LineFeedCount + 1 >= lineNumber)
            {
                leftLen += x.SizeLeft;
                // lineNumber >= 2
                int accumulatedValInCurrentIndex = GetAccumulatedValue(x, lineNumber - x.LfLeft - 2);
                return leftLen + accumulatedValInCurrentIndex + column - 1;
            }
            else
            {
                lineNumber -= x.LfLeft + x.Piece.LineFeedCount;
                leftLen += x.SizeLeft + x.Piece.Length;
                x = x.Right;
            }
        }

        return leftLen;
    }

    public TextPosition GetPositionAt(int offset)
    {
        offset = Math.Max(0, offset);

        TreeNode x = _pieceTree.Root;
        int lfCnt = 0;
        int originalOffset = offset;

        while (!x.IsSentinel)
        {
            if (x.SizeLeft != 0 && x.SizeLeft >= offset)
            {
                x = x.Left;
            }
            else if (x.SizeLeft + x.Piece.Length >= offset)
            {
                (int index, int remainder) = GetIndexOf(x, offset - x.SizeLeft);
                lfCnt += x.LfLeft + index;

                if (index == 0) {
                    int lineStartOffset = GetOffsetAt(lfCnt + 1, 1);
                    int column = originalOffset - lineStartOffset;
                    return new TextPosition(lfCnt + 1, column + 1);
                }

                return new TextPosition(lfCnt + 1, remainder + 1);
            }
            else
            {
                offset -= x.SizeLeft + x.Piece.Length;
                lfCnt += x.LfLeft + x.Piece.LineFeedCount;

                if (x.Right.IsSentinel)
                {
                    // last node
                    int lineStartOffset = GetOffsetAt(lfCnt + 1, 1);
                    int column = originalOffset - offset - lineStartOffset;
                    return new TextPosition(lfCnt + 1, column + 1);
                }
                else
                {
                    x = x.Right;
                }
            }
        }

        return new TextPosition(1, 1);
    }

    public string GetValueInRange(TextRange range, string? eol)
    {
        if (range.StartLineNumber == range.EndLineNumber && range.StartColumn == range.EndColumn)
        {
            return "";
        }

        NodePosition startPosition = NodeAt2(range.StartLineNumber, range.StartColumn);
        NodePosition endPosition = NodeAt2(range.EndLineNumber, range.EndColumn);

        string value = GetValueInRange2(startPosition, endPosition);
        if (eol is not null)
        {
            if (eol != _EOL || !_EOLNormalized)
                return StringExtensions.EndOfLinesRegex.Replace(value, eol);

            if (eol == _EOL && _EOLNormalized)
                return value;

            return StringExtensions.EndOfLinesRegex.Replace(value, eol);
        }
        return value;
    }

    internal string GetValueInRange2(NodePosition startPosition, NodePosition endPosition)
    {
        TreeNode x = startPosition.Node;
        int startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);

        if (startPosition.Node == endPosition.Node)
            return _buffers[x.Piece.BufferIndex].Text[(startOffset + startPosition.Remainder)..(startOffset + endPosition.Remainder)].ToString();

        var sb = new StringBuilder();
        sb.Append(_buffers[x.Piece.BufferIndex].Text[(startOffset + startPosition.Remainder)..(startOffset + x.Piece.Length)]);

        x = x.Next();
        while (x != TreeNode.Sentinel)
        {
            startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);

            if (x == endPosition.Node)
            {
                sb.Append(_buffers[x.Piece.BufferIndex].Text.Slice(startOffset, endPosition.Remainder));
                break;
            }
            else
            {
                sb.Append(_buffers[x.Piece.BufferIndex].Text.Slice(startOffset, x.Piece.Length));
            }

            x = x.Next();
        }

        return sb.ToString();
    }

    public IReadOnlyList<string> GetLinesContent()
    {
        List<string> lines = [];
        string currentLine = "";
        bool danglingCR = false;

        PieceTree.IterateInOrder(_pieceTree.Root, node =>
        {
            Piece piece = node.Piece;
            int pieceLength = piece.Length;
            if (pieceLength == 0)
                return true;

            var buffer = _buffers[piece.BufferIndex];
            var lineStarts = buffer.LineStarts;

            int pieceStartLine = piece.Start.Line;
            int pieceEndLine = piece.End.Line;
            int pieceStartOffset = lineStarts[pieceStartLine] + piece.Start.Column;

            if (danglingCR)
            {
                if (buffer.Text[pieceStartOffset] == '\n')
                {
                    // pretend the \n was in the previous piece..
                    pieceStartOffset++;
                    pieceLength--;
                }
                lines.Add(currentLine);
                currentLine = "";
                danglingCR = false;
                if (pieceLength == 0)
                    return true;
            }

            if (pieceStartLine == pieceEndLine)
            {
                // this piece has no new lines
                if (!_EOLNormalized && buffer.Text[pieceStartOffset + pieceLength - 1] == '\r')
                {
                    danglingCR = true;
                    currentLine += buffer.Text.Slice(pieceStartOffset, pieceLength - 1).ToString();
                }
                else
                {
                    currentLine += buffer.Text.Slice(pieceStartOffset, pieceLength).ToString();
                }
                return true;
            }


            // add the text before the first line start in this piece
            currentLine += _EOLNormalized
                ? buffer.Text.Slice(pieceStartOffset, Math.Max(pieceStartOffset, lineStarts[pieceStartLine + 1] - _EOL.Length) - pieceStartOffset).ToString()
                : StringExtensions.EndOfLinesRegex.Replace(buffer.Text.Slice(pieceStartOffset, lineStarts[pieceStartLine + 1] - pieceStartOffset).ToString(), "");
            lines.Add(currentLine);

            for (int line = pieceStartLine + 1; line < pieceEndLine; line++)
            {
                currentLine = _EOLNormalized
                    ? buffer.Text.Slice(lineStarts[line], lineStarts[line + 1] - _EOL.Length - lineStarts[line]).ToString()
                    : StringExtensions.EndOfLinesRegex.Replace(buffer.Text.Slice(lineStarts[line], lineStarts[line + 1] - lineStarts[line]).ToString(), "");
                lines.Add(currentLine);
            }

            if (!_EOLNormalized && buffer.Text[lineStarts[pieceEndLine] + piece.End.Column - 1] == '\r')
            {
                danglingCR = true;
                if (piece.End.Column == 0)
                    // The last line ended with a \r, let's undo the push, it will be pushed by next iteration
                    lines.RemoveAt(lines.Count - 1);
                else
                    currentLine = buffer.Text.Slice(lineStarts[pieceEndLine], piece.End.Column - 1).ToString();
            }
            else
            {
                currentLine = buffer.Text.Slice(lineStarts[pieceEndLine], piece.End.Column).ToString();
            }

            return true;
        });

        if (danglingCR)
        {
            lines.Add(currentLine);
            currentLine = "";
        }

        lines.Add(currentLine);
        return lines;
    }

    public int Length { get => _length; }

    public int LineCount { get => _lineCount; }

    public string GetLineContent(int lineNumber)
    {
        if (_lastVisitedLine.LineNumber == lineNumber)
            return _lastVisitedLine.Value;

        _lastVisitedLine.LineNumber = lineNumber;

        if (lineNumber == _lineCount)
            _lastVisitedLine.Value = GetLineRawContent(lineNumber);
        else if (_EOLNormalized)
            _lastVisitedLine.Value = GetLineRawContent(lineNumber, _EOL.Length);
        else
            _lastVisitedLine.Value = StringExtensions.EndOfLinesRegex.Replace(GetLineRawContent(lineNumber), "");

        return _lastVisitedLine.Value;
    }

    private char GetCharCode(NodePosition nodePos)
    {
        if (nodePos.Remainder == nodePos.Node.Piece.Length)
        {
            // the char we want to fetch is at the head of next node.
            var matchingNode = nodePos.Node.Next();
            if (matchingNode is null)
                return '\0';

            int startOffset = OffsetInBuffer(matchingNode.Piece.BufferIndex, matchingNode.Piece.Start);
            return _buffers[matchingNode.Piece.BufferIndex].Text[startOffset];
        }
        else
        {
            int startOffset = OffsetInBuffer(nodePos.Node.Piece.BufferIndex, nodePos.Node.Piece.Start);
            int targetOffset = startOffset + nodePos.Remainder;

            return _buffers[nodePos.Node.Piece.BufferIndex].Text[targetOffset];
        }
    }

    public char GetLineCharCode(int lineNumber, int index)
    {
        var nodePos = NodeAt2(lineNumber, index + 1);
        return GetCharCode(nodePos);
    }

    public int GetLineLength(int lineNumber)
    {
        if (lineNumber == LineCount)
        {
            int startOffset = GetOffsetAt(lineNumber, 1);
            return Length - startOffset;
        }
        return GetOffsetAt(lineNumber + 1, 1) - GetOffsetAt(lineNumber, 1) - _EOL.Length;
    }

    public char GetCharCode(int offset)
    {
        var nodePos = NodeAt(offset);
        return GetCharCode(nodePos);
    }

    public string GetNearestChunk(int offset)
    {
        var nodePos = NodeAt(offset);
        if (nodePos.Remainder == nodePos.Node.Piece.Length)
        {
            // the offset is at the head of next node.
            var matchingNode = nodePos.Node.Next();
            if (matchingNode.IsSentinel)
                return "";

            var startOffset = OffsetInBuffer(matchingNode.Piece.BufferIndex, matchingNode.Piece.Start);
            return _buffers[matchingNode.Piece.BufferIndex].Text.Slice(startOffset, matchingNode.Piece.Length).ToString();
        }
        else
        {
            var startOffset = OffsetInBuffer(nodePos.Node.Piece.BufferIndex, nodePos.Node.Piece.Start);
            var targetOffset = startOffset + nodePos.Remainder;
            var targetEnd = startOffset + nodePos.Node.Piece.Length;
            return _buffers[nodePos.Node.Piece.BufferIndex].Text.Slice(targetOffset, targetEnd - targetOffset).ToString();
        }
    }

    public IReadOnlyList<FindMatch> FindMatchesLineByLine(
        TextRange searchRange,
        SearchData searchData,
        bool captureMatches,
        int limitResultCount)
    {
        List<FindMatch> result = [];
        var searcher = new Searcher(searchData.WordSeparators, searchData.Regex);

        var startPosition = NodeAt2(searchRange.StartLineNumber, searchRange.StartColumn);
        if (startPosition is null)
            return [];
        var endPosition = NodeAt2(searchRange.EndLineNumber, searchRange.EndColumn);
        if (endPosition is null)
            return [];
        var start = PositionInBuffer(startPosition.Node, startPosition.Remainder);
        var end = PositionInBuffer(endPosition.Node, endPosition.Remainder);

        if (startPosition.Node == endPosition.Node)
        {
            FindMatchesInNode(startPosition.Node, searcher, searchRange.StartLineNumber, searchRange.StartColumn, start, end, searchData, captureMatches, limitResultCount, result);
            return result;
        }

        int startLineNumber = searchRange.StartLineNumber;
        var currentNode = startPosition.Node;
        int startColumn;
        while (currentNode != endPosition.Node)
        {
            int lineBreakCnt = GetLineFeedCnt(currentNode.Piece.BufferIndex, start, currentNode.Piece.End);
            if (lineBreakCnt >= 1)
            {
                // last line break position
                var lineStarts = _buffers[currentNode.Piece.BufferIndex].LineStarts;
                int startOffsetInBuffer = OffsetInBuffer(currentNode.Piece.BufferIndex, currentNode.Piece.Start);
                int nextLineStartOffset = lineStarts[start.Line + lineBreakCnt];
                startColumn = startLineNumber == searchRange.StartLineNumber ? searchRange.StartColumn : 1;
                FindMatchesInNode(currentNode, searcher, startLineNumber, startColumn, start, PositionInBuffer(currentNode, nextLineStartOffset - startOffsetInBuffer), searchData, captureMatches, limitResultCount, result);
                if (result.Count >= limitResultCount)
                    return result;
                startLineNumber += lineBreakCnt;
            }

            startColumn = startLineNumber == searchRange.StartLineNumber ? searchRange.StartColumn - 1 : 0;
            // search for the remaining content
            if (startLineNumber == searchRange.EndLineNumber)
            {
                string text = GetLineContent(startLineNumber).Substring(startColumn, searchRange.EndColumn - 1 - startColumn);
                FindMatchesInLine(searchData, searcher, text, searchRange.EndLineNumber, startColumn, result, captureMatches, limitResultCount);
                return result;
            }
            FindMatchesInLine(searchData, searcher, GetLineContent(startLineNumber).Substring(startColumn), startLineNumber, startColumn, result, captureMatches, limitResultCount);
            if (result.Count >= limitResultCount)
                return result;
            startLineNumber++;
            startPosition = NodeAt2(startLineNumber, 1);
            currentNode = startPosition.Node;
            start = PositionInBuffer(startPosition.Node, startPosition.Remainder);
        }

        if (startLineNumber == searchRange.EndLineNumber)
        {
            startColumn = startLineNumber == searchRange.StartLineNumber ? searchRange.StartColumn - 1 : 0;
            string text = GetLineContent(startLineNumber).Substring(startColumn, searchRange.EndColumn - 1 - startColumn);
            FindMatchesInLine(searchData, searcher, text, searchRange.EndLineNumber, startColumn, result, captureMatches, limitResultCount);
            return result;
        }

        startColumn = startLineNumber == searchRange.StartLineNumber ? searchRange.StartColumn : 1;
        FindMatchesInNode(endPosition.Node, searcher, startLineNumber, startColumn, start, end, searchData, captureMatches, limitResultCount, result);
        return result;
    }

    private void FindMatchesInNode(
        TreeNode node,
        Searcher searcher,
        int startLineNumber,
        int startColumn,
        BufferCursor startCursor,
        BufferCursor endCursor,
        SearchData searchData,
        bool captureMatches,
        int limitResultCount,
        List<FindMatch> result)
    {
        int startOffsetInBuffer = OffsetInBuffer(node.Piece.BufferIndex, node.Piece.Start);
        int start = OffsetInBuffer(node.Piece.BufferIndex, startCursor);
        int end = OffsetInBuffer(node.Piece.BufferIndex, endCursor);

        Match? m;
        // Reset regex to search from the beginning
        BufferCursor? ret = new BufferCursor(0, 0);
        string searchText;
        Func<int, int> offsetInBuffer;

        if (searcher._wordSeparators is not null)
        {
            searchText = _buffers[node.Piece.BufferIndex].Text.Slice(start, end - start).ToString();
            offsetInBuffer = (int offset) => offset + start;
            searcher.Reset(0);
        }
        else
        {
            searchText = _buffers[node.Piece.BufferIndex].Text.ToString();
            offsetInBuffer = (int offset) => offset;
            searcher.Reset(start);
        }

        do
        {
            m = searcher.Next(searchText);
            if (m is not null)
            {
                if (offsetInBuffer(m.Index) >= end)
                    return;
                PositionInBuffer(node, offsetInBuffer(m.Index) - startOffsetInBuffer, ref ret);
                BufferCursor ret1 = (BufferCursor)ret!;
                int lineFeedCnt = GetLineFeedCnt(node.Piece.BufferIndex, startCursor, ret1);
                int retStartColumn = ret1.Line == startCursor.Line ? ret1.Column - startCursor.Column + startColumn : ret1.Column + 1;
                int retEndColumn = retStartColumn + m.Length;
                result.Add(SearchUtils.CreateFindMatch(new TextRange(startLineNumber + lineFeedCnt, retStartColumn, startLineNumber + lineFeedCnt, retEndColumn), [m], captureMatches));

                if (offsetInBuffer(m.Index) + m.Length >= end)
                    return;
                if (result.Count >= limitResultCount)
                    return;
            }
        } while (m is not null);
    }


    private void FindMatchesInLine(
        SearchData searchData,
        Searcher searcher,
        string text,
        int lineNumber,
        int deltaOffset,
        List<FindMatch> result,
        bool captureMatches,
        int limitResultCount)
    {
        var wordSeparators = searchData.WordSeparators;
        if (!captureMatches && searchData.SimpleSearch is string searchString)
        {
            int lastMatchIndex = -searchString.Length;
            while((lastMatchIndex = text.IndexOf(searchString, lastMatchIndex + searchString.Length)) != -1)
            {
                if (wordSeparators is null || SearchUtils.IsValidMatch(wordSeparators, text, text.Length, lastMatchIndex, searchString.Length))
                {
                    result.Add(new FindMatch(new TextRange(lineNumber, lastMatchIndex + 1 + deltaOffset, lineNumber, lastMatchIndex + 1 + searchString.Length + deltaOffset), null));
                    if (result.Count >= limitResultCount)
                        return;
                }
            }
            return;
        }

        Match? m = null;
        // Reset regex to search from the beginning
        searcher.Reset(0);
        do
        {
            m = searcher.Next(text);
            if (m is not null)
            {
                result.Add(SearchUtils.CreateFindMatch(
                    new TextRange(lineNumber, m.Index + 1 + deltaOffset, lineNumber, m.Index + 1 + m.Length + deltaOffset),
                    [m],
                    captureMatches)
                );
            }
        } while (m is not null);
    }

    #endregion

    #region Piece Table

    public void Insert(int offset, string value, bool eolNormalized = false)
    {
        _EOLNormalized = _EOLNormalized && eolNormalized;
        _lastVisitedLine.LineNumber = 0;
        _lastVisitedLine.Value = "";

        if (!_pieceTree.Root.IsSentinel)
        {
            var nodePosition = NodeAt(offset);
            var node = nodePosition.Node;
            int remainder = nodePosition.Remainder;
            int nodeStartOffset = nodePosition.NodeStartOffset;

            var piece = node.Piece;
            int bufferIndex = piece.BufferIndex;
            var insertPosInBuffer = PositionInBuffer(node, remainder);
            if (node.Piece.BufferIndex == 0 &&
                piece.End.Line == _lastChangeBufferPos.Line &&
                piece.End.Column == _lastChangeBufferPos.Column &&
                (nodeStartOffset + piece.Length == offset) &&
                value.Length < AverageBufferSize
            )
            {
                // changed buffer
                AppendToNode(node, value);
                ComputeBufferMetadata();
                return;
            }

            if (nodeStartOffset == offset)
            {
                InsertContentToNodeLeft(value, node);
                _searchCache.Validate(offset);
            }
            else if (nodeStartOffset + node.Piece.Length > offset)
            {
                // we are inserting into the middle of a node.
                List<TreeNode> nodesToDel = [];
                var newRightPiece = new Piece(
                    piece.BufferIndex,
                    insertPosInBuffer,
                    piece.End,
                    GetLineFeedCnt(piece.BufferIndex, insertPosInBuffer, piece.End),
                    OffsetInBuffer(bufferIndex, piece.End) - OffsetInBuffer(bufferIndex, insertPosInBuffer)
                );

                if (ShouldCheckCRLF && EndWithCR(value))
                {
                    var headOfRight = NodeCharCodeAt(node, remainder);

                    if (headOfRight == 10 /** \n */)
                    {
                        var newStart = new BufferCursor
                        {
                            Line = newRightPiece.Start.Line + 1,
                            Column = 0
                        };
                        newRightPiece = new Piece(
                            newRightPiece.BufferIndex,
                            newStart,
                            newRightPiece.End,
                            GetLineFeedCnt(newRightPiece.BufferIndex, newStart, newRightPiece.End),
                            newRightPiece.Length - 1
                        );

                        value += '\n';
                    }
                }

                // reuse node for content before insertion point.
                if (ShouldCheckCRLF && StartWithLF(value))
                {
                    var tailOfLeft = NodeCharCodeAt(node, remainder - 1);
                    if (tailOfLeft == 13 /** \r */)
                    {
                        var previousPos = PositionInBuffer(node, remainder - 1);
                        DeleteNodeTail(node, previousPos);
                        value = '\r' + value;

                        if (node.Piece.Length == 0)
                        {
                            nodesToDel.Add(node);
                        }
                    }
                    else
                    {
                        DeleteNodeTail(node, insertPosInBuffer);
                    }
                }
                else
                {
                    DeleteNodeTail(node, insertPosInBuffer);
                }

                var newPieces = CreateNewPieces(value);
                if (newRightPiece.Length > 0)
                {
                    _pieceTree.InsertRight(node, new PieceNodeData(newRightPiece));
                }

                var tmpNode = node;
                for (int k = 0; k < newPieces.Count; k++)
                {
                    tmpNode = _pieceTree.InsertRight(tmpNode, new PieceNodeData(newPieces[k]));
                }
                DeleteNodes(nodesToDel);
            }
            else
            {
                InsertContentToNodeRight(value, node);
            }
        }
        else
        {
            // insert new node
            var pieces = CreateNewPieces(value);
            var node = _pieceTree.InsertLeft(null, new PieceNodeData(pieces[0]));

            for (int k = 1; k < pieces.Count; k++)
            {
                node = _pieceTree.InsertRight(node, new PieceNodeData(pieces[k]));
            }
        }

        // TODO: this is too brutal. Total line feed count should be updated the same way as lf_left.
        ComputeBufferMetadata();
    }

    public void Delete(int offset, int cnt)
    {
        _lastVisitedLine.LineNumber = 0;
        _lastVisitedLine.Value = "";

        if (cnt <= 0 || _pieceTree.Root.IsSentinel)
            return;

        var startPosition = NodeAt(offset);
        var endPosition = NodeAt(offset + cnt);
        var startNode = startPosition.Node;
        var endNode = endPosition.Node;

        if (startNode == endNode)
        {
            var startSplitPosInBuffer = PositionInBuffer(startNode, startPosition.Remainder);
            var endSplitPosInBuffer = PositionInBuffer(startNode, endPosition.Remainder);

            if (startPosition.NodeStartOffset == offset)
            {
                if (cnt == startNode.Piece.Length)
                {
                    // delete node
                    var next = startNode.Next();
                    _pieceTree.Delete(startNode);
                    ValidateCRLFWithPrevNode(next);
                    ComputeBufferMetadata();
                    return;
                }
                DeleteNodeHead(startNode, endSplitPosInBuffer);
                _searchCache.Validate(offset);
                ValidateCRLFWithPrevNode(startNode);
                ComputeBufferMetadata();
                return;
            }

            if (startPosition.NodeStartOffset + startNode.Piece.Length == offset + cnt)
            {
                DeleteNodeTail(startNode, startSplitPosInBuffer);
                ValidateCRLFWithNextNode(startNode);
                ComputeBufferMetadata();
                return;
            }

            // delete content in the middle, this node will be splitted to nodes
            ShrinkNode(startNode, startSplitPosInBuffer, endSplitPosInBuffer);
            ComputeBufferMetadata();
            return;
        }

        List<TreeNode> nodesToDel = [];

        var startSplitPosInBuffer1 = PositionInBuffer(startNode, startPosition.Remainder);
        DeleteNodeTail(startNode, startSplitPosInBuffer1);
        _searchCache.Validate(offset);
        if (startNode.Piece.Length == 0)
        {
            nodesToDel.Add(startNode);
        }

        // update last touched node
        var endSplitPosInBuffer1 = PositionInBuffer(endNode, endPosition.Remainder);
        DeleteNodeHead(endNode, endSplitPosInBuffer1);
        if (endNode.Piece.Length == 0)
        {
            nodesToDel.Add(endNode);
        }

        // delete nodes in between
        var secondNode = startNode.Next();
        for (var node = secondNode; node != TreeNode.Sentinel && node != endNode; node = node.Next())
        {
            nodesToDel.Add(node);
        }

        var prev = startNode.Piece.Length == 0 ? startNode.Prev() : startNode;
        DeleteNodes(nodesToDel);
        ValidateCRLFWithNextNode(prev);
        ComputeBufferMetadata();
    }

    private void InsertContentToNodeLeft(string value, TreeNode node)
    {
        // we are inserting content to the beginning of node
        List<TreeNode> nodesToDel = [];
        if (ShouldCheckCRLF && EndWithCR(value) && StartWithLF(node))
        {
            // move `\n` to new node.

            var piece = node.Piece;
            var newStart = new BufferCursor
            {
                Line = piece.Start.Line + 1,
                Column = 0
            };
            var nPiece = new Piece(
                piece.BufferIndex,
                newStart,
                piece.End,
                GetLineFeedCnt(piece.BufferIndex, newStart, piece.End),
                piece.Length - 1
            );

            node.Piece = nPiece;

            value += '\n';
            _pieceTree.UpdateTreeMetadata(node, -1, -1);

            if (node.Piece.Length == 0)
                nodesToDel.Add(node);
        }

        var newPieces = CreateNewPieces(value);
        var newNode = _pieceTree.InsertLeft(node, new PieceNodeData(newPieces[newPieces.Count - 1]));
        for (int k = newPieces.Count - 2; k >= 0; k--)
        {
            newNode = _pieceTree.InsertLeft(newNode, new PieceNodeData(newPieces[k]));
        }
        ValidateCRLFWithPrevNode(newNode);
        DeleteNodes(nodesToDel);
    }

    private void InsertContentToNodeRight(string value, TreeNode node)
    {
        // we are inserting to the right of this node.
        if (AdjustCarriageReturnFromNext(value, node))
        {
            // move \n to the new node.
            value += '\n';
        }

        var newPieces = CreateNewPieces(value);
        var newNode = _pieceTree.InsertRight(node, new PieceNodeData(newPieces[0]));
        var tmpNode = newNode;

        for (int k = 1; k < newPieces.Count; k++)
        {
            tmpNode = _pieceTree.InsertRight(tmpNode, new PieceNodeData(newPieces[k]));
        }

        ValidateCRLFWithPrevNode(newNode);
    }

    private BufferCursor PositionInBuffer(TreeNode node, int remainder)
    {
        BufferCursor? ret = null;
        return (BufferCursor)PositionInBuffer(node, remainder, ref ret)!; // not null when ret is null
    }

    private BufferCursor? PositionInBuffer(TreeNode node, int remainder, ref BufferCursor? ret)
    {
        var piece = node.Piece;
        int bufferIndex = node.Piece.BufferIndex;
        var lineStarts = _buffers[bufferIndex].LineStarts;

        int startOffset = lineStarts[piece.Start.Line] + piece.Start.Column;

        int offset = startOffset + remainder;

        // binary search offset between startOffset and endOffset
        int low = piece.Start.Line;
        int high = piece.End.Line;

        int mid = 0;
        int midStop = 0;
        int midStart = 0;

        while (low <= high)
        {
            mid = low + ((high - low) / 2) | 0;
            midStart = lineStarts[mid];

            if (mid == high)
                break;

            midStop = lineStarts[mid + 1];

            if (offset < midStart)
                high = mid - 1;
            else if (offset >= midStop)
                low = mid + 1;
            else
                break;
        }

        if (ret is BufferCursor)
        {
            ret = new BufferCursor(mid, offset - midStart);
            return null;
        }

        return new BufferCursor
        {
            Line = mid,
            Column = offset - midStart
        };
    }

    private int GetLineFeedCnt(int bufferIndex, BufferCursor start, BufferCursor end)
    {
        // we don't need to worry about start: abc\r|\n, or abc|\r, or abc|\n, or abc|\r\n doesn't change the fact that, there is one line break after start.
        // now let's take care of end: abc\r|\n, if end is in between \r and \n, we need to add line feed count by 1
        if (end.Column == 0)
            return end.Line - start.Line;

        var lineStarts = _buffers[bufferIndex].LineStarts;
        if (end.Line == lineStarts.Length - 1)
        {
            // it means, there is no \n after end, otherwise, there will be one more lineStart.
            return end.Line - start.Line;
        }

        int nextLineStartOffset = lineStarts[end.Line + 1];
        int endOffset = lineStarts[end.Line] + end.Column;
        if (nextLineStartOffset > endOffset + 1)
        {
            // there are more than 1 character after end, which means it can't be \n
            return end.Line - start.Line;
        }
        // endOffset + 1 === nextLineStartOffset
        // character at endOffset is \n, so we check the character before first
        // if character at endOffset is \r, end.column is 0 and we can't get here.
        int previousCharOffset = endOffset - 1; // end.column > 0 so it's okay.

        if (_buffers[bufferIndex].Text[previousCharOffset] == 13)
            return end.Line - start.Line + 1;
        else
            return end.Line - start.Line;
    }

    private int OffsetInBuffer(int bufferIndex, BufferCursor cursor)
    {
        var lineStarts = _buffers[bufferIndex].LineStarts;
        return lineStarts[cursor.Line] + cursor.Column;
    }

    private void DeleteNodes(IReadOnlyList<TreeNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
            _pieceTree.Delete(nodes[i]);
    }

    private IReadOnlyList<Piece> CreateNewPieces(string text)
    {
        List<int> lineStarts;
        if (text.Length > AverageBufferSize)
        {
            // the content is large, operations like substring, charCode becomes slow
            // so here we split it into smaller chunks, just like what we did for CR/LF normalization
            List<Piece> newPieces = [];
            while (text.Length > AverageBufferSize)
            {
                char lastChar = text[AverageBufferSize - 1];
                string splitText;
                if (lastChar == '\r' || (lastChar >= 0xD800 && lastChar <= 0xDBFF))
                {
                    // last character is \r or a high surrogate => keep it back
                    splitText = text[0..(AverageBufferSize - 1)];
                    text = text[(AverageBufferSize - 1)..];
                }
                else
                {
                    splitText = text[0..AverageBufferSize];
                    text = text[AverageBufferSize..];
                }

                lineStarts = LineStarts.CreateFast(splitText);
                newPieces.Add(new Piece(
                    _buffers.Count,
                    new BufferCursor { Line = 0, Column = 0 },
                    new BufferCursor
                    {
                        Line = lineStarts.Count - 1,
                        Column = splitText.Length - lineStarts[lineStarts.Count - 1]
                    },
                    lineStarts.Count - 1,
                    splitText.Length
                ));
                _buffers.Add(new InlineStringBuffer(splitText, lineStarts));
            }

            lineStarts = LineStarts.CreateFast(text);
            newPieces.Add(new Piece(
                _buffers.Count, /* buffer index */
                new BufferCursor { Line = 0, Column = 0 },
                new BufferCursor
                {
                    Line = lineStarts.Count - 1,
                    Column = text.Length - lineStarts[lineStarts.Count - 1]
                },
                lineStarts.Count - 1,
                text.Length
            ));
            _buffers.Add(new InlineStringBuffer(text, lineStarts));

            return newPieces;
        }

        int startOffset = _buffers[0].Text.Length;
        lineStarts = LineStarts.CreateFast(text);

        var start = _lastChangeBufferPos;
        ref var changeBuffer = ref CollectionsMarshal.AsSpan(_buffers)[0];
        if (changeBuffer.LineStarts[changeBuffer.LineStarts.Length - 1] == startOffset
            && startOffset != 0
            && StartWithLF(text)
            && EndWithCR(changeBuffer.Text) // TODO: we can check this._lastChangeBufferPos's column as it's the last one
        )
        {
            _lastChangeBufferPos = new BufferCursor
            {
                Line = _lastChangeBufferPos.Line,
                Column = _lastChangeBufferPos.Column + 1
            };
            start = _lastChangeBufferPos;

            changeBuffer.Append('_');
            changeBuffer.AppendText(text);
            startOffset += 1;
        }
        else
        {
            changeBuffer.AppendText(text);
        }

        var endOffset = changeBuffer.Text.Length;
        var endIndex = changeBuffer.LineStarts.Length - 1;
        var endColumn = endOffset - changeBuffer.LineStarts[endIndex];
        var endPos = new BufferCursor
        {
            Line = endIndex,
            Column = endColumn
        };
        var newPiece = new Piece(
            0, /** todo@peng */
            start,
            endPos,
            GetLineFeedCnt(0, start, endPos),
            endOffset - startOffset
        );
        _lastChangeBufferPos = endPos;
        return [newPiece];
    }

    public string GetLinesRawContent() => GetContentOfSubTree(_pieceTree.Root);

    public string GetLineRawContent(int lineNumber, int endOffset = 0)
    {
        var x = _pieceTree.Root;

        string ret = "";
        var cacheRet = _searchCache.Get2(lineNumber);
        if (cacheRet is (TreeNode cacheNode, int cacheNodeStartOffset, int cacheNodeStartLineNumber))
        {
            x = cacheNode;
            int prevAccumulatedValue = GetAccumulatedValue(x, lineNumber - cacheNodeStartLineNumber - 1);
            int startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);
            if (cacheNodeStartLineNumber + x.Piece.LineFeedCount == lineNumber)
            {
                ret = _buffers[x.Piece.BufferIndex].Text.Slice(startOffset + prevAccumulatedValue, x.Piece.Length - prevAccumulatedValue).ToString();
            }
            else
            {
                int accumulatedValue = GetAccumulatedValue(x, lineNumber - cacheNodeStartLineNumber);
                return _buffers[x.Piece.BufferIndex].Text.Slice(startOffset + prevAccumulatedValue, accumulatedValue - endOffset - prevAccumulatedValue).ToString();
            }
        }
        else // cache is null
        {
            int nodeStartOffset = 0;
            int originalLineNumber = lineNumber;
            while (!x.IsSentinel)
            {
                if (!x.Left.IsSentinel && x.LfLeft >= lineNumber - 1)
                {
                    x = x.Left;
                }
                else if (x.LfLeft + x.Piece.LineFeedCount > lineNumber - 1)
                {
                    int prevAccumulatedValue = GetAccumulatedValue(x, lineNumber - x.LfLeft - 2);
                    int accumulatedValue = GetAccumulatedValue(x, lineNumber - x.LfLeft - 1);
                    var startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);
                    nodeStartOffset += x.SizeLeft;
                    _searchCache.Set(new CacheEntry
                    {
                        Node = x,
                        NodeStartOffset = nodeStartOffset,
                        NodeStartLineNumber = originalLineNumber - (lineNumber - 1 - x.LfLeft)
                    });

                    return _buffers[x.Piece.BufferIndex].Text.Slice(startOffset + prevAccumulatedValue, accumulatedValue - endOffset - prevAccumulatedValue).ToString();
                }
                else if (x.LfLeft + x.Piece.LineFeedCount == lineNumber - 1)
                {
                    var prevAccumulatedValue = GetAccumulatedValue(x, lineNumber - x.LfLeft - 2);
                    var startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);

                    ret = _buffers[x.Piece.BufferIndex].Text.Slice(startOffset + prevAccumulatedValue, x.Piece.Length - prevAccumulatedValue).ToString();
                    break;
                }
                else
                {
                    lineNumber -= x.LfLeft + x.Piece.LineFeedCount;
                    nodeStartOffset += x.SizeLeft + x.Piece.Length;
                    x = x.Right;
                }
            }
        }

        // search in order, to find the node contains end column
        x = x.Next();
        while (x != TreeNode.Sentinel)
        {
            if (x.Piece.LineFeedCount > 0)
            {
                int accumulatedValue = GetAccumulatedValue(x, 0);
                int startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);

                ret += _buffers[x.Piece.BufferIndex].Text.Slice(startOffset, accumulatedValue - endOffset).ToString();
                return ret;
            }
            else
            {
                int startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);
                ret += _buffers[x.Piece.BufferIndex].Text.Slice(startOffset, x.Piece.Length).ToString();
            }

            x = x.Next();
        }

        return ret;
    }

    private void ComputeBufferMetadata()
    {
        TreeNode x = _pieceTree.Root;

        int lfCount = 1;
        int len = 0;

        while (!x.IsSentinel)
        {
            lfCount += x.LfLeft +x.Piece.LineFeedCount;
            len += x.SizeLeft + x.Piece.Length;
            x = x.Right;
        }

        _lineCount = lfCount;
        _length = len;
        _searchCache.Validate(_length);
    }

    #endregion

    #region Node Operations

    private (int Index, int Remainder) GetIndexOf(TreeNode node, int accumulatedValue)
    {
        var piece = node.Piece;
        var pos = PositionInBuffer(node, accumulatedValue);
        int lineCnt = pos.Line - piece.Start.Line;

        if (OffsetInBuffer(piece.BufferIndex, piece.End) - OffsetInBuffer(piece.BufferIndex, piece.Start) == accumulatedValue)
        {
            // we are checking the end of this node, so a CRLF check is necessary.
            int realLineCnt = GetLineFeedCnt(node.Piece.BufferIndex, piece.Start, pos);
            if (realLineCnt != lineCnt)
            {
                // aha yes, CRLF
                return (realLineCnt, 0);
            }
        }

        return (lineCnt, pos.Column);
    }

    private int GetAccumulatedValue(TreeNode node, int index)
    {
        if (index < 0)
            return 0;

        var piece = node.Piece;
        var lineStarts = _buffers[piece.BufferIndex].LineStarts;
        int expectedLineStartIndex = piece.Start.Line + index + 1;
        if (expectedLineStartIndex > piece.End.Line)
            return lineStarts[piece.End.Line] + piece.End.Column - lineStarts[piece.Start.Line] - piece.Start.Column;
        else
            return lineStarts[expectedLineStartIndex] - lineStarts[piece.Start.Line] - piece.Start.Column;
    }

    private void DeleteNodeTail(TreeNode node, BufferCursor pos)
    {
        var piece = node.Piece;
        int originalLFCnt = piece.LineFeedCount;
        int originalEndOffset = OffsetInBuffer(piece.BufferIndex, piece.End);

        var newEnd = pos;
        int newEndOffset = OffsetInBuffer(piece.BufferIndex, newEnd);
        int newLineFeedCnt = GetLineFeedCnt(piece.BufferIndex, piece.Start, newEnd);

        int lf_delta = newLineFeedCnt - originalLFCnt;
        int size_delta = newEndOffset - originalEndOffset;
        int newLength = piece.Length + size_delta;

        node.Piece = new Piece(
            piece.BufferIndex,
            piece.Start,
            newEnd,
            newLineFeedCnt,
            newLength
        );

        _pieceTree.UpdateTreeMetadata(node, size_delta, lf_delta);
    }

    private void DeleteNodeHead(TreeNode node, BufferCursor pos)
    {
        var piece = node.Piece;
        var originalLFCnt = piece.LineFeedCount;
        var originalStartOffset = OffsetInBuffer(piece.BufferIndex, piece.Start);

        var newStart = pos;
        var newLineFeedCnt = GetLineFeedCnt(piece.BufferIndex, newStart, piece.End);
        var newStartOffset = OffsetInBuffer(piece.BufferIndex, newStart);
        var lf_delta = newLineFeedCnt - originalLFCnt;
        var size_delta = originalStartOffset - newStartOffset;
        var newLength = piece.Length + size_delta;
        node.Piece = new Piece(
            piece.BufferIndex,
            newStart,
            piece.End,
            newLineFeedCnt,
            newLength
        );

        _pieceTree.UpdateTreeMetadata(node, size_delta, lf_delta);
    }

    private void ShrinkNode(TreeNode node, BufferCursor start, BufferCursor end)
    {
        var piece = node.Piece;
        var originalStartPos = piece.Start;
        var originalEndPos = piece.End;

        // old piece, originalStartPos, start
        var oldLength = piece.Length;
        var oldLFCnt = piece.LineFeedCount;
        var newEnd = start;
        var newLineFeedCnt = GetLineFeedCnt(piece.BufferIndex, piece.Start, newEnd);
        var newLength = OffsetInBuffer(piece.BufferIndex, start) - OffsetInBuffer(piece.BufferIndex, originalStartPos);

        node.Piece = new Piece(
            piece.BufferIndex,
            piece.Start,
            newEnd,
            newLineFeedCnt,
            newLength
        );

        _pieceTree.UpdateTreeMetadata(node, newLength - oldLength, newLineFeedCnt - oldLFCnt);

        // new right piece, end, originalEndPos
        var newPiece = new Piece(
            piece.BufferIndex,
            end,
            originalEndPos,
            GetLineFeedCnt(piece.BufferIndex, end, originalEndPos),
            OffsetInBuffer(piece.BufferIndex, originalEndPos) - OffsetInBuffer(piece.BufferIndex, end)
        );

        var newNode = _pieceTree.InsertRight(node, new PieceNodeData(newPiece));
        ValidateCRLFWithPrevNode(newNode);
    }

    private void AppendToNode(TreeNode node, string value)
    {
        if (AdjustCarriageReturnFromNext(value, node))
            value += '\n';

        bool hitCRLF = ShouldCheckCRLF && StartWithLF(value) && EndWithCR(node);
        ref var changeBuffer = ref CollectionsMarshal.AsSpan(_buffers)[0];
        int startOffset = changeBuffer.Text.Length;
        if (hitCRLF)
        {
            var prevStartOffset = changeBuffer.LineStarts[changeBuffer.LineStarts.Length - 2];
            changeBuffer.RemoveLastLineStart();
            // _lastChangeBufferPos is already wrong
            _lastChangeBufferPos = new BufferCursor
            {
                Line = _lastChangeBufferPos.Line - 1,
                Column= startOffset - prevStartOffset
            };
        }

        changeBuffer.AppendText(value);
        int endIndex = changeBuffer.LineStarts.Length - 1;
        int endColumn = changeBuffer.Text.Length - changeBuffer.LineStarts[endIndex];
        var newEnd = new BufferCursor
        {
            Line = endIndex,
            Column = endColumn
        };
        int newLength = node.Piece.Length + value.Length;
        int oldLineFeedCnt = node.Piece.LineFeedCount;
        int newLineFeedCnt = GetLineFeedCnt(0, node.Piece.Start, newEnd);
        int lf_delta = newLineFeedCnt - oldLineFeedCnt;

        node.Piece = new Piece(
            node.Piece.BufferIndex,
            node.Piece.Start,
            newEnd,
            newLineFeedCnt,
            newLength
        );

        _lastChangeBufferPos = newEnd;
        _pieceTree.UpdateTreeMetadata(node, value.Length, lf_delta);
    }

    private NodePosition NodeAt(int offset)
    {
        TreeNode x = _pieceTree.Root;
        CacheEntry? cache = _searchCache.Get(offset);
        if (cache is not null)
        {
            return new NodePosition
            {
                Node = cache.Node,
                NodeStartOffset = cache.NodeStartOffset,
                Remainder = offset - cache.NodeStartOffset
            };
        }

        int nodeStartOffset = 0;

        while (!x.IsSentinel)
        {
            if (x.SizeLeft > offset)
            {
                x = x.Left;
            }
            else if (x.SizeLeft + x.Piece.Length >= offset)
            {
                nodeStartOffset += x.SizeLeft;
                NodePosition ret = new()
                {
                    Node = x,
                    Remainder = offset - x.SizeLeft,
                    NodeStartOffset = nodeStartOffset
                };
                // TODO: consider making NodePosition an interface
                _searchCache.Set(new CacheEntry()
                {
                    Node = x,
                    NodeStartLineNumber = ret.Remainder,
                    NodeStartOffset = ret.NodeStartOffset
                });
                return ret;
            }
            else
            {
                offset -= x.SizeLeft + x.Piece.Length;
                nodeStartOffset += x.SizeLeft + x.Piece.Length;
                x = x.Right;
            }
        }

        return null!;
    }

    private NodePosition NodeAt2(int lineNumber, int column)
    {
        var x = _pieceTree.Root;
        int nodeStartOffset = 0;

        while (!x.IsSentinel)
        {
            if (!x.Left.IsSentinel && x.LfLeft >= lineNumber - 1)
            {
                x = x.Left;
            }
            else if (x.LfLeft + x.Piece.LineFeedCount > lineNumber - 1)
            {
                int prevAccumualtedValue = GetAccumulatedValue(x, lineNumber - x.LfLeft - 2);
                int accumulatedValue = GetAccumulatedValue(x, lineNumber - x.LfLeft - 1);
                nodeStartOffset += x.SizeLeft;

                return new NodePosition
                {
                    Node = x,
                    Remainder = Math.Min(prevAccumualtedValue + column - 1, accumulatedValue),
                    NodeStartOffset = nodeStartOffset
                };
            }
            else if (x.LfLeft + x.Piece.LineFeedCount == lineNumber - 1)
            {
                var prevAccumualtedValue = GetAccumulatedValue(x, lineNumber - x.LfLeft - 2);
                if (prevAccumualtedValue + column - 1 <= x.Piece.Length)
                {
                    return new NodePosition
                    {
                        Node = x,
                        Remainder = prevAccumualtedValue + column - 1,
                        NodeStartOffset = nodeStartOffset
                    };
                }
                else
                {
                    column -= x.Piece.Length - prevAccumualtedValue;
                    break;
                }
            }
            else
            {
                lineNumber -= x.LfLeft + x.Piece.LineFeedCount;
                nodeStartOffset += x.SizeLeft + x.Piece.Length;
                x = x.Right;
            }
        }

        // search in order, to find the node contains position.column
        x = x.Next();
        while (x != TreeNode.Sentinel)
        {

            if (x.Piece.LineFeedCount > 0)
            {
                int accumulatedValue = GetAccumulatedValue(x, 0);
                return new NodePosition
                {
                    Node = x,
                    Remainder = Math.Min(column - 1, accumulatedValue),
                    NodeStartOffset = OffsetOfNode(x)
                };
            }
            else
            {
                if (x.Piece.Length >= column - 1)
                {
                    return new NodePosition
                    {
                        Node = x,
                        Remainder = column - 1,
                        NodeStartOffset = OffsetOfNode(x)
                    };
                }
                else
                {
                    column -= x.Piece.Length;
                }
            }

            x = x.Next();
        }

        return null!;
    }

    private int NodeCharCodeAt(TreeNode node, int offset)
    {
        if (node.Piece.LineFeedCount < 1)
            return -1;

        var newOffset = OffsetInBuffer(node.Piece.BufferIndex, node.Piece.Start) + offset;
        return _buffers[node.Piece.BufferIndex].Text[newOffset];
    }

    private int OffsetOfNode(TreeNode node)
    {
        if (node is null)
            return 0;

        int pos = node.SizeLeft;
        while (node != _pieceTree.Root)
        {
            if (node.Parent.Right == node)
            {
                pos += node.Parent.SizeLeft + node.Parent.Piece.Length;
            }

            node = node.Parent;
        }

        return pos;
    }

    #endregion

    #region CRLF

    private bool ShouldCheckCRLF { get => !(_EOLNormalized && _EOL == "\n"); }

    private bool StartWithLF(string str) => str[0] == '\n';

    private bool StartWithLF(TreeNode val)
    {
        if (val == TreeNode.Sentinel || val.Piece.LineFeedCount == 0)
            return false;

        var piece = val.Piece;
        var lineStarts = _buffers[piece.BufferIndex].LineStarts;
        int line = piece.Start.Line;
        int startOffset = lineStarts[line] + piece.Start.Column;

        if (line == lineStarts.Length - 1)
            // last line, so there is no line feed at the end of this line
            return false;

        int nextLineOffset = lineStarts[line + 1];
        if (nextLineOffset > startOffset + 1)
            return false;

        return _buffers[piece.BufferIndex].Text[startOffset] == 10;
    }

    private bool EndWithCR(string str)
    {
        return str[^1] == '\r';
    }

    private bool EndWithCR(ReadOnlySpan<char> span)
    {
        return span.Length > 0 && span[^1] == '\r';
    }

    private bool EndWithCR(TreeNode val)
    {
        if (val == TreeNode.Sentinel || val.Piece.LineFeedCount == 0)
            return false;

        return NodeCharCodeAt(val, val.Piece.Length - 1) == '\r';
    }

    private void ValidateCRLFWithPrevNode(TreeNode nextNode)
    {
        if (ShouldCheckCRLF && StartWithLF(nextNode))
        {
            var node = nextNode.Prev();
            if (EndWithCR(node))
            {
                FixCRLF(node, nextNode);
            }
        }
    }

    private void ValidateCRLFWithNextNode(TreeNode node)
    {
        if (ShouldCheckCRLF && EndWithCR(node))
        {
            var nextNode = node.Next();
            if (StartWithLF(nextNode))
            {
                FixCRLF(node, nextNode);
            }
        }
    }

    private void FixCRLF(TreeNode prev, TreeNode next)
    {
        List<TreeNode> nodesToDel = [];
        // update node
        var lineStarts = _buffers[prev.Piece.BufferIndex].LineStarts;
        BufferCursor newEnd;
        if (prev.Piece.End.Column == 0)
        {
            // it means, last line ends with \r, not \r\n
            newEnd = new BufferCursor
            {
                Line = prev.Piece.End.Line - 1,
                Column = lineStarts[prev.Piece.End.Line] - lineStarts[prev.Piece.End.Line - 1] - 1
            };
        }
        else
        {
            // \r\n
            newEnd = new BufferCursor
            {
                Line = prev.Piece.End.Line,
                Column = prev.Piece.End.Column - 1
            };
        }

        int prevNewLength = prev.Piece.Length - 1;
        int prevNewLFCnt = prev.Piece.LineFeedCount - 1;
        prev.Piece = new Piece(
            prev.Piece.BufferIndex,
            prev.Piece.Start,
            newEnd,
            prevNewLFCnt,
            prevNewLength
        );

        _pieceTree.UpdateTreeMetadata(prev, -1, -1);
        if (prev.Piece.Length == 0)
        {
            nodesToDel.Add(prev);
        }

        // update nextNode
        var newStart = new BufferCursor
        {
            Line = next.Piece.Start.Line + 1,
            Column = 0
        };
        int newLength = next.Piece.Length - 1;
        int newLineFeedCnt = GetLineFeedCnt(next.Piece.BufferIndex, newStart, next.Piece.End);
        next.Piece = new Piece(
            next.Piece.BufferIndex,
            newStart,
            next.Piece.End,
            newLineFeedCnt,
            newLength
        );

        _pieceTree.UpdateTreeMetadata(next, -1, -1);
        if (next.Piece.Length == 0)
        {
            nodesToDel.Add(next);
        }

        // create new piece which contains \r\n
        var pieces = CreateNewPieces("\r\n");
        _pieceTree.InsertRight(prev, new PieceNodeData(pieces[0]));
        // delete empty nodes

        for (int i = 0; i < nodesToDel.Count; i++)
        {
            _pieceTree.Delete(nodesToDel[i]);
        }
    }

    private bool AdjustCarriageReturnFromNext(string value, TreeNode node)
    {
        if (ShouldCheckCRLF && EndWithCR(value))
        {
            var nextNode = node.Next();
            if (StartWithLF(nextNode))
            {
                // move `\n` forward
                value += '\n';

                if (nextNode.Piece.Length == 1)
                {
                    _pieceTree.Delete(nextNode);
                }
                else
                {
                    var piece = nextNode.Piece;
                    var newStart = new BufferCursor
                    {
                        Line = piece.Start.Line + 1,
                        Column = 0
                    };
                    var newLength = piece.Length - 1;
                    var newLineFeedCnt = GetLineFeedCnt(piece.BufferIndex, newStart, piece.End);
                    nextNode.Piece = new Piece(
                        piece.BufferIndex,
                        newStart,
                        piece.End,
                        newLineFeedCnt,
                        newLength
                    );

                    _pieceTree.UpdateTreeMetadata(nextNode, -1, -1);
                }
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Tree Operations

    private string GetNodeContent(TreeNode node)
    {
        if (node.IsSentinel)
            return "";

        Piece piece = node.Piece;
        int startOffset = OffsetInBuffer(piece.BufferIndex, piece.Start);
        int endOffset = OffsetInBuffer(piece.BufferIndex, piece.End);
        return _buffers[node.Piece.BufferIndex].Text[startOffset..endOffset].ToString();
    }

    internal string GetPieceContent(Piece piece)
    {
        int startOffset = OffsetInBuffer(piece.BufferIndex, piece.Start);
        int endOffset = OffsetInBuffer(piece.BufferIndex, piece.End);
        return _buffers[piece.BufferIndex].Text[startOffset..endOffset].ToString();
    }

    private string GetContentOfSubTree(TreeNode node)
    {
        StringBuilder sb = new();
        PieceTree.IterateInOrder(node, node => {
            sb.Append(GetNodeContent(node));
            return true;
        });
        return sb.ToString();
    }

    #endregion

    #region Helpers

    /**
     * Assumes `operations` are validated and sorted ascending
     */
    internal static TextRange[] GetInverseEditRanges(ValidatedEditOperation[] operations)
    {
        var result = new TextRange[operations.Length];

        int prevOpEndLineNumber = 0;
        int prevOpEndColumn = 0;
        ValidatedEditOperation? prevOp = null;
        for (int i = 0, len = operations.Length; i < len; i++)
        {
            var op = operations[i];
            int startLineNumber, startColumn;

            if (prevOp is not null)
            {
                if(prevOp.Range.EndLineNumber == op.Range.StartLineNumber)
                {
                    startLineNumber = prevOpEndLineNumber;
                    startColumn = prevOpEndColumn + (op.Range.StartColumn - prevOp.Range.EndColumn);
                }
                else
                {
                    startLineNumber = prevOpEndLineNumber + (op.Range.StartLineNumber - prevOp.Range.EndLineNumber);
                    startColumn = op.Range.StartColumn;
                }
            }
            else
            {
                startLineNumber = op.Range.StartLineNumber;
                startColumn = op.Range.StartColumn;
            }

            TextRange resultRange;
            if (op.Text.Length > 0)
            {
                // the operation inserts something
                int lineCount = op.EOLCount + 1;
                if (lineCount == 1) // single line insert
                    resultRange = new TextRange(startLineNumber, startColumn, startLineNumber, startColumn + op.FirstLineLength);
                else // multi line insert
                    resultRange = new TextRange(startLineNumber, startColumn, startLineNumber + lineCount - 1, op.LastLineLength + 1);
            }
            else
            {
                // There is nothing to insert
                resultRange = new TextRange(startLineNumber, startColumn, startLineNumber, startColumn);
            }

            prevOpEndLineNumber = resultRange.EndLineNumber;
            prevOpEndColumn = resultRange.EndColumn;

            result[i] = resultRange;
            prevOp = op;
        }
        return result;
    }

    private static int SortOpsAscending(ValidatedEditOperation a, ValidatedEditOperation b)
    {
        int r = TextRange.CompareRangesUsingEnds(a.Range, b.Range);
        if (r == 0)
            return a.SortIndex - b.SortIndex;
        return r;
    }

    private static int SortOpsDescending(ValidatedEditOperation a, ValidatedEditOperation b)
    {
        int r = TextRange.CompareRangesUsingEnds(a.Range, b.Range);
        if (r == 0)
            return b.SortIndex - a.SortIndex;
        return -r;
    }

    #endregion
}

internal class NodePosition
{
    /// <summary>
    /// Piece index.
    /// </summary>
    public required TreeNode Node { get; init; }

    /// <summary>
    /// Remainder in current piece.
    /// </summary>
    public required int Remainder { get; init; }

    /// <summary>
    /// Node start offset in document.
    /// </summary>
    public required int NodeStartOffset { get; init; }
}

internal class CacheEntry
{
    public required TreeNode Node { get; init; }
    public required int NodeStartOffset { get; init; }
    public required int? NodeStartLineNumber { get; init; }
}
