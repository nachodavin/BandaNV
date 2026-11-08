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

### BUILD 7A.2 — Barra de comandos estilo Windows 11

**Estado:** implementada en la rama experimental; pendiente de compilar y comprobar en Windows.

- [x] Barra fija debajo de la ruta con los comandos Nuevo, Cortar, Copiar, Pegar, Renombrar, Compartir, Eliminar, Organizar, Ordenar, Ver, Más y panel Detalles.
- [x] Mantener posiciones e íconos al seleccionar y deseleccionar elementos; controles deshabilitados se presentan atenuados.
- [x] Copiar y Cortar archivos/carpetas reales al portapapeles de Windows, interoperable con otras aplicaciones; atajos Ctrl+C y Ctrl+X cuando la lista tiene el foco.
- [x] Copiar ruta y Abrir desde el menú «Más»; Ordenar y alternar panel de Detalles siguen funcionando.
- [x] Evitar habilitar Cortar para unidades o rutas protegidas por BandaNV.
- [ ] Habilitar Nuevo, Pegar, Renombrar, Eliminar, Compartir y «Organizar selección» **solo después de implementar y probar sus operaciones**. Permanecen visibles y atenuados por ahora.
- [ ] Validación visual y compilación en Windows: estados sin selección / uno / múltiples / unidad, atajos y portapapeles.

**Regla de diseño:** ningún botón cambia de lugar ni desaparece por la selección. Los controles no operativos nunca deben aparentar estar activos.

### BUILD 7A.3 — Submenús del Explorador (referencia Windows 11)

**Estado:** integrado en rama experimental; pendiente de compilación y validación visual en Windows.

- [x] **Ordenar:** Nombre, Fecha de modificación, Tipo y Tamaño; Ascendente/Descendente.
- [x] **Agrupar por:** Nombre, Fecha de modificación, Tipo, Tamaño o Ninguno; encabezados de grupo reales en el listado. Inicialmente se ordena y agrupa por fecha de modificación, descendente.
- [x] **Ver:** modo Detalles, panel de detalles y panel de vista previa; alternar panel de navegación, vista compacta, casillas de selección, extensiones de nombre y elementos ocultos.
- [x] **Más:** Seleccionar todo (también Ctrl+A), Deseleccionar e Invertir selección; Propiedades mediante Windows; Opciones de BandaNV.
- [x] La navegación y el filtro local siguen sin modificar archivos; mostrar ocultos excluye archivos de sistema por seguridad y conserva el filtro al actualizar.
- [x] Pruebas adicionales del motor: ocultos, extensiones y densidad de filas.
- [ ] Validar interacción y posición de submenús anidados en WinUI, agrupaciones con carpetas extensas, estados visuales de las casillas y funcionamiento del diálogo de Propiedades.
- [ ] **Pendiente:** modos de iconos y mosaicos, agrupación por metadatos extendidos (fecha de creación, autores, categorías, etiquetas, título), anclado a Acceso rápido y funcionamiento completo de la vista previa en pantalla completa. No habilitar opciones sin implementación.

Los controles no implementados se muestran deshabilitados. La versión estable v2.0.3 no se modifica.

### BUILD 7A.4 — Ajustes de la barra y columnas

**Estado:** integrado en rama experimental; pendiente de compilar y comprobar en Windows.

- [x] **Detalles** queda fijo a la derecha de la barra de comandos, como en Windows 11, sin desplazarse con Ordenar, Ver y Más.
- [x] **Nuevo, Cortar, Copiar, Pegar, Renombrar, Compartir, Eliminar, Organizar, Ordenar, Ver y Más** siguen agrupados a la izquierda.
- [x] Agregar zonas de arrastre entre las columnas **Nombre, Modificación, Tipo y Tamaño**.
- [x] Sincronizar anchos de cabecera y filas (incluso con virtualización, agrupación y selección múltiple) mediante `ExplorerColumnLayout`.
- [x] Respetar mínimos de ancho y permitir ampliar una columna usando el espacio disponible o reduciendo columnas vecinas sin ocultarlas.
- [ ] Comprobar en Windows el redimensionado de las cuatro columnas, la alineación de datos y el estado de los separadores en las vistas agrupada y sin agrupar.
- [ ] Evaluar persistencia portable de anchos por usuario en una BUILD posterior.

El diseño conserva el panel de detalles a la derecha, sin modificar las funciones del motor.

### BUILD 7A.5 — Panel Detalles redimensionable y cursor de ajuste

**Estado:** integrado en la rama experimental; pendiente de compilación y prueba visual en Windows.

