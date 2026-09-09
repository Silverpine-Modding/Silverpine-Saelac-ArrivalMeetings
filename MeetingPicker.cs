using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArrivalMeetings;

internal static class MeetingPicker
{
    private sealed class Snapshot
    {
        internal DialogBox Box = null!;
        internal string Text = "";
        internal Action<string> Input = null!;
        internal List<UpperButtonOption> Buttons = new();
        internal Action? Finished;
    }
    private static Snapshot? snapshot;

    internal static void Clear() => snapshot = null;

    internal static void Remember(DialogBox box, string text, Action<string> input,
        List<UpperButtonOption> buttons, Action? finished)
    {
        snapshot = new Snapshot { Box = box, Text = text, Input = input,
            Buttons = new List<UpperButtonOption>(buttons), Finished = finished };
    }

    internal static void Restore()
    {
        Snapshot? previous = snapshot;
        if (previous == null || previous.Box != DialogBox.Instance || !MeetingController.IsCurrent(MeetingController.Current)) return;
        Action<string> callback = MeetingController.OwnsGroup ? text => NeuralNPC.OnMultiInputCallback(null, text) : previous.Input;
        Action? finished = MeetingController.OwnsGroup ? null : previous.Finished;
        previous.Box.DisplayText(previous.Text, callback, previous.Buttons, finished);
    }

    internal static void Open()
    {
        if (!MeetingController.CanSelect || snapshot == null)
        {
            MeetingController.Notify("Wait for the current dialogue action to finish.");
            return;
        }
        Arrival arrival = MeetingController.Current!;
        // Capture native actions added since DisplayText, such as Follow, before showing the picker.
        if (AccessTools.Field(typeof(DialogBox), "lastUpperButtonOptions").GetValue(arrival.Box) is List<UpperButtonOption> buttons)
            snapshot.Buttons = new List<UpperButtonOption>(buttons);
        List<NeuralNPC> candidates = MeetingController.Candidates(arrival, mentionedOnly: false);
        var selected = new HashSet<NeuralNPC>();
        const int pageSize = 4;
        Show(0);

        void Show(int page)
        {
            if (!MeetingController.IsCurrent(arrival)) return;
            int pages = Math.Max(1, (candidates.Count + pageSize - 1) / pageSize);
            page = Math.Max(0, Math.Min(page, pages - 1));
            int capturedPage = page;
            var options = new List<DialogOption>();
            foreach (NeuralNPC npc in candidates.Skip(page * pageSize).Take(pageSize))
            {
                NeuralNPC guest = npc;
                if (!MeetingController.Eligible(guest, arrival)) continue;
                string location = MapZone.GetPositionZoneName(guest.transform.GetVector2IntPosition()) == arrival.Destination
                    ? "here" : "bring here";
                options.Add(new DialogOption((selected.Contains(guest) ? "[x] " : "[ ] ") + guest.GetFinalName() + " (" + location + ")", () =>
                {
                    if (!selected.Remove(guest))
                    {
                        if (selected.Count + arrival.Added < Plugin.MaxGuests.Value) selected.Add(guest);
                        else MeetingController.Notify("The guest limit for this arrival is " + Plugin.MaxGuests.Value + ".");
                    }
                    Show(capturedPage);
                }, endDialog: false));
            }
            if (page > 0) options.Add(new DialogOption("Previous page", () => Show(capturedPage - 1), endDialog: false));
            if (page + 1 < pages) options.Add(new DialogOption("Next page", () => Show(capturedPage + 1), endDialog: false));
            if (selected.Count > 0) options.Add(new DialogOption("Add selected (" + selected.Count + ")", () =>
            {
                if (!MeetingController.IsCurrent(arrival)) return;
                try { MeetingController.AddGuests(arrival, candidates.Where(selected.Contains)); }
                catch (Exception ex) { Plugin.Log.LogWarning("Manual arrival meeting failed: " + ex); }
                finally { Restore(); }
            }, endDialog: false));
            options.Add(new DialogOption("Back to conversation", Restore, endDialog: false));
            arrival.Box.DisplayTextNoDialog(
                "Meet at " + arrival.Destination + ". Select people to join the ongoing conversation.\n" +
                (Plugin.BringAbsent.Value ? "People marked 'bring here' will be moved to this room.\n" : "Only people already in this room are listed.\n") +
                (candidates.Count == 0 ? "No eligible people are available." : "Page " + (page + 1) + " of " + pages + ". " +
                    (Plugin.MaxGuests.Value - arrival.Added) + " guest places remaining."), options.ToArray());
        }
    }
}
