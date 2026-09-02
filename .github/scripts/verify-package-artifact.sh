#!/usr/bin/env sh
set -eu

if [ "$#" -ne 2 ]; then
    printf 'Usage: %s <artifact-directory> <Debug|Staging|Release>\n' "$0" >&2
    exit 1
fi

artifact_dir=$1
configuration=$2

case "$configuration" in
    Debug|Staging|Release)
        ;;
    *)
        printf 'Usage: %s <artifact-directory> <Debug|Staging|Release>\n' "$0" >&2
        exit 1
        ;;
esac

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_root=$(CDPATH= cd -- "$script_dir/../.." && pwd)
cd "$repository_root"

if [ ! -d "$artifact_dir" ]; then
    printf 'Artifact directory "%s" does not exist.\n' "$artifact_dir" >&2
    exit 1
fi

artifact_dir=$(CDPATH= cd -- "$artifact_dir" && pwd)

printf '\n=== Verify packed ProcPs artifact (%s) ===\n' "$configuration"
pwsh -NoLogo -NoProfile -File .github/scripts/verify-package-artifact.ps1 \
    -ArtifactDirectory "$artifact_dir" \
    -Configuration "$configuration"
