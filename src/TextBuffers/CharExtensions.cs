namespace FluidX.TextBuffers;

internal static class CharExtensions
{
    // IsBasicASCII
    public static bool IsBasicASCII(this char ch)
    {
        return (ch >= 0x20 && ch <= 0x7E) || ch == '\t';
    }
}
