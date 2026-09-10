# Reverse-engineering notes

Date of analysis: 2026-09-11. Decompiled and cloned material was kept outside the repository and is not distributed.

## Target environment

| Property | Evidence-backed result |
| --- | --- |
| Game | Ultimate Chicken Horse `1.13.13.779.4563fd9.REL` |
| Unity | `2021.3.45f1` |
| Runtime | 64-bit MonoBleedingEdge; no IL2CPP metadata/binary |
| Platform of supplied build | Windows x86-64 |
| Gameplay networking | Unity UNET HLAPI (`com.unity.multiplayer-hlapi.Runtime.dll`) |
| Matchmaking/services | Steamworks and BrainCloud are present; legacy GameSparks types also remain |
| Photon | No Photon assembly or selection path found |
| Authority | Listen-server host; `NetworkServer` relays gameplay messages |
| BepInEx choice | Stable BepInEx 5.4.23.5 x64 Mono; plugin targets .NET Framework 4.8 and uses its bundled HarmonyX |

The executable is PE32+ x86-64. `Assembly-CSharp.dll` is 3,397,120 bytes with SHA-256 `bba395fd616ab57fc90551f03a36b77c516a46b71bab8ef4709f200ea2a597e3`. The executable SHA-256 is `3bb61c51d73d6fa511fdb42c4c589347fb107539022a9243c67f8b1fc3d5baa8`.

The loose `version.txt.txt` reports `1.9.03`, but it belongs to an Online-Fix wrapper. The compiled PlayerSettings/global manager data and `Application.version` source report `1.13.13.779.4563fd9.REL`; compiled data is authoritative.

## Reference repositories

### [`batram/UCH-EvenMorePlayers`](https://github.com/batram/UCH-EvenMorePlayers)

- Inspected through tag `10.0.1`, commit `d19e7e78a7e906056ca32bbf1766ed85c55d7256` (2026-05-19). Its history spans older 1.8/1.9 builds and explicitly updates for the UCH 1.13 era.
- Targets `net48`, BepInEx 5, HarmonyX via `0Harmony.dll`, and Krafs.Publicizer.
- Confirms stable UNET concepts and symbols including `LobbyManager`, `LobbyPlayer`, `GamePlayer`, `PartyBox`, `InventoryBook`, network numbers, lobby slots, and spawned netIds.
- Expands registration, lobby state, UI, player arrays, Steam lobby capacity, and selection cursor layouts. It uses version-string matchmaking isolation for incompatible player-count rules.
- Several patches replace positional `ldc.i4.4` constants or unpatch groups dynamically. Those approaches are fragile across compiler/game changes and were not reused.
- Potential overlap: `PartyBox.AddPlayer`, player arrays, and cursor layout. It does not patch `LobbyManager.distributeServerMessage`, `PartyBox.ShowBox`, or `PartyBox.Hide` at tag 10.0.1.
- License: The Unlicense. No source was copied into this project.

### [`notfood/UCH-MorePlayers`](https://github.com/notfood/UCH-MorePlayers)

- Inspected sole commit `7f5cf5fdc6bd0848fa49eb498be0e9afb18058ed` (2021-10-18), an explicitly unfinished UCH 1.8-era project with no tags.
- Targets `net48`, BepInEx 5/HarmonyX, and Krafs.Publicizer.
- Uses a broad transpiler that changes `ldc.i4.4` to `ldc.i4.8` in numerous methods. It does not cover all registration, Steam lobby, UI, inventory, and party-box paths required by later versions.
- Useful historically for confirming player network-number/index pressure, but unsafe as a 1.13.13 architecture template.
- License: The Unlicense. No source was copied.

### [`batram/UCH-ChainedChicken`](https://github.com/batram/UCH-ChainedChicken)

- Inspected head `9118acc97d9be7af84603d4f35824d9b0dce12a8` (2024-08-25), tags through `0.0.0.3`, from the UCH 1.11 era.
- Targets `net48`, BepInEx 5/HarmonyX, and Krafs.Publicizer.
- Uses lifecycle patches such as `GameControl.ToPlayMode` and `Character.PositionCharacter`, client-side GameObject/physics manipulation, and UNET custom-message handlers.
- Its custom message ID is calculated with an arbitrary large offset from the game's message count. That can collide with other mods; this project avoids custom network messages entirely.
- Its online feature gate depends on local/modded version-string behavior rather than a general peer capability handshake.
- License: The Unlicense. No source was copied.

