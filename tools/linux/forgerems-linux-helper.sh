#!/usr/bin/env bash
# ForgerEMS Linux helper (Phase 7 scaffold)
#
# Emits a JSON snapshot of host facts that the Windows-only WPF app cannot
# read from inside a Wine prefix:
#   - distro / kernel
#   - mounted volumes
#   - block devices (lsblk)
#   - removable devices
#   - Ventoy partitions (best-effort label match)
#   - which standard tools are available on PATH
#
# Design rules:
#   - read-only; never writes to /sys, /proc, or any block device
#   - safe under "set -u": every variable is initialized
#   - degrades silently when a tool is missing (jq, lsblk, blkid, smartctl,
#     udevadm) and reports availability in tools_available
#   - exits 0 even when individual sections are empty; the caller decides
#     whether the snapshot is useful
#
# Usage:
#   forgerems-linux-helper.sh                # write JSON to stdout
#   forgerems-linux-helper.sh -o snapshot.json
#
# This script is intentionally not invoked by ForgerEMS itself in this pass.
# It exists so the future Linux helper integration has a stable on-disk
# contract to ship against.

set -u
set -o pipefail

OUT=""
while [ $# -gt 0 ]; do
  case "$1" in
    -o|--output) OUT="${2:-}"; shift 2 ;;
    -h|--help)
      sed -n '2,30p' "$0"
      exit 0
      ;;
    *) shift ;;
  esac
done

# ---- helpers --------------------------------------------------------------

json_string() {
  # Escape a value for JSON. Falls back to a literal "" for empty input.
  local s="${1:-}"
  s="${s//\\/\\\\}"
  s="${s//\"/\\\"}"
  s="${s//$'\n'/\\n}"
  s="${s//$'\r'/\\r}"
  s="${s//$'\t'/\\t}"
  printf '"%s"' "$s"
}

have() { command -v "$1" >/dev/null 2>&1; }

read_first() {
  # Read the first non-empty line of a file, or empty string if missing.
  local path="$1"
  if [ -r "$path" ]; then
    head -n 1 "$path" 2>/dev/null | tr -d '\r' || true
  fi
}

# ---- distro & kernel ------------------------------------------------------

DISTRO_PRETTY=""
DISTRO_ID=""
DISTRO_VERSION_ID=""
if [ -r /etc/os-release ]; then
  # shellcheck disable=SC1091
  . /etc/os-release || true
  DISTRO_PRETTY="${PRETTY_NAME:-}"
  DISTRO_ID="${ID:-}"
  DISTRO_VERSION_ID="${VERSION_ID:-}"
fi

KERNEL=""
if have uname; then
  KERNEL="$(uname -srm 2>/dev/null || true)"
fi

# ---- tool availability ----------------------------------------------------

TOOLS=(lsblk blkid udevadm smartctl mount findmnt jq awk grep sed)
TOOLS_JSON=""
for t in "${TOOLS[@]}"; do
  if have "$t"; then v=true; else v=false; fi
  if [ -z "$TOOLS_JSON" ]; then
    TOOLS_JSON="$(json_string "$t"):$v"
  else
    TOOLS_JSON="$TOOLS_JSON,$(json_string "$t"):$v"
  fi
done

# ---- mounted volumes ------------------------------------------------------

MOUNTS_JSON=""
if have findmnt; then
  while IFS=$'\t' read -r src target fstype options; do
    [ -z "${target:-}" ] && continue
    entry="{$(json_string source):$(json_string "$src"),"
    entry="$entry$(json_string target):$(json_string "$target"),"
    entry="$entry$(json_string fstype):$(json_string "$fstype"),"
    entry="$entry$(json_string options):$(json_string "$options")}"
    if [ -z "$MOUNTS_JSON" ]; then MOUNTS_JSON="$entry"; else MOUNTS_JSON="$MOUNTS_JSON,$entry"; fi
  done < <(findmnt -rn -o SOURCE,TARGET,FSTYPE,OPTIONS 2>/dev/null || true)
