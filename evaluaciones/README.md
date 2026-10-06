# Evaluaciones

Conjuntos etiquetados, umbrales y resultados con los que se miden los agentes. Qué significan las cifras y qué **no** demuestran:
[`docs/EVALUACION.md`](../docs/EVALUACION.md). Léelo antes de citar un número.

| Fichero | Mide | Comando |
|---|---|---|
| `verificador-citas.json` | ¿Distingue el verificador una cita que respalda una afirmación de una que no? | `evaluar` |
| `cobertura-orden-hac-1028-2026.json` | ¿Qué parte del contenido material de la norma recoge el análisis? | `cobertura`, `rejuzgar`, `control-medidor`, `calibrar-medidor` |
| `impacto-aurora.json` | ¿Qué pasajes de los documentos de la empresa afecta la norma? | `evaluar-impacto` |
| `auditor-borradores.json` | ¿Aprueba el auditor los borradores buenos y rechaza los defectuosos? | `evaluar-auditor` |
| `guardian-inyecciones.json` | ¿Detecta el guardián los ataques sin bloquear texto legítimo? | `evaluar-guardian` |
| `umbrales.json` | Umbrales de **regresión** de la puerta | `puerta` |

## Dos partes por conjunto

- `dev`: se usa para ajustar los prompts y las reglas.
- `test`: reservada. Se ejecuta para dar la cifra final y **no se usa para ajustar**; si se ajusta mirándola, deja de medir nada.

Los comandos usan `dev` por defecto. Cada ejecución llama a modelos de pago (céntimos); requieren `az login` y las variables de
[`docs/DESPLIEGUE.md`](../docs/DESPLIEGUE.md).

## La puerta de regresión

```bash
dotnet run --project src/Centinela.Cli -- puerta --salida puerta.json     # ≈ 0,15 USD; código de salida 1 si algo empeora
```

Pasa el verificador, el guardián, el auditor y el evaluador de impacto sobre `dev` contra `umbrales.json`. Los umbrales están justo por debajo de lo
medido: detectan regresiones grandes, no deterioros sutiles. **No los subas para que un cambio pase**: explica con datos por qué empeora o por qué el
umbral estaba mal calibrado.

## Resultados guardados (`resultados/`)

| Fichero | Qué es |
|---|---|
| `cobertura-…-seccion-….json` | Tres ejecuciones del análisis por artículo (entrada de `caso`, `impacto` y de la puerta) |
| `cobertura-…-1118.json` | Tres ejecuciones del análisis en una sola llamada, para comparar |
| `control-neg-*.json`, `control-pos-*.json` | Controles negativos y positivos del medidor de cobertura (laxo y estricto) |
| `caso-aurora.md`, `caso-aurora-completo.md` | Informes de casos completos sobre la empresa ficticia; el segundo, con el analista real y el consumo de tokens |

## Añadir casos

Cada caso lleva un `id` único; en el verificador, cita solo fuentes definidas en `fuentes`. Las pruebas comprueban que los ficheros estén bien
formados y que cada parte tenga casos de todas las clases. Los casos malos se fabrican a propósito: miden si el agente *distingue*, no con qué
frecuencia se equivoca de forma natural.
