using System.Text.Json;

namespace LogServer;

/// <summary>
/// poc_ts 분석 서버 진입점. 수신 서버의 poc_ts 세션을 미러에 맞추고, 게임 분석 JSONL(Telemetry 폴더)을 읽어
/// 버전·웨이브·슬롯·상점·무기·피해 통계와 대시보드(/dashboard)를 제공한다.
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 분석 서버를 기동한다. 설정과 서비스를 등록하고 미러 동기화와 분석 API·대시보드를 연결한 뒤 요청을 처리한다.
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
        builder.Services.AddSingleton<PocTsAnalytics>();
        builder.Services.AddSingleton<IngestMirror>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestMirror>());

        WebApplication app = builder.Build();

        app.UseMiddleware<ApiKeyAuth>(new ApiKeys(options.ApiKey));
        app.MapGet("/", () => Results.Redirect("/analytics"));
        app.MapGet("/health", () => Results.Text("ok", "text/plain"));
        app.MapGet("/analytics", () => Results.Content(DashboardPage.Html.Replace("__GM_TOOL_URL__", options.GmToolUrl, StringComparison.Ordinal), "text/html; charset=utf-8"));
        app.MapGet("/dashboard", () => Results.Redirect("/analytics"));
        app.MapGet("/api/mirror/status", (IngestMirror mirror) => Results.Json(mirror.Status, _jsonOptions));
        app.MapGet("/api/analytics/poc-ts", (PocTsAnalytics analytics) => Results.Json(analytics.Build(), _jsonOptions));

        app.Services.GetRequiredService<CsvSessionStore>().EnsureDirectories();
        app.Run();
    }
}
