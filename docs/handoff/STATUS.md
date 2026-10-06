# STATUS - estado actual y QUÉ SIGUE

> Documento VIVO. Toda sesión que termine un bloque de trabajo lo **actualiza** (ver `WORKFLOW.md`).
> Última actualización: **2026-10-06**, por Codex. **v0.9.2 publicada y repositorio PUBLIC**; PR#1 integrado tras CI exitosa. Cuatro paquetes y SHA256SUMS coinciden con los hashes remotos. Asset0.8.0 con rutas privadas retirado y respaldado; reportes privados de seguridad habilitados. Instalación2025 conserva78/78 controles y262 hashes correctos. UNDO automático deshabilitado;2024/2026 solo evidencia de compilación/pruebas sin anfitrión. Ver [PUBLIC_READINESS.md](../PUBLIC_READINESS.md).

## Versión

| Qué | Valor |
|---|---|
| Código en el repo | **v0.9.2**: 28 herramientas / 163 acciones / 169 operaciones, contrato `e36ee390efbb5d34ee0fe86c`. 552 Core/Server,391 por net48/net8/net10,14 receptor y8 cliente. Builds2024/net48,2025/net8,2026/net8/net10 completos; undo_last reservado pero rechazado sin ejecutar |
| Instalación | **v0.9.2 operativa2025**. Generación `0.9.2-20261006-085000`, servidor aislado `server-releases/0.9.2-final` registrado en Codex;262 archivos verificados. SECURELOAD y perfil safe_write conservados. Ensayo de78 controles con el payload final y rechazo de UNDO sin cambios |
| Civil 3D disponible | **2025**: aceptación nativa de corredores, CSV, comparación, planos, copia DWG y exportación de terreno. Revit2025: Toposolid y DirectShape creados, controles independientes y modelos guardados. 2024/2026 no instalados; solo build/pruebas sin anfitrión |
| Git / GitHub | [Repositorio público](https://github.com/HorizunGroup/horizun-civil3d-mcp). PR#1 integrado en `main` (`2c02c2d`); [release v0.9.2](https://github.com/HorizunGroup/horizun-civil3d-mcp/releases/tag/v0.9.2) con cuatro paquetes y SHA256SUMS. `develop` conserva estado histórico previo. Notas personales en `.local/` ignorado |
| Perfil de permisos activo | `safe_write`. `settings.json` no existe: se borró tras la prueba de guardado |
| Integración ChatGPT | Auxiliares instalados, cliente oficial 0.0.15; 111 checks pasan en PowerShell 5.1 y 7.6.5. **pending_user_action**: faltan túnel/clave de cuenta y llamada real. Ver docs/CHATGPT.md |
| Registro en Claude Desktop | Hecho, como `horizun-civil3d`. Los otros conectores siguen intactos |
| Registro en Codex | `horizun-civil3d` habilitado por CLI oficial, stdio apunta al EXE instalado. Configuración respaldada y comparación semántica de ajustes previos pasa; catálogo del servidor instalado comprobado |

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

1. Automatic undo_last deshabilitado: ensayo nuevo no retiró una línea; snapshots experimentales no cubrieron toda la topología. Se rechaza antes de ejecutar y no se anuncia una inversa automática. UNDO manual requiere inspección.
2. **Probar "busy" con un diálogo modal** abierto (Opciones). Solo está probado con un comando (`LINE`).
3. **Investigar los objetos `AeccDbVAlignment` sin alineamientos** (79 en un dibujo real de cliente, probablemente internos de gradings) antes de contarlos como perfiles.
4. 2024/2026 compilan contra referencias reales firmadas. Falta L y coincidencia de actualizaciones instaladas; 2024 rechaza UseSameSideTarget no disponible. 2026 necesita net8 o net10 según actualización. 2023 no es target; 2027 no tiene nueva evidencia.

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

**Publicación completada:** autorización reiterada y nueva sesión con aprobación de ejecución permitieron push. PR#1 pasó CI (`37476925655`) y se integró en `2c02c2d6122c9904df15e03aa8a6ca7c819998df`. Release0.9.2 publicada con cinco hashes remotos idénticos. ZIP0.8.0 retirado después de verificar respaldo; tag/fuente conservados. Visibilidad PUBLIC verificada y reportes privados de seguridad habilitados. Configuración instalada y evidencia nativa conservadas.

1. Piloto controlado2025 según la matriz de evidencia pública; registrar resultados sobre copias autorizadas. No describir todas las acciones declaradas como certificadas ni anunciar restauración automática.
2. Ejecutar aceptación en anfitriones 2024 y 2026 cuando existan, usando el runtime y referencias de su actualización. El dueño confirmó que no están instalados: no volver a pedir rutas ni declarar L para esos años.
3. Bloque futuro: investigar atribución nativa y snapshots completos antes de reactivar undo_last. Mantener cantidades estimadas y refresco explícito documentados.

## Plan histórico anterior (sustituido por la aceptación de v0.9.1)

**Inicio resuelto en nueva instancia2025:** health y comparación de superficies ya pasan. El dibujo de ensayo nuevo permanece abierto y sin guardar; guardar si se quiere conservar. Una instancia anterior sin ventana sigue abierta y no fue terminada. **Qué sigue:** aceptación de corredores/CSV/viewports/UNDO en fixture y abrir Revit para ensayar transferencia real, con controles independientes. 2024/2026 requieren L en sus equipos.

**Prioridad actual tras la comparación con Revit/Navisworks/Power BI/Project:**

**Compatibilidad pedida por el dueño:** compilaciones completas terminadas para 2024 y ambas familias 2026; sondeos offline reales y diferencias corregidas. Paquete canónico 2024/2025/2026 net8 y ZIP separado 2026 net10. El dueño confirma que no las tiene: no volver a pedir su ruta. **Qué sigue:** medir versiones de DLL en el equipo de destino, respetar `host-builds.json`, instalar después de guardar/cerrar y ejecutar fixture; Toposolid Revit y ubicación/fidelidad siguen pendientes de L. Ver `docs/COMPATIBILITY.md`.

**Prioridad del dueño:** probar los bloques ya implementados. Autorizó **ensayo con archivos nuevos**. `.local/engineering-fixture-20261005` contiene TIN XML/OBJ/ZIP de 100 m², CSV de ejemplo, RMSE esperado 0.02 m y peticiones de creación nativa. No equivale a DWG/RVT creados. **Qué sigue:** instalar servidor+addin juntos después de confirmar guardar/cerrar según WORKFLOW; abrir Civil/Revit, crear fixture y modelo nuevo por API, ensayar Toposolid y/o DirectShape, medir controles e interiores, y comprobar targets/split-merge/CSV/viewports/UNDO. Python de Revit requiere su autorización existente, nunca se autoactiva.

1. Revisar `docs/BENCHMARK.md`, `docs/CAPABILITY_REVIEW.md` y `docs/CAPABILITY_CATALOG.md`. Se compararon tres MCP públicos, Dynamo, Camber, CTC CIM Project Suite y Grading Optimization. El catálogo ya puede consultarse sin Civil 3D con `horizun_c3d_capabilities`; su disponibilidad no prueba al anfitrión.
2. Desplegar el ZIP preparado servidor + add-in juntos: pedir **guardar dibujos y cerrar Civil 3D**, esperar confirmación, instalar y reiniciar el cliente MCP. No se hizo esta instalación durante la revisión.
3. Probar en el fixture: auditoría; presión (list/get/red vacía/rename); copia DWG sin guardar el origen; LandXML métrico/pie internacional/pie US con INSUNITS distinto; CSV/XML/DWG denegados bajo safe_write. Mantener regresiones de dibujos homónimos, C# query, UNDO, PDF existente + fallo y carga del plugin/MCPB.
   Añadir `export_revit` con hash/conectividad/coordenadas y rechazo bajo safe_write. Con el DWG/superficie y modelo destino identificados por el dueño, ejecutar ensayo Toposolid y contrastar controles/rotación/cota/nivel e interiores. Revit tiene un modal que debe resolver la persona; no fue cerrado automáticamente.
4. Completar snapshots anteriores para verificar UNDO de modificaciones. Actualmente se informa honestamente verificación incompleta, sin reintentar el UNDO ya ejecutado.
5. Con instrucción del dueño: commit/push y publicación de v0.8.1 con ZIP, plugin ZIP, MCPB y hashes. Los artefactos locales no son una release publicada.
6. Siguiente bloque de interoperabilidad: probar transferencia y sondear malla exacta/3DFACE/DirectShape para preservar TIN/huecos, separado del Toposolid editable. Mantener snapshots de UNDO, planes transaccionales y estado de operaciones; después targets/geometría aplicada + cantidades trazables y presión. Los criterios están en `docs/INTEROPERABILITY.md` y `docs/BENCHMARK.md`; una secuencia de commits no equivale a todo-o-nada.

**Hoja de ruta histórica que continúa pendiente:**

1. ~~Probar el camino aplicado de FULL WRITE~~: **HECHO 2026-10-02** (294/294). Falta solo publicar y referenciar accesos directos, que necesitan un proyecto.
2. ~~Accesos directos~~: **HECHO 2026-10-04** (publicar y referenciar con asociación automática, dos instancias). Falta re-ejecutar en vivo la publicación de la v0.7.4 (ahora asocia el dibujo).
3. **Probar los botones "Canal C#" y "Escritura completa"** pulsados a mano (encender, `execute_csharp`, apagar, apagado automático al reiniciar) y `shortcuts_publish`/`shortcuts_reference` con un proyecto de accesos directos real.
3. **Probar en dibujos reales** (con permiso del dueño, nunca sin él): etiquetas sobre referencias de accesos directos, corredores con ensamblajes importados (`assembly_import`) y redes con el catálogo del cliente.
4. **Siguientes funciones:** `execute_plan` todo-o-nada; espirales; piezas y conexiones de redes de presión (la red vacía y sus consultas ya están compiladas); cómputos, Excel y Power BI.
5. **Pendientes menores:** busy con diálogo modal, breakline proximity, `AeccDbVAlignment` y el índice de `SetPointElevation`.

**Ciclo de despliegue** (cada versión nueva):
1. El dueño **guarda su trabajo antes de cerrar Civil 3D** y confirma el cierre. La regresión se hace después sobre un fixture nuevo.
2. Correr `scripts/install.ps1 -Years 2025`.
3. El dueño abre Civil 3D, ejecuta `NEW` y pulsa "Dibujo de ensayo".
4. Correr `python scripts/verify_live.py`.

**ChatGPT** se completa en paralelo (faltan las credenciales de la cuenta del dueño; ver `docs/CHATGPT.md`).
