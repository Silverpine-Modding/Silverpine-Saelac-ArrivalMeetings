using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArrivalMeetings;

// Mirrors DebugUI.ShowDebugOptions: native GenericListUI rows opened over the current dialogue.
// No EndDialog or TriggerMultiDialog calls are needed to visit submenus or return.
internal sealed class TravelMenu
{
    private const int PageSize = 5;
    private static TravelMenu? active;
    private static bool drawing;
    private readonly GenericListUI list;
    private readonly DialogBox box;
    private readonly Player player;
    private readonly int revision;
    private readonly Vector2Int origin;
    private readonly List<NeuralNPC> travelers;
    private readonly List<TravelDestination> destinations;
    private readonly HashSet<NeuralNPC> selected = new();
    private TravelDestination? destination;
    private string owner = "";
    private bool meet;
    private int ownerPage, locationPage, guestPage;

    private TravelMenu(List<TravelDestination> locations)
    {
        list = GenericListUI.Instance;
        box = DialogBox.Instance;
        player = Player.Instance;
        revision = MeetingController.ConversationRevision;
        origin = player.transform.GetVector2IntPosition();
        travelers = MeetingController.Participants();
        destinations = locations;
    }

    internal static void Open()
    {
        if (!ManualTravel.CanStart || GenericListUI.Instance == null)
        { MeetingController.Notify("Wait for the current dialogue action to finish, then choose Travel."); return; }
        if (GenericListUI.Instance.gameObject.activeSelf)
        { MeetingController.Notify("Close the current list menu before opening Travel."); return; }
        try
        {
            var locations = TravelLocations.Snapshot();
            active = new TravelMenu(locations);
            active.box.SetTalkAllowedState(false);
            active.ShowOwners();
        }
        catch (Exception ex)
        {
            Cancel();
            Plugin.Log.LogWarning("Could not open conversation travel: " + ex);
            MeetingController.Notify("The travel menu could not open. See the BepInEx log for details.");
        }
    }

    private bool Valid => active == this && MeetingController.WorldReady && revision == MeetingController.ConversationRevision &&
        box == DialogBox.Instance && player == Player.Instance && player.transform.GetVector2IntPosition() == origin &&
        travelers.SequenceEqual(MeetingController.Participants());

    private ListUIItem_Generic Row(string label, Action? action = null, string detail = "") => new(label, null!, detail,
        action == null ? null! : () =>
        {
            if (!Valid) { if (active == this) Cancel(); return; }
            try { action(); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Conversation travel menu failed: " + ex);
                if (active == this) Cancel();
                MeetingController.Notify("The travel menu closed after an error. The conversation is still available.");
            }
        });

    private void Draw(List<ListUIItem_Generic> rows)
    {
        if (!Valid) { if (active == this) Cancel(); return; }
        rows.Add(Row("Cancel travel / return to conversation", () => Cancel()));
        drawing = true;
        try { list.Draw(rows); list.Open(); }
        finally { drawing = false; }
    }

    private void Pages(List<ListUIItem_Generic> rows, int page, int count, Action<int> change)
    {
        if (page > 0) rows.Add(Row("Previous page", () => change(page - 1)));
        if ((page + 1) * PageSize < count) rows.Add(Row("Next page", () => change(page + 1)));
    }

    private void ShowOwners()
    {
        string[] owners = destinations.Select(d => d.OwnerLabel).Distinct().ToArray();
        var rows = new List<ListUIItem_Generic> { Row("Travel — choose property owner", detail: destinations.Count + " named locations") };
        foreach (string category in owners.Skip(ownerPage * PageSize).Take(PageSize))
            rows.Add(Row(category, () => { owner = category; locationPage = 0; ShowLocations(); },
                destinations.Count(d => d.OwnerLabel == category) + " rooms / locations"));
        if (owners.Length == 0) rows.Add(Row("No named locations are currently loaded."));
        Pages(rows, ownerPage, owners.Length, page => { ownerPage = page; ShowOwners(); });
        Draw(rows);
    }

    private void ShowLocations()
    {
        var locations = destinations.Where(d => d.OwnerLabel == owner).ToArray();
        var rows = new List<ListUIItem_Generic> { Row("Property: " + owner, detail: "Choose a room or location") };
        foreach (TravelDestination room in locations.Skip(locationPage * PageSize).Take(PageSize))
            rows.Add(Row(room.Zone, () => { destination = room; selected.Clear(); meet = false; ShowMeetingChoice(); },
                room.Zone == MapZone.GetPositionZoneName(origin) ? "Current location" : ""));
        Pages(rows, locationPage, locations.Length, page => { locationPage = page; ShowLocations(); });
        rows.Add(Row("Back to property owners", ShowOwners));
        Draw(rows);
    }

