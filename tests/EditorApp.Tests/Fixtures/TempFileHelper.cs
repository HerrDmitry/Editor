using System.Text;

namespace EditorApp.Tests.Fixtures;

public static class TempFileHelper
{
    public static string CreateTempFile(string content, Encoding? encoding = null)
    {
        var path = Path.GetTempFileName();
        var enc = encoding ?? new UTF8Encoding(false); // UTF-8 without BOM
        File.WriteAllBytes(path, enc.GetPreamble().Concat(enc.GetBytes(content)).ToArray());
        return path;
    }

    public static string CreateTempFileRawBytes(byte[] bytes)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static void Cleanup(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
