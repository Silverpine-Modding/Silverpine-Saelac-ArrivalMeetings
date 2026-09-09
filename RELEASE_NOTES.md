# Arrival Meetings 1.1.1

Arrival Meetings adds NPCs to an ongoing conversation after dialogue travel and
provides a Travel action for choosing the destination and exact arrival guests.

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

Release build completed with no warnings or errors. All 111 automated checks
passed, including travel, native menu flow, shared-tile placement, conversation
membership, cancellation, and game API compatibility. Unity rendering and live
dialogue behavior still need in-game testing.

Created by Saelac and ChatGPT.
