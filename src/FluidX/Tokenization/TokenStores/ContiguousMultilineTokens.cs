using FluidX.TextBuffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace FluidX.Tokenization.TokenStores;

public class ContiguousMultilineTokens
{
    private int _startLineIndex;
    private List<LineToken[]> _tokens;

    public int StartLineIndex => _startLineIndex;
    public int EndLineIndex => _startLineIndex + _tokens.Count - 1;
    public Range LineRange => new(_startLineIndex, _startLineIndex + _tokens.Count);

    public ContiguousMultilineTokens(int startLineIndex, List<LineToken[]> tokens)
    {
        _startLineIndex = startLineIndex;
        _tokens = tokens;
    }

    public LineToken[] GetLineTokens(int lineIndex)
        => _tokens[lineIndex - _startLineIndex];

    public void AppendLineTokens(LineToken[] lineTokens)
        => _tokens.Add(lineTokens);

    #region Editing

    public void ApplyEdit(TextRange range, string text)
    {
        var result = EOLCounter.CountEOL(text);
        AcceptDeleteRange(range);
        AcceptInsertText(range.StartPosition, result.eolCount, result.firstLineLength);
    }

    private void AcceptDeleteRange(TextRange range)
    {
        if (range.IsEmpty)
            return; // Nothing to delete

        int firstLineIndex = range.StartLineIndex - _startLineIndex;
        int lastLineIndex = range.EndLineIndex - _startLineIndex;

        if (lastLineIndex < 0)
        {
            // this deletion occurs entirely before this block, so we only need to adjust line indices
            int deletedLinesCount = lastLineIndex - firstLineIndex;
            _startLineIndex -= deletedLinesCount;
            return;
        }

        if (firstLineIndex >= _tokens.Count)
            return; // this deletion occurs entirely after this block, so there is nothing to do

        if (firstLineIndex < 0 && lastLineIndex >= _tokens.Count)
        {
            // this deletion completely encompasses this block
            _startLineIndex = 0;
            _tokens.Clear();
            return;
        }

        if (firstLineIndex == lastLineIndex)
        {
            // a delete on a single line
            _tokens[firstLineIndex] = ContiguousTokensEditing.Delete(_tokens[firstLineIndex], range.StartColumnIndex, range.EndColumnIndex);
            return;
        }

        if (firstLineIndex >= 0)
        {
            // The first line survives
            _tokens[firstLineIndex] = ContiguousTokensEditing.DeleteEnding(_tokens[firstLineIndex], range.StartColumnIndex);

            if (lastLineIndex < _tokens.Count)
            {
                // The last line survives
                var lastLineTokens = ContiguousTokensEditing.DeleteBeginning(_tokens[lastLineIndex], range.EndColumnIndex);

                // Take remaining text on last line and append it to remaining text on first line
                _tokens[firstLineIndex] = ContiguousTokensEditing.Append(_tokens[firstLineIndex], lastLineTokens);

                // Delete middle lines
                _tokens.RemoveRange(firstLineIndex + 1, lastLineIndex - firstLineIndex);
            }
            else
            {
                // The last line does not survive

                // Take remaining text on last line and append it to remaining text on first line
                _tokens[firstLineIndex] = ContiguousTokensEditing.Append(_tokens[firstLineIndex], null);

                // Delete lines
                _tokens = _tokens[0..(firstLineIndex + 1)];
            }
        }
        else
        {
            // The first line does not survive

            int deletedBefore = -firstLineIndex;
            _startLineIndex -= deletedBefore;

            // Remove beginning from last line
            _tokens[lastLineIndex] = ContiguousTokensEditing.DeleteBeginning(_tokens[lastLineIndex], range.EndColumnIndex);

            // Delete lines
            _tokens = _tokens[lastLineIndex..];
        }
    }

    private void AcceptInsertText(TextPosition position, int eolCount, int firstLineLength)
    {
        if (eolCount == 0 && firstLineLength == 0)
            return; // Noting to insert

        int lineIndex = position.LineIndex - _startLineIndex;
        if (lineIndex < 0)
        {
            // this insertion occurs before this block, so we only need to adjust line indices
            _startLineIndex += eolCount;
            return;
        }
        if (lineIndex >= _tokens.Count)
        {
            // this insertion occurs after this block, so there is nothing to do
            return;
        }

        if (eolCount == 0)
        {
            // Inserting text on one line
            _tokens[lineIndex] = ContiguousTokensEditing.Insert(_tokens[lineIndex], position.ColumnIndex, firstLineLength);
            return;
        }

        _tokens[lineIndex] = ContiguousTokensEditing.DeleteEnding(_tokens[lineIndex], position.ColumnIndex);
        _tokens[lineIndex] = ContiguousTokensEditing.Insert(_tokens[lineIndex], position.ColumnIndex, firstLineLength);

        InsertLines(lineIndex + 1, eolCount);
    }

