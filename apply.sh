#!/usr/bin/env bash
# Card Wars AR: puts this mod on top of a CardWars-Revival checkout.
#   ./apply.sh /path/to/CardWars-Revival
# It applies patches/upstream-changes.patch (changes to existing files) and copies overlay/ (new files).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
target="${1:?usage: ./apply.sh /path/to/CardWars-Revival}"
base=246cef8e86ee74e49f1fe1b4a9f86447c4ff7a1e   # the CardWars-Revival commit this mod was made on

cd "$target"
git rev-parse --is-inside-work-tree >/dev/null 2>&1 || { echo "error: $target is not a git checkout" >&2; exit 1; }
[ -f Assets/Scripts/Assembly-CSharp/SQSettings.cs ] || { echo "error: $target doesn't look like CardWars-Revival" >&2; exit 1; }
if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
	echo "error: commit or stash your changes first" >&2; exit 1
fi
if [ "$(git rev-parse HEAD)" != "$base" ]; then
	echo "note: this mod was made on CardWars-Revival ${base:0:8}; you are on $(git rev-parse --short HEAD)."
	echo "      If the patch doesn't apply, start from that commit:  git checkout -b ar $base"
fi

if git apply --check "$here/patches/upstream-changes.patch" 2>/dev/null; then
	git apply --index "$here/patches/upstream-changes.patch"
else
	echo "The patch doesn't apply cleanly; trying a 3-way merge..."
	git apply --index --3way "$here/patches/upstream-changes.patch"
fi

cp -R "$here/overlay/." .
(cd "$here/overlay" && find . -type f) | sed 's|^\./||' | while IFS= read -r f; do git add -- "$f"; done

echo
echo "Card Wars AR applied (staged, not committed)."
echo "Next: open the folder in Unity 6000.6.7f1 (it resolves AR Foundation 6.6.2) and build for Android or iOS."
