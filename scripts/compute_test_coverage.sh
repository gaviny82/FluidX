#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "${script_dir}/.." && pwd)"

cd "${repository_root}"

dotnet test tests/FluidX.TextBuffers.Tests/FluidX.TextBuffers.Tests.csproj --no-restore -- \
  --coverage \
  --coverage-output-format cobertura \
  --coverage-output textbuffers.cobertura.xml \
  --results-directory tests/FluidX.TextBuffers.Tests/TestResults/Coverage \
  --no-progress

dotnet test tests/FluidX.Common.Tests/FluidX.Common.Tests.csproj --no-restore -- \
  --coverage \
  --coverage-output-format cobertura \
  --coverage-output common.cobertura.xml \
  --results-directory tests/FluidX.Common.Tests/TestResults/Coverage \
  --no-progress

dotnet test tests/FluidX.TextModels.Tests/FluidX.TextModels.Tests.csproj --no-restore -- \
  --coverage \
  --coverage-output-format cobertura \
  --coverage-output textmodels.cobertura.xml \
  --results-directory tests/FluidX.TextModels.Tests/TestResults/Coverage \
  --no-progress

printf '%s\n' \
  "Coverage reports:" \
  "  tests/FluidX.TextBuffers.Tests/TestResults/Coverage/textbuffers.cobertura.xml" \
  "  tests/FluidX.Common.Tests/TestResults/Coverage/common.cobertura.xml" \
  "  tests/FluidX.TextModels.Tests/TestResults/Coverage/textmodels.cobertura.xml"
