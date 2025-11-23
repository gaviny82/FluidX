namespace FluidX.TextBuffers;

public enum StringEndOfLine
{
    Unknown = 0,
    Invalid = 3,
    LF = 1,
    CRLF = 2,
}

internal static class EOLCounter
{
    public static (int eolCount, int firstLineLength, int lastLineLength, StringEndOfLine eol) CountEOL(string text)
    {
        int eolCount = 0;
        int firstLineLength = 0;
        int lastLineStart = 0;
        StringEndOfLine eol = StringEndOfLine.Unknown;
        for (int i = 0, len = text.Length; i < len; i++)
        {
            char chr = text[i];
            if (chr == '\r')
            {
                if (eolCount == 0)
                {
                    firstLineLength = i;
                }
                eolCount++;
                if (i + 1 < len && text[i + 1] == '\n')
                {
                    // \r\n... case
                    eol |= StringEndOfLine.CRLF;
                    i++; // skip \n
                }
                else
                {
                    // \r... case
                    eol |= StringEndOfLine.Invalid;
                }
                lastLineStart = i + 1;
            }
            else if (chr == '\n')
            {
                // \n... case
                eol |= StringEndOfLine.LF;
                if (eolCount == 0)
                {
                    firstLineLength = i;
                }
                eolCount++;
                lastLineStart = i + 1;
            }
        }
        if (eolCount == 0)
        {
            firstLineLength = text.Length;
        }
        return (eolCount, firstLineLength, text.Length - lastLineStart, eol);
    }
}
