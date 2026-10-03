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
- Historial cronológico.
- Detalle integrado.
- Deshacer seguro.

### Etapa 5 — Buscar
- Pantalla de trabajo a ancho completo.
- Resumen superior por categoría con cantidad de archivos y acceso como filtro rápido.
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
