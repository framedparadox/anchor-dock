namespace Anchor.Models;

/// <summary>App-wide visual flags that dock item bindings read without holding a window reference.</summary>
public static class DockItemAnimations
{
    public static bool ReducedMotion { get; set; }

    public static bool ShowLabels { get; set; }

    public static event Action? ShowLabelsChanged;

    public static void SetShowLabels(bool on)
    {
        if (ShowLabels == on)
            return;
        ShowLabels = on;
        ShowLabelsChanged?.Invoke();
    }
}
