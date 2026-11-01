#!/usr/bin/env bash
# Claude Code cloud session setup — Kepler Drift (Unity 6 + C#).
#
# Why: the root `.mcp.json` carries the owner's Windows paths and does not work in
# the cloud container. Here the same base (codebase-memory-mcp, ui-ux-pro-mcp) is
# registered with Linux paths, Unity's official Claude Code plugin skills are
# installed, and mono is added so Core compiles without Unity. There is no Unity
# Editor/CLI in the cloud; the `unity` MCP will not connect, which is expected
# (verification ladder: kepler-drift-unity skill).
#
# Usage: Project settings › Cloud environment › Setup script:
#   bash tool/cloud_setup.sh || true
# If the environment is shared with Birdie/sandık, sandikapp's script already
# installs codebase-memory + ui-ux-pro; this script is idempotent on top of it.
set -uo pipefail
REPO="${REPO:-/home/user/kepler-drift}"
CBM_VER=0.9.0
UIUX_VER=1.5.0
log() { echo "[cloud_setup] $*"; }

if [ "${CLOUD_SETUP_FORCE:-0}" != 1 ]; then
  if [ "$(uname -s)" != Linux ] || { [ "${CLAUDE_CODE_REMOTE:-}" != true ] && ! [ -w /opt ]; }; then
    log "Only for the Claude Code cloud container; nothing was done on this machine."; exit 0
  fi
fi

# 1) codebase-memory-mcp (code graph; discovery starts here on every task).
if [ ! -x /opt/cbm/codebase-memory-mcp ]; then
  mkdir -p /opt/cbm && curl -fsSL \
    "https://github.com/DeusData/codebase-memory-mcp/releases/download/v${CBM_VER}/codebase-memory-mcp-linux-amd64.tar.gz" \
    | tar -xz -C /opt/cbm || log "WARNING: codebase-memory-mcp install failed"
fi
# 2) ui-ux-pro-mcp (menu/HUD layout and hierarchy; palettes are not taken from it).
command -v ui-ux-pro-mcp >/dev/null 2>&1 || npm i -g "ui-ux-pro-mcp@${UIUX_VER}" >/dev/null 2>&1 || log "WARNING: ui-ux-pro-mcp install failed"
# 3) mono: Core (pure C#) + tool/unity_test compile without Unity.
command -v mcs >/dev/null 2>&1 || { apt-get update -qq >/dev/null 2>&1 && apt-get install -y -qq mono-mcs mono-runtime >/dev/null 2>&1; } || log "WARNING: mono install failed"

# 4) MCP registration: this repo path (local) and user scope (threads start in /home/user).
if command -v claude >/dev/null 2>&1; then
  for scope in local user; do
    ( [ "$scope" = local ] && cd "$REPO" 2>/dev/null || true
      claude mcp remove -s "$scope" codebase-memory-mcp >/dev/null 2>&1 || true
      claude mcp remove -s "$scope" ui-ux-pro-mcp >/dev/null 2>&1 || true
      claude mcp add -s "$scope" codebase-memory-mcp -- /opt/cbm/codebase-memory-mcp >/dev/null 2>&1
      claude mcp add -s "$scope" ui-ux-pro-mcp -- "$(command -v ui-ux-pro-mcp || echo ui-ux-pro-mcp)" >/dev/null 2>&1 ) || true
  done
  log "MCP servers registered (codebase-memory, ui-ux-pro)"
  # 5) Unity official Claude Code plugin: ~45 Unity skills (unity-cli, urp-postprocessing,
  # create-2d-physics, ui-uitk, implement-in-app-purchases…). Skills are plain SKILL.md and load in
  # the cloud; steps needing the CLI/Editor run on the PC. License: Unity Companion License
  # (installed as a plugin, NOT copied into the repo).
  claude plugin marketplace add Unity-Technologies/unity-agent-plugin >/dev/null 2>&1 || true
  claude plugin install unity@unity-agent-plugin >/dev/null 2>&1 && log "Unity plugin installed" || log "WARNING: Unity plugin install failed (network?)"
else
  log "WARNING: claude CLI missing, MCP/plugin registration skipped"
fi
# 6) Repo skills into user scope (so a thread starting in /home/user sees them too).
if [ -d "$REPO/.claude/skills" ]; then mkdir -p ~/.claude/skills && cp -r "$REPO"/.claude/skills/* ~/.claude/skills/ 2>/dev/null || true; fi
# 7) Code graph.
[ -x /opt/cbm/codebase-memory-mcp ] && [ -d "$REPO/unity" ] && /opt/cbm/codebase-memory-mcp cli index_repository --repo-path "$REPO" >/dev/null 2>&1 && log "code graph indexed" || true
exit 0
