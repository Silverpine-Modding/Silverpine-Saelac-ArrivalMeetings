using Silverpine.ModdingTools;
using UnityEngine;

namespace ArrivalMeetings;

public sealed class SettingsWindow : ModToolBehaviour
{
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;
    private const float WindowWidth = 1000f;
    private const float WindowHeight = 850f;
    private const float ScreenMargin = 24f;

    private Rect window;
    private Vector2 scroll;
    private GUIStyle? windowStyle, labelStyle, noteStyle, toggleStyle, buttonStyle;

    internal static void Open(ModToolSession session)
    {
        var root = new GameObject("Arrival Meetings Settings");
        var view = root.AddComponent<SettingsWindow>();
        view.AttachSession(session);
        view.window = new Rect((DesignWidth - WindowWidth) / 2f, (DesignHeight - WindowHeight) / 2f,
            WindowWidth, WindowHeight);
    }

    private void OnGUI()
    {
        if (FrameworkSession == null || FrameworkSession.IsClosed || Screen.width <= 0 || Screen.height <= 0)
            return;

        // Use the same aspect-preserving framework coordinates as the other mod settings screens.
        // BeginScaled recalculates on every draw, including resolution/fullscreen changes while open.
        using ModGuiScope scope = ModGui.BeginScaled(DesignWidth, DesignHeight);
        GUI.enabled = true;
        GUI.depth = -1000;
        GUI.color = Color.white;
        GUI.contentColor = Color.white;
        EnsureStyles();

        // Cover the actual screen, including the margins outside the design area on ultrawide displays.
        Matrix4x4 scaledMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        GUI.color = new Color(0.025f, 0.035f, 0.05f, 0.98f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.matrix = scaledMatrix;
        GUI.color = Color.white;

        ClampWindow();
        GUI.backgroundColor = new Color(0.035f, 0.045f, 0.06f, 1f);
        window = GUILayout.Window(GetInstanceID(), window, Draw, "Arrival Meetings", windowStyle!,
            GUILayout.Width(WindowWidth), GUILayout.Height(WindowHeight));
        ClampWindow();
    }

    private void ClampWindow()
    {
        window.x = Mathf.Clamp(window.x, ScreenMargin, DesignWidth - window.width - ScreenMargin);
        window.y = Mathf.Clamp(window.y, ScreenMargin, DesignHeight - window.height - ScreenMargin);
    }

    private void EnsureStyles()
    {
        if (windowStyle != null) return;
        windowStyle = new GUIStyle(GUI.skin.window)
        {
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperCenter,
            padding = new RectOffset(32, 32, 64, 28)
        };
        // A solid texture avoids the translucent stock window, including when another control has focus.
        foreach (GUIStyleState state in new[] { windowStyle.normal, windowStyle.hover, windowStyle.active,
            windowStyle.focused, windowStyle.onNormal, windowStyle.onHover, windowStyle.onActive, windowStyle.onFocused })
        {
            state.background = Texture2D.whiteTexture;
            state.textColor = Color.white;
        }
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, wordWrap = true };
        noteStyle = new GUIStyle(labelStyle) { fontSize = 22 };
        noteStyle.normal.textColor = new Color(0.8f, 0.85f, 0.91f);
        toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 24, wordWrap = true };
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 24 };
        labelStyle.normal.textColor = Color.white;
    }

    private void Draw(int id)
    {
        // Restore the ordinary control tint inside the dark window.
        GUI.backgroundColor = Color.white;
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        GUILayout.Label("When a dialogue game action moves you to another room, intended meeting targets can join the conversation.",
            labelStyle!);
        GUILayout.Space(20);
        Plugin.EnabledSetting.Value = GUILayout.Toggle(Plugin.EnabledSetting.Value, "Enable arrival meetings", toggleStyle!, GUILayout.MinHeight(52));
        Plugin.Automatic.Value = GUILayout.Toggle(Plugin.Automatic.Value, "Automatically add targets confirmed by the dialogue model", toggleStyle!, GUILayout.MinHeight(64));
        Plugin.BringAbsent.Value = GUILayout.Toggle(Plugin.BringAbsent.Value, "Bring intended NPCs from elsewhere in the loaded world", toggleStyle!, GUILayout.MinHeight(64));
        GUILayout.Space(18);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Guests per arrival: " + Plugin.MaxGuests.Value, labelStyle!);
        if (GUILayout.Button("−", buttonStyle!, GUILayout.Width(64), GUILayout.Height(48))) Plugin.MaxGuests.Value = Mathf.Max(1, Plugin.MaxGuests.Value - 1);
        if (GUILayout.Button("+", buttonStyle!, GUILayout.Width(64), GUILayout.Height(48))) Plugin.MaxGuests.Value = Mathf.Min(8, Plugin.MaxGuests.Value + 1);
        GUILayout.EndHorizontal();
        GUILayout.Space(18);
        GUILayout.Label("Meeting routine duration: " + Plugin.StayTurns.Value + " turns", labelStyle!);
        Plugin.StayTurns.Value = Mathf.RoundToInt(GUILayout.HorizontalSlider(Plugin.StayTurns.Value, 1, 120, GUILayout.Height(30)));
        GUILayout.Space(20);
        GUILayout.Label("In conversation actions, choose Travel to select a property owner, room, and optional meeting guests. After arriving, Meet on arrival lets you add more guests. Existing conversation history is preserved.",
            noteStyle!);
        GUILayout.Space(16);
        GUILayout.Label(MeetingController.LastStatus, noteStyle!);
        GUILayout.EndScrollView();
        GUILayout.Space(16);
        if (GUILayout.Button("Close", buttonStyle!, GUILayout.Height(52)))
        {
            ReleaseSession();
            Destroy(gameObject);
        }
        GUI.DragWindow(new Rect(0, 0, window.width, 54));
    }
}
