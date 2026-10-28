namespace BulesrtLauncher.Models;

/// <summary>Pending "add item" payload used by the duplicate-resolution flow.</summary>
public sealed record ItemDraft(string Name, string Target, string CategoryId, string Args, string WorkDir);
