using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Centinela.Domain;

namespace Centinela.Application;

/// <summary>
/// Un problema encontrado en un borrador.
/// </summary>
/// <param name="Type">no_cubre, dato_inventado, cambio_innecesario, contradice_norma, cita_invalida, sin_cambios o ambiguo.</param>
/// <param name="Blocking">Si impide aprobar el borrador. «ambiguo» y «pendiente» solo avisan.</param>
/// <param name="Origin">«automatica» (código, sin modelo) o «modelo» (el auditor).</param>
public sealed record DraftIssue(
    string Type, string Description, string? Quote = null, int? Obligation = null,
    bool Blocking = true, string Origin = "automatica");

/// <summary>
/// Comprobaciones de un borrador que no necesitan ningún modelo. Son la parte más fiable de la auditoría: no se
/// pueden convencer. Detectan lo que un redactor con prisa hace de forma más típica: cumplir la norma de palabra
/// sin cambiar nada, inventar cifras, reescribir todo el pasaje o citar fuentes que no existen.
/// </summary>
public static partial class DraftChecks
{
    /// <summary>
    /// Fracción mínima de las palabras del original que debe conservar el borrador. Por debajo se considera que
    /// reescribe el pasaje en lugar de corregir lo afectado. Es una heurística: se ajusta con los casos buenos.
    /// </summary>
    public const double MinPreserved = 0.5;

    public const string PlaceholderMarker = "[COMPLETAR:";

    /// <param name="problemQuote">La frase del pasaje que el evaluador de impacto identificó como lo que incumple.</param>
    /// <param name="problemEffect">Efecto del hallazgo; solo «incumple» exige que la frase desaparezca o cambie.</param>
    public static IReadOnlyList<DraftIssue> Run(
        CorrectiveAction action,
        IReadOnlyList<string> obligations,
        IReadOnlyCollection<string> allowedNormIds,
        string externalText,
        string? problemQuote = null,
        string? problemEffect = null)
    {
        var issues = new List<DraftIssue>();
        var proposed = action.ProposedText;
        var original = action.OriginalText ?? "";

        if (string.IsNullOrWhiteSpace(proposed))
        {
            return [new DraftIssue("sin_cambios", "El borrador está vacío.")];
        }

        // 1. Cada obligación debe tener una cita literal de dónde se cumple.
        for (var i = 0; i < obligations.Count; i++)
        {
            var entry = action.Coverage?.FirstOrDefault(c => c.Obligation == obligations[i]);
            if (entry is null)
            {
                issues.Add(new DraftIssue("no_cubre",
                    $"El borrador no indica dónde cumple la obligación {i + 1}: «{Shorten(obligations[i])}».", null, i + 1));
            }
            else if (!QuoteMatch.Appears(entry.Quote, proposed))
            {
                issues.Add(new DraftIssue("no_cubre",
                    $"La cita con la que el borrador dice cumplir la obligación {i + 1} no figura literalmente en su texto.",
                    entry.Quote, i + 1));
            }
        }

        // 2. Cifras nuevas: un número que no estaba en el original, ni en las obligaciones, ni en la norma.
        var allowedNumbers = NumbersIn(original + " " + string.Join(" ", obligations) + " " + externalText);
        foreach (var n in NumbersIn(StripPlaceholders(proposed)).Except(allowedNumbers).Order(StringComparer.Ordinal))
        {
            issues.Add(new DraftIssue("dato_inventado",
                $"El borrador introduce la cifra «{n}», que no aparece en el pasaje original, en las obligaciones ni en la norma.", n));
        }

        // 3. Cambio mínimo: el borrador debe conservar lo que no estaba afectado.
        if (original.Length > 0)
        {
            var kept = PreservedRatio(original, proposed);
            if (kept < MinPreserved)
            {
                issues.Add(new DraftIssue("cambio_innecesario",
                    $"El borrador conserva solo el {kept:P0} de las palabras del pasaje original (mínimo {MinPreserved:P0}): " +
                    "reescribe el pasaje en lugar de corregir lo afectado."));
            }

            if (Normalize(original) == Normalize(proposed))
            {
                issues.Add(new DraftIssue("sin_cambios", "El borrador es idéntico al pasaje original."));
            }
        }

        // 3b. Si el pasaje CONTRADECÍA la norma, la frase que la contradecía no puede seguir ahí tal cual. Un borrador que
        // la conserva y añade debajo lo contrario se contradice a sí mismo.
        if (problemEffect == "incumple" && problemQuote is { } pq && SentenceSurvives(pq, proposed))
        {
            issues.Add(new DraftIssue("problema_persiste",
                $"El borrador conserva tal cual lo que el pasaje tenía de incumplimiento: «{Shorten(pq)}». Hay que eliminarlo o cambiarlo.", pq));
        }

        // 4. Citas a la norma: deben existir y haber al menos una.
        var cited = action.NormCitations ?? [];
        var unknown = cited.Where(c => !allowedNormIds.Contains(c)).ToList();
        if (cited.Count == 0 || unknown.Count > 0)
        {
            issues.Add(new DraftIssue("cita_invalida", cited.Count == 0
                ? "El borrador no cita ningún fragmento de la norma."
                : $"El borrador cita fragmentos de la norma que no se le entregaron: {string.Join(", ", unknown)}."));
        }

        // 5. Marcadores pendientes: no es un fallo, es lo que debe completar una persona.
        var placeholders = PlaceholderCount(proposed);
        if (placeholders > 0)
        {
            issues.Add(new DraftIssue("pendiente",
                $"El borrador deja {placeholders} dato(s) por completar por la empresa.", null, null, Blocking: false));
        }

        return issues;
    }

