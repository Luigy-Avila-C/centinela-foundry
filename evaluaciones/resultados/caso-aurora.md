# Informe de caso

**Norma:** Orden HAC/1028/2026, de 2 de octubre, por la que se regula la solución pública de facturación electrónica, de acuerdo con lo dispuesto en la disposición final tercera del Real Decreto 238/2026, de 25 de marzo, por el que se desarrolla el sistema de facturación electrónica obligatoria entre empresarios y profesionales y por el que se modifica el Reglamento por el que se regulan las obligaciones de facturación, aprobado por el Real Decreto 1619/2012, de 30 de noviembre.

> **Datos ficticios.** La empresa («Distribuciones Aurora, S.L.») y sus documentos están inventados para probar Centinela.
> **No se ha aplicado ningún cambio.** Cada borrador espera la aprobación de una persona, y los datos marcados como
> `[COMPLETAR: …]` los tiene que aportar la empresa antes de aprobar.

- Estado final: **AwaitingHumanApproval** tras 3 ronda(s) de redacción
- Obligaciones analizadas: 93 · pasajes afectados: 7 · borradores: 7

## Auditoría, ronda a ronda

**Ronda 1:** 5 incidencia(s) bloqueante(s)
- [02-procedimiento-de-conservacion-y-plataformas_004] no_cubre: No incorpora que, una vez recuperadas, las facturas deben ponerse a disposición de los clientes de forma inmediata, tal y como exige el artículo 9.2 de la Orden HAC/1028/2026. (cita: «Asimismo, se solicitará al proveedor que automatice el acceso a la solución pública de facturación electrónica para recuperar las facturas recibidas por nuestros clientes.»)
- [03-facturas-rectificativas-errores-y-bajas_003] no_cubre: El borrador regula errores comunicados por el cliente en general, pero la obligación se refiere específicamente al supuesto de rechazo de la copia fiel por la solución pública; no se menciona ni el rechazo ni el procedimiento tras dicho rechazo. (cita: «Si un cliente nos comunica que una factura tiene un dato incorrecto (por ejemplo, una dirección), administración emite una nueva factura electrónica original correcta, con un nuevo número, y la remite al cliente por correo electrónico. De forma simultánea, se remite una copia fiel de la nueva factura electrónica original a la solución pública de facturación electrónica conforme a la Orden HAC/1028/2026.»)
- [03-facturas-rectificativas-errores-y-bajas_003] cambio_innecesario: La obligación solo exige emitir una nueva factura electrónica original correcta y remitir simultáneamente una copia fiel; no obliga a prohibir la corrección del PDF ni a impedir conservar el mismo número, por lo que este cambio excede lo requerido. (cita: «No se corrige el PDF ni se conserva el mismo número de factura.»)
- [09-politica-de-compras-y-aprobacion-de-facturas_002] no_cubre: Solo regula la comunicación del rechazo, pero no menciona la obligación de informar de la fecha de pago efectivo completo ni de la fecha de vencimiento del plazo de pago. (cita: «Además, el responsable de área debe informar del rechazo de la factura a la solución pública de facturación electrónica, dejando constancia escrita del rechazo y comunicándolo a la plataforma o servicio de facturación correspondiente.»)
- [04-manual-de-cobros-y-pagos_002] contradice_norma: La norma exige comunicar estos datos a la solución pública de facturación electrónica, no directamente a los proveedores; el borrador atribuye la obligación de comunicación a los proveedores, contradiciendo el canal previsto por la norma. (cita: «Administración comunicará a los proveedores la fecha en la que paga una factura y la fecha en la que vence su plazo de pago, conforme a lo dispuesto en la Orden HAC/1028/2026.»)

