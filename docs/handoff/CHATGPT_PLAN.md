# Plan de integración ChatGPT — Civil 3D MCP

Fecha: 2026-10-01. Autorizado por el dueño: avanzar sin esperar autorizaciones rutinarias.

1. **Transporte independiente:** portar Secure MCP Tunnel de Revit; perfil `horizun-civil3d`, rutas propias y ninguna modificación de la integración Revit.
2. **Preflight y diagnóstico:** comprobar initialize/tools/list y namespace Civil 3D antes de iniciar; API key cifrada con DPAPI CurrentUser; listener loopback y proceso verificado; no confundir health local con conexión OpenAI ni conexión con llamada desde ChatGPT.
3. **Pruebas:** ejecutar regresiones de secretos, argumentos con espacios/Unicode, variantes de cliente, init/doctor/start/stop/revoke y frescura de polls con fixtures locales. Añadir rechazo de servidor Revit/namespace incorrecto.
4. **Distribución:** incluir auxiliares en install.ps1 y añadir instalación independiente que no reemplaza el plugin. Instalar cliente oficial completo con checksum y auxiliares; comprobar hashes y diagnóstico contra el servidor instalado.
5. **Cuenta y prueba real:** preparar comandos de conexión; crear/seleccionar Tunnel en Platform y app ChatGPT con la cuenta del dueño. La evidencia final requiere llamada real `horizun_c3d_health` desde ChatGPT; abrir Civil 3D permite después verificar el plugin.

## Criterios de cierre
- Automatización local instalada y probada; configuración Civil aislada; ninguna clave en argumentos/logs.
- No cambiar permisos del dibujo ni ejecutar escrituras en documentos del dueño.
- Registrar por separado pruebas simuladas, servidor instalado, contacto OpenAI y llamada real ChatGPT.
- Si faltan credenciales/acceso de cuenta, dejar comandos concretos y estado pending_user_action con la causa precisa.

## Referencia oficial
https://developers.openai.com/api/docs/guides/secure-mcp-tunnels

## Avance comprobado
- Etapas 1 a 4: completadas localmente. Diez auxiliares instalados por separado; cliente oficial 0.0.15 verificado; diagnóstico real contra el servidor instalado.
- 69/69 pruebas Core/Server y 111/111 checks del túnel en cada host (PowerShell 5.1 y 7.6.5). Full installer DryRun correcto con pruebas ejecutadas por separado.
- Etapa 5: pendiente de cuenta. Crear Tunnel ID/runtime API key en OpenAI Platform, ejecutar el asistente y vincular la app ChatGPT. No hay acceso a una sesión Platform ni herramientas de administración de túneles en este chat.
- Civil 3D sigue cerrado. Health local responde no_civil3d_instance; no se afirma verificación del plugin ni llamada desde ChatGPT.
