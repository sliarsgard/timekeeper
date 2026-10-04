using CommunityToolkit.Mvvm.ComponentModel;

namespace Timekeeper.App.ViewModels;

/// <summary>A page in the sidebar.</summary>
/// <param name="icon">A Segoe MDL2 Assets glyph.</param>
public abstract class PageViewModel(string title, string icon) : ObservableObject
{
    public string Title { get; } = title;

    public string Icon { get; } = icon;

    /// <summary>Called each time the page is shown, so it can pick up changes made elsewhere.</summary>
    public virtual void OnActivated()
    {
    }
}
