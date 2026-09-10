using ArrivalMeetings;
using UnityEngine;

internal static class ParticipantChecks
{
    internal static async Task Run(Action<bool, string> check, Func<(NeuralNPC owner, NeuralNPC alice, NeuralNPC bob)> world)
    {
        bool Apply(params NeuralNPC[] selected) => ParticipantController.Apply(MeetingController.Participants(), selected, out _);
        void Click(string label) => GenericListUI.Instance.Rows.Single(r => r.name == label).callback();
        void Continue() => DialogBox.Instance.Options.Single(o => o.label.StartsWith("Continue")).callback();
        var w = world();
        check(ParticipantController.CanOffer && MeetingController.Current == null, "Participants is available before any location arrival");
        w.alice.transform.Position = new(2, 2); w.bob.transform.Position = new(3, 0);
        check(ParticipantController.Nearby().SequenceEqual(new[] { w.alice }), "nearby selection uses a two-tile radius around the player or speaker");
        w.owner.Visible = t => t != w.alice.transform;
        check(ParticipantController.Nearby().Count == 0, "NPCs hidden behind walls cannot be pulled into the conversation");
        w.owner.Visible = _ => true; w.alice.Locked = true;
        check(ParticipantController.Nearby().Count == 0, "forced-encounter NPCs cannot be added");
        w.alice.Locked = false; w.owner.Locked = true;
        check(!ParticipantController.CanEdit, "forced active encounters cannot be edited");

        w = world(); w.alice.transform.Position = new(0, 0); w.bob.transform.Position = new(1, 0);
        Plugin.BringAbsent.Value = Plugin.Automatic.Value = false; Plugin.MaxGuests.Value = 1;
        check(Apply(w.owner, w.alice, w.bob), "nearby additions work independently of arrival inference, relocation settings and guest caps");
        check(w.alice.GetComponent<EntityMover>().Moves == 0 && w.bob.GetComponent<EntityMover>().Moves == 0,
            "nearby participant editing does not teleport characters or require free tiles");
        check(NeuralNPC.initialMultiDialogParticipants!.Count == 3 && MeetingController.OwnsGroup,
            "nearby joining converts a solo dialogue and registers everyone for native cleanup");
        check(!w.alice.dialogElements.Any(d => d.contents == "PRIVATE_OLD_HISTORY") && w.alice.dialogElements.Any(d => d.contents == "ALICE_PRIVATE_HISTORY"),
            "a newcomer retains personal history without receiving the earlier private conversation");
        check(Apply(w.alice) && NeuralNPC.currentActiveDialogNeuralNPC == w.alice && DialogBox.Instance.isOpen,
            "removing the active speaker transfers the portrait and keeps a one-NPC conversation open");
        check(NeuralNPC.initialMultiDialogParticipants.Count == 3 && w.owner.Cleanups == 0 && w.owner.Releases == 0,
            "departed NPCs retain native deferred cleanup and portrait resources until the dialogue ends");
        int departedHistory = w.owner.dialogElements.Count;
        DialogBox.Instance.Input!("Only Alice should hear this.");
        check(w.owner.dialogElements.Count == departedHistory && w.alice.dialogElements.Last().contents == "Only Alice should hear this.",
            "the refreshed input callback excludes removed participants from subsequent turns");
        int beforeRejoin = 0; w.owner.OnBeforeDialogBegins = () => beforeRejoin++;
        check(Apply(w.alice, w.owner) && beforeRejoin == 0 && NeuralNPC.initialMultiDialogParticipants.Count == 3,
            "rejoining the same scene does not duplicate native entry lifecycle or cleanup membership");
        check(Apply() && !DialogBox.Instance.isOpen && w.owner.Cleanups == 1 && w.alice.Cleanups == 1 && w.bob.Cleanups == 1,
            "removing everyone uses native dialogue end and cleans up current and departed participants exactly once");

        w = world(); w.alice.transform.Position = new(1, 0);
        w.alice.Custom = true;
        CustomContentDefinition_NPC.loaded[w.alice.Name] = new() { enabled = true, customPlayerCharacterDefinitionName = "bad" };
        CustomContentDefinition_PlayerCharacter.loaded["bad"] = new() { Fail = true };
        check(!Apply(w.alice) && MeetingController.Participants().SequenceEqual(new[] { w.owner }) && DialogBox.Instance.isOpen,
            "a failed replacement portrait never removes the original conversation partner");

        w = world(); w.alice.transform.Position = new(1, 0);
        ParticipantMenu.Open(); Click("[ ] Alice Vale");
        check(GenericListUI.Instance.Rows.Single(r => r.name == "Apply changes").suffix == "1 to add, 0 to remove",
            "participant menu previews selected additions and removals");
        Click("Cancel / return to conversation");
        check(MeetingController.Participants().Count == 1 && DialogBox.Instance.TalkAllowed, "canceling participant selection leaves the roster unchanged");
        ParticipantMenu.Open(); GenericListUI.Instance.Close();
        check(DialogBox.Instance.TalkAllowed, "closing the native participant list restores dialogue input");
        ParticipantMenu.Open();
        GenericListUI.Instance.Draw(new[] { new ListUIItem_Generic("Debug", null!, "", () => { }) });
        check(DialogBox.Instance.TalkAllowed && GenericListUI.Instance.Rows.Single().name == "Debug", "another native menu can take over the participant list");
        GenericListUI.Instance.Close(); ParticipantMenu.Open();
        var staleApply = GenericListUI.Instance.Rows.Single(r => r.name == "Apply changes").callback;
        GenericListUI.Instance.Close(); ParticipantMenu.Open(); staleApply();
        check(GenericListUI.Instance.gameObject.activeSelf && !DialogBox.Instance.TalkAllowed, "stale participant menu callbacks cannot close a newer menu");
        Click("[ ] Alice Vale"); w.alice.transform.Position = new(50, 0); Click("Apply changes");
        check(MeetingController.Participants().Count == 1 && DialogBox.Instance.TalkAllowed, "an NPC that moves out of range before applying is not added");

        w = world(); w.alice.transform.Position = new(1, 0);
        w.alice.Answer = () => Task.FromResult("Hello, everyone!");
        w.owner.Answer = () => Task.FromResult("Goodbye for now.");
        Task exchange = ParticipantExchange.Start(MeetingController.Participants(), new[] { w.alice });
        check(!exchange.IsCompleted && MeetingController.Participants().Contains(w.owner) && DialogBox.Instance.Text.Contains("Hello, everyone!"),
            "newcomer gives a generated greeting before the outgoing participant leaves");
        check(w.alice.LastTakes == -1 && w.alice.LastQuestion.Contains("did not hear") && SaveUI.Instance.IsSavingBlocked(),
            "greetings use the full native character prompt, preserve absence context and block saving during the exchange");
        Continue();
        check(DialogBox.Instance.Text.Contains("Goodbye for now.") && MeetingController.Participants().Contains(w.owner),
            "the departing NPC gives a generated farewell while still in the conversation");
        Continue(); await exchange;
        check(MeetingController.Participants().SequenceEqual(new[] { w.alice }) && NeuralNPC.currentActiveDialogNeuralNPC == w.alice &&
            !ParticipantExchange.Busy && SaveUI.Instance.Blocks == 0 && DialogBox.Instance.TalkAllowed,
            "farewell acknowledgement removes the speaker and restores normal conversation input and saving");
        check(w.alice.dialogElements.Any(d => d.speakerType == SpeakerType.NPC && d.contents.Contains("Goodbye for now.")) && w.owner.Questions == 1,
            "the farewell is recorded for participants who heard it without generating unrelated game actions");

        w = world(); w.owner.Answer = () => Task.FromResult("See you later.");
        exchange = ParticipantExchange.Start(MeetingController.Participants(), Array.Empty<NeuralNPC>());
        check(DialogBox.Instance.isOpen && DialogBox.Instance.Text.Contains("See you later."), "the last NPC says farewell before the conversation closes");
        Continue(); await exchange;
        check(!DialogBox.Instance.isOpen && w.owner.Cleanups == 1 && SaveUI.Instance.Blocks == 0, "last-NPC farewell ends and unlocks the native dialogue");

        w = world(); w.owner.Answer = () => Task.FromException<string>(new Exception("Model unavailable"));
        exchange = ParticipantExchange.Start(MeetingController.Participants(), Array.Empty<NeuralNPC>());
        check(DialogBox.Instance.Text.Contains("could not be generated") && !w.owner.dialogElements.Any(d => d.speakerType == SpeakerType.NPC),
            "generation failure shows a clear fallback instead of inventing an NPC farewell");
        Continue(); await exchange;
        check(!DialogBox.Instance.isOpen && SaveUI.Instance.Blocks == 0, "model failure does not trap the player or prevent removal");

        w = world();
        var pending = new TaskCompletionSource<string>(); w.owner.Answer = () => pending.Task;
        var oldSave = SaveUI.Instance;
        exchange = ParticipantExchange.Start(MeetingController.Participants(), Array.Empty<NeuralNPC>());
        DialogBox.Instance.Options.Single(o => o.label == "Cancel remaining changes").callback();
        await exchange;
        pending.SetResult("A late goodbye."); await Task.Yield();
        check(DialogBox.Instance.isOpen && MeetingController.Participants().Contains(w.owner) && !ParticipantExchange.Busy && oldSave.Blocks == 0 &&
            !w.owner.dialogElements.Any(d => d.contents.Contains("A late goodbye")), "canceling a pending farewell keeps its NPC and rejects the late model response");

        w = world(); pending = new(); w.owner.Answer = () => pending.Task;
        exchange = ParticipantExchange.Start(MeetingController.Participants(), Array.Empty<NeuralNPC>());
        var oldOwner = w.owner; oldSave = SaveUI.Instance;
        world(); pending.SetResult("Wrong scene goodbye."); await exchange;
        check(oldSave.Blocks == 0 && !oldOwner.dialogElements.Any(d => d.contents.Contains("Wrong scene")) && DialogBox.Instance.Text == "",
            "a new conversation cancels pending speech without overwriting its UI, history or saving state");
        DialogBox.Instance.SetNotificationMode();
        check(!ParticipantController.CanOffer, "Participants is hidden in notifications that are not NPC conversations");
    }
}
