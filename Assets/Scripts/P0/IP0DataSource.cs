using System;
using System.Collections;
using System.Collections.Generic;

public interface IP0DataSource : IP0AnnotationSource
{
    bool IsDemo { get; }

    /// <summary>加载是否成功完成。失败时为 false，调用方据此走空态而不是读到半份数据。</summary>
    bool Ready { get; }

    /// <summary>
    /// 同步加载。只有演示数据包能这么干（它读的是随包 TextAsset），
    /// 网络数据源会直接抛——调用方一律走 <see cref="LoadAsync"/>。
    /// 保留这个重载是给 Editor 校验用的（见 P0MockDataChecks）。
    /// </summary>
    void Load();

    /// <summary>运行时的统一加载入口，两种数据源都实现。</summary>
    IEnumerator LoadAsync();

    IReadOnlyList<P0Company> GetCompanies();
    IReadOnlyList<P0TankArea> GetTankAreas(string companyId);
    IReadOnlyList<P0AreaSnapshot> GetAreaSnapshots(
        string companyId,
        int frameIndex,
        DateTimeOffset now);
    IReadOnlyList<P0HazardSource> GetHazards(string companyId);
    IReadOnlyList<P0ResponsiblePerson> GetResponsiblePeople(string hazardId);
}
