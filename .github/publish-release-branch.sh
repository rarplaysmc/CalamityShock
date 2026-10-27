#!/usr/bin/env bash
# Run by .github/workflows/build.yml after a successful build on main.
# Commits the freshly built CalamityShock.dll to the 'release' branch, which contains only the DLL
# (plus a README and a .gitignore), so a server can keep ServerPlugins/ up to date with `git pull`.
set -euo pipefail

DLL="$GITHUB_WORKSPACE/bin/Release/CalamityShock.dll"
URL="https://x-access-token:${TOKEN}@github.com/${GITHUB_REPOSITORY}.git"
REL="$(mktemp -d)"
TEMPLATES="$GITHUB_WORKSPACE/.github/release-branch"

if git clone --quiet --branch release --single-branch "$URL" "$REL" 2>/dev/null; then
  echo "Updating the existing release branch."
else
  echo "No release branch yet; creating it."
  rm -rf "$REL"
  mkdir -p "$REL"
  git -C "$REL" init --quiet -b release
  git -C "$REL" remote add origin "$URL"
fi

cp "$DLL" "$REL/CalamityShock.dll"
cp "$TEMPLATES/README.md" "$REL/README.md"
cp "$TEMPLATES/gitignore" "$REL/.gitignore"

cd "$REL"
git add -A
if git diff --cached --quiet; then
  echo "The DLL did not change; nothing to publish."
  exit 0
fi

VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$GITHUB_WORKSPACE/CalamityShock.csproj" || echo unknown)"
git -c user.name="github-actions[bot]" -c user.email="41898282+github-actions[bot]@users.noreply.github.com" \
  commit --quiet -m "CalamityShock.dll v${VERSION} built from ${GITHUB_SHA:0:7}"
git push --quiet origin release
echo "Published CalamityShock.dll v${VERSION} to the release branch."
