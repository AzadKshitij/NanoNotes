# NanoNotes landing page: design system

**File:** `landing-page/index.html` (single self-contained page: inline CSS, inline SVG, vanilla JS).
**Doc:** this file. Another agent can rebuild or restyle the page from the tokens below.

---

## 1. Idea

The page is the product. NanoNotes catches a thought mid-flow and hands it back later, so the landing page is a sheet on a desk with a **working capture field**: type, press Enter, the note is caught and added to the ledger. One interactive moment, and it is the most characteristic thing the app does.

Everything else stays quiet. Boldness is spent on two things: the physical clip at the top of the sheet, and the live capture. No scroll-reveal, no hover-everything, no card grid.

### Where the look comes from
- The app's own icon family is *drawn from vector geometry*, so the look is drafted and engineered.
- Data is plain JSON in `Documents\Notes`, so the page shows file panels, paths, and a ledger.
- Clipboard history means a real clipboard, oversized, holding the sheet.
- Precision, retention counts, and line counts give a cutting-mat grid under the sheet and a legal-pad margin rule.

---

## 2. Tokens

### Colour
| Token | Value | Use |
|---|---|---|
| `--desk` | `#14161A` | Page background (the desk) |
| `--desk-line` | `rgba(255,255,255,.035)` | Cutting-mat grid lines |
| `--paper` | `#FCFBF8` | The sheet |
| `--paper-hi` | `#FFFFFF` | Inputs, panels on paper |
| `--ink` | `#1A1C20` | Primary text |
| `--ink-2` | `#4A4F57` | Secondary text |
| `--ink-3` | `#8A8F98` | Timestamps, hints |
| `--line` | `#E6E3DA` | Hairlines on paper |
| `--line-strong` | `#CFCBC0` | Input and card borders |
| `--cobalt` | `#1E3FCC` | Single accent: clip, primary action, links |
| `--cobalt-ink` | `#16309F` | Accent hover |
| `--cobalt-wash` | `#EEF1FC` | Focus ring fill |
| `--clip-hi` | `#C9CCD2` | Clip metal |
| panel bg | `#14161A` | File and command panels |
| panel text | `#C7CBD2`, keys `#79B8FF`, strings `#3FB950`, numbers `#D29922` | Code colours |

Contrast: `--ink` on `--paper` is about 16:1. `--cobalt` on `--paper` is about 7:1. `--ink-2` on `--paper` is about 7:1.

### Type
- **IBM Plex Sans**: display + body. Weights 400/500/600/700.
- **IBM Plex Mono**: captured text, ledger rows, paths, timestamps, code. Weights 400/500.
- Display: `clamp(2.5rem, 6.2vw, 4.4rem)`, weight 600, `letter-spacing:-.035em`, `line-height:1.02`.
- H2: `clamp(1.7rem, 3.2vw, 2.5rem)`, `letter-spacing:-.025em`.
- Body: `1.0625rem`, `line-height:1.65`, colour `--ink-2`, measure up to 62ch.
- Ledger and mono rows: `.92` to `.95rem`.

### Space, shape, grid
- Sheet max width `1080px`; gutter `clamp(1.25rem, 4vw, 4rem)`.
- Section padding `clamp(2.5rem, 6vw, 4.5rem)`, separated by a 1px `--line` top rule.
- Radius: `2` to `3px` everywhere. Paper has square corners.
- Sheet sits `clamp(5.5rem,10vh,8rem)` from the top so the fixed bar never covers the clip.
- Legal-pad margin rule: 1px `--cobalt` at 22% opacity, `left: calc(gutter + .6rem)`, with content indented `calc(gutter + 1.75rem)` so text never crosses it; hidden at or below 640px.

---

## 3. Icon set

One inline SVG sprite at the top of `<body>` (`<symbol>` + `<use href="#i-...">`). No emoji, no icon font, no image files.

- Grid `viewBox="0 0 24 24"`, stroke `1.6` to `1.8`, round caps and joins, `fill:none` unless noted, all `currentColor`.
- Sizing: `.ic{ flex:none; width:1em; height:1em }` plus per-context overrides (wordmark 22px, column heads 15px, thumb 16px, footer 13px).

| Symbol | Meaning |
|---|---|
| `i-mark` | Wordmark: sheet + clip |
| `i-note` | A note (folded sheet, two lines) |
| `i-clip` | Clipboard (board, clip, two lines) |
| `i-search` | Magnifier |
| `i-copy` | Two overlapping cards |
| `i-close` | Delete |
| `i-return` | Enter / command prompt |
| `i-store` | Microsoft logo (fill) |
| `i-github` | GitHub mark (fill) |
| `i-image` | Image entry |
| `i-lock` | Network panel (local, locked) |

