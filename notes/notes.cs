// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace notes;

[Guid("87e6b7d0-dfa3-4b83-9dd5-eeaa3071ca74")]
public sealed partial class notes : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;

    private readonly notesCommandsProvider _provider = new();
    private readonly ClipboardMonitor _clipboardMonitor;

    public notes(ManualResetEvent extensionDisposedEvent)
    {
        this._extensionDisposedEvent = extensionDisposedEvent;
        this._clipboardMonitor = new ClipboardMonitor(ClipboardStore.Instance, SettingsManager.Instance);
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => _provider,
            _ => null,
        };
    }

    public void Dispose()
    {
        this._clipboardMonitor.Dispose();
        this._extensionDisposedEvent.Set();
    }
}
