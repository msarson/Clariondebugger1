using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clarion.SourceResolution
{
    /// <summary>
    /// A parsed Clarion <c>.sln</c>: the set of <c>.cwproj</c> projects it
    /// contains plus the solution-level configurations. Supplies the third
    /// resolution anchor (which projects exist, where, and their GUIDs) so the
    /// FileList locator knows each project dir and the IDE prefs'
    /// <c>StartupProject</c> GUID can be mapped to the project that builds the EXE.
    /// </summary>
    public sealed class ClarionSolution
    {
        /// <summary>Full path to the .sln file.</summary>
        public string SolutionFile { get; set; } = string.Empty;

        /// <summary>Directory containing the .sln (the redirection anchor dir).</summary>
        public string SolutionDir =>
            Path.GetDirectoryName(SolutionFile) ?? string.Empty;

        /// <summary>Solution name (file name without extension).</summary>
        public string Name => Path.GetFileNameWithoutExtension(SolutionFile);

        /// <summary>The .cwproj projects (solution folders excluded).</summary>
        public IReadOnlyList<ClarionProject> Projects { get; set; } = new List<ClarionProject>();

        /// <summary>
        /// Solution configurations as "Config|Platform" (e.g. "Debug|Win32"),
        /// from the SolutionConfigurationPlatforms section.
        /// </summary>
        public IReadOnlyList<string> Configurations { get; set; } = new List<string>();

        /// <summary>Finds a project by GUID (case-insensitive, brace-tolerant), or null.</summary>
        public ClarionProject? FindProjectByGuid(string? guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;
            var needle = NormalizeGuid(guid!);
            return Projects.FirstOrDefault(p => NormalizeGuid(p.Guid) == needle);
        }

        private static string NormalizeGuid(string guid) =>
            guid.Trim().Trim('{', '}').ToUpperInvariant();
    }

    /// <summary>
    /// A parsed Clarion <c>.cwproj</c>: GUID, output, and compile items. Output
    /// fields decide which project produces the debuggee EXE; the project dir +
    /// file name feed <see cref="FileListLocator"/>.
    /// </summary>
    public sealed class ClarionProject
    {
        /// <summary>Project name (from the .sln entry, else the file stem).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Full path to the .cwproj file.</summary>
        public string ProjectFile { get; set; } = string.Empty;

        /// <summary>Project GUID in "{....}" form.</summary>
        public string Guid { get; set; } = string.Empty;

        /// <summary>Output type: "Exe", "Dll", or "Lib".</summary>
        public string OutputType { get; set; } = string.Empty;

        /// <summary>The &lt;AssemblyName&gt; value.</summary>
        public string AssemblyName { get; set; } = string.Empty;

        /// <summary>The &lt;OutputName&gt; value (the produced binary's base name).</summary>
        public string OutputName { get; set; } = string.Empty;

        /// <summary>The &lt;Compile Include="..."&gt; source items, as written.</summary>
        public IReadOnlyList<string> CompileItems { get; set; } = new List<string>();

        /// <summary>Directory containing the .cwproj.</summary>
        public string ProjectDir =>
            Path.GetDirectoryName(ProjectFile) ?? string.Empty;

        /// <summary>The .cwproj file name, e.g. "SCHOOL.cwproj".</summary>
        public string ProjectFileName =>
            Path.GetFileName(ProjectFile);

        /// <summary>True when this project builds an executable.</summary>
        public bool IsExe =>
            string.Equals(OutputType, "Exe", StringComparison.OrdinalIgnoreCase);
    }
}
