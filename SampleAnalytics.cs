namespace LogServer;

/// <summary>
/// 분석 템플릿의 예제 분석기. 세션 요약 행만으로 앱 버전별 통계를 계산한다.
/// 게임 통계를 만들 때는 이 클래스를 참고해 게임별 분석기를 추가하고, 세션 CSV(통합 로그)가 필요하면 CsvSessionStore로 읽는다.
/// </summary>
public sealed class SampleAnalytics
{
    private readonly SessionSummary _summary;

    /// <summary>요약 집계기(미러 저장소)를 받아 분석기를 만든다.</summary>
    public SampleAnalytics(SessionSummary summary)
    {
        _summary = summary;
    }

    /// <summary>
    /// 앱 버전별 세션 수·로그 수·오류 수·정상 종료 비율·평균 지속 시간을 계산한다.
    /// month(yyyy-MM)가 없으면 미러 전체 기간을 쓰고, 버전은 AppVersionOrder 순(최신 먼저)으로 돌려준다.
    /// </summary>
    public SampleVersionsResponse Versions(string? month)
    {
        List<SessionRow> rows = string.IsNullOrEmpty(month)
            ? _summary.ListMonths().SelectMany(info => _summary.GetMonthSessions(info.Month)).ToList()
            : _summary.GetMonthSessions(month);

        List<SampleVersion> versions = rows
            .GroupBy(row => row.AppVersion, StringComparer.Ordinal)
            .Select(group => new SampleVersion
            {
                AppVersion = group.Key,
                Sessions = group.Count(),
                Logs = group.Sum(row => row.Total),
                Errors = group.Sum(row => row.Errors + row.Exceptions),
                CleanEndRate = group.Count(row => row.EndedCleanly) / (double)group.Count(),
                AverageDurationSec = Math.Round(group.Average(row => row.DurationSec), 1),
            })
            .OrderByDescending(version => version.AppVersion, Comparer<string>.Create(AppVersionOrder.Compare))
            .ToList();

        return new SampleVersionsResponse { Month = month ?? string.Empty, Versions = versions };
    }
}

/// <summary>예제 분석 응답. 월(없으면 전체)과 버전별 집계.</summary>
public sealed class SampleVersionsResponse
{
    public string Month { get; set; } = string.Empty;
    public List<SampleVersion> Versions { get; set; } = new();
}

/// <summary>버전 한 줄의 집계.</summary>
public sealed class SampleVersion
{
    public string AppVersion { get; set; } = string.Empty;
    public int Sessions { get; set; }
    public long Logs { get; set; }
    public int Errors { get; set; }
    public double CleanEndRate { get; set; }
    public double AverageDurationSec { get; set; }
}
