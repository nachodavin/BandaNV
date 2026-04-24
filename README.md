# 📂 BandaNV - Organizador de Archivos
**BandaNV** es la solución definitiva para controlar el caos de tus carpetas. Este sistema separa los archivos por tipo, los mueve a una carpeta madre organizada y genera reportes detallados de cada movimiento.

## 🛠️ Setup e Instalación

### 1. Ubicar los archivos
Vas a necesitar guardar dos archivos en tu sistema:

* **El archivo main (`bandanv.ps1`):** Guardá este archivo en una carpeta segura que no vayas a mover (por ejemplo, una carpeta de herramientas portables).
* **El ejecutable (`bandanv.bat`):** Guardá este archivo donde te quede más cómodo ejecutarlo. Por ejemplo, directamente en la carpeta donde actuará el script (como tu carpeta de *Descargas*).

### 2. Configurar las rutas
Si cambiaste los archivos de lugar, tenés que actualizar las direcciones para que se encuentren entre ellos:

#### **En el archivo `bandanv.ps1`:**
Abrilo con el Bloc de notas y fijate en las primeras líneas. Cambiá estas rutas por las tuyas:

```powershell
$global:source = "C:\RUTA_A_TU_CARPETA"  # CARPETA QUE SE VA A ORDENAR (idealmente Descargas)
$global:logDir = "C:\RUTA_A_TU_CARPETA"     # CARPETA DONDE VA A ESTAR EL SCRIPT GUARDADO
```

#### **En el archivo `bandanv.bat`:**
Hacé click derecho, dale a **Editar** y actualizá la ruta de la línea del medio para que apunte exactamente a donde está el archivo `.ps1`:

```cmd
powershell.exe -ExecutionPolicy Bypass -File "C:\RUTA_DONDE_ESTA\bandanv.ps1"
```

### 3. Agregado de extensión nueva al script
Si querés agregar alguna extensión de archivo que no esté incluida de base, podés buscar en el archivo `.ps1` el apartado **"Diccionario"** y agregar lo que quieras siguiendo la lógica del código:

```powershell
"NombreCarpeta" = @(".extension1", ".extension2")
```

---

## 🚀 Cómo se utiliza
1. Una vez ubicado y editado los archivos, buscá tu archivo **`bandanv.bat`** y ejecutalo.
2. Se abrirá la consola con el diseño de **BandaNV** y procesará los archivos.
3. Para comprobar si funcionó, se va a crear un archivo **Log** (`bandanv_log.txt`) donde vas a ver:
    * El registro de cuándo inicia y termina el script.
    * La ruta exacta hacia donde se reubicó cada archivo individualmente.
    * **Nota:** El log está invertido; la ejecución más reciente siempre aparece arriba de todo.

---
*Desarrollado por **Nacho Davin**.*