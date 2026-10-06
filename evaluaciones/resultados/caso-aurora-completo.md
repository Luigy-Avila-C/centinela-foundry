# Informe de caso

**Norma:** Orden HAC/1028/2026, de 2 de octubre, por la que se regula la solución pública de facturación electrónica, de acuerdo con lo dispuesto en la disposición final tercera del Real Decreto 238/2026, de 25 de marzo, por el que se desarrolla el sistema de facturación electrónica obligatoria entre empresarios y profesionales y por el que se modifica el Reglamento por el que se regulan las obligaciones de facturación, aprobado por el Real Decreto 1619/2012, de 30 de noviembre.

> **Datos ficticios.** La empresa («Distribuciones Aurora, S.L.») y sus documentos están inventados para probar Centinela.
> **No se ha aplicado ningún cambio.** Cada borrador espera la aprobación de una persona, y los datos marcados como
> `[COMPLETAR: …]` los tiene que aportar la empresa antes de aprobar.

- Estado final: **AwaitingHumanApproval** tras 3 ronda(s) de redacción
- Obligaciones analizadas: 91 · pasajes afectados: 7 · borradores: 7

## Auditoría, ronda a ronda

**Ronda 1:** 4 incidencia(s) bloqueante(s)
- [08-acceso-a-la-sede-electronica-de-la-aeat_002] problema_persiste: El borrador conserva tal cual lo que el pasaje tenía de incumplimiento: «No tenemos registrada ninguna autorización a favor de la gestoría en el Registro de apoder…». Hay que eliminarlo o cambiarlo. (cita: «No tenemos registrada ninguna autorización a favor de la gestoría en el Registro de apoderamientos de la Agencia Tributaria.»)
- [01-politica-de-facturacion_002] contradice_norma: El borrador presenta la remisión de la copia UBL a la solución pública como una decisión futura de la empresa (“en adelante, se generará…”) y no como una obligación legal ya vigente que debe cumplirse, lo que puede interpretarse como que hasta ahora no era exigible, en contradicción con la norma. (cita: «en adelante, se generará simultáneamente a la emisión una copia electrónica fiel de cada factura en la sintaxis UBL y se remitirá a la solución pública de facturación electrónica, conforme a lo dispuesto en la Orden HAC/1028/2026.»)
- [02-procedimiento-de-conservacion-y-plataformas_004] no_cubre: No incorpora la obligación de poner las facturas recuperadas a disposición de los clientes de forma inmediata, que forma parte del alcance aplicable del artículo 9.2. (cita: «Además, la plataforma deberá automatizar el acceso a la solución pública de facturación electrónica para recuperar las facturas recibidas por nuestros clientes.»)
- [09-politica-de-compras-y-aprobacion-de-facturas_002] no_cubre: Solo regula la comunicación del rechazo, pero no menciona la obligación de informar de la fecha del pago efectivo completo ni de la fecha de vencimiento del plazo de pago, que también forman parte del alcance aplicable de la obligación. (cita: «en adelante, el rechazo de la factura deberá ser informado y comunicado a la solución pública de facturación electrónica conforme a lo dispuesto en la Orden HAC/1028/2026.»)

**Ronda 2:** 2 incidencia(s) bloqueante(s)
- [01-politica-de-facturacion_002] dato_inventado: Se menciona un número de orden (HAC/1028/2026) que no figura en el pasaje original, ni en las obligaciones, ni en el extracto de la norma aportado. (cita: «conforme a lo dispuesto en la Orden HAC/1028/2026»)
- [02-procedimiento-de-conservacion-y-plataformas_004] cambio_innecesario: La referencia expresa al artículo 11 y a la Orden HAC/1028/2026 no es necesaria para cubrir el alcance aplicable de la obligación, que solo exige que se indique la obligación de recuperar los mensajes cuando se utilice la solución pública. (cita: «En adelante, la plataforma deberá recuperar los mensajes de factura electrónica intercambiados cuando utilice la solución pública de facturación electrónica, cumpliendo con los requisitos de representación e identificación previstos en el artículo 11 de la Orden HAC/1028/2026.»)

