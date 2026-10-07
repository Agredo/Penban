// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using CommunityToolkit.Mvvm.ComponentModel;

namespace Penban.ViewModels;

/// <summary>
/// One label offered by the filter row above the projects list. A label becomes a chip as soon as a
/// project carries it, and the chip stays for as long as it does - so the row shows what the projects
/// are actually labelled with rather than a list kept somewhere else.
/// </summary>
public partial class TagFilterViewModel : ObservableObject
{
    public TagFilterViewModel(string tag)
    {
        Tag = tag;
    }

    /// <summary>The label itself, spelled the way the project that carries it first spelled it.</summary>
    public string Tag { get; }

    /// <summary>Whether the list is currently narrowed down to this label.</summary>
    [ObservableProperty]
    private bool isSelected;
}
