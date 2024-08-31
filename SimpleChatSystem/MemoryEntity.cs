using System.Text.Json.Serialization;

namespace SimpleChatSystem;

public class MemoryEntity
{
    [JsonPropertyName("EntityId")]
    public string EntityId { get; set; }

    [JsonPropertyName("User")]
    public string User { get; set; }

    [JsonPropertyName("Intent")]
    public string Intent { get; set; }

    [JsonPropertyName("Context")]
    public string Context { get; set; }

    [JsonPropertyName("Memory_Structure")]
    public MemoryStructure MemoryStructure { get; set; }

    public DateTime Timestamp { get; set; }

    public override string ToString()
    {
        return $"User: {User}, Intent: {Intent}, Context: {Context}, Tags: {string.Join(", ", MemoryStructure.Tags)}";
    }
}

public class MemoryStructure
{
    [JsonPropertyName("Tags")]
    public List<string> Tags { get; set; }
}