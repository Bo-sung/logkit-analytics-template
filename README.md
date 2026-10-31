# poc_ts 분석 서버 (KJG_6_B2_POC_TrenchSlot)

TrenchSlot POC의 게임 분석 대시보드다. 게임이 남긴 분석 JSONL(`Telemetry/<sessionId>.jsonl`)을 읽어 버전·웨이브·슬롯·상점·무기·피해 통계를 계산하고, `/dashboard`에서 보여 준다.
LogKit 분석 서버 템플릿(GGGG 분석 서버 구조)을 바탕으로 만들었다.

## 데이터 출처

- **분석 JSONL(주 데이터)**: 게임의 `Application.persistentDataPath/Telemetry/`. 이 PC에서는 `C:\Users\JUNGLE\AppData\LocalLow\DefaultCompany\POC2\Telemetry`다. 서버가 이 폴더를 직접 읽는다.
- **LogKit 수신 서버(`IngestUrl`)**: 같은 프로젝트의 콘솔·오류 로그 세션을 미러로 복사한다(`Project = poc_ts`). 대시보드 통계에는 쓰지 않는다.

> 게임의 분석 파일은 LogKit 업로드 대상이 아니다(`Docs/LogKit-로그-적용.md`). 그래서 수신 서버에는 분석 이벤트가 올라오지 않는다. 서버에서 분석 데이터를 받으려면 게임이 Telemetry 파일을 업로드하도록 바꾸거나, 이 서버가 파일 폴더를 읽는 지금 방식을 쓴다.

## 구성

- `Program.cs`: 진입점. 미러 동기화, 분석 API, 대시보드를 연결한다.
- `TelemetryReader.cs`: 판 JSONL 한 개를 읽어 `SessionDigest`로 줄인다. 웨이브는 summaryId별 최고 revision, 회전·구매·발사는 id별 마지막 값만 센다.
- `PocTsAnalytics.cs`: 파일 캐시(크기·수정 시각 기준)와 버전별·전체 집계, 응답 모델.
- `DashboardPage.cs`: 대시보드 HTML(한 파일, 데이터는 API에서 받는다).
- `IngestMirror.cs`: 수신 서버의 `poc_ts` 세션을 미러에 맞춘다(`/api/mirror/status`).
- `AnalyticsOptions.cs`: 설정. `core/`는 LogKit 공용 코어 서브모듈이다.

## 설정 (`appsettings.json`)

| 키 | 기본값 | 설명 |
| --- | --- | --- |
| Urls | `http://0.0.0.0:5002` | 대기 주소. 5001은 GGGG 분석 서버가 쓰므로 피한다 |
| ApiKey | `dev-log-key` | 대시보드와 `/api` 호출이 요구하는 키 |
| TelemetryPath | (비움) | 분석 JSONL 폴더. 비우면 대시보드가 설정 안내만 보여 준다 |
| IngestUrl | `http://localhost:5000` | 수신 서버 주소 |
| IngestApiKey | `dev-log-key` | 수신 서버의 **열람 키**. 업로드 키나 관리 키를 넣지 않는다 |
| Project | `poc_ts` | 수신 서버 `Projects`의 이름과 같아야 한다 |
| SyncIntervalSeconds | 60 | 수신 서버 세션 목록 확인 간격 |
| DataPath | `data` | 미러 저장소 |

키 값은 저장소에 넣지 않는다. 운영 키는 환경 변수(`IngestApiKey`, `ApiKey`)로 덮어쓴다.

## 엔드포인트

| 엔드포인트 | 인증 | 설명 |
| --- | --- | --- |
| `GET /dashboard` | 불필요 | 대시보드 페이지. API 키는 화면에서 입력받는다 |
| `GET /api/analytics/poc-ts` | 필요 | 전체·버전별 집계, 판 목록 |
| `GET /api/mirror/status` | 필요 | 수신 서버 미러 동기화 상태 |
| `GET /health` | 불필요 | 생존 확인 |

## 집계 기준

- **판**: `session_end.reason`별 종료 수, 사망 종료 비율, 도달 웨이브 평균, 판당 처치.
- **웨이브**: 웨이브 번호별 진입(마감 기록 수), 완료율(`status=completed`), 사망 이탈, 평균 처치·클리어 시간.
- **슬롯**: 회전 수, 판당 회전, 적중률(미스 아님), 니어미스 비율, 회전당 금전 변화.
- **상점**: 구매 시도·성공률, 품목 종류별 건수와 지출(`actualSpent`), 실패 사유.
- **무기**: 발사 수(`player_shot`), 명중률(명중한 발사 / 전체 발사), 처치, 헤드샷, 피해.
- **피해**: 적 종류별 피격 수와 실제 피해량(`player_damage`).

## 실행

```bash
dotnet run        # http://0.0.0.0:5002/dashboard
```

요구사항: .NET 8 SDK. 수신 서버가 꺼져 있어도 분석은 로컬 폴더로 동작한다(미러는 마지막 상태로 응답).

## 새 판 반영

분석 폴더의 파일은 크기·수정 시각이 바뀔 때마다 다시 읽는다. 게임이 기록 중인 판도 새로고침하면 반영된다.
