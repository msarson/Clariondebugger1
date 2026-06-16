using System;
using System.IO;
using System.Xml.Linq;

namespace Clarion.SourceResolution
{
    /// <summary>
    /// Reads the Clarion IDE's per-solution preferences file to recover the
    /// active build configuration (and startup project) without the IDE running.
    ///
    /// Read-only port of the extension's <c>ClarionIdePreferences.ts</c>: the
    /// debugger consumes the IDE's choice but must never author it, so the
    /// write side is intentionally omitted. The file is located by hashing the
    /// solution path with <see cref="SlnHash"/> — see that type for why the
    /// hash must be the literal Framework algorithm.
    /// </summary>
    public static class IdePreferencesReader
    {
        /// <summary>
        /// Returns the full path to the IDE preferences XML for a solution.
        /// The preferences folder is derived from the ClarionProperties.xml
        /// path: e.g.
        ///   <c>...\Clarion\11.0\ClarionProperties.xml</c>
        ///   → <c>...\Clarion\11.0\preferences\MySln.sln.&lt;hash&gt;.xml</c>.
        /// </summary>
        /// <param name="slnPath">Full path to the .sln file.</param>
        /// <param name="propertiesFile">Full path to the version's ClarionProperties.xml.</param>
        public static string GetPreferencesFilePath(string slnPath, string propertiesFile)
        {
            if (slnPath == null) throw new ArgumentNullException(nameof(slnPath));
            if (propertiesFile == null) throw new ArgumentNullException(nameof(propertiesFile));

            var preferencesDir = Path.Combine(
                Path.GetDirectoryName(propertiesFile) ?? string.Empty, "preferences");
            // Keep the .sln extension so the file is "<name>.sln.<hash>.xml".
            var slnBasename = Path.GetFileName(slnPath);
            var hash = SlnHash.Compute(slnPath);
            return Path.Combine(preferencesDir, $"{slnBasename}.{hash}.xml");
        }

        /// <summary>
        /// Reads the IDE preferences for a solution, locating the file via
        /// <see cref="GetPreferencesFilePath"/>. Returns null if the file does
        /// not exist or cannot be parsed.
        /// </summary>
        public static IdePreferences? ReadIdePreferences(string slnPath, string propertiesFile)
        {
            var prefsPath = GetPreferencesFilePath(slnPath, propertiesFile);
            return ReadIdePreferencesFromFile(prefsPath);
        }

        /// <summary>
        /// Parses an IDE preferences XML at an explicit path. Returns null if
        /// the file is missing or unparseable. Exposed so the parser can be
        /// tested independently of the hash-derived path.
        /// </summary>
        public static IdePreferences? ReadIdePreferencesFromFile(string prefsPath)
        {
            if (string.IsNullOrEmpty(prefsPath) || !File.Exists(prefsPath))
                return null;

            try
            {
                var doc = XDocument.Load(prefsPath);
                var root = doc.Element("Properties");
                if (root == null)
                    return null;

                return new IdePreferences
                {
                    StartupProjectGuid = PropertyValue(root, "StartupProject"),
                    ActiveConfiguration = PropertyValue(root, "ActiveConfiguration"),
                    ActivePlatform = PropertyValue(root, "ActivePlatform"),
                };
            }
            catch
            {
                // Corrupt / partially-written file — treat as "no prefs" so the
                // caller falls through to .sln.cache / prompt.
                return null;
            }
        }

        // Each setting is an element like <ActiveConfiguration value="Release" />.
        // An empty value (e.g. StartupProject value="") is normalised to null.
        private static string? PropertyValue(XElement root, string name)
        {
            var value = (string?)root.Element(name)?.Attribute("value");
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
