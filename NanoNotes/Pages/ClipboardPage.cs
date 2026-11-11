// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// The clipboard history: type to filter, pick an entry to copy it.
/// </summary>
internal sealed partial class ClipboardPage : DynamicListPage
{
    private const int MaxTitleLength = 90;

    private readonly ClipboardStore _store = ClipboardStore.Instance;
    private readonly SettingsManager _settings = SettingsManager.Instance;

    public ClipboardPage()
    {
        Name = "Open";
        Title = "Clipboard history";
        Icon = NotesIcons.History;
        PlaceholderText = "Search clipboard history";
        ShowDetails = true;

        _store.Changed += OnStoreChanged;
        _settings.Settings.SettingsChanged += OnSettingsChanged;
    }

    private void OnStoreChanged(object? sender, EventArgs e) => RaiseItemsChanged();

    private void OnSettingsChanged(object? sender, Settings e)
    {
        // A shorter retention window can drop entries immediately.
        _store.Prune(_settings.RetentionDays);
        RaiseItemsChanged();
    }

    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        var query = (SearchText ?? string.Empty).Trim();
        var entries = _store.All();

        if (query.Length == 0)
        {
            return entries.Count == 0 ? [EmptyItem()] : entries.Select(e => ItemFactory.ForClipboard(e)).ToArray();
        }

        return entries.Where(e => Matches(e, query)).Select(e => ItemFactory.ForClipboard(e)).ToArray();
    }

    internal static bool Matches(ClipboardEntry entry, string query)
    {
        if (entry.Kind == ClipboardEntryKind.Text)
        {
            return (entry.Text ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        // Images have no text to search, so match the word "image" or the file name.
        // Use only the name: the storage path itself contains "Notes" and would match everything.
        var fileName = System.IO.Path.GetFileName(entry.ImagePath ?? string.Empty);
        return "image".Contains(query, StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
    internal static string Headline(string text)
    {
        var trimmed = text.Trim();
        var newline = trimmed.IndexOf('\n');
        var firstLine = (newline >= 0 ? trimmed[..newline] : trimmed).Trim();

        if (firstLine.Length == 0)
        {
            firstLine = trimmed;
        }

        return firstLine.Length <= MaxTitleLength
            ? firstLine
            : firstLine[..(MaxTitleLength - 1)].TrimEnd() + "\u2026";
    }

    private static ListItem EmptyItem() => new(new NoOpCommand())
    {
        Title = "Nothing copied yet",
        Subtitle = "Text and images you copy will appear here",
        Icon = NotesIcons.Clipboard,
    };
}
