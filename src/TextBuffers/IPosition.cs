namespace FluidX.TextBuffers;

/// <summary>
/// A position in the editor.
/// </summary>
public interface IPosition
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
