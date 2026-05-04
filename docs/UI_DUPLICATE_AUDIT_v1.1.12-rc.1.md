# UI duplicate audit — v1.1.12-rc.1

**Scope:** `MainWindow.xaml`, grep for `TabItem`, sidebar nav, Kyra user strings, USB Intelligence surfaces.

---

## Tabs / navigation

| Finding | Verdict |
|---------|---------|
| Sidebar: USB Builder, System Intelligence, Toolkit Manager, Kyra, Diagnostics, Settings | **Single** nav button per major area — **no duplicate tabs**. |
| USB Intelligence | Implemented as a **GroupBox** inside **USB Builder** tab — not a second top-level tab; avoids parallel “USB” tabs. |

## Duplicate “Run scan” / System Intelligence

| Finding | Verdict |
|---------|---------|
| System Intelligence tab has scan controls with tooltip referencing “same as refreshing hardware context” | **Intentional** cross-link wording, not two unrelated buttons with different behavior (needs human confirmation during manual smoke). |

## Kyra naming (implementation note)

| Finding | Verdict |
|---------|---------|
| XAML `Content` for nav shows **“Kyra”** | User-facing name is consistent. |
| Some internal styles/bindings still use legacy identifier prefixes on elements | **Technical debt** — binds to `MainViewModel` property names; **not** changed in this pass (no UI redesign). |
| No legacy assistant name in user-visible `Content=` labels found in `MainWindow.xaml` for this audit | Safe for beta messaging. |

## USB Intelligence controls

| Finding | Verdict |
|---------|---------|
| Single bounded panel for benchmark + mapping + builder hints | **No duplicate** second USB Intelligence page located. |

## Diagnostics

| Finding | Verdict |
|---------|---------|
| Single Diagnostics tab | No duplicate diagnostics tab. |

## Toolkit Manager

| Finding | Verdict |
|---------|---------|
| Single Toolkit tab | No duplicate counts in XAML; counting logic covered by `ToolkitHealthItemView` tests (`MANUAL_REQUIRED` → “Manual required”). |

## Cut-off / hidden controls

| Finding | Verdict |
|---------|---------|
| Not measured in automated XAML pass | **Manual smoke** (window sizes 100%, 125%, 150% scaling) required — see smoke matrix. |

## Actions taken

- **None** that remove controls — only documentation. Any consolidation waits on reproducible UX bug reports.

## Needs human review

- Confirm whether **two** entry points to “refresh hardware context” vs “Run System Intelligence scan” confuse testers (wording-only tweak candidate, not done here).
