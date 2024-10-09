using System.Text.RegularExpressions;

namespace EditorTextBuffers.PieceTree;

public class PieceTreeTextBufferFactory
{
    private readonly IList<StringBuffer> _chunks;
    private readonly string _bom;
    private readonly int _cr;
    private readonly int _lf;
    private readonly int _crlf;
    private readonly bool _containsRTL;
    private readonly bool _containsUnusualLineTerminators;
    private readonly bool _isBasicASCII;
    private readonly bool _normalizeEOL;

    public PieceTreeTextBufferFactory(
        IList<StringBuffer> chunks,
        string bom,
        int cr,
        int lf,
        int crlf,
        bool containsRTL,
        bool containsUnusualLineTerminators,
        bool isBasicASCII,
        bool normalizeEOL)
    {
        _chunks = chunks;
        _bom = bom;
        _cr = cr;
        _lf = lf;
        _crlf = crlf;
        _containsRTL = containsRTL;
        _containsUnusualLineTerminators = containsUnusualLineTerminators;
        _isBasicASCII = isBasicASCII;
        _normalizeEOL = normalizeEOL;
    }

    private string _getEOL(DefaultEndOfLine defaultEOL)
    {
        int totalEOLCount = _cr + _lf + _crlf;
        int totalCRCount = _cr + _crlf;
        if (totalEOLCount == 0) {
            // This is an empty file or a file with precisely one line
            return (defaultEOL == DefaultEndOfLine.LF? "\n" : "\r\n");
        }
        if (totalCRCount > totalEOLCount / 2) {
            // More than half of the file contains \r\n ending lines
            return "\r\n";
        }
        // At least one line more ends in \n
        return "\n";
    }

    public PieceTreeTextBuffer Create(DefaultEndOfLine defaultEOL)
    {
        string eol = _getEOL(defaultEOL);
        var chunks = _chunks;

        if (_normalizeEOL &&
            ((eol == "\r\n" && (_cr > 0 || _lf > 0)) || (eol == "\n" && (_cr > 0 || _crlf > 0))))
        {
            // Normalize pieces
            for (int i = 0, len = chunks.Count; i < len; i++)
            {
                string str = chunks[i].Buffer;
                // TODO: optimize Regex
                str = Regex.Replace(str, @"\r\n|\r|\n", eol);
                var newLineStart = LineStarts.CreateFast(str);
                chunks[i] = new StringBuffer(str, newLineStart);
            }
        }

        return new PieceTreeTextBuffer(chunks, _bom, eol, _containsRTL, _containsUnusualLineTerminators, _isBasicASCII, _normalizeEOL);
    }
}

public class PieceTreeTextBufferBuilder
{
    private readonly List<StringBuffer> _chunks = [];
    private string BOM = "";

    private bool _hasPreviousChar = false;
    private char _previousChar = '\0';
    private readonly List<int> _tmpLineStarts = [];

    private int cr = 0;
    private int lf = 0;
    private int crlf = 0;
    private bool containsRTL = false;
    private bool containsUnusualLineTerminators = false;
    private bool isBasicASCII = true;

    public void AcceptChunk(string chunk)
    {
        if (chunk.Length == 0)
            return;

        if (_chunks.Count == 0 && chunk.StartsWith((char)CharCode.UTF8_BOM))
        {
            char bom = (char)CharCode.UTF8_BOM;
            BOM = bom.ToString();
            chunk = chunk.Substring(1);
        }

        char lastChar = chunk[^1];
        if (lastChar == '\r' || (lastChar >= 0xD800 && lastChar <= 0xDBFF))
        {
            // last character is \r or a high surrogate => keep it back
            _acceptChunk1(chunk[..^1], false);
            _hasPreviousChar = true;
            _previousChar = lastChar;
        }
        else
        {
            _acceptChunk1(chunk, false);
            _hasPreviousChar = false;
            _previousChar = lastChar;
        }
    }

    private void _acceptChunk1(string chunk, bool allowEmptyStrings){
        if (!allowEmptyStrings && chunk.Length == 0)
            return;

        if (_hasPreviousChar)
            _acceptChunk2(_previousChar + chunk);
        else
            _acceptChunk2(chunk);
    }

    private void _acceptChunk2(string chunk)
    {
        var lineStarts = LineStarts.Create(_tmpLineStarts, chunk);

        _chunks.Add(new StringBuffer(chunk, lineStarts.Starts));
        cr += lineStarts.CR;
        lf += lineStarts.LF;
        crlf += lineStarts.CRLF;

        if (!lineStarts.IsBasicAscii)
        {
            // this chunk contains non basic ASCII characters
            isBasicASCII = false;

            // FIXME:
            //if (!containsRTL)
            //    containsRTL = strings.containsRTL(chunk);

            //if (!containsUnusualLineTerminators)
            //    containsUnusualLineTerminators = strings.containsUnusualLineTerminators(chunk);
        }
    }
}
