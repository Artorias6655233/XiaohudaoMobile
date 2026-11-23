using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// 把同一家企业的储罐聚成一个沙盘图标。
///
/// 起因：tank/list 的经纬度是【企业级】的，同一家企业所有罐共用一个点。
/// 2026-09-21 实测——福达石化 30 个罐全在 (113.552026, 22.835008)，
/// 鸿业储运 18 个全在 (113.52497, 22.80169)，建滔电子 9 个全在 (113.559059, 22.820264)。
/// 一罐一图标的话它们严丝合缝地叠在一起，射线只命中最上面那个，
/// 用户点十次都是同一个罐，其余的永远选不到。
///
/// 所以同企业只放一个图标，落在【该企业罐最密集的那个坐标】上，其余罐走卡片里的列表选。
/// 不按半径把它们散开摆：那是在编造罐的位置，而现场人员会照着图标去找罐。
/// </summary>
public static class TankGrouping
{
    /// <summary>沙盘上的一个图标：一个坐标点，以及落在这个点上的全部罐。</summary>
    public sealed class Marker
    {
        public double longitude;
        public double latitude;

        /// <summary>挂图标的那个罐，它的 MarkerKey 就是图标的 tankID。</summary>
        public Tank representative;

        /// <summary>落在这个点上的罐数。1 表示这个罐本来就有自己的位置，没和别人挤。</summary>
        public int tankCount;
    }

    /// <summary>一家企业：沙盘上的若干个图标 + 卡片列表要用的全部罐。</summary>
    public sealed class Group
    {
        /// <summary>企业组织编码（tank.sysOrgCode，已 Trim）。</summary>
        public string sysOrgCode;

        /// <summary>按罐数从多到少排；并列时保持先出现的在前。</summary>
        public List<Marker> markers = new List<Marker>();

        /// <summary>本企业全部可定位的罐，按罐名排序，供卡片列表用。</summary>
        public List<Tank> tanks = new List<Tank>();

        /// <summary>最显眼的那个落点（罐最密集的坐标）。沙盘上就只放这一个图标。</summary>
        public Marker Primary => markers.Count > 0 ? markers[0] : null;
    }

    /// <summary>
    /// 沙盘上要激活的图标：每家企业【只有一个】，落在 <see cref="Group.Primary"/> 上。
    ///
    /// 即便某家企业的罐各有各的真实坐标（华隆 6 个、建滔环保 5 个就是这样），也一样只放
    /// 一个图标：入口统一成"点图标 → 看列表 → 选罐"，用户不必先分辨这家是散的还是叠的。
    /// 罐的真实坐标没有丢，仍在 <see cref="Group.markers"/> 里，将来要摊开随时能改回来。
    /// </summary>
    public static HashSet<string> MarkerKeys(IEnumerable<Group> groups)
    {
        var keys = new HashSet<string>();
        foreach (Group group in groups)
        {
            Marker primary = group == null ? null : group.Primary;
            if (primary != null && primary.representative != null)
                keys.Add(primary.representative.MarkerKey);
        }
        return keys;
    }

    /// <summary>
    /// 坐标能不能用。与两个宿主面板原有的判断保持一致：解析得出、有限、在值域内、非零。
    /// 注意 (1, 1) 是通过的——实测碧辟油品 79 个罐就是这个坐标，虽然落在几内亚湾，
    /// 但那是后端给的真值，本函数不替业务判断"合不合理"。
    /// </summary>
    public static bool TryCoordinate(Tank tank, out double longitude, out double latitude)
    {
        longitude = 0;
        latitude = 0;
        if (tank == null) return false;

        if (!double.TryParse(tank.longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude)
            || !double.TryParse(tank.latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude))
            return false;

        if (double.IsNaN(longitude) || double.IsNaN(latitude)
            || double.IsInfinity(longitude) || double.IsInfinity(latitude)) return false;

        return System.Math.Abs(longitude) <= 180 && System.Math.Abs(latitude) <= 90
            && longitude != 0 && latitude != 0;
    }

    /// <summary>
    /// 按 sysOrgCode 聚合。坐标不可用、没有 MarkerKey、没有组织编码的罐直接丢掉——
    /// 它们本来也上不了沙盘。
    /// </summary>
    public static List<Group> Build(IEnumerable<Tank> tanks)
    {
        var byOrg = new Dictionary<string, List<Tank>>();
        var order = new List<string>();

        foreach (Tank tank in tanks)
        {
            if (tank == null || tank.MarkerKey == null) continue;

            string org = tank.sysOrgCode == null ? null : tank.sysOrgCode.Trim();
            if (string.IsNullOrEmpty(org)) continue;

            if (!TryCoordinate(tank, out _, out _)) continue;

            if (!byOrg.TryGetValue(org, out var list))
            {
                list = new List<Tank>();
                byOrg[org] = list;
                order.Add(org);
            }
            list.Add(tank);
        }

        var groups = new List<Group>(order.Count);
        foreach (string org in order) groups.Add(Pack(org, byOrg[org]));
        return groups;
    }

    private static Group Pack(string org, List<Tank> tanks)
    {
        // 按坐标分桶。【不能】无脑一企业一图标：建滔环保那 5 个罐各有各的真实坐标，
        // 压成一个点等于把仅有的罐级定位丢掉。只有真正重叠的才合并。
        var byCoord = new Dictionary<string, Marker>();
        var order = new List<string>();

        foreach (Tank tank in tanks)
        {
            string key = tank.longitude + "," + tank.latitude;
            if (!byCoord.TryGetValue(key, out var marker))
            {
                marker = new Marker { representative = tank };
                TryCoordinate(tank, out marker.longitude, out marker.latitude);
                byCoord[key] = marker;
                order.Add(key);
            }
            marker.tankCount++;
        }

        var group = new Group { sysOrgCode = org };
        group.tanks.AddRange(tanks);

        // 罐多的排前面，Primary 就是"最显眼的那个重叠点"。
        // 并列时保持先出现的在前：同一份目录每次算出来的顺序必须一样，
        // 否则图标会在两个点之间来回跳。
        foreach (string key in order) group.markers.Add(byCoord[key]);
        StableSortByCountDesc(group.markers);

        // 列表按罐名排，卡片上看着有序；罐名为空的排最后，用设备码兜底
        group.tanks.Sort((a, b) => string.CompareOrdinal(Name(a), Name(b)));
        return group;
    }

    /// <summary>List.Sort 不保证稳定，这里用插入排序自己保证。图标少，代价可以忽略。</summary>
    private static void StableSortByCountDesc(List<Marker> markers)
    {
        for (int i = 1; i < markers.Count; i++)
        {
            Marker current = markers[i];
            int j = i - 1;
            while (j >= 0 && markers[j].tankCount < current.tankCount)
            {
                markers[j + 1] = markers[j];
                j--;
            }
            markers[j + 1] = current;
        }
    }

    private static string Name(Tank tank)
    {
        return string.IsNullOrWhiteSpace(tank.tankName) ? "\uFFFF" + tank.MarkerKey : tank.tankName;
    }
}
