using ACE.Entity.Enum.Properties;
using ACE.Entity.Enum;
using ACE.Entity;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using System.Diagnostics;
using System.Text;
using ACE.Shared.Helpers;
using ACE.Server.Command;
using ACE.Server.Network;
using static ACE.Server.Physics.Common.LandDefs;

namespace LootAll;

[HarmonyPatch]
public class PatchClass
{
    #region Settings
    const int RETRIES = 10;

    public static Settings Settings = new();
    static string settingsPath => Path.Combine(Mod.ModPath, "Settings.json");
    private FileInfo settingsInfo = new(settingsPath);

    private JsonSerializerOptions _serializeOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private void SaveSettings()
    {
        string jsonString = JsonSerializer.Serialize(Settings, _serializeOptions);

        if (!settingsInfo.RetryWrite(jsonString, RETRIES))
        {
            ModManager.Log($"Failed to save settings to {settingsPath}...", ModManager.LogLevel.Warn);
            Mod.State = ModState.Error;
        }
    }

    private void LoadSettings()
    {
        if (!settingsInfo.Exists)
        {
            ModManager.Log($"Creating {settingsInfo}...");
            SaveSettings();
        }
        else
            ModManager.Log($"Loading settings from {settingsPath}...");

        if (!settingsInfo.RetryRead(out string jsonString, RETRIES))
        {
            Mod.State = ModState.Error;
            return;
        }

        try
        {
            Settings = JsonSerializer.Deserialize<Settings>(jsonString, _serializeOptions);
        }
        catch (Exception)
        {
            ModManager.Log($"Failed to deserialize Settings: {settingsPath}", ModManager.LogLevel.Warn);
            Mod.State = ModState.Error;
            return;
        }
    }
    #endregion

    #region Start/Shutdown
    public void Start()
    {
        //Need to decide on async use
        Mod.State = ModState.Loading;
        LoadSettings();

        if (Mod.State == ModState.Error)
        {
            ModManager.DisableModByPath(Mod.ModPath);
            return;
        }

        Mod.State = ModState.Running;
    }

    public void Shutdown()
    {
        //if (Mod.State == ModState.Running)
        // Shut down enabled mod...

        //If the mod is making changes that need to be saved use this and only manually edit settings when the patch is not active.
        //SaveSettings();

        if (Mod.State == ModState.Error)
            ModManager.Log($"Improper shutdown: {Mod.ModPath}", ModManager.LogLevel.Error);
    }
    #endregion

