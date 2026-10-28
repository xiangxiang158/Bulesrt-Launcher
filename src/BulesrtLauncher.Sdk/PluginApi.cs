using System;
using System.Collections.Generic;
using System.Windows;

namespace Bulesrt.Sdk;

/// <summary>
/// The whole plugin surface. Compile against BulesrtLauncher.Sdk.dll, drop the dll +
/// a plugin.json into the launcher's <c>plugins\</c> folder, and the host will load it.
/// </summary>
public interface IPlugin
{
    /// <summary>Called once at startup, on the UI thread.</summary>
    void Initialize(IPluginHost host);

    /// <summary>Called when the launcher is closing. Do not block for long.</summary>
    void Shutdown();
}

/// <summary>What a plugin can ask the launcher to do.</summary>
public interface IPluginHost
{
    /// <summary>Launcher version, e.g. "2.0.0".</summary>
    string Version { get; }

    /// <summary>Absolute path of the launcher's data folder (data.json lives here).</summary>
    string DataDirectory { get; }

    /// <summary>Absolute path of the folder the plugin was loaded from.</summary>
    string PluginDirectory { get; }

    /// <summary>"dark" or "light".</summary>
    string Theme { get; }

    /// <summary>Every library item, in display order.</summary>
    IReadOnlyList<IItemRef> Items { get; }

    /// <summary>Category ids currently defined (misc, tools, …).</summary>
    IReadOnlyList<string> Categories { get; }

    /// <summary>Adds a library item; returns null when the target is empty or duplicated.</summary>
    IItemRef? AddItem(string name, string target, string categoryId = "misc");

    /// <summary>Removes an item by id. Returns false when it does not exist.</summary>
    bool RemoveItem(int id);

    /// <summary>Bottom-right toast.</summary>
    void Toast(string message);

    /// <summary>Status-bar text.</summary>
    void Status(string message);

    /// <summary>Appends a line to data\plugins.log.</summary>
    void Log(string message);

    /// <summary>Registers a command; it shows up in Ctrl+K search and in 插件命令.</summary>
    void RegisterCommand(IPluginCommand command);

    /// <summary>Registers an action appended to an item's right-click menu.</summary>
    void RegisterItemAction(IPluginItemAction action);

    /// <summary>Registers a page; it appears in the sidebar under the categories.</summary>
    void RegisterPage(IPluginPage page);

    /// <summary>Raised for every launch; set <see cref="LaunchEvent.Cancel"/> to stop it.</summary>
    event Action<LaunchEvent>? Launching;

    /// <summary>Raised after the library changed outside the plugin (add/remove/rename).</summary>
    event Action? ItemsChanged;

    /// <summary>Asks the shell to repaint the grid/list.</summary>
    void Refresh();
}

/// <summary>Read-only view of a library item.</summary>
public interface IItemRef
{
    int Id { get; }
    string Name { get; }
    string Target { get; }
    string CategoryId { get; }
}

/// <summary>A command contributed by a plugin.</summary>
public interface IPluginCommand
{
    string Title { get; }
    string Description { get; }
    void Execute(IPluginHost host);
}

/// <summary>An extra entry in an item's context menu.</summary>
public interface IPluginItemAction
{
    string Title { get; }

    /// <summary>Return false to hide the action for that item.</summary>
    bool AppliesTo(IItemRef item);

    void Execute(IItemRef item, IPluginHost host);
}

/// <summary>
/// A full page contributed by a plugin: a sidebar entry plus its content. The page is built
/// lazily — <see cref="CreateView"/> is called once, the first time the user opens it.
/// </summary>
public interface IPluginPage
{
    /// <summary>Stable key, e.g. "hello-stats". Two pages must not share it.</summary>
    string Id { get; }

    /// <summary>Sidebar label.</summary>
    string Title { get; }

    /// <summary>Segoe Fluent Icons glyph, e.g. "\uE8B7". Empty falls back to a puzzle piece.</summary>
    string Icon { get; }

    /// <summary>Builds the page content on the UI thread; the returned element is the page root.</summary>
    FrameworkElement CreateView(IPluginHost host);
}

/// <summary>Passed to <see cref="IPluginHost.Launching"/> before the process starts.</summary>
public sealed class LaunchEvent
{
    public LaunchEvent(int itemId, string name, string target, bool admin)
    {
        ItemId = itemId;
        Name = name;
        Target = target;
        Admin = admin;
    }

    public int ItemId { get; }
    public string Name { get; }
    public string Target { get; }
    public bool Admin { get; }

    /// <summary>Set to true to abort the launch (the plugin should explain why via Toast).</summary>
    public bool Cancel { get; set; }
}
