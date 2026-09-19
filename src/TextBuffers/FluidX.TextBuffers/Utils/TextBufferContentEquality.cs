namespace FluidX.TextBuffers;

internal static class TextBufferContentEquality
{
    public static bool Equals(IReadOnlyTextBuffer buffer, IReadOnlyTextBuffer? other)
    {
        if (ReferenceEquals(buffer, other)) return true;
        if (other is null || buffer.Length != other.Length || buffer.LineCount != other.LineCount)
            return false;
        for (int line = 0; line < buffer.LineCount; line++)
            if (buffer.GetLineContent(line) != other.GetLineContent(line)
                || buffer.GetLineEOL(line) != other.GetLineEOL(line))
                return false;
        return true;
    }
}