    private void ShowMeetingChoice()
    {
        Draw(new List<ListUIItem_Generic>
        {
            Row("Destination: " + destination!.Zone, detail: "Meet anyone on arrival?"),
            Row("No — travel with the current conversation group", () => { meet = false; selected.Clear(); ShowReview(); }),
            Row("Yes — choose who to meet", () => { meet = true; guestPage = 0; ShowGuests(); }),
            Row("Back to locations", ShowLocations)
        });
    }

    private void ShowGuests()
    {
        List<NeuralNPC> candidates = ManualTravel.GuestsFor(destination!);
        selected.RemoveWhere(n => !candidates.Contains(n));
        guestPage = Math.Min(guestPage, Math.Max(0, (candidates.Count - 1) / PageSize));
        var rows = new List<ListUIItem_Generic>
        {
            Row("Meet at " + destination!.Zone, detail: selected.Count + " / " + Plugin.MaxGuests.Value + " selected"),
            Row("Review selected guests", selected.Count > 0 ? () => ShowReview() : null)
        };
        foreach (NeuralNPC npc in candidates.Skip(guestPage * PageSize).Take(PageSize))
        {
            string where = MapZone.GetPositionZoneName(npc.transform.GetVector2IntPosition()) == destination.Zone ? "Already at destination" : "Bring to destination";
            rows.Add(Row((selected.Contains(npc) ? "[x] " : "[ ] ") + npc.GetFinalName(), () =>
            {
                if (!selected.Remove(npc))
                {
                    if (selected.Count < Plugin.MaxGuests.Value) selected.Add(npc);
                    else MeetingController.Notify("Select at most " + Plugin.MaxGuests.Value + " guests.");
                }
                ShowGuests();
            }, where));
        }
        if (candidates.Count == 0) rows.Add(Row("No eligible guests are currently available."));
        Pages(rows, guestPage, candidates.Count, page => { guestPage = page; ShowGuests(); });
        rows.Add(Row("Back to meeting choice", ShowMeetingChoice));
        Draw(rows);
    }

    private void ShowReview(string error = "")
    {
        var rows = new List<ListUIItem_Generic>
        {
            Row("Travel to " + destination!.Zone, detail: destination.OwnerLabel),
            Row("Travelers", detail: player.playerName + ", " + string.Join(", ", travelers.Select(n => n.GetFinalName()))),
            Row(meet ? "Meet on arrival: Yes" : "Meet on arrival: No", detail:
                meet ? string.Join(", ", selected.Select(n => n.GetFinalName())) : "No guests will be added automatically"),
            Row("Travel now", !meet || selected.Count > 0 ? Commit : null),
            Row("Change meeting selection", ShowMeetingChoice),
            Row("Change destination", ShowLocations)
        };
        if (error.Length > 0) rows.Insert(0, Row(error));
        Draw(rows);
    }

    private async void Commit()
    {
        if (!ManualTravel.TryPlan(destination!, meet ? selected : Enumerable.Empty<NeuralNPC>(), out var plan, out string error))
        { ShowReview(error); return; }
        active = null; // Native Close now belongs to the committed operation; do not unlock dialogue early.
        list.Close();
        try { await ManualTravel.Execute(plan!); }
        catch (Exception ex) { Plugin.Log.LogWarning("Manual travel failed unexpectedly: " + ex); }
        finally
        {
            if (revision == MeetingController.ConversationRevision && box != null && box == DialogBox.Instance && box.isOpen &&
                !NeuralNPC.npcFunctionsBeingInvoked && !SaveUI.Instance.IsSavingBlocked()) box.SetTalkAllowedState(true);
        }
    }

    internal static void Cancel(bool restoreConversation = true, bool closeList = true)
    {
        TravelMenu? menu = active;
        if (menu == null) return;
        active = null;
        if (closeList && menu.list != null) menu.list.Close();
        if (restoreConversation && menu.revision == MeetingController.ConversationRevision && menu.box != null &&
            menu.box == DialogBox.Instance && menu.box.isOpen && !NeuralNPC.npcFunctionsBeingInvoked && !SaveUI.Instance.IsSavingBlocked())
            menu.box.SetTalkAllowedState(true);
    }

    internal static void OnNativeClose(GenericListUI list)
    {
        if (active?.list == list) Cancel(closeList: false);
    }
    internal static void OnNativeDraw(GenericListUI list)
    {
        // If Debug or another feature claims the shared list, relinquish only our own state.
        if (!drawing && active?.list == list) Cancel(closeList: false);
    }
}

[HarmonyPatch(typeof(GenericListUI), nameof(GenericListUI.Close))]
internal static class TravelListClosePatch
{
    private static void Postfix(GenericListUI __instance)
    { TravelMenu.OnNativeClose(__instance); ParticipantMenu.OnNativeClose(__instance); }
}

[HarmonyPatch(typeof(GenericListUI), nameof(GenericListUI.Draw))]
internal static class TravelListDrawPatch
{
    private static void Prefix(GenericListUI __instance)
    { TravelMenu.OnNativeDraw(__instance); ParticipantMenu.OnNativeDraw(__instance); }
}
