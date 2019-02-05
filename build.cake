// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

//////////////////////////////////////////////////////////////////////
// ADDINS
//////////////////////////////////////////////////////////////////////

#addin "nuget:?package=Cake.FileHelpers&version=3.1.0"
#addin "nuget:?package=Cake.Codecov&version=0.5.0"
#addin "nuget:?package=Cake.Coverlet&version=2.2.1"
#addin "nuget:?package=Cake.GitVersioning&version=2.3.38"

//////////////////////////////////////////////////////////////////////
// MODULES
//////////////////////////////////////////////////////////////////////

#module nuget:?package=Cake.DotNetTool.Module&version=0.1.0

//////////////////////////////////////////////////////////////////////
// TOOLS
//////////////////////////////////////////////////////////////////////

#tool "nuget:?package=vswhere&version=2.5.9"
#tool "nuget:?package=xunit.runner.console&version=2.4.1"
#tool "nuget:?package=Codecov&version=1.1.0"
#tool "nuget:?package=ReportGenerator&version=4.0.9"

//////////////////////////////////////////////////////////////////////
// DOTNET TOOLS
//////////////////////////////////////////////////////////////////////

#tool "dotnet:?package=SignClient&version=1.0.82"
#tool "dotnet:?package=coverlet.console&version=1.4.1"
#tool "dotnet:?package=nbgv&version=2.3.38"

//////////////////////////////////////////////////////////////////////
// CONSTANTS
//////////////////////////////////////////////////////////////////////

const string project = "Punchclock";

// Whitelisted Packages
var packageWhitelist = new[] 
{ 
    "Punchclock",
};

var packageTestWhitelist = new[]
{
    "Punchclock.Tests", 
};

var testFrameworks = new[] { "netcoreapp2.1" };

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
if (string.IsNullOrWhiteSpace(target))
{
    target = "Default";
}

var configuration = Argument("configuration", "Release");
if (string.IsNullOrWhiteSpace(configuration))
{
    configuration = "Release";
}

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// Should MSBuild treat any errors as warnings?
var treatWarningsAsErrors = false;

// Build configuration
var local = BuildSystem.IsLocalBuild;
var isPullRequest = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SYSTEM_PULLREQUEST_PULLREQUESTNUMBER"));
var isRepository = StringComparer.OrdinalIgnoreCase.Equals($"reactiveui/{project}", TFBuild.Environment.Repository.RepoName);

var msBuildPath = VSWhereLatest().CombineWithFilePath("./MSBuild/15.0/Bin/MSBuild.exe");

var informationalVersion = EnvironmentVariable("GitAssemblyInformationalVersion");

//////////////////////////////////////////////////////////////////////
// FOLDERS
//////////////////////////////////////////////////////////////////////

// Artifacts
var artifactDirectory = "./artifacts/";
var testsArtifactDirectory = artifactDirectory + "tests/";
var binariesArtifactDirectory = artifactDirectory + "binaries/";
var packagesArtifactDirectory = artifactDirectory + "packages/";

// OpenCover file location
var testCoverageOutputFile = MakeAbsolute(File(testsArtifactDirectory + "TestCoverage.xml"));

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////
Setup(context =>
{
    if (!IsRunningOnWindows())
    {
        throw new NotImplementedException($"{project} will only build on Windows (w/Xamarin installed) because it's not possible to target UWP, WPF and Windows Forms from UNIX.");
    }

    StartProcess(Context.Tools.Resolve("nbgv*").ToString(), "cloud");
    Information($"Building version {GitVersioningGetVersion().SemVer2} of {project}.");
    Information($"Building on pull request {isPullRequest} of {TFBuild.Environment.Repository.RepoName}.");

    CleanDirectories(artifactDirectory);
    CreateDirectory(testsArtifactDirectory);
    CreateDirectory(binariesArtifactDirectory);
    CreateDirectory(packagesArtifactDirectory);
});

Teardown(context =>
{
    // Executed AFTER the last task.
});

//////////////////////////////////////////////////////////////////////
// HELPER METHODS
//////////////////////////////////////////////////////////////////////
Action<string, string, bool> Build = (solution, packageOutputPath, doNotOptimise) =>
{
    Information("Building {0} using {1}", solution, msBuildPath);

    var msBuildSettings = new MSBuildSettings() {
            ToolPath = msBuildPath,
            ArgumentCustomization = args => args.Append("/m /NoWarn:VSX1000"),
            NodeReuse = false,
            Restore = true
        }
        .WithProperty("TreatWarningsAsErrors", treatWarningsAsErrors.ToString())
        .SetConfiguration(configuration)     
        .WithTarget("build;pack")                   
        .SetVerbosity(Verbosity.Minimal);

    if (!string.IsNullOrWhiteSpace(packageOutputPath))
    {
        msBuildSettings = msBuildSettings.WithProperty("PackageOutputPath",  MakeAbsolute(Directory(packageOutputPath)).ToString().Quote());
    }

    if (doNotOptimise)
    {
        msBuildSettings = msBuildSettings.WithProperty("Optimize",  "False");
    }

    MSBuild(solution, msBuildSettings);
};

