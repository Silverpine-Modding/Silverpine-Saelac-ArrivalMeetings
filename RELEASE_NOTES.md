# Arrival Meetings 1.2.1

Arrival Meetings adds NPCs to an ongoing conversation after dialogue travel and
provides a Travel action for choosing the destination and exact arrival guests.

## Fixed in 1.2.1

- Keep the Participants button and native list selection window. Hide Salt Extra Debug's leftover black X while Participants or Travel owns the list; Cancel remains available in the menu. Restore the debug control when another menu takes over.
- Generate greetings and farewells through the native dialogue generator and display them as normal NPC turns with the game's portrait, formatting, animation, translation and Continue control.
- Wait for the farewell to finish animating and for Continue before removing its speaker. Native Interrupt cancels remaining changes and keeps an NPC whose farewell has not been acknowledged.
- Keep the final greeting visible when normal conversation resumes. Reject canceled or outdated speech and translation results.

## Added in 1.2.0

- A separate **Participants** conversation button adds nearby visible NPCs and removes current participants, without requiring travel.
- Check the NPCs who should stay or join, then apply the changes. Canceling the selection leaves the conversation unchanged.
- Newcomers give a brief greeting. Departing NPCs give a farewell before leaving. These lines use each NPC's full native character and memory prompt through the existing dialogue model.
- Read each line with **Continue**. Use **Interrupt** to stop remaining changes while reading, or **Cancel remaining changes** on the waiting screen.
- Removing the active speaker transfers the conversation to someone remaining. Removing everyone ends the native dialogue after the last farewell.
- Removed NPCs stop receiving future conversation turns; rejoining preserves what they actually heard and native cleanup runs once at dialogue end.

## Included

- Bring intended NPCs to the destination and include them in the native multi-NPC conversation.
- Travel with the current conversation group using the game's native list menu.
- Browse all loaded named rooms grouped by property owner, including custom rooms.
- Choose whether to meet on arrival and select exactly who joins.
- Share tiles when a room has limited space or is already occupied. One valid tile can hold the group.
- Configure automatic meetings and guest limits in a dark, resolution-aware settings window.

## Installation

Requires Silverpine 1.7.3, BepInEx 5, and
[Modding Tools 1.9.3 or newer](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases).

Close Silverpine and extract the ZIP's `ArrivalMeetings` folder into
`BepInEx/plugins/`, replacing an older copy. The standalone DLL is also provided.
Keep a single installed copy of `ArrivalMeetings.dll`, then restart the game.

## Validation

Release build completed with no warnings or errors. All 162 automated checks
passed, including travel, native menu flow, shared-tile placement, conversation
membership, greetings/farewells, cancellation, and game API compatibility. Unity rendering and live
dialogue behavior still need in-game testing.

Created by Saelac and ChatGPT.
