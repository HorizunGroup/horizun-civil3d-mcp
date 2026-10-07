# 00 - EMPIEZA AQUÍ (traspaso para cualquier IA o desarrollador)

> Este documento existe para que **cualquier IA pueda continuar el trabajo sin contexto previo**.
> Léelo completo antes de tocar nada. Después lee, en este orden:
> `STATUS.md` (estado y qué sigue) → `WORKFLOW.md` (cómo se trabaja, obligatorio) → `SESSION_LOG.md` (historia)
> → `PHASE1_PLAN.md` (la siguiente fase en detalle) → `LIVE_TESTING.md` (cómo instalar y probar en Civil 3D).

---

## 1. Propósito

Construir **Horizun Civil 3D MCP**, el MCP (Model Context Protocol) de **Horizun Group** para **Autodesk Civil 3D**. La meta del dueño es que sea **el mejor MCP de Civil 3D del mundo**.

La gran falencia de los MCP de Civil 3D existentes es que casi solo **leen** el dibujo. Este MCP debe **escribir** en el dibujo de forma confiable y **verificada**.

Dueño: **Juan Daniel Unigarro Pabón**, BIM Manager y consultor en Horizun AEC Partners / Horizun Group. Domina Civil 3D (gradings, feature lines, isopacas, análisis).

## 2. Instrucciones del dueño (textuales en espíritu; respetarlas siempre)

1. **Es una APP APARTE.** No es el mismo MCP ni el mismo servidor que el de Revit.
   - Se *basa* en el MCP de Revit de Horizun (`HorizunGroup/horizun-revit-mcp`): su contrato, su seguridad y su calidad.
   - Tiene su propio servidor, add-in, instalador y datos.
2. **Todo con nombre Horizun.** Las herramientas se llaman `horizun_c3d_*`.
   - **No usar nombres de funciones de otros MCP**, aunque hagan lo mismo. Eso incluye el conector Sacred-G `civil3d_*` y las herramientas `horizun_*` del MCP de Revit.
3. **Usar las funciones que ya se crearon y validaron** en el conector parcheado anterior. Hay que portarlas, no reinventarlas, pero reescritas bajo el contrato y los nombres Horizun.
4. **Siempre buscar ser el mejor MCP del mundo para Civil 3D**: escritura real, verificada y segura.
5. **Las decisiones técnicas las toma la IA** ("toma las mejores decisiones"). El dueño no quiere detalles técnicos innecesarios: explicarle en **español**, breve y claro.
6. Todos sus desarrollos van en `%USERPROFILE%\Documents\Desarrollos\`. Este proyecto vive en `...\Desarrollos\Civil3D MCP\`.
7. **Flujo de trabajo:** al terminar cada bloque de trabajo hay que **reportar siempre qué sigue** y dejar actualizado `STATUS.md` y `SESSION_LOG.md` (ver `WORKFLOW.md`).
8. Preferencias registradas en sesiones previas:
   - **Nunca usar su pantalla, mouse ni teclado** (nada de computer-use); todo por API o plugin.
   - **Recordarle siempre guardar** antes de pedirle cerrar Civil 3D; se ha perdido trabajo por no guardar.
   - Conservar trazabilidad: no sobrescribir superficies base; usar versiones `_ANT`.
   - Mostrar valores mínimo, máximo y promedio, no solo el promedio.
   - En comunicaciones a clientes, nunca decir "les damos la razón"; usar "concordamos".

## 3. Rutas (todas las que importan)

| Qué | Ruta |
|---|---|
| **Repo del producto** (aquí se trabaja) | `<repo>\` |
| Entrada para IAs en la carpeta padre | `%USERPROFILE%\Documents\Desarrollos\Civil3D MCP\LEEME_PRIMERO.md` |
| Brief de ingeniería original (fases, principios) | `%USERPROFILE%\Downloads\horizun-civil3d-mcp-brief.md` |
| Clon de referencia del MCP de Revit (solo lectura, patrones) | `%USERPROFILE%\Documents\Desarrollos\Civil3D MCP\horizun-revit-mcp\` |
| Conector anterior (Sacred-G parcheado) | **RETIRADO el 2026-10-04** y archivado fuera del repo. Sus hallazgos de API vigentes: `docs/API_NOTES_CIVIL3D_2025.md` |
| Add-in instalado (lo carga Civil 3D al arrancar) | `%APPDATA%\Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle\` |
| Servidor MCP instalado | `%LOCALAPPDATA%\Programs\Horizun\Civil3D-MCP\server\horizun-civil3d-mcp.exe` |
| Manifiesto de instalación y respaldos | `%LOCALAPPDATA%\Programs\Horizun\Civil3D-MCP\manifest.json`, `...\_backup\<fecha>\` |
| Datos en ejecución (permisos, descubrimiento, logs, sondeos) | `%USERPROFILE%\.horizun\civil3d\` (`settings.json`, `discovery\`, `logs\`, `probes\`) |
| Config de Claude Desktop (servidor registrado como `horizun-civil3d`) | `%APPDATA%\Claude\claude_desktop_config.json` |
| Memoria persistente de Claude Code | `<memoria local de Claude Code>\` (`horizun-civil3d-mcp.md`) |
| Dibujos reales de cliente | Solo locales, nunca en el repo; **no modificar sin permiso del dueño** |

## 4. Arquitectura en 30 segundos

```
Claude / cliente MCP
   │ MCP por stdio (JSON-RPC, una línea por mensaje)
