namespace FluidX.TextModels;

public class TextModelEditSource
{
    public TextModelEditSourceMetadata Metadata { get; }

    public TextModelEditSource(TextModelEditSourceMetadata metadata)
    {
        Metadata = metadata;
    }

    public override string ToString() => Metadata.Source;

    public string GetTypeName() => Metadata switch
    {
        CursorMetadata cursor => cursor.Kind,
        UnknownMetadata unknown => unknown.Name ?? "unknown",
        _ => Metadata.Source,
    };

    public static bool IsAiEdit(TextModelEditSource source)
    {
        return source.Metadata.Source switch
        {
            "inlineCompletionAccept" or
            "inlineCompletionPartialAccept" or
            "inlineChat.applyEdits" or
            "Chat.applyEdits" => true,
            _ => false
        };
    }

    public static bool IsUserEdit(TextModelEditSource source)
    {
        return source.Metadata is CursorMetadata c && c.Kind == "type";
    }
}

public static class EditSources
{
    public static TextModelEditSource CreateUnknown(string? name = null)
        => new(new UnknownMetadata(name));

    public static TextModelEditSource CreateRename()
        => new(new RenameMetadata());

    public static TextModelEditSource CreateCursor(string kind, string? detailedSource = null)
        => new(new CursorMetadata(kind, detailedSource));

    public static TextModelEditSource CreateEOLChange()
        => new(new EOLChangeMetadata());

    public static TextModelEditSource CreateApplyEdits()
        => new(new ApplyEditsMetadata());
}

#region Metadata Types

public abstract class TextModelEditSourceMetadata
{
    public abstract string Source { get; }
}

public sealed class UnknownMetadata(string? name = null) : TextModelEditSourceMetadata
{
    public override string Source => "unknown";
    public string? Name { get; } = name;
}

public sealed class RenameMetadata : TextModelEditSourceMetadata
{
    public override string Source => "rename";
}

public sealed class CursorMetadata(string kind, string? detailedSource = null) : TextModelEditSourceMetadata
{
    public override string Source => "cursor";
    public string Kind { get; } = kind;
    public string? DetailedSource { get; } = detailedSource;
}

public sealed class EOLChangeMetadata : TextModelEditSourceMetadata
{
    public override string Source => "eolChange";
}

public sealed class ApplyEditsMetadata : TextModelEditSourceMetadata
{
    public override string Source => "applyEdits";
}

#endregion
