namespace Clarion.SourceResolution
{
    /// <summary>
    /// The per-solution state the Clarion IDE persists in
    /// <c>%APPDATA%\SoftVelocity\Clarion\&lt;ver&gt;\preferences\&lt;Sln&gt;.sln.&lt;hash&gt;.xml</c>.
    ///
    /// This is the authoritative, survives-Clean source of the active build
    /// configuration — the primary config anchor for source resolution, ahead
    /// of the build-only <c>.sln.cache</c>. Ports the <c>IdePreferences</c>
    /// shape from the extension's <c>ClarionIdePreferences.ts</c>.
    /// </summary>
    public sealed class IdePreferences
    {
        /// <summary>Startup project GUID, e.g. "{641834BD-...}". May be empty/null.</summary>
        public string? StartupProjectGuid { get; set; }

        /// <summary>Active configuration, e.g. "Debug" / "Release". Null if absent.</summary>
        public string? ActiveConfiguration { get; set; }

        /// <summary>Active platform, e.g. "Win32". Null if absent.</summary>
        public string? ActivePlatform { get; set; }
    }
}
