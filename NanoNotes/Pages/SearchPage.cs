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
/// with each row labelled by where it came from. With an empty box it lists
/// everything, so the page is useful before typing.
/// </summary>
internal sealed partial class SearchPage : DynamicListPage
{
    private readonly NoteStore _notes = NoteStore.Instance;
    private readonly ClipboardStore _clipboard = ClipboardStore.Instance;
    private bool _suppressRefresh;

    public SearchPage()
    {
        Name = "Open";
        Title = "Search";
        Icon = NotesIcons.Search;
        PlaceholderText = "Search notes and clipboard history";
        ShowDetails = true;

        _notes.Changed += OnStoreChanged;
        _clipboard.Changed += OnStoreChanged;
    }

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        if (_suppressRefresh)
        {
            // The delete action leaves a tombstone in place of the row. Rebuilding
            // the list here would send the selection back to the first item.
            _suppressRefresh = false;
            return;
        }

        RaiseItemsChanged();
    }

    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        var query = (SearchText ?? string.Empty).Trim();
        var matches = new List<(DateTimeOffset When, IListItem Item)>();

        foreach (var note in _notes.All())
        {
            if (query.Length == 0 || note.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((note.Updated, ItemFactory.ForNote(note, "Note", () => _suppressRefresh = true)));
            }
        }

        foreach (var entry in _clipboard.All())
        {
            if (query.Length == 0 || ClipboardPage.Matches(entry, query))
            {
                matches.Add((entry.Created, ItemFactory.ForClipboard(entry, "Clipboard", () => _suppressRefresh = true)));
            }
        }

        if (matches.Count == 0)
        {
            return query.Length == 0 ? [EmptyItem()] : [NoResultsItem(query)];
        }

        return matches
            .OrderByDescending(m => m.When)
            .Select(m => m.Item)
            .ToArray();
    }

    private static ListItem EmptyItem() => new(new NoOpCommand())
    {
        Title = "Nothing to search yet",
        Subtitle = "Your notes and copied items will appear here",
        Icon = NotesIcons.Search,
    };

    private static ListItem NoResultsItem(string query) => new(new NoOpCommand())
    {
        Title = "No matches",
        Subtitle = $"Nothing in notes or clipboard history matches \"{query}\"",
        Icon = NotesIcons.Search,
    };
}
