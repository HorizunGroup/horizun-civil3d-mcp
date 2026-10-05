# ChatGPT y Horizun Civil 3D MCP

> Current local deployment (2026-10-01): Civil 3D MCP **v0.2.0**, seven tools including the surface preview. The helpers are unchanged and the installed status validates the expanded catalogue. Earlier v0.1.1/six-tool results below describe historical integration checks. Real account connection and ChatGPT call remain pending.

La integración usa **OpenAI Secure MCP Tunnel**: el cliente local recibe trabajo MCP mediante una conexión saliente HTTPS y lo entrega por stdio al servidor existente. No requiere servidor público, adaptador HTTP propio ni abrir puertos entrantes. El endpoint de salud se limita a loopback.

Referencia oficial: https://developers.openai.com/api/docs/guides/secure-mcp-tunnels

## Estado comprobado en esta máquina — 2026-10-01

- Servidor instalado: `horizun-civil3d` 0.1.1, seis herramientas `horizun_c3d_*`.
- Cliente oficial completo: OpenAI tunnel-client 0.0.15; ZIP verificado con SHA256SUMS y digest publicado en GitHub. `init --help`, `doctor --help`, `run --help` y generación de perfil temporal comprobados. El identificador ficticio de la prueba está exclusivamente en el workspace, nunca en el perfil real.
- Auxiliares instalados por separado, con respaldo y manifiesto SHA-256; sin reemplazar servidor/plugin ni cambiar permisos.
- Pruebas Core/Server: 69/69. Regresiones del túnel: 111/111 comprobaciones bajo Windows PowerShell 5.1 y 111/111 bajo PowerShell 7.6.5.
- Full installer DryRun con empaquetado: build del plugin 2025 sin errores/advertencias, publicación correcta, auxiliares incluidos. SkipTests utilizado porque las suites se ejecutaron por separado.
- Initialize/tools/list del servidor instalado correctos. `horizun_c3d_health` responde `no_civil3d_instance`, esperado porque Civil 3D sigue cerrado.
- Estado de integración: **pending_user_action**. No hay runtime API key ni perfil/túnel de cuenta; no hay contacto OpenAI ni llamada ChatGPT verificados.

## Qué es cada dato

**Tunnel ID**: identificador de una conexión creada en OpenAI Platform; tiene forma `tunnel_` seguida de 32 caracteres hexadecimales. No es una contraseña. Conviene un túnel independiente para Civil 3D; no reutilizar el de Revit.

**Runtime API key**: credencial de OpenAI Platform con permiso de uso del túnel. Se ingresa sin eco en PowerShell; no pegarla en chats, archivos de configuración YAML, comandos ni repositorio. El auxiliar la cifra con DPAPI CurrentUser y la entrega al proceso mediante su entorno. Iniciar sesión en ChatGPT no crea automáticamente esta credencial.

**App de ChatGPT**: conexión MCP en modo desarrollador que selecciona ese túnel. Su disponibilidad depende del acceso de la cuenta/workspace; crear el túnel en una organización Platform no garantiza que aparezca en otro workspace ChatGPT.

## Completar la conexión

1. En https://platform.openai.com/settings/organization/tunnels crear un túnel con nombre `Horizun Civil 3D`. Copiar el Tunnel ID.
2. En https://platform.openai.com/settings/organization/api-keys crear una runtime API key con acceso al túnel. Conservarla para ingresarla de forma oculta en el siguiente paso.
3. En PowerShell ejecutar:

```powershell
$tools = Join-Path $env:LOCALAPPDATA 'Programs\Horizun\Civil3D-MCP\server\client-tools'
& (Join-Path $tools 'connect-chatgpt.ps1') -Interactive
```

El asistente pide el Tunnel ID y, si falta, la API key sin mostrarla. Luego hace init, doctor y start. Ese comando de conexión incluye aceptación del envío de solicitudes/respuestas MCP por OpenAI. No cambia el perfil de permisos del dibujo.

