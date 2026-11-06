#!/usr/bin/env bash
# Build + headless smoke test for DownhillRoguelite. Run from anywhere:
#   bash .claude/skills/smoke-test/smoke.sh [frames]
# Exits non-zero if any check fails. Logs are kept in a temp folder (printed at the end).

set -u

FRAMES="${1:-300}"
PROJECT_DIR="$(cd "$(dirname "$0")/../../.." && pwd)"
LOG_DIR="$(mktemp -d)"
FAILED=0

cd "$PROJECT_DIR" || exit 1

pass() { echo "PASS  $1"; }
fail() { echo "FAIL  $1"; FAILED=1; }

# --- Resolve Godot: $GODOT, then `godot` on PATH, then a .NET build unzipped next to the project.
GODOT_BIN="${GODOT:-}"
if [ -z "$GODOT_BIN" ] && command -v godot >/dev/null 2>&1; then
    GODOT_BIN="godot"
fi
if [ -z "$GODOT_BIN" ]; then
    GODOT_BIN="$(ls -d "$PROJECT_DIR"/../Godot_v*_mono_win64/Godot_v*_mono_win64_console.exe 2>/dev/null | sort | tail -1)"
fi
if [ -z "$GODOT_BIN" ]; then
    echo "FAIL  Godot not found. Put the .NET console exe on PATH as 'godot' or set GODOT=<path>."
    exit 1
fi

# --- 1. Godot is a .NET build and matches the Godot.NET.Sdk version in the .csproj.
GODOT_VERSION="$("$GODOT_BIN" --version 2>/dev/null | tr -d '\r' | tail -1)"
SDK_VERSION="$(grep -oE 'Godot\.NET\.Sdk/[0-9][^"]*' ./*.csproj | head -1 | cut -d/ -f2)"
echo "Godot: $GODOT_VERSION ($GODOT_BIN)"
echo "SDK:   Godot.NET.Sdk/$SDK_VERSION"
case "$GODOT_VERSION" in
    *mono*) ;;
    *) fail "Godot is not the .NET (mono) build" ;;
esac
# 4.7.2.stable.mono... -> 4.7.2 ; 4.7.stable.mono... -> 4.7.0
GODOT_NUMERIC="$(echo "$GODOT_VERSION" | grep -oE '^[0-9]+\.[0-9]+(\.[0-9]+)?')"
if [ "$(echo "$GODOT_NUMERIC" | tr -cd '.' | wc -c)" -eq 1 ]; then
    GODOT_NUMERIC="$GODOT_NUMERIC.0"
fi
if [ "$GODOT_NUMERIC" = "$SDK_VERSION" ]; then
    pass "Godot version matches Godot.NET.Sdk ($SDK_VERSION)"
else
    fail "Godot $GODOT_NUMERIC does not match Godot.NET.Sdk/$SDK_VERSION in the .csproj"
fi

# --- 2. dotnet build: zero errors, zero warnings. Runs before the import, because the import
# executes [Tool] scripts and must use the current assembly, not the last one built.
dotnet build > "$LOG_DIR/build.log" 2>&1
BUILD_EXIT=$?
WARNINGS="$(grep -oE '[0-9]+ Warning\(s\)' "$LOG_DIR/build.log" | tail -1 | cut -d' ' -f1)"
ERRORS="$(grep -oE '[0-9]+ Error\(s\)' "$LOG_DIR/build.log" | tail -1 | cut -d' ' -f1)"
if [ "$BUILD_EXIT" -eq 0 ] && [ "${WARNINGS:-1}" = "0" ] && [ "${ERRORS:-1}" = "0" ]; then
    pass "dotnet build (0 warnings, 0 errors)"
else
    fail "dotnet build (exit $BUILD_EXIT, ${WARNINGS:-?} warnings, ${ERRORS:-?} errors):"
    grep -E ": (error|warning) [A-Z]+[0-9]+" "$LOG_DIR/build.log" | sort -u | sed 's/^/      /'
