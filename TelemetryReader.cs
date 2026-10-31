using System.Text.Json;

namespace LogServer;

/// <summary>
/// 한 판의 분석 이벤트 줄(로그 수신 서버에 올라온 Telemetry 항목의 dataJson)에서 집계에 필요한 값만 남긴 요약.
/// 원문 줄은 들고 있지 않으며, 파일이 바뀌지 않는 한 한 번만 파싱한다.
/// </summary>
public sealed class SessionDigest
{
    public string SessionId { get; set; } = string.Empty;
    public string BuildVersion { get; set; } = "unknown";
    public string StartUtc { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public double PlaySeconds { get; set; }
    public string EndReason { get; set; } = "incomplete";
    public int? FinalReachedWave { get; set; }
    public int? LastCompletedWave { get; set; }
    public int TotalKills { get; set; }
    public List<WaveDigest> Waves { get; set; } = new();
    public List<SpinDigest> Spins { get; set; } = new();
    public List<PurchaseDigest> Purchases { get; set; } = new();
    public List<ShotDigest> Shots { get; set; } = new();
    public List<DamageDigest> DamageTaken { get; set; } = new();

    /// <summary>플레이어 위치 표본(0.5초 간격). 이동 히트맵에 쓴다.</summary>
    public List<PathPoint> Path { get; set; } = new();
}

/// <summary>플레이어 위치 한 점(게임 시간 T초, 월드 X·Z).</summary>
public readonly record struct PathPoint(float T, float X, float Z);

/// <summary>웨이브 한 번의 마감 요약(summaryId당 최고 revision 하나).</summary>
public sealed record WaveDigest(int Wave, string Status, string Reason, int Kills, double? ClearSeconds);

/// <summary>슬롯 회전 한 번의 결과(spinId당 하나).</summary>
public sealed record SpinDigest(bool Miss, bool NearMiss, long MoneyDelta);

/// <summary>상점 구매 시도 한 번의 결과(attemptId당 하나).</summary>
public sealed record PurchaseDigest(string ItemType, bool Success, string FailureCode, long Spent);

/// <summary>발사 한 번과 그 탄들의 결과(shotId당 하나).</summary>
public sealed record ShotDigest(string WeaponType, bool Hit, int Kills, int Headshots, double Damage);

/// <summary>플레이어가 받은 피해 한 번.</summary>
public sealed record DamageDigest(string EnemyType, double Damage);

/// <summary>
/// 분석 JSONL을 줄 단위로 읽어 SessionDigest를 만든다.
/// 게임이 기록 중인 파일도 읽을 수 있도록 공유 읽기로 열고, 마지막 줄이 덜 쓰였으면 그 줄만 건너뛴다.
/// 같은 키가 여러 번 나오면 게임의 정의대로 한 번만 센다(웨이브는 최고 revision, 회전·구매·발사는 id별 마지막 값).
/// </summary>
public static class TelemetryReader
{
    /// <summary>위치 표본 간격(게임 초). 플레이어 위치는 초당 50회라 그대로 모으면 너무 크다.</summary>
    private const double PATH_SAMPLE_SECONDS = 0.5;

