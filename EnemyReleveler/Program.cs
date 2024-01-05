using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json;
using Noggog;
using System.IO;
using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace EnemyReleveler
{
    public enum LevelType
    {
        MaxLevel,
        MinLevel,
        Level
    }

    // ReSharper disable once ClassNeverInstantiated.Global
    public class Program
    {
        private static readonly List<string> UnderleveledNpcs = new();
        private static readonly List<string> OverleveledNpcs = new();
        private static readonly List<string> LowPoweredNpcs = new();
        private static readonly List<string> HighPoweredNpcs = new();

        private static readonly HashSet<IFormLinkGetter<INpcGetter>> NpcsToIgnore = new()
        {
            Skyrim.Npc.MQ101Bear,
            Skyrim.Npc.WatchesTheRootsCorpse,
            Skyrim.Npc.BreyaCorpse,
            Skyrim.Npc.WatchesTheRoots,
            Skyrim.Npc.Drennen,
            Skyrim.Npc.Breya,
            Skyrim.Npc.dunHunterBear,
            Dawnguard.Npc.DLC1HowlSummonWerewolf,
        };

        private static int[][] _rule = { new[] {0, 0}, new[] {0, 0} };

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "enemies_releveled.esp")
                .Run(args);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            //set up rules
            var creatureRulesPath = Path.Combine(state.ExtraSettingsDataPath, "enemy_rules.json");

            if (!File.Exists(creatureRulesPath))
            {
                System.Console.Error.WriteLine($"ERROR: Missing required file {creatureRulesPath}");
            }

            var enemyRules = JsonConvert.DeserializeObject<Dictionary<string, int[][]>>(File.ReadAllText(creatureRulesPath));

            foreach (var getter in state.LoadOrder.PriorityOrder.Npc().WinningOverrides())
            {
                //filter NPCs
                if (NpcsToIgnore.Contains(getter)
                    || getter.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Stats))
                {
                    continue;
                }

                bool skip = true;

                foreach (var rank in getter.Factions)
                {
                    if (!rank.Faction.TryResolve(state.LinkCache, out var factionRecord)) continue;
                    var faction = factionRecord.EditorID ?? "";
                    if (enemyRules.TryGetValue(faction, out var rule))
                    {
                        skip = false;
                        _rule = rule;
                    }
                    if (skip == false) break;
                }

                if (skip) continue;

                //Start releveling
                var npc = getter.DeepCopy();
                if (npc.Configuration.Level is IPcLevelMult)
                {
                    EditValue(npc, LevelType.MinLevel, _rule);
                    EditValue(npc, LevelType.MaxLevel, _rule);
                }
                else
                {
                    EditValue(npc, LevelType.Level, _rule);
                }
                state.PatchMod.Npcs.GetOrAddAsOverride(npc);
            }
            PrintWarnings();
        }


        private static void EditValue(INpc npc, LevelType levelType, IReadOnlyList<int[]> rule)
        {
            decimal currentLevel = 1;
            switch (levelType)
            {
                case LevelType.MinLevel:
                    currentLevel = npc.Configuration.CalcMinLevel;
                    break;
                case LevelType.MaxLevel:
                    currentLevel = npc.Configuration.CalcMaxLevel;
                    if (currentLevel == 0) return;
                    break;
                case LevelType.Level:
                    if (npc.Configuration.Level is INpcLevelGetter level) currentLevel = level.Level;
                    if (currentLevel < rule[0][0]) UnderleveledNpcs.Add(npc.EditorID ?? "");
                    if (currentLevel > rule[0][1]) OverleveledNpcs.Add(npc.EditorID ?? "");
                    break;
                default:
                    break;
            }
            var newLevel =
                Math.Round(
                        Math.Pow(
                            (double)(currentLevel - rule[0][0]) / (rule[0][1] - rule[0][0])
                        , 1.5)
                        * (rule[1][1] - rule[1][0]
                    )
                    + rule[1][0]);

            if (newLevel < 1)
            {
                if (levelType == LevelType.Level) LowPoweredNpcs.Add(npc.EditorID ?? "");
                newLevel = 1;
            }
            if (newLevel > 100 & levelType == LevelType.Level) HighPoweredNpcs.Add(npc.EditorID ?? "");

            switch (levelType)
            {
                case LevelType.MinLevel:
                    npc.Configuration.CalcMinLevel = (short)newLevel;
                    break;
                case LevelType.MaxLevel:
                    npc.Configuration.CalcMaxLevel = (short)newLevel;
                    break;
                case LevelType.Level:
                    npc.Configuration.Level = new NpcLevel()
                    {
                        Level = (short)newLevel
                    };
                    break;
                default:
                    break;
            }
        }

        private static void PrintWarnings()
        {
            if (UnderleveledNpcs.Count > 0)
            {
                Console.WriteLine("Warning, the following NPCs were at a lower level than the patcher expected (i.e. below the lower bound of the starting range). Its not a problem, and they have been patched, chances are another mod has changed their level too. This is just to let you know.");
                foreach (var item in UnderleveledNpcs)
                {
                    Console.WriteLine(item);
                }
            }
            if (OverleveledNpcs.Count > 0)
            {
                Console.WriteLine("Warning, the following NPCs were at a higher level than the patcher expected (i.e. above the upper bound of the starting range). Its not a problem, and they have been patched, chances are another mod has changed their level too. This is just to let you know.");
                foreach (var item in OverleveledNpcs)
                {
                    Console.WriteLine(item);
                }
            }
            if (LowPoweredNpcs.Count > 0)
            {
                Console.WriteLine("Warning, the faction rule told the patcher to give the following NPCs a level < 1. This has been ignored and the NPCs level has been set to 1.");
                foreach (var item in LowPoweredNpcs)
                {
                    Console.WriteLine(item);
                }
            }
            if (HighPoweredNpcs.Count > 0)
            {
                Console.WriteLine("Warning, the faction rule told the patcher to give the following NPCs a level > 100. Good luck!");
                foreach (var item in HighPoweredNpcs)
                {
                    Console.WriteLine(item);
                }
            }
        }
    }
}

