using FluidX.Tokenization;
using FluidX.TokenizationTextMateDemo;
using FluidX.TextBuffers;
using FluidX.TextModels;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

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
Console.WriteLine("Legend: comments=green, strings=yellow, numbers=cyan, keywords=blue");
Console.WriteLine(new string('=', 80));

for (int lineNumber = 1; lineNumber <= model.TextBuffer.LineCount; lineNumber++)
{
    model.Tokenization.ForceTokenization(lineNumber);
    var lineTokens = model.Tokenization.GetLineTokens(lineNumber);
    PrintLine(lineNumber, lineTokens);
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

static void PrintLine(int lineNumber, FluidX.Tokenization.TokenStores.LineTokens lineTokens)
{
    Console.Write($"{lineNumber,4}: ");

    for (int i = 0; i < lineTokens.Count; i++)
    {
        var textSpan = lineTokens.GetTokenText(i);
        if (textSpan.Length == 0)
            continue;

        var metadata = lineTokens.GetMetadata(i);
        var category = Classify(metadata.TokenType, textSpan);
        var style = GetStyle(category);

        var previousColor = Console.ForegroundColor;
        Console.ForegroundColor = style.Color;

        if (style.Bold)
            Console.Write("\u001b[1m");
        if (style.Italic)
            Console.Write("\u001b[3m");

        Console.Write(textSpan.ToString());

        if (style.Bold || style.Italic)
            Console.Write("\u001b[0m");
        Console.ForegroundColor = previousColor;
    }

    Console.WriteLine();
}

static TokenCategory Classify(FluidX.Tokenization.TokenStores.StandardTokenType tokenType, ReadOnlySpan<char> tokenText)
{
    if (tokenType == FluidX.Tokenization.TokenStores.StandardTokenType.Comment)
        return TokenCategory.Comment;
    if (tokenType == FluidX.Tokenization.TokenStores.StandardTokenType.String
        || tokenType == FluidX.Tokenization.TokenStores.StandardTokenType.RegEx)
        return TokenCategory.StringLike;

    if (tokenText.Length > 0)
    {
        if (char.IsDigit(tokenText[0]))
            return TokenCategory.Number;
        if (IsKeyword(tokenText))
            return TokenCategory.Keyword;
    }

    return TokenCategory.Other;
}

static bool IsKeyword(ReadOnlySpan<char> text)
{
    return text.SequenceEqual("if")
        || text.SequenceEqual("else")
        || text.SequenceEqual("for")
        || text.SequenceEqual("while")
        || text.SequenceEqual("switch")
        || text.SequenceEqual("case")
        || text.SequenceEqual("return")
        || text.SequenceEqual("class")
        || text.SequenceEqual("struct")
        || text.SequenceEqual("enum")
        || text.SequenceEqual("namespace")
        || text.SequenceEqual("using")
        || text.SequenceEqual("public")
        || text.SequenceEqual("private")
        || text.SequenceEqual("protected")
        || text.SequenceEqual("static")
        || text.SequenceEqual("const")
        || text.SequenceEqual("void")
        || text.SequenceEqual("int")
        || text.SequenceEqual("float")
        || text.SequenceEqual("double")
        || text.SequenceEqual("bool")
        || text.SequenceEqual("char")
        || text.SequenceEqual("string");
}

static TokenStyle GetStyle(TokenCategory category) => category switch
{
    TokenCategory.Comment => new(ConsoleColor.DarkGreen, Italic: true, Bold: false),
    TokenCategory.StringLike => new(ConsoleColor.DarkYellow, Italic: false, Bold: false),
    TokenCategory.Number => new(ConsoleColor.Cyan, Italic: false, Bold: false),
    TokenCategory.Keyword => new(ConsoleColor.Blue, Italic: false, Bold: true),
    _ => new(ConsoleColor.Gray, Italic: false, Bold: false),
};

readonly record struct TokenStyle(ConsoleColor Color, bool Italic, bool Bold);

enum TokenCategory
{
    Other,
    Comment,
    StringLike,
    Number,
    Keyword,
}
