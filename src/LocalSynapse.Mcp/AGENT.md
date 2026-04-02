# Agent 5: MCP/API 규칙

## 역할
MCP stdio 서버. AI agent에게 파일 검색 도구 제공.

## 의존성
- 프로젝트 참조: **LocalSynapse.Core, LocalSynapse.Search**

## 절대 규칙
1. UI 관련 코드 금지 (Avalonia 참조 금지)
2. stdin/stdout만 사용
3. MCP 프로토콜 스펙(2024-11-05) 준수

## 구현 대상 파일
- Server/McpServer.cs, McpToolRouter.cs, McpProtocol.cs
- Tools/SearchFilesTool.cs, GetFileContentTool.cs
- Tools/GetPipelineStatusTool.cs, ListIndexedFilesTool.cs

## MCP 도구
| Tool | Input | Output |
|------|-------|--------|
| search_files | {query, topK?, extensions?} | SearchResponse |
| get_file_content | {fileId} | {content, path} |
| get_pipeline_status | {} | PipelineStamps |
| list_indexed_files | {folder?, extension?, limit?} | FileMetadata[] |