**Ronda 2:** 3 incidencia(s) bloqueante(s)
- [03-facturas-rectificativas-errores-y-bajas_003] no_cubre: La obligación 1 exige la emisión de una nueva factura electrónica original correcta cuando el rechazo de la copia fiel se deba a errores en la factura original, con independencia de que el error haya sido comunicado por el cliente; el borrador condiciona la actuación a que el cliente comunique el error y no contempla el supuesto general de rechazo de la copia fiel sin comunicación del cliente. (cita: «Si un cliente nos comunica que una factura tiene un dato incorrecto (por ejemplo, una dirección) y la copia fiel de la factura electrónica original ha sido rechazada por la solución pública de facturación electrónica por errores sintácticos, semánticos o de contenido, administración emite una nueva factura electrónica original correcta y la remite al cliente por correo electrónico.»)
- [03-facturas-rectificativas-errores-y-bajas_003] contradice_norma: La norma prevé que, si la copia fiel es rechazada, se subsane la causa y se efectúe una nueva remisión de la copia fiel manteniendo la correspondencia con la factura original; solo cuando el rechazo se deba a errores en la factura original se exige emitir una nueva factura y remitir simultáneamente su copia fiel. El borrador presenta la emisión de nueva factura y la remisión simultánea como el único procedimiento, sin contemplar el caso de mera subsanación y nueva remisión de la copia fiel. (cita: «De forma simultánea, se remite una copia fiel de la nueva factura electrónica original a la solución pública de facturación electrónica conforme a la Orden HAC/1028/2026.»)
- [09-politica-de-compras-y-aprobacion-de-facturas_002] no_cubre: La redacción no aclara que la obligación de informar aplica a todas las facturas electrónicas recibidas (no solo a las rechazadas), y no concreta cómo se informará de la fecha de pago y de vencimiento cuando no haya rechazo. (cita: «Además, el responsable de área debe informar del rechazo de la factura, de la fecha del pago efectivo completo de la factura y de la fecha de vencimiento del plazo de pago a la solución pública de facturación electrónica, dejando constancia escrita del rechazo y comunicándolo a la plataforma o servicio de facturación correspondiente.»)

**Ronda 3:** 3 incidencia(s) bloqueante(s)
- [01-politica-de-facturacion_002] dato_inventado: Se afirma como hecho actual que se genera y remite una copia fiel en sintaxis UBL a la solución pública, cuando ni el pasaje original ni la obligación indican que la empresa lo esté haciendo ya; debería describirse el funcionamiento real, no el deseado. (cita: «Simultáneamente a su emisión, se genera una copia electrónica fiel de cada factura en la sintaxis UBL y se remite a la solución pública de facturación electrónica.»)
- [04-manual-de-cobros-y-pagos_002] contradice_norma: Presenta la comunicación como obligación general sin matizar que, según el art. 7.4 de la Orden, la obligación de informar se entiende cumplida en ciertos casos sin necesidad de comunicar a la solución pública, salvo comunicación expresa del estado de pago. (cita: «Administración comunicará a la solución pública de facturación electrónica la fecha en la que paga una factura y la fecha en la que vence su plazo de pago, conforme a lo dispuesto en la Orden HAC/1028/2026.»)
- [04-manual-de-cobros-y-pagos_002] dato_inventado: Introduce una referencia concreta a la Orden HAC/1028/2026 que no figuraba en el pasaje original ni es exigida por la obligación, añadiendo un dato normativo específico no presente antes. (cita: «conforme a lo dispuesto en la Orden HAC/1028/2026.»)

## Borradores

### 01-politica-de-facturacion · 2. Emisión de facturas

**Antes**

> 2. Emisión de facturas
> Las facturas se generan en nuestro ERP «Aurora-ERP» el mismo día de la entrega de la mercancía. El ERP las exporta en formato PDF y administración las envía por correo electrónico al cliente. No se genera ningún otro formato de factura ni se remite copia de las facturas a ninguna plataforma, sede o servicio externo.

**Después (propuesto)**

> 2. Emisión de facturas
> Las facturas se generan en nuestro ERP «Aurora-ERP» el mismo día de la entrega de la mercancía. El ERP las exporta en formato PDF y administración las envía por correo electrónico al cliente. Simultáneamente a su emisión, se genera una copia electrónica fiel de cada factura en la sintaxis UBL y se remite a la solución pública de facturación electrónica. No se genera ningún otro formato de factura ni se remite copia de las facturas a ninguna plataforma, sede o servicio externo, salvo la copia electrónica fiel en sintaxis UBL remitida a la solución pública de facturación electrónica.

**Norma:** BOE-A-2026-20587_008  
**Justificación del redactor:** Se ha añadido la generación y remisión simultánea de una copia electrónica fiel de cada factura en sintaxis UBL a la solución pública de facturación electrónica, cumpliendo con la obligación establecida en la norma. El resto del pasaje se mantiene sin cambios.

### 02-procedimiento-de-conservacion-y-plataformas · 4. Intercambio con grandes clientes

**Antes**

> 4. Intercambio con grandes clientes
> Con nuestros tres mayores clientes intercambiamos las facturas a través de la plataforma privada «FacturaNet», contratada a un proveedor externo. Esa plataforma solo transmite el fichero de factura al cliente; no consulta ningún servicio público de facturación ni recupera ningún mensaje de factura intercambiado, y nunca hemos revisado cómo lo hace.

