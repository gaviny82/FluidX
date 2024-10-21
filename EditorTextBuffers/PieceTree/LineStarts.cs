namespace EditorTextBuffers.PieceTree;

internal class LineStarts
{
    public required IReadOnlyList<int> Starts { get; init; }
    public required int CR { get; init; }
    public required int LF { get; init; }
    public required int CRLF { get; init; }
    public required bool IsBasicAscii { get; init; }

    // TODO: Review usage of this function and the readonly implementation
    public static List<int> CreateFast(string str)
    {
        List<int> r = [0];
        int rLength = 1;

        for (int i = 0, len = str.Length; i < len; i++)
        {
            char chr = str[i];

            if (chr == '\r')
            {
                if (i + 1 < len && str[i + 1] == '\n')
                {
                    // \r\n case
                    r.Add(i + 2);
                    rLength++;
                    i++; // skip \n
                }
                else
                {
                    // \r case
                    r.Add(i + 1);
                    rLength++;
                }
            }
            else if (chr == '\n')
            {
                r.Add(i + 1);
                rLength++;
            }
        }

        return r;
    }

    public static LineStarts Create(List<int> r, string str)
    {
        r.Clear();
        r.Add(0);
        int rLength = 1;
        int cr = 0, lf = 0, crlf = 0;
        bool isBasicASCII = true;

        for (int i = 0, len = str.Length; i < len; i++)
        {
            char chr = str[i];

            if (chr == '\r')
            {
                if (i + 1 < len && str[i + 1] == '\n')
                {
                    // \r\n case
                    crlf++;
                    r.Add(i + 2);
                    rLength++;
                    i++; // skip \n
                }
                else
                {
                    // \r case
                    cr++;
                    r.Add(i + 1);
                    rLength++;
                }
            }
            else if (chr == '\n')
            {
                lf++;
                r.Add(i + 1);
                rLength++;
            }
            else
            {
                if (isBasicASCII && !chr.IsBasicASCII())
                    isBasicASCII = false;
            }
        }

        return new LineStarts()
        {
            Starts = new List<int>(r), // copy the array
            CR = cr,
            LF = lf,
            CRLF = crlf,
            IsBasicAscii = isBasicASCII
        };
    }
}
