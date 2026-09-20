using System;
using BenchmarkDotNet.Running;

// Keep quick runs as the default, while allowing --job Default for noisy cases.
// Job attributes on benchmark classes would otherwise add a second job.
if (!Array.Exists(args, arg => arg is "--job" or "-j" || arg.StartsWith("--job=")))
    args = [.. args, "--job", "Short"];

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
