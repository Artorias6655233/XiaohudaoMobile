using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LZW.Module.MajorHazard.Scripts
{
    /// <summary>
    /// 读接口 JSON 的小工具。平台各子系统的返回结构不统一（body.data / result.records / result …），
    /// 部分接口（报警快照、设备列表）没有抓到样例，所以字段都按「候选键名取第一个非空值」的方式读取。
    /// </summary>
    public static class HazardJson
    {
        public static JToken Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                return JToken.Parse(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[{nameof(HazardJson)}] 解析失败: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 按顺序尝试路径，返回第一个数组中的对象；路径为空字符串表示根节点。
        /// </summary>
        public static List<JObject> FindList(JToken root, params string[] paths)
        {
            var result = new List<JObject>();
            if (root == null)
                return result;

            foreach (var path in paths)
            {
                var token = string.IsNullOrEmpty(path) ? root : root.SelectToken(path);
                if (token is not JArray array)
                    continue;
                foreach (var item in array)
                    if (item is JObject obj)
                        result.Add(obj);
                return result;
            }
            return result;
        }

        /// <summary>
        /// 取第一个非空的字段值（字符串形式）。
        /// </summary>
        public static string Str(JToken item, params string[] keys)
        {
            if (item == null)
                return null;
            foreach (var key in keys)
            {
                var token = item.SelectToken(key);
                if (token == null || token.Type == JTokenType.Null)
                    continue;
                var value = token.ToString().Trim();
                if (value.Length > 0)
                    return value;
            }
            return null;
        }

        public static bool TryDouble(JToken item, out double value, params string[] keys)
        {
            value = 0;
            var text = Str(item, keys);
            return text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// 经纬度有效（不是 0,0 占位，且在中国境内的大致范围）。
        /// </summary>
        public static bool IsValidLonLat(double lon, double lat)
        {
            return lon > 70 && lon < 140 && lat > 3 && lat < 55;
        }

        /// <summary>
        /// 接口是否返回成功：double-prevention 用 status = "success"，门户 / jeecg 用 success = true。
        /// </summary>
        public static bool IsSuccess(JToken root)
        {
            if (root == null)
                return false;
            if (root.SelectToken("success") is { Type: JTokenType.Boolean } success)
                return success.Value<bool>();
            return Str(root, "status") == "success";
        }

        public static string Message(JToken root)
        {
            return Str(root, "message", "msg") ?? "";
        }

        /// <summary>
        /// "#a3001480"（RGBA）/ "#ff0000"（RGB）→ Color，忽略透明度（标注颜色是给地图半透明填充用的）。
        /// </summary>
        public static bool TryColor(string hex, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(hex))
                return false;
            hex = hex.Trim();
            if (hex.Length >= 7)
                hex = hex.Substring(0, 7);
            if (!ColorUtility.TryParseHtmlString(hex, out color))
                return false;
            color.a = 1f;
            return true;
        }
    }
}
