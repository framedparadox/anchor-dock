using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DockGx.Models;

public enum DockItemKind
{
    Application, // .exe (or any launchable app)
    File,
    Folder,
    WebLink,
    Separator,
}

/// <summary>
/// One entry in the dock. Serializable data lives on the public settable properties;
/// the resolved <see cref="IconImage"/> is a runtime-only visual and is not persisted.
/// </summary>
public sealed class DockItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DockItemKind Kind { get; set; } = DockItemKind.Application;

    private string _displayName = "";
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    /// <summary>File-system path for apps/files/folders, or a URL for web links.</summary>
    public string Target { get; set; } = "";

    /// <summary>Optional command-line arguments (apps only).</summary>
    public string? Arguments { get; set; }

    /// <summary>Optional path to a user-supplied icon overriding the shell icon.</summary>
    public string? CustomIconPath { get; set; }

    // ---- Runtime-only visual state (never serialized) ----------------------

    private ImageSource? _iconImage;

    [JsonIgnore]
    public ImageSource? IconImage
    {
        get => _iconImage;
        set
        {
            _iconImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImageVisibility));
            OnPropertyChanged(nameof(GlyphVisibility));
        }
    }

    /// <summary>Fallback Segoe Fluent glyph shown when no bitmap icon is available.</summary>
    [JsonIgnore]
    public string Glyph => Kind switch
    {
        DockItemKind.WebLink => "",   // Globe
        DockItemKind.Folder => "",    // Folder
        DockItemKind.Separator => "",
        _ => "",                       // Page (generic file/app fallback)
    };

    [JsonIgnore]
    public Visibility ImageVisibility => _iconImage is null ? Visibility.Collapsed : Visibility.Visible;

    [JsonIgnore]
    public Visibility GlyphVisibility => _iconImage is null ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public bool IsSeparator => Kind == DockItemKind.Separator;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
