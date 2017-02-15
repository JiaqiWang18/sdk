﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;

using Microsoft.DotNet.Cli.Utils;
using Microsoft.NET.TestFramework;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.Commands;
using Microsoft.NET.TestFramework.ProjectConstruction;

using FluentAssertions;
using Xunit;

using static Microsoft.NET.TestFramework.Commands.MSBuildTest;

namespace Microsoft.NET.Build.Tests
{
    public class GivenThatWeWantToBuildADesktopExe : SdkTest
    {
        [Theory]

        // If we don't set platformTarget and don't use native dependency, we get working AnyCPU app.
        [InlineData("defaults", null, false, "Native code was not used (MSIL)")]

        // If we don't set platformTarget and do use native dependency, we get working x86 app.
        [InlineData("defaultsNative", null, true, "Native code was used (X86)")]

        // If we set x86 and don't use native dependency, we get working x86 app.
        [InlineData("x86", "x86", false, "Native code was not used (X86)")]

        // If we set x86 and do use native dependency, we get working x86 app.
        [InlineData("x86Native", "x86", true, "Native code was used (X86)")]

        // If we set x64 and don't use native dependency, we get working x64 app.
        [InlineData("x64", "x64", false, "Native code was not used (Amd64)")]

        // If we set x64 and do use native dependency, we get working x64 app.
        [InlineData("x64Native", "x64", true, "Native code was used (Amd64)")]

        // If we set AnyCPU and don't use native dependency, we get working  AnyCPU app.
        [InlineData("AnyCPU", "AnyCPU", false, "Native code was not used (MSIL)")]

        // If we set AnyCPU and do use native dependency, we get any CPU app that can't find its native dependency.
        // Tests current behavior, but ideally we'd also raise a build diagnostic in this case: https://github.com/dotnet/sdk/issues/843
        [InlineData("AnyCPUNative", "AnyCPU", true, "Native code failed (MSIL)")]
        public void It_handles_native_depdencies_and_platform_target(
             string identifier,
             string platformTarget,
             bool useNativeCode,
             string expectedProgramOutput)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var testAsset = _testAssetsManager
               .CopyTestAsset("DesktopMinusRid", identifier: Path.DirectorySeparatorChar + identifier)
               .WithSource()
               .WithProjectChanges(project =>
               {
                   var ns = project.Root.Name.Namespace;
                   var propertyGroup = project.Root.Elements(ns + "PropertyGroup").First();
                   propertyGroup.Add(new XElement(ns + "UseNativeCode", useNativeCode));

                   if (platformTarget != null)
                   {
                       propertyGroup.Add(new XElement(ns + "PlatformTarget", platformTarget));
                   }
               })
              .Restore();

            var buildCommand = new BuildCommand(Stage0MSBuild, testAsset.TestRoot);
            buildCommand
                .Execute()
                .Should()
                .Pass();

            var exe = Path.Combine(buildCommand.GetOutputDirectory("net46").FullName, "DesktopMinusRid.exe");
            var runCommand = Command.Create(exe, Array.Empty<string>());
            runCommand
                .CaptureStdOut()
                .Execute()
                .Should()
                .Pass()
                .And
                .HaveStdOutContaining(expectedProgramOutput);
        }

        [Theory]

        // implict rid with option to append rid to output path off -> do not append
        [InlineData("implicitOff", "", false, false)]

        // implicit rid with option to append rid to output path on -> do not append (never append implicit rid irrespective of option)
        [InlineData("implicitOn", "", true, false)]

        // explicit  rid with option to append rid to output path off -> do not append
        [InlineData("explicitOff", "win7-x86", false, false)]
        
        // explicit rid with option to append rid to output path on -> append
        [InlineData("explicitOn", "win7-x64", true, true)]
        public void It_appends_rid_to_outdir_correctly(string identifier, string rid, bool useAppendOption, bool shouldAppend)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var testAsset = _testAssetsManager
                .CopyTestAsset("DesktopMinusRid", identifier: Path.DirectorySeparatorChar + identifier)
                .WithSource()
                .WithProjectChanges(project =>
                {
                    var ns = project.Root.Name.Namespace;
                    var propertyGroup = project.Root.Elements(ns + "PropertyGroup").First();
                    propertyGroup.Add(new XElement(ns + "RuntimeIdentifier", rid));
                    propertyGroup.Add(new XElement(ns + "AppendRuntimeIdentifierToOutputPath", useAppendOption.ToString()));
                })
                .Restore();

            var buildCommand = new BuildCommand(Stage0MSBuild, testAsset.TestRoot);
            buildCommand
                .Execute()
                .Should()
                .Pass();

            var publishCommand = new PublishCommand(Stage0MSBuild, testAsset.TestRoot);
            publishCommand
                .Execute()
                .Should()
                .Pass();

