namespace FluidX.TextBuffers;

public static class CharExtensions
{
    extension(char ch)
    {
        /// <summary>The Unicode byte order mark character (U+FEFF).</summary>
        public static char Utf8Bom => '\uFEFF';

        public bool IsBasicASCII()
        {
            return (ch >= 0x20 && ch <= 0x7E) || ch == '\t';
        }
    }
}
