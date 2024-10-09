namespace EditorTextBuffers;

/// <summary>
/// A range in the editor.
/// </summary>
public interface IRange
{
    /// <summary>
    /// Line number on which the range starts (starts at 1).
    /// </summary>
    int StartLineNumber { get; }

    /// <summary>
    /// Column on which the range starts in line <see cref="StartLineNumber"/> (starts at 1).
    /// </summary>
    int StartColumn { get; }

    /// <summary>
    /// Line number on which the range ends.
    /// </summary>
    int EndLineNumber { get; }

    /// <summary>
    /// Column on which the range ends in line <see cref="EndLineNumber"/>.
    /// </summary>
    int EndColumn { get; }
}
