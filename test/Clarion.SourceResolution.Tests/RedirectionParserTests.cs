using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clarion.SourceResolution;
using Xunit;

namespace Clarion.SourceResolution.Tests
{
    public class RedirectionParserTests
    {
        // ---- a disposable on-disk install + project layout -------------------------------

        private sealed class Layout : IDisposable
        {
            public string Root { get; }
            public string BinDir { get; }
            public string ProjectDir { get; }
            public string LibSrcDir { get; }

            public Layout()
            {
                Root = Path.Combine(Path.GetTempPath(), "ClaRed_" + Guid.NewGuid().ToString("N"));
                BinDir = Path.Combine(Root, "bin");
                ProjectDir = Path.Combine(Root, "proj");
                LibSrcDir = Path.Combine(Root, "libsrc");
                Directory.CreateDirectory(BinDir);
                Directory.CreateDirectory(ProjectDir);
                Directory.CreateDirectory(LibSrcDir);
            }

            public string Write(string relUnderRoot, string content)
            {
                var full = Path.Combine(Root, relUnderRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
                return full;
            }

            public RedirectionContext Context(string configuration) => new RedirectionContext
            {
                ProjectPath = ProjectDir,
                RedirectionFileName = "Clarion110.red",
                PrimaryRedirectionPath = BinDir,
                Configuration = configuration,
                Macros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["root"] = Root,
                },
                LibSrcPaths = new[] { LibSrcDir },
            };

            public void Dispose()
            {
                try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
            }
        }

        // Standard global red mirroring Clarion110.red's shape.
        private const string GlobalRed =
@"[Debug]
*.obj = obj\debug
*.FileList.xml = obj\debug
[Release]
*.obj = obj\release
*.FileList.xml = obj\release
[Common]
*.* = .; %ROOT%\libsrc\win
";

        [Fact]
        public void Parse_PrefersProjectLocalRed_OverBin()
        {
            using var lay = new Layout();
            lay.Write(@"bin\Clarion110.red", GlobalRed);
            // Local red overrides: a distinctive entry only the local file has.
            lay.Write(@"proj\Clarion110.red", "[Common]\n*.clw = localsrc\n");

            var entries = new RedirectionParser(lay.Context("Debug")).Parse();

            // The local red was chosen (its *.clw=localsrc entry is present) and
            // the bin red's entries are NOT (no include directive linked them).
            Assert.Contains(entries, e => e.Extension == "*.clw" && e.Paths.Contains("localsrc"));
            Assert.DoesNotContain(entries, e => e.Extension == "*.FileList.xml");
        }

        [Fact]
        public void Parse_IncludeChainsToGlobal_FromLocalRed()
        {
            using var lay = new Layout();
            lay.Write(@"bin\Clarion110.red", GlobalRed);
            // Local red includes the bin red by absolute path via %bin%.
            lay.Write(@"proj\Clarion110.red",
                "[Common]\n*.clw = localsrc\n{include %bin%\\Clarion110.red}\n");

            var entries = new RedirectionParser(lay.Context("Debug")).Parse();

            // Local entry comes first; included global entries follow.
            Assert.Contains(entries, e => e.Paths.Contains("localsrc"));
            Assert.Contains(entries, e => e.Extension == "*.FileList.xml");
        }

        [Fact]
        public void FindFile_BareName_ResolvesViaCommonCatchAll_InProjectDir()
        {
            using var lay = new Layout();
            lay.Write(@"bin\Clarion110.red", GlobalRed);
            lay.Write(@"proj\MyProc.clw", "  CODE\n");

            var parser = new RedirectionParser(lay.Context("Debug"));
            parser.Parse();

            var hit = parser.FindFile("MyProc.clw");
            Assert.NotNull(hit);
            // The "*.* = ." catch-all anchors on the PROJECT dir.
            Assert.Equal(Path.Combine(lay.ProjectDir, "MyProc.clw"), hit!.Path);
        }

        [Fact]
        public void FindFile_ResolvesMacroPath_IntoVersionLibsrc()
        {
            using var lay = new Layout();
            lay.Write(@"bin\Clarion110.red", GlobalRed);
            // %ROOT%\libsrc\win\ABWINDOW.INC — exercises macro expansion in a path.
            lay.Write(@"libsrc\win\ABWINDOW.INC", "! abc\n");

            var parser = new RedirectionParser(lay.Context("Debug"));
            parser.Parse();

            var hit = parser.FindFile("ABWINDOW.INC");
            Assert.NotNull(hit);
            Assert.Equal(
                Path.Combine(lay.Root, "libsrc", "win", "ABWINDOW.INC"),
                hit!.Path);
            Assert.Equal(FilePathSource.Redirected, hit.Source);
        }