**Ronda 3:** 3 incidencia(s) bloqueante(s)
- [01-politica-de-facturacion_002] dato_inventado: El borrador introduce la cifra «008», que no aparece en el pasaje original, en las obligaciones ni en la norma. (cita: «008»)
- [01-politica-de-facturacion_002] dato_inventado: El borrador introduce la cifra «20587», que no aparece en el pasaje original, en las obligaciones ni en la norma. (cita: «20587»)
- [01-politica-de-facturacion_002] problema_persiste: El borrador conserva tal cual lo que el pasaje tenía de incumplimiento: «No se genera ningún otro formato de factura ni se remite copia de las facturas a ninguna p…». Hay que eliminarlo o cambiarlo. (cita: «No se genera ningún otro formato de factura ni se remite copia de las facturas a ninguna plataforma, sede o servicio externo.»)

## Borradores

### 01-politica-de-facturacion · 2. Emisión de facturas

**Antes**

> 2. Emisión de facturas
> Las facturas se generan en nuestro ERP «Aurora-ERP» el mismo día de la entrega de la mercancía. El ERP las exporta en formato PDF y administración las envía por correo electrónico al cliente. No se genera ningún otro formato de factura ni se remite copia de las facturas a ninguna plataforma, sede o servicio externo.

**Después (propuesto)**

> 2. Emisión de facturas
> Las facturas se generan en nuestro ERP «Aurora-ERP» el mismo día de la entrega de la mercancía. El ERP las exporta en formato PDF y administración las envía por correo electrónico al cliente. Hasta ahora, no se genera ningún otro formato de factura ni se remite copia de las facturas a ninguna plataforma, sede o servicio externo; en adelante, se generará simultáneamente a la emisión una copia electrónica fiel de cada factura en la sintaxis UBL y se remitirá a la solución pública de facturación electrónica, conforme a lo dispuesto en BOE-A-2026-20587_008. [COMPLETAR: quién será responsable de la generación y remisión de la copia UBL] [COMPLETAR: sistema o procedimiento que realizará la generación y remisión de la copia UBL]

**Pendiente de la empresa:** quién será responsable de la generación y remisión de la copia UBL · sistema o procedimiento que realizará la generación y remisión de la copia UBL  
**Norma:** BOE-A-2026-20587_008  
**Justificación del redactor:** Se ha eliminado la referencia inventada a la Orden HAC/1028/2026 y se ha sustituido por la referencia literal al identificador normativo BOE-A-2026-20587_008. Se ha reescrito la frase problemática como situación anterior más compromiso, y se ha añadido el compromiso de generar y remitir la copia UBL, dejando los datos pendientes de completar.

### 02-procedimiento-de-conservacion-y-plataformas · 4. Intercambio con grandes clientes

**Antes**

> 4. Intercambio con grandes clientes
> Con nuestros tres mayores clientes intercambiamos las facturas a través de la plataforma privada «FacturaNet», contratada a un proveedor externo. Esa plataforma solo transmite el fichero de factura al cliente; no consulta ningún servicio público de facturación ni recupera ningún mensaje de factura intercambiado, y nunca hemos revisado cómo lo hace.

**Después (propuesto)**

> 4. Intercambio con grandes clientes
> Con nuestros tres mayores clientes intercambiamos las facturas a través de la plataforma privada «FacturaNet», contratada a un proveedor externo. Hasta ahora, esa plataforma solo transmitía el fichero de factura al cliente; no consultaba ningún servicio público de facturación ni recuperaba ningún mensaje de factura intercambiado, y nunca hemos revisado cómo lo hace. En adelante, la plataforma deberá recuperar los mensajes de factura electrónica intercambiados cuando utilice la solución pública de facturación electrónica. Además, la plataforma deberá automatizar el acceso a la solución pública de facturación electrónica para recuperar las facturas recibidas por nuestros clientes y, una vez recuperadas, ponerlas a su disposición de forma inmediata. [COMPLETAR: quién será responsable de verificar la implementación de estos requisitos y cuándo se realizará la revisión]

