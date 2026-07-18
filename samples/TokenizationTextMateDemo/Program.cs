using System.Globalization;
using FluidX.TextBuffers;
using FluidX.TextModels;
using FluidX.Tokenization;
using FluidX.TokenizationTextMateDemo;
using Spectre.Console;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using FontStyle = FluidX.Tokenization.TokenStores.FontStyle;

if (args.Length != 1)
{
    PrintUsage();
    return;
}

string filePath = Path.GetFullPath(args[0]);
if (!File.Exists(filePath))
{
    Console.Error.WriteLine($"File not found: {filePath}");
    Environment.ExitCode = 1;
    return;
}

if (!TryGetLanguage(filePath, out var languageName, out var extension))
{
    Console.Error.WriteLine("Only .c, .cpp, .cc, .cxx, .h, .hpp, .hxx, and .cs files are supported.");
    Environment.ExitCode = 1;
    return;
}

var options = new RegistryOptions(ThemeName.DarkPlus);
string scopeName = options.GetScopeByExtension(extension);
if (string.IsNullOrWhiteSpace(scopeName))
{
    Console.Error.WriteLine($"No TextMate scope found for extension '{extension}'.");
    Environment.ExitCode = 1;
    return;
}

var registry = new Registry(options);
var theme = registry.GetTheme();
var grammar = registry.LoadGrammar(scopeName);
if (grammar is null)
{
    Console.Error.WriteLine($"Failed to load TextMate grammar for scope '{scopeName}'.");
    Environment.ExitCode = 1;
    return;
}

var globalLanguageId = LanguageRegistry.Instance.RegisterLanguage(languageName);
var tokenizationSupport = new TextMateTokenizationSupport(grammar, globalLanguageId);
LanguageRegistry.Instance.Register(globalLanguageId, tokenizationSupport);

string source = File.ReadAllText(filePath);
var model = new TextModel(source, DefaultEndOfLine.LF, globalLanguageId);

Console.WriteLine($"File: {filePath}");
Console.WriteLine($"Language: {languageName}");
Console.WriteLine($"Scope: {scopeName}");
Console.WriteLine(new string('=', 80));

for (int lineNumber = 1; lineNumber <= model.TextBuffer.LineCount; lineNumber++)
{
    model.Tokenization.ForceTokenization(lineNumber);
    var lineTokens = model.Tokenization.GetLineTokens(lineNumber);
    PrintLine(lineNumber, lineTokens, theme);
}

model.Tokenization.Dispose();

return;

static bool TryGetLanguage(string filePath, out string languageName, out string extension)
{
    extension = Path.GetExtension(filePath).ToLowerInvariant();
    switch (extension)
    {
        case ".cs":
            languageName = "csharp";
            return true;
        case ".c":
            languageName = "c";
            return true;
        case ".cpp":
        case ".cc":
        case ".cxx":
        case ".h":
        case ".hpp":
        case ".hxx":
            languageName = "cpp";
            return true;
        default:
            languageName = string.Empty;
            return false;
    }
}

static void PrintUsage()
{
    Console.WriteLine("TokenizationTextMateDemo - tokenize C/C++/C# files with TextMate");
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project samples/TokenizationTextMateDemo -- <path-to-source-file>");
}

static void PrintLine(int lineNumber, FluidX.Tokenization.TokenStores.LineTokens lineTokens, Theme theme)
{
    Console.Write($"{lineNumber,4}: ");
    for (int i = 0; i < lineTokens.Count; i++)
    {
        var textSpan = lineTokens.GetTokenText(i);
        if (textSpan.Length == 0)
            continue;

        var metadata = lineTokens.GetMetadata(i);
        var markup = new Markup(textSpan.ToString(), new Style
        {
            Foreground = GetColor((int)metadata.Foreground, theme),
            Background = GetColor((int)metadata.Background, theme),
            Decoration = GetDecoration(metadata.FontStyle),
        });
        AnsiConsole.Write(markup);
    }
    Console.WriteLine(); // newline
}

// Utils for decoding token metadata
static Color GetColor(int colorId, Theme theme)
{
    if (colorId <= 2)
        return Color.Default;

    return HexToColor(theme.GetColor(colorId));
}

static Color HexToColor(string hexString)
{
    //replace # occurences
    if (hexString.IndexOf('#') != -1)
        hexString = hexString.Replace("#", "");

    byte r, g, b = 0;

    r = byte.Parse(hexString.Substring(0, 2), NumberStyles.AllowHexSpecifier);
    g = byte.Parse(hexString.Substring(2, 2), NumberStyles.AllowHexSpecifier);
    b = byte.Parse(hexString.Substring(4, 2), NumberStyles.AllowHexSpecifier);

    return new Color(r, g, b);
}

static Decoration GetDecoration(FontStyle fontStyle)
{
    Decoration result = Decoration.None;

    if (fontStyle == FontStyle.NotSet)
        return result;

    if ((fontStyle & FontStyle.Italic) != 0)
        result |= Decoration.Italic;

    if ((fontStyle & FontStyle.Underline) != 0)
        result |= Decoration.Underline;

    if ((fontStyle & FontStyle.Bold) != 0)
        result |= Decoration.Bold;

    return result;
}
