using System.Globalization;
using System.Text;

namespace Centinela.Application;

/// <summary>
/// Comprobación de citas literales: la base de «el modelo propone, el código verifica». Cualquier agente que pida a un modelo
/// que copie una frase de un texto (guardián, impacto, redactor, auditor, medidor de cobertura) la valida aquí, y descarta lo
/// que no aparezca de verdad.
/// </summary>
public static class QuoteMatch
{
    /// <summary>Una cita más corta que esto casi siempre «aparece» por casualidad; no se acepta.</summary>
    public const int MinChars = 8;

    /// <summary>
    /// ¿Aparece la cita en el texto? Se compara sin mayúsculas, acentos ni puntuación, porque un modelo al copiar suele cambiar
    /// comillas o tildes, pero una cita inventada no pasa esta prueba.
    /// </summary>
    public static bool Appears(string quote, string text)
    {
        var q = Normalize(quote);
        return q.Length >= MinChars && Normalize(text).Contains(q, StringComparison.Ordinal);
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastWasSpace = true;

        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue; // quita tildes

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' '); // puntuación y espacios → un único espacio
                lastWasSpace = true;
            }
        }

        return sb.ToString().Trim();
    }
}
