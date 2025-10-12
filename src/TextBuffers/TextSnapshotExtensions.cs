using FluidX.TextBuffers.Contracts;

namespace FluidX.TextBuffers;

public static class TextSnapshotExtensions
{
    public static async Task SaveAsync(this ITextSnapshot snapshot, Stream stream)
    {
        using var writer = new StreamWriter(stream, leaveOpen: true);
        string? chunk;
        while ((chunk = snapshot.Read()) is not null)
        {
            await writer.WriteAsync(chunk).ConfigureAwait(false);
        }
        await writer.FlushAsync().ConfigureAwait(false);
    }
}
