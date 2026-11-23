#if UNITY_EDITOR
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SandTable.Editor
{
    /// <summary>
    /// 往现有 TMP 图集里【追加】缺失字形，供 -batchmode -executeMethod 调用：
    ///
    ///   Unity.exe -batchmode -nographics -quit \
    ///     -projectPath &lt;repo&gt;/sandtable \
    ///     -executeMethod SandTable.Editor.CliAddGlyphs.AddMissingGlyphs
    ///
    /// 为什么是追加而不是重烘：三个在用的字体资产是 <b>Static</b> 图集
    /// （<c>m_AtlasPopulationMode: 0</c>，运行时没有 Dynamic 兜底，表外字必然是豆腐块），
    /// 单张 4096² 里已经装了 3866 个字形。而 <c>Assets/Fonts/charset_used.txt</c> 只有 1738 字，
    /// 两者【早已不同步】—— 拿那份字表去 Font Asset Creator 重烘，会把图集里现有的
    /// 两千多个字直接烘掉（例如场景标题"周界入侵告警详情"的"侵"字就只在图集里、不在字表里）。
    /// 所以这里走 TryAddCharacters 增量追加：现有字形一个不动，只往空闲区塞新的。
    ///
    /// Static 资产不能直接 TryAddCharacters，需要临时切到 Dynamic，加完再切回去。
    /// 切回 Static 是必须的：留在 Dynamic 会让真机运行时按需生成字形，
    /// 行为和出包时验证的不一致。
    /// </summary>
    internal static class CliAddGlyphs
    {
        /// <summary>
        /// 用字表。图集里缺的字会从这里补齐 —— 所以往这份文件加字、再跑一次本工具，
        /// 就是新增 UI 文案后的标准动作，不用改代码。
        ///
        /// 注意方向：是【字表补进图集】，不是拿字表重烘。图集里有些字（如场景标题
        /// "周界入侵告警详情"的"侵"）压根不在字表里，反向覆盖会把它们烘掉。
        /// </summary>
        private const string CharsetPath = "Assets/Fonts/charset_used.txt";

        /// <summary>
        /// 只处理场景实际引用的三个字重（Bold 392 处、CN 320 处、Heavy 2 处）。
        /// 其余 6 个字重场景没用到，不动它们，免得凭空产生几十 MB 的二进制改动。
        /// </summary>
        private static readonly string[] FontAssetPaths =
        {
            "Assets/Fonts/SourceHanSansCN/SourceHanSansCN.asset",
            "Assets/Fonts/SourceHanSansCN/SourceHanSansCN-Bold.asset",
            "Assets/Fonts/SourceHanSansCN/SourceHanSansCN-Heavy.asset",
        };

        [MenuItem("Tools/字体/补齐缺失字形")]
        public static void AddMissingGlyphs()
        {
            var summary = new StringBuilder();
            bool allOk = true;

            string charsetFullPath = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), CharsetPath);
            if (!System.IO.File.Exists(charsetFullPath))
            {
                throw new System.Exception($"[补字形] 找不到用字表：{charsetFullPath}");
            }

            // 去掉换行/控制字符，TMP 不需要也加不进去
            var sb = new StringBuilder();
            foreach (char c in System.IO.File.ReadAllText(charsetFullPath))
            {
                if (!char.IsControl(c))
                {
                    sb.Append(c);
                }
            }
            string charsToAdd = sb.ToString();
            Debug.Log($"[补字形] 用字表 {CharsetPath} 共 {charsToAdd.Length} 个字符（去重前）");

            foreach (string path in FontAssetPaths)
            {
                TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (fontAsset == null)
                {
                    Debug.LogError($"[补字形] 找不到字体资产：{path}");
                    allOk = false;
                    continue;
                }

                int before = fontAsset.characterTable.Count;
                AtlasPopulationMode originalMode = fontAsset.atlasPopulationMode;

                // Static 资产的 m_SourceFontFile 是空的（{fileID: 0}），只有编辑器专用的
                // m_SourceFontFile_EditorRef 还指着 .otf。Dynamic 追加要读前者，而它是只读属性，
                // 所以走 SerializedObject 直接改底层字段。
                //
                // 加完必须【还原】：Static 资产留着 m_SourceFontFile 的引用，会把三个 .otf
                // （约 25MB）一起打进 APK，白白撑大包体。
                var so = new SerializedObject(fontAsset);
                SerializedProperty runtimeRef = so.FindProperty("m_SourceFontFile");
                SerializedProperty editorRef = so.FindProperty("m_SourceFontFile_EditorRef");
                Object originalRuntimeRef = runtimeRef != null ? runtimeRef.objectReferenceValue : null;

                if (runtimeRef != null && editorRef != null && runtimeRef.objectReferenceValue == null)
                {
                    runtimeRef.objectReferenceValue = editorRef.objectReferenceValue;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                if (fontAsset.sourceFontFile == null)
                {
                    Debug.LogError($"[补字形] {path} 拿不到源字体文件，无法追加字形");
                    allOk = false;
                    continue;
                }

                fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                bool ok = fontAsset.TryAddCharacters(charsToAdd, out string missing);
                fontAsset.atlasPopulationMode = originalMode;

                // 还原源字体引用，避免 .otf 被打进包
                if (runtimeRef != null)
                {
                    so.Update();
                    runtimeRef.objectReferenceValue = originalRuntimeRef;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                int after = fontAsset.characterTable.Count;
                fontAsset.ReadFontAssetDefinition();
                EditorUtility.SetDirty(fontAsset);

                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (ok && string.IsNullOrEmpty(missing))
                {
                    summary.AppendLine($"  {name}: {before} -> {after} 字形，全部补齐");
                }
                else
                {
                    allOk = false;
                    summary.AppendLine($"  {name}: {before} -> {after} 字形，仍缺 [{missing}]");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[补字形] 按用字表补齐结果：\n{summary}");

            if (!allOk)
            {
                // batchmode 下靠退出码判断成败，不能只打日志
                throw new System.Exception("[补字形] 有字符未能追加，见上面的逐项结果。");
            }
        }
    }
}
#endif
