namespace LogServer;

/// <summary>
/// poc_ts(KJG_6_B2_POC_TrenchSlot) 분석기. Telemetry 폴더의 판별 JSONL을 읽어 버전별 통계를 만든다.
/// 파일은 이름(sessionId)·크기·수정 시각이 같으면 다시 읽지 않는다. 진행 중인 판은 파일이 커질 때마다 다시 읽는다.
/// </summary>
public sealed class PocTsAnalytics
{
    private readonly AnalyticsOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, CachedFile> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>설정(TelemetryPath)을 받아 분석기를 만든다. 파일은 Build를 부를 때 읽는다.</summary>
    public PocTsAnalytics(AnalyticsOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// 폴더의 모든 판을 읽어 전체·버전별 통계와 판별 목록을 돌려준다.
    /// 폴더가 없으면 Configured=false로 돌려주고, 화면이 설정 안내를 보여 준다.
    /// </summary>
    public PocTsResponse Build()
    {
        string root = _options.TelemetryPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new PocTsResponse { Configured = false, TelemetryPath = root };
        }

        List<SessionDigest> sessions = Load(root);
        return Aggregate(sessions, root);
    }

    /// <summary>
    /// 판 하나의 위치 기록(게임 시간·X·Z)을 돌려준다. 이동 시뮬레이터가 재생에 쓴다.
    /// sessionId로 찾으며, 설정이 없거나 그 판이 없으면 null을 반환한다.
    /// </summary>
    public PathResponse? Path(string sessionId)
    {
        string root = _options.TelemetryPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return null;
        }

        SessionDigest? session = Load(root).FirstOrDefault(entry => entry.SessionId == sessionId);
        if (session is null)
        {
            return null;
        }

        return new PathResponse
        {
            SessionId = session.SessionId,
            BuildVersion = session.BuildVersion,
            MapId = session.MapId,
            StartUtc = session.StartUtc,
            PlaySeconds = Round(session.PlaySeconds),
            EndReason = session.EndReason,
            Points = session.Path.Select(point => new[] { point.T, point.X, point.Z }).ToList(),
        };
    }

    /// <summary>
    /// 선택한 버전·맵의 모든 판 위치 기록을 한 번에 돌려준다(이동 시뮬레이터가 여러 명을 같은 시간축으로 재생한다).
    /// version·map이 비어 있으면 전체로 본다. 좌표는 소수 둘째 자리까지 줄여 응답을 작게 한다.
    /// 버전과 맵 목록은 필터와 상관없이 전체 기준으로 돌려준다(선택 상자용).
    /// </summary>
    public PathsResponse Paths(string? version, string? map)
    {
        string root = _options.TelemetryPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new PathsResponse { Configured = false, TelemetryPath = root ?? string.Empty };
        }

        List<SessionDigest> all = Load(root);
        List<SessionDigest> chosen = all
            .Where(session => string.IsNullOrEmpty(version) || session.BuildVersion == version)
            .Where(session => string.IsNullOrEmpty(map) || session.MapId == map)
            .Where(session => session.Path.Count > 0)
            .OrderByDescending(session => session.StartUtc, StringComparer.Ordinal)
            .ToList();

        return new PathsResponse
        {
            Configured = true,
            TelemetryPath = root,
            Version = version ?? string.Empty,
            Map = map ?? string.Empty,
            Versions = all.Select(session => session.BuildVersion).Distinct(StringComparer.Ordinal)
                .OrderByDescending(name => name, Comparer<string>.Create(AppVersionOrder.Compare)).ToList(),
            Maps = all.Select(session => session.MapId).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal).ToList(),
            Sessions = chosen.Select(session => new PathSession
            {
                SessionId = session.SessionId,
                BuildVersion = session.BuildVersion,
                MapId = session.MapId,
                StartUtc = session.StartUtc,
                PlaySeconds = Round(session.PlaySeconds),
                EndReason = session.EndReason,
                Points = session.Path.Select(point => new[] { Round2(point.T), Round2(point.X), Round2(point.Z) }).ToList(),
            }).ToList(),
        };
    }

    private static float Round2(float value) => (float)Math.Round(value, 2);

    /// <summary>
    /// 이동 히트맵. version·map이 비어 있으면 그 조건은 전체로 본다.
    /// 판들의 플레이어 위치를 정사각형 격자(grid×grid)의 칸별 횟수로 바꿔 돌려준다. 행 0은 Z가 가장 작은 쪽이다.
    /// 맵마다 좌표계가 다르므로 화면은 맵을 골라서 보여 주는 것을 기본으로 한다.
    /// </summary>
    public HeatmapResponse Heatmap(string? version, string? map, int grid)
    {
        string root = _options.TelemetryPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new HeatmapResponse { Configured = false, TelemetryPath = root ?? string.Empty };
        }

        List<SessionDigest> all = Load(root);
        List<SessionDigest> chosen = all
            .Where(session => string.IsNullOrEmpty(version) || session.BuildVersion == version)
            .Where(session => string.IsNullOrEmpty(map) || session.MapId == map)
            .ToList();
        List<PathPoint> points = chosen.SelectMany(session => session.Path).ToList();

        HeatmapResponse response = new()
        {
            Configured = true,
            TelemetryPath = root,
            Version = version ?? string.Empty,
            Map = map ?? string.Empty,
            Grid = grid,
            Maps = all.Select(session => session.MapId).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal).ToList(),
            SessionCount = chosen.Count,
            PointCount = points.Count,
        };
        if (points.Count == 0)
        {
            return response;
        }

        float minX = points.Min(point => point.X);
        float maxX = points.Max(point => point.X);
        float minZ = points.Min(point => point.Z);
        float maxZ = points.Max(point => point.Z);
        float centerX = (minX + maxX) / 2;
        float centerZ = (minZ + maxZ) / 2;
        float half = Math.Max(maxX - minX, maxZ - minZ) / 2 * 1.04f + 0.5f;
        response.OriginX = centerX - half;
        response.OriginZ = centerZ - half;
        response.Side = half * 2;

        int[] counts = new int[grid * grid];
        int max = 0;
        foreach (PathPoint point in points)
        {
            int col = Math.Clamp((int)((point.X - response.OriginX) / response.Side * grid), 0, grid - 1);
            int row = Math.Clamp((int)((point.Z - response.OriginZ) / response.Side * grid), 0, grid - 1);
            int index = row * grid + col;
            counts[index]++;
            max = Math.Max(max, counts[index]);
        }

        response.Counts = counts;
        response.MaxCount = max;
        return response;
    }

    /// <summary>
    /// 폴더의 *.jsonl을 캐시와 맞춰 판 요약 목록으로 돌려준다. 사라진 파일의 캐시는 지운다.
    /// root는 텔레메트리 폴더 경로이며, 캐시 접근은 한 번에 한 요청만 하도록 잠근다.
    /// </summary>
    private List<SessionDigest> Load(string root)
    {
        lock (_gate)
        {
            List<SessionDigest> sessions = new();
            HashSet<string> present = new(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(root, "*.jsonl"))
            {
                FileInfo info = new(path);
                present.Add(path);
                if (!_files.TryGetValue(path, out CachedFile? cached) || cached.Length != info.Length || cached.Written != info.LastWriteTimeUtc)
                {
                    cached = new CachedFile(info.Length, info.LastWriteTimeUtc, TelemetryReader.Read(path));
                    _files[path] = cached;
                }

                sessions.Add(cached.Digest);
            }

            foreach (string gone in _files.Keys.Where(key => !present.Contains(key)).ToList())
            {
                _files.Remove(gone);
            }

            return sessions;
        }
    }

    /// <summary>
    /// 판 요약 목록을 버전별로 묶어 집계한다. 버전은 AppVersionOrder 순(최신 먼저)이다.
    /// sessions는 빈 목록일 수 있고, root는 응답에 그대로 싣는다.
    /// </summary>
    private static PocTsResponse Aggregate(List<SessionDigest> sessions, string root)
    {
        List<VersionStats> versions = sessions
            .GroupBy(session => session.BuildVersion, StringComparer.Ordinal)
            .Select(group => Summarize(group.Key, group.ToList()))
            .OrderByDescending(version => version.BuildVersion, Comparer<string>.Create(AppVersionOrder.Compare))
            .ToList();

        return new PocTsResponse
        {
            Configured = true,
            TelemetryPath = root,
            SessionCount = sessions.Count,
            Total = sessions.Count == 0 ? null : Summarize("전체", sessions),
            Versions = versions,
            Sessions = sessions
                .OrderByDescending(session => session.StartUtc, StringComparer.Ordinal)
                .Select(session => new PocSessionRow
                {
                    SessionId = session.SessionId,
                    BuildVersion = session.BuildVersion,
                    StartUtc = session.StartUtc,
                    MapId = session.MapId,
                    PlaySeconds = Round(session.PlaySeconds),
                    EndReason = session.EndReason,
                    FinalReachedWave = session.FinalReachedWave,
                    LastCompletedWave = session.LastCompletedWave,
                    TotalKills = session.TotalKills,
                    Spins = session.Spins.Count,
                    Purchases = session.Purchases.Count(purchase => purchase.Success),
                    Shots = session.Shots.Count,
                })
                .ToList(),
        };
    }

    /// <summary>한 묶음(버전 또는 전체)의 집계. sessions는 한 개 이상이다.</summary>
    private static VersionStats Summarize(string name, List<SessionDigest> sessions)
    {
        List<WaveDigest> waves = sessions.SelectMany(session => session.Waves).ToList();
        List<SpinDigest> spins = sessions.SelectMany(session => session.Spins).ToList();
        List<PurchaseDigest> purchases = sessions.SelectMany(session => session.Purchases).ToList();
        List<ShotDigest> shots = sessions.SelectMany(session => session.Shots).ToList();
        List<DamageDigest> damage = sessions.SelectMany(session => session.DamageTaken).ToList();
        int count = sessions.Count;

        return new VersionStats
        {
            BuildVersion = name,
            Sessions = count,
            AvgPlaySeconds = Round(sessions.Average(session => session.PlaySeconds)),
            TotalKills = sessions.Sum(session => session.TotalKills),
            AvgKillsPerSession = Round(sessions.Average(session => session.TotalKills)),
            DeathRate = Ratio(sessions.Count(session => session.EndReason == "death"), count),
            AvgReachedWave = Average(sessions.Where(session => session.FinalReachedWave is not null).Select(session => (double)session.FinalReachedWave!.Value)),
            EndReasons = sessions.GroupBy(session => session.EndReason, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            Waves = WaveRows(waves),
            Slot = SlotRows(spins, count),
            Shop = ShopRows(purchases, count),
            Shots = shots.Count,
            ShotHitRate = Ratio(shots.Count(shot => shot.Hit), shots.Count),
            Weapons = WeaponRows(shots),
            DamageTaken = DamageRows(damage),
            TotalDamageTaken = Round(damage.Sum(entry => entry.Damage)),
        };
    }

    /// <summary>웨이브 번호별 진입·완료·사망 이탈. 번호 순서로 돌려준다.</summary>
    private static List<WaveRow> WaveRows(List<WaveDigest> waves) =>
        waves
            .GroupBy(wave => wave.Wave)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                List<WaveDigest> list = group.ToList();
                int completed = list.Count(wave => wave.Status == "completed");
                return new WaveRow
                {
                    Wave = group.Key,
                    Entered = list.Count,
                    Completed = completed,
                    DiedHere = list.Count(wave => wave.Reason == "death"),
                    CompletionRate = Ratio(completed, list.Count),
                    AvgKills = Average(list.Select(wave => (double)wave.Kills)),
                    AvgClearSeconds = Average(list.Where(wave => wave.ClearSeconds is not null).Select(wave => wave.ClearSeconds!.Value)),
                };
            })
            .ToList();

    /// <summary>슬롯 회전 결과. hitRate는 미스가 아닌 회전 비율, nearMissRate는 전체 회전 중 니어미스 비율이다.</summary>
    private static SlotStats SlotRows(List<SpinDigest> spins, int sessionCount)
    {
        int total = spins.Count;
        return new SlotStats
        {
            Spins = total,
            SpinsPerSession = sessionCount == 0 ? null : Round(total / (double)sessionCount),
            HitRate = Ratio(spins.Count(spin => !spin.Miss), total),
            NearMissRate = Ratio(spins.Count(spin => spin.NearMiss), total),
            AvgMoneyDelta = Average(spins.Select(spin => (double)spin.MoneyDelta)),
            TotalMoneyDelta = spins.Sum(spin => spin.MoneyDelta),
        };
    }

    /// <summary>상점 구매 결과. 품목 종류별 건수·성공률·지출과 실패 사유별 건수를 돌려준다.</summary>
    private static ShopStats ShopRows(List<PurchaseDigest> purchases, int sessionCount)
    {
        int successes = purchases.Count(purchase => purchase.Success);
        return new ShopStats
        {
            Attempts = purchases.Count,
            AttemptsPerSession = sessionCount == 0 ? null : Round(purchases.Count / (double)sessionCount),
            SuccessRate = Ratio(successes, purchases.Count),
            TotalSpent = purchases.Where(purchase => purchase.Success).Sum(purchase => purchase.Spent),
            ByItemType = purchases
                .GroupBy(purchase => purchase.ItemType, StringComparer.Ordinal)
                .Select(group => new PurchaseTypeRow
                {
                    ItemType = group.Key,
                    Attempts = group.Count(),
                    Successes = group.Count(purchase => purchase.Success),
                    Spent = group.Where(purchase => purchase.Success).Sum(purchase => purchase.Spent),
                })
                .OrderByDescending(row => row.Attempts)
                .ToList(),
            FailureCodes = purchases
                .Where(purchase => !purchase.Success && purchase.FailureCode.Length > 0)
                .GroupBy(purchase => purchase.FailureCode, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
        };
    }

    /// <summary>무기 종류별 발사·명중·처치·헤드샷·피해. 발사 수가 많은 순서다.</summary>
    private static List<WeaponRow> WeaponRows(List<ShotDigest> shots) =>
        shots
            .GroupBy(shot => shot.WeaponType, StringComparer.Ordinal)
            .Select(group =>
            {
                int hits = group.Count(shot => shot.Hit);
                return new WeaponRow
                {
                    Weapon = group.Key,
                    Shots = group.Count(),
                    Hits = hits,
                    HitRate = Ratio(hits, group.Count()),
                    Kills = group.Sum(shot => shot.Kills),
                    Headshots = group.Sum(shot => shot.Headshots),
                    Damage = Round(group.Sum(shot => shot.Damage)),
                };
            })
            .OrderByDescending(row => row.Shots)
            .ToList();

    /// <summary>받은 피해를 적 종류별로 묶는다. 피해량이 큰 순서다.</summary>
    private static List<DamageRow> DamageRows(List<DamageDigest> damage) =>
        damage
            .GroupBy(entry => entry.EnemyType, StringComparer.Ordinal)
            .Select(group => new DamageRow
            {
                EnemyType = group.Key,
                Hits = group.Count(),
                Damage = Round(group.Sum(entry => entry.Damage)),
            })
            .OrderByDescending(row => row.Damage)
            .ToList();

    /// <summary>분자/분모 비율. 분모가 0이면 null(기록 없음)이다.</summary>
    private static double? Ratio(int part, int whole) => whole == 0 ? null : Round(part / (double)whole, 4);

    /// <summary>평균. 값이 하나도 없으면 null이다.</summary>
    private static double? Average(IEnumerable<double> values)
    {
        List<double> list = values.ToList();
        return list.Count == 0 ? null : Round(list.Average());
    }

    private static double Round(double value, int digits = 2) => Math.Round(value, digits);

    /// <summary>캐시에 보관하는 파일 한 개의 요약과 그 파일의 크기·수정 시각.</summary>
    private sealed record CachedFile(long Length, DateTime Written, SessionDigest Digest);
}

