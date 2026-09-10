using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UCHFixes.Diagnostics;
using UCHFixes.Selection;
using UnityEngine;
using UnityEngine.Networking;

namespace UCHFixes.Networking
{
    internal sealed class ItemSelectionAuthority
    {
        private enum GateState
        {
            VanillaFallback,
            Active,
            Closed
        }

        private readonly ClaimRegistry registry = new ClaimRegistry();
        private readonly FieldInfo piecesField;
        private readonly ModLog log;
        private GateState state;

        internal ItemSelectionAuthority(FieldInfo piecesField, ModLog log)
        {
            this.piecesField = piecesField;
            this.log = log;
        }

        internal void BeginBox(PartyBox partyBox)
        {
            if (!NetworkServer.active || partyBox == null || !partyBox.HasAuthority)
            {
                return;
            }

            try
            {
                IList pieces = piecesField.GetValue(partyBox) as IList;
                if (pieces == null)
                {
                    state = GateState.VanillaFallback;
                    log.Error("DuplicateSelectionFix: PartyBox.pieces was not a list; using vanilla relay for this box.");
                    return;
                }

                List<uint> selectionIds = new List<uint>(pieces.Count);
                for (int i = 0; i < pieces.Count; i++)
                {
                    PickableBlock piece = pieces[i] as PickableBlock;
                    if (piece != null && piece.netIdValue != 0)
                    {
                        selectionIds.Add(piece.netIdValue);
                    }
                }

                state = selectionIds.Count > 0 ? GateState.Active : GateState.VanillaFallback;
                long session = registry.BeginSession(selectionIds);
                if (state == GateState.Active)
                {
                    if (log.IsVerbose)
                    {
                        log.Debug("Party-box claim session started: session=" + session + " selections=" + selectionIds.Count);
                    }
                }
                else
                {
                    log.Warning("DuplicateSelectionFix: no spawned selection netIds were found; using vanilla relay for this box.");
                }
            }
            catch (Exception exception)
            {
                state = GateState.VanillaFallback;
                registry.EndSession();
                log.Error("DuplicateSelectionFix: failed to begin party-box session; using vanilla relay. " + exception);
            }
        }

        internal void EndBox()
        {
            if (state == GateState.Active)
            {
                state = GateState.Closed;
            }
            registry.EndSession();
        }

        internal bool TryHandle(NetworkMessage networkMessage)
        {
            if (!NetworkServer.active || networkMessage == null || networkMessage.msgType != NetMsgTypes.PiecePicked)
            {
                return false;
            }

            if (state == GateState.VanillaFallback)
            {
                return false;
            }
            if (state == GateState.Closed)
            {
                if (log.IsVerbose)
                {
                    log.Debug("Late PiecePicked packet suppressed after party-box close.");
                }
                return true;
            }

            MsgPiecePicked request;
            try
            {
                request = networkMessage.ReadMessage<MsgPiecePicked>();
                networkMessage.reader.SeekZero();
            }
            catch (Exception exception)
            {
                TryRewind(networkMessage);
                log.Error("DuplicateSelectionFix: could not deserialize PiecePicked; falling back to vanilla relay. " + exception);
                return false;
            }

            if (!ConnectionOwnsPlayer(networkMessage.conn, request.PlayerNumber))
            {
                log.Warning("Rejected unauthenticated selection claim: player=" + request.PlayerNumber + " selection=" + request.PickableNetID);
                return true;
            }

            ClaimDecision decision = registry.TryClaim(registry.CurrentSession, request.PickableNetID, request.PlayerNumber);
            if (!decision.ShouldBroadcast)
            {
                if (decision.Disposition != ClaimDisposition.DuplicateRequest)
                {
                    log.Warning("Selection claim dropped: session=" + decision.SessionId
                        + " selection=" + request.PickableNetID
                        + " player=" + request.PlayerNumber
                        + " result=" + decision.Disposition);
                }
                else
                {
                    if (log.IsVerbose)
                    {
                        log.Debug("Duplicate selection packet ignored: session=" + decision.SessionId
                            + " selection=" + request.PickableNetID
                            + " player=" + request.PlayerNumber);
                    }
                }
                return true;
            }

            MsgPiecePicked authoritative = new MsgPiecePicked
            {
                PickableNetID = decision.AssignedSelectionId,
                PlayerNumber = request.PlayerNumber,
                PieceID = request.PieceID
            };

            try
            {
                NetworkServer.SendToAll(NetMsgTypes.PiecePicked, authoritative);
            }
            catch (Exception exception)
            {
                log.Error("DuplicateSelectionFix: authoritative relay failed after reserving a claim; packet was suppressed to preserve uniqueness. " + exception);
                return true;
            }

            if (decision.Disposition == ClaimDisposition.AcceptedFallback)
            {
                log.Warning("Duplicate selection rejected and reconciled: session=" + decision.SessionId
                    + " requested=" + decision.RequestedSelectionId
                    + " assigned=" + decision.AssignedSelectionId
                    + " player=" + decision.PlayerId);
            }
            else if (decision.Disposition == ClaimDisposition.AcceptedNoItem && request.PickableNetID != 0)
            {
                log.Warning("Duplicate selection rejected with no fallback: session=" + decision.SessionId
                    + " requested=" + decision.RequestedSelectionId
                    + " player=" + decision.PlayerId);
            }
            else
            {
                if (log.IsVerbose)
                {
                    log.Debug("Selection claim accepted: session=" + decision.SessionId
                        + " selection=" + decision.AssignedSelectionId
                        + " player=" + decision.PlayerId);
                }
            }

            return true;
        }

        private static void TryRewind(NetworkMessage message)
        {
            try
            {
                if (message != null && message.reader != null)
                {
                    message.reader.SeekZero();
                }
            }
            catch
            {
                // The original handler will produce the canonical networking error.
            }
        }

        private static bool ConnectionOwnsPlayer(NetworkConnection connection, int playerNumber)
        {
            if (connection == null || connection.playerControllers == null)
            {
                return false;
            }

            foreach (PlayerController controller in connection.playerControllers)
            {
                if (controller == null || controller.gameObject == null)
                {
                    continue;
                }

                GamePlayer gamePlayer = controller.gameObject.GetComponent<GamePlayer>();
                if (gamePlayer != null && gamePlayer.networkNumber == playerNumber)
                {
                    return true;
                }

                LobbyPlayer lobbyPlayer = controller.gameObject.GetComponent<LobbyPlayer>();
                if (lobbyPlayer != null && lobbyPlayer.networkNumber == playerNumber)
                {
                    return true;
                }
            }

            LobbyManager manager = LobbyManager.instance;
            if (manager == null)
            {
                return false;
            }

            LobbyPlayer trackedLobbyPlayer = manager.GetLobbyPlayer(playerNumber);
            if (trackedLobbyPlayer != null && trackedLobbyPlayer.connectionToClient == connection)
            {
                return true;
            }

            if (manager.PlayerTracker != null)
            {
                GamePlayer trackedGamePlayer = manager.PlayerTracker.GetGamePlayer(playerNumber);
                if (trackedGamePlayer != null && trackedGamePlayer.connectionToClient == connection)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
