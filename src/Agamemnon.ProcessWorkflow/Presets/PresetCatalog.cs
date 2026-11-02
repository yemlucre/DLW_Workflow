using System;
using System.Collections.Generic;
using System.Linq;

namespace Agamemnon.ProcessWorkflow;

/// <summary>预设目录接口：增/复制/改名/更新/删除/选择；名称唯一（区分大小写）；Revision 随任何变化递增。</summary>
public interface IPresetCatalog
{
    IReadOnlyList<ProcessingPreset> Presets { get; }
    /// <summary>当前选中的预设 Id；未选择为 null；删除选中预设后清除选择。</summary>
    Guid? SelectedId { get; }
    long Revision { get; }
    ProcessingPreset Add(ProcessingPreset preset);
    ProcessingPreset Copy(Guid sourceId, string newName);
    ProcessingPreset Rename(Guid id, string newName);
    ProcessingPreset Update(ProcessingPreset preset);
    void Remove(Guid id);
    void Select(Guid id);
}

/// <summary>内存预设目录（进程内）。持久化由宿主另行包装；本实现不做文件 IO。</summary>
public sealed class PresetCatalog : IPresetCatalog
{
    private readonly object _gate = new();
    private readonly List<ProcessingPreset> _presets = new();

    public IReadOnlyList<ProcessingPreset> Presets { get { lock (_gate) return _presets.ToArray(); } }
    public Guid? SelectedId { get { lock (_gate) return _selected; } }
    public long Revision { get { lock (_gate) return _revision; } }
    private Guid? _selected;
    private long _revision;

    public ProcessingPreset Add(ProcessingPreset preset)
    {
        var frozen = ProcessingPreset.Validate(preset);
        lock (_gate)
        {
            EnsureNameFree(frozen.Name, null);
            _presets.Add(frozen); _revision++;
            return frozen;
        }
    }

    public ProcessingPreset Copy(Guid sourceId, string newName)
    {
        lock (_gate)
        {
            var source = Find(sourceId);
            return Add(source with { Id = Guid.NewGuid(), Name = newName });
        }
    }

    public ProcessingPreset Rename(Guid id, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName.Length > 80) throw new ArgumentException("预设名称需为 1–80 字符。");
        lock (_gate)
        {
            var preset = Find(id);
            EnsureNameFree(newName, id);
            var renamed = preset with { Name = newName };
            _presets[_presets.IndexOf(preset)] = renamed; _revision++;
            return renamed;
        }
    }

    public ProcessingPreset Update(ProcessingPreset preset)
    {
        var frozen = ProcessingPreset.Validate(preset);
        lock (_gate)
        {
            var existing = Find(frozen.Id);
            EnsureNameFree(frozen.Name, frozen.Id);
            _presets[_presets.IndexOf(existing)] = frozen; _revision++;
            return frozen;
        }
    }

    public void Remove(Guid id)
    {
        lock (_gate)
        {
            var preset = Find(id);
            _presets.Remove(preset);
            if (_selected == id) _selected = null;
            _revision++;
        }
    }

    public void Select(Guid id)
    {
        lock (_gate) { Find(id); _selected = id; _revision++; }
    }

    private ProcessingPreset Find(Guid id)
        => _presets.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException("预设不存在。", nameof(id));

    private void EnsureNameFree(string name, Guid? except)
    {
        if (_presets.Any(p => p.Name == name && p.Id != except))
            throw new ArgumentException("已有同名预设：" + name);
    }
}
