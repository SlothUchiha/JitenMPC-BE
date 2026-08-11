namespace JitenMpcBe.Models;

public sealed record StudyDeckInfo(int Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record JitenPlusInfo(bool IsPlus, string Tier, long UsedBytes, long MaxBytes, string Status)
{
    public string TierLabel => string.IsNullOrWhiteSpace(Tier) ? (IsPlus ? "Jiten+" : "Free") : Tier;
    public string QuotaLabel => MaxBytes > 0 ? $"{FormatBytes(UsedBytes)} / {FormatBytes(MaxBytes)}" : "";
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GB" : $"{bytes / (1024d * 1024):0.0} MB";
}

public enum MediaOverwriteDecision { Cancel, Replace, SkipMedia }

public sealed record ExistingCardMedia(bool HasImage, bool HasAudio);
public sealed record MediaUploadResult(bool Success, bool QuotaExceeded, long UsedBytes, long MaxBytes, string Error)
{
    public static MediaUploadResult Ok(long used = 0, long max = 0) => new(true, false, used, max, "");
    public static MediaUploadResult Fail(string error) => new(false, false, 0, 0, error);
    public static MediaUploadResult Quota(long used, long max, string error) => new(false, true, used, max, error);
}

public sealed record MiningMediaFile(byte[] Bytes, string FileName, string ContentType, string Kind);
public sealed class MiningMediaBundle
{
    public MiningMediaFile? Image { get; init; }
    public MiningMediaFile? Audio { get; init; }
    public string? PreviewImagePath { get; init; }
    public string? PreviewAudioPath { get; init; }
    public double ImageTime { get; init; }
    public double AudioStart { get; init; }
    public double AudioEnd { get; init; }
}

public sealed class MiningReviewResult
{
    public bool Accepted { get; init; }
    public double ImageTime { get; init; }
    public double AudioStart { get; init; }
    public double AudioEnd { get; init; }
    public string Sentence { get; init; } = "";
}
