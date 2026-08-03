using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SheetMan.Tests
{
    /// <summary>
    /// Runs Unreal Header Tool over a generated module.
    ///
    /// UHT is normally invoked by UnrealBuildTool as part of a build, and takes a manifest
    /// UBT writes describing every module in the target. Building a whole target to check
    /// one generated header would take minutes; a manifest naming CoreUObject and the
    /// module under test takes seconds and checks the same thing - the reflection macros,
    /// the include order, and which property types the tool will accept.
    ///
    /// CoreUObject is there because every USTRUCT depends on it. Its own generated headers
    /// are already built in an engine that has been compiled, which is the only kind this
    /// can run against.
    /// </summary>
    internal static class UnrealToolchain
    {
        public static ToolResult RunHeaderTool(
            string engineRoot, string moduleDir, string moduleName, string headerName)
        {
            string headerTool = Path.Combine(engineRoot, "Engine", "Binaries", "Win64", "UnrealHeaderTool.exe");

            if (!File.Exists(headerTool))
            {
                return new ToolResult
                {
                    Succeeded = false,
                    Output = $"No UnrealHeaderTool at {headerTool}. SHEETMAN_UE_ROOT must name a built engine.",
                };
            }

            string workDir = Path.Combine(RepoLayout.OutputDir("_uht"));

            if (Directory.Exists(workDir))
                Directory.Delete(workDir, recursive: true);

            string outputDir = Path.Combine(workDir, "Inc", moduleName);
            Directory.CreateDirectory(outputDir);

            string borrowed = FindEngineManifest(engineRoot);

            if (borrowed == null)
            {
                return new ToolResult
                {
                    Succeeded = false,
                    Output =
                        "No .uhtmanifest found under the engine's Intermediate directory. The gate borrows " +
                        "CoreUObject's module entry from one, because its header list is curated - globbing " +
                        "the directory pulls in headers the tool rejects. Build the engine editor target once, " +
                        "or point SHEETMAN_UE_MANIFEST at a manifest.",
                };
            }

            string manifest = Path.Combine(workDir, "SheetManVerify.uhtmanifest");

            File.WriteAllText(manifest,
                Manifest(engineRoot, borrowed, moduleDir, moduleName, headerName, outputDir, workDir));

            // A .uproject is required even though nothing in it is used here; UHT resolves
            // engine paths from it.
            string project = Path.Combine(engineRoot, "Engine", "Engine.uproject");

            return Execute(headerTool, workDir,
                File.Exists(project) ? project : engineRoot,
                manifest,
                "-Unattended",
                "-WarningsAsErrors",
                "-installed");
        }

        /// <summary>
        /// A manifest the engine or a project has already produced, whose CoreUObject entry
        /// this borrows.
        ///
        /// Borrowed rather than reconstructed: that entry lists a curated set of headers,
        /// and globbing the directory instead pulls in ones the tool rejects - the first
        /// attempt at this failed inside ObjectMacros.h, nowhere near the code under test.
        /// </summary>
        private static string FindEngineManifest(string engineRoot)
        {
            string configured = Environment.GetEnvironmentVariable("SHEETMAN_UE_MANIFEST");
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                return configured;

            string intermediate = Path.Combine(engineRoot, "Engine", "Intermediate", "Build");
            if (!Directory.Exists(intermediate))
                return null;

            return Directory.EnumerateFiles(intermediate, "*.uhtmanifest", SearchOption.AllDirectories)
                            .FirstOrDefault(path => HasCoreUObject(path));
        }

        private static bool HasCoreUObject(string manifestPath)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

                return document.RootElement.GetProperty("Modules").EnumerateArray()
                               .Any(module => module.GetProperty("Name").GetString() == "CoreUObject");
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The manifest UHT reads: CoreUObject as some real build described it, and the
        /// module under test.
        /// </summary>
        private static string Manifest(
            string engineRoot, string borrowedManifest, string moduleDir, string moduleName,
            string headerName, string outputDir, string workDir)
        {
            using var source = JsonDocument.Parse(File.ReadAllText(borrowedManifest));

            var coreUObject = source.RootElement.GetProperty("Modules").EnumerateArray()
                                    .First(module => module.GetProperty("Name").GetString() == "CoreUObject");

            var manifest = new
            {
                IsGameTarget = true,
                RootLocalPath = engineRoot,
                TargetName = "SheetManVerify",
                ExternalDependenciesFile = Path.Combine(workDir, "SheetManVerify.deps"),
                Modules = new object[]
                {
                    JsonSerializer.Deserialize<object>(coreUObject.GetRawText()),
                    new
                    {
                        Name = moduleName,
                        ModuleType = "GameRuntime",
                        OverrideModuleType = "None",
                        BaseDirectory = moduleDir,
                        IncludeBase = Path.Combine(moduleDir, "Public"),
                        OutputDirectory = outputDir,
                        ClassesHeaders = Array.Empty<string>(),
                        PublicHeaders = new[] { Path.Combine(moduleDir, "Public", headerName) },
                        PrivateHeaders = Array.Empty<string>(),
                        GeneratedCPPFilenameBase = Path.Combine(outputDir, moduleName + ".gen"),
                        SaveExportedHeaders = true,
                        UHTGeneratedCodeVersion = "None",
                    },
                },
            };

            return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        }

        private static ToolResult Execute(string fileName, string workingDirectory, params string[] args)
        {
            var psi = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            var output = new StringBuilder();

            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(milliseconds: 600_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("UnrealHeaderTool did not finish within ten minutes.");
            }

            process.WaitForExit();

            return new ToolResult
            {
                Succeeded = process.ExitCode == 0,
                StdOut = output.ToString(),
                Output = output.ToString(),
            };
        }
    }
}
