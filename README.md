# LogKit 분석 서버 템플릿

LogKit 수신 서버(공통)에서 세션 로그를 주기적으로 복사해 미러에 쌓고, 그 사본으로 게임별 통계를 계산하는 분석 서버의 베이스다.
게임마다 이 저장소를 복제해 그 게임의 분석기를 만든다.

## 구성

- `Program.cs`: 진입점. 미러 동기화 서비스와 분석 API를 연결한다.
- `IngestMirror.cs`: 수신 서버 내보내기 API(`/api/export/*`)를 주기적으로 읽어 세션을 미러로 복사한다. 게임과 무관하다.
- `AnalyticsOptions.cs`: 설정(수신 서버 주소·키, 동기화 간격, 미러 경로).
- `SampleAnalytics.cs`: 예제 분석기. 세션 요약 행만으로 앱 버전별 통계를 계산한다.
- `core/`: 공용 코어(LogKit Core) 서브모듈. 세션 저장 형식, CSV 파서, 요약, 인증 미들웨어를 담는다.

## 저장소 받기

```bash
git clone --recurse-submodules <이 저장소 URL>
# 이미 받은 경우
git submodule update --init --recursive
```

## 새 게임 분석기 만들기

1. 이 템플릿을 복제해 새 저장소를 만든다(예: `logkit-analytics-<게임명>`).
2. `AnalyticsServer.csproj`의 `AssemblyName`을 게임에 맞게 바꾸고, 파일 이름도 맞춘다.
3. `SampleAnalytics.cs`를 참고해 게임 분석 클래스를 추가한다. 세션 요약만으로 충분하면 `SessionSummary`의 행을 쓰고, 세부 로그가 필요하면 `CsvSessionStore.ReadSessionEntries`로 통합 CSV를 읽는다.
4. 게임 규약 파일(이벤트 이름, 스테이지 목록 등)이 필요하면 `GameConfig/` 폴더에 JSON을 두고 csproj에 `CopyToOutputDirectory`를 건다.
5. `Program.cs`에 게임별 엔드포인트를 등록한다. 그 API는 `ApiKeyAuth`가 `/api` 아래에서 인증한다.
6. `appsettings.json`에서 `Urls`, `DataPath`, `IngestUrl`, `IngestApiKey`, `Project`를 맞춘다.

## 설정

| 키 | 기본값 | 설명 |
| --- | --- | --- |
| Urls | `http://0.0.0.0:5001` | 대기 주소 |
| ApiKey | `dev-log-key` | 분석 API가 요구하는 키 |
| IngestUrl | `http://localhost:5000` | 세션을 복사해 올 수신 서버 주소 |
| IngestApiKey | `dev-log-key` | 수신 서버의 열람 키(`ReadApiKey`)와 맞춘다. 읽기만 하므로 열람 키면 충분하다 |
| Project | (비움) | 이 분석 서버가 맡는 프로젝트. 수신 서버 `Projects`의 이름과 같아야 한다 |
| GmToolUrl | `http://localhost:5000/dashboard` | 페이지 머리글의 GM 툴 링크 주소 |
| SyncIntervalSeconds | 60 | 수신 서버 세션 목록을 다시 확인하는 간격 |
| IngestTimeoutSeconds | 120 | 수신 서버 요청 한 번의 제한 시간 |
| DataPath | `data` | 미러 저장소. 이 서버만 쓴다. 수신 서버의 `data`와 같은 폴더로 두지 않는다 |

## 동기화 방식

- 기동 직후 한 번, 이후 `SyncIntervalSeconds`마다 `GET /api/export/index`로 수신 서버의 세션 목록(ID·`updatedUtc`)을 읽는다.
- 미러에 없거나 변경 시각이 다른 세션만 통합 CSV·메타·지표를 내려받아 저장하고 월별 요약을 다시 계산한다.
- 수신 서버에서 사라진 세션은 미러에서도 지운다.
- 수신 서버가 꺼져 있으면 마지막 미러로 계속 응답한다. 상태는 `GET /api/mirror/status`에서 본다.

## 엔드포인트(템플릿 기본)

| 엔드포인트 | 인증 | 설명 |
| --- | --- | --- |
| `GET /health` | 불필요 | 생존 확인 |
| `GET /api/mirror/status` | 필요 | 미러 동기화 상태 |
| `GET /api/months` | 필요 | 미러의 월 목록과 월별 집계 |
| `GET /api/analytics/versions?month=` | 필요 | 예제 분석: 앱 버전별 세션·로그·오류·정상 종료 비율 |

## 실행

```bash
dotnet run     # http://0.0.0.0:5001
```

수신 서버(LogKit 저장소의 `server/`)가 떠 있어야 최신 세션을 받는다.
