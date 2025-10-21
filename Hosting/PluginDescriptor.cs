using System.Text.Json.Serialization;

namespace BlazorPdfApp.Hosting;

/// <summary>
/// Host-side representation of a plugin manifest file before mapping to the shared contract.
/// </summary>
public sealed class PluginDescriptor
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("assembly")]
    public string? Assembly { get; set; }

    [JsonPropertyName("entryType")]
    public string? EntryType { get; set; }

    [JsonPropertyName("routeBase")]
    public string? RouteBase { get; set; }

    [JsonIgnore]
    public string? Folder { get; set; }

    public bool TryValidate(out string? error)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            error = "Plugin id is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "Plugin name is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            error = "Plugin version is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Assembly))
        {
            error = "Plugin assembly is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(EntryType))
        {
            error = "Plugin entry type is missing.";
            return false;
        }

        error = null;
        return true;
    }
}
