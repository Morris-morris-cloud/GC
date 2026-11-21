using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GC.UI.Server;

/// <summary>
/// 本地用户系统：用户名/密码/积分，数据存在 .GC/user/ 目录。
/// </summary>
public sealed class UserService
{
    private readonly string _userDir;
    public readonly ConcurrentDictionary<string, UserRecord> _users = new();

    public UserService(string homeRoot)
    {
        _userDir = Path.Combine(homeRoot, "user");
        Directory.CreateDirectory(_userDir);
        LoadAll();
    }

    private void LoadAll()
    {
        foreach (var file in Directory.GetFiles(_userDir, "*.yaml"))
        {
            try
            {
                var username = Path.GetFileNameWithoutExtension(file);
                var content = File.ReadAllText(file);
                var rec = DeserializeUser(username, content);
                if (rec != null) _users[username] = rec;
            }
            catch { /* 忽略损坏文件 */ }
        }
    }

    private UserRecord? DeserializeUser(string username, string yaml)
    {
        var lines = yaml.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string? pwHash = null, pwSalt = null;
        int points = 0;
        DateTime? lastSignIn = null;
        int consecutiveDays = 0;
        string? themeColor = null;

        foreach (var line in lines)
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var val = parts[1].Trim();
            switch (key)
            {
                case "pw_hash": pwHash = val; break;
                case "pw_salt": pwSalt = val; break;
                case "points": points = int.TryParse(val, out var p) ? p : 0; break;
                case "last_sign_in":
                    if (DateTime.TryParse(val, out var ls)) lastSignIn = ls;
                    break;
                case "consecutive_days": consecutiveDays = int.TryParse(val, out var d) ? d : 0; break;
                case "theme_color": themeColor = val; break;
            }
        }

        if (pwHash == null || pwSalt == null) return null;
        return new UserRecord(username, pwHash, pwSalt, points, lastSignIn, consecutiveDays, themeColor);
    }

    public bool Register(string username, string password, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(username) || username.Length > 20)
        {
            error = "用户名无效";
            return false;
        }
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            error = "密码至少6位";
            return false;
        }
        if (_users.ContainsKey(username.ToLower()))
        {
            error = "用户名已存在";
            return false;
        }

        var salt = GenerateSalt();
        var hash = HashPassword(password, salt);
        var rec = new UserRecord(username, hash, salt, 0, null, 0, null);
        _users[username.ToLower()] = rec;
        SaveUser(rec);
        return true;
    }

    public bool Login(string username, string password, out string error, out UserRecord? record)
    {
        error = "";
        record = null;
        if (!_users.TryGetValue(username.ToLower(), out var rec))
        {
            error = "用户名或密码错误";
            return false;
        }
        var hash = HashPassword(password, rec.PwSalt);
        if (hash != rec.PwHash)
        {
            error = "用户名或密码错误";
            return false;
        }
        record = rec;
        return true;
    }

    public bool UpdatePoints(string username, int deltaPoints, out string error)
    {
        error = "";
        if (!_users.TryGetValue(username, out var rec))
        {
            error = "用户不存在";
            return false;
        }
        rec.Points = Math.Max(0, rec.Points + deltaPoints);
        _users[username] = rec;
        SaveUser(rec);
        return true;
    }

    public bool UpdateSignIn(string username, out string error)
    {
        error = "";
        if (!_users.TryGetValue(username, out var rec))
        {
            error = "用户不存在";
            return false;
        }

        var today = DateTime.UtcNow.Date;
        if (rec.LastSignIn.HasValue)
        {
            var last = rec.LastSignIn.Value.Date;
            var diff = (today - last).Days;
            if (diff == 0)
            {
                error = "今日已签到";
                return false;
            }
            if (diff == 1)
            {
                rec.ConsecutiveDays++;
            }
            else
            {
                rec.ConsecutiveDays = 1;
            }
        }
        else
        {
            rec.ConsecutiveDays = 1;
        }
        rec.LastSignIn = today;

        var cycleDay = (rec.ConsecutiveDays - 1) % 30 + 1;
        int reward = GetSignInReward(cycleDay);
        rec.Points += reward;

        _users[username] = rec;
        SaveUser(rec);
        return true;
    }

    public int GetSignInReward(int day)
    {
        if (day <= 7) return 10;
        if (day <= 15) return 20;
        return 30;
    }

    public UserRecord? GetFirstUser()
    {
        return _users.Values.FirstOrDefault();
    }

    public bool TryGetUser(string username, out UserRecord? record)
    {
        return _users.TryGetValue(username.ToLower(), out record);
    }

    public bool ChangeThemeColor(string username, string colorHex, int cost, out string error)
    {
        error = "";
        if (!_users.TryGetValue(username, out var rec))
        {
            error = "用户不存在";
            return false;
        }
        if (rec.Points < cost)
        {
            error = "积分不足";
            return false;
        }
        rec.Points -= cost;
        rec.ThemeColor = colorHex;
        _users[username] = rec;
        SaveUser(rec);
        return true;
    }

    private void SaveUser(UserRecord rec)
    {
        try
        {
            var file = Path.Combine(_userDir, $"{rec.Username.ToLower()}.yaml");
            var yaml = $"username: {rec.Username}\npw_hash: {rec.PwHash}\npw_salt: {rec.PwSalt}\npoints: {rec.Points}\nlast_sign_in: {rec.LastSignIn?.ToString("yyyy-MM-dd") ?? ""}\nconsecutive_days: {rec.ConsecutiveDays}\ntheme_color: {rec.ThemeColor ?? ""}\n";
            File.WriteAllText(file, yaml);
        }
        catch { /* 忽略写入失败 */ }
    }

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
        var combined = Encoding.UTF8.GetBytes(password + ":" + salt);
        var hash = sha512.ComputeHash(combined);
        return Convert.ToBase64String(hash);
    }
}

public class UserRecord
{
    public string Username { get; init; } = "";
    public string PwHash { get; init; } = "";
    public string PwSalt { get; init; } = "";
    public int Points { get; set; }
    public DateTime? LastSignIn { get; set; }
    public int ConsecutiveDays { get; set; }
    public string? ThemeColor { get; set; }

    public UserRecord(string username, string pwHash, string pwSalt, int points, DateTime? lastSignIn, int consecutiveDays, string? themeColor = null)
    {
        Username = username;
        PwHash = pwHash;
        PwSalt = pwSalt;
        Points = points;
        LastSignIn = lastSignIn;
        ConsecutiveDays = consecutiveDays;
        ThemeColor = themeColor;
    }
}
