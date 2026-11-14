using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GC.Models;
using Avalonia.Media.Imaging;
using GC.Services;

namespace GC.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;

    public ObservableCollection<ModelCardViewModel> Models { get; } = new();

    [ObservableProperty] private ModelCardViewModel? _selected;
    [ObservableProperty] private bool _isEmpty;

    public string CountText => L.I.F("home.count", Models.Count);
    public string SourceCountText => _services.Settings.Current.Sources
        .Count(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Url)).ToString();
    public string MemoryText => $"{_services.Memory.BudgetMb} MB";
    public string SelectedHint => Selected == null ? L.I["home.recent"] : L.I["home.launchHint"];

    public HomeViewModel(AppServices services, MainViewModel main)
    {
        _services = services;
        _main = main;
        _services.Models.Changed += Refresh;
        L.I.LanguageChanged += RefreshLanguage;
        Refresh();
    }

    public void Refresh()
    {
        var keepId = Selected?.Package.Id;
        Models.Clear();
        foreach (var package in _services.Models.Scan())
            Models.Add(new ModelCardViewModel(package, _services, this));

        Selected = Models.FirstOrDefault(m => m.Package.Id == keepId) ?? Models.FirstOrDefault();
        IsEmpty = Models.Count == 0;
        RefreshLanguage();
    }

    private void RefreshLanguage()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(SourceCountText));
        OnPropertyChanged(nameof(MemoryText));
        OnPropertyChanged(nameof(SelectedHint));
        foreach (var model in Models) model.RefreshLanguage();
    }

    public void LaunchModel(ModelCardViewModel card)
    {
        Selected = card;
        _main.PlayModel(card.Package);
    }

    public async Task DeleteAsync(ModelCardViewModel card)
    {
        if (!await _main.ConfirmAsync(L.I.F("home.deleteConfirm", card.Name))) return;
        _services.Models.Delete(card.Package.Id);
        _services.Models.NotifyChanged();
        Refresh();
    }

    [RelayCommand]
    private void Launch()
    {
        var card = Selected ?? Models.FirstOrDefault();
        if (card != null) _main.PlayModel(card.Package);
    }

    [RelayCommand]
    private void OpenDownload() => _main.GoDownload();

    [RelayCommand]
    private void OpenSettings() => _main.GoSettings();

    [RelayCommand]
    private void OpenFolder() => Shell.OpenFolder(_services.Paths.ModelRoot);
}

public partial class ModelCardViewModel : ObservableObject
{
    private readonly AppServices _services;
    private Bitmap? _preview;

    public ModelPackage Package { get; }

    public ModelCardViewModel(ModelPackage package, AppServices services, HomeViewModel home)
    {
        Package = package;
        _services = services;
        LaunchCommand = new RelayCommand(() => home.LaunchModel(this));
        DeleteCommand = new AsyncRelayCommand(() => home.DeleteAsync(this));
        OpenFolderCommand = new RelayCommand(() => Shell.OpenFolder(package.Directory));
    }

    public string Name => Package.LocalizedName;
    public string Description => Package.LocalizedDescription;
    public string VersionText => "v" + Package.Manifest.Version;
    public string SizeText => ModelScanner.HumanSize(Package.SizeBytes);
    public string MetaText => $"{VersionText}  ·  {SizeText}";
    public string TagsText => string.Join("  ·  ", Package.Manifest.Tags ?? new());
    public bool HasTags => !string.IsNullOrWhiteSpace(TagsText);
    public string AuthorText => string.IsNullOrWhiteSpace(Package.Manifest.Author) ? "" : Package.Manifest.Author;

    /// <summary>封面图来自模型包自带的 preview.png —— 启动器只显示，不渲染。</summary>
    public Bitmap? Preview
    {
        get
        {
            if (_preview != null) return _preview;
            try
            {
                if (File.Exists(Package.PreviewPath)) _preview = new Bitmap(Package.PreviewPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"读取模型封面失败 {Package.Id}: {ex.Message}");
            }
            return _preview;
        }
    }

    public bool HasPreview => Preview != null;

    public ICommand LaunchCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(TagsText));
    }
}
