# Hoja de ruta

Estado de cada capacidad y lo que falta. Las cifras y sus límites están en [`EVALUACION.md`](EVALUACION.md); el diseño, en
[`ARQUITECTURA.md`](ARQUITECTURA.md).

| Capacidad | Estado |
|---|---|
| Dominio, máquina de estados y orquestador determinista | Hecho |
| Cribado de publicaciones del BOE | Hecho |
| Ingesta del texto del BOE, troceado por artículo y búsqueda híbrida (RAG) | Hecho |
| Analista normativo con citas en tres niveles y verificador independiente | Hecho, con límites |
| Evaluador de impacto sobre los documentos de la empresa | Hecho, con límites |
| Redactor y auditor (bucle acotado, escala a una persona) | Hecho, con límites |
| Guardián de seguridad (reglas + modelo + filtro de la plataforma) | Hecho, con límites |
| Persistencia en Cosmos DB, API de aprobación y panel | Hecho, con límites |
| Vigilante autónomo (Worker) con topes de gasto | Hecho, con límites |
| Evaluación continua: puerta de regresión, trazas, CI, consumo y coste por caso | Hecho, con límites |
| Modo demo sin Azure y guía de despliegue | Hecho |

## Pendiente

**Calidad de los agentes**
- Reducir el ruido del auditor (rechaza ≈ 40 % de los borradores buenos) y subir la proporción de ejecuciones que lo superan (hoy 2 o 3 de cada 5).
- Resolver la deriva por obligaciones compuestas: el alcance por pasaje está implementado pero no basta.
- Una puerta tras el análisis que compruebe que alguna obligación va dirigida a empresas privadas (una norma irrelevante que pasa el cribado puede
  producir impactos inventados).
- Medir la **sensibilidad del cribado por título**, hoy solo con una lectura manual.
- Analizar los anexos, medir los duplicados entre afirmaciones y ajustar la gravedad del impacto (casi todo sale «alta»).
- Ingesta automática de las normas relacionadas que cita el XML del BOE, y un «diff» real contra la versión consolidada.

**Evaluación**
- Una segunda norma, un segundo revisor de las etiquetas y de la referencia, y casos nuevos para una parte reservada limpia.
- Ataques para el guardián escritos por otra persona, y que los documentos de la empresa también pasen por él.
- Evaluar el analista y el flujo completo en la puerta de regresión; más casos medidos para fijar el coste con una media y no con una sola medición.
- Prompt Shields como servicio aparte, si compensa su coste.

**Plataforma**
- Autenticación con Microsoft Entra ID y roles de revisor en la API (hoy, clave compartida).
- Pruebas automáticas del repositorio de Cosmos (emulador en el CI) y pruebas de interfaz del panel.
- Ejecutar los flujos de GitHub Actions y montar la identidad OIDC para las evaluaciones; ver las trazas en un recolector real; despliegue continuo.
- Una prueba de larga duración del Worker (festivos, caídas del BOE, cuotas agotadas).
