using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace SliceIsRight;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;

    // the below exist just to make saving less cumbersome
    public void Save()
    {
        SliceIsRightPlugin.PluginInterface.SavePluginConfig(this);
    }
}
