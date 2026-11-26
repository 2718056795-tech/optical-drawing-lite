using System.Globalization;

namespace lcdb.Optic.Import;

/// <summary>Source identifiers stay with each optical entity, including after a subset is selected.</summary>
public static class ImportIdentity
{
    public static string Tag(Entity entity, string name)
    {
        var extensions = (entity as OpticalLens)?.Extensions ?? (entity as CementedLens)?.Extensions;
        return extensions != null && extensions.Tags.TryGetValue(name, out var value) ? value : "";
    }

    public static int ElementNumber(Entity entity, int fallback) =>
        int.TryParse(Tag(entity, "sourceElementNumber"), NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : fallback;

    public static string Configuration(Entity entity) =>
        int.TryParse(Tag(entity, "sourceConfigurationCount"), out var count) && count > 1 ? "Z" + Tag(entity, "sourceConfiguration") : "";
}
