#!/bin/bash
# PreToolUse(Bash): keep build and test output out of the context window.
#
# `dotnet build` here treats warnings as errors and prints a full restore log; the pytest
# suite in docker-ai-service/ prints hundreds of collection lines. Everything printed stays
# in context for the rest of the session and is re-read on every later turn, so a single
# noisy run is paid for many times over.
#
# This rewrites a plain build/test invocation to print only the lines the outcome depends
# on: errors, warnings-as-errors, failed tests, assertion detail, the summary. It passes
# the command through untouched when the caller already shaped the output themselves
# (pipe, redirect, custom --logger/--verbosity/-q).
set -euo pipefail

input=$(cat)
cmd=$(printf '%s' "$input" | jq -r '.tool_input.command // empty')

passthrough() { echo '{}'; exit 0; }

[ -n "$cmd" ] || passthrough
case "$cmd" in
  dotnet\ test*|dotnet\ build*|pytest*|python\ -m\ pytest*|python3\ -m\ pytest*) ;;
  *) passthrough ;;
esac
case "$cmd" in
  *\|*|*'>'*|*--logger*|*--verbosity*|*-v\ *|*\ -q*|*--quiet*) passthrough ;;
esac

# xUnit/MSBuild and pytest failure vocabulary in one pattern.
keep='error|warning|[Ff]ailed|FAILED|ERROR|Passed!|Failed!|Skipped!|Assert|Expected:|Actual:|^\s+at .*\.cs:line|^E  |^FAILED |^ERROR |=+ (FAILURES|ERRORS|short test summary|[0-9]+ (passed|failed))'

wrapped="_o=\$(mktemp); $cmd > \"\$_o\" 2>&1; _rc=\$?; grep -aE '$keep' \"\$_o\" | head -120; \
echo \"--- filtered by .claude/hooks/filter-build-output.sh · \$(wc -l < \"\$_o\") lines total · exit \$_rc ---\"; \
rm -f \"\$_o\"; exit \$_rc"

jq -n --arg c "$wrapped" '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    permissionDecision: "allow",
    updatedInput: { command: $c }
  }
}'
