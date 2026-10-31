# Changelog

## 0.3.2 — 2026-10-30

### Fixed
- **Claude** could show "—" for every window after a failed live query: the monitor quietly fell back to an old
  status-line reading whose windows had all reset, and cached it for two minutes. That fallback is now used only
  when it still has current numbers; otherwise the real error is shown, and neither errors nor fallbacks are cached,
  so clicking again retries right away.

### Added
- Failed Claude queries are logged to `%LOCALAPPDATA%\NostalgicRellm\ai.log` (time and HTTP status only, never the token).
- `FEATURES.md`: the wish list for upcoming versions (in Portuguese).

## 0.3.1 — 2026-10-29

The AI panel actually works now.

### Fixed
- **GPT · Codex** stopped answering ("No response from the server"). Usage is now read by asking Codex itself
  (`codex app-server`), the same way the Codex app does. Bonus: the monitor no longer touches the Codex sign-in token.
- **Claude** only refreshed if you used Claude Code in a terminal, which nobody should be forced to do. It now
  reads usage straight from Anthropic with Claude Code's sign-in, and lets Claude Code renew that sign-in on its
  own (`claude auth status`) when it expires or a query fails. No terminal needed after the first login.
- Stale readings no longer show a misleading 0%; windows that reset since the last reading show "—".

### Changed
- **Connections…** shows the Claude Code sign-in first, with the status line as an optional backup.
- `--dump` now also tests both AI providers.

## 0.3.0 — 2026-10-29

First public release.

- Translucent horizontal bar with CPU, RAM, GPU, VRAM, temperatures and every disk
- GKrellM-style history graphs and LED buttons (collapse, dock to the top of the screen)
- Top 5 processes per resource, with a kill button
- Claude and GPT usage panel
- Four skins (E17 Glass, E16 Bevel, GKrellM Classic, Minimal), English and Brazilian Portuguese
