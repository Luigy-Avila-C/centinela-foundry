namespace Centinela.Cli;

/// <summary>Utilidades comunes de los comandos.</summary>
internal static class CliUtil
{
    public static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    public static string Shorten(string s, int length) => s.Length <= length ? s : s[..length] + "…";
}
