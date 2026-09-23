# 7. Tokenize tags as one category-labeled token per clause, weighted by significance

## Status

Implemented on `experiment/tag-clause-tokens` (2026-09-23) and validated
against the real NES library — see "Validation" below. Net effect on top
picks: 9 improvements, 6 neutral reorders, 2 ambiguous, 0 regressions; and
the validation surfaced a pre-existing bug that had left the old tag-inclusive
score broken for every multi-region tag. One placement changed during
validation: **Platform moved from Descriptive to Distinguishing** (the
tables below show the final state). Decided in discussion 2026-09-23.
**Absorbs ADR-0004**: "omit translation-credit tag phrases" is the
Irrelevant-level case of the general rule below, so 0004 is not picked up
separately. Composes with ADR-0006 (title structure) — see "Relationship to
ADR-0006." Leaves ADR-0005 (N-in-1 phrasing) untouched: under 0006 that is a
title-side concern, not a tag one.

## Context

The tag-inclusive **display score** (`NameNormalizer.TagHandling.ForceInclude`,
consumed in `MatchingService` where `displayScore` is computed) exists to show
how close a candidate really is once tags are taken into account — the
candidacy pass strips tags entirely under the default settings, so without it
"Foo (USA)" and "Foo (Europe)" would both show 100 against a USA ROM. Today it
works by canonicalizing region/tag-word spellings inside each `(...)`/`[...]`
group (`CanonicalizeTagWords`), then splitting the whole name into words and
scoring every word — title and tag alike — at weight 1 (the call passes no
`tokenWeight`).

Three problems, all visible in docs/catalog's real vocabulary:

1. **Every tag word counts, whether or not it carries art signal.** A
   translator credit ("English Translated by KingMike's Translations")
   contributes five tokens that can never match anything on the image side
   because a translation patch doesn't get its own box — ADR-0004's
   motivating case. The same is true of language lists ("En,Fr,De,Es,It" =
   five tokens), dated protos ("1994-05-01" = "1994", "05", "01" plus the
   "94" year variant), and hardware-compatibility flags. The catalog shows
   these are overwhelmingly one-sided: e.g. "SGB Enhanced" appears on 743
   images and 0 ROMs, so it can only ever *lower* a score.

2. **Word-splitting halves the penalty a differing tag should pay, and
   collides unrelated tags.** "Rev 1" becomes the two tokens "rev" and "1",
   so a Rev 1 ROM against Rev 2 art still shares "rev" and pays half the
   mismatch. Worse, the "1" from "Disc 1", "Rev 1", "Beta 1", and a title's
   own sequel number are the *same token* in the set, so "Foo (Disc 1)"
   accidentally shares a token with "Foo (Rev 1)".

3. **No notion of which tags matter.** A differing disc is a wrong file
   outright; a differing region is almost always a different box; a differing
   revision is almost never one. The flat model can't tell them apart.

Meanwhile `TagCategorizer` now (as of 2026-09-23) defines exactly the two
things a fix needs: a **rank** over tag categories (`TagCategory` declaration
order) and a coarser **significance** level over that rank
(`TagSignificance`: Decisive / Distinguishing / Descriptive / Irrelevant,
via `SignificanceOf`). Until this ADR, nothing consumed either.

## Decision

### 1. One token per tag clause, labeled with its category

In the ForceInclude path, each `(...)`/`[...]` group is split into clauses
(`TagCategorizer`'s comma / `" - "` split, with the same whole-tag
translation/hack check first so a credit stays one unit), each clause is
classified, canonicalized, and emitted as a **single token of the form
`<category>:<canonical clause>`** — never split into words:

| Raw tag | Emitted tokens |
|---|---|
| `(USA)` / `(US)` / `(NA)` | `region:usa` |
| `(USA, Europe)` | `region:usa`, `region:europe` |
| `(Rev 1)` | `revision:rev 1` |
| `(Disc 1)` / `(Disk 1)` | `disc:1` |
| `(Proto)` / `(Prototype)` | `preview:proto` |
| `(NA - Disc 2)` | `region:usa`, `disc:2` |
| `(English Translated by X)` | *(nothing — see §2)* |
| `(Tengen)` *(NeedsReview)* | `tag:tengen` |

