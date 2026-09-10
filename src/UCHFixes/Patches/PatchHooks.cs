using UCHFixes.Background;
using UCHFixes.Diagnostics;
using UCHFixes.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace UCHFixes.Patches
{
    internal static class PatchHooks
    {
        private static ItemSelectionAuthority itemAuthority;
        private static BackgroundExecutionController backgroundController;
        private static ModLog log;

        internal static void Configure(ItemSelectionAuthority authority, BackgroundExecutionController background, ModLog modLog)
        {
            itemAuthority = authority;
            backgroundController = background;
            log = modLog;
        }

        // Target: LobbyManager.distributeServerMessage(NetworkMessage), UCH 1.13.13.
        // Prefix is required because the vanilla body unconditionally relays PiecePicked.
        internal static bool DistributeServerMessagePrefix(NetworkMessage msg)
        {
            if (itemAuthority == null || msg == null || msg.msgType != NetMsgTypes.PiecePicked)
            {
                return true;
            }

            try
            {
                return !itemAuthority.TryHandle(msg);
            }
            catch (System.Exception exception)
            {
                log.Error("DuplicateSelectionFix: unexpected server-gate failure; reverting this message to vanilla handling. " + exception);
                try { msg.reader.SeekZero(); } catch { }
                return true;
            }
        }

        // Target: PartyBox.ShowBox(bool), UCH 1.13.13 and verified older mod eras.
        // Postfix observes fully spawned netIds and creates a new internal box epoch.
        internal static void PartyBoxShowPostfix(PartyBox __instance)
        {
            if (itemAuthority != null)
            {
                itemAuthority.BeginBox(__instance);
            }
        }

        // Target: PartyBox.Hide(bool), UCH 1.13.13 and verified older mod eras.
        // Prefix closes the epoch before late requests can be accepted.
        internal static void PartyBoxHidePrefix()
        {
            if (itemAuthority != null)
            {
                itemAuthority.EndBox();
            }
        }

        // Target: PartyBox.OnPiecePicked(MsgPiecePicked), UCH 1.13.13.
        // Prefix reconciles the local optimistic cursor before vanilla creates the placement item.
        internal static void PartyBoxOnPiecePickedPrefix(MsgPiecePicked pickMsg)
        {
            if (itemAuthority == null || pickMsg == null)
            {
                return;
            }

            try
            {
                LobbyManager manager = LobbyManager.instance;
                if (manager == null || manager.PlayerTracker == null)
                {
                    return;
                }

                GamePlayer player = manager.PlayerTracker.GetGamePlayer(pickMsg.PlayerNumber);
                PartyPickCursor cursor = player != null ? player.PartyPickCursor : null;
                if (cursor == null || !cursor.hasAuthority)
                {
                    return;
                }

                GameObject optimisticObject = cursor.NetworkpickedPiece;
                PickableBlock optimisticPiece = optimisticObject != null
                    ? optimisticObject.GetComponent<PickableBlock>()
                    : null;
                uint optimisticId = optimisticPiece != null ? optimisticPiece.netIdValue : 0u;
                if (optimisticId == pickMsg.PickableNetID)
                {
                    return;
                }

                GameObject authoritativeObject = pickMsg.PickableNetID == 0
                    ? null
                    : ClientScene.FindLocalObject(new NetworkInstanceId(pickMsg.PickableNetID));
                cursor.NetworkpickedPiece = authoritativeObject;

                string notice = pickMsg.PickableNetID == 0
                    ? "That item was already taken; no replacement was available."
                    : "That item was already taken; a different item was assigned.";
                UserMessageManager messages = UserMessageManager.Instance;
                if (messages != null)
                {
                    messages.UserMessage(notice, 3f, UserMessageManager.UserMsgPriority.lo, true);
                }
                log.Warning("Local selection reconciled: player=" + pickMsg.PlayerNumber
                    + " requested=" + optimisticId
                    + " assigned=" + pickMsg.PickableNetID);
            }
            catch (System.Exception exception)
            {
                log.Error("DuplicateSelectionFix: local cursor reconciliation failed; vanilla item delivery will continue. " + exception);
            }
        }

        // Target: GameState.Start(), UCH 1.13.13. Postfix repairs its hardcoded 300 FPS assignment.
        internal static void GameStateStartPostfix()
        {
            if (backgroundController != null)
            {
                backgroundController.ReapplyAfterGameInitialization();
            }
        }
    }
}