- [x] El borde izquierdo de **Detalles** permite modificar su ancho arrastrando horizontalmente.
- [x] Ancho de Detalles limitado entre 220 y 800 píxeles, respetando un mínimo de espacio para la lista.
- [x] Al ocultar y volver a mostrar el panel se conserva su último ancho durante la sesión.
- [x] Los cuatro bordes entre columnas tienen una zona de agarre mayor (18 píxeles).
- [x] Cursor horizontal de doble flecha de Windows (`SizeWestEast`) en los cinco separadores.
- [x] Guía vertical discreta en reposo, que cambia al color de acento de BandaNV al pasar el mouse o arrastrar.
- [x] Uso de `Thumb` estándar encapsulado en `ExplorerResizeZone`, ya que `Thumb` es una clase sellada en WinUI.
- [ ] Comprobar en Windows el cursor, las zonas de agarre y la fluidez de redimensionar el panel y las cuatro columnas.

### BUILD 7A.6 — Detalles y barra de estado fieles a Windows 11

**Estado:** integrado en `feature/explorador-completo`; pendiente de compilar y revisar visualmente en Windows.

- [x] Vista grande del archivo: miniatura independiente de **256 px**, en modo `SingleItem` de Windows y caché separada de la versión de 40 px del listado, para evitar ampliar iconos pequeños.
- [x] La franja de vista previa se amplía a la anchura completa del panel, con altura fija y sin márgenes sobrantes. La vista previa compatible de imágenes, PDF, SVG y TXT continúa funcionando.
- [x] Reorganizar **Detalles**: imagen grande arriba, nombre destacado, acción Compartir visible pero deshabilitada hasta la integración nativa, sección de metadatos y botón Propiedades.
- [x] Metadatos de **Tipo, Tamaño, Ubicación y Modificado** en dos columnas con valores alineados a la derecha. Ubicación muestra la carpeta contenedora y conserva la ruta completa al pasar el mouse.
- [x] El tipo de archivo se obtiene de la asociación nativa de Windows cuando está disponible, sin bloquear la selección.
- [x] Barra de estado inferior con tres datos juntos a la izquierda: **total de elementos | elementos seleccionados | tamaño de archivos seleccionados**.
- [x] Si se seleccionan carpetas, el tamaño no se calcula recursivamente; en selección mixta se identifica que el valor corresponde a los archivos.
- [x] Conservar un área discreta a la derecha de la barra de estado para mensajes de operaciones o errores.
- [ ] Validación real en Windows: nitidez con ZIP/RAR y asociaciones instaladas, vista previa de PDF/imagen, metadatos y selección múltiple.
- [ ] **Limitación externa:** Windows puede devolver una miniatura inferior al tamaño solicitado si el programa asociado no ofrece una fuente de alta resolución; no se puede garantizar nitidez absoluta para todos los formatos.

### BUILD 7A.7 — Selección por clic y atajos sin globos

**Estado:** implementado en la rama experimental; pendiente de prueba en Windows.

- [x] Ocultar el globo automático `Ctrl+C` (y equivalentes) del listado usando `KeyboardAcceleratorPlacementMode="Hidden"`, sin eliminar los atajos `Ctrl+C`, `Ctrl+X` y `Ctrl+A`.
- [x] Clic izquierdo en el fondo vacío del listado: deseleccionar archivos.
- [x] Clic izquierdo a la derecha de la última columna, incluso si la fila ocupa todo el ancho del panel: deseleccionar, como en Windows.
- [x] Mantener selección habitual al pulsar dentro de la fila y preservar Ctrl+clic, Shift+clic y clic derecho.
- [x] No interpretar el uso de la barra de desplazamiento como clic en una zona vacía.
- [x] Usar los anchos actuales de las columnas para identificar el espacio libre cuando el usuario las redimensiona.
- [ ] Verificar con clics en filas, encabezados de grupo, zona vacía inferior, zona vacía a la derecha, Ctrl+clic, Shift+clic y scrollbar en el equipo de desarrollo.

### BUILD 7A.8 — Zona seleccionable limitada y espacios muertos

**Estado:** integrado en la rama experimental; pendiente de compilar y validar visualmente en Windows.

