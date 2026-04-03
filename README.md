# LocalSynapse

Local file search engine with MCP server. Indexes files across all drives and provides full-text search, filename search, and semantic search — all running 100% offline.

## Features

- **File scanning** — Indexes all fixed drives, skips cloud placeholders (OneDrive, Dropbox, etc.)
- **Full-text search** — BM25 ranking with Porter stemmer, Korean/English support
- **Semantic search** — BGE-M3 embedding model (optional, runs locally via ONNX)
- **MCP server** — stdio JSON-RPC server for AI assistant integration
- **Desktop UI** — Avalonia cross-platform GUI

## Build

```bash
dotnet build LocalSynapse.v2.sln
```

## Run

```bash
# GUI
dotnet run --project src/LocalSynapse.UI

# MCP server
dotnet run --project src/LocalSynapse.UI -- mcp
```

## Project Structure

```
src/
  LocalSynapse.Core/       # Models, Interfaces, DB
  LocalSynapse.Pipeline/   # Scan, parse, chunk, embed
  LocalSynapse.Search/     # BM25, Dense, Hybrid search
  LocalSynapse.Mcp/        # MCP stdio server
  LocalSynapse.UI/         # Avalonia desktop app
```

## MCP Configuration

Add LocalSynapse as an MCP server in your Claude Desktop or Claude Code config:

```json
{
  "mcpServers": {
    "localsynapse": {
      "command": "C:\\Program Files\\LocalSynapse\\LocalSynapse.exe",
      "args": ["mcp"],
      "env": {}
    }
  }
}
```

### Available MCP Tools

| Tool | Description |
|------|-------------|
| `search_files` | Search files by keyword or semantic query |
| `get_file_content` | Read the content of an indexed file |
| `list_indexed_files` | List all indexed files with filters |
| `get_pipeline_status` | Check indexing pipeline status |

## License

[Apache License 2.0](LICENSE)

## Code Signing Policy

LocalSynapse releases are signed to ensure authenticity and integrity.

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

### Team Roles

- **Authors & Reviewers**: [Repository Owner](https://github.com/LocalSynapse/LocalSynapse)
- **Approvers**: [Organization Owners](https://github.com/orgs/LocalSynapse/people?query=role%3Aowner)

### Privacy Policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

All file indexing, search, and AI embedding operations run 100% locally on your machine. No data is sent to any external server. No login or account is required.
