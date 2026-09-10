using System;
using System.Reflection;
using HarmonyLib;
using UCHFixes.Background;
using UCHFixes.Diagnostics;
using UCHFixes.Networking;
using UCHFixes.Patches;
using UnityEngine.Networking;

namespace UCHFixes.Compatibility
{
    internal sealed class PatchInstaller
    {
        private const BindingFlags InstanceAnyVisibility = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private readonly Harmony harmony;
        private readonly ModLog log;

        internal PatchInstaller(Harmony harmony, ModLog log)
        {
            this.harmony = harmony;
            this.log = log;
        }

        internal ItemSelectionAuthority InstallItemSelectionFix()
        {
            MethodInfo distributor = FindExact(typeof(LobbyManager), "distributeServerMessage", typeof(void), typeof(NetworkMessage));
            MethodInfo showBox = FindExact(typeof(PartyBox), "ShowBox", typeof(void), typeof(bool));
            MethodInfo hideBox = FindExact(typeof(PartyBox), "Hide", typeof(void), typeof(bool));
            MethodInfo onPiecePicked = FindExact(typeof(PartyBox), "OnPiecePicked", typeof(void), typeof(MsgPiecePicked));
            FieldInfo pieces = typeof(PartyBox).GetField("pieces", InstanceAnyVisibility);

            if (distributor == null || showBox == null || hideBox == null || onPiecePicked == null || pieces == null)
            {
                log.Error("DuplicateSelectionFix: required 1.13.13-compatible targets were not found; feature disabled.");
                return null;
            }

            Type expectedPiecesType = typeof(System.Collections.Generic.List<PickableBlock>);
            if (!expectedPiecesType.IsAssignableFrom(pieces.FieldType))
            {
                log.Error("DuplicateSelectionFix: PartyBox.pieces has unexpected type " + pieces.FieldType.FullName + "; feature disabled.");
                return null;
            }

            ItemSelectionAuthority authority = new ItemSelectionAuthority(pieces, log);
            PatchHooks.Configure(authority, null, log);
            try
            {
                HarmonyMethod distributorPrefix = Hook("DistributeServerMessagePrefix");
                distributorPrefix.priority = Priority.First;
                harmony.Patch(distributor, prefix: distributorPrefix);
                harmony.Patch(showBox, postfix: Hook("PartyBoxShowPostfix"));
                harmony.Patch(hideBox, prefix: Hook("PartyBoxHidePrefix"));
                harmony.Patch(onPiecePicked, prefix: Hook("PartyBoxOnPiecePickedPrefix"));
            }
            catch (Exception exception)
            {
                TryUnpatch(distributor);
                TryUnpatch(showBox);
                TryUnpatch(hideBox);
                TryUnpatch(onPiecePicked);
                PatchHooks.Configure(null, null, log);
                log.Error("DuplicateSelectionFix: patch installation failed and was rolled back; feature disabled. " + exception);
                return null;
            }
            log.Compatibility("DuplicateSelectionFix targets validated: LobbyManager.distributeServerMessage, PartyBox.ShowBox, PartyBox.Hide, PartyBox.OnPiecePicked, PartyBox.pieces.");
            return authority;
        }

        internal bool InstallBackgroundFix(BackgroundExecutionController controller, ItemSelectionAuthority authority)
        {
            PatchHooks.Configure(authority, controller, log);
            MethodInfo start = FindExact(typeof(GameState), "Start", typeof(void));
            if (start == null)
            {
                log.Warning("BackgroundSyncFix: GameState.Start target unavailable; bootstrap and watchdog remain enabled.");
                return false;
            }

            try
            {
                harmony.Patch(start, postfix: Hook("GameStateStartPostfix"));
            }
            catch (Exception exception)
            {
                TryUnpatch(start);
                log.Warning("BackgroundSyncFix: GameState.Start patch failed; bootstrap and watchdog remain enabled. " + exception);
                return false;
            }
            log.Compatibility("BackgroundSyncFix target validated: GameState.Start().");
            return true;
        }

        private void TryUnpatch(MethodInfo method)
        {
            try
            {
                harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
            }
            catch (Exception exception)
            {
                log.Error("Could not roll back patch on " + method.DeclaringType.FullName + "." + method.Name + ": " + exception);
            }
        }

        private static HarmonyMethod Hook(string name)
        {
            MethodInfo method = typeof(PatchHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(typeof(PatchHooks).FullName, name);
            }
            return new HarmonyMethod(method);
        }

        private static MethodInfo FindExact(Type type, string name, Type returnType, params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, InstanceAnyVisibility, null, parameters, null);
            return method != null && method.ReturnType == returnType ? method : null;
        }
    }
}