- [x] Limitar el resaltado de cada archivo hasta el borde de la última columna (**Tamaño**), sin extenderlo hasta el panel Detalles.
- [x] El encabezado y las filas usan un ancho común `RowWidth`, calculado a partir de Nombre, Modificación, Tipo, Tamaño, separaciones y padding.
- [x] Pasar el ancho inicial de Nombre a píxeles (antes era `*`), para conservar un espacio libre real a la derecha de la tabla.
- [x] Crear un margen muerto de 12 px a la izquierda y 4 px de separación entre filas, parecido al Explorador de Windows.
- [x] Fondo del listado transparente y detección de clic fuera de filas para quitar selección, incluso en el espacio libre a la derecha, debajo de las filas y entre grupos.
- [x] Preservar clics dentro de la fila, checkbox, clic derecho, Ctrl+clic, Shift+clic y barra de desplazamiento.
- [x] Continuar sincronizando el ancho de encabezados y filas al redimensionar las columnas.
- [ ] Verificar en WinUI real los límites de resaltado, la alineación de encabezados, los márgenes clickeables y la selección agrupada y sin agrupar.

### BUILD 7A.9 — Carga estable de íconos al entrar en Explorar

**Estado:** implementada en la rama experimental; pendiente de compilación y prueba en Windows.

- [x] Unificar la carga de íconos para formatos con asociaciones compartidas (ZIP, RAR y 7z), evitando que cada fila consulte Windows por separado.
- [x] Compartir una misma tarea en curso por extensión y resolución (lista de 40 px / Detalles de 256 px).
- [x] Mantener la tarea activa aunque el elemento de la lista se recicle mientras se carga, mostrando el resultado únicamente si coincide la ruta y versión de la fila.
- [x] Precargar asociaciones al ingresar a una carpeta, sin bloquear la navegación.
- [x] Limitar a seis operaciones de íconos simultáneas para no sobrecargar el proveedor de miniaturas de Windows.
- [x] Si la primera muestra falla, probar hasta tres archivos del mismo tipo sin guardar fallos en caché; notificar a las filas visibles cuando se recupera el ícono.
- [x] Preservar el modo de alta resolución independiente para el panel Detalles.
- [ ] Verificar en Windows: abrir Explorar desde cero, navegar por una carpeta con muchos ZIP/RAR/7z, desplazar y volver al inicio, comprobar que todos los archivos de un mismo tipo utilicen su ícono asociado.

### BUILD 7A.10 — Ventana de escritorio en lugar de maximizada

**Estado:** implementado en `feature/explorador-completo`; pendiente de compilar y probar en Windows.

- [x] Retirar la llamada a `OverlappedPresenter.Maximize()` que ampliaba BandaNV al iniciarse.
- [x] Iniciar una ventana estándar, centrada en el área de trabajo del monitor y sin tapar la barra de tareas.
- [x] Tamaño inicial moderado, aproximadamente el 60% del ancho y 62% del alto del monitor, con límites mínimos y máximos adaptados a la escala DPI.
- [x] Conservar minimizar, maximizar, restaurar, mover y redimensionar mediante controles de Windows.
- [x] Ajustar en Explorar la anchura del panel de ubicaciones, de la búsqueda y de Detalles cuando la ventana sea angosta.
- [x] Ajustar de forma proporcional las columnas Nombre/Modificación/Tipo/Tamaño al ancho visible, sin perder la posibilidad de redimensionarlas manualmente.
- [x] Al ampliar la ventana, recuperar proporciones de columnas predeterminadas si el usuario no las modificó; no pisar los anchos elegidos a mano.
- [ ] Prueba real con monitor 1080p/1440p y distintas escalas DPI; navegar entre secciones, maximizar/restaurar y mover entre monitores.
- [ ] Futuro: recordar el último tamaño y posición en preferencias portables, si se desea.

La versión estable 2.0.3 en `main` permanece intacta.

### BUILD 7A.11 — Adaptación del resto de secciones al modo ventana

**Estado:** implementado en `feature/explorador-completo`; pendiente de compilar y comprobar visualmente en Windows.

- [x] **Inicio:** el gráfico de categorías se escala sin cortarse; la leyenda queda debajo y las métricas se apilan cuando falta ancho.
- [x] **Organizar:** el contenido principal puede desplazarse verticalmente; la barra de filtros/orden/agrupación pasa a dos líneas y Resumen puede ubicarse debajo del listado en una ventana angosta.
- [x] **Buscar:** el carrusel ajusta la cantidad de categorías visibles por tanda al ancho y preserva la selección de categorías; la tabla y Detalle se apilan si no hay espacio para ambas.
- [x] **Historial:** el panel Detalle puede colocarse debajo de la tabla para que sus siete columnas tengan espacio suficiente.
- [x] **Categorías:** el área principal permite desplazamiento, manteniendo el GridView y la reubicación de tarjetas accesibles.
- [x] **Configuración:** las parejas de tarjetas de General, Apariencia, Organización, Historial y seguridad y Avanzado se apilan en ventanas normales; las pestañas siguen desplazándose horizontalmente.
- [x] El contenido de las páginas complejas cuenta con desplazamiento vertical y horizontal de respaldo para ventanas especialmente reducidas, sin recortar botones u opciones.
- [ ] Validar tamaños aprox. 1100×650, 1400×800 y pantalla maximizada; comprobar navegación, selección, filtros, drag de categorías, modales y operaciones de Organizar.
- [ ] Revisar si alguna pantalla requiere ajustes adicionales de densidad de texto en ventanas extremadamente pequeñas.