        [Fact]
        public void GetSearchPaths_FileListXml_IsConfigSpecific()
        {
            using var lay = new Layout();
            lay.Write(@"bin\Clarion110.red", GlobalRed);
            var parser = new RedirectionParser(lay.Context("Release"));
            parser.Parse();

            var dirs = parser.GetSearchPaths(".FileList.xml");
            // The config-specific *.FileList.xml entry resolves FIRST (Release →
            // obj\release, anchored on the project dir). The Common "*.* = ."
            // catch-all also matches and contributes later dirs — realistic, and
            // harmless since the locator takes the first hit.
            Assert.Equal(Path.Combine(lay.ProjectDir, "obj", "release"), dirs[0]);
            Assert.Contains(Path.Combine(lay.ProjectDir, "obj", "release"), dirs);
        }

        [Fact]
        public void FindFile_SectionFilter_IgnoresInactiveConfiguration()
        {
            using var lay = new Layout();
            lay.Write(@"bin\Clarion110.red",
                "[Debug]\n*.clw = dbgonly\n[Release]\n*.clw = relonly\n");
            // File only exists under the Release-only dir.
            lay.Write(@"proj\relonly\Only.clw", "  CODE\n");

            // Active config Debug must NOT see the Release entry → miss.
            var dbg = new RedirectionParser(lay.Context("Debug"));
            dbg.Parse();
            Assert.Null(dbg.FindFile("Only.clw"));

            // Active config Release sees it.
            var rel = new RedirectionParser(lay.Context("Release"));
            rel.Parse();
            Assert.NotNull(rel.FindFile("Only.clw"));
        }

        [Fact]
        public void FindFile_Tier3_LibsrcFallback_WhenEntriesAndProjectMiss()
        {
            using var lay = new Layout();
            // A red with no useful entry for .inc (only an obj rule).
            lay.Write(@"bin\Clarion110.red", "[Common]\n*.obj = obj\n");
            lay.Write(@"libsrc\Helper.inc", "! helper\n");

            var parser = new RedirectionParser(lay.Context("Debug"));
            parser.Parse();

            var hit = parser.FindFile("Helper.inc");
            Assert.NotNull(hit);
            Assert.Equal(FilePathSource.LibSrc, hit!.Source);
            Assert.Equal(Path.Combine(lay.LibSrcDir, "Helper.inc"), hit.Path);
        }

        [Fact]
        public void FindFile_PathedName_JoinsProjectRoot_SkipsRed()
        {
            using var lay = new Layout();
            lay.Write(@"bin\Clarion110.red", GlobalRed);
            lay.Write(@"proj\sub\Nested.clw", "  CODE\n");

            var parser = new RedirectionParser(lay.Context("Debug"));
            parser.Parse();

            var hit = parser.FindFile(@"sub\Nested.clw");
            Assert.NotNull(hit);
            Assert.Equal(Path.Combine(lay.ProjectDir, "sub", "Nested.clw"), hit!.Path);
            Assert.Equal(FilePathSource.Project, hit.Source);
        }

        [Fact]
        public void FindFile_Unknown_ReturnsNull()
        {
            using var lay = new Layout();
            lay.Write(@"bin\Clarion110.red", GlobalRed);
            var parser = new RedirectionParser(lay.Context("Debug"));
            parser.Parse();
            Assert.Null(parser.FindFile("DoesNotExist.clw"));
        }

        [Fact]
        public void MatchesMask_PortedSemantics()
        {
            Assert.True(RedirectionParser.MatchesMask("*.*", "anything.xyz"));
            Assert.True(RedirectionParser.MatchesMask("*.clw", "FOO.CLW"));
            Assert.True(RedirectionParser.MatchesMask("*.FileList.xml", "P.cwproj.FileList.xml"));
            Assert.False(RedirectionParser.MatchesMask("*.clw", "foo.inc"));
        }

        [Fact]
        public void MatchesActiveConfiguration_CommonAlwaysMatches()
        {
            var common = new RedirectionEntry { Section = "Common" };
            var debug = new RedirectionEntry { Section = "Debug" };
            Assert.True(RedirectionParser.MatchesActiveConfiguration(common, "Release"));
            Assert.True(RedirectionParser.MatchesActiveConfiguration(debug, "debug")); // case-insensitive
            Assert.False(RedirectionParser.MatchesActiveConfiguration(debug, "Release"));
        }
    }
}
