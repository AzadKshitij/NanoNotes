// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
    public static ListItem ForNote(Note note, string? source = null)
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
            Result = CommandResult.ShowToast(new ToastArgs { Message = "Note copied" }),
        };

        return new ListItem(copy)
        {
            Title = notesPage.Headline(text),
            Subtitle = subtitle,
            Icon = NotesIcons.Note,
            Details = DetailsPreview.ForNote(note),
            MoreCommands =
            [
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
    }

    public static ListItem ForClipboard(ClipboardEntry entry, string? source = null)
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
                Result = CommandResult.ShowToast(new ToastArgs { Message = "Image copied to clipboard" }),
            };

            return new ListItem(copy)
            {
                Title = "Image",
                Subtitle = $"{entry.Width}x{entry.Height}  \u00b7  {subtitle}",
                Icon = icon,
                Details = DetailsPreview.ForClipboard(entry),
                MoreCommands = [DeleteCommand(entry)],
            };
        }

        var text = entry.Text ?? string.Empty;
        var copyText = new CopyTextCommand(text)
        {
            Result = CommandResult.ShowToast(new ToastArgs { Message = "Copied to clipboard" }),
        };

        return new ListItem(copyText)
        {
            Title = ClipboardPage.Headline(text),
            Subtitle = subtitle,
            Icon = NotesIcons.Clipboard,
            Details = DetailsPreview.ForClipboard(entry),
            MoreCommands = [DeleteCommand(entry)],
        };
    }

    private static CommandContextItem DeleteCommand(ClipboardEntry entry) => new(new AnonymousCommand(() => _ = ClipboardStore.Instance.Delete(entry.Id))
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
        if (!string.IsNullOrEmpty(entry.ImagePath))
        {
            ClipboardWriter.SetImage(entry.ImagePath);
        }
    }
}
