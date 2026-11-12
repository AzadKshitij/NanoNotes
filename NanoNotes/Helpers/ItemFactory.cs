// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
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
            Icon = NotesIcons.CopyGlyph,
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
            PasteCommand(text),
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

            AnonymousCommand? copy = null;
            copy = new AnonymousCommand(() =>
            {
                var message = CopyImage(entry);
                copy!.Result = CommandResult.ShowToast(new ToastArgs { Message = message });
            })
            {
                Name = "Copy image",
                Icon = NotesIcons.CopyGlyph,
            };

            var item = new ListItem(copy)
            {
                Title = "Image",
                Subtitle = $"{entry.Width}x{entry.Height}  \u00b7  {subtitle}",
                Icon = icon,
                Details = DetailsPreview.ForClipboard(entry),
            };

            item.MoreCommands = [PasteImageCommand(entry), DeleteItem(entry, item, onDelete)];
            return item;
        }

        var text = entry.Text ?? string.Empty;
        var copyText = new CopyTextCommand(text)
        {
            Icon = NotesIcons.CopyGlyph,
            Result = CommandResult.ShowToast(new ToastArgs { Message = "Copied to clipboard" }),
        };

        var row = new ListItem(copyText)
        {
            Title = ClipboardPage.Headline(text),
            Subtitle = subtitle,
            Icon = NotesIcons.Clipboard,
            Details = DetailsPreview.ForClipboard(entry),
        };

        row.MoreCommands = [PasteCommand(text), DeleteItem(entry, row, onDelete), SaveAsNoteCommand(entry)];
        return row;
    }

    /// <summary>
    /// Pastes the note into the window the user came from. The palette closes
    /// first, so the text lands in the app and not in the palette's search box.
    /// </summary>
    private static CommandContextItem PasteCommand(string text) => new(new AnonymousCommand(() =>
    {
        try
        {
            PasteIntoApp.PasteText(text);
        }
        catch (Exception ex)
        {
            ExtensionHost.LogMessage("notes paste failed: " + ex.Message);
        }
    })
    {
        Name = "Paste",
        Icon = NotesIcons.PasteGlyph,
        Result = CommandResult.Dismiss(),
    })
    {
        Title = "Paste",
        Subtitle = "Paste this note into the app you came from",
        Icon = NotesIcons.PasteGlyph,
    };

    /// <summary>
    /// Pastes the picture into the window the user came from.
    /// </summary>
    private static CommandContextItem PasteImageCommand(ClipboardEntry entry) => new(new AnonymousCommand(() =>
    {
        try
        {
            if (string.IsNullOrEmpty(entry.ImagePath) || !File.Exists(entry.ImagePath))
            {
                return;
            }

            PasteIntoApp.PasteImage(entry.ImagePath);
        }
        catch (Exception ex)
        {
            ExtensionHost.LogMessage("notes paste failed: " + ex.Message);
        }
    })
    {
        Name = "Paste",
        Icon = NotesIcons.PasteGlyph,
        Result = CommandResult.Dismiss(),
    })
    {
        Title = "Paste",
        Subtitle = "Paste this picture into the app you came from",
        Icon = NotesIcons.PasteGlyph,
    };

    /// <summary>
    /// Deletes the note but keeps its row in place as a tombstone. Removing the
    /// row right away makes the host fall back to the first item, which is
    /// disorienting; the row disappears on the next refresh.
    /// </summary>
    private static AnonymousCommand DeleteNote(Note note, ListItem item, Action? onDelete)
    {
        AnonymousCommand? delete = null;
        delete = new AnonymousCommand(() =>
        {
            try
            {
                // The page has to know before the store changes, so it can keep
                // the row instead of rebuilding the list.
                onDelete?.Invoke();
                _ = NoteStore.Instance.Delete(note.Id);
                MarkDeleted(item, "note");
                delete!.Result = CommandResult.ShowToast(new ToastArgs
                {
                    Message = "Note deleted",
                    Result = CommandResult.KeepOpen(),
                });
            }
            catch (Exception ex)
            {
                delete!.Result = CommandResult.ShowToast(new ToastArgs { Message = "Could not delete the note: " + ex.Message });
            }
        })
        {
            Name = "Delete note",
            Icon = NotesIcons.DeleteGlyph,
        };

        return delete;
    }

    private static CommandContextItem DeleteItem(ClipboardEntry entry, ListItem item, Action? onDelete)
    {
        AnonymousCommand? delete = null;
        delete = new AnonymousCommand(() =>
        {
            try
            {
                // The page has to know before the store changes, so it can keep
                // the row instead of rebuilding the list.
                onDelete?.Invoke();
                _ = ClipboardStore.Instance.Delete(entry.Id);
                MarkDeleted(item, "item");
                delete!.Result = CommandResult.ShowToast(new ToastArgs
                {
                    Message = "Item deleted",
                    Result = CommandResult.KeepOpen(),
                });
            }
            catch (Exception ex)
            {
                delete!.Result = CommandResult.ShowToast(new ToastArgs { Message = "Could not delete the item: " + ex.Message });
            }
        })
        {
            Name = "Delete item",
            Icon = NotesIcons.DeleteGlyph,
        };

        return new CommandContextItem(delete)
        {
            Title = "Delete item",
            Subtitle = "Remove this entry from the history",
            Icon = NotesIcons.DeleteGlyph,
            IsCritical = true,
        };
    }

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
                if (NoteStore.Instance.Contains(text))
                {
                    save!.Result = CommandResult.ShowToast(new ToastArgs { Message = "This clip is already a note" });
                    return;
                }

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

    /// <summary>
    /// Copies the picture back to the clipboard and says what happened: a
    /// missing file or a clipboard that refuses the image must not be reported
    /// as a success.
    /// </summary>
    private static string CopyImage(ClipboardEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ImagePath))
        {
            return "This image has no file behind it";
        }

        if (!File.Exists(entry.ImagePath))
        {
            return "The image file is missing";
        }

        try
        {
            ClipboardWriter.SetImage(entry.ImagePath);
            return "Image copied to clipboard";
        }
        catch (Exception ex)
        {
            return "Could not copy the image: " + ex.Message;
        }
    }
}
