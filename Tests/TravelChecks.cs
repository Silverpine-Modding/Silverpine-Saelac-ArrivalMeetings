using ArrivalMeetings;
using UnityEngine;

internal static class TravelChecks
{
    internal static async Task Run(Action<bool, string> check, Func<(NeuralNPC owner, NeuralNPC alice, NeuralNPC bob)> world)
    {
        void Room(string name = "Tavern", int x = 10, int y = 10, int width = 6, int height = 6)
        {
            for (int xx = x; xx < x + width; xx++) for (int yy = y; yy < y + height; yy++)
            { var p = new Vector2Int(xx, yy); MapZone.Register(p, name); Turfs.Valid.Add(p); }
        }
        TravelDestination Destination(string name = "Tavern") => TravelLocations.Snapshot().First(d => d.Zone == name);
        TravelPlan Plan(TravelDestination destination, params NeuralNPC[] guests)
        {
            bool success = ManualTravel.TryPlan(destination, guests, out var plan, out var error);
            if (!success) throw new Exception("Plan failed: " + error);
            return plan!;
        }
        void Click(string name) => GenericListUI.Instance.Rows.Single(row => row.name == name).callback();

        var w = world();
        Plot.allPlots.Add(new() { bounds = new(10, 10, 12, 12), owners = new() { NPCName.Alice } });
        Room("Tavern - Kitchen"); Room("Tavern - Bedroom", 16, 10);
        Room("Custom Workshop", 50, 50);
        var catalog = TravelLocations.Snapshot();
        check(catalog.Count(d => d.OwnerLabel == "Alice Vale") == 2, "all named rooms inside a property share its owner category");
        check(catalog.Any(d => d.Zone == "Custom Workshop" && d.OwnerLabel == "Public / unowned"), "custom/unowned locations appear without faction restrictions");
        Room("Alice's Annex", 70, 70);
        check(Destination("Alice's Annex").OwnerLabel == "Public / unowned", "room names do not invent ownership outside plot boundaries");
        Plot.allPlots[0].owners.Add(NPCName.Bob);
        check(Destination("Tavern - Kitchen").OwnerLabel == "Alice Vale & Bob", "shared property owners are grouped together");
        WorldInfoManager.Instance.ownedPlots.Add(NPCName.Alice);
        check(Destination("Tavern - Kitchen").OwnerLabel == "Player property (Player)", "native player-owned property gets a player category");

        var islands = new[] { new Vector2Int(0, 0), new(0, 1), new(8, 0), new(8, 1) };
        var sharedFormation = TravelLocations.Formation(islands, 3);
        check(sharedFormation.Count == 3 && sharedFormation.All(p => p.x == 0) && sharedFormation.Distinct().Count() == 2,
            "small disconnected areas share tiles within one area instead of rejecting or scattering the group");
        var formation = TravelLocations.Formation(new[] { new Vector2Int(0, 0), new(0, 1), new(1, 1), new(1, 2) }, 4);
        check(formation.Count == 4 && formation.Distinct().Count() == 4, "connected formation reserves unique player/traveler/guest tiles");
        check(TravelLocations.Formation(Array.Empty<Vector2Int>(), 4).Count == 0, "a destination still needs at least one valid tile");

        w = world(); Room();
        w.owner.Answer = () => Task.FromResult("MEET: 1");
        var p = Plan(Destination());
        check(await ManualTravel.Execute(p), "manual travel executes without asking the model to approve the destination");
        check(w.owner.Questions == 0 && MeetingController.Current?.Added == 0 && w.alice.GetComponent<EntityMover>().Moves == 0,
            "No meeting overrides automatic intent for that trip");
        check(Player.Instance.transform.Position == p.Positions[0] && w.owner.transform.Position == p.Positions[1],
            "manual travel brings the player and the active conversation partner");
        check(!SaveUI.Instance.IsSavingBlocked() && !NeuralNPC.npcFunctionsBeingInvoked && !ManualTravel.Busy && DialogBox.Instance.TalkAllowed,
            "manual travel releases save and dialogue locks");
        check(w.owner.dialogElements.Any(d => d.contents.Contains("Location changed.")), "travel location is recorded in the ongoing conversation");

        w = world(); Room(); Plugin.Automatic.Value = false;
        p = Plan(Destination(), w.bob);
        check(await ManualTravel.Execute(p) && MeetingController.Current?.Added == 1, "explicit guest selection works with automatic intent disabled");
        check(NeuralNPC.multiDialogParticipants!.SequenceEqual(new[] { w.owner, w.bob }) && w.owner.Questions == 0,
            "only selected guests join as native multi-NPC participants without an inference request");
        check(w.bob.transform.Position == p.Positions[2] && w.alice.GetComponent<EntityMover>().Moves == 0,
            "selected absent guest uses its reserved destination tile and unselected NPC stays put");

        w = world(); Room();
        NeuralNPC.multiDialogParticipants = new() { w.owner, w.alice };
        NeuralNPC.initialMultiDialogParticipants = new(NeuralNPC.multiDialogParticipants);
        p = Plan(Destination(), w.bob);
        check(await ManualTravel.Execute(p) && p.Travelers.All(n => MapZone.GetPositionZoneName(n.transform.Position) == "Tavern"),
            "all existing group members travel together");
        check(NeuralNPC.initialMultiDialogParticipants.Count == 3 && w.alice.dialogElements.Any(d => d.contents == "ALICE_PRIVATE_HISTORY"),
            "group travel preserves histories and extends native cleanup membership");

        w = world(); Room(width: 1, height: 1);
        NeuralNPC.multiDialogParticipants = new() { w.owner, w.alice };
        NeuralNPC.initialMultiDialogParticipants = new(NeuralNPC.multiDialogParticipants);
        p = Plan(Destination(), w.bob);
        check(await ManualTravel.Execute(p) && p.Positions.Distinct().Count() == 1 &&
            p.Travelers.Append(w.bob).All(n => n.transform.Position == Player.Instance.transform.Position) && MeetingController.Current?.Added == 1,
            "a one-tile room accepts the player, existing group, and an absent arrival guest on the same tile");

        w = world(); Room(width: 1, height: 1);
        w.bob.transform.Position = new(10, 10);
        p = Plan(Destination(), w.alice);
        check(await ManualTravel.Execute(p) && w.alice.transform.Position == w.bob.transform.Position &&
            Player.Instance.transform.Position == w.bob.transform.Position && !NeuralNPC.multiDialogParticipants!.Contains(w.bob),
            "a fully occupied room allows stacking without automatically adding its unselected resident to the conversation");

        w = world(); Room(width: 1, height: 1);
        w.bob.transform.Position = new(10, 10); TurfCollider.Blocked.Add(w.bob.transform.Position);
        check(!ManualTravel.TryPlan(Destination(), Array.Empty<NeuralNPC>(), out _, out _) && w.owner.GetComponent<EntityMover>().Moves == 0,
            "a character standing on a scenery obstacle does not make a blocked room valid for travel");

        w = world(); Room(width: 1, height: 1); p = Plan(Destination(), w.alice);
        BlackScreen.Instance.DuringFadeIn = () => w.bob.transform.Position = p.Positions[0];
        check(await ManualTravel.Execute(p) && MeetingController.Current?.Added == 1,
            "a character occupying the arrival tile during the fade does not cancel travel or prevent guest joining");

        w = world(); Room(); Plugin.MaxGuests.Value = 1;
        check(!ManualTravel.TryPlan(Destination(), new[] { w.alice, w.bob }, out _, out _), "travel guest selection enforces the configured guest limit");
        w.alice.Locked = true;
        check(!ManualTravel.TryPlan(Destination(), new[] { w.alice }, out _, out _), "locked guests are rejected before travel");

        w = world(); Room(); p = Plan(Destination());
        BlackScreen.Instance.DuringFadeIn = () => TurfCollider.Blocked.Add(p.Positions[1]);
        check(!await ManualTravel.Execute(p) && Player.Instance.transform.Position == p.Origin && w.owner.GetComponent<EntityMover>().Moves == 0,
            "destination changes during fade cancel movement");
        check(SaveUI.Instance.Blocks == 0 && DialogBox.Instance.TalkAllowed, "canceled travel restores UI and saving");

        w = world(); Room(); p = Plan(Destination());
        BlackScreen.Instance.DuringFadeIn = () => { ManualTravel.OnConversationReset(); MeetingController.Reset(); };
        check(!await ManualTravel.Execute(p) && Player.Instance.transform.Position == p.Origin && !NeuralNPC.npcFunctionsBeingInvoked,
            "conversation reset during travel cannot move the new conversation or leave the action lock set");

        w = world(); Room();
        NeuralNPC.multiDialogParticipants = new() { w.owner, w.alice };
        NeuralNPC.initialMultiDialogParticipants = new(NeuralNPC.multiDialogParticipants);
        p = Plan(Destination());
        var ownerOrigin = w.owner.transform.Position;
        var aliceOrigin = w.alice.transform.Position;
        w.alice.GetComponent<EntityMover>().FailAt = tile => tile.y >= 10;
        check(!await ManualTravel.Execute(p) && Player.Instance.transform.Position == p.Origin &&
            w.owner.transform.Position == ownerOrigin && w.alice.transform.Position == aliceOrigin,
            "a mid-party teleport failure restores already moved characters");
        check(w.owner.GetComponent<NPCRoutineExecutor>().currentRoutine.Activity == "base" && SaveUI.Instance.Blocks == 0,
            "failed party travel restores temporary routines and save access");

        w = world(); Room();
        var history = w.owner.dialogElements.ToArray();
        TravelMenu.Open();
        check(GenericListUI.Instance.gameObject.activeSelf && !DialogBox.Instance.TalkAllowed && DialogBox.Instance.isOpen,
            "travel opens the debug-style native list while retaining the current dialogue");
        Click("Public / unowned"); Click("Tavern");
        check(GenericListUI.Instance.Rows.Any(r => r.name == "Yes — choose who to meet"), "location selection leads to the arrival meeting choice");
        Click("No — travel with the current conversation group");
        check(GenericListUI.Instance.Rows.Any(r => r.name == "Meet on arrival: No"), "review clearly displays no-meeting selection");
        Click("Cancel travel / return to conversation");
        check(!GenericListUI.Instance.gameObject.activeSelf && DialogBox.Instance.TalkAllowed && w.owner.dialogElements.SequenceEqual(history),
            "canceling submenus restores the unchanged conversation");

        TravelMenu.Open();
        GenericListUI.Instance.Close();
        check(DialogBox.Instance.TalkAllowed, "closing the native list externally restores dialogue input");
        TravelMenu.Open();
        GenericListUI.Instance.Draw(new[] { new ListUIItem_Generic("Debug", null!, "", () => { }) });
        check(DialogBox.Instance.TalkAllowed && GenericListUI.Instance.Rows.Single().name == "Debug",
            "another native list owner can take over without losing its contents");
        GenericListUI.Instance.Close();

        TravelMenu.Open();
        Click("Public / unowned"); Click("Tavern"); Click("Yes — choose who to meet"); Click("[ ] Bob"); Click("Review selected guests");
        check(GenericListUI.Instance.Rows.Single(r => r.name == "Meet on arrival: Yes").suffix == "Bob", "guest toggles are reflected in the travel review");
        Click("Travel now");
        check(!GenericListUI.Instance.gameObject.activeSelf && NeuralNPC.multiDialogParticipants!.Contains(w.bob),
            "the native menu commits travel and selected guest joining");

        w = world(); Room();
        TravelMenu.Open();
        Action oldCancel = GenericListUI.Instance.Rows.Single(r => r.name == "Cancel travel / return to conversation").callback;
        GenericListUI.Instance.Close();
        TravelMenu.Open(); oldCancel();
        check(GenericListUI.Instance.gameObject.activeSelf && !DialogBox.Instance.TalkAllowed,
            "stale callbacks from an old menu cannot close a newer menu");
        TravelMenu.Cancel();
    }
}
