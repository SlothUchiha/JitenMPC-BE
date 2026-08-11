using System.Text.Json;
using System.Text.Json.Serialization;

namespace JitenMpcBe.Models;

public sealed class JitenParseResponse
{
    [JsonPropertyName("tokens")] public List<List<JitenToken>> Tokens { get; set; } = [];
    [JsonPropertyName("vocabulary")] public List<JitenWord> Vocabulary { get; set; } = [];
}

public sealed class JitenToken
{
    [JsonPropertyName("start")] public int Start { get; set; }
    [JsonPropertyName("end")] public int End { get; set; }
    [JsonPropertyName("length")] public int? Length { get; set; }
    [JsonPropertyName("wordId")] public long? WordId { get; set; }
    [JsonPropertyName("readingIndex")] public int? ReadingIndex { get; set; }
    [JsonPropertyName("conjugations")] public JsonElement Conjugations { get; set; }
}

public sealed class JitenWord
{
    [JsonPropertyName("wordId")] public long WordId { get; set; }
    [JsonPropertyName("readingIndex")] public int ReadingIndex { get; set; }
    [JsonPropertyName("spelling")] public string? Spelling { get; set; }
    [JsonPropertyName("reading")] public string? Reading { get; set; }
    [JsonPropertyName("frequencyRank")] public int? FrequencyRank { get; set; }
    [JsonPropertyName("partsOfSpeech")] public JsonElement PartsOfSpeech { get; set; }
    [JsonPropertyName("pitchAccents")] public JsonElement PitchAccents { get; set; }
    [JsonPropertyName("meaningsChunks")] public JsonElement MeaningsChunks { get; set; }
    [JsonPropertyName("knownState")] public JsonElement KnownState { get; set; }
}

public sealed class ParsedSubtitle
{
    public required string Text { get; init; }
    public List<JitenToken> Tokens { get; init; } = [];
    public List<JitenWord> Vocabulary { get; init; } = [];
    public Dictionary<string, JitenWord> VocabByKey { get; init; } = [];
}

public sealed record RenderSegment(string Text, JitenWord? Word, JitenToken? Token);
