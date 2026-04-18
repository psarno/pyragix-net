<img width="500" height="auto" alt="image" src="https://github.com/user-attachments/assets/4c3fe1f7-b078-4fa9-99df-779c7f0867d1" />

# PyRagix.Net

.NET 9.0 port of [PyRagix](https://github.com/psarno/pyragix) - local-first RAG system with query expansion, cross-encoder reranking, hybrid search (FAISS + Lucene BM25), and semantic chunking. Runs entirely on your machine via Ollama and ONNX Runtime. No cloud APIs, no data leaving your network.

![License](https://img.shields.io/badge/license-MIT-green.svg)
![.NET](https://img.shields.io/badge/.NET-9.0-blue.svg)
![C#](https://img.shields.io/badge/C%23-12.0-purple.svg)

## Architecture

PyRagix.Net implements a multi-stage retrieval pipeline.

**Query Pipeline:**
```
User Query
  ↓
Multi-Query Expansion (3-5 variants via local LLM)
  ↓
Hybrid Search (FAISS semantic 70% + Lucene BM25 keyword 30%)
  ↓
Cross-Encoder Reranking (top-20 → top-7 by relevance)
  ↓
Answer Generation (local Ollama LLM)
```

**Ingestion Pipeline:**
```
Document Input (PDF, HTML, Images)
  ↓
Text Extraction (PdfPig, AngleSharp, Tesseract OCR)
  ↓
Semantic Chunking (sentence-boundary aware)
  ↓
Embedding Generation (ONNX Runtime - CPU/GPU)
  ↓
Dual Indexing (FAISS vector + Lucene BM25 keyword)
  ↓
SQLite Metadata Storage
```

Query expansion helps with recall on vague or paraphrased questions. Reranking filters out keyword-matched junk. Hybrid search handles structured queries (names, dates, IDs) that pure semantic search misses.

## Features

- **Query expansion** - generates multiple query variants via the local LLM to improve recall
- **Cross-encoder reranking** - re-scores retrieved chunks with a dedicated relevance model
- **Hybrid search** - FAISS semantic search + Lucene BM25 keyword matching, weighted and fused
- **Semantic chunking** - splits at sentence boundaries instead of fixed character counts
- **Multi-format ingestion** - PDF (PdfPig), HTML (AngleSharp), images (Tesseract OCR)
- **TOML configuration** - all RAG features toggled and tuned via `settings.toml`
- **Runs on Windows, Linux, and macOS**

## Quick Start

### Prerequisites

1. **.NET 9.0 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/9.0)
2. **Ollama** - [Download here](https://ollama.com) for local LLM inference
3. **Python 3.8+** - For one-time ONNX model export
4. **8GB+ RAM** (16GB+ recommended)

### Installation

```bash
git clone https://github.com/psarno/pyragix-net.git
cd pyragix-net

dotnet restore
dotnet build
dotnet test
```

For test-writing guidance, see [`PyRagix.Net.Tests/README.md`](PyRagix.Net.Tests/README.md).

### ONNX Models (One-Time Setup)

PyRagix.Net requires two ONNX models for embeddings and reranking.

> [!IMPORTANT]
> These models must be exported before first run. Without them, embedding and reranking will fail.

Export from Python:

```bash
pip install optimum[exporters-onnx]

# Embedding model (sentence-transformers)
optimum-cli export onnx \
  --model sentence-transformers/all-MiniLM-L6-v2 \
  --task feature-extraction \
  pyragix-net/Models/embeddings

# Reranker model (cross-encoder)
optimum-cli export onnx \
  --model cross-encoder/ms-marco-MiniLM-L-6-v2 \
  --task text-classification \
  pyragix-net/Models/reranker
```

See [`docs/ONNX_SETUP.md`](docs/ONNX_SETUP.md) for detailed instructions.

### Configure and Run

```bash
cp pyragix-net-console/settings.example.toml pyragix-net-console/settings.toml

# Start Ollama (separate terminal)
ollama pull qwen2.5:7b
ollama serve

# Run console app
cd pyragix-net-console

dotnet run -- ingest ./docs
dotnet run -- query "What is retrieval-augmented generation?"
```

### Vector Index Backends (Windows vs. Linux/WSL)

PyRagix.Net selects the vector index implementation automatically:

- **Windows (native)** - uses [FaissNet](https://www.nuget.org/packages/FaissNet) with the FAISS C++ backend.
- **Linux / macOS / WSL** - uses the built-in managed inner-product index (no native dependencies). Keeps the project runnable when FAISS binaries are unavailable.

> [!WARNING]
> When switching operating systems, delete previously generated artifacts before re-ingesting. The index format is not portable across backends.

```bash
rm -f faiss_index.bin
rm -f pyragix.db
rm -rf lucene_index
```

> [!TIP]
> TODO: Port the `--fresh` CLI flag from the Python version. The engine API already supports `fresh: true` (see library usage below), but the console app doesn't expose it yet. For now, delete artifacts manually.

## Usage

### As a Library

```csharp
using PyRagix.Net.Core;
using PyRagix.Net.Config;

// Load configuration from TOML file
var engine = RagEngine.FromSettings("settings.toml");

// Or configure programmatically
var config = new PyRagixConfig
{
    OllamaEndpoint = "http://localhost:11434",
    OllamaModel = "qwen2.5:7b",
    EmbeddingModelPath = "./Models/embeddings/model.onnx",
    RerankerModelPath = "./Models/reranker/model.onnx",
    EnableQueryExpansion = true,
    EnableHybridSearch = true,
    EnableReranking = true
};
var engine = new RagEngine(config);

// Ingest documents (PDF, HTML, images). Set fresh: true to recreate indexes from scratch.
await engine.IngestDocumentsAsync("./my-documents", fresh: false);

// Query with natural language
var answer = await engine.QueryAsync("What are the key findings?");
Console.WriteLine(answer);
```

### Console Application

```bash
dotnet run -- ingest <folder_path>
dotnet run -- query "<your question>"
```

## Configuration

PyRagix.Net uses `settings.toml` for configuration. Copy `settings.example.toml` and customize:

```toml
# Core Paths
EmbeddingModelPath = "./Models/embeddings/model.onnx"
RerankerModelPath = "./Models/reranker/model.onnx"
DatabasePath = "pyragix.db"
FaissIndexPath = "faiss_index.bin"
LuceneIndexPath = "lucene_index"

# Ollama LLM
OllamaEndpoint = "http://localhost:11434"
OllamaModel = "qwen2.5:7b"
OllamaTimeout = 180

# RAG Features
EnableQueryExpansion = true      # Multi-query generation
EnableHybridSearch = true        # FAISS + BM25 fusion
EnableReranking = true           # Cross-encoder scoring
EnableSemanticChunking = true   # Sentence-aware splitting

# Performance Tuning
EmbeddingBatchSize = 16         # Higher = faster (more RAM)
DefaultTopK = 7                  # Top chunks for answer generation
HybridAlpha = 0.7               # 70% semantic, 30% keyword
QueryExpansionCount = 3         # Number of query variants

# GPU Acceleration (requires CUDA)
GpuEnabled = false              # Set true for GPU inference
GpuDeviceId = 0
```

- **Query expansion** generates variant phrasings of your query. Helps most with vague or ambiguous questions. `QueryExpansionCount` controls how many variants (default 3).
- **Reranking** re-scores the top candidates with a cross-encoder. Filters out chunks that matched on keywords but aren't actually relevant. `DefaultTopK` sets the final result count (default 7).
- **Hybrid search** fuses FAISS and Lucene BM25 results. Mostly useful for structured queries (names, dates, IDs) that pure vector search misses. `HybridAlpha` controls the weight split.
- **Semantic chunking** splits at sentence boundaries instead of fixed character counts. Better context preservation.

### Hardware Tuning

For memory-constrained systems (8-12GB RAM):
```toml
EmbeddingBatchSize = 8
OcrBaseDpi = 100
```

For high-performance systems (32GB+ RAM):
```toml
EmbeddingBatchSize = 32
OcrBaseDpi = 200
```

> [!NOTE]
> GPU acceleration requires CUDA. TODO: Port `ExecutionProviderPreference` from the Python version for auto-detection. Currently uses a simple `GpuEnabled` bool.

```toml
GpuEnabled = true
GpuDeviceId = 0
```

## Project Structure

```
pyragix-net/
├── pyragix-net.sln                    # Visual Studio solution
├── docs/
│   └── ONNX_SETUP.md                  # Model export guide
│
├── pyragix-net/                       # RAG Engine (Class Library)
│   ├── Config/
│   │   ├── PyRagixConfig.cs           # TOML configuration loader
│   │   ├── settings.toml              # User configuration (gitignored)
│   │   └── settings.example.toml      # Configuration template
│   ├── Core/
│   │   ├── RagEngine.cs               # Public API entry point
│   │   ├── Models/
│   │   │   └── ChunkMetadata.cs       # EF Core metadata entity
│   │   └── Data/
│   │       └── PyRagixDbContext.cs    # SQLite database context
│   ├── Ingestion/
│   │   ├── DocumentProcessor.cs       # PDF/HTML/OCR extraction
│   │   ├── SemanticChunker.cs         # Sentence-aware text splitting
│   │   ├── EmbeddingService.cs        # ONNX embedding generation
│   │   ├── IndexService.cs            # FAISS + Lucene indexing
│   │   └── IngestionService.cs        # Pipeline orchestration
│   ├── Retrieval/
│   │   ├── QueryExpander.cs           # Multi-query generation
│   │   ├── HybridRetriever.cs         # FAISS + BM25 fusion (RRF)
│   │   ├── Reranker.cs                # Cross-encoder ONNX scoring
│   │   ├── OllamaGenerator.cs         # LLM answer generation
│   │   └── RetrievalService.cs        # Pipeline orchestration
│   ├── Models/                        # .onnx files (gitignored)
│   └── pyragix-net.csproj             # .NET 9.0 class library
│
├── pyragix-net-console/               # Console Demo App
│   ├── Program.cs                     # CLI implementation
│   ├── Models/                        # .onnx models location
│   ├── settings.toml                  # App-specific config (gitignored)
│   └── pyragix-net-console.csproj     # .NET 9.0 executable
│
└── PyRagix.Net.Tests/                 # xUnit Test Project
    └── TestInfrastructure/            # InMemoryVectorIndex, TempDirectory
```

## Dependencies

**Core AI/ML:**
- **Microsoft.SemanticKernel** (1.66.0+) - AI orchestration framework
- **Microsoft.ML.OnnxRuntime** (1.23.2+) - Embedding/reranking inference (CPU)
- **Microsoft.ML.OnnxRuntime.Gpu** (1.23.2+) - Optional GPU acceleration

**Search & Indexing:**
- **FaissNet** (1.1.0+) - Vector search (Windows only)
- **Lucene.Net** (4.8.0+) - BM25 keyword search
- **Lucene.Net.Analysis.Common** - Text analysis and tokenization
- **Lucene.Net.QueryParser** - Query parsing

**Document Processing:**
- **UglyToad.PdfPig** (1.7.0+) - PDF text extraction
- **AngleSharp** (1.1.2+) - HTML/XML parsing
- **Tesseract** (5.2.0+) - OCR for images

**Infrastructure:**
- **Microsoft.EntityFrameworkCore.Sqlite** (9.0.10+) - Metadata storage
- **Tomlyn** (0.19.0+) - TOML configuration parsing
- **System.Text.Json** (9.0.10+) - JSON serialization

## Contributing

```bash
git clone https://github.com/psarno/pyragix-net.git
cd pyragix-net
dotnet restore
dotnet build
```

**Rules:**
- .NET 9.0 with nullable reference types enabled
- Follow existing architectural patterns (service-based pipeline design)
- Add XML documentation comments for public APIs
- xUnit tests for new features (see `PyRagix.Net.Tests/` for patterns)

## License

MIT License - see [LICENSE](LICENSE) for details.

## Acknowledgements

.NET port of [PyRagix](https://github.com/psarno/pyragix). Built on FAISS, Ollama, Semantic Kernel, ONNX Runtime, and Sentence Transformers.
