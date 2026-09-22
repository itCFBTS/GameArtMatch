# 6. Parse titles into base title / series index / remainder and score them field by field

## Status

Proposed — decided in discussion (2026-09-22), implementation not started.
Builds on ADR-0002 (numeral canonicalization) and ADR-0003 (corpus
weighting) rather than superseding either; see "Relationship to ADR-0003"
below for exactly what changes in 0003's scope.

## Context

Today a title is one flat token set (`NameNormalizer.ToTokens`), and
`SimilarityScorer` compares two flat sets. That model has no notion of
*where* in the title a token sits, which produces two failures that can't be
fixed by tuning weights:

1. **A sequel number carries asymmetric evidence, and a per-token weight
   can't express asymmetry.** A *shared* number is weak evidence of the
   same game (every series has a 2), but a *differing* number is strong
   evidence of a different game. ADR-0003 damps common numbers so "Sonic 2"
   doesn't match "Streets of Rage 2" off the shared "2" — but the same low
   weight applies when the number differs, so "Super Mario Bros" vs
   "Super Mario Bros 3" scores close to 100 and the exact match becomes
   nearly indistinguishable from its wrong sibling. Raising the weight brings
   the coincidental-"2" false positives back. One weight cannot serve both
   directions.

2. **The absence of a number is invisible.** "Super Mario Bros" has no token
   that "Super Mario Bros 2" lacks, so the mismatch is one-sided: one side
   merely has an extra token. Unweighted, that pair scores 87.5, not the
   symmetric 75 it would score if the first title carried an explicit "1".

A survey of the real library (17,844 ROM files, 39,311 art files, tags
stripped) shows the structure this ADR relies on is common, not a corner
case:

| | ROMs | Images |
|---|---|---|
| Titles containing the `" - "` separator | 3,522 | 12,229 |
| Titles ending in an arabic number | 1,373 | 3,654 |
| Titles ending in a Roman numeral | 510 | 1,144 |
| Titles of the form `... <index> - <text>` | 819 | 2,557 |

No-Intro and Redump encode a title's colon as `" - "` because colons are
illegal in filenames, so that separator is a reliable structural boundary
that the current tokenizer erases (hyphen → space in `PunctuationRules`).

### Terminology used here

- **Title** — the whole name with `(...)`/`[...]` tags stripped.
- **Base title** — the text before the series index (or the whole title when
  there is none). Deliberately *not* called "series title": the parser only
  knows it found text before a number, not that the text names a series
  ("Sydney 2000" has a base title, not a series).
- **Series index** — the installment number: "3", "III", "Vol. 3" all
  denote the same value. Not the same thing as the *numeral* (the token as
  written), which ADR-0002 already canonicalizes.
- **Remainder** — everything after the index, as an ordered list of
  `" - "`-separated segments. Holds the **subtitle** (a secondary title
  naming a distinct game: "Wing Commander III - *Heart of the Tiger*") and
  any **variant** text (an edition/release qualifier of the same game:
  "Championship Edition", "Director's Cut", "White Version", "Turbo").

Subtitle and variant are deliberately *not* separate fields — see
"Alternatives considered."

## Decision

### 1. Parse each title into three fields

```
record TitleStructure(
    HashSet<string> BaseTokens,      // tokens of everything before the index
    int?            SeriesIndex,     // null = no index found; compared as 1
    HashSet<string> RemainderTokens, // tokens of everything after the index
    bool            ParsedCleanly);  // false => caller must use the flat score
```

Parse steps:

1. Strip tags (existing `RemoveBetween` for both bracket types). Tags are
   never seen by the title parser; the tag pipeline (RegionCatalog,
   TagWordCatalog, TagCategorizer, docs/catalog) is untouched by this ADR.
