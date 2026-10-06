# Guía de despliegue paso a paso

Cómo montar Centinela en **tu propia cuenta de Azure**, desde cero, y cuánto cuesta probarlo. Calcula **entre 30 y 45 minutos** la
primera vez (casi todo es esperar al despliegue y a la indexación).

> **¿Solo quieres verlo?** No necesitas Azure: el [modo demo](../README.md#pruébalo-en-2-minutos-sin-azure) carga tres casos reales en
> memoria y abre el panel. Esta guía es para ejecutar los agentes de verdad.

## 0. Qué vas a crear y qué cuesta

| Recurso | Para qué | Coste fijo | Coste variable |
|---|---|---|---|
| **Azure AI Foundry** (cuenta + proyecto) | Aloja los modelos | **0** | Se paga **por token** |
| 4 despliegues de modelos (`gpt-4.1-mini`, `gpt-4.1`, `gpt-5.1`, `text-embedding-3-small`) | Cribado, análisis, redacción, juez, RAG | **0** (la «capacidad» es un tope de velocidad, no una reserva) | Por token |
| **Azure AI Search**, nivel *free* | Índice vectorial de la normativa y de los documentos de la empresa | **0** | – |
| **Cosmos DB**, nivel gratuito | Casos, borradores y decisiones | **0** (1000 RU/s y 25 GB) | – |
| API, panel y Worker | Se ejecutan **en tu máquina** | 0 | – |

Con los niveles gratuitos, **un entorno parado no cuesta nada**: solo pagas los tokens de lo que ejecutes. Costes **medidos** en este
proyecto (USD, precios de referencia; ver [«Consumo y coste»](EVALUACION.md#consumo-y-coste)):

| Qué haces | Coste aproximado | Tiempo |
|---|---:|---|
| `dotnet test` (más de 350 pruebas) | **0** | segundos |
| Modo demo | **0** | – |
| `inspeccionar` un texto con el guardián | ≈ 0,001 | 3 s |
| `vigilar --simular` (un día del BOE, solo cribado) | ≈ 0,01 | 1–2 min |
| **`puerta`** (4 evaluaciones con modelos reales) | ≈ **0,15** | ~3 min |
| `caso` reutilizando un análisis guardado | ≈ **0,24** | ~2 min |
| **`caso real`** (análisis + impacto + redacción + auditoría de una norma larga) | ≈ **0,60** | ~5 min |
| El Worker activo, **tope** de 3 casos al día (proyección) | ≈ 1,9 al día | – |

Son **mediciones sueltas, no promedios** (un caso, una norma): una norma más larga o con más rondas de redacción cuesta más. Los
precios por millón de tokens que usa el cálculo (`gpt-4.1` 2,00/8,00; `gpt-4.1-mini` 0,40/1,60; `gpt-5.1` 1,25/10,00;
embeddings 0,02) son de referencia y **configurables** (`Pricing:Models`). **Contrástalos con la [página de precios de Azure OpenAI](https://azure.microsoft.com/pricing/details/cognitive-services/openai-service/)
para tu región.** No incluyen las evaluaciones largas de cobertura (`cobertura`, `rejuzgar`), que no se han medido.

### Si ya usaste tus niveles gratuitos

Azure solo deja **un** Search *free* y **una** cuenta Cosmos gratuita por suscripción. Si ya los tienes ocupados el despliegue falla
(no cobra por sorpresa) y puedes elegir:

```bash
# Search «basic» y Cosmos sin nivel gratuito: ya NO es gratis
az deployment sub create ... --parameters ... searchSku=basic cosmosFreeTier=false
```

Pasarían a tener cuota fija mensual: Search *basic* del orden de **decenas de dólares al mes** y Cosmos con 400 RU/s aprovisionados
del orden de **20-25 USD al mes** (según la lista de precios que conozco; **verifícalo en el [calculador de Azure](https://azure.microsoft.com/pricing/calculator/)**).
Otra opción es usar otra suscripción o borrar lo anterior.

### Protege tu crédito antes de empezar

1. En el portal: **Cost Management → Budgets → Add**, un presupuesto mensual (p. ej. 20 USD) con alertas al 50 % y al 90 %.
2. Deja el Worker **desactivado** (lo está por defecto) hasta que sepas qué hace.
3. Si usas una **prueba gratuita**, el gasto sale del crédito inicial y los servicios se detienen al agotarse; no se cobra nada
   hasta que tú pases la cuenta a pago por uso.

## 1. Requisitos

- **Suscripción de Azure** (vale la prueba gratuita) y permisos de **Propietario**, o de *Colaborador* **más** *Administrador de
  acceso de usuarios* sobre la suscripción: el despliegue crea asignaciones de roles.
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) ≥ 2.60 (incluye Bicep: `az bicep version`).
- [.NET 10 SDK](https://dotnet.microsoft.com/download) y `git`.
- Nada más: **no hay claves de API en ningún sitio**. Todo se autentica con tu identidad de Microsoft Entra ID.

## 2. Descargar y comprobar

```bash
git clone https://github.com/Luigy-Avila-C/centinela-foundry.git
cd centinela-foundry
dotnet test            # no usa Azure: debe pasar todo
```

## 3. Iniciar sesión

```bash
az login
az account set --subscription "<nombre o id de tu suscripción>"
az account show --query "{suscripcion:name, id:id}" -o table      # compruébalo antes de desplegar
```

## 4. Elegir región y comprobar que hay modelos y cuota

El proyecto usa por defecto **`spaincentral`** (los datos se quedan en España). Comprueba que tu región tiene los modelos y cuota; si
no, elige otra (`swedencentral`, `eastus2`…):

```bash
REGION=spaincentral
az cognitiveservices model list --location $REGION \
  --query "[?model.format=='OpenAI' && (model.name=='gpt-4.1' || model.name=='gpt-4.1-mini' || model.name=='gpt-5.1' || model.name=='text-embedding-3-small')].{modelo:model.name, version:model.version}" -o table
az cognitiveservices usage list --location $REGION \
  --query "[?contains(name.value,'GlobalStandard.gpt-4.1') || contains(name.value,'GlobalStandard.gpt-5.1') || contains(name.value,'text-embedding-3-small')].{cuota:name.value, usado:currentValue, limite:limit}" -o table
```

Los despliegues piden 150 mil tokens/minuto de `gpt-4.1` y `gpt-5.1` y 50 mil de los otros dos. Si tu cuota es menor, baja la
`capacity` en [`infra/modules/foundry.bicep`](../infra/modules/foundry.bicep): solo limita la velocidad (con menos verás más errores
429 reintentados), no el coste. Si un modelo no existe en tu región, cámbialo ahí y en la configuración (`Models__Smart`,
`Models__Judge`, `Models__Fast`; el **juez debe ser de otra familia que el analista**, ese es el diseño).

## 5. Desplegar la infraestructura

```bash
az deployment sub create \
  --name centinela-dev --location spaincentral \
  --template-file infra/main.bicep \
  --parameters principalId=$(az ad signed-in-user show --query id -o tsv) location=spaincentral
```

En PowerShell:

```powershell
az deployment sub create --name centinela-dev --location spaincentral --template-file infra/main.bicep `
  --parameters principalId=$(az ad signed-in-user show --query id -o tsv) location=spaincentral
```

Tarda **5-10 minutos**. Crea el grupo `rg-centinela-dev` y dentro Foundry con su proyecto y los 4 modelos, Search, Cosmos (base y contenedor) y los roles
para tu usuario: 15 elementos en total. Antes de ejecutarlo de verdad puedes ver qué haría con `az deployment sub what-if` (mismos argumentos).

Anota las tres direcciones que devuelve:

```bash
az deployment sub show --name centinela-dev --query properties.outputs -o json
```

## 6. Configurar el entorno

Bash:

```bash
export Foundry__ProjectEndpoint="https://<foundry-...>.services.ai.azure.com/api/projects/centinela"
export Search__Endpoint="https://<srch-...>.search.windows.net"
export Cosmos__Endpoint="https://<cosmos-...>.documents.azure.com:443/"
```

PowerShell:

```powershell
$env:Foundry__ProjectEndpoint = "https://<foundry-...>.services.ai.azure.com/api/projects/centinela"
$env:Search__Endpoint = "https://<srch-...>.search.windows.net"
$env:Cosmos__Endpoint = "https://<cosmos-...>.documents.azure.com:443/"
```

Los roles tardan **1-5 minutos** en propagarse: si la primera llamada da `403`, espera y reintenta.

## 7. Comprobar que los modelos responden

```bash
echo "Los obligados tributarios deberán emitir facturas electrónicas." > /tmp/prueba.txt
dotnet run --project src/Centinela.Cli -- inspeccionar /tmp/prueba.txt        # «Sin indicios de inyección.» (≈ 0,001 USD)
dotnet run --project src/Centinela.Cli -- cribar 2026-10-05 --max 5           # criba 5 publicaciones reales del BOE
```

## 8. Indexar la normativa y los documentos de la empresa

```bash
# Normas previas que el analista usa como contexto (RAG): el Reglamento de facturación y el Real Decreto 238/2026
dotnet run --project src/Centinela.Cli -- indexar BOE-A-2012-14696 BOE-A-2026-7295
# Los documentos internos de la empresa FICTICIA de ejemplo (o los tuyos, en Markdown: ver datos/README.md)
dotnet run --project src/Centinela.Cli -- empresa-indexar datos/empresa-ejemplo
```

## 9. Tu primer caso completo

```bash
dotnet run --project src/Centinela.Cli -- caso real --informe mi-caso.md
```

Descarga la Orden HAC/1028/2026 y recorre **todo** el flujo: guardián → cribado → análisis → impacto → redactor ⇄ auditor. Al final
imprime la **tabla de tokens por etapa** y el coste estimado, guarda el caso en Cosmos y escribe un informe en Markdown. Casi
siempre quedará **escalado** o con datos `[COMPLETAR]` por rellenar: es el comportamiento esperado, no un fallo (ver
[los límites](EVALUACION.md#flujo-completo-con-agentes-reales)).

## 10. Revisarlo en el panel

```bash
export Api__Key="<inventa una clave larga>"
dotnet run --project src/Centinela.Api          # http://localhost:5182
```

Escribe la clave, abre el caso y compara original y propuesto. Para aprobar un caso escalado o con datos pendientes tendrás que marcar
que asumes los riesgos. **Aprobar no modifica ningún documento**: solo deja constancia.

> La API se protege con **una clave compartida** y no tiene usuarios. Sirve en local; **no la expongas a Internet**.

## 11. (Opcional) Dejar que vigile el BOE

```bash
dotnet run --project src/Centinela.Cli -- vigilar --simular      # solo cribado: ≈ 0,01 USD, no guarda ni descarga nada
dotnet run --project src/Centinela.Cli -- vigilar --max-casos 1  # una pasada real, con tope

export Watcher__Enabled=true                                     # sin esto el Worker no hace nada
dotnet run --project src/Centinela.Worker
```

Topes por defecto: 2 casos por pasada y 3 por día (`Watcher__MaxNewCasesPerRun`, `Watcher__MaxNewCasesPerDay`). Limitan **cuántos**
casos se abren, no cuántos dólares cuestan.

## 12. (Opcional) Medir que no empeora

```bash
dotnet run --project src/Centinela.Cli -- puerta --salida puerta.json     # ≈ 0,15 USD; código de salida 1 si algo empeora
```

Y para ver las trazas (desactivadas por defecto, nunca llevan texto de normas ni de documentos):

```bash
export Telemetry__Enabled=true Telemetry__Console=true      # o Telemetry__OtlpEndpoint=http://localhost:4317
```

## 13. Borrarlo todo

```bash
az group delete --name rg-centinela-dev --yes --no-wait
# Foundry se borra «de forma suave» durante unos días y bloquea el nombre; si vas a volver a desplegar, purga la cuenta:
az cognitiveservices account list-deleted -o table
az cognitiveservices account purge --location spaincentral --resource-group rg-centinela-dev --name <foundry-...>
```

Al borrar el grupo se liberan también tus niveles gratuitos de Search y Cosmos.

## Solución de problemas

| Síntoma | Causa y arreglo |
|---|---|
| `ServiceQuotaExceeded … 'free' tier service quota` (Search) | Ya tienes un Search gratuito en la suscripción. Bórralo, usa otra suscripción o despliega con `searchSku=basic` (tiene cuota fija). |
| Error de Cosmos con `enableFreeTier` | Igual con la cuenta gratuita: borra la otra o despliega con `cosmosFreeTier=false` (pasa a pago). |
| `InsufficientQuota` / el modelo no existe en la región | Mira el paso 4: baja la `capacity` en `foundry.bicep`, cambia de región o de modelo. |
| `403 / PermissionDenied` al primer uso | Los roles tardan unos minutos. Espera, y si sigue, `az login` de nuevo. |
| `RoleAssignmentUpdatePermission` al desplegar | Te falta *Administrador de acceso de usuarios* (o ser Propietario) en la suscripción. |
| Muchos `429` o el análisis va lento | Tu cuota de tokens/minuto es baja. Hay reintentos con espera automáticos; sube la cuota o ten paciencia. |
| `HTTP 400 content_filter` en el guardián | Es una **señal**, no un fallo: el filtro de Azure rechazó un texto (p. ej. un intento de inyección). El guardián lo trata como hallazgo. |
| La API no arranca: «Falta Api:Key» | Define `Api__Key` (o usa el modo demo). Es a propósito: no arranca sin protección. |
| `No se encuentra datos/demo` | Ejecuta desde la raíz del repositorio o define `Demo__Folder`. |
| El Worker no hace nada | Está desactivado por defecto: `Watcher__Enabled=true`. |
| El caso queda «escalado» casi siempre | Esperado hoy: solo 2-3 de cada 5 ejecuciones superan al auditor. Mira [`EVALUACION.md`](EVALUACION.md#flujo-completo-con-agentes-reales). |

## Qué está comprobado y qué no

- **Comprobado:** los comandos se ejecutaron contra una suscripción real (región `spaincentral`), y el Bicep se validó con `what-if` en un
  entorno nuevo (15 elementos a crear, región `swedencentral`).
- **No comprobado:** un despliegue **completo desde cero en una suscripción limpia**. El `what-if` se hizo con
  `searchSku=basic cosmosFreeTier=false` porque en la suscripción de pruebas los niveles gratuitos ya estaban ocupados, y no creó nada.
  Si algo de esta guía falla en tu cuenta, **abre una incidencia**: es justo la información que más ayuda.
