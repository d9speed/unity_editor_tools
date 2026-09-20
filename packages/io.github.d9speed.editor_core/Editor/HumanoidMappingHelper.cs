using D9speed_BaseEditorUtils;
using System;
using System.Collections.Generic;
using UnityEngine;



// Humanoidのマッピング処理をまとめたヘルパー
public static class HumanoidMappingHelper
{
    // humanNameごとのTransform対応表
    public struct HumanBoneMapping
    {
        public Transform Source;
        public Transform Target;
    }

    // Humanoid判定
    public static bool IsHumanoid(Animator animator)
    {
        if (animator == null) return false;
        if (animator.isHuman) return true;
        if (animator.avatar == null) return false;
        
        // アバターがヒューマノイド　or アバターのボーンとヒューマノイドボーンのマッピングがNullじゃなくて何かしら値を持っている
        // 欠損ボーンがあっても、中途半端にヒューマノイドだった場合もヒューマノイド判定にする(服だとHeadがないとか脚がないとかありがち)

        return animator.avatar.isHuman || (animator.avatar.humanDescription.human != null
            && animator.avatar.humanDescription.human.Length > 0);
    }

    // humanNameをキーに、Source/TargetのTransformを返す
    public static Dictionary<string, HumanBoneMapping> BuildHumanBoneMapping(Animator sourceAnimator, Animator targetAnimator)
    {
        var map = new Dictionary<string, HumanBoneMapping>();
        if (sourceAnimator == null || targetAnimator == null) return map;

        // Humanoid同士ならHumanBodyBonesで対応付け
        if (IsHumanoid(sourceAnimator) && IsHumanoid(targetAnimator))
        {
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                // アニメーターからヒューマノイドボーンを取得する 返り値はTransform
                var source = sourceAnimator.GetBoneTransform(bone);
                var target = targetAnimator.GetBoneTransform(bone);
                if (source == null && target == null) continue;

                // 対応を辞書化

                map[bone.ToString()] = new HumanBoneMapping
                {
                    Source = source,
                    Target = target
                };
            }
            return map;
        }

        // Humanoidでない場合でも、AvatarがあるならHumanDescriptionから取れる分だけマッピング
        
        if (sourceAnimator.avatar == null || targetAnimator.avatar == null) return map;

        var sourceHuman = sourceAnimator.avatar.humanDescription.human;
        var targetHuman = targetAnimator.avatar.humanDescription.human;
        if (sourceHuman == null || targetHuman == null) return map;

        var sourceNameMap = BuildNameMap(sourceAnimator.transform);
        var targetNameMap = BuildNameMap(targetAnimator.transform);

        var sourceHumanToBone = new Dictionary<string, string>();
        foreach (var bone in sourceHuman)
        {
            if (string.IsNullOrEmpty(bone.humanName) || string.IsNullOrEmpty(bone.boneName)) continue;
            sourceHumanToBone[bone.humanName] = bone.boneName;
        }

        var targetHumanToBone = new Dictionary<string, string>();
        foreach (var bone in targetHuman)
        {
            if (string.IsNullOrEmpty(bone.humanName) || string.IsNullOrEmpty(bone.boneName)) continue;
            targetHumanToBone[bone.humanName] = bone.boneName;
        }

        var shared = new HashSet<string>(sourceHumanToBone.Keys);
        shared.IntersectWith(targetHumanToBone.Keys);

        foreach (var humanName in shared)
        {
            sourceNameMap.TryGetValue(sourceHumanToBone[humanName], out var source);
            targetNameMap.TryGetValue(targetHumanToBone[humanName], out var target);
            map[humanName] = new HumanBoneMapping
            {
                Source = source,
                Target = target
            };
        }

        return map;
    }




    // sourceTransformが属するhumanNameを探して、対応するTargetを返す
    public static bool TryMapByHumanName(
        Animator sourceAnimator,
        Animator targetAnimator,
        Transform sourceTransform,
        out Transform mapped,
        out string humanName)
    {
        mapped = null;
        humanName = null;
        if (sourceAnimator == null || targetAnimator == null || sourceTransform == null) return false;

        var mapping = BuildHumanBoneMapping(sourceAnimator, targetAnimator);
        if (mapping.Count == 0) return false;


        // 最も近いHumanボーン（深さが最小の祖先）を優先
        var bestDepth = int.MaxValue;
        foreach (var entry in mapping)
        {
            if (entry.Value.Source == null) continue;
            if (!TryGetDepthFromAncestor(sourceTransform, entry.Value.Source, out var depth)) continue;
            if (depth < bestDepth)
            {
                bestDepth = depth;
                humanName = entry.Key;
                mapped = entry.Value.Target;
            }
        }

        if (humanName == null) return false;
        return mapped != null;

    }

    public static void CopyTransformValues(Transform source, Transform target)
    {
        if (source == null || target == null) return;

        target.localPosition = source.localPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    public static int GetDepth(Transform transform)
    {
        var depth = 0;
        while (transform != null && transform.parent != null)
        {
            depth++;
            transform = transform.parent;
        }
        return depth;
    }

    public static int GetDepthFrom(Transform root, Transform transform)
    {
        var depth = 0;
        var current = transform;
        while (current != null && current != root)
        {
            depth++;
            current = current.parent;
        }
        return current == root ? depth : 0;
    }

    public static bool IsDescendantOf(Transform child, Transform root)
    {
        return TryGetDepthFromAncestor(child, root, out _);
    }

    public static string GetRelativePath(Transform root, Transform target)
    {
        return HierarchyPathHelper.GetRelativePath(root, target);
    }


    // 子から親へ上流を辿っていく再帰関数
    // childからrootまでの距離を返す。rootが祖先でない場合はfalse。
    public static bool TryGetDepthFromAncestor(Transform child, Transform root, out int depth)
    {
        depth = 0;
        if (child == null || root == null) return false;

        var current = child;
        while (current != null)
        {
            if (current == root) return true;
            depth++;
            current = current.parent;
        }

        depth = 0;
        return false;
    }

    // transformの辞書を返すやつ
    public static Dictionary<string, Transform> BuildNameMap(Transform root)
    {
        var map = new Dictionary<string, Transform>();
        if (root == null) return map;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!map.ContainsKey(t.name))
            {
                map[t.name] = t;
            }
        }
        return map;
    }
}