**Pendiente de la empresa:** quién será responsable de verificar la implementación de estos requisitos y cuándo se realizará la revisión  
**Norma:** BOE-A-2026-20587_013  
**Justificación del redactor:** Se elimina la referencia innecesaria al artículo 11 y a la Orden HAC/1028/2026, manteniendo solo el compromiso de recuperar los mensajes y automatizar el acceso, conforme a las obligaciones y el alcance aplicable. Se conserva la descripción de la situación anterior y se añade el compromiso futuro, cumpliendo con el cambio mínimo.

### 03-facturas-rectificativas-errores-y-bajas · 2. Facturas emitidas por error o sin operación

**Antes**

> 2. Facturas emitidas por error o sin operación
> Cuando una factura se emite por error o por una operación que finalmente no se realiza, administración la borra del ERP y reutiliza su número para la siguiente factura. No se deja constancia en ningún registro de que esa factura existió.

**Después (propuesto)**

> 2. Facturas emitidas por error o sin operación
> Cuando una factura se emite por error o por una operación que finalmente no se realiza, administración procederá a dar de baja la factura en el sistema, dejando constancia y trazabilidad de la citada baja conforme a lo dispuesto en la Orden HAC/1028/2026. El número de la factura no será reutilizado para la siguiente factura. Se conservará en el registro la información necesaria para garantizar la trazabilidad de la baja de la factura emitida por error o sin operación.

**Norma:** BOE-A-2026-20587_007  
**Justificación del redactor:** Se elimina la práctica de borrar la factura y reutilizar el número, y se introduce la obligación de dejar constancia y trazabilidad de la baja, tal como exige la norma. Se añade que el número no se reutiliza y que se conserva la información necesaria para la trazabilidad.

### 03-facturas-rectificativas-errores-y-bajas · 3. Errores de datos detectados por el cliente

**Antes**

> 3. Errores de datos detectados por el cliente
> Si un cliente nos comunica que una factura tiene un dato incorrecto (por ejemplo, una dirección), administración corrige el PDF, conserva el mismo número y se lo reenvía al cliente por correo electrónico. No se emite una nueva factura.

**Después (propuesto)**

> 3. Errores de datos detectados por el cliente
> Si un cliente nos comunica que una factura tiene un dato incorrecto (por ejemplo, una dirección), hasta ahora administración corregía el PDF, conservaba el mismo número y se lo reenviaba al cliente por correo electrónico, sin emitir una nueva factura. En adelante, se deberá subsanar el error mediante la emisión de una factura electrónica original correcta y, de forma simultánea, remitir una copia fiel a la solución pública de facturación electrónica, conforme a lo dispuesto en la Orden HAC/1028/2026. [COMPLETAR: quién será responsable de emitir la nueva factura y remitir la copia fiel]

**Pendiente de la empresa:** quién será responsable de emitir la nueva factura y remitir la copia fiel  
**Norma:** BOE-A-2026-20587_009  
**Justificación del redactor:** Se elimina la práctica de solo corregir el PDF y reenviarlo, y se introduce el compromiso de emitir una nueva factura electrónica original correcta y remitir una copia fiel a la solución pública, tal como exige la norma. Se mantiene la referencia a la situación anterior para reflejar el cambio de procedimiento.

### 04-manual-de-cobros-y-pagos · 2. Pago a proveedores

**Antes**

> 2. Pago a proveedores
> Pagamos las facturas de proveedores a 60 días desde la fecha de la factura, mediante transferencia el día 10 del mes siguiente al vencimiento. Administración no comunica a nadie la fecha en la que paga una factura ni la fecha en la que vence su plazo de pago.

