using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LZW.Module.MajorHazard.Scripts
{
    /// <summary>
    /// 一张图标注（设备 / 企业坐标）的共享缓存，四个功能面板都用它定位，避免重复拉取 500 多条标注。
    /// 平台的报警设备、风险点大多不带坐标，靠这里的索引换算：
    /// 设备编码 / 设备 id → 设备标注；企业组织编码（sysOrgCode）/ 企业名 → 企业位置。
    /// </summary>
    public static class HazardCache
    {
        /// <summary>
        /// 缓存有效期（秒），标注很少变化。
        /// </summary>
        public const float TagsLifetime = 600f;

        public static IReadOnlyList<HazardTag> Tags => _tags;

        public static bool HasTags => _tags.Count > 0;

        private static readonly List<HazardTag> _tags = new List<HazardTag>();
        private static readonly Dictionary<string, HazardTag> TagByDevice = new Dictionary<string, HazardTag>();
        private static readonly Dictionary<string, HazardTag> TagByCompanyOrg = new Dictionary<string, HazardTag>();
        private static readonly Dictionary<string, HazardTag> TagByCompanyName = new Dictionary<string, HazardTag>();

        private static float _loadedAt = float.NegativeInfinity;
        private static float _loadingSince = float.NegativeInfinity;

        // 宿主物体被停用时协程直接中止、不会走到清除标记那一步，所以加载标记按时间过期（登录 + 请求各一次超时）
        private static bool IsLoading => Time.realtimeSinceStartup - _loadingSince < HazardApi.Timeout * 2 + 5;

        /// <summary>
        /// 加载标注（未过期则直接返回缓存）；已有请求在进行时等待它完成。完成后回调是否有数据。
        /// </summary>
        public static IEnumerator LoadTags(bool force, System.Action<bool> done = null)
        {
            if (IsLoading)
            {
                while (IsLoading)
                    yield return null;
                done?.Invoke(HasTags);
                yield break;
            }

            if (!force && HasTags && Time.realtimeSinceStartup - _loadedAt < TagsLifetime)
            {
                done?.Invoke(true);
                yield break;
            }

            _loadingSince = Time.realtimeSinceStartup;
            List<HazardTag> loaded = null;
            yield return HazardApi.FindTags(t => loaded = t);
            _loadingSince = float.NegativeInfinity;

            if (loaded != null)
            {
                SetTags(loaded);
                _loadedAt = Time.realtimeSinceStartup;
            }
            done?.Invoke(HasTags);
        }

        public static HazardTag FindByDevice(string deviceCodeOrId)
        {
            return !string.IsNullOrEmpty(deviceCodeOrId) && TagByDevice.TryGetValue(deviceCodeOrId, out var tag) ? tag : null;
        }

        /// <summary>
        /// 按企业组织编码（风险点的 riskUnitDeptId / topOrgCode，设备的 sysOrgCode）或企业名找企业位置。
        /// </summary>
        public static HazardTag FindCompany(string orgCode, string companyName)
        {
            if (!string.IsNullOrEmpty(orgCode) && TagByCompanyOrg.TryGetValue(orgCode, out var tag))
                return tag;
            if (!string.IsNullOrEmpty(companyName) && TagByCompanyName.TryGetValue(Normalize(companyName), out tag))
                return tag;
            return null;
        }

        private static void SetTags(List<HazardTag> tags)
        {
            _tags.Clear();
            _tags.AddRange(tags);
            TagByDevice.Clear();
            TagByCompanyOrg.Clear();
            TagByCompanyName.Clear();

            foreach (var tag in tags)
            {
                if (tag.tagType == HazardTag.TypeCompany && !string.IsNullOrEmpty(tag.remarks))
                    TagByCompanyName[Normalize(tag.remarks)] = tag; // 企业标注优先于设备标注

                foreach (var id in tag.relationIds)
                    TagByDevice.TryAdd(id, tag);

                foreach (var device in tag.devices)
                {
                    if (!string.IsNullOrEmpty(device.deviceCode))
                        TagByDevice.TryAdd(device.deviceCode, tag);
                    if (!string.IsNullOrEmpty(device.deviceId))
                        TagByDevice.TryAdd(device.deviceId, tag);
                    if (!string.IsNullOrEmpty(device.sysOrgCode))
                        TagByCompanyOrg.TryAdd(device.sysOrgCode, tag);
                    if (!string.IsNullOrEmpty(device.companyName))
                        TagByCompanyName.TryAdd(Normalize(device.companyName), tag);
                }
            }
        }

        private static string Normalize(string name)
        {
            return name.Replace("（", "(").Replace("）", ")").Replace(" ", "").Trim();
        }
    }
}
