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
/// The notes list: type to filter, type something new and press Enter to save it.
/// </summary>
internal sealed partial class notesPage : DynamicListPage
{
    private const int MaxTitleLength = 90;

    private readonly NoteStore _store = NoteStore.Instance;

    public notesPage()
    {
        Name = "Open";
        Title = "Notes";
        Icon = NotesIcons.Note;
        PlaceholderText = "Search notes, or type a new one and press Enter";

        _store.Changed += OnNotesChanged;
    }

    private void OnNotesChanged(object? sender, EventArgs e) => RaiseItemsChanged();

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        var query = (SearchText ?? string.Empty).Trim();
        var notes = _store.All();

        if (query.Length == 0)
        {
            return notes.Count == 0
                ? [EmptyItem()]
                : notes.Select(NoteItem).ToArray();
        }

        var items = new List<IListItem>();
        items.AddRange(notes
            .Where(n => n.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(NoteItem));

        if (!notes.Any(n => string.Equals(n.Text.Trim(), query, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(NewNoteItem(query));
        }

        return items.ToArray();
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
        Title = "No notes yet",
        Subtitle = "Type anything in the box above and press Enter to save it",
        Icon = NotesIcons.Add,
    };

    private static ListItem NoteItem(Note note) => new(new NoteDetailPage(note.Id))
    {
        Title = Headline(note.Text),
        Subtitle = Describe(note),
        Icon = NotesIcons.Note,
        MoreCommands =
        [
            new CommandContextItem(new CopyTextCommand(note.Text)
            {
                Result = CommandResult.ShowToast(new ToastArgs { Message = "Note copied" }),
            })
            {
                Title = "Copy note",
                Icon = NotesIcons.Copy,
            },
            new CommandContextItem(new AnonymousCommand(() => _ = NoteStore.Instance.Delete(note.Id))
            {
                Name = "Delete note",
                Result = CommandResult.KeepOpen(),
            })
            {
                Title = "Delete note",
                Subtitle = "Remove this note",
                Icon = NotesIcons.Delete,
                IsCritical = true,
            },
        ],
    };

    private static ListItem NewNoteItem(string text)
    {
        AnonymousCommand? save = null;
        save = new AnonymousCommand(() =>
        {
            try
            {
                NoteStore.Instance.Add(text);
                save!.Result = CommandResult.ShowToast(new ToastArgs { Message = "Note saved" });
            }
            catch (Exception ex)
            {
                save!.Result = CommandResult.ShowToast(new ToastArgs { Message = "Could not save note: " + ex.Message });
            }
        })
        {
            Name = "Save note",
            Icon = NotesIcons.Add,
        };

        return new ListItem(save)
        {
            Title = $"Save note: {Headline(text)}",
            Subtitle = "Press Enter to save this as a new note",
            Icon = NotesIcons.Add,
        };
    }

    private static string Describe(Note note)
    {
        var lineCount = note.Text.Trim().Split('\n').Length;
        var when = Humanize.RelativeTime(note.Updated);

        return lineCount > 1 ? $"{when}  \u00b7  {lineCount} lines" : when;
    }
}
