using ArrivalMeetings;
using Mono.Cecil;
using UnityEngine;

int passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    passed++;
    Console.WriteLine("PASS: " + label);
}
NeuralNPC Make(NPCName id, string name, int x)
{
    var npc = new NeuralNPC { npcName = id, Name = name };
    npc.transform.Position = new(x, 0);
    npc.gameObject.Components[typeof(EntityMover)] = new EntityMover(npc);
    npc.gameObject.Components[typeof(NPCRoutineExecutor)] = new NPCRoutineExecutor();
    NeuralNPC.neuralNPCs[id] = npc;
    return npc;
}
(NeuralNPC owner, NeuralNPC alice, NeuralNPC bob) World()
{
    TravelMenu.Cancel();
    ParticipantMenu.Cancel();
    ParticipantExchange.Cancel();
    ManualTravel.OnConversationReset();
    MeetingController.Reset();
    Plugin.EnabledSetting.Value = Plugin.Automatic.Value = Plugin.BringAbsent.Value = true;
    Plugin.MaxGuests.Value = 3;
    Player.Instance = new Player();
    DialogBox.Instance = new DialogBox();
    GenericListUI.Instance = new GenericListUI();
    SaveUI.Instance = new SaveUI();
    SettingsUI.Instance = new SettingsUI();
    InferenceServerSetupHandler.Instance = new InferenceServerSetupHandler();
    BlackScreen.Instance = new BlackScreen();
    MapZone.Reset();
    Plot.allPlots.Clear();
    WorldInfoManager.Instance.ownedPlots.Clear();
    Turfs.Valid.Clear();
    Turfs.BumpTiles.Clear();
    TurfCollider.Blocked.Clear();
    NeuralNPC.npcFunctionsBeingInvoked = false;
    DungeonGenerationManager.Instance.playerInInstance = false;
    SerializationManager.loadingSave = false;
    NeuralNPC.neuralNPCs.Clear();
    NeuralNPC.multiDialogParticipants = NeuralNPC.initialMultiDialogParticipants = null;
    CustomContentDefinition_NPC.loaded.Clear();
    CustomContentDefinition_PlayerCharacter.loaded.Clear();
    MapZone.Zone = p => p.x < 10 ? "Outside" : "Tavern";
    for (int x = 10; x <= 12; x++) for (int y = -1; y <= 1; y++) Turfs.Valid.Add(new(x, y));
    var owner = Make(NPCName.Owner, "Guide", 0);
    var alice = Make(NPCName.Alice, "Alice Vale", 30);
    var bob = Make(NPCName.Bob, "Bob", 40);
    NeuralNPC.currentActiveDialogNeuralNPC = owner;
    owner.dialogElements.Add(new(SpeakerType.System, "PRIVATE_OLD_HISTORY"));
    owner.dialogElements.MarkNewDialog();
    owner.dialogElements.Add(new(SpeakerType.Player, "Let's go to the tavern to meet Alice."));
    alice.dialogElements.Add(new(SpeakerType.System, "ALICE_PRIVATE_HISTORY"));
    return (owner, alice, bob);
}
Arrival Begin(NeuralNPC owner)
{
    var arrival = MeetingController.Begin(owner, "Tavern")!;
    Player.Instance.transform.Position = new(11, 0);
    owner.transform.Position = new(10, 0);
    return arrival;
}

Check(MeetingIntent.Mentions("Let's meet ALICE.", "Alice Vale", new[] { "Alice Vale" }), "unique first names are recognized");
Check(!MeetingIntent.Mentions("malice", "Alice", new[] { "Alice" }), "name substrings do not count");
Check(!MeetingIntent.Mentions("meet Alice", "Alice Vale", new[] { "Alice Vale", "Alice Reed" }), "ambiguous first names are excluded");
Check(MeetingIntent.Mentions("meet Alice Vale", "Alice Vale", new[] { "Alice Vale", "Alice Reed" }), "full names disambiguate matching first names");
Check(MeetingIntent.Mentions("Visit Élodie's shop", "Élodie", new[] { "Élodie" }), "Unicode and possessive name boundaries work");
Check(MeetingIntent.ParseSelection("MEET: 2, 1, 2", 3, 3).SequenceEqual(new[] { 1, 0 }), "strict model IDs are deduplicated");
foreach (string output in new[] { "NONE", "We should MEET: 1", "MEET: 0", "MEET: 9", "MEET: 1, 9", "MEET: 1.5", "MEET: -1", "MEET: 99999999999999", "MEET: 1\nbring everyone", "```MEET: 1```", "" })
    Check(MeetingIntent.ParseSelection(output, 3, 3).Length == 0, "invalid or empty selection has no fallback: " + output.Replace('\n', ' '));
