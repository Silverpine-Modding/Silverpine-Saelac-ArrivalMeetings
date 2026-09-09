using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArrivalMeetings;

internal sealed class TravelDestination
{
    internal string Zone = "", OwnerLabel = "";
    internal HashSet<NPCName> Owners = new();
    internal List<Vector2Int> Tiles = new();
}

internal static class TravelLocations
{
    internal static List<TravelDestination> Snapshot()
    {
        // The public MapZone enumeration is faction-filtered. Read the live registry to include
        // every named zone, including custom zones and those outside a town's faction.
        var registered = AccessTools.Field(typeof(MapZone), "registered").GetValue(null) as Dictionary<Vector2Int, MapZone>;
        if (registered == null) return new();
        Plot[] plots = Plot.allPlots.Where(p => p != null && p.gameObject.activeInHierarchy).ToArray();
        var groups = new Dictionary<(string zone, string owners), TravelDestination>();
        foreach (var entry in registered)
        {
            MapZone zone = entry.Value;
            if (zone == null || !zone.isActiveAndEnabled || string.IsNullOrWhiteSpace(zone.zoneName) || zone.zoneName == "Outside") continue;
            Vector2Int tile = entry.Key;
            Plot[] containing = plots.Where(p => p.bounds.Contains(new Vector3Int(tile.x, tile.y, 0))).ToArray();
            var owners = new HashSet<NPCName>(containing.SelectMany(p => p.owners ?? new List<NPCName>()).Where(n => n != NPCName.None));
            bool playerOwned = WorldInfoManager.Instance.IsOnPlayerOwnedPlot(tile);
            string ownerKey = (playerOwned ? "player:" : "npc:") + string.Join(",", owners.OrderBy(n => (int)n).Select(n => (int)n));
            var key = (zone.zoneName, ownerKey);
            if (!groups.TryGetValue(key, out var destination))
            {
                string label = playerOwned ? "Player property (" + Player.Instance.playerName + ")" :
                    owners.Count == 0 ? "Public / unowned" :
                    string.Join(" & ", owners.Select(NameOf).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                destination = new TravelDestination { Zone = zone.zoneName, OwnerLabel = label, Owners = owners };
                groups.Add(key, destination);
            }
            destination.Tiles.Add(tile);
        }
        return groups.Values.OrderBy(d => d.OwnerLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Zone, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NameOf(NPCName name) => NeuralNPC.neuralNPCs.TryGetValue(name, out var npc) && npc != null
        ? npc.GetFinalName() : name.ToString();

    internal static bool SafeTile(Vector2Int tile, string zone) => MapZone.GetPositionZoneName(tile) == zone &&
        Turfs.IsValidTurf(tile) && !HasBlockingScenery(tile) &&
        Turfs.GetTurfComponent<IPseudoBumpHandler>(tile) == null;

    private static bool HasBlockingScenery(Vector2Int tile) =>
        TurfCollider.TryGetImpassableColliders(tile, out var colliders) &&
        colliders.Any(c => c.GetComponent<Player>() == null && c.GetComponent<NeuralNPC>() == null);

    internal static IEnumerable<Vector2Int> MeetingTiles(Vector2Int at, string zone)
    {
        for (int x = at.x - 1; x <= at.x + 1; x++)
            for (int y = at.y - 1; y <= at.y + 1; y++)
            {
                var tile = new Vector2Int(x, y);
                if (SafeTile(tile, zone)) yield return tile;
            }
    }

    // Keep the party in one connected area. Use separate tiles when available, then share them.
    internal static List<Vector2Int> Formation(IEnumerable<Vector2Int> safeTiles, int count)
    {
        if (count <= 0) return new();
        var remaining = new HashSet<Vector2Int>(safeTiles);
        var largest = new HashSet<Vector2Int>();
        while (remaining.Count > 0)
        {
            Vector2Int first = remaining.OrderBy(p => p.x).ThenBy(p => p.y).First();
            var component = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(first);
            remaining.Remove(first);
            while (queue.Count > 0)
            {
                Vector2Int point = queue.Dequeue();
                component.Add(point);
                foreach (Vector2Int neighbor in Neighbors(point))
                    if (remaining.Remove(neighbor)) queue.Enqueue(neighbor);
            }
            if (component.Count > largest.Count) largest = component;
        }
        if (largest.Count == 0) return new();
        double centerX = largest.Average(p => p.x), centerY = largest.Average(p => p.y);
        Vector2Int center = largest.OrderBy(p => Math.Abs(p.x - centerX) + Math.Abs(p.y - centerY))
            .ThenBy(p => p.x).ThenBy(p => p.y).First();
        var result = new List<Vector2Int>();
        var placementQueue = new Queue<Vector2Int>();
        placementQueue.Enqueue(center);
        largest.Remove(center);
        while (placementQueue.Count > 0 && result.Count < count)
        {
            Vector2Int point = placementQueue.Dequeue();
            result.Add(point);
            foreach (Vector2Int neighbor in Neighbors(point))
                if (largest.Remove(neighbor)) placementQueue.Enqueue(neighbor);
        }
        int distinctCount = result.Count;
        while (result.Count < count) result.Add(result[result.Count % distinctCount]);
        return result;
    }

    private static IEnumerable<Vector2Int> Neighbors(Vector2Int p)
    {
        yield return new(p.x - 1, p.y); yield return new(p.x + 1, p.y);
        yield return new(p.x, p.y - 1); yield return new(p.x, p.y + 1);
    }
}
