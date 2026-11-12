using System.Text.Json.Serialization;

namespace Screenbox.Core.Models.Serialization;

/// <summary>
/// Provides source-generated JSON metadata for the OpenSubtitles API, which keeps parsing compatible with Native AOT.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(OpenSubtitlesSearchResult[]))]
internal sealed partial class OpenSubtitlesJsonContext : JsonSerializerContext
{
}
