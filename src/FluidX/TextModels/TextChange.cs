using System.Buffers.Binary;
using System.Text;

namespace FluidX.TextModels;

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
