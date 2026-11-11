// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

public partial class notesCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly SettingsManager _settingsManager = SettingsManager.Instance;

    public notesCommandsProvider()
    {
        DisplayName = "NanoNotes";
        Icon = NotesIcons.Note;
        _commands = [
            new CommandItem(new notesPage())
            {
                Title = DisplayName,
                Subtitle = "Search your quick notes, or save a new one",
                Icon = NotesIcons.Note,
                MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
            },
            new CommandItem(new ClipboardPage())
            {
                Title = "Clipboard history",
                Subtitle = "Search the text and images you copied",
                Icon = NotesIcons.History,
                MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
            },
            new CommandItem(new AnonymousCommand(OpenNotesFile)
            {
                Name = "Open notes file",
                Result = CommandResult.Dismiss(),
            })
            {
                Title = "Open notes file",
                Subtitle = NoteStore.DataFilePath,
                Icon = NotesIcons.File,
                MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
            },
        ];

        Settings = _settingsManager.Settings;
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

    /// <summary>
    /// Opens notes.json in whatever handles JSON files, so the single notes file can
    /// be read or edited by hand. The extension reloads it when it changes on disk.
    /// </summary>
    private static void OpenNotesFile()
    {
        NoteStore.Instance.EnsureFile();
        var path = NoteStore.DataFilePath;

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No default handler: at least show the file in Explorer.
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
    }
}
