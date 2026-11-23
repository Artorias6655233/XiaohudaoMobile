using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

[Serializable]
public sealed class P0AnnotationDocument
{
    public List<P0Annotation> items = new List<P0Annotation>();
}

public sealed class P0AnnotationStore
{
    public const int MaximumLength = 500;

    private readonly string path;
    private readonly string currentAuthorId;
    private readonly string currentAuthorName;
    private readonly List<P0Annotation> builtIn;
    private readonly List<P0Annotation> local = new List<P0Annotation>();
    private bool loaded;

    public bool RecoveredCorruptFile { get; private set; }

    public P0AnnotationStore(
        string path,
        string currentAuthorId,
        string currentAuthorName,
        IEnumerable<P0Annotation> builtIn = null)
    {
        this.path = path ?? throw new ArgumentNullException(nameof(path));
        this.currentAuthorId = currentAuthorId ?? throw new ArgumentNullException(nameof(currentAuthorId));
        this.currentAuthorName = currentAuthorName ?? string.Empty;
        this.builtIn = (builtIn ?? Enumerable.Empty<P0Annotation>()).Select(Clone).ToList();
    }

    public IReadOnlyList<P0Annotation> Load(string hazardId)
    {
        EnsureLoaded();
        return new ReadOnlyCollection<P0Annotation>(builtIn.Concat(local)
            .Where(item => item.targetType == "hazardSource" && item.targetId == hazardId)
            .OrderByDescending(item => item.updatedAt)
            .Select(Clone)
            .ToList());
    }

    public P0Annotation Add(string hazardId, string content)
    {
        EnsureLoaded();
        string normalized = ValidateContent(content);
        if (string.IsNullOrWhiteSpace(hazardId))
            throw new ArgumentException("批注目标不能为空", nameof(hazardId));

        DateTimeOffset now = DateTimeOffset.Now;
        var item = new P0Annotation
        {
            annotationId = Guid.NewGuid().ToString("N"),
            targetType = "hazardSource",
            targetId = hazardId,
            authorId = currentAuthorId,
            authorName = currentAuthorName,
            content = normalized,
            createdAt = now,
            updatedAt = now,
            version = 1
        };
        local.Add(item);
        SaveOrRollback(() => local.Remove(item));
        return Clone(item);
    }

    public P0Annotation Update(string annotationId, int expectedVersion, string content)
    {
        EnsureLoaded();
        string normalized = ValidateContent(content);
        P0Annotation item = local.FirstOrDefault(candidate => candidate.annotationId == annotationId);
        if (item == null)
        {
            if (builtIn.Any(candidate => candidate.annotationId == annotationId))
                throw new UnauthorizedAccessException("只能编辑当前用户新增的批注");
            throw new KeyNotFoundException("批注不存在");
        }
        if (!string.Equals(item.authorId, currentAuthorId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("只能编辑本人批注");
        if (item.version != expectedVersion)
            throw new InvalidOperationException("批注版本已变化，请刷新后重试");

        string oldContent = item.content;
        DateTimeOffset oldUpdatedAt = item.updatedAt;
        int oldVersion = item.version;
        item.content = normalized;
        item.updatedAt = DateTimeOffset.Now;
        item.version++;
        SaveOrRollback(() =>
        {
            item.content = oldContent;
            item.updatedAt = oldUpdatedAt;
            item.version = oldVersion;
        });
        return Clone(item);
    }

    private void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        local.Clear();
        if (!File.Exists(path)) return;

        try
        {
            P0AnnotationDocument document = JsonConvert.DeserializeObject<P0AnnotationDocument>(
                File.ReadAllText(path));
            if (document?.items == null) throw new InvalidDataException("批注文件结构不完整");
            local.AddRange(document.items.Select(Clone));
        }
        catch (Exception)
        {
            string brokenPath = path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
            File.Move(path, brokenPath);
            RecoveredCorruptFile = true;
            local.Clear();
        }
    }

    private void SaveOrRollback(Action rollback)
    {
        try
        {
            Save();
        }
        catch
        {
            rollback();
            throw;
        }
    }

    private void Save()
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporaryPath = path + ".tmp";
        string backupPath = path + ".backup";
        try
        {
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(
                new P0AnnotationDocument { items = local.Select(Clone).ToList() },
                Formatting.Indented));
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, backupPath);
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temporaryPath, path, true);
                    File.Delete(temporaryPath);
                }
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string ValidateContent(string content)
    {
        string normalized = (content ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new ArgumentException("批注内容不能为空", nameof(content));
        if (normalized.Length > MaximumLength)
            throw new ArgumentException("批注内容不能超过 500 字", nameof(content));
        return normalized;
    }

    private static P0Annotation Clone(P0Annotation source)
    {
        return new P0Annotation
        {
            annotationId = source.annotationId,
            targetType = source.targetType,
            targetId = source.targetId,
            authorId = source.authorId,
            authorName = source.authorName,
            content = source.content,
            createdAt = source.createdAt,
            updatedAt = source.updatedAt,
            version = source.version
        };
    }
}
