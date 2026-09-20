using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using UnityBlenderPoseSync.World;
using UnityBlenderPoseSync.World.Editor;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace D9speed.PackageValidation
{
    public static class PoseSyncNdmfChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void Run()
        {
            var root = new GameObject("Synthetic NDMF Avatar");
            BuildContext context = null;
            try
            {
                root.AddComponent<VRCAvatarDescriptor>();
                var bone = new GameObject("Original Bone").transform;
                bone.SetParent(root.transform, false);
                var child = new GameObject("Ignored Child").transform;
                child.SetParent(bone, false);
                var physics = root.AddComponent<VRCPhysBone>();
                physics.rootTransform = bone;
                physics.ignoreTransforms = new List<Transform> { child };
                var sender = root.AddComponent<BlenderPoseSenderWorld>();
                typeof(BlenderPoseSenderWorld).GetField("include_humanoid", Private).SetValue(sender, false);
                typeof(BlenderPoseSenderWorld).GetField("include_all_phys_bones", Private).SetValue(sender, true);
                context = new BuildContext(root, "Assets/pose_sync_ndmf_generated");
                context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
                bone.name = "Merged Bone$123";
                typeof(PoseSyncBakePlugin).GetMethod("Bake", Private).Invoke(new PoseSyncBakePlugin(), new object[] { context });
                var baked = (List<BakedBone>)typeof(BlenderPoseSenderWorld).GetField("baked_bones", Private).GetValue(sender);
                if (baked.Count != 1 || baked[0].target_name != "Original Bone" || baked[0].transform != bone)
                    throw new Exception("NDMF original bone name / PhysBone ignore mapping failed");
                if (!UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Editor).Any(a => a.name == "D9speed.PoseSync.NDMF.Editor"))
                    throw new Exception("Optional integration not compiled");
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/pose_sync_ndmf_results.json", "{\"passed\":true,\"ndmf\":\"1.13.1\",\"sdk\":\"3.10.5\",\"checks\":[\"optional assembly enabled\",\"PhysBone ignore respected\",\"original name survives rename\"]}");
            }
            finally
            {
                context?.DeactivateAllExtensionContexts();
                Object.DestroyImmediate(root);
            }
            EditorApplication.Exit(0);
        }
    }
}