## Item-selection path and root cause

The complete standard party-box path in the supplied 1.13.13 assembly is:

1. On the host, `VersusControl.SetupPartyBoxForRound(bool)` calls `PartyBox.ChoosePieces(playerCount + 1 + extras, ...)`.
2. `PartyBox.ChoosePieces` instantiates `PickableBlock` objects, adds them to its private `pieces` list, enables them, and calls `NetworkServer.Spawn` for each.
3. The concrete selection identity is `PickableBlock.netIdValue`, backed by `NetworkIdentity.netId.Value`. It identifies one spawned entry, not merely an item type, so two legitimate copies of the same type remain distinct.
4. `PartyPickCursor.tryPickPiece()` optimistically stores the hovered object, freezes/disables the local cursor, and sends `MsgPiecePicked { PickableNetID, PlayerNumber }` to `NetMsgTypes.PiecePicked`.
5. `LobbyManager` registers `distributeServerMessage(NetworkMessage)` as the server handler. Its entire body reads the message and calls `NetworkServer.SendToAll`; it performs no membership, sender, already-claimed, or per-player validation.
6. Each client runs `PartyBox.OnPiecePicked`. It disables—but does not destroy—the matching `PickableBlock` and emits `PickBlockEvent` even if the same netId was already processed.
7. The matching `PiecePlacementCursor` instantiates `pickablePiece.placeablePrefab`, generates a new placement ID, and keeps it as the player's piece. Repeated claims of one selectable netId therefore become multiple independently placeable instances.
8. The authoritative `PartyBox` also decrements `remainingPickingCursor` for every relayed `PickBlockEvent`, so duplicate packets can corrupt phase progression as well as inventory.

This confirms a distributed race: local UI disabling cannot close it because competing requests may already be in flight.

## Selected patch design

| Game concept | Game class/member | Patch | Reason |
| --- | --- | --- | --- |
| Host claim decision | `LobbyManager.distributeServerMessage(NetworkMessage)` | Prefix only for `NetMsgTypes.PiecePicked` | Smallest point before the blind broadcast; host receives requests in a single order |
| Box/session start | `PartyBox.ShowBox(bool)` | Postfix | All selection objects have already been spawned and have concrete netIds |
| Box/session end | `PartyBox.Hide(bool)` | Prefix | Closes the epoch before late messages can claim an old box |
| Local optimistic state | `PartyBox.OnPiecePicked(MsgPiecePicked)` | Prefix | Replaces the losing local cursor's stale requested reference with the host-assigned entry and displays a notice |
| Game frame-rate initialization | `GameState.Start()` | Postfix | Reasserts background policy after vanilla writes `targetFrameRate = 300` |

Runtime resolution requires exact declaring type, return type, parameter list, and `PartyBox.pieces` field type. Missing or changed targets produce logs and disable the affected patch rather than choosing a similarly named method.

The host registry keeps an internal incrementing box-session ID, a set of live selection netIds, a selection-to-player map, and a player-to-result map. It enforces:

- first claim of a concrete netId wins;
- one completed result per player per box;
- duplicate packets do not rebroadcast or decrement counters twice;
- selections absent from the current spawned set are rejected as invalid/stale;
- a second claimant is atomically assigned another unclaimed current netId;
- if no fallback exists, `PickableNetID = 0` uses the game's existing no-item/placement-skip path;
- the claimed `PlayerNumber` must belong to a `GamePlayer` or `LobbyPlayer` controller on the sending UNET connection, including couch co-op connections with multiple controllers.

Vanilla `MsgPiecePicked` has no request or round identifier. To retain wire compatibility, this project does not add either field: idempotence is enforced host-side by one recorded result per authenticated player per internal box session, while stale packets are rejected when their concrete netId is absent from the current box.

