# 1. Record architecture decisions

## Status

Accepted

## Context

GameArtMatch's matching/indexing logic (`NameNormalizer`, `SimilarityScorer`,
`MatchingService`) involves a series of judgment calls — how to tokenize
titles, how to score similarity, how to weight different kinds of tokens —
that are inherently debatable and likely to be revisited as real-world scans
surface new edge cases. Git commit messages capture *what* changed, not *why*
one approach was chosen over another, or which alternatives were considered
and rejected along the way.

## Decision

Use Architecture Decision Records (ADRs), one per significant decision,
stored under `docs/adr/` and numbered sequentially. Each ADR captures the
context, the decision, alternatives considered, and consequences. A later
decision that changes an earlier one is written as a new ADR that explicitly
supersedes it, rather than editing the old one in place — preserving the full
trail of how a design evolved instead of just its current end state.

## Consequences

Anyone (including a future session) working on the matcher/indexer can read
`docs/adr/` in order to understand not just the current behavior but why it
looks the way it does, and what was already tried and rejected. An
experimental implementation of a not-yet-decided idea belongs on its own git
branch (e.g. `experiment/<name>`) rather than in an ADR — ADRs record
decisions and reasoning, branches hold the actual code being evaluated.
