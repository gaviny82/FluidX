namespace EditorTextBuffers.Contracts;

/// <summary>
/// Text snapshot that works like an iterator.
/// </summary>
public interface ITextSnapshot
{
    /// <summary>
    /// Reads the next chunk of text.
    /// </summary>
    /// <returns>the next chunk of text or <see langword="null"/> when finished.</returns>
    string? Read();
}
