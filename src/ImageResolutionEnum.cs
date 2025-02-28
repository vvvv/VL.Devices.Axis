using System.Reactive.Linq;
using System.Reactive.Subjects;
using VL.Core.CompilerServices;
using VL.Lib.Collections;

namespace VL.Devices.Axis;

[Serializable]
public class ImageResolutionEnum : DynamicEnumBase<ImageResolutionEnum, ImageResolutionEnumDefinition>
{
    public ImageResolutionEnum(string value) : base(value)
    {
    }

    [CreateDefault]
    public static ImageResolutionEnum CreateDefault()
    {
        return CreateDefaultBase();
    }
}

public class ImageResolutionEnumDefinition : DynamicEnumDefinitionBase<ImageResolutionEnumDefinition>
{
    Dictionary<string, object> entries = new Dictionary<string, object>();
    Subject<object> trigger = new Subject<object>(); //Really just used as a trigger, the "object" is ignored

    [CreateDefault]
    public static ImageResolutionEnumDefinition CreateDefault()
    {
        return Instance;
    }

    /// <summary>
    /// Adds an entry to the enum that can optionally have an object associated as its tag
    /// </summary>
    /// <param name="name">Name of the entry to add</param>
    /// <param name="tag">Optional: Object associated to the enum entry</param>
    public void AddEntry(string name, object? tag = null)
    {
        entries[name] = tag;
        trigger.OnNext("");
    }

    /// <summary>
    /// Removes the given entry from the enum
    /// </summary>
    /// <param name="name">Name of the entry to remove</param>
    public void RemoveEntry(string name)
    {
        entries.Remove(name);
        trigger.OnNext("");
    }

    /// <summary>
    /// Removes all entries from the enum
    /// </summary>
    public void ClearEntries()
    {
        entries.Clear();
        trigger.OnNext("");
    }

    protected override IReadOnlyDictionary<string, object> GetEntries()
    {
        return entries;
    }

    protected override IObservable<object> GetEntriesChangedObservable()
    {
        return trigger;
    }

    protected override bool AutoSortAlphabetically => false;
}