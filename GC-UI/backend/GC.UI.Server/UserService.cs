using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GC.UI.Server;

/// <summary>
/// 本地用户系统：注册 / 登录 / 签到 / 积分 / 主题色。
///
/// 目录结构（都在 .GC/user 下）：
///   .GC/user/user.yaml          本地登录信息（当前用户 + session token）
///   .GC/user/&lt;用户名&gt;/pw.yaml   该用户的账号数据（用户名、积分、密码 sha512）
///
/// 密码只存 sha512(密码 + ":" + 随机盐)，不存明文；盐单独一行保存。
/// </summary>
public sealed class UserService
{
    /// <summary>默认主题色（蓝色）：免费。</summary>
    public const string DefaultThemeColor = "4A90D9";

    /// <summary>预置颜色单价。</summary>
    public const int PresetColorCost = 50;

    /// <summary>自定义颜色单价。</summary>
    public const int CustomColorCost = 150;

    /// <summary>退款窗口：购买后 1 小时内换色可退 80%。</summary>
    public static readonly TimeSpan RefundWindow = TimeSpan.FromHours(1);

    /// <summary>退款比例。</summary>
    public const double RefundRate = 0.8;

    /// <summary>签到周期长度（天）。</summary>
    public const int SignInCycleDays = 30;

    private static readonly string[] PresetColors =
    {
        "4A90D9", "E74C3C", "27AE60", "9B59B6",
        "F39C12", "1ABC9C", "E91E63", "2C3E50"
    };

    private readonly string _userDir;
    private readonly string _sessionFile;

    public readonly ConcurrentDictionary<string, UserRecord> _users = new();

    /// <summary>验证码：id → (答案, 过期时间)。一次性，用后即删。</summary>
    private readonly ConcurrentDictionary<string, (int Answer, DateTime Expires)> _captchas = new();

    /// <summary>记住的登录用户（来自 user.yaml），没有显式 token 时用它。</summary>
    private string? _restoredUser;

    public UserService(string homeRoot)
    {
        _userDir = Path.Combine(homeRoot, "user");
        _sessionFile = Path.Combine(_userDir, "user.yaml");
        Directory.CreateDirectory(_userDir);
        LoadAll();
        LoadSession();
    }

    // ---------------------------------------------------------------- 读写磁盘

    private void LoadAll()
    {
        foreach (var dir in Directory.GetDirectories(_userDir))
        {
            try
            {
                var file = Path.Combine(dir, "pw.yaml");
                if (!File.Exists(file)) continue;
                var rec = DeserializeUser(Path.GetFileName(dir), File.ReadAllText(file));
                if (rec != null) _users[rec.Username.ToLowerInvariant()] = rec;
            }
            catch { /* 单个用户文件损坏不影响其他用户 */ }
        }

        // 兼容旧布局：.GC/user/<用户名>.yaml 直接放在根下（迁移到 <用户名>/pw.yaml）
        foreach (var legacy in Directory.GetFiles(_userDir, "*.yaml"))
        {
            if (string.Equals(legacy, _sessionFile, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var name = Path.GetFileNameWithoutExtension(legacy);
                var rec = DeserializeUser(name, File.ReadAllText(legacy));
                if (rec == null) continue;
                _users[rec.Username.ToLowerInvariant()] = rec;
                SaveUser(rec);
                File.Delete(legacy);
            }
            catch { /* 忽略 */ }
        }
    }

    private static UserRecord? DeserializeUser(string fallbackName, string yaml)
    {
        var lines = yaml.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string? username = null, pwHash = null, pwSalt = null, themeColor = null;
        decimal points = 0, themePaid = 0;
        int consecutiveDays = 0, fortuneCount = 0, pickCount = 0;
        DateTime? lastSignIn = null, themePaidAt = null, fortuneDate = null, pickDate = null;

        foreach (var line in lines)
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var val = parts[1].Trim();
            switch (key)
            {
                case "username": username = val; break;
                case "pw_hash": pwHash = val; break;
                case "pw_salt": pwSalt = val; break;
                // 旧文件里积分是整数；decimal.TryParse 兼容读取
                case "points": decimal.TryParse(val, out var p); points = p; break;
                case "last_sign_in":
                    if (DateTime.TryParse(val, out var ls)) lastSignIn = ls;
                    break;
                case "consecutive_days": int.TryParse(val, out var d); consecutiveDays = d; break;
                case "theme_color": themeColor = val; break;
                case "theme_paid": decimal.TryParse(val, out var tp); themePaid = tp; break;
                case "theme_paid_at":
                    if (DateTime.TryParse(val, out var ta)) themePaidAt = ta;
                    break;
                case "fortune_date":
                    if (DateTime.TryParse(val, out var fd)) fortuneDate = fd;
                    break;
                case "fortune_count": int.TryParse(val, out var fc); fortuneCount = fc; break;
                case "pick_date":
                    if (DateTime.TryParse(val, out var pd)) pickDate = pd;
                    break;
                case "pick_count": int.TryParse(val, out var pc); pickCount = pc; break;
            }
        }

        if (string.IsNullOrWhiteSpace(username)) username = fallbackName;
        if (pwHash == null || pwSalt == null) return null;

        return new UserRecord(username!, pwHash, pwSalt, points, lastSignIn, consecutiveDays,
            string.IsNullOrWhiteSpace(themeColor) ? DefaultThemeColor : themeColor,
            themePaid, themePaidAt)
        {
            FortuneDate = fortuneDate,
            FortuneCount = fortuneCount,
            PickDate = pickDate,
            PickCount = pickCount
        };
    }

