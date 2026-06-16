using System;
using System.IO;
using System.Linq;
using Clarion.SourceResolution;
using Xunit;

namespace Clarion.SourceResolution.Tests
{
    public class ClarionInstallationDetectorTests
    {
        // A trimmed but structurally faithful ClarionProperties.xml: the
        // Clarion.Versions block with one real version, a Clarion.NET version
        // that must be skipped, and the RedirectionFile/Macros nesting.
        private const string SampleXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ClarionProperties>
  <Properties name=""Clarion.Versions"">
    <Properties name=""Clarion 11.1.13855"">
      <path value=""C:\Clarion\Clarion11.1\bin"" />
      <Properties name=""RedirectionFile"">
        <Name value=""Clarion110.red"" />
        <SupportsInclude value=""True"" />
        <Properties name=""Macros"">
          <root value=""C:\Clarion\Clarion11.1"" />
          <reddir value=""C:\Clarion\Clarion11.1\bin"" />
        </Properties>
      </Properties>
      <libsrc value=""C:\Clarion\Clarion11.1\Accessory\libsrc\win;C:\Clarion\Clarion11.1\libsrc\win"" />
    </Properties>
    <Properties name=""Clarion.NET 1.0"">
      <path value=""C:\ClarionNET\bin"" />
    </Properties>
  </Properties>
</ClarionProperties>";

        private static string WriteTempProperties(out string dir)
        {
            // Lay it out like a real install: <temp>\<ideVer>\ClarionProperties.xml
            dir = Path.Combine(Path.GetTempPath(), "ClaSrcRes_" + Guid.NewGuid().ToString("N"), "11.0");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "ClarionProperties.xml");
            File.WriteAllText(path, SampleXml);
            return path;
        }

        [Fact]
        public void ParseInstallation_ExtractsVersionPathRedirectionMacrosLibsrc()
        {
            var path = WriteTempProperties(out var dir);
            try
            {
                var install = ClarionInstallationDetector.ParseInstallationFromPropertiesPath(path);

                Assert.NotNull(install);
                Assert.Equal("11.0", install!.IdeVersion);          // containing folder name
                Assert.Single(install.CompilerVersions);            // Clarion.NET skipped

                var v = install.CompilerVersions[0];
                Assert.Equal("Clarion 11.1.13855", v.Name);
                Assert.Equal(@"C:\Clarion\Clarion11.1\bin", v.Path);
                Assert.Equal("Clarion110.red", v.RedirectionFile);
                Assert.Equal(
                    @"C:\Clarion\Clarion11.1\Accessory\libsrc\win;C:\Clarion\Clarion11.1\libsrc\win",
                    v.LibSrc);
            }
            finally
            {
                TryDeleteParent(dir);
            }
        }

        [Fact]
        public void ParseInstallation_MacrosAreCaseInsensitiveAndLowercased()
        {
            var path = WriteTempProperties(out var dir);
            try
            {
                var install = ClarionInstallationDetector.ParseInstallationFromPropertiesPath(path);
                var macros = install!.CompilerVersions[0].Macros;

                Assert.Equal(@"C:\Clarion\Clarion11.1", macros["root"]);
                Assert.Equal(@"C:\Clarion\Clarion11.1\bin", macros["reddir"]);
                // Lookups are case-insensitive (OrdinalIgnoreCase comparer).
                Assert.Equal(@"C:\Clarion\Clarion11.1", macros["ROOT"]);
            }
            finally
            {
                TryDeleteParent(dir);
            }
        }

        [Fact]
        public void ParseInstallation_MissingFile_ReturnsNull()
        {
            var missing = Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid().ToString("N"), "ClarionProperties.xml");
            Assert.Null(ClarionInstallationDetector.ParseInstallationFromPropertiesPath(missing));
        }

        private static void TryDeleteParent(string versionDir)
        {
            try
            {
                var parent = Directory.GetParent(versionDir)?.FullName;
                if (parent != null && Directory.Exists(parent))
                    Directory.Delete(parent, recursive: true);
            }
            catch { /* best-effort temp cleanup */ }
        }
    }
}
