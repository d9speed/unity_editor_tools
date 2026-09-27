using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class HumanoidAliasComponentCopierWindow
{
    private static void collect_bone_evidence(ScanResult scan)
    {
        var root = scan.Root.transform;
        foreach (var renderer in scan.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (var bone in renderer.bones)
            {
                if (bone == null || bone == root || !bone.IsChildOf(root)) continue;
                scan.skin_bones.Add(bone);
                add_bone_ancestors(scan, bone);
            }
        }

        foreach (var animator in scan.Root.GetComponentsInChildren<Animator>(true))
        {
            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman) continue;
            foreach (HumanBodyBones bone_id in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone_id == HumanBodyBones.LastBone) continue;
                var bone = animator.GetBoneTransform(bone_id);
                if (bone == null || bone == root || !bone.IsChildOf(root)) continue;
                // 辞書の指キーには空白があるため、HumanBodyBonesとの比較時に正規化する。
                var key = NormalizeBoneName(bone_id.ToString());
                if (!scan.humanoid_bones.TryGetValue(key, out var matches))
                    scan.humanoid_bones[key] = matches = new HashSet<Transform>();
                matches.Add(bone);
                scan.humanoid_transforms.Add(bone);
                add_bone_ancestors(scan, bone);
            }
        }
    }

    private static void add_bone_ancestors(ScanResult scan, Transform bone)
    {
        for (var current = bone; current != null && current != scan.Root.transform; current = current.parent)
            scan.bone_hierarchy.Add(current);
    }

    private static BoneCandidate score_bone_candidate(ScanResult scan, string key, List<string> aliases, TransformInfo info)
    {
        if (info.Transform == scan.Root.transform) return null;
        var from_humanoid = scan.humanoid_bones.TryGetValue(NormalizeBoneName(key), out var mapped)
            && mapped.Contains(info.Transform);
        // 設定で別のHumanBodyBonesに割り当てられたボーンを、名前から再解釈しない。
        if (!from_humanoid && scan.humanoid_transforms.Contains(info.Transform)) return null;
        if (scan.bone_hierarchy.Count > 0 && !scan.bone_hierarchy.Contains(info.Transform)) return null;

        var candidate = ScoreTransform(key, aliases, info);
        if (from_humanoid)
        {
            candidate.evidence_priority = 2;
            candidate.Score = ScoreExact;
            candidate.MatchedAlias = key;
            candidate.Reason = "Humanoid設定";
        }
        else if (scan.bone_hierarchy.Contains(info.Transform))
        {
            candidate.evidence_priority = 1;
            candidate.Reason += scan.skin_bones.Contains(info.Transform)
                ? " / SkinnedMeshのボーン参照" : " / ボーンの親階層";
        }
        return candidate;
    }

    private static bool is_bone_transform(Transform transform, ScanResult scan)
    {
        return scan.bone_hierarchy.Contains(transform)
            || scan.ResolvedByKey.Values.Any(candidate => candidate.Transform == transform);
    }

    private static bool have_matching_bone_roles(Transform source_transform, Transform target_transform,
        ScanResult source, ScanResult target)
    {
        return is_bone_transform(source_transform, source) == is_bone_transform(target_transform, target);
    }

    private static Transform find_unique_relative_path(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;
        var current = root;
        foreach (var name in path.Split('/'))
        {
            Transform match = null;
            foreach (Transform child in current)
            {
                if (child.name != name) continue;
                if (match != null) return null;
                match = child;
            }
            if (match == null) return null;
            current = match;
        }
        return current;
    }
}
