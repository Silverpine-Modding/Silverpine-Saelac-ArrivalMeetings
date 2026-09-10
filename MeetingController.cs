using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace ArrivalMeetings;

internal sealed class Arrival
{
    internal int Generation;
    internal NeuralNPC Owner = null!;
    internal Player Player = null!;
    internal DialogBox Box = null!;
    internal string Destination = "", PreviousZone = "";
    internal Vector2Int Before, At;
    internal bool Arrived;
    internal int Added;
    internal List<NeuralNPC> Travelers = new();
    internal List<NeuralNPC.DialogElement> History = new();
    // null follows automatic intent; an empty list explicitly means no meeting for manual travel.
    internal List<NeuralNPC>? RequestedGuests;
    internal Dictionary<NeuralNPC, Vector2Int>? GuestPositions;
}

internal static class MeetingController
{
    private static int generation;
    internal static int ConversationRevision { get; private set; }
    internal static Arrival? Current { get; private set; }
    internal static bool OwnsGroup { get; private set; }
    internal static bool RefreshPending;
    internal static string LastStatus = "Waiting for a dialogue location change.";

    internal static void Reset()
    {
        generation++;
        ConversationRevision++;
        Current = null;
        OwnsGroup = false;
        RefreshPending = false;
        MeetingPicker.Clear();
    }

    internal static List<NeuralNPC> Participants() =>
        (NeuralNPC.multiDialogParticipants ?? new List<NeuralNPC> { NeuralNPC.currentActiveDialogNeuralNPC })
        .Where(n => n != null).Distinct().ToList();

    internal static bool WorldReady => Plugin.EnabledSetting.Value && !SerializationManager.loadingSave &&
        Player.Instance != null && !Player.Instance.inCombat && DialogBox.Instance != null &&
        DialogBox.Instance.isOpen && NeuralNPC.currentActiveDialogNeuralNPC != null &&
        DungeonGenerationManager.Instance != null && !DungeonGenerationManager.Instance.playerInInstance;

    internal static Arrival? Begin(NeuralNPC owner, string destination)
    {
        if (!WorldReady || owner == null || !Participants().Contains(owner)) return null;
        generation++;
        Current = null;
        RefreshPending = false;
        var history = owner.dialogElements;
        int marker = history.FindLastIndex(d => d.speakerType == SpeakerType.NewDialogMarker);
        return new Arrival
        {
            Generation = generation, Owner = owner, Player = Player.Instance, Box = DialogBox.Instance,
            Destination = destination, Before = Player.Instance.transform.GetVector2IntPosition(),
            PreviousZone = MapZone.GetPositionZoneName(Player.Instance.transform.GetVector2IntPosition()),
            Travelers = Participants(),
            History = history.Skip(Math.Max(0, marker + 1)).TakeLast(Plugin.HistoryLength.Value).ToList()
        };
    }

    private static bool SameConversation(Arrival arrival) => WorldReady && arrival.Generation == generation &&
        arrival.Player == Player.Instance && arrival.Box == DialogBox.Instance &&
        Participants().Any(n => arrival.Travelers.Contains(n));

    internal static bool IsCurrent(Arrival? arrival) => arrival != null && arrival == Current &&
        arrival.Arrived && SameConversation(arrival) &&
        Player.Instance.transform.GetVector2IntPosition() == arrival.At &&
        MapZone.GetPositionZoneName(arrival.At) == arrival.Destination;

    internal static bool CanOffer => !ParticipantExchange.Busy && IsCurrent(Current) && Current!.Added < Plugin.MaxGuests.Value;
    internal static bool CanSelect => CanOffer && !NeuralNPC.npcFunctionsBeingInvoked &&
        !DialogBox.Instance.isAnimatingText && !SaveUI.Instance.IsSavingBlocked();

