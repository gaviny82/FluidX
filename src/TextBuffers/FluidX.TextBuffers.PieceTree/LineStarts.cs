namespace FluidX.TextBuffers.PieceTree;

internal class LineStarts
{
    public required int[] Starts { get; init; }
    public required int CR { get; init; }
    public required int LF { get; init; }
    public required int CRLF { get; init; }
    public required bool IsBasicAscii { get; init; }

    public static List<int> CreateFast(ReadOnlySpan<char> span)
    {
        List<int> r = [0];

        for (int i = 0, len = span.Length; i < len; i++)
        {
            char chr = span[i];

            if (chr == '\r')
            {
                if (i + 1 < len && span[i + 1] == '\n')
                {
                    r.Add(i + 2);
                    i++; // skip \n
                }
                else
                {
                    r.Add(i + 1);
                }
            }
            else if (chr == '\n')
            {
                r.Add(i + 1);
            }
        }

        return r;
    }

    public static LineStarts Create(ReadOnlySpan<char> span)
    {
        List<int> r = [0];
        int cr = 0, lf = 0, crlf = 0;
        bool isBasicASCII = true;

        for (int i = 0, len = span.Length; i < len; i++)
        {
            char chr = span[i];

            if (chr == '\r')
            {
                if (i + 1 < len && span[i + 1] == '\n')
                {
                    // \r\n case
                    crlf++;
                    r.Add(i + 2);
                    i++; // skip \n
                }
                else
                {
                    // \r case
                    cr++;
                    r.Add(i + 1);
                }
            }
            else if (chr == '\n')
            {
                lf++;
                r.Add(i + 1);
            }
            else
            {
                if (isBasicASCII && !chr.IsBasicASCII())
                    isBasicASCII = false;
            }
        }

        return new LineStarts()
        {
            Starts = r.ToArray(),
            CR = cr,
            LF = lf,
            CRLF = crlf,
            IsBasicAscii = isBasicASCII
        };
    }
}
