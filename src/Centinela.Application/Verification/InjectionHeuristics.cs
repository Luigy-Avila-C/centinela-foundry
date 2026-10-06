using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Centinela.Application;

/// <summary>Un indicio de inyección encontrado por las reglas del código.</summary>
/// <param name="Kind">Familia del indicio (<c>anula_instrucciones</c>, <c>ruptura_delimitador</c>…).</param>
/// <param name="Quote">Fragmento detectado. Sale del texto NORMALIZADO (sin tildes, en minúsculas), no del original.</param>
public sealed record InjectionSignal(string Kind, string Quote);

/// <summary>
/// Primera capa del guardián: reglas deterministas, explicables y baratas. No juzgan «el sentido» del texto, solo
/// buscan formas conocidas de dar órdenes a un modelo: frases de anulación de instrucciones, cierres de las etiquetas
/// que usan nuestros prompts, caracteres invisibles, mensajes ocultos y cargas codificadas.
/// Cada regla es deliberadamente estrecha: el texto legal está lleno de órdenes («se ordena», «deberán»), y una regla
/// ancha bloquearía normas reales. Lo que escape a estas reglas lo busca el modelo (<see cref="SecurityGuardAgent"/>).
/// </summary>
public static class InjectionHeuristics
{
    private const RegexOptions Opts = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // Reglas sobre el texto normalizado (ver Fold): minúsculas, sin tildes ni invisibles, espacios colapsados.
    private static readonly (string Kind, Regex Pattern)[] Rules =
    [
        ("anula_instrucciones", new(@"\b(ignora|ignore|ignorad|ignorez|olvida|olvidad|oblida|forget|disregard)\b.{0,40}\b(instrucciones|instruccions|indicaciones|reglas|ordenes|directrices|instructions|rules|prompts?|guidelines|regles|consignes|anteriores|anterior|previous|above)\b", Opts)),
        ("anula_instrucciones", new(@"\b(a partir de ahora|from now on|desde ahora|desormais|a partir d ara)\b.{0,60}\b(eres|actua|responde|siempre|you are|act as|respond|always|tu es)\b", Opts)),
        ("anula_instrucciones", new(@"\b(you are now|ahora eres|nuevo rol|new role|finge (ser|que)|fingir|pretend (to be|you)|actua como (un |una |el |la )?(asistente|modelo de lenguaje|ia|inteligencia artificial|agente|bot|experto|auditor)|act as (a |an |the )?(assistant|ai|agent|bot|auditor|language model))\b", Opts)),
        ("anula_instrucciones", new(@"\b(system prompt|prompt del sistema|instrucciones del sistema|jailbreak|developer mode|modo desarrollador)\b", Opts)),

        ("dirigido_a_ia", new(@"\b(si eres|if you are|if you re|si tu es|si ets)\b.{0,30}\b(ia|inteligencia artificial|llm|modelo de lenguaje|asistente|ai|language model|bot|chatbot)\b", Opts)),
        ("dirigido_a_ia", new(@"\b(nota|aviso|mensaje|atencion|importante|note|notice|important)\b.{0,15}\b(para|to|for|pour)\b.{0,12}\b(asistente|ia|llm|assistant|ai|modelo de lenguaje|language model|agente de ia|motores? de analisis|herramientas? de analisis)\b", Opts)),
        ("dirigido_a_ia", new(@"\b(al|para el|para la|to the|for the)\s+(asistente de ia|modelo de lenguaje|llm|agente de ia|ai assistant|language model|sistema de ia)\b", Opts)),

        ("orden_de_resultado", new(@"\b(clasifica|clasificalo|clasifique|marca|marcalo|etiqueta|etiquetalo|classify|mark|label)\b.{0,50}\b(como|as)\b.{0,20}\b(no relevante|irrelevante|no aplica|seguro|aprobado|valido|approved|not relevant|irrelevant|safe|valid|sin impacto)\b", Opts)),
        ("orden_de_resultado", new(@"\b(aprueba|aprobar|approve)\b.{0,30}\b(automaticamente|sin revision|sin revisar|automatically|without review)\b", Opts)),
        ("orden_de_resultado", new(@"\b(relevante|relevant|is safe|issafe|sospechoso|incidencias|hallazgos)\b""?\s*[:=]\s*(true|false|\[\s*\])", Opts)),
        ("orden_de_resultado", new(@"\b(responde|respond|reply|answer|devuelve|return|output|contesta|say)\b.{0,40}\b(no es relevante|no relevante|irrelevante|not relevant|irrelevant|nothing applies|no changes required|ninguna empresa|no company)\b", Opts)),

        ("exfiltracion", new(@"\b(revela|muestra|imprime|repite|reveal|print|show|repeat|leak|filtra|dime|tell me|divulga)\b.{0,30}\b(tus|tu|your)\b.{0,20}\b(instrucciones|prompt|configuracion|reglas|instructions|secrets?|claves?|api keys?|credenciales|tokens?|credentials|settings)\b", Opts)),
        ("exfiltracion", new(@"\b(revela|muestra|imprime|repite|reveal|print|show|repeat|leak|filtra|divulga)\b.{0,25}\b(instrucciones|reglas|prompt)\s+(internas?|ocultas?|originales|iniciales|completas|internal|hidden|original|initial)\b", Opts)),
        ("exfiltracion", new(@"\b(envia|enviar|send|post|sube|upload|exfiltra)\b.{0,40}\b(datos|data|conversacion|prompt|instrucciones|secrets?|claves?|credenciales|credentials|configuracion)\b.{0,60}https?://", Opts)),

        ("ruptura_delimitador", new(@"</?\s*(publicacion|norma|pasaje|obligaciones?|problema_detectado|identificadores_validos|incidencias_del_auditor|alcance|documento|fragmento|system|assistant|user|instructions?|prompt|borrador|cobertura)\b[^>]{0,40}>", Opts)),
        ("ruptura_delimitador", new(@"\[/?inst\]|<\|[a-z_]{2,20}\|>|<<sys>>|\[/?system\]", Opts)),
        ("ruptura_delimitador", new(@"(^|\s)(system|sistema|assistant|asistente|developer)\s*:\s*(ignora|ignore|nueva|new|la |you|eres|a partir|de ahora|olvida)", Opts)),
    ];

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex Base64Run = new(@"[A-Za-z0-9+/]{60,}={0,2}", RegexOptions.Compiled);

