#!/bin/sh
DIR="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$DIR/Ncode/bin/Debug/net8.0/Ncode.dll" ]; then
  exec dotnet "$DIR/Ncode/bin/Debug/net8.0/Ncode.dll" "$@"
elif [ -f "$DIR/Ncode/bin/Debug/net8.0-windows/Ncode.dll" ]; then
  exec dotnet "$DIR/Ncode/bin/Debug/net8.0-windows/Ncode.dll" "$@"
else
  exec dotnet "$DIR/Ncode/bin/Debug/net8.0/Ncode.dll" "$@"
fi
