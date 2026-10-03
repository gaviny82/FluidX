using System.Collections.Immutable;

namespace FluidX.TextModels;

/// <summary>
/// The outcome of a text model batch edit operation.
/// </summary>
/// <param name="Source">The text document state before the operation.</param>
/// <param name="Target">The text document state after the operation.</param>
/// <param name="ChangeSpans">
/// On success, one change span per input text replacement in input order.
/// Old coordinates refer to <paramref name="Source"/> and new coordinates to <paramref name="Target"/>.
/// Empty for an empty batch or validation failure.<br/><br/>
/// Coordinates may start or end inside a newly formed CRLF pair; they are not necessarily valid caret positions
/// or edit endpoints.
/// </param>
/// <param name="Error">A validation error message, or null on success. Messages are descriptive
/// text, not stable error codes.</param>
public sealed record TextEditResult(
    TextRecord Source,
    TextRecord Target,
    ImmutableArray<TextChangeSpan> ChangeSpans,
    string? Error)
{
    /// <summary>
    /// Whether the edit batch is applied successfully.
    /// </summary>
    public bool Succeeded => Error is null;
}
