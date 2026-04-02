# Agent 1: DataCore 규칙

## 역할
DB 스키마, 모델 정의, Repository 구현, 마이그레이션.

## 의존성
- 외부 프로젝트 참조: **없음**
- NuGet: Microsoft.Data.Sqlite

## 절대 규칙
1. Models/ 와 Interfaces/ 폴더의 파일을 절대 수정하지 마라
2. async 메서드가 아닌 Repository 메서드는 동기로 유지
3. 모든 SQL은 파라미터 바인딩 사용
4. UNIQUE 제약조건과 ON CONFLICT 절 반드시 동기화

## 구현 대상 파일
- Database/SqliteConnectionFactory.cs
- Database/MigrationService.cs (v1의 32개 마이그레이션 → 단일 CREATE TABLE 세트)
- Repositories/FileRepository.cs (IFileRepository 구현)
- Repositories/ChunkRepository.cs (IChunkRepository 구현)
- Repositories/EmbeddingRepository.cs (IEmbeddingRepository 구현)
- Repositories/PipelineStampRepository.cs (IPipelineStampRepository 구현)
- Repositories/SettingsStore.cs (ISettingsStore 구현)
- Constants/FileExtensions.cs (콘텐츠 인덱싱 가능 확장자 23종)

## 이식 출처 (v1.2.0)
| v2 파일 | v1 출처 |
|---------|---------|
| FileRepository.cs | Core/Repositories/FileRepository.cs |
| ChunkRepository.cs | Core/Repositories/ChunkRepository.cs |
| EmbeddingRepository.cs | Core/Repositories/OptionalEmbeddingRepository.cs |
| PipelineStampRepository.cs | Core/Repositories/PipelineStampRepository.cs |
| MigrationService.cs | Core/Database/MigrationService.cs (최종 스키마만) |
| FileExtensions.cs | Core/Constants/FileExtensions.cs |

## 핵심 설정값 (변경 금지)
- FTS5 tokenizer: `unicode61 separators '_-().[]'`
- FTS5 bm25 가중치: text=1.0, filename=5.0, folder_path=0.5
- embeddings UNIQUE: `(file_id, chunk_id, model_id)`