Check(MeetingIntent.ParseSelection("MEET: 1, 2, 3", 3, 1).SequenceEqual(new[] { 0 }), "automatic guest cap is enforced");

var world = World();
world.owner.Answer = () => Task.FromResult("MEET: 1");
var a = Begin(world.owner);
await MeetingController.AfterTravel(Task.CompletedTask, a);
Check(a.Added == 1 && world.alice.GetComponent<EntityMover>().Moves == 1, "confirmed absent guest moves once after travel");
Check(world.alice.transform.Position != a.At && world.alice.transform.Position != world.owner.transform.Position,
    "arrival guests prefer an unoccupied tile when one is available");
Check(NeuralNPC.multiDialogParticipants!.SequenceEqual(new[] { world.owner, world.alice }), "single conversation becomes group with the existing speaker");
Check(NeuralNPC.initialMultiDialogParticipants!.Contains(world.alice), "guest enters native end-dialog cleanup roster");
Check(world.owner.dialogElements.Any(d => d.contents == "PRIVATE_OLD_HISTORY"), "existing speaker history is preserved");
Check(world.alice.dialogElements.Any(d => d.contents == "ALICE_PRIVATE_HISTORY") && !world.alice.dialogElements.Any(d => d.contents == "PRIVATE_OLD_HISTORY"), "guest retains own history and does not inherit earlier private dialogue");
Check(world.owner.dialogElements.Count(d => d.contents.Contains("joined the ongoing")) == 1 && world.alice.dialogElements.Count(d => d.contents.Contains("joined the ongoing")) == 1, "arrival message is written once to each history");
Check(world.owner.lastDialogWasMulti && world.alice.lastDialogWasMulti, "both participants retain correct conversation mode for the next encounter");
Check(world.alice.GetComponent<NPCRoutineExecutor>().currentRoutine.Activity == "meeting Player", "guest receives a temporary meeting routine");
Check(MeetingController.AddGuests(a, new[] { world.alice }) == 0 && a.Added == 1, "repeated manual addition cannot duplicate guests");
Check(MeetingController.AddGuests(a, new[] { world.bob }) == 1, "manual selection can bring an unmentioned person");

world = World();
world.owner.Answer = () => Task.FromResult("MEET: 1");
a = MeetingController.Begin(world.owner, "Tavern")!;
await MeetingController.AfterTravel(Task.CompletedTask, a);
Check(MeetingController.Current == null && world.owner.Questions == 0, "canceled/no-move travel never checks intent");

world = World();
a = Begin(world.owner);
bool faulted = false;
try { await MeetingController.AfterTravel(Task.FromException(new InvalidOperationException("native failure")), a); }
catch (InvalidOperationException) { faulted = true; }
Check(faulted && MeetingController.Current == null, "native travel failure propagates without guest work");
bool canceled = false;
try { await MeetingController.AfterTravel(Task.FromCanceled(new CancellationToken(true)), a); }
catch (OperationCanceledException) { canceled = true; }
Check(canceled, "native task cancellation remains cancellation");

world = World();
Player.Instance.transform.Position = new(10, 0);
a = Begin(world.owner);
await MeetingController.AfterTravel(Task.CompletedTask, a);
Check(MeetingController.Current == null, "movement inside the same zone does not count as an arrival");

world = World();
var pending = new TaskCompletionSource<string>();
world.owner.Answer = () => pending.Task;
a = Begin(world.owner);
Task work = MeetingController.AfterTravel(Task.CompletedTask, a);
Check(!work.IsCompleted, "native action waits for intent work rather than launching detached mutations");
MeetingController.Reset();
pending.SetResult("MEET: 1");
await work;
Check(world.alice.GetComponent<EntityMover>().Moves == 0, "late model response after conversation reset cannot move a guest");

world = World();
pending = new TaskCompletionSource<string>();
world.owner.Answer = () => pending.Task;
a = Begin(world.owner);
work = MeetingController.AfterTravel(Task.CompletedTask, a);
Player.Instance.transform.Position = new(14, 0);
pending.SetResult("MEET: 1");
await work;
Check(a.Added == 0, "leaving the arrival position invalidates pending results");

world = World();
Plugin.Automatic.Value = false;
a = Begin(world.owner);
await MeetingController.AfterTravel(Task.CompletedTask, a);
Check(world.owner.Questions == 0 && MeetingController.CanOffer, "manual mode makes no model request and offers the arrival action");
Plugin.MaxGuests.Value = 1;
Check(MeetingController.AddGuests(a, new[] { world.alice, world.bob }) == 1 && !MeetingController.CanOffer, "manual and automatic additions share the per-arrival cap");

