using System.Text.RegularExpressions;

namespace FluidX.TextBuffers;

public static partial class StringExtensions
{
    [GeneratedRegex(@"^[\t\n\r\x20-\x7E]*$")]
    private static partial Regex IsBasicASCIIRegex { get; }

    /// <summary>
    /// Determines if the string contains only basic ASCII characters.
    /// </summary>
    /// <param name="str"><see cref="string"/> to test</param>
    /// <returns><see langword="true"/> if <paramref name="str"/> contains only basic ASCII characters.</returns>
    public static bool IsBasicASCII(this string str) => IsBasicASCIIRegex.IsMatch(str);

    [GeneratedRegex(@"(?:[\u05BE\u05C0\u05C3\u05C6\u05D0-\u05F4\u0608\u060B\u060D\u061B-\u064A\u066D-\u066F\u0671-\u06D5\u06E5\u06E6\u06EE\u06EF\u06FA-\u0710\u0712-\u072F\u074D-\u07A5\u07B1-\u07EA\u07F4\u07F5\u07FA\u07FE-\u0815\u081A\u0824\u0828\u0830-\u0858\u085E-\u088E\u08A0-\u08C9\u200F\uFB1D\uFB1F-\uFB28\uFB2A-\uFD3D\uFD50-\uFDC7\uFDF0-\uFDFC\uFE70-\uFEFC]|\uD802[\uDC00-\uDD1B\uDD20-\uDE00\uDE10-\uDE35\uDE40-\uDEE4\uDEEB-\uDF35\uDF40-\uDFFF]|\uD803[\uDC00-\uDD23\uDE80-\uDEA9\uDEAD-\uDF45\uDF51-\uDF81\uDF86-\uDFF6]|\uD83A[\uDC00-\uDCCF\uDD00-\uDD43\uDD4B-\uDFFF]|\uD83B[\uDC00-\uDEBB])")]
    private static partial Regex ContainsRTLRegex { get; }

    /// <summary>
    /// Determines if the string contains any right-to-left characters.
    /// </summary>
    /// <param name="str"><see cref="string"/> to test</param>
    /// <returns><see langword="true"/> if <paramref name="str"/> contains right-to-left characters.</returns>
    public static bool ContainsRTL(this string str) => ContainsRTLRegex.IsMatch(str);

    [GeneratedRegex(@"[\u2028\u2029]")] // LINE SEPARATOR (LS) or PARAGRAPH 
    private static partial Regex ContainsUnusualLineTerminatorsRegex { get; }

    /// <summary>
    /// Determines if the string contains LINE SEPARATOR (LS) or PARAGRAPH.
    /// </summary>
    /// <param name="str"><see cref="string"/> to test</param>
    /// <returns><see langword="true"/> if <paramref name="str"/> contains LINE SEPARATOR (LS) or PARAGRAPH.</returns>
    public static bool ContainsUnusualLineTerminators(this string str) => ContainsUnusualLineTerminatorsRegex.IsMatch(str);

    [GeneratedRegex(@"\r\n|\r|\n")]
    public static partial Regex EndOfLinesRegex { get; }

    /// <summary>
    /// Returns first index of the string that is not whitespace.
    /// </summary>
    /// <param name="str"></param>
    /// <returns>Index of the first non-whitespace character, or -1 if the string is empty or contains only whitespaces.</returns>
    /// <remarks>Whitespace characters are ' ' and '\t'.</remarks>
    public static int FirstNonWhitespaceIndex(this string str) => str.IndexOfAny([' ', '\t']);

    /// <summary>
    /// Returns last index of the string that is not whitespace.
    /// </summary>
    /// <param name="str"></param>
    /// <returns>Index of the last non-whitespace character, or -1 if the string is empty or contains only whitespaces.</returns>
    /// <remarks>Whitespace characters are ' ' and '\t'.</remarks>
    public static int LastNonWhitespaceIndex(this string str) => str.LastIndexOfAny([' ', '\t']);
}