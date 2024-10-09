namespace EditorTextBuffers.Contracts;

/// <summary>
/// Text snapshot that works like an iterator.
/// Will try to return chunks of roughly ~64KB size.
/// Will return null when finished.
/// </summary>
public interface ITextSnapshot
{
    string? Read();
}
