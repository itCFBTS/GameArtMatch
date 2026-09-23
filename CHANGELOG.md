# Changelog

Notable changes to GameArtMatch, release by release — written for users, not as
a commit log. See `git log` or compare tags on GitHub for full detail.

## Unreleased

- Tags are now sorted into categories (region, disc, revision, ...), and only the
  ones that help find the right art count toward a match score.

- The image preview is now a carousel of every distinct image matched to the
  selected ROM. Step through it with the mouse wheel, its scrollbar, or by
  clicking a neighbouring image; the results list follows along. Identical
  files share one slide.
- The preview pane lost its rounded-card look: it now sits behind a thin
  divider, with a matching rule under the toolbar.
- The status line moved to the bottom of the results list, and the preview
  pane stays hidden until a row is selected.

## v0.4.0 - 2026-09-19

- Improved match scoring: rare, distinguishing words now count more than common
  ones, so a title no longer matches unrelated box art on one shared word.
- Recognized 17 more region tags, including UK, Scandinavia, Taiwan, Russia,
  and Hong Kong.

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
