# STATUS - estado actual y QUÉ SIGUE

> Documento VIVO. Toda sesión que termine un bloque de trabajo lo **actualiza** (ver `WORKFLOW.md`).
> Última actualización: **2026-10-02 12:35**, por Claude Code, al cierre de la sesión de pruebas en vivo: **v0.6.9 instalada, 284/284 en vivo**. Incluye el trabajo previo de Codex.

## Versión

| Qué | Valor |
|---|---|
| Código en el repo | **v0.8.0**: 26 herramientas, contrato `550ff5cb0f7cdce48d7194d5`, 435 tests. Verificado en vivo: 294/294 con FULL WRITE aplicado + accesos directos + 2 instancias. Pendiente en vivo: `undo_last` y la publicación que asocia el dibujo |
| Instalado en la máquina del dueño | **v0.8.0, edición de desarrollo**, registrada como `horizun-civil3d`. El conector anterior (`civil3d-mcp`) fue **retirado** el 2026-10-04 (respaldo en `Desarrollos\Civil3D MCP\_archivo\`) |
| Civil 3D disponible | Solo **2025** (ACADVER 25.0s, AeccDbMgd 13.7.0.145) |
| Git / GitHub | `HorizunGroup/horizun-civil3d-mcp` (privado). Ramas `develop` (trabajo) y `main` (versiones estables, cada una con Release e instalable `.zip`). Las notas personales van en `.local/` (ignorado) |
| Perfil de permisos activo | `safe_write`. `settings.json` no existe: se borró tras la prueba de guardado |
| Integración ChatGPT | Auxiliares instalados, cliente oficial 0.0.15; 111 checks pasan en PowerShell 5.1 y 7.6.5. **pending_user_action**: faltan túnel/clave de cuenta y llamada real. Ver docs/CHATGPT.md |
| Registro en Claude Desktop | Hecho, como `horizun-civil3d`. Los otros conectores siguen intactos |

## Herramientas existentes (fase 0 y primer bloque de fase 1)

| Herramienta | Acciones | Evidencia en vivo 2025 |
|---|---|---|
| `horizun_c3d_health` | n/a | Sí |
| `horizun_c3d_target` | listar o elegir instancia | Sí |
| `horizun_c3d_document` | `info`, `list_open`, `object_census`, `save` | Sí. El guardado se probó con token y verificación en disco |
| `horizun_c3d_query` | `list`, `get` (15 tipos) | Sí para superficies y feature lines. Los demás tipos solo están compilados |
| `horizun_c3d_styles` | `list`, `get` (con quién usa cada estilo) | Sí |
| `horizun_c3d_probe` | firmas de la API en vivo | Sí |
| `horizun_c3d_surface` | list, get, sample_elevation, volumes_report, rename, set_style, duplicate_style, create_tin, create_volume, rebuild, **add_data, paste** | **Sí**: 46/46 en el dibujo de ensayo, deshacer incluido (v0.3.4) |

### Herramientas de la fase 3 (v0.6.x): VERIFICADAS EN VIVO 284/284 (2026-10-02)

| Bloque | Herramientas |
|---|---|
| A, vías | `horizun_c3d_alignment`, `horizun_c3d_profile`, `horizun_c3d_sections`, `horizun_c3d_corridor` |
| B, etiquetas | `horizun_c3d_labels` |
| C, AutoCAD | `horizun_c3d_layers`, `horizun_c3d_entities`, `horizun_c3d_dimensions`, `horizun_c3d_cad_styles`, `horizun_c3d_blocks`, `horizun_c3d_tables`, `horizun_c3d_layouts`, `horizun_c3d_cleanup` |
| D, redes y datos | `horizun_c3d_pipes`, `horizun_c3d_points`, `horizun_c3d_exchange` (data shortcuts + LandXML propio) |

Detalle en `CHANGELOG.md` (v0.6.0). Lo que no tiene API pública se rechaza por nombre y se ofrece una alternativa: láminas planta-perfil, view frames, intersecciones, subensamblajes de stock y gradings nativos.

Comandos dentro de Civil 3D: `HZ_STATUS`, `HZ_PROBE` y **`HZ_BUILD_FIXTURE`**, y la cinta "Horizun Hub" → panel "Horizun C3D MCP" (botones Estado del puente, Sondeo API y Dibujo de ensayo).

Detalle de la evidencia: `docs/CIVIL3D.md`.

## Pendientes conocidos (pequeños)

1. Superficies v0.3.4: verificadas en vivo (46/46). Lo pendiente en vivo es v0.4.0+ (ver QUÉ SIGUE).
2. **Probar "busy" con un diálogo modal** abierto (Opciones). Solo está probado con un comando (`LINE`).
3. **Investigar los objetos `AeccDbVAlignment` sin alineamientos** (79 en un dibujo real de cliente, probablemente internos de gradings) antes de contarlos como perfiles.
4. Las versiones de Civil 3D 2026 y 2027 compilan, pero no se pueden probar en esta máquina. 2023 y 2024 (net48) no están soportadas.

## Correcciones de la revisión (Codex, 2026-10-01)

- **Corregido en código v0.1.1:** permisos con tipos inválidos, null, claves duplicadas/desconocidas o elementos inválidos fallan a read_only; los archivos inaccesibles no se tratan como inexistentes.
- **Corregido en código:** save incluye una revisión de la base de datos en memoria; eventos de objetos, variables y vista la incrementan. La confirmación se comprueba bajo el mismo bloqueo que mantiene SaveAs.
- **Corregido en código:** query list/get, object_census y save incluyen units. La versión de build es 0.1.1.
- **Evidencia:** 69/69 tests pasan; plugin 2025 compila con 0 errores / 0 advertencias. Eventos sondeados en las DLL instaladas; nuevos volcados guardados en docs/api-probes/2025.
- **Contrato nuevo:** `6b2a8fe91890a5452113b9af`; instalar servidor y plugin juntos y reiniciar el cliente MCP.
- **Desplegado:** scripts/install.ps1 -Years 2025 completado; 69 tests, build y publicación correctos. 8 archivos del bundle y 3 del servidor verificados por SHA-256; instalación anterior respaldada.
- **Instalado:** v0.1.1. Initialize/tools/list verificados contra el servidor instalado; health informa no_civil3d_instance porque Civil 3D está cerrado. Las correcciones del plugin requieren regresión en vivo; no se modificó el dibujo del dueño.

## Integración ChatGPT (Codex, 2026-10-01)

- Plan CHATGPT_PLAN.md, guía docs/CHATGPT.md. Perfil horizun-civil3d y estado bajo LOCALAPPDATA/Horizun/Civil3D-MCP; Revit queda independiente.
- Diez auxiliares instalados con respaldo y manifiesto SHA-256. Full installer incluye helpers y regresiones; instalador independiente permite añadirlos sin reemplazar DLL.
- Cliente OpenAI completo 0.0.15 descargado/verificado; init temporal y help real comprobados. No se instaló un perfil ficticio como perfil de producción.
- 69 Core/Server y 111 checks en cada PowerShell pasan. Full installer DryRun/SkipTests compila/publica/empaqueta; las suites pasaron por separado.
- Initialize/tools/list del servidor instalado: seis herramientas correctas. Health: no_civil3d_instance, esperado con Civil cerrado. Los once archivos originales del bundle/servidor conservan los hashes del despliegue anterior.
- Se verifica identidad/namespace, comando de perfil y proceso; DPAPI CurrentUser; entorno hijo aislado de overrides de otros túneles, restaurando el padre.
- Falta acceso de cuenta: Tunnel ID/runtime API key y app ChatGPT. Un poll reciente solo prueba contacto del túnel; una llamada real desde ChatGPT sigue pendiente.

## Superficies v0.2.0 (Codex, 2026-10-01)

- Diez acciones implementadas. Lecturas de estadísticas, muestreo XY/línea y volumen; escrituras con dry run por defecto, confirmación de un uso y relectura tras commit. Creación TIN vacía y volumen entre superficies existentes; aún falta add_data.
- Estadísticas de grilla y áreas se etiquetan como estimadas; las celdas de borde usan su área efectiva. Fuera de dominio/sin muestras/unreadable: null con motivo, nunca cero inventado. Volumen nativo conserva su convención; factores calculados reportan neto fill-minus-cut.
- Consultas de volumen nativo abren Write pero siempre Abort; el volumen temporal base/comparison nunca se confirma. Esos eventos conservadoramente invalidan tokens viejos de revisión.
- duplicate_style relee nombre/handle y todos los componentes de visualización plan/model; otros parámetros internos dependen de CopyAsSibling, sin afirmarlos verificados individualmente.
- Firmas y tipos sondeados desde DLL 2025; volcados en docs/api-probes/2025. Plugin compila con cero advertencias/errores. 119/119 Core/Server; 111/111 checks PowerShell 5.1 en DryRun completo; regresión previa ChatGPT en PowerShell 7.6.5 sigue vigente, helpers sin cambios.
- Instalación con respaldo _backup/20261001-124001; comprobación independiente SHA-256 de 8 archivos de bundle y 13 de servidor. Initialize versión 0.2.0 y tools/list siete herramientas comprobados contra el ejecutable instalado.
- rename sin target_document devuelve invalid_input antes de buscar Civil. health y surface list válidos devuelven no_civil3d_instance: Civil cerrado, **sin prueba L nueva**. Diagnóstico ChatGPT reconoce siete tools y permanece pending_user_action; sin proceso túnel.
- Las correcciones v0.1.1 y los auxiliares ChatGPT anteriores están incluidos en v0.2.0. Las secciones previas conservan su evidencia histórica.

## ▶ QUÉ SIGUE (siguiente bloque de trabajo)

1. ~~Probar el camino aplicado de FULL WRITE~~: **HECHO 2026-10-02** (294/294). Falta solo publicar y referenciar accesos directos, que necesitan un proyecto.
2. ~~Accesos directos~~: **HECHO 2026-10-04** (publicar y referenciar con asociación automática, dos instancias). Falta re-ejecutar en vivo la publicación de la v0.7.4 (ahora asocia el dibujo).
3. **Probar los botones "Canal C#" y "Escritura completa"** pulsados a mano (encender, `execute_csharp`, apagar, apagado automático al reiniciar) y `shortcuts_publish`/`shortcuts_reference` con un proyecto de accesos directos real.
3. **Probar en dibujos reales** (con permiso del dueño, nunca sin él): etiquetas sobre referencias de accesos directos, corredores con ensamblajes importados (`assembly_import`) y redes con el catálogo del cliente.
4. **Siguientes funciones:** `execute_plan` todo-o-nada; espirales en alineaciones y en el LandXML; redes de presión; Excel y Power BI.
5. **Pendientes menores:** busy con diálogo modal, breakline proximity, `AeccDbVAlignment` y el índice de `SetPointElevation`.

**Ciclo de despliegue** (cada versión nueva):
1. El dueño cierra Civil 3D sin guardar el dibujo de ensayo.
2. Correr `scripts/install.ps1 -Years 2025`.
3. El dueño abre Civil 3D, ejecuta `NEW` y pulsa "Dibujo de ensayo".
4. Correr `python scripts/verify_live.py`.

**ChatGPT** se completa en paralelo (faltan las credenciales de la cuenta del dueño; ver `docs/CHATGPT.md`).
