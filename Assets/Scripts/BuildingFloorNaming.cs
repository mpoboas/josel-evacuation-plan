using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Shared parsing for <c>Floor N</c> hierarchy names used by <see cref="BuildingSystem.BuildingTool"/> and map capture.
/// </summary>
public static class BuildingFloorNaming
{
    private static readonly Regex FloorRegex = new Regex(@"^Floor\s+(-?\d+)\s*$", RegexOptions.CultureInvariant);

    public static bool TryParseFloorLevelFromName(string objectName, out int floorLevel)
    {
        floorLevel = 0;
        Match m = FloorRegex.Match(objectName);
        if (!m.Success)
            return false;
        floorLevel = int.Parse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return true;
    }

    public static bool TryFindFloorRoot(Transform start, out Transform floorRoot, out int floorIndex)
    {
        floorRoot = null;
        floorIndex = 0;
        for (Transform p = start; p != null; p = p.parent)
        {
            if (!TryParseFloorLevelFromName(p.name, out floorIndex))
                continue;
            floorRoot = p;
            return true;
        }

        return false;
    }
}
