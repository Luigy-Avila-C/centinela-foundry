namespace Centinela.Application;

public enum AnalysisMode
{
    /// <summary>Una única llamada con la norma entera. Barato, pero omite apartados de artículos largos.</summary>
    Single,

    /// <summary>Una llamada por artículo, para obligar a recorrer cada uno de forma exhaustiva.</summary>
    PerSection,
}

public sealed class AnalysisOptions
{
    public const string SectionName = "Analysis";

    /// <summary>
    /// Por artículo: es el modo por defecto porque recoge contenido que el modo único omite en todas las
    /// ejecuciones (ver docs/ARQUITECTURA.md), a costa de unas 2,5 veces más afirmaciones que verificar.
    /// </summary>
    public AnalysisMode Mode { get; set; } = AnalysisMode.PerSection;

    /// <summary>
    /// En el modo por artículo se saltan los fragmentos cuya etiqueta empieza así. Por defecto, el
    /// preámbulo (exposición de motivos, sin obligaciones) y los anexos (tablas técnicas).
    /// </summary>
    public string[] SkipLabelPrefixes { get; set; } = ["Preámbulo", "ANEXO"];

    /// <summary>Fragmentos más cortos que esto no se analizan por separado (cabeceras, firmas).</summary>
    public int MinSectionChars { get; set; } = 100;

    /// <summary>Fragmentos de normativa previa que se recuperan para cada artículo.</summary>
    public int RelatedPerSection { get; set; } = 3;

    public int Parallelism { get; set; } = 4;
}
