# Changelog

## [1.0.3] - 2026-03-04

### Improved
- Push batch buffering for WebView2 IPC (200ms timer flush)
- FTS5 filename search for faster results
- Phase 1/Phase 2 parallel execution
- 30-second LRU query cache

## [1.0.2] - 2026-03-03

### Fixed
- Search results no longer change when switching tabs and returning
- Folder results no longer disappear due to race condition between quick and full search
- Quick search (filename/folder) no longer returns unlimited results (LIMIT 200)

### Improved
- Search speed: filename/folder matches appear within ~100ms
- Full search TopK reduced from 1000 to 300 for faster sorting and lower memory usage
- Sequential Phase 1→Phase 2 execution ensures stable result merging

## [1.0.1] - 2026-02-27

### Fixed
- Search banner now hides after file scan completes
- Auto-update pipeline cycle guard prevents overlapping execution
- Real-time file count displayed during scanning
- Scan-in-progress message shown when searching before scan completion

## [1.0.0] - 2026-02-27

### Added
- 3-tier search: filename, full-text (BM25), semantic (BGE-M3)
- Automatic full-drive scanning on first launch
- Support for PDF, DOCX, XLSX, PPTX, HWP, HWPX, EML, MSG, and more
- Background auto-update every 10 minutes
- Korean and English UI
- 100% offline operation with zero telemetry
- Windows App Runtime bundled in installer
