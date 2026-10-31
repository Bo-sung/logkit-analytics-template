namespace LogServer;

/// <summary>
/// 분석 서버 설정. appsettings.json에서 바인딩한다.
/// DataPath는 수신 서버에서 복사해 온 미러 저장소이며, 원문(raw)은 가져오지 않으므로 KeepRawJsonl은 꺼 둔다.
/// 게임별 설정(규약 파일 경로 등)은 각 게임 저장소에서 옵션 클래스를 더해 쓴다.
/// </summary>
public sealed class AnalyticsOptions : StorageOptions
{
    /// <summary>분석 서버 자체의 X-Api-Key. 분석 페이지와 /api 호출이 이 값을 요구한다.</summary>
    public string ApiKey { get; set; } = "dev-log-key";

    /// <summary>복사해 올 수신 서버 주소(예: http://localhost:5000).</summary>
    public string IngestUrl { get; set; } = "http://localhost:5000";

    /// <summary>수신 서버의 열람 키(ReadApiKey). 세션 목록과 파일을 읽기만 하므로 열람 키면 충분하다.</summary>
    public string IngestApiKey { get; set; } = "dev-log-key";

    /// <summary>
    /// 이 분석 서버가 맡는 프로젝트 이름. 수신 서버에서 이 프로젝트 세션만 복사한다.
    /// 프로젝트 라벨은 수신 서버가 업로드 키로 정하므로, 이름은 수신 서버 설정의 Projects 키와 같아야 한다.
    /// </summary>
    public string Project { get; set; } = "poc_ts";

    /// <summary>
    /// 게임 분석 JSONL이 있는 폴더(Telemetry). 비우면 대시보드는 설정 안내만 보여 준다.
    /// 게임의 분석 파일은 LogKit으로 업로드되지 않으므로 이 폴더를 직접 읽는다(예: %USERPROFILE%\AppData\LocalLow\DefaultCompany\POC2\Telemetry).
    /// </summary>
    public string TelemetryPath { get; set; } = string.Empty;

    /// <summary>
    /// 페이지 머리글의 GM 툴 링크 주소. 브라우저가 열 수 있는 주소(사내망이면 172.21.10.67)로 맞춘다.
    /// </summary>
    public string GmToolUrl { get; set; } = "http://localhost:5000/dashboard";

    /// <summary>수신 서버에서 세션 목록을 다시 확인하는 간격(초).</summary>
    public double SyncIntervalSeconds { get; set; } = 60;

    /// <summary>수신 서버 한 번의 요청 제한 시간(초). 큰 세션 CSV 내려받기를 기준으로 넉넉히 둔다.</summary>
    public double IngestTimeoutSeconds { get; set; } = 120;

    public AnalyticsOptions()
    {
        DataPath = "data";
        KeepRawJsonl = false;
    }
}
