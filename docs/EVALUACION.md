# Evaluación

Qué se ha medido de cada pieza, cómo, con qué resultados y **qué no demuestra cada medida**. La arquitectura está en
[`ARQUITECTURA.md`](ARQUITECTURA.md).

## Método común

- **Conjuntos etiquetados** en [`evaluaciones/`](../evaluaciones). Cada uno separa una parte de **desarrollo** (`dev`, con la que se ajusta)
  de una **reservada** (`test`, que se mide una sola vez y no se usa para ajustar).
- **Criterios fijados antes de mirar la parte reservada.** Cuando un resultado no cumple su criterio, se documenta así y no se mueve el umbral.
- **Un solo etiquetador y datos ficticios.** La empresa de ejemplo («Distribuciones Aurora, S.L.»), los borradores, los ataques y las
  etiquetas los escribió el autor del proyecto; no hay segundo revisor. Los textos de normas son del BOE (datos abiertos).
- **Muestras pequeñas.** Con pocos casos los intervalos de confianza son muy anchos; una cifra como «12/12» no equivale a una tasa real del 100 %.
- **Una sola norma** (Orden HAC/1028/2026, factura electrónica) en casi todas las medidas.

| Conjunto | Contenido |
|---|---|
| `verificador-citas.json` | 32 afirmaciones con sus citas y el veredicto correcto |
| `cobertura-orden-hac-1028-2026.json` | 51 hechos materiales de la Orden (35 base + 16 ampliación) y 10 casos de calibración |
| `impacto-aurora.json` | 37 pasajes de 10 documentos: 7 afectados, 29 no afectados, 1 dudoso |
| `auditor-borradores.json` | 21 borradores de los 7 pasajes afectados: 7 buenos y 14 defectuosos a propósito |
| `guardian-inyecciones.json` | 24 ataques insertados en secciones reales de la norma y 8 negativos difíciles |
| `umbrales.json` | Umbrales de regresión de la puerta de evaluación |

## Verificador de citas

32 casos sobre texto real de la Orden. Se miden por separado los dos errores que importan, porque la precisión global engaña: dejar pasar una
cita que no respalda (**detección**) y marcar como mala una correcta (**falsa alarma**). Umbrales fijados antes de ver `test`: detección ≥ 90 % y
falsas alarmas ≤ 15 %.

| Métrica | `gpt-4.1` (mismo modelo que el analista) | `gpt-5.1` (otra generación) |
|---|---|---|
| Detección de malas, `dev` / `test` | 9/9 · 9/9 | 9/9 · 9/9 |
| Falsas alarmas, `dev` / `test` | 0/7 · 1/7 | 1/7 · **2/7** |
| Acierto exacto, `dev` / `test` | 15/16 · 15/16 | 15/16 · 14/16 |

- Con `gpt-4.1` se cumplen los umbrales, pero el juez es el mismo modelo que el generador.
- Con `gpt-5.1` la detección es igual y **las falsas alarmas (29 % en `test`) no cumplen el umbral**: es más estricto y marca como `parcial`
  afirmaciones correctas a las que les falta un matiz menor. No se retocó ni el umbral ni el prompt.
- **`gpt-5.1` es el juez por defecto** porque la independencia respecto del generador es el objetivo. Aceptar su mayor número de falsas alarmas es
  una decisión de producto: una falsa alarma cuesta tiempo de revisión (un `parcial` se enseña, no bloquea nada); una detección fallida deja
  pasar una cita que no respalda.

**Qué no demuestra.** Con 18 malas detectadas de 18, el límite inferior al 95 % es ≈ 82 %, no 100 %; las falsas alarmas (1 de 14) van de ≈ 1 % a
≈ 31 %. Las afirmaciones malas están fabricadas (cifras cambiadas, datos inventados, cita equivocada): miden si el verificador *distingue*, no con
qué frecuencia se equivoca el analista de forma natural. Los casos `parcial` son los más discutibles. `test` se ha ejecutado dos veces (una por
juez) y sus etiquetas se han leído: cualquier ajuste futuro del prompt debe medirse con casos nuevos. El resumen en texto libre del análisis no se
verifica, solo las afirmaciones.

