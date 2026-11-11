// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace notes;

/// <summary>
/// Short, human wording for timestamps shown under list items.
/// </summary>
internal static class Humanize
{
    public static string RelativeTime(DateTimeOffset timestamp)
    {
        var delta = DateTimeOffset.Now - timestamp;

        if (delta.TotalMinutes < 1)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes} min ago";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours} h ago";
        }

        if (delta.TotalDays < 7)
        {
            return $"{(int)delta.TotalDays} d ago";
        }

        return timestamp.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }
}
