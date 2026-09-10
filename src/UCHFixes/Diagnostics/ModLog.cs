using BepInEx.Logging;

namespace UCHFixes.Diagnostics
{
    internal sealed class ModLog
    {
        private readonly ManualLogSource source;
        private readonly bool verbose;
        private readonly bool compatibility;

        internal ModLog(ManualLogSource source, bool verbose, bool compatibility)
        {
            this.source = source;
            this.verbose = verbose;
            this.compatibility = compatibility;
        }

        internal bool IsVerbose { get { return verbose; } }
        internal void Info(string message) { source.LogInfo(message); }
        internal void Warning(string message) { source.LogWarning(message); }
        internal void Error(string message) { source.LogError(message); }
        internal void Debug(string message) { if (verbose) source.LogInfo("[verbose] " + message); }
        internal void Compatibility(string message) { if (compatibility) source.LogInfo("[compatibility] " + message); }
    }
}
