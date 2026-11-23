using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LZW.Module.MajorHazard.Mobile.Editor
{
    /// <summary>
    /// 修复 URP 下显示为粉色的材质：
    /// 1. 重新导入模型（FBX 内嵌材质是导入时按当时的渲染管线生成的，工程原来是内置管线，内嵌材质是 Standard → 粉色；URP 下重导即生成 URP Lit）；
    /// 2. 把 .mat 中 URP 不支持的着色器（Standard / Legacy / 丢失的着色器 / 编译失败）换成 URP Lit，迁移贴图、颜色、法线、金属度、自发光、透明 / 镂空设置；
    /// 3. 检查场景和模型预制体里仍然不支持的材质，输出到控制台（点日志可定位）。
    /// 可重复执行。
    /// </summary>
    public static class PinkMaterialFixer
    {
        private static readonly string[] SearchFolders = { "Assets" };

        [MenuItem("Tools/MajorHazard/Fix Pink Materials (URP)")]
        public static void FixAll()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                if (!EditorUtility.DisplayDialog("修复粉色材质", "当前没有启用 URP（Graphics Settings 里渲染管线为空）。\n请先执行 Tools/MajorHazard/Setup Mobile Scene。", "仍然继续", "取消"))
                    return;
            }

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                EditorUtility.DisplayDialog("修复粉色材质", "找不到 Universal Render Pipeline/Lit，请确认已安装 URP 包。", "好");
                return;
            }

            var reimported = ReimportModels();
            var converted = ConvertMaterials(lit);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var remaining = ReportRemaining();

            EditorUtility.DisplayDialog("修复粉色材质",
                $"重新导入模型 {reimported} 个\n转换材质 {converted} 个\n" +
                (remaining == 0 ? "未发现不支持的材质。" : $"仍有 {remaining} 个材质不支持，详见控制台。"), "好");
        }

        /// <summary>重新导入使用内嵌材质的模型，内嵌材质会按当前（URP）管线重新生成。</summary>
        private static int ReimportModels()
        {
            var paths = AssetDatabase.FindAssets("t:Model", SearchFolders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith("Assets/") && AssetImporter.GetAtPath(p) is ModelImporter importer
                                                    && importer.materialImportMode != ModelImporterMaterialImportMode.None
                                                    && HasUnsupportedEmbedded(p))
                .ToList();
            if (paths.Count == 0)
                return 0;

            try
            {
                AssetDatabase.StartAssetEditing();
                for (var i = 0; i < paths.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("重新导入模型", paths[i], (float)i / paths.Count))
                        break;
                    AssetDatabase.ImportAsset(paths[i], ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }
            return paths.Count;
        }

        private static bool HasUnsupportedEmbedded(string modelPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().Any(IsUnsupported);
        }

        /// <summary>把工程里不被 URP 支持的 .mat 换成 URP Lit。</summary>
        private static int ConvertMaterials(Shader lit)
        {
            var count = 0;
            var paths = AssetDatabase.FindAssets("t:Material", SearchFolders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".mat"))
                .ToList();
            for (var i = 0; i < paths.Count; i++)
            {
                EditorUtility.DisplayProgressBar("转换材质", paths[i], (float)i / paths.Count);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(paths[i]);
                if (mat == null || !IsUnsupported(mat))
                    continue;
                Convert(mat, lit);
                EditorUtility.SetDirty(mat);
                Debug.Log($"[{nameof(PinkMaterialFixer)}] 已转换为 URP Lit：{paths[i]}", mat);
                count++;
            }
            EditorUtility.ClearProgressBar();
            return count;
        }

        /// <summary>着色器丢失、编译失败，或是内置管线专用着色器。</summary>
        private static bool IsUnsupported(Material mat)
        {
            var shader = mat.shader;
            if (shader == null || !shader.isSupported || shader.name == "Hidden/InternalErrorShader")
                return true;
            if (ShaderUtil.ShaderHasError(shader))
                return true;
            var name = shader.name;
            return name == "Standard" || name == "Standard (Specular setup)" || name.StartsWith("Legacy Shaders/")
                   || name.StartsWith("Mobile/") || name.StartsWith("Nature/") || name == "Diffuse" || name == "Bumped Diffuse"
                   || name == "Specular" || name == "Bumped Specular" || name.StartsWith("Autodesk Interactive");
        }

        /// <summary>
        /// 内置 Standard / Legacy → URP Lit 的属性迁移（与 URP 自带升级器的映射一致的常用子集）。
        /// </summary>
        private static void Convert(Material mat, Shader lit)
        {
            // 换着色器前先读出旧属性
            var mainTex = GetTex(mat, "_MainTex");
            var mainScale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
            var mainOffset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
            var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            var bump = GetTex(mat, "_BumpMap");
            var bumpScale = GetFloat(mat, "_BumpScale", 1f);
            var metallicMap = GetTex(mat, "_MetallicGlossMap");
            var metallic = GetFloat(mat, "_Metallic", 0f);
            var smoothness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness")
                : mat.HasProperty("_Shininess") ? Mathf.Clamp01(mat.GetFloat("_Shininess")) : 0.3f;
            var occlusion = GetTex(mat, "_OcclusionMap");
            var emissionMap = GetTex(mat, "_EmissionMap");
            var emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
            var emissive = mat.IsKeywordEnabled("_EMISSION") || emissionMap != null && emissionColor.maxColorComponent > 0.01f;
            var cutoff = GetFloat(mat, "_Cutoff", 0.5f);
            var shaderName = mat.shader != null ? mat.shader.name : "";
            // Standard 的 _Mode：0 不透明 / 1 镂空 / 2 渐隐 / 3 透明；Legacy 按着色器名判断
            var mode = mat.HasProperty("_Mode") ? Mathf.RoundToInt(mat.GetFloat("_Mode"))
                : shaderName.Contains("Cutout") ? 1 : shaderName.Contains("Transparent") ? 3 : 0;

            mat.shader = lit;

            mat.SetTexture("_BaseMap", mainTex);
            mat.SetTextureScale("_BaseMap", mainScale);
            mat.SetTextureOffset("_BaseMap", mainOffset);
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BumpMap", bump);
            mat.SetFloat("_BumpScale", bumpScale);
            mat.SetTexture("_MetallicGlossMap", metallicMap);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetTexture("_OcclusionMap", occlusion);
            mat.SetTexture("_EmissionMap", emissionMap);
            mat.SetColor("_EmissionColor", emissionColor);
            SetKeyword(mat, "_NORMALMAP", bump != null);
            SetKeyword(mat, "_METALLICSPECGLOSSMAP", metallicMap != null);
            SetKeyword(mat, "_OCCLUSIONMAP", occlusion != null);
            SetKeyword(mat, "_EMISSION", emissive);
            mat.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            // 表面类型
            var transparent = mode >= 2;
            var cutout = mode == 1;
            mat.SetFloat("_Surface", transparent ? 1f : 0f);
            mat.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            mat.SetFloat("_Cutoff", cutoff);
            mat.SetFloat("_Blend", 0f); // Alpha 混合
            SetKeyword(mat, "_ALPHATEST_ON", cutout);
            SetKeyword(mat, "_SURFACE_TYPE_TRANSPARENT", transparent);
            SetKeyword(mat, "_ALPHAPREMULTIPLY_ON", false);
            if (transparent)
            {
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                mat.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                mat.renderQueue = cutout ? (int)RenderQueue.AlphaTest : -1; // -1 = 用着色器默认队列
                mat.SetShaderPassEnabled("ShadowCaster", true);
            }
        }

        /// <summary>
        /// 检查当前场景和工程里的预制体 / 模型中仍不支持的材质，逐个输出日志。返回数量。
        /// </summary>
        private static int ReportRemaining()
        {
            var bad = new HashSet<Material>();

            foreach (var renderer in Object.FindObjectsOfType<Renderer>(true))
                foreach (var mat in renderer.sharedMaterials)
                    if (mat != null && IsUnsupported(mat) && bad.Add(mat))
                        Debug.LogWarning($"[{nameof(PinkMaterialFixer)}] 场景物体 {renderer.name} 的材质 {mat.name}（{mat.shader?.name}）仍不支持", renderer);

            foreach (var path in AssetDatabase.FindAssets("t:Prefab t:Model", SearchFolders).Select(AssetDatabase.GUIDToAssetPath).Distinct())
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null)
                    continue;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    foreach (var mat in renderer.sharedMaterials)
                        if (mat != null && IsUnsupported(mat) && bad.Add(mat))
                            Debug.LogWarning($"[{nameof(PinkMaterialFixer)}] {path} / {renderer.name} 的材质 {mat.name}（{mat.shader?.name}，{AssetDatabase.GetAssetPath(mat)}）仍不支持", mat);
            }

            return bad.Count;
        }

        private static Texture GetTex(Material mat, string name) => mat.HasProperty(name) ? mat.GetTexture(name) : null;

        private static float GetFloat(Material mat, string name, float fallback) => mat.HasProperty(name) ? mat.GetFloat(name) : fallback;

        private static void SetKeyword(Material mat, string keyword, bool on)
        {
            if (on)
                mat.EnableKeyword(keyword);
            else
                mat.DisableKeyword(keyword);
        }
    }
}