Alternativa con el identificador explícito (la clave sigue sin ir en argumentos):

```powershell
& (Join-Path $tools 'connect-chatgpt.ps1') -TunnelId 'tunnel_ID_REAL_DE_32_HEX' -SetApiKey
```

El texto `tunnel_ID_REAL_DE_32_HEX` es un marcador que hay que reemplazar; el script lo rechazará si no se reemplaza.

4. En ChatGPT, habilitar modo desarrollador si está disponible y crear la app/conector MCP con conexión **Tunnel**, seleccionando `Horizun Civil 3D`. Mantener el cliente local ejecutándose durante el descubrimiento y las llamadas. La CLI oficial también indica https://chatgpt.com/#settings/Connectors; la ubicación visual puede variar por producto/workspace.
5. Abrir Civil 3D y comprobar `HZ_STATUS`. En un chat con la app seleccionada pedir una llamada a `horizun_c3d_health`. Solo esa llamada confirma ChatGPT → túnel → servidor → plugin. No guardar ni editar dibujos de cliente durante esta verificación.

## Operación cotidiana

```powershell
& (Join-Path $tools 'chatgpt-tunnel.ps1') -Status
& (Join-Path $tools 'chatgpt-tunnel.ps1') -Doctor
& (Join-Path $tools 'chatgpt-tunnel.ps1') -Start -IUnderstandTrafficLeavesThisMachine
& (Join-Path $tools 'chatgpt-tunnel.ps1') -Stop
```

Para cambiar el Tunnel ID recrear el perfil con `connect-chatgpt.ps1 -TunnelId ... -Force`; reutiliza la clave almacenada o usar `-SetApiKey` para sustituirla.

`-Revoke` detiene únicamente el proceso registrado cuya identidad se verifica y elimina únicamente la clave y perfil locales de Civil 3D. Para revocar la credencial y eliminar app/túnel de la cuenta, hacerlo en OpenAI Platform/ChatGPT. No toca Revit ni perfiles ajenos.

Estado propio: `%LOCALAPPDATA%\Horizun\Civil3D-MCP\integrations\chatgpt`.
Perfil propio: `profiles\horizun-civil3d.yaml`.
Diagnóstico durable: `%LOCALAPPDATA%\Horizun\Civil3D-MCP\install-status.json`.
Manifiesto de auxiliares: `%LOCALAPPDATA%\Programs\Horizun\Civil3D-MCP\client-tools-manifest.json`.

El diagnóstico vuelve a medir el proceso y exige un poll exitoso reciente; `/readyz` verde por sí solo no demuestra conexión. El perfil debe contener un único comando que coincida con el servidor Civil validado. Se eliminan del entorno hijo selectores de otros perfiles/servidores y flags de logs crudos, restaurando el entorno del proceso padre.

## Desarrollo y distribución

`scripts/install.ps1` incluye los auxiliares y ejecuta las regresiones del túnel además de Core/Server. Para instalar solo los auxiliares sin cerrar Civil 3D usar `scripts/install-client-tools.ps1`; hace respaldo, verificación SHA-256 y rollback de los archivos escritos.

`scripts/install-tunnel-client.ps1` instala el cliente completo oficial, con checksum, ZIP confinado a su destino y comprobación de capacidad. No necesita clave ni altera perfiles Revit. Versiones del servidor/plugin permanecen 0.1.1 porque este bloque añade auxiliares; el contrato MCP no cambió.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/chatgpt-tunnel.tests.ps1
pwsh -NoProfile -File scripts/chatgpt-tunnel.tests.ps1
```

Las pruebas con stand-in cubren cifrado/redacción, rutas difíciles, selección de cliente, init/doctor/start/status/stop/revoke, identidad de proceso, listener, frescura de polls, namespace MCP, perfil y aislamiento del entorno. Son evidencia simulada local; no sustituyen una llamada desde ChatGPT.
