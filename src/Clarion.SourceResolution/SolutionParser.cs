using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Clarion.SourceResolution
{
    /// <summary>
    /// Parses a Visual-Studio-format Clarion <c>.sln</c> into a
    /// <see cref="ClarionSolution"/>: the <c>.cwproj</c> entries (solution
    /// folders such as "Solution Items" are skipped) and the solution
    /// configurations. Each referenced .cwproj is parsed via
    /// <see cref="CwprojParser"/>; a referenced-but-missing project is still
    /// listed using the data from the .sln line.
    /// </summary>
    public static class SolutionParser
    {
        // Project("{type-guid}") = "Name", "Relative\Path.cwproj", "{proj-guid}"
        private static readonly Regex ProjectLine = new Regex(
            "Project\\(\"\\{[^}]+\\}\"\\)\\s*=\\s*\"([^\"]+)\",\\s*\"([^\"]+)\",\\s*\"(\\{[^}]+\\})\"",
            RegexOptions.Compiled);

        public static ClarionSolution Parse(string slnPath)
        {
            var solution = new ClarionSolution { SolutionFile = SafeFullPath(slnPath) };
            if (!File.Exists(solution.SolutionFile))
                return solution;

            var dir = Path.GetDirectoryName(solution.SolutionFile) ?? string.Empty;
            var text = File.ReadAllText(solution.SolutionFile);

            var projects = new List<ClarionProject>();
            foreach (Match m in ProjectLine.Matches(text))
            {
                var name = m.Groups[1].Value;
                var rel = m.Groups[2].Value;
                var guid = m.Groups[3].Value;

                // Only real .cwproj entries — this drops solution folders, whose
                // "path" is just the folder name (e.g. "Solution Items").
                if (!rel.EndsWith(".cwproj", StringComparison.OrdinalIgnoreCase))
                    continue;

                var projFull = SafeFullPath(Path.Combine(dir, rel));
                var project = File.Exists(projFull)
                    ? CwprojParser.Parse(projFull)
                    : new ClarionProject { ProjectFile = projFull };

                // The .sln name is authoritative; keep the cwproj GUID if it had
                // one, else fall back to the .sln's.
                project.Name = name;
                if (string.IsNullOrEmpty(project.Guid))
                    project.Guid = guid;

                projects.Add(project);
            }
            solution.Projects = projects;
            solution.Configurations = ParseConfigurations(text);
            return solution;
        }

        private static List<string> ParseConfigurations(string text)
        {
            var configs = new List<string>();
            var inSection = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("GlobalSection(SolutionConfigurationPlatforms)", StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    continue;
                }
                if (!inSection)
                    continue;
                if (line.StartsWith("EndGlobalSection", StringComparison.OrdinalIgnoreCase))
                    break;

                // "Debug|Win32 = Debug|Win32" — take the left side.
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var cfg = line.Substring(0, eq).Trim();
                if (cfg.Length > 0 && !configs.Contains(cfg))
                    configs.Add(cfg);
            }
            return configs;
        }

        private static string SafeFullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }
    }
}