    /// <summary>
    /// 한 판의 이벤트 줄 목록을 읽어 요약을 돌려준다. 줄은 게임이 남긴 분석 이벤트 JSON 한 개씩이다.
    /// lines는 로그 수신 서버 세션의 Telemetry 항목 dataJson 목록이며, 해석할 수 없는 줄은 건너뛴다.
    /// </summary>
    public static SessionDigest ReadLines(IEnumerable<string> lines)
    {
        SessionDigest digest = new();
        Dictionary<string, (int Revision, WaveDigest Wave)> waves = new(StringComparer.Ordinal);
        Dictionary<string, SpinDigest> spins = new(StringComparer.Ordinal);
        Dictionary<string, PurchaseDigest> purchases = new(StringComparer.Ordinal);
        Dictionary<string, ShotAccumulator> shots = new(StringComparer.Ordinal);
        bool ended = false;
        double lastElapsed = 0;
        double lastPathSample = double.NegativeInfinity;

        foreach (string line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            JsonElement root;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            string eventType = Str(root, "eventType");
            JsonElement payload = Obj(root, "payload");
            lastElapsed = Num(root, "elapsedSeconds") ?? lastElapsed;
            if (digest.SessionId.Length == 0)
            {
                digest.SessionId = Str(root, "sessionId");
            }

            switch (eventType)
            {
                case "session_start":
                {
                    digest.BuildVersion = NonEmpty(Str(payload, "buildVersion"), "unknown");
                    digest.StartUtc = Str(payload, "utc");
                    digest.MapId = Str(payload, "mapId");
                    break;
                }
                case "session_end":
                {
                    ended = true;
                    digest.EndReason = NonEmpty(Str(payload, "reason"), "unknown");
                    digest.PlaySeconds = Num(payload, "playSeconds") ?? lastElapsed;
                    digest.FinalReachedWave = Int(payload, "finalReachedWave");
                    digest.LastCompletedWave = Int(payload, "lastCompletedWave");
                    digest.TotalKills = Int(payload, "totalKills") ?? 0;
                    break;
                }
                case "wave_summary":
                case "wave_summary_correction":
                {
                    string summaryId = Str(payload, "summaryId");
                    int revision = Int(payload, "revision") ?? 0;
                    if (summaryId.Length > 0 && (!waves.TryGetValue(summaryId, out var previous) || revision >= previous.Revision))
                    {
                        int wave = Int(Obj(payload, "plan"), "waveNumber") ?? 0;
                        WaveDigest digestWave = new(wave, Str(payload, "status"), Str(payload, "reason"), Int(payload, "kills") ?? 0, Num(payload, "clearDurationSeconds"));
                        waves[summaryId] = (revision, digestWave);
                    }

                    break;
                }
                case "slot_spin_resolved":
                {
                    string spinId = KeyOr(Str(payload, "spinId"));
                    JsonElement outcome = Obj(payload, "outcome");
                    spins[spinId] = new SpinDigest(Flag(outcome, "isMiss"), Flag(outcome, "isNearMiss"), Int64(payload, "actualMoneyDelta") ?? 0);
                    break;
                }
                case "shop_purchase_result":
                {
                    string attemptId = KeyOr(Str(payload, "attemptId"));
                    string itemType = NonEmpty(Str(Obj(payload, "product"), "itemType"), "unknown");
                    purchases[attemptId] = new PurchaseDigest(itemType, Flag(payload, "success"), Str(payload, "failureCode"), Int64(payload, "actualSpent") ?? 0);
                    break;
                }
                case "player_shot":
                {
                    string shotId = Str(payload, "shotId");
                    if (shotId.Length > 0)
                    {
                        ShotAccumulator shot = Accumulator(shots, shotId);
                        shot.Weapon = NonEmpty(Str(payload, "weaponTypeId"), shot.Weapon);
                        shot.Fired = true;
                    }

                    break;
                }
                case "projectile_impact":
                {
                    string shotId = Str(payload, "shotId");
                    if (shotId.Length > 0)
                    {
                        ShotAccumulator shot = Accumulator(shots, shotId);
                        shot.Weapon = NonEmpty(Str(payload, "weaponTypeId"), shot.Weapon);
                        JsonElement impact = Obj(payload, "impact");
                        shot.Hit |= Flag(impact, "hitEnemy");
                        if (Flag(impact, "killed"))
                        {
                            shot.Kills++;
                        }

                        if (Flag(impact, "headshot"))
                        {
                            shot.Headshots++;
                        }

                        shot.Damage += Num(impact, "actualDamage") ?? 0;
                    }

                    break;
                }
                case "player_position":
                {
                    JsonElement player = Obj(payload, "player");
                    double? x = Num(player, "x");
                    double? z = Num(player, "z");
                    if (x is double px && z is double pz && lastElapsed - lastPathSample >= PATH_SAMPLE_SECONDS)
                    {
                        digest.Path.Add(new PathPoint((float)lastElapsed, (float)px, (float)pz));
                        lastPathSample = lastElapsed;
                    }

                    break;
                }
                case "player_damage":
                {
                    digest.DamageTaken.Add(new DamageDigest(NonEmpty(Str(payload, "enemyTypeId"), "unknown"), Num(payload, "actualDamage") ?? 0));
                    break;
                }
            }
        }

        if (!ended)
        {
            digest.PlaySeconds = lastElapsed;
        }

        digest.Waves = waves.Values.Select(entry => entry.Wave).ToList();
        digest.Spins = spins.Values.ToList();
        digest.Purchases = purchases.Values.ToList();
        digest.Shots = shots.Values
            .Where(shot => shot.Fired)
            .Select(shot => new ShotDigest(shot.Weapon, shot.Hit, shot.Kills, shot.Headshots, shot.Damage))
            .ToList();
        return digest;
    }

    /// <summary>발사 한 번을 줄 단위로 모으는 임시 누적기.</summary>
    private sealed class ShotAccumulator
    {
        public string Weapon { get; set; } = "unknown";
        public bool Fired { get; set; }
        public bool Hit { get; set; }
        public int Kills { get; set; }
        public int Headshots { get; set; }
        public double Damage { get; set; }
    }

    private static ShotAccumulator Accumulator(Dictionary<string, ShotAccumulator> shots, string shotId)
    {
        if (!shots.TryGetValue(shotId, out ShotAccumulator? shot))
        {
            shot = new ShotAccumulator();
            shots[shotId] = shot;
        }

        return shot;
    }

    /// <summary>id가 비어 있으면 줄마다 다른 키를 줘서 서로 덮어쓰지 않게 한다.</summary>
    private static string KeyOr(string id) => id.Length > 0 ? id : Guid.NewGuid().ToString("N");

    private static string NonEmpty(string value, string fallback) => value.Length > 0 ? value : fallback;

    private static JsonElement Obj(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) ? value : default;

    private static string Str(JsonElement element, string name)
    {
        JsonElement value = Obj(element, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static double? Num(JsonElement element, string name)
    {
        JsonElement value = Obj(element, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) ? number : null;
    }

    private static int? Int(JsonElement element, string name) => Num(element, name) is double number ? (int?)Math.Round(number) : null;

    private static long? Int64(JsonElement element, string name) => Num(element, name) is double number ? (long?)Math.Round(number) : null;

    private static bool Flag(JsonElement element, string name) => Obj(element, name).ValueKind == JsonValueKind.True;
}
