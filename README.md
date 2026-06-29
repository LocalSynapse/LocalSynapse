# LocalSynapse

AI-powered local search for your files **and email** on Windows. Find anything by keyword or by meaning —
and let your AI assistant read your documents and spreadsheets — all on your own machine.

## Features

- **Hybrid search** — full-text (BM25) and semantic (local AI embeddings) together, with first-class
  Korean/English handling.
- **Reads your formats** — PDF, Word, PowerPoint, Hangul (HWP), CSV, Markdown, plain text, and more.
- **Faithful spreadsheets** — Excel files are read with their structure intact: cell coordinates, merged
  ranges, and correct types (dates stay dates, numbers stay numbers). Large workbooks parse quickly, up to
  25 MB.
- **Local email search** — your mail in `.eml`, `.msg`, `.mbox`, and exported mailbox JSON is searchable
  alongside your documents — by sender, subject, and body.
- **Works with your AI** — a built-in MCP server lets assistants like Claude search your index and read a
  faithful, structured view of your spreadsheets — locally, with source attribution.
- **Local-first & private** — search, indexing, and AI embedding run on your machine, and your files and
  their content never leave your device.

## Download

Get the latest installer from the [Releases page](https://github.com/LocalSynapse/LocalSynapse/releases/latest).

Project site: [localsynapse.com](https://localsynapse.com)

## What's in this repository

This repository hosts release artifacts only — Windows installers and their SHA-256 checksums on the Releases page. Active development happens in a private repository; the source tree is no longer published here from v2.15.0 onward.

Historical source from v1.0.0 through v2.14.0 remains in the repository's git history per the Apache License 2.0 under which those versions were distributed. Anyone who needs a snapshot of those versions can recover them from the tag history or from public forks.

## License

Apache License 2.0 — see [LICENSE](LICENSE).

The license covers the distributed binaries and all source code that was published in this repository through v2.14.0. v2.15.0 and later are distributed as installer binaries only.

## Issues and questions

Use the [Issues tab](https://github.com/LocalSynapse/LocalSynapse/issues) to report bugs, request features, or ask questions about LocalSynapse.

— Ryan
