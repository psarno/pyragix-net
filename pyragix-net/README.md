# PyRagix.Net

A **local-first** Retrieval-Augmented Generation (RAG) engine for .NET 9.0, porting the core capabilities of [PyRagix](https://github.com/psarno/PyRagix) to C#. Implements modern RAG techniques including query expansion, hybrid search (semantic + keyword), cross-encoder reranking, and local LLM generation via Ollama.

![.NET](https://img.shields.io/badge/.NET-9.0-blue.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)
![Build](https://img.shields.io/badge/build-passing-brightgreen.svg)

## Features

- **100% Local Processing**: All document processing, indexing, and search run offline (requires local Ollama for LLM)
- **Multi-Format Ingestion**: PDF (PdfPig), HTML (AngleSharp), Images (Tesseract OCR)
- **Semantic Chunking**: Sentence-boundary-aware text splitting for context coherence
- **ONNX Embeddings**: Local inference via ONNX Runtime (CPU/GPU support)
- **Hybrid Search**: FAISS vector search (70%) + Lucene BM25 keyword search (30%) with Reciprocal Rank Fusion
- **Query Expansion**: Generates 3-5 query variants for improved recall
- **Cross-Encoder Reranking**: Precision re-scoring of top candidates
- **Local LLM Generation**: Answer synthesis via Ollama (any model: llama, qwen, mistral, etc.)

## Quick Start

### Prerequisites

1. **.NET 9.0 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/9.0)
2. **Ollama** - [Download](https://ollama.com) and run `ollama serve`
3. **ONNX Models** (see Setup below)
4. **Tesseract OCR Data** (optional, for image processing)

### Installation

```bash
# Clone repository
git clone https://github.com/psarno/pyragix-net.git
cd pyragix-net

# Restore packages
dotnet restore

# Build
dotnet build
```

### Basic Usage

```csharp
using PyRagix.Net.Core;
using PyRagix.Net.Config;

// Load configuration from settings.toml
var engine = RagEngine.FromSettings("settings.toml");

// Or create config programmatically
var config = new PyRagixConfig
{
    OllamaEndpoint = "http://localhost:11434",
    OllamaModel = "qwen2.5:7b",
    EmbeddingModelPath = "./Models/embeddings/model.onnx",
    RerankerModelPath = "./Models/reranker/model.onnx"
};
var engine = new RagEngine(config);

// Ingest documents
await engine.IngestDocumentsAsync("./docs");

// Query
var answer = await engine.QueryAsync("What is retrieval-augmented generation?");
Console.WriteLine(answer);
```

## Setup Guide

### 1. ONNX Model Export

PyRagix.Net requires two ONNX models. Export from Python using Hugging Face `optimum`:

```bash
# Install optimum (one-time)
pip install optimum[exporters]

# Export embedding model (384-dim vectors)
optimum-cli export onnx --model sentence-transformers/all-MiniLM-L6-v2 ./Models/embeddings

# Export reranker model
optimum-cli export onnx --model cross-encoder/ms-marco-MiniLM-L-6-v2 ./Models/reranker
```

**Expected output:**
```
Models/
├── embeddings/
│   ├── model.onnx
│   ├── tokenizer.json
│   └── ...
└── reranker/
    ├── model.onnx
    ├── tokenizer.json
    └── ...
```

### 2. Tesseract OCR Setup (Optional)

For image document processing (PNG, JPEG, etc.):

1. Download tessdata: https://github.com/tesseract-ocr/tessdata
2. Place `eng.traineddata` in `./tessdata/` folder
3. Update config: `OcrBaseDpi = 150` (adjust for quality vs speed)

### 3. Ollama Setup

```bash
# Install Ollama from https://ollama.com

# Pull a model (e.g., qwen2.5:7b, llama3.2, mistral)
ollama pull qwen2.5:7b

# Start server
ollama serve
```

### 4. Configuration

Copy `settings.example.toml` to `settings.toml` and customize:

```toml
# Paths
EmbeddingModelPath = "./Models/embeddings/model.onnx"
RerankerModelPath = "./Models/reranker/model.onnx"
DatabasePath = "pyragix.db"

# Ollama
OllamaEndpoint = "http://localhost:11434"
OllamaModel = "qwen2.5:7b"

# RAG Features
EnableQueryExpansion = true
EnableHybridSearch = true
EnableReranking = true
EnableSemanticChunking = true

# Performance
EmbeddingBatchSize = 16
DefaultTopK = 7
HybridAlpha = 0.7  # 70% semantic, 30% keyword
```

## Architecture

```
User Documents → Ingestion Pipeline → Indexes → Query Pipeline → Answer
                      ↓                   ↓           ↓
                 [Process]          [FAISS]     [Expand Query]
                 [Chunk]            [Lucene]    [Retrieve]
                 [Embed]            [SQLite]    [Rerank]
                 [Index]                        [Generate]
```

**Ingestion Pipeline:**
1. DocumentProcessor: Extract text (PDF/HTML/OCR)
2. SemanticChunker: Split at sentence boundaries
3. EmbeddingService: Generate ONNX vectors
4. IndexService: FAISS (vectors) + Lucene (keywords) + SQLite (metadata)

**Query Pipeline:**
1. QueryExpander: Generate 3-5 variants via Ollama
2. HybridRetriever: FAISS + BM25 fusion (RRF)
3. Reranker: Cross-encoder scoring
4. OllamaGenerator: LLM answer synthesis

## Project Structure

```
PyRagix.Net/
├── Config/
│   ├── PyRagixConfig.cs          # TOML configuration loader
│   └── settings.example.toml     # Config template
├── Core/
│   ├── RagEngine.cs              # Public API
│   ├── Models/
│   │   └── ChunkMetadata.cs      # EF Core entity
│   └── Data/
│       └── PyRagixDbContext.cs   # SQLite context
├── Ingestion/
│   ├── DocumentProcessor.cs      # PDF/HTML/OCR extraction
│   ├── SemanticChunker.cs        # Sentence-aware splitting
│   ├── EmbeddingService.cs       # ONNX inference
│   ├── IndexService.cs           # FAISS + Lucene
│   └── IngestionService.cs       # Pipeline orchestration
├── Retrieval/
│   ├── QueryExpander.cs          # Multi-query generation
│   ├── HybridRetriever.cs        # FAISS + BM25 fusion
│   ├── Reranker.cs               # Cross-encoder ONNX
│   ├── OllamaGenerator.cs        # LLM client
│   └── RetrievalService.cs       # Pipeline orchestration
├── Models/                        # .onnx files (git-ignored)
└── pyragix-net.csproj
```

## Dependencies

### Core AI/ML
- **Microsoft.SemanticKernel** (1.66.0+) - AI orchestration
- **Microsoft.ML.OnnxRuntime** (1.23.2+) - Embedding/reranking inference
- **Microsoft.ML.OnnxRuntime.Gpu** (1.23.2+) - Optional GPU acceleration

### Search
- **FaissNet** (1.1.0+, Windows) - Native FAISS vector search
- **Lucene.Net** (4.8.0+) - BM25 keyword search
- **Lucene.Net.Analysis.Common** - Text analyzers
- **Lucene.Net.QueryParser** - Query parsing

### Document Processing
- **UglyToad.PdfPig** (1.7.0+) - PDF text extraction
- **AngleSharp** (1.1.2+) - HTML parsing
- **Tesseract** (5.2.0+) - OCR for images

### Infrastructure
- **Microsoft.EntityFrameworkCore.Sqlite** (9.0.10+) - Metadata storage
- **Tomlyn** (0.19.0+) - TOML configuration
- **System.Text.Json** (9.0.10+) - JSON serialization

## Performance

- **Ingestion**: ~10-50 docs/min (depends on OCR usage)
- **Query**: ~1-3 sec (expansion + retrieval + reranking), ~5-15 sec (+ LLM generation)
- **Memory**: ~2-4 GB for 10k chunks (CPU), ~4-8 GB (GPU)

**Optimization Tips:**
- Set `EmbeddingBatchSize = 32` for faster ingestion (requires more RAM)
- Disable `EnableQueryExpansion` for 2x faster queries (lower recall)
- Use GPU via `GpuEnabled = true` for 5-10x faster embedding/reranking

## Troubleshooting Native Vector Indexes

PyRagix.Net automatically selects a vector index backend:

- **Windows** → `FaissNet` (native FAISS bindings)
- **Linux / macOS (including WSL)** → managed fallback (`ManagedVectorIndex`) with exhaustive inner-product search

If the engine detects an old FAISS artifact after switching operating systems, delete the generated files and re-run ingestion:

```bash
rm -f faiss_index.bin
rm -f pyragix.db
rm -rf lucene_index
```

Need a different backend? The vector index is abstracted behind `IVectorIndexFactory` (see `pyragix-net/Ingestion/Vector`). You can supply your own factory when constructing `IndexService` or `HybridRetriever`. The test suite ships an `InMemoryVectorIndexFactory` example under `PyRagix.Net.Tests/TestInfrastructure` that uses pure C# data structures for predictable behaviour.
- In DI-friendly scenarios, register the factory you prefer and inject it:  
  ```csharp
  services.AddSingleton<IVectorIndexFactory, InMemoryVectorIndexFactory>();
  services.AddScoped(sp => new IndexService(config, dbContext, sp.GetRequiredService<IVectorIndexFactory>()));
  services.AddScoped(sp => new HybridRetriever(config, dbContext, sp.GetRequiredService<IVectorIndexFactory>()));
  ```
- The managed fallback is intended for development and testing; production deployments should continue using FAISS for accuracy and performance.

## Comparison: PyRagix Python vs PyRagix.Net

| Feature | PyRagix (Python) | PyRagix.Net (C#) |
|---------|------------------|------------------|
| **Core RAG Pipeline** | ✅ | ✅ |
| Query Expansion | ✅ | ✅ |
| Hybrid Search (FAISS+BM25) | ✅ | ✅ |
| Cross-Encoder Reranking | ✅ | ✅ |
| Semantic Chunking | ✅ | ✅ |
| Local Ollama LLM | ✅ | ✅ |
| **Document Formats** |  |  |
| PDF | ✅ PyMuPDF | ✅ PdfPig |
| HTML | ✅ BeautifulSoup | ✅ AngleSharp |
| Images (OCR) | ✅ PaddleOCR | ✅ Tesseract |
| **Platform** |  |  |
| Cross-Platform | ✅ | ✅ |
| Web UI | ✅ FastAPI + TypeScript | ❌ (engine only) |
| **Type Safety** | ✅ pyright strict | ✅ C# 9.0 nullable |

## Roadmap

- [x] Core ingestion pipeline
- [x] Core query pipeline
- [x] ONNX embedding/reranking
- [x] Hybrid FAISS + Lucene search
- [x] Console demo app
- [ ] Unit/integration tests
- [ ] Incremental ingestion (resume from checkpoint)
- [ ] Advanced FAISS IVF indexing (large-scale)
- [ ] Optional Blazor/WPF UI

## Contributing

Contributions welcome! This is a portfolio project showcasing C# expertise alongside the Python original.

## License

MIT License - see [LICENSE](LICENSE) for details.

## Acknowledgements

- **Python Original**: [PyRagix](https://github.com/psarno/PyRagix) by Patrick Sarno
- **FAISS**: Meta AI Research
- **Ollama**: Ollama Team
- **Semantic Kernel**: Microsoft
- **ONNX Runtime**: Microsoft

Built for **privacy, performance, and pure .NET**.