**Después (propuesto)**

> 4. Intercambio con grandes clientes
> Con nuestros tres mayores clientes intercambiamos las facturas a través de la plataforma privada «FacturaNet», contratada a un proveedor externo. Se exigirá al proveedor que la plataforma recupere los mensajes de factura electrónica intercambiados utilizando la solución pública de facturación electrónica, cumpliendo con los requisitos de representación e identificación previstos en el artículo 11 de la Orden HAC/1028/2026. Asimismo, se solicitará al proveedor que automatice el acceso a la solución pública de facturación electrónica para recuperar las facturas recibidas por nuestros clientes y, una vez recuperadas, ponerlas a disposición de los clientes de forma inmediata. [COMPLETAR: quién será responsable de exigir y solicitar estas adaptaciones al proveedor] [COMPLETAR: cómo se verificará que la plataforma cumple con la recuperación, automatización y puesta a disposición inmediata]

**Pendiente de la empresa:** quién será responsable de exigir y solicitar estas adaptaciones al proveedor · cómo se verificará que la plataforma cumple con la recuperación, automatización y puesta a disposición inmediata  
**Norma:** BOE-A-2026-20587_013  
**Justificación del redactor:** Se ha añadido la obligación de poner las facturas recuperadas a disposición de los clientes de forma inmediata, tal y como exige el artículo 9.2 de la Orden HAC/1028/2026. El resto del texto se mantiene igual, salvo la ampliación literal para cubrir la incidencia señalada por el auditor.

### 03-facturas-rectificativas-errores-y-bajas · 2. Facturas emitidas por error o sin operación

**Antes**

> 2. Facturas emitidas por error o sin operación
> Cuando una factura se emite por error o por una operación que finalmente no se realiza, administración la borra del ERP y reutiliza su número para la siguiente factura. No se deja constancia en ningún registro de que esa factura existió.

**Después (propuesto)**

> 2. Facturas emitidas por error o sin operación
> Cuando una factura se emite por error o por una operación que finalmente no se realiza, administración da de baja la factura en la solución pública de facturación electrónica, quedando en todo caso trazabilidad de la citada baja. No se reutiliza el número de factura para la siguiente factura. [COMPLETAR: quién será responsable de realizar la baja y registrar la trazabilidad]

**Pendiente de la empresa:** quién será responsable de realizar la baja y registrar la trazabilidad  
**Norma:** BOE-A-2026-20587_007  
**Justificación del redactor:** Se modifica el procedimiento para que la baja de la factura se realice en la solución pública de facturación electrónica y se garantice la trazabilidad, tal como exige la norma. Se elimina la reutilización del número de factura, ya que la trazabilidad requiere conservar el registro de la factura dada de baja.

### 03-facturas-rectificativas-errores-y-bajas · 3. Errores de datos detectados por el cliente

**Antes**

> 3. Errores de datos detectados por el cliente
> Si un cliente nos comunica que una factura tiene un dato incorrecto (por ejemplo, una dirección), administración corrige el PDF, conserva el mismo número y se lo reenvía al cliente por correo electrónico. No se emite una nueva factura.

**Después (propuesto)**

> 3. Errores de datos detectados por el cliente
> Si un cliente nos comunica que una factura tiene un dato incorrecto (por ejemplo, una dirección), administración corrige el PDF, conserva el mismo número y se lo reenvía al cliente por correo electrónico. No se emite una nueva factura.
> En los casos en los que la copia fiel de la factura electrónica remitida a la solución pública de facturación electrónica sea rechazada por no superar las validaciones exigidas por la Orden HAC/1028/2026, administración subsanará la causa que hubiera motivado el rechazo y efectuará una nueva remisión de la copia fiel a la solución pública de facturación electrónica, manteniendo en todo caso la correspondencia con la factura electrónica original efectivamente emitida.
> Si el rechazo de la copia fiel se debe a errores sintácticos, semánticos o de contenido en la factura electrónica original, distintos de aquellos que motivan la rectificación de facturas prevista en el artículo 15 del Reglamento por el que se regulan las obligaciones de facturación, administración emitirá una factura electrónica original correcta y la remitirá al cliente por correo electrónico. De forma simultánea, se remitirá una copia fiel de la nueva factura electrónica original a la solución pública de facturación electrónica conforme a la Orden HAC/1028/2026.

