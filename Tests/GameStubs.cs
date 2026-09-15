// Deterministic world/model adapter for exercising the actual production controller outside Unity.
using System.Reflection;
using UnityEngine;

namespace ArrivalMeetings
{
    internal class Setting<T>(T value) { public T Value = value; }
    internal static class Plugin
    {
        internal static Setting<bool> EnabledSetting = new(true), Automatic = new(true), BringAbsent = new(true);
        internal static Setting<int> MaxGuests = new(3), HistoryLength = new(12), StayTurns = new(10);
        internal static TestLog Log = new();
    }
    internal class TestLog { public void LogInfo(object value) { } public void LogWarning(object value) { } }
    internal static class MeetingPicker { internal static void Clear() { } }
}
namespace BepInEx.Bootstrap { public static class Chainloader { public static Dictionary<string, object> PluginInfos = new(); } }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch(Type type, string method) : Attribute { }
    public static class AccessTools
    {
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
    }
}
namespace UnityEngine
{
    public record struct Vector2Int(int x, int y);
    public record struct Vector3Int(int x, int y, int z);
    public record struct BoundsInt(int x, int y, int width, int height)
    { public bool Contains(Vector3Int point) => point.x >= x && point.x < x + width && point.y >= y && point.y < y + height; }
    public static class Time { public static float realtimeSinceStartup => (float)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds; }
    public class Sprite { }
    public class Scene { public bool IsValid() => true; }
    public class Transform
    {
        public Vector2Int Position; public Vector2Int GetVector2IntPosition() => Position;
        public Transform? parent; public GameObject gameObject = null!;
        public Dictionary<string, Transform> Children = new();
        public Transform? Find(string name) => Children.GetValueOrDefault(name);
    }
    public class GameObject
    {
        public Scene scene = new(); public bool activeInHierarchy = true, activeSelf;
        public Dictionary<Type, object> Components = new();
        public Transform transform;
        public GameObject() { transform = new() { gameObject = this }; }
        public void SetActive(bool active) => activeSelf = active;
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new(); public Transform transform = new();
        public bool isActiveAndEnabled = true;
        public T GetComponent<T>() => gameObject.Components.TryGetValue(typeof(T), out var value) ? (T)value : default!;
    }
    public static class Resources
    {
        public static T Load<T>(string path) where T : new() => new();
        public static T[] LoadAll<T>(string path) => Array.Empty<T>();
    }
}
public enum NPCName { None = -1, Owner, Alice, Bob, Carol, Acacia, Extra }
public enum SpeakerType { NPC, Player, System, NewDialogMarker }
public class NeuralNPC : MonoBehaviour
{
    public static Dictionary<NPCName, NeuralNPC> neuralNPCs = new();
    public static NeuralNPC currentActiveDialogNeuralNPC = null!;
    public static List<NeuralNPC>? multiDialogParticipants, initialMultiDialogParticipants;
    public static bool npcFunctionsBeingInvoked;
    public NPCName npcName;
    public string Name = "";
    public List<DialogElement> dialogElements = new();
    public bool Locked, playerLeft, lastDialogWasMulti, Custom;
    public int firstTalkedToPlayerTurnCount = -1, relationshipLevel;
    public Sprite? dialogNudeSprite;
    private Sprite? dialogSprite, currentExpression;
    private string lastTranslation = "";
    private List<Sprite> expressionSprites = new();
    private int preDialogVisualDirection, preDialogRelationshipLevel, dialogTimeBias, lastTalkedToPlayerTurnCount;
    private bool metBefore;
    public Action? OnBeforeDialogBegins;
    public string GetFinalName() => Name;
    public bool IsCustomNPC() => Custom;
    public int Releases, Cleanups, PortraitSwitches;
    public Func<Transform, bool> Visible = _ => true;
    public void ReleaseLargeAssets() { Releases++; }
    public bool CanSeeTransform(Transform transform) => Visible(transform);
    public void DoStartNPCMode(DialogBox.SpriteSwitchMode mode) { PortraitSwitches++; }
    private static void DisplayMultiDialogText(NeuralNPC speaker, NeuralNPC? nextSpeaker, string text)
    { speaker.DoStartNPCMode(DialogBox.SpriteSwitchMode.Normal); DialogBox.Instance.DisplayText(text, s => OnMultiInputCallback(nextSpeaker, s), new()); }
    private void DisplayDialogText(string text) => DialogBox.Instance.DisplayText(text, s => dialogElements.Add(new(SpeakerType.Player, s)), new());
    public static void OnMultiInputCallback(NeuralNPC? nextSpeaker, string text)
    { foreach (var history in multiDialogParticipants!.Select(n => n.dialogElements).Distinct()) history.AddToDialog(SpeakerType.Player, text); }
    public Func<Task<string>> Answer = () => Task.FromResult("NONE");
    public int DialogGenerations;
    public string LastDialogCue = "";
    private Task<string> Generate(bool newline) { DialogGenerations++; LastDialogCue = dialogElements.Last().contents; return Answer(); }
    private static string RemoveToolTags(string input) => System.Text.RegularExpressions.Regex.Replace(input, @"<tool>.*?</tool>|<[^>]*>", "");
    private string TransformTextForGenerateDialog(string text) => text.StartsWith(Name + ":") ? text : Name + ": " + text;
    public int Questions;
    public int LastTakes;
    public string LastQuestion = "";
    public Task<string> AskQuestion(string question, bool deterministic, int takes, string grammar, List<DialogElement> targetDialogElements)
    { Questions++; LastTakes = takes; LastQuestion = question; return Answer(); }
    public static string ToAnd(List<string> list) => string.Join(" and ", list);
    public class DialogElement(SpeakerType speaker, string contents)
    { public SpeakerType speakerType = speaker; public string contents = contents; public string GetNamedContents() => contents; }
}
public static class DialogExtensions
{
    public static void MarkNewDialog(this List<NeuralNPC.DialogElement> history)
    { history.RemoveAll(d => d.speakerType == SpeakerType.NewDialogMarker); history.Add(new(SpeakerType.NewDialogMarker, "")); }
    public static NeuralNPC.DialogElement AddToDialog(this List<NeuralNPC.DialogElement> history, SpeakerType speaker, string text)
    { var entry = new NeuralNPC.DialogElement(speaker, text); history.Add(entry); return entry; }
}
public class Player : MonoBehaviour
{ public static Player Instance = new(); public bool inCombat; public string playerName = "Player"; public EntityMover entityMover; public Player() { entityMover = new(this); } }
public class DialogBox
{
    public static DialogBox Instance = new(); public bool isOpen = true, isAnimatingText;
    private bool isNPCDIalog = true;
    public bool TalkAllowed = true;
    public enum SpriteSwitchMode { Normal, Instant }
    public string Text = "";
    public Action<string>? Input;
    public List<DialogOption> Options = new();
    public int Ends;
    public bool ContinueMode, NativeText, AutoFinishAnimation = true;
    private Action? finished;
    public void SetNotificationMode() => isNPCDIalog = false;
    public void DisplayText(string text, Action<string> input, List<UpperButtonOption> buttons, Action? finishedAnimatingCallback = null)
    {
        ArrivalMeetings.ParticipantExchange.ConfigureDisplay(this, ref input, ref finishedAnimatingCallback);
        Text = text; Input = input; Options.Clear(); NativeText = true; isAnimatingText = true;
        finished = finishedAnimatingCallback;
        if (AutoFinishAnimation) FinishAnimation();
    }
    public void FinishAnimation() { isAnimatingText = false; var callback = finished; finished = null; callback?.Invoke(); }
    public void DisplayTextNoDialog(string text, params DialogOption[] options) { Text = text; Options = options.ToList(); NativeText = false; }
    public bool IsEndDialogAllowed(NeuralNPC npc) => !npc.Locked;
    public void StartContinueOnlyMode() => ContinueMode = true;
    public void StopContinueOnlyMode() { ContinueMode = false; ArrivalMeetings.ParticipantExchange.OnInterrupt(); }
    public void SetTalkAllowedState(bool state) { TalkAllowed = state; }
    public void EndDialog()
    {
        Ends++;
        foreach (var npc in NeuralNPC.initialMultiDialogParticipants ?? ArrivalMeetings.MeetingController.Participants()) { npc.Cleanups++; npc.ReleaseLargeAssets(); }
        isOpen = false;
        NeuralNPC.multiDialogParticipants = NeuralNPC.initialMultiDialogParticipants = null;
        ArrivalMeetings.ParticipantExchange.Cancel(); ArrivalMeetings.ParticipantMenu.Cancel(false);
        ArrivalMeetings.MeetingController.Reset();
    }
}
public class UpperButtonOption { }
public class DialogOption(string label, Action callback, bool endDialog = true)
{ public string label = label; public Action callback = callback; }
public static class SerializationManager { public static bool loadingSave; }
public enum Language { English, French }
public class SettingsUI
{ public static SettingsUI Instance = new(); public Language DialogLanguage; public Language GetDialogLanguage() => DialogLanguage; }
public class InferenceServerSetupHandler
{
    public static InferenceServerSetupHandler Instance = new();
    public Func<Task<string>> Translation = () => Task.FromResult("Bonjour.");
    public Task<string> Translate(string context, string speakerName, string input, Language language) => Translation();
}
public class DungeonGenerationManager { public static DungeonGenerationManager Instance = new(); public bool playerInInstance; }
public class SaveUI
{ public static SaveUI Instance = new(); public bool Blocked; public int Blocks; public bool IsSavingBlocked() => Blocked || Blocks > 0; public void SetSaveBlock(bool state) => Blocks += state ? 1 : -1; }
public class MapZone : MonoBehaviour
{
    private static Dictionary<Vector2Int, MapZone> registered = new();
    public string zoneName = "";
    public static void Reset() => registered.Clear();
    public static void Register(Vector2Int p, string name) => registered[p] = new() { zoneName = name };
    public static Func<Vector2Int, string> Zone = p => p.x < 10 ? "Outside" : "Tavern";
    public static string GetPositionZoneName(Vector2Int p) => registered.TryGetValue(p, out var zone) ? zone.zoneName : Zone(p);
}
public static class Turfs
{
    public static HashSet<Vector2Int> Valid = new();
    public static HashSet<Vector2Int> BumpTiles = new();
    public static bool IsValidTurf(Vector2Int p) => Valid.Contains(p);
    public static bool IsOccupiedByCharacter(Vector2Int p) => Player.Instance.transform.Position == p || NeuralNPC.neuralNPCs.Values.Any(n => n.transform.Position == p);
    public static T GetTurfComponent<T>(Vector2Int p) => typeof(T) == typeof(IPseudoBumpHandler) && BumpTiles.Contains(p)
        ? (T)(object)new BumpHandler() : default!;
    private sealed class BumpHandler : IPseudoBumpHandler { }
}
public class TurfCollider : MonoBehaviour
{
    public static HashSet<Vector2Int> Blocked = new();
    public static bool TryGetImpassableColliders(Vector2Int p, out List<TurfCollider> colliders)
    {
        colliders = new();
        if (Blocked.Contains(p)) colliders.Add(new());
        if (Player.Instance.transform.Position == p) colliders.Add(For(Player.Instance));
        colliders.AddRange(NeuralNPC.neuralNPCs.Values.Where(n => n.transform.Position == p).Select(For));
        return colliders.Count > 0;
    }
    private static TurfCollider For(MonoBehaviour character)
    {
        var collider = new TurfCollider();
        collider.gameObject.Components[character.GetType()] = character;
        return collider;
    }
}
public interface IPseudoBumpHandler { }
public class Plot : MonoBehaviour { public static List<Plot> allPlots = new(); public BoundsInt bounds; public List<NPCName> owners = new(); }
public class EntityMover(MonoBehaviour owner)
{
    public int currentVisualDirection, Moves;
    public Func<Vector2Int, bool>? FailAt;
    public void Teleport(Vector2Int p) { if (FailAt?.Invoke(p) == true) throw new Exception("Teleport failed"); Moves++; owner.transform.Position = p; }
    public void TurnTowards(Vector2Int p) { }
}
public class NPCRoutine(string activity, string visible, string other, Vector2Int target, params object[] args)
{ public string Activity = activity; public Vector2Int Target = target; }
public class NPCRoutineExecutor
{
    public NPCRoutine currentRoutine = new("base", "base", "base", new());
    public LinkedList<NPCRoutine> preOverrideRoutines = new();
    public void StartOverrideRoutine(NPCRoutine routine) { preOverrideRoutines.AddFirst(currentRoutine); currentRoutine = routine; }
    public void StopOverrideRoutine(NPCRoutine routine) { currentRoutine = preOverrideRoutines.First!.Value; preOverrideRoutines.RemoveFirst(); }
}
public class RoutineArgument_EndOverrideIf_Overridden { }
public class RoutineArgument_EndOverrideIf_AfterTurns(int turns) { }
public class UpperNotificationUI { public static UpperNotificationUI Instance = new(); public void OneOff(string value) { } }
public class WorldInfoManager
{
    public static WorldInfoManager Instance = new(); public int TotalTurnCount = 100;
    public HashSet<NPCName> ownedPlots = new();
    public bool IsOnPlayerOwnedPlot(Vector2Int p) => Plot.allPlots.Any(plot => plot.bounds.Contains(new(p.x, p.y, 0)) && plot.owners.Any(ownedPlots.Contains));
}
public class BlackScreen : MonoBehaviour
{
    public static BlackScreen Instance = new(); public Action? DuringFadeIn;
    public void FadeIn(float speed, bool fadeAudio, Action callback) { DuringFadeIn?.Invoke(); callback(); }
    public void FadeOut(float speed, bool fadeAudio, Action callback) => callback();
}
public class ListUIItem_Generic(string name, Sprite icon, string suffix, Action callback)
{ public string name = name, suffix = suffix; public Action callback = callback; }
public class GenericListUI : MonoBehaviour
{
    public static GenericListUI Instance = new(); public List<ListUIItem_Generic> Rows = new();
    private GameObject instanceRoot = new();
    public GameObject DebugClose = new() { activeSelf = true };
    public GenericListUI()
    {
        instanceRoot.transform.parent = new Transform();
        instanceRoot.transform.parent.Children["SaltExtraDebug_CloseButton"] = DebugClose.transform;
    }
    public void Draw(IEnumerable<ListUIItem_Generic> rows) { ArrivalMeetings.TravelMenu.OnNativeDraw(this); ArrivalMeetings.ParticipantMenu.OnNativeDraw(this); Rows = rows.ToList(); }
    public void Open() => gameObject.activeSelf = true;
    public void Close() { gameObject.activeSelf = false; ArrivalMeetings.TravelMenu.OnNativeClose(this); ArrivalMeetings.ParticipantMenu.OnNativeClose(this); }
}
public class CustomContentDefinition_NPC
{
    public static Dictionary<string, CustomContentDefinition_NPC> loaded = new();
    public bool enabled, overrideExisting; public string customPlayerCharacterDefinitionName = "";
}
public class CustomContentDefinition_PlayerCharacter
{
    public static Dictionary<string, CustomContentDefinition_PlayerCharacter> loaded = new();
    public class Assets { public Sprite dialogSprite = new(); public List<Sprite> expressionSprites = new(); }
    public Assets assets = new(); public Sprite? dialogNudeSprite; public bool Fail;
    public void ClaimLargeAssets(GameObject owner) { if (Fail) throw new Exception("Artwork unavailable"); }
}
