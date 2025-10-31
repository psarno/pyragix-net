# PyRagix.Net.Tests

Developer guide for the xUnit test suite that keeps the .NET port aligned with the original Python project.

## Goals
- Mirror the behaviour tested in `../pyragix/tests` without depending on heavyweight components such as FAISS, ONNX models, or Ollama.
- Validate configuration guard-rails so bad settings fail fast.
- Exercise ingestion utilities (chunking, document cleanup) with fast, deterministic inputs.

## Project Layout
- `PyRagixConfigTests.cs` – verifies TOML binding and validation rules.
- `SemanticChunkerTests.cs` – covers semantic vs. fixed-window chunking logic.
- `DocumentProcessorTests.cs` – ensures HTML extraction strips scripts/styles and normalises whitespace.
- `Integration/HybridRetrieverIntegrationTests.cs` – exercises the indexing pipeline (SQLite + Lucene) and vector search via an in-memory FAISS substitute.

Add new files per area you port from the Python suite (e.g., retrieval stubs, file filtering). Keep tests small and self-contained.

## Running Locally
```bash
dotnet test PyRagix.Net.Tests/PyRagix.Net.Tests.csproj
```
Use the `--filter` option during focused runs, e.g. `--filter SemanticChunker`.

To inspect code coverage locally, install the report generator tool once and capture coverage:
```bash
dotnet tool install --global dotnet-reportgenerator-globaltool
dotnet test pyragix-net.sln --configuration Release --no-build --collect:"XPlat Code Coverage" --results-directory TestResults
reportgenerator -reports:TestResults/**/coverage.cobertura.xml -targetdir:CoverageReport -reporttypes:HtmlSummary
```
Open `CoverageReport/index.html` to view the summary.

## Conventions
- Prefer pure unit tests; replace disk/network dependencies with stubs or temporary files.
- Match the naming of Python tests where it clarifies intent (`test_x` → `XyzTests` class with `Task`/`Async` suffix when needed).
- Keep assertions expressive—verify both behaviour and any key side effects (e.g., overlap sizing, warnings).
- Document tricky scenarios inline with short comments so parity with Python remains obvious.

## Integration Tests
When you need cross-component coverage without native dependencies, use the `InMemoryVectorIndexFactory` helper. It provides a deterministic stand-in for FAISS so hybrid retrieval logic can be validated entirely in-process.

## Adding Coverage
When porting a Python test:
1. Read the source in `../pyragix/tests/<file>.py` to understand its intent.
2. Identify the corresponding .NET class/method. If missing, coordinate with the main project before introducing deep stubs.
3. Ideate minimal fixtures. For example, replace FAISS with lightweight in-memory collections.
4. Write the xUnit test. Aim for fast (<50ms) execution.
5. Run `dotnet test` and ensure the suite remains green.

If the .NET implementation lacks the hooks needed for parity, capture the gap in an issue or TODO comment so the ingestion/retrieval layers evolve with testability in mind.
