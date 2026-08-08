using BenchmarkDotNet.Running;

// Run all benchmarks: dotnet run -c Release --filter "*"
// Run a specific benchmark: dotnet run -c Release --filter "*ReadLineBenchmark*"
// List benchmarks: dotnet run -c Release --list flat

var _ = BenchmarkRunner.Run(typeof(Program).Assembly);
