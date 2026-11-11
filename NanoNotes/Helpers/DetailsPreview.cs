// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
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

    public static Details ForNote(Note note)
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

    public static Details ForClipboard(ClipboardEntry entry)
    {
        if (entry.Kind == ClipboardEntryKind.Image)
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

        var text = entry.Text ?? string.Empty;
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
        var first = true;

        while (rest.Length > WrapColumn)
        {
            var cut = rest.LastIndexOf(' ', WrapColumn - 1);
            if (cut <= 0)
            {
                cut = WrapColumn - 1;
            }

            var chunk = rest[..cut].TrimEnd();
            lines.Add((first ? prefix.ToString() : string.Empty) + Escape(chunk));
            first = false;
            rest = rest[cut..].TrimStart(' ');
        }

        lines.Add((first ? prefix.ToString() : string.Empty) + Escape(rest));
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
