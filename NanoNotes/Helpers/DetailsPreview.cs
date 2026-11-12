// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// Builds the right-hand details pane: the whole note or clipboard entry, so
/// nothing has to be opened just to read it.
/// </summary>
internal static class DetailsPreview
{
    private const int MaxTitleLength = 120;

    /// <summary>
    /// The details pane renders markdown without wrapping, so long lines are
    /// broken here, before they reach it. Chosen from the default palette
    /// window (800 px wide, details pane about 430 px, 14 px text).
    /// </summary>
    private const int WrapColumn = 52;

    /// <summary>
    /// The list asks for its rows on every keystroke, and every row carries a
    /// details pane. Building that pane means escaping the whole note again and
    /// again, so the finished panes are kept here. The key holds the text and
    /// the timestamps, because the pane shows both.
    /// </summary>
    private const int MaxCachedDetails = 512;

    private static readonly ConcurrentDictionary<string, Details> _cache = new(StringComparer.Ordinal);

    public static Details ForNote(Note note)
    {
        var text = note.Text ?? string.Empty;

        // The key names the content without copying the text into it: the id and
        // the timestamp identify it, and the hash catches a hand-edited note
        // whose timestamp was left alone.
        return Cached($"note|{note.Id}|{note.Updated.Ticks}|{text.GetHashCode()}", () => BuildNoteDetails(note));
    }

    public static Details ForClipboard(ClipboardEntry entry)
    {
        if (entry.Kind == ClipboardEntryKind.Image)
        {
            return BuildImageDetails(entry);
        }

        var text = entry.Text ?? string.Empty;
        return Cached($"clip|{entry.Id}|{entry.Created.Ticks}|{text.GetHashCode()}", () => BuildClipboardDetails(entry, text));
    }

    private static Details BuildNoteDetails(Note note)
    {
        var text = note.Text ?? string.Empty;
        var (lines, words, characters) = Count(text);

        var metadata = new List<IDetailsElement>
        {
            Text("Saved", note.Created.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)),
        };

        if (note.Updated - note.Created > TimeSpan.FromMinutes(1))
        {
            metadata.Add(Text("Edited", note.Updated.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)));
        }

        metadata.Add(Tags("Size", Plural(lines, "line"), Plural(words, "word"), Plural(characters, "character")));

        return new Details
        {
            Title = DisplayTitle(text, "Note"),
            Body = MarkdownBody(text),
            Size = ContentSize.Large,
            Metadata = metadata.ToArray(),
        };
    }

    private static Details BuildClipboardDetails(ClipboardEntry entry, string text)
    {
        var (lines, words, characters) = Count(text);

        var metadata = new List<IDetailsElement>
        {
            Text("Copied", entry.Created.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)),
        };

        metadata.Add(Tags("Size", Plural(lines, "line"), Plural(words, "word"), Plural(characters, "character")));

        return new Details
        {
            Title = DisplayTitle(text, "Clipboard item"),
            Body = MarkdownBody(text),
            Size = ContentSize.Large,
            Metadata = metadata.ToArray(),
        };
    }

    private static Details BuildImageDetails(ClipboardEntry entry)
    {
        var details = new Details
        {
            Title = "Image",
            Body = $"_{entry.Width}\u00a0\u00d7\u00a0{entry.Height} pixels_",
            Size = ContentSize.Large,
            Metadata =
            [
                Text("Copied", entry.Created.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)),
            ],
        };

        if (!string.IsNullOrEmpty(entry.ImagePath))
        {
            details.HeroImage = new IconInfo(entry.ImagePath);
        }

        return details;
    }

    private static Details Cached(string key, Func<Details> build)
    {
        if (_cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var details = build();

        if (_cache.Count >= MaxCachedDetails)
        {
            _cache.Clear();
        }

        _cache[key] = details;
        return details;
    }

    /// <summary>
    /// The first line, kept short enough to work as a heading in the details pane.
    /// </summary>
    private static string DisplayTitle(string text, string fallback)
    {
        var trimmed = text.Trim();
        var newline = trimmed.IndexOf('\n');
        var firstLine = (newline >= 0 ? trimmed[..newline] : trimmed).Trim();

        if (firstLine.Length == 0)
        {
            return fallback;
        }

        return firstLine.Length <= MaxTitleLength
            ? firstLine
            : firstLine[..(MaxTitleLength - 1)].TrimEnd() + "\u2026";
    }

    /// <summary>
    /// Renders plain text as markdown without letting its characters turn into
    /// formatting. Source line breaks stay visible, and long lines are wrapped
    /// because the pane's markdown viewer does not wrap them itself.
    /// </summary>
    private static string MarkdownBody(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (normalized.Trim().Length == 0)
        {
            return "_Empty_";
        }

        var lines = new List<string>();
        foreach (var line in normalized.Split('\n'))
        {
            AppendWrappedLine(lines, line);
        }

        return string.Join("  \n", lines);
    }

    private static void AppendWrappedLine(List<string> lines, string line)
    {
        var prefix = new StringBuilder();
        var index = 0;

        // Indentation becomes non-breaking spaces so it survives markdown and
        // cannot start an indented code block.
        while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
        {
            prefix.Append(line[index] == '\t' ? "\u00a0\u00a0\u00a0\u00a0" : "\u00a0");
            index++;
        }

        var rest = line[index..];
        var chunk = new StringBuilder();
        var first = true;
        var start = 0;

        // Walk the line with an index instead of cutting new strings out of it:
        // a long line used to be copied once per wrapped row.
        while (rest.Length - start > WrapColumn)
        {
            var cut = rest.LastIndexOf(' ', start + WrapColumn - 1, WrapColumn - 1);
            if (cut <= start)
            {
                cut = start + WrapColumn - 1;
            }

            chunk.Clear();
            chunk.Append(rest, start, cut - start);
            lines.Add((first ? prefix.ToString() : string.Empty) + Escape(chunk.ToString().TrimEnd()));
            first = false;

            start = cut;
            while (start < rest.Length && rest[start] == ' ')
            {
                start++;
            }
        }

        chunk.Clear();
        chunk.Append(rest, start, rest.Length - start);
        lines.Add((first ? prefix.ToString() : string.Empty) + Escape(chunk.ToString()));
    }

    private static string Escape(string line)
    {
        var builder = new StringBuilder(line.Length + 8);

        foreach (var c in line)
        {
            // Backslash-escape every markdown punctuation character that can
            // start or change formatting. CommonMark renders the escaped
            // character as itself.
            if ("\\`*_[]<>#|~!+-=.()".Contains(c))
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static DetailsElement Text(string key, string value) => new()
    {
        Key = key,
        Data = new DetailsLink { Text = value },
    };

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static DetailsElement Tags(string key, params string[] values)
    {
        var tags = new List<ITag>(values.Length);
        foreach (var value in values)
        {
            tags.Add(new Tag(value));
        }

        return new DetailsElement
        {
            Key = key,
            Data = new DetailsTags { Tags = tags.ToArray() },
        };
    }

    private static (int Lines, int Words, int Characters) Count(string text)
    {
        var trimmed = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (trimmed.Length == 0)
        {
            return (0, 0, 0);
        }

        var lines = trimmed.Split('\n').Length;
        var words = 0;

        foreach (var _ in trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            words++;
        }

        return (lines, words, trimmed.Length);
    }
}
