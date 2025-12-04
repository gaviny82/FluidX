using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

public class TokenizationRegistry
{
    // Snigleton
    public static TokenizationRegistry Instance { get; private set; } = new TokenizationRegistry();

    private readonly Dictionary<string, ITokenizationSupport> _tokenizationSupports = [];
    private readonly Dictionary<string, IAsyncTokenizationSupportFactory> _factories = [];

    public SyntaxColor[]? ColorMap
    {
        get => field;
        set
        {
            field = value;
            TokenizationSupportsChanged?.Invoke(
                this,
                new TokenizationSupportsChangedEventArgs(_tokenizationSupports.Keys.ToArray(), true)
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

    public event EventHandler<TokenizationSupportsChangedEventArgs>? TokenizationSupportsChanged;

    public ITokenizationSupport? GetSupport(string languageId)
    {
        if (_tokenizationSupports.TryGetValue(languageId, out var result))
            return result;
        else
            return null;
    }

    public bool IsResolved(string languageId)
        => _tokenizationSupports.ContainsKey(languageId);

    public void Register(string languageId, ITokenizationSupport support)
    {
        _tokenizationSupports[languageId] = support;
        OnTokenizationSupportsChanged([languageId]);
    }

    public void RegisterFactory(string languageId, IAsyncTokenizationSupportFactory factory)
    {
        _factories[languageId] = factory;
    }

    public async ValueTask<ITokenizationSupport?> GetOrCreateSupportAsync(string languageId)
    {
        // Return immediately if the required tokenization support already exists
        if (_tokenizationSupports.TryGetValue(languageId, out var support))
            return support;

        if (!_factories.TryGetValue(languageId, out var factory))
            return null; // No factory registered for the given language ID

        // Create and store the tokenization support using the factory
        support = await factory.CreateTokenizationSupportAsync();
        _tokenizationSupports[languageId] = support;
        OnTokenizationSupportsChanged([languageId]);
        return support;
    }

    private void OnTokenizationSupportsChanged(string[] languageIds)
    {
        TokenizationSupportsChanged?.Invoke(
            this,
            new TokenizationSupportsChangedEventArgs(languageIds, false)
        );
    }
}

public record struct TokenizationSupportsChangedEventArgs(
    string[] ChangedLanguages,
    bool IsColorMapChanged
);

public record struct SyntaxColor(byte R, byte G, byte B, byte A);

public interface IAsyncTokenizationSupportFactory
{
    Task<ITokenizationSupport> CreateTokenizationSupportAsync();
}
