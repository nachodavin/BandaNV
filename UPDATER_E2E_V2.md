# Prueba integrada de NVupdate (v2.0 → v2.0.1)

**Objetivo:** comprobar el popup real, la consulta GitHub, descarga, SHA-256, reemplazo de archivos, reinicio y handshake de la aplicación sin publicar aún la Release oficial de v2.0.

## Seguridad

- El paquete **tester base** se ejecuta desde `dist-updater-e2e/TesterInstall/BandaNV`; jamás sobre la instalación principal.
- Las builds de ensayo tienen su propia identidad de instancia de Windows. La normal sigue usando `BandaNV.MainInstance`.
- Sólo la build base de prueba acepta `--test-updates`. Sin ese argumento, incluso el tester consulta únicamente la última release estable. El canal de prueba NO se compila en la aplicación oficial ni en la build destino.
- La build destino v2.0.1 lleva un identificador propio y confirma el reinicio con la versión correcta.
- Los paquetes no contienen los directorios portables `config`, `history` ni `logs`. El empaquetador comprueba esto.
- **No ejecutar en una carpeta de BandaNV que contenga datos importantes**. Usar el tester aislado. No publicar todavía la v2.0 oficial.

## Paso 1 — Generar los dos paquetes en Windows

En PowerShell, desde la raíz del repositorio:

```powershell
git pull origin v2.0
powershell -ExecutionPolicy Bypass -File .\Prepare_Updater_E2E_V2.ps1
```

Se generan:

- `dist-updater-e2e/BandaNV_UpdaterTest_Base_v2.0.zip`: sólo para el tester local; **NO subir a GitHub**.
- `dist-updater-e2e/BandaNV_v2.0.1.zip`: paquete de prueba para subir como prerelease.
- `dist-updater-e2e/TesterInstall/BandaNV/BandaNV.exe`: ejecutable listo para probar.

También se imprimen los SHA-256. El script conserva una instalación de prueba ya existente y no la sobrescribe.

## Paso 2 — Publicar sólo la prerelease

En [GitHub Releases](https://github.com/nachodavin/BandaNV/releases/new):

1. Seleccionar o crear tag **`v2.0.1`** (debe apuntar al commit de esta prueba en rama `v2.0`).
2. Poner título **`BandaNV v2.0.1 — Prueba del actualizador`**.
3. Adjuntar exactamente **`BandaNV_v2.0.1.zip`**.
4. Marcar **Set as a pre-release / prerelease**. **No** marcarla como última release estable.
5. Publicar la prerelease y esperar a que GitHub complete el asset. Verificar mediante API que se muestra un `digest` `sha256:...` que coincide con el SHA-256 impreso por el script.

El updater real requiere el digest oficial de GitHub: si todavía no aparece, mostrará que falta la verificación SHA-256 y no instalará nada.

## Paso 3 — Lanzar el tester

```powershell
powershell -ExecutionPolicy Bypass -File .\Start_Updater_E2E_V2.ps1
```

Aparecerá la ventana **BandaNV — TESTER v2.0**. En Configuración → Avanzado → Buscar actualizaciones (o el aviso inicial, si auto-update está activo) se debería ofrecer **v2.0.1**. Confirmar la instalación, esperar la descarga y el reinicio.

Después del reinicio, comprobar:

- El título debe ser **BandaNV — TESTER v2.0.1**.
- En Configuración → Acerca de debe figurar **2.0.1 · TESTER**.
- Los archivos de prueba que se creen dentro de `config`, `logs` y `history` permanecen.
- No se modificó ninguna otra copia de BandaNV.

No mezclar archivos reales del usuario con el tester. El test de rollback aislado sigue disponible mediante `Test_Updater_V2.ps1` y ya está separado de esta prueba.

## Si no aparece la actualización

- Verificar que la release sea `v2.0.1`, `prerelease=true` y que el asset se llame exactamente `BandaNV_v2.0.1.zip`.
- Verificar el digest SHA-256 del asset.
- Cerrar el tester y lanzarlo **con el script**, no haciendo doble clic a `BandaNV.exe`, para pasar `--test-updates`.
- Si falló un intento anterior, inspeccionar `TesterInstall/BandaNV/config/bandanv_update_state.json` antes de reintentar. Sólo corresponde eliminar ese registro en el tester después de investigar el fallo.

La publicación de esta prerelease es manual. **No se publicó ninguna release desde este cambio del código**.
