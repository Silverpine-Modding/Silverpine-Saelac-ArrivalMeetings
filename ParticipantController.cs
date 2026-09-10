using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArrivalMeetings;

internal static class ParticipantController
{
    internal static bool CanOffer => !ParticipantExchange.Busy && Plugin.EnabledSetting.Value && !SerializationManager.loadingSave &&
        Player.Instance != null && !Player.Instance.inCombat && DialogBox.Instance != null && DialogBox.Instance.isOpen &&
        AccessTools.Field(typeof(DialogBox), "isNPCDIalog").GetValue(DialogBox.Instance) is true &&
        NeuralNPC.currentActiveDialogNeuralNPC != null && !ManualTravel.Busy &&
        MeetingController.Participants().All(n => NeuralNPC.neuralNPCs.TryGetValue(n.npcName, out var registered) && registered == n);

    internal static bool CanEdit => CanOffer && !NeuralNPC.npcFunctionsBeingInvoked &&
        !DialogBox.Instance.isAnimatingText && !SaveUI.Instance.IsSavingBlocked() &&
        MeetingController.Participants().All(MeetingController.EligibleCharacter);

    internal static List<NeuralNPC> Nearby()
    {
        if (!CanOffer) return new();
        List<NeuralNPC> current = MeetingController.Participants();
        NeuralNPC speaker = NeuralNPC.currentActiveDialogNeuralNPC;
        Vector2Int playerAt = Player.Instance.transform.GetVector2IntPosition();
        Vector2Int speakerAt = speaker.transform.GetVector2IntPosition();
        return NeuralNPC.neuralNPCs.Values.Distinct()
            .Where(n => MeetingController.EligibleCharacter(n) && !current.Contains(n))
            .Where(n => WithinRange(n.transform.GetVector2IntPosition(), playerAt) || WithinRange(n.transform.GetVector2IntPosition(), speakerAt))
            .Where(n => speaker.CanSeeTransform(n.transform))
            .OrderBy(n => n.GetFinalName(), StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool WithinRange(Vector2Int a, Vector2Int b) => Math.Abs(a.x - b.x) <= 2 && Math.Abs(a.y - b.y) <= 2;

    internal static bool Apply(IReadOnlyList<NeuralNPC> original, IEnumerable<NeuralNPC> selection, out string message, bool exchange = false)
    {
        message = "The conversation changed. Open Participants again.";
        if (!(exchange ? ParticipantExchange.CanMutate : CanEdit) || !original.SequenceEqual(MeetingController.Participants())) return false;
        List<NeuralNPC> desired = selection.Distinct().ToList();
        List<NeuralNPC> additions = desired.Except(original).ToList();
        List<NeuralNPC> removals = original.Except(desired).ToList();
        if (removals.Any(n => !MeetingController.EligibleCharacter(n)))
        { message = "A participant cannot leave this encounter right now."; return false; }
        List<NeuralNPC> nearby = exchange ? ParticipantExchange.SelectedNearby : Nearby();
        if (additions.Any(n => n == null || !nearby.Contains(n)))
        { message = "A selected NPC is no longer nearby and visible. Open Participants again."; return false; }
        if (additions.Count == 0 && removals.Count == 0) { message = "No participant changes."; return true; }
        if (desired.Count == 0)
        {
            DialogBox.Instance.EndDialog();
            message = "Conversation ended.";
            return true;
        }

        var joined = new List<NeuralNPC>();
        var failed = new List<string>();
        foreach (NeuralNPC npc in additions)
        {
            bool returning = NeuralNPC.initialMultiDialogParticipants?.Contains(npc) == true;
            try
            {
                GuestLifecycle.Prepare(npc);
                MeetingController.IncludeParticipant(npc);
                joined.Add(npc);
            }
            catch (Exception ex)
            {
                failed.Add(npc.GetFinalName());
                if (!returning)
                {
                    try { npc.ReleaseLargeAssets(); }
                    catch (Exception cleanup) { Plugin.Log.LogWarning("Participant portrait cleanup failed: " + cleanup); }
                }
                Plugin.Log.LogWarning("Could not add conversation participant " + npc.GetFinalName() + ": " + ex);
            }
        }
        // A failed replacement must not remove the original conversation partners.
        if (failed.Count > 0) removals.Clear();
        if (joined.Count == 0 && removals.Count == 0)
        { message = "Could not add " + string.Join(", ", failed) + ". No participants were removed."; return false; }

        // Retain departed NPCs in the native cleanup roster, as native location travel does.
        // They stop receiving group turns immediately and are cleaned up once at dialogue end.
        NeuralNPC.multiDialogParticipants!.RemoveAll(removals.Contains);
        List<NeuralNPC> remaining = MeetingController.Participants();
        if (!remaining.Contains(NeuralNPC.currentActiveDialogNeuralNPC)) NeuralNPC.currentActiveDialogNeuralNPC = remaining[0];
        MeetingController.OnParticipantsChanged();
        string roster = NeuralNPC.ToAnd(remaining.Select(n => n.GetFinalName()).Prepend(Player.Instance.playerName).ToList());
        var changes = new List<string>();
        if (joined.Count > 0) changes.Add(NeuralNPC.ToAnd(joined.Select(n => n.GetFinalName()).ToList()) + " joined the conversation.");
        if (removals.Count > 0) changes.Add(NeuralNPC.ToAnd(removals.Select(n => n.GetFinalName()).ToList()) + " left the conversation.");
        message = string.Join(" ", changes) + " Present participants: " + roster + ".";
        if (failed.Count > 0) message += " Could not add " + string.Join(", ", failed) + "; nobody was removed.";
        // Each participant keeps only the turns they were actually present for.
        foreach (var history in original.Concat(joined).Select(n => n.dialogElements).Distinct())
            history.AddToDialog(SpeakerType.System, message);
        if (joined.Count > 0)
        {
            string context = "New or returning participants did not hear what was said while they were absent. Explain earlier events to them if needed.";
            foreach (var history in remaining.Select(n => n.dialogElements).Distinct()) history.AddToDialog(SpeakerType.System, context);
        }
        // Rebuild native portrait, vendor actions and input callback with no cached next speaker.
        // Native multi-dialogue supports a one-NPC roster, so shrinking a group needs no restart.
        if (!exchange) Refresh("Conversation updated. " + message);
        return true;
    }

    internal static void Refresh(string text)
    {
        if (NeuralNPC.multiDialogParticipants != null)
        {
            AccessTools.Method(typeof(NeuralNPC), "DisplayMultiDialogText").Invoke(null,
                new object?[] { NeuralNPC.currentActiveDialogNeuralNPC, null, text });
            DialogBox.Instance.StopContinueOnlyMode();
        }
        else AccessTools.Method(typeof(NeuralNPC), "DisplayDialogText").Invoke(NeuralNPC.currentActiveDialogNeuralNPC, new object[] { text });
    }
}
