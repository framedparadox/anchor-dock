using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Xunit;

namespace Anchor.UITests;

/// <summary>
/// The launch-and-look-at-it tests: does Anchor come up, does the dock render its seeded items,
/// and do the windows it can open actually open. Everything here goes through UI Automation, so
/// it exercises the same tree Narrator sees — a regression in the accessibility surface fails
/// these tests too, which is the point.
/// </summary>
public sealed class DockSmokeTests
{
    /// <summary>The dock Anchor seeds on a genuine first run (see <c>DockWindow.SeedDefaults</c>).</summary>
    private static readonly string[] SeededItems =
    {
        "File Explorer", "Notepad", "Calculator", "Home", "WinUI Gallery",
    };

    [UIFact]
    public void The_dock_window_appears_on_launch()
    {
        using var app = AnchorApp.Launch();

        var dock = app.WaitForDock();

        Assert.Equal(AnchorApp.DockWindowTitle, dock.Name);
        // Borderless and off the taskbar, but still a real, non-empty window on screen.
        Assert.True(dock.BoundingRectangle.Width > 0, "The dock has no width.");
        Assert.True(dock.BoundingRectangle.Height > 0, "The dock has no height.");
    }

    [UIFact]
    public void A_first_run_dock_shows_its_seeded_items_and_the_gear()
    {
        // Every launch here gets an empty ANCHOR_DATA_DIR, so this is always a true first run:
        // the dock seeds its defaults and must render them rather than the empty-state pill.
        using var app = AnchorApp.Launch();
        var dock = app.WaitForDock();

        var buttons = AnchorApp.FindButtons(dock);
        var names = buttons.Select(b => b.Name).ToList();

        // Each item is a real, named, invokable Button — which is what makes the dock keyboard-
        // and screen-reader-usable in the first place.
        foreach (var seeded in SeededItems)
            Assert.Contains(seeded, names);

        Assert.NotNull(AnchorApp.FindById(dock, "AnchorSettingsButton"));
    }

    [UIFact]
    public void The_gear_opens_the_Settings_window_and_its_pages_switch()
    {
        using var app = AnchorApp.Launch();
        var dock = app.WaitForDock();

        AnchorApp.Press(AnchorApp.FindById(dock, "AnchorSettingsButton")!);

        var settings = app.WaitForWindow("Anchor Settings");
        Assert.NotNull(AnchorApp.FindById(settings, "SettingsNavGeneral"));

        // The Apps page is collapsed until its nav item is selected; selecting it has to reveal
        // the page, which is the wiring most likely to rot as the window grows.
        AnchorApp.Press(AnchorApp.FindById(settings, "SettingsNavApps")!);
        Assert.NotNull(AnchorApp.FindButtonByName(settings, "Add new"));

        settings.Close();
        Assert.True(app.WaitForWindowToClose("Anchor Settings"), "The Settings window stayed open.");
    }

