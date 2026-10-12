#!/usr/bin/env bash
# Release version preflight, taskly-style: the version lives in one place
# (VERSION) and every other carrier must agree with it.
#
#   scripts/check-release-version.sh [tag]
#
# Checks VERSION == rivet.rktd version, and, when a tag argument is given,
# that the tag (v-prefixed or bare) matches too.
# Runs in CI and in the release pipeline's validate job.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(tr -d '[:space:]' < "$ROOT/VERSION")"
TAG="${1:-}"

fail() {
  echo "release preflight: $*" >&2
  exit 1
}

[[ -n "$VERSION" ]] || fail "VERSION is empty"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]] || \
  fail "VERSION '$VERSION' is not a supported semantic version"

if [[ -n "$TAG" ]]; then
  TAG_VERSION="${TAG#v}"
  [[ "$TAG_VERSION" == "$VERSION" ]] || \
    fail "tag '$TAG' does not match VERSION '$VERSION'"
fi

# The Rivet app manifest carries the product version on this tree.
RIVET_RKTD_VERSION="$(grep -o '"[0-9]*\.[0-9]*\.[0-9]*"' "$ROOT/rivet.rktd" | head -n1 | tr -d '"')"
[[ "$RIVET_RKTD_VERSION" == "$VERSION" ]] || \
  fail "rivet.rktd version '$RIVET_RKTD_VERSION' does not match VERSION '$VERSION'"

echo "release preflight: version $VERSION is aligned (VERSION == rivet.rktd)"
