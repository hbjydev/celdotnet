bench *args:
    dotnet run -c Release --project benchmarks/CelDotNet.Benchmarks -- --filter "*" {{args}}

bench-lexer *args:
    dotnet run -c Release --project benchmarks/CelDotNet.Benchmarks -- --filter "*LexerBenchmarks*" {{args}}

bench-parser *args:
    dotnet run -c Release --project benchmarks/CelDotNet.Benchmarks -- --filter "*ParserBenchmarks*" {{args}}

bench-compiler *args:
    dotnet run -c Release --project benchmarks/CelDotNet.Benchmarks -- --filter "*CompilerBenchmarks*" {{args}}

bench-e2e *args:
    dotnet run -c Release --project benchmarks/CelDotNet.Benchmarks -- --filter "*EndToEndBenchmarks*" {{args}}

bench-quick *args:
    dotnet run -c Release --project benchmarks/CelDotNet.Benchmarks -- --filter "*" --job Short {{args}}

bench-list:
    dotnet run -c Release --project benchmarks/CelDotNet.Benchmarks -- --list flat
