using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArrivalMeetings;

internal sealed class ParticipantMenu
{
    private const int PageSize = 5;
    private static ParticipantMenu? active;
    private static bool drawing;
    private readonly GenericListUI list = GenericListUI.Instance;
    private readonly DialogBox box = DialogBox.Instance;
    private readonly Player player = Player.Instance;
    private readonly NeuralNPC speaker = NeuralNPC.currentActiveDialogNeuralNPC;
    private readonly int revision = MeetingController.ConversationRevision;
    private readonly Vector2Int origin = Player.Instance.transform.GetVector2IntPosition();
    private readonly List<NeuralNPC> original = MeetingController.Participants();
    private readonly HashSet<NeuralNPC> selected;
    private readonly ListMenuChrome chrome = new();
    private int page;

    private ParticipantMenu() => selected = new HashSet<NeuralNPC>(original);

    internal static void Open()
    {
        if (!ParticipantController.CanEdit || GenericListUI.Instance == null)
        { MeetingController.Notify("Wait for the current dialogue action to finish. Forced encounters cannot be edited."); return; }
        if (GenericListUI.Instance.gameObject.activeSelf)
        { MeetingController.Notify("Close the current list menu before opening Participants."); return; }
        try
        {
            active = new ParticipantMenu();
            active.box.SetTalkAllowedState(false);
            active.Show();
        }
        catch (Exception ex)
        {
            Cancel();
            Plugin.Log.LogWarning("Could not open conversation participants: " + ex);
        }
    }

    private bool Valid => active == this && ParticipantController.CanEdit && revision == MeetingController.ConversationRevision &&
        box == DialogBox.Instance && player == Player.Instance && player.transform.GetVector2IntPosition() == origin &&
        speaker == NeuralNPC.currentActiveDialogNeuralNPC && original.SequenceEqual(MeetingController.Participants());

    private ListUIItem_Generic Row(string label, Action? action = null, string detail = "") => new(label, null!, detail,
        action == null ? null! : () =>
        {
            if (!Valid) { if (active == this) Cancel(); return; }
            try { action(); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Participant menu failed: " + ex);
                if (active == this) Cancel();
                MeetingController.Notify("Could not complete the participant change. See the BepInEx log.");
            }
        });

    private void Show()
    {
        if (!Valid) { if (active == this) Cancel(); return; }
        List<NeuralNPC> nearby = ParticipantController.Nearby();
        List<NeuralNPC> choices = original.Concat(nearby).ToList();
        selected.RemoveWhere(n => !choices.Contains(n));
        page = Math.Min(page, Math.Max(0, (choices.Count - 1) / PageSize));
        int adding = selected.Count(n => !original.Contains(n));
        int removing = original.Count(n => !selected.Contains(n));
        var rows = new List<ListUIItem_Generic>
        {
            Row("Participants — select who stays", detail: "Checked: in conversation. Unchecked: outside conversation."),
            Row(selected.Count == 0 ? "Apply changes — end conversation" : "Apply changes", Commit,
                adding + " to add, " + removing + " to remove")
        };
        foreach (NeuralNPC npc in choices.Skip(page * PageSize).Take(PageSize))
        {
            string detail = original.Contains(npc) ? (selected.Contains(npc) ? "Current participant" : "Will leave conversation") :
                (selected.Contains(npc) ? "Will join conversation" : "Nearby and visible");
            rows.Add(Row((selected.Contains(npc) ? "[x] " : "[ ] ") + npc.GetFinalName(), () =>
            { if (!selected.Remove(npc)) selected.Add(npc); Show(); }, detail));
        }
        if (nearby.Count == 0) rows.Add(Row("No other nearby visible NPCs."));
        if (page > 0) rows.Add(Row("Previous page", () => { page--; Show(); }));
        if ((page + 1) * PageSize < choices.Count) rows.Add(Row("Next page", () => { page++; Show(); }));
        rows.Add(Row("Cancel / return to conversation", () => Cancel()));
        drawing = true;
        try { list.Draw(rows); chrome.HideDebugClose(list); list.Open(); }
        finally { drawing = false; }
    }

    private async void Commit()
    {
        active = null;
        list.Close();
        chrome.Restore();
        try
        {
            await ParticipantExchange.Start(original, selected);
        }
        finally
        {
            if (revision == MeetingController.ConversationRevision && box == DialogBox.Instance && box.isOpen &&
                !box.isAnimatingText && !NeuralNPC.npcFunctionsBeingInvoked && !SaveUI.Instance.IsSavingBlocked())
                box.SetTalkAllowedState(true);
        }
    }

    internal static void Cancel(bool restoreConversation = true, bool closeList = true)
    {
        ParticipantMenu? menu = active;
        if (menu == null) return;
        active = null;
        if (closeList && menu.list != null) menu.list.Close();
        menu.chrome.Restore();
        if (restoreConversation && menu.revision == MeetingController.ConversationRevision && menu.box == DialogBox.Instance &&
            menu.box.isOpen && !NeuralNPC.npcFunctionsBeingInvoked && !SaveUI.Instance.IsSavingBlocked()) menu.box.SetTalkAllowedState(true);
    }

    internal static void OnNativeClose(GenericListUI list) { if (active?.list == list) Cancel(closeList: false); }
    internal static void OnNativeDraw(GenericListUI list) { if (!drawing && active?.list == list) Cancel(closeList: false); }
}
