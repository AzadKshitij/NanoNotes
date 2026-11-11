// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        lock (_gate)
        {
            EnsureLoaded();
            ReloadIfChangedOnDisk();
            return _notes.ToArray();
        }
    }

    public Note? Find(string id) => All().FirstOrDefault(n => n.Id == id);

    public bool Contains(string text)
    {
        var trimmed = text.Trim();
        return All().Any(n => string.Equals(n.Text.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
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

        _loaded = true;

        if (!File.Exists(DataFilePath))
        {
            return;
        }

        try
        {
            LoadFromDisk();
        }
        catch (Exception)
        {
            // Never lose the file silently: keep it next to the live one and start clean.
            try
            {
                File.Move(DataFilePath, DataFilePath + ".bad", overwrite: true);
            }
            catch (IOException)
            {
            }

            _notes = [];
            _lastWriteUtc = default;
        }
    }

    /// <summary>
    /// Picks up edits made to the JSON file by hand while the extension is running.
    /// </summary>
    private void ReloadIfChangedOnDisk()
    {
        var exists = File.Exists(DataFilePath);

        if (!exists)
        {
            if (_lastWriteUtc == default)
            {
                return;
            }

            _notes = [];
            _lastWriteUtc = default;
            return;
        }

        var stamp = File.GetLastWriteTimeUtc(DataFilePath);
        if (stamp == _lastWriteUtc)
        {
            return;
        }

        try
        {
            LoadFromDisk();
        }
        catch (Exception)
        {
            // A half-written or temporarily locked file: keep what we have and try again next time.
        }
    }

    private void LoadFromDisk()
    {
        var json = File.ReadAllText(DataFilePath);
        var file = JsonSerializer.Deserialize(json, NotesJsonContext.Default.NotesFile);
        _notes = (file?.Notes ?? []).OrderByDescending(n => n.Updated).ToList();
        _lastWriteUtc = File.GetLastWriteTimeUtc(DataFilePath);
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(DataDirectory);

        var json = JsonSerializer.Serialize(new NotesFile { Notes = _notes }, NotesJsonContext.Default.NotesFile);
        var temp = DataFilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, DataFilePath, overwrite: true);
        _lastWriteUtc = File.GetLastWriteTimeUtc(DataFilePath);
    }
}
