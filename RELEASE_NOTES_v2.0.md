# BandaNV v2.0 — Notas oficiales de versión

**BandaNV 2.0 es una renovación integral del organizador de archivos para Windows.** La aplicación fue reconstruida con una interfaz moderna, un motor de organización más seguro, búsqueda y gestión de archivos reales, historial con operaciones reversibles y un sistema propio de actualizaciones.

La versión mantiene la idea original de BandaNV: **organizar tus archivos por categorías sin perder el control sobre lo que se mueve, modifica o elimina**.

## Principales novedades

- **Interfaz completamente rediseñada** en WinUI 3, con seis secciones integradas y personalización visual.
- **Nuevo motor de organización** con análisis previo, carpetas completas, resolución de conflictos y recuperación de operaciones interrumpidas.
- **Buscar y Organizar más completos**, con selección múltiple, filtros, ordenación, agrupación y navegación por carpetas y subcarpetas.
- **Historial y Deshacer seguros**, con registros de operaciones y seguimiento técnico independiente del historial visible.
- **Categorías personalizables** con colores, extensiones, reordenamiento por arrastre y contadores reales de archivos.
- **Protección de carpetas, backups de configuración y actualizaciones automáticas verificadas**.
- **Distribución 100 % portable** para Windows x64, sin instalador ni dependencia de AppData para los datos de BandaNV.

---

## 1. Nueva interfaz y experiencia de uso

BandaNV deja atrás la interfaz de la versión anterior y adopta un entorno de escritorio basado en **C#, .NET 10 y WinUI 3**.

- Navegación principal unificada: **Inicio, Organizar, Buscar, Historial, Categorías y Configuración**.
- Ventana principal redimensionable, con diseño que aprovecha el espacio disponible.
- Adaptación del contenido al tamaño de la ventana y a los cambios de resolución o escala entre monitores.
- Popups, confirmaciones, menús y selectores con una estética consistente dentro de BandaNV.
- Interacciones más familiares: selección múltiple, acciones contextuales, estados de selección y navegación integrada.
- Uso de **Enter** y **Escape** en los diálogos correspondientes para confirmar o cerrar.
- Nueva jerarquía visual, mayor legibilidad de textos y estados más claros durante las operaciones.
- Posibilidad de usar temas **Oscuro, Claro o Sistema**, además de colores personalizados.

## 2. Inicio: un panel de control real

La sección **Inicio** muestra información actual de la organización, sin depender de valores de demostración.

- **Archivos pendientes** en la ubicación de origen.
- **Archivos sin asignar**, con advertencia visual amarilla cuando hay archivos que requieren atención.
- **Total de ejecuciones** registradas.
- **Archivos organizados** actualmente.
- **Tamaño total** ocupado por los archivos organizados.
- **Categorías en uso** y fecha de la última organización.
- Gráfico de dona **Uso por categoría**, con distribución de los archivos entre las categorías.
- Actualización de las métricas ante cambios relevantes en archivos, categorías o ejecuciones.

## 3. Organizar: análisis, control y movimientos seguros

La sección **Organizar** se conecta con un motor real de análisis y ejecución.

### Vista previa y clasificación

- Análisis de los elementos pendientes **sin moverlos durante la exploración**.
- Vista previa con nombre, categoría, tamaño, modificación, destino y resumen general.
- Asignación rápida de extensiones desconocidas a categorías existentes o nuevas.
- Resolución y reversión de asignaciones antes de confirmar la organización.
- Tratamiento configurable de extensiones desconocidas: **preguntar en la vista previa, dejar en origen o mover a OTROS**.
- Filtros por fecha, tamaño y extensiones, más opciones de **ordenar** y **agrupar**.
- Selección múltiple y panel de información contextual.
- Reanálisis de cambios externos para evitar trabajar con resultados desactualizados.

### Carpetas completas y subcarpetas

- Opción **Detectar carpetas completas**: cada carpeta puede organizarse como una sola unidad, conservando su estructura interna.
- Análisis del contenido de carpetas anidadas, incluyendo sus archivos y subcarpetas.
- **Asignación manual de carpetas** antes de moverlas, para evitar decisiones automáticas incorrectas.
- Carpetas con contenido mixto, desconocido o no resuelto permanecen **sin asignar** hasta que se revisen.
- Detección de carpetas y subcarpetas vacías, incorporadas a las comprobaciones de seguridad.
- Navegación por el contenido de carpetas desde el propio listado y panel de detalle.

### Conflictos y ejecución

- Opciones configurables cuando ya existe un nombre en destino: **Preguntar, Renombrar automáticamente, Omitir elemento o Reemplazar**.
- Resolución de conflictos entre archivos y carpetas, incluidos elementos de distinto tipo que comparten nombre.
- Revalidación de los elementos al ejecutar para detectar renombres, cambios o nuevos conflictos posteriores al análisis.
- Confirmación de cambios importantes según las preferencias de seguridad.
- Registro técnico de la operación para permitir la recuperación y el deshacer cuando sea seguro.
- Acciones contextuales sobre elementos del origen, como abrir, copiar ruta, cambiar categoría, renombrar o eliminar, según corresponda.

