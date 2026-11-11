// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// Shows one clipboard entry in full: the whole text, or the image itself.
/// </summary>
internal sealed partial class ClipboardDetailPage : ContentPage
{
    private readonly ClipboardStore _store = ClipboardStore.Instance;
    private readonly string _entryId;

    public ClipboardDetailPage(string entryId)
    {
        _entryId = entryId;

        Name = "Open";
        Icon = NotesIcons.Clipboard;

        var entry = _store.Find(entryId);
        Title = entry is null
            ? "Clipboard item"
            : entry.Kind == ClipboardEntryKind.Image
                ? "Image"
                : ClipboardPage.Headline(entry.Text ?? string.Empty);

        Commands = entry is null ? [] : BuildCommands(entry);
    }

    private static IContextItem[] BuildCommands(ClipboardEntry entry)
    {
        if (entry.Kind == ClipboardEntryKind.Image)
        {
            return
            [
                new CommandContextItem(new AnonymousCommand(() => ClipboardWriter.SetImage(entry.ImagePath!))
                {
                    Name = "Copy image",
                    Result = CommandResult.ShowToast(new ToastArgs { Message = "Image copied to clipboard" }),
                })
                {
                    Title = "Copy image",
                    Icon = NotesIcons.Copy,
                },
                new CommandContextItem(new AnonymousCommand(() => _ = ClipboardStore.Instance.Delete(entry.Id))
                {
                    Name = "Delete item",
                    Result = CommandResult.GoBack(),
                })
                {
                    Title = "Delete item",
                    Subtitle = "Remove this entry from the history",
                    Icon = NotesIcons.Delete,
                    IsCritical = true,
                },
            ];
        }

        return
        [
            new CommandContextItem(new CopyTextCommand(entry.Text ?? string.Empty)
            {
                Result = CommandResult.ShowToast(new ToastArgs { Message = "Copied to clipboard" }),
            })
            {
                Title = "Copy text",
                Icon = NotesIcons.Copy,
            },
            new CommandContextItem(new AnonymousCommand(() => _ = ClipboardStore.Instance.Delete(entry.Id))
            {
                Name = "Delete item",
                Result = CommandResult.GoBack(),
            })
            {
                Title = "Delete item",
                Subtitle = "Remove this entry from the history",
                Icon = NotesIcons.Delete,
                IsCritical = true,
            },
        ];
    }

    public override IContent[] GetContent()
    {
        var entry = _store.Find(_entryId);

        if (entry is null)
        {
            return [new MarkdownContent("_This item is no longer in the history._")];
        }

        if (entry.Kind == ClipboardEntryKind.Image)
        {
            return
            [
                new ImageContent(new IconInfo(entry.ImagePath ?? string.Empty)) { MaxWidth = 640, MaxHeight = 480 },
                new MarkdownContent(
                    $"\n\n---\n\n_{entry.Width}x{entry.Height}  \u00b7  Copied {entry.Created.ToLocalTime():ddd d MMM yyyy, HH:mm}_"),
            ];
        }

        return
        [
            new PlainTextContent(entry.Text ?? string.Empty) { WrapWords = true },
            new MarkdownContent($"\n\n---\n\n_Copied {entry.Created.ToLocalTime():ddd d MMM yyyy, HH:mm}_"),
        ];
    }
}
