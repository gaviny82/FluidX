using System.Text.RegularExpressions;

namespace FluidX.TextBuffers.PieceTree;

public partial class PieceTreeBase
{
    private const int AverageBufferSize = 65535; // 64 * 1024

    internal TreeNode Root { get; set; } = null!;

    protected List<StringBuffer> _buffers = null!; // 0 is change buffer, others are readonly original buffer.
    protected int _lineCount;
    protected int _length;
    protected string _EOL = "\n"; // Either "\r\n" or "\n"
    protected bool _EOLNormalized;

    private BufferCursor _lastChangeBufferPos;
    private PieceTreeSearchCache _searchCache = null!;
    private (int LineNumber, string Value) _lastVisitedLine;

    public PieceTreeBase(IList<StringBuffer> chunks, string eol, bool eolNormalized)
    {
        Create(chunks, eol, eolNormalized);
    }

    private void Create(IList<StringBuffer> chunks, string eol, bool eolNormalized)
    {
        _buffers = [
            new StringBuffer("", [0])
        ];
        _lastChangeBufferPos = new BufferCursor { Line = 0, Column = 0 };
        Root = TreeNode.Sentinel;
        _lineCount = 1;
        _length = 0;
        _EOL = eol;
        _EOLNormalized = eolNormalized;

        TreeNode? lastNode = null;
        for (int i = 0, len = chunks.Count; i < len; i++)
        {
            if (chunks[i].Buffer.Length > 0)
            {
                IReadOnlyList<int>? ithChunkLineStarts = chunks[i].LineStarts;
                if (ithChunkLineStarts is null)
                {
                    ithChunkLineStarts = LineStarts.CreateFast(chunks[i].Buffer);
                    chunks[i].LineStarts = ithChunkLineStarts;
                }

                var piece = new Piece(
                    i + 1,
                    new BufferCursor { Line = 0, Column = 0 },
                    new BufferCursor
                    {
                        Line = ithChunkLineStarts.Count - 1,
                        Column = chunks[i].Buffer.Length - ithChunkLineStarts[ithChunkLineStarts.Count - 1]
                    },
                    ithChunkLineStarts.Count - 1,
                    chunks[i].Buffer.Length
                );
                _buffers.Add(chunks[i]);
                lastNode = RbInsertRight(lastNode, piece);
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
        List<StringBuffer> chunks = [];

        Iterate(Root, node =>
        {
            string str = GetNodeContent(node);
            int len = str.Length;
            if (tempChunkLen <= min || tempChunkLen + len < max)
            {
                tempChunk += str;
                tempChunkLen += len;
                return true;
            }

            // Flush anyways
            string text = StringExtensions.EndOfLinesRegex.Replace(tempChunk, eol);
            chunks.Add(new StringBuffer(text, LineStarts.CreateFast(text)));
            tempChunk = str;
            tempChunkLen = len;
            return true;
        });

        if (tempChunkLen > 0)
        {
            string text = StringExtensions.EndOfLinesRegex.Replace(tempChunk, eol);
            chunks.Add(new StringBuffer(text, LineStarts.CreateFast(text)));
        }

        Create(chunks, eol, true);
    }

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

    public ITextSnapshot CreateSnapshot(string BOM)
        => new PieceTreeSnapshot(this, BOM);

    public bool Equals(PieceTreeBase other)
    {
        if (Length != other.Length || LineCount != other.LineCount)
            return false;

        int offset = 0;
        return Iterate(Root, node =>
        {
            if (node == TreeNode.Sentinel)
                return true;

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
        TreeNode x = Root;

        while (x != TreeNode.Sentinel)
        {
            if (x.Left != TreeNode.Sentinel && x.LfLeft + 1 >= lineNumber)
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

        TreeNode x = Root;
        int lfCnt = 0;
        int originalOffset = offset;

        while (x != TreeNode.Sentinel)
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

                if (x.Right == TreeNode.Sentinel)
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
        string buffer = _buffers[x.Piece.BufferIndex].Buffer;
        int startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);

        if (startPosition.Node == endPosition.Node)
            return buffer[(startOffset + startPosition.Remainder)..(startOffset + endPosition.Remainder)];

        // TODO: consider optimization using StringBuilder
        string ret = buffer[(startOffset + startPosition.Remainder)..(startOffset + x.Piece.Length)];

        x = x.Next();
        while (x != TreeNode.Sentinel)
        {
            buffer = _buffers[x.Piece.BufferIndex].Buffer;
            startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);

            if (x == endPosition.Node)
            {
                ret += buffer[startOffset..(startOffset + endPosition.Remainder)];
                break;
            }
            else
            {
                ret += buffer.Substring(startOffset, x.Piece.Length);
            }

            x = x.Next();
        }

        return ret;
    }

    public IReadOnlyList<string> GetLinesContent()
    {
        List<string> lines = [];
        string currentLine = "";
        bool danglingCR = false;

        Iterate(Root, node =>
        {
            if (node == TreeNode.Sentinel)
                return true;

            Piece piece = node.Piece;
            int pieceLength = piece.Length;
            if (pieceLength == 0)
                return true;

            string buffer = _buffers[piece.BufferIndex].Buffer;
            var lineStarts = _buffers[piece.BufferIndex].LineStarts;

            int pieceStartLine = piece.Start.Line;
            int pieceEndLine = piece.End.Line;
            int pieceStartOffset = lineStarts[pieceStartLine] + piece.Start.Column;

            if (danglingCR)
            {
                if (buffer[pieceStartOffset] == '\n')
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
                if (!_EOLNormalized && buffer[pieceStartOffset + pieceLength - 1] == '\r')
                {
                    danglingCR = true;
                    currentLine += buffer.Substring(pieceStartOffset, pieceLength - 1);
                }
                else
                {
                    currentLine += buffer.Substring(pieceStartOffset, pieceLength);
                }
                return true;
            }


            // add the text before the first line start in this piece
            currentLine += _EOLNormalized
                ? buffer[pieceStartOffset..Math.Max(pieceStartOffset, lineStarts[pieceStartLine + 1] - _EOL.Length)]
                : StringExtensions.EndOfLinesRegex.Replace(buffer[pieceStartOffset..lineStarts[pieceStartLine + 1]], "");
            lines.Add(currentLine);

            for (int line = pieceStartLine + 1; line < pieceEndLine; line++)
            {
                currentLine = _EOLNormalized
                    ? buffer[lineStarts[line]..(lineStarts[line + 1] - _EOL.Length)]
                    : StringExtensions.EndOfLinesRegex.Replace(buffer[lineStarts[line]..lineStarts[line + 1]], "");
                lines.Add(currentLine);
            }

            if (!_EOLNormalized && buffer[lineStarts[pieceEndLine] + piece.End.Column - 1] == '\r')
            {
                danglingCR = true;
                if (piece.End.Column == 0)
                    // The last line ended with a \r, let's undo the push, it will be pushed by next iteration
                    lines.RemoveAt(lines.Count - 1);
                else
                    currentLine = buffer.Substring(lineStarts[pieceEndLine], piece.End.Column - 1);
            }
            else
            {
                currentLine = buffer.Substring(lineStarts[pieceEndLine], piece.End.Column);
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

            var buffer = _buffers[matchingNode.Piece.BufferIndex];
            int startOffset = OffsetInBuffer(matchingNode.Piece.BufferIndex, matchingNode.Piece.Start);
            return buffer.Buffer[startOffset];
        }
        else
        {
            var buffer = _buffers[nodePos.Node.Piece.BufferIndex];
            int startOffset = OffsetInBuffer(nodePos.Node.Piece.BufferIndex, nodePos.Node.Piece.Start);
            int targetOffset = startOffset + nodePos.Remainder;

            return buffer.Buffer[targetOffset];
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
            if (matchingNode is null || matchingNode == TreeNode.Sentinel)
                return "";

            var buffer = _buffers[matchingNode.Piece.BufferIndex];
            var startOffset = OffsetInBuffer(matchingNode.Piece.BufferIndex, matchingNode.Piece.Start);
            return buffer.Buffer[startOffset..(startOffset + matchingNode.Piece.Length)];
        }
        else
        {
            var buffer = _buffers[nodePos.Node.Piece.BufferIndex];
            var startOffset = OffsetInBuffer(nodePos.Node.Piece.BufferIndex, nodePos.Node.Piece.Start);
            var targetOffset = startOffset + nodePos.Remainder;
            var targetEnd = startOffset + nodePos.Node.Piece.Length;
            return buffer.Buffer[targetOffset..targetEnd];
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
        var buffer = _buffers[node.Piece.BufferIndex];
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
            searchText = buffer.Buffer.Substring(start, end - start);
            offsetInBuffer = (int offset) => offset + start;
            searcher.Reset(0);
        }
        else
        {
            searchText = buffer.Buffer;
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

        if (Root != TreeNode.Sentinel)
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
                    RbInsertRight(node, newRightPiece);
                }

                var tmpNode = node;
                for (int k = 0; k < newPieces.Count; k++)
                {
                    tmpNode = RbInsertRight(tmpNode, newPieces[k]);
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
            var node = RbInsertLeft(null, pieces[0]);

            for (int k = 1; k < pieces.Count; k++)
            {
                node = RbInsertRight(node, pieces[k]);
            }
        }

        // TODO: this is too brutal. Total line feed count should be updated the same way as lf_left.
        ComputeBufferMetadata();
    }

    public void Delete(int offset, int cnt)
    {
        _lastVisitedLine.LineNumber = 0;
        _lastVisitedLine.Value = "";

        if (cnt <= 0 || Root == TreeNode.Sentinel)
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
                    RedBlackTreeHelper.RbDelete(this, startNode);
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
            RedBlackTreeHelper.UpdateTreeMetadata(this, node, -1, -1);

            if (node.Piece.Length == 0)
                nodesToDel.Add(node);
        }

        var newPieces = CreateNewPieces(value);
        var newNode = RbInsertLeft(node, newPieces[newPieces.Count - 1]);
        for (int k = newPieces.Count - 2; k >= 0; k--)
        {
            newNode = RbInsertLeft(newNode, newPieces[k]);
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
        var newNode = RbInsertRight(node, newPieces[0]);
        var tmpNode = newNode;

        for (int k = 1; k < newPieces.Count; k++)
        {
            tmpNode = RbInsertRight(tmpNode, newPieces[k]);
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
        if (end.Line == lineStarts.Count - 1)
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
        string buffer = _buffers[bufferIndex].Buffer;

        if (buffer[previousCharOffset] == 13)
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
            RedBlackTreeHelper.RbDelete(this, nodes[i]);
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
                _buffers.Add(new StringBuffer(splitText, lineStarts));
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
            _buffers.Add(new StringBuffer(text, lineStarts));

            return newPieces;
        }

        int startOffset = _buffers[0].Buffer.Length;
        lineStarts = LineStarts.CreateFast(text);

        var start = _lastChangeBufferPos;
        if (_buffers[0].LineStarts[_buffers[0].LineStarts.Count - 1] == startOffset
            && startOffset != 0
            && StartWithLF(text)
            && EndWithCR(_buffers[0].Buffer) // TODO: we can check this._lastChangeBufferPos's column as it's the last one
        )
        {
            _lastChangeBufferPos = new BufferCursor
            {
                Line = _lastChangeBufferPos.Line,
                Column = _lastChangeBufferPos.Column + 1
            };
            start = _lastChangeBufferPos;

            for (int i = 0; i < lineStarts.Count; i++)
            {
                lineStarts[i] += startOffset + 1;
            }

            _buffers[0].LineStarts = _buffers[0].LineStarts
                .Concat(lineStarts.Skip(1))
                .ToList();
            _buffers[0].Buffer += '_' + text;
            startOffset += 1;
        }
        else
        {
            if (startOffset != 0)
            {
                for (int i = 0; i < lineStarts.Count; i++)
                {
                    lineStarts[i] += startOffset;
                }
            }
            _buffers[0].LineStarts = _buffers[0].LineStarts
                .Concat(lineStarts.Skip(1))
                .ToList();
            _buffers[0].Buffer += text;
        }

        var endOffset = _buffers[0].Buffer.Length;
        var endIndex = _buffers[0].LineStarts.Count - 1;
        var endColumn = endOffset - _buffers[0].LineStarts[endIndex];
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

    public string GetLinesRawContent() => GetContentOfSubTree(Root);

    public string GetLineRawContent(int lineNumber, int endOffset = 0)
    {
        var x = Root;

        string ret = "";
        var cacheRet = _searchCache.Get2(lineNumber);
        if (cacheRet is (TreeNode cacheNode, int cacheNodeStartOffset, int cacheNodeStartLineNumber))
        {
            x = cacheNode;
            int prevAccumulatedValue = GetAccumulatedValue(x, lineNumber - cacheNodeStartLineNumber - 1);
            string buffer = _buffers[x.Piece.BufferIndex].Buffer;
            int startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);
            if (cacheNodeStartLineNumber + x.Piece.LineFeedCount == lineNumber)
            {
                ret = buffer[(startOffset + prevAccumulatedValue)..(startOffset + x.Piece.Length)];
            }
            else
            {
                int accumulatedValue = GetAccumulatedValue(x, lineNumber - cacheNodeStartLineNumber);
                return buffer[(startOffset + prevAccumulatedValue)..(startOffset + accumulatedValue - endOffset)];
            }
        }
        else // cache is null
        {
            int nodeStartOffset = 0;
            int originalLineNumber = lineNumber;
            while (x != TreeNode.Sentinel)
            {
                if (x.Left != TreeNode.Sentinel && x.LfLeft >= lineNumber - 1)
                {
                    x = x.Left;
                }
                else if (x.LfLeft + x.Piece.LineFeedCount > lineNumber - 1)
                {
                    int prevAccumulatedValue = GetAccumulatedValue(x, lineNumber - x.LfLeft - 2);
                    int accumulatedValue = GetAccumulatedValue(x, lineNumber - x.LfLeft - 1);
                    string buffer = _buffers[x.Piece.BufferIndex].Buffer;
                    var startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);
                    nodeStartOffset += x.SizeLeft;
                    _searchCache.Set(new CacheEntry
                    {
                        Node = x,
                        NodeStartOffset = nodeStartOffset,
                        NodeStartLineNumber = originalLineNumber - (lineNumber - 1 - x.LfLeft)
                    });

                    return buffer[(startOffset + prevAccumulatedValue)..(startOffset + accumulatedValue - endOffset)];
                }
                else if (x.LfLeft + x.Piece.LineFeedCount == lineNumber - 1)
                {
                    var prevAccumulatedValue = GetAccumulatedValue(x, lineNumber - x.LfLeft - 2);
                    var buffer = _buffers[x.Piece.BufferIndex].Buffer;
                    var startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);

                    ret = buffer[(startOffset + prevAccumulatedValue)..(startOffset + x.Piece.Length)];
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
            string buffer = _buffers[x.Piece.BufferIndex].Buffer;

            if (x.Piece.LineFeedCount > 0)
            {
                int accumulatedValue = GetAccumulatedValue(x, 0);
                int startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);

                ret += buffer[startOffset..(startOffset + accumulatedValue - endOffset)];
                return ret;
            }
            else
            {
                int startOffset = OffsetInBuffer(x.Piece.BufferIndex, x.Piece.Start);
                ret += buffer.Substring(startOffset, x.Piece.Length);
            }

            x = x.Next();
        }

        return ret;
    }

    private void ComputeBufferMetadata()
    {
        TreeNode x = Root;

        int lfCount = 1;
        int len = 0;

        while (x != TreeNode.Sentinel)
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

        RedBlackTreeHelper.UpdateTreeMetadata(this, node, size_delta, lf_delta);
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

        RedBlackTreeHelper.UpdateTreeMetadata(this, node, size_delta, lf_delta);
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

        RedBlackTreeHelper.UpdateTreeMetadata(this, node, newLength - oldLength, newLineFeedCnt - oldLFCnt);

        // new right piece, end, originalEndPos
        var newPiece = new Piece(
            piece.BufferIndex,
            end,
            originalEndPos,
            GetLineFeedCnt(piece.BufferIndex, end, originalEndPos),
            OffsetInBuffer(piece.BufferIndex, originalEndPos) - OffsetInBuffer(piece.BufferIndex, end)
        );

        var newNode = RbInsertRight(node, newPiece);
        ValidateCRLFWithPrevNode(newNode);
    }

    private void AppendToNode(TreeNode node, string value)
    {
        if (AdjustCarriageReturnFromNext(value, node))
            value += '\n';

        bool hitCRLF = ShouldCheckCRLF && StartWithLF(value) && EndWithCR(node);
        int startOffset = _buffers[0].Buffer.Length;
        _buffers[0].Buffer += value;
        var lineStarts = LineStarts.CreateFast(value);
        for (int i = 0; i < lineStarts.Count; i++)
        {
            lineStarts[i] += startOffset;
        }
        if (hitCRLF)
        {
            var prevStartOffset = _buffers[0].LineStarts[_buffers[0].LineStarts.Count - 2];
            var buffer0LineStarts = (List<int>)_buffers[0].LineStarts;
            buffer0LineStarts.RemoveAt(buffer0LineStarts.Count - 1);
            // _lastChangeBufferPos is already wrong
            _lastChangeBufferPos = new BufferCursor
            {
                Line = _lastChangeBufferPos.Line - 1,
                Column= startOffset - prevStartOffset
            };
        }

        _buffers[0].LineStarts = _buffers[0].LineStarts
            .Concat(lineStarts.Skip(1))
            .ToList();
        int endIndex = _buffers[0].LineStarts.Count - 1;
        int endColumn = _buffers[0].Buffer.Length - _buffers[0].LineStarts[endIndex];
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
        RedBlackTreeHelper.UpdateTreeMetadata(this, node, value.Length, lf_delta);
    }

    private NodePosition NodeAt(int offset)
    {
        TreeNode x = Root;
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

        while (x != TreeNode.Sentinel)
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
        var x = Root;
        int nodeStartOffset = 0;

        while (x != TreeNode.Sentinel)
        {
            if (x.Left != TreeNode.Sentinel && x.LfLeft >= lineNumber - 1)
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

        var buffer = _buffers[node.Piece.BufferIndex];
        var newOffset = OffsetInBuffer(node.Piece.BufferIndex, node.Piece.Start) + offset;
        return buffer.Buffer[newOffset];
    }

    private int OffsetOfNode(TreeNode node)
    {
        if (node is null)
            return 0;

        int pos = node.SizeLeft;
        while (node != Root)
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

        if (line == lineStarts.Count - 1)
            // last line, so there is no line feed at the end of this line
            return false;

        int nextLineOffset = lineStarts[line + 1];
        if (nextLineOffset > startOffset + 1)
            return false;

        return _buffers[piece.BufferIndex].Buffer[startOffset] == 10;
    }

    private bool EndWithCR(string str)
    {
        return str[^1] == '\r';
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

        RedBlackTreeHelper.UpdateTreeMetadata(this, prev, -1, -1);
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

        RedBlackTreeHelper.UpdateTreeMetadata(this, next, -1, -1);
        if (next.Piece.Length == 0)
        {
            nodesToDel.Add(next);
        }

        // create new piece which contains \r\n
        var pieces = CreateNewPieces("\r\n");
        RbInsertRight(prev, pieces[0]);
        // delete empty nodes

        for (int i = 0; i < nodesToDel.Count; i++)
        {
            RedBlackTreeHelper.RbDelete(this, nodesToDel[i]);
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
                    RedBlackTreeHelper.RbDelete(this, nextNode);
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

                    RedBlackTreeHelper.UpdateTreeMetadata(this, nextNode, -1, -1);
                }
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Tree Operations

    internal static bool Iterate(TreeNode node, Func<TreeNode, bool> callback)
    {
        if (node == TreeNode.Sentinel)
            return callback(TreeNode.Sentinel);

        if (!Iterate(node.Left, callback))
            return false;

        return callback(node) && Iterate(node.Right, callback);
    }

    private string GetNodeContent(TreeNode node)
    {
        if (node == TreeNode.Sentinel)
            return "";

        StringBuffer buffer = _buffers[node.Piece.BufferIndex];
        Piece piece = node.Piece;
        int startOffset = OffsetInBuffer(piece.BufferIndex, piece.Start);
        int endOffset = OffsetInBuffer(piece.BufferIndex, piece.End);
        string currentContent = buffer.Buffer[startOffset..endOffset];
        return currentContent;
    }

    internal string GetPieceContent(Piece piece)
    {
        var buffer = _buffers[piece.BufferIndex];
        int startOffset = OffsetInBuffer(piece.BufferIndex, piece.Start);
        int endOffset = OffsetInBuffer(piece.BufferIndex, piece.End);
        string currentContent = buffer.Buffer[startOffset..endOffset];
        return currentContent;
    }

    /**
     *      node              node
     *     /  \              /  \
     *    a   b    <----   a    b
     *                         /
     *                        z
     */
    private TreeNode RbInsertRight(TreeNode? node, Piece p)
    {
        var z = new TreeNode(p, NodeColor.Red)
        {
            Left = TreeNode.Sentinel,
            Right = TreeNode.Sentinel,
            Parent = TreeNode.Sentinel,
            SizeLeft = 0,
            LfLeft = 0
        };

        var x = Root;
        if (x == TreeNode.Sentinel)
        {
            Root = z;
            z.Color = NodeColor.Black;
        }
        else if (node!.Right == TreeNode.Sentinel)
        {
            node!.Right = z;
            z.Parent = node!;
        }
        else
        {
            var nextNode = RedBlackTreeHelper.Leftest(node!.Right);
            nextNode.Left = z;
            z.Parent = nextNode;
        }

        RedBlackTreeHelper.FixInsert(this, z);
        return z;
    }

    /**
     *      node              node
     *     /  \              /  \
     *    a   b     ---->   a    b
     *                       \
     *                        z
     */
    private TreeNode RbInsertLeft(TreeNode? node, Piece p)
    {
        var z = new TreeNode(p, NodeColor.Red)
        {
            Left = TreeNode.Sentinel,
            Right = TreeNode.Sentinel,
            Parent = TreeNode.Sentinel,
            SizeLeft = 0,
            LfLeft = 0
        };

        if (Root == TreeNode.Sentinel)
        {
            Root = z;
            z.Color = NodeColor.Black;
        }
        else if (node!.Left == TreeNode.Sentinel)
        {
            node!.Left = z;
            z.Parent = node!;
        }
        else
        {
            var prevNode = RedBlackTreeHelper.Rightest(node!.Left); // a
            prevNode.Right = z;
            z.Parent = prevNode;
        }

        RedBlackTreeHelper.FixInsert(this, z);
        return z;
    }

    private string GetContentOfSubTree(TreeNode node)
    {
        string str = "";

        Iterate(node, node => {
            str += GetNodeContent(node);
            return true;
        });

        return str;
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

public class StringBuffer
{
    public string Buffer { get; set; }
    public IReadOnlyList<int> LineStarts { get; set; }

    public StringBuffer(string buffer, IReadOnlyList<int> lineStarts)
    {
        Buffer = buffer;
        LineStarts = lineStarts;
    }
}

// TODO: Confirm 0-based or 1-based indexing
/// <summary>
/// A position in a text buffer
/// </summary>
/// <param name="Line">Line number in current buffer</param>
/// <param name="Column">Column number in current buffer</param>
internal readonly record struct BufferCursor(int Line, int Column);

internal record class Piece(int BufferIndex, BufferCursor Start, BufferCursor End, int LineFeedCount, int Length);

internal class CacheEntry
{
    public required TreeNode Node { get; init; }
    public required int NodeStartOffset { get; init; }
    public required int? NodeStartLineNumber { get; init; }
}
