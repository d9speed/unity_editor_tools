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
            // NuGetForUnity resolves the transitive dependencies using the project's
            // API compatibility level. Do not maintain a hand-written dependency list.
            foreach (var id in new [] { "MessagePack", "MessagePack.Annotations", "MessagePackAnalyzer" })
            {
                var existing = VersionOf(id);
                if (existing.Length > 0 && existing != PoseSyncDependencies.MessagePackVersion)
                    throw new InvalidOperationException(id + " " + existing + " が導入済みです。NuGet画面で "
                        + PoseSyncDependencies.MessagePackVersion + " へ揃えてから再試行してください。既存版は変更していません。");
            }
            return NugetPackageInstaller.InstallIdentifier(new NugetPackageIdentifier("MessagePack", PoseSyncDependencies.MessagePackVersion)
            { IsManuallyInstalled = true }, refreshAssets: false, isSlimRestoreInstall: false, allowUpdateForExplicitlyInstalled: false);
        }
    }
}