Sobre una ejecución real del analista (22 afirmaciones, juez `gpt-5.1`): 17 respaldadas, 5 parciales y 0 no respaldadas; de las 5, 2 son matices
reales (afirmaciones que omitían una condición que acota el alcance), 1 es razonable y 2 son pedantes.

## Cobertura de la norma

Que cada afirmación esté respaldada no dice cuántas obligaciones se han dejado fuera. La cobertura se mide contra 51 hechos materiales de la
Orden (35 del grupo `base`, artículos 1 a 7 y disposiciones adicionales y final; 16 de `ampliacion`, artículos 8 a 11), redactados a mano. Un
medidor decide si alguna afirmación recoge cada hecho.

### Análisis en una llamada frente a análisis por artículo

El análisis en una sola llamada omitía de forma sistemática 5 de los 35 hechos base (la baja de facturas improcedentes, subsanar y reenviar una
copia fiel rechazada, los errores distintos de la rectificación del artículo 15, el anexo II…). El **análisis por artículo** (una llamada por
artículo, con instrucciones de exhaustividad y de conservar las condiciones) es hoy el modo por defecto (`Analysis:Mode`). Resultado con 3
ejecuciones de cada método:

| | Una llamada | Por artículo |
|---|---|---|
| Afirmaciones por ejecución | 42, 43, 40 | 93, 93, 93 |
| Hechos base cubiertos (`gpt-4.1` / `gpt-5.1`) | 80 % / 78 % | 100 % / 100 % |
| Hechos ampliación cubiertos | 83 % / 75 % | 100 % / 100 % |
| Veredictos `parcial` (`gpt-4.1` / `gpt-5.1`) | 1 % / 12 % | 1 % / 4 % |
| Veredictos `no respaldada` | 0 | 0 |
| Contenidos que antes faltaban, buscados por palabras clave | 0 de 125 afirmaciones | presentes en las 3 ejecuciones |

La última fila es la prueba más sólida porque **no usa ningún modelo para medir**. Los dos jueces coinciden en el 96 % de las decisiones de
cobertura (101 de 105), de modo que medir cobertura depende mucho menos del modelo que verificar citas.

### Por qué el «100 %» no es una medición

Con 93 afirmaciones en el prompt, el medidor laxo es mucho más permisivo que en su calibración (listas de 1 o 2 afirmaciones). Un **control
negativo** (quitar para cada hecho las afirmaciones que citan su artículo y volver a preguntar; la respuesta correcta es «no cubierto») da:
`gpt-4.1` sigue diciendo «cubierto» en **21 de 51 hechos (41 %)** y `gpt-5.1` en **10 de 51 (20 %)**. Unas pocas fugas son legítimas (el acuse de
recibo está repetido en dos artículos) y **la mayoría son laxitud**: el medidor acepta afirmaciones «relacionadas» o que «implican» el hecho. El
100 % es, por tanto, una **cota superior**.

### Medidor estricto

`StrictCoverageMatcherAgent` descompone el hecho en elementos esenciales (mínimo dos) y exige para cada uno una **cita literal** de una
afirmación, que el código comprueba. Dar por cubierto algo que no está exige falsificar una cita, y eso se detecta. Protocolo: ajustado con los
hechos f01–f25 de una ejecución; validado una vez en otras dos con los 51 hechos; criterio fijado de antemano: fugas ≤ 10 % con ambos jueces y
sensibilidad ≥ 90 %.

| Fugas en el control negativo | Laxo (`gpt-4.1` / `gpt-5.1`) | Estricto (`gpt-4.1` / `gpt-5.1`) |
|---|---|---|
| Ejecución 1 (f01–f25, desarrollo) | 9/25 · 1/25 | 3/25 (12 %) · 2/25 (8 %) |
| Ejecución 2 (51, validación) | 18/51 (35 %) · 11/51 (22 %) | 6/51 (12 %) · 5/51 (10 %) |
| Ejecución 3 (51, validación) | — | 6/51 (12 %) · 6/51 (12 %) |