Action<string> CoverageTest = (packageName) => 
{
    var projectName = $"./src/{packageName}/{packageName}.csproj";
    Build(projectName, null, true);
        
    foreach (var testFramework in testFrameworks)
    {
        Information($"Performing coverage tests on {packageName}");

        var testFile = $"./src/{packageName}/bin/{configuration}/{testFramework}/{packageName}.dll";

        StartProcess(Context.Tools.Resolve("Coverlet*").ToString(), new ProcessSettings {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = new ProcessArgumentBuilder()
                .AppendQuoted(testFile)
                .AppendSwitch("--include", $"[{project}*]*")
                .AppendSwitch("--exclude", "[*.Tests*]*")
                .AppendSwitch("--exclude", "[*]*ThisAssembly*")
                .AppendSwitch("--exclude-by-file", "*ApprovalTests*")
                .AppendSwitchQuoted("--output", testCoverageOutputFile.ToString())
                .AppendSwitchQuoted("--merge-with", testCoverageOutputFile.ToString())
                .AppendSwitch("--format", "cobertura")
                .AppendSwitch("--target", "dotnet")
                .AppendSwitchQuoted("--targetargs", $"test {projectName}  --no-build -c {configuration} --logger:trx;LogFileName=testresults-{testFramework}.trx -r {testsArtifactDirectory}")
            });

        Information($"Finished coverage testing {packageName}");
    }
};

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Build")
    .Does (() =>
{

    // Clean the directories since we'll need to re-generate the debug type.
    CleanDirectories($"./src/**/obj/{configuration}");
    CleanDirectories($"./src/**/bin/{configuration}");

    foreach(var packageName in packageWhitelist)
    {
        Build($"./src/{packageName}/{packageName}.csproj", packagesArtifactDirectory, false);
    }

    CopyFiles(GetFiles($"./src/**/bin/{configuration}/**/*"), Directory(binariesArtifactDirectory), true);
});

Task("RunUnitTests")
    .Does(() =>
{
    // Clean the directories since we'll need to re-generate the debug type.
    CleanDirectories($"./src/**/obj/{configuration}");
    CleanDirectories($"./src/**/bin/{configuration}");

    foreach (var packageName in packageTestWhitelist)
    {
        CoverageTest(packageName);
    }

    ReportGenerator(testCoverageOutputFile, testsArtifactDirectory + "Report/");
})
.ReportError(exception =>
{
    var apiApprovals = GetFiles("./**/ApiApprovalTests.*");
    CopyFiles(apiApprovals, artifactDirectory);
});

Task("UploadTestCoverage")
    .WithCriteria(() => !local)
    .WithCriteria(() => isRepository)
    .IsDependentOn("RunUnitTests")
    .Does(() =>
{
    // Resolve the API key.
    var token = EnvironmentVariable("CODECOV_TOKEN");

    if(EnvironmentVariable("CODECOV_TOKEN") == null)
    {
        throw new Exception("Codecov token not found, not sending code coverage data.");
    }

    if (!string.IsNullOrEmpty(token))
    {
        Information("Upload {0} to Codecov server", testCoverageOutputFile);

        // Upload a coverage report.
        Codecov(testCoverageOutputFile.ToString(), token);
    }
});

Task("SignPackages")
    .IsDependentOn("Build")
    .WithCriteria(() => !local)
    .WithCriteria(() => !isPullRequest)
    .Does(() =>
{
    if(EnvironmentVariable("SIGNCLIENT_SECRET") == null)
    {
        throw new Exception("Client Secret not found, not signing packages.");
    }

    var nupkgs = GetFiles(packagesArtifactDirectory + "*.nupkg");
    foreach(FilePath nupkg in nupkgs)
    {
        var packageName = nupkg.GetFilenameWithoutExtension();
        Information($"Submitting {packageName} for signing");

        StartProcess(Context.Tools.Resolve("SignClient*").ToString(), new ProcessSettings {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = new ProcessArgumentBuilder()
                .Append("sign")
                .AppendSwitch("-c", "./SignPackages.json")
                .AppendSwitch("-i", nupkg.FullPath)
                .AppendSwitch("-r", EnvironmentVariable("SIGNCLIENT_USER"))
                .AppendSwitch("-s", EnvironmentVariable("SIGNCLIENT_SECRET"))
                .AppendSwitch("-n", "ReactiveUI")
                .AppendSwitch("-d", "ReactiveUI")
                .AppendSwitch("-u", "https://reactiveui.net")
            });

        Information($"Finished signing {packageName}");
    }
    
    Information("Sign-package complete");
});

Task("Package")
    .IsDependentOn("Build")
    .IsDependentOn("SignPackages")
    .Does (() =>
{
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Package")
    .IsDependentOn("RunUnitTests")
    .IsDependentOn("UploadTestCoverage")
    .Does (() =>
{
});

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
