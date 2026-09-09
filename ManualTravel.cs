using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace ArrivalMeetings;

internal sealed class TravelPlan
{
    internal TravelDestination Destination = null!;
    internal List<NeuralNPC> Travelers = new(), Guests = new();
    internal List<Vector2Int> Positions = new();
    internal int Revision;
    internal Player Player = null!;
    internal Vector2Int Origin;
}

internal static class ManualTravel
{
    internal static bool Busy { get; private set; }
    private static int lockRevision = -1;
    internal static void OnConversationReset()
    {
        if (Busy && lockRevision == MeetingController.ConversationRevision)
        {
            NeuralNPC.npcFunctionsBeingInvoked = false;
            lockRevision = -1;
        }
    }
    internal static bool CanOffer => MeetingController.WorldReady && !Busy;
    internal static bool CanStart => CanOffer && !NeuralNPC.npcFunctionsBeingInvoked &&
        !DialogBox.Instance.isAnimatingText && !SaveUI.Instance.IsSavingBlocked() &&
        MeetingController.Participants().All(n => n.GetComponent<EntityMover>() != null &&
            n.GetComponent<NPCRoutineExecutor>()?.currentRoutine != null && DialogBox.Instance.IsEndDialogAllowed(n));

    internal static List<NeuralNPC> GuestsFor(TravelDestination destination)
    {
        var preview = new Arrival { Destination = destination.Zone, Travelers = MeetingController.Participants() };
        return NeuralNPC.neuralNPCs.Values.Where(n => MeetingController.Eligible(n, preview))
            .Distinct().OrderByDescending(n => destination.Owners.Contains(n.npcName))
            .ThenBy(n => n.GetFinalName(), StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static bool TryPlan(TravelDestination destination, IEnumerable<NeuralNPC> guests,
        out TravelPlan? plan, out string error)
    {
        plan = null;
        error = "Wait for the current dialogue action to finish.";
        if (!CanStart) return false;
        List<NeuralNPC> selected = guests.Distinct().ToList();
        if (selected.Count > Plugin.MaxGuests.Value)
        { error = "Select at most " + Plugin.MaxGuests.Value + " meeting guests."; return false; }
        List<NeuralNPC> eligible = GuestsFor(destination);
        if (selected.Any(n => n == null || !eligible.Contains(n)))
        { error = "A selected guest is no longer available. Review your guest selection."; return false; }
        List<NeuralNPC> travelers = MeetingController.Participants();
        int required = 1 + travelers.Count + selected.Count;
        List<Vector2Int> positions = TravelLocations.Formation(destination.Tiles.Where(p => TravelLocations.SafeTile(p, destination.Zone)), required);
        if (positions.Count != required)
        { error = "This location has no valid arrival tile. Choose another room."; return false; }
        plan = new TravelPlan { Destination = destination, Travelers = travelers, Guests = selected, Positions = positions,
            Revision = MeetingController.ConversationRevision, Player = Player.Instance, Origin = Player.Instance.transform.GetVector2IntPosition() };
        error = "";
        return true;
    }

    private static bool Valid(TravelPlan plan) => MeetingController.WorldReady &&
        plan.Revision == MeetingController.ConversationRevision && plan.Player == Player.Instance &&
        plan.Travelers.SequenceEqual(MeetingController.Participants());

    internal static async Task<bool> Execute(TravelPlan plan)
    {
        if (Busy || !Valid(plan) || plan.Player.transform.GetVector2IntPosition() != plan.Origin) return false;
        Busy = true;
        SaveUI save = SaveUI.Instance;
        DialogBox box = DialogBox.Instance;
        BlackScreen screen = BlackScreen.Instance;
        bool saveBlocked = false, faded = false;
        int revision = plan.Revision;
        try
        {
            save.SetSaveBlock(true);
            saveBlocked = true;
            NeuralNPC.npcFunctionsBeingInvoked = true;
            lockRevision = revision;
            box.SetTalkAllowedState(false);
            if (screen != null) { faded = true; await Fade(screen, fadeIn: true); }
            if (!Valid(plan) || plan.Player.transform.GetVector2IntPosition() != plan.Origin ||
                plan.Positions.Any(p => !TravelLocations.SafeTile(p, plan.Destination.Zone)))
                throw new InvalidOperationException("The conversation or destination changed before travel completed.");
            // Recheck restrictions after the fade before making any world changes.
            if (plan.Guests.Count > Plugin.MaxGuests.Value || plan.Travelers.Any(n => !box.IsEndDialogAllowed(n)) ||
                plan.Guests.Any(n => !GuestsFor(plan.Destination).Contains(n)))
                throw new InvalidOperationException("A traveler or selected guest is no longer available.");
            NeuralNPC speaker = NeuralNPC.currentActiveDialogNeuralNPC;
            Arrival arrival = MeetingController.Begin(speaker, plan.Destination.Zone)
                ?? throw new InvalidOperationException("The conversation is no longer available for travel.");
            arrival.RequestedGuests = new List<NeuralNPC>(plan.Guests);
            arrival.GuestPositions = plan.Guests.Select((npc, i) => (npc, pos: plan.Positions[1 + plan.Travelers.Count + i]))
                .ToDictionary(pair => pair.npc, pair => pair.pos);
            MoveParty(plan);
            string message = "Location changed. " + arrival.PreviousZone + " -> " + arrival.Destination +
                ". " + Player.Instance.playerName + " brought the conversation group here.";
            foreach (var history in plan.Travelers.Select(n => n.dialogElements).Distinct()) history.AddToDialog(SpeakerType.System, message);
            await MeetingController.AfterTravel(Task.CompletedTask, arrival);
            MeetingController.Notify("Traveled to " + plan.Destination.Zone + (plan.Guests.Count == 0 ? ". No arrival meeting requested." : "."));
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Manual conversation travel failed: " + ex);
            MeetingController.Notify("Travel could not finish: " + ex.Message);
            return false;
        }
        finally
        {
            try
            {
                if (faded && screen != null && screen == BlackScreen.Instance) await Fade(screen, fadeIn: false);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Travel fade cleanup failed: " + ex); }
            finally
            {
                Busy = false;
                if (saveBlocked && save != null) save.SetSaveBlock(false);
                if (lockRevision == revision && revision == MeetingController.ConversationRevision)
                {
                    NeuralNPC.npcFunctionsBeingInvoked = false;
                    lockRevision = -1;
                    if (box != null && box == DialogBox.Instance && box.isOpen) box.SetTalkAllowedState(true);
                }
            }
        }
    }

    internal static void MoveParty(TravelPlan plan)
    {
        var originals = plan.Travelers.ToDictionary(n => n, n => n.transform.GetVector2IntPosition());
        var overrides = new Dictionary<NeuralNPC, NPCRoutine>();
        try
        {
            plan.Player.entityMover.Teleport(plan.Positions[0]);
            for (int i = 0; i < plan.Travelers.Count; i++)
            {
                NeuralNPC npc = plan.Travelers[i];
                Vector2Int position = plan.Positions[i + 1];
                npc.GetComponent<EntityMover>().Teleport(position);
                string activity = "hanging out with " + plan.Player.playerName;
                var routine = new NPCRoutine(activity, activity, activity, position,
                    new RoutineArgument_EndOverrideIf_Overridden(), new RoutineArgument_EndOverrideIf_AfterTurns(Plugin.StayTurns.Value));
                overrides.Add(npc, routine);
                npc.GetComponent<NPCRoutineExecutor>().StartOverrideRoutine(routine);
                npc.GetComponent<EntityMover>().TurnTowards(plan.Positions[0]);
            }
            plan.Player.entityMover.TurnTowards(plan.Positions[1]);
        }
        catch
        {
            foreach (NeuralNPC npc in plan.Travelers)
            {
                try
                {
                    var executor = npc.GetComponent<NPCRoutineExecutor>();
                    if (overrides.TryGetValue(npc, out var routine) &&
                        (executor.currentRoutine == routine || executor.preOverrideRoutines.Contains(routine))) executor.StopOverrideRoutine(routine);
                    if (npc.transform.GetVector2IntPosition() != originals[npc]) npc.GetComponent<EntityMover>().Teleport(originals[npc]);
                }
                catch (Exception ex) { Plugin.Log.LogWarning("Traveler rollback failed: " + ex); }
            }
            if (plan.Player.transform.GetVector2IntPosition() != plan.Origin) plan.Player.entityMover.Teleport(plan.Origin);
            throw;
        }
    }

    private static async Task Fade(BlackScreen screen, bool fadeIn)
    {
        bool done = false;
        if (fadeIn) screen.FadeIn(4f, fadeAudio: true, callback: () => done = true);
        else screen.FadeOut(4f, fadeAudio: true, callback: () => done = true);
        float deadline = Time.realtimeSinceStartup + 5f;
        while (!done && screen != null && screen == BlackScreen.Instance)
        {
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("The travel screen fade did not finish.");
            await Task.Yield();
        }
    }
}
