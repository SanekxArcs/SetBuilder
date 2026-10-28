using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TunerFest.Ui.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A value with a display label, for selectors bound to codes or enums.</summary>
public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
