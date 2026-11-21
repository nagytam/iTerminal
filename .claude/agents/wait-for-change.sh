#!/bin/bash
# One-shot wait for a pull request state change. Run with run_in_background.
# Exits 0 as soon as the open pull requests' state differs from the snapshot
# given as $1 (a file path, may be missing), printing what changed.
# Exits 1 after 15 minutes with no change, printing "timeout".
# Usage: wait-for-change.sh <snapshot-file>
snap="$1"
snapshot() {
  gh pr list --state open --json number,headRefOid,isDraft,mergeStateStatus,reviewDecision,statusCheckRollup --limit 100 \
    --jq '.[] | "#\(.number) \(.headRefOid[0:7]) draft=\(.isDraft) \(.mergeStateStatus) \(.reviewDecision) " + ([.statusCheckRollup[]? | "\(.name // .context):\(.conclusion // .status)"] | join(","))' 2>/dev/null | sort
}
prev=""
[ -f "$snap" ] && prev=$(cat "$snap")
deadline=$(( $(date +%s) + 900 ))
while [ "$(date +%s)" -lt "$deadline" ]; do
  cur=$(snapshot)
  if [ -n "$cur" ] && [ "$cur" != "$prev" ]; then
    printf '%s\n' "$cur" > "$snap"
    echo "PR state changed:"; diff <(echo "$prev") <(echo "$cur") | grep '^[<>]'
    exit 0
  fi
  sleep 60
done
echo "timeout"
exit 1
