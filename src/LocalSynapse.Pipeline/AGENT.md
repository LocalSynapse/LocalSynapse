# Agent 2: Pipeline 규칙

## 역할
파일 스캔 → 콘텐츠 추출 → 텍스트 청킹 → 임베딩 생성. 10분 주기 자동 실행.

## 의존성
- 프로젝트 참조: **LocalSynapse.Core만**
- NuGet: OnnxRuntime, OpenXml, PdfPig, OpenMcdf

## 절대 규칙
1. Interfaces/ 폴더의 파일을 절대 수정하지 마라
2. Core의 Interface로만 DB에 접근 (SqliteConnection 직접 사용 금지)
3. 모든 I/O 작업에 CancellationToken 전파 필수
4. 배치 처리 시 500개 단위로 트랜잭션 커밋

## 구현 대상 파일
- Parsing/ContentExtractor.cs (파서 라우터)
- Parsing/PlainTextParser.cs, PdfParser.cs, DocxParser.cs, XlsxParser.cs
- Parsing/PptxParser.cs, HwpParser.cs, HwpxParser.cs, HtmlParser.cs
- Chunking/TextChunker.cs
- Scanning/FileScanner.cs, ScanFilterHelper.cs
- Scanning/UsnJournalService.cs, UsnNativeMethods.cs, UsnPathBuilder.cs
- Embedding/EmbeddingService.cs, OnnxModelLoader.cs, BertTokenizer.cs
- Embedding/BgeM3Installer.cs
- Orchestration/PipelineOrchestrator.cs

## 이식 출처 (v1.2.0)
| v2 파일 | v1 출처 |
|---------|---------|
| ContentExtractor.cs | Services/Indexing/ContentExtractor.cs |
| HwpParser.cs | Services/Indexing/HwpExtractor.cs |
| FileScanner.cs | Services/Onboarding/FileScanService.cs |
| ScanFilterHelper.cs | Services/Scanning/ScanFilterHelper.cs |
| UsnJournalService.cs | Services/Scanning/UsnJournalService.cs |
| EmbeddingService.cs | Services/EmbeddingOptional/EmbeddingService.cs |
| OnnxModelLoader.cs | Services/EmbeddingOptional/OnnxModelLoader.cs |
| BertTokenizer.cs | Services/EmbeddingOptional/BertTokenizer.cs |
| BgeM3Installer.cs | Services/EmbeddingOptional/BgeM3InstallerService.cs |
| PipelineOrchestrator.cs | Services/AutoPipelineOrchestrator.cs |

## 핵심 설정값 (변경 금지)
- 청크 크기: 1000자, 파일당 최대 청크: 500개, 배치: 500개
- 자동 실행 주기: 10분
- 모델 경로: %LOCALAPPDATA%/LocalSynapse/models/{model_id}/