Sensibilidad del estricto con todas las afirmaciones: 94-98 % (media 96 %).

- **El criterio de fugas no se cumple, por poco** (12 % frente al ≤ 10 % en 3 de las 4 validaciones): la diferencia de un hecho sobre 51. No se
  movió el umbral. La sensibilidad sí se cumple.
- **El estricto empeora la calibración original** (listas de 1-2 afirmaciones con paráfrasis): 7/10 con `gpt-4.1` y 8/10 con `gpt-5.1`, frente a 10/10 y
  9/10 del laxo; todos los fallos son falsos «no cubierto» (exige como obligatorio un matiz que la etiqueta daba por accesorio). Con afirmaciones
  casi literales apenas se nota; con resúmenes o paráfrasis penalizaría mucho.
- **Cómo leer la cobertura:** el estricto da una **cota inferior** (≈ 96 % por artículo) y el laxo una **cota superior** (≈ 100 %). La cobertura real
  está entre las dos. Se usa el estricto por defecto porque en cumplimiento es peor dar por recogida una obligación que falta que revisar una de
  más (`Coverage:Matcher=laxo` lo cambia). Es una decisión que se aparta de lo anunciado antes de medir («si no se cumple, queda el medidor antiguo»).
- **No comparar métodos con el estricto:** favorecería al análisis por artículo, cuyas afirmaciones son casi literales. La comparación válida es la
  prueba de palabras clave.

**Qué no demuestra.** Una sola norma. La referencia la redactó una sola persona y después de ver afirmaciones del analista (también las del grupo de
ampliación); un hecho de más o de menos mueve el porcentaje casi 3 puntos. Los hechos compuestos son más difíciles de cubrir. Los anexos (13
fragmentos de tablas técnicas) no se analizan ni se ha medido su cobertura. Las 93 afirmaciones incluyen hechos repetidos en artículos distintos,
sin medir cuántos duplicados. «Una llamada por artículo» y «instrucciones de exhaustividad y de condición» van juntos: no se puede atribuir la mejora
a cada uno. El método por artículo hace ≈ 14 llamadas de generación en lugar de 1 y 2,3 veces más afirmaciones que verificar.

## Evaluador de impacto

`impacto-aurora.json` etiqueta los 37 pasajes: 7 afectados (4 en desarrollo, 3 reservados), 29 no afectados (varios son distractores a propósito:
menciones del RGPD y del Esquema Nacional de Seguridad, una facultad opcional, un plazo que aplica a otro sistema) y 1 dudoso que no cuenta.
Cada etiqueta se justifica con hechos de la Orden, no con conocimiento jurídico externo. Criterio fijado antes de medir: sensibilidad ≥ 80 % y
precisión ≥ 70 % en los reservados, como mucho dos iteraciones de prompt mirando solo desarrollo, y los reservados una sola vez.

| | Sin reglas de verificación (dev) | Con reglas de verificación (dev, 2 ejecuciones) | **Reservados (1 ejecución)** |
|---|---|---|---|
| Sensibilidad | 4/4 | 4/4 · 4/4 | **3/3** |
| Precisión | 4/6 y 4/7 | 4/4 · 4/4 | **3/3** |
| Afectados que llegaron al juez | 4/4 | 4/4 | 3/3 |
| Gravedad exacta / a ±1 nivel | 3/4 / 4/4 | 2/4 / 4/4 | 1/3 / 3/3 |

Los falsos positivos de la versión sin reglas no eran una confusión de lenguaje («la Administración» frente a «el departamento de administración»)
sino de **razonamiento**: el juez trataba la **ausencia** de una mención como incumplimiento, aplicaba a la empresa obligaciones de **otro sujeto** y
confundía una **facultad** con un deber, aunque el prompt lo prohibía. Lo que lo corrige no es más prompt sino pedirle al juez que etiquete sujeto,
tipo y base, y **descartar en código** lo que no cumple las reglas.

