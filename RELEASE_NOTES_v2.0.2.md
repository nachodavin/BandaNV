# BandaNV v2.0.2 — Corrección de acciones en Organizar

**Primera actualización correctiva de BandaNV 2.0.** Soluciona un problema en la sección **Organizar** que impedía ejecutar acciones sobre archivos seleccionados.

## Corrección principal

- Se corrigió un error por el que el panel de detalle podía limpiar la selección al visualizar un archivo.
- Se corrigió el mismo comportamiento al seleccionar varios archivos simultáneamente.
- Los botones **Eliminar**, **Renombrar**, **Copiar ruta** y **Cambiar categoría**, entre otras acciones contextuales, vuelven a recibir correctamente los archivos seleccionados.
- Se conserva el comportamiento de navegación en carpetas y subcarpetas.

## Mejoras adicionales

- **Inicio** y **Configuración > Acerca de** ahora muestran la versión a partir de los datos internos de la aplicación, evitando etiquetas de versión desactualizadas.
- El empaquetador de releases verifica que la versión del ejecutable, el tag y el manifiesto coincidan, facilitando las futuras actualizaciones correctivas.

## Instalación y actualización

- Para una instalación nueva, descargá `BandaNV_v2.0.2.zip` desde la release estable oficial, extraé la carpeta `BandaNV` y ejecutá `BandaNV.exe`.
- Si ya tenés BandaNV v2.0 y el sistema de actualizaciones está disponible, podés actualizar desde **Configuración > Acerca de > Buscar actualizaciones**, una vez publicada la release estable v2.0.2.
- Las carpetas portables `config/`, `logs/` y `history/` se conservan durante las actualizaciones gestionadas por BandaNV.

## Validación

- La corrección de selección y botones en **Organizar** se comprobó manualmente en Windows antes de su integración en `main`.
- El paquete oficial v2.0.2 debe pasar los smoke tests del motor y la validación del ZIP antes de su publicación.

Para todas las novedades de la versión principal, consultá [BandaNV v2.0 — Notas de versión](RELEASE_NOTES_v2.0.md).

---

**BandaNV — Organizar tus archivos, bajo control.**
