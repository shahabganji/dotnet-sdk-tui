#!/usr/bin/env bash
# run-tests.sh CATEGORY — run one test category and fail if it didn't actually run.
#
# E2E/FullFlow tests skip themselves unless DSM_E2E/DSM_E2E_FULL are set, and a
# filter that matches nothing still "passes" — this guard turns both into failures.

set -euo pipefail

category="$1"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
results="$repo_root/TestResults"
trx="$category.trx"
rm -f "$results/$trx"

dotnet test "$repo_root/tests/DotnetSdkTui.Tests" \
    --configuration Release --no-build \
    --filter "Category=$category" \
    --results-directory "$results" \
    --logger "trx;LogFileName=$trx" \
    --logger "console;verbosity=normal"

counters="$(grep -o '<Counters [^>]*>' "$results/$trx" || true)"
echo "$counters"
executed="$(sed -n 's/.* executed="\([0-9]*\)".*/\1/p' <<<"$counters")"
passed="$(sed -n 's/.* passed="\([0-9]*\)".*/\1/p' <<<"$counters")"
if [[ -z "$executed" || "$executed" -eq 0 || "$passed" -ne "$executed" ]]; then
    echo "::error::Category=$category: ${executed:-0} tests executed, ${passed:-0} passed — expected at least one and all to pass (were they skipped?)"
    exit 1
fi
echo "Category=$category: $passed/$executed passed"
