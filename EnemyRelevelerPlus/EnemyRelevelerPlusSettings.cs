using System.Collections.Generic;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.WPF.Reflection.Attributes;

namespace EnemyRelevelerPlus;
public class EnemyRelevelerPlusSettings
{
    [SettingName("Mods to exclude")]
    [Tooltip("These mods raise NPC levels on their own")]
    public HashSet<ModKey> ModsToExclude = [];
}