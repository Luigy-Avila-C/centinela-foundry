# Política de seguridad

Centinela es un proyecto de investigación y aprendizaje, **no un producto listo para producción**. Léelo con esa expectativa.

## Reportar una vulnerabilidad

Si encuentras un fallo de seguridad, **no abras una incidencia pública**. Usa *Security → Report a vulnerability* de este repositorio
(avisos privados de GitHub). Intentaré responder en unos días; es un proyecto personal, sin plazos garantizados.

## Qué se considera en alcance

- Formas de **saltarse el guardián** (inyección de instrucciones en el texto de una fuente) o de hacer que un modelo actúe fuera del flujo.
- Cualquier camino que permita **aprobar o aplicar un cambio sin una persona**.
- **Fugas de datos** en las trazas, los logs o la API (las trazas no deben llevar texto de normas ni de documentos).
- Secretos o credenciales en el repositorio.

## Límites conocidos (no hace falta reportarlos)

- **La API no tiene usuarios:** se protege con **una clave compartida** (`Api__Key`) y el nombre del revisor no se verifica. Es para uso local; **no
  la expongas a Internet** sin añadir Microsoft Entra ID.
- Los ataques con los que se midió el guardián los escribió el autor, y las reglas se diseñaron conociéndolos: **no es una garantía** frente a un
  atacante que conozca el sistema.
- Una norma manipulada que **afirme algo falso** sin dar órdenes a ningún modelo no la ve el guardián; la defensa ahí son las citas verificadas y la
  persona que aprueba.
- Los documentos de la empresa que se usan en los prompts **no pasan por el guardián** (solo el texto de las fuentes).

Más detalle en [`docs/ARQUITECTURA.md`](docs/ARQUITECTURA.md#41-guardián-de-seguridad).
