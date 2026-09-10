using System.Collections.Generic;

namespace UCHFixes.Selection
{
    internal sealed class ClaimRegistry
    {
        private struct PlayerClaim
        {
            internal uint Requested;
            internal uint Assigned;
        }

        private readonly object sync = new object();
        private readonly List<uint> selectionOrder = new List<uint>();
        private readonly HashSet<uint> validSelections = new HashSet<uint>();
        private readonly Dictionary<uint, int> owners = new Dictionary<uint, int>();
        private readonly Dictionary<int, PlayerClaim> playerClaims = new Dictionary<int, PlayerClaim>();
        private long sessionId;
        private bool active;

        internal long BeginSession(IEnumerable<uint> selectionIds)
        {
            lock (sync)
            {
                sessionId++;
                active = true;
                selectionOrder.Clear();
                validSelections.Clear();
                owners.Clear();
                playerClaims.Clear();

                foreach (uint selectionId in selectionIds)
                {
                    if (selectionId != 0 && validSelections.Add(selectionId))
                    {
                        selectionOrder.Add(selectionId);
                    }
                }

                return sessionId;
            }
        }

        internal void EndSession()
        {
            lock (sync)
            {
                active = false;
            }
        }

        internal long CurrentSession
        {
            get { lock (sync) return sessionId; }
        }

        internal ClaimDecision TryClaim(long expectedSessionId, uint requestedSelectionId, int playerId)
        {
            lock (sync)
            {
                if (!active)
                {
                    return Decision(ClaimDisposition.Inactive, requestedSelectionId, 0, playerId);
                }

                if (expectedSessionId != sessionId)
                {
                    return Decision(ClaimDisposition.StaleSession, requestedSelectionId, 0, playerId);
                }

                PlayerClaim existing;
                if (playerClaims.TryGetValue(playerId, out existing))
                {
                    ClaimDisposition disposition = existing.Requested == requestedSelectionId
                        ? ClaimDisposition.DuplicateRequest
                        : ClaimDisposition.PlayerAlreadyCompleted;
                    return Decision(disposition, requestedSelectionId, existing.Assigned, playerId);
                }

                if (requestedSelectionId == 0)
                {
                    playerClaims.Add(playerId, new PlayerClaim { Requested = 0, Assigned = 0 });
                    return Decision(ClaimDisposition.AcceptedNoItem, 0, 0, playerId);
                }

                if (!validSelections.Contains(requestedSelectionId))
                {
                    return Decision(ClaimDisposition.InvalidSelection, requestedSelectionId, 0, playerId);
                }

                if (!owners.ContainsKey(requestedSelectionId))
                {
                    owners.Add(requestedSelectionId, playerId);
                    playerClaims.Add(playerId, new PlayerClaim { Requested = requestedSelectionId, Assigned = requestedSelectionId });
                    return Decision(ClaimDisposition.AcceptedRequested, requestedSelectionId, requestedSelectionId, playerId);
                }

                for (int i = 0; i < selectionOrder.Count; i++)
                {
                    uint fallback = selectionOrder[i];
                    if (!owners.ContainsKey(fallback))
                    {
                        owners.Add(fallback, playerId);
                        playerClaims.Add(playerId, new PlayerClaim { Requested = requestedSelectionId, Assigned = fallback });
                        return Decision(ClaimDisposition.AcceptedFallback, requestedSelectionId, fallback, playerId);
                    }
                }

                playerClaims.Add(playerId, new PlayerClaim { Requested = requestedSelectionId, Assigned = 0 });
                return Decision(ClaimDisposition.AcceptedNoItem, requestedSelectionId, 0, playerId);
            }
        }

        private ClaimDecision Decision(ClaimDisposition disposition, uint requested, uint assigned, int player)
        {
            return new ClaimDecision(disposition, sessionId, requested, assigned, player);
        }
    }
}
