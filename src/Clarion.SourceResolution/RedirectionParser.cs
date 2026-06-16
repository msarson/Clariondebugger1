using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Clarion.SourceResolution
{
    /// <summary>
    /// Parses Clarion <c>.red</c> redirection files and resolves file names
    /// against them. Faithful port of the extension's
    /// <c>redirectionFileParserServer.ts</c> (the canonical reference) — same
    /// section model, <c>{include}</c> chaining, macro rules, build-config
    /// filtering, and 3-tier <see cref="FindFile"/> resolution.
    ///
    /// Anchoring (the bit that's easy to get wrong):
    ///  - The local red is found by NAME in the project dir, overriding the bin
    ///    copy; the chosen red is parsed recursively following <c>{include}</c>.
    ///  - <c>{include}</c> paths anchor on the dir of the red being parsed.
    ///  - Entry search paths that are relative anchor on the PROJECT dir (not
    ///    the red's own dir) — this is what makes <c>obj\debug</c> and the
    ///    <c>*.* = .; %ROOT%\libsrc\win</c> catch-all resolve correctly.
    ///
    /// Note: the parse/include caches in the TS version are intentionally omitted
    /// — the debugger parses a project's reds once per session and .red files are
    /// tiny, so the static-state complexity isn't worth it here.
    /// </summary>
    public sealed class RedirectionParser
    {
        private static readonly Regex SectionLine = new Regex(@"^\[([^\]]+)\]$", RegexOptions.Compiled);
        private static readonly Regex IncludeLine = new Regex(@"\{include\s+([^}]+)\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MacroToken = new Regex("%([^%]+)%", RegexOptions.Compiled);

        private readonly RedirectionContext _ctx;
        private List<RedirectionEntry> _entries = new List<RedirectionEntry>();

        public RedirectionParser(RedirectionContext context)
        {
            _ctx = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>The parsed entries, in file/include order. Empty until <see cref="Parse"/> runs.</summary>
        public IReadOnlyList<RedirectionEntry> Entries => _entries;

        /// <summary>
        /// Locates the effective red (project-local by name, else the bin copy)
        /// and parses it recursively. Returns the entries; also stored on
        /// <see cref="Entries"/>. No-op (empty) if no red name is configured or
        /// neither red exists.
        /// </summary>
        public IReadOnlyList<RedirectionEntry> Parse()
        {
            _entries = new List<RedirectionEntry>();
            if (string.IsNullOrEmpty(_ctx.RedirectionFileName))
                return _entries;

            var projectRed = Path.Combine(_ctx.ProjectPath, _ctx.RedirectionFileName);
            var globalRed = Path.Combine(_ctx.PrimaryRedirectionPath, _ctx.RedirectionFileName);

            string? redToParse =
                File.Exists(projectRed) ? projectRed :
                File.Exists(globalRed) ? globalRed :
                null;

            if (redToParse == null)
                return _entries;

            ParseRecursive(redToParse, _entries);
            return _entries;
        }

        private void ParseRecursive(string redFile, List<RedirectionEntry> entries)
        {
            if (!File.Exists(redFile))
                return;

            string[] lines;
            try { lines = File.ReadAllLines(redFile); }
            catch { return; }

            var redDir = Path.GetDirectoryName(redFile) ?? string.Empty;
            string? currentSection = null;

            foreach (var raw in lines)
            {
                var trimmed = raw.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
                    continue;

                var sectionMatch = SectionLine.Match(trimmed);
                if (sectionMatch.Success)
                {
                    currentSection = sectionMatch.Groups[1].Value.Trim();
                    continue;
                }

                if (currentSection == null)
                    currentSection = "Common";

                if (trimmed.StartsWith("{include", StringComparison.OrdinalIgnoreCase))
                {
                    var inc = IncludeLine.Match(trimmed);
                    if (inc.Success)
                    {
                        var includePath = ResolveMacro(inc.Groups[1].Value);
                        // {include} anchors on the dir of the red being parsed.
                        includePath = Path.IsPathRooted(includePath)
                            ? includePath
                            : Resolve(redDir, includePath);
                        ParseRecursive(includePath, entries);
                    }
                    continue;
                }

                var eq = trimmed.IndexOf('=');
                if (eq > 0)
                {
                    var mask = trimmed.Substring(0, eq).Trim();
                    var rawPaths = trimmed.Substring(eq + 1).Trim();
                    var paths = rawPaths.Split(';')
                        .Select(p => ResolveMacro(p.Trim()))
                        .ToList();

                    entries.Add(new RedirectionEntry
                    {
                        RedFile = redFile,
                        Section = currentSection,
                        Extension = mask,
                        Paths = paths,
                    });
                }
            }
        }

        /// <summary>
        /// Resolves a file name against the parsed entries, in strict
        /// compiler-truth order:
        ///  1. Absolute → exists check.
        ///  2. Pathed (contains a separator) → project-root join only, RED skipped.
        ///  3. Bare → Tier 1 RED entries (config-filtered) → Tier 2 project root
        ///     → Tier 3 libsrc. First existing hit wins. Null if nothing exists.
        /// </summary>
        public ResolvedFilePath? FindFile(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return null;

            // 1. Absolute.
            if (Path.IsPathRooted(filename) && !ContainsOnlyRootRelative(filename))
            {
                return File.Exists(filename)
                    ? new ResolvedFilePath(filename, FilePathSource.Project)
                    : null;
            }

            // 2. Pathed — direct project-root join, skip RED.
            if (filename.IndexOf('/') >= 0 || filename.IndexOf('\\') >= 0)
            {
                if (string.IsNullOrEmpty(_ctx.ProjectPath))
                    return null;
                var candidate = Normalize(Path.Combine(_ctx.ProjectPath, filename));
                return File.Exists(candidate)
                    ? new ResolvedFilePath(candidate, FilePathSource.Project)
                    : null;
            }

            // 3. Bare filename.
            var checkd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _entries)
            {
                if (!MatchesActiveConfiguration(entry, _ctx.Configuration))
                    continue;
                if (!MatchesMask(entry.Extension, filename))
                    continue;

                foreach (var dir in entry.Paths)
                {
                    var resolvedDir = ResolveEntryDir(dir, entry);
                    var candidate = Normalize(Path.Combine(resolvedDir, filename));
                    if (!checkd.Add(candidate))
                        continue;
                    if (File.Exists(candidate))
                        return new ResolvedFilePath(candidate, FilePathSource.Redirected, entry);
                }
            }

            // Tier 2: explicit project-root probe.
            if (!string.IsNullOrEmpty(_ctx.ProjectPath))
            {
                var projectCandidate = Normalize(Path.Combine(_ctx.ProjectPath, filename));
                if (checkd.Add(projectCandidate) && File.Exists(projectCandidate))
                    return new ResolvedFilePath(projectCandidate, FilePathSource.Project);
            }

            // Tier 3: libsrc fallback.
            foreach (var libDir in _ctx.LibSrcPaths)
            {
                var candidate = Normalize(Path.Combine(libDir, filename));
                if (checkd.Add(candidate) && File.Exists(candidate))
                    return new ResolvedFilePath(candidate, FilePathSource.LibSrc);
            }

            return null;
        }

        /// <summary>
        /// Returns the ordered, de-duplicated search directories for a given
        /// file extension (e.g. ".FileList.xml", ".clw") under the active
        /// configuration. Mirrors the extension's <c>getSearchPaths</c>; useful
        /// for locating the obj output (e.g. where <c>*.FileList.xml</c> lands).
        /// </summary>
        public IReadOnlyList<string> GetSearchPaths(string extension)
        {
            var probe = "probe" + extension;
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _entries)
            {
                if (!MatchesActiveConfiguration(entry, _ctx.Configuration))
                    continue;
                if (!MatchesMask(entry.Extension, probe))
                    continue;
                foreach (var dir in entry.Paths)
                {
                    var resolvedDir = ResolveEntryDir(dir, entry);
                    if (seen.Add(resolvedDir))
                        result.Add(resolvedDir);
                }
            }
            return result;
        }

        // ---- helpers ----------------------------------------------------------------------

        private string ResolveEntryDir(string dir, RedirectionEntry entry)
        {
            // Relative entry paths anchor on the project dir (fall back to the
            // red's own dir only when no project dir was supplied).
            if (dir == "." || dir == ".." || !Path.IsPathRooted(dir))
            {
                var baseDir = !string.IsNullOrEmpty(_ctx.ProjectPath)
                    ? _ctx.ProjectPath
                    : (Path.GetDirectoryName(entry.RedFile) ?? string.Empty);
                return Resolve(baseDir, dir);
            }
            return dir;
        }

        private string ResolveMacro(string input)
        {
            if (input.IndexOf('%') < 0)
                return NormalizeLight(input);

            var resolved = MacroToken.Replace(input, m =>
            {
                var name = m.Groups[1].Value.ToLowerInvariant();
                if (_ctx.Macros.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
                    return value;
                if (name == "bin")
                    return _ctx.PrimaryRedirectionPath;
                if (name == "redname")
                    return Path.GetFileName(_ctx.RedirectionFileName);
                return m.Value; // leave unknown macros intact
            });

            return NormalizeLight(resolved);
        }

        /// <summary>Active-config filter: Common entries plus the active configuration's, case-insensitive.</summary>
        public static bool MatchesActiveConfiguration(RedirectionEntry entry, string configuration)
        {
            var section = entry.Section.ToLowerInvariant();
            return section == "common" || section == (configuration ?? string.Empty).ToLowerInvariant();
        }

        /// <summary>File-mask match, ported verbatim: "*.*" matches all; "*.ext" is a suffix test.</summary>
        public static bool MatchesMask(string mask, string filename)
        {
            if (mask == "*.*")
                return true;
            var maskLower = mask.ToLowerInvariant();
            var fileLower = filename.ToLowerInvariant();
            if (maskLower.StartsWith("*.", StringComparison.Ordinal))
            {
                var ext = maskLower.Substring(1); // ".clw"
                return fileLower.EndsWith(ext, StringComparison.Ordinal);
            }
            return fileLower.EndsWith(maskLower.Replace("*", string.Empty), StringComparison.Ordinal);
        }

        // path.resolve(base, rel) ~= GetFullPath(Combine(base, rel)) — works on netstandard2.0
        // (no 2-arg GetFullPath there). Collapses "." / ".." against the base.
        private static string Resolve(string baseDir, string rel)
        {
            try { return Path.GetFullPath(Path.Combine(baseDir, rel)); }
            catch { return Path.Combine(baseDir, rel); }
        }

        // Normalize an already-rooted candidate path; leave relative input mostly
        // as-is (only rooted candidates reach File.Exists in the resolution chain).
        private static string Normalize(string p)
        {
            if (Path.IsPathRooted(p))
            {
                try { return Path.GetFullPath(p); }
                catch { return p; }
            }
            return p;
        }

        // Light normalize for macro output (may be relative like "obj\debug"):
        // just unify separators; anchoring/collapsing happens later via Resolve.
        private static string NormalizeLight(string p) =>
            p.Replace('/', '\\');

        // A rooted-but-not-fully-qualified path like "\foo" should fall through
        // to the pathed branch rather than be treated as absolute.
        private static bool ContainsOnlyRootRelative(string filename) =>
            (filename.StartsWith("\\", StringComparison.Ordinal) ||
             filename.StartsWith("/", StringComparison.Ordinal)) &&
            !(filename.Length >= 2 && filename[1] == ':');
    }
}
