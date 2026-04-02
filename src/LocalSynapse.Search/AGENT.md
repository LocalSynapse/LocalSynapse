# Agent 3: SearchEngine 규칙

## 역할
BM25 전문 검색, Dense 벡터 검색, Hybrid 결합, 문서 계열 그룹핑, 스니펫 추출.

## 의존성
- 프로젝트 참조: **LocalSynapse.Core만**
- NuGet: Porter2StemmerStandard

## 절대 규칙
1. Interfaces/ 와 SearchOptions.cs 를 절대 수정하지 마라
2. DB 직접 접근 금지 — Core의 Repository Interface 사용
3. 예외: Bm25SearchService는 FTS5 MATCH를 위해 SqliteConnectionFactory 직접 사용 가능

## 구현 대상 파일
- Services/Bm25SearchService.cs, DenseSearchService.cs, HybridSearchService.cs
- Services/RrfFusion.cs, DocumentFamilyService.cs, SnippetExtractor.cs
- Services/TextHighlighter.cs, NaturalQueryParser.cs, SearchClickService.cs
- Constants/QueryExpansionMap.cs, ExtensionBoost.cs

## 이식 출처 (v1.2.0)
| v2 파일 | v1 출처 |
|---------|---------|
| Bm25SearchService.cs | Services/Search/Bm25SearchService.cs |
| HybridSearchService.cs | Services/Search/HybridSearchService.cs |
| RrfFusion.cs | Services/Search/RrfFusion.cs |
| DenseSearchService.cs | Services/EmbeddingOptional/OptionalDenseSearchService.cs |
| DocumentFamilyService.cs | Services/DocumentFamilyService.cs |
| SnippetExtractor.cs | Helpers/SnippetExtractor.cs |
| NaturalQueryParser.cs | Services/Search/NaturalQueryParser.cs |
| QueryExpansionMap.cs | Core/Constants/QueryExpansionMap.cs |
| ExtensionBoost.cs | Core/Constants/ExtensionBoost.cs |

## 핵심 설정값 (변경 금지)
- RRF k = 60
- BM25 가중치: text=1.0, filename=5.0, folder_path=0.5
- 기본 SearchOptions: Bm25Weight=0.8, DenseWeight=0.2
- QuickSearch: LIKE '%query%', limit 20
- 파일명 부스트: 5.0x, 최근 감쇠: 0일=1.0, 365일=0.67
