using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

[Serializable]
public sealed class InspectionAnnotationDocument
{
    public List<InspectionAnnotation> items = new List<InspectionAnnotation>();
}

/// <summary>Persists patrol-item annotations on this headset only.</summary>
public sealed class InspectionAnnotationStore
{
    private readonly string path;
    private readonly string currentAuthorId;
    private readonly string currentAuthorName;
    private readonly List<InspectionAnnotation> items = new List<InspectionAnnotation>();
    private bool loaded;

    public bool RecoveredCorruptFile { get; private set; }

    public InspectionAnnotationStore(string path, string currentAuthorId, string currentAuthorName)
    {
        this.path = path ?? throw new ArgumentNullException(nameof(path));
        this.currentAuthorId = currentAuthorId ?? throw new ArgumentNullException(nameof(currentAuthorId));
        this.currentAuthorName = currentAuthorName ?? string.Empty;
    }

    public List<InspectionAnnotation> Load(string patrolRecordId)
    {
        EnsureLoaded();
        return items.Where(item => item.patrolRecordId == patrolRecordId)
            .OrderByDescending(item => item.updated)
            .Select(CloneForCurrentUser)
            .ToList();
    }

    public InspectionAnnotation Add(string patrolRecordId, string content)
    {
        EnsureLoaded();
        string normalized = DoublePreventionRules.NormalizeContent(content);
        if (string.IsNullOrWhiteSpace(patrolRecordId))
            throw new ArgumentException("检查条目不能为空", nameof(patrolRecordId));

        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var item = new InspectionAnnotation
        {
            sid = Guid.NewGuid().ToString("N"),
            patrolRecordId = patrolRecordId,
            content = normalized,
            authorId = currentAuthorId,
            authorName = currentAuthorName,
            created = now,
            updated = now,
            version = 1,
            editable = true
        };
        items.Add(item);
        SaveOrRollback(() => items.Remove(item));
        return CloneForCurrentUser(item);
    }

    public InspectionAnnotation Update(string sid, int expectedVersion, string content)
    {
        EnsureLoaded();
        string normalized = DoublePreventionRules.NormalizeContent(content);
        InspectionAnnotation item = FindEditable(sid, expectedVersion);
        string oldContent = item.content;
        string oldUpdated = item.updated;
        int oldVersion = item.version;
        item.content = normalized;
        item.updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        item.version++;
        SaveOrRollback(() =>
        {
            item.content = oldContent;
            item.updated = oldUpdated;
            item.version = oldVersion;
        });
        return CloneForCurrentUser(item);
    }

    public void Delete(string sid, int expectedVersion)
    {
        EnsureLoaded();
        InspectionAnnotation item = FindEditable(sid, expectedVersion);
        int index = items.IndexOf(item);
        items.RemoveAt(index);
        SaveOrRollback(() => items.Insert(index, item));
    }

    private InspectionAnnotation FindEditable(string sid, int expectedVersion)
    {
        InspectionAnnotation item = items.FirstOrDefault(candidate => candidate.sid == sid);
        if (item == null) throw new KeyNotFoundException("批注不存在");
        if (!string.Equals(item.authorId, currentAuthorId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("只能修改或删除本机用户的批注");
        if (item.version != expectedVersion)
            throw new InvalidOperationException("批注版本已变化，请刷新后重试");
        return item;
    }

    private void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        if (!File.Exists(path)) return;
        try
        {
            InspectionAnnotationDocument document =
                JsonConvert.DeserializeObject<InspectionAnnotationDocument>(File.ReadAllText(path));
            if (document?.items == null) throw new InvalidDataException("批注文件结构不完整");
            items.AddRange(document.items.Select(CloneStored));
        }
        catch (Exception)
        {
            string brokenPath = path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
            File.Move(path, brokenPath);
            RecoveredCorruptFile = true;
            items.Clear();
        }
    }

    private void SaveOrRollback(Action rollback)
    {
        try { Save(); }
        catch { rollback(); throw; }
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
                new InspectionAnnotationDocument { items = items.Select(CloneStored).ToList() },
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
            else File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private InspectionAnnotation CloneForCurrentUser(InspectionAnnotation source)
    {
        InspectionAnnotation clone = CloneStored(source);
        clone.editable = string.Equals(source.authorId, currentAuthorId, StringComparison.Ordinal);
        return clone;
    }

    private static InspectionAnnotation CloneStored(InspectionAnnotation source)
    {
        return new InspectionAnnotation
        {
            sid = source.sid,
            patrolRecordId = source.patrolRecordId,
            content = source.content,
            authorId = source.authorId,
            authorName = source.authorName,
            created = source.created,
            updated = source.updated,
            version = source.version,
            editable = source.editable
        };
    }
}
