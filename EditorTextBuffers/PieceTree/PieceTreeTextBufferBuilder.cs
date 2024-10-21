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
    private string _BOM = ""; // Either "" or "\uFEFF"

    private bool _hasPreviousChar = false;
    private char _previousChar = '\0';
    private readonly List<int> _tmpLineStarts = [];

    private int _cr = 0;
    private int _lf = 0;
    private int _crlf = 0;
    private bool _containsRTL = false;
    private bool _containsUnusualLineTerminators = false;
    private bool _isBasicASCII = true;

    public void AcceptChunk(string chunk)
    {
        if (chunk.Length == 0)
            return;

        if (_chunks.Count == 0 && chunk.StartsWith((char)CharCode.UTF8_BOM))
        {
            char bom = (char)CharCode.UTF8_BOM;
            _BOM = bom.ToString();
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
        _cr += lineStarts.CR;
        _lf += lineStarts.LF;
        _crlf += lineStarts.CRLF;

        if (!lineStarts.IsBasicAscii)
        {
            // this chunk contains non basic ASCII characters
            _isBasicASCII = false;

            if (!_containsRTL)
                _containsRTL = chunk.ContainsRTL();

            if (!_containsUnusualLineTerminators)
                _containsUnusualLineTerminators = chunk.ContainsUnusualLineTerminators();
        }
    }

    public PieceTreeTextBufferFactory Finish(bool normalizeEOL = true)
    {
        _Finish();
        return new PieceTreeTextBufferFactory(
            _chunks,
            _BOM,
            _cr,
            _lf,
            _crlf,
            _containsRTL,
            _containsUnusualLineTerminators,
            _isBasicASCII,
            normalizeEOL
        );
    }

    private void _Finish()
    {
        if (_chunks.Count == 0)
        {
            _acceptChunk1("", true);
        }

        if (this._hasPreviousChar)
        {
            this._hasPreviousChar = false;
            // recreate last chunk
            var lastChunk = _chunks[_chunks.Count - 1];
            lastChunk.Buffer += _previousChar;
            var newLineStarts = LineStarts.CreateFast(lastChunk.Buffer);
            lastChunk.LineStarts = newLineStarts;
            if (_previousChar == '\r')
            {
                _cr++;
            }
        }
    }
}