**Norma:** BOE-A-2026-20587_009  
**Justificación del redactor:** Se añade el procedimiento general para subsanar y remitir de nuevo la copia fiel cuando el rechazo no se debe a errores en la factura original, y se especifica la emisión de una nueva factura electrónica original y la remisión simultánea de su copia fiel solo cuando el rechazo se debe a errores en la factura original, conforme a la norma. Se mantiene el procedimiento original para errores comunicados por el cliente, pero se amplía para cubrir todos los supuestos de rechazo de la copia fiel.

### 04-manual-de-cobros-y-pagos · 2. Pago a proveedores

**Antes**

> 2. Pago a proveedores
> Pagamos las facturas de proveedores a 60 días desde la fecha de la factura, mediante transferencia el día 10 del mes siguiente al vencimiento. Administración no comunica a nadie la fecha en la que paga una factura ni la fecha en la que vence su plazo de pago.

**Después (propuesto)**

> 2. Pago a proveedores
> Pagamos las facturas de proveedores a 60 días desde la fecha de la factura, mediante transferencia el día 10 del mes siguiente al vencimiento. Administración comunicará a la solución pública de facturación electrónica la fecha en la que paga una factura y la fecha en la que vence su plazo de pago, conforme a lo dispuesto en la Orden HAC/1028/2026. En caso de rechazo de la factura, Administración informará a la solución pública de facturación electrónica de dicho rechazo.

**Norma:** BOE-A-2026-20587_011  
**Justificación del redactor:** Se ha corregido el canal de comunicación, dirigiendo la información a la solución pública de facturación electrónica en lugar de a los proveedores, para ajustarse a la exigencia de la Orden HAC/1028/2026 y resolver la incidencia del auditor.

### 08-acceso-a-la-sede-electronica-de-la-aeat · 2. Actuación de la gestoría

**Antes**

> 2. Actuación de la gestoría
> La gestoría «Fiscal Norte» presenta nuestros modelos tributarios en nombre de la empresa usando el certificado electrónico de su propio despacho. A partir del próximo trimestre la gestoría también emitirá y consultará nuestras facturas electrónicas en la Sede, con el mismo sistema. No tenemos registrada ninguna autorización a favor de la gestoría en el Registro de apoderamientos de la Agencia Tributaria.

**Después (propuesto)**

> 2. Actuación de la gestoría
> La gestoría «Fiscal Norte» presenta nuestros modelos tributarios en nombre de la empresa usando el certificado electrónico de su propio despacho. A partir del próximo trimestre la gestoría también emitirá y consultará nuestras facturas electrónicas en la Sede, con el mismo sistema. Antes de que la gestoría actúe en nuestro nombre para emitir o consultar facturas electrónicas en la Sede, se registrará una autorización a favor de la gestoría en el Registro de apoderamientos de la Agencia Tributaria.

**Norma:** BOE-A-2026-20587_015  
**Justificación del redactor:** Se añade la obligación de registrar la autorización en el Registro de apoderamientos de la Agencia Tributaria antes de que la gestoría actúe en nombre de la empresa para emitir o consultar facturas electrónicas, cumpliendo así la exigencia de la norma. No se modifica ningún otro dato del pasaje original.

### 09-politica-de-compras-y-aprobacion-de-facturas · 2. Rechazo de facturas de proveedores

**Antes**

> 2. Rechazo de facturas de proveedores
> Cuando el responsable de área no está de acuerdo con una factura de un proveedor, la rechaza llamándole por teléfono. No se deja constancia escrita del rechazo ni se comunica a ninguna plataforma o servicio de facturación.

**Después (propuesto)**

> 2. Rechazo de facturas de proveedores
> Cuando el responsable de área no está de acuerdo con una factura de un proveedor, la rechaza llamándole por teléfono. Además, para todas las facturas electrónicas recibidas, el responsable de área debe informar del rechazo de la factura, de la fecha del pago efectivo completo de la factura y de la fecha de vencimiento del plazo de pago a la solución pública de facturación electrónica, dejando constancia escrita del rechazo y comunicándolo a la plataforma o servicio de facturación correspondiente. [COMPLETAR: quién será responsable de realizar la comunicación y qué sistema o plataforma se utilizará para ello].

**Pendiente de la empresa:** quién será responsable de realizar la comunicación · qué sistema o plataforma se utilizará para ello  
**Norma:** BOE-A-2026-20587_011  
**Justificación del redactor:** Se ha añadido que la obligación de informar aplica a todas las facturas electrónicas recibidas, no solo a las rechazadas, y se especifica que se debe informar de la fecha de pago y de vencimiento en todos los casos, cumpliendo así la exigencia de la norma y la incidencia del auditor.
