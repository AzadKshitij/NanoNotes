// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;

namespace notes;

/// <summary>
/// Writes to <c>%TEMP%\notes-debug.log</c>, but only while a
/// <c>%TEMP%\notes-debug.on</c> marker file exists. Handy for diagnosing the
/// clipboard listener on a real machine; costs one file check per event.
/// </summary>
internal static class DebugLog
{
    public static void Write(string message)
    {
        try
        {
            var temp = Path.GetTempPath();
            if (!File.Exists(Path.Combine(temp, "notes-debug.on")))
            {
                return;
            }

            File.AppendAllText(Path.Combine(temp, "notes-debug.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch
        {
        }
    }
}
