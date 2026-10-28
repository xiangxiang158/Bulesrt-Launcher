namespace BulesrtLauncher.Models;

/// <summary>Persisted preferences (data.json -> Settings).</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "dark";
    public string View { get; set; } = "grid";
    public bool HideAfterLaunch { get; set; } = true;
    public bool CloseToTray { get; set; }
    public bool Autostart { get; set; }
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public int Scale { get; set; } = 100;
    public bool SoftwareRender { get; set; }

    /// <summary>File name (inside data\background) of the custom backdrop, empty = built-in navy.</summary>
    public string BackgroundImage { get; set; } = "";

    /// <summary>
    /// False on a brand-new profile: the first run drops the bundled blue artwork in as the default
    /// wallpaper, then sets this. Choosing 内置深蓝 also sets it, so it never comes back on its own.
    /// </summary>
    public bool BackdropChosen { get; set; }

    /// <summary>Backdrop blur radius in px (0–60).</summary>
    public double BackgroundBlur { get; set; } = 18;

    /// <summary>How much the backdrop is darkened behind the UI (0–0.85).</summary>
    public double BackgroundDim { get; set; } = 0.35;

    /// <summary>How much panels/cards let the backdrop through (0 = opaque, 0.85 = glassy).</summary>
    public double SurfaceOpacity { get; set; }

    /// <summary>Whole-window transparency so the desktop shows through (0 = solid, 0.4 = very glassy).</summary>
    public double WindowTransparency { get; set; }

    /// <summary>Show which items are currently running (and how many instances).</summary>
    public bool ShowRunning { get; set; } = true;

    /// <summary>Play animated GIF backdrops.</summary>
    public bool BackdropAnimation { get; set; } = true;

    /// <summary>ask | tray | exit — what the X button does.</summary>
    public string CloseAction { get; set; } = "ask";

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
