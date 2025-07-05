using EditorTextBuffers;
using System.Buffers.Binary;
using System.Text;

namespace EditorTextBuffers;

public class ApplyEditsResult
{
    public required IValidEditOperation[]? ReverseEdits { get; init; }
    public required IReadOnlyList<IInternalModelContentChange> Changes { get; init; }
    public required IReadOnlyList<int>? TrimAutoWhitespaceLineNumbers { get; init; }
}

public interface IValidEditOperation
{
    /**
     * An identifier associated with this single edit operation.
     * @internal
     */
    ISingleEditOperationIdentifier? Identifier { get; }
    /**
     * The range to replace. This can be empty to emulate a simple insert.
     */
    Range Range { get; }
    /**
     * The text to replace with. This can be empty to emulate a simple delete.
     */
    string Text { get; }
    /**
     * @internal
     */
    TextChange TextChange { get; }
}

interface IValidatedEditOperation
{
    int SortIndex { get; }
    ISingleEditOperationIdentifier? Identifier { get; }
    Range Range { get; }
    int RangeOffset { get; }
    int RangeLength { get; }
    string Text { get; }
    int EOLCount { get; }
    int FirstLineLength { get; }
    int LastLineLength { get; }
    bool ForceMoveMarkers { get; }
    bool IsAutoWhitespaceEdit { get; }
}

class ValidatedEditOperation : IValidatedEditOperation
{
    public required int SortIndex { get; init; }
    public required ISingleEditOperationIdentifier? Identifier { get; init; }
    public required Range Range { get; init; }
    public required int RangeOffset { get; init; }
    public required int RangeLength { get; init; }
    public required string Text { get; init; }
    public required int EOLCount { get; init; }
    public required int FirstLineLength { get; init; }
    public required int LastLineLength { get; init; }
    public required bool ForceMoveMarkers { get; init; }
    public required bool IsAutoWhitespaceEdit { get; init; }
}

interface IReverseSingleEditOperation : IValidEditOperation
{
    int SortIndex { get; }
}

class ReverseSingleEditOperation : IReverseSingleEditOperation
{
    public required ISingleEditOperationIdentifier? Identifier { get; init; }
    public required Range Range { get; init; }
    public required string Text { get; init; }
    public required TextChange TextChange { get; init; }
    public required int SortIndex { get; init; }
}

public class TextChange
{
    public int OldPosition { get; }
    public string OldText { get; }
    public int NewPosition { get; }
    public string NewText { get; }

    public int OldLength => OldText.Length;
    public int OldEnd => OldPosition + OldText.Length;
    public int NewLength => NewText.Length;
    public int NewEnd => NewPosition + NewText.Length;

    public TextChange(int oldPosition, string oldText, int newPosition, string newText)
    {
        OldPosition = oldPosition;
        NewPosition = newPosition;
        OldText = oldText;
        NewText = newText;
    }

    public override string ToString()
    {
        if (OldLength == 0)
            return $"(insert@{OldPosition} \"{EscapeNewLine(NewText)}\")";

        if (NewLength == 0)
            return $"(delete@{OldPosition} \"{EscapeNewLine(OldText)}\")";

        return $"(replace@{OldPosition} \"{EscapeNewLine(OldText)}\" with \"{EscapeNewLine(NewText)}\")";
    }

    private static string EscapeNewLine(string str)
    {
        return str
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    private static int WriteStringSize(string str)
    {
        return 4 + 2 * str.Length;
    }

    private static int WriteString(byte[] b, string str, int offset)
    {
        int len = str.Length;
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(offset), (uint)len);
        offset += 4;
        for (int i = 0; i < len; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset), (ushort)str[i]);
            offset += 2;
        }
        return offset;
    }

    private static string ReadString(byte[] b, int offset)
    {
        uint len = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset));
        int byteCount = (int)len * 2;
        ReadOnlySpan<byte> strBytes = b.AsSpan(offset, byteCount);

        // Use Encoding.Unicode (UTF-16LE)
        return Encoding.Unicode.GetString(strBytes);
    }

    public int WriteSize()
    {
        return 4 + // OldPosition
               4 + // NewPosition
               WriteStringSize(OldText) +
               WriteStringSize(NewText);
    }

    public int Write(byte[] b, int offset)
    {
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(offset), (uint)OldPosition); offset += 4;
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(offset), (uint)NewPosition); offset += 4;
        offset = WriteString(b, OldText, offset);
        offset = WriteString(b, NewText, offset);
        return offset;
    }

    public static int Read(byte[] b, int offset, IList<TextChange> dest)
    {
        uint oldPosition = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset)); offset += 4;
        uint newPosition = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset)); offset += 4;
        string oldText = ReadString(b, offset); offset += WriteStringSize(oldText);
        string newText = ReadString(b, offset); offset += WriteStringSize(newText);
        dest.Add(new TextChange((int)oldPosition, oldText, (int)newPosition, newText));
        return offset;
    }
}

public interface IInternalModelContentChange : IModelContentChange
{
    //Range Range { get; }//duplicate?
    bool ForceMoveMarkers { get; }
}

class InternalModelContentChange : IInternalModelContentChange
{
    public required IRange Range { get; init; }
    public required int RangeOffset { get; init; }
    public required int RangeLength { get; init; }
    public required string Text { get; init; }
    public required bool ForceMoveMarkers { get; init; }
}

public class ValidAnnotatedEditOperation
{
    public required ISingleEditOperationIdentifier? Identifier { get; init; }
    public required Range Range { get; init; }
    public required string? Text { get; init; }
    public required bool ForceMoveMarkers { get; init; }
    public required bool IsAutoWhitespaceEdit { get; init; }
    public required bool IsTracked { get; init; }
}

public interface ISingleEditOperationIdentifier
{
    /**
     * Identifier major
     */
    int Major { get; }
    /**
     * Identifier minor
     */
    int Minor { get; }
}

public interface IModelContentChange
{
    /**
     * The old range that got replaced.
     */
    IRange Range { get; }

    /**
     * The offset of the range that got replaced.
     */
    int RangeOffset { get; }
    /**
     * The length of the range that got replaced.
     */
    int RangeLength { get; }
    /**
     * The new text for the range.
     */
    string Text { get; }
}