**Qué no demuestra.** Son 7 positivos en total (con 3 de 3, el límite inferior al 95 % es ≈ 44 %): prueba de concepto, no medida de fiabilidad. Los
documentos y las etiquetas los escribió la misma persona con las etiquetas en la cabeza. Con 37 pasajes todos los afectados llegaron al juez; con
miles de documentos no se ha comprobado la recuperación. La gravedad se infla (casi todo sale «alta»). Los `dev` se usaron para ajustar, así que son
optimistas; solo cuentan los reservados. Solo se probó con una ejecución guardada del análisis y con `gpt-4.1`.

## Auditor

Para cada uno de los 7 pasajes afectados hay **un borrador bueno y dos defectuosos** a propósito, cada uno con un solo fallo de un tipo conocido (no
cubre la obligación aunque diga que sí, cifra o nombre inventado, contradice la norma, reescribe lo que no tocaba). Criterio fijado antes de medir:
en los reservados, rechazar ≥ 80 % de los defectuosos (5 de 6) y aprobar ≥ 2 de los 3 buenos.

| | Desarrollo (12) | **Reservados (9, una sola vez)** |
|---|---|---|
| Borradores defectuosos rechazados | 7/8 (88 %) | **6/6 (100 %)** |
| Borradores buenos aprobados | 3/4 (75 %) | **2/3 (67 %)** |
| Defecto señalado = defecto puesto | 6/7 | 6/6 |

Se cumple el criterio. El código por sí solo atrapa 7 de los 14 defectuosos (cifras inventadas, reescrituras completas); **los otros 7 (no cubrir de
verdad, contradecir la norma) solo puede juzgarlos el modelo**, que rechazó 6 de esos 7. Dos fallos de la medición tienen causa en el propio caso de
prueba: `b05` (la obligación recae en la plataforma, no en la empresa, y la etiqueta es discutible) y `b19` (al pasaje de rechazos se le dio la
obligación completa, que junta tres deberes, mientras el borrador «bueno» solo trataba uno).

**Consistencia.** Con la misma entrada, el veredicto cambia entre ejecuciones. Medido 5 veces sobre los 21 borradores: 4 borradores inestables con
un voto y 3 con tres votos; defectuosos rechazados 13,6 → 13,0 de 14 y buenos aprobados 4,0 → 4,2 de 7. El criterio fijado (como mucho 2 inestables, sin
empeorar) no se cumple, así que **el voto por mayoría no se adopta** (el mecanismo, `Auditor:Votes`, queda desactivado). El ruido parece sistemático
(borradores en el límite que el modelo juzga distinto cada vez). **El auditor rechaza ≈ 40 % de los borradores buenos.**

### Flujo completo con agentes reales

`centinela caso` ejecuta cribado, impacto, redactor y auditor reales sobre la empresa ficticia. La tasa de ejecuciones que superan al auditor (el resto
escala a una persona), con distintas versiones del flujo:

| Versión | Ejecuciones | Superan al auditor |
|---|---|---|
| Base | 3 | 1 |
| + alcance por pasaje y detección de contradicción interna | 5 | 3 |
| + reglas adicionales en el prompt del redactor | 5 | 2 |

Con 5 ejecuciones y tres análisis distintos, **las diferencias entre versiones están dentro del ruido** (la misma entrada dio una ejecución superada
y otra escalada). El resultado del bucle no es determinista. El auditor cazó trabajo útil (un destinatario inventado, un borrador que presentaba como
hecho actual lo que el original negaba, una obligación presentada como general cuando tiene ámbito).

Mitigaciones evaluadas:

