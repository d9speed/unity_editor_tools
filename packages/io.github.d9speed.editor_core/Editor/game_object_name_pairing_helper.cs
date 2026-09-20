using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace D9speed_BaseEditorUtils
{
public static class GameObjectNamePairingUniqueFinder
{
    /// <summary>
    /// SkinnedMeshRendererの名前を正規化？して異なるアバター間でのスキンメッシュに
    /// くっついたMA系コンポーネントをまとめてコピー可能にするため、まず
    /// スキンメッシュオブジェクトの名前からアバター名のPrefix/Suffixを取り除いてマッチング、比較する
    /// </summary>
    /// 
    // ---- tokenize ----
    public static List<string> Tokenize(string name)
    {
        if (string.IsNullOrEmpty(name)) return new List<string>();
        name = name.Trim();

        var tokens = new List<string>();
        var seps = new[] { '_', '-', ' ', '.', '/', '\\' };

        foreach (var part in name.Split(seps, StringSplitOptions.RemoveEmptyEntries))
            tokens.AddRange(SplitCamel(part));

        return tokens
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static IEnumerable<string> SplitCamel(string s)
    {
        if (string.IsNullOrEmpty(s)) yield break;

        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i > 0 && char.IsLower(s[i - 1]) && char.IsUpper(c))
            {
                yield return sb.ToString();
                sb.Clear();
            }
            sb.Append(c);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    // ---- key heuristic ----
    /// <summary>
    /// リスト全体のトークン頻度を使って「一番ユニークなトークン」をキーにする。
    /// 例: Costume_Manuka_bag -> bag, bag_Costume_Manuka -> bag
    /// </summary>
    public static Func<string, string> BuildKeySelector(IEnumerable<string> names, IEnumerable<string> ignoreTokens = null)
    {
        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ignoreSet = new HashSet<string>(ignoreTokens ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var n in names ?? Array.Empty<string>())
        {
            foreach (var t in Tokenize(n).Distinct())
            {
                if (ignoreSet.Contains(t)) continue;
                freq.TryGetValue(t, out var c);
                freq[t] = c + 1;
            }
        }

        // その文字列に対してキーを返す関数
        return (string n) =>
        {
            var toks = Tokenize(n);
            if (toks.Count == 0) return "";

            string best = null;
            int bestFreq = int.MaxValue;
            int bestLen = -1;

            foreach (var t in toks)
            {
                if (ignoreSet.Contains(t)) continue;
                freq.TryGetValue(t, out var f);
                // 低頻度優先 → 長い方優先（boots vs boot などの事故を減らす）
                if (best == null || f < bestFreq || (f == bestFreq && t.Length > bestLen))
                {
                    best = t;
                    bestFreq = f;
                    bestLen = t.Length;
                }
            }
            return best ?? toks.Last();
        };
    }

    public sealed class Pair
    {
        public string Key;
        public string A;
        public string B;
        public override string ToString() => $"{Key}, {A}, {B}";
    }

    /// <summary>
    /// list1/list2 をそれぞれキー抽出して突き合わせ（同キー複数は順番に消費）
    /// </summary>
    public static List<Pair> PairByHeuristicKeys(IEnumerable<string> list1, IEnumerable<string> list2, IEnumerable<string> ignoreTokens = null)
    {
        var a = (list1 ?? Array.Empty<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        var b = (list2 ?? Array.Empty<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();

        // 両方の情報で頻度を作る方が安定
        var keyOf = BuildKeySelector(a.Concat(b), ignoreTokens);

        Dictionary<string, Queue<string>> ToMap(List<string> src)
        {
            var map = new Dictionary<string, Queue<string>>();
            foreach (var s in src)
            {
                var k = keyOf(s);
                if (!map.TryGetValue(k, out var q)) map[k] = q = new Queue<string>();
                q.Enqueue(s);
            }
            return map;
        }

        var aMap = ToMap(a);
        var bMap = ToMap(b);

        var keys = new HashSet<string>(aMap.Keys);
        keys.UnionWith(bMap.Keys);

        var result = new List<Pair>();
        foreach (var k in keys.OrderBy(x => x))
        {
            aMap.TryGetValue(k, out var aq);
            bMap.TryGetValue(k, out var bq);

            int count = Math.Max(aq?.Count ?? 0, bq?.Count ?? 0);
            if (count == 0) count = 1;

            for (int i = 0; i < count; i++)
            {
                result.Add(new Pair
                {
                    Key = k,
                    A = (aq != null && aq.Count > 0) ? aq.Dequeue() : null,
                    B = (bq != null && bq.Count > 0) ? bq.Dequeue() : null,
                });
            }
        }
        return result;
    }
}
}
