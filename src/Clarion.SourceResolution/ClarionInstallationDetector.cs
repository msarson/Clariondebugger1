using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Clarion.SourceResolution
{
    /// <summary>
    /// Discovers installed Clarion IDEs and their compiler versions by parsing
    /// <c>%APPDATA%\SoftVelocity\Clarion\&lt;ideVersion&gt;\ClarionProperties.xml</c>.
    ///
    /// Faithful C# port of the extension's <c>ClarionInstallationDetector.ts</c>
    /// (single source of truth for ClarionProperties.xml parsing). This is slice
    /// one of the source-resolution package: nothing else can resolve source
    /// until a version is chosen, and the version supplies the redirection
    /// anchors (bin path, .red name, macros, libsrc).
    /// </summary>
    public static class ClarionInstallationDetector
    {
        // Only the AppData scan is cached — it reflects installed IDEs, which
        // don't change within a session. Reads of an explicit properties path
        // (ParseInstallationFromPropertiesPath) are NOT cached: an edited /
        // relocated properties file is always read fresh.
        private static IReadOnlyList<ClarionInstallation>? _cachedInstallations;
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Detects all Clarion IDE installations by scanning the standard
        /// AppData directory (newest IDE version first). Result is cached for
        /// the process; call <see cref="ClearCache"/> to force a re-scan.
        /// </summary>
        public static IReadOnlyList<ClarionInstallation> DetectInstallations()
        {
            lock (_cacheLock)
            {
                if (_cachedInstallations != null)
                    return _cachedInstallations;

                var installations = new List<ClarionInstallation>();

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(appData))
                    return installations;

                var clarionBase = Path.Combine(appData, "SoftVelocity", "Clarion");
                if (!Directory.Exists(clarionBase))
                    return installations;

                // Version dirs (e.g. 10.0, 11.0, 11.1, 12.0), newest first.
                var versionDirs = Directory.GetDirectories(clarionBase)
                    .Select(d => Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar)))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .OrderByDescending(ParseVersion)
                    .ToList();

                foreach (var version in versionDirs)
                {
                    var propertiesPath = Path.Combine(clarionBase, version!, "ClarionProperties.xml");
                    if (!File.Exists(propertiesPath))
                        continue;

                    try
                    {
                        var compilers = ParseCompilerVersions(propertiesPath);
                        if (compilers.Count > 0)
                        {
                            installations.Add(new ClarionInstallation
                            {
                                IdeVersion = version!,
                                PropertiesPath = propertiesPath,
                                CompilerVersions = compilers
                            });
                        }
                    }
                    catch
                    {
                        // Malformed properties file for this version — skip it,
                        // matching the TS behaviour (log + continue).
                    }
                }

                // Promote ideVersion if a higher sibling folder exists with no
                // ClarionProperties.xml. e.g. folder "11.1" exists but is empty
                // → the "11.0" installation is surfaced as "11.1".
                var emptyFolders = versionDirs.Where(v =>
                    !File.Exists(Path.Combine(clarionBase, v!, "ClarionProperties.xml")));

                foreach (var empty in emptyFolders)
                {
                    var emptyVal = ParseVersion(empty);
                    var emptyMajor = Math.Floor(emptyVal);
                    var candidate = installations.FirstOrDefault(inst =>
                    {
                        var instVal = ParseVersion(inst.IdeVersion);
                        return Math.Floor(instVal) == emptyMajor && instVal < emptyVal;
                    });
                    if (candidate != null)
                        candidate.IdeVersion = empty!;
                }

                _cachedInstallations = installations;
                return installations;
            }
        }

        /// <summary>Clears the cached installations (test hook / post-install refresh).</summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
                _cachedInstallations = null;
        }

        /// <summary>Gets the most recent Clarion installation (highest version), or null.</summary>
        public static ClarionInstallation? GetMostRecentInstallation()
        {
            var all = DetectInstallations();
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>Finds a specific installation by its AppData IDE version folder name.</summary>
        public static ClarionInstallation? GetInstallationByVersion(string ideVersion)
        {
            return DetectInstallations().FirstOrDefault(i =>
                string.Equals(i.IdeVersion, ideVersion, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Parses a ClarionProperties.xml at an arbitrary path (e.g. the user
        /// browsed for one, or a /ConfigDir setup). Not cached — always reads
        /// fresh. Returns null if no compiler versions are found.
        /// </summary>
        public static ClarionInstallation? ParseInstallationFromPropertiesPath(string propertiesPath)
        {
            try
            {
                var compilers = ParseCompilerVersions(propertiesPath);
                if (compilers.Count == 0)
                    return null;

                // Best-effort ideVersion label = the containing folder's name.
                var folderName = Path.GetFileName(
                    Path.GetDirectoryName(propertiesPath)?.TrimEnd(Path.DirectorySeparatorChar) ?? string.Empty);

                return new ClarionInstallation
                {
                    IdeVersion = folderName,
                    PropertiesPath = propertiesPath,
                    CompilerVersions = compilers
                };
            }
            catch
            {
                return null;
            }
        }

        // ---- ClarionProperties.xml parsing -------------------------------------------------

        private static List<ClarionCompilerVersion> ParseCompilerVersions(string propertiesPath)
        {
            var result = new List<ClarionCompilerVersion>();
            var doc = XDocument.Load(propertiesPath);

            var root = doc.Element("ClarionProperties");
            if (root == null)
                return result;

            // Find <Properties name="Clarion.Versions">.
            var versionsBlock = root.Elements("Properties")
                .FirstOrDefault(p => (string?)p.Attribute("name") == "Clarion.Versions");
            if (versionsBlock == null)
                return result;

            foreach (var versionProp in versionsBlock.Elements("Properties"))
            {
                var name = (string?)versionProp.Attribute("name") ?? string.Empty;

                // Skip Clarion.NET versions (matches TS).
                if (name.IndexOf("Clarion.NET", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var redirectionProp = versionProp.Elements("Properties")
                    .FirstOrDefault(p => (string?)p.Attribute("name") == "RedirectionFile");

                result.Add(new ClarionCompilerVersion
                {
                    Name = name,
                    Path = AttrValue(versionProp.Element("path")),
                    LibSrc = AttrValue(versionProp.Element("libsrc")),
                    RedirectionFile = ExtractRedirectionFile(redirectionProp),
                    Macros = ExtractMacros(redirectionProp)
                });
            }

            return result;
        }

        private static string ExtractRedirectionFile(XElement? redirectionProp)
        {
            // <Properties name="RedirectionFile"><Name value="Clarion110.red" />...
            return AttrValue(redirectionProp?.Element("Name"));
        }

        private static IReadOnlyDictionary<string, string> ExtractMacros(XElement? redirectionProp)
        {
            var macros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (redirectionProp == null)
                return macros;

            // <Properties name="Macros"><root value=".." /><reddir value=".." />...
            var macrosProp = redirectionProp.Elements("Properties")
                .FirstOrDefault(p => (string?)p.Attribute("name") == "Macros");
            if (macrosProp == null)
                return macros;

            foreach (var macro in macrosProp.Elements())
            {
                var value = (string?)macro.Attribute("value");
                if (value != null)
                    macros[macro.Name.LocalName.ToLowerInvariant()] = value;
            }

            return macros;
        }

        private static string AttrValue(XElement? element) =>
            (string?)element?.Attribute("value") ?? string.Empty;

        private static double ParseVersion(string? folderName) =>
            double.TryParse(folderName, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : double.NegativeInfinity;
    }
}
