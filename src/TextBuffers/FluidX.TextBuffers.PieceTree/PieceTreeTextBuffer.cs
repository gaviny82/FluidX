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

    protected StringBufferCollection _buffers; // 0 is change buffer, others are readonly original buffer.
    protected int _lineCount;
    protected int _length;
    protected string _EOL = "\n"; // Either "\r\n" or "\n"
    protected bool _EOLNormalized;

    private BufferCursor _lastChangeBufferPos;
    private PieceTreeSearchCache _searchCache = null!;
    private (int LineIndex, string Value) _lastVisitedLine = (-1, string.Empty);

    private PieceTreeTextBuffer(
        IList<InlineStringBuffer> chunks,
        string eol,
        LineStarts? initialLineStarts = null)
    {
        Initialize(chunks, eol, initialLineStarts);
    }

    /// <summary>
    /// Rebuilds the buffer with the given chunks and derives whether every stored line
    /// break uses <paramref name="eol"/>. This deliberately keeps the flag private and
    /// intake-computed, rather than trusting a caller-provided hint.
    /// </summary>
    private void Initialize(IList<InlineStringBuffer> chunks, string eol, LineStarts? initialLineStarts = null)
    {
        _buffers = new StringBufferCollection();
        _buffers.Add(new InlineStringBuffer());
        _lastChangeBufferPos = new BufferCursor { LineIndex = 0, ColumnIndex = 0 };
        _lineCount = 1;
        _length = 0;
        _EOL = eol;
        _EOLNormalized = initialLineStarts is null
            ? AreChunksEOLNormalized(chunks, eol)
            : !NeedsNormalization(eol, initialLineStarts.CR, initialLineStarts.LF, initialLineStarts.CRLF);

        TreeNode? lastNode = null;
        for (int i = 0, len = chunks.Count; i < len; i++)
        {
            if (chunks[i].Text.Length > 0)
            {
                var ithChunkLineStarts = chunks[i].LineStarts;

                var piece = new Piece(
                    i + 1,
                    new BufferCursor { LineIndex = 0, ColumnIndex = 0 },
                    new BufferCursor
                    {
                        LineIndex = ithChunkLineStarts.Length - 1,
                        ColumnIndex = chunks[i].Text.Length - ithChunkLineStarts[^1]
                    },
                    ithChunkLineStarts.Length - 1,
                    chunks[i].Text.Length
                );
                _buffers.Add(chunks[i]);
                lastNode = _pieceTree.InsertRight(lastNode, new PieceNodeData(piece));
            }
        }

        _searchCache = new PieceTreeSearchCache(1);
        _lastVisitedLine = (-1, "");
        ComputeBufferMetadata();
    }

    private static bool AreChunksEOLNormalized(IList<InlineStringBuffer> chunks, string eol)
    {
        bool pendingCR = false;

        foreach (InlineStringBuffer chunk in chunks)
        {
            foreach (char character in chunk.Text)
            {
                if (pendingCR)
                {
                    if (character == '\n')
                    {
                        if (eol != "\r\n")
                            return false;
                        pendingCR = false;
                        continue;
                    }

                    // A bare CR is never one of the two supported normalized forms.
                    return false;
                }

                if (character == '\r')
                {
                    pendingCR = true;
                }
                else if (character == '\n' && eol != "\n")
                {
                    return false;
                }
            }
        }

        return !pendingCR;
    }

    #region Creation

    // Steps for initializing a PieceTreeTextBuffer:
    // 1. Compute line starts and count CR/LF/CRLF EOLs
    // 2. Determine the internal fast-path EOL
    // 3. Store content as-is and derive the private EOL normalization status

    /// <summary>
    /// Creates a buffer from the given text.
    /// </summary>
    /// <param name="text">Raw content. The caller must remove any BOM before calling this method.</param>
    /// <param name="defaultEOL">Fallback end-of-line used when the text contains no line breaks.</param>
    public static PieceTreeTextBuffer Create(
        ReadOnlySpan<char> text,
        DefaultEndOfLine defaultEOL = DefaultEndOfLine.LF)
    {
        char[] content = text.ToArray();

        // Delegates the rest of the steps to CreateCore
        return CreateCore(content, defaultEOL);
    }

    /// <summary>
    /// Asynchronously creates a buffer from a stream.
    /// The stream is left open after reading.
    /// </summary>
    /// <param name="stream">Stream containing raw text. The caller must remove any BOM before calling this method.</param>
    /// <param name="defaultEOL">Fallback end-of-line used when the text contains no line breaks.</param>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    public static async Task<PieceTreeTextBuffer> CreateAsync(
        Stream stream,
        DefaultEndOfLine defaultEOL = DefaultEndOfLine.LF,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(
            stream,
            encoding: null,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);

        // Read the rest of the stream

        // It is impossible to pre-allocate the exact number of char even for a seekable stream,
        // because the encoding may be variable-length (e.g., UTF-8).
        //
        // FUTURE: For UTF-16 and UTF-32, we could pre-allocate reliably and avoid the 2x memory overhead of StringBuilder.
        // For UTF-8, we could pre-allocate the upper bound (stream length in bytes) and then shrink the array after reading.
        // This is optimal for common ASCII files, but not for files with many multi-byte characters.

        var sb = new StringBuilder();
        char[] scratch = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(scratch, cancellationToken).ConfigureAwait(false)) > 0)
            sb.Append(scratch, 0, read);
        char[] content = new char[sb.Length];
        sb.CopyTo(0, content, 0, sb.Length);

        // Delegates the rest of the steps to CreateCore
        return CreateCore(content, defaultEOL);
    }

    /// <summary>
    private static PieceTreeTextBuffer CreateCore(
        char[] content,
        DefaultEndOfLine defaultEOL)
    {
        // Step 3: Count CR/LF/CRLF EOLs
        var lineStarts = LineStarts.Create(content);

        // Step 7: Determine EOL to use
        string eol = DetermineEOL(defaultEOL, lineStarts.CR, lineStarts.LF, lineStarts.CRLF);

        // Step 8: Content is stored as-is. Initialize derives normalization status
        // from the stored chunks (mixed breaks -> slower read paths).
        var buffer = new InlineStringBuffer(content, lineStarts.Starts);

        return new PieceTreeTextBuffer(
            [buffer],
            eol,
            lineStarts);
    }

    private static string DetermineEOL(DefaultEndOfLine defaultEOL, int cr, int lf, int crlf)
    {
        int totalEOLCount = cr + lf + crlf;
        int totalCRCount = cr + crlf;
        if (totalEOLCount == 0)
            return defaultEOL == DefaultEndOfLine.LF ? "\n" : "\r\n";
        if (totalCRCount > totalEOLCount / 2)
            return "\r\n";
        return "\n";
    }

    private static bool NeedsNormalization(string eol, int cr, int lf, int crlf)
        => (eol == "\r\n" && (cr > 0 || lf > 0)) || (eol == "\n" && (cr > 0 || crlf > 0));

    #endregion

    /// <summary>
    /// Rewrites every line break in the buffer to the given EOL sequence,
    /// rebuilding the piece tree in the process.
    /// </summary>
    public void NormalizeEOL(string eol)
    {
        if (eol != "\n" && eol != "\r\n")
            throw new ArgumentException("Invalid EOL value");

        if (_EOLNormalized && _EOL == eol)
            return; // already normalized to the target

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

        Initialize(chunks, eol);
    }

    #region IReadOnlyTextBuffer Members

    public event EventHandler? OnDpiChangeContent;

    public ITextSnapshot CreateSnapshot(bool preserveBOM)
        => new PieceTreeSnapshot(this);

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
        return new TextRange(startPosition.LineIndex, startPosition.ColumnIndex, endPosition.LineIndex, endPosition.ColumnIndex);
    }

    public string GetTextInRange(TextRange range)
    {
        if (range.IsEmpty)
            return "";
        return GetValueInRange2(
            NodeAt2(range.StartLineIndex, range.StartColumnIndex),
            NodeAt2(range.EndLineIndex, range.EndColumnIndex));
    }

    public int GetTextLengthInRange(TextRange range)
    {
        if (range.IsEmpty)
            return 0;

        if (range.StartLineIndex == range.EndLineIndex)
        {
            return (range.EndColumnIndex - range.StartColumnIndex);
        }

        int startOffset = GetOffsetAt(range.StartLineIndex, range.StartColumnIndex);
        int endOffset = GetOffsetAt(range.EndLineIndex, range.EndColumnIndex);

        return endOffset - startOffset;
    }

    public int GetLineFirstNonWhitespaceColumnIndex(int lineIndex)
    {
        int result = GetLineContent(lineIndex).FirstNonWhitespaceIndex();
        if (result == -1)
            return -1;
        return result;
    }

    public int GetLineLastNonWhitespaceColumnIndex(int lineIndex)
    {
        int result = GetLineContent(lineIndex).LastNonWhitespaceIndex();
        if (result == -1)
            return -1;
        return result + 1;
    }

    #endregion

    #region Editing (ITextBuffer Members)

    public event EventHandler? OnDidChangeContent;

    // The text buffer applies replacements verbatim. Any EOL normalization of
    // replacement text is the caller's (TextModel's) responsibility, so the
    // actual changes made here always match the provided replacements. This
    // allows reverse edit computation to be lifted to TextModel.
    public ApplyEditsResult ApplyEdits(TextReplacement[] replacements, bool computeUndoEdits)
    {
        bool eolNormalized = _EOLNormalized;

        var operations = new ValidatedEditOperation[replacements.Length];
        for (int i = 0; i < replacements.Length; i++)
        {
            TextReplacement replacement = replacements[i];
            TextRange validatedRange = replacement.Range;
            string validText = "";
            int eolCount = 0;
            int firstLineLength = 0;
            int lastLineLength = 0;
            if (!string.IsNullOrEmpty(replacement.Text))
            {
                // Replacement text is stored verbatim; we only measure its line
                // structure. If its break kind differs from the buffer EOL, the
                // buffer becomes non-normalized and slower read paths apply.
                (eolCount, firstLineLength, lastLineLength, StringEndOfLine strEOL) =
                    EOLCounter.CountEOL(replacement.Text);

                if (strEOL != StringEndOfLine.Unknown)
                {
                    StringEndOfLine expectedStrEOL = (_EOL == "\r\n" ? StringEndOfLine.CRLF : StringEndOfLine.LF);
                    if (strEOL != expectedStrEOL)
                        eolNormalized = false;
                }

                validText = replacement.Text;
            }
            operations[i] = new ValidatedEditOperation
            {
                SortIndex = i,
                Range = validatedRange,
                RangeOffset = GetOffsetAt(validatedRange.StartLineIndex, validatedRange.StartColumnIndex),
                RangeLength = GetTextLengthInRange(validatedRange),
                Text = validText,
                EOLCount = eolCount,
                FirstLineLength = firstLineLength,
                LastLineLength = lastLineLength
            };
        }

        // A whole-buffer replacement is also the model's mechanism for changing
        // EOL policy. Re-establish the fast normalized path from the new raw text.
        if (operations.Length == 1 && operations[0].RangeOffset == 0 && operations[0].RangeLength == Length)
        {
            var (_, _, _, replacementEOL) = EOLCounter.CountEOL(operations[0].Text);
            if (replacementEOL is StringEndOfLine.LF or StringEndOfLine.CRLF)
            {
                _EOL = replacementEOL == StringEndOfLine.CRLF ? "\r\n" : "\n";
                eolNormalized = true;
            }
            else if (!operations[0].Text.Contains('\r') && !operations[0].Text.Contains('\n'))
            {
                eolNormalized = true;
            }
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

        // WithLineDelta encode operations
        TextRange[] reverseRanges = computeUndoEdits
            ? GetInverseEditRanges(operations)
            : [];
        //List<(int lineIndex, string oldContent)> newTrimAutoWhitespaceCandidates = [];
        //if (recordTrimAutoWhitespace)
        //{
        //    for (int i = 0; i < operations.Length; i++)
        //    {
        //        var op = operations[i];
        //        var reverseRange = reverseRanges[i];

        //        if (op.IsAutoWhitespaceEdit && op.Range.IsEmpty)
        //        {
        //            // Record already the future line indices that might be auto whitespace removal candidates on next edit
        //            for (int lineIndex = reverseRange.StartLineIndex; lineIndex < reverseRange.EndLineIndex; lineIndex++)
        //            {
        //                string currentLineContent = "";
        //                if (lineIndex == reverseRange.StartLineIndex)
        //                {
        //                    currentLineContent = GetLineContent(op.Range.StartLineIndex);
        //                    if (currentLineContent.FirstNonWhitespaceIndex() != -1)
        //                        continue;
        //                }
        //                newTrimAutoWhitespaceCandidates.Add((lineIndex, currentLineContent));
        //            }
        //        }
        //    }
        //}

        ReverseSingleEditOperation[]? reverseOperations = null;
        if (computeUndoEdits)
        {
            int reverseRangeDeltaOffset = 0;
            reverseOperations = new ReverseSingleEditOperation[operations.Length];
            for (int i = 0; i < operations.Length; i++)
            {
                ValidatedEditOperation op = operations[i];
                TextRange reverseRange = reverseRanges[i];
                string bufferText = GetTextInRange(op.Range);
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

        _EOLNormalized = eolNormalized;

        var contentChanges = DoApplyEdits(operations);

        //List<int>? trimAutoWhitespaceLineIndices = null;
        //if (recordTrimAutoWhitespace && newTrimAutoWhitespaceCandidates.Count > 0)
        //{
        //    // sort line indices auto whitespace removal candidates for next edit descending
        //    newTrimAutoWhitespaceCandidates.Sort((a, b) => b.lineIndex - a.lineIndex);

        //    trimAutoWhitespaceLineIndices = [];
        //    for (int i = 0, len = newTrimAutoWhitespaceCandidates.Count; i < len; i++)
        //    {
        //        int lineIndex = newTrimAutoWhitespaceCandidates[i].lineIndex;
        //        if (i > 0 && newTrimAutoWhitespaceCandidates[i - 1].lineIndex == lineIndex)
        //            continue; // Do not have the same line index twice

        //        string prevContent = newTrimAutoWhitespaceCandidates[i].oldContent;
        //        string lineContent = GetLineContent(lineIndex);

        //        if (lineContent.Length == 0 || lineContent == prevContent || lineContent.FirstNonWhitespaceIndex() != -1)
        //            continue;

        //        trimAutoWhitespaceLineIndices.Add(lineIndex);
        //    }
        //}

        OnDidChangeContent?.Invoke(this, EventArgs.Empty);

        return new ApplyEditsResult
        {
            ReverseEdits = reverseOperations,
            Changes = contentChanges
        };
    }

    /**
     * Transform operations such that they represent the same logic edit,
     * but that they also do not cause OOM crashes.
     */
    //private ValidatedEditOperation[] ReduceOperations(ValidatedEditOperation[] operations)
    //{
    //    if (operations.Length < 1000)
    //        return operations; // We know from empirical testing that a thousand edits work fine regardless of their shape.

    //    bool forceMoveMarkers = false;
    //    TextRange firstEditRange = operations[0].Range;
    //    TextRange lastEditRange = operations[^1].Range;
    //    TextRange entireEditRange = new(firstEditRange.StartLineIndex, firstEditRange.StartColumnIndex, lastEditRange.EndLineIndex, lastEditRange.EndColumnIndex);
    //    int lastEndLineIndex = firstEditRange.StartLineIndex;
    //    int lastEndColumnIndex = firstEditRange.StartColumnIndex;
    //    List<string> result = [];

    //    for (int i = 0, len = operations.Length; i < len; i++)
    //    {
    //        ValidatedEditOperation operation = operations[i];
    //        TextRange range = operation.Range;

    //        forceMoveMarkers = forceMoveMarkers || operation.ForceMoveMarkers;

    //        // (1) -- Push old text
    //        result.Add(GetValueInRange(new TextRange(lastEndLineIndex, lastEndColumnIndex, range.StartLineIndex, range.StartColumnIndex)));

    //        // (2) -- Push new text
    //        if (operation.Text.Length > 0)
    //            result.Add(operation.Text);

    //        lastEndLineIndex = range.EndLineIndex;
    //        lastEndColumnIndex = range.EndColumnIndex;
    //    }

    //    string text = string.Concat(result);
    //    var (eolCount, firstLineLength, lastLineLength, _) = EOLCounter.CountEOL(text);

    //    var combinedOperation = new ValidatedEditOperation
    //    {
    //        SortIndex = 0,
    //        Range = entireEditRange,
    //        RangeOffset = GetOffsetAt(entireEditRange.StartLineIndex, entireEditRange.StartColumnIndex),
    //        RangeLength = GetValueLengthInRange(entireEditRange, EndOfLinePreference.TextDefined),
    //        Text = text,
    //        EOLCount = eolCount,
    //        FirstLineLength = firstLineLength,
    //        LastLineLength = lastLineLength,
    //        ForceMoveMarkers = forceMoveMarkers,
    //        IsAutoWhitespaceEdit = false
    //    };

    //    // At one point, due to how events are emitted and how each operation is handled,
    //    // some operations can trigger a high amount of temporary string allocations,
    //    // that will immediately get edited again.
    //    // e.g. a formatter inserting ridiculous amounts of \n on a model with a single line
    //    // Therefore, the strategy is to collapse all the operations into a huge single edit operation
    //    return [combinedOperation];
    //}

    private List<InternalModelContentChange> DoApplyEdits(ValidatedEditOperation[] operations)
    {
        Array.Sort(operations, SortOpsDescending);
        List<InternalModelContentChange> contentChanges = [];

        // operations are from bottom to top
        for (int i = 0; i < operations.Length; i++)
        {
            ValidatedEditOperation op = operations[i];

            if (op.Range.StartLineIndex == op.Range.EndLineIndex
                && op.Range.StartColumnIndex == op.Range.EndColumnIndex
                && op.Text.Length == 0)
                continue; // no-op

            if (!string.IsNullOrEmpty(op.Text))
            {
                // replacement
                Delete(op.RangeOffset, op.RangeLength);
                Insert(op.RangeOffset, op.Text);
            }
            else
            {
                // deletion
                Delete(op.RangeOffset, op.RangeLength);
            }

            contentChanges.Add(new InternalModelContentChange
            {
                SortIndex = op.SortIndex,
                Range = op.Range,
                RangeLength = op.RangeLength,
                Text = op.Text,
                RangeOffset = op.RangeOffset
            });
        }
        return contentChanges;
    }

    #endregion

    #region Buffer API

    public bool Equals(PieceTreeTextBuffer? other)
    {
        if (other is null)
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

    public int GetOffsetAt(TextPosition position)
        => GetOffsetAt(position.LineIndex, position.ColumnIndex);

    private int GetOffsetAt(int lineIndex, int columnIndex)
    {
        int leftLen = 0; // inorder
        TreeNode x = _pieceTree.Root;

        while (!x.IsSentinel)
        {
            if (!x.Left.IsSentinel && x.LfLeft >= lineIndex)
            {
                x = x.Left;
            }
            else if (x.LfLeft + x.Piece.LineFeedCount >= lineIndex)
            {
                leftLen += x.SizeLeft;
                // Locate the preceding line break within this piece.
                int accumulatedValInCurrentIndex = GetAccumulatedValue(x, lineIndex - x.LfLeft - 1);
                return leftLen + accumulatedValInCurrentIndex + columnIndex;
            }
            else
            {
                lineIndex -= x.LfLeft + x.Piece.LineFeedCount;
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

                if (index == 0)
                {
                    int lineStartOffset = GetOffsetAt(lfCnt, 0);
                    return new TextPosition(lfCnt, originalOffset - lineStartOffset);
                }

                return new TextPosition(lfCnt, remainder);
            }
            else
            {
                offset -= x.SizeLeft + x.Piece.Length;
                lfCnt += x.LfLeft + x.Piece.LineFeedCount;

                if (x.Right.IsSentinel)
                {
                    // last node
                    int lineStartOffset = GetOffsetAt(lfCnt, 0);
                    int columnIndex = originalOffset - offset - lineStartOffset;
                    return new TextPosition(lfCnt, columnIndex);
                }
                else
                {
                    x = x.Right;
                }
            }
        }

        return new TextPosition(0, 0);
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

            int pieceStartLine = piece.Start.LineIndex;
            int pieceEndLine = piece.End.LineIndex;
            int pieceStartOffset = lineStarts[pieceStartLine] + piece.Start.ColumnIndex;

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

            if (!_EOLNormalized && buffer.Text[lineStarts[pieceEndLine] + piece.End.ColumnIndex - 1] == '\r')
            {
                danglingCR = true;
                if (piece.End.ColumnIndex == 0)
                    // The last line ended with a \r, let's undo the push, it will be pushed by next iteration
                    lines.RemoveAt(lines.Count - 1);
                else
                    currentLine = buffer.Text.Slice(lineStarts[pieceEndLine], piece.End.ColumnIndex - 1).ToString();
            }
            else
            {
                currentLine = buffer.Text.Slice(lineStarts[pieceEndLine], piece.End.ColumnIndex).ToString();
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

    public string GetLineContent(int lineIndex)
    {
        if (_lastVisitedLine.LineIndex == lineIndex)
            return _lastVisitedLine.Value;

        _lastVisitedLine.LineIndex = lineIndex;

        if (lineIndex == _lineCount - 1)
            _lastVisitedLine.Value = GetLineRawContent(lineIndex);
        else if (_EOLNormalized)
            _lastVisitedLine.Value = GetLineRawContent(lineIndex, _EOL.Length);
        else
            _lastVisitedLine.Value = StringExtensions.EndOfLinesRegex.Replace(GetLineRawContent(lineIndex), "");

        return _lastVisitedLine.Value;
    }

    public string GetLineEOL(int lineIndex)
    {
        int eolLength = GetEOLLengthAtLineBreak(lineIndex);
        if (eolLength == 0)
            return string.Empty;
        int nextLineOffset = GetOffsetAt(lineIndex + 1, 0);
        int eolOffset = nextLineOffset - eolLength;
        return GetValueInRange2(
            NodeAt(eolOffset),
            NodeAt(nextLineOffset));
    }

    private char GetChar(NodePosition nodePos)
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

    public char GetChar(TextPosition position)
        => GetChar(NodeAt2(position.LineIndex, position.ColumnIndex));

    public int GetLineLength(int lineIndex)
    {
        if (lineIndex == LineCount - 1)
        {
            int startOffset = GetOffsetAt(lineIndex, 0);
            return Length - startOffset;
        }
        int eolLength = _EOLNormalized
            ? _EOL.Length
            : GetEOLLengthAtLineBreak(lineIndex);
        return GetOffsetAt(lineIndex + 1, 0) - GetOffsetAt(lineIndex, 0) - eolLength;
    }

    /// <summary>
    /// Determines the actual number of EOL characters at the end of a line.
    /// Used for mixed-EOL buffers where <see name="_EOLNormalized"/> is false.
    /// </summary>
    private int GetEOLLengthAtLineBreak(int lineIndex)
    {
        if (lineIndex >= LineCount - 1)
            return 0; // last line has no trailing EOL

        int currentStart = GetOffsetAt(lineIndex, 0);
        int nextStart = GetOffsetAt(lineIndex + 1, 0);
        char lastChar = GetChar(nextStart - 1);
        if (lastChar == '\n')
        {
            if (nextStart - currentStart >= 2 && GetChar(nextStart - 2) == '\r')
                return 2; // \r\n
            return 1; // \n
        }
        return 1; // bare \r
    }

    public char GetChar(int offset)
    {
        var nodePos = NodeAt(offset);
        return GetChar(nodePos);
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

        var startPosition = NodeAt2(searchRange.StartLineIndex, searchRange.StartColumnIndex);
        if (startPosition is null)
            return [];
        var endPosition = NodeAt2(searchRange.EndLineIndex, searchRange.EndColumnIndex);
        if (endPosition is null)
            return [];
        var start = PositionInBuffer(startPosition.Node, startPosition.Remainder);
        var end = PositionInBuffer(endPosition.Node, endPosition.Remainder);

        if (startPosition.Node == endPosition.Node)
        {
            FindMatchesInNode(startPosition.Node, searcher, searchRange.StartLineIndex, searchRange.StartColumnIndex, start, end, searchData, captureMatches, limitResultCount, result);
            return result;
        }

        int startLineIndex = searchRange.StartLineIndex;
        var currentNode = startPosition.Node;
        int startColumnIndex;
        while (currentNode != endPosition.Node)
        {
            int lineBreakCnt = GetLineFeedCnt(currentNode.Piece.BufferIndex, start, currentNode.Piece.End);
            if (lineBreakCnt >= 1)
            {
                // last line break position
                var lineStarts = _buffers[currentNode.Piece.BufferIndex].LineStarts;
                int startOffsetInBuffer = OffsetInBuffer(currentNode.Piece.BufferIndex, currentNode.Piece.Start);
                int nextLineStartOffset = lineStarts[start.LineIndex + lineBreakCnt];
                startColumnIndex = startLineIndex == searchRange.StartLineIndex ? searchRange.StartColumnIndex : 0;
                FindMatchesInNode(currentNode, searcher, startLineIndex, startColumnIndex, start, PositionInBuffer(currentNode, nextLineStartOffset - startOffsetInBuffer), searchData, captureMatches, limitResultCount, result);
                if (result.Count >= limitResultCount)
                    return result;
                startLineIndex += lineBreakCnt;
            }

            startColumnIndex = startLineIndex == searchRange.StartLineIndex ? searchRange.StartColumnIndex : 0;
            // search for the remaining content
            if (startLineIndex == searchRange.EndLineIndex)
            {
                string text = GetLineContent(startLineIndex).Substring(startColumnIndex, searchRange.EndColumnIndex - startColumnIndex);
                FindMatchesInLine(searchData, searcher, text, searchRange.EndLineIndex, startColumnIndex, result, captureMatches, limitResultCount);
                return result;
            }
            FindMatchesInLine(searchData, searcher, GetLineContent(startLineIndex).Substring(startColumnIndex), startLineIndex, startColumnIndex, result, captureMatches, limitResultCount);
            if (result.Count >= limitResultCount)
                return result;
            startLineIndex++;
            startPosition = NodeAt2(startLineIndex, 0);
            currentNode = startPosition.Node;
            start = PositionInBuffer(startPosition.Node, startPosition.Remainder);
        }

        if (startLineIndex == searchRange.EndLineIndex)
        {
            startColumnIndex = startLineIndex == searchRange.StartLineIndex ? searchRange.StartColumnIndex : 0;
            string text = GetLineContent(startLineIndex).Substring(startColumnIndex, searchRange.EndColumnIndex - startColumnIndex);
            FindMatchesInLine(searchData, searcher, text, searchRange.EndLineIndex, startColumnIndex, result, captureMatches, limitResultCount);
            return result;
        }

        startColumnIndex = startLineIndex == searchRange.StartLineIndex ? searchRange.StartColumnIndex : 0;
        FindMatchesInNode(endPosition.Node, searcher, startLineIndex, startColumnIndex, start, end, searchData, captureMatches, limitResultCount, result);
        return result;
    }

    private void FindMatchesInNode(
        TreeNode node,
        Searcher searcher,
        int startLineIndex,
        int startColumnIndex,
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
                int retStartColumnIndex = ret1.LineIndex == startCursor.LineIndex ? ret1.ColumnIndex - startCursor.ColumnIndex + startColumnIndex : ret1.ColumnIndex;
                int retEndColumnIndex = retStartColumnIndex + m.Length;
                result.Add(SearchUtils.CreateFindMatch(new TextRange(startLineIndex + lineFeedCnt, retStartColumnIndex, startLineIndex + lineFeedCnt, retEndColumnIndex), [m], captureMatches));

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
        int lineIndex,
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
                    result.Add(new FindMatch(new TextRange(lineIndex, lastMatchIndex + deltaOffset, lineIndex, lastMatchIndex + searchString.Length + deltaOffset), null));
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
                    new TextRange(lineIndex, m.Index + deltaOffset, lineIndex, m.Index + m.Length + deltaOffset),
                    [m],
                    captureMatches)
                );
            }
        } while (m is not null);
    }

    #endregion

    #region Piece Table

    /// <summary>
    /// Evaluates the EOL kind of <paramref name="value"/> and degrades
    /// <see cref="_EOLNormalized"/> when the text introduces breaks that do not
    /// match the buffer's fast-path EOL. Whole-buffer replacements can upgrade it.
    /// </summary>
    private void UpdateEOLNormalized(string value)
    {
        if (!_EOLNormalized || string.IsNullOrEmpty(value))
            return;

        var (_, _, _, strEOL) = EOLCounter.CountEOL(value);
        if (strEOL == StringEndOfLine.Unknown)
            return; // no breaks in the inserted text

        StringEndOfLine expectedStrEOL = (_EOL == "\r\n" ? StringEndOfLine.CRLF : StringEndOfLine.LF);
        if (strEOL != expectedStrEOL)
            _EOLNormalized = false;
    }

    /// <summary>
    /// Inserts raw text. If the text contains line breaks that do not match the
    /// buffer's preferred EOL, the buffer degrades to non-normalized mode and
    /// slower read paths apply until a normalized whole-buffer replacement is applied.
    /// </summary>
    public void Insert(int offset, string value)
    {
        UpdateEOLNormalized(value);
        _lastVisitedLine.LineIndex = -1;
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
                piece.End.LineIndex == _lastChangeBufferPos.LineIndex &&
                piece.End.ColumnIndex == _lastChangeBufferPos.ColumnIndex &&
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
                            LineIndex = newRightPiece.Start.LineIndex + 1,
                            ColumnIndex = 0
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
        _lastVisitedLine.LineIndex = -1;
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
                LineIndex = piece.Start.LineIndex + 1,
                ColumnIndex = 0
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

        int startOffset = lineStarts[piece.Start.LineIndex] + piece.Start.ColumnIndex;

        int offset = startOffset + remainder;

        // binary search offset between startOffset and endOffset
        int low = piece.Start.LineIndex;
        int high = piece.End.LineIndex;

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
            LineIndex = mid,
            ColumnIndex = offset - midStart
        };
    }

    private int GetLineFeedCnt(int bufferIndex, BufferCursor start, BufferCursor end)
    {
        // we don't need to worry about start: abc\r|\n, or abc|\r, or abc|\n, or abc|\r\n doesn't change the fact that, there is one line break after start.
        // now let's take care of end: abc\r|\n, if end is in between \r and \n, we need to add line feed count by 1
        if (end.ColumnIndex == 0)
            return end.LineIndex - start.LineIndex;

        var lineStarts = _buffers[bufferIndex].LineStarts;
        if (end.LineIndex == lineStarts.Length - 1)
        {
            // it means, there is no \n after end, otherwise, there will be one more lineStart.
            return end.LineIndex - start.LineIndex;
        }

        int nextLineStartOffset = lineStarts[end.LineIndex + 1];
        int endOffset = lineStarts[end.LineIndex] + end.ColumnIndex;
        if (nextLineStartOffset > endOffset + 1)
        {
            // there are more than 1 character after end, which means it can't be \n
            return end.LineIndex - start.LineIndex;
        }
        // endOffset + 1 === nextLineStartOffset
        // character at endOffset is \n, so we check the character before first
        // if character at endOffset is \r, end.columnIndex is 0 and we can't get here.
        int previousCharOffset = endOffset - 1; // end.columnIndex > 0 so it's okay.

        if (_buffers[bufferIndex].Text[previousCharOffset] == 13)
            return end.LineIndex - start.LineIndex + 1;
        else
            return end.LineIndex - start.LineIndex;
    }

    private int OffsetInBuffer(int bufferIndex, BufferCursor cursor)
    {
        var lineStarts = _buffers[bufferIndex].LineStarts;
        return lineStarts[cursor.LineIndex] + cursor.ColumnIndex;
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
                    new BufferCursor { LineIndex = 0, ColumnIndex = 0 },
                    new BufferCursor
                    {
                        LineIndex = lineStarts.Count - 1,
                        ColumnIndex = splitText.Length - lineStarts[lineStarts.Count - 1]
                    },
                    lineStarts.Count - 1,
                    splitText.Length
                ));
                _buffers.Add(new InlineStringBuffer(splitText, lineStarts));
            }

            lineStarts = LineStarts.CreateFast(text);
            newPieces.Add(new Piece(
                _buffers.Count, /* buffer index */
                new BufferCursor { LineIndex = 0, ColumnIndex = 0 },
                new BufferCursor
                {
                    LineIndex = lineStarts.Count - 1,
                    ColumnIndex = text.Length - lineStarts[lineStarts.Count - 1]
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
        ref var changeBuffer = ref _buffers[0];
        if (changeBuffer.LineStarts[changeBuffer.LineStarts.Length - 1] == startOffset
            && startOffset != 0
            && StartWithLF(text)
            && EndWithCR(changeBuffer.Text) // TODO: we can check this._lastChangeBufferPos's columnIndex as it's the last one
        )
        {
            _lastChangeBufferPos = new BufferCursor
            {
                LineIndex = _lastChangeBufferPos.LineIndex,
                ColumnIndex = _lastChangeBufferPos.ColumnIndex + 1
            };
            start = _lastChangeBufferPos;

            changeBuffer.AppendText(['_']);
            changeBuffer.AppendText(text);
            startOffset += 1;
        }
        else
        {
            changeBuffer.AppendText(text);
        }

        var endOffset = changeBuffer.Text.Length;
        var endIndex = changeBuffer.LineStarts.Length - 1;
        var endColumnIndex = endOffset - changeBuffer.LineStarts[endIndex];
        var endPos = new BufferCursor
        {
            LineIndex = endIndex,
            ColumnIndex = endColumnIndex
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

    public string GetLineRawContent(int lineIndex, int endOffset = 0)
    {
        var x = _pieceTree.Root;

        string ret = "";
        var cacheRet = _searchCache.Get2(lineIndex);
        if (cacheRet is (TreeNode cacheNode, int cacheNodeStartOffset, int cacheNodeStartLineIndex))
        {
            x = cacheNode;
            int prevAccumulatedValue = GetAccumulatedValue(x, lineIndex - cacheNodeStartLineIndex - 1);
            int startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);
            if (cacheNodeStartLineIndex + x.Piece.LineFeedCount == lineIndex)
            {
                ret = _buffers[x.Piece.BufferIndex].Text.Slice(startOffset + prevAccumulatedValue, x.Piece.Length - prevAccumulatedValue).ToString();
            }
            else
            {
                int accumulatedValue = GetAccumulatedValue(x, lineIndex - cacheNodeStartLineIndex);
                return _buffers[x.Piece.BufferIndex].Text.Slice(startOffset + prevAccumulatedValue, accumulatedValue - endOffset - prevAccumulatedValue).ToString();
            }
        }
        else // cache is null
        {
            int nodeStartOffset = 0;
            int originalLineIndex = lineIndex;
            while (!x.IsSentinel)
            {
                if (!x.Left.IsSentinel && x.LfLeft >= lineIndex)
                {
                    x = x.Left;
                }
                else if (x.LfLeft + x.Piece.LineFeedCount > lineIndex)
                {
                    int prevAccumulatedValue = GetAccumulatedValue(x, lineIndex - x.LfLeft - 1);
                    int accumulatedValue = GetAccumulatedValue(x, lineIndex - x.LfLeft);
                    var startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);
                    nodeStartOffset += x.SizeLeft;
                    _searchCache.Set(new CacheEntry
                    {
                        Node = x,
                        NodeStartOffset = nodeStartOffset,
                        NodeStartLineIndex = originalLineIndex - (lineIndex - x.LfLeft)
                    });

                    return _buffers[x.Piece.BufferIndex].Text.Slice(startOffset + prevAccumulatedValue, accumulatedValue - endOffset - prevAccumulatedValue).ToString();
                }
                else if (x.LfLeft + x.Piece.LineFeedCount == lineIndex)
                {
                    var prevAccumulatedValue = GetAccumulatedValue(x, lineIndex - x.LfLeft - 1);
                    var startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);

                    ret = _buffers[x.Piece.BufferIndex].Text.Slice(startOffset + prevAccumulatedValue, x.Piece.Length - prevAccumulatedValue).ToString();
                    break;
                }
                else
                {
                    lineIndex -= x.LfLeft + x.Piece.LineFeedCount;
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
        int lineCnt = pos.LineIndex - piece.Start.LineIndex;

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

        return (lineCnt, pos.ColumnIndex);
    }

    private int GetAccumulatedValue(TreeNode node, int index)
    {
        if (index < 0)
            return 0;

        var piece = node.Piece;
        var lineStarts = _buffers[piece.BufferIndex].LineStarts;
        int expectedLineStartIndex = piece.Start.LineIndex + index + 1;
        if (expectedLineStartIndex > piece.End.LineIndex)
            return lineStarts[piece.End.LineIndex] + piece.End.ColumnIndex - lineStarts[piece.Start.LineIndex] - piece.Start.ColumnIndex;
        else
            return lineStarts[expectedLineStartIndex] - lineStarts[piece.Start.LineIndex] - piece.Start.ColumnIndex;
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
        ref var changeBuffer = ref _buffers[0];
        int startOffset = changeBuffer.Text.Length;
        if (hitCRLF)
        {
            var prevStartOffset = changeBuffer.LineStarts[changeBuffer.LineStarts.Length - 2];
            // _lastChangeBufferPos is already wrong
            _lastChangeBufferPos = new BufferCursor
            {
                LineIndex = _lastChangeBufferPos.LineIndex - 1,
                ColumnIndex= startOffset - prevStartOffset
            };
        }

        // AppendText handles the \r\n merge of the trailing \r with the leading \n.
        changeBuffer.AppendText(value);
        int endIndex = changeBuffer.LineStarts.Length - 1;
        int endColumnIndex = changeBuffer.Text.Length - changeBuffer.LineStarts[endIndex];
        var newEnd = new BufferCursor
        {
            LineIndex = endIndex,
            ColumnIndex = endColumnIndex
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
                    NodeStartLineIndex = null,
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

    private NodePosition NodeAt2(int lineIndex, int columnIndex)
    {
        var x = _pieceTree.Root;
        int nodeStartOffset = 0;

        while (!x.IsSentinel)
        {
            if (!x.Left.IsSentinel && x.LfLeft >= lineIndex)
            {
                x = x.Left;
            }
            else if (x.LfLeft + x.Piece.LineFeedCount > lineIndex)
            {
                int prevAccumualtedValue = GetAccumulatedValue(x, lineIndex - x.LfLeft - 1);
                int accumulatedValue = GetAccumulatedValue(x, lineIndex - x.LfLeft);
                nodeStartOffset += x.SizeLeft;

                return new NodePosition
                {
                    Node = x,
                    Remainder = Math.Min(prevAccumualtedValue + columnIndex, accumulatedValue),
                    NodeStartOffset = nodeStartOffset
                };
            }
            else if (x.LfLeft + x.Piece.LineFeedCount == lineIndex)
            {
                var prevAccumualtedValue = GetAccumulatedValue(x, lineIndex - x.LfLeft - 1);
                if (prevAccumualtedValue + columnIndex <= x.Piece.Length)
                {
                    return new NodePosition
                    {
                        Node = x,
                        Remainder = prevAccumualtedValue + columnIndex,
                        NodeStartOffset = nodeStartOffset
                    };
                }
                else
                {
                    columnIndex -= x.Piece.Length - prevAccumualtedValue;
                    break;
                }
            }
            else
            {
                lineIndex -= x.LfLeft + x.Piece.LineFeedCount;
                nodeStartOffset += x.SizeLeft + x.Piece.Length;
                x = x.Right;
            }
        }

        // search in order, to find the node contains position.columnIndex
        x = x.Next();
        while (x != TreeNode.Sentinel)
        {

            if (x.Piece.LineFeedCount > 0)
            {
                int accumulatedValue = GetAccumulatedValue(x, 0);
                return new NodePosition
                {
                    Node = x,
                    Remainder = Math.Min(columnIndex, accumulatedValue),
                    NodeStartOffset = OffsetOfNode(x)
                };
            }
            else
            {
                if (x.Piece.Length >= columnIndex)
                {
                    return new NodePosition
                    {
                        Node = x,
                        Remainder = columnIndex,
                        NodeStartOffset = OffsetOfNode(x)
                    };
                }
                else
                {
                    columnIndex -= x.Piece.Length;
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

    private bool StartWithLF(string str) => str.Length > 0 && str[0] == '\n';

    private bool StartWithLF(TreeNode val)
    {
        if (val == TreeNode.Sentinel || val.Piece.LineFeedCount == 0)
            return false;

        var piece = val.Piece;
        var lineStarts = _buffers[piece.BufferIndex].LineStarts;
        int line = piece.Start.LineIndex;
        int startOffset = lineStarts[line] + piece.Start.ColumnIndex;

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
        return str.Length > 0 && str[^1] == '\r';
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
        if (prev.Piece.End.ColumnIndex == 0)
        {
            // it means, last line ends with \r, not \r\n
            newEnd = new BufferCursor
            {
                LineIndex = prev.Piece.End.LineIndex - 1,
                ColumnIndex = lineStarts[prev.Piece.End.LineIndex] - lineStarts[prev.Piece.End.LineIndex - 1] - 1
            };
        }
        else
        {
            // \r\n
            newEnd = new BufferCursor
            {
                LineIndex = prev.Piece.End.LineIndex,
                ColumnIndex = prev.Piece.End.ColumnIndex - 1
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
            LineIndex = next.Piece.Start.LineIndex + 1,
            ColumnIndex = 0
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
                        LineIndex = piece.Start.LineIndex + 1,
                        ColumnIndex = 0
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

        int prevOpEndLineIndex = 0;
        int prevOpEndColumnIndex = 0;
        ValidatedEditOperation? prevOp = null;
        for (int i = 0, len = operations.Length; i < len; i++)
        {
            var op = operations[i];
            int startLineIndex, startColumnIndex;

            if (prevOp is not null)
            {
                if(prevOp.Range.EndLineIndex == op.Range.StartLineIndex)
                {
                    startLineIndex = prevOpEndLineIndex;
                    startColumnIndex = prevOpEndColumnIndex + (op.Range.StartColumnIndex - prevOp.Range.EndColumnIndex);
                }
                else
                {
                    startLineIndex = prevOpEndLineIndex + (op.Range.StartLineIndex - prevOp.Range.EndLineIndex);
                    startColumnIndex = op.Range.StartColumnIndex;
                }
            }
            else
            {
                startLineIndex = op.Range.StartLineIndex;
                startColumnIndex = op.Range.StartColumnIndex;
            }

            TextRange resultRange;
            if (op.Text.Length > 0)
            {
                // the operation inserts something
                int lineCount = op.EOLCount + 1;
                if (lineCount == 1) // single line insert
                    resultRange = new TextRange(startLineIndex, startColumnIndex, startLineIndex, startColumnIndex + op.FirstLineLength);
                else // multi line insert
                    resultRange = new TextRange(startLineIndex, startColumnIndex, startLineIndex + lineCount - 1, op.LastLineLength);
            }
            else
            {
                // There is nothing to insert
                resultRange = new TextRange(startLineIndex, startColumnIndex, startLineIndex, startColumnIndex);
            }

            prevOpEndLineIndex = resultRange.EndLineIndex;
            prevOpEndColumnIndex = resultRange.EndColumnIndex;

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
    public required int? NodeStartLineIndex { get; init; }
}
