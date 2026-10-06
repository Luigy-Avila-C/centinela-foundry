# Datos de ejemplo

`empresa-ejemplo/` contiene documentos internos de **«Distribuciones Aurora, S.L.», una empresa inventada**
para probar Centinela. Ningún dato corresponde a una empresa, persona ni contrato reales, y cada archivo lo
indica en su cabecera.

Se escribieron a propósito con casos fáciles, difíciles y engañosos (pasajes que *parecen* afectados por una
norma y no lo están) para poder medir el evaluador de impacto. Las etiquetas están en
`evaluaciones/impacto-aurora.json`.

## Probarlo

```bash
# 1. Indexar los documentos (índice «empresa»)
dotnet run --project src/Centinela.Cli -- empresa-indexar datos/empresa-ejemplo

# 2. Ver qué pasajes afecta una norma ya analizada (resultado guardado de `cobertura`)
dotnet run --project src/Centinela.Cli -- impacto evaluaciones/resultados/<resultado>.json --ejecucion 1

# 3. Medirlo contra las etiquetas (parte «dev» para ajustar; «test» solo para la medida final)
dotnet run --project src/Centinela.Cli -- evaluar-impacto evaluaciones/resultados/<resultado>.json --ejecucion 1 --parte dev
```

Para usar documentos propios basta con Markdown: el título es `# Título`, cada sección `## Encabezado`, y las
líneas que empiezan por `>` se ignoran.
