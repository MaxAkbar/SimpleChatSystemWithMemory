using System.Text.RegularExpressions;

namespace SimpleChatSystem;

public class JsonExtractor
{
    public static string? ExtractJsonFromLLMResponse(string llmResponse)
    {
        // Define a Regex pattern to match a single JSON object: a block that starts with '{' and ends with '}'
        var pattern = @"\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}";

        // Find the first match
        var match = Regex.Match(llmResponse, pattern, RegexOptions.Singleline);

        // Check if there's a match
        if (match.Success)
        {
            return match.Value;
        }
        else
        {
            return null;
        }
    }
}
