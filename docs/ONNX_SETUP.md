# ONNX Model Export Guide

PyRagix.Net requires ONNX models for embeddings and reranking. Export once from Python.

## Prerequisites

```bash
pip install optimum-onnx
pip install onnxruntime  # or onnxruntime-gpu for CUDA
```

## Export Models

```bash
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

## Verify

Check for `model.onnx` in each folder:
- `pyragix-net/Models/embeddings/model.onnx`
- `pyragix-net/Models/reranker/model.onnx`

## Tesseract OCR (Optional)

For image/PDF OCR:

**Windows:**
```bash
# Install from: https://github.com/UB-Mannheim/tesseract/wiki
# Then verify:
tesseract --version
```

**Linux:**
```bash
sudo apt install tesseract-ocr tesseract-ocr-eng
```

**macOS:**
```bash
brew install tesseract
```

Models are gitignored (large files).
