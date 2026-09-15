using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Silverpine.ModdingTools;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace ArrivalMeetings;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Silverpine.ModdingTools.Plugin.PluginGuid, "1.9.3")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "Saelac.Silverpine.ArrivalMeetings";
    public const string PluginName = "Arrival Meetings";
    public const string PluginVersion = "1.2.1";
    internal static ManualLogSource Log = null!;
    internal static ConfigEntry<bool> EnabledSetting = null!, Automatic = null!, BringAbsent = null!;
    internal static ConfigEntry<int> MaxGuests = null!, HistoryLength = null!, StayTurns = null!;

    private void Awake()
    {
        Log = Logger;
        EnabledSetting = Config.Bind("General", "Enabled", true, "Enable conversation travel and meetings after location changes.");
        Automatic = Config.Bind("General", "AutomaticMeetings", true, "Use the current dialogue model to confirm named meeting targets and add them automatically on arrival.");
        BringAbsent = Config.Bind("General", "BringAbsentNPCs", true, "Allow intended meeting targets to be moved from elsewhere in the loaded world.");
        MaxGuests = Config.Bind("General", "MaxGuestsPerArrival", 3, new ConfigDescription("Maximum NPCs added at one arrival, including manual additions.", new AcceptableValueRange<int>(1, 8)));
        HistoryLength = Config.Bind("General", "RecentDialogueElements", 12, new ConfigDescription("Recent entries from the current conversation used to detect meeting intent.", new AcceptableValueRange<int>(4, 30)));
        StayTurns = Config.Bind("General", "StayTurns", 10, new ConfigDescription("Turns before the temporary meeting routine expires, matching native location-change behavior by default.", new AcceptableValueRange<int>(1, 120)));
        ModdingToolsMenu.RegisterSession(PluginGuid + ".settings", PluginName,
            (_, session) => SettingsWindow.Open(session), order: 320);
        InventoryModTools.RegisterSession(PluginGuid + ".settings-ingame", PluginName,
            (_, session) => SettingsWindow.Open(session), order: 320);
        DialogueActions.Register(PluginGuid, new DialogueActionDefinition
        {
            Id = PluginGuid + ".participants", Label = "Participants", Order = 100,
            IsVisible = _ => ParticipantController.CanOffer,
            OnSelected = _ => ParticipantMenu.Open()
        });
        DialogueActions.Register(PluginGuid, new DialogueActionDefinition
        {
            Id = PluginGuid + ".meet", Label = "Meet on arrival", Order = 120,
            IsVisible = _ => MeetingController.CanOffer,
            OnSelected = _ => MeetingPicker.Open()
        });
        DialogueActions.Register(PluginGuid, new DialogueActionDefinition
        {
            Id = PluginGuid + ".travel", Label = "Travel", Order = 110,
            IsVisible = _ => ManualTravel.CanOffer,
            OnSelected = _ => TravelMenu.Open()
        });
        new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo($"Arrival Meetings {PluginVersion} loaded; dialogue arrivals can bring intended meeting targets.");
        // Static configuration, registrations and patches survive Silverpine's bootstrap host destruction.
    }
}

[HarmonyPatch(typeof(NPCFunction_ChangeLocation), "InnerInvoke")]
internal static class LocationChangePatch
{
    private static void Prefix(NPCFunction_ChangeLocation __instance, string mapZoneName, out Arrival? __state)
    {
        __state = null;
        try { __state = MeetingController.Begin(__instance.owner, mapZoneName); }
        catch (Exception ex) { Plugin.Log.LogWarning("Could not observe location change: " + ex); }
    }

    private static void Postfix(ref Task __result, Arrival? __state)
    {
        if (__state != null) __result = MeetingController.AfterTravel(__result, __state);
    }
}

// A new conversation, closed box, or save load invalidates every outstanding arrival.
[HarmonyPatch]
internal static class ConversationBoundaryPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NeuralNPC), "TriggerDialog");
        yield return AccessTools.Method(typeof(NeuralNPC), "TriggerMultiDialog");
        yield return AccessTools.Method(typeof(DialogBox), nameof(DialogBox.CloseBox));
        yield return AccessTools.Method(typeof(SerializationManager), nameof(SerializationManager.Load));
    }
    private static void Prefix()
    {
        TravelMenu.Cancel(restoreConversation: false);
        ParticipantMenu.Cancel(restoreConversation: false);
        ParticipantExchange.Cancel();
        ManualTravel.OnConversationReset();
        MeetingController.Reset();
    }
}

[HarmonyPatch(typeof(DialogBox), nameof(DialogBox.DisplayText))]
internal static class DisplaySnapshotPatch
{
    private static void Postfix(DialogBox __instance, string text, Action<string> inputCallback,
        List<UpperButtonOption> upperButtonOptions, Action finishedAnimatingCallback) =>
        MeetingPicker.Remember(__instance, text, inputCallback, upperButtonOptions, finishedAnimatingCallback);
}

// A single-NPC display may still hold its original callback after conversion to a group.
[HarmonyPatch(typeof(NeuralNPC), "OnInputCallback")]
internal static class ConvertedInputPatch
{
    private static bool Prefix(NeuralNPC __instance, string text)
    {
        if (!MeetingController.OwnsGroup || (NeuralNPC.multiDialogParticipants?.Count ?? 0) < 1) return true;
        NeuralNPC.OnMultiInputCallback(null, text);
        return false;
    }
}

[HarmonyPatch(typeof(DialogBox), nameof(DialogBox.SetTalkAllowedState))]
internal static class ArrivalReadyPatch
{
    private static void Postfix(bool state)
    {
        if (!state || !MeetingController.RefreshPending) return;
        MeetingController.RefreshPending = false;
        // A later native action may already be generating new text. Its DisplayText will draw our action.
        if (SaveUI.Instance != null && SaveUI.Instance.IsSavingBlocked()) return;
        try { MeetingPicker.Restore(); }
        catch (Exception ex) { Plugin.Log.LogWarning("Could not refresh arrival actions: " + ex); }
    }
}
