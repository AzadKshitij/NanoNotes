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
/// One captured clipboard item: either a piece of text or an image on disk.
/// </summary>
[JsonConverter(typeof(ClipboardEntryKindConverter))]
internal enum ClipboardEntryKind
{
    Text,
    Image,
}

/// <summary>
/// Reads and writes the kind of an entry as a name, so the history file stays
/// readable and hand-editable. Numbers are still accepted, because older files
/// wrote them.
/// </summary>
internal sealed class ClipboardEntryKindConverter : JsonConverter<ClipboardEntryKind>
{
    public override ClipboardEntryKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            var number = reader.GetInt32();
            return Enum.IsDefined(typeof(ClipboardEntryKind), number)
                ? (ClipboardEntryKind)number
                : ClipboardEntryKind.Text;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return Enum.TryParse<ClipboardEntryKind>(reader.GetString(), ignoreCase: true, out var parsed)
                ? parsed
                : ClipboardEntryKind.Text;
        }

        return ClipboardEntryKind.Text;
    }

    public override void Write(Utf8JsonWriter writer, ClipboardEntryKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
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
internal sealed partial class ClipboardStore : IDisposable
{
    /// <summary>
    /// Safety net so a very busy day cannot grow the file without bound. The
    /// retention window is the user-facing control; this only guards the worst case.
    /// </summary>
    private const int MaxEntries = 500;

    private const int SaveDelayMilliseconds = 1000;

    /// <summary>
    /// An image file younger than this is left alone when the folder is swept
    /// for orphans: it may be a picture that was just copied and whose entry is
    /// written a moment later.
    /// </summary>
    private static readonly TimeSpan OrphanGrace = TimeSpan.FromMinutes(10);

    private static readonly Lazy<ClipboardStore> _instance = new(() => new ClipboardStore());

    private readonly object _gate = new();
    private readonly List<ClipboardEntry> _entries = [];
    private readonly List<string> _pendingImageDeletes = [];
    private readonly List<(string Content, DateTimeOffset When)> _ownCopies = [];
    private bool _loaded;
    private bool _dirty;
    private bool _loadBlocked;
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
                ScheduleImageDelete(entry!);
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
                    ScheduleImageDelete(entry);
                }

                ScheduleSave();
            }
        }

        PruneOrphanImages();
    }

    /// <summary>
    /// Notes that the app itself put something on the clipboard, for example
    /// when a note is pasted into another window. The history keeps what the
    /// user copied, so these copies are skipped.
    /// </summary>
    public void MarkOwnCopy(string content)
    {
        lock (_gate)
        {
            _ownCopies.Add((content, DateTimeOffset.Now));

            if (_ownCopies.Count > 20)
            {
                _ownCopies.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// True when the given content is one the app copied itself, moments ago.
    /// </summary>
    public bool IsOwnCopy(string content)
    {
        lock (_gate)
        {
            var cutoff = DateTimeOffset.Now - TimeSpan.FromSeconds(10);
            _ownCopies.RemoveAll(c => c.When < cutoff);

            return _ownCopies.Any(c => string.Equals(c.Content, content, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Writes any pending change to disk. The extension calls this when it
    /// shuts down, so a copy made a moment before the palette closes is not
    /// lost to the debounce.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_saveTimer is not null)
            {
                _saveTimer.Dispose();
                _saveTimer = null;
            }

            SaveIfDirty();
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
            TrimToLimit();
            ScheduleSave();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Keeps the history within <see cref="MaxEntries"/>. The oldest entries go
    /// first; their images are only deleted when nothing else points at them.
    /// </summary>
    private void TrimToLimit()
    {
        while (_entries.Count > MaxEntries)
        {
            var oldest = _entries[^1];
            _entries.RemoveAt(_entries.Count - 1);
            ScheduleImageDelete(oldest);
        }
    }

    private static bool SameContent(ClipboardEntry a, ClipboardEntry b)
    {
        if (a.Kind == ClipboardEntryKind.Text)
        {
            return string.Equals((a.Text ?? string.Empty).Trim(), (b.Text ?? string.Empty).Trim(), StringComparison.Ordinal);
        }

        return string.Equals(a.ImagePath, b.ImagePath, StringComparison.Ordinal);
    }

    /// <summary>
    /// Marks an image file for deletion once the history that pointed at it has
    /// been written to disk. The same picture can sit in several entries, so
    /// the file is only removed when no other entry uses it.
    /// </summary>
    private void ScheduleImageDelete(ClipboardEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ImagePath))
        {
            return;
        }

        var stillUsed = _entries.Any(e =>
            !string.IsNullOrEmpty(e.ImagePath)
            && string.Equals(e.ImagePath, entry.ImagePath, StringComparison.OrdinalIgnoreCase));

        if (!stillUsed)
        {
            _pendingImageDeletes.Add(entry.ImagePath!);
        }
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
            // is lost, and start with an empty history.
            TryMoveAside();
            _entries.Clear();
            _loaded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file is locked or half-written, most likely by an editor that
            // has it open. Keep what we have, try again later, and refuse to
            // write over the file so the data on disk survives.
            _loadBlocked = true;
            ExtensionHost.LogMessage("notes clipboard history unreadable: " + ex.Message);
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
        try
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
        catch (Exception ex)
        {
            // This runs on a threadpool thread: an exception here would take the
            // whole extension down.
            ExtensionHost.LogMessage("notes clipboard save failed: " + ex.Message);
        }
    }

    private void SaveLocked()
    {
        if (_loadBlocked)
        {
            throw new IOException("The clipboard history file could not be read, so it was left untouched.");
        }

        Directory.CreateDirectory(DataDirectory);

        var json = JsonSerializer.Serialize(new ClipboardFile { Entries = _entries }, ClipboardJsonContext.Default.ClipboardFile);
        var temp = DataFilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, DataFilePath, overwrite: true);

        // The history no longer points at these pictures, so they can go.
        foreach (var path in _pendingImageDeletes)
        {
            TryDeleteFile(path);
        }

        _pendingImageDeletes.Clear();
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

            var cutoff = DateTimeOffset.UtcNow - OrphanGrace;

            foreach (var path in Directory.EnumerateFiles(ImageDirectory, "*.png"))
            {
                if (referenced.Contains(Path.GetFileName(path)))
                {
                    continue;
                }

                // A picture that was just copied may not have its entry yet.
                if (File.GetLastWriteTimeUtc(path) > cutoff.UtcDateTime)
                {
                    continue;
                }

                TryDeleteFile(path);
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

    private void LoadFromDisk()
    {
        var json = File.ReadAllText(DataFilePath);
        var file = JsonSerializer.Deserialize(json, ClipboardJsonContext.Default.ClipboardFile);

        _entries.Clear();
        foreach (var entry in file?.Entries ?? [])
        {
            if (entry is null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(entry.Id))
            {
                entry.Id = Guid.NewGuid().ToString("n");
            }

            if (entry.Created == default)
            {
                entry.Created = DateTimeOffset.Now;
            }

            _entries.Add(entry);
        }

        _entries.Sort((a, b) => b.Created.CompareTo(a.Created));
    }
}