    /// <summary>Fracción de las palabras del original (contando repeticiones) que sigue en el borrador.</summary>
    public static double PreservedRatio(string original, string proposed)
    {
        var o = Words(original);
        if (o.Count == 0) return 1.0;

        var available = Words(proposed).GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        var kept = 0;
        foreach (var w in o)
        {
            if (available.TryGetValue(w, out var n) && n > 0)
            {
                kept++;
                available[w] = n - 1;
            }
        }

        return (double)kept / o.Count;
    }

    /// <summary>
    /// ¿Sigue el texto de <paramref name="quote"/> en <paramref name="proposed"/> como una oración que termina ahí?
    /// Si lo que sigue a la frase es una coma o una palabra en minúscula, la oración continúa (p. ej. «, salvo la
    /// copia UBL»): se ha modificado y ya no es la misma. Se compara sin distinguir mayúsculas ni espacios.
    /// </summary>
    public static bool SentenceSurvives(string quote, string proposed)
    {
        const int MinChars = 12; // una cita muy corta coincidiría con cualquier cosa

        var q = Regex.Replace(quote, @"\s+", " ").Trim().TrimEnd('.', ';', '!', '?', ' ');
        if (q.Length < MinChars) return false;

        var p = Regex.Replace(proposed, @"\s+", " ");
        var from = 0;
        while (true)
        {
            var i = p.IndexOf(q, from, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;

            var end = i + q.Length;
            if (EndsSentence(p, end)) return true;
            from = i + 1;
        }
    }

    private static bool EndsSentence(string text, int end)
    {
        if (end >= text.Length) return true;

        var c = text[end];
        if (c is '.' or '!' or '?' or ';' or ':') return true;
        if (c == ',') return false;

        // Tras un espacio: mayúscula o fin de texto = nueva oración; minúscula = la misma oración continúa.
        var j = end;
        while (j < text.Length && text[j] == ' ') j++;
        return j >= text.Length || !char.IsLower(text[j]);
    }

    public static int PlaceholderCount(string text) =>
        PlaceholderRegex().Matches(text).Count;

    public static string StripPlaceholders(string text) => PlaceholderRegex().Replace(text, " ");

    private static HashSet<string> NumbersIn(string text) =>
        NumberRegex().Matches(text).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

    private static List<string> Words(string text) =>
        WordRegex().Matches(Normalize(text)).Select(m => m.Value).ToList();

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static string Shorten(string s) => s.Length <= 90 ? s : s[..90] + "…";

    [GeneratedRegex(@"\[COMPLETAR:[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();
}
