// Red-black trees share a mutable sentinel. Keep the assembly serialized until
// independent trees can safely execute concurrently.
[assembly: DoNotParallelize]