    private void SaveUser(UserRecord rec)
    {
        try
        {
            var dir = Path.Combine(_userDir, SafeDirName(rec.Username));
            Directory.CreateDirectory(dir);

            var yaml = new StringBuilder()
                .AppendLine($"username: {rec.Username}")
                .AppendLine($"pw_hash: {rec.PwHash}")
                .AppendLine($"pw_salt: {rec.PwSalt}")
                .AppendLine($"points: {rec.Points.ToString("0.##")}")
                .AppendLine($"last_sign_in: {rec.LastSignIn?.ToString("yyyy-MM-dd") ?? ""}")
                .AppendLine($"consecutive_days: {rec.ConsecutiveDays}")
                .AppendLine($"theme_color: {rec.ThemeColor ?? DefaultThemeColor}")
                .AppendLine($"theme_paid: {rec.ThemePaid.ToString("0.##")}")
                .AppendLine($"theme_paid_at: {rec.ThemePaidAt?.ToString("o") ?? ""}")
                .AppendLine($"fortune_date: {rec.FortuneDate?.ToString("yyyy-MM-dd") ?? ""}")
                .AppendLine($"fortune_count: {rec.FortuneCount}")
                .AppendLine($"pick_date: {rec.PickDate?.ToString("yyyy-MM-dd") ?? ""}")
                .AppendLine($"pick_count: {rec.PickCount}")
                .ToString();

            File.WriteAllText(Path.Combine(dir, "pw.yaml"), yaml);
        }
        catch { /* 写盘失败不影响内存状态 */ }
    }

    /// <summary>用户名做目录名时过滤掉路径分隔符与 Windows 保留字符。</summary>
    private static string SafeDirName(string username)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = username.Select(c => invalid.Contains(c) || c == '.' ? '_' : c).ToArray();
        var name = new string(chars).Trim();
        return string.IsNullOrEmpty(name) ? "_" : name;
    }

    // ---------------------------------------------------------------- 登录态

    private void LoadSession()
    {
        try
        {
            if (!File.Exists(_sessionFile)) return;
            foreach (var line in File.ReadAllLines(_sessionFile))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2 && parts[0].Trim() == "username")
                {
                    var name = parts[1].Trim();
                    if (!string.IsNullOrEmpty(name) && _users.ContainsKey(name.ToLowerInvariant()))
                        _restoredUser = name.ToLowerInvariant();
                }
            }
        }
        catch { /* 忽略 */ }
    }

    public void SaveSession(string username, string token)
    {
        try
        {
            var lower = username.ToLowerInvariant();
            _restoredUser = lower;
            Directory.CreateDirectory(_userDir);
            File.WriteAllText(_sessionFile,
                $"username: {username}\nsession_token: {token}\n");
        }
        catch { /* 忽略 */ }
    }

    public void ClearSession()
    {
        _restoredUser = null;
        try { if (File.Exists(_sessionFile)) File.Delete(_sessionFile); } catch { /* 忽略 */ }
    }

    /// <summary>没有携带 token 时，回退到 user.yaml 里记住的用户（自动登录）。</summary>
    public string? RestoredUser => _restoredUser;

    // ---------------------------------------------------------------- 验证码

    /// <summary>签发一个算术验证码，5 分钟内有效，用后即焚。</summary>
    public (string Id, string Question) IssueCaptcha()
    {
        var now = DateTime.UtcNow;

        // 顺手清理过期项，避免长时间运行后字典无限增长
        if (_captchas.Count > 128)
        {
            foreach (var kv in _captchas)
                if (kv.Value.Expires < now) _captchas.TryRemove(kv.Key, out _);
        }

        var a = RandomNumberGenerator.GetInt32(2, 10);
        var b = RandomNumberGenerator.GetInt32(2, 10);
        var plus = RandomNumberGenerator.GetInt32(0, 2) == 0;
        var answer = plus ? a + b : Math.Max(a, b) - Math.Min(a, b);
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

        _captchas[id] = (answer, now.AddMinutes(5));
        var question = plus ? $"{a} + {b} = ?" : $"{Math.Max(a, b)} - {Math.Min(a, b)} = ?";
        return (id, question);
    }

    public bool VerifyCaptcha(string? id, string? answer, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(answer))
        {
            error = "请先完成人机验证";
            return false;
        }
        if (!_captchas.TryRemove(id, out var entry))
        {
            error = "验证码已失效，请重新获取";
            return false;
        }
        if (entry.Expires < DateTime.UtcNow)
        {
            error = "验证码已过期，请重新获取";
            return false;
        }
        if (!int.TryParse(answer.Trim(), out var value) || value != entry.Answer)
        {
            error = "验证码答案错误";
            return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- 账号

    public bool Register(string username, string password, string? captchaId, string? captcha,
        out string error)
    {
        if (!VerifyCaptcha(captchaId, captcha, out error)) return false;

        if (string.IsNullOrWhiteSpace(username) || username.Length > 20)
        {
            error = "用户名需为 1–20 个字符";
            return false;
        }
        if (username.Any(char.IsWhiteSpace))
        {
            error = "用户名不能包含空格";
            return false;
        }
        if (_users.ContainsKey(username.Trim().ToLowerInvariant()))
        {
            error = "用户名已存在，请换一个";
            return false;
        }
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            error = "密码至少 6 位";
            return false;
        }

        var name = username.Trim();
        var salt = GenerateSalt();
        var rec = new UserRecord(name, HashPassword(password, salt), salt,
            0, null, 0, DefaultThemeColor, 0, null);
        _users[name.ToLowerInvariant()] = rec;
        SaveUser(rec);
        return true;
    }

    public bool Login(string username, string password, out string error, out UserRecord? record)
    {
        error = "";
        record = null;
        if (!_users.TryGetValue(username.Trim().ToLowerInvariant(), out var rec))
        {
            error = "用户名或密码错误";
            return false;
        }
        if (HashPassword(password, rec.PwSalt) != rec.PwHash)
        {
            error = "用户名或密码错误";
            return false;
        }
        record = rec;
        return true;
    }

    public bool TryGetUser(string username, out UserRecord? record)
        => _users.TryGetValue(username.Trim().ToLowerInvariant(), out record);

    public UserRecord? GetFirstUser() => _users.Values.FirstOrDefault();

    public bool UpdatePoints(string username, decimal deltaPoints, out string error)
    {
        error = "";
        if (!_users.TryGetValue(username.Trim().ToLowerInvariant(), out var rec))
        {
            error = "用户不存在";
            return false;
        }
        rec.Points = Math.Max(0, rec.Points + deltaPoints);
        SaveUser(rec);
        return true;
    }

    // ---------------------------------------------------------------- 签到

    /// <summary>
    /// 30 天一循环的签到奖励。第 7/14/21/28 天给「周奖励」，第 30 天给满月奖励。
    /// </summary>
    public static int GetSignInReward(int cycleDay)
    {
        if (cycleDay < 1) cycleDay = 1;
        if (cycleDay > SignInCycleDays) cycleDay = ((cycleDay - 1) % SignInCycleDays) + 1;

        return cycleDay switch
        {
            7 => 30,
            14 => 40,
            21 => 50,
            28 => 60,
            30 => 100,
            <= 6 => 10,
            <= 13 => 15,
            <= 20 => 20,
            <= 27 => 25,
            _ => 40
        };
    }

    public static int CycleDay(int consecutiveDays)
    {
        if (consecutiveDays < 1) return 1;
        return ((consecutiveDays - 1) % SignInCycleDays) + 1;
    }

    public bool UpdateSignIn(string username, out string error, out int reward)
    {
        error = "";
        reward = 0;
        if (!_users.TryGetValue(username.Trim().ToLowerInvariant(), out var rec))
        {
            error = "用户不存在";
            return false;
        }

        // 本地日历日：签到按用户机器上的「今天」算（0 点翻篇），不能用 UTC——
        // 否则东八区用户要到早上 8 点才算新的一天，会出现同一天签到两次。
        var today = DateTime.Today;
        if (rec.LastSignIn.HasValue)
        {
            var diff = (today - rec.LastSignIn.Value.Date).Days;
            if (diff == 0)
            {
                error = "今日已签到";
                return false;
            }
            // 断签超过一天：连续天数重置（但 30 天周期按新的连续天数重新算）
            rec.ConsecutiveDays = diff == 1 ? rec.ConsecutiveDays + 1 : 1;
        }
        else
        {
            rec.ConsecutiveDays = 1;
        }

        rec.LastSignIn = today;
        reward = GetSignInReward(CycleDay(rec.ConsecutiveDays));
        rec.Points += reward;
        SaveUser(rec);
        return true;
    }

    // ---------------------------------------------------------------- 主题色

    /// <summary>某个颜色的价格：默认蓝免费，预置色 50，其它（自定义）150。</summary>
    public static int ThemeCost(string hex)
    {
        var h = NormalizeHex(hex);
        if (string.IsNullOrEmpty(h)) return -1;
        if (string.Equals(h, DefaultThemeColor, StringComparison.OrdinalIgnoreCase)) return 0;
        return PresetColors.Contains(h, StringComparer.OrdinalIgnoreCase)
            ? PresetColorCost
            : CustomColorCost;
    }

    public static string NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "";
        var h = hex.Trim().TrimStart('#');
        if (h.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) h = h[2..];
        if (h.Length != 6) return "";
        return h.All(Uri.IsHexDigit) ? h.ToUpperInvariant() : "";
    }

    /// <summary>
    /// 换主题色。服务端自己算价，不信客户端传的 cost。
    /// 上次购买在 1 小时内，先按 80% 退还上次花费。
    /// </summary>
    public bool ChangeThemeColor(string username, string colorHex, out string error, out decimal refunded)
    {
        error = "";
        refunded = 0;

        if (!_users.TryGetValue(username.Trim().ToLowerInvariant(), out var rec))
        {
            error = "用户不存在";
            return false;
        }

        var hex = NormalizeHex(colorHex);
        if (string.IsNullOrEmpty(hex))
        {
            error = "颜色格式应为 #RRGGBB";
            return false;
        }

        var cost = ThemeCost(hex);
        if (cost < 0)
        {
            error = "颜色格式应为 #RRGGBB";
            return false;
        }

        if (string.Equals(NormalizeHex(rec.ThemeColor), hex, StringComparison.OrdinalIgnoreCase))
        {
            error = "当前已经是这个颜色";
            return false;
        }

        // 1 小时内换色：退回上次花费的 80%（保留 2 位小数）
        var available = rec.Points;
        if (rec.ThemePaid > 0 && rec.ThemePaidAt.HasValue &&
            DateTime.UtcNow - rec.ThemePaidAt.Value <= RefundWindow)
        {
            refunded = Math.Floor(rec.ThemePaid * (decimal)RefundRate * 100) / 100;
            available += refunded;
        }

        if (available < cost)
        {
            error = refunded > 0
                ? $"积分不足（含可退 {refunded}，需 {cost}）"
                : $"积分不足，需要 {cost} 积分";
            return false;
        }

        rec.Points = available - cost;
        rec.ThemeColor = hex;
        rec.ThemePaid = cost;
        rec.ThemePaidAt = cost > 0 ? DateTime.UtcNow : null;
        SaveUser(rec);
        return true;
    }

    /// <summary>距离退款窗口关闭还剩多久；没有可退的就返回 null。</summary>
    public static TimeSpan? RefundRemaining(UserRecord rec)
    {
        if (rec.ThemePaid <= 0 || !rec.ThemePaidAt.HasValue) return null;
        var left = RefundWindow - (DateTime.UtcNow - rec.ThemePaidAt.Value);
        return left > TimeSpan.Zero ? left : null;
    }

    // ---------------------------------------------------------------- 人品值 / 二选一

    /// <summary>
    /// 人品值当日费用。第一次免费；之后按当日已付费次数阶梯计费：
    /// 第 1–5 次 0.1 / 第 6–10 次 0.2 / 第 11–25 次 0.5 / 第 26 次起 1。
    /// </summary>
    public static decimal FortuneCost(int paidIndexToday)
    {
        if (paidIndexToday <= 0) return 0m;
        if (paidIndexToday <= 5) return 0.1m;
        if (paidIndexToday <= 10) return 0.2m;
        if (paidIndexToday <= 25) return 0.5m;
        return 1m;
    }

    /// <summary>二选一当日费用：第一次免费，之后每次 0.5。</summary>
    public static decimal PickCost(int paidIndexToday)
        => paidIndexToday <= 0 ? 0m : 0.5m;

    /// <summary>算出当日费用并扣费；积分不足返回 false。成功后计数 +1。</summary>
    private bool ChargeDailyFeature(UserRecord rec, Func<int, decimal> costFn,
        DateTime? featureDate, int featureCount, out decimal cost, out string error)
    {
        error = "";
        cost = 0;

        // 本地日历日：签到按用户机器上的「今天」算（0 点翻篇），不能用 UTC——
        // 否则东八区用户要到早上 8 点才算新的一天，会出现同一天签到两次。
        var today = DateTime.Today;
        // 跨天了先重置计数
        if (featureDate.HasValue && featureDate.Value.Date != today)
            featureCount = 0;

        cost = costFn(featureCount);
        if (rec.Points < cost)
        {
            error = cost > 0
                ? $"积分不足，本次需要 {cost.ToString("0.##")} 积分"
                : "积分不足";
            return false;
        }

        rec.Points = Math.Round(rec.Points - cost, 2);
        return true;
    }

    /// <summary>
    /// 摇一次人品值（服务端算随机数，客户端不信自己传的值）。
    /// 当天第一次免费，之后按阶梯扣积分。
    /// </summary>
    public bool RollFortune(string username, out string error, out int value,
        out decimal cost, out UserRecord? record)
    {
        error = "";
        value = 0;
        cost = 0;
        record = null;

        if (!_users.TryGetValue(username.Trim().ToLowerInvariant(), out var rec))
        {
            error = "用户不存在";
            return false;
        }

        // 本地日历日：签到按用户机器上的「今天」算（0 点翻篇），不能用 UTC——
        // 否则东八区用户要到早上 8 点才算新的一天，会出现同一天签到两次。
        var today = DateTime.Today;
        var count = rec.FortuneDate.HasValue && rec.FortuneDate.Value.Date == today
            ? rec.FortuneCount : 0;

        if (!ChargeDailyFeature(rec, FortuneCost, rec.FortuneDate, count, out cost, out error))
            return false;

        rec.FortuneDate = today;
        rec.FortuneCount = count + 1;
        value = RandomNumberGenerator.GetInt32(0, 101);
        record = rec;
        SaveUser(rec);
        return true;
    }

    /// <summary>二选一：服务端随机挑一个。当天第一次免费，之后每次 0.5。</summary>
    public bool PickOne(string username, string optionA, string optionB, out string error,
        out string choice, out decimal cost, out UserRecord? record)
    {
        error = "";
        choice = "";
        cost = 0;
        record = null;

        if (string.IsNullOrWhiteSpace(optionA) || string.IsNullOrWhiteSpace(optionB))
        {
            error = "两个选项都不能为空";
            return false;
        }

        if (!_users.TryGetValue(username.Trim().ToLowerInvariant(), out var rec))
        {
            error = "用户不存在";
            return false;
        }

        // 本地日历日：签到按用户机器上的「今天」算（0 点翻篇），不能用 UTC——
        // 否则东八区用户要到早上 8 点才算新的一天，会出现同一天签到两次。
        var today = DateTime.Today;
        var count = rec.PickDate.HasValue && rec.PickDate.Value.Date == today
            ? rec.PickCount : 0;

        if (!ChargeDailyFeature(rec, PickCost, rec.PickDate, count, out cost, out error))
            return false;

        rec.PickDate = today;
        rec.PickCount = count + 1;
        choice = RandomNumberGenerator.GetInt32(0, 2) == 0 ? optionA : optionB;
        record = rec;
        SaveUser(rec);
        return true;
    }

    // ---------------------------------------------------------------- 散列

    private static string GenerateSalt()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashPassword(string password, string salt)
    {
        using var sha512 = SHA512.Create();
        var hash = sha512.ComputeHash(Encoding.UTF8.GetBytes(password + ":" + salt));
        return Convert.ToBase64String(hash);
    }
}

