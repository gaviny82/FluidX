namespace FluidX.TextBuffers;

/// <summary>
/// An immutable, persistent view of the exact raw UTF-16 content captured from a text buffer.
/// </summary>
/// <remarks>
/// All text, lengths, line structure, and coordinate mappings remain fixed for the lifetime of
/// this object, regardless of subsequent source edits, normalization, or storage replacement.
/// The snapshot retains the storage it needs independently of the source's lifetime.
/// Reads are repeatable and safe to call concurrently, including while the source is edited.
/// Creating a snapshot of a snapshot may return the same instance.
/// </remarks>
public interface ITextSnapshot : IReadOnlyTextBuffer
{
}
