# LIVE_TESTING: instalar, desplegar y probar en Civil 3D real

## Requisitos de la máquina del dueño
| Requisito | Valor |
|---|---|
| Civil 3D | 2025 en `C:\Program Files\Autodesk\AutoCAD 2025\` (con `C3D\AeccDbMgd.dll` y `ACA\AecBaseMgd.dll`) |
| .NET SDK | 8.0.419 y 10.0.202. El `global.json` del repo pide 10.0.100 con rollForward latestFeature |
| Otros | Python 3.11, git y PowerShell 7 (`pwsh`) |
| CLI de Claude | No está instalada. La app de escritorio registra los MCP en `%APPDATA%\Claude\claude_desktop_config.json` |

## Compilar la herramienta de sondeo (una vez por clon)
```bash
cd tools/Horizun.Civil3D.ApiProbe && dotnet build -c Release -o ../../.tools/apiprobe
../../.tools/apiprobe/hz-c3d-apiprobe.exe --year 2025 --find Surface
../../.tools/apiprobe/hz-c3d-apiprobe.exe --year 2025 --asm acdbmgd.dll Autodesk.AutoCAD.DatabaseServices.Database
```

## Desplegar una versión nueva
1. Corre `dotnet test` y `dotnet build -p:Civil3DYear=2025`; ambos deben quedar en verde.
2. Pídele al dueño **que guarde y cierre Civil 3D**, y espera su confirmación.
   - `acad.exe` puede tardar unos 20 s en salir sin ventana; espéralo con un bucle de `tasklist`.
   - **Nunca lo mates.**
3. Corre `pwsh scripts/install.ps1`. Lo que hace:
   1. se niega si `acad.exe` sigue corriendo;
   2. corre las pruebas;
   3. compila;
   4. publica;
   5. respalda la instalación anterior;
   6. copia los archivos nuevos;
   7. verifica SHA-256;
   8. revierte si algo falla.
4. Si cambió `Contract.cs` (hash nuevo), el dueño debe **reiniciar Claude Desktop**. Si no, basta con reabrir Civil 3D.
5. El dueño abre Civil 3D y escribe `HZ_STATUS`; debe mostrar "publicado". El log está en `%USERPROFILE%\.horizun\civil3d\logs\plugin-<año>-<pid>.log`.

## Probar sin tener las herramientas cargadas en la sesión
Una sesión de IA que empezó antes del registro no ve las herramientas `horizun_c3d_*`. Usa el servidor instalado por stdio, que es la misma vía que usa Claude:
```bash
python scripts/mcp_call.py '[["horizun_c3d_health",{}]]'
python scripts/mcp_call.py '[["horizun_c3d_query",{"action":"list","type":"surface"}],["horizun_c3d_styles",{"action":"list","object_type":"surface"}]]' 6000
```
Las llamadas de una misma invocación son concurrentes, lo que sirve para probar la cola.

## Checklist de verificación en vivo (fase 0, ya superada el 2026-10-01)
Ver `docs/CIVIL3D.md`. Resumen:
1. `health`: año, ACADVER, unidades y sistema de coordenadas.
2. `object_census`: compararlo con el Prospector.
3. `query get` de una superficie: compararlo con Surface Properties.
4. `styles get`: el `used_by` debe ser correcto.
5. "Busy": con `LINE` activo, la respuesta debe ser `busy` y no debe ejecutarse nada. **Falta repetirlo con un diálogo modal abierto.**
6. Guardado, que requiere `full_write`:
   1. dry run, que devuelve el token;
   2. aplicar, con resultado `verified=match`;
   3. reusar el token, que da `already_used`.

## Verificación automática con el dibujo de ensayo (desde v0.3.0)
1. En Civil 3D: `NEW` con plantilla de Civil 3D, luego `HZ_BUILD_FIXTURE`, o el botón "Dibujo de ensayo" en la cinta Horizun Hub. Se niega en dibujos con nombre o con superficies.
2. `python scripts/verify_live.py`. Corre unos 40 pasos contra valores analíticos y escribe el informe en `%USERPROFILE%\.horizun\civil3d\fixtures\`.
3. Paso manual: un `UNDO` en Civil 3D debe revertir la última escritura como un solo paso.
4. El perfil `safe_write` basta para estas pruebas. No se guarda nada.

## Bloques de la fase 3 (desde v0.6.0)

`verify_live.py` llama a `scripts/verify_blocks.py` antes del paso de UNDO. Ese script también se puede correr solo, sobre el dibujo de ensayo:

```
python scripts/verify_blocks.py road labels cad data
```

- **Dibujo de ensayo:** hay que regenerarlo con el plug-in v0.6, porque el eje vial `road_centerline` es nuevo.
- **Perfil de permisos:** las acciones FULL WRITE (erase, layout delete, plot_pdf, purge, borrar puntos, publicar accesos directos) cuentan como PASS si se rechazan con `permission_profile=safe_write`. Para verificarlas de verdad, repetir con `"permission_profile": "full_write"`.
- **Archivos generados:** el PDF, el CSV, el LandXML y el archivo de importación quedan en `%USERPROFILE%\.horizun\civil3dixtures\` con sello de fecha.

## Permisos para probar escrituras
- Archivo: `%USERPROFILE%\.horizun\civil3d\settings.json`. Si no existe, el perfil es `safe_write`.
- Crearlo o editarlo **solo con permiso del dueño**, y devolverlo a su estado al terminar.
- Ejemplo: `{ "permission_profile": "full_write" }`.

## Conector anterior: retirado (2026-10-04)
- El conector parcheado anterior (`Civil3DMcp.bundle`, TCP 8080) se retiró y archivó fuera del repo: exponía un canal C# sin protección.
- La comprobación de UNDO de `verify_live.py` es MANUAL: después de la escritura de prueba, haz un `U` en Civil 3D y confirma que el cambio se revirtió como un solo paso.
- Para diagnosticar con C#, usa el canal propio (`horizun_c3d_execute_csharp`) encendido por el dueño con el botón **Canal C#**.
