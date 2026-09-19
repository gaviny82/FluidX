// Piece trees share a mutable red-black-tree sentinel. Keep the assembly serialized
// until the sentinel is made safe for independent concurrent trees.
[assembly: DoNotParallelize]
