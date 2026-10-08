# BandaNV v2.0.2 — Menús contextuales y correcciones en Organizar

**Primera actualización de mantenimiento de BandaNV 2.0.** Incorpora menús contextuales con clic derecho en Organizar y Buscar, y corrige el manejo de la selección de archivos en Organizar.

## Nuevo: menús contextuales en Organizar y Buscar

- Acceso a las acciones de archivos y carpetas mediante **clic derecho**, inspirado en el Explorador de Windows 11.
- Disponibles en los listados principales y al navegar por carpetas y subcarpetas.
- Acciones según el elemento seleccionado: **Abrir archivo o contenido, Abrir ubicación, Copiar ruta, Cambiar categoría, Renombrar y Eliminar**.
- Opciones adaptadas a la **selección múltiple**; las acciones que requieren un elemento individual no se ofrecen para varios archivos.
- Diseño integrado con BandaNV: íconos, separadores, esquinas redondeadas, aparición junto al cursor y hover con el color secundario y texto negro.
- Los nombres de las opciones coinciden con los botones del panel lateral.
- Reutiliza las mismas acciones, confirmaciones y reglas de seguridad que ya utiliza el panel lateral.

## Corrección principal en Organizar

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
- Los nuevos menús contextuales y sus ajustes visuales se probaron manualmente en Windows, incluyendo la apertura, el hover y las acciones.
- El paquete oficial v2.0.2 debe pasar los smoke tests del motor y la validación del ZIP antes de su publicación.

Para todas las novedades de la versión principal, consultá [BandaNV v2.0 — Notas de versión](RELEASE_NOTES_v2.0.md).

---

**BandaNV — Organizar tus archivos, bajo control.**
