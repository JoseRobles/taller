#!/usr/bin/env bash
# Run this ONCE before your interview to pre-restore packages.
set -euo pipefail

echo "dotnet: $(dotnet --version)"
dotnet restore
dotnet build --no-restore
echo
echo "✓ Setup complete. On interview day, run:"
echo "    dotnet run"
echo "API will listen on http://localhost:5000 (or whatever ASP.NET reports)."