world = World();
Plugin.Automatic.Value = false;
Plugin.BringAbsent.Value = false;
MapZone.Zone = p => p.x >= 10 && p.x < 20 ? "Tavern" : "Outside";
a = Begin(world.owner);
await MeetingController.AfterTravel(Task.CompletedTask, a);
Check(MeetingController.AddGuests(a, new[] { world.alice }) == 0, "disabling relocation excludes absent NPCs");
world.alice.transform.Position = new(12, 0);
Check(MeetingController.AddGuests(a, new[] { world.alice }) == 1 && world.alice.GetComponent<EntityMover>().Moves == 0, "nearby guest joins without teleporting");

world = World();
world.owner.Answer = () => Task.FromResult("MEET: 1");
a = Begin(world.owner);
Turfs.Valid.Clear(); Turfs.Valid.Add(Player.Instance.transform.Position);
await MeetingController.AfterTravel(Task.CompletedTask, a);
Check(a.Added == 1 && world.alice.transform.Position == a.At,
    "automatic arrival can bring an absent guest onto the player's tile when all other tiles are unavailable");
Check(MeetingController.AddGuests(a, new[] { world.bob }) == 1 && world.bob.transform.Position == a.At,
    "Meet on arrival can add another guest to the same occupied tile");

world = World();
Plugin.Automatic.Value = false;
a = Begin(world.owner);
await MeetingController.AfterTravel(Task.CompletedTask, a);
Turfs.Valid.Clear(); Turfs.Valid.Add(a.At); TurfCollider.Blocked.Add(a.At);
Check(MeetingController.AddGuests(a, new[] { world.alice }) == 0 && world.alice.GetComponent<EntityMover>().Moves == 0,
    "sharing a tile with the player does not bypass a scenery obstacle on that same tile");
TurfCollider.Blocked.Clear(); Turfs.BumpTiles.Add(a.At);
Check(MeetingController.AddGuests(a, new[] { world.alice }) == 0,
    "occupied bump-trigger tiles are still excluded from arrival placement");
Turfs.BumpTiles.Clear(); MapZone.Register(a.At, "Other Room");
Check(!TravelLocations.SafeTile(a.At, "Tavern"), "occupied tiles outside the selected room remain excluded");

world = World();
Plugin.Automatic.Value = false;
a = Begin(world.owner);
await MeetingController.AfterTravel(Task.CompletedTask, a);
world.alice.Locked = true;
Check(MeetingController.AddGuests(a, new[] { world.alice }) == 0, "forced encounters are excluded");
world.alice.Locked = false;
world.alice.isActiveAndEnabled = false;
Check(MeetingController.AddGuests(a, new[] { world.alice }) == 0, "inactive NPCs are excluded");
world.alice.isActiveAndEnabled = true;
NeuralNPC.neuralNPCs.Remove(world.alice.npcName);
Check(MeetingController.AddGuests(a, new[] { world.alice }) == 0, "removed or unregistered NPCs are excluded");

world = World();
world.owner.dialogElements.Clear();
world.owner.dialogElements.Add(new(SpeakerType.Player, "Meet Alice yesterday"));
world.owner.dialogElements.MarkNewDialog();
world.owner.dialogElements.Add(new(SpeakerType.System, "Alice's Tavern"));
world.owner.dialogElements.Add(new(SpeakerType.Player, "Let's head inside."));
a = Begin(world.owner);
await MeetingController.AfterTravel(Task.CompletedTask, a);
Check(world.owner.Questions == 0, "earlier conversations and system location names do not seed automatic targets");

world = World();
Plugin.Automatic.Value = false;
a = Begin(world.owner);
await MeetingController.AfterTravel(Task.CompletedTask, a);
world.alice.Custom = true;
CustomContentDefinition_NPC.loaded[world.alice.Name] = new() { enabled = true, customPlayerCharacterDefinitionName = "bad" };
CustomContentDefinition_PlayerCharacter.loaded["bad"] = new() { Fail = true };
Check(MeetingController.AddGuests(a, new[] { world.alice }) == 0 && world.alice.GetComponent<EntityMover>().Moves == 0, "failed portrait preparation cannot move or register a guest");

world = World();
Plugin.Automatic.Value = false;
a = Begin(world.owner);
await MeetingController.AfterTravel(Task.CompletedTask, a);
world.alice.OnBeforeDialogBegins = () => throw new Exception("consumer failure");
Check(MeetingController.AddGuests(a, new[] { world.alice }) == 0 && world.alice.transform.Position == new Vector2Int(30, 0) &&
    world.alice.GetComponent<NPCRoutineExecutor>().currentRoutine.Activity == "base", "guest setup failure restores original position and routine");

