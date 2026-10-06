---
name: horizun-civil3d-setup
description: Instala, actualiza y diagnostica Horizun Civil 3D MCP como plugin de Codex o Claude cuando solo aparecen herramientas de instalación, falta el add-in o el puente no responde.
---

# Preparar Horizun Civil 3D MCP

El producto tiene dos componentes de la misma versión: servidor MCP autocontenido
y add-in de Autodesk. Requiere Windows y un paquete compilado para el año y
actualización instalados de Civil 3D. Los targets incluyen 2024, 2025, 2026 y 2027;
el paquete canónico incluye 2024/2025/2026 net8 y existe un ZIP separado 2026 net10. Para 2024 se
prepara net48; 2026 usa net8 hasta 2026.2.1 y net10 desde 2026.2.2. El instalador
lee metadatos Autodesk y rechaza paquetes de otro runtime/actualización. Compilar
Core en esos runtimes no demuestra que todo el add-in funcione en esos años.
AutoCAD sin Civil 3D no sirve. El paquete de usuario no necesita Python, .NET SDK
ni permisos de administrador. Las pruebas en vivo actuales corresponden a 2025.

## Si aparecen herramientas de instalación

Consulta `horizun_c3d_install_status`. El estado `ready` comprueba archivos y
hashes; todavía no demuestra que el puente esté cargado en Civil 3D.

`horizun_c3d_install_runtime` prepara el plan por defecto. Antes de aplicarlo,
indica a la persona que **guarde sus dibujos y cierre todas las ventanas de
Civil 3D/AutoCAD**, y espera confirmación de cierre. Aplica con `confirm=true`.
El instalador comprueba SHA-256, respalda la instalación anterior y verifica
los archivos copiados. No cambies los permisos para instalar.

Tras `ready`, la persona reinicia el cliente MCP para cargar las herramientas
de dibujo, abre Civil 3D y se comprueba `horizun_c3d_health`. Si falta la release
o el repositorio exige acceso, conserva el error y usa el paquete local verificado;
no inventes un hash ni instales un SDK para eludir un fallo de descarga.

## Diagnóstico sin herramientas

Resuelve la raíz del plugin dos directorios por encima de esta skill y ejecuta:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/plugin-launcher.ps1 -Status
```

El launcher conserva los errores de manifiesto y hashes. No borres un runtime
degradado ni sus respaldos como primer remedio. Si el estado es `update_required`,
instala el paquete que corresponde a la versión del plugin.

El servidor instalado vive en
`%LOCALAPPDATA%\Programs\Horizun\Civil3D-MCP\server\horizun-civil3d-mcp.exe`;
el add-in en `%APPDATA%\Autodesk\ApplicationPlugins\Horizun.Civil3D.bundle`.
El ZIP completo también se instala ejecutando `install.ps1` desde su carpeta.
El registro opcional `-RegisterClaudeDesktop` requiere Claude cerrado.

## Si las herramientas existen pero no conectan

- `no_civil3d_instance`: abre Civil 3D; pide ejecutar `HZ_STATUS` si el add-in no publica el puente.
- `ambiguous`: lista y elige un PID con `horizun_c3d_target`; conserva la elección en la misma sesión.
- `contract_mismatch`: servidor y add-in pertenecen a builds distintos. Reinstálalos juntos y reinicia ambos clientes.
- `busy`: espera a que termine el comando o diálogo. No envíes ESC ni conduzcas pantalla, mouse o teclado.
- `permission_denied`: explica el permiso necesario. Los perfiles son globales por usuario de Windows; el botón de una instancia afecta a las demás. No edites `settings.json` sin autorización.

Informa por separado: archivos instalados, reinicio pendiente y puente verificado
en vivo. Un código de salida cero no demuestra las tres cosas.
