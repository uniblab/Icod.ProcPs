#!/usr/bin/env sh
set -eu

clean()
{
    printf '\n=== Clean ===\n'
    dotnet clean Icod.ProcPs.sln -c Debug
}

restore()
{
    printf '\n=== Restore ===\n'
    dotnet restore Icod.ProcPs.sln
}

build()
{
    printf '\n=== Build ===\n'
    dotnet build Icod.ProcPs.sln -c Debug --no-restore
}

test()
{
    printf '\n=== Test ===\n'
    dotnet test Icod.ProcPs.sln \
        -c Debug \
        --no-build
}

pack()
{
    printf '\n=== Pack ===\n'
    dotnet pack Icod.ProcPs.sln \
        -c Debug \
        --no-build \
        --output artifacts
}

validate()
{
    printf '\n=== Validate ===\n'
    sh ./.github/scripts/verify-release-package.sh artifacts Debug
}

case "${1-}" in
    "")
        clean
        restore
        build
        test
        pack
        validate
        ;;

    clean)
        clean
        ;;

    restore)
        restore
        ;;

    build)
        build
        ;;

    test)
        test
        ;;

    pack)
        pack
        ;;

    validate)
        validate
        ;;

    *)
        printf 'Invalid section: %s\n' "$1" >&2
        printf 'Usage: %s [clean|restore|build|test|pack|validate]\n' "$0" >&2
        exit 1
        ;;
esac
