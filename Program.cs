using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogServer;

/// <summary>
/// 게임 분석 서버 진입점(템플릿). 미러 동기화 서비스와 분석 API를 연결한다.
/// 게임별 엔드포인트와 페이지는 이 파일에 추가한다.
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex _monthPattern = new(@"^\d{4}-\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// 분석 서버를 기동한다. 설정과 서비스를 등록하고 미러 동기화와 분석 API를 연결한 뒤 요청을 처리한다.
    /// args를 받아들이며, 미러 동기화는 백그라운드에서 주기적으로 돈다.
    /// </summary>
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        AnalyticsOptions options = builder.Configuration.Get<AnalyticsOptions>() ?? new AnalyticsOptions();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<StorageOptions>(options);
        builder.Services.AddSingleton<CsvSessionStore>();
        builder.Services.AddSingleton<SessionSummary>();
        builder.Services.AddSingleton<SampleAnalytics>();
        builder.Services.AddSingleton<IngestMirror>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestMirror>());

        WebApplication app = builder.Build();

        app.UseMiddleware<ApiKeyAuth>(options.ApiKey);
        app.MapGet("/health", () => Results.Text("ok", "text/plain"));
        app.MapGet("/api/mirror/status", (IngestMirror mirror) => Results.Json(mirror.Status, _jsonOptions));
        app.MapGet("/api/months", (SessionSummary summary) => Results.Json(new { months = summary.ListMonths() }, _jsonOptions));
        app.MapGet("/api/analytics/versions", HandleVersions);

        app.Services.GetRequiredService<CsvSessionStore>().EnsureDirectories();
        app.Run();
    }

    /// <summary>
    /// GET /api/analytics/versions 처리기. 앱 버전별 예제 집계를 돌려준다.
    /// month(yyyy-MM, 생략 시 전체 기간)를 사용하며 형식이 틀리면 400, 그 외에는 200과 집계 JSON을 반환한다.
    /// </summary>
    private static IResult HandleVersions(string? month, SampleAnalytics analytics)
    {
        if (!string.IsNullOrEmpty(month) && !_monthPattern.IsMatch(month))
        {
            return Results.BadRequest(new { error = "month must be yyyy-MM" });
        }

        return Results.Json(analytics.Versions(month), _jsonOptions);
    }
}
