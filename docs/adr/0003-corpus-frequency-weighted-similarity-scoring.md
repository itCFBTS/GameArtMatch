# 3. Weight similarity scoring by corpus token frequency (TF-IDF-style)

## Status

Implemented — validated against a synthetic corpus; real-library-size
validation (the "Small-corpus behavior" section below) still pending

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

### Formula

Weight each token's contribution by how rare it is across the scan's corpus,
in the spirit of TF-IDF/inverse document frequency — but weight **both**
sides of the containment ratio, not just the shared-token count:

```
weightedIntersect = Σ weight(t) for t in (A ∩ B)
scoreFromA = weightedIntersect / Σ weight(t) for t in A
scoreFromB = weightedIntersect / Σ weight(t) for t in B
score = average(scoreFromA, scoreFromB) * 100
```

This isn't a stylistic choice — it's forced by an invariant the existing
formula already guarantees and that we shouldn't give up: identical token
sets must score exactly 100%. If only the numerator were weighted (`Σ
weight(t) for t in A∩B` divided by a *plain* `|A|`), a set matched against
itself would score the *average weight* of its own tokens, not 1 — breaking
100%-on-identical-match for any title not made entirely of maximally-rare
tokens. Weighting both sides makes numerator and denominator the same sum
when `A == B`, so the ratio is exactly 1 regardless of what the individual
weights are — the guarantee falls out of the algebra rather than needing
special-cased code.

`weight(t)` itself, normalized to `[0, 1]` so it composes cleanly into a
percentage-based formula (raw IDF, `log(N/df)`, is unbounded and grows with
corpus size):

```
weight(t) = 1 - log(df(t)) / log(N)
```

where `df(t)` = number of documents (ROM + image titles combined — see
below) containing token `t`, and `N` = total document count. `df(t) = 1`
(as rare as possible) → weight 1. `df(t) = N` (in every document) → weight
0. **Guard: if `N ≤ 1`, define every token's weight as 1** (rarity is
meaningless with zero or one document to measure it against, and the raw
formula divides by `log(N) = 0` in that case — this is a correctness fix
required regardless of anything else below, not a design choice).

### One combined corpus, not two

Document frequency is computed over ROM titles and image titles *together*
as a single population, rather than keeping separate ROM-corpus and
image-corpus frequency tables. Keeping them separate would create a real
ambiguity: a shared (intersecting) token would need one `weight(t)` value,
but a two-corpus setup could compute two different values for it (rare among
ROMs, common among images, or vice versa) with no principled way to pick
between them short of an arbitrary tie-break. A single combined corpus gives
every token exactly one document frequency and therefore exactly one weight,
used consistently on both sides of the ratio and in the intersection term.

### Where the document frequencies come from

Images already have this for free: `BuildImageIndex`'s existing inverted
index (`Dictionary<string, List<int>>`) tracks, per token, which images
contain it — `index[token].Count` is document frequency, no extra work
needed. ROMs have no equivalent today (tokenized on the fly, inside the main
matching loop, and discarded immediately after use). This adds a ROM-side
pre-pass, shaped like `BuildImageIndex` but simpler:

- A plain `Dictionary<string, int>` (token → count), not a full inverted
  index — ROMs never need "which ROMs contain this token" for a lookup the
  way images do (nothing does a reverse candidacy search from an image back
  to ROMs); only the count is needed for weighting.
- As a side effect, caches each ROM's own tokenized set, so the main
  matching loop can reuse it instead of re-tokenizing the same ROM a second
  time.

Combined: `df(t) = image_index[t].Count + rom_token_freq[t]`, `N =
images.Count + roms.Count`. Every token's `weight(t)` is precomputed once,
right after both passes finish and before the main matching loop starts —
not recomputed per candidate pair, since `weight(t)` only depends on the
token, and the same common tokens (a bare "2", a region tag) get looked at
across many candidate comparisons.

### Scope: only the token universe candidacy already uses

There are two separate scoring computations today: a candidacy/threshold
score (decides whether something becomes a candidate at all) using
*stripped* tokens (tags removed, or left untouched if "Disregard ROM tags"
is off — either way, whatever `NameNormalizer.ToTokens`'s default
`StripPerSettings` handling produces), and — only when "Disregard ROM tags"
is on — a *separate* display-only score recomputed with tag-inclusive
tokens (tags canonicalized, not removed), since a token like `"(japan)"`
only exists in that second universe.