    // Letras de otros alfabetos que se escriben igual que una latina: la forma típica de esquivar un filtro de palabras.
    private static readonly Dictionary<char, char> Homoglyphs = new()
    {
        ['а'] = 'a', ['е'] = 'e', ['о'] = 'o', ['р'] = 'p', ['с'] = 'c', ['х'] = 'x', ['у'] = 'y',
        ['і'] = 'i', ['ѕ'] = 's', ['ј'] = 'j', ['һ'] = 'h', ['ԁ'] = 'd', ['ɡ'] = 'g', ['к'] = 'k',
        ['м'] = 'm', ['н'] = 'h', ['т'] = 't', ['в'] = 'b',
        ['ο'] = 'o', ['α'] = 'a', ['ν'] = 'v', ['ι'] = 'i', ['ρ'] = 'p', ['τ'] = 't', ['υ'] = 'u', ['κ'] = 'k',
    };

    /// <summary>Busca indicios de inyección en un texto. Una lista vacía significa «las reglas no ven nada», no «es seguro».</summary>
    public static IReadOnlyList<InjectionSignal> Scan(string text)
    {
        var signals = new List<InjectionSignal>();

        var invisible = text.Count(IsInvisible) + TagPair.Matches(text).Count;
        if (invisible > 0)
        {
            signals.Add(new("caracteres_invisibles", $"{invisible} carácter(es) invisible(s) o de control bidireccional"));
        }

        var hidden = HiddenAscii(text);
        if (hidden.Length >= 4)
        {
            signals.Add(new("texto_oculto", Shorten(hidden)));
        }

        var folded = Fold(text);
        foreach (var (kind, pattern) in Rules)
        {
            foreach (Match m in pattern.Matches(folded))
            {
                signals.Add(new(kind, Shorten(m.Value.Trim())));
            }
        }

        foreach (Match m in Base64Run.Matches(text))
        {
            var decoded = TryDecodeText(m.Value);
            if (decoded is not null) signals.Add(new("carga_codificada", Shorten($"{m.Value[..24]}… → {decoded}")));
        }

        return signals;
    }

    /// <summary>
    /// Normaliza para comparar: compatibilidad Unicode (anchura completa → normal), sin invisibles ni tildes, en
    /// minúsculas, homoglifos cirílicos y griegos pasados a latino, y espacios colapsados.
    /// </summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in TagPair.Replace(text, "").Normalize(NormalizationForm.FormKD))
        {
            if (IsInvisible(ch) || char.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var lower = char.ToLowerInvariant(ch);
            sb.Append(Homoglyphs.TryGetValue(lower, out var latin) ? latin : lower);
        }

        return Whitespace.Replace(sb.ToString(), " ").Trim();
    }

    // Caracteres que no se ven: anchura cero, marcas direccionales, separadores de formato y la etiqueta Unicode
    // U+E0000–E007F (par suplente DB40 + DC00–DC7F), con la que se puede escribir texto ASCII invisible.
    private static bool IsInvisible(char c) =>
        c is (>= (char)0x200B and <= (char)0x200F)
        or (>= (char)0x202A and <= (char)0x202E)
        or (>= (char)0x2060 and <= (char)0x2064)
        or (>= (char)0x2066 and <= (char)0x2069)
        or (char)0xFEFF;

    // Par suplente de la etiqueta Unicode: no se puede tratar suelto, porque el suplente bajo también forma emojis.
    private static readonly Regex TagPair = new($"{(char)0xDB40}[{(char)0xDC00}-{(char)0xDC7F}]", RegexOptions.Compiled);

    /// <summary>Decodifica el texto escondido en caracteres de etiqueta Unicode (invisibles).</summary>
    private static string HiddenAscii(string text)
    {
        var sb = new StringBuilder();
        for (var i = 0; i + 1 < text.Length; i++)
        {
            if (text[i] == '\uDB40' && text[i + 1] is >= '\uDC00' and <= '\uDC7F')
            {
                sb.Append((char)(text[i + 1] - '\uDC00'));
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>Si un tramo base64 decodifica a texto legible lo devuelve; los hash y binarios dan null.</summary>
    private static string? TryDecodeText(string run)
    {
        var padded = run.PadRight((run.Length + 3) / 4 * 4, '=');
        var buffer = new byte[padded.Length];
        if (!Convert.TryFromBase64String(padded, buffer, out var written) || written < 20) return null;

        string decoded;
        try { decoded = new UTF8Encoding(false, true).GetString(buffer, 0, written); }
        catch (ArgumentException) { return null; }

        var letters = decoded.Count(c => char.IsLetter(c) || c == ' ');
        return decoded.Contains(' ') && letters >= decoded.Length * 0.85 ? decoded : null;
    }

    private static string Shorten(string s) => s.Length <= 160 ? s : s[..160] + "…";
}
