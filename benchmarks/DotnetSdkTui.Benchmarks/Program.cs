using BenchmarkDotNet.Running;

// Run all benchmarks (or filter with --filter '*Version*', etc.)
BenchmarkSwitcher.FromAssembly(System.Reflection.Assembly.GetExecutingAssembly()).Run(args);
