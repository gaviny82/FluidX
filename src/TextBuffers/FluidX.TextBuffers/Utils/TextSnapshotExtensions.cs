namespace FluidX.TextBuffers;

public static class TextSnapshotExtensions
{
    /// <summary>
    /// Writes the snapshot as UTF-8 without adding a BOM, preserving its raw line endings.
    /// The stream is left open and the snapshot can be read or saved again.
    /// </summary>
    /// <remarks>
    /// This line-based convenience extension is not part of the buffer or snapshot contracts.
    /// The most efficient saving strategy may be implementation-specific; consider adding a
    /// saving operation to IReadOnlyTextBuffer or ITextSnapshot in the future. Encoding and BOM
    /// policy belong to the model/serialization layer. Stored U+FEFF characters are written normally.
    /// </remarks>
    public static async Task SaveAsync(this ITextSnapshot snapshot, Stream stream)
    {
        using var writer = new StreamWriter(stream, leaveOpen: true);
        for (int line = 0; line < snapshot.LineCount; line++)
        {
            await writer.WriteAsync(snapshot.GetLineContent(line)).ConfigureAwait(false);
            await writer.WriteAsync(snapshot.GetLineEOL(line)).ConfigureAwait(false);
        }
        await writer.FlushAsync().ConfigureAwait(false);
    }
}
