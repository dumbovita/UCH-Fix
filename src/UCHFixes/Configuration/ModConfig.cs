using BepInEx.Configuration;

namespace UCHFixes.Configuration
{
    internal sealed class ModConfig
    {
        internal ModConfig(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true, "Enable UCH Fixes.");
            BackgroundSync = config.Bind("Fixes", "BackgroundSync", true, "Keep the Unity/UNET update loop alive while unfocused.");
            DuplicateItemSelection = config.Bind("Fixes", "DuplicateItemSelection", true, "Let the host serialize party-box item claims.");
            BackgroundFrameRate = config.Bind("Background", "TargetFrameRate", 30, "Requested frame rate while unfocused. Configured values below 30 are clamped to 30 for network health.");
            ManageVSync = config.Bind("Background", "ManageVSync", true, "Temporarily disable VSync while unfocused so the background frame-rate setting is effective.");
            VerboseLogging = config.Bind("Diagnostics", "VerboseLogging", false, "Log accepted claims and background watchdog details.");
            CompatibilityLogging = config.Bind("Diagnostics", "CompatibilityLogging", true, "Log patch-target validation and feature fallbacks.");
        }

        internal ConfigEntry<bool> Enabled { get; private set; }
        internal ConfigEntry<bool> BackgroundSync { get; private set; }
        internal ConfigEntry<bool> DuplicateItemSelection { get; private set; }
        internal ConfigEntry<int> BackgroundFrameRate { get; private set; }
        internal ConfigEntry<bool> ManageVSync { get; private set; }
        internal ConfigEntry<bool> VerboseLogging { get; private set; }
        internal ConfigEntry<bool> CompatibilityLogging { get; private set; }
    }
}