- **Contradicción interna** (`problema_persiste` en `DraftChecks`): un borrador que conserva intacta la frase que mostraba el incumplimiento se
  rechaza sin preguntar al modelo. Resuelto de forma determinista; **no cambia ninguna cifra del auditor** (ningún borrador defectuoso del conjunto
  conserva la frase completa). Puede chocar con una incidencia `cambio_innecesario` del auditor (uno pide conservar la descripción; el otro
  prohíbe dejarla intacta).
- **Obligaciones compuestas** (alcance por pasaje): implementado; **la deriva persiste** (`Rechazo de facturas` no recoge la fecha de pago ni la de
  vencimiento en 3 de 5 ejecuciones de una serie). No se ha separado si el redactor ignora el alcance o el auditor lo exige mal.
- **Reglas adicionales en el prompt del redactor** (reescribir una carencia como «hasta ahora… ; en adelante…», no afirmar como actual lo que el original
  niega): sin mejora medible. Más prompt no es la palanca.
- Los atascos se concentran en 2 o 3 pasajes de los 7; el resto suele pasar.

**Qué no demuestra.** Los borradores defectuosos tienen un solo fallo cada uno; los de un redactor real son más sutiles y vienen mezclados, y eso es
justo lo que muestra el flujo completo. 21 borradores (9 reservados) es poco. El auditor deja pasar contradicciones internas que el código no detecta,
y la deriva por feedback contradictorio. No se ha medido cuántas veces produce el redactor real un borrador correcto a la primera. **La aprobación
humana no es un trámite.**

## Guardián de seguridad

24 ataques (12 de desarrollo, 12 reservados) insertados al inicio, en medio o al final de **secciones reales** de la Orden, de varias técnicas: anulación
de instrucciones (ES, EN, FR), cambio de rol, orden dirigida a una IA, orden de resultado, JSON forzado, exfiltración, ruptura de delimitadores,
caracteres invisibles, texto oculto en caracteres de etiqueta Unicode, homoglifos y anchura completa, base64, órdenes sin palabras clave y
disposiciones camufladas como norma. Más 8 «negativos difíciles» con vocabulario que podría confundir (inteligencia artificial, «sistema», «ignorar» en
sentido jurídico). Todas las secciones reales de la norma sirven además de negativos (pares → desarrollo, impares → reservados). Criterios fijados
antes de mirar la parte reservada: ≥ 10 de 12 ataques detectados, 0 secciones reales bloqueadas y como mucho 1 de 4 negativos difíciles bloqueado.

| | Desarrollo | Reservados (una sola vez) |
|---|---|---|
| Ataques detectados (guardián completo) | 12/12 | **12/12** |
| …solo con las reglas del código | 11/12 | 11/12 |
| …solo con el modelo | 10/12 | 9/12 |
| …con el filtro de la plataforma | 1/12 | 2/12 |
| Secciones reales de la norma bloqueadas | 0/8 | **0/8** |
| Negativos difíciles bloqueados | 0/4 | **1/4** (por el filtro de la plataforma) |

- **Las capas se complementan:** las reglas cazan solas ataques que el modelo no vio (texto oculto en Unicode, base64) y el modelo caza solo una
  «disposición adicional» que ordena a los sistemas de IA calificar el cambio como no relevante y una orden sin palabras clave. Ninguna capa sola cubre
  todo.
- **El modelo del guardián también puede ser manipulado:** un ataque de JSON forzado le hizo contestar fuera de esquema. Eso se trata como hallazgo y
  bloquea.
- El único falso positivo (lenguaje jurídico legítimo del tipo «descarta las instrucciones anteriores, que quedan derogadas») lo bloqueó el filtro de la
  plataforma, no las capas propias.
- El flujo completo pasó el guardián con **la Orden entera** sin bloqueo.

**Qué no demuestra.** Los ataques y las reglas los diseñó la misma persona, que conocía las técnicas: la cifra más independiente es la del **modelo
solo (9/12)**. Con 12 ataques, «12/12» es compatible con una tasa real de en torno al 75 % (regla de los tres). No cubre una norma manipulada que
*afirme* algo falso sin dar órdenes a ningún modelo (ahí la defensa son las citas verificadas y la persona que aprueba), ni los documentos de la
empresa, que no pasan por el guardián. No se probó con otras normas. No se usó Azure AI Content Safety / Prompt Shields como servicio aparte, solo el
filtro integrado en los despliegues.