Canonicalization reuses `RegionCatalog.TryCanonicalize` and
`TagWordCatalog.TryCanonicalize`; the category prefix comes from the
clause's `TagCategory`. Title text (everything outside a group) is tokenized
exactly as today and carries no prefix, so a tag token can never collide with
a title word.

### 2. Weight tag tokens by significance in the display score

`SimilarityScorer.ScorePercent` already accepts a per-token weight function
(ADR-0003). The display-score call, which today passes none, passes one keyed
on the token's category prefix:

| Significance | Categories | Weight |
|---|---|---|
| Decisive | Disc, Region | 1.0 — same as a title word |
| Distinguishing | Unofficial, Preview, Revision, Platform | 0.75 |
| Descriptive | Label, Language, Date | **omitted** — not emitted at all |
| Irrelevant | TranslationCredit, HackOrPatchCredit | **omitted** — not emitted at all |
| *(unclassified — NeedsReview clause)* | — | 0.75, emitted as `tag:<clause>` |

Title tokens keep weight 1.0. Descriptive and Irrelevant tokens are omitted
rather than weighted 0: in this scorer a weight-0 token contributes nothing
to numerator or denominator either way, but omission avoids the "set made
entirely of weight-0 tokens scores 0" edge and keeps the emitted set
honest about what's being compared.

Why these values: what matters is *ranking*, and at 1.0 the right disc or
region already beats the wrong one whenever the rest of the name agrees — a
higher Decisive weight would only make wrong-disc percentages look worse
while starting to swamp genuine title mismatches. Distinguishing at 0.75
still separates a Rev 1 ROM from Rev 2 art (now a full clause-token
mismatch, not half of one) but lets a plain ROM score closer to 100 against
Rev 1 art than against another region's. Descriptive is dropped *for now*
rather than given a small weight: start from "no effect," and promote a
category only when the validation diff shows ties that should have been
ordered. That happened once, during this ADR's own validation: Platform
started in Descriptive and moved to Distinguishing — see "Validation".

