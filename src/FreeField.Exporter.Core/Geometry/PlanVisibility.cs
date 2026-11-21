namespace FreeField.Exporter.Core.Geometry;

/// <summary>Pure helpers deciding whether an element can be drawn in a plan view (no Revit types).</summary>
public static class PlanVisibility
{
    /// <summary>
    /// True when the element's vertical extent [zMin, zMax] overlaps the plan view range [rangeMin, rangeMax]
    /// (view depth .. top clip plane, in the same units). An element entirely below the view depth or entirely above the
    /// top clip plane is not drawn. Infinite bounds mean "unlimited". Arguments may be given in either order.
    /// </summary>
    public static bool IntersectsViewRange(double zMin, double zMax, double rangeMin, double rangeMax, double tolerance = 1e-6)
    {
        if (zMin > zMax) (zMin, zMax) = (zMax, zMin);
        if (rangeMin > rangeMax) (rangeMin, rangeMax) = (rangeMax, rangeMin);
        return zMax >= rangeMin - tolerance && zMin <= rangeMax + tolerance;
    }
}
