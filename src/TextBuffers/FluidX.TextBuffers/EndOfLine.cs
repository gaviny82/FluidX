namespace FluidX.TextBuffers;

/// <summary>
/// A concrete end-of-line sequence.
/// </summary>
public enum EndOfLine
{
    CR,
    LF,
    CRLF
}

public static class EndOfLineExtensions
{
    /// <summary>Returns the characters represented by the end-of-line sequence.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a supported sequence.</exception>
    public static string AsString(this EndOfLine eol) => eol switch
    {
        EndOfLine.CR => "\r",
        EndOfLine.LF => "\n",
        EndOfLine.CRLF => "\r\n",
        _ => throw new ArgumentOutOfRangeException(nameof(eol))
    };
}
