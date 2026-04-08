using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

/// <summary>
/// Maps globally registered language ids to model-local language ids used in token metadata.
/// Local <see cref="LanguageId"/> values are packed into 8 bits and therefore capped at 255.
/// </summary>
public sealed class ModelLanguageIdMapper
{
    private static readonly LanguageId LocalNullLanguageId = new(0);
    private static readonly LanguageId LocalPlainTextLanguageId = new(1);

    private readonly Dictionary<GlobalLanguageId, LanguageId> _globalToLocal = [];
    private readonly Dictionary<LanguageId, GlobalLanguageId> _localToGlobal = [];
    private uint _nextLocalId = LocalPlainTextLanguageId.Value + 1;

    public ModelLanguageIdMapper()
    {
        Register(GlobalLanguageId.Null, LocalNullLanguageId);
        Register(GlobalLanguageId.PlainText, LocalPlainTextLanguageId);
    }

    private void Register(GlobalLanguageId globalLanguageId, LanguageId localLanguageId)
    {
        _globalToLocal[globalLanguageId] = localLanguageId;
        _localToGlobal[localLanguageId] = globalLanguageId;
    }

    public LanguageId Encode(GlobalLanguageId globalLanguageId)
    {
        if (_globalToLocal.TryGetValue(globalLanguageId, out var localLanguageId))
            return localLanguageId;

        if (_nextLocalId > byte.MaxValue)
            throw new InvalidOperationException("Maximum of 255 local language ids reached for this text model.");

        localLanguageId = new LanguageId(_nextLocalId++);
        Register(globalLanguageId, localLanguageId);
        return localLanguageId;
    }

    public GlobalLanguageId Decode(LanguageId localLanguageId)
    {
        if (_localToGlobal.TryGetValue(localLanguageId, out var globalLanguageId))
            return globalLanguageId;
        return GlobalLanguageId.Null;
    }
}
