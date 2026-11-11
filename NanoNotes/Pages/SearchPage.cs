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
/// Searches the notes and the clipboard history in one list, newest match first,
/// with each row labelled by where it came from.
/// </summary>
internal sealed partial class SearchPage : DynamicListPage
{
    private readonly NoteStore _notes = NoteStore.Instance;
    private readonly ClipboardStore _clipboard = ClipboardStore.Instance;

    public SearchPage()
    {
        Name = "Open";
        Title = "Search";
        Icon = NotesIcons.Search;
        PlaceholderText = "Search notes and clipboard history";

        _notes.Changed += OnStoreChanged;
        _clipboard.Changed += OnStoreChanged;
    }

    private void OnStoreChanged(object? sender, EventArgs e) => RaiseItemsChanged();

    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        var query = (SearchText ?? string.Empty).Trim();

        if (query.Length == 0)
        {
            return [HintItem()];
        }

        var matches = new List<(DateTimeOffset When, IListItem Item)>();

        foreach (var note in _notes.All().Where(n => n.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            matches.Add((note.Updated, NoteItem(note)));
        }

        foreach (var entry in _clipboard.All().Where(e => ClipboardPage.Matches(e, query)))
        {
            matches.Add((entry.Created, ClipboardItem(entry)));
        }

        if (matches.Count == 0)
        {
            return [NoResultsItem(query)];
        }

        return matches
            .OrderByDescending(m => m.When)
            .Select(m => m.Item)
            .ToArray();
    }

    private static ListItem HintItem() => new(new NoOpCommand())
    {
        Title = "Type to search both lists",
        Subtitle = "One box searches your notes and your clipboard history at the same time",
        Icon = NotesIcons.Search,
    };

    private static ListItem NoResultsItem(string query) => new(new NoOpCommand())
    {
        Title = "No matches",
        Subtitle = $"Nothing in notes or clipboard history matches \"{query}\"",
        Icon = NotesIcons.Search,
    };

    private static ListItem NoteItem(Note note)
    {
        var when = Humanize.RelativeTime(note.Updated);
        var lineCount = note.Text.Trim().Split('\n').Length;

        return new ListItem(new NoteDetailPage(note.Id))
        {
            Title = notesPage.Headline(note.Text),
            Subtitle = lineCount > 1
                ? $"Note  \u00b7  {when}  \u00b7  {lineCount} lines"
                : $"Note  \u00b7  {when}",
            Icon = NotesIcons.Note,
        };
    }

    private static ListItem ClipboardItem(ClipboardEntry entry)
    {
        var when = Humanize.RelativeTime(entry.Created);

        if (entry.Kind == ClipboardEntryKind.Image)
        {
            return new ListItem(new ClipboardDetailPage(entry.Id))
            {
                Title = "Image",
                Subtitle = $"Clipboard  \u00b7  {entry.Width}x{entry.Height}  \u00b7  {when}",
                Icon = string.IsNullOrEmpty(entry.ImagePath)
                    ? NotesIcons.Image
                    : new IconInfo(entry.ImagePath),
            };
        }

        return new ListItem(new ClipboardDetailPage(entry.Id))
        {
            Title = ClipboardPage.Headline(entry.Text ?? string.Empty),
            Subtitle = $"Clipboard  \u00b7  {when}",
            Icon = NotesIcons.Clipboard,
        };
    }
}
