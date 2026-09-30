#!/bin/bash
# Builds the Windows bridge (JoggingBleBridge.exe, self-contained, ~35 MB) on macOS or Windows with the .NET 10 SDK.
# Output: Tools/WinBleBridge/bin/publish/JoggingBleBridge.exe – Editor/BuildTools copies it next to Jogging.exe.
set -eu
cd "$(dirname "$0")"
dotnet publish -c Release -o bin/publish -nologo -v q
ls -la bin/publish/JoggingBleBridge.exe
