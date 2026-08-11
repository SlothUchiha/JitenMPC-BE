using System.Net.Http.Json;
using System.Text.Json;
using JitenMpcBe.Models;

namespace JitenMpcBe.Services;

public sealed class JitenApiClient
{
    private readonly FileLogger _log;
    private readonly Dictionary<string, ParsedSubtitle> _cache = new();
    private readonly Queue<string> _cacheOrder = new();
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public JitenApiClient(FileLogger log) => _log = log;
    public void ClearCache() { _cache.Clear(); _cacheOrder.Clear(); }

    private HttpClient Client(int timeoutSeconds)
        => new() { Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 300)) };

    private static void Auth(HttpRequestMessage req, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey)) req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
    }

    public async Task<bool> PingAsync(string baseUrl, string apiKey, int timeoutSeconds = 30)
    {
        using var http = Client(timeoutSeconds);
        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/reader/ping");
        Auth(req, apiKey);
        req.Content = JsonContent.Create(new { });
        using var resp = await http.SendAsync(req);
        return resp.IsSuccessStatusCode;
    }

    public async Task<ParsedSubtitle?> ParseAsync(string baseUrl, string apiKey, string text, int timeoutSeconds = 30, int cacheSize = 2000)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        if (_cache.TryGetValue(text, out var cached)) return cached;
        try
        {
            using var http = Client(timeoutSeconds);
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/reader/parse");
            Auth(req, apiKey);
            req.Content = JsonContent.Create(new { text = new[] { text } });
            using var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var parsed = await resp.Content.ReadFromJsonAsync<JitenParseResponse>(_json) ?? new JitenParseResponse();
            var tokens = parsed.Tokens.Count > 0 ? parsed.Tokens[0] : [];
            var byKey = new Dictionary<string, JitenWord>(StringComparer.Ordinal);
            foreach (var word in parsed.Vocabulary)
                byKey[$"{word.WordId}:{word.ReadingIndex}"] = word;
            var result = new ParsedSubtitle { Text = text, Tokens = tokens, Vocabulary = parsed.Vocabulary, VocabByKey = byKey };
            AddCache(text, result, cacheSize);
            _log.Write($"Jiten parse success: tokens={tokens.Count}; vocabulary={parsed.Vocabulary.Count}; text=[{text.Replace('\n', ' ')}]");
            return result;
        }
        catch (Exception ex)
        {
            _log.Write("Jiten parse failed: " + ex.Message);
            return null;
        }
    }

    private void AddCache(string text, ParsedSubtitle parsed, int cacheSize)
    {
        var max = Math.Clamp(cacheSize, 100, 10000);
        if (!_cache.ContainsKey(text)) _cacheOrder.Enqueue(text);
        _cache[text] = parsed;
        while (_cache.Count > max && _cacheOrder.Count > 0)
        {
            var old = _cacheOrder.Dequeue();
            if (!string.Equals(old, text, StringComparison.Ordinal)) _cache.Remove(old);
        }
    }

    public async Task<bool> SetVocabularyStateAsync(string baseUrl, string apiKey, long wordId, int readingIndex, string stateAction, int timeoutSeconds = 30)
    {
        try
        {
            using var http = Client(timeoutSeconds);
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/srs/set-vocabulary-state");
            Auth(req, apiKey);
            req.Content = JsonContent.Create(new { wordId, readingIndex, state = stateAction });
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) _log.Write($"SetVocabularyState returned {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
            ClearCache();
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { _log.Write("SetVocabularyState failed: " + ex.Message); return false; }
    }

    public async Task<bool> ReviewAsync(string baseUrl, string apiKey, long wordId, int readingIndex, int rating, int timeoutSeconds = 30)
    {
        try
        {
            using var http = Client(timeoutSeconds);
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/srs/review");
            Auth(req, apiKey);
            req.Content = JsonContent.Create(new { wordId, readingIndex, rating });
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) _log.Write($"Review returned {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
            ClearCache();
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { _log.Write("Review failed: " + ex.Message); return false; }
    }

    public async Task<List<string>> LookupDeckMembershipAsync(string baseUrl, string apiKey, long wordId, int readingIndex, int timeoutSeconds = 30)
    {
        try
        {
            using var http = Client(timeoutSeconds);
            using var lookupReq = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/reader/lookup-vocabulary");
            Auth(lookupReq, apiKey);
            lookupReq.Content = JsonContent.Create(new { words = new[] { new long[] { wordId, readingIndex } } });
            using var lookupResp = await http.SendAsync(lookupReq);
            if (!lookupResp.IsSuccessStatusCode) return [];

            using var lookupDoc = JsonDocument.Parse(await lookupResp.Content.ReadAsStringAsync());
            var deckIds = ReadFirstDeckIdList(lookupDoc.RootElement);
            if (deckIds.Count == 0) return [];

            // JitenMPV resolves lookup-vocabulary's positional deck ids against reader-study-decks.
            // Do the same, but parse the list defensively so minor DTO changes don't break the popup.
            using var decksReq = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/srs/reader-study-decks");
            Auth(decksReq, apiKey);
            decksReq.Content = JsonContent.Create(new { });
            using var decksResp = await http.SendAsync(decksReq);
            if (!decksResp.IsSuccessStatusCode) return deckIds.Select(id => $"Deck #{id}").ToList();

            using var decksDoc = JsonDocument.Parse(await decksResp.Content.ReadAsStringAsync());
            var namesById = new Dictionary<int, string>();
            CollectDeckObjects(decksDoc.RootElement, namesById);
            return deckIds.Select(id => namesById.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : $"Deck #{id}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) { _log.Write("Deck membership lookup failed: " + ex.Message); return []; }
    }

    private static List<int> ReadFirstDeckIdList(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("decks", out var decks)
            || decks.ValueKind != JsonValueKind.Array)
            return [];
        var first = decks.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Array) return [];
        return first.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _))
            .Select(x => x.GetInt32()).ToList();
    }

    private static void CollectDeckObjects(JsonElement element, Dictionary<int, string> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            int? id = null;
            string? name = null;
            foreach (var p in element.EnumerateObject())
            {
                if (p.NameEquals("id") && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n)) id = n;
                else if ((p.NameEquals("name") || p.NameEquals("title")) && p.Value.ValueKind == JsonValueKind.String)
                    name ??= p.Value.GetString();
            }
            if (id.HasValue && !string.IsNullOrWhiteSpace(name)) result[id.Value] = name!;
            foreach (var p in element.EnumerateObject()) CollectDeckObjects(p.Value, result);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectDeckObjects(item, result);
        }
    }


    public async Task<List<StudyDeckInfo>> GetStudyDecksAsync(string baseUrl, string apiKey, int timeoutSeconds = 30)
    {
        try
        {
            using var http = Client(timeoutSeconds);
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/srs/reader-study-decks");
            Auth(req, apiKey); req.Content = JsonContent.Create(new { });
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) { _log.Write($"StudyDecks returned {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}"); return []; }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var map = new Dictionary<int, string>(); CollectDeckObjects(doc.RootElement, map);
            return map.OrderBy(x => x.Value, StringComparer.CurrentCultureIgnoreCase).Select(x => new StudyDeckInfo(x.Key, x.Value)).ToList();
        }
        catch (Exception ex) { _log.Write("Study deck load failed: " + ex.Message); return []; }
    }

    public async Task<bool> AddToStudyDeckAsync(string baseUrl, string apiKey, int deckId, long wordId, int readingIndex, string? sentence, string? source, int timeoutSeconds = 30)
    {
        try
        {
            using var http = Client(timeoutSeconds);
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + $"/api/srs/study-decks/{deckId}/words");
            Auth(req, apiKey); req.Content = JsonContent.Create(new { wordId, readingIndex, sentence, source });
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) _log.Write($"AddToStudyDeck returned {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
            ClearCache(); return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { _log.Write("AddToStudyDeck failed: " + ex.Message); return false; }
    }

    public async Task<List<int>> LookupDeckIdsAsync(string baseUrl, string apiKey, long wordId, int readingIndex, int timeoutSeconds = 30)
    {
        try
        {
            using var http = Client(timeoutSeconds);
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/reader/lookup-vocabulary");
            Auth(req, apiKey); req.Content = JsonContent.Create(new { words = new[] { new long[] { wordId, readingIndex } } });
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return [];
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return ReadFirstDeckIdList(doc.RootElement);
        }
        catch (Exception ex) { _log.Write("Deck id lookup failed: " + ex.Message); return []; }
    }

    public async Task<JitenPlusInfo> GetJitenPlusStatusAsync(string baseUrl, string apiKey, int timeoutSeconds = 30)
    {
        try
        {
            using var http = Client(timeoutSeconds);
            using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/api/jiten-plus/status");
            Auth(req, apiKey);
            using var resp = await http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return new(false, "Free", 0, 0, $"Jiten+ check failed ({(int)resp.StatusCode}).");
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            bool plus = ReadBoolRecursive(root, "isPlus", "hasJitenPlus", "active", "subscribed", "isActive");
            var tier = ReadStringRecursive(root, "tier", "plan", "subscription", "name") ?? (plus ? "Jiten+" : "Free");
            var used = ReadLongRecursive(root, "usedBytes", "used", "storageUsedBytes");
            var max = ReadLongRecursive(root, "maxBytes", "limitBytes", "quotaBytes", "storageMaxBytes");
            // A positive quota is itself strong evidence the account is entitled even if the status DTO changes.
            if (max > 0) plus = true;
            return new(plus, tier, used, max, plus ? "Jiten+ media mining is available." : "Jiten+ is required for card media.");
        }
        catch (Exception ex) { _log.Write("Jiten+ status failed: " + ex.Message); return new(false, "Unknown", 0, 0, "Could not check Jiten+ status."); }
    }

    public async Task<ExistingCardMedia> GetCardMediaAsync(string baseUrl, string apiKey, long wordId, int readingIndex, int timeoutSeconds = 30)
    {
        try
        {
            using var http = Client(timeoutSeconds);
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/srs/card-media/batch");
            Auth(req, apiKey); req.Content = JsonContent.Create(new { items = new[] { new { wordId, readingIndex } } });
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return new(false, false);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var hasImage = ContainsTruthyMedia(doc.RootElement, "image", "imageUrl", "imageFile", "hasImage");
            var hasAudio = ContainsTruthyMedia(doc.RootElement, "audio", "audioUrl", "audioFile", "hasAudio");
            return new(hasImage, hasAudio);
        }
        catch (Exception ex) { _log.Write("Card media lookup failed: " + ex.Message); return new(false, false); }
    }

    public async Task<MediaUploadResult> UploadCardMediaAsync(string baseUrl, string apiKey, long wordId, int readingIndex, MiningMediaFile file, int timeoutSeconds = 30)
    {
        try
        {
            using var http = Client(Math.Max(120, timeoutSeconds));
            using var content = new MultipartFormDataContent();
            var part = new ByteArrayContent(file.Bytes);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(part, "file", file.FileName);
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + $"/api/srs/card-media/{wordId}/{readingIndex}");
            Auth(req, apiKey); req.Content = content;
            using var resp = await http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
            {
                try { using var doc = JsonDocument.Parse(body); return MediaUploadResult.Ok(ReadLongRecursive(doc.RootElement,"usedBytes"), ReadLongRecursive(doc.RootElement,"maxBytes")); }
                catch { return MediaUploadResult.Ok(); }
            }
            long used=0,max=0; string error=$"HTTP {(int)resp.StatusCode}";
            try { using var doc=JsonDocument.Parse(body); used=ReadLongRecursive(doc.RootElement,"usedBytes"); max=ReadLongRecursive(doc.RootElement,"maxBytes"); error=ReadStringRecursive(doc.RootElement,"error","message")??error; } catch { if(!string.IsNullOrWhiteSpace(body)) error=body; }
            _log.Write($"Card media upload failed for {file.Kind}: {error}");
            return resp.StatusCode == System.Net.HttpStatusCode.BadRequest && max > 0 ? MediaUploadResult.Quota(used,max,error) : MediaUploadResult.Fail(error);
        }
        catch (Exception ex) { _log.Write("Card media upload failed: " + ex.Message); return MediaUploadResult.Fail(ex.Message); }
    }

    private static bool ContainsTruthyMedia(JsonElement e, params string[] names)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (names.Any(n => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                {
                    if (p.Value.ValueKind == JsonValueKind.True) return true;
                    if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString())) return true;
                    if (p.Value.ValueKind == JsonValueKind.Object && p.Value.EnumerateObject().Any()) return true;
                }
                if (ContainsTruthyMedia(p.Value, names)) return true;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) if (ContainsTruthyMedia(x,names)) return true;
        return false;
    }

    private static bool ReadBoolRecursive(JsonElement e, params string[] names)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                if (names.Any(n => p.Name.Equals(n,StringComparison.OrdinalIgnoreCase)) && p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) return p.Value.GetBoolean();
                if (ReadBoolRecursive(p.Value,names)) return true;
            }
        else if (e.ValueKind == JsonValueKind.Array) foreach(var x in e.EnumerateArray()) if(ReadBoolRecursive(x,names)) return true;
        return false;
    }
    private static string? ReadStringRecursive(JsonElement e, params string[] names)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                if (names.Any(n => p.Name.Equals(n,StringComparison.OrdinalIgnoreCase)) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
                var nested=ReadStringRecursive(p.Value,names); if(nested is not null) return nested;
            }
        else if (e.ValueKind == JsonValueKind.Array) foreach(var x in e.EnumerateArray()){var nested=ReadStringRecursive(x,names);if(nested is not null)return nested;}
        return null;
    }
    private static long ReadLongRecursive(JsonElement e, params string[] names)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                if (names.Any(n => p.Name.Equals(n,StringComparison.OrdinalIgnoreCase)) && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var v)) return v;
                var nested=ReadLongRecursive(p.Value,names); if(nested!=0)return nested;
            }
        else if(e.ValueKind==JsonValueKind.Array)foreach(var x in e.EnumerateArray()){var nested=ReadLongRecursive(x,names);if(nested!=0)return nested;}
        return 0;
    }

    public static List<RenderSegment> BuildSegments(string text, ParsedSubtitle? parsed)
    {
        if (parsed is null || parsed.Tokens.Count == 0) return [new RenderSegment(text, null, null)];
        var result = new List<RenderSegment>();
        var cursor = 0;
        foreach (var t in parsed.Tokens.OrderBy(t => t.Start))
        {
            var start = Math.Max(0, t.Start);
            var end = t.End;
            if (end <= start && t.Length is > 0) end = start + t.Length.Value;
            if (start > text.Length) continue;
            end = Math.Min(end, text.Length);
            if (start > cursor) result.Add(new RenderSegment(text.Substring(cursor, start - cursor), null, null));
            if (end > start)
            {
                var surface = text.Substring(start, end - start);
                JitenWord? word = null;
                if (t.WordId is not null && t.ReadingIndex is not null)
                    parsed.VocabByKey.TryGetValue($"{t.WordId}:{t.ReadingIndex}", out word);
                result.Add(new RenderSegment(surface, word, t));
                cursor = Math.Max(cursor, end);
            }
        }
        if (cursor < text.Length) result.Add(new RenderSegment(text[cursor..], null, null));
        return result;
    }

    public static int CollapseKnownState(JitenWord? word)
    {
        if (word is null || word.KnownState.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return -1;
        var states = new HashSet<int>();
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { ["new"] = 0, ["young"] = 1, ["mature"] = 2, ["blacklisted"] = 3, ["due"] = 4, ["mastered"] = 5, ["redundant"] = 6, ["suspended"] = 7 };
        IEnumerable<JsonElement> elems = word.KnownState.ValueKind == JsonValueKind.Array ? word.KnownState.EnumerateArray() : [word.KnownState];
        foreach (var e in elems)
        {
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n)) states.Add(n);
            else if (e.ValueKind == JsonValueKind.String && map.TryGetValue(e.GetString() ?? "", out var mapped)) states.Add(mapped);
        }
        foreach (var p in new[] { 3, 5, 6, 7, 4, 2, 1, 0 }) if (states.Contains(p)) return p;
        return -1;
    }
}