fi

# --- 3. Import: refreshes the .godot cache and generates .uid files for new resources.
"$GODOT_BIN" --headless --path . --import > "$LOG_DIR/import.log" 2>&1
if grep -qiE "error" "$LOG_DIR/import.log"; then
    fail "Import reported errors:"
    grep -iE "error" "$LOG_DIR/import.log" | sed 's/^/      /'
else
    pass "Import"
fi

# --- 4. Headless run of the main scene; any engine/script error or warning fails.
"$GODOT_BIN" --headless --path . --quit-after "$FRAMES" > "$LOG_DIR/smoke.log" 2>&1
SMOKE_EXIT=$?
if [ "$SMOKE_EXIT" -ne 0 ]; then
    fail "Headless run exited with code $SMOKE_EXIT"
fi
if grep -qE "ERROR|WARNING|Unhandled exception|Exception:" "$LOG_DIR/smoke.log"; then
    fail "Headless run ($FRAMES frames) printed errors/warnings:"
    grep -E -A2 "ERROR|WARNING|Unhandled exception|Exception:" "$LOG_DIR/smoke.log" | sed 's/^/      /'
elif [ "$SMOKE_EXIT" -eq 0 ]; then
    pass "Headless run ($FRAMES frames), no errors"
fi

# --- 5. Verbose run: the main scene and every autoload actually loaded.
"$GODOT_BIN" --headless --verbose --path . --quit-after 30 > "$LOG_DIR/verbose.log" 2>&1
MAIN_SCENE="$(grep -oE '^run/main_scene="[^"]+"' project.godot | cut -d'"' -f2)"
AUTOLOADS="$(sed -n '/^\[autoload\]/,/^\[/p' project.godot | grep -oE '"\*?res://[^"]+"' | tr -d '"*')"
for RES in $MAIN_SCENE $AUTOLOADS; do
    if grep -qF "Completed load for: '$RES'" "$LOG_DIR/verbose.log"; then
        pass "Loaded $RES"
    else
        fail "Did not load $RES"
    fi
done
if grep -q "\.NET: GodotPlugins initialized" "$LOG_DIR/verbose.log"; then
    pass ".NET runtime initialized"
else
    fail ".NET runtime did not initialize"
fi

# --- 6. Automated test scenes: every debug/tests/*.tscn must exit 0. They print PASS/FAIL lines.
for TEST_SCENE in debug/tests/*.tscn; do
    [ -e "$TEST_SCENE" ] || continue
    TEST_NAME="$(basename "$TEST_SCENE" .tscn)"
    "$GODOT_BIN" --headless --path . --fixed-fps 60 --quit-after 20000 "res://$TEST_SCENE" > "$LOG_DIR/$TEST_NAME.log" 2>&1
    TEST_EXIT=$?
    if [ "$TEST_EXIT" -eq 0 ] && ! grep -qE "^FAIL|ERROR|Unhandled exception" "$LOG_DIR/$TEST_NAME.log"; then
        pass "$TEST_NAME ($(grep -c '^PASS' "$LOG_DIR/$TEST_NAME.log") checks)"
    else
        fail "$TEST_NAME (exit $TEST_EXIT):"
        grep -E "^FAIL|ERROR|Unhandled exception" "$LOG_DIR/$TEST_NAME.log" | sed 's/^/      /'
    fi
done

# --- 7. Reminder about generated files that should be committed.
UNTRACKED_UIDS="$(git status --porcelain --untracked-files=all 2>/dev/null | grep -E '^\?\? .*\.uid$' | cut -c4-)"
if [ -n "$UNTRACKED_UIDS" ]; then
    echo "NOTE  Untracked .uid files (commit them with their resources):"
    echo "$UNTRACKED_UIDS" | sed 's/^/      /'
fi

echo "Logs: $LOG_DIR"
if [ "$FAILED" -ne 0 ]; then
    echo "RESULT: FAIL"
    exit 1
fi
echo "RESULT: PASS"
