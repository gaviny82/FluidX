namespace FluidX;

/// <summary>
/// A position in a text document.
/// </summary>
public interface ITextPosition
{
    /// <summary>
    /// Line number (starts at 1).
    /// </summary>
    int LineNumber { get; }

    /// <summary>
    /// Column (the first character in a line is between column 1 and column 2).
    /// </summary>
    int Column { get; }
}
