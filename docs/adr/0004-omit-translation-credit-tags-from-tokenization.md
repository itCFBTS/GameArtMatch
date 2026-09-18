# 4. Omit translation-credit tag phrases from tokenization

## Status

Proposed — queued to start after ADR-0003 (corpus-frequency weighting) is
validated and merged

## Context

Found while validating ADR-0003 against the real NES library: ROM
"Arumana no Kiseki (English Translated, Rev A, by DvD Translations).fds"
has its translator credit spelled out in a tag. With `DisregardRomTags` on
(the real default), that whole tag is already stripped for the *candidacy*
score — so it wasn't the cause of that specific ROM losing its candidate
(see ADR-0003's Validation section for the actual cause). But it does hurt
the separate *display* score: unlike the stripped candidacy pass, the
tag-inclusive display computation keeps tag content (spelling-canonicalized,
not removed), so words like `"english"`, `"translated"`, `"rev"`, `"a"`,
`"by"`, `"dvd"`, `"translations"` all become real tokens that can never
match anything on the image side (a translation patch doesn't get its own
dedicated box art — it should be scored against the original release's
art). That needlessly drags down the displayed percentage for a ROM that
*did* clear candidacy, for tokens carrying zero matching signal.

This is a different situation from a region word like "USA"/"US": a region
word is genuinely informative (it should match, or genuinely shouldn't) and
just needs spelling normalized. A translator/group credit is never
informative either way — it should be dropped entirely, not
canonicalized-and-kept.

## Decision (tentative — refine when this is actually picked up)

Recognize translation-credit-shaped tag phrases (e.g. "English Translated",
"Translated by X", "Fan Translation", "by X Translations") during
tokenization and omit them entirely — contribute zero tokens, the same way
a region word contributes exactly one canonical token, rather than keeping
the literal words. Likely lives alongside `RegionCatalog`/`TagWordCatalog`
as a similar pattern-recognition catalog, given the existing "recognize a
tag phrase, canonicalize or drop it" infrastructure those two already
provide.

## Consequences (anticipated)

Should improve displayed scores for already-passing candidates whose ROM
name carries a translation credit; does not, by itself, change candidacy
(the stripped pass already excludes tags entirely under the real default
settings) — so it won't recover ROMs like "Arumana no Kiseki" that were
excluded at the candidacy stage. Needs its own scoping pass when picked up:
how to detect the phrase pattern robustly without false-triggering on a
title that legitimately contains one of those words.