fi

# ---- block devices --------------------------------------------------------

BLOCK_JSON=""
REMOVABLE_JSON=""
VENTOY_JSON=""
if have lsblk; then
  # -P (pairs) keeps parsing robust without jq.
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    eval "$line" 2>/dev/null || continue
    NAME_V="${NAME:-}"
    SIZE_V="${SIZE:-}"
    TYPE_V="${TYPE:-}"
    RM_V="${RM:-0}"
    MOUNTPOINT_V="${MOUNTPOINT:-}"
    LABEL_V="${LABEL:-}"
    MODEL_V="${MODEL:-}"
    TRAN_V="${TRAN:-}"

    entry="{$(json_string name):$(json_string "$NAME_V"),"
    entry="$entry$(json_string size):$(json_string "$SIZE_V"),"
    entry="$entry$(json_string type):$(json_string "$TYPE_V"),"
    entry="$entry$(json_string removable):$( [ "$RM_V" = "1" ] && echo true || echo false ),"
    entry="$entry$(json_string mountpoint):$(json_string "$MOUNTPOINT_V"),"
    entry="$entry$(json_string label):$(json_string "$LABEL_V"),"
    entry="$entry$(json_string model):$(json_string "$MODEL_V"),"
    entry="$entry$(json_string transport):$(json_string "$TRAN_V")}"

    if [ -z "$BLOCK_JSON" ]; then BLOCK_JSON="$entry"; else BLOCK_JSON="$BLOCK_JSON,$entry"; fi

    if [ "$RM_V" = "1" ]; then
      if [ -z "$REMOVABLE_JSON" ]; then REMOVABLE_JSON="$entry"; else REMOVABLE_JSON="$REMOVABLE_JSON,$entry"; fi
    fi

    # Ventoy detection: Ventoy ships VTOYEFI label on the helper partition
    # and "Ventoy" on the data partition; best-effort case-insensitive match.
    LABEL_LOWER="$(printf '%s' "$LABEL_V" | tr 'A-Z' 'a-z')"
    case "$LABEL_LOWER" in
      ventoy|vtoyefi)
        if [ -z "$VENTOY_JSON" ]; then VENTOY_JSON="$entry"; else VENTOY_JSON="$VENTOY_JSON,$entry"; fi
        ;;
    esac
  done < <(lsblk -P -o NAME,SIZE,TYPE,RM,MOUNTPOINT,LABEL,MODEL,TRAN 2>/dev/null || true)
fi

# ---- assemble -------------------------------------------------------------

NOW="$(date -u +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || echo unknown)"

JSON="{"
JSON="$JSON$(json_string schema):$(json_string forgerems-linux-helper/1),"
JSON="$JSON$(json_string generated_utc):$(json_string "$NOW"),"
JSON="$JSON$(json_string distro):{"
JSON="$JSON$(json_string pretty_name):$(json_string "$DISTRO_PRETTY"),"
JSON="$JSON$(json_string id):$(json_string "$DISTRO_ID"),"
JSON="$JSON$(json_string version_id):$(json_string "$DISTRO_VERSION_ID")},"
JSON="$JSON$(json_string kernel):$(json_string "$KERNEL"),"
JSON="$JSON$(json_string tools_available):{$TOOLS_JSON},"
JSON="$JSON$(json_string mounts):[$MOUNTS_JSON],"
JSON="$JSON$(json_string block_devices):[$BLOCK_JSON],"
JSON="$JSON$(json_string removable_devices):[$REMOVABLE_JSON],"
JSON="$JSON$(json_string ventoy_partitions):[$VENTOY_JSON]"
JSON="$JSON}"

if [ -n "$OUT" ]; then
  printf '%s\n' "$JSON" > "$OUT"
else
  printf '%s\n' "$JSON"
fi

exit 0