    internal static async Task AfterTravel(Task original, Arrival arrival)
    {
        // Preserve native cancellation/failure and complete only after the native fade and teleport.
        await original;
        try
        {
            if (!SameConversation(arrival)) return;
            Vector2Int position = arrival.Player.transform.GetVector2IntPosition();
            if (position == arrival.Before || (arrival.RequestedGuests == null && arrival.PreviousZone == arrival.Destination) ||
                MapZone.GetPositionZoneName(position) != arrival.Destination) return;
            arrival.At = position;
            arrival.Arrived = true;
            Current = arrival;
            RefreshPending = true;
            LastStatus = "Arrived at " + arrival.Destination + ". Meet on arrival is available in conversation actions.";
            if (arrival.RequestedGuests != null)
            {
                if (arrival.RequestedGuests.Count > 0) AddGuests(arrival, arrival.RequestedGuests);
                return;
            }
            if (!Plugin.Automatic.Value) return;
            List<NeuralNPC> candidates = Candidates(arrival, mentionedOnly: true).Take(24).ToList();
            if (candidates.Count == 0) return;
            LastStatus = "Checking meeting intent at " + arrival.Destination + ".";
            var names = candidates.Select(n => n.GetFinalName()).ToArray();
            string answer = await arrival.Owner.AskQuestion(
                MeetingIntent.Question(arrival.Destination, names, Plugin.MaxGuests.Value),
                deterministic: true, takes: -2,
                grammar: "root ::= \"NONE\" | \"MEET: \" [1-9] [0-9]* (\", \" [1-9] [0-9]*)*",
                targetDialogElements: arrival.History);
            if (!IsCurrent(arrival) || !Plugin.Automatic.Value) return;
            int[] selected = MeetingIntent.ParseSelection(answer, candidates.Count, Plugin.MaxGuests.Value);
            if (selected.Length == 0)
            {
                LastStatus = "No confirmed meeting targets at " + arrival.Destination + ". Use Meet on arrival to choose.";
                return;
            }
            AddGuests(arrival, selected.Select(index => candidates[index]));
        }
        catch (Exception ex)
        {
            LastStatus = "Automatic meeting check failed; manual selection is still available.";
            Plugin.Log.LogWarning("Arrival meeting check failed; native travel completed: " + ex);
        }
    }

    internal static bool Eligible(NeuralNPC npc, Arrival arrival)
    {
        return EligibleCharacter(npc) && !arrival.Travelers.Contains(npc) && !Participants().Contains(npc) &&
            (Plugin.BringAbsent.Value || MapZone.GetPositionZoneName(npc.transform.GetVector2IntPosition()) == arrival.Destination);
    }

    internal static bool EligibleCharacter(NeuralNPC npc)
    {
        if (npc == null || !npc.isActiveAndEnabled || !npc.gameObject.scene.IsValid() ||
            !NeuralNPC.neuralNPCs.TryGetValue(npc.npcName, out var registered) || registered != npc) return false;
        var routine = npc.GetComponent<NPCRoutineExecutor>();
        if (npc.GetComponent<EntityMover>() == null || routine == null || routine.currentRoutine == null ||
            !DialogBox.Instance.IsEndDialogAllowed(npc)) return false;
        if (npc.npcName == NPCName.Acacia && !Chainloader.PluginInfos.ContainsKey("salt.silverpine.acaciaunlocked")) return false;
        return true;
    }

    internal static void IncludeParticipant(NeuralNPC npc)
    {
        if (Participants().Contains(npc)) return;
        GuestLifecycle.Begin(npc);
        if (NeuralNPC.multiDialogParticipants == null) NeuralNPC.multiDialogParticipants = Participants();
        if (NeuralNPC.initialMultiDialogParticipants == null)
            NeuralNPC.initialMultiDialogParticipants = new List<NeuralNPC>(NeuralNPC.multiDialogParticipants);
        if (!NeuralNPC.initialMultiDialogParticipants.Contains(npc)) NeuralNPC.initialMultiDialogParticipants.Add(npc);
        NeuralNPC.multiDialogParticipants.Add(npc);
        OwnsGroup = true;
        foreach (NeuralNPC participant in Participants()) participant.lastDialogWasMulti = true;
    }

    internal static void OnParticipantsChanged()
    {
        generation++;
        ConversationRevision++;
        Current = null;
        RefreshPending = false;
        OwnsGroup = true;
    }

