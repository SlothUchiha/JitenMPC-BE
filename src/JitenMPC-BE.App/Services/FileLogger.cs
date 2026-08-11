namespace JitenMpcBe.Services;

public sealed class FileLogger
{
    private const long MaxFileBytes = 5L * 1024 * 1024;
    private const int BackupCount = 3;

    private readonly object _gate = new();
    public string Path { get; }
    public bool Enabled { get; set; } = true;

    public FileLogger(string baseDirectory)
    {
        Path = System.IO.Path.Combine(baseDirectory, "JitenMPC-BE.log");
    }

    public void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (_gate)
            {
                RotateIfNeeded();
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(Path) || new FileInfo(Path).Length < MaxFileBytes) return;

        var oldest = BackupPath(BackupCount);
        if (File.Exists(oldest)) File.Delete(oldest);

        for (var i = BackupCount - 1; i >= 1; i--)
        {
            var source = BackupPath(i);
            if (File.Exists(source)) File.Move(source, BackupPath(i + 1), true);
        }

        File.Move(Path, BackupPath(1), true);
    }

    private string BackupPath(int index) => $"{Path}.{index}";
}
