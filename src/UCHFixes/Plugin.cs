using System;
using BepInEx;
using HarmonyLib;
using UCHFixes.Background;
using UCHFixes.Compatibility;
using UCHFixes.Configuration;
using UCHFixes.Diagnostics;
using UCHFixes.Networking;
using UnityEngine;

namespace UCHFixes
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "dev.dumbovita.uchfixes";
        public const string PluginName = "UCH Fixes";
        public const string PluginVersion = "0.1.0";

        private Harmony harmony;
        private BackgroundExecutionController backgroundController;

        private void Awake()
        {
            ModConfig settings = new ModConfig(Config);
            ModLog log = new ModLog(Logger, settings.VerboseLogging.Value, settings.CompatibilityLogging.Value);

            log.Info("Game version: " + Application.version);
            log.Info("Unity version: " + Application.unityVersion);
            log.Info("Runtime: " + (Type.GetType("Mono.Runtime") != null ? "Mono" : ".NET/unknown") + " " + (IntPtr.Size * 8) + "-bit");
            log.Info("BepInEx version: " + typeof(BaseUnityPlugin).Assembly.GetName().Version);
            log.Info("Networking: Unity UNET HLAPI (validated from com.unity.multiplayer-hlapi.Runtime.dll)");

            if (!settings.Enabled.Value)
            {
                log.Info("General.Enabled=false; no fixes installed.");
                return;
            }

            harmony = new Harmony(PluginGuid);
            PatchInstaller installer = new PatchInstaller(harmony, log);
            ItemSelectionAuthority authority = null;

            if (settings.DuplicateItemSelection.Value)
            {
                authority = installer.InstallItemSelectionFix();
            }
            log.Info("DuplicateSelectionFix: " + (authority != null ? "enabled" : "disabled"));

            if (settings.BackgroundSync.Value)
            {
                backgroundController = new BackgroundExecutionController(
                    log,
                    settings.BackgroundFrameRate.Value,
                    settings.ManageVSync.Value);
                backgroundController.Initialize();
                installer.InstallBackgroundFix(backgroundController, authority);
            }
            log.Info("BackgroundSyncFix: " + (backgroundController != null ? "enabled" : "disabled"));
        }

        private void Update()
        {
            if (backgroundController != null)
            {
                backgroundController.Update();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (backgroundController != null)
            {
                backgroundController.OnApplicationFocus(hasFocus);
            }
        }

        private void OnDestroy()
        {
            if (backgroundController != null)
            {
                backgroundController.Dispose();
            }
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }
        }
    }
}
