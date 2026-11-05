# BandaNV v2.0.3 — Vista previa de archivos

Esta actualización incorpora un visor integrado en los paneles de detalle de **Buscar** y **Organizar**, que permite consultar el contenido de archivos sin abrir ventanas adicionales. Está disponible también dentro de carpetas y subcarpetas.

## Nuevo: vista previa integrada

- **Imágenes:** PNG, JPG, JPEG, BMP, GIF y WebP. Ajuste al espacio disponible sin recortar ni deformar. Los GIF se muestran como imagen fija.
- **PDF:** visualización de páginas dentro del panel, con botones **Anterior** y **Siguiente** e indicador de página actual.
- **TXT:** lectura con desplazamiento y selección de texto. Para archivos grandes, se muestran hasta los primeros 150.000 caracteres.
- **SVG:** visualización mediante el componente vectorial nativo de Windows.
- El visor aprovecha el espacio vertical disponible y adapta su altura al panel lateral.
- Al seleccionar otro archivo, la vista previa se actualiza sin que las cargas antiguas sustituyan al elemento actual.
- La vista previa se oculta para carpetas, selecciones múltiples y formatos no admitidos.
- **Ningún archivo original se modifica** al obtener su vista previa.

## Estabilidad y correcciones

- Se corrigió una carga de imágenes que podía quedarse en «Cargando vista previa…» tras cambiar de archivo.
- Se implementó la decodificación independiente de imágenes con `SoftwareBitmapSource`.
- Se limita la resolución de imágenes y páginas PDF para controlar el consumo de memoria.
- Se manejan errores de lectura y cargas prolongadas sin cerrar la aplicación.

## Instalación y actualización

- **Instalación nueva:** descargá `BandaNV_v2.0.3.zip` de la release oficial estable, extraé la carpeta `BandaNV` y ejecutá `BandaNV.exe`.
- **Actualización:** desde v2.0.2 podés utilizar el sistema de actualizaciones integrado después de que la release v2.0.3 publique su ZIP oficial con SHA-256.
- El actualizador conserva los datos portables del usuario en `config/`, `logs/` y `history/`.

## Validación

- Se probaron manualmente en Windows los cambios entre imágenes y la vista previa de PDF, TXT y SVG.
- Antes de publicar la release oficial debe ejecutarse `Package_V2_Release.ps1` en Windows, que realiza los smoke tests, compila la aplicación y NVupdate en Release, y valida el paquete y su manifiesto.

Para más información, consultá [BandaNV v2.0.2](RELEASE_NOTES_v2.0.2.md) y [BandaNV v2.0](RELEASE_NOTES_v2.0.md).

---

**BandaNV — Organizar tus archivos, bajo control.**
