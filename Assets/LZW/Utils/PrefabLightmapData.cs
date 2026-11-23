using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class PrefabLightmapData : MonoBehaviour
{
    [SerializeField]
    private RendererInfo[] m_RendererInfo;

    [SerializeField]
    private TerrainInfo[] m_TerrainInfo;

    [SerializeField]
    private TextureInfo[] m_TextureInfo;

    private bool hasLoaded = false;

    private void OnEnable()
    {
        if (!hasLoaded) {
            StartCoroutine(DelayCall(() => {
                BindLightmap();
            }, 0.22f));
        }
    }

    IEnumerator DelayCall(UnityAction action, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        action?.Invoke();
    }

    [ContextMenu("加载光照贴图")]
    public void BindLightmap()
    {
        if (m_RendererInfo == null || m_RendererInfo.Length == 0)
            return;

        LightmapData[] lightmaps = LightmapSettings.lightmaps;
        LightmapData[] combinedLightmaps = new LightmapData[lightmaps.Length + m_TextureInfo.Length];

        lightmaps.CopyTo(combinedLightmaps, 0);
        for (int i = 0; i < m_TextureInfo.Length; i++)
        {
            combinedLightmaps[i + lightmaps.Length] = new LightmapData();
            combinedLightmaps[i + lightmaps.Length].lightmapColor = m_TextureInfo[i].LightmapColor;
            combinedLightmaps[i + lightmaps.Length].lightmapDir = m_TextureInfo[i].LightmapDir;
            combinedLightmaps[i + lightmaps.Length].shadowMask = m_TextureInfo[i].Shadowmask;
        }

        ApplyRendererInfo(m_RendererInfo, lightmaps.Length);
        if (m_TerrainInfo != null && m_TerrainInfo.Length > 0)
            ApplyTerrainInfo(m_TerrainInfo, lightmaps.Length);
        LightmapSettings.lightmaps = combinedLightmaps;
        hasLoaded = true;
    }

    public void BindLightmapInBuild()
    {
        BindLightmap();
        Debug.Log($"<color=#ff00ff>Added light map.</color>");
    }


    private void ApplyRendererInfo(RendererInfo[] infos, int lightmapOffsetIndex)
    {
        for (int i = 0; i < infos.Length; i++)
        {
            RendererInfo info = infos[i];
            info.renderer.lightmapIndex = info.lightmapIndex + lightmapOffsetIndex;
            info.renderer.lightmapScaleOffset = info.lightmapOffsetScale;
        }
    }

    private void ApplyTerrainInfo(TerrainInfo[] infos, int lightmapOffsetIndex)
    {
        for (int i = 0; i < infos.Length; i++)
        {
            TerrainInfo info = infos[i];
            info.terrain.lightmapIndex = info.lightmapIndex + lightmapOffsetIndex;
            info.terrain.lightmapScaleOffset = info.lightmapOffsetScale;
        }
    }

    [Serializable]
    private struct RendererInfo
    {
        public Renderer renderer;
        public int lightmapIndex;
        public Vector4 lightmapOffsetScale;
    }

    [Serializable]
    private struct TerrainInfo
    {
        public Terrain terrain;
        public int lightmapIndex;
        public Vector4 lightmapOffsetScale;
    }

    [Serializable]
    private struct TextureInfo
    {
        public Texture2D LightmapColor;
        public Texture2D LightmapDir;
        public Texture2D Shadowmask;
    }

#if UNITY_EDITOR

    [ContextMenu("保存光照贴图")]
    private void SaveLightMaping()
    {
        List<RendererInfo> rendererInfos = new List<RendererInfo>();
        List<TerrainInfo> terrainInfos = new List<TerrainInfo>();

        List<TextureInfo> lightmapscolor = new List<TextureInfo>();
        GenerateLightmapRendererInfo(gameObject, rendererInfos, lightmapscolor);
        GenerateLightmapTerrainInfo(gameObject, terrainInfos, lightmapscolor);

        m_RendererInfo = rendererInfos.ToArray();
        m_TerrainInfo = terrainInfos.ToArray();
        m_TextureInfo = lightmapscolor.ToArray();

        EditorUtility.SetDirty(gameObject);
        // var targetPrefab = UnityEditor.PrefabUtility.GetPrefabParent(gameObject) as GameObject;
        // if (targetPrefab != null)
        // {
        //     //UnityEditor.Prefab
        //     UnityEditor.PrefabUtility.ReplacePrefab(gameObject, targetPrefab);
        // }
    }

    [ContextMenu("清空光照贴图")]
    public void ClearLightmapsInner()
    {
        LightmapSettings.lightmaps = null;
        Debug.Log($"<color=#ff00ff>Clear light maps successfully.</color>");
    }

    public static void ClearLightmaps()
    {
        LightmapSettings.lightmaps = null;
        Debug.Log($"<color=#ff00ff>Clear light maps successfully.</color>");
    }

    private void GenerateLightmapRendererInfo(GameObject root, List<RendererInfo> rendererInfos, List<TextureInfo> lightmaps)
    {
        MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer rend in renderers)
        {
            if (rend.receiveGI == ReceiveGI.LightProbes)
            {
                continue;
            }
            if (rend.lightmapIndex != -1)
            {
                RendererInfo info = new RendererInfo();
                info.renderer = rend;
                info.lightmapOffsetScale = rend.lightmapScaleOffset;

                int lightmapIndex = rend.lightmapIndex;
                Texture2D lightmap = default;
                LightmapData lightmapData = LightmapSettings.lightmaps[lightmapIndex];
                lightmap = lightmapData.lightmapColor;

                info.lightmapIndex = lightmaps.FindIndex(l => l.LightmapColor == lightmap);
                if (info.lightmapIndex == -1)
                {
                    info.lightmapIndex = lightmaps.Count;
                    lightmaps.Add(new TextureInfo
                    {
                        LightmapColor = lightmap,
                        LightmapDir = lightmapData.lightmapDir,
                        Shadowmask = lightmapData.shadowMask
                    });
                }

                rendererInfos.Add(info);
            }
        }
    }

    private void GenerateLightmapTerrainInfo(GameObject root, List<TerrainInfo> terrainInfos, List<TextureInfo> lightmaps)
    {
        Terrain[] terrains = root.GetComponentsInChildren<Terrain>();
        foreach (Terrain ter in terrains)
        {
            if (ter.lightmapIndex != -1)
            {
                TerrainInfo info = new TerrainInfo();
                info.terrain = ter;
                info.lightmapOffsetScale = ter.lightmapScaleOffset;
                int lightmapIndex = ter.lightmapIndex;
                Texture2D lightmap = default;
                LightmapData lightmapData = LightmapSettings.lightmaps[lightmapIndex];
                lightmap = lightmapData.lightmapColor;

                info.lightmapIndex = lightmaps.FindIndex(l => l.LightmapColor == lightmap);
                if (info.lightmapIndex == -1)
                {
                    info.lightmapIndex = lightmaps.Count;
                    lightmaps.Add(new TextureInfo
                    {
                        LightmapColor = lightmap,
                        LightmapDir = lightmapData.lightmapDir,
                        Shadowmask = lightmapData.shadowMask
                    });
                }

                terrainInfos.Add(info);
            }
        }
    }
#endif
}