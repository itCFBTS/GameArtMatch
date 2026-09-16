# Architecture Decision Records

An ADR captures one significant, debatable design decision: the context that
prompted it, the decision itself, the alternatives considered and why they
were rejected, and its consequences. Git commit messages capture *what*
changed; these capture *why*, and what else was on the table at the time.

A later decision that changes an earlier one is written as a **new** ADR that
explicitly supersedes it, rather than editing the old one in place — the goal
is a readable trail of how a design evolved, not just its current end state.

An idea that hasn't been decided yet doesn't belong here — try it on its own
git branch (e.g. `experiment/<name>`) first. An ADR records a decision that's
been made (even if implementation is still pending); a branch holds the
working code being evaluated to help make one.

## Index

- [0001 — Record architecture decisions](0001-record-architecture-decisions.md)
- [0002 — Normalize Roman-numeral/arabic tokens instead of aliasing both forms](0002-normalize-numeral-tokens-instead-of-aliasing.md)
- [0003 — Weight similarity scoring by corpus token frequency (TF-IDF-style)](0003-corpus-frequency-weighted-similarity-scoring.md)
