using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 批注的读写边界。
///
/// 【为什么要从 IP0DataSource 里拆出来】批注入口已经挪到「重大风险管控」的罐体列表上，
/// 那一页跑的是真实接口数据，而 P0DataSourceFactory 对 Network 模式直接 throw
/// （正式接口没接）。批注面板只需要这三个方法，却被整个演示数据源绑着，
/// 在真实数据下根本构造不出来。
///
/// 拆开之后：演示页仍然用 MockP0DataSource（它本来就实现了这三个方法），
/// 罐体列表用 <see cref="LocalAnnotationSource"/> 直连本地存储，互不依赖。
/// </summary>
public interface IP0AnnotationSource
{
    IReadOnlyList<P0Annotation> GetAnnotations(string targetId);
    P0Annotation AddAnnotation(string targetId, string content);
    P0Annotation UpdateAnnotation(string annotationId, int expectedVersion, string content);
}

/// <summary>
/// 不依赖演示数据包的批注源，直接落 <see cref="P0AnnotationStore"/>。
///
/// targetId 用罐的 <see cref="Tank.MarkerKey"/>（即 equipCode，实测 264/264 唯一），
/// 和演示页那套 hazardId 是两个互不重叠的键空间，所以存在同一个文件里也不会串。
/// 存储位置仍是 persistentDataPath，换台眼镜看不到——后端批注接口还没有，
/// 这一点在面板角标上写明了。
/// </summary>
public sealed class LocalAnnotationSource : IP0AnnotationSource
{
    private readonly P0AnnotationStore store;

    public LocalAnnotationSource()
    {
        store = new P0AnnotationStore(
            System.IO.Path.Combine(Application.persistentDataPath, "tank_annotations.json"),
            "mock-user-self",
            "当前用户");
    }

    public IReadOnlyList<P0Annotation> GetAnnotations(string targetId) => store.Load(targetId);

    public P0Annotation AddAnnotation(string targetId, string content) =>
        store.Add(targetId, content);

    public P0Annotation UpdateAnnotation(string annotationId, int expectedVersion, string content) =>
        store.Update(annotationId, expectedVersion, content);
}
