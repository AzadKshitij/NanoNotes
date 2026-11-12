# NanoNotes — Issue List

- Branch: `split-view` · Date: 2026-11-12
- Method: **code review only** — nothing was built, registered or run while the
  issues were found. The fixes below compile (`dotnet build`, 0 errors); the
  behaviour changes still need a run through `build.ps1` and a manual test.
- Intended location: `docs/ISSUES.md`

## Reported issues

| ID  | Issue | Status |
|-----|-------|--------|
| R1  | Search page shows only a hint when the box is empty; it should list all notes and clipboard entries directly | **Fixed** (`SearchPage`, committed earlier) |
| R2  | Clipboard: no way to save a clip as a note | **Fixed** for text clips (`ItemFactory`); now also refuses to add the same clip twice ("This clip is already a note"). Image clips cannot be notes — `Note` is text-only |
| R3  | Context menu (More / Ctrl+M) icons render as a muddy, unrecognizable blob | **Fixed**: the menu and the action bar now use Segoe Fluent glyphs (`CopyGlyph` `\uE8C8`, `AddGlyph` `\uE710`, `DeleteGlyph` `\uE74D`) instead of 96×96 PNGs. List rows keep the brand artwork, which reads fine at that size |
| R4  | Pasting an image copied from NanoNotes into Paint fails; Win+Shift+S into Paint works | **Fixed** (`ClipboardWriter.SetImage`): the picture is now loaded into memory and handed to the clipboard from there, and the stream is kept alive until the next copy. Before, the DataPackage pointed at a `File.OpenRead` stream that was disposed immediately, so Paint's request for the bitmap came back empty. **Needs a manual paste test** |
| R5  | Selection jumps to the first row after deleting an item | **Kept as it was**: the tombstone row plus the suppressed refresh is what holds the selection. The `-2` that was passed to `RaiseItemsChanged` was meaningless — the parameter is `TotalItems` — and is gone |

## Fixed issues

### Data loss / crash

- **N1 — shared image PNG deleted while other rows still reference it** — the same picture copied twice gives two entries with one `ImagePath`; `Delete` and `Prune` now check whether another entry still points at the file (`ClipboardStore.ScheduleImageDelete`).
- **N2 — pending clipboard save never flushed** — the history is written when the extension shuts down (`notes.Dispose` → `ClipboardStore.Dispose`). Image files are deleted only *after* the history that pointed at them was written, so an entry can no longer survive without its picture.
- **N3 — a transient read error destroyed the data file** — only a `JsonException` counts as corruption now (the file is moved to `.bad`). A locked or half-written file leaves the data alone, and saving is refused with a message instead of overwriting the file (`NoteStore.EnsureLoaded`, `ClipboardStore.EnsureLoaded`).
- **N13 — listener dispose could hang** — the join is timeboxed and the thread id is checked (`ClipboardListener.Dispose`).
- **N15 — unhandled exception in the save timer** — `SaveIfDirty` catches and logs; it used to take the extension process down.

### Correctness

- **N4 — no cap on copied text; the image guard ran after the decode** — text over 100 000 characters is skipped, and a decoded picture over 64 MB is dropped (`ClipboardMonitor`).
- **N6 — recorded image dimensions were the pre-downscale size** — the stored size is reported now.
- **N7 — the clipboard page went blank on no match; image matching was inverted** — a "No matches" row was added and images match on the word "image" (`ClipboardPage.Matches`); typing `a` no longer matches every picture.
- **N8 — image copy failures were silent** — a missing file or a refusing clipboard now says so instead of "copied".
- **N9 — the copy waited five seconds on the calling thread** — the wait is bounded at two seconds and the failure is reported.
- **N10 — "Save as note" created duplicates** — a clip that is already a note is refused.
- **N11 — two private STA clipboard threads raced** — the monitor and the writer share one clipboard thread (`ClipboardThreadQueue.Shared`).
- **N12 — edits to notes.json did not refresh the open pages** — the store notices the change and tells the pages (`NoteStore.All`).
- **N14 — the orphan sweep could delete a picture that was just copied** — files younger than ten minutes are left alone.
- **N16/N17 — hand-edited JSON was not sanitized** — missing ids, texts and timestamps are filled in on load instead of throwing or sorting the note to the year 1.
- **N18 — the 500 entry cap only ran every fifteen minutes** — the cap is applied when an entry is added.
- **N21 — dead code removed** — `ClipboardStore.Find`, `ClipboardStore.EnsureFile`.

### Performance

- **N5 — the details pane was rebuilt for every row on every keystroke** — finished panes are cached (`DetailsPreview`), and the line wrapping no longer copies the line once per wrapped row.

## Added

- **Paste a row where you were** — every note and clipboard row now offers a
  second action next to Copy: it puts the content on the clipboard and sends a
  real Ctrl+V to the window the user came from, so it lands in a text box, a
  form field or a canvas such as Paint.
  - `Helpers/ForegroundTracker.cs` — a WinEvent hook remembers the windows that
    were in front, so the app behind the palette can be found.
  - `Helpers/PasteIntoApp.cs` — writes the clipboard, waits for the palette to
    close and hand the focus back, then sends Ctrl+V as keyboard input. If the
    palette is still in front, nothing is typed, so the text never lands in the
    palette's own search box.
  - The app's own copies are marked (`ClipboardStore.MarkOwnCopy`), so pasting
    a note does not push a duplicate into the clipboard history.
  - Limitation: `SendInput` cannot reach a window that runs elevated.

## Open / by design

- **R4 needs a manual test**: copy an image from the history into Paint.
- **R5 is host behaviour**: the palette rebuilds the list on `ItemsChanged` and the SDK has no selection API, so the tombstone row is the workaround.
- Images cannot be saved as notes — `Note` is text-only.
- The clipboard monitor captures the text format only; rich copies keep the text paste.
- The `.NET` type name `notes` is lowercase (compiler warning CS8981) — a leftover from the template.
- Every source file still carries the Microsoft template header ("Copyright (c) Microsoft Corporation", MIT) although the project is GPL-3 by Azad Kshitij. Worth stripping.
