# Changelog

Notable changes to GameArtMatch, release by release — written for users, not as
a commit log. See `git log` or compare tags on GitHub for full detail.

## v0.4.0 - 2026-09-19

- Improved match scoring to weight rare, distinguishing words more heavily than
  common ones (e.g. a shared sequel number no longer counts as much as a shared
  title word) — fixes titles matching unrelated box art off a coincidental
  shared word alone.
- Recognized several previously-missed regions in ROM/art tags: Taiwan, Russia,
  UK, Scandinavia, Denmark, Norway, Finland, Argentina, Hong Kong, Portugal,
  Greece, Belgium, Ireland, Israel, India, Mexico, Peru, and "Ja" as Japan.

## v0.3.0 - 2026-09-16

- New retro visual theme.
- Added the ability to ignore an entire folder, not just individual ROMs.
- Faster scanning.
- Reorganized menus.

## v0.2.0 - 2026-09-14

- Report window: multi-select and batch-ignore ROMs, right-click ignore/
  un-ignore, alternating row shading, and a filter to hide duplicate matches.
- Smarter match scoring, and the app now remembers your last folder selections.
- Console Mode options moved into the Path tab.
- Exported reports are now named after the current ROMs folder.
- Fixed a crash when a candidate image disappears mid-scan.

## v0.1.0 - 2026-09-12

- Initial release.