            string expectedOutput;
            switch (rid)
            {
                case "":
                    expectedOutput = "Native code was not used (MSIL)";
                    break;

                case "win7-x86":
                    expectedOutput = "Native code was not used (X86)";
                    break;

                case "win7-x64":
                    expectedOutput = "Native code was not used (Amd64)";
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(rid));
            }

            var outputDirectory = buildCommand.GetOutputDirectory("net46", runtimeIdentifier: shouldAppend ? rid : "");
            var publishDirectory = publishCommand.GetOutputDirectory("net46", runtimeIdentifier: rid);

            foreach (var directory in new[] { outputDirectory, publishDirectory })
            {
                var exe = Path.Combine(directory.FullName, "DesktopMinusRid.exe");

                var runCommand = Command.Create(exe, Array.Empty<string>());
                runCommand
                    .CaptureStdOut()
                    .Execute()
                    .Should()
                    .Pass()
                    .And
                    .HaveStdOutContaining(expectedOutput);
            }
        }

        [Theory]
        [InlineData("win7-x86", "x86")]
        [InlineData("win8-x86-aot", "x86")]
        [InlineData("win7-x64", "x64")]
        [InlineData("win8-x64-aot", "x64")]
        [InlineData("win10-arm", "arm")]
        [InlineData("win10-arm-aot", "arm")]
        //PlatformTarget=arm64 is not supported and never inferred
        [InlineData("win10-arm64", "AnyCPU")]
        [InlineData("win10-arm64-aot", "AnyCPU")]
        // cpu architecture is never expected at the front
        [InlineData("x86-something", "AnyCPU")]
        [InlineData("x64-something", "AnyCPU")]
        [InlineData("arm-something", "AnyCPU")]
        public void It_builds_with_inferred_platform_target(string runtimeIdentifier, string expectedPlatformTarget)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var testAsset = _testAssetsManager
                .CopyTestAsset("DesktopMinusRid", identifier: Path.DirectorySeparatorChar + runtimeIdentifier)
                .WithSource()
                .Restore("", $"/p:RuntimeIdentifier={runtimeIdentifier}");

            var getValuesCommand = new GetValuesCommand(Stage0MSBuild, testAsset.TestRoot,
                "net46", "PlatformTarget", GetValuesCommand.ValueType.Property);

            getValuesCommand
                .Execute($"/p:RuntimeIdentifier={runtimeIdentifier}")
                .Should()
                .Pass();

            getValuesCommand
                .GetValues()
                .Should()
                .BeEquivalentTo(expectedPlatformTarget);
        }

        [Fact]
        public void It_respects_explicit_platform_target()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var testAsset = _testAssetsManager
                .CopyTestAsset("DesktopMinusRid")
                .WithSource()
                .Restore("", $"/p:RuntimeIdentifier=win7-x86");

            var getValuesCommand = new GetValuesCommand(Stage0MSBuild, testAsset.TestRoot,
                "net46", "PlatformTarget", GetValuesCommand.ValueType.Property);

            getValuesCommand
                .Execute($"/p:RuntimeIdentifier=win7-x86", "/p:PlatformTarget=x64")
                .Should()
                .Pass();

            getValuesCommand
                .GetValues()
                .Should()
                .BeEquivalentTo("x64");
        }

        [Fact]
        public void It_includes_default_framework_references()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var testProject = new TestProject()
            {
                Name = "DefaultReferences",
                //  TODO: Add net35 to the TargetFrameworks list once https://github.com/Microsoft/msbuild/issues/1333 is fixed
                TargetFrameworks = "net40;net45;net461",
                IsSdkProject = true,
                IsExe = true
            };

            string sourceFile =
@"using System;

namespace DefaultReferences
{
    public class TestClass
    {
        public static void Main(string [] args)
        {
            var uri = new System.Uri(""http://github.com/dotnet/corefx"");
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        }
    }
}";
            testProject.SourceFiles.Add("TestClass.cs", sourceFile);

            var testAsset = _testAssetsManager.CreateTestProject(testProject)
                .Restore("DefaultReferences");

            var buildCommand = new BuildCommand(Stage0MSBuild, Path.Combine(testAsset.TestRoot, "DefaultReferences"));

            buildCommand
                .CaptureStdOut()
                .Execute()
                .Should()
                .Pass()
                .And
                .NotHaveStdOutMatching("Could not resolve this reference", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        }

        [Fact]
        public void It_generates_binding_redirects_if_needed()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var testAsset = _testAssetsManager
                .CopyTestAsset("DesktopNeedsBindingRedirects")
                .WithSource()
                .Restore();

            var buildCommand = new BuildCommand(Stage0MSBuild, testAsset.TestRoot);

            buildCommand
                .Execute()
                .Should()
                .Pass();

            var outputDirectory = buildCommand.GetOutputDirectory("net452");

            outputDirectory.Should().HaveFiles(new[] {
                "DesktopNeedsBindingRedirects.exe",
                "DesktopNeedsBindingRedirects.exe.config"
            });
        }
    }
}
