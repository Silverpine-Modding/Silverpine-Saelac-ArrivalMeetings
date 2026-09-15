# Arrival Meetings 1.2.1

[Download the latest release](https://github.com/Silverpine-Modding/Silverpine-Saelac-ArrivalMeetings/releases/latest)

A BepInEx 5 plugin for Silverpine 1.7.3. When a dialogue game action moves the
conversation to a different room or building, people the group intended to meet
can join that same conversation. An intended NPC who is elsewhere in the loaded
world can be brought to the destination. Version 1.1 adds player-controlled travel
from the conversation menu, with rooms grouped by property owner and explicit
meeting guest selection. Version 1.2 adds a separate Participants action for
nearby invitations and removals, with character-generated greetings and farewells.

Created by Saelac and ChatGPT.

## Use

### Manage the active conversation

Open **Participants** in the conversation action menu. This is separate from
**Travel** and **Meet on arrival** and works before any location change.

- Current participants start checked. Uncheck someone to remove them.
- Nearby visible NPCs start unchecked. Check someone to invite them.
- Choose **Apply changes** to begin the greetings and farewells, or **Cancel**
  to leave the conversation unchanged.

The Participants button and native list window remain in place. The selection
window uses its **Cancel** row; the black X left behind by Salt Extra Debug is
hidden while this mod owns the list. Travel uses the same handling.

Nearby uses the same range as the existing conversation picker: within two
tiles horizontally and vertically of the player or current speaker, and visible
to the current speaker. Inviting someone does not teleport them, require a free
tile, or count against the arrival-specific guest cap.

Each newcomer gives a short greeting through the game's dialogue generator,
using their character and memory context. Each departing NPC gives a farewell
while still in the conversation. These are normal NPC dialogue turns, with the
native portrait, text formatting, animation and configured dialogue translation.
Use the normal **Continue** control after reading each line before the next change.
Removing everyone is labeled **Apply changes — end conversation**; the last
farewell finishes before the game closes the dialogue.

Use **Interrupt** while reading a greeting or farewell to stop the remaining
changes. The waiting screen also offers **Cancel remaining changes**. Already
completed changes remain in place. If generation fails, an explanatory message
lets you continue the change without inventing dialogue.
These short turns use the existing model connection and do not invoke game actions.

Removing the displayed speaker switches to a remaining participant. Departed
NPCs stop receiving subsequent conversation turns. Rejoining NPCs keep only the
history they actually heard, and native cleanup runs once when the whole dialogue
ends. Each invitation and departure is recorded in the conversation context.

Use the game's **Interrupt** action to return to player input before opening
Participants from an automatic NPC turn. Changes are unavailable during model
work, combat, or forced encounters. Saving is temporarily blocked while reading
an entrance/farewell exchange and restored when it finishes or is canceled.

### Manual conversation travel

Open conversation actions and choose **Travel**. This is available before any
automatic location-change action has occurred. At an automatic NPC turn, use
**Interrupt** first so the conversation is accepting player input.

1. Choose a **property owner** category.
2. Choose a named **room or location** within that property.
3. Choose **No** or **Yes** for meeting people on arrival.
4. If Yes, toggle the NPCs to meet and select **Review selected guests**. Property
   owners are listed first when eligible. People marked **Bring to destination**
   will be moved from elsewhere; the existing `BringAbsentNPCs` setting applies.
5. Review the destination, current traveling group, and guests, then **Travel now**.

This uses Silverpine's `GenericListUI` and `ListUIItem_Generic` menu, following
the built-in debug menu. Owner, room, and guest lists are paginated. Back and
**Cancel travel / return to conversation** preserve the current dialogue and
make no travel changes. The shared Modding Tools action registry provides the
conversation button without requiring debug mode.

Manual travel brings the player and every current conversation participant.
It does not ask the dialogue model to approve the destination or choose who
travels. **No meeting** suppresses automatic guest inference for that trip, even
with automatic meetings enabled. **Yes** uses only the explicitly selected
guests, including when automatic meetings are disabled. Guests join the ongoing
native multi-NPC conversation. This does not assign them a permanent residence.

Destinations come from the live registry of **all loaded named map zones**, with
no faction filter; custom/constructed rooms are included. Ownership comes from
the property's actual `Plot` bounds and owner records, never from guessing names:

- NPC-owned rooms are grouped by the owner's current name.
- Shared owners are grouped together.
- The game's player-owned plots appear under **Player property**.
- Rooms outside any recorded owned plot appear under **Public / unowned**.

A custom named room inside Gareth's mapped property therefore appears under
Gareth. An extension outside that property boundary does not gain ownership by
touching the building or by using Gareth's name. A room spanning different
ownership areas can appear in more than one category, with each entry targeting
only the tiles in that ownership area. Room names describe named floor zones;
they do not infer unnamed rooms from walls. Outdoor terrain without a named
map zone and unloaded dungeon/instance maps are not fabricated as destinations.
The list refreshes each time Travel opens.

Travel places the player, traveling NPCs, and guests in one connected area of
the selected room. Characters use separate tiles when available and share tiles
when needed, so even a one-tile or fully occupied room can accept the group.
Character colliders do not prevent arrival. Invalid terrain, scenery obstacles,
and bump-trigger tiles remain excluded; at least one valid arrival tile is needed.
Destination availability is checked again after the fade. Failed movement
attempts restore already-moved travelers and temporary routine overrides where
the native APIs allow it. Dialogue and saving are unlocked on completion or error.

### Automatic dialogue arrivals

1. Discuss visiting a named NPC, for example: "Let's go to the tavern to meet Mara."
2. Let the game's **location-change action** actually move the conversation there.
3. The plugin checks named people in the recent conversation using your already
   configured dialogue model. Confirmed meeting targets join automatically.
4. To choose guests yourself, open the conversation action menu and select
   **Meet on arrival**. Select names, then **Add selected**. People labeled
   **bring here** will be moved from elsewhere. **Back to conversation** cancels
   the selection without ending the conversation.

The picker lists eligible loaded NPCs, prioritizing people mentioned recently.
If the conversation is waiting for an NPC's automatic turn, use the game's
**Interrupt** action to return to player input before selecting guests.

Settings are available from **Modding Tools > Arrival Meetings** on the main menu
and **Inventory > Mods > Arrival Meetings** in game. Settings save immediately.
The settings window scales with the current screen resolution, including changes
while it is open, using the shared Modding Tools scaling used by Adaptive LLM
Context and Vendor Inventory Manager. Larger text and controls sit on an opaque
dark panel, with a dark backdrop covering the entire screen on ultrawide displays
too. The window stays within the screen when dragged; settings scroll independently
of the always-visible Close button.

## Defaults and behavior

- Enabled, automatic intent checks, and bringing absent NPCs are all **on**.
- Up to **3 guests per arrival**, including manual additions.
- Intent detection reads up to **12 recent dialogue entries** from the current
  conversation and checks up to 24 named candidates in one model request.
- Full names and unambiguous first names work. Role descriptions and pronouns
  alone do not select automatic targets; use the manual picker for those.
- The model is asked to exclude incidental mentions, negated plans, people left
  behind, building-owner names, and meetings planned for later or elsewhere.
  Ambiguous or malformed responses add nobody. Intent quality still depends on
  the configured model; turn off automatic meetings for full manual control.
- Existing characters are moved through the game's teleport API; the plugin
  does not create duplicates or invent new characters.
- A guest already nearby in the destination can join without moving. Otherwise
  guests prefer an unoccupied nearby tile in the same room, and can share the
  player's tile or another occupied tile when needed. Room size does not limit
  guest placement; the configured guest cap still applies.
- Guests receive the game's temporary override routine for **10 turns** by
  default. Normal routine scheduling resumes when the override expires. The
  plugin does not teleport them back when you end the conversation.
- Current speakers retain their histories. Newcomers retain their own histories
  and receive a scene-entry message rather than a copy of the earlier dialogue.
  New guests enter the native group and its end-of-dialog cleanup roster.
- Registered custom NPCs are supported, including their configured dialogue art.
- Inactive/unregistered NPCs, forced guard encounters, locked Acacia encounters,
  and the current traveling companions are excluded. Acacia also requires the
  installed Acacia Unlocked plugin.

The automatic trigger is specifically `NPCFunction_ChangeLocation.InnerInvoke`. Ordinary
walking, loading a save, map travel, movement within the same room, and an action
that only offers **Follow** do not count. Canceled/failed travel does nothing.
The manual arrival action expires when you move away, begin another location
change, close the conversation, or load a save. Instance/dungeon conversations
and combat are excluded. Manual Travel also respects forced encounters where
the game currently prevents leaving the NPC.

The extra intent check uses the current game's inference connection; it does
not require a separate API key or service. It adds one model request when named
candidates exist. Setting `AutomaticMeetings = false` removes that request.

## Installation

Requires BepInEx 5, Silverpine 1.7.3, and
[**Modding Tools 1.9.3 or newer**](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases)
(developed against installed 1.10.2).

Download `ArrivalMeetings-1.2.1.zip` from the release page, close Silverpine, and
extract its `ArrivalMeetings` folder into `BepInEx/plugins/`. Alternatively, place
the standalone `ArrivalMeetings.dll` and this README under:

```text
BepInEx/plugins/ArrivalMeetings/
```

Keep the single existing Modding Tools installation. This release does not
bundle another framework DLL, game DLL, or BepInEx DLL. Start/restart Silverpine
to load the plugin. Its BepInEx log entry is **Arrival Meetings 1.2.1 loaded**.

Configuration is created on first startup at:

```text
BepInEx/config/Saelac.Silverpine.ArrivalMeetings.cfg
```

Available keys under `[General]`: `Enabled`, `AutomaticMeetings`,
`BringAbsentNPCs`, `MaxGuestsPerArrival` (1–8), `RecentDialogueElements` (4–30),
and `StayTurns` (1–120). To uninstall, close the game and remove the
`ArrivalMeetings` plugin folder. Guest positions and native routines already
saved in a game remain ordinary game state; there is no custom save format.

## Build and validation

The project references local game, BepInEx, and Modding Tools assemblies without
copying them to the output. It uses no external NuGet packages.

From the Silverpine installation directory:

```powershell
dotnet build 'Plugin Development/ArrivalMeetings/ArrivalMeetings.csproj' -c Release
dotnet build 'Plugin Development/ArrivalMeetings/Tests/ArrivalMeetings.Tests.csproj' -c Release
dotnet 'Plugin Development/ArrivalMeetings/Tests/bin/Release/net9.0/ArrivalMeetings.Tests.dll' .
```

For a standalone GitHub checkout, run these commands from its root with your
own Silverpine installation path (.NET SDK 9 is required for the test harness):

```powershell
dotnet build ArrivalMeetings.csproj -c Release -p:SilverpineGameDir="C:\path\to\Silverpine"
dotnet build Tests/ArrivalMeetings.Tests.csproj -c Release -p:SilverpineGameDir="C:\path\to\Silverpine"
dotnet Tests/bin/Release/net9.0/ArrivalMeetings.Tests.dll "C:\path\to\Silverpine"
```

The game and dependency assemblies come from your installation and are not
included in this source repository or release package.

The tests execute the production intent parser, meeting/travel controllers,
location catalog, and native-list menu flow against
deterministic world/model adapters and inspect the actual game's private hook
and lifecycle member signatures with Mono.Cecil. They cover cancellation,
stale replies, group conversion, cleanup membership, history preservation,
duplicate/cap handling, placement constraints, exclusions, and setup rollback.
Travel checks also cover property categories, custom rooms, connected formations,
stacking in one-tile and occupied rooms, character versus scenery colliders,
No/Yes meeting choices, exact guest selection, native menu cancellation/takeover,
stale callbacks, mid-fade changes, and party rollback.
Participant checks cover nearby visibility, active-speaker removal, final-NPC
cleanup, rejoining, private history, native menu lifecycle, generated greetings
and farewells, model failure, cancellation, and stale model responses.
They do not run Unity rendering or a live dialogue model.

### In-game smoke check

1. Start the game and verify the load message and both shared settings entries.
2. In a disposable test save, name an absent NPC while requesting a room change.
   Verify that the completed game action brings the confirmed target into the
   room, shows the arrival notice, and keeps the existing conversation intact.
3. Address the newcomer. Verify their portrait, speaker selection, and shared
   future turns; end the conversation and talk to each participant again.
4. Disable automatic meetings, repeat a move, and use **Meet on arrival** to
   add a nearby NPC and an absent NPC. Verify **Back** preserves the conversation.
5. Cancel a location change. Verify nobody is added or moved. Test a full room:
   guests should share valid tiles. Locked NPCs should still be skipped.
6. Save/reload after a meeting and advance time. Verify the guest's ordinary
   routine resumes after the temporary override expires.
7. Start a fresh conversation and choose **Travel** before an automatic arrival.
   Browse owner categories and their rooms, including a custom named room on an
   NPC plot. Cancel and verify the conversation and player position are unchanged.
8. Travel with **No meeting**, then repeat with **Yes** and a specifically chosen
   absent NPC. Check that the whole original group travels, only the requested
   guest joins, and both existing and new participants can speak normally.
9. Travel to a one-tile room with an existing group and an absent guest. Verify
   they share the tile, including when another NPC already occupies it.
10. Open Participants without traveling, select a nearby NPC, and apply. Read
    their greeting and continue; verify both NPCs can take subsequent turns.
11. Remove the displayed speaker. Verify their farewell appears before Continue
    removes them, then the remaining NPC takes over. Remove the last NPC and
    verify their farewell completes before the dialogue closes.
12. Cancel a greeting/farewell while the model is generating. Verify later model
    output does not replace the conversation and saving becomes available again.

Static registrations and Harmony patches deliberately survive destruction of
BepInEx's bootstrap plugin host, as required by Silverpine's plugin lifetime.