Automatic fallback is intentional. A private rejection protocol would require a handshake and identical mod versions; overloading `MsgPiecePicked.PieceID` is unsafe because vanilla `PartyBox.OnPiecePicked` ignores that field. Relaying another live netId preserves the exact vanilla protocol, removes the contested item globally, produces one placement item, and decrements the box counter exactly once for each player. It also means the losing player receives an available item chosen by host list order rather than returning to the box.

On clients running UCH Fixes, the `PartyBox.OnPiecePicked` prefix also replaces the losing local `PartyPickCursor.NetworkpickedPiece` reference with the authoritative object and shows a short notice. An unmodified client still receives and places the correct fallback through vanilla `PiecePlacementCursor` behavior, but does not receive the added notice.

The prefix rewinds the UNET reader after inspection. When it handles a party-box claim, it suppresses the original blind relay and broadcasts exactly one normalized vanilla message. Outside a validated active box it leaves vanilla behavior unchanged.

## Background/focus path

Static evidence does **not** show a simple missing flag in this build:

- PlayerSettings serializes `runInBackground = true`.
- The serialized `LobbyManager` has `m_RunInBackground = true`.
- UNET `NetworkManager.StartServer`, `StartClient`, and `UseExternalClient` set `Application.runInBackground = true` when that field is enabled.
- `GameState.OnApplicationFocus(bool)` changes cursor lock/visibility and Wwise muting only. It does not pause gameplay or networking.
- `GameState.Start()` sets `Application.targetFrameRate = 300`.
- Save data applies `QualitySettings.vSyncCount = 1` when VSync is enabled, which prevents `Application.targetFrameRate` from controlling pacing.
- progression (`VersusControl`, `GameControl`) and UNET processing depend on Unity frame callbacks/coroutines. If the platform actually suspends or severely throttles the player loop, both network messages and unscaled timers stop advancing locally.

The plugin therefore implements an evidence-bounded mitigation and diagnostic:

- set and periodically reassert `Application.runInBackground = true`;
- while unfocused, preserve the foreground frame/VSync settings, set VSync to zero, and request a background frame rate whose configurable value is clamped to at least 30;
- restore the exact saved foreground settings on focus return or plugin teardown;
- log focus duration and observed plugin update frequency without per-frame logging.

The unresolved root is platform/runtime behavior outside managed game code—likely Unity/Proton/window-manager scheduling despite already-enabled background flags—and requires the Linux tests. Rendering cost was not rewritten because no safe rendering-only hook was established from static evidence.

## Compatibility conclusions

- Applicable from the reference mods: BepInEx 5 Mono/net48, HarmonyX, UNET types, player `networkNumber`, concrete network IDs, exact lifecycle hooks, and cautious feature gating.
- Deliberately avoided: publicizing the entire game assembly, broad constant transpilers, hardcoded hierarchy traversal, arbitrary custom message IDs, item-type identities, and version strings as the sole compatibility test.
- Host migration: this is a UNET listen-server path, not Photon master-client authority. Host departure tears down the session, so claim transfer is not implemented.
- Hot paths: reflection is cached and used only once per box; no reflection or logging occurs each frame. Claim lookup is constant-time apart from choosing among the small party-box list.

## Evidence classification

### Confirmed by static reverse engineering

- Environment/runtime/network stack and exact versions above.
- Blind server relay and duplicate-instantiation root cause.
- Concrete selection identity and party-box lifecycle.
- Existing background flags, focus handler behavior, VSync interaction, and frame-driven progression.

### Confirmed by build/test

- Plugin compiles against the supplied target assemblies and official BepInEx 5.4.23.5 binaries.
- Isolated first-wins, two/three-way concurrency, idempotence, stale session, invalid selection, reset, timeout, and exhausted-pool tests pass.

### Inferred

- A normalized alternate live netId is transparent to vanilla clients and avoids phase-counter skew.
- A 30 FPS background loop is sufficient for normal UNET callback and progression work without foreground-level load.

### Requires multiplayer verification

- Real host ordering and local-host connection ownership in online/couch combinations.
- UI and placement reconciliation under high latency and repeated races.
- EvenMorePlayers sessions above four players.
- Linux/Proton background update rate, network continuity, GPU/CPU cost, and compositor-specific suspension.
