using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia.Markup.Xaml;
using Avalonia.Data;
using Avalonia.Platform;

namespace GC.Services;

/// <summary>轻量 i18n：键值表 + 运行时切换（所有绑定自动刷新）。</summary>
public sealed class L : INotifyPropertyChanged
{
    private static readonly L _instance = new();
    public static L I => _instance;

    private Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WeakReference<LocalizedString>> _watchers = new();

    public string Language { get; private set; } = "zh-CN";
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public static readonly (string Code, string Label)[] Supported =
    {
        ("zh-CN", "简体中文"),
        ("en", "English")
    };

    public void Initialize(string? preference)
    {
        var code = Resolve(preference);
        LoadLanguage(code, force: true);
    }

    public static string Resolve(string? pref)
    {
        if (!string.IsNullOrWhiteSpace(pref) && !pref.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return pref!.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-CN";
        var ui = CultureInfo.CurrentUICulture;
        return ui.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en";
    }

    public void SetLanguage(string code)
    {
        code = Resolve(code);
        if (code == Language) return;
        LoadLanguage(code, force: false);
    }

    private void LoadLanguage(string code, bool force)
    {
        var loaded = ReadTable(code) ?? ReadTable("en") ?? new Dictionary<string, string>();
        if (code != "zh-CN")
        {
            // 中文兜底，避免缺键出现空白
            foreach (var kv in ReadTable("zh-CN") ?? new Dictionary<string, string>())
                loaded.TryAdd(kv.Key, kv.Value);
        }
        _map = loaded;
        if (!force && code == Language) return;
        Language = code;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        // 顺带清理已回收的弱引用，避免 _watchers 只增不减
        _watchers.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var w in _watchers)
            if (w.TryGetTarget(out var ls)) ls.Refresh();
        LanguageChanged?.Invoke();
    }

    private static Dictionary<string, string>? ReadTable(string code)
    {
        try
        {
            var uri = new Uri($"avares://GC.Core/Assets/i18n/{code}.json");
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd(),
                new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch
        {
            return null;
        }
    }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return "";
            return _map.TryGetValue(key, out var v) ? v : key;
        }
    }

    public string F(string key, params object?[] args)
    {
        var fmt = this[key];
        try { return string.Format(fmt, args); }
        catch (FormatException) { return fmt; }
    }

    public LocalizedString For(string key)
    {
        var ls = new LocalizedString(key);
        _watchers.Add(new WeakReference<LocalizedString>(ls));
        return ls;
    }

    /// <summary>按 i18n 字典取值：当前语言 -> 英文 -> zh-CN -> 默认名。</summary>
    public string Pick(Dictionary<string, string>? dict, string fallback)
    {
        if (dict == null || dict.Count == 0) return fallback;
        if (dict.TryGetValue(Language, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        if (Language != "en" && dict.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en)) return en;
        if (dict.TryGetValue("zh-CN", out var zh) && !string.IsNullOrWhiteSpace(zh)) return zh;
        foreach (var kv in dict) if (!string.IsNullOrWhiteSpace(kv.Value)) return kv.Value;
        return fallback;
    }
}

public sealed class LocalizedString : INotifyPropertyChanged
{
    public string Key { get; }
    public LocalizedString(string key) => Key = key;
    public string Value => L.I[Key];
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}

/// <summary>XAML 用法：Text="{loc:Loc nav.home}"</summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding(nameof(LocalizedString.Value)) { Source = L.I.For(Key) };
}
