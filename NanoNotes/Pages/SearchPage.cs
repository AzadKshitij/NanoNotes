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
        ShowDetails = true;

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
            matches.Add((note.Updated, ItemFactory.ForNote(note, "Note")));
        }

        foreach (var entry in _clipboard.All().Where(e => ClipboardPage.Matches(e, query)))
        {
            matches.Add((entry.Created, ItemFactory.ForClipboard(entry, "Clipboard")));
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
}