    [UIFact]
    public void The_Docks_page_lists_the_one_dock_a_first_run_has()
    {
        // Multi-dock support turned the old per-dock General settings into a page of their own.
        // A fresh profile has exactly one dock, and it has to show up there.
        using var app = AnchorApp.Launch();
        var dock = app.WaitForDock();

        AnchorApp.Press(AnchorApp.FindById(dock, "AnchorSettingsButton")!);
        var settings = app.WaitForWindow("Anchor Settings");

        AnchorApp.Press(AnchorApp.FindById(settings, "SettingsNavDocks")!);
        Assert.NotNull(AnchorApp.FindButtonByName(settings, "Add dock"));

        // Each dock's card carries an editable name, so an edit box is the marker that a card was
        // built at all rather than the list rendering empty.
        var list = AnchorApp.Retry(() =>
        {
            var boxes = settings.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit));
            return boxes.Length > 0 ? boxes : null;
        });
        Assert.NotNull(list);

        settings.Close();
    }

    [UIFact]
    public void The_Appearance_page_offers_the_personalization_controls()
    {
        // Density, accent tint and magnification are app-wide and live on their own page. Finding the
        // page's own automation id proves the nav entry, the tag routing and the panel all line
        // up — the wiring that silently rots whenever a page is added.
        using var app = AnchorApp.Launch();
        var dock = app.WaitForDock();

        AnchorApp.Press(AnchorApp.FindById(dock, "AnchorSettingsButton")!);
        var settings = app.WaitForWindow("Anchor Settings");

        AnchorApp.Press(AnchorApp.FindById(settings, "SettingsNavAppearance")!);
        Assert.NotNull(AnchorApp.FindById(settings, "SettingsAppearancePanel"));

        // A collapsed page is absent from the automation tree entirely, so a combo box being
        // reachable here is proof the Appearance page is the one showing.
        var combos = AnchorApp.Retry(() =>
        {
            var found = settings.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox));
            return found.Length > 0 ? found : null;
        });
        Assert.NotNull(combos);

        settings.Close();
    }

    [UIFact]
    public void The_Shortcuts_page_shows_the_summon_and_search_shortcuts()
    {
        using var app = AnchorApp.Launch();
        var dock = app.WaitForDock();

        AnchorApp.Press(AnchorApp.FindById(dock, "AnchorSettingsButton")!);
        var settings = app.WaitForWindow("Anchor Settings");

        AnchorApp.Press(AnchorApp.FindById(settings, "SettingsNavShortcuts")!);
        Assert.NotNull(AnchorApp.FindById(settings, "SettingsShortcutsPanel"));

        // Both capture buttons are built in code-behind rather than declared in XAML, so this is
        // the check that they were actually added to their cards — and that each announces which
        // shortcut it is for, which is the only thing distinguishing them to a screen reader.
        // The summon shortcut is seeded with Ctrl+Alt+A; the search one starts unassigned.
        Assert.NotNull(AnchorApp.FindButtonByName(settings, "Summon the docks: Ctrl+Alt+A"));
        Assert.NotNull(AnchorApp.FindButtonByName(settings, "Quick-launch search: None"));

        settings.Close();
    }

    [UIFact]
    public void A_group_opens_a_fly_out_bar_of_its_children()
    {
        // The fly-out bar renders its cells imperatively rather than from the strip's template,
        // so nothing else in this suite would catch it failing to build them. A child is only
        // reachable through the bar — it never appears on the strip itself — which makes finding
        // one proof that the bar opened and populated.
        using var app = AnchorApp.Launch();
        var dock = app.WaitForDock();

        var group = CreateGroupWithFileExplorer(app, dock);
        AnchorApp.Press(group);

        var child = AnchorApp.Retry(() => AnchorApp.FindButtonByName(dock, "File Explorer"));
        Assert.NotNull(child);
    }

    [UIFact]
    public void A_group_opens_a_fly_out_bar_on_hover_too()
    {
        // Same fly-out as above, but landed on rather than clicked: the strip's own pointer
        // tracking (DockWindow.TrackStripPointer) opens a group's bar as soon as the cursor is
        // over its icon, so the bar comes up without an Invoke/Click at all.
        using var app = AnchorApp.Launch();
        var dock = app.WaitForDock();

        var group = CreateGroupWithFileExplorer(app, dock);
        FlaUI.Core.Input.Mouse.MoveTo(group.GetClickablePoint());

        var child = AnchorApp.Retry(() => AnchorApp.FindButtonByName(dock, "File Explorer"));
        Assert.NotNull(child);
    }

    /// <summary>
    /// Builds a group holding "File Explorer" through the Settings list — the one route to "move
    /// to group" that does not depend on driving a context menu over the dock strip — and returns
    /// the group's own button on the strip.
    /// </summary>
    private static AutomationElement CreateGroupWithFileExplorer(AnchorApp app, Window dock)
    {
        var notepad = AnchorApp.FindButtonByName(dock, "Notepad");
        Assert.NotNull(notepad);

        AnchorApp.Press(AnchorApp.FindById(dock, "AnchorSettingsButton")!);
        var settings = app.WaitForWindow("Anchor Settings");
        AnchorApp.Press(AnchorApp.FindById(settings, "SettingsNavApps")!);

        var addToGroup = AnchorApp.FindButtonByName(settings, "Move to group");
        Assert.NotNull(addToGroup);
        AnchorApp.Press(addToGroup!);

        var newGroup = AnchorApp.Retry(() => settings.FindFirstDescendant(cf => cf.ByName("New group…")));
        Assert.NotNull(newGroup);
        AnchorApp.Press(newGroup!);

        var create = AnchorApp.FindButtonByName(dock, "Create");
        Assert.NotNull(create);
        AnchorApp.Press(create!);

        settings.Close();

        // The group is now on the strip.
        var group = AnchorApp.FindButtonByName(dock, "Group");
        Assert.NotNull(group);
        return group!;
    }

    [UIFact]
    public void The_dock_survives_opening_and_closing_Settings()
    {
        // Closing a child window has taken the dock with it before now (the dock is the app's
        // only real window but is not its "main" one), so this guards that specifically.
        using var app = AnchorApp.Launch();
        var dock = app.WaitForDock();

        AnchorApp.Press(AnchorApp.FindById(dock, "AnchorSettingsButton")!);
        app.WaitForWindow("Anchor Settings").Close();
        Assert.True(app.WaitForWindowToClose("Anchor Settings"));

        Assert.False(app.Application.HasExited, "Anchor exited when its Settings window closed.");
        Assert.NotNull(app.WaitForDock());
    }
}
