// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// A single quick note.
/// </summary>
internal sealed class Note
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; set; }

    [JsonPropertyName("updated")]
    public DateTimeOffset Updated { get; set; }
}

/// <summary>
/// The on-disk document: one JSON file holding every note.
/// </summary>
internal sealed class NotesFile
{
    [JsonPropertyName("notes")]
    public List<Note> Notes { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(NotesFile))]
internal sealed partial class NotesJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Reads and writes the notes JSON file. Every note lives in one file so it can be
/// opened, edited or copied by hand.
/// </summary>
internal sealed class NoteStore
{
    private static readonly Lazy<NoteStore> _instance = new(() => new NoteStore());

    private readonly object _gate = new();
    private List<Note> _notes = [];
    private bool _loaded;
    private bool _loadBlocked;
    private DateTime _lastWriteUtc;

    private NoteStore()
    {
    }

    public static NoteStore Instance => _instance.Value;

    /// <summary>
    /// Notes live in Documents, not in AppData: a packaged app's AppData writes are
    /// redirected into the package's private LocalCache, where you would never find
    /// the file. Documents is a real folder you can open and edit with any editor.
    /// </summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string DataFilePath => Path.Combine(DataDirectory, "notes.json");

    private static string ResolveDataDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        return Path.Combine(documents, "Notes");
    }

    /// <summary>
    /// Raised after a note was added or removed, so open pages can refresh.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// All notes, newest first.
    /// </summary>
    public IReadOnlyList<Note> All()
    {
        var reloaded = false;

        lock (_gate)
        {
            EnsureLoaded();
            reloaded = ReloadIfChangedOnDisk();
        }

        if (reloaded)
        {
            // The file was edited outside the app: tell the open pages. This runs
            // on a worker thread, because All() is often called from the list
            // itself and the host must not be called back into while it is
            // building the list.
            ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke(this, EventArgs.Empty));
        }

        lock (_gate)
        {
            return _notes.ToArray();
        }
    }

    public bool Contains(string text)
    {
        var trimmed = text.Trim();
        return All().Any(n => string.Equals((n.Text ?? string.Empty).Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Makes sure the JSON file exists, so it can be opened in an editor.
    /// </summary>
    public void EnsureFile()
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (!File.Exists(DataFilePath))
            {
                SaveLocked();
            }
        }
    }

    public Note Add(string text)
    {
        var note = new Note
        {
            Id = Guid.NewGuid().ToString("n"),
            Text = text,
            Created = DateTimeOffset.Now,
            Updated = DateTimeOffset.Now,
        };

        lock (_gate)
        {
            EnsureLoaded();
            _notes.Insert(0, note);
            SaveLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return note;
    }

    public bool Delete(string id)
    {
        bool removed;
        lock (_gate)
        {
            EnsureLoaded();
            removed = _notes.RemoveAll(n => n.Id == id) > 0;
            if (removed)
            {
                SaveLocked();
            }
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        if (!File.Exists(DataFilePath))
        {
            _loaded = true;
            return;
        }

        try
        {
            LoadFromDisk();
            _loaded = true;
            _loadBlocked = false;
        }
        catch (JsonException)
        {
            // The file is really broken. Keep it next to the live one so nothing
            // is lost, and start with an empty list.
            TryMoveAside();
            _notes = [];
            _lastWriteUtc = default;
            _loaded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file is locked or half-written, most likely by an editor that
            // has it open. Keep what we have, try again later, and refuse to
            // write over the file so the data on disk survives.
            _loadBlocked = true;
            ExtensionHost.LogMessage("notes file unreadable: " + ex.Message);
        }
    }

    private static void TryMoveAside()
    {
        try
        {
            File.Move(DataFilePath, DataFilePath + ".bad", overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Picks up edits made to the JSON file by hand while the extension is running.
    /// Returns true when something actually changed.
    /// </summary>
    private bool ReloadIfChangedOnDisk()
    {
        var exists = File.Exists(DataFilePath);

        if (!exists)
        {
            if (_lastWriteUtc == default)
            {
                return false;
            }

            _notes = [];
            _lastWriteUtc = default;
            return true;
        }

        var stamp = File.GetLastWriteTimeUtc(DataFilePath);
        if (stamp == _lastWriteUtc)
        {
            return false;
        }

        try
        {
            LoadFromDisk();
            return true;
        }
        catch (Exception)
        {
            // A half-written or temporarily locked file: keep what we have and try again next time.
            return false;
        }
    }

    private void LoadFromDisk()
    {
        var json = File.ReadAllText(DataFilePath);
        var file = JsonSerializer.Deserialize(json, NotesJsonContext.Default.NotesFile);

        _notes = [];
        foreach (var note in file?.Notes ?? [])
        {
            if (note is null)
            {
                continue;
            }

            // A hand-edited file can leave fields empty; fill in the blanks
            // instead of showing a note from the year 1 or throwing on its text.
            if (string.IsNullOrEmpty(note.Id))
            {
                note.Id = Guid.NewGuid().ToString("n");
            }

            note.Text ??= string.Empty;

            if (note.Created == default)
            {
                note.Created = DateTimeOffset.Now;
            }

            if (note.Updated == default)
            {
                note.Updated = note.Created;
            }

            _notes.Add(note);
        }

        _notes.Sort((a, b) => b.Updated.CompareTo(a.Updated));
        _lastWriteUtc = File.GetLastWriteTimeUtc(DataFilePath);
    }

    private void SaveLocked()
    {
        if (_loadBlocked)
        {
            throw new IOException("The notes file could not be read, so it was left untouched.");
        }

        Directory.CreateDirectory(DataDirectory);

        var json = JsonSerializer.Serialize(new NotesFile { Notes = _notes }, NotesJsonContext.Default.NotesFile);
        var temp = DataFilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, DataFilePath, overwrite: true);
        _lastWriteUtc = File.GetLastWriteTimeUtc(DataFilePath);
    }
}
