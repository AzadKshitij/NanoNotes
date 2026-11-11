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

namespace notes;

/// <summary>
/// One captured clipboard item: either a piece of text or an image on disk.
/// </summary>
internal enum ClipboardEntryKind
{
    Text,
    Image,
}

internal sealed class ClipboardEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public ClipboardEntryKind Kind { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("imagePath")]
    public string? ImagePath { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; set; }
}

/// <summary>
/// The on-disk document: one JSON file holding every clipboard entry.
/// </summary>
internal sealed class ClipboardFile
{
    [JsonPropertyName("entries")]
    public List<ClipboardEntry> Entries { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ClipboardFile))]
internal sealed partial class ClipboardJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Reads and writes the clipboard history. Entries live in one JSON file next to
/// the notes; images live as PNG files in a subfolder and are referenced by path,
/// so the JSON never grows with image data.
///
/// Writes are debounced: a burst of copies produces a single save a second later.
/// </summary>
internal sealed class ClipboardStore : IDisposable
{
    /// <summary>
    /// Safety net so a very busy day cannot grow the file without bound. The
    /// retention window is the user-facing control; this only guards the worst case.
    /// </summary>
    private const int MaxEntries = 500;

    private const int SaveDelayMilliseconds = 1000;

    private static readonly Lazy<ClipboardStore> _instance = new(() => new ClipboardStore());

    private readonly object _gate = new();
    private readonly List<ClipboardEntry> _entries = [];
    private bool _loaded;
    private bool _dirty;
    private Timer? _saveTimer;

    private ClipboardStore()
    {
    }

    public static ClipboardStore Instance => _instance.Value;

    public static string DataDirectory { get; } = Path.Combine(NoteStore.DataDirectory, "Clipboard");

    public static string DataFilePath => Path.Combine(DataDirectory, "clipboard.json");

    public static string ImageDirectory => Path.Combine(DataDirectory, "Images");

    /// <summary>
    /// Raised after an entry was added or removed, so open pages can refresh.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// All entries, newest first.
    /// </summary>
    public IReadOnlyList<ClipboardEntry> All()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _entries.ToArray();
        }
    }

    public ClipboardEntry? Find(string id) => All().FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// Saves a piece of copied text. Consecutive copies of the same text are
    /// ignored, so re-copying the selected entry does not create a duplicate.
    /// </summary>
    public ClipboardEntry AddText(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Clipboard text is empty.", nameof(text));
        }

        ClipboardEntry entry = new()
        {
            Id = Guid.NewGuid().ToString("n"),
            Kind = ClipboardEntryKind.Text,
            Text = text,
            Created = DateTimeOffset.Now,
        };

        Add(entry);
        return entry;
    }

    /// <summary>
    /// Saves a captured image. <paramref name="path"/> is a file in <see cref="ImageDirectory"/>.
    /// </summary>
    public ClipboardEntry AddImage(string path, int width, int height)
    {
        var entry = new ClipboardEntry
        {
            Id = Guid.NewGuid().ToString("n"),
            Kind = ClipboardEntryKind.Image,
            ImagePath = path,
            Width = width,
            Height = height,
            Created = DateTimeOffset.Now,
        };

        Add(entry);
        return entry;
    }

    public bool Delete(string id)
    {
        bool removed;
        lock (_gate)
        {
            EnsureLoaded();
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            removed = entry is not null && _entries.Remove(entry);
            if (removed)
            {
                if (entry!.ImagePath is not null)
                {
                    TryDeleteFile(entry.ImagePath);
                }

                ScheduleSave();
            }
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>
    /// Drops entries older than the retention window, trims to <see cref="MaxEntries"/>
    /// and removes image files that are no longer referenced.
    /// </summary>
    public void Prune(int retentionDays)
    {
        lock (_gate)
        {
            EnsureLoaded();

            var cutoff = DateTimeOffset.Now - TimeSpan.FromDays(retentionDays);
            var kept = new List<ClipboardEntry>(_entries.Count);
            var removed = new List<ClipboardEntry>();

            foreach (var entry in _entries)
            {
                if (entry.Created >= cutoff && kept.Count < MaxEntries)
                {
                    kept.Add(entry);
                }
                else
                {
                    removed.Add(entry);
                }
            }

            if (removed.Count > 0)
            {
                _entries.Clear();
                _entries.AddRange(kept);

                foreach (var entry in removed)
                {
                    if (entry.ImagePath is not null)
                    {
                        TryDeleteFile(entry.ImagePath);
                    }
                }

                ScheduleSave();
            }
        }

        PruneOrphanImages();
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

    public void Dispose()
    {
        lock (_gate)
        {
            if (_saveTimer is not null)
            {
                _saveTimer.Dispose();
                _saveTimer = null;
            }
        }
    }

    private void Add(ClipboardEntry entry)
    {
        lock (_gate)
        {
            EnsureLoaded();

            // Re-copying the newest entry (for example with "Copy" on the selected
            // item) should not add a second identical row.
            if (_entries.Count > 0
                && _entries[0].Kind == entry.Kind
                && SameContent(_entries[0], entry))
            {
                return;
            }

            _entries.Insert(0, entry);
            ScheduleSave();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool SameContent(ClipboardEntry a, ClipboardEntry b)
    {
        if (a.Kind == ClipboardEntryKind.Text)
        {
            return string.Equals((a.Text ?? string.Empty).Trim(), (b.Text ?? string.Empty).Trim(), StringComparison.Ordinal);
        }

        return string.Equals(a.ImagePath, b.ImagePath, StringComparison.Ordinal);
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
            var json = File.ReadAllText(DataFilePath);
            var file = JsonSerializer.Deserialize(json, ClipboardJsonContext.Default.ClipboardFile);
            _entries.Clear();
            _entries.AddRange((file?.Entries ?? []).OrderByDescending(e => e.Created));
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

            _entries.Clear();
        }
    }

    private void ScheduleSave()
    {
        _dirty = true;

        if (_saveTimer is null)
        {
            _saveTimer = new Timer(_ => SaveIfDirty(), null, SaveDelayMilliseconds, Timeout.Infinite);
        }
        else
        {
            _saveTimer.Change(SaveDelayMilliseconds, Timeout.Infinite);
        }
    }

    private void SaveIfDirty()
    {
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            SaveLocked();
            _dirty = false;
        }
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(DataDirectory);

        var json = JsonSerializer.Serialize(new ClipboardFile { Entries = _entries }, ClipboardJsonContext.Default.ClipboardFile);
        var temp = DataFilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, DataFilePath, overwrite: true);
    }

    private static void PruneOrphanImages()
    {
        try
        {
            if (!Directory.Exists(ImageDirectory))
            {
                return;
            }

            var referenced = new HashSet<string>(
                Instance.All()
                    .Select(e => e.ImagePath)
                    .Where(p => p is not null)
                    .Select(p => Path.GetFileName(p!)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var path in Directory.EnumerateFiles(ImageDirectory, "*.png"))
            {
                if (!referenced.Contains(Path.GetFileName(path)))
                {
                    TryDeleteFile(path);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
