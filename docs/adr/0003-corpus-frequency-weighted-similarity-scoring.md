# 3. Weight similarity scoring by corpus token frequency (TF-IDF-style)

## Status

Accepted in principle — implementation and cross-library-size validation
pending

## Context

`SimilarityScorer.ScorePercent` treats every shared token as equally
informative: a shared `"rockman"` counts the same as a shared `"2"` or a
shared `"(japan)"` region tag. In practice, tokens vary enormously in how
much they actually indicate two titles are related — a token that appears in
only a handful of entries in a scan is strong evidence of a real
relationship; a token that appears in hundreds of entries (a common sequel
number, a common region tag) is nearly uninformative on its own. ADR-0002
fixes one specific mechanical cause of numeral tokens being over-counted
(duplication), but even after that fix, a short title sharing only a generic
token with another short title can still score high enough to clear typical
accuracy thresholds.

## Decision

Move to corpus-frequency-weighted scoring, in the spirit of TF-IDF: weight
each token's contribution to the intersection by how rare it is across the
current scan's own corpus (inverse document frequency), rather than counting
every shared token as 1. A token present in only a few entries contributes
close to full weight; a token present in a large fraction of entries
contributes close to none. `MatchingService.BuildImageIndex`'s existing
inverted index (`Dictionary<string, List<int>>`) already tracks, for free,
how many images contain each token (`index[token].Count`) — the
document-frequency input this needs already exists as a side effect of the
existing recall/candidacy lookup. The ROM side has no equivalent index today
and will need one built the same way for symmetric weighting.

## Alternatives considered

- **Explicitly flag and discount "generated variant" tokens** (Roman-numeral/
  year aliases) rather than general corpus-frequency weighting: narrower —
  only fixes numeral-shaped collisions, not other generic-but-not-numeral
  collisions (e.g. common tag words that survive stripping, or a common word
  that isn't in the user's configured common-words list). Corpus-frequency
  weighting handles both without needing to enumerate every generic-word case
  in advance.
- **Length-based token weighting**: rejected for the same reason as in
  ADR-0002 — an unreliable proxy for informativeness.
- **Expose old vs. new scoring as a permanent user-facing setting**:
  rejected. It would require maintaining two scoring code paths indefinitely,
  and most users lack the context to make an informed choice between them.
  More importantly, corpus-frequency weighting's behavior is expected to
  degrade on very small libraries (rarity isn't a meaningful signal with only
  a handful of documents to measure it against) — a toggle would leave a user
  with a small collection quietly getting worse results with no way to know
  why. That's a real problem to solve directly (e.g. a minimum-corpus-size
  floor before weighting applies), not one to defer to user choice.

## Consequences

Needs a document-frequency source for ROM tokens (new), not just image
tokens (existing). Must be validated across a range of library sizes before
being treated as final — specifically including a small library (e.g. the
~8-ROM case already seen in earlier testing) to confirm it doesn't misbehave
exactly where the "Alternatives considered" concern predicts it might.
Implementation should happen on its own git branch (see ADR-0001) so it can
be compared against real scans before merging.
