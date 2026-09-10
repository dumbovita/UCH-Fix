# UCH Fixes

UCH Fixes is a BepInEx plugin for Ultimate Chicken Horse. Its primary target is game version **1.13.13** (Unity 2021.3.45f1, 64-bit Mono).

The current build contains two narrowly scoped fixes:

- The listen-server host serializes party-box claims by the concrete spawned `PickableBlock` network ID. One networked selection entry can be assigned only once. If simultaneous claimants collide, the first host-ordered request gets the requested entry and later claimants receive distinct unclaimed entries through the existing vanilla message. If no entry remains, vanilla's no-item path is used. Modded clients reconcile their optimistic cursor state and display a short notice when this happens.
- The plugin keeps Unity background execution enabled and maintains a configurable background update rate. It temporarily disables VSync while unfocused so Unity's frame-rate setting is effective, then restores the user's foreground settings.

No game assembly is modified. The plugin uses three small Harmony prefixes/postfixes and introduces no custom network message IDs, RPCs, or runtime dependencies beyond BepInEx/HarmonyX.

## Verification status

- **Confirmed by static reverse engineering:** the party-box race and its complete path from local click, through the blind host relay, to duplicate prefab creation; the game's background settings and focus handler; the UNET listen-server architecture.
- **Confirmed by build/test:** compilation against the supplied 1.13.13 assemblies and BepInEx 5.4.23.5; isolated claim ordering, concurrency, replay, stale-session, invalid-slot, reset, timeout, and exhausted-pool tests.
- **Inferred:** authoritative reassignment is compatible with unmodified peers because it uses the exact vanilla `MsgPiecePicked` shape and a concrete live netId.
- **Requires multiplayer verification:** end-to-end behavior under real latency; Linux/Proton focus behavior and background resource use. A successful build is not treated as proof of either live fix.

See [the reverse-engineering notes](docs/reverse-engineering.md) and [the Linux test procedure](docs/linux-testing.md) for evidence and remaining gates.

## Install

1. Install [BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) into the Ultimate Chicken Horse directory. Use `BepInEx_win_x64` for the supplied Windows build (including Proton/Wine); use `BepInEx_linux_x64` only for a native 64-bit Linux Mono build.
2. Run the game once so BepInEx creates its directories.
3. Copy `UCHFixes.dll` to `BepInEx/plugins/UCHFixes/UCHFixes.dll`.
4. Start the game and confirm `BepInEx/LogOutput.log` reports both fixes as enabled.

The plugin does not require every peer to install it for the item gate: only the host performs arbitration, and all resulting messages remain vanilla-compatible. Installing it on every machine is recommended for the background fix and diagnostics.

## Configuration

BepInEx creates `BepInEx/config/dev.dumbovita.uchfixes.cfg` after first launch.

| Setting | Default | Purpose |
| --- | ---: | --- |
| `General.Enabled` | `true` | Master switch |
| `Fixes.BackgroundSync` | `true` | Preserve background network/game updates |
| `Fixes.DuplicateItemSelection` | `true` | Enable host-side claim arbitration |
| `Background.TargetFrameRate` | `30` | Unfocused rate; values below 30 are clamped |
| `Background.ManageVSync` | `true` | Disable VSync only while unfocused and restore it afterward |
| `Diagnostics.VerboseLogging` | `false` | Log accepted/duplicate claim details |
| `Diagnostics.CompatibilityLogging` | `true` | Log target validation and fallbacks |

## Build

Requirements:

- .NET 8 SDK or newer
- a legal local Ultimate Chicken Horse installation
- BepInEx 5.4.23.5 extracted into the game directory, or `BEPINEX_DIR` set to another extracted copy

On macOS/Linux:

```bash
BEPINEX_DIR=/path/to/BepInEx ./scripts/build.sh
```

After a successful restore, pass `SKIP_RESTORE=1` for an offline rebuild.

The plugin is emitted at `artifacts/UCHFixes/BepInEx/plugins/UCHFixes/UCHFixes.dll`, with `artifacts/UCHFixes-0.1.0.zip` as the distributable package. Proprietary assemblies are referenced locally with `Private=false` and are never copied into the package.

To run only the deterministic tests:

```bash
dotnet run --project tests/UCHFixes.Tests/UCHFixes.Tests.csproj -c Release
```

## Compatibility and limitations

- Primary compatibility target: UCH 1.13.13. Older versions are accepted only when exact method signatures and field types pass runtime validation. A missing target disables only the affected feature and produces a clear log entry.
- `UCH-EvenMorePlayers` 10.0.1 patches `PartyBox.AddPlayer` and player-count/UI paths but does not patch the server relay or `PartyBox.ShowBox`/`Hide`; no direct collision was found. More-than-four-player runtime testing is still required.
- The original `UCH-MorePlayers` is a 2021 WIP and is not a reliable modern compatibility target.
- `UCH-ChainedChicken` uses a custom UNET message and unrelated gameplay hooks. This project deliberately adds no custom message ID.
- UCH's supplied player settings and serialized `LobbyManager` already request background execution. Therefore the exact platform-specific Alt-Tab failure cannot be proven statically; this plugin adds enforcement, VSync-aware pacing, and measurement, but Linux runtime evidence remains required.
- UNET has no master-client migration in this game path. If the listen-server host leaves, the session ends; the claim registry intentionally does not invent migration behavior.

## Diagnostics

Startup logs include the game version, Unity version, runtime architecture, networking stack, patch validation, and feature status. A contested claim reports the requested and server-assigned concrete selection IDs. Focus restoration reports unfocused duration and observed plugin update frequency, which helps distinguish Unity-loop suspension from ordinary latency.

Please include the host log and at least one client log with bug reports.
