namespace UCHFixes.Selection
{
    internal enum ClaimDisposition
    {
        AcceptedRequested,
        AcceptedFallback,
        AcceptedNoItem,
        DuplicateRequest,
        PlayerAlreadyCompleted,
        InvalidSelection,
        StaleSession,
        Inactive
    }

    internal struct ClaimDecision
    {
        internal ClaimDecision(ClaimDisposition disposition, long sessionId, uint requestedSelectionId, uint assignedSelectionId, int playerId)
        {
            Disposition = disposition;
            SessionId = sessionId;
            RequestedSelectionId = requestedSelectionId;
            AssignedSelectionId = assignedSelectionId;
            PlayerId = playerId;
        }

        internal ClaimDisposition Disposition { get; private set; }
        internal long SessionId { get; private set; }
        internal uint RequestedSelectionId { get; private set; }
        internal uint AssignedSelectionId { get; private set; }
        internal int PlayerId { get; private set; }

        internal bool ShouldBroadcast
        {
            get
            {
                return Disposition == ClaimDisposition.AcceptedRequested
                    || Disposition == ClaimDisposition.AcceptedFallback
                    || Disposition == ClaimDisposition.AcceptedNoItem;
            }
        }
    }
}
