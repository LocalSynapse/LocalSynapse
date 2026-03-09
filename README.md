# LocalSynapse — AI-Powered Local File Search for Windows

<p align="center">
  <strong>Search inside files by content and meaning — 100% offline</strong><br>
  <em>파일 이름이 아닌 내용으로 검색. 완전 오프라인 AI 파일 검색.</em>
</p>

<p align="center">
  <a href="https://github.com/LocalSynapse/LocalSynapse/releases/latest">
    <img src="https://img.shields.io/github/v/release/LocalSynapse/LocalSynapse?style=flat-square&label=Latest%20Release" alt="Latest Release">
  </a>
  <a href="https://github.com/LocalSynapse/LocalSynapse/releases">
    <img src="https://img.shields.io/github/downloads/LocalSynapse/LocalSynapse/total?style=flat-square&label=Downloads" alt="Total Downloads">
  </a>
  <a href="https://localsynapse.com">
    <img src="https://img.shields.io/badge/Website-localsynapse.com-4F46E5?style=flat-square" alt="Website">
  </a>
  <a href="LICENSE">
    <img src="https://img.shields.io/badge/License-Apache%202.0-green?style=flat-square" alt="License">
  </a>
</p>

<p align="center">
  <a href="https://localsynapse.com"><strong>🌐 Website</strong></a> ·
  <a href="https://github.com/LocalSynapse/LocalSynapse/releases/latest"><strong>⬇️ Download</strong></a> ·
  <a href="https://localsynapse.com/en/blog"><strong>📝 Blog</strong></a>
</p>

---

## Why LocalSynapse?

You know the file exists somewhere on your PC. You wrote it last week. But the filename? Something like `report_final_v3_revised.docx`. Windows search can't find it by content. **Everything** can find filenames instantly — but can't look inside files.

**LocalSynapse fills the gap.** It searches inside your documents by content and meaning, not just filenames. Everything runs locally on your machine — no cloud, no upload, no account needed.

---

## Features

### 🔍 3-Tier Hybrid Search

| Tier | What it does | How it works |
|------|-------------|-------------|
| **Filename** | Find files by name instantly | Indexed filename search |
| **Full-Text** | Search inside document contents | BM25 ranking with SQLite FTS5 |
| **Semantic** | Find files by meaning, not exact words | BGE-M3 dense vector embeddings via ONNX |

Search for "revenue forecast" and find documents that say "sales projection" — because the AI understands they mean the same thing.

### 📄 13+ Supported File Formats

| Category | Formats |
|----------|---------|
| **Office** | DOCX, XLSX, PPTX, PDF |
| **Korean** | HWP, HWPX |
| **Plain text** | TXT, MD, CSV, JSON, LOG |
| **Email** | EML, MSG |

### 🔒 100% Offline & Private

- All AI processing happens on your PC — the BGE-M3 model runs locally via ONNX Runtime
- **Zero telemetry** — no analytics, no tracking, no data collection
- **No cloud sync** — your files never leave your device
- **No login required** — install and start searching immediately
- **No internet needed** — works in air-gapped environments

Perfect for **financial institutions**, **government agencies**, and **security-restricted corporate environments** where cloud tools are not allowed.

### ⚡ Fast

- ~0.3 second search across thousands of documents
- Background indexing with automatic updates
- Lightweight: ~150MB app + ~2.3GB AI model (optional — BM25 search works without it)

### 🤖 MCP Server for AI Agents

LocalSynapse includes a built-in [MCP (Model Context Protocol)](https://modelcontextprotocol.io/) server. Connect AI agents like Claude or Cursor to search your local files programmatically — giving them local memory and context.

```
LocalSynapse.exe mcp    # Start as MCP server
```

---

## Download & Install

### 📥 [Download Latest Release](https://github.com/LocalSynapse/LocalSynapse/releases/latest)

1. Download `LocalSynapse-x.x.x-Setup.exe`
2. Run the installer (Windows SmartScreen: click "More info" → "Run anyway")
3. Launch — scanning starts automatically in the background

### System Requirements

| Requirement | Minimum | Recommended |
|-------------|---------|-------------|
| **OS** | Windows 10 x64 | Windows 11 x64 |
| **RAM** | 4 GB | 8 GB |
| **Disk** | ~150 MB (app) | + ~2.3 GB (AI model) |

> **Note**: Semantic search requires the AI model (~2.3GB, auto-downloaded on first use). Without it, filename + full-text (BM25) search still works perfectly.

---

## Everything vs LocalSynapse

| | Everything | LocalSynapse |
|---|-----------|-------------|
| **Searches** | Filenames | Filenames + file contents + meaning |
| **Method** | Exact match + regex | BM25 + AI semantic vectors |
| **Office docs** | Names only | Full content parsing |
| **Version grouping** | No | Automatic |
| **Content snippets** | No | Shows matching text |
| **Speed** | Instant | ~0.3 seconds |
| **Price** | Free | Free |
| **Offline** | Yes | Yes |

**Use both.** Everything for quick filename lookups. LocalSynapse when you need to find files by what's inside them.

> Read the full comparison: [Everything Search Is Great — But It Can't Search Inside Files](https://localsynapse.com/en/blog/everything-search-alternative)

---

## Blog & Guides

- [How to Search Inside Files on Windows: 5 Methods Compared](https://localsynapse.com/en/blog/search-inside-files-windows)
- [Everything Search Is Great — But It Can't Search Inside Files](https://localsynapse.com/en/blog/everything-search-alternative)
- [Give Your AI Agent a Local Memory: MCP Server for File Search](https://localsynapse.com/en/blog/mcp-local-file-search)
- [How to Search Inside Word Documents on Windows](https://localsynapse.com/en/blog/search-inside-word-documents)
- [Search Got a Lot Faster — v1.0.2 & v1.0.3 Update](https://localsynapse.com/en/blog/v102-v103-update)

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full version history.

---

## License

[Apache License 2.0](LICENSE)

---

<p align="center">
  <strong>🔍 Find files by meaning, not just filenames.</strong><br>
  <a href="https://localsynapse.com">localsynapse.com</a>
</p>
