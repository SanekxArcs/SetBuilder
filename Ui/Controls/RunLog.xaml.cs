using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace TunerFest.Ui.Controls;

/// <summary>
/// The run log panel. Lines are categorised exactly like the old AppendLog:
/// "HH:mm:ss   " in (124,137,156), the bold category and the text in the category colour.
/// </summary>
public partial class RunLog : UserControl
{
    public const int MinFontSize = 9,
        MaxFontSize = 18;

    static readonly Brush Timestamp = Frozen(Color.FromRgb(124, 137, 156));
    static readonly Brush DoneBrush = Frozen(Color.FromRgb(134, 218, 164));
    static readonly Brush StopBrush = Frozen(Color.FromRgb(255, 159, 139));
    static readonly Brush DefaultDark = Frozen(Color.FromRgb(203, 208, 218));
    static readonly Brush DefaultLight = Frozen(Color.FromRgb(52, 58, 70));
    static readonly Brush DoneLight = Frozen(Color.FromRgb(22, 128, 72));
    static readonly Brush StopLight = Frozen(Color.FromRgb(196, 60, 30));
    static readonly Brush TimestampLight = Frozen(Color.FromRgb(110, 120, 138));

    int fontSize = 11;
    bool dark = true;

    public RunLog() => InitializeComponent();

    /// <summary>Font size in points, like Profile.LogFontSize (9..18). The document uses the same visual size in DIPs.</summary>
    public int LogFontSize
    {
        get => fontSize;
        set
        {
            fontSize = Math.Clamp(value, MinFontSize, MaxFontSize);
            Events.FontSize = fontSize * 96.0 / 72.0;
            Document.FontSize = Events.FontSize;
        }
    }

    public bool FollowLog
    {
        get => Follow.IsChecked == true;
        set => Follow.IsChecked = value;
    }

    public bool IsCollapsed { get; private set; }
    public int LineCount => Document.Blocks.Count;
    public string Text => new TextRange(Document.ContentStart, Document.ContentEnd).Text;

    /// <summary>Raised when A− or A+ changed the size; the host persists it.</summary>
    public event Action<int>? FontSizeChanged;

    /// <summary>Raised when the chevron collapsed or expanded the log body; the host resizes its row.</summary>
    public event Action<bool>? CollapsedChanged;

    public void SetDarkTheme(bool isDark) => dark = isDark;

    public static string Categorize(string text) =>
        text.StartsWith('✓') || text.StartsWith("Готово") ? "ГОТОВО"
        : text.Contains("Зупинено") || text.Contains("Помилка") ? "ЗУПИНКА"
        : text.StartsWith("Кидок") ? "КИДОК"
        : text.StartsWith("Збережено:") ? "РЕЗУЛЬТАТ"
        : text.Contains("знайдено у слоті") ? "ПЕРК"
        : "ПОДІЯ";

    public void Append(string text) => Append(text, DateTime.Now);

    public void Append(string text, DateTime at)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Append(text, at));
            return;
        }
        string kind = Categorize(text);
        var paragraph = new Paragraph { KeepTogether = true };
        paragraph.Inlines.Add(new Run($"{at:HH:mm:ss}   ") { Foreground = dark ? Timestamp : TimestampLight });
        var category = new Run(kind + "  ·  ") { FontWeight = FontWeights.Bold };
        var body = new Run(text);
        Colour(category, kind);
        Colour(body, kind);
        paragraph.Inlines.Add(category);
        paragraph.Inlines.Add(body);
        bool hadSelection = !Events.Selection.IsEmpty;
        Document.Blocks.Add(paragraph);
        if (FollowLog && !hadSelection)
            Events.ScrollToEnd();
    }

    void Colour(Run run, string kind)
    {
        switch (kind)
        {
            case "ГОТОВО":
                run.Foreground = dark ? DoneBrush : DoneLight;
                break;
            case "ЗУПИНКА":
                run.Foreground = dark ? StopBrush : StopLight;
                break;
            case "КИДОК":
            case "ПЕРК":
                run.SetResourceReference(TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
                break;
            default:
                run.Foreground = dark ? DefaultDark : DefaultLight;
                break;
        }
    }

    public void Clear() => Document.Blocks.Clear();

    void OnCopy(object sender, RoutedEventArgs e)
    {
        string text = Text;
        if (string.IsNullOrWhiteSpace(text))
            return;
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception) { }
    }

    void OnClear(object sender, RoutedEventArgs e) => Clear();

    void OnFontDown(object sender, RoutedEventArgs e) => Step(-1);

    void OnFontUp(object sender, RoutedEventArgs e) => Step(1);

    public void Step(int delta)
    {
        LogFontSize = fontSize + delta;
        FontSizeChanged?.Invoke(fontSize);
    }

    void OnToggleCollapsed(object sender, RoutedEventArgs e) => SetCollapsed(!IsCollapsed);

    public void SetCollapsed(bool collapsed)
    {
        IsCollapsed = collapsed;
        Events.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapseButton.Icon = new SymbolIcon(collapsed ? SymbolRegular.ChevronUp24 : SymbolRegular.ChevronDown24);
        CollapseButton.ToolTip = collapsed ? "Розгорнути журнал" : "Згорнути журнал";
        CollapsedChanged?.Invoke(collapsed);
    }

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
