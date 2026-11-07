using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EarthModelStudio.Models;
using EarthModelStudio.Services;

namespace EarthModelStudio.ViewModels;

public sealed class LanguageOption
{
    public string Code { get; init; } = "auto";
    public string Label { get; set; } = "";
    public override string ToString() => Label;
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private bool _loading = true;

    public ObservableCollection<SettingsSourceViewModel> Sources { get; } = new();
    public ObservableCollection<LanguageOption> Languages { get; } = new();
    public int[] FrameRates { get; } = { 24, 30, 60 };

    [ObservableProperty] private LanguageOption? _selectedLanguage;
    [ObservableProperty] private int _selectedFps = 30;
    [ObservableProperty] private double _memoryValue = 2048;
    [ObservableProperty] private string _statusMessage = "";

    public string MemoryValueText => $"{(int)Math.Round(MemoryValue)} MB";
    public string UsageText => _services.Memory.UsageText;
    public string ModelRoot => _services.Paths.ModelRoot;
    public string DataRoot => _services.Paths.DataRoot;
    public string PlatformText => $"{RuntimeInformation.OSDescription}  ·  {RuntimeInformation.RuntimeIdentifier}";
    public string VersionText => "v1.0.0";
    public string MemoryHint => L.I["set.memory.hint"];

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        L.I.LanguageChanged += OnLanguageChanged;
        Refresh();
        _loading = false;
    }

    public void Refresh()
    {
        _loading = true;
        BuildLanguages();
        var settings = _services.Settings.Current;
        SelectedFps = settings.RenderFps;
        MemoryValue = settings.MemoryMb;

        Sources.Clear();
        foreach (var source in settings.Sources)
            Sources.Add(new SettingsSourceViewModel(source, _services, this));

        _loading = false;
        OnPropertyChanged(nameof(UsageText));
        OnPropertyChanged(nameof(MemoryValueText));
        OnPropertyChanged(nameof(MemoryHint));
        OnPropertyChanged(nameof(ModelRoot));
        OnPropertyChanged(nameof(DataRoot));
    }

    private void BuildLanguages()
    {
        var current = _services.Settings.Current.Language;
        Languages.Clear();
        Languages.Add(new LanguageOption { Code = "auto", Label = L.I["set.language.auto"] });
        foreach (var (code, label) in L.Supported)
            Languages.Add(new LanguageOption { Code = code, Label = label });
        SelectedLanguage = Languages.FirstOrDefault(l => l.Code == current) ?? Languages[0];
    }

    private void OnLanguageChanged()
    {
        _loading = true;
        BuildLanguages();
        _loading = false;
        Refresh();
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (_loading || value == null) return;
        _services.Settings.Apply(s => s.Language = value.Code);
        L.I.SetLanguage(value.Code);
    }

    partial void OnSelectedFpsChanged(int value)
    {
        if (_loading) return;
        _services.Settings.Apply(s => s.RenderFps = Math.Clamp(value, 12, 120));
    }

    partial void OnMemoryValueChanged(double value)
    {
        if (_loading) return;
        var mb = (int)Math.Round(value);
        _services.Settings.Apply(s => s.MemoryMb = mb);
        _services.Memory.Apply(mb);
        OnPropertyChanged(nameof(MemoryValueText));
        OnPropertyChanged(nameof(UsageText));
    }

    [RelayCommand]
    private void OpenModelFolder() => Shell.OpenFolder(_services.Paths.ModelRoot);

    [RelayCommand]
    private void OpenDataFolder() => Shell.OpenFolder(_services.Paths.DataRoot);

    [RelayCommand]
    private void ClearCache()
    {
        // 模型数据由模型自己管理；这里只回收启动器侧托管内存
        GC.Collect();
        GC.WaitForPendingFinalizers();
        _services.Memory.Refresh();
        StatusMessage = L.I["set.cacheCleared"];
        OnPropertyChanged(nameof(UsageText));
    }

    [RelayCommand]
    private void ApplyMemory()
    {
        _services.Memory.Apply((int)Math.Round(MemoryValue));
        StatusMessage = L.I["common.applied"];
        OnPropertyChanged(nameof(UsageText));
    }

    internal void RemoveSource(SettingsSourceViewModel source)
    {
        _services.Settings.Current.Sources.Remove(source.Model);
        _services.Settings.Save();
        Sources.Remove(source);
        _services.Settings.Notify();
    }
}

public partial class SettingsSourceViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly SettingsViewModel _owner;

    public ModelSource Model { get; }

    public SettingsSourceViewModel(ModelSource model, AppServices services, SettingsViewModel owner)
    {
        Model = model;
        _services = services;
        _owner = owner;
        RemoveCommand = new RelayCommand(() => _owner.RemoveSource(this));
    }

    public string Name => string.IsNullOrWhiteSpace(Model.Name) ? Model.Id : Model.Name;
    public string UrlText => string.IsNullOrWhiteSpace(Model.Url) ? L.I["dl.noSource"] : Model.Url;

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            if (Model.Enabled == value) return;
            Model.Enabled = value;
            _services.Settings.Save();
            OnPropertyChanged();
        }
    }

    public ICommand RemoveCommand { get; }
}