Unclassified clauses are **kept**, at Distinguishing weight, not dropped.
The NeedsReview pile holds real signal ("Tengen", "Namcot Collection",
"Whirlwind Manu" all appear on both sides) alongside one-sided noise ("SGB
Enhanced"); dropping it wholesale would silently discard the former, and
0.75 is strictly no worse than today's 1.0 for the latter. The catalog's
triage loop — promote a NeedsReview clause into a real category as evidence
accumulates — is how that noise gets removed, one category at a time, with
the significance table deciding its fate when it lands.

### 3. Scope: display score only

Candidacy is unchanged. Under the default `DisregardRomTags = true` it
already strips tags before indexing and thresholding, so this ADR cannot
add or remove a candidate — same containment ADR-0004 planned. (With
`DisregardRomTags = false`, candidacy tokenizes tags as literal words today
and continues to; that path is out of scope here.) `RegionFilter` is a
display filter over raw filenames and is unaffected.

### Relationship to ADR-0006

ADR-0006 left open whether the tag-inclusive display score "becomes a
multiplier on top of the structured score or is dropped." This ADR decides
it is kept and becomes **a fourth field**: once 0006's parser lands, tag
clause tokens are scored as their own weighted set (`tagFactor`, same
lerp-with-floor shape as 0006's remainder factor) and multiplied into the
structured score, rather than mixed into the base-title set. Until 0006
lands, the interim implementation is the simpler one: title tokens (weight
1.0) and tag clause tokens (weighted per §2) in **one** set, scored by the
existing display call with the weight function attached. Both shapes use the
same tokens and the same weights; only where the multiplication happens
differs.

## Alternatives considered

- **Keep word tokens, just weight them.** Rejected — doesn't fix the
  half-penalty ("rev" still shared when the number differs) or the numeric
  collision ("1" from Disc/Rev/Beta/title all one token). Clause tokens fix
  both for free, since `Categorize` already splits and classifies clauses.
- **Descriptive at a small weight (0.25).** Deferred, not rejected. Starting
  from omitted gives a clean baseline; the diff will show whether any
  Descriptive category is doing ranking work that's now missing.
- **Drop unclassified (NeedsReview) clauses.** Rejected — loses "Tengen"-type
  signal that's on both sides and has no other home yet. Kept at 0.75 and
  reduced through catalog triage instead.
- **A user-facing setting for the weights.** Rejected — the existing
  matching options are FatMatch-lineage checkboxes users understand
  ("disregard tags", "match case"); "Distinguishing weight = 0.75" is not a
  knob a user can reason about. Tune against the validation diff, ship as
  constants.
- **Apply the same weighting to candidacy.** Rejected as scope, not as end
  state, for the reason 0006 gives: a bug here reorders candidates, the
  same bug there removes a ROM's only candidate.

## Consequences

- Translation and hack credits stop dragging displayed scores — ADR-0004's
  intended effect, delivered by the general rule.
- A differing revision, disc, or region pays its full penalty as one token
  instead of half of one; unrelated tags no longer share a number token.
- Language, date, and label tags stop affecting the display score entirely.
  (Platform was going to be in that list; validation moved it — see below.)
- Unclassified tags remain a penalty source, so the size of the NeedsReview
  pile now has a (small, 0.75-weighted) effect on displayed numbers. That
  makes the catalog triage worth doing, which is the point.
- Tag tokens gain a prefix, so anything that inspects ForceInclude token sets
  by raw string (nothing does today; the `--selftest` in ADR-0002 was
  removed) would need updating.
- Region clauses like "USA, Europe" emit two region tokens; a USA ROM against
  "(USA, Europe)" art shares one of two and pays a small penalty, which is
  the right ranking (the USA-only art, if present, wins) and no worse than
  today.

## Open questions (not decided here)

- **Canonical form for Disc clauses.** "Disc 1", "Disk 1", "Disc-B", "Side
  A", "Game Disc" — the table above assumes Disc/Disk fold together and the
  number/letter is the canonical value; the rest needs a look when
  implementing.
- ~~"Alt 1" / "Alt 2"~~ — `RevisionPattern` now matches them; landed with
  the implementation.
- **Weight for unclassified clauses** — 0.75 is a default, not a measured
  value.

## Validation plan

Same discipline as ADR-0003 and ADR-0006: real NES library, before and
after, diff the top candidate per ROM and every displayed score that moved
by more than a few points; hand-review every ROM whose top pick changed.
Specifically look for (a) ROMs whose top pick changed *because* a
Descriptive tag was dropped — the signal for promoting a category — and (b)
ROMs whose top pick is now a tie that used to be ordered. Tune the two
constants (0.75 for Distinguishing, 0.75 for unclassified) against that
diff.

## Validation (NES, 315 ROMs with candidates, 1,362 candidate pairs)

Run via the `--scan` dev CLI added for this purpose (real settings.json
paths, Console Mode with existing-art skip, ignore list applied, compiled
defaults for matching options, threshold 65) on the branch immediately
before and after the change, then diffed per ROM and per pair. Candidacy
is untouched, so the candidate set was identical in both runs (1,362 pairs
across 315 ROMs; 2,211 ROMs with none) — only displayed scores and
ordering moved.

### A pre-existing bug this exposed

Dumping the old `ForceInclude` tokens on `main` showed that the previous
tag-inclusive path never stripped the brackets: "(Japan, USA)" tokenized as
`(japan` and `usa)`, and "(Japan)" as `(japan)`, so a region word inside a
multi-region tag could never match the same region on the other side, and
"(En)" was matching `(en)` only by luck of being a one-word group. The old
display score was therefore already wrong for every compound tag — e.g.
"Konamic Tennis (JP)" vs "Tennis (Japan, USA) (En)" scored 29.2 when the
region actually agrees. This is why the bulk of pairs moved up (below):
regions that should have matched all along now do.

### Pair-level movement

| | Count |
|---|---|
| Pairs whose displayed score changed | 1,311 of 1,362 |
| … up | 1,140 |
| … down | 171 |
| Largest gains | +37 to +40 — translation-credit ROMs ("Yume Koujou - Doki Doki Panic (English Translated, Rev 1.1, by Vice Translations)" 40.4 → 80.8; "Eggerland (English Translated by Necrosaro)" 20.0 → 60.0) and compound-tag ROMs ("Fruits Mahjong 3 (Disk 1 - Yonin no Tenshi Tachi - JP)" 30.0 → 67.4) |
| Largest drops | −12 to −21 — a language list that used to match on both sides no longer counts ("Wonderland Dizzy (World) (En,Fr,Es,Nl,Pt,Pl) (Aftermarket) (Unl)" vs its Europe Proto art 81.8 → 61.1, which is honest: different region, different build), and multi-game pirate carts vs single-game art losing the shared "(En)" |

### Top-candidate changes (17 ROMs)

| Verdict | Count | ROMs |
|---|---|---|
| IMPROVEMENT | 9 | Rockman IV / VI (Taiwan) now land on *Rockman 4* / *Rockman 6* instead of *Rockman*; "Super Bros. 2" and "Super Mario Bros. II+" now pick *Super Mario Bros. 2* instead of *1*; "Mortal Kombat III Super Special" now *Mortal Kombat 3* instead of *II*; "Super Contra 8" now *Super Contra* instead of *Contra*; "Konamic Tennis (JP)" now *Tennis (Japan, USA)* (66.7) instead of *Tennis (Europe)* (41.7); "Final Fantasy - Taikong Zhanshi V (v1.1)" now the plain *Final Fantasy (Japan)* over *(Rev 1)*; "Dragon Ball Z - Super Butouden 2 (v1.0)" now the un-versioned Taiwan art over *(v1.1)* |
| NEUTRAL | 6 | Region-mismatched Asian pirate carts whose old top pick won only on a shared "(En)": Bishoujo Sexy Puzzle, Double Dragon V, Ghostbusters III, Over Horizon, Puzzle Boys, Super Bros. 10 - Kung Fu Mari. Now a tie among equally-wrong-region candidates, broken by filename order; no candidate is more correct than another |
| AMBIGUOUS | 2 | "Super 4-in-1 - Fantasy Gun" (*4-in-1* → *Super Gun*), "Super Contra 3-in-1" (*Contra* → *3-in-1*) — compilation carts, ADR-0005 territory |
| REGRESSION | 0 | |

### Platform: Descriptive → Distinguishing

The first run had Platform in Descriptive (omitted), per the original
table. The diff showed exactly the pattern §2 said to watch for, seven
times: "Foo (Europe) (Virtual Console)" and "Foo (Europe)" tied, and the
Virtual Console file won on filename order ("(Europe) (V…" sorts before
"(Europe).jpg") — Double Dragon, Ice Hockey, Lode Runner ×2, Rockman 4,
Mario Bros. (e-Reader) — and in one case the omission caused a genuine
regression: "Super Mario Bros. 2j (USA) (RetroZone) (Aftermarket) (Pirate)"
lost *Super Mario Bros. 2 (USA)* (67.6) to the short *Mario Bros. (USA)
(e-Reader)* (70.7), because with "e-Reader" contributing nothing the image
side's three tokens were fully contained in the ROM's. Moving Platform to
Distinguishing (0.75) resolved all seven in favor of the plain release and
restored the SMB2 pick; 104 pairs moved, all down (a Platform tag on one
side now costs 0.75), no other top pick changed.

### Ties

ROMs whose top candidates tie rose from 69 to 111. Inspected: the new ties
are (a) the region-mismatched pirates above, where the tie is correct, and
(b) images differing only in a Language tag — "Contra (Asia) (En)" vs
"Contra (Asia) (Ja)" — which is the deliberate consequence of Language
being Descriptive. If (b) turns out to matter, Language is the next
candidate for promotion, by the same evidence rule Platform followed.

### Not measured

Only NES. Disc tokens (`disc:1`) were checked by token dump, not by a
multi-disc system scan; PSX/Saturn is the obvious next validation set.
