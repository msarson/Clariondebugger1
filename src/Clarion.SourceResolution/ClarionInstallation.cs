using System.Collections.Generic;

namespace Clarion.SourceResolution
{
    /// <summary>
    /// One installed Clarion IDE, keyed by its AppData version folder
    /// (e.g. "11.0", "12.0"). Ports the <c>ClarionInstallation</c> interface
    /// from the extension's <c>ClarionInstallationDetector.ts</c>.
    /// </summary>
    public sealed class ClarionInstallation
    {
        /// <summary>The AppData folder name, e.g. "12.0", "11.1".</summary>
        public string IdeVersion { get; set; } = string.Empty;

        /// <summary>Full path to the ClarionProperties.xml this was parsed from.</summary>
        public string PropertiesPath { get; set; } = string.Empty;

        /// <summary>The compiler versions registered under <c>Clarion.Versions</c>.</summary>
        public IReadOnlyList<ClarionCompilerVersion> CompilerVersions { get; set; }
            = new List<ClarionCompilerVersion>();
    }

    /// <summary>
    /// A single registered compiler version (one entry under
    /// <c>Clarion.Versions</c> in ClarionProperties.xml), e.g.
    /// "Clarion 11.1.13855". This is the unit the user picks at first-open and
    /// the source of the redirection anchors (bin path, .red file, macros).
    /// </summary>
    public sealed class ClarionCompilerVersion
    {
        /// <summary>The registered name, e.g. "Clarion 11.1.13855".</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The bin directory, e.g. "C:\Clarion\Clarion11.1\bin".</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>The redirection file name, e.g. "Clarion110.red" (no path).</summary>
        public string RedirectionFile { get; set; } = string.Empty;

        /// <summary>
        /// Global redirection macros (lowercased keys), e.g. root, reddir.
        /// Feed these into the redirection parser as the macro context.
        /// </summary>
        public IReadOnlyDictionary<string, string> Macros { get; set; }
            = new Dictionary<string, string>();

        /// <summary>The raw libsrc value (a ';'-separated path list).</summary>
        public string LibSrc { get; set; } = string.Empty;
    }
}
