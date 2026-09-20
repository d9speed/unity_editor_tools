using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEngine;
using UnityBlenderPoseSync.World;

[assembly: ExportsPlugin(typeof(UnityBlenderPoseSync.World.Editor.PoseSyncBakePlugin))]

namespace UnityBlenderPoseSync.World.Editor
{
    public sealed class PoseSyncBakePlugin : Plugin<PoseSyncBakePlugin>
    {
        public override string QualifiedName => "unity-blender-pose-sync.bake";
        public override string DisplayName => "Pose Sync (bake bone refs)";

        protected override void Configure()
        {
            // Run late in Transforming, after Modular Avatar has merged/restructured the rig,
            // so the resolved transforms are the final ones.
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                    sequence.Run("Bake pose-sync bone references", Bake));
        }

        private void Bake(BuildContext ctx)
        {
            var sender = ctx.AvatarRootObject.GetComponentInChildren<BlenderPoseSenderWorld>(true);
            if (sender == null)
            {
                return;
            }

            // ObjectPathRemapper maps a current object back to its original (pre-build) path.
            // It is part of AnimatorServicesContext, normally already active (Modular Avatar uses
            // it). If unavailable, fall back to the current name (fine when names are unchanged).
            ObjectPathRemapper remapper = null;
            try
            {
                remapper = ctx.Extension<AnimatorServicesContext>()?.ObjectPathRemapper;
            }
            catch
            {
                remapper = null;
            }

            var transforms = sender.CollectExtraBoneTransformsForBake();
            var baked = new List<BakedBone>(transforms.Count);
            foreach (var t in transforms)
            {
                if (t == null)
                {
                    continue;
                }
                baked.Add(new BakedBone
                {
                    target_name = OriginalLeafName(remapper, t),
                    human_bone = null,
                    transform = t,
                });
            }

            sender.SetBakedBones(baked);
            Debug.Log($"Pose Sync: baked {baked.Count} extra/PhysBone bone references.", sender);
        }

        // The original (pre-build) leaf bone name, used as the Blender match target.
        private static string OriginalLeafName(ObjectPathRemapper remapper, Transform t)
        {
            if (remapper != null)
            {
                var path = remapper.GetAllPathsForObject(t)?.FirstOrDefault();
                if (!string.IsNullOrEmpty(path))
                {
                    var slash = path.LastIndexOf('/');
                    return slash >= 0 ? path.Substring(slash + 1) : path;
                }
            }
            return t.name;
        }
    }
}