## 4. Buscar: explorador y gestor de archivos organizados

La sección **Buscar** pasa a trabajar con los archivos y carpetas reales que existen dentro del destino configurado.

- Vista de categorías con cards y sus extensiones configuradas.
- **Contadores recursivos de archivos físicos**: cuentan todos los archivos individuales, incluso dentro de carpetas y subcarpetas, sin sumar las carpetas como archivos.
- Búsqueda y filtros por categorías, extensiones, tamaño y fechas de modificación.
- Ordenación ascendente o descendente por **nombre, fecha de modificación, tamaño, categoría o extensión**.
- Agrupación independiente por nombre, fecha, tamaño, categoría o extensión, o sin agrupar.
- Preferencias de filtros, orden y agrupación que se guardan para las próximas sesiones.
- **Selección múltiple estilo Explorador de Windows** y acciones sobre uno o varios elementos.
- Carpetas y subcarpetas navegables en el listado principal, con el contenido disponible también desde el panel lateral.
- Panel de detalle para consultar ubicación, tipo, extensión, tamaño y fecha.
- Acciones como **abrir archivo, abrir ubicación, copiar ruta, cambiar categoría, renombrar y eliminar**, con controles de seguridad.
- Posibilidad de registrar por separado las acciones realizadas desde Buscar en el Historial.

## 5. Historial y Deshacer

El historial se convierte en una sección de consulta y recuperación conectada con las operaciones reales de BandaNV.

- Registro individual de organizaciones y, si están habilitadas las opciones correspondientes, acciones realizadas desde **Organizar** y **Buscar**.
- Registros con fecha y hora, **incluidos los segundos**, para facilitar el orden cronológico.
- Lista de ejecuciones con tipo, origen, destino, elementos, archivos físicos y tamaño.
- Búsqueda por fecha, elemento, categoría, origen o destino.
- Filtros por tipo de operación, disponibilidad de Deshacer y origen.
- Ordenación de ejecuciones por fecha, cantidad de elementos o tamaño.
- Panel integrado con el detalle de los elementos afectados y sus estados.
- **Deshacer** de operaciones reversibles, sujeto a comprobaciones que evitan modificar archivos que ya cambiaron.
- Soporte de reversión de carpetas completas y **Deshacer parcial** cuando solo una parte de los elementos sigue siendo segura.
- Separación entre el **historial visible** y los **registros técnicos necesarios para Deshacer**: desactivar el historial no deshabilita por sí mismo la recuperación técnica.
- Opciones para conservar, limitar y limpiar el historial según las preferencias configuradas.

## 6. Categorías: más control y personalización

La administración de categorías fue rediseñada como una grilla de cards.

- Crear, editar, duplicar y eliminar categorías.
- Gestionar las extensiones reconocidas por cada categoría.
- Asignar un **color propio** a cada categoría, reflejado en diferentes secciones de la aplicación.
- **Reordenar categorías arrastrando las cards**, con respuesta visual durante el movimiento.
- Acceder a sus acciones desde el botón de tres puntos o mediante **clic derecho**.
- Consultar archivos organizados y extensiones asociadas a cada categoría.
- Contar archivos individualmente en toda la estructura de carpetas y subcarpetas.
- Sincronización segura entre cambios de categorías y las carpetas físicas asociadas cuando corresponda.
- Gestión de carpetas de categorías eliminadas: se preserva el contenido y la limpieza de carpetas vacías identificadas como huérfanas es configurable.
- Indicador de **Archivos sin asignar** consistente con Inicio.

La instalación nueva incluye categorías iniciales para archivos comprimidos, instaladores, documentos, imágenes, GIF, videos, audio y editables. Todas se pueden personalizar.

## 7. Configuración: preferencias completas y portables

La configuración se reorganiza en pestañas: **General, Apariencia, Organización, Historial y seguridad, Avanzado y Acerca de**.

### Aplicación y apariencia

- Selección de las carpetas de **origen** y **destino**.
- Elección de la página que se abre al iniciar.
- Opción para cerrar BandaNV o **minimizarlo a la bandeja del sistema**.
- Opción para **iniciar con Windows**.
- Prevención de ventanas duplicadas mediante instancia única.
- Temas visuales **Claro, Oscuro o Sistema**.
- **Color primario** para fondos y superficies y **color secundario** para botones, selección y acciones, con vista previa y restablecimiento.

### Organización, historial y seguridad

- Activar o desactivar la vista previa y la detección de carpetas completas.
- Elegir la creación de carpetas de categoría y la limpieza segura de carpetas de categorías eliminadas.
- Configurar los conflictos, las extensiones sin categoría y las confirmaciones.
- Usar la **Papelera de reciclaje** para eliminaciones cuando sea posible.
- Decidir qué actividad guardar en el historial general, en Organizar y en Buscar.
- Definir una política de conservación de registros.
- Guardar filtros, orden y agrupación de las listas entre sesiones.

### Carpetas protegidas

