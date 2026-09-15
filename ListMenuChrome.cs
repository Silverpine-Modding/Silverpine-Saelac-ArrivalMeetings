using HarmonyLib;
using UnityEngine;

namespace ArrivalMeetings;

// Salt Extra Debug attaches its X beside the scrolling content, so Draw does not
// remove it. Borrow the shared list without inheriting that unrelated close UI.
internal sealed class ListMenuChrome
{
    private GameObject? debugClose;
    private bool wasActive;

    internal void HideDebugClose(GenericListUI list)
    {
        if (debugClose != null) return;
        var content = AccessTools.Field(typeof(GenericListUI), "instanceRoot").GetValue(list) as GameObject;
        var close = content?.transform.parent?.Find("SaltExtraDebug_CloseButton");
        if (close == null) return;
        debugClose = close.gameObject;
        wasActive = debugClose.activeSelf;
        debugClose.SetActive(false);
    }

    internal void Restore()
    {
        if (debugClose != null) debugClose.SetActive(wasActive);
        debugClose = null;
    }
}
