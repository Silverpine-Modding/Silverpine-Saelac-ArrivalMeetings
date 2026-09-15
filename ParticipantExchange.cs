using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using System.Threading.Tasks;
using UnityEngine;

namespace ArrivalMeetings;

// One controlled speaking turn per entering/departing NPC, with Continue between speakers.
// Use the game's dialogue generator and native NPC display. Only turn scheduling
// belongs to this exchange; Continue must finish the line before removing its speaker.
internal sealed class ParticipantExchange
{
    private static ParticipantExchange? active;
    internal static bool Busy => active != null;
    internal static bool CanMutate => active?.Valid == true;
    internal static List<NeuralNPC> SelectedNearby => active?.nearby ?? new();
    private readonly DialogBox box = DialogBox.Instance;
    private readonly Player player = Player.Instance;
    private readonly SaveUI save = SaveUI.Instance;
    private readonly Vector2Int origin = Player.Instance.transform.GetVector2IntPosition();
    private readonly List<NeuralNPC> nearby, desired;
    private List<NeuralNPC> expected;
    private int revision = MeetingController.ConversationRevision;
    private readonly TaskCompletionSource<bool> canceled = new();
    private bool saveBlocked;
    private TaskCompletionSource<bool>? rendering, waitingForContinue;
    private string lastLine = "";
    private NeuralNPC? lastSpeaker;

    private ParticipantExchange(List<NeuralNPC> original, List<NeuralNPC> selection, List<NeuralNPC> candidates)
    { expected = original; desired = selection; nearby = candidates; }

    private bool Valid => active == this && Plugin.EnabledSetting.Value && !SerializationManager.loadingSave &&
        box == DialogBox.Instance && box.isOpen && player == Player.Instance && !player.inCombat &&
        player.transform.GetVector2IntPosition() == origin && revision == MeetingController.ConversationRevision &&
        expected.SequenceEqual(MeetingController.Participants());

    internal static async Task Start(IReadOnlyList<NeuralNPC> original, IEnumerable<NeuralNPC> selection)
    {
        if (!ParticipantController.CanEdit || !original.SequenceEqual(MeetingController.Participants()))
        { MeetingController.Notify("The conversation changed. Open Participants again."); return; }
        var desired = selection.Distinct().ToList();
        var nearby = ParticipantController.Nearby();
        if (desired.Except(original).Any(n => !nearby.Contains(n)))
        { MeetingController.Notify("A selected NPC is no longer nearby and visible."); return; }
        if (desired.Count == original.Count && desired.All(original.Contains)) return;
        var exchange = new ParticipantExchange(original.ToList(), desired, nearby);
        active = exchange;
        await exchange.Run();
    }

    private void AcceptChanges()
    { revision = MeetingController.ConversationRevision; expected = MeetingController.Participants(); }

    private async Task Run()
    {
        string finalText = "The conversation continues.";
        try
        {
            save.SetSaveBlock(true); saveBlocked = true;
            box.SetTalkAllowedState(false);
            List<NeuralNPC> removals = expected.Except(desired).ToList();
            List<NeuralNPC> additions = desired.Except(expected).ToList();
            if (additions.Count > 0)
            {
                if (!ParticipantController.Apply(expected, expected.Concat(additions), out finalText, exchange: true)) return;
                AcceptChanges();
                if (additions.Any(n => !expected.Contains(n))) removals.Clear();
                foreach (NeuralNPC npc in additions.Where(expected.Contains))
                    if (!await Say(npc, farewell: false)) return;
            }
            foreach (NeuralNPC npc in removals)
            {
                if (!Valid || !MeetingController.EligibleCharacter(npc)) return;
                if (!await Say(npc, farewell: true)) return;
                if (!ParticipantController.Apply(expected, expected.Where(n => n != npc).ToList(), out finalText, exchange: true)) return;
                if (!box.isOpen) return;
                AcceptChanges();
            }
        }
        catch (Exception ex)
        {
            finalText = "Participant changes stopped after an error. The current conversation is still available.";
            Plugin.Log.LogWarning("Participant greetings/farewells failed: " + ex);
        }
        finally
        {
            bool restore = Valid;
            if (active == this) active = null;
            Unlock();
            if (restore)
            {
                ParticipantController.Refresh(lastSpeaker == NeuralNPC.currentActiveDialogNeuralNPC && lastLine.Length > 0 ? lastLine : finalText);
                box.SetTalkAllowedState(true);
            }
        }
    }

