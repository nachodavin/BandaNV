# BandaNV v2.0 — Notas de versión

BandaNV 2.0 renueva la aplicación como un organizador portable de escritorio para Windows, con una interfaz moderna y operaciones más seguras.

## Novedades destacadas

**Nueva interfaz**
- Diseño en WinUI 3, navegación unificada entre Inicio, Organizar, Buscar, Historial, Categorías y Configuración.
- Tema y colores personalizables, ventanas emergentes propias y adaptación del contenido al tamaño de la ventana.
- Vista Inicio con métricas y alertas de archivos sin asignar.

**Organización**
- Análisis previo a la ejecución y opción de organizar carpetas completas como unidades.
- Conservación de carpetas y subcarpetas durante el movimiento.
- Detección de contenido mixto y extensiones desconocidas: estos casos se dejan sin asignar en vez de clasificarse automáticamente.
- Tratamiento de conflictos, reanálisis y controles de seguridad durante operaciones que modifican archivos.

**Búsqueda, historial y recuperación**
- Búsqueda con filtros, ordenación, agrupación y visualización de archivos contenidos en carpetas.
- Historial configurable, registros de acciones y deshacer cuando la operación lo permite.
- Recuperación ante interrupciones con información técnica de las operaciones.

**Categorías y configuración**
- Crear, editar, duplicar, eliminar y reordenar categorías arrastrando sus cards.
- Menú de acciones accesible con los tres puntos o con clic derecho sobre una card.
- Contadores que incluyen archivos de las subcarpetas, sin contar carpetas como si fueran archivos.
- Configuración de carpetas protegidas para impedir modificaciones desde las operaciones de BandaNV.
- Exportación e importación de preferencias mediante archivos `.bandanv`, con validación previa, confirmación y restauración.

**Actualizaciones y distribución**
- Distribución portable para Windows x64, sin instalador.
- Comprobación de actualizaciones desde GitHub Releases.
- Verificación SHA-256 del paquete, manifiesto de archivos y actualizador auxiliar `NVupdate`.
- Reinicio de la versión instalada y recuperación automática de la anterior si el nuevo inicio no se confirma.
- Conservación de las carpetas portables `config/`, `logs/` y `history/`.

## Instalación

1. Descargá `BandaNV_v2.0.zip` desde la release oficial.
2. Extraé la carpeta `BandaNV` en una ubicación donde tengas permisos de escritura.
3. Ejecutá `BandaNV.exe`.

El paquete incluye sus dependencias de ejecución. Evitá instalarlo dentro de `Program Files`.

## Alcance de los backups

El archivo `.bandanv` guarda preferencias, rutas, apariencia, categorías, extensiones y opciones de seguridad. **No** contiene los archivos personales organizados ni el historial y los logs; guardalos aparte si necesitás una copia íntegra de tus datos.

## Verificación previa al lanzamiento

- Smoke tests del motor: **39 OK, 0 errores**, informados tras las últimas pruebas del motor.
- Tests aislados del actualizador: **3 OK, 0 errores**, informados en Windows.
- Prueba E2E con `v2.0.1` prerelease de ensayo: detección, descarga, instalación, reinicio y conservación de datos confirmados.

Las pruebas se realizaron antes de retirar el canal de ensayo del código de producción. El **ZIP oficial v2.0** se generará con `Package_V2_Release.ps1`, que verifica su versión, ejecuta las pruebas del motor y comprueba el paquete final.
