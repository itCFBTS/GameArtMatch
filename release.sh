#!/usr/bin/env bash
# Cuts a release: stamps the CHANGELOG's "## Unreleased" section with the version and
# today's date, commits "Release vX.Y.Z", creates the annotated vX.Y.Z tag, builds the
# binaries (publish.sh), then — after one last confirmation — pushes main and the tag.
#
#   ./release.sh 0.6.0
#
# The app's version is read from the git tag at build time (see SetVersionFromGitTag in
# GameArtMatch.csproj), which is why the tag is created BEFORE publish.sh runs: built
# any earlier, the binaries would report the previous release's version.
#
# Refuses to start unless: on main, working tree clean, main not behind origin/main,
# the version is new and higher than the latest tag, and CHANGELOG.md has a non-empty
# "## Unreleased" section. Nothing leaves this machine until the final push prompt.
set -euo pipefail
cd "$(dirname "$0")"

die() { echo "release: $*" >&2; exit 1; }

confirm() {
  local reply
  read -r -p "$1 [y/N] " reply
  [[ "$reply" =~ ^[Yy]$ ]]
}

[[ $# -eq 1 ]] || die "usage: ./release.sh X.Y.Z"
version="${1#v}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "version must look like X.Y.Z (got '$1')"
tag="v$version"

# --- Preconditions -------------------------------------------------------------------

branch="$(git rev-parse --abbrev-ref HEAD)"
[[ "$branch" == "main" ]] || die "must be on main (currently on '$branch')"

[[ -z "$(git status --porcelain)" ]] || die "working tree has uncommitted changes — commit or stash them first"

echo "Fetching origin..."
git fetch --quiet --tags origin
behind="$(git rev-list --count HEAD..origin/main)"
[[ "$behind" -eq 0 ]] || die "main is $behind commit(s) behind origin/main — pull first"

if git rev-parse -q --verify "refs/tags/$tag" >/dev/null; then
  die "tag $tag already exists"
fi

latest="$(git tag --list 'v[0-9]*' --sort=-v:refname | head -n1)"
if [[ -n "$latest" ]]; then
  highest="$(printf '%s\n%s\n' "${latest#v}" "$version" | sort -V | tail -n1)"
  [[ "$highest" == "$version" ]] || die "$tag is not higher than the latest tag, $latest"
fi

grep -q '^## Unreleased$' CHANGELOG.md || die "CHANGELOG.md has no '## Unreleased' section"

# The section's body: everything after "## Unreleased" up to the next "## " heading.
notes="$(awk '/^## Unreleased$/ {on=1; next} on && /^## / {exit} on' CHANGELOG.md)"
grep -q '^- ' <<<"$notes" || die "the '## Unreleased' section in CHANGELOG.md has no entries"

# --- Plan ----------------------------------------------------------------------------

today="$(date +%F)"
echo
echo "Releasing $tag (previous: ${latest:-none}), dated $today. Changelog entries:"
echo "$notes" | sed '/^[[:space:]]*$/d'
echo
confirm "Stamp CHANGELOG, commit, tag $tag, and build?" || die "aborted, nothing changed"

# --- Commit + tag (local only) -------------------------------------------------------

sed -i "s/^## Unreleased\$/## $tag - $today/" CHANGELOG.md
git add CHANGELOG.md
git commit --quiet -m "Release $tag"
git tag -a "$tag" -m "$tag"
echo "Committed and tagged $tag locally."

# --- Build ---------------------------------------------------------------------------

# Belt and braces: the build must see this exact tag, or the version will be wrong.
[[ "$(git describe --tags --exact-match)" == "$tag" ]] || die "HEAD isn't exactly at $tag — not building"

if ! ./publish.sh; then
  echo >&2
  echo "release: publish.sh failed. The commit and tag are still local only. To undo:" >&2
  echo "  git tag -d $tag && git reset --hard HEAD~1" >&2
  exit 1
fi

# --- Push ----------------------------------------------------------------------------

echo
if confirm "Push main and $tag to origin?"; then
  git push origin main "$tag"
  echo "Released $tag."
else
  echo "Not pushed. When ready:  git push origin main $tag"
fi
