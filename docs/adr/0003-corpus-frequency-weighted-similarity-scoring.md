# 3. Weight similarity scoring by corpus token frequency (TF-IDF-style)

## Status

Implemented and merged to `main`. Validated against a synthetic corpus (see
"Validation" below) and, since then, against the real 2,526-ROM NES library
(see "Real-library validation (NES, 2,526 ROMs)" below). The real-library
run confirms the formula does fix real instances of the motivating
false-positive problem, but it is **not a strict improvement**: of the 53
ROMs that lost their only candidate when switching from unweighted to
weighted scoring, 16 are genuine regressions (a real match scored below
threshold, not a false positive removed), 24 are correct exclusions (the
false positive this ADR targets), and 13 are too ambiguous to call either
way without playing the ROM. Real-world corpora — where the "same real
game" can legitimately recur many times as region/revision/pirate-cart
duplicates — violate the implicit assumption that a common token is common
*because* it's uninformative; sometimes it's common because the one true
match has a lot of near-duplicate copies.

**Decision to merge as-is**: weighed as net progress rather than a strict
improvement — 24 real false positives fixed clearly outweighs 16 real
regressions (plus 13 unresolved either way) — so this merges to `main`
without waiting on a mitigation for the "same game, many corpus copies"
gap. That gap remains open: a "title cluster" concept (grouping
region/revision/hack/pirate variants of the same underlying game before
counting document frequency, so N copies of one game count once rather than
N times) is the leading candidate fix, discussed but **not yet scoped or
decided** — a future ADR's job if pursued.

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

Running this against the real ROM libraries already profiled this session
(8, 44, 80, 2,526 ROMs — see the "Small-corpus behavior" section), to check
for the small-N sensitivity that section anticipates, was deferred at the
time this was written. It has since been done for the 2,526-ROM case — see
"Real-library validation (NES, 2,526 ROMs)" below. The synthetic corpus
above (30 documents total) is itself on the smaller end, and didn't show
obviously erratic behavior, but as suspected, it didn't substitute for the
real thing: the real run surfaced a failure mode the synthetic corpus never
could have (see below).

## Real-library validation (NES, 2,526 ROMs)

Ran the real user's NES library (2,526 ROMs after existing-art filtering,
against the real `~/.config/GameArtMatch/settings.json` paths, `Disregard
RomTags=true`, `AccuracyThreshold=65` — both compiled `MatchSettings`
defaults, not persisted) through `FindMatchesAsync` on both `main`
(unweighted) and `experiment/tfidf-weighting` (weighted), via a temporary
headless CLI mode and a temporary candidate-score trace hook (both
reverted after this validation — see this ADR's git history for the exact
diff if needed).

Moving from unweighted to weighted scoring:

- **53 ROMs newly lost their only candidate** (≥1 candidate at threshold 65
  on `main`, 0 on `experiment/tfidf-weighting`).
- **5 ROMs newly gained a candidate** (0 on `main`, ≥1 on
  `experiment/tfidf-weighting`).

### The 5 newly-gained ROMs

