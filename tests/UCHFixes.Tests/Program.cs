using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UCHFixes.Selection;

namespace UCHFixes.Tests
{
    internal static class Program
    {
        private static int failures;

        private static int Main()
        {
            Run("FirstClaimWinsSecondGetsUniqueFallback", FirstClaimWinsSecondGetsUniqueFallback);
            Run("TwoConcurrentClaimsProduceUniqueAssignments", TwoConcurrentClaimsProduceUniqueAssignments);
            Run("ThreeConcurrentClaimsProduceUniqueAssignments", ThreeConcurrentClaimsProduceUniqueAssignments);
            Run("RepeatedRequestIsIdempotent", RepeatedRequestIsIdempotent);
            Run("RepeatedFallbackRequestIsIdempotent", RepeatedFallbackRequestIsIdempotent);
            Run("PlayerCannotClaimTwice", PlayerCannotClaimTwice);
            Run("StaleRoundClaimRejected", StaleRoundClaimRejected);
            Run("OldSelectionIdRejectedInNewSession", OldSelectionIdRejectedInNewSession);
            Run("InvalidSelectionRejected", InvalidSelectionRejected);
            Run("RoundResetClearsPreviousClaims", RoundResetClearsPreviousClaims);
            Run("TimeoutRequestIsIdempotent", TimeoutRequestIsIdempotent);
            Run("ExhaustedPoolReturnsNoItem", ExhaustedPoolReturnsNoItem);
            Run("EndedSessionRejectsClaims", EndedSessionRejectsClaims);

            Console.WriteLine(failures == 0 ? "All claim-registry tests passed." : failures + " test(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        private static void FirstClaimWinsSecondGetsUniqueFallback()
        {
            ClaimRegistry registry = Registry(17, 18, 19);
            long session = registry.CurrentSession;
            ClaimDecision first = registry.TryClaim(session, 17, 1);
            ClaimDecision second = registry.TryClaim(session, 17, 2);
            Equal(ClaimDisposition.AcceptedRequested, first.Disposition);
            Equal((uint)17, first.AssignedSelectionId);
            Equal(ClaimDisposition.AcceptedFallback, second.Disposition);
            NotEqual(first.AssignedSelectionId, second.AssignedSelectionId);
        }

        private static void TwoConcurrentClaimsProduceUniqueAssignments()
        {
            ConcurrentRace(2, new uint[] { 17, 18, 19 });
        }

        private static void ThreeConcurrentClaimsProduceUniqueAssignments()
        {
            ConcurrentRace(3, new uint[] { 17, 18, 19, 20 });
        }

        private static void ConcurrentRace(int players, uint[] selections)
        {
            ClaimRegistry registry = Registry(selections);
            long session = registry.CurrentSession;
            Barrier barrier = new Barrier(players);
            ConcurrentBag<ClaimDecision> results = new ConcurrentBag<ClaimDecision>();
            Task[] tasks = Enumerable.Range(1, players).Select(player => Task.Run(() =>
            {
                barrier.SignalAndWait();
                results.Add(registry.TryClaim(session, selections[0], player));
            })).ToArray();
            Task.WaitAll(tasks);

            uint[] assigned = results.Select(result => result.AssignedSelectionId).ToArray();
            Equal(players, assigned.Distinct().Count());
            Equal(1, results.Count(result => result.Disposition == ClaimDisposition.AcceptedRequested));
        }

        private static void RepeatedRequestIsIdempotent()
        {
            ClaimRegistry registry = Registry(17, 18);
            long session = registry.CurrentSession;
            ClaimDecision first = registry.TryClaim(session, 17, 1);
            ClaimDecision duplicate = registry.TryClaim(session, 17, 1);
            Equal(ClaimDisposition.AcceptedRequested, first.Disposition);
            Equal(ClaimDisposition.DuplicateRequest, duplicate.Disposition);
            Equal((uint)17, duplicate.AssignedSelectionId);
            False(duplicate.ShouldBroadcast);
        }

        private static void RepeatedFallbackRequestIsIdempotent()
        {
            ClaimRegistry registry = Registry(17, 18, 19);
            long session = registry.CurrentSession;
            registry.TryClaim(session, 17, 1);
            ClaimDecision fallback = registry.TryClaim(session, 17, 2);
            ClaimDecision duplicate = registry.TryClaim(session, 17, 2);
            Equal(ClaimDisposition.AcceptedFallback, fallback.Disposition);
            Equal(ClaimDisposition.DuplicateRequest, duplicate.Disposition);
            Equal(fallback.AssignedSelectionId, duplicate.AssignedSelectionId);
            False(duplicate.ShouldBroadcast);
        }

        private static void PlayerCannotClaimTwice()
        {
            ClaimRegistry registry = Registry(17, 18);
            long session = registry.CurrentSession;
            registry.TryClaim(session, 17, 1);
            Equal(ClaimDisposition.PlayerAlreadyCompleted, registry.TryClaim(session, 18, 1).Disposition);
        }

        private static void StaleRoundClaimRejected()
        {
            ClaimRegistry registry = Registry(17, 18);
            long oldSession = registry.CurrentSession;
            registry.BeginSession(new uint[] { 21, 22 });
            Equal(ClaimDisposition.StaleSession, registry.TryClaim(oldSession, 17, 1).Disposition);
        }

        private static void OldSelectionIdRejectedInNewSession()
        {
            ClaimRegistry registry = Registry(17, 18);
            long next = registry.BeginSession(new uint[] { 21, 22 });
            Equal(ClaimDisposition.InvalidSelection, registry.TryClaim(next, 17, 1).Disposition);
        }

        private static void InvalidSelectionRejected()
        {
            ClaimRegistry registry = Registry(17, 18);
            Equal(ClaimDisposition.InvalidSelection, registry.TryClaim(registry.CurrentSession, 999, 1).Disposition);
        }

        private static void RoundResetClearsPreviousClaims()
        {
            ClaimRegistry registry = Registry(17, 18);
            registry.TryClaim(registry.CurrentSession, 17, 1);
            long next = registry.BeginSession(new uint[] { 17, 18 });
            Equal(ClaimDisposition.AcceptedRequested, registry.TryClaim(next, 17, 2).Disposition);
        }

        private static void TimeoutRequestIsIdempotent()
        {
            ClaimRegistry registry = Registry(17, 18);
            long session = registry.CurrentSession;
            Equal(ClaimDisposition.AcceptedNoItem, registry.TryClaim(session, 0, 1).Disposition);
            Equal(ClaimDisposition.DuplicateRequest, registry.TryClaim(session, 0, 1).Disposition);
        }

        private static void ExhaustedPoolReturnsNoItem()
        {
            ClaimRegistry registry = Registry(17);
            long session = registry.CurrentSession;
            registry.TryClaim(session, 17, 1);
            Equal(ClaimDisposition.AcceptedNoItem, registry.TryClaim(session, 17, 2).Disposition);
        }

        private static void EndedSessionRejectsClaims()
        {
            ClaimRegistry registry = Registry(17, 18);
            long session = registry.CurrentSession;
            registry.EndSession();
            Equal(ClaimDisposition.Inactive, registry.TryClaim(session, 17, 1).Disposition);
        }

        private static ClaimRegistry Registry(params uint[] selections)
        {
            ClaimRegistry registry = new ClaimRegistry();
            registry.BeginSession(selections);
            return registry;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
            }
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
            }
        }

        private static void NotEqual<T>(T left, T right)
        {
            if (object.Equals(left, right))
            {
                throw new InvalidOperationException("Expected distinct values, got " + left + ".");
            }
        }

        private static void False(bool value)
        {
            if (value)
            {
                throw new InvalidOperationException("Expected false.");
            }
        }
    }
}
