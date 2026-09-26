#!/usr/bin/env bash
set -uo pipefail

PLATFORM="${1:-EditMode}"
# Optional Unity -testFilter (semicolon-separated test/fixture/namespace names or a regex). A filtered run writes
# <Platform>-targeted-results.xml so it never overwrites the full-suite results that audits read.
FILTER="${2:-}"
case "$PLATFORM" in
  EditMode|PlayMode|All) ;;
  *) echo "Usage: $0 [EditMode|PlayMode|All] [testFilter]" >&2; exit 2 ;;
esac

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_PATH="$(cd "$SCRIPT_DIR/.." && pwd)"
UNITY_VERSION="6000.3.24f1"

resolve_unity() {
  if [[ -n "${UNITY_PATH:-}" && -x "${UNITY_PATH}" ]]; then
    printf '%s\n' "$UNITY_PATH"
    return 0
  fi

  local candidates=(
    "/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"
    "$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity"
    "/opt/unity/Editor/Unity"
  )

  local candidate
  for candidate in "${candidates[@]}"; do
    if [[ -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  echo "Unity $UNITY_VERSION not found. Set UNITY_PATH to the Unity executable." >&2
  return 127
}

xml_attr() {
  local file="$1"
  local attr="$2"
  tr -d '\r\n' < "$file" | sed -n "s/.*<test-run[^>]* ${attr}=\"\([0-9][0-9]*\)\".*/\1/p" | head -1
}

run_tests() {
  local platform="$1"
  local unity="$2"
  local results_dir="$PROJECT_PATH/TestResults"
  mkdir -p "$results_dir"

  local suffix=""
  [[ -n "$FILTER" ]] && suffix="-targeted"
  local result_file="$results_dir/$platform$suffix-results.xml"
  local log_file="$results_dir/$platform$suffix-unity.log"
  rm -f "$result_file"

  echo "Running RUINRAIL $platform tests with $unity"
  local unity_args=(
    -batchmode
    -runTests
    -projectPath "$PROJECT_PATH"
    -testPlatform "$platform"
    -testResults "$result_file"
    -logFile "$log_file"
  )
  if [[ -n "$FILTER" ]]; then
    unity_args+=( -testFilter "$FILTER" )
  fi

  # EditMode is safe to run headless. PlayMode intentionally keeps graphics enabled
  # because some PlayMode tests may require a graphics device/render loop.
  if [[ "$platform" == "EditMode" ]]; then
    unity_args+=( -nographics )
  fi

  "$unity" "${unity_args[@]}"
  local unity_exit=$?

  if [[ $unity_exit -ne 0 ]]; then
    echo "FAIL: Unity $platform tests exited with code $unity_exit. See $log_file${result_file:+ and $result_file}." >&2
    return 1
  fi

  if [[ ! -f "$result_file" ]]; then
    echo "FAIL: Unity returned success but produced no test result file: $result_file" >&2
    echo "See log: $log_file" >&2
    return 1
  fi

  local total passed failed
  total="$(xml_attr "$result_file" total)"
  passed="$(xml_attr "$result_file" passed)"
  failed="$(xml_attr "$result_file" failed)"

  if [[ -z "$total" ]]; then
    echo "FAIL: Could not read the <test-run total=...> count from $result_file." >&2
    return 1
  fi

  if (( total == 0 )); then
    echo "FAIL: $platform reported 0 tests. Check test assembly definitions, references, and test discovery." >&2
    return 1
  fi

  if [[ -n "$failed" ]] && (( failed > 0 )); then
    echo "FAIL: $platform reported $failed failed test(s) out of $total. See $result_file and $log_file." >&2
    return 1
  fi

  if [[ -z "$passed" ]] || (( passed == 0 )); then
    echo "FAIL: $platform discovered $total test(s) but reported 0 passed tests. Check skipped/inconclusive tests in $result_file." >&2
    return 1
  fi

  echo "PASS: $platform — $passed passed / $total discovered. Results: $result_file"
  return 0
}

UNITY_EXECUTABLE="$(resolve_unity)" || exit $?

if [[ "$PLATFORM" == "All" ]]; then
  failures=0
  run_tests EditMode "$UNITY_EXECUTABLE" || failures=$((failures + 1))
  run_tests PlayMode "$UNITY_EXECUTABLE" || failures=$((failures + 1))

  if (( failures > 0 )); then
    echo "FAIL: $failures test platform(s) failed. Both EditMode and PlayMode were attempted; inspect TestResults/." >&2
    exit 1
  fi

  echo "PASS: EditMode and PlayMode test suites completed successfully."
else
  run_tests "$PLATFORM" "$UNITY_EXECUTABLE" || exit 1
fi