    private async Task<bool> Say(NeuralNPC npc, bool farewell)
    {
        if (!Valid) return false;
        NeuralNPC.currentActiveDialogNeuralNPC = npc;
        npc.DoStartNPCMode(DialogBox.SpriteSwitchMode.Instant);
        string kind = farewell ? "farewell" : "greeting";
        box.DisplayTextNoDialog("Generating " + npc.GetFinalName() + "'s " + kind + "...",
            new DialogOption("Cancel remaining changes", () => Cancel(restore: true), endDialog: false));
        string roster = NeuralNPC.ToAnd(expected.Select(n => n.GetFinalName()).Prepend(player.playerName).ToList());
        string instruction = "Present participants: " + roster + ". " +
            (farewell ? "You are leaving this conversation now. Give a brief, in-character farewell before stepping out of the discussion. " :
                "You have just joined this conversation. Give a brief, in-character greeting or entrance. You did not hear discussion while you were absent. ") +
            "Write only " + npc.GetFinalName() + "'s spoken line and optional brief action, at most two sentences. " +
            "Do not speak for others, start new activities, request game actions, or move the group to another location.";
        string text;
        bool generated = false;
        string historyText = "";
        var history = npc.dialogElements;
        var cue = history.AddToDialog(SpeakerType.System, instruction);
        try
        {
            // Generate is the same awaitable dialogue path used by GenerateDialog /
            // GenerateMultiDialog, including model settings, memory and character context.
            Task<string> request = (Task<string>)AccessTools.Method(typeof(NeuralNPC), "Generate")
                .Invoke(npc, new object[] { false })!;
            // Observe a late request failure even when its scene was canceled while awaiting the model.
            _ = request.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            if (await Task.WhenAny(request, canceled.Task) != request || !Valid) return false;
            text = await request;
            text = (string)AccessTools.Method(typeof(NeuralNPC), "RemoveToolTags").Invoke(null, new object[] { text })!;
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("The dialogue model returned an empty line.");
            historyText = (string)AccessTools.Method(typeof(NeuralNPC), "TransformTextForGenerateDialog").Invoke(npc, new object[] { text })!;
            text = historyText;
            string translation = "";
            if (SettingsUI.Instance.GetDialogLanguage() != Language.English)
            {
                string context = history.LastOrDefault(d => d.speakerType == SpeakerType.Player || d.speakerType == SpeakerType.NPC)?.GetNamedContents() ?? "";
                Task<string> translating = InferenceServerSetupHandler.Instance.Translate(context, npc.GetFinalName(),
                    text.Replace(npc.GetFinalName() + ": ", ""), SettingsUI.Instance.GetDialogLanguage());
                _ = translating.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                if (await Task.WhenAny(translating, canceled.Task) != translating || !Valid) return false;
                translation = npc.GetFinalName() + ": " + await translating;
                text = translation;
            }
            AccessTools.Field(typeof(NeuralNPC), "lastTranslation").SetValue(npc, translation);
            generated = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Could not generate " + npc.GetFinalName() + "'s " + kind + ": " + ex);
            text = "A " + kind + " could not be generated for " + npc.GetFinalName() + ". Continue to complete the participant change.";
        }
        finally { history.Remove(cue); }
        if (!Valid) return false;
        if (generated)
            foreach (var listenerHistory in expected.Select(n => n.dialogElements).Distinct()) listenerHistory.AddToDialog(SpeakerType.NPC, historyText);
        var continued = new TaskCompletionSource<bool>();
        waitingForContinue = rendering = continued;
        lastSpeaker = npc;
        lastLine = text;
        try
        {
            AccessTools.Method(typeof(NeuralNPC), "DisplayMultiDialogText").Invoke(null, new object?[] { npc, null, text });
        }
        finally { rendering = null; }
        await Task.WhenAny(continued.Task, canceled.Task);
        if (waitingForContinue == continued) waitingForContinue = null;
        return Valid && continued.Task.IsCompleted;
    }

    // Modify just our native DisplayText call. Other dialogue retains its own callbacks.
    internal static void ConfigureDisplay(DialogBox box, ref Action<string> input, ref Action? finished)
    {
        var exchange = active;
        var continued = exchange?.rendering;
        if (exchange == null || continued == null || !exchange.Valid || exchange.box != box) return;
        input = _ =>
        {
            if (!exchange.Valid || box.isAnimatingText || continued.Task.IsCompleted) return;
            box.SetTalkAllowedState(false);
            continued.TrySetResult(true);
        };
        finished = () =>
        {
            if (!exchange.Valid || exchange.waitingForContinue != continued) return;
            box.StartContinueOnlyMode();
            box.SetTalkAllowedState(true);
        };
    }

    internal static void OnInterrupt()
    {
        if (active?.waitingForContinue != null) Cancel(restore: true);
    }

    private void Unlock()
    { if (saveBlocked) { saveBlocked = false; if (save != null) save.SetSaveBlock(false); } }

    internal static void Cancel(bool restore = false)
    {
        var exchange = active;
        if (exchange == null) return;
        bool valid = exchange.Valid;
        active = null;
        exchange.canceled.TrySetResult(true);
        exchange.Unlock();
        if (restore && valid)
        {
            ParticipantController.Refresh(exchange.lastSpeaker == NeuralNPC.currentActiveDialogNeuralNPC && exchange.lastLine.Length > 0
                ? exchange.lastLine : "Remaining participant changes canceled. The conversation continues.");
            exchange.box.SetTalkAllowedState(true);
        }
    }
}

[HarmonyPatch(typeof(DialogBox), nameof(DialogBox.DisplayText))]
internal static class ParticipantSpeechDisplayPatch
{
    private static void Prefix(DialogBox __instance, ref Action<string> inputCallback, ref Action? finishedAnimatingCallback) =>
        ParticipantExchange.ConfigureDisplay(__instance, ref inputCallback, ref finishedAnimatingCallback);
}

[HarmonyPatch(typeof(DialogBox), nameof(DialogBox.StopContinueOnlyMode))]
internal static class ParticipantSpeechInterruptPatch
{
    private static void Postfix() => ParticipantExchange.OnInterrupt();
}
