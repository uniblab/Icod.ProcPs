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
package_version=$(dotnet msbuild procps/Icod.ProcPs.Router.csproj -nologo -getProperty:PackageVersion)
if [ -z "$package_version" ]; then
    printf 'Unable to determine PackageVersion.\n' >&2
    exit 1
fi

package_path="$artifact_dir/Icod.ProcPs.$package_version.nupkg"
if [ ! -f "$package_path" ]; then
    printf 'Icod.ProcPs package not found at "%s".\n' "$package_path" >&2
    exit 1
fi

printf '\n=== Verify ProcPs distribution (%s) ===\n' "$configuration"
pwsh -NoLogo -NoProfile -File packaging/VerifyDistribution.ps1 -Configuration "$configuration"