Este cambio no modifica el motor, la lógica de organización, el historial ni los ajustes portables. La rama estable sigue intacta.

### BUILD 7A.12 — Estabilidad de páginas y corrección del gráfico de Inicio

**Estado:** corrección aplicada en la rama experimental; pendiente de compilar y validar en Windows.

- [x] **Prioridad estabilidad:** revertir las modificaciones de layout de Historial y Categorías introducidas en la BUILD 7A.11, restaurando exactamente su XAML estable. Historial recupera también su código anterior al controlador de SizeChanged.
- [x] Mantener los datos, acciones, filtros, gestión de categorías y el motor sin cambios.
- [x] Reducir la altura mínima del gráfico de Inicio y calcular su escala según el alto disponible de la ventana, para evitar el recorte al abrir en modo ventana.
- [ ] Confirmar con la compilación real que Historial y Categorías abren sin cerrar la aplicación.
- [ ] Revisar en Windows que el gráfico de Inicio sea completamente visible y que las demás páginas sigan funcionando.
- [ ] Volver a planear la adaptación responsive de Historial y Categorías de forma aislada y con pruebas de navegación, evitando el ScrollViewer anidado que causó la regresión.

**Nota:** los cambios a Historial y Categorías de la BUILD 7A.11 quedan temporalmente revertidos para priorizar la estabilidad. No asumir que la excepción de WinUI quedó confirmada hasta disponer de logs o prueba local.

### BUILD 7A.13 — Inicio legible, Historial y Categorías sin cortes

**Estado:** implementado en rama experimental; pendiente de compilar y verificar en Windows.

- [x] **Inicio:** dona a la izquierda y leyendas a la derecha dentro de la misma tarjeta, con dimensiones para que las cifras centrales no se vuelvan diminutas.
- [x] Las leyendas pasan a dos líneas por categoría (nombre y cantidad), y la lista de leyendas tiene desplazamiento vertical independiente para mostrar todas sin que queden debajo o cortadas.
- [x] El tamaño de la dona depende del ancho disponible, no de la altura total de la ventana.
- [x] **Historial:** se quita el mínimo de altura que sobrepasaba la ventana; el detalle tiene desplazamiento interno, mientras el título y el botón «Deshacer ejecución» se mantienen fijos y accesibles.
- [x] La tabla de Historial reduce separaciones de columnas y abrevia dos encabezados con tooltips para aprovechar mejor el ancho.
- [x] **Categorías:** el GridView conserva su virtualización y la funcionalidad de arrastrar tarjetas, pero se adapta al espacio vertical disponible y habilita desplazamiento interno para alcanzar las tarjetas inferiores.
- [x] No se introduce un ScrollViewer alrededor de toda la página ni un enlace circular a ActualHeight; así se evitan los cambios de diseño que hicieron fallar Historial y Categorías en BUILD 7A.11.
- [ ] Validar compilación y funcionamiento real: Inicio con todos los nombres legibles; Historial con y sin registros, scroll del detalle y acción Deshacer; Categorías con 8+ tarjetas, scroll y reordenamiento.

Este trabajo no cambia el motor ni los registros de los usuarios. `main` continúa sin cambios.

### BUILD 7A.14 — Cierre automático de submenús al elegir una opción

**Estado:** implementado en rama experimental, pendiente de prueba en Windows.

- [x] Al escoger una opción final en **Ordenar**, **Más**, **Agrupar por**, **Ver** y **Mostrar**, cerrar tanto el submenú como el menú raíz.
- [x] Las opciones de **Más opciones** (seleccionar todo, quitar/invertir selección, propiedades, configuración, abrir y copiar ruta) cierran el menú al ejecutarse.
- [x] Conservar abiertos los menús intermedios cuando el usuario solamente despliega **Más**, **Agrupar por** o **Mostrar**.
- [x] Reutilizar un único método de cierre para todos los manejadores, sin alterar filtros, agrupación, visualización ni selección.
- [ ] Comprobar en Windows especialmente `Ordenar → Agrupar por → (Ninguno)`, `Ver → Mostrar → Extensiones de nombre` y el menú de tres puntos.