await TravelChecks.Run(Check, World);
await ParticipantChecks.Run(Check, World);

// Inspect the real game DLL too: stubs cannot prove compatibility of private reflection hooks.
if (args.Length != 1) throw new ArgumentException("Pass the Silverpine root directory.");
string game = Path.GetFullPath(args[0]);
using var native = AssemblyDefinition.ReadAssembly(Path.Combine(game, "Silverpine_Data", "Managed", "Assembly-CSharp.dll"));
TypeDefinition Type(string name) => native.MainModule.Types.Single(t => t.Name == name);
foreach (var hook in new[] { ("NPCFunction_ChangeLocation", "InnerInvoke", 2), ("NeuralNPC", "TriggerDialog", 1),
    ("NeuralNPC", "TriggerMultiDialog", 3), ("NeuralNPC", "OnInputCallback", 1), ("DialogBox", "CloseBox", 0),
    ("DialogBox", "DisplayText", 4), ("DialogBox", "SetTalkAllowedState", 1), ("SerializationManager", "Load", 1) })
    Check(Type(hook.Item1).Methods.Any(m => m.Name == hook.Item2 && m.Parameters.Count == hook.Item3), "native patch signature exists: " + hook.Item1 + "." + hook.Item2);
Check(Type("NPCFunction_ChangeLocation").Methods.Single(m => m.Name == "InnerInvoke").ReturnType.FullName == "System.Threading.Tasks.Task", "location hook returns an awaitable Task");
foreach (string field in new[] { "dialogSprite", "expressionSprites", "preDialogVisualDirection", "preDialogRelationshipLevel", "dialogTimeBias", "currentExpression", "OnBeforeDialogBegins", "lastTalkedToPlayerTurnCount", "metBefore" })
    Check(Type("NeuralNPC").Fields.Any(f => f.Name == field), "native guest lifecycle field exists: " + field);
Check(Type("DialogBox").Fields.Any(f => f.Name == "lastUpperButtonOptions"), "native dialogue action snapshot exists");
Check(Type("MapZone").Fields.Any(f => f.Name == "registered" && f.IsStatic), "native live location registry exists");
Check(Type("Plot").Fields.Any(f => f.Name == "owners") && Type("Plot").Fields.Any(f => f.Name == "bounds"), "native property ownership and boundaries exist");
Check(Type("GenericListUI").Methods.Any(m => m.Name == "Draw" && m.Parameters.Count == 1) && Type("GenericListUI").Methods.Any(m => m.Name == "Close"),
    "native debug-style menu draw/close integration points exist");
Check(Type("DialogBox").Fields.Any(f => f.Name == "isNPCDIalog"), "native NPC dialogue mode flag exists");
Check(Type("NeuralNPC").Methods.Any(m => m.Name == "DisplayMultiDialogText" && m.IsStatic && m.Parameters.Count == 3) &&
    Type("NeuralNPC").Methods.Any(m => m.Name == "DisplayDialogText" && m.Parameters.Count == 1), "native portrait and input refresh methods exist");
Check(Type("NeuralNPC").Methods.Any(m => m.Name == "Generate" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Boolean" &&
    m.ReturnType.FullName == "System.Threading.Tasks.Task`1<System.String>"), "native dialogue generator returns the expected string task");
Check(Type("NeuralNPC").Methods.Any(m => m.Name == "RemoveToolTags" && m.IsStatic && m.Parameters.Count == 1) &&
    Type("NeuralNPC").Methods.Any(m => m.Name == "TransformTextForGenerateDialog" && m.Parameters.Count == 1) &&
    Type("NeuralNPC").Fields.Any(f => f.Name == "lastTranslation"), "native dialogue formatting and translation storage exist");
Check(Type("DialogBox").Methods.Any(m => m.Name == "StartContinueOnlyMode" && m.Parameters.Count == 0) &&
    Type("DialogBox").Methods.Any(m => m.Name == "StopContinueOnlyMode" && m.Parameters.Count == 0), "native Continue and Interrupt integration points exist");
Check(Type("GenericListUI").Fields.Any(f => f.Name == "instanceRoot" && f.FieldType.FullName == "UnityEngine.GameObject"),
    "shared list content root used to scope the debug X suppression exists");
Console.WriteLine($"{passed} checks passed. Unity rendering, live model intent quality, and live dialogue transitions still require an in-game smoke test.");