    internal static List<NeuralNPC> Candidates(Arrival arrival, bool mentionedOnly)
    {
        var all = NeuralNPC.neuralNPCs.Values.Where(n => n != null).Distinct().ToArray();
        var names = all.Select(n => n.GetFinalName()).Append(Player.Instance.playerName).ToArray();
        // System/location messages must not turn a building owner's name into a meeting request.
        string recent = string.Join("\n", arrival.History.Where(d =>
            d.speakerType == SpeakerType.Player || d.speakerType == SpeakerType.NPC).Select(d => d.contents));
        return all.Where(n => Eligible(n, arrival))
            .Where(n => !mentionedOnly || MeetingIntent.Mentions(recent, n.GetFinalName(), names))
            .OrderByDescending(n => MeetingIntent.Mentions(recent, n.GetFinalName(), names))
            .ThenBy(n => n.GetFinalName(), StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static int AddGuests(Arrival arrival, IEnumerable<NeuralNPC> requested)
    {
        if (!IsCurrent(arrival)) return 0;
        var added = new List<NeuralNPC>();
        foreach (NeuralNPC npc in requested.Distinct().Take(Math.Max(0, Plugin.MaxGuests.Value - arrival.Added)))
        {
            if (!IsCurrent(arrival) || !Eligible(npc, arrival)) continue;
            Vector2Int oldPosition = npc.transform.GetVector2IntPosition();
            bool alreadyNear = MapZone.GetPositionZoneName(oldPosition) == arrival.Destination &&
                Math.Abs(oldPosition.x - arrival.At.x) <= 2 && Math.Abs(oldPosition.y - arrival.At.y) <= 2 &&
                arrival.Owner.CanSeeTransform(npc.transform);
            Vector2Int reserved = default;
            bool hasReservation = arrival.GuestPositions != null && arrival.GuestPositions.TryGetValue(npc, out reserved);
            if (hasReservation && !TravelLocations.SafeTile(reserved, arrival.Destination))
            {
                Notify("The reserved meeting tile is no longer available for " + npc.GetFinalName() + ".");
                continue;
            }
            // Prefer unoccupied nearby tiles, but sharing a tile with the player or another NPC is valid.
            var targets = TravelLocations.MeetingTiles(arrival.At, arrival.Destination)
                .OrderBy(p => Turfs.IsOccupiedByCharacter(p))
                .ThenBy(p => Math.Abs(p.x - arrival.At.x) + Math.Abs(p.y - arrival.At.y)).ToList();
            if (!alreadyNear && !hasReservation && targets.Count == 0)
            {
                Notify("No valid meeting tile in this room for " + npc.GetFinalName() + ".");
                continue;
            }
            Vector2Int target = alreadyNear ? oldPosition : hasReservation ? reserved : targets[0];
            var mover = npc.GetComponent<EntityMover>();
            var executor = npc.GetComponent<NPCRoutineExecutor>();
            NPCRoutine? meetingRoutine = null;
            try
            {
                // Load portraits before moving or changing conversation membership.
                GuestLifecycle.Prepare(npc);
                if (!alreadyNear) mover.Teleport(target);
                string activity = "meeting " + Player.Instance.playerName;
                meetingRoutine = new NPCRoutine(activity, activity, activity, target,
                    new RoutineArgument_EndOverrideIf_Overridden(), new RoutineArgument_EndOverrideIf_AfterTurns(Plugin.StayTurns.Value));
                executor.StartOverrideRoutine(meetingRoutine);
                IncludeParticipant(npc);
                arrival.Added++;
                added.Add(npc);
            }
            catch (Exception ex)
            {
                // If setup failed, return the existing character instead of leaving an invisible guest moved.
                try
                {
                    if (meetingRoutine != null && (executor.currentRoutine == meetingRoutine || executor.preOverrideRoutines.Contains(meetingRoutine)))
                        executor.StopOverrideRoutine(meetingRoutine);
                    if (npc.transform.GetVector2IntPosition() != oldPosition) mover.Teleport(oldPosition);
                    npc.ReleaseLargeAssets();
                }
                catch (Exception rollback) { Plugin.Log.LogWarning("Could not restore guest position/routine: " + rollback); }
                Plugin.Log.LogWarning("Could not add " + npc.GetFinalName() + " to arrival meeting: " + ex);
            }
        }
        if (added.Count > 0)
        {
            foreach (NeuralNPC participant in Participants()) participant.lastDialogWasMulti = true;
            string names = NeuralNPC.ToAnd(added.Select(n => n.GetFinalName()).ToList());
            string roster = NeuralNPC.ToAnd(Participants().Select(n => n.GetFinalName()).Prepend(Player.Instance.playerName).ToList());
            string message = names + " joined the ongoing conversation at " + arrival.Destination +
                ". Present participants: " + roster + ". The conversation continues here. " +
                "The newcomers did not hear the earlier conversation; explain earlier events to them if needed.";
            // Add once per distinct history: QuietlyAddSystemDialog itself broadcasts to the whole group.
            foreach (var history in Participants().Select(n => n.dialogElements).Distinct())
                history.AddToDialog(SpeakerType.System, message);
            LastStatus = names + " joined at " + arrival.Destination + ".";
            Notify(LastStatus);
            Plugin.Log.LogInfo(LastStatus);
            arrival.Box.StopContinueOnlyMode();
            if (NeuralNPC.npcFunctionsBeingInvoked) arrival.Box.SetTalkAllowedState(false);
        }
        else LastStatus = "No guests added at " + arrival.Destination + ".";
        return added.Count;
    }

    internal static void Notify(string text)
    {
        if (UpperNotificationUI.Instance != null) UpperNotificationUI.Instance.OneOff(text);
    }
}

internal static class GuestLifecycle
{
    internal static void Prepare(NeuralNPC npc)
    {
        // Native group cleanup retains departed members until the whole dialogue ends.
        // Rejoining the same scene reuses their loaded portrait and entry bookkeeping.
        if (NeuralNPC.initialMultiDialogParticipants?.Contains(npc) == true) return;
        Sprite? portrait;
        if (CustomContentDefinition_NPC.loaded.TryGetValue(npc.GetFinalName(), out var definition) && definition.enabled &&
            (npc.IsCustomNPC() || definition.overrideExisting) &&
            CustomContentDefinition_PlayerCharacter.loaded.TryGetValue(definition.customPlayerCharacterDefinitionName, out var art))
        {
            art.ClaimLargeAssets(npc.gameObject);
            portrait = art.assets.dialogSprite;
            npc.dialogNudeSprite = art.dialogNudeSprite;
            Set(npc, "expressionSprites", art.assets.expressionSprites);
        }
        else
        {
            portrait = Resources.Load<Sprite>("Sprites/NPC/sprite_npc_" + npc.npcName.ToString().ToLowerInvariant() + "_big");
            Set(npc, "expressionSprites", Resources.LoadAll<Sprite>($"Sprites/NPC/Expressions/{npc.npcName}").ToList());
        }
        Set(npc, "dialogSprite", portrait);
    }

    internal static void Begin(NeuralNPC npc)
    {
        if (NeuralNPC.initialMultiDialogParticipants?.Contains(npc) == true) return;
        // Match native group-entry bookkeeping without TriggerMultiDialog, which starts a new scene for everyone.
        var mover = npc.GetComponent<EntityMover>();
        Set(npc, "preDialogVisualDirection", mover.currentVisualDirection);
        Set(npc, "preDialogRelationshipLevel", npc.relationshipLevel);
        Set(npc, "dialogTimeBias", 0);
        Set(npc, "currentExpression", null);
        (AccessTools.Field(typeof(NeuralNPC), "OnBeforeDialogBegins").GetValue(npc) as Action)?.Invoke();
        npc.dialogElements.MarkNewDialog();
        npc.playerLeft = false;
        npc.lastDialogWasMulti = true;
        if (npc.firstTalkedToPlayerTurnCount == -1) npc.firstTalkedToPlayerTurnCount = WorldInfoManager.Instance.TotalTurnCount;
        Set(npc, "lastTalkedToPlayerTurnCount", WorldInfoManager.Instance.TotalTurnCount);
        Set(npc, "metBefore", true);
        mover.TurnTowards(Player.Instance.transform.GetVector2IntPosition());
    }

    private static void Set(NeuralNPC npc, string field, object? value) => AccessTools.Field(typeof(NeuralNPC), field).SetValue(npc, value);
}