### BUILD 7A.15 — Barra de direcciones y búsqueda al estilo Windows

**Estado:** implementado en `feature/explorador-completo`; pendiente de compilar y probar en Windows.

- [x] Eliminar el botón independiente **Ruta** de la barra superior y liberar su columna para la dirección.
- [x] Un clic izquierdo en un espacio vacío de la barra de ruta activa el editor, seleccionando la ruta completa para escribir o pegar otra dirección.
- [x] Los botones de carpetas en la ruta conservan su navegación, sin activar el editor.
- [x] `Enter` navega a la ruta ingresada y `Esc` cancela; al perder foco se vuelve a mostrar la ruta navegable.
- [x] Actualizar el placeholder del buscador según la carpeta abierta, por ejemplo **Buscar en 1 - RAR**, **Buscar en Imágenes** o **Buscar en Este equipo**.
- [x] Conservar el filtro local existente sin introducir búsquedas en toda la computadora.
- [ ] Comprobar en Windows clics en espacios libres de la ruta, selección de texto, pegar una ruta válida/inválida, navegación mediante migas y actualización de búsqueda al cambiar de carpeta.

### BUILD 7A.16 — Cajas desenfocables, panel Detalles limpio y tipos de Windows

**Estado:** integrado en `feature/explorador-completo`; pendiente de compilación y comprobación visual en Windows.

- [x] Un clic izquierdo fuera de los campos de **ruta** y **Buscar en…** quita el foco y la selección de texto; la ruta vuelve a las migas de navegación.
- [x] La búsqueda escrita **no se borra** al desenfocar: continúa filtrando la carpeta actual.
- [x] Los clics dentro de los campos no interrumpen la edición y los botones de migas siguen navegando normalmente.
- [x] Quitar el tooltip instructivo que aparecía al posar el mouse sobre la barra de ruta.
- [x] Eliminar el botón **Compartir** deshabilitado del panel de Detalles, reajustando el espaciado; no eliminar otras acciones de la barra.
- [x] Obtener el tipo descriptivo de cada extensión mediante `SHGetFileInfoW` (`SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES`), respetando las asociaciones instaladas y el idioma del sistema.
- [x] Mantener **Carpeta** para directorios y **Unidad** para discos; usar solo la extensión si Windows no proporciona un nombre de tipo.
- [x] Compartir el mismo nombre de tipo entre listado, agrupación/ordenamiento y panel de Detalles; guardar los resultados por extensión para evitar consultas repetidas.
- [ ] Probar que el campo Buscar pierde foco al pulsar espacios vacíos sin perder su texto; y que la ruta vuelve a migas tras hacer clic fuera.
- [ ] Comprobar los tipos de ZIP/RAR con WinRAR instalado, otros formatos, archivos sin extensión y carpetas.

### BUILD 7A.17 — Editor de ruta alineado con el marco original

**Estado:** implementado en la rama experimental; pendiente de compilación y revisión visual en Windows.

- [x] Conservar el borde externo `ExplorerAddressSurface` como **único marco** de la barra de ubicación.
- [x] Eliminar el padding del contenedor externo y asignar el espacio interior directamente a las migas de navegación y al `TextBox` de edición.
- [x] Extender el `TextBox` a todo el espacio disponible, sin margen propio y sin modificar la altura ni la anchura del bloque al entrar en edición.
- [x] Anular el borde enfocado interno y su línea inferior de acento mediante recursos locales de WinUI; mantener la selección y el foco del texto.
- [x] Conservar clic en el espacio vacío, `Enter`, `Esc` y clic fuera como antes.
- [ ] Validar visualmente en Windows que navegar y editar la ruta compartan **exactamente el mismo rectángulo** sin doble contorno ni salto de posición.

### BUILD 7A.18 — Centrado real del editor de ruta

**Estado:** ajuste implementado en rama experimental, pendiente de inspección visual en Windows.

- [x] Corregir la alineación superior que persiste en WinUI 3 a pesar de `VerticalContentAlignment="Center"`.
- [x] Mantener la plantilla nativa del TextBox, incluida selección de texto, portapapeles y navegación por teclado.
- [x] Centrar el **control compacto** dentro del marco de la ruta y compensar la posición interna del texto con padding superior, sin crear un borde adicional.
- [x] Mantener intacto el tamaño de la barra externa y los eventos Enter/Esc/clic fuera.
- [ ] Validar que la selección del texto quede centrada a la misma altura que las migas y que el botón de limpiar no se corte.

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
