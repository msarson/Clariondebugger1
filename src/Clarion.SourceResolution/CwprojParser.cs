using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Clarion.SourceResolution
{
    /// <summary>
    /// Parses a Clarion <c>.cwproj</c> (an MSBuild project) into a
    /// <see cref="ClarionProject"/>. Element matching is namespace-agnostic
    /// (by local name) so the MSBuild xmlns on the root doesn't matter, and
    /// only the unconditioned property values are taken (the first occurrence),
    /// which is where OutputType / AssemblyName / OutputName live.
    /// </summary>
    public static class CwprojParser
    {
        /// <summary>
        /// Parses the .cwproj at <paramref name="cwprojPath"/>. If the file is
        /// missing or unparseable, returns a project carrying just the path
        /// (so a solution can still list it).
        /// </summary>
        public static ClarionProject Parse(string cwprojPath)
        {
            var full = SafeFullPath(cwprojPath);
            var project = new ClarionProject
            {
                ProjectFile = full,
                Name = Path.GetFileNameWithoutExtension(full),
            };

            if (!File.Exists(full))
                return project;

            XDocument doc;
            try { doc = XDocument.Load(full); }
            catch { return project; }

            project.Guid = FirstValue(doc, "ProjectGuid");
            project.OutputType = FirstValue(doc, "OutputType");
            project.AssemblyName = FirstValue(doc, "AssemblyName");
            project.OutputName = FirstValue(doc, "OutputName");
            project.CompileItems = doc.Descendants()
                .Where(e => e.Name.LocalName == "Compile")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .ToList();

            if (!string.IsNullOrEmpty(project.AssemblyName))
                project.Name = project.AssemblyName;

            return project;
        }

        private static string FirstValue(XDocument doc, string localName)
        {
            var el = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
            return el?.Value.Trim() ?? string.Empty;
        }

        private static string SafeFullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }
    }
}