horizun-civil3d-mcp.exe            src/Horizun.Civil3D.Server   (.NET 8, autocontenido)
   │ named pipe "Horizun.Civil3D-<pid>-<aleatorio>" + token de 256 bits; el servidor comprueba el pid antes de enviar (archivo discovery\civil3d-<año>-<pid>.json)
Horizun.Civil3D.dll en acad.exe    src/Horizun.Civil3D.Plugin   (un build por año: -p:Civil3DYear=2025)
   │ RequestGate (FIFO, 16) → hilo principal, contexto de aplicación (control oculto + timer + Idle) → LockDocument → Transaction
API .NET de Civil 3D (AeccDbMgd) + AutoCAD
```

- `src/Horizun.Civil3D.Core` contiene las reglas sin Autodesk: contrato y hash, permisos, descubrimiento, cola, tokens de confirmación y verificación. Todo está probado sin Civil 3D.
- `src/Horizun.Civil3D.Core/Contract.cs` es la **única** fuente de las herramientas y sus esquemas. Si cambia, cambia el hash, y servidor y add-in deben instalarse juntos.
- `tools/Horizun.Civil3D.ApiProbe` vuelca las firmas reales de la API **sin abrir Civil 3D**. Los volcados quedan en `docs/api-probes/2025/`.

## 5. El contrato (no negociable; ver también `CLAUDE.md`)

1. **Nunca reportar trabajo no verificado.** Toda escritura se re-lee en una transacción **nueva** después del commit (`VerificationSet`). Una verificación vacía nunca cuenta como éxito.
2. **Dry run por defecto en toda escritura.** El dry run devuelve el plan y un `confirmation_token` de un solo uso que vence en 10 min, atado al dibujo, a la petición y al plan resuelto. Se aplica con `dry_run=false` y el token.
3. **Resolver todo antes de la transacción.** Un nombre que no existe o es ambiguo se rechaza con la lista de candidatos.
4. **Rechazar con honestidad.** Un valor que no se puede leer es `null` con motivo, nunca `0`.
5. **Una operación a la vez.** Si Civil 3D está ocupado (comando o diálogo), se responde `busy` y no se ejecuta nada. Nunca se envía ESC.
6. **No inventar API.** Antes de usar un miembro de la API, sondearlo y guardar el volcado. Compilar contra las DLL instaladas es la segunda puerta.
7. **Organización neutral.** Ningún estándar de Horizun ni de clientes va compilado; todo eso entra como parámetro.

## 6. Comandos básicos

```bash
cd "<repo>"
dotnet test tests/Horizun.Civil3D.Core.Tests                     # sin Civil 3D
dotnet build src/Horizun.Civil3D.Plugin -c Release -p:Civil3DYear=2025
.tools/apiprobe/hz-c3d-apiprobe.exe --year 2025 Autodesk.Civil.DatabaseServices.TinSurface   # tras compilar tools/ (ver LIVE_TESTING.md)
pwsh scripts/install.ps1 -DryRun                                  # compila y prepara sin instalar
pwsh scripts/install.ps1                                          # instala (Civil 3D CERRADO; pedir al dueño GUARDAR antes)
python scripts/mcp_call.py '[["horizun_c3d_health",{}]]'          # prueba en vivo contra el servidor instalado
```

## 7. Dónde seguir

Mira **`STATUS.md` → sección "QUÉ SIGUE"**. Siempre está al día; si no lo está, el último en trabajar rompió el flujo, y lo primero es corregirlo con `SESSION_LOG.md` y `git status`.
