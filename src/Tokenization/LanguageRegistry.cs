using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

public sealed class LanguageRegistry
{
    public static LanguageRegistry Instance { get; private set; } = new();

    private readonly Dictionary<LanguageId, string> _idToName = [];
    private readonly Dictionary<string, LanguageId> _nameToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<LanguageId, ITokenizationSupport> _tokenizationSupports = [];
    private readonly Dictionary<LanguageId, IAsyncTokenizationSupportFactory> _factories = [];
    private uint _nextLanguageId = LanguageId.PlainText.Value + 1;

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
        RegisterLanguageInternal(string.Empty, LanguageId.Null);
        RegisterLanguageInternal("plaintext", LanguageId.PlainText);
    }

    private void RegisterLanguageInternal(string name, LanguageId id)
    {
        _idToName[id] = name;
        _nameToId[name] = id;
    }

    public LanguageId RegisterLanguage(string languageName)
    {
        if (_nameToId.TryGetValue(languageName, out var existing))
            return existing;

        if (_nextLanguageId > byte.MaxValue)
            throw new InvalidOperationException("Maximum of 255 registered languages reached.");

        var id = new LanguageId(_nextLanguageId++);
        RegisterLanguageInternal(languageName, id);
        return id;
    }

    public bool TryGetLanguageId(string languageName, out LanguageId languageId)
        => _nameToId.TryGetValue(languageName, out languageId);

    public bool TryGetLanguageName(LanguageId languageId, out string name)
    {
        bool hasValue = _idToName.TryGetValue(languageId, out var lanName);
        name = lanName ?? string.Empty;
        return hasValue;
    }

    public ITokenizationSupport? GetSupport(LanguageId languageId)
        => _tokenizationSupports.TryGetValue(languageId, out var result) ? result : null;

    public bool IsResolved(LanguageId languageId)
        => _tokenizationSupports.ContainsKey(languageId);

    public void Register(LanguageId languageId, ITokenizationSupport support)
    {
        _tokenizationSupports[languageId] = support;
        OnSupportsChanged([languageId]);
    }

    public void RegisterFactory(LanguageId languageId, IAsyncTokenizationSupportFactory factory)
    {
        _factories[languageId] = factory;
    }

    public async ValueTask<ITokenizationSupport?> GetOrCreateSupportAsync(LanguageId languageId)
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

    private void OnSupportsChanged(LanguageId[] languageIds)
    {
        SupportsChanged?.Invoke(
            this,
            new LanguageSupportsChangedEventArgs(languageIds, false)
        );
    }
}

public readonly record struct LanguageSupportsChangedEventArgs(
    LanguageId[] ChangedLanguages,
    bool IsColorMapChanged
);

public readonly record struct SyntaxColor(byte R, byte G, byte B, byte A);

public interface IAsyncTokenizationSupportFactory
{
    Task<ITokenizationSupport> CreateTokenizationSupportAsync();
}