public class UserRecord
{
    public string Username { get; init; } = "";
    public string PwHash { get; init; } = "";
    public string PwSalt { get; init; } = "";

    /// <summary>积分，精确到 0.01。</summary>
    public decimal Points { get; set; }
    public DateTime? LastSignIn { get; set; }
    public int ConsecutiveDays { get; set; }
    public string? ThemeColor { get; set; }

    /// <summary>当前这个颜色实际花了多少分（用于 1 小时内退款）。</summary>
    public decimal ThemePaid { get; set; }

    /// <summary>购买该颜色的时间（UTC）。</summary>
    public DateTime? ThemePaidAt { get; set; }

    /// <summary>人品值当日最后计算日期（UTC），跨天计数清零。</summary>
    public DateTime? FortuneDate { get; set; }

    /// <summary>人品值当日已计算次数（含免费的那次）。</summary>
    public int FortuneCount { get; set; }

    /// <summary>二选一当日最后使用日期（UTC）。</summary>
    public DateTime? PickDate { get; set; }

    /// <summary>二选一当日已使用次数（含免费的那次）。</summary>
    public int PickCount { get; set; }

    public UserRecord(string username, string pwHash, string pwSalt, decimal points,
        DateTime? lastSignIn, int consecutiveDays, string? themeColor = null,
        decimal themePaid = 0, DateTime? themePaidAt = null)
    {
        Username = username;
        PwHash = pwHash;
        PwSalt = pwSalt;
        Points = points;
        LastSignIn = lastSignIn;
        ConsecutiveDays = consecutiveDays;
        ThemeColor = themeColor;
        ThemePaid = themePaid;
        ThemePaidAt = themePaidAt;
    }
}
