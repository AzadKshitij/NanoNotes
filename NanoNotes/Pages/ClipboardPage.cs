// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// The clipboard history: type to filter, pick an entry to see it in full.
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
            return entries.Count == 0 ? [EmptyItem()] : entries.Select(Item).ToArray();
        }

        return entries.Where(e => Matches(e, query)).Select(Item).ToArray();
    }

    private static bool Matches(ClipboardEntry entry, string query)
    {
        if (entry.Kind == ClipboardEntryKind.Text)
        {
            return (entry.Text ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        // Images have no text to search, so match the word "image" or the file name.
        return "image".Contains(query, StringComparison.OrdinalIgnoreCase)
            || (entry.ImagePath ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);
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

    private ListItem Item(ClipboardEntry entry)
    {
        if (entry.Kind == ClipboardEntryKind.Image)
        {
            return ImageItem(entry);
        }

        var text = entry.Text ?? string.Empty;
        return new ListItem(new ClipboardDetailPage(entry.Id))
        {
            Title = Headline(text),
            Subtitle = RelativeTime(entry.Created),
            Icon = NotesIcons.Clipboard,
            MoreCommands =
            [
                new CommandContextItem(new CopyTextCommand(text)
                {
                    Result = CommandResult.ShowToast(new ToastArgs { Message = "Copied to clipboard" }),
                })
                {
                    Title = "Copy text",
                    Icon = NotesIcons.Copy,
                },
                DeleteCommand(entry),
            ],
        };
    }

    private ListItem ImageItem(ClipboardEntry entry)
    {
        var icon = string.IsNullOrEmpty(entry.ImagePath)
            ? NotesIcons.Image
            : new IconInfo(entry.ImagePath);

        return new ListItem(new ClipboardDetailPage(entry.Id))
        {
            Title = "Image",
            Subtitle = $"{entry.Width}x{entry.Height}  \u00b7  {RelativeTime(entry.Created)}",
            Icon = icon,
            MoreCommands =
            [
                new CommandContextItem(new AnonymousCommand(() => CopyImage(entry))
                {
                    Name = "Copy image",
                    Result = CommandResult.ShowToast(new ToastArgs { Message = "Image copied to clipboard" }),
                })
                {
                    Title = "Copy image",
                    Icon = NotesIcons.Copy,
                },
                DeleteCommand(entry),
            ],
        };
    }

    private CommandContextItem DeleteCommand(ClipboardEntry entry) => new(new AnonymousCommand(() => _ = _store.Delete(entry.Id))
    {
        Name = "Delete item",
        Result = CommandResult.KeepOpen(),
    })
    {
        Title = "Delete item",
        Subtitle = "Remove this entry from the history",
        Icon = NotesIcons.Delete,
        IsCritical = true,
    };

    private static void CopyImage(ClipboardEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ImagePath))
        {
            return;
        }

        ClipboardWriter.SetImage(entry.ImagePath);
    }

    private static string RelativeTime(DateTimeOffset timestamp)
    {
        var delta = DateTimeOffset.Now - timestamp;

        if (delta.TotalMinutes < 1)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes} min ago";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours} h ago";
        }

        if (delta.TotalDays < 7)
        {
            return $"{(int)delta.TotalDays} d ago";
        }

        return timestamp.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }
}
