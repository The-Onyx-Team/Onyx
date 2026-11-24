#!/usr/bin/env bash
# Prints the next Onyx version (MAJOR.MINOR, no "v") for a push to master.
#
#   next-version.sh [minor|major]
#
# - Normally the minor number goes up by one from the highest v* tag: v0.3 -> 0.4.
# - "major" (or "[major]" in the last commit message) starts the next major version: v0.7 -> 1.0.
# - Setting OnyxVersion in Directory.Build.props above the highest tag (e.g. to 1.0) releases that version next,
#   which is the other way to order a major version.
# - Without any tag, the version in Directory.Build.props is used as it is.
set -euo pipefail

bump="${1:-minor}"
if git log -1 --format=%B | grep -qiF '[major]'; then
    bump=major
fi

props=$(sed -n 's:.*<OnyxVersion>\([0-9][0-9]*\.[0-9][0-9]*\)</OnyxVersion>.*:\1:p' Directory.Build.props | head -1)
props=${props:-0.0}
latest=$(git tag --list 'v[0-9]*.[0-9]*' | sed 's/^v//' | grep -E '^[0-9]+\.[0-9]+$' | sort -t. -k1,1n -k2,2n | tail -1 || true)

if [ -z "$latest" ]; then
    echo "$props"
    exit 0
fi

IFS=. read -r major minor <<< "$latest"
if [ "$bump" = major ]; then
    next="$((major + 1)).0"
else
    next="$major.$((minor + 1))"
fi

# A higher version set by hand in Directory.Build.props wins.
IFS=. read -r pmajor pminor <<< "$props"
IFS=. read -r nmajor nminor <<< "$next"
if [ "$pmajor" -gt "$nmajor" ] || { [ "$pmajor" -eq "$nmajor" ] && [ "$pminor" -gt "$nminor" ]; }; then
    next="$props"
fi

echo "$next"
