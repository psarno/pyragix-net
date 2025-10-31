# PyRagix.Net Technology Explainer

_A plain-language tour of the acronyms behind the engine._

## Retrieval-Augmented Generation (RAG)
Think of RAG as an open-book exam for AI. Instead of relying only on the model’s memorised knowledge, we let it search a curated library of your documents before answering. The result: fresher, more accurate responses without sending data to the cloud.

## Semantic Chunking
Documents are sliced into snack-sized “chunks” the AI can digest. Instead of chopping purely by character count, semantic chunking keeps sentences together so you don’t end up with half a paragraph in one chunk and half in another. Clear context in, better answers out.

## Embeddings & Vector Search (FAISS)
Embeddings convert text into lists of numbers that capture meaning. FAISS (Facebook AI Similarity Search) is a high-speed index that finds the closest matches between those vectors. Imagine a librarian who can instantly pull the most relevant pages that “feel” like your question.

## BM25 Keyword Search
BM25 is the classic keyword search algorithm—like Ctrl+F on steroids. It boosts results that match your exact phrasing and downranks fluff. We blend BM25 with vector search so precise keywords and fuzzy semantic matches both play a role.

## Query Expansion (Ollama)
Ollama lets us run large language models locally. We ask the model to rephrase a user’s question a few different ways—similar to brainstorming search queries. More phrasings mean more chances to match relevant content in the index.

## Cross-Encoder Reranking
After the first search pass, a reranker reads each candidate chunk alongside the original question, like a careful editor. It scores how well they match, ensuring the final answer is built from the most relevant evidence.

## ONNX Runtime
ONNX packages machine-learning models in a portable format. Think of it as “plug and play” for AI. We export models once, then run them anywhere without pulling in Python dependencies—perfect for a .NET-native pipeline.

## Tesseract OCR
Tesseract does optical character recognition: it turns scanned images into text. If a PDF is actually just pictures (looking at you, scanned contracts), Tesseract is the one transcribing them so the rest of the pipeline can search the content.

## SQLite Metadata Store
We maintain a lightweight SQLite database to track where each chunk came from—file path, page number, timestamps. When the AI answers, we can point back to the exact source, keeping the system auditable and trustworthy.

## Putting It Together
1. **Ingestion**: Text is extracted (Tesseract/HTML parsing), chunked smartly, encoded into vectors, and stored in FAISS, Lucene (BM25), and SQLite.
2. **Retrieval**: Incoming questions are rephrased (Ollama), searched via vectors (FAISS) and keywords (BM25), then reranked for the best matches.
3. **Generation**: The top chunks are passed to an LLM (Ollama) to produce a grounded answer along with citations.

The result is a local-first assistant that understands your documents and explains its reasoning—built entirely on open, portable tooling.
