namespace FluidX.TextBuffers;

/// <summary>
/// Replaces a <see cref="TextRange"/> in a text buffer with a new <see langword="string"/>.
/// An empty range represents an insertion, and an empty string represents a deletion.
/// </summary>
/// <param name="Range"></param>
/// <param name="Text"></param>
public record class TextReplacement(TextRange Range, string Text)
{
    public bool IsEmpty => Range.IsEmpty && Text.Length == 0;
}
