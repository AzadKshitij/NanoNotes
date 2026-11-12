// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// Custom brand icons shipped as package assets (Assets\icons\*.png), drawn
/// in the app tile's sticker style and resolved relative to the extension.
/// </summary>
internal static class NotesIcons
{
    public static readonly IconInfo Note = IconHelpers.FromRelativePath(@"Assets\icons\note.png");
    public static readonly IconInfo Add = IconHelpers.FromRelativePath(@"Assets\icons\add.png");
    public static readonly IconInfo File = IconHelpers.FromRelativePath(@"Assets\icons\file.png");
    public static readonly IconInfo Clipboard = IconHelpers.FromRelativePath(@"Assets\icons\clipboard.png");
    public static readonly IconInfo History = IconHelpers.FromRelativePath(@"Assets\icons\history.png");
    public static readonly IconInfo Image = IconHelpers.FromRelativePath(@"Assets\icons\image.png");
    public static readonly IconInfo Search = IconHelpers.FromRelativePath(@"Assets\icons\search.png");

    // Menu entries sit on a much smaller icon box than list rows, where the
    // sticker PNGs shrink to an unreadable size. Font glyphs stay crisp.
    public static readonly IconInfo CopyGlyph = new("\uE8C8");
    public static readonly IconInfo PasteGlyph = new("\uE77B");
    public static readonly IconInfo AddGlyph = new("\uE710");
    public static readonly IconInfo DeleteGlyph = new("\uE74D");
}
