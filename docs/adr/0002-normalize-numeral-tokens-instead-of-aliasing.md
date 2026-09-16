# 2. Normalize Roman-numeral/arabic tokens instead of aliasing both forms

## Status

Accepted

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
`AddYearAbbreviationVariant` ("1994" also adds "94") and should be reviewed
for the same fix when this is implemented.
