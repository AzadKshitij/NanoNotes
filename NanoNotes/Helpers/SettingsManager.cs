// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

internal sealed partial class SettingsManager : JsonSettingsManager
{
    private const int DefaultRetentionDays = 14;
    private const int MinRetentionDays = 7;
    private const int MaxRetentionDays = 30;

    private static readonly Lazy<SettingsManager> _instance = new(() => new SettingsManager());

    private static readonly string _namespace = "notes";

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

    private readonly TextSetting _retentionDays = new(
        Namespaced(nameof(RetentionDays)),
        "History retention (days)",
        "How many days of clipboard history to keep (7-30)",
        DefaultRetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private readonly ToggleSetting _saveImages = new(
        Namespaced(nameof(SaveImages)),
        "Save images",
        "Also save copied images to the clipboard history",
        true);

    private SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_retentionDays);
        Settings.Add(_saveImages);

        // Load settings from file upon initialization
        LoadSettings();

        Settings.SettingsChanged += (_, _) => SaveSettings();
    }

    public static SettingsManager Instance => _instance.Value;

    /// <summary>
    /// The retention window in days, clamped to the 7-30 range the user asked for.
    /// </summary>
    public int RetentionDays => Math.Clamp(
        int.TryParse(_retentionDays.Value, out var days) ? days : DefaultRetentionDays,
        MinRetentionDays,
        MaxRetentionDays);

    public bool SaveImages => _saveImages.Value;

    private static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("notes");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "settings.json");
    }
}
