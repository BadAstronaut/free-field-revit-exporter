namespace FreeField.Exporter.Revit.Automation;

public static class Log
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeFieldExporter", "logs");
    public static void Write(string msg)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path.Combine(Dir, "exporter.log"), $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
