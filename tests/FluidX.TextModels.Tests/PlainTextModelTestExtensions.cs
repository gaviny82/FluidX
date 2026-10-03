using FluidX.TextBuffers;
using FluidX.TextModels;

namespace FluidX.TextModels.Tests;

internal static class PlainTextModelTestExtensions
{
    public static TextRange GetFullModelRange(this TextModel model)
    {
        var buffer = model.TextBuffer;
        return buffer.GetRangeAt(0, buffer.Length);
    }
}
