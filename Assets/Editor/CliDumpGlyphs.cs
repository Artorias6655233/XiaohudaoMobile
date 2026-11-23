#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SandTable.Editor
{
    /// <summary>
    /// 把三个在用字体资产的**实际字形表**导出成文本，供缺字核查。
    ///
    /// 【为什么需要这个】<c>Assets/Fonts/charset_used.txt</c> 不是图集内容的真相：
    /// 字表 1770 字，而图集里实际有 3800+ 字形，两者早已不同步
    /// （见 <see cref="CliAddGlyphs"/> 的注释）。拿字表判断「这个字会不会是豆腐块」
    /// 会得出大量假阳性 —— 实测按字表算缺 33 个字，真正补进去的只有 1 个，
    /// 其余 32 个图集里本来就有。
    ///
    /// 所以核查缺字必须以本工具导出的字形表为准。
    ///
    ///   Unity.exe -batchmode -nographics -quit -projectPath &lt;repo&gt;/sandtable \
    ///     -executeMethod SandTable.Editor.CliDumpGlyphs.Dump
    ///
    /// 只读，不改任何资产。输出到 Builds/glyphs_in_atlas.txt。
    /// </summary>
    internal static class CliDumpGlyphs
    {
        private static readonly string[] FontAssetPaths =
        {
            "Assets/Fonts/SourceHanSansCN/SourceHanSansCN.asset",
            "Assets/Fonts/SourceHanSansCN/SourceHanSansCN-Bold.asset",
            "Assets/Fonts/SourceHanSansCN/SourceHanSansCN-Heavy.asset",
        };

        [MenuItem("Tools/字体/导出图集现有字形")]
        public static void Dump()
        {
            var perFont = new List<string>();
            HashSet<uint> intersection = null;

            foreach (string path in FontAssetPaths)
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null)
                {
                    Debug.LogError("[导字形] 找不到字体资产：" + path);
                    continue;
                }

                var codes = new HashSet<uint>();
                foreach (TMP_Character c in font.characterTable) codes.Add(c.unicode);

                perFont.Add(Path.GetFileNameWithoutExtension(path) + "\t" + codes.Count);
                Debug.Log("[导字形] " + Path.GetFileNameWithoutExtension(path) + "：" + codes.Count + " 个字形");

                // 三个字重都得有，才算「这个字能正常显示」—— 场景里三个字重都在用
                if (intersection == null) intersection = new HashSet<uint>(codes);
                else intersection.IntersectWith(codes);
            }

            if (intersection == null) return;

            var sb = new StringBuilder();
            var sorted = new List<uint>(intersection);
            sorted.Sort();
            foreach (uint u in sorted)
            {
                if (u == 0 || u > 0x10FFFF) continue;
                sb.Append(char.ConvertFromUtf32((int)u));
            }

            Directory.CreateDirectory("Builds");
            File.WriteAllText("Builds/glyphs_in_atlas.txt", sb.ToString(), new UTF8Encoding(false));

            Debug.Log("[导字形] 三个字重都有的字形：" + intersection.Count +
                      " 个，已写到 Builds/glyphs_in_atlas.txt");
            foreach (string line in perFont) Debug.Log("[导字形] " + line);
        }
    }
}
#endif
