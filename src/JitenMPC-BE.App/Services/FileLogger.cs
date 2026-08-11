namespace JitenMpcBe.Services;

public sealed class FileLogger
{
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
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
