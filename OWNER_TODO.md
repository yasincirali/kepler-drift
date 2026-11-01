# OWNER TODO — Kepler Drift

Steps only the owner can do. Order matters.

## 1. Repository and project (first)
1. **GitHub repo** `yasincirali/kepler-drift` (public), empty (no README).
   Claude's GitHub app cannot create repos (403; same as Birdie).
2. **Claude Projects › Project settings › Resources**: add the repo. The first
   commit is ready and gets pushed as soon as the repo exists
   (done: first commit pushed 1 Nov 2026).

## 2. Tools (on your PC, once)
3. **Unity official Claude Code plugin** (Unity Technologies, Sept 2026; ~45 skills
   + Unity CLI): in Claude Code
   `/plugin marketplace add Unity-Technologies/unity-agent-plugin` →
   `/plugin install unity@unity-agent-plugin`. CLI: `curl -fsSL https://unity.com/install.sh | bash`.
   In the cloud the skills install; CLI/Editor do not connect (expected).
4. **MCP for Unity** (CoplayDev, 15.6k ★, MIT; most popular community server):
   Unity › Package Manager › Add from git URL
   `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`, then
   Window › MCP for Unity › Configure All Detected Clients. Needs Python 3.10+ and `uv`.
   Optional if the official plugin's CLI is enough.
5. `.mcp.json` carries the same Windows paths as sandikapp (codebase-memory,
   ui-ux-pro); fix if they differ on your machine. Index:
   `codebase-memory-mcp cli index_repository --repo-path "<path>"`.

## 3. TestFlight
6. **GitHub secrets** (same values as Birdie Smash): `UNITY_LICENSE`, `UNITY_EMAIL`,
   `UNITY_PASSWORD`, `APPLE_ID`, `APPLE_TEAM_ID`, `ITC_TEAM_ID`, `ASC_KEY_ID`,
   `ASC_ISSUER_ID`, `ASC_KEY_CONTENT`, `MATCH_GIT_URL`, `MATCH_GIT_BASIC_AUTH`,
   `MATCH_PASSWORD` (match branch `keplerdrift`; password same as
   Harçlıklarım/Birdie: Apple's 3-distribution-certificate limit).
7. **Apple Developer › Identifiers**: `com.keplerdrift.app`.
8. **App Store Connect › Apps › +**: name "Kepler Drift", bundle id above, SKU
   `keplerdrift`, primary language English, category Games › Arcade. TestFlight Test Information.
9. Actions › "iOS — Setup Match (one-time)" → then the first merge to `main` uploads a TF build.

## Later
- Ad mediation (LevelPlay/AdMob) and IAP products: once the v1 scope is final.
- Texture credits: if ESA/Hubble or Solar System Scope textures are used, a credits
  screen (code prepares it, you approve the text).

## Known gaps (code side, to check at the first Unity open)
- No app icon yet (1024 px); ASC may reject the upload. `Editor/CiBuild.cs` TODO.
- Match branch `keplerdrift`; if the Apple certificate limit is full, switch to the
  `harcliklarim` branch as the Matchfile comment explains.
- The Unity side has never been opened here: URP version, the code-generated 2D
  renderer, the shader and the runtime UI Toolkit panel get verified at the first Editor open.