Add a new glyph by appending a `<symbol>` to the sprite and referencing it; keep the same grid and stroke so it belongs.

---

## 4. Components

- **Top bar**: fixed, `rgba(20,22,26,.72)` + `blur(10px)`, 60px tall, wordmark left, "Get it" right.
- **Sheet**: `--paper`, `border-radius:2px`, large soft shadow; a 14px graphite `::before` strip is the clipboard backing; `.clip` is an absolutely positioned SVG overlapping the top edge.
- **Capture field**: mono input + cobalt "Keep it" button; `:focus-within` gets a cobalt border + 3px `--cobalt-wash` ring. Stacks vertically at or below 640px.
- **Ledger**: `border-top:1px solid --line`; each row is mono text + timestamp + delete icon. New rows animate in with `catch` (fade + 8px rise, `.38s`).
- **Buttons**: `.btn-primary` cobalt on paper; `.btn-ghost` transparent with a `--line-strong` border and mono label. The hero has three actions: Store (primary), a **copy** button for `winget install NanoNotes`, and a **GitHub** link. Both ghosts carry `data-copy`/icon markup; the copy button shows `copied` and a cobalt border for 1.2s.
- **Screenshots**: `.viewer` with a `.tabs` tablist (Commands, Search, Notes, Clipboard) and a `.shot` figure. One `<img>` is swapped on click; the other three images are preloaded on `load`. Images are 972x571, kept in `landing-page/assets/`.
- **Search**: search bar over the two lists; filters rows live and shows `n of total`.
- **Lists**: two columns (Notes / Clipboard) of mono rows with timestamps; column heads are sentence case with `i-note` / `i-clip`. Image rows use a 34px thumb with `i-image`. Stack at or below 820px.
- **Panels**: dark `#14161A` boxes with a traffic-light bar; file tree and `notes.json` snippet with code colours.
- **Network panel**: bar with `i-lock` + "Network" and a `0 requests` badge; empty state "This panel stays empty."
- **Install**: Store badge + command panel with a working copy button.

---

## 5. Behaviour (vanilla JS, no dependencies)

1. `capture-form` submit: trim, prepend a ledger row with `today HH:MM`, clear and refocus the input. Delete button removes a row.
2. Screenshot tabs: click swaps `#shot-img` src/alt and the caption, moves `.is-active` and `aria-selected`; the remaining images preload on `load`.
3. `#filter` input: toggles `.hidden` on rows whose text does not contain the query; updates `#count`.
4. Any `.copy` click: writes its `data-copy` or the row's `.txt` to the clipboard (`navigator.clipboard`, with an `execCommand` fallback) and swaps the label to "copied" for 1.2s.
5. The ledger is `aria-live="polite"`; the input has a visually-hidden label.

Do not add a framework. Keep it small.

---

## 6. Responsive

| Breakpoint | Change |
|---|---|
| at or below 820px | lists and file panels stack |
| at or below 640px | sheet full-bleed (`margin:5rem 0 3rem`), margin rule hidden, clip 104px, capture stacks, top bar padding 1.25rem |

---

## 7. Accessibility

- `:focus-visible{ outline:2px solid --cobalt; outline-offset:2px }`.
- `@media (prefers-reduced-motion: reduce)` kills animation and transitions.
- Every icon is `aria-hidden="true"`; icon-only buttons carry `aria-label`.
- Tabs use `role="tablist"` / `role="tab"` with `aria-selected` and `aria-controls`; the figure is the `tabpanel`.
- Text colours above meet WCAG AA on their backgrounds.

---

## 8. Links and assets

| Label | URL |
|---|---|
| Microsoft Store | https://apps.microsoft.com/detail/9nw3g5pqwf51 |
| GitHub | https://github.com/AzadKshitij/NanoNotes |
| Releases | https://github.com/AzadKshitij/NanoNotes/releases |
| Privacy | https://github.com/AzadKshitij/NanoNotes/blob/main/PRIVACY.md |
| License | https://github.com/AzadKshitij/NanoNotes/blob/main/LICENSE |
| Live site | https://azadkshitij.github.io/NanoNotes/ |

Screenshots live in `landing-page/assets/` (copied from `docs/`): `search.png`, `search-all.png`, `notes.png`, `clipboard.png`.

---

## 9. Build notes

- One HTML file, no build step, no framework, no icon images. Loads fast.
- Fonts from Google Fonts with `preconnect` + `display=swap`; system-font fallbacks are in the stacks.
- Content rows are real, believable notes and clipboard entries (not lorem).
- Copy is plain and active: "A note, kept before you lose it." / "Keep it" leads to "copied".
- Avoid: all-caps eyebrows, `A · B · C` meta strings, `01/02/03` on non-sequences, identical rounded cards, emoji, and em dashes in prose.
