using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace NeoRF.ContourMap;

/// <summary>等高线模型的界面：顶部标题、中间双图联动、右侧判读要点、底部实时读数。</summary>
public sealed class ContourPage
{
    private static readonly Color CAppBg = Color.Parse("#0B1524");
    private static readonly Color CPanel = Color.Parse("#101E31");
    private static readonly Color CLine = Color.Parse("#1E3350");
    private static readonly Color CInk = Color.Parse("#EAF2FF");
    private static readonly Color CInkDim = Color.Parse("#93A8C6");
    private static readonly Color CInkFaint = Color.Parse("#6A80A0");
    private static readonly Color CAccent = Color.Parse("#4D8CFF");
    private static readonly Color CWarn = Color.Parse("#FFB454");
    private static readonly Color CGood = Color.Parse("#5FD69B");

    private readonly TerrainField _field;
    private readonly Dictionary<string, string> _texts;
    private ContourView? _view;
    private TextBlock? _readout;
    private TextBlock? _featureText;

    public ContourPage(TerrainField field, Dictionary<string, string> texts)
    {
        _field = field;
        _texts = texts;
    }

    private string T(string key) => _texts.TryGetValue(key, out var v) ? v : key;

    private static SolidColorBrush B(Color c) => new(c);

    public Control Build()
    {
        var root = new Grid { Background = B(CAppBg) };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        root.Children.Add(BuildHeader());

        var body = new Grid { Margin = new Thickness(14, 8, 14, 6) };
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(288)));

        _view = new ContourView(_field, T);
        _view.HoverChanged += OnHover;
        var canvasHolder = new Border
        {
            Background = B(Color.Parse("#08111E")),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = _view
        };
        Grid.SetColumn(canvasHolder, 0);
        body.Children.Add(canvasHolder);

        var side = BuildSidePanel();
        Grid.SetColumn(side, 1);
        body.Children.Add(side);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        root.Children.Add(BuildFooter());
        Grid.SetRow(root.Children[^1], 2);

        return root;
    }

    private Control BuildHeader()
    {
        var title = new TextBlock
        {
            Text = T("app.title"),
            Foreground = B(CInk),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        };
        var sub = new TextBlock
        {
            Text = T("app.subtitle"),
            Foreground = B(CInkFaint),
            FontSize = 10.5
        };
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(title);
        stack.Children.Add(sub);

        return new Border
        {
            Background = B(CPanel),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 11),
            Child = stack
        };
    }

    private Control BuildSidePanel()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(12, 0, 0, 0) };

        panel.Children.Add(Section(T("side.features")));

        foreach (var (titleKey, descKey) in new[]
                 {
                     ("feature.peak", "feature.peakDesc"),
                     ("feature.ridge", "feature.ridgeDesc"),
                     ("feature.valley", "feature.valleyDesc"),
                     ("feature.saddle", "feature.saddleDesc"),
                     ("feature.cliff", "feature.cliffDesc")
                 })
        {
            panel.Children.Add(FeatureRow(T(titleKey), T(descKey)));
        }

        panel.Children.Add(Section(T("side.rules")));
        panel.Children.Add(Bullet(T("rule.interval")));
        panel.Children.Add(Bullet(T("rule.dense")));
        panel.Children.Add(Bullet(T("rule.index")));
        panel.Children.Add(Bullet(T("rule.height")));

        var scroll = new ScrollViewer
        {
            Content = new Border
            {
                Background = B(CPanel),
                BorderBrush = B(CLine),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12),
                Child = panel
            },
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        return scroll;
    }

    private Control Section(string text) => new TextBlock
    {
        Text = text,
        Foreground = B(CInkDim),
        FontSize = 11.5,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 4, 0, 0)
    };

    private Control Bullet(string text) => new TextBlock
    {
        Text = "•  " + text,
        Foreground = B(CInkDim),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 17
    };

    private Control FeatureRow(string name, string desc)
    {
        var title = new TextBlock
        {
            Text = name,
            Foreground = B(CWarn),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        };
        var d = new TextBlock
        {
            Text = desc,
            Foreground = B(CInkFaint),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 16
        };
        var stack = new StackPanel { Spacing = 1 };
        stack.Children.Add(title);
        stack.Children.Add(d);
        return stack;
    }

    private Control BuildFooter()
    {
        _readout = new TextBlock
        {
            Text = T("footer.hint"),
            Foreground = B(CInkDim),
            FontSize = 11.5
        };
        _featureText = new TextBlock
        {
            Text = "",
            Foreground = B(CGood),
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(_readout);
        Grid.SetColumn(_featureText, 1);
        row.Children.Add(_featureText);

        return new Border
        {
            Background = B(CPanel),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 9),
            Child = row
        };
    }

    private void OnHover(double u, double v, float height, TerrainFeature feature)
    {
        if (_readout == null || _featureText == null) return;

        var relative = height - _field.MinHeight;
        _readout.Text = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            T("footer.readout"), (int)height, (int)relative);

        _featureText.Text = FeatureName(feature);
        _featureText.Foreground = B(feature switch
        {
            TerrainFeature.Peak => Color.Parse("#FFD166"),
            TerrainFeature.Cliff => Color.Parse("#FF7B7B"),
            TerrainFeature.Saddle => Color.Parse("#C08CFF"),
            TerrainFeature.Ridge => Color.Parse("#5FD69B"),
            TerrainFeature.Valley => Color.Parse("#5BC8FF"),
            _ => CInkDim
        });
    }

    private string FeatureName(TerrainFeature f) => f switch
    {
        TerrainFeature.Peak => T("feature.peak"),
        TerrainFeature.Ridge => T("feature.ridge"),
        TerrainFeature.Valley => T("feature.valley"),
        TerrainFeature.Saddle => T("feature.saddle"),
        TerrainFeature.Cliff => T("feature.cliff"),
        TerrainFeature.Slope => T("feature.slope"),
        _ => ""
    };
}
