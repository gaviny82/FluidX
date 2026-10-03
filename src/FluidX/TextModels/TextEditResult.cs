using System.Collections.Immutable;

namespace FluidX.TextModels;

/// <summary>
/// The outcome of a text model edit batch.
/// </summary>
/// <param name="Transition">The committed transition, or null when the batch leaves content unchanged.</param>
/// <param name="EditSpans">One span per input replacement, in input order, including unchanged
/// replacements and batches that leave content unchanged.</param>
/// <remarks>
/// <paramref name="EditSpans"/> coordinates are exact UTF-16 offsets and may end
/// inside a newly formed CRLF pair; they are not necessarily valid caret positions
/// or edit endpoints. These per-input mappings are distinct from committed change spans.
/// </remarks>
public sealed record TextEditResult(
    TextRecordTransition? Transition,
    ImmutableArray<TextChangeSpan> EditSpans);
