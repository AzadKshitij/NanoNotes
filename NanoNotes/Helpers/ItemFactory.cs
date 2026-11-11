// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// One place that builds the rows shared by the notes, clipboard and search
/// pages: the truncated title on the left, the full item on the right, and
/// Enter (or a click) to copy.
/// </summary>
internal static class ItemFactory
{
    public static ListItem ForNote(Note note, string? source = null, Action? onDelete = null)
    {
        var text = note.Text ?? string.Empty;
        var lineCount = text.Trim().Split('\n').Length;
        var subtitle = lineCount > 1
            ? $"{Humanize.RelativeTime(note.Updated)}  \u00b7  {lineCount} lines"
            : Humanize.RelativeTime(note.Updated);

        if (source is not null)
        {
            subtitle = $"{source}  \u00b7  {subtitle}";
        }

        var copy = new CopyTextCommand(text)
        {
            Icon = NotesIcons.Copy,
            Result = CommandResult.ShowToast(new ToastArgs { Message = "Note copied" }),
        };

        var item = new ListItem(copy)
        {
            Title = notesPage.Headline(text),
            Subtitle = subtitle,
            Icon = NotesIcons.Note,
            Details = DetailsPreview.ForNote(note),
        };

        item.MoreCommands =
        [
            new CommandContextItem(DeleteNote(note, item, onDelete))
            {
                Title = "Delete note",
                Subtitle = "Remove this note",
                Icon = NotesIcons.DeleteGlyph,
                IsCritical = true,
            },
        ];

        return item;
    }

    public static ListItem ForClipboard(ClipboardEntry entry, string? source = null, Action? onDelete = null)
    {
        var subtitle = Humanize.RelativeTime(entry.Created);
        if (source is not null)
        {
            subtitle = $"{source}  \u00b7  {subtitle}";
        }

        if (entry.Kind == ClipboardEntryKind.Image)
        {
            var icon = string.IsNullOrEmpty(entry.ImagePath)
                ? NotesIcons.Image
                : new IconInfo(entry.ImagePath);

            var copy = new AnonymousCommand(() => CopyImage(entry))
            {
                Name = "Copy image",
                Icon = NotesIcons.Image,
                Result = CommandResult.ShowToast(new ToastArgs { Message = "Image copied to clipboard" }),
            };

            var item = new ListItem(copy)
            {
                Title = "Image",
                Subtitle = $"{entry.Width}x{entry.Height}  \u00b7  {subtitle}",
                Icon = icon,
                Details = DetailsPreview.ForClipboard(entry),
            };

            item.MoreCommands = [DeleteItem(entry, item, onDelete)];
            return item;
        }

        var text = entry.Text ?? string.Empty;
        var copyText = new CopyTextCommand(text)
        {
            Icon = NotesIcons.Copy,
            Result = CommandResult.ShowToast(new ToastArgs { Message = "Copied to clipboard" }),
        };

        var row = new ListItem(copyText)
        {
            Title = ClipboardPage.Headline(text),
            Subtitle = subtitle,
            Icon = NotesIcons.Clipboard,
            Details = DetailsPreview.ForClipboard(entry),
        };

        row.MoreCommands = [DeleteItem(entry, row, onDelete), SaveAsNoteCommand(entry)];
        return row;
    }

    /// <summary>
    /// Deletes the note but keeps its row in place as a tombstone. Removing the
    /// row right away makes the host fall back to the first item, which is
    /// disorienting; the row disappears on the next refresh.
    /// </summary>
    private static AnonymousCommand DeleteNote(Note note, ListItem item, Action? onDelete) => new(() =>
    {
        onDelete?.Invoke();
        _ = NoteStore.Instance.Delete(note.Id);
        MarkDeleted(item, "note");
    })
    {
        Name = "Delete note",
        Icon = NotesIcons.DeleteGlyph,
        Result = CommandResult.ShowToast(new ToastArgs
        {
            Message = "Note deleted",
            Result = CommandResult.KeepOpen(),
        }),
    };

    private static CommandContextItem DeleteItem(ClipboardEntry entry, ListItem item, Action? onDelete) => new(new AnonymousCommand(() =>
    {
        onDelete?.Invoke();
        _ = ClipboardStore.Instance.Delete(entry.Id);
        MarkDeleted(item, "item");
    })
    {
        Name = "Delete item",
        Icon = NotesIcons.DeleteGlyph,
        Result = CommandResult.ShowToast(new ToastArgs
        {
            Message = "Item deleted",
            Result = CommandResult.KeepOpen(),
        }),
    })
    {
        Title = "Delete item",
        Subtitle = "Remove this entry from the history",
        Icon = NotesIcons.DeleteGlyph,
        IsCritical = true,
    };

    private static void MarkDeleted(ListItem item, string what)
    {
        item.Title = what == "note" ? "Deleted note" : "Deleted item";
        item.Subtitle = string.Empty;
        item.Command = new NoOpCommand();
        item.Details = new Details
        {
            Title = "Deleted",
            Body = $"_This {what} was deleted._",
            Size = ContentSize.Medium,
        };
        item.MoreCommands = [];
    }

    private static CommandContextItem SaveAsNoteCommand(ClipboardEntry entry)
    {
        AnonymousCommand? save = null;
        save = new AnonymousCommand(() =>
        {
            var text = entry.Text ?? string.Empty;
            if (text.Trim().Length == 0)
            {
                save!.Result = CommandResult.ShowToast(new ToastArgs { Message = "This clip has no text to save" });
                return;
            }

            try
            {
                NoteStore.Instance.Add(text);
                save!.Result = CommandResult.ShowToast(new ToastArgs
                {
                    Message = "Saved to notes",
                    Result = CommandResult.KeepOpen(),
                });
            }
            catch (Exception ex)
            {
                save!.Result = CommandResult.ShowToast(new ToastArgs { Message = "Could not save note: " + ex.Message });
            }
        })
        {
            Name = "Save as note",
            Icon = NotesIcons.AddGlyph,
        };

        return new CommandContextItem(save)
        {
            Title = "Save as note",
            Subtitle = "Add this clip to your notes",
            Icon = NotesIcons.AddGlyph,
        };
    }

    private static void CopyImage(ClipboardEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.ImagePath))
        {
            ClipboardWriter.SetImage(entry.ImagePath);
        }
    }
}