/// <summary>분석 응답. Configured=false면 TelemetryPath 설정이 없거나 폴더가 없다.</summary>
public sealed class PocTsResponse
{
    public bool Configured { get; set; }
    public string TelemetryPath { get; set; } = string.Empty;
    public int SessionCount { get; set; }
    public VersionStats? Total { get; set; }
    public List<VersionStats> Versions { get; set; } = new();
    public List<PocSessionRow> Sessions { get; set; } = new();
}

/// <summary>버전(또는 전체) 한 묶음의 집계.</summary>
public sealed class VersionStats
{
    public string BuildVersion { get; set; } = string.Empty;
    public int Sessions { get; set; }
    public double AvgPlaySeconds { get; set; }
    public int TotalKills { get; set; }
    public double AvgKillsPerSession { get; set; }
    public double? DeathRate { get; set; }
    public double? AvgReachedWave { get; set; }
    public Dictionary<string, int> EndReasons { get; set; } = new();
    public List<WaveRow> Waves { get; set; } = new();
    public SlotStats Slot { get; set; } = new();
    public ShopStats Shop { get; set; } = new();
    public int Shots { get; set; }
    public double? ShotHitRate { get; set; }
    public List<WeaponRow> Weapons { get; set; } = new();
    public List<DamageRow> DamageTaken { get; set; } = new();
    public double TotalDamageTaken { get; set; }
}

