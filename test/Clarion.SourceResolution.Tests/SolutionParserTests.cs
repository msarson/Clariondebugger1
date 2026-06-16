using System;
using System.IO;
using System.Linq;
using Clarion.SourceResolution;
using Xunit;

namespace Clarion.SourceResolution.Tests
{
    public class SolutionParserTests
    {
        // SCHOOL.sln shape: two .cwproj projects plus a "Solution Items" folder
        // that must be ignored.
        private const string SchoolSln =
@"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio 2012
# Clarion 2.1.0.2447
Project(""{12B76EC0-1D7B-4FA7-A7D0-C524288B48A1}"") = ""SCHOOL"", ""SCHOOL.cwproj"", ""{6675A038-9BE5-4F68-9161-74511D006162}""
EndProject
Project(""{2150E333-8FDC-42A3-9474-1A3956D46DE8}"") = ""Solution Items"", ""Solution Items"", ""{2150E333-8FDC-42A3-9474-1A3956D46DE8}""
	ProjectSection(SolutionItems) = postProject
		SCHOOL.APP = SCHOOL.APP
	EndProjectSection
EndProject
Project(""{12B76EC0-1D7B-4FA7-A7D0-C524288B48A1}"") = ""SchoolData"", ""SchoolData.cwproj"", ""{D4A2DBE3-3E20-4FF5-8E2C-B53B3C56E0F0}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Win32 = Debug|Win32
		Release|Win32 = Release|Win32
	EndGlobalSection
EndGlobal";

        private const string SchoolCwproj =
@"<Project DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <ProjectGuid>{6675A038-9BE5-4F68-9161-74511D006162}</ProjectGuid>
    <OutputType>Exe</OutputType>
    <AssemblyName>SCHOOL</AssemblyName>
    <OutputName>school</OutputName>
  </PropertyGroup>
  <PropertyGroup Condition="" '$(Configuration)' == 'Debug' "">
    <vid>full</vid>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""SCHOOL.clw"" />
    <Compile Include=""SCHOOL001.clw"" />
  </ItemGroup>
</Project>";

        [Fact]
        public void Parse_ExcludesSolutionFolder_ListsCwprojProjects()
        {
            var dir = NewTempDir();
            try
            {
                var sln = WriteSolutionLayout(dir);
                var solution = SolutionParser.Parse(sln);

                Assert.Equal(2, solution.Projects.Count); // Solution Items dropped
                Assert.Contains(solution.Projects, p => p.Name == "SCHOOL");
                Assert.Contains(solution.Projects, p => p.Name == "SchoolData");
                Assert.Equal(new[] { "Debug|Win32", "Release|Win32" }, solution.Configurations.ToArray());
                Assert.Equal(dir, solution.SolutionDir);
                Assert.Equal("SCHOOL", solution.Name);
            }
            finally { TryDeleteDir(dir); }
        }

        [Fact]
        public void Parse_MergesCwprojDetails_AndDerivesProjectDir()
        {
            var dir = NewTempDir();
            try
            {
                var sln = WriteSolutionLayout(dir);
                var solution = SolutionParser.Parse(sln);
                var school = solution.Projects.Single(p => p.Name == "SCHOOL");

                Assert.Equal("Exe", school.OutputType);
                Assert.True(school.IsExe);
                Assert.Equal("school", school.OutputName);
                Assert.Equal("SCHOOL.cwproj", school.ProjectFileName);
                Assert.Equal(dir, school.ProjectDir);
                Assert.Contains("SCHOOL001.clw", school.CompileItems);
            }
            finally { TryDeleteDir(dir); }
        }

        [Fact]
        public void FindProjectByGuid_IsBraceAndCaseTolerant()
        {
            var dir = NewTempDir();
            try
            {
                var sln = WriteSolutionLayout(dir);
                var solution = SolutionParser.Parse(sln);

                Assert.Equal("SCHOOL",
                    solution.FindProjectByGuid("6675a038-9be5-4f68-9161-74511d006162")?.Name);
                Assert.Equal("SCHOOL",
                    solution.FindProjectByGuid("{6675A038-9BE5-4F68-9161-74511D006162}")?.Name);
                Assert.Null(solution.FindProjectByGuid("{00000000-0000-0000-0000-000000000000}"));
            }
            finally { TryDeleteDir(dir); }
        }

        [Fact]
        public void Parse_MissingCwproj_StillListedFromSlnLine()
        {
            var dir = NewTempDir();
            try
            {
                // Write only the .sln (SchoolData.cwproj deliberately absent).
                var sln = Path.Combine(dir, "SCHOOL.sln");
                File.WriteAllText(sln, SchoolSln);
                File.WriteAllText(Path.Combine(dir, "SCHOOL.cwproj"), SchoolCwproj);

                var solution = SolutionParser.Parse(sln);
                var data = solution.Projects.Single(p => p.Name == "SchoolData");

                // No cwproj on disk, but the .sln still gave us name + guid + path.
                Assert.Equal("{D4A2DBE3-3E20-4FF5-8E2C-B53B3C56E0F0}", data.Guid);
                Assert.Equal("SchoolData.cwproj", data.ProjectFileName);
                Assert.Empty(data.OutputType);
            }
            finally { TryDeleteDir(dir); }
        }

        private static string WriteSolutionLayout(string dir)
        {
            var sln = Path.Combine(dir, "SCHOOL.sln");
            File.WriteAllText(sln, SchoolSln);
            File.WriteAllText(Path.Combine(dir, "SCHOOL.cwproj"), SchoolCwproj);
            // SchoolData.cwproj minimal so it parses as a project.
            File.WriteAllText(Path.Combine(dir, "SchoolData.cwproj"),
                @"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <ProjectGuid>{D4A2DBE3-3E20-4FF5-8E2C-B53B3C56E0F0}</ProjectGuid>
    <OutputType>Dll</OutputType>
    <AssemblyName>SchoolData</AssemblyName>
  </PropertyGroup>
</Project>");
            return sln;
        }

        private static string NewTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ClaSln_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
