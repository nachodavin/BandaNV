# BandaNV v2.0 — Roadmap

## Dirección

BandaNV v2.0 deja atrás la interfaz basada en ventanas pequeñas y pasa a una aplicación de escritorio moderna con una ventana principal maximizada y redimensionable.

Tecnología elegida:

- C#
- .NET 10
- WinUI 3
- Windows App SDK 2.5.1
- distribución portable, unpackaged y self-contained

La v1.0 permanece como base estable en `main` mientras v2.0 se desarrolla en su rama.

## Navegación principal

1. Inicio
2. Organizar
3. Buscar
4. Historial
5. Categorías
6. Configuración

## Principios

- Una sola ventana principal.
- Popups únicamente para confirmaciones o errores que realmente lo necesiten.
- El motor queda separado de la interfaz.
- No convertir BandaNV en otro explorador de archivos.
- Mantener el flujo central: detectar desorden → revisar → organizar.
- Seguir siendo portable: descargar ZIP → extraer carpeta → ejecutar.

## Migración por etapas

### Etapa 1 — Shell moderno
- Solución C#.
- Core separado de App.
- Navegación principal.
- Inicio como dashboard informativo, sin ejecutar acciones desde esa pantalla.
- Métricas de Inicio: archivos pendientes, archivos organizados totales, tamaño total movido, categorías activas, última organización y organizados en la última ejecución.
- Bloques de uso por categoría y actividad reciente.
- Aplicación maximizada.

### Etapa 2 — Configuración y Categorías
- Leer y migrar `bandanv_config.json`.
- Carpeta origen.
- Descargas automáticas.
- CRUD y reordenamiento de categorías.
- Compatibilidad con configuración v1.0.

### Etapa 3 — Motor de organización
- Preview real.
- Archivos clasificados y sin clasificar.
- Confirmación.
- Movimiento seguro y nombres duplicados.

### Etapa 4 — Historial y Undo
- Leer logs existentes.
- Historial cronológico con la misma lógica visual de búsqueda, filtros, orden y paginación usada en Buscar.
- Métricas superiores: total de ejecuciones, archivos organizados, tamaño movido y última organización.
- Búsqueda por fecha, archivo, categoría, carpeta de origen o destino.
- Tabla principal de ejecuciones con fecha/hora, tipo, origen, cantidad de archivos y tamaño.
- Detalle integrado de la ejecución seleccionada dentro de la misma pantalla.
- El detalle mostrará rutas, todos los archivos afectados, categorías, tamaños, estado y disponibilidad de Undo.
- Cuando una ejecución tenga muchos archivos, el panel derecho mantiene encabezado y botón de Undo fijos y desplaza verticalmente el contenido completo; no se resume con "+N archivos".
- La maqueta visual incluye estados seleccionables de ejemplo: ejecución reversible, ejecución ya deshecha, log antiguo sin metadatos de Undo y registro de Deshacer.
- El botón de Undo permanece fijo al pie del panel de detalle, solo se habilita cuando la ejecución seleccionada es reversible y usa el teal principal de BandaNV como estado activo.
- Deshacer seguro únicamente cuando la ejecución sea realmente reversible.

### Etapa 5 — Buscar
- Pantalla de trabajo a ancho completo.
- Resumen superior generado dinámicamente con todas las categorías configuradas, sin categorías privilegiadas u ocultas.
- Las tarjetas respetan siempre el orden global numérico de categorías (`CategoryDefinition.Order`), compartido con Categorías, Organizar y futuras estadísticas.
- Las categorías se muestran en un carrusel por tandas de hasta 10 tarjetas, siempre en una sola fila; las 10 columnas se distribuyen de forma uniforme para ocupar todo el ancho disponible sin dejar espacio muerto.
- Si existen más de 10 categorías, aparecen flechas laterales para avanzar o retroceder una tanda completa.
- Debajo del carrusel se muestra un indicador de páginas mediante puntos (por ejemplo: `● ○ ○`); la tanda activa usa el color teal.
- Con 10 categorías o menos, las flechas y el indicador permanecen ocultos.
- Cada tarjeta muestra cantidad de archivos y las extensiones asignadas a la categoría; se muestran hasta 4 extensiones y, si hay más, se resume con `+N`.
- Las extensiones mostradas provienen de la misma configuración de Categorías y se actualizan junto con ella.
- Cada tarjeta funciona como filtro rápido por categoría.
- Búsqueda por nombre, extensión y categoría.
- Filtros activos mediante chips y opción para limpiar filtros.
- Ordenamiento por nombre, fecha, tamaño y categoría.
- Tabla principal con nombre, categoría, tamaño, fecha, ubicación y menú contextual.
- Resultados enriquecidos.
- Abrir archivo, abrir ubicación y copiar ruta desde las acciones de cada resultado.

### Etapa 6 — Updater y distribución
- Migrar el updater a la arquitectura v2.0.
- Mantener en Configuración las opciones nativas de actualización de v1.0: comprobación al iniciar, canal beta/estable, comprobación manual y última comprobación.
- Quick Update Status en el sidebar como acceso extra al mismo sistema de actualización.
- El Quick Update Status permanece oculto cuando BandaNV está actualizado y aparece únicamente si existe una versión más nueva que la instalada.
- Texto cuando aparece: "Nueva actualización" y "Última versión: vX.X".
- Estados futuros del flujo: nueva versión disponible, descargando y listo para reiniciar.
- Publicación portable self-contained.
- Compatibilidad de actualización desde v1.x.
