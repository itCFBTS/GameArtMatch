# 2. Normalize Roman-numeral/arabic tokens instead of aliasing both forms

## Status

Implemented

## Context

`NameNormalizer.ToTokens` tokenizes a title, then `AddRomanNumeralVariant`
adds an *additional* alias token for any numeral found — e.g. `"2"` also adds
`"ii"` to the same set, and `"II"` also adds `"2"` — so that "Rockman 2" and
an alternately-spelled "Rockman II" release still tokenize compatibly.

This was found to cause real false-positive matches: "Rockman 2 (Japan)
(Capcom Town).nes" was scoring 60%+ against completely unrelated titles like
"DuckTales 2 (Japan).jpg", "RoboCop 2 (Japan).jpg", and "Sangokushi II
(Japan).jpg" — games sharing nothing but a sequel number. Root cause:
`SimilarityScorer.ScorePercent` counts each shared *token* equally
(`intersectCount = a.Count(b.Contains)`), and because a numeral occupies
**two** slots in its own token set (`"2"` and `"ii"`) rather than one, a
shared sequel number contributes double the weight of a shared genuine title
word like `"rockman"` — which only ever occupies one slot.

## Decision

Change numeral tokenization from *alias-adding* to *canonicalizing*: store a
single canonical form (the arabic digit) regardless of whether the source
text used `"2"` or `"II"`, rather than adding a second token alongside the
original. Cross-notation matching ("Rockman 2" ↔ "Rockman II") is preserved —
both now tokenize to the identical single token — without the sequel number
ever occupying more than one slot in the set.

## Alternatives considered

- **Weight tokens by string length** (shorter = less weight): rejected —
  length is a leaky proxy for "generic/uninformative." It would also discount
  legitimately short, meaningful title words (e.g. "Doom", "Kid", "Fox"),
  trading one class of scoring error for another.
- **Leave tokenization as-is, discount known generated-variant tokens in the
  scorer**: workable, but narrower and more complex than fixing the root
  duplication directly — still leaves the underlying "same concept occupies
  two set slots" bug in place for anything else that might alias in the
  future (e.g. the year-abbreviation variant has the same shape of issue).

## Consequences

This alone reduces but does not eliminate the class of false positive it was
found from — two otherwise-unrelated titles that are both, say, "X 2" will
still share one real token (the numeral) out of a very small set, which can
still clear a lenient accuracy threshold. See ADR-0003 for the broader fix to
that remaining problem. The same duplication shape exists in
`AddYearAbbreviationVariant` ("1994" also adds "94") — reviewed as this ADR
asked, but *not* fixed the same way: that variant is one-directional (no
reverse "94 → assume 1994" case), so canonicalizing it would either break the
"NBA Jam '94" ↔ "NBA Jam 1994" cross-match it exists for, or require guessing
which bare 2-digit tokens are actually years — a different, riskier problem
than this one. Left as-is; would be its own ADR if it turns out to matter in
practice.

## Validation

Verified two ways before merging:

- **Hand-traced**: with the fix, `NameNormalizer.ToTokens` on "Rockman 2" and
  "Rockman II" both produce `{rockman, 2}` (previously "Rockman 2" alone
  produced `{rockman, 2, ii}`). `SimilarityScorer.ScorePercent("Rockman 2",
  "DuckTales 2")` dropped from ~66.7% to a clean 50.0% — the shared real
  token (the numeral) still counts, just once instead of twice. Confirmed via
  a temporary self-test in `Program.cs` (`--selftest`), removed after
  confirming.
- **Real scan, before/after**: same NES ROM set (Console Mode, Skip Existing
  Art on) scanned before and after the fix. Total candidates dropped from
  2,270 across 375 ROMs to 1,693 across 363 ROMs. Concretely, the "Rockman 2
  (Japan) (Capcom Town).nes" group previously included "DuckTales 2
  (Japan).jpg", "RoboCop 2 (Japan).jpg", "Kyonshiizu 2 (Japan).jpg",
  "Sangokushi II (Japan).jpg", "Shanghai II (Japan).jpg", and "Terminator 2
  (Japan).jpg" as candidates (all sharing nothing with Rockman but a sequel
  number) — after the fix, every one of those is gone, and the group's
  remaining candidates are exclusively genuine Rockman box art across its
  several regional/release variants.