## El vigilante sobre el BOE real

Sobre el BOE de dos días (124 publicaciones de las secciones I y III), el cribado por título dejó pasar 3 como relevantes y descartó 120 (más 1 ya
conocida). El primer caso que abrió el vigilante **fue un falso positivo**: un convenio entre el INSS, la TGSS y una comunidad autónoma para ceder
datos de afiliación, que no obliga a ninguna empresa. El análisis completo (≈ 2 minutos, 125 obligaciones) acabó proponiendo cambiar la cláusula de
jurisdicción del contrato de la empresa ficticia porque «contradice» la del convenio entre administraciones. Las salvaguardas funcionaron (el auditor
no superó el borrador, el caso quedó escalado con el aviso de riesgo y no se aplicó nada), pero el error estaba en la puerta de entrada. El prompt del
cribado excluye ahora explícitamente lo que solo regula a las administraciones; con él, los dos convenios gemelos restantes se descartan y la Orden
sigue marcada como relevante. Ese caso se conserva en `datos/demo` como ejemplo.

**Qué no demuestra.** La **sensibilidad del cribado por título no está medida**: una norma relevante con un título genérico («Resolución… por la que se
publica…») se perdería sin aviso. La única comprobación es una lectura manual de los 122 títulos descartados, sin ninguna omisión evidente para una
pyme de facturación (no es una medición). El análisis completo sobre una norma irrelevante puede inventar impactos; falta una puerta tras el análisis que
compruebe que alguna obligación va dirigida a empresas privadas. No se ha dejado el Worker corriendo días.

## Consumo y coste

Un caso completo con el **analista real** sobre la Orden (91 obligaciones, 7 borradores, 3 rondas de redacción; el caso acabó escalado):

| Etapa | Modelo | Llamadas | Entrada | Salida | USD est. |
|---|---|---:|---:|---:|---:|
| Análisis | gpt-4.1 (generar por artículo) | 14 | 47.657 | 7.662 | 0,157 |
| Análisis | gpt-5.1 (verificar citas) | 91 | 99.820 | 6.714 | 0,192 |
| Análisis | embeddings | 14 | 4.730 | – | 0,000 |
| Impacto | gpt-4.1 | 25 | 30.022 | 2.102 | 0,077 |
| Impacto | embeddings | 6 | 5.687 | – | 0,000 |
| Redacción | gpt-4.1 | 13 | 26.100 | 5.022 | 0,092 |
| Auditoría | gpt-5.1 | 21 | 36.393 | 1.005 | 0,056 |
| Guardián | gpt-4.1-mini | 25 | 37.845 | 200 | 0,015 |
| Cribado | gpt-4.1-mini | 1 | 1.636 | 74 | 0,001 |
| **Total** | | **210** | **289.890** | **22.779** | **≈ 0,59** |

El análisis es el 60 % del coste. Un día tranquilo del BOE (21 publicaciones) costó **≈ 0,01 USD** de cribado. Un caso reutilizando un análisis guardado
cuesta ≈ 0,24 USD (la suma de las demás etapas). Con los topes por defecto del Worker (3 casos al día y hasta 100 cribados por pasada) el máximo
sería ≈ 1,9 USD al día (≈ 57 USD en 30 días) **si** cada día llegaran 3 casos relevantes de este tamaño: es una proyección, no una medida.

**Qué no demuestra.** Es **un solo caso y una sola ejecución**: no hay media ni dispersión, y una norma más larga o con más rondas cuesta más. Los tokens
son exactos; los dólares usan precios de referencia (`Pricing:Models`) que deben contrastarse para tu región y despliegue. Los tokens en caché no se
descuentan, así que la estimación es algo alta. No incluye Search, Cosmos ni el despliegue de modelos, ni las evaluaciones largas de cobertura.