/// <summary>웨이브 번호 한 줄. 진입 = 웨이브 마감 기록 수, 완료 = status completed.</summary>
public sealed class WaveRow
{
    public int Wave { get; set; }
    public int Entered { get; set; }
    public int Completed { get; set; }
    public int DiedHere { get; set; }
    public double? CompletionRate { get; set; }
    public double? AvgKills { get; set; }
    public double? AvgClearSeconds { get; set; }
}

/// <summary>슬롯 회전 집계.</summary>
public sealed class SlotStats
{
    public int Spins { get; set; }
    public double? SpinsPerSession { get; set; }
    public double? HitRate { get; set; }
    public double? NearMissRate { get; set; }
    public double? AvgMoneyDelta { get; set; }
    public long TotalMoneyDelta { get; set; }
}

/// <summary>상점 구매 집계.</summary>
public sealed class ShopStats
{
    public int Attempts { get; set; }
    public double? AttemptsPerSession { get; set; }
    public double? SuccessRate { get; set; }
    public long TotalSpent { get; set; }
    public List<PurchaseTypeRow> ByItemType { get; set; } = new();
    public Dictionary<string, int> FailureCodes { get; set; } = new();
}

/// <summary>구매 품목 종류 한 줄.</summary>
public sealed class PurchaseTypeRow
{
    public string ItemType { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int Successes { get; set; }
    public long Spent { get; set; }
}

/// <summary>무기 종류 한 줄.</summary>
public sealed class WeaponRow
{
    public string Weapon { get; set; } = string.Empty;
    public int Shots { get; set; }
    public int Hits { get; set; }
    public double? HitRate { get; set; }
    public int Kills { get; set; }
    public int Headshots { get; set; }
    public double Damage { get; set; }
}

/// <summary>적 종류별 받은 피해 한 줄.</summary>
public sealed class DamageRow
{
    public string EnemyType { get; set; } = string.Empty;
    public int Hits { get; set; }
    public double Damage { get; set; }
}

/// <summary>판 한 줄(판 목록 표).</summary>
public sealed class PocSessionRow
{
    public string SessionId { get; set; } = string.Empty;
    public string BuildVersion { get; set; } = string.Empty;
    public string StartUtc { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public double PlaySeconds { get; set; }
    public string EndReason { get; set; } = string.Empty;
    public int? FinalReachedWave { get; set; }
    public int? LastCompletedWave { get; set; }
    public int TotalKills { get; set; }
    public int Spins { get; set; }
    public int Purchases { get; set; }
    public int Shots { get; set; }
}

/// <summary>이동 히트맵 응답. Counts는 행×Grid+열 순서이며 행 0(Z 최소)부터 채운다.</summary>
public sealed class HeatmapResponse
{
    public bool Configured { get; set; }
    public string TelemetryPath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public List<string> Maps { get; set; } = new();
    public int Grid { get; set; }
    public int SessionCount { get; set; }
    public int PointCount { get; set; }
    public float OriginX { get; set; }
    public float OriginZ { get; set; }
    public float Side { get; set; }
    public int MaxCount { get; set; }
    public int[] Counts { get; set; } = Array.Empty<int>();
}

/// <summary>판 하나의 위치 기록. Points는 [게임 시간 T, X, Z] 순서의 배열 목록이다.</summary>
public sealed class PathResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string BuildVersion { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public string StartUtc { get; set; } = string.Empty;
    public double PlaySeconds { get; set; }
    public string EndReason { get; set; } = string.Empty;
    public List<float[]> Points { get; set; } = new();
}

/// <summary>여러 판의 위치 기록 응답. 선택 상자용 전체 버전·맵 목록과 필터된 판 목록을 함께 준다.</summary>
public sealed class PathsResponse
{
    public bool Configured { get; set; }
    public string TelemetryPath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public List<string> Versions { get; set; } = new();
    public List<string> Maps { get; set; } = new();
    public List<PathSession> Sessions { get; set; } = new();
}

/// <summary>판 한 개의 위치 기록. Points는 [게임 시간 T, X, Z] 목록이다.</summary>
public sealed class PathSession
{
    public string SessionId { get; set; } = string.Empty;
    public string BuildVersion { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public string StartUtc { get; set; } = string.Empty;
    public double PlaySeconds { get; set; }
    public string EndReason { get; set; } = string.Empty;
    public List<float[]> Points { get; set; } = new();
}