- Incorporar rutas a una lista de **carpetas protegidas**.
- Impedir que BandaNV las mueva, renombre, reemplace o elimine, así como a su contenido.
- Bloquear también operaciones sobre carpetas superiores cuando pudieran afectar una ubicación protegida.
- Aplicar estas protecciones a **Organizar, Buscar, sincronización de categorías, Deshacer y recuperación de inicio**.

### Copias de seguridad de configuración

- **Exportar e importar** preferencias mediante archivos `.bandanv`.
- Incluir categorías, extensiones, colores, ubicaciones, apariencia, comportamiento y carpetas protegidas.
- Validar integridad, estructura y datos del backup antes de aplicarlo.
- Mostrar una confirmación previa y sincronizar los cambios aplicables de forma controlada.
- Admitir backups anteriores compatibles, incluidos aquellos que no contienen la lista nueva de carpetas protegidas.
- Herramientas avanzadas para restablecer opciones o borrar datos propios de BandaNV con confirmación.

**Importante:** los backups `.bandanv` son de **configuración**. No contienen los archivos personales que organizaste ni sustituyen una copia completa de `logs/` e `history/`.

## 8. Seguridad y recuperación de operaciones

BandaNV 2.0 incorpora comprobaciones para reducir el riesgo de pérdida o modificación inesperada de datos.

- Análisis inicial de solo lectura y revalidación antes de ejecutar movimientos.
- Comprobaciones de cambios de archivos y estructura interna de carpetas.
- Exclusión de destinos dentro de la propia carpeta de origen cuando corresponda.
- Manejo de nombres duplicados sin sobrescritura accidental.
- Protección de rutas restringidas y contenido protegido.
- **Journal de operaciones** y recuperación de ejecuciones interrumpidas.
- Respaldos temporales durante reemplazos que los requieren.
- Deshacer condicionado a la integridad y disponibilidad real de los elementos.

**Nota:** eliminar archivos de forma permanente, reemplazar contenido o modificar elementos externamente puede limitar la posibilidad de deshacer una acción. BandaNV verifica cada caso antes de ofrecerla.

## 9. Sistema propio de actualizaciones

La versión 2.0 incorpora un sistema de actualizaciones integrado con **GitHub Releases**.

- Comprobación de nuevas **releases estables**.
- Aviso de actualización y acceso rápido contextual desde la interfaz cuando hay una versión nueva disponible.
- Búsqueda manual de actualizaciones desde **Configuración > Acerca de**.
- Posibilidad de decidir si se muestra automáticamente el aviso al iniciar.
- Descarga del ZIP oficial y validación de su **SHA-256** contra el digest publicado en GitHub.
- Comprobación del manifiesto y de los archivos requeridos antes de instalar.
- Instalación a través de **NVupdate**, con cierre y reinicio de BandaNV.
- Confirmación de inicio correcto; si falla, el actualizador intenta **restaurar la instalación anterior**.
- Conservación de las carpetas portables del usuario durante el reemplazo.

## 10. Distribución portable y compatibilidad

- Compilación para **Windows x64**, sin instalador.
- Paquete **self-contained** con las dependencias de ejecución necesarias.
- Los datos de BandaNV permanecen junto a la aplicación, principalmente en:
  - `config/` — configuración y preferencias.
  - `logs/` — registros de actividad.
  - `history/` — historial y metadatos de operaciones.
- Persistencia de la configuración con escritura segura y compatibilidad con formatos anteriores contemplados por el motor.
- Lectura de registros históricos compatibles de versiones anteriores.

**Para quienes vienen de v1.0:** hacé una copia de tus datos portables antes de migrar. Si querés conservar una instalación existente, probá inicialmente v2.0 en una carpeta separada. No se presupone una actualización automática directa entre arquitecturas.

## 11. Instalación de BandaNV v2.0

1. Descargá **`BandaNV_v2.0.zip`** desde la release oficial.
2. Extraé completamente la carpeta **`BandaNV`** donde tengas permisos de escritura.
3. Abrí **`BandaNV.exe`**.
4. Elegí la carpeta de origen, la de destino y revisá tus categorías y opciones de seguridad.

No requiere instalador. Para preservar el comportamiento portable, evitá ubicaciones protegidas del sistema como `Program Files`.

## 12. Pruebas y validaciones previas al lanzamiento

- **Motor de organización:** **39 pruebas OK, 0 errores**, incluyendo carpetas completas, conflictos, reanálisis, categorías, contadores recursivos, protecciones, Deshacer y validación de backups.
- **Actualizador independiente:** **3 pruebas OK, 0 errores**, cubriendo instalación, restauración y validación de paquetes.
- **Actualización real de extremo a extremo:** prueba aislada de **TESTER v2.0 → TESTER v2.0.1** completada, con detección, descarga, instalación, reinicio y conservación de datos confirmados.
- **Empaquetado oficial:** compilaciones Release de BandaNV y NVupdate correctas, ZIP `BandaNV_v2.0.zip` generado y validación interna del paquete completada.

Las herramientas y el canal usados exclusivamente en la prueba TESTER se retiraron del código oficial antes del lanzamiento.

---

**BandaNV v2.0** reúne una nueva experiencia visual con un motor más robusto, mayor control sobre los archivos y una base preparada para próximas versiones.

**Desarrollado por Nacho.**