2. Split on `" - "` into segments.
3. Find the **first** series index, scanning every segment word by word (it
   is not always in the first segment: "Mega Man - Battle Network 3 - White
   Version"). A candidate is an arabic number, a Roman numeral (reusing
   `NameNormalizer`'s lookalike guard for CD/DC), or `Vol.`/`Part`/`Episode`
   followed by a number — and is **not a false friend**:
   - a year: `1942`, `1994`, `'94`, `2000`–`2029`;
   - a platform/dimension suffix: `64`, `32X`, `3D`;
   - an ordinal: `2nd`, `3rd` ("2nd Impact", "3rd Strike" are subtitle text,
     not indices — only cardinal numerals count).
   The false-friend list is a literal catalog in the same "extend as real
   data turns up more, don't guess ahead of it" spirit as RegionCatalog.
4. **Base title** = everything before the index, dashes included ("Mega Man -
   Battle Network"). **Remainder** = everything after it, including text in
   the same segment as the index ("Special Edition" in "Super Turrican 2
   Special Edition") followed by all later segments. With no index, the first
   segment is the base title and the rest is the remainder.
5. Tokenize each field with the existing `ToTokens` rules (punctuation,
   common words, standalone letters, numeral canonicalization) so the
   per-field sets compare the way whole-name sets do today.

`ParsedCleanly` is false when the base title yields no tokens; the caller
then falls back to the existing flat score for that pair.

### 2. Score field by field

```
baseScore       = ScorePercent(a.BaseTokens, b.BaseTokens, weight)   // ADR-0003 weighting

indexFactor     = (a.SeriesIndex ?? 1) == (b.SeriesIndex ?? 1) ? 1.0 : IndexMismatchFactor

remainderFactor = both empty                    -> 1.0
                  exactly one empty             -> OneSidedRemainderFactor
                  both present                  -> lerp(RemainderMismatchFloor, 1.0,
                                                        ScorePercent(a.Remainder, b.Remainder) / 100)

score           = baseScore * indexFactor * remainderFactor
```

The three constants are the tunable surface (placeholders: 0.6 / 0.9 / 0.6).
What each rule encodes:

- **Base title** carries most of the score. If base titles don't agree,
  nothing else matters. Corpus weighting applies here, doing the job it was
  designed for (a shared rare word beats a shared common one).
- **Series index is an equality check, not a token.** Equal contributes
  nothing extra (it's expected); unequal applies a fixed penalty regardless
  of how common the number is. This is the asymmetry a weight can't express.
  A missing index is treated as 1, so "Super Mario Bros" vs "Super Mario
  Bros 2" is a real two-sided mismatch rather than one side having an extra
  token. The implicit 1 lives in the comparison, not as an emitted token —
  as a token it would be the most common in the corpus and ADR-0003 would
  weight it to nothing.
- **Remainder** is softer. One side empty is a mild penalty because the best
  available art for "Resident Evil" may genuinely be "Resident Evil -
  Director's Cut" and should still rank first, just visibly imperfect. Both
  present and different is a stronger penalty because "New Generation" vs
  "3rd Strike" really is a different box.

Worked pairs:

| ROM | Image | Base | Index | Remainder |
|---|---|---|---|---|
| Super Mario Bros | Super Mario Bros 3 | match | 1 vs 3 → penalty | both empty |
| Street Fighter III - 2nd Impact | Street Fighter III - 3rd Strike | match | 3 = 3 | differ → penalty |
| Resident Evil | Resident Evil - Director's Cut | match | 1 = 1 | one empty → mild |
| Super Turrican 2 Special Edition | Super Turrican 2 - Special Edition | match | 2 = 2 | same tokens → match |

The last row is why subtitle and variant share one bucket: the dash
placement differs across the pair but the remainder tokens are identical, so
it scores as the same product.

### 3. Roll out display-score first, then candidacy

The matching pipeline is already two computations: **candidacy** (inverted
index on tokens → flat weighted score → accuracy threshold) and the
**display score** over candidates that passed. The structured score lands in
the display score first, with candidacy left exactly as it is:

```
displayScore = romStruct.ParsedCleanly && imageStruct.ParsedCleanly
    ? StructuredScore(romStruct, imageStruct, weight)
    : existingFlatDisplayScore;
```

Rationale: a parser bug in the display path reorders candidates or shows an
odd percentage; the same bug in candidacy silently removes a ROM's only
candidate — the failure ADR-0003's validation spent its effort on. Keeping
candidacy flat means this change cannot lose anyone a match while the parser
and the three constants are tuned against the real library.

Once validated, candidacy moves to the structured model as a second step:

- **Inverted index keys on base-title tokens only.** A candidate with no
  base-title overlap is never a real match; indexing remainder tokens would
  mostly pull in every image containing "edition".
- **The series index is not an index key.** It's a post-lookup factor: a
  ROM with index 3 still wants the index 1 and 2 siblings as candidates,
  scored lower.
- **The accuracy threshold applies to `baseScore`, not the product.**
  Candidacy asks "is this the same series"; the factors then rank "is this
  the same entry in it." Thresholding the product would let an index
  mismatch push a genuine sibling below the gate.
- **Corpus frequencies are counted per field.** A word like "version" is
  common in remainders and rare in base titles; one blended weight is wrong
  for both.

At that point the flat score survives only as the `ParsedCleanly == false`
fallback.

### Relationship to ADR-0003

Corpus weighting is **kept and narrowed**, not replaced. It continues to
answer "which words of a title are distinctive" within the base title and
within the remainder. It stops being asked to do two things it can't: stand
in for the series index (now the equality check) and blend fields (now
per-field frequencies). ADR-0003's validation recorded 16 genuine
regressions where the weighting was being pushed onto structural problems
(sequel numbers, compilation carts); moving those into the parser should let
the weighting be tuned less aggressively, since it no longer carries them.
Whether that actually recovers any of those 16 is a validation question,
not a claim.

## Alternatives considered

- **Emit an implicit "1" token when no index is found, keep the flat
  model.** Gets the symmetric penalty in the *unweighted* display score with
  no scorer change, and was the first idea. Rejected as the primary
  mechanism: it still needs the full parser (you can only emit "1" once you
  know the title has no index, which means classifying every trailing number
  against the false-friend list), and under ADR-0003 weighting "1" would be
  the most common token in the corpus and be weighted to ~0, cancelling the
  penalty exactly where candidacy needs it.
- **Four fields: separate subtitle and variant.** Rejected for now. The
  dash gives segments but cannot tell a subtitle segment from a variant
  segment; the only tool is a word catalog (Edition, Version, Cut, Turbo,
  DX, ...) that misfires in known ways ("Yellow Version" is Pokemon's
  *name*, not an edition of "Pokemon"). Variant text also doesn't sit in one
  place ("*Super* Street Fighter II *Turbo*"). And for box-art matching the
  distinction doesn't change the outcome: a subtitle and a variant both get
  their own box, so a mismatch in either should score the same way. Add a
  variant label over the remainder segments only when a scoring rule exists
  that consumes it.
- **Structured score in candidacy from day one.** Rejected as rollout order
  (not as end state) — see §3.
- **Detect version-like words in title text.** Considered because
  conventions could leak "(Rev 1)"-style content outside brackets. Measured:
  47 ROM titles carry a `v1.x` in title text (all homebrew, e.g. "Cosmos
  v1.2"), 29 carry Beta/Demo; on the image side it's 2, both the real
  product name "Family BASIC v2.1". "Edition"/"Version" in titles (310 and
  133 image titles) are title content No-Intro puts there deliberately. So
  the leakage is rare, ROM-side, homebrew-only, and not worth a rule yet.
  Note for whoever adds one: a *word* regex for "Rev" is useless on titles
  (259 hits on "Revenge"/"Revolution"); anchor on the number, never the word.

## Consequences

- Corrects the sequel-number asymmetry that neither ADR-0002 nor ADR-0003
  could: "Super Mario Bros 3" hits 100 against its own art and takes the full
  penalty against "Super Mario Bros", regardless of how many other games have
  a 3; "Sonic 2" gets no help from a shared 2 because the index check only
  ever subtracts.
- Restores the `" - "` boundary as signal instead of erasing it.
- Introduces a parser with a literal false-friend list that will be wrong
  for titles it hasn't seen ("Bomberman '94", "Pilotwings 64", sports
  year-titles). The `ParsedCleanly` fallback bounds the damage to "no better
  than today" for any pair where one side fails to parse; a *wrong* parse
  that still yields base tokens is not caught by it and is what validation
  has to find.
- Tags are untouched. The tag-inclusive display score (`ForceInclude`)
  becomes either a multiplier on top of the structured score or is dropped —
  a separate decision, to be made when ADR-0004 is picked up.
- ADR-0005's "N-in-1" phrasing becomes a base-title normalization concern
  under this model, not a tag one (that phrasing lives outside brackets: only
  7 catalog tags contain it, vs. 40+ ROM titles with "4-in-1" alone).

## Open questions (not decided here)

- **Leading articles.** No-Intro moves "The" to the end with a comma
  ("Cowabunga Collection, The"); other files keep it in front. Both spellings
  are in the library. "The" is already a default common word so it drops
  from tokens either way, but the comma-flip should be normalized in base
  title parsing regardless, and whether "The" should ever carry weight
  (e.g. distinguishing "The Game" from "Game") is unresolved. Backburner.
- **Ordinal indices.** "Dance Dance Revolution 2nd Remix" arguably has index
  2. Excluded for now (ordinals are subtitle text) because "2nd Impact"/"3rd
  Strike" are the more common case; revisit with data.
- **Mismatched brackets.** "Final Fight 3 (NA - Optimized Patch by
  MaxwelOlinda, Rev 1.1]" exists in the real library; the tag-group regex's
  comment says that case doesn't occur. It occurs once and leaks into title
  text. Not worth handling; the comment should be corrected.

## Validation plan

Same discipline as ADR-0003: run the real NES library (2,526 ROMs) before
and after, diff the top candidate per ROM, and hand-review every ROM whose
top pick changed. Additionally check (a) every pair where exactly one side
returned `ParsedCleanly == false`, to size the fallback, and (b) a sample of
titles ending in a number, to catch false-friend gaps. Tune the three
constants against that diff, not by intuition.
