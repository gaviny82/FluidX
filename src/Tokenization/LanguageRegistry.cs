using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

public sealed class LanguageRegistry
{
    public static LanguageRegistry Instance { get; private set; } = new();

    private readonly Dictionary<GlobalLanguageId, string> _idToName = [];
    private readonly Dictionary<string, GlobalLanguageId> _nameToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<GlobalLanguageId, ITokenizationSupport> _tokenizationSupports = [];
    private readonly Dictionary<GlobalLanguageId, IAsyncTokenizationSupportFactory> _factories = [];
    private uint _nextLanguageId = GlobalLanguageId.PlainText.Value + 1;

    public event EventHandler<LanguageSupportsChangedEventArgs>? SupportsChanged;

    public SyntaxColor[]? ColorMap
    {
        get => field;
        set
        {
            field = value;
            SupportsChanged?.Invoke(
                this,
                new LanguageSupportsChangedEventArgs(_tokenizationSupports.Keys.ToArray(), true)
            );
        }
    } = null;

    public SyntaxColor? DefaultBackground
    {
        get
        {
            if (ColorMap is not null && ColorMap.Length > (int)ColorId.DefaultBackground)
                return ColorMap[(int)ColorId.DefaultBackground];
            return null;
        }
    }

    public LanguageRegistry()
    {
        RegisterLanguageInternal(string.Empty, GlobalLanguageId.Null);
        RegisterLanguageInternal("plaintext", GlobalLanguageId.PlainText);
    }

    private void RegisterLanguageInternal(string name, GlobalLanguageId id)
    {
        _idToName[id] = name;
        _nameToId[name] = id;
    }

    public GlobalLanguageId RegisterLanguage(string languageName)
    {
        if (_nameToId.TryGetValue(languageName, out var existing))
            return existing;

        var id = new GlobalLanguageId(_nextLanguageId++);
        RegisterLanguageInternal(languageName, id);
        return id;
    }

    public bool TryGetLanguageId(string languageName, out GlobalLanguageId languageId)
        => _nameToId.TryGetValue(languageName, out languageId);

    public bool TryGetLanguageName(GlobalLanguageId languageId, out string name)
    {
        bool hasValue = _idToName.TryGetValue(languageId, out var lanName);
        name = lanName ?? string.Empty;
        return hasValue;
    }

    public ITokenizationSupport? GetSupport(GlobalLanguageId languageId)
        => _tokenizationSupports.TryGetValue(languageId, out var result) ? result : null;

    public bool IsResolved(GlobalLanguageId languageId)
        => _tokenizationSupports.ContainsKey(languageId);

    public void Register(GlobalLanguageId languageId, ITokenizationSupport support)
    {
        _tokenizationSupports[languageId] = support;
        OnSupportsChanged([languageId]);
    }

    public void RegisterFactory(GlobalLanguageId languageId, IAsyncTokenizationSupportFactory factory)
    {
        _factories[languageId] = factory;
    }

    public async ValueTask<ITokenizationSupport?> GetOrCreateSupportAsync(GlobalLanguageId languageId)
    {
        if (_tokenizationSupports.TryGetValue(languageId, out var support))
            return support;

        if (!_factories.TryGetValue(languageId, out var factory))
            return null;

        support = await factory.CreateTokenizationSupportAsync();
        _tokenizationSupports[languageId] = support;
        OnSupportsChanged([languageId]);
        return support;
    }

    private void OnSupportsChanged(GlobalLanguageId[] languageIds)
    {
        SupportsChanged?.Invoke(
            this,
            new LanguageSupportsChangedEventArgs(languageIds, false)
        );
    }
}

public readonly record struct LanguageSupportsChangedEventArgs(
    GlobalLanguageId[] ChangedLanguages,
    bool IsColorMapChanged
);

public readonly record struct SyntaxColor(byte R, byte G, byte B, byte A);

public interface IAsyncTokenizationSupportFactory
{
    Task<ITokenizationSupport> CreateTokenizationSupportAsync();
}
