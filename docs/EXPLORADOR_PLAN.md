# BandaNV — Plan de implementación del Explorador completo

**Estado:** planificación inicial · **Rama de desarrollo:** `feature/explorador-completo` · **Base estable:** v2.0.3 / `main`.

## Visión de producto: BandaNV como explorador principal

**Objetivo central acordado:** BandaNV debe poder usarse **todos los días en lugar de abrir el Explorador de archivos de Windows 11**. No basta con incluir un visor de carpetas dentro de la app: la exploración, la búsqueda y las operaciones cotidianas sobre archivos deben ser una experiencia de primera clase, familiar, rápida y completa. **Organizar** es la funcionalidad que la diferencia y debe estar disponible en todo el flujo, sin obligar al usuario a cambiar de aplicación.

**Criterios de producto que guían cada etapa:**

- **Paridad cotidiana con Windows 11:** navegación por la PC, discos y rutas reales, rutas UNC y ubicaciones sincronizadas cuando sean accesibles; abrir archivos, portapapeles interoperable, copiar/cortar/pegar, renombrar, eliminar, crear carpetas, arrastrar/soltar, búsqueda, vistas, pestañas, favoritos y panel de detalles.
- **Familiaridad:** que una persona acostumbrada al Explorador de Windows entienda de inmediato dónde está cada función, pueda usar los mismos atajos y no tenga que aprender otro paradigma de navegación.
- **Rendimiento:** carga bajo demanda, virtualización, listas grandes sin congelamientos, observación de cambios externos y manejo de errores de acceso sin bloquear la interfaz.
- **Organizar como ventaja diferencial:** desde cualquier carpeta, seleccionar archivos y acceder a la clasificación por categoría, vista previa, resolución segura de conflictos, historial y recuperación. Diferenciar claramente organizar (enviar a categorías configuradas) de mover/copiar archivos comunes.
- **BandaNV como una sola experiencia:** que Explorar sea protagonista y Organizar esté a un clic, manteniendo identidad visual y configuración portable.
- **Compatibilidad práctica:** archivos asociados, accesos directos, unidades externas, permisos, carpetas de sistema, rutas largas, dispositivos y ubicaciones de red que el usuario realmente utilice. Las integraciones exclusivas del shell de Windows se documentan como limitaciones si no pueden ofrecerse de manera segura.
- **Seguridad primero:** evitar pérdida de datos, revalidar rutas y operaciones, conservar las carpetas protegidas y las garantías de Deshacer cuando existan; no asumir permisos especiales.

**Definición de éxito:** poder abrir BandaNV en lugar del Explorador de Windows para localizar, navegar, visualizar, abrir, copiar, mover, crear, renombrar, borrar y administrar los archivos del día a día **sin sentir que falta una operación básica**, y contar además con el sistema de organización propio de BandaNV.

**Aclaración técnica:** ser la alternativa de uso diario al Explorador no implica sustituir `explorer.exe` como shell de Windows ni intervenir en componentes del sistema operativo. Es una aplicación independiente que busca la misma experiencia práctica y la mejora con organización inteligente.

## Objetivo

Evolucionar la sección **Buscar** hacia **Explorar**, con una experiencia cotidiana comparable al Explorador de archivos de Windows 11: navegar por unidades y carpetas reales de la PC, ver y gestionar archivos, elegir modos de vista, usar portapapeles y atajos de teclado y conservar las funcionalidades propias de BandaNV.

**Límite deliberado:** no se busca sustituir el proceso de shell de Windows, replicar todas las extensiones del Explorador ni obtener permisos de administrador. Se respetan permisos del usuario y protecciones configuradas. La experiencia cotidiana es la meta, no reproducir mecanismos internos del sistema operativo.

## Decisiones de arquitectura

1. `main` y la release estable permanecen intactas durante el desarrollo.
2. Crear `ExplorerPage.xaml` y su lógica **sin borrar SearchPage**. Migrar la navegación y renombrar la opción «Buscar» a «Explorar» solo después de verificar la paridad funcional.
3. Separar el nuevo módulo del `SearchIndexService`, que indexa únicamente `settings.DestinationFolder`. **Nunca** intentar explorar toda la PC mediante un escaneo recursivo global.
4. No reutilizar sin revisión `SearchFileActionService`: sus operaciones están deliberadamente restringidas al destino organizado. El explorador necesita un servicio de operaciones de alcance general, con reglas de seguridad compartidas, conflictos, permisos y auditoría.
5. Conservar `ProtectedFolderService`, `BandaContextMenu`, `BandaImagePreview`, preferencias de tema y mecanismos de registro existentes cuando correspondan. Conservar `Organizar`, `Historial`, `Categorías`, `Inicio` y `Configuración`.
6. La interfaz del explorador tendrá barra de navegación/ruta, barra de acciones, panel de ubicaciones, lista central, panel de detalles/vista previa y barra de estado. Para evitar dos barras laterales anchas, permitir colapsar la barra general de BandaNV al entrar en Explorar.
7. Las rutas se consultan **a demanda**, con cancelación, carga diferida y manejo de errores por carpeta. Mostrar carpetas protegidas por permisos como no accesibles, sin bloquear toda la página.
8. La aplicación sigue siendo portable: guardar preferencias propias, favoritos y estado de navegación junto al ejecutable, sin moverlos a AppData.

## Etapas

### BUILD 7A — Fundamentos y navegación (solo lectura)

**Estado:** primera implementación integrada en la rama experimental; pendiente de compilar y validar visualmente en Windows antes de cerrar la etapa.