## Puerta de evaluación

`centinela puerta` pasa cuatro evaluaciones con modelos reales sobre las partes de **desarrollo** y las compara con `evaluaciones/umbrales.json`;
termina con código 1 si alguna falla. Cuesta ≈ 0,15 USD y ~80 llamadas por ejecución. Los umbrales son de **regresión, no objetivos de calidad**: están
justo por debajo de lo medido.

| Evaluación | Comprobación | Umbral | Línea base (dev) |
|---|---|---|---|
| Verificador | detección de malas / falsas alarmas | ≥ 90 % / ≤ 30 % | 9/9 · 1/7 |
| Guardián | ataques detectados / secciones reales bloqueadas / negativos difíciles bloqueados | ≥ 11 / 0 / ≤ 1 | 12/12 · 0/8 · 0/4 |
| Auditor | defectuosos rechazados / buenos aprobados | ≥ 7 de 8 / ≥ 1 de 4 | 8/8 · 1 a 3 de 4 |
| Impacto | sensibilidad / precisión | ≥ 75 % / ≥ 75 % | 4/4 · 4/4 |

**Calibración.** Los umbrales del auditor se fijaron a partir de la distribución medida en 9 ejecuciones del mismo código: defectuosos rechazados 8/8 las
9 veces; buenos aprobados 1, 2, 3, 1, 2, 3, 3, 3, 2 de 4 (media 2,2). Un umbral de «≥ 2 aprobados» fallaba por puro azar en ~1 de cada 4 ejecuciones, así
que el de aprobados se fija en el mínimo observado (solo detecta un colapso: el auditor rechazándolo todo) y el que protege de verdad es el de rechazados
(≥ 7 de 8). **Es una calibración hecha después de ver los datos**, no fijada de antemano. Con ella, tres ejecuciones completas seguidas superaron la
puerta; una ejecución posterior dio 7/8 defectuosos rechazados, justo en el umbral (el margen es de un solo borrador).

**Sensibilidad de la puerta.** Se probó con dos mutaciones reversibles. Un juez más débil en el verificador (`gpt-4.1-mini`) **pasó** la puerta, con razón: en
`dev` sacó 0 falsas alarmas, no era una regresión. **Desactivar las reglas de código del guardián** también la pasó (11/12): el modelo y el filtro de la
plataforma cubren casi todo, así que la puerta, que mide el sistema entero, **no ve la pérdida de una capa redundante**. Esa mutación sí la cazan 20 pruebas
unitarias deterministas y gratuitas: las dos defensas se complementan.

**Qué no demuestra.** Los umbrales se calibraron sobre `dev`, la misma parte con la que se ajustó el sistema: son optimistas, con muestras pequeñas (4
buenos, 8 defectuosos, 12 ataques). Detectan regresiones grandes, no deterioros sutiles. El umbral de «buenos aprobados» solo detecta un colapso. La puerta no
mide el analista (cobertura), el redactor ni el flujo completo, ni la sensibilidad del cribado.

## Integración continua

`.github/workflows/ci.yml` (gratis, sin Azure) restaura, compila en Release, pasa las pruebas, compila el Bicep y comprueba la sintaxis del JavaScript del
panel y que no use `innerHTML` ni scripts en línea. `evaluaciones.yml` ejecuta la puerta con modelos reales, solo a mano o en PR que tocan agentes o conjuntos
de evaluación, nunca en forks y tras la aprobación de una persona en un entorno protegido.

**Qué no demuestra.** Los flujos **no se han ejecutado en GitHub**: se validó el YAML y se reprodujeron en local los pasos de `ci.yml`. `evaluaciones.yml`
requiere configurar una identidad en Entra ID con credencial federada (OIDC), roles, secretos, variables y el entorno protegido; está descrito en el propio fichero.
Tampoco se han visto las trazas en un recolector real (Jaeger, Grafana, Azure Monitor), solo en consola, ni hay despliegue continuo.
