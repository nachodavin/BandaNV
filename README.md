# BandaNV v2.0

**BandaNV** es una aplicación portable para organizar archivos y carpetas en Windows con categorías configurables, búsqueda, historial y opciones de recuperación.

La versión 2.0 incorpora una interfaz renovada en **WinUI 3**, conserva los datos del usuario junto a la aplicación y cuenta con un sistema de actualización integrado.

## Descargar e instalar

1. Entrá en [Releases](https://github.com/nachodavin/BandaNV/releases) y elegí la **release estable** más reciente.
2. Descargá el archivo ZIP correspondiente.
3. Extraé por completo la carpeta `BandaNV` en una ubicación donde tengas permisos de escritura.
4. Ejecutá `BandaNV.exe`. No requiere instalador.

**Requisitos:** Windows de 64 bits compatible con Windows App SDK 2.5.1. El paquete oficial es `win-x64`, self-contained y portable.

> Evitá extraer BandaNV dentro de `Program Files` o carpetas del sistema protegidas: la app guarda ajustes, historial y logs en su propia carpeta.

## Funciones principales

- **Inicio:** métricas de archivos pendientes, archivos sin asignar y actividad.
- **Organizar:** análisis y vista previa antes de mover archivos. Puede tratar carpetas completas como unidades y preservar su estructura.
- **Buscar:** exploración de archivos organizados, filtros, ordenación, agrupación, sublistados y acciones.
- **Historial:** registros de operaciones y acciones reversibles cuando corresponda.
- **Categorías:** creación, edición, duplicación, extensiones asignadas y reordenamiento de cards; contadores recursivos de archivos.
- **Configuración:** personalización visual, comportamiento de organización, protección de carpetas y copias de seguridad `.bandanv`.

### Datos portables

La aplicación crea y usa las siguientes carpetas junto al ejecutable:

- `config/`: preferencias y configuración de BandaNV.
- `logs/`: registros.
- `history/`: historial y datos necesarios para operaciones reversibles.

El actualizador conserva estas carpetas y comprueba la integridad SHA-256 del ZIP publicado en GitHub antes de iniciar la instalación. Si la versión nueva no confirma un inicio correcto, `NVupdate` intenta restaurar la instalación anterior.

**Importante:** una copia de seguridad `.bandanv` contiene configuraciones y categorías. No incluye tus archivos personales ni reemplaza un respaldo de tus datos.

## Desarrollo y pruebas

Tecnologías: C#, .NET 10, WinUI 3 y Windows App SDK 2.5.1.

Desde PowerShell, en la raíz del repositorio:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build_V2.ps1
powershell -ExecutionPolicy Bypass -File .\Test_Motor_V2.ps1
powershell -ExecutionPolicy Bypass -File .\Test_Updater_V2.ps1
```

La primera versión oficial se genera con:

```powershell
powershell -ExecutionPolicy Bypass -File .\Package_V2_Release.ps1
```

El empaquetador ejecuta los smoke tests y valida la estructura del ZIP antes de indicar el SHA-256. **No publica automáticamente la release.**

Para conocer los cambios de esta versión, consultá [las notas de BandaNV v2.0](RELEASE_NOTES_v2.0.md).