- [x] Crear `ExplorerPage` sin modificar el flujo actual de Buscar. Disponible desde una entrada temporal «Explorar».
- [x] Crear modelo `ExplorerItem`: ruta, nombre, tipo, tamaño, fecha, atributos e identificación de carpeta.
- [x] Crear `ExplorerFileSystemService` para enumerar directorios y unidades a demanda.
- [x] Pantalla de inicio del explorador: «Este equipo», unidades disponibles, ubicaciones personales, acceso al origen y destino configurados.
- [x] Barra de direcciones editable, migas de pan, Atrás, Adelante, Subir y Actualizar.
- [x] Tabla central en vista Detalles: nombre, fecha, tipo y tamaño; doble clic, selección múltiple y apertura de archivos.
- [x] Estado visible: carpeta, cantidad de elementos, selección y errores de permisos.
- [ ] Validar manualmente en Windows: carpetas vacías/inaccesibles, rutas profundas, cambios durante el listado, unidades desconectadas y rendimiento en carpetas grandes. Se agregaron tres smoke tests para el servicio, pendientes de ejecutar en el equipo de desarrollo.

**Criterio de salida:** recorrer carpetas reales y volver por el historial sin movimientos de archivos, sin congelamiento de UI y sin perder las capacidades actuales de Buscar.

### BUILD 7B — Operaciones de archivos

- [ ] Crear servicio de operaciones `ExplorerFileOperationsService`, separado de los handlers de interfaz.
- [ ] Crear carpeta; renombrar; copiar; cortar; pegar; mover; eliminar con Papelera donde corresponda.
- [ ] Portapapeles interoperable con el Explorador de Windows, con archivos y carpetas reales, no solo texto con rutas.
- [ ] Menús contextuales y atajos `Ctrl+C`, `Ctrl+X`, `Ctrl+V`, `F2`, `Supr`, `Ctrl+A`.
- [ ] Diálogos propios para conflictos con opciones seguras y progreso/cancelación.
- [ ] Respetar `ProtectedFolderService`, permisos de Windows y validación de origen/destino (incluidas rutas iguales, carpetas descendientes y enlaces simbólicos).
- [ ] Registrar en el historial las operaciones que corresponda; ofrecer Deshacer **solo cuando pueda garantizarse** su seguridad.

**Criterio de salida:** gestionar un archivo o lote en carpetas reales, cancelar o resolver conflictos y conservar datos sin sobrescrituras inesperadas.

### BUILD 8 — Experiencia visual Windows 11

- [ ] Íconos de archivo y carpeta coherentes con asociaciones del sistema.
- [ ] Miniaturas con carga diferida y caché limitada.
- [ ] Vistas Detalles, Lista, Íconos y Galería.
- [ ] Ordenación y agrupación por columnas; filtros de búsqueda dentro de la ubicación.
- [ ] Panel derecho de propiedades y vista previa usando `BandaImagePreview`.
- [ ] Ajustes de ancho de columnas, densidad y paneles; persistencia portable de preferencias.

**Criterio de salida:** carpetas grandes siguen siendo fluidas y la visualización no impide navegar ni seleccionar.

### BUILD 9 — Productividad y navegación avanzada

- [ ] Arrastrar y soltar desde/hacia Windows, entre carpetas y entre ubicaciones.
- [ ] Pestañas internas con estado de carpeta, historial y selección independientes.
- [ ] Favoritos y accesos rápidos; vista de destinos y rutas frecuentes.
- [ ] Búsqueda por nombre dentro de la ubicación actual, sin indexación global obligatoria.
- [ ] Actualización reactiva ante cambios externos y manejo de unidades extraíbles.
- [ ] Revisar rutas de red UNC y ubicaciones sincronizadas (sin exigir que todos los proveedores de nube funcionen como archivos locales).

**Criterio de salida:** navegación con varias pestañas y operaciones con ratón/teclado consistentes sin pérdida de estado.

### BUILD 10 — Integración total con BandaNV

- [ ] Integrar accesos por categorías como ubicaciones virtuales o accesos directos.
- [ ] Volver a exponer en Explorar todas las funciones actuales de Buscar: filtros, orden/agrupación, cambiar categoría, subcarpetas, selección múltiple, estados y registro.
- [ ] Agregar acciones propias de BandaNV al contexto: organizar selección, abrir origen/destino, cambiar categoría cuando corresponda.
- [ ] Migrar preferencias y la navegación principal de «Buscar» a «Explorar», manteniendo compatibilidad con valores guardados.
- [ ] Pruebas completas de regresión de Inicio, Organizar, Historial, Categorías y Configuración, además de los smoke tests del motor/actualizador.
- [ ] Solo entonces retirar o fusionar `SearchPage` y publicar una nueva versión estable con nota en `CHANGELOG.md`.

**Criterio de salida:** la nueva sección sustituye a Buscar sin perder ninguna función existente y mantiene intactos los datos portables.

## Precauciones y casos límite

- No navegar recursivamente todas las unidades de inicio; las carpetas se enumeran al abrirse.
- Evitar ciclos al recorrer junctions, enlaces simbólicos y puntos de reanálisis.
- No modificar rutas protegidas de usuario ni ubicaciones de sistema inadvertidamente.
- Tratar nombres largos, archivos bloqueados, rutas UNC, accesos denegados, dispositivos desconectados y contenido en nube como estados normales, no fallas fatales.
- No descargar automáticamente archivos en nube para generar miniaturas o vista previa sin necesidad/confirmación.
- No prometer deshacer universal: algunas operaciones externas pueden ser irreversibles.
- No actualizar `AppVersionInfo` ni publicar release hasta la prueba final.
- Usar carpetas temporales de prueba y datos ficticios para validar operaciones destructivas.

## Primera implementación acordada

**Empezar por BUILD 7A**, incluyendo el esqueleto del nuevo explorador y la navegación de solo lectura. Mantener Buscar visible para poder comparar ambas experiencias durante las pruebas. Después incorporar las operaciones de archivos.
