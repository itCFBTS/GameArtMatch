# 5. Normalize "N-in-1" compilation-cart phrasing

## Status

Proposed — needs a research pass before the decision itself can be
finalized; queued after ADR-0003 and ADR-0004

## Context

Found while validating ADR-0003 against the real NES library: the 53-ROM
newly-missing list is full of multi-game compilation carts, and their
naming is wildly inconsistent for what's the same underlying concept —
"3-in-1", "3 in 1", "5-in-1 (1993)", "Poker III - 5 in 1", "Caltron - 6 in
1", "Pokemon 4-in-1", and (per the user) likely also "4 Games in 1" and
other phrasings not yet cataloged. A ROM and its correct compilation-cart
box art can use two different phrasings of the exact same "N games, one
cartridge" idea, which today tokenize as unrelated words (e.g. "in" is
likely a dropped common word, "games" is not, so "4-in-1" and "4 Games in
1" don't tokenize the same way) — a phrasing-variant problem, similar in
spirit to the spelling-variant problem found in ADR-0003's validation
(Xevious/Gump, Arumana/Armana) but about word choice rather than spelling.

## Decision (not yet finalized)

Likely shape: recognize "N-in-1"-style phrasing (however it's actually
written across this corpus) and canonicalize it to one token, the same
pattern already used for Roman numerals (ADR-0002) and region words
(RegionCatalog) — so "3-in-1" and "3 Games in 1" tokenize identically
instead of drifting apart. Before writing the actual recognition rule,
this needs the same kind of cataloging pass that built up
RegionCatalog/TagWordCatalog in the first place: survey the real ROM/image
naming conventions in use (No-Intro, TOSEC, Redump, and whatever
convention this art pack itself follows) for every phrasing actually seen,
rather than guessing a regex from a couple of examples and missing common
variants.

## Consequences (anticipated)

Should improve matching for compilation-cart titles specifically; low risk
of interfering with anything else, since "N-in-1" phrasing is a fairly
distinctive pattern unlikely to appear by coincidence in an unrelated
title. Scope is genuinely unknown until the research pass happens — this
ADR intentionally doesn't commit to a specific recognition rule yet.
