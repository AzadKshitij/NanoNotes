// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// Shows one note in full, with copy and delete at the bottom.
/// </summary>
internal sealed partial class NoteDetailPage : ContentPage
{
    private readonly NoteStore _store = NoteStore.Instance;
    private readonly string _noteId;

    public NoteDetailPage(string noteId)
    {
        _noteId = noteId;

        Name = "Open";
        Icon = NotesIcons.Note;

        var note = _store.Find(noteId);
        var text = note?.Text ?? string.Empty;
        Title = note is null ? "Note" : notesPage.Headline(text);

        Commands =
        [
            new CommandContextItem(new CopyTextCommand(text)
            {
                Result = CommandResult.ShowToast(new ToastArgs { Message = "Note copied" }),
            })
            {
                Title = "Copy note",
                Icon = NotesIcons.Copy,
            },
            new CommandContextItem(new AnonymousCommand(() => _ = _store.Delete(_noteId))
            {
                Name = "Delete note",
                Result = CommandResult.GoBack(),
            })
            {
                Title = "Delete note",
                Subtitle = "Remove this note for good",
                Icon = NotesIcons.Delete,
                IsCritical = true,
            },
        ];
    }

    public override IContent[] GetContent()
    {
        var note = _store.Find(_noteId);

        if (note is null)
        {
            return [new MarkdownContent("_This note no longer exists._")];
        }

        return
        [
            new PlainTextContent(note.Text) { WrapWords = true },
            new MarkdownContent($"\n\n---\n\n_Saved {note.Created.ToLocalTime():ddd d MMM yyyy, HH:mm}_"),
        ];
    }
}