    private void InsertLines(int insertIndex, int insertCount)
    {
        if (insertCount == 0)
            return;

        _tokens.InsertRange(insertIndex, Enumerable.Repeat<LineToken[]>([], insertCount));
    }

    #endregion

    #region Serialization

    internal int SerializeSize()
    {
        int result = 0;
        result += 4; // 4 bytes for the start line index
        result += 4; // 4 bytes for the line count
        foreach (var lineTokens in _tokens)
        {
            result += 4; // 4 bytes for the byte count
            result += lineTokens.Length * 8; // 8 bytes per token (4 bytes for end offset, 4 bytes for metadata)
        }
        return result;
    }

    internal int Serialize(Span<byte> destination)
    {
        int offset = 0;
        BinaryPrimitives.WriteUInt32BigEndian(destination[offset..], (uint)_startLineIndex); offset += 4;
        BinaryPrimitives.WriteUInt32BigEndian(destination[offset..], (uint)_tokens.Count); offset += 4;
        foreach (var lineTokens in _tokens)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination[offset..], (uint)(lineTokens.Length * 8)); offset += 4;
            MemoryMarshal.AsBytes(lineTokens).CopyTo(destination[offset..]);
            offset += lineTokens.Length * 8;
        }
        return offset;
    }

    internal static int Deserialize(ReadOnlySpan<byte> buff, out ContiguousMultilineTokens result)
    {
        int offset = 0;
        int startLineIndex = (int)BinaryPrimitives.ReadUInt32BigEndian(buff[offset..]); offset += 4;
        int lineCount = (int)BinaryPrimitives.ReadUInt32BigEndian(buff[offset..]); offset += 4;
        List<LineToken[]> tokens = new List<LineToken[]>(lineCount);
        for (int i = 0; i < lineCount; i++)
        {
            int byteCount = (int)BinaryPrimitives.ReadUInt32BigEndian(buff[offset..]); offset += 4;
            LineToken[] lineTokens = new LineToken[byteCount / 8];
            buff[offset..(offset + byteCount)].CopyTo(MemoryMarshal.AsBytes(lineTokens.AsSpan()));
            offset += byteCount;
            tokens.Add(lineTokens);
        }
        result = new ContiguousMultilineTokens(startLineIndex, tokens);
        return offset;
    }

    #endregion
}

public class ContiguousMultilineTokensBuilder
{
    public static ContiguousMultilineTokens[] Deserialize(ReadOnlySpan<byte> buff)
    {
        int offset = 0;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(buff[offset..]); offset += 4;
        var result = new ContiguousMultilineTokens[count];
        for (int i = 0; i < count; i++)
        {
            offset += ContiguousMultilineTokens.Deserialize(buff[offset..], out var tokens);
            result[i] = tokens;
        }
        return result;
    }

    private readonly List<ContiguousMultilineTokens> _tokens = [];

    public void Add(int lineIndex, LineToken[] lineTokens)
    {
        if (_tokens.Count > 0)
        {
            var last = _tokens[^1];
            if (last.EndLineIndex + 1 == lineIndex)
            {
                last.AppendLineTokens(lineTokens);
                return;
            }
        }
        _tokens.Add(new ContiguousMultilineTokens(lineIndex, [lineTokens]));
    }

    public List<ContiguousMultilineTokens> Finalize()
    {
        return _tokens;
    }

    public byte[] Serialize()
    {
        int size = SerializeSize();
        byte[] result = new byte[size];
        Serialize(result);
        return result;

        int SerializeSize()
        {
            int result = 0;
            result += 4; // 4 bytes for the count
            foreach (var item in _tokens)
            {
                result += item.SerializeSize();
            }
            return result;
        }

        void Serialize(Span<byte> destination)
        {
            int offset = 0;
            BinaryPrimitives.WriteUInt32BigEndian(destination[offset..], (uint)_tokens.Count); offset += 4;
            foreach (var item in _tokens)
            {
                offset += item.Serialize(destination[offset..]);
            }
        }
    }
}
