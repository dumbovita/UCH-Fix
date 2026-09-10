# Linux multiplayer verification

Run these tests from the exact commit and DLL reported with a release candidate. Enable `Diagnostics.VerboseLogging=true` on the host and affected client. Preserve complete `BepInEx/LogOutput.log` files; do not paste only error lines.

## Test A — duplicate item-selection race

Setup:

- Ultimate Chicken Horse 1.13.13 on Linux/Proton
- BepInEx enabled
- three online players; Player A hosts
- UCH Fixes installed on the host; preferably on all clients for matching diagnostics
- standard Party Box mode and placement timer enabled

Steps:

1. Start an online game and reach the party-box selection phase.
2. Choose one visible concrete entry—not just an item type—and have A, B, and C press select as simultaneously as practical.
3. Record which item each player receives, then complete placement.
4. Repeat at least 10 times, including one attempt on the last available-looking item.
5. Repeat once with two couch players sharing one remote machine.
6. If possible, add 150–300 ms latency and repeat five attempts.
7. Repeat one round with the placement timer disabled.

Expected:

- exactly one player receives the contested concrete entry;
- other claimants receive distinct unused entries, or no item only if the pool is exhausted;
- no two players can place copies derived from the same selection netId;
- the contested item disappears/turns unavailable for every player;
- no phantom item or cursor remains;
- the party box closes and placement/play phases advance normally;
- the host log contains one accepted result and reconciliation entries with distinct `assigned` IDs.

Return:

- host `LogOutput.log` and one client `LogOutput.log`;
- duplicate attempts succeeded out of total attempts;
- whether anyone was stuck, skipped unexpectedly, or saw a phantom item/cursor;
- whether couch co-op differed;
- screenshots/video if UI state disagreed between clients.

## Test B — background progression, remote client

Setup: Player A hosts; Player B is the Linux/Proton client under test. Record approximate CPU/GPU usage before and during backgrounding.

Steps:

1. During party-box selection, Alt-Tab Player B for 45 seconds while A continues and observes B.
2. Restore focus; record whether B catches up immediately and can select/place.
3. During placement, Alt-Tab B for 45 seconds while A places and waits.
4. Restore focus and finish the round.
5. During active play, Alt-Tab B for 60 seconds while A finishes or dies and the game changes phase.
6. Repeat across a scorecard-to-next-round transition.

Expected:

- A does not wait indefinitely for B because B's client loop stopped;
- B processes current state while backgrounded or is synchronized immediately on return without a delayed event burst;
- logs report roughly 30 background plugin updates per second, not near zero;
- foreground VSync/target-frame-rate settings are restored exactly;
- background CPU/GPU load is materially below an uncapped 300 FPS run.

## Test C — background progression, host

Setup: Player A is the Linux/Proton host under test; Player B stays focused.

Steps:

1. Alt-Tab A for 60 seconds during active play while B continues.
2. Repeat during party-box selection and during the placement-to-play transition.
3. Keep A backgrounded through one complete timer expiry.

Expected:

- B continues receiving timely authoritative messages;
- no disconnect, frozen party box, delayed placement confirmation, or scorecard stall occurs;
- host background update log remains near the configured rate;
- focus return does not produce a large queued-message burst or physics jump.

Return both full logs, the desktop environment/window mode, Proton version, observed CPU/GPU usage, and a timestamped description of any stall.
