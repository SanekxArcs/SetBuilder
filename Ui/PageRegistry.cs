using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace TunerFest.Ui;

/// <summary>One navigation entry: the page title, the class-name stem and the pane icon.</summary>
public sealed record PageEntry(string Title, string Name, SymbolRegular Icon);

/// <summary>A constructed page: its view model (null for a placeholder) and the element shown in the content area.</summary>
public sealed class PageHost
{
    public required PageEntry Entry { get; init; }
    public required FrameworkElement View { get; init; }
    public PageViewModel? ViewModel { get; init; }

    /// <summary>Set when the page class exists but could not be constructed; the smoke run treats this as a failure.</summary>
    public string? Error { get; init; }

    public string Title => Entry.Title;
    public SymbolRegular Icon => Entry.Icon;
    public bool IsPlaceholder => ViewModel == null;
}

/// <summary>
/// The six pages in navigation order. Each is created by reflection
/// (TunerFest.Ui.ViewModels.&lt;Name&gt;ViewModel(IShellContext) and TunerFest.Ui.Pages.&lt;Name&gt;Page(vm)),
/// exactly like tools/PagePreview; a missing class becomes a placeholder so the shell runs without the pages.
/// </summary>
public static class PageRegistry
{
    public static readonly IReadOnlyList<PageEntry> Entries =
    [
        new("Збірка", "Build", SymbolRegular.Wrench24),
        new("Зони екрана", "Zones", SymbolRegular.ScreenSearch24),
        new("Керування", "Controls", SymbolRegular.Keyboard24),
        new("Журнали", "Logs", SymbolRegular.DocumentBulletList24),
        new("Сповіщення", "Notifications", SymbolRegular.Alert24),
        new("Аналітика", "Analytics", SymbolRegular.DataBarVertical24),
    ];

    public static List<PageHost> Create(IShellContext shell)
    {
        var assembly = typeof(PageRegistry).Assembly;
        var hosts = new List<PageHost>();
        foreach (var entry in Entries)
        {
            var vmType = assembly.GetType("TunerFest.Ui.ViewModels." + entry.Name + "ViewModel");
            var pageType = assembly.GetType("TunerFest.Ui.Pages." + entry.Name + "Page");
            if (vmType == null || pageType == null)
            {
                hosts.Add(new PageHost { Entry = entry, View = Placeholder(entry.Title, "Сторінка готується") });
                continue;
            }
            try
            {
                var vm = (PageViewModel)Activator.CreateInstance(vmType, shell)!;
                var view = (FrameworkElement)Activator.CreateInstance(pageType, vm)!;
                hosts.Add(new PageHost { Entry = entry, View = view, ViewModel = vm });
            }
            catch (Exception e)
            {
                var error = e is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : e;
                shell.Log($"Сторінка «{entry.Title}» не завантажилась: {error.Message}");
                hosts.Add(
                    new PageHost
                    {
                        Entry = entry,
                        View = Placeholder(entry.Title, "Сторінка не завантажилась: " + error.Message),
                        Error = error.ToString(),
                    }
                );
            }
        }
        return hosts;
    }

    static FrameworkElement Placeholder(string title, string text)
    {
        var body = new StackPanel();
        body.SetResourceReference(FrameworkElement.StyleProperty, "PageBody");
        var heading = new System.Windows.Controls.TextBlock { Text = title };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "PageTitle");
        var note = new System.Windows.Controls.TextBlock { Text = text };
        note.SetResourceReference(FrameworkElement.StyleProperty, "CardDescription");
        body.Children.Add(heading);
        body.Children.Add(note);
        var scroll = new ScrollViewer { Content = body };
        scroll.SetResourceReference(FrameworkElement.StyleProperty, "PageScroll");
        return scroll;
    }
}
