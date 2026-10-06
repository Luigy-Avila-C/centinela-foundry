# Cómo contribuir

¡Gracias por querer mejorar Centinela! Es un proyecto de aprendizaje y de guía de arquitectura: las contribuciones que más valen son las
que **hacen más honesta o más verificable** una parte, no solo las que añaden funciones.

## Para empezar

```bash
git clone https://github.com/Luigy-Avila-C/centinela-foundry.git
cd centinela-foundry
dotnet test                                                         # sin Azure; debe pasar todo
Demo__Enabled=true dotnet run --project src/Centinela.Api           # el panel con datos de ejemplo, sin Azure
```

Para ejecutar los agentes con modelos reales necesitas tu propia cuenta de Azure: [`docs/DESPLIEGUE.md`](docs/DESPLIEGUE.md). **No hace falta
para contribuir** a la mayor parte del código: casi todo está cubierto por pruebas que no llaman a ningún modelo.

## Reglas del proyecto

1. **Código en inglés; comentarios y documentación en español.** Un comentario explica el *porqué*, no repite lo que dice el código.
2. **Las capas no se saltan:** `Domain` no depende de nada, `Application` no conoce a Foundry (solo interfaces) y `Infrastructure` implementa.
3. **Todo juicio de un modelo se verifica en código** (citas literales, reglas de dominio) y lo que no se puede comprobar se descarta. Si añades un
   agente, no dejes que decida un modelo sin que el código pueda comprobarlo.
4. **Nada se aprueba ni se aplica sin una persona.** No añadas ningún camino que lo evite.
5. **Sin secretos ni claves**, nunca: la autenticación es con Microsoft Entra ID. Las trazas **no deben llevar texto** de normas ni de documentos.
6. **Los resultados se cuentan como son.** Si una medida no cumple un umbral, se documenta; si un experimento no funciona, también.

## Antes de abrir una PR

- `dotnet test` en verde y pruebas nuevas para lo que cambias.
- **Si tocas un prompt, un agente o un conjunto de evaluación**, pasa la puerta de evaluación (necesita tu Azure, ≈ 0,15 USD):
  `dotnet run --project src/Centinela.Cli -- puerta`. Si falla, **no subas el umbral para que pase**: explica con datos por qué empeora o por qué el
  umbral estaba mal calibrado.
- No «ajustes» a la vista de la parte **`test`** de un conjunto etiquetado: está reservada para la medida final (ver `docs/EVALUACION.md`).
- Si cambias algo medido, **actualiza la documentación** con la cifra nueva y lo que **no** demuestra.

## Ideas por dónde empezar

Mira la sección «Contribuir» del [README](README.md#contribuir) y las incidencias etiquetadas como `good first issue`. Si algo de la guía de
despliegue falla en tu cuenta, abre una incidencia con el error: es justo lo que más ayuda.

## Conducta

Sé amable y asume buena fe. Las críticas al diseño y a las medidas son bienvenidas; las personales, no.
