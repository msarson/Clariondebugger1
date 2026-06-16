using System;
using System.IO;
using Clarion.SourceResolution;
using Xunit;

namespace Clarion.SourceResolution.Tests
{
    public class IdePreferencesReaderTests
    {
        // Shape taken verbatim from a real preferences file (DarkMode.sln.*.xml),
        // including the empty StartupProject the IDE writes.
        private const string SampleXml =
            "﻿<Properties>\r\n" +
            "  <StartupProject value=\"\" />\r\n" +
            "  <ActiveConfiguration value=\"Release\" />\r\n" +
            "  <ActivePlatform value=\"Win32\" />\r\n" +
            "  <Array name=\"OpenFiles\" />\r\n" +
            "  <ActiveVersion value=\"Current\" />\r\n" +
            "</Properties>";

        [Fact]
        public void GetPreferencesFilePath_UsesHashAndKeepsSlnExtension()
        {
            // Known SlnHash vector → file name must be "ap1.sln.ecfee7f0.xml"
            // under the <propertiesDir>\preferences folder.
            var prefsPath = IdePreferencesReader.GetPreferencesFilePath(
                @"c:\development\ibsworking\ap1.sln",
                @"X:\AppData\SoftVelocity\Clarion\11.0\ClarionProperties.xml");

            Assert.Equal(
                @"X:\AppData\SoftVelocity\Clarion\11.0\preferences\ap1.sln.ecfee7f0.xml",
                prefsPath);
        }

        [Fact]
        public void ReadFromFile_ParsesConfigAndPlatform_EmptyStartupBecomesNull()
        {
            var path = WriteTemp(SampleXml);
            try
            {
                var prefs = IdePreferencesReader.ReadIdePreferencesFromFile(path);

                Assert.NotNull(prefs);
                Assert.Equal("Release", prefs!.ActiveConfiguration);
                Assert.Equal("Win32", prefs.ActivePlatform);
                Assert.Null(prefs.StartupProjectGuid); // value="" normalises to null
            }
            finally { TryDelete(path); }
        }

        [Fact]
        public void ReadFromFile_MissingFile_ReturnsNull()
        {
            var missing = Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid().ToString("N") + ".xml");
            Assert.Null(IdePreferencesReader.ReadIdePreferencesFromFile(missing));
        }

        [Fact]
        public void ReadIdePreferences_LocatesFileViaHashedPath()
        {
            // Build a fake install layout, drop the prefs file at the exact
            // hashed location, and prove the hash-driven lookup finds it.
            var propertiesDir = Path.Combine(Path.GetTempPath(), "ClaPrefs_" + Guid.NewGuid().ToString("N"), "11.0");
            Directory.CreateDirectory(propertiesDir);
            var propertiesFile = Path.Combine(propertiesDir, "ClarionProperties.xml");

            var slnPath = @"c:\development\ibsworking\ap1.sln";
            var prefsPath = IdePreferencesReader.GetPreferencesFilePath(slnPath, propertiesFile);
            Directory.CreateDirectory(Path.GetDirectoryName(prefsPath)!);
            File.WriteAllText(prefsPath, SampleXml);

            try
            {
                var prefs = IdePreferencesReader.ReadIdePreferences(slnPath, propertiesFile);
                Assert.NotNull(prefs);
                Assert.Equal("Release", prefs!.ActiveConfiguration);
            }
            finally
            {
                var parent = Directory.GetParent(propertiesDir)?.FullName;
                if (parent != null) TryDeleteDir(parent);
            }
        }

        private static string WriteTemp(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), "ClaPrefs_" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(path, content);
            return path;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
