# WORKFLOW: el flujo de trabajo obligatorio

> Toda IA o persona que trabaje en este repo sigue este ciclo. Existe para que el trabajo **nunca se pierda**,
> aunque una sesión se corte a mitad de una tarea (por ejemplo, porque se acabaron los tokens).

## El ciclo

```
 ┌─► 1. LEER ──► 2. PLANEAR ──► 3. SONDEAR API ──► 4. IMPLEMENTAR ──► 5. PROBAR SIN CIVIL ─┐
 │                                                                                        │
 │   10. REPORTAR "QUÉ SIGUE" ◄── 9. REGISTRAR ◄── 8. VERIFICAR EN VIVO ◄── 7. INSTALAR ◄── 6. COMPILAR POR AÑO
 └────────────────────────────────────────────────────────────────────────────────────────┘
```

### 1. LEER (al empezar cualquier sesión)
1. Lee `docs/handoff/00_START_HERE.md`, `STATUS.md` y las últimas 3 entradas de `SESSION_LOG.md`.
2. Corre `git status` para ver trabajo sin registrar. Si hay cambios que el log no explica, la sesión anterior se cortó: reconstruye qué hizo, revisando los archivos tocados, y anótalo en el log antes de seguir.
3. Si tienes las herramientas `horizun_c3d_*`, llama `horizun_c3d_health`. Si no las tienes, usa `python scripts/mcp_call.py '[["horizun_c3d_health",{}]]'`.

### 2. PLANEAR
1. Toma el siguiente punto de "QUÉ SIGUE" en `STATUS.md`. Si el dueño pidió otra cosa, manda el dueño.
2. Escribe **antes de codificar** una entrada "EN CURSO" en `SESSION_LOG.md` con el objetivo del bloque. Así, si la sesión muere, la siguiente sabe qué se intentaba.

### 3. SONDEAR LA API (regla de oro: no inventar)
1. Para cada clase o miembro de Civil 3D que vayas a usar, compruébalo:
   - **sin Civil 3D:** `.tools/apiprobe/hz-c3d-apiprobe.exe --year 2025 <Tipo>`;
   - **con Civil 3D:** con `horizun_c3d_probe`.
2. Guarda el volcado en `docs/api-probes/2025/<tema>.txt`.
3. Consulta también `docs/API_NOTES_CIVIL3D_2025.md`, que tiene firmas ya verificadas y trampas conocidas.

### 4. IMPLEMENTAR
1. Herramienta nueva o acción nueva: declárala en `Contract.cs` y crea o extiende un `ICommand`. Regístralo en `App.CreateDispatcher`.
2. Cada escritura sigue el patrón completo:
   1. `ctx.Document(forWrite:true)`;
   2. resolver todo;
   3. armar el plan;
   4. `ctx.Rehearse` si es dry run;
   5. `ctx.RequireConfirmation`;
   6. `ctx.Write(undoLabel, …)`;
   7. `ctx.Verify(…)` con `VerificationSet`;
   8. responder `verified`, `undo`, unidades y `bridge_queue`.
3. Nombres: siempre `horizun_c3d_*`. Nunca nombres de otros MCP.

### 5. PROBAR SIN CIVIL 3D
1. Ejecuta `dotnet test tests/Horizun.Civil3D.Core.Tests`; todo debe pasar.
2. Agrega pruebas para todo lo que no dependa de Civil 3D: validación, planes, tokens y esquemas.

### 6. COMPILAR POR AÑO
1. Compila con `dotnet build src/Horizun.Civil3D.Plugin -c Release -p:Civil3DYear=2025`.
2. El resultado debe ser 0 errores. Si compila contra las DLL instaladas, la API existe con esa firma.

### 7. INSTALAR (requiere al dueño)
1. **Pídele al dueño: "guarda tu trabajo y cierra Civil 3D"**. Espera su confirmación.
2. `acad.exe` tarda unos 20 s en salir; espera a que termine sin matarlo.
3. Ejecuta `pwsh scripts/install.ps1`, sin `-RegisterClaudeDesktop`, porque el registro ya está hecho.
4. Si cambió el contrato, el dueño debe reiniciar Claude Desktop. Si solo cambió el add-in, basta con reabrir Civil 3D.

### 8. VERIFICAR EN VIVO
1. El dueño abre Civil 3D y un dibujo. Confirma con `HZ_STATUS` o `horizun_c3d_health`.
2. Prueba cada acción nueva: dry run, aplicación y re-lectura, más los rechazos esperados.
3. **Nunca escribas en un dibujo de cliente sin permiso explícito del dueño.** Prefiere una copia o el fixture.
4. Si cambiaste permisos (`settings.json`) para probar, **devuélvelos** al terminar.

### 9. REGISTRAR (siempre, aunque el bloque haya fallado)
1. **`docs/CIVIL3D.md`**: la matriz de evidencia. La letra L va solo con prueba en vivo.
2. **`CHANGELOG.md`**: qué se agregó o corrigió y qué se midió.
3. **`docs/handoff/SESSION_LOG.md`**: cierra la entrada "EN CURSO" con lo hecho, la evidencia, los problemas y qué sigue.
4. **`docs/handoff/STATUS.md`**: versión, herramientas, pendientes y la sección **"QUÉ SIGUE"**, siempre accionable.
5. **Memoria de Claude Code** (si eres Claude Code): actualiza `memory/horizun-civil3d-mcp.md`.

### 10. REPORTAR AL DUEÑO, siempre con "qué sigue"
Cada entrega al dueño, en español, breve y sin jerga innecesaria, incluye:
- **Qué se hizo**, con la evidencia (qué se probó y el resultado).
- **Qué falló o queda pendiente**, dicho honestamente.
- **QUÉ SIGUE**: el siguiente paso recomendado y lo que necesitas de él (cerrar Civil 3D, abrir un dibujo, un permiso).

## Reglas de seguridad durante el ciclo
- Nunca usar la pantalla, el mouse ni el teclado del dueño. Nunca matar `acad.exe`. Nunca enviar ESC a Civil 3D.
- Nunca editar `claude_desktop_config.json` ni crear o modificar `settings.json` sin permiso del dueño. Respalda siempre antes.
- No hacer commit ni push sin que el dueño lo pida.
- Si te quedas sin presupuesto a mitad de un bloque, **lo primero es actualizar `SESSION_LOG.md` y `STATUS.md`**. El código a medio hacer se puede retomar; lo que no esté escrito, no.