**Después (propuesto)**

> 2. Pago a proveedores
> Pagamos las facturas de proveedores a 60 días desde la fecha de la factura, mediante transferencia el día 10 del mes siguiente al vencimiento. Hasta ahora, Administración no comunicaba a nadie la fecha en la que paga una factura ni la fecha en la que vence su plazo de pago; en adelante, Administración deberá informar de la fecha del pago efectivo completo de la factura y de la fecha de vencimiento del plazo de pago a los proveedores, conforme a lo establecido en la normativa aplicable.

**Norma:** BOE-A-2026-20587_011  
**Justificación del redactor:** Se ha eliminado la afirmación de que Administración no comunica las fechas y se ha añadido el compromiso de informar tanto la fecha de pago efectivo como la de vencimiento del plazo de pago, cumpliendo así la obligación de la Orden HAC/1028/2026.

### 08-acceso-a-la-sede-electronica-de-la-aeat · 2. Actuación de la gestoría

**Antes**

> 2. Actuación de la gestoría
> La gestoría «Fiscal Norte» presenta nuestros modelos tributarios en nombre de la empresa usando el certificado electrónico de su propio despacho. A partir del próximo trimestre la gestoría también emitirá y consultará nuestras facturas electrónicas en la Sede, con el mismo sistema. No tenemos registrada ninguna autorización a favor de la gestoría en el Registro de apoderamientos de la Agencia Tributaria.

**Después (propuesto)**

> 2. Actuación de la gestoría
> La gestoría «Fiscal Norte» presenta nuestros modelos tributarios en nombre de la empresa usando el certificado electrónico de su propio despacho. A partir del próximo trimestre la gestoría también emitirá y consultará nuestras facturas electrónicas en la Sede, con el mismo sistema. Antes de que la gestoría actúe en nombre de la empresa para la emisión y consulta de facturas electrónicas, se deberá registrar una autorización a favor de la gestoría en el Registro de apoderamientos de la Agencia Tributaria.

**Norma:** BOE-A-2026-20587_015  
**Justificación del redactor:** Se elimina la frase que describe la carencia actual y se sustituye por un compromiso de registrar la autorización antes de que la gestoría actúe, cumpliendo así la obligación de que el tercero figure inscrito en el Registro de apoderamientos.

### 09-politica-de-compras-y-aprobacion-de-facturas · 2. Rechazo de facturas de proveedores

**Antes**

> 2. Rechazo de facturas de proveedores
> Cuando el responsable de área no está de acuerdo con una factura de un proveedor, la rechaza llamándole por teléfono. No se deja constancia escrita del rechazo ni se comunica a ninguna plataforma o servicio de facturación.

**Después (propuesto)**

> 2. Rechazo de facturas de proveedores
> Cuando el responsable de área no está de acuerdo con una factura de un proveedor, la rechaza llamándole por teléfono. Hasta ahora, no se dejaba constancia escrita del rechazo ni se comunicaba a ninguna plataforma o servicio de facturación; en adelante, el rechazo de la factura, la fecha del pago efectivo completo de la factura y la fecha de vencimiento del plazo de pago deberán ser informados y comunicados a la solución pública de facturación electrónica conforme a lo dispuesto en la Orden HAC/1028/2026. [COMPLETAR: quién será responsable de realizar la comunicación y el procedimiento concreto a seguir].

**Pendiente de la empresa:** quién será responsable de realizar la comunicación y el procedimiento concreto a seguir  
**Norma:** BOE-A-2026-20587_011  
**Justificación del redactor:** Se ha añadido la obligación de informar y comunicar la fecha del pago efectivo completo y la fecha de vencimiento del plazo de pago, además del rechazo, para cubrir el alcance completo de la obligación según la norma y la incidencia del auditor.
