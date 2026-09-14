#!/bin/sh
DIR="$(cd "$(dirname "$0")" && pwd)"
exec dotnet "$DIR/Ncode.Editor/bin/Debug/net8.0/Ncode.Editor.dll" "$DIR" "$@"