This weighting applies only to the first (candidacy) universe — the same
one `BuildImageIndex` and the new ROM-side pass already tokenize. The
tag-inclusive display recomputation is left unweighted for now. Reasoning:
the false-positive problem this ADR exists to fix is a candidacy problem
(an unrelated title clearing the threshold at all) — fixing that already
prevents the bad candidate from ever reaching the display-score computation,
which doesn't gate anything and only cosmetically affects an already-good
candidate's shown percentage. Weighting it too would require eagerly
tokenizing every ROM and image with `ForceInclude` up front to get a
corpus-wide count — but that computation is deliberately lazy today (only
run for images that already survived the stripped-token threshold, cached
in `fullTokenCache`), and abandoning that laziness to support a score that
doesn't gate anything isn't justified without evidence it's still a problem
after the candidacy fix lands. When "Disregard ROM tags" is off, there's no
split at all — the one computation both universes use is the (unweighted-
for-tags-but-otherwise-weighted) stripped-token one, so this scope
boundary only actually excludes something in the "tags on" configuration.

### Small-corpus behavior: validate before adding complexity

The weight formula is legitimately more sensitive with a small `N` — e.g. at
`N = 5`, a token's `df` going from 1 to 2 swings its weight from 1.0 to
~0.57, a much bigger jump than the same `df` change would cause at `N =
2500`. Three ways to soften this were considered — a hard corpus-size floor
below which weighting is disabled entirely (simple, but the threshold is
arbitrary and small libraries get none of this fix's benefit), a smooth
blend between weighted and flat scoring that phases in as `N` grows (no hard
cliff, but still has an arbitrary saturation constant), and additive/
Laplace-style smoothing inside the formula itself (`1 - log(df+k)/log(N+k)`,
softens but doesn't eliminate small-N sensitivity, still needs `k` chosen).
None of these are adopted yet. Real scan data already exists across a
genuine size range (8, 44, 80, and 2,526 ROMs, from earlier profiling this
session) — the plan is to implement the plain formula (with only the `N ≤
1` crash-guard above, which is unconditional regardless of this question)
and run it against that same range before deciding whether any of the three
options is actually needed, rather than picking one speculatively.

## Alternatives considered

- **Weight only the numerator, leave denominators as plain counts**:
  rejected — breaks the "identical sets score 100%" guarantee, as derived
  above.
- **Separate ROM-corpus and image-corpus frequency tables**: rejected —
  creates an unresolvable ambiguity for which weight applies to a token
  shared between the two corpora, without a principled tie-break rule.
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
  why. That's a problem to solve directly (see "Small-corpus behavior"
  above), not one to defer to user choice.

## Consequences

Needs a document-frequency source for ROM tokens (new, a lightweight
`Dictionary<string, int>` pre-pass), not just image tokens (existing, from
`BuildImageIndex`). `SimilarityScorer.ScorePercent`'s signature changes to
accept a weight lookup rather than working from raw set sizes alone. Must be
validated across the same range of real library sizes already profiled this
session before being treated as final, per the "Small-corpus behavior"
section above. Implementation happens on `experiment/tfidf-weighting` (see
ADR-0001) so it can be compared against real scans before merging into
`main`.

## Validation

Verified two ways before merging:

- **Formula check, in isolation**: `SimilarityScorer.ScorePercent(a, a,
  weight)` for a non-trivial `weight` function still returns exactly 100%,
  confirming the both-sides-weighted derivation holds in code, not just on
  paper. A hand-picked weight (a common token down-weighted to 0.1) dropped
  `{rockman, 2}` vs. `{ducktales, 2}` from the unweighted 50.0% to 9.1%,
  confirming the mechanism suppresses a shared generic token as expected.
- **Full pipeline, synthetic corpus**: a temporary self-test (removed after
  confirming) built a small on-disk library reproducing the original false
  positive — "Rockman 2" alongside several unrelated "X 2"/"X II" titles
  (DuckTales 2, RoboCop 2, Sangokushi II, Shanghai II, Terminator 2,
  Kyonshiizu 2, Zelda II) whose art also kept its sequel number, plus enough
  single-copy unrelated titles that a franchise name is genuinely rare. With
  `DisregardRomTags` off (so the one computation IS the weighted candidacy
  score, not the separate unweighted display score), the true match
  ("Rockman" art) scored 96.1% while every numeral-only collision scored a
  flat 14.1% — a clear, wide separation, up from what would have been a much
  narrower 75%-vs-50% gap without this ADR. With `DisregardRomTags` on (the
  realistic default) and a real threshold of 25 between those two clusters,
  the scan's results for "Rockman 2" contained exactly one candidate — the
  true match — confirming the weighted candidacy gate actually excludes the
  false positives outright, even though the number displayed to the user
  (the deliberately-unweighted tag-inclusive score) doesn't itself change.

Not yet done: running this against the real ROM libraries already profiled
this session (8, 44, 80, 2,526 ROMs — see the "Small-corpus behavior"
section) to check for the small-N sensitivity that section anticipates. The
synthetic corpus above (30 documents total) is itself on the smaller end,
and didn't show obviously erratic behavior, but a synthetic corpus can't
substitute for the real thing.
