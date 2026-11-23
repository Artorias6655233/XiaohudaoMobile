using System;
using UnityEngine;

namespace LZW.Module.Map3DViewer.Scripts
{
    /// <summary>
    /// 三维地图上的一个 POI。位置用经纬度，由 <see cref="GeoMapper"/> 映射到模型上。
    /// </summary>
    [Serializable]
    public class Map3DPoiData
    {
        public string id;

        [Tooltip("标签上显示的名称")]
        public string name;

        [Tooltip("分类，如 应急物资 / 救援队伍 / 专家")]
        public string type;

        public double longitude;
        public double latitude;

        [Tooltip("离地高度（模型本地单位，XHD 中约等于米）")]
        public float height;

        [Tooltip("点击后弹窗显示的详情，可多行")]
        [TextArea]
        public string detail;

        public Color color = new Color(0.2f, 0.75f, 1f);

        /// <summary>
        /// 业务自定义数据，如接口返回的原始对象。
        /// </summary>
        [NonSerialized] public object userData;
    }
}
