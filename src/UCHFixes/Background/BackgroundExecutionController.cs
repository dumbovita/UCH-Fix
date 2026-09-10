using System;
using UCHFixes.Diagnostics;
using UnityEngine;

namespace UCHFixes.Background
{
    internal sealed class BackgroundExecutionController
    {
        private readonly ModLog log;
        private readonly int backgroundFrameRate;
        private readonly bool manageVSync;
        private bool focused;
        private bool backgroundPolicyApplied;
        private int savedTargetFrameRate;
        private int savedVSyncCount;
        private float focusLostAt;
        private float lastWatchdogAt;
        private int backgroundFrames;
        private bool observingInitialForegroundSettings;

        internal BackgroundExecutionController(ModLog log, int configuredFrameRate, bool manageVSync)
        {
            this.log = log;
            backgroundFrameRate = Math.Max(30, configuredFrameRate);
            this.manageVSync = manageVSync;
            focused = Application.isFocused;
            observingInitialForegroundSettings = !focused;
        }

        internal void Initialize()
        {
            Application.runInBackground = true;
            if (!focused)
            {
                EnterBackground();
            }
        }

        internal void OnApplicationFocus(bool hasFocus)
        {
            if (focused == hasFocus && backgroundPolicyApplied == !hasFocus)
            {
                return;
            }

            focused = hasFocus;
            if (hasFocus)
            {
                LeaveBackground();
            }
            else
            {
                EnterBackground();
            }
        }

        internal void Update()
        {
            if (!focused)
            {
                backgroundFrames++;
            }

            float now = Time.realtimeSinceStartup;
            if (now - lastWatchdogAt < 1f)
            {
                return;
            }

            lastWatchdogAt = now;
            Application.runInBackground = true;
            if (!focused)
            {
                CaptureLateInitialSettings();
                ApplyBackgroundFramePolicy();
            }
        }

        internal void ReapplyAfterGameInitialization()
        {
            Application.runInBackground = true;
            if (!focused)
            {
                if (backgroundPolicyApplied)
                {
                    // GameState.Start has just replaced the pre-start value with vanilla's
                    // foreground target (300 in 1.13.13). Preserve that newer value.
                    savedTargetFrameRate = Application.targetFrameRate;
                }
                CaptureLateInitialSettings();
                ApplyBackgroundFramePolicy();
            }
        }

        internal void Dispose()
        {
            if (backgroundPolicyApplied)
            {
                RestoreForegroundFramePolicy();
            }
        }

        private void EnterBackground()
        {
            if (!backgroundPolicyApplied)
            {
                savedTargetFrameRate = Application.targetFrameRate;
                savedVSyncCount = QualitySettings.vSyncCount;
                focusLostAt = Time.realtimeSinceStartup;
                backgroundFrames = 0;
                backgroundPolicyApplied = true;
            }

            Application.runInBackground = true;
            ApplyBackgroundFramePolicy();
            log.Info("BackgroundSyncFix: focus lost; runInBackground=true targetFrameRate="
                + backgroundFrameRate + " vSync=" + QualitySettings.vSyncCount);
        }

        private void LeaveBackground()
        {
            if (!backgroundPolicyApplied)
            {
                Application.runInBackground = true;
                return;
            }

            float duration = Math.Max(0f, Time.realtimeSinceStartup - focusLostAt);
            float observedRate = duration > 0f ? backgroundFrames / duration : 0f;
            RestoreForegroundFramePolicy();
            observingInitialForegroundSettings = false;
            log.Info("BackgroundSyncFix: focus restored after " + duration.ToString("F1")
                + "s; observedBackgroundUpdates=" + observedRate.ToString("F1")
                + "/s restoredTargetFrameRate=" + Application.targetFrameRate
                + " restoredVSync=" + QualitySettings.vSyncCount);
        }

        private void ApplyBackgroundFramePolicy()
        {
            if (manageVSync)
            {
                QualitySettings.vSyncCount = 0;
            }
            Application.targetFrameRate = backgroundFrameRate;
        }

        private void CaptureLateInitialSettings()
        {
            if (!observingInitialForegroundSettings)
            {
                return;
            }

            int currentTarget = Application.targetFrameRate;
            if (currentTarget != backgroundFrameRate)
            {
                savedTargetFrameRate = currentTarget;
            }

            if (manageVSync && QualitySettings.vSyncCount != 0)
            {
                // SaveFileData.ApplySettings can run asynchronously after GameState.Start.
                savedVSyncCount = QualitySettings.vSyncCount;
            }
        }

        private void RestoreForegroundFramePolicy()
        {
            Application.targetFrameRate = savedTargetFrameRate;
            if (manageVSync)
            {
                QualitySettings.vSyncCount = savedVSyncCount;
            }
            backgroundPolicyApplied = false;
        }
    }
}
