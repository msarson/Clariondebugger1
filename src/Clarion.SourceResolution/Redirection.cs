using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clarion.SourceResolution
{
    /// <summary>Where a resolved file path came from.</summary>
    public enum FilePathSource
    {
        /// <summary>Matched a redirection entry's search path.</summary>
        Redirected,
        /// <summary>Direct hit in the project directory (or an absolute/pathed include).</summary>
        Project,
        /// <summary>Matched a libsrc fallback path.</summary>
        LibSrc,
    }

    /// <summary>One parsed redirection rule: <c>mask = path;path;...</c> within a section.</summary>
    public sealed class RedirectionEntry
    {
        /// <summary>The .red file this entry was read from.</summary>
        public string RedFile { get; set; } = string.Empty;
        /// <summary>The section it lived in, case-preserved (e.g. "Debug", "Common").</summary>
        public string Section { get; set; } = string.Empty;
        /// <summary>The file mask, e.g. "*.clw", "*.*".</summary>
        public string Extension { get; set; } = string.Empty;
        /// <summary>The macro-resolved search paths (as written, may be relative).</summary>
        public IReadOnlyList<string> Paths { get; set; } = new List<string>();
    }

    /// <summary>A resolved file path plus the rule/source that produced it.</summary>
    public sealed class ResolvedFilePath
    {
        public string Path { get; }
        public FilePathSource Source { get; }
        public RedirectionEntry? Entry { get; }

        public ResolvedFilePath(string path, FilePathSource source, RedirectionEntry? entry = null)
        {
            Path = path;
            Source = source;
            Entry = entry;
        }
    }

    /// <summary>
    /// The inputs the redirection resolver needs — the C# stand-in for the
    /// extension's <c>serverSettings</c>. Bundles the three anchors plus the
    /// version-derived roots/macros so the resolver itself stays stateless wrt
    /// global config.
    /// </summary>
    public sealed class RedirectionContext
    {
        /// <summary>The project (.cwproj) directory — the anchor for relative paths and the local red.</summary>
        public string ProjectPath { get; set; } = string.Empty;

        /// <summary>The redirection file NAME (e.g. "Clarion110.red"); looked up project-local first, then bin.</summary>
        public string RedirectionFileName { get; set; } = string.Empty;

        /// <summary>The version's bin directory = the <c>%bin%</c> macro and the global-red location.</summary>
        public string PrimaryRedirectionPath { get; set; } = string.Empty;

        /// <summary>Active build configuration, e.g. "Debug" / "Release".</summary>
        public string Configuration { get; set; } = string.Empty;

        /// <summary>Version macros (lowercased keys), e.g. root, reddir.</summary>
        public IReadOnlyDictionary<string, string> Macros { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Tier-3 libsrc fallback dirs (from the version's libsrc list).</summary>
        public IReadOnlyList<string> LibSrcPaths { get; set; } = new List<string>();

        /// <summary>
        /// Builds a context from a detected compiler version + the project dir +
        /// active config. LibSrc is split on ';' into the Tier-3 fallback list.
        /// </summary>
        public static RedirectionContext FromVersion(
            ClarionCompilerVersion version, string projectPath, string configuration)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));

            return new RedirectionContext
            {
                ProjectPath = projectPath ?? string.Empty,
                Configuration = configuration ?? string.Empty,
                RedirectionFileName = version.RedirectionFile,
                PrimaryRedirectionPath = version.Path,
                Macros = version.Macros,
                LibSrcPaths = (version.LibSrc ?? string.Empty)
                    .Split(';')
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToList(),
            };
        }
    }
}
