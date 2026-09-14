# GameArtMatch

A cross-platform tool that fuzzy-matches ROM filenames to box-art images and
renames the art to match, for use with [MiSTer FPGA](https://github.com/MiSTer-devel)
and similar retro-gaming setups. Built from scratch in Avalonia/.NET, inspired
by the classic Windows-only FatMatch.exe.

## Features

- **Fuzzy matching engine** — tolerant of tag noise like `(USA)`, `(Rev 1)`,
  standalone letters, Roman numerals vs. Arabic numbers, and common words, so
  ROM and image filenames don't need to match exactly.
- **Meaningful scores** — the match score reflects how close a candidate
  actually is: an identical filename scores 100, a same-title-different-version
  sibling scores visibly lower, and region spelling differences (`USA` vs
  `US`) don't count against a match the way a genuine difference does.
- **Duplicate detection** — candidate images that are byte-identical (same
  cover, different filename) are clustered and visually grouped, with an
  option to collapse them down to one per group.
- **ROM ignore list** — right-click one or many ROMs (multi-select and Ctrl+A
  supported) to permanently skip them in future scans, independent of which
  folder you're currently pointed at.
- **Report window** — Missing, Matched, and Ignored ROMs in one place,
  exportable as plain text.
- **MiSTer Console Mode** — understands the `<system>/media/<name>.png`
  convention and can skip ROMs that already have art.
- **Rename with a safety net** — optional backup/copy scripts alongside the
  actual rename, so nothing is destructive by default.

## Download

Pre-built binaries for Linux and Windows are on the
[Releases page](https://github.com/itCFBTS/GameArtMatch/releases) — no .NET
runtime installation required, just download and run.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run
```

To produce the same self-contained, single-file binaries as the Releases page
(for both Linux and Windows, cross-compiled from either platform):

```bash
./publish.sh
```

Output lands in `publish/linux-x64/` and `publish/win-x64/`.

## Usage

1. Pick a **ROMs** folder and an **Images** folder from the main window.
2. Click **Start** to scan. Candidates are grouped by ROM, best match first,
   with a score and any relevant markers (exact match, duplicate elsewhere,
   identical to another candidate here).
3. Select the correct image for each ROM (or use the **Selection** dropdown
   to auto-select best/single matches across the board) and click
   **Rename Selected**.
4. Use **File > Options** to configure root folders, matching behavior, and
   renaming options; **File > Report** to see what's missing, matched, or
   ignored across a scan.

Folder selections and the last-used ROMs↔Images pairing are remembered
between launches.

## License

[GPL-3.0](LICENSE)