    static Random random = new();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Creature), nameof(Creature.CreateCorpse), new Type[] { typeof(DamageHistoryInfo), typeof(bool) })]
    public static bool PreCreateCorpse(DamageHistoryInfo killer, bool hadVitae, ref Creature __instance)
    {
        //Don't apply to players
        if (__instance is Player p)
            return true;

        //If you can't find the killer handle the corpse in the regular fashion
        if (killer.TryGetPetOwnerOrAttacker() is not Player player)
            return true;

        //Get a list of lootable items
        var loot = LootAll(player, __instance);

        if (loot.Count == 0)
            return false;

        //Get a list of looters, just the player if not in a fellow with some restriction
        List<Player> looters = Settings.LooterRequirements switch
        {
            LooterRequirements.Landblock => 
                player.GetFellowshipTargets().Where(x => x.CurrentLandblock.Id == player.CurrentLandblock.Id).ToList(),
            LooterRequirements.Range => 
                player.GetFellowshipTargets().Where(x => x.Location.Distance2D(player.Location) < Fellowship.MaxDistance * 2).ToList(),
            _ => player.GetFellowshipTargets().ToList(),
        };


        if (player.GetProperty(LootMuted) != true)
            player.SendMessage($"Looting {loot.Count} items for {looters.Count} players", Settings.MessageType);

        //Roll a random starting player for round-robin
        var index = random.Next(0, looters.Count);

        //For each loot item
        foreach (var item in loot)
        {
            switch (Settings.LootStyle)
            {
                case LootStyle.Finder:
                    var success = player.TryCreateInInventoryWithNetworking(item);
                    if (player.GetProperty(LootMuted) != true)
                        player.SendMessage($"{(success ? "Looted" : "Failed to loot")} {item.Name}.", Settings.MessageType);
                    break;
                case LootStyle.RoundRobin:
                    var looter = looters[index];
                    index = (index + 1) % looters.Count;
                    var s2 = looter.TryCreateInInventoryWithNetworking(item);
                    if (looter.GetProperty(LootMuted) != true)
                        looter.SendMessage($"{(s2 ? "Looted" : "Failed to loot")} {item.Name}.", Settings.MessageType);
                    break;
                case LootStyle.OneForAll:
                    foreach (var l in looters)
                    {
                        //TODO: proper clone instead of weenie clone
                        var clonedItem = WorldObjectFactory.CreateNewWorldObject(item.WeenieClassId);

                        var s3 = l.TryCreateInInventoryWithNetworking(item);
                        if (l.GetProperty(LootMuted) != true)
                            l.SendMessage($"{(s3 ? "Looted" : "Failed to loot")} {item.Name}.", Settings.MessageType);
                    }
                    item?.Destroy(); //Clean up source item?
                    break;
            }
        }

        return false;
    }

    private static List<WorldObject> LootAll(Player player, Creature __instance)
    {
        var droppedItems = new List<WorldObject>();

        //Death
        if (__instance.DeathTreasure != null)
        {
            foreach (var item in LootGenerationFactory.CreateRandomLootObjects(__instance.DeathTreasure))
                //if (!player.TryCreateInInventoryWithNetworking(item))
                droppedItems.Add(item);
        }

        //Wielded
        var dropFlags = PropertyManager.GetBool("creatures_drop_createlist_wield").Item ? DestinationType.WieldTreasure : DestinationType.Treasure;
        var wieldedTreasure = __instance.Inventory.Values.Concat(__instance.EquippedObjects.Values).Where(i => (i.DestinationType & dropFlags) != 0);
        foreach (var item in wieldedTreasure)
        {
            if (item.Bonded == BondedStatus.Destroy)
                continue;

            if (__instance.TryDequipObjectWithBroadcasting(item.Guid, out var wo, out var wieldedLocation))
                __instance.EnqueueBroadcast(new GameMessagePublicUpdateInstanceID(item, PropertyInstanceId.Wielder, ObjectGuid.Invalid));

            //if (!player.TryCreateInInventoryWithNetworking(item))
            droppedItems.Add(item);
        }

        //Non-wielded Create
        if (__instance.Biota.PropertiesCreateList != null)
        {
            var createList = __instance.Biota.PropertiesCreateList.Where(i => (i.DestinationType & DestinationType.Contain) != 0 ||
                            (i.DestinationType & DestinationType.Treasure) != 0 && (i.DestinationType & DestinationType.Wield) == 0).ToList();

            var selected = Creature.CreateListSelect(createList);

            foreach (var create in selected)
            {
                var item = WorldObjectFactory.CreateNewWorldObject(create);
                if (item is null)
                    continue;

                //if (!player.TryCreateInInventoryWithNetworking(item))
                droppedItems.Add(item);
            }
        }

        return droppedItems;
    }

    //Create a fake property bool to track
    static PropertyBool LootMuted = (PropertyBool)39998;

    //Create a command to toggle the variable
    [CommandHandler("lootmute", AccessLevel.Player, CommandHandlerFlag.RequiresWorld)]
    public static void HandleT2(Session session, params string[] parameters)
    {
        var player = session.Player;
        if (player is null) return;

        //Get the current property, defaulting to false if absent
        var toggle = player.GetProperty(LootMuted) ?? false;

        //Toggle the prop
        toggle = !toggle;

        //Set it to the opposite and inform the player
        player.SetProperty(LootMuted, toggle);
        player.SendMessage($"Loot messages will {(toggle ? "not" : "")} be sent.");
    }

    //[CommandHandler("clean", AccessLevel.Admin, CommandHandlerFlag.RequiresWorld, 0)]
    //public static void Clean(Session session, params string[] parameters)
    //{
    //    // @delete - Deletes the selected object. Players may not be deleted this way.

    //    var player = session.Player;

    //    foreach (var item in player.Inventory.Values)
    //    {
    //        item.DeleteObject(player);
    //        session.Network.EnqueueSend(new GameMessageDeleteObject(item));
    //    }
    //}
}

