using EditorTextBuffers;
using EditorTextBuffers.PieceTree;

var pieceTreeBuilder = new PieceTreeTextBufferBuilder();
pieceTreeBuilder.AcceptChunk("abc\n");
pieceTreeBuilder.AcceptChunk("def");
var pieceTreeFactory = pieceTreeBuilder.Finish(true);
var pieceTree = pieceTreeFactory.Create(DefaultEndOfLine.LF);

Console.WriteLine(pieceTree.LineCount); // 2
Console.WriteLine(pieceTree.GetLineContent(1)); // abc
Console.WriteLine(pieceTree.GetLineContent(2)); // def
Console.WriteLine(pieceTree.GetValueInRange(new EditorTextBuffers.Range(1, 1, 2, 2), EndOfLinePreference.LF)); // abc\nd