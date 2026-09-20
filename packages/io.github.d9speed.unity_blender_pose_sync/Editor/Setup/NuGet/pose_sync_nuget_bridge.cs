using System;
using System.IO;
using System.Linq;
using NugetForUnity;
using NugetForUnity.Models;
using NugetForUnity.Configuration;
using UnityEditor;

namespace UnityBlenderPoseSync.Setup
{
    /// <summary>Compiled only after supported NuGetForUnity is installed.</summary>
    public static class PoseSyncNuGetBridge
    {
        public static string VersionOf(string id) =>
            InstalledPackagesManager.InstalledPackages.FirstOrDefault(package => package.Id == id)?.Version ?? "";

        public static bool CoreInstalled()
        {
            return new [] { "MessagePack", "MessagePack.Annotations", "MessagePackAnalyzer" }
                .All(id => VersionOf(id) == PoseSyncDependencies.MessagePackVersion);
        }

        public static bool AnalyzerReady()
        {
            var root = ConfigurationManager.NugetConfigFile.RepositoryPath;
            return Directory.Exists(root) && Directory.GetFiles(root, "MessagePack.SourceGenerator.dll", SearchOption.AllDirectories)
                .Any(path => AssetDatabase.GetLabels(AssetImporter.GetAtPath(Relative(path))).Contains("RoslynAnalyzer"));
        }

        private static string Relative(string path) =>
            Path.GetRelativePath(Directory.GetCurrentDirectory(), path).Replace('\\', '/');

        public static bool Install()
        {
            // Preflight the complete family before changing anything. Explicitly
            // update each member because NuGet preserves manually installed dependencies.
            var ids = new [] { "MessagePack.Annotations", "MessagePackAnalyzer", "MessagePack" };
            foreach (var id in ids)
                PoseSyncDependencies.ValidateMessagePackUpgrade(id, VersionOf(id));

            foreach (var id in ids)
            {
                var existing = InstalledPackagesManager.InstalledPackages.FirstOrDefault(package => package.Id == id);
                if (existing?.Version == PoseSyncDependencies.MessagePackVersion) continue;
                var identifier = new NugetPackageIdentifier(id, PoseSyncDependencies.MessagePackVersion)
                { IsManuallyInstalled = id == "MessagePack" || existing?.IsManuallyInstalled == true };
                if (!NugetPackageInstaller.InstallIdentifier(identifier, refreshAssets: false,
                    isSlimRestoreInstall: false, allowUpdateForExplicitlyInstalled: true)) return false;
            }
            // Remaining dependencies are resolved by NuGetForUnity for the project's
            // API compatibility level, without a hand-written transitive dependency list.
            return CoreInstalled();
        }
    }
}