Spot-checked, not deeply verified (lower risk — a new candidate appearing
can't silently break an existing correct match the way a lost one can).
All 5 look like plausible, correct matches — no new false positive found:

| ROM | New top candidate | Score |
|---|---|---|
| Eggerland (English Translated by Necrosaro) | Eggerland - Meikyuu no Fukkatsu (Japan) | 66.2 |
| Chip & Dale 3 (Asia) (En) (C-D3) (Pirate) | Chip to Dale no Daisakusen (Japan) | 68.0 |
| Mortal Kombat V Pro (Asia) (En) (Pirate) | Mortal Kombat II (Asia) (En) (Hummer Team) (Blue Version) (Pirate) | 65.1 |
| Ultimate Mortal Kombat 4 (Asia) (En) (Pirate) | Mortal Kombat II (Asia) (En) (Hummer Team) (Blue Version) (Pirate) | 65.9 |
| Gremlins (World) (Aftermarket) (Pirate) | Gremlins 2 - The New Batch (Europe) (Beta) | 65.3 |

The Mortal Kombat and Chip & Dale cases can't be pinned to one exact
numbered entry (common with mislabeled pirate carts — many NES "Mortal
Kombat" pirate carts of any claimed number are actually MK II or MK3
underneath), but the franchise match itself is solid, and "Gremlins" has no
NES release of its own for the pack to be matching against — "Gremlins 2"
is the only game in the corpus it could correctly land on.

### The 53 newly-missing ROMs

Every one individually checked: old top candidate (`main`, unweighted,
uncapped) vs. new top candidates (`experiment/tfidf-weighting`, weighted,
uncapped), judging title identity the way a human spot-checking box art
would. Tally:

| Verdict | Count | Meaning |
|---|---|---|
| REGRESSION | 16 | Old top candidate was the real match; weighting incorrectly pushed it below threshold. |
| CORRECT EXCLUSION | 24 | Old top candidate was a false positive (the ADR's motivating problem); weighting correctly excluded it. |
| AMBIGUOUS | 13 | Genuinely can't call it without playing the ROM — noted explicitly rather than forced. |

**REGRESSION (16) — the ones that matter most:**

| ROM | Old top (main) | New top (experiment) | Why this looks like a real match |
|---|---|---|---|
| Arumana no Kiseki (English Translated, Rev A, by DvD Translations) | Armana no Kiseki (Asia) (Ja) (Co Tung) (Pirate) — 66.7 | same, 55.0 | Same title, alt. romanization (Arumana/Armana); no closer candidate exists. |
| Golf - Japan Course (JP) | Golf (Europe) (Animal Crossing) / other Golf variants — 66.7 | same, 62.3 | FDS add-on disk to the same "Golf" game; no dedicated art exists for the add-on, so the base game's art is the correct fallback. |
| Golf - Special Course (JP) | Golf variants — 66.7 | same, 64.1 | Same as above. |
| The Golf - Bishoujo Classic (JP) | Golf variants — 66.7 | same, 64.9 | Reskin hack of the same base "Golf" game. |
| VS. Super Mario Bros. Home Edition (Hack) v1.1 BMF54123 | Super Mario Bros. (Europe/World/Asia) — 66.7 | same, 62.6 | Literally an arcade-VS.-System hack of Super Mario Bros. |
| Vs. Mighty Bomb Jack 2C03 & Credit Hack - Power cycle to menu | Mighty Bomb Jack (all regions) — 65.0 | same, 63.4 | Arcade VS. hack of the same game. |
| Vs. Super Xevious - GAMP no Nazo | Super Xevious - Gump no Nazo (Japan) — 73.3 | same, 63.3 | Same game (arcade VS. version); "GAMP"/"Gump" is a romanization variant. This is the ADR's own "Super Xevious/Xevious is still basically correct" example. |
| Super Child Bros. 3 (World) (Aftermarket) (Unl) | Super Mario Bros. 3 (all regions) — 75.0 | same, 64.1 | "Child" swapped for "Mario" — reads as a themed hack of SMB3. |
| Super Beta Bros. 3 (World) (Demo) (Aftermarket) (Unl) | Super Mario Bros. 3 (all regions) — 75.0 | same, 64.1 | Same pattern as above; likely a beta-build hack of SMB3. |
| Track + Feel II (World) (Aftermarket) (Unl) | Track & Field II (all regions) — 66.7 | same, 54.1 | "&"→"+", "Field"→"Feel" reads as a pun-title hack of the real game. |
| Super Mario & Sonic 2 (Asia) (Ja) (v1.0) (Pirate) | Super Mario Bros. 2 (all regions) — 75.0 | same, 62.6 | Shares "Super Mario ... 2"; "Sonic" is cosmetic branding on what looks like a Mario-based pirate cart. |
| Super Mario & Sonic 2 (Asia) (Ja) (v1.1) (Pirate) | Super Mario Bros. 2 (all regions) — 75.0 | same, 62.6 | Same as above. |
| Chaoji Zhan Hun - Super Contra 7 (China) (960418) (Pirate) | Super Contra (Japan) — 66.7 | same, 62.4 | Title itself advertises "Super Contra 7" — a common Chinese-pirate-cart pattern of naming the real franchise it's built on. |
| Chaoji Zhan Hun - Super Contra 7 (China) (Pirate) | Super Contra (Japan) — 66.7 | same, 62.4 | Same as above. |
| Yongzhe Dou Elong II - Dragon Quest (China) (980342) (Pirate) | Dragon Quest (Japan) — 66.7 | same, 64.7 | Same pattern — title names "Dragon Quest" directly. |
| Yongzhe Dou Elong V - Dragon Quest (China) (0100382) (Pirate) | Dragon Quest (Japan) — 66.7 | same, 63.4 | Same as above. |

Notice the common thread in most of these: `weight(t)` is low not because
`t` is semantically generic, but because the *same real game* ("Golf",
"Super Mario Bros. 3", "Contra") legitimately recurs many times in the
corpus as region dumps, VS.-arcade hacks, or pirate-cart relabels — each
occurrence adds to `df(t)`, and the formula can't distinguish that from a
token that's common because it's uninformative (a bare "2", "(Japan)").
This is a real gap in the "rare = informative" assumption the whole ADR
rests on, not a small-N artifact — most of these titles are near the
2,526-document end of the corpus, not the small-library end the "Small-
corpus behavior" section worried about.

**CORRECT EXCLUSION (24)** — the old top candidate shared only a generic
token (a bare number, a common connector word, a structural naming pattern
like "N-in-1") with an unrelated title, confirming the weighting worked as
intended:

Zhong Guo Mahjong (Asia) (Unl); 1996 Yingyu CAI 3-in-1 (China) (Unl); LIKO -
Study Cartridge 3-in-1 (Russia) (Subor Keyboard) (Unl); Famimaga Disk Vol. 3
- All 1 (JP); Bishoujo Mahjong Club (JP); Disk Hacker - Version 1.3 (JP);
Professional Mahjong Gokuu (JP); Wardner no Mori (JP) (unrelated game
sharing only the generic "no Mori" = "'s forest" suffix with "Wario no
Mori"); KHAN Games 4-in-1 Retro Gamepak (World) (Aftermarket) (Unl); Nin Nin
(World) (v1.3) (Aftermarket) (Unl); Retro Puzzle Maker (World) (v1.1)
(Program) (Aftermarket) (Unl); Super City Mayor (World) (Global Game Jam
2020) (Aftermarket) (Unl); Xin Yingxiong Zhuan (China) (Aftermarket) (Unl);
Nin Nin (World) (Beta) (Aftermarket) (Unl); Basu The Demon Hunter (World)
(v0.12) (Demo) (Aftermarket) (Unl); Basu The Demon Hunter (World) (v0.13)
(Demo) (Aftermarket) (Unl); Super NeSnake 2 (USA) (Demo) (Aftermarket)
(Unl); Tapeworm - Disco Puzzle (World) (Demo) (Aftermarket) (Unl); Nin Nin
(World) (v1.1) (NESDev Compo 2019) (Aftermarket) (Unl); Nin Nin (World)
(v1.2) (Aftermarket) (Unl); Super City Mayor (World) (NESDev Compo 2019)
(Aftermarket) (Unl); Power Rangers 2 (Asia) (Ja) (Pirate); Wario Land 2
(Asia) (Ja) (JY039) (Pirate) (a Game Boy title unofficially ported — the
old "match", Wagyan Land 2, is an unrelated franchise that only shares
"Land 2"; the correct art doesn't exist in this NES-only corpus); Game Paks
- 4 Games in 1 (Asia) (En) (NC-80) (Pirate).

**AMBIGUOUS (13)** — flagged explicitly rather than forced, per instructions
not to paper over genuine uncertainty:

- **Tantei Jinguuji Saburou - Shinjuku Chuuou Kouen Satsujin Jiken (JP, Rev
  1)** (old top: ...Yokohamakou Renzoku Satsujin Jiken (Japan), 67.0 → 62.0):
  same detective-series franchise name shared, but a different specific
  entry in the series — unclear whether the sibling entry's art is an
  intentional "closest available" fallback or a genuine mismatch.
- **Adventures of Panzer 2, The (World) (v0.6) (Beta) (Aftermarket) (Unl)**
  (old top: Adventures of Lolo 2, 66.7 → 52.1): shares "Adventures of ... 2"
  structure, but "Panzer" (tank) vs. "Lolo" (puzzle-egg character) suggests
  unrelated content behind a similarly-structured title — leaning correct
  exclusion, not confident enough to call it that outright.
- **Power Rangers (Asia) (En) (Pirate)** (old top: Mighty Morphin - Power
  Rangers IV - The Movie, 66.7 → 64.9): full "Power Rangers" phrase shared
  (not a generic word), but the candidate is a different numbered/subtitled
  entry — pirate-cart relabeling makes this plausible either way.
- **Street Fighter II Pro (Asia) (En) (Pirate)** (old top: Street Fighter
  Zero 2 '97, 67.5 → new top Street Fighter V, 59.1): NES "Street Fighter"
  pirate carts are known to frequently reuse identical content under
  different numbered/lettered titles, so any of the top candidates could be
  the same underlying cart.
- **Super Bros. 5 / 6 / 8 (v1.0) / 8 (v1.1) / 9 (Asia) (Pirate)** and
  **Super Mario 15 / 6 / IV / Sister (Asia) (Pirate)** (8 ROMs; old top:
  various Super Mario Bros./World/USA entries, 66.7 → 58–64): these read as
  Mario-adjacent pirate carts (some explicitly keep "Mario," others drop it
  to just "Bros."), but which specific real Mario game (if any single one)
  underlies each pirated, arbitrarily-numbered cart isn't verifiable from
  the filename alone — would need to actually run each ROM.

### Implication

The weighted formula does what ADR-0003 set out to do — 24 of the 53 losses
are exactly the false-positive pattern this ADR exists to fix, and the 5
gains are clean. But 16 real regressions (30% of the losses), concentrated
in exactly the "same real game, many corpus copies" pattern described
above, mean this shouldn't be merged to `main` as a strict drop-in without
first deciding how to handle that pattern — it's a second, real gap
alongside the already-known small-corpus sensitivity, not a small-N-only
concern.
