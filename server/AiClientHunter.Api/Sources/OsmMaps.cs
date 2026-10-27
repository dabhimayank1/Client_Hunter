namespace AiClientHunter.Api.Sources;

public static class OsmMaps
{
    // Bounding boxes = south,west,north,east
    public static readonly Dictionary<string, string> CityBoxes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ahmedabad"] = "22.90,72.45,23.15,72.75",
        ["Gandhinagar"] = "23.15,72.55,23.35,72.75",
        ["Surat"] = "21.05,72.70,21.30,72.95",
        ["Vadodara"] = "22.20,73.05,22.40,73.30",
        ["Rajkot"] = "22.20,70.70,22.35,70.90",
        ["Mumbai"] = "18.89,72.75,19.27,73.00",
        ["Delhi"] = "28.40,76.84,28.88,77.35",
        ["Bengaluru"] = "12.83,77.46,13.14,77.78",
        ["Pune"] = "18.43,73.74,18.62,73.98",
        ["Jaipur"] = "26.78,75.68,27.02,75.92",
    };

    // OpenStreetMap Overpass Category QL filters
    public static readonly Dictionary<string, string> CategoryFilters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["clinic"] = "[\"amenity\"~\"^(clinic|doctors|dentist)$\"]",
        ["restaurant"] = "[\"amenity\"~\"^(restaurant|cafe)$\"]",
        ["salon"] = "[\"shop\"~\"^(hairdresser|beauty)$\"]",
        ["gym"] = "[\"leisure\"=\"fitness_centre\"]",
        ["school"] = "[\"amenity\"~\"^(school|kindergarten)$\"]",
        ["hotel"] = "[\"tourism\"~\"^(hotel|guest_house)$\"]",
    };

    public static string? NormalizeCategory(string category)
    {
        var lower = category.Trim().ToLowerInvariant();
        if (lower.Contains("dent") || lower.Contains("clinic") || lower.Contains("doctor") || lower.Contains("hospital") || lower.Contains("ayurved"))
            return "clinic";
        if (lower.Contains("rest") || lower.Contains("cafe") || lower.Contains("food") || lower.Contains("bakery"))
            return "restaurant";
        if (lower.Contains("salon") || lower.Contains("beauty") || lower.Contains("spa") || lower.Contains("hair") || lower.Contains("parlour"))
            return "salon";
        if (lower.Contains("gym") || lower.Contains("fitness"))
            return "gym";
        if (lower.Contains("school") || lower.Contains("tuition") || lower.Contains("coaching"))
            return "school";
        if (lower.Contains("hotel") || lower.Contains("stay") || lower.Contains("guest"))
            return "hotel";

        return CategoryFilters.ContainsKey(lower) ? lower : null;
    }
}
