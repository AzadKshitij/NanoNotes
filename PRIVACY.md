# NanoNotes — Privacy Policy

**Last updated:** November 11, 2026

NanoNotes is a Microsoft PowerToys Command Palette extension that keeps
quick notes and a history of things you have copied. This policy describes
exactly what the app does and does not do with your data.

## The short version

- Everything NanoNotes stores stays **on your device**.
- NanoNotes does **not** send any data to the developer, Microsoft, or any
  third party. There is no telemetry, analytics, advertising, account, or
  sign-in of any kind.
- Nothing leaves your computer. There is nothing to intercept in transit,
  because there is no transmission at all.

## What data the app handles

| Data | Why | Where it lives |
|------|-----|----------------|
| Notes you type | The core feature | `Documents\Notes\notes.json` |
| Clipboard text | The clipboard history feature | `Documents\Notes\Clipboard\clipboard.json` |
| Clipboard images (only if "Save images" is enabled) | So copied images can be re-pasted later | `Documents\Notes\Clipboard\Images\` |
| Settings (history retention in days, "Save images" toggle) | To apply your preferences | `settings.json` in the Command Palette extension settings folder |
| Debug log (only if you create `%TEMP%\notes-debug.on` yourself) | Diagnostics when you ask for it | `%TEMP%\notes-debug.log` |

All of these are plain files on your local disk. The clipboard history is
kept for 7–30 days depending on your retention setting (14 days by default),
older items are pruned automatically, and the history is hard-capped at
500 entries. Notes are kept until you delete them.

## What the app does not do

- No network requests at all. The source code contains no HTTP client,
  socket, or endpoint usage. The package manifest declares the
  `internetClient` capability but the app never uses it.
- No data collection beyond the notes and clipboard entries you create
  yourself.
- No sharing, selling, renting, or trading of any data, with anyone, ever.
- No crash reporting or usage statistics.
- No access to your files, contacts, microphone, camera, location, or
  accounts. The clipboard listener reads only the system clipboard — that is
  the entire point of a clipboard history — and stores what you copied
  locally as described above.

## The clipboard listener

To build the history, NanoNotes monitors the Windows clipboard and records
what you copy (text always; images only if you leave "Save images" enabled).
This happens locally and the contents never leave your machine.

You are in control:

- Turn off **Save images** in the extension settings to stop storing images.
- Set **History retention** (7–30 days) to control how long items are kept.
- Delete individual entries from the clipboard history at any time.
- Delete everything by removing the `Documents\Notes` folder. Doing so does
  not affect the app's ability to start; it will simply begin with empty
  notes and history.

## Security

Your notes and clipboard history are stored as **unencrypted plaintext**
files. Anyone with read access to your user profile folder can read them.
Protect your Windows session accordingly (strong sign-in, BitLocker, etc.),
the same as you would for a text document you care about.

## Uninstalling

Removing the extension removes the app. It does **not** automatically delete
your data in `Documents\Notes` — delete that folder yourself if you want the
notes and clipboard history erased.

## Children

NanoNotes is not directed at children and is unlikely to be used by them. It
has no child-directed features and collects no data from anyone.

## Third parties

No third party receives any data from NanoNotes. The extension is a
standalone local component hosted by PowerToys Command Palette; it has no
services, servers, SDKs, or analytics of its own.

## Changes to this policy

This policy may change. The current version always lives at this path in the
repository, and the version linked from the Microsoft Store listing is the
authoritative one. Material changes will be noted in the release notes.

## Contact

Questions or concerns: open an issue in this repository's **Issues** tab.
