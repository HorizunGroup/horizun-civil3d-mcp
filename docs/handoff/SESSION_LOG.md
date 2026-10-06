# SESSION LOG: bitácora de sesiones (agregar al final; nunca borrar)

> Formato de cada entrada:
> `## AAAA-MM-DD - <quién> - <tema>` · **Estado**: EN CURSO | TERMINADO | INTERRUMPIDO
> **Objetivo** · **Hecho** · **Evidencia** · **Problemas / decisiones** · **Qué sigue**

---

## 2026-10-01 - Claude Code (Opus 5.5) - Arranque del producto, fase 0 completa y verificada en vivo
**Estado**: TERMINADO

**Objetivo**
1. Revisar la carpeta de conectores locales y borrar el conector de Civil 3D duplicado.
2. Estudiar el MCP de Revit de Horizun.
3. Crear el MCP propio de Civil 3D.

**Hecho**
- **Duplicado:** se eliminó el worktree un clon duplicado del upstream, una copia limpia del upstream de Sacred-G v1.2.1 sin uso; se puede recuperar desde git. El conector parcheado el conector anterior (retirado, archivado fuera del repo) se dejó activo.
- **Estudio del Revit MCP:** se clonó `HorizunGroup/horizun-revit-mcp` como referencia y se entregó el resumen de la sección 0 del brief.
- **Decisión del dueño:** debe ser una **app aparte** y no un segundo host del servidor de Revit. Los nombres van con prefijo `horizun_c3d_*`, y las decisiones técnicas quedan delegadas en la IA.
- **Repo creado:** `horizun-civil3d-mcp` con Core, Server, Plugin, Tests, ApiProbe, instalador y documentación.
- **Herramientas de la fase 0:** health, target, document, query, styles y probe. También los comandos `HZ_STATUS` y `HZ_PROBE` y la cinta "Horizun Hub".
- **Instalado** con `scripts/install.ps1 -RegisterClaudeDesktop`. Los hashes se verificaron; hay respaldo de la configuración de Claude.

**Evidencia**
- 48 de 48 pruebas pasan sin Civil 3D.
- Compila con 0 advertencias y 0 errores contra las DLL de Civil 3D 2025.
- En vivo, sobre un dibujo real de cliente:
  - health, censo (32 superficies, 26 feature lines), query y styles responden bien;
  - 6 llamadas simultáneas se atendieron en orden FIFO;
  - con `LINE` activo, la respuesta fue `busy` y no se ejecutó nada;
  - el guardado pasó por dry run, token y aplicación: archivo verificado en disco (40.5 MB) y DBMOD volvió a 0. Reusar el token y omitirlo se rechazan.

**Problemas / decisiones**
- Las pruebas encontraron dos bugs antes del despliegue y se corrigieron:
  - los parámetros JSON quedaban con dos padres;
  - los enteros creados en memoria no se reconocían como número.
- En vivo:
  - un código de coordenadas `"."` significa que no hay sistema asignado;
  - un zoom o paneo pone `DBMOD=16`, que es solo cambio de vista.
- En los dos casos el código ya está corregido (v0.1.1), pero **aún no está instalado**.
- **Hilo:** se usa contexto de aplicación con LockDocument, no `ExecuteInCommandContextAsync`. La justificación está en `docs/CIVIL3D.md`.
- El guardado sobre el dibujo del cliente lo autorizó el dueño explícitamente ("si no hiciste cambios"). Antes de guardar se confirmó que solo había cambiado la vista.

**Qué sigue**
- Instalar v0.1.1 la próxima vez que Civil 3D esté cerrado.
- Arrancar la **fase 1: superficies** (`PHASE1_PLAN.md`), previa confirmación del dueño.

## 2026-10-01 - Codex - Revisión del proyecto y validación de fase 0
**Estado**: TERMINADO

**Objetivo**
- Revisar la carpeta y entender el desarrollo, por solicitud del dueño.

**Hecho / evidencia**
- Leídos el traspaso, las reglas, el plan, la matriz de capacidades y el código de Core, Server y Plugin.
- 48/48 pruebas pasan en una copia temporal; dos avisos xUnit1031 en las pruebas del servidor.
- Plugin 2025 compilado con 0 errores y 0 advertencias contra las DLL instaladas.
- Lecturas reales por el servidor instalado: health responde con plugin 0.1.0, contrato compatible y safe_write; query list surface devuelve 32 superficies y no incluye units.
- No se modificaron dibujos, permisos, configuración del cliente ni instalación. No se hizo commit ni push.

**Problemas / decisiones**
1. Settings.Parse ignora tipos inválidos en controles: paused como string y allow/deny lists mal tipadas pueden conservar SafeWrite. Reproducido con la DLL compilada, sin editar settings.json real.
2. El plan de save usa ruta, fecha en disco y flags DBMOD; cambios adicionales en memoria con los mismos flags pueden conservar el token. Hallazgo por inspección; no se editó el dibujo del dueño para reproducirlo.
3. Query devuelve medidas en unidades del dibujo sin incluir units en su respuesta. Confirmado por código y consulta real.
4. v0.1.1 está documentada como pendiente, pero Directory.Build.props sigue en 0.1.0: alinear antes del despliegue.

**Qué sigue**
- Corregir estos puntos antes de ampliar escrituras; después comenzar fase 1 de superficies, incorporando un dibujo de ensayo antes de nuevas escrituras en vivo.
- La revisión no inicia la fase 1 ni despliega. Para instalar: pedir al dueño guardar y cerrar Civil 3D y esperar confirmación.

## 2026-10-01 - Codex - Correcciones de seguridad y unidades v0.1.1
**Estado**: TERMINADO (código; despliegue y verificación en vivo pendientes)

**Objetivo**
- Corregir los hallazgos autorizados por el dueño: permisos, vigencia del token de save y unidades; alinear v0.1.1.

**Hecho**
- Validación estricta de todos los controles de settings: tipos, null, listas, claves desconocidas y duplicadas. Solo un archivo realmente ausente toma los valores por defecto.
- RevisionClock en Core y observador por base de datos en Plugin: eventos de objetos, variables de base de datos y vista. El plan de save incorpora la revisión y mantiene un bloqueo desde el plan hasta SaveAs.
- Unidades en query list/get, object_census y save; versión 0.1.1; descripciones de contrato actualizadas.
- Sondeador offline ampliado para eventos con firmas; volcados 2025 guardados. No se inventó API.
- Fuente comprobada contra la copia temporal probada; sin commit ni push, sin cambiar dibujo/permisos/configuración del cliente ni instalación.

**Evidencia**
- Antes del fix: 16 de 18 nuevas regresiones de permisos fallaron sobre la versión anterior.
- Después del fix: 69/69 tests pasan; plugin 2025 compila con 0 errores y 0 advertencias.
- Nuevo servidor: v0.1.1, contrato 6b2a8fe91890a5452113b9af, protocolo 1.
- scripts/install.ps1 -DryRun -Years 2025 pasó desde el repositorio: 69 tests, build y publicación autocontenida correctos. Bundle validado en XML, hashes registrados en el workspace; nada instalado.
- El enganche real de eventos y las nuevas respuestas de unidades todavía requieren prueba en vivo; no se asignó evidencia L a las correcciones.

**Problemas / decisiones**
- Undo, cambios descartados y cambios de vista invalidan conservadoramente el plan de save: hay que rehacer el dry run. Reabrir el dibujo no recupera una aprobación antigua.
- Se prepara la instalación con DryRun antes de pedir cierre. El contrato cambió: deben instalarse las dos mitades y reiniciarse el cliente MCP.

**Qué sigue**
- Guardar y cerrar Civil 3D con confirmación del dueño; instalar el paquete y verificar en un dibujo de ensayo (sin escribir en dibujos de cliente sin permiso explícito).
- Después, fase 1 de superficies. La prueba de busy con diálogo modal sigue pendiente.

## 2026-10-01 - Codex - Despliegue v0.1.1 autorizado
**Estado**: TERMINADO (instalación; prueba del plugin en vivo pendiente)

**Objetivo**
- Instalar v0.1.1 después de que el dueño confirmó el cierre de Civil 3D.

**Hecho / evidencia**
- acad.exe no estaba corriendo. Rutas de bundle/servidor comprobadas antes de reemplazar la instalación.
- scripts/install.ps1 -Years 2025 completado: 69/69 pruebas, plugin 2025 con 0 errores/advertencias, servidor autocontenido publicado.
- 8 archivos del bundle y 3 del servidor coinciden por SHA-256 con build y manifiesto; verificación independiente posterior completada.
- Servidor instalado 0.1.1, contrato 6b2a8fe91890a5452113b9af. Initialize responde 0.1.1; tools/list devuelve las 6 herramientas.
- health devuelve no_civil3d_instance: esperado con Civil 3D cerrado, no es una prueba del plugin cargado.
- Respaldo anterior conservado bajo _backup/20261001-112112. No se modificó configuración del cliente ni settings.json; sin commits/push.

**Qué sigue**
- El dueño reabre Civil 3D y reinicia el cliente MCP. Verificar health/query y las nuevas unidades.
- Probar confirmaciones de save en un dibujo de ensayo con permiso explícito para escrituras y perfil full_write; no escribir en dibujos de cliente sin permiso.
- Sigue pendiente busy con diálogo modal. Después, fase 1 de superficies.

## 2026-10-01 - Codex - Integración ChatGPT
**Estado**: TERMINADO (parte local); pendiente de cuenta y llamada real

**Objetivo / plan**
- Portar la integración Secure MCP Tunnel de Revit con aislamiento Civil 3D, validar e instalar auxiliares. Plan en CHATGPT_PLAN.md.
- Avance autorizado por el dueño; acceso a cuenta y llamada ChatGPT se reportarán según evidencia, sin afirmar conexión por una prueba local.

**Hecho / evidencia**
- Diez auxiliares Civil 3D aislados, identidad MCP y perfil comprobados antes de exponerlos; DPAPI, proceso verificado, entorno hijo limpio y padre restaurado.
- Instalador completo empaqueta helpers y corre tests. Instalador independiente instalado con respaldo, rollback y SHA-256; servidor/plugin permanecen 0.1.1, once archivos originales comprobados sin cambios.
- OpenAI tunnel-client completo 0.0.15 instalado, ZIP SHA-256 contra SHA256SUMS/digest. Flags reales init/doctor/run comprobados y perfil temporal generado sin credenciales ni tráfico de cuenta.
- 69/69 Core/Server; 111/111 comprobaciones con stand-in en PS 5.1 y 111/111 en PS 7.6.5. Sintaxis sin errores. DryRun/SkipTests del instalador completo correcto con pruebas ejecutadas aparte.
- Initialize/tools/list contra instalado: seis herramientas. Health: no_civil3d_instance, Civil cerrado. Estado ChatGPT pending_user_action; sin clave/perfil real ni proceso túnel.
- Guía docs/CHATGPT.md y plan CHATGPT_PLAN.md; README, STATUS, CHANGELOG y matriz actualizados. Sin commit/push, sin cambios a Revit, permisos o dibujos.

**Problemas / decisiones**
- Descubrimiento inicial confirmó ausencia del cliente/perfil. Se instaló el paquete completo oficial, no runtime-only.
- Primer port conservaba una regresión del diagnóstico general de Revit no distribuido: se sustituyó por diagnóstico del helper Civil propio. El test de sustitución de comando falló por el escape JSON de apóstrofes; se reconstruyó la fixture sin depender de reemplazo textual. Todas las suites finales pasan.
- Los flags/variables de entorno prevalecen sobre YAML en el cliente oficial: se aisló entorno y se pasa profile-file explícito para impedir que la configuración de otro producto cambie el destino.
- No hay sesión Platform ni herramientas de creación de túneles/keys disponibles en este chat. No se simuló una integración de cuenta como completada.

**Qué sigue**
- Crear túnel Civil 3D y runtime API key en la cuenta Platform; ejecutar connect-chatgpt.ps1 -Interactive, seleccionar Tunnel en app ChatGPT y llamar health desde ChatGPT con Civil 3D abierto.
- Después verificar correcciones v0.1.1 en un dibujo de ensayo y continuar fase 1 de superficies. Escrituras en dibujos de cliente solo con permiso explícito.

## 2026-10-01 - Codex - Fase 1: superficies
**Estado**: TERMINADO (primer bloque instalado; validación en vivo pendiente)

**Objetivo / plan**
- Iniciar herramientas de superficies sin esperar la configuracion de cuenta ChatGPT: lecturas list/get, muestreo y volumen; primeras escrituras rename/set_style/duplicate_style con dry run, confirmacion y relectura.
- Sondear firmas reales 2025, portar patrones validados, probar matematicas/validacion sin Civil y compilar. Preparar/desplegar solo si acad.exe sigue cerrado; no tocar dibujos del dueño.


**Hecho / evidencia**
- Rama phase-1/surfaces creada sin commits. Sondeo de DLL 2025 de superficies, propiedades, tablas/capas, display styles y color; firmas guardadas en docs/api-probes/2025. Las conjeturas de tipos que no existían se corrigieron antes de integrar.
- Diez acciones implementadas: list/get/sample_elevation/volumes_report/rename/set_style/duplicate_style/create_tin/create_volume/rebuild. Validación por acción, límites de muestras, respuesta null con motivo, estadísticas por área y comparación nativa/muestreada. create_tin vacía; capacidades pendientes no anunciadas.
- Escrituras con plan resuelto, un lock, revisión/token, commit y verificación fresca. Estilos duplicados verifican handle/nombre y display plan/model. Consultas nativas/transitorias de volumen siempre Abort.
- 119/119 Core/Server desde repo; plugin 2025 cero errores/advertencias. Dos advertencias xUnit1031 preexistentes en ServerTests. DryRun completo del instalador pasó 119 tests + 111 checks helpers PS 5.1. Helpers sin cambios; regresión PS 7.6.5 anterior sigue vigente.
- Instalada 0.2.0 contrato 176bfc22a49d728636cc039a. Última instalación -SkipTests después de suites; respaldo _backup/20261001-124001. 21 archivos verificados por hashes independientemente del instalador.
- Initialize/tools/list contra instalado: 0.2.0, siete tools, diez acciones surface. Rename sin target_document: invalid_input. Health y list válido: no_civil3d_instance. Diagnóstico ChatGPT: siete tools, pending_user_action sin daemon/clave; integración real no verificada.
- Guía SURFACES.md, changelog, matriz, STATUS y plan actualizados. Reporte y evidencia JSON entregados en outputs. Sin cambios a drawings/settings/client config/Revit, sin commits/push.

**Decisiones / límites**
- El typo del test RevisionClock.Changed se corrigió a Advance; suite final completa pasa. Ningún resultado de host se deduce de esa prueba.
- Estimaciones de grilla usan área efectiva de celda y distinguen fuera de dominio de errores API. Sin muestras válidas, área/volumen son null, nunca ceros ficticios.
- El volumen transitorio abortado puede disparar eventos y caducar tokens previos conservadoramente. Persistencia, undo y evaluación nativa requieren prueba en vivo de fixture.
- CopyAsSibling verifica todos los componentes display públicos; no se afirma verificación independiente de cada parámetro interno del estilo.

**Qué sigue**
- add_data (puntos/breaklines/límites) y paste; fixture determinista y verify-live.ps1; análisis/visualización; luego feature lines. Sondear/compilar/testear cada bloque.
- Reabrir Civil 3D y reiniciar cliente MCP; validación de nuevas escrituras solo en dibujo de ensayo autorizado. ChatGPT cuenta/llamada real se completa en paralelo, sin frenar herramientas.

## 2026-10-01 - Claude Code (Opus 5.5) - Fixture determinista + verificador en vivo + add_data/paste
**Estado**: TERMINADO (código e instalación; prueba en vivo pendiente porque Civil 3D estaba cerrado)

**Objetivo / plan**
- Retomar donde quedó Codex (v0.2.0 instalada, superficies sin prueba en vivo). Civil 3D está cerrado: no hay prueba L posible ahora.
- `HZ_BUILD_FIXTURE`: dibujo de ensayo con superficies de geometría analítica (volúmenes, pendiente y cotas conocidos), solo en un dibujo nuevo sin título y sin superficies; valores esperados guardados en JSON.
- `scripts/verify_live.py`: corre lecturas y escrituras (dry run → token → aplicar → re-leer, rechazos) contra el fixture y compara con los valores analíticos.
- `horizun_c3d_surface` `add_data` (puntos, breaklines, bordes) y `paste`, con sondeo de API previo, pruebas y compilación.

**Hecho**
- Se leyó dónde quedó Codex: v0.2.0 instalada, 10 acciones de superficies sin prueba en vivo, integración con ChatGPT pendiente de la cuenta del dueño.
- **API sondeada antes de codificar** (`docs/api-probes/2025/AeccDbMgd.phase1-adddata-paste.txt`): AddVertices, Add{Standard,Proximity,NonDestructive}Breaklines, AddBoundaries, PasteSurface, SurfaceOperationCollection y los enums.
- **`add_data` y `paste`** en `horizun_c3d_surface`:
  - validación en `SurfaceInputs` (servidor y add-in) y contrato ampliado;
  - implementación en `Commands/SurfaceCommand.Geometry.cs`;
  - la verificación re-lee como cota de la superficie cada vértice pedido y cada vértice de breakline estándar sin weeding.
- **`HZ_BUILD_FIXTURE`** (`Civil/Fixture.cs`, comando y botón de la cinta): crea un dibujo de ensayo analítico y solo actúa en un dibujo nuevo sin título y sin superficies.
- **`scripts/verify_live.py`**: regresión en vivo de unos 40 pasos contra el dibujo de ensayo.
- Versión 0.3.0 instalada con `install.ps1 -Years 2025`, con Civil 3D cerrado.

**Evidencia**
- 135/135 pruebas pasan (16 nuevas).
- El plug-in 2025 compila con 0 errores y 0 advertencias.
- Instalación: 21 archivos verificados por SHA-256. El servidor instalado reporta 0.3.0 y 12 acciones de superficie, y rechaza un vértice sin z antes de llegar a Civil 3D.
- **Sin evidencia L nueva**: Civil 3D estaba cerrado.

**Problemas / decisiones**
- El tipo con que Civil guarda un grupo de breaklines "proximity" no se afirma: el enum de 2025 no tiene Proximity. Solo se verifican standard y non_destructive.
- Un punto que queda fuera de la superficie por un borde agregado en la misma llamada se reporta y no cuenta como acierto.
- Lección repetida: editar con python dentro de un heredoc corrompe `\n`, `\f` y `\v`. Usar Write/Edit, o scripts .py escritos con Write. Ya se escaneó el repo: no quedan caracteres de control.

**Qué sigue**
- El dueño abre Civil 3D y reinicia Claude Desktop. Luego `NEW` + `HZ_BUILD_FIXTURE` + `python scripts/verify_live.py`. Corregir los FAIL y anotar la evidencia L.
- Después: análisis de elevaciones y pendientes, y style_display (`PHASE1_PLAN.md` §3).

## 2026-10-01 - Claude Code (Opus 5.5) - Bug en vivo: JSON dentro de acad.exe (v0.3.1)
**Estado**: TERMINADO (código); falta instalar, porque Civil 3D está abierto

**Hecho / evidencia**
- El dueño abrió una copia de un dibujo real de cliente en `Desarrollos\Civil3D MCP\`, no el dibujo de ensayo. `surface list` funcionó en vivo (32 superficies), pero `health` falló con `transport_failed`.
- Log del add-in: la excepción indica que JsonSerializerOptions debía especificar un TypeInfoResolver. Dentro de acad.exe la serialización JSON por reflexión está desactivada, y `JsonArray.Add("texto")` crea valores que la necesitan.
- Corrección: 14 llamadas pasan a `JsonValue.Create`, y las opciones compartidas declaran `DefaultJsonTypeInfoResolver`.
- El proyecto de pruebas ahora corre con la misma restricción. Así se reprodujo el fallo y luego se comprobó la corrección: 138/138 pruebas, plug-in con 0 advertencias.
- La regla quedó escrita en `CLAUDE.md` (puntos 10 y 11).

**Qué sigue**
1. El dueño guarda y cierra Civil 3D.
2. Se instala la v0.3.1.
3. El dueño reabre Civil 3D, ejecuta `NEW` y `HZ_BUILD_FIXTURE`.
4. Se corre `python scripts/verify_live.py`.
- Instalada la v0.3.1 tras el cierre de Civil 3D: 138 pruebas, 21 archivos verificados por SHA-256 y el mismo contrato.

## 2026-10-01 - Claude Code (Opus 5.5) - Primera corrida en vivo del dibujo de ensayo (v0.3.1 -> v0.3.2)
**Estado**: TERMINADO (corrección compilada); pendiente instalar y repetir la corrida

**Evidencia en vivo**
- `verify_live.py` sobre `HZ_BUILD_FIXTURE` (Drawing1.dwg): 18 de 36 pasos PASS. Informe: `%USERPROFILE%\.horizun\civil3d\fixtures\verify-20261001-151952.json`.
- Todas las lecturas son exactas frente a los valores analíticos y todos los rechazos funcionan.
- Todas las escrituras se rechazaron con `stale_plan`. Ninguna se aplicó.

**Causa y corrección**
- El observador de revisión (de Codex) contaba como cambios del dibujo los eventos que generan las propias lecturas del MCP.
- Se agregó `DrawingRevision.Quiet()` en `Dispatcher.RunNext`, y `Bump()` tras cada commit y cada save.
- v0.3.2: 138/138 pruebas y el plug-in compila con 0 advertencias.

**Qué sigue**
- Cerrar Civil 3D, sin guardar el dibujo de ensayo; instalar la v0.3.2; reabrir; `NEW` y `HZ_BUILD_FIXTURE`; correr `verify_live.py` de nuevo.
- Segunda corrida (v0.3.2): de nuevo 18/36. Al comparar dos dry runs idénticos, el contador estaba en 0, pero el id de vida del observador cambiaba en cada llamada. `doc.Database` devuelve un objeto .NET nuevo en cada acceso, y la ConditionalWeakTable indexada por ese objeto perdía al observador.
- v0.3.3: los observadores se indexan por `Database.UnmanagedObject`. Compila con 0 advertencias. Falta instalar y hacer la tercera corrida.

## 2026-10-01 - Claude Code (Opus 5.5) - Tercera corrida en vivo y UNDO (v0.3.3 -> v0.3.4)
**Estado**: TERMINADO (código); pendiente instalar la v0.3.4 y hacer la cuarta corrida

**Evidencia en vivo (v0.3.3)**
- `verify_live.py`: 41/43 PASS. Informe: `fixtures\verify-20261001-152959.json`.
- Todas las escrituras de superficie pasaron con verificación: rename, duplicate_style, set_style, create_tin, add_data (vértices, breakline con cota re-leída, borde outer con área exacta de 6400) y paste (cota de EG en el pegado).
- create_volume salió stale después de un paste: Civil actualiza objetos dependientes en segundo plano. Re-ensayado con el dibujo en reposo, pasó con verificación y leyó 5833.333/833.333 del objeto nuevo.
- **UNDO**: el `U` del dueño y un `_.UNDO 1` nativo (por runCommand del conector viejo, sobre el dibujo de ensayo) NO revirtieron un rename del MCP. Las ediciones en contexto de aplicación no entran al historial de deshacer.

**Corrección v0.3.4**
- Las escrituras aplicadas corren en `ExecuteInCommandContextAsync`. El mensaje de stale menciona la actualización en segundo plano.
- `verify_live.py` re-ensaya una vez si sale stale y prueba el UNDO automáticamente.
- Compila con 0 advertencias.

**Qué sigue**
- Cerrar Civil 3D sin guardar, instalar la v0.3.4, NEW, botón Dibujo de ensayo y `verify_live.py`. Si el UNDO pasa, marcar las superficies como L en `CIVIL3D.md` y `STATUS.md`.
- Riesgo a vigilar: si Civil no entra al contexto de comando (por ejemplo, el usuario arranca un comando justo en ese instante), la petición espera hasta el timeout.

## 2026-10-01 - Claude Code (Opus 5.5) - Corrida final en vivo: 46/46 (v0.3.4)
**Estado**: TERMINADO

**Evidencia**
- `verify_live.py` sobre el dibujo de ensayo: **46/46 PASS**. Informe: `fixtures\verify-20261001-201419.json`.
- Todas las lecturas y escrituras de superficies pasan, igual que los rechazos y la confirmación.
- create_volume pasa sin re-ensayo.
- **El UNDO nativo revierte la última escritura del MCP como un solo paso**: la corrección de la v0.3.4 está confirmada.
- Docs actualizados: CIVIL3D.md (L), CHANGELOG, README e Instructions.cs. Instructions.cs se despliega con la próxima instalación y no cambia el contrato.

**Qué sigue**
- Fase 1, bloque de análisis: `apply_elevation_analysis` y `apply_slope_analysis` (área y volumen reales por banda, leyenda como parámetro) y `style_display`. Agregarlos al dibujo de ensayo y a `verify_live.py`.
- Pendientes menores: busy con diálogo modal, tipo de breakline proximity, AeccDbVAlignment.

## 2026-10-01 - Claude Code (Opus 5.5) - Fase 1, bloque de análisis
**Estado**: TERMINADO (código v0.4.0); falta instalar y hacer la corrida en vivo

**Objetivo / plan**
- `horizun_c3d_surface` `apply_elevation_analysis` (modos equal, step, ranges y recolor; área y volumen reales por banda), `apply_slope_analysis` (rangos en %, área por rango) y `style_display` (visibilidad, color y capa de componentes del estilo; advierte si el estilo está compartido).
- Sondeo de API antes de codificar; tests; casos nuevos en `verify_live.py` (EG tiene pendiente analítica de 2.236 %).

**Hecho**
- Sondeo de API (`AeccDbMgd.phase1-analysis.txt`): Get/SetElevationData, Get/SetSlopeData, SurfaceStyle.GetDisplayStylePlan/Model y DisplayStyle (Visible, Color, Layer).
- Core `SurfaceAnalysisMath`: rangos equal/step con quiebre, paletas, colores ACI/RGB y área/volumen por banda. Validación en `SurfaceInputs`; contrato con 3 acciones nuevas.
- Plug-in `Commands/SurfaceCommand.Analysis.cs`: plan, token, escritura en contexto de comando, re-lectura de bandas y propiedades, y estadísticas por banda.
- `verify_live.py`: 15 pasos nuevos de análisis.

**Evidencia**
- 162/162 pruebas pasan. El plug-in 2025 compila con 0 advertencias.
- Sin evidencia L todavía.

**Decisiones**
- La pendiente siempre se expresa en %. La API guarda fracciones: evidencia indirecta (MeanGradeOrSlope 0.2525 en un terreno real) más la comprobación en el plano del dibujo de ensayo.
- Para confirmar cómo pinta Civil la pendiente, el dueño debe ver EG en amarillo (banda 2-5 %), no en morado (>30 %).
- style_display rechaza un estilo compartido sin `allow_shared_style`.

**Qué sigue**
- Cerrar Civil 3D, instalar la v0.4.0 y reiniciar Claude, porque el contrato cambió. Luego NEW + Dibujo de ensayo + `verify_live.py` y la comprobación visual del color de EG.

## 2026-10-01 - Claude Code (Opus 5.5) - Bloque largo: fase 2 núcleo (v0.5.0)
**Estado**: TERMINADO (código); pendiente UNA sesión de pruebas en vivo larga para v0.4.0 + v0.5.0

**Contexto**: el dueño pidió turnos largos de construcción y luego pruebas largas, porque cada ciclo de instalación le cuesta cerrar y abrir Civil 3D (memoria `feedback-bloques-largos`).

**Hecho**
- Core `GradingEngine`: port del `grading_engine.csx` validado, más dos correcciones de robustez: detección del colapso por desfase interior y rechazo del daylight fuera de la superficie. 17 pruebas analíticas.
- `horizun_c3d_grading` (create_geometric), `horizun_c3d_feature_line` (4 acciones) y `horizun_c3d_execute_csharp` (Roslyn 4.8.0, apagado por defecto).
- Validación en `Phase2Inputs.cs` y en el servidor. Dibujo de ensayo con plataforma. `verify_live.py` llega a unos 80 pasos. Instrucciones del servidor actualizadas.
- Sondeo `AeccDbMgd.phase2-featureline.txt`.

**Evidencia**: 200/200 pruebas; plug-in 2025 con 0 advertencias; `install.ps1 -DryRun` correcto.

**Qué sigue**: la sesión larga de pruebas. Pasos:
1. El dueño cierra Civil 3D.
2. Correr `install.ps1 -Years 2025` y reiniciar Claude, porque el contrato cambió.
3. NEW + Dibujo de ensayo.
4. `verify_live.py` y la comprobación visual (EG amarillo con borde magenta).
5. Corregir TODO lo que falle en una sola pasada antes de pedir otro ciclo.

## 2026-10-01 - Claude Code (Opus 5.5) - Estudio del mercado de MCP Civil 3D / AutoCAD
**Estado**: TERMINADO

**Hecho**
- Dos agentes de investigación: auditoría del código de Sacred-G y estudio web de 13 MCP de AutoCAD.
- Sondeo de la API 2025 para vías, etiquetas y anotación AutoCAD: `AeccDbMgd.phase3-roads.txt`, `AeccDbMgd.phase3-labels.txt`, `acdbmgd.phase3-annotation.txt`.
- Plan en `docs/handoff/PHASE3_PLAN.md`: bloques A vías, B anotación Civil, C AutoCAD y D redes/datos.

**Hallazgos clave**
- Sacred-G reporta éxito en escrituras que no hacen nada: alineamiento, PVI, recubrimiento, láminas.
- No hay corredores, vistas de sección, LandXML ni cotas en ningún MCP de Civil 3D revisado.
- Ningún MCP de AutoCAD verifica cada escritura como Horizun.
- No existe un MCP oficial de Autodesk que escriba: AutoCAD 2027 Assistant es solo lectura.

**Qué sigue**: pruebas en vivo de la v0.5.0; después, Bloque A (vías).

## 2026-10-01 (noche) - Claude Code (Opus 5.5) - Bloques A-D de PHASE3_PLAN (trabajo nocturno autónomo)
**Estado**: TERMINADO (2026-10-02, v0.6.0)

**Instrucción del dueño**: "sigue directo con todos los bloques y no pares hasta tenerlos todos, luego hacemos pruebas de todos... no pidas confirmación". Civil 3D queda ABIERTO toda la noche: no se instala nada ni se cierra acad.exe. Se compila, se prueba sin Civil 3D y se amplía `verify_live.py`, todo listo para UN ciclo de instalación y pruebas.

**Plan**
1. Infraestructura: `WriteFlow` en el plug-in (dry run, token, commit en contexto de comando, re-lectura) y `ToolRules` en Core (validación por acción).
2. Bloque A, vías: alignment, profile, sections, corridor.
3. Bloque B, etiquetas Civil: labels.
4. Bloque C, AutoCAD: layers, entities/draw, dimensions, cad_styles, blocks, tables, layouts/pdf, cleanup.
5. Bloque D: pipes, points, data shortcuts y exportación LandXML propia.

**Si la sesión se corta**: revisar `git status` y esta entrada. Cada bloque cierra con build y tests en verde y una nota aquí.

**Avance 1, Bloque A (vías) TERMINADO sin probar en vivo**
- Archivos nuevos:
  - Core: `ToolRules.cs`, `RoadInputs.cs`, `ContractBlocks.cs`.
  - Plug-in: `Commands/WriteFlow.cs`, `Civil/Resolve.cs` y los comandos Alignment, Profile, Sections y Corridor, ya registrados en `App.CreateDispatcher`.
- Al dibujo de ensayo se le añadió `road_centerline` (5,50)-(95,50).
- `scripts/verify_blocks.py` cubre las vías con valores analíticos y `verify_live.py` lo llama antes del UNDO:
  - perfil EG = 100.6 + 0.02s;
  - K de cresta = 3.0;
  - longitud de la alineación por PIs = 89.537;
  - sección del EG a la abscisa 40 entre 101.3 y 101.5.
- Pruebas: 257/257 en verde, incluidas las genéricas de `BlockToolTests`, que cubren automáticamente todas las herramientas por acción. Compila con 0 errores.
- Pendiente en vivo: el signo del offset en `station_offset`, `IsOutOfDate` tras rebuild con ensamblaje vacío y la convención de `SectionPoint.Location`.

**Avance 2, Bloque B (etiquetas) TERMINADO sin probar en vivo**
- Archivos: `AnnotationInputs.cs` (Core), `LabelTool()` en ContractBlocks y `Commands/LabelsCommand.cs` (registrado).
- Acciones de `horizun_c3d_labels`:
  - lectura: list_styles (15 familias), list (barrido de model space filtrado por objeto), get;
  - escritura: alignment_stations, alignment_geometry, station_offset, surface_spot, surface_slope (uno o dos puntos), contour_labels, profile_pvis, station_elevation, note, segment, set_text, erase (solo etiquetas, herramienta marcada como destructiva).
- Decisión: crear una etiqueta NO exige que el objeto etiquetado sea editable, así se pueden etiquetar las referencias de accesos directos de datos. set_text y erase sí lo exigen.
- Lo que se re-lee: FeatureId (o ViewId), estilo, ancla y estación/elevación/ratio, y el texto del override.
- Pendiente en vivo:
  - confirmar que los PVI usan `GradeBreakLabelStyles`;
  - el conteo de subetiquetas de estación (se espera 5 cada 20 m sobre 90 m).
- 279/279 pruebas en verde.

**Avance 3, Bloque C (AutoCAD) TERMINADO sin probar en vivo**
- Archivos:
  - Core: `CadInputs.cs` (reglas de las 8 herramientas) y `ContractCad.cs` (esquemas).
  - Plug-in: `Civil/Cad.cs` (colores, grosores, tipos de línea, describir y validar entidades) y los comandos Layers, Entities, Dimensions, CadStyles, Blocks, Tables, Layouts y Cleanup, todos registrados.
- Herramientas:
  - `horizun_c3d_layers`: estados nativos de capa.
  - `horizun_c3d_entities`: draw de 9 tipos con re-lectura geométrica, transform verificado por punto ancla e invariantes de longitud y área, offset verificado por distancia, explode y join; el join se ensaya antes sobre una copia en memoria.
  - `horizun_c3d_dimensions`: la medida se re-lee contra el valor analítico; incluye cadena, línea base y mleader.
  - `horizun_c3d_cad_styles`: dimvars en lista blanca y escalas anotativas.
  - `horizun_c3d_blocks`: atributos, dinámicos e importación desde otro DWG.
  - `horizun_c3d_tables`: filas o CSV.
  - `horizun_c3d_layouts`: viewport, page_setup y plot_pdf, que verifica el %PDF y el número de páginas.
  - `horizun_c3d_cleanup`: purge en varias pasadas, drawing_report, xrefs y standards_check.
- Decisiones:
  - erase, delete layout, plot_pdf y purge son FULL WRITE, así que el perfil por defecto los rechaza.
  - `SetDatabaseDefaults` se llama ANTES de asignar propiedades (`Cad.New`), porque si no resetea el estilo y la altura del texto.
  - La lectura de xrefs no resuelve nada.
- Pendiente en vivo:
  - viewport `On` en un layout no activado (se usa `Layout.Initialize()` si no hay viewports);
  - cambio de layout dentro de plot_pdf;
  - conteo de páginas del PDF;
  - `JoinEntities` sobre un clon.
- 371/371 pruebas en verde. El catálogo queda en 23 herramientas.

**Avance 4, Bloque D (redes y datos) TERMINADO sin probar en vivo**
- Archivos:
  - Core: `DataInputs.cs` (reglas de pipes, points y exchange, más `NumberSet`), `Exchange.cs` (escritor y lector LandXML 1.2 y `PointFile` PNEZD, probados) y `ContractData.cs`.
  - Plug-in: los comandos Pipes, Points y Exchange (este con `Shortcuts`, que carga `AeccDataShortcutMgd.dll` bajo demanda), todos registrados. El csproj referencia `AeccDataShortcutMgd`.
- Bug encontrado por las pruebas y corregido: el lector de puntos recortaba los espacios dentro de una descripción entre comillas.

**Cierre del bloque nocturno (v0.6.0)**
- Versión 0.6.0, contrato `994cb37f0c96babb3de87993`, 26 herramientas.
- 425/425 pruebas, 0 errores y 0 advertencias de producto. El humo del servidor da tools/list = 26 y FULL WRITE rechazado.
- Los 24 comandos del plug-in coinciden con el contrato.
- Advertencias corregidas: `ProfilePVI.Station` pasa a `RawStation`; `AlignmentStationLabelGroup.Create` (estaciones menores) queda con pragma, porque es la única API.
- Documentos actualizados: CHANGELOG, README, STATUS (QUÉ SIGUE), CIVIL3D.md, ROADMAP y LIVE_TESTING. Memoria actualizada.
- NO se instaló ni se cerró Civil 3D ni se hizo commit (instrucción del dueño).
- **Qué sigue:** una sola sesión larga de pruebas en vivo de v0.4.0 a v0.6.0 (ver STATUS).

---

## 2026-10-02 - Claude Code (Opus 5.5) - Sesión larga de pruebas en vivo v0.4–v0.6 → v0.6.9, 284/284
**Estado**: TERMINADO

**Objetivo:** una sola sesión de pruebas en vivo de todo lo construido (pedido del dueño), corrigiendo lo que falle.

**Hecho:**
- 10 ciclos de prueba: cierre → instalar → NEW + Dibujo de ensayo → `verify_live.py`. Resultados: 139/226 → 107/160 → 109/160 → 252/264 → 259/275 → 278/280 → 278/280 → 278/280 → 283/284 → **284/284**.
- Versiones v0.6.1 a v0.6.9. Cada hallazgo está en el CHANGELOG.
- Hallazgos clave:
  - la red asociativa de AutoCAD (AcDbAssocNetwork) cambia en cada pausa e invalidaba los tokens;
  - **Civil 3D aborta (eNotOpenForWrite, no capturable)** si las líneas de muestreo y secciones se tocan mal: el grupo debe estar abierto para escritura, hay que muestrear las fuentes ANTES de crear las líneas y una sección pendiente nunca se lee solo para lectura;
  - el catálogo de tuberías está en mm;
  - las Null Structures no tienen fondo propio;
  - el fondo se mide bajo la batea más baja.
- Diagnóstico nuevo: trazas por etapa en `WriteFlow` (log del plug-in) y una sonda de eventos por el canal C# del conector viejo. Esa sonda tumbó Civil 3D una vez, por leer una sección pendiente solo para lectura: lección registrada.
- Pedidos del dueño durante la sesión, ya hechos:
  - íconos dibujados por código para los botones de Horizun Hub;
  - botón **"Canal C#"** (`HZ_CSHARP`) que enciende y apaga `execute_csharp` con confirmación y se apaga solo al reiniciar Civil 3D.
- Se re-registró `horizun-civil3d` en Claude Desktop, porque la config lo había perdido.

**Evidencia:** `verify-20261002-123122.json`: 284/284, Civil 3D 2025, sin cierres, UNDO incluido. 432/432 tests.

**Qué sigue:** ver STATUS: camino aplicado de FULL WRITE, botón Canal C#, dibujos reales con permiso y `execute_plan`.

**Anexo, v0.7.0 y FULL WRITE en vivo (2026-10-02 12:45)**
- Pedido del dueño: botón **"Escritura completa"** (`HZ_FULLWRITE`), con las mismas reglas que "Canal C#" (confirmación, estado visible y apagado al reiniciar Civil 3D). El MCP de Revit tiene los mismos 4 perfiles y un botón de consentimiento para Python.
- Con autorización explícita del dueño (opción A), subí el perfil a `full_write` con Civil 3D ya abierto. Hay que hacerlo después de abrir, porque el inicio del plug-in revierte las elevaciones.
- Resultado: **294/294**. erase, plot_pdf (PDF de 1 página), layout delete, purge y borrar puntos pasan aplicados.
- Al terminar borré `settings.json` (no existía antes), y health confirma `safe_write`.

---

## 2026-10-04 - Claude Code - Accesos directos en vivo (v0.7.1 → v0.7.4)
**Estado**: TERMINADO (la publicación de la v0.7.4 queda compilada, sin re-ejecutar en vivo)

**Hecho:**
- Con autorización del dueño se creó el proyecto `HZ_PRUEBA` en `Escritorio\HZ Shortcuts Prueba` (`shortcuts_project`, acción nueva). El dueño hizo "Guardar como" del dibujo de ensayo dentro del proyecto.
- Hallazgos en vivo:
  - `GetCurrentProjectFolder` devuelve solo el nombre del proyecto;
  - la lista de publicados lanza error si el dibujo activo no está asociado al proyecto (o si el proyecto aún no tiene nada publicado);
  - `CreateReference` exige asociar el dibujo anfitrión (la interfaz de Civil 3D lo hace sola);
  - publicar NO exige asociar, pero verificarlo después de un reinicio sí.
- La v0.7.3 asocia automáticamente en `shortcuts_reference`; la v0.7.4 también en `shortcuts_publish`.
- Dos instancias de Civil 3D: rechazo `ambiguous` sin elección y cambio con `horizun_c3d_target {pid}` dentro de una sesión (`mcp_call.Session`).
- La red de seguridad también baja el perfil al abrir una segunda ventana de Civil 3D.
- Al final se restauró la carpeta de trabajo original de Civil 3D y el perfil quedó en `safe_write`.

**Qué sigue:** instalar la v0.7.4 y re-ejecutar la publicación en vivo (por ejemplo HZ_FG desde hz-fuente.dwg); probar a mano los botones Canal C# y Escritura completa; GitHub (privado en HorizunGroup) cuando el dueño lo autorice.

---

## 2026-10-05 - Codex - Revisión multiagente de paridad con Revit, Navisworks, Power BI y Project
**Estado**: TERMINADO EN CÓDIGO; DESPLIEGUE Y REGRESIÓN EN VIVO PENDIENTES

**Objetivo autorizado:** comparar la estructura, instalación, seguridad y experiencia de uso de los cuatro MCP locales con Civil 3D; corregir los fallos confirmados y completar las piezas comunes que falten.

**Alcance:** cambios en este repositorio, rama `phase-0/cross-product-parity-review`. Los repositorios de referencia se consultan solo para lectura. No se instala ni se modifica la configuración real de clientes o permisos; la prueba en vivo y el despliegue se preparan después de las verificaciones locales.

**Trabajo paralelo:** transporte/servidor frente a Revit; flujo de escrituras/UNDO frente a Navisworks y Project; instalación/CI frente a Power BI y Project. El agente principal integra alcance de permisos, contrato, empaquetado de plugins y documentación.

**Base comprobada:** v0.8.0 (`131b142`), 435/435 pruebas .NET, suite PowerShell correcta y plugin 2025 compilado sin errores ni advertencias en la revisión inicial.

**Hecho:**
- Comparación documentada con Revit `e204f310`, NavisCoord 1.1.1 (`e19e49d`), Power BI 2.1.2 (`9c64386`) y Project (`af99478`). Los repos de referencia y sus cambios ajenos no se modificaron.
- Retirado el hallazgo inicial que clasificaba los permisos globales como un defecto: Revit usa el mismo alcance. Ahora botones y guías lo explican.
- Corregidos límites de transporte, entrada MCP y concurrencia, dibujos homónimos, promesas falsas del C# query, registro de UNDO para efectos externos y verificación incompleta de modificados.
- PDF/CSV/LandXML generan y validan staging. PDF conserva respaldo al reemplazar e invalida confirmación/promoción si el destino cambió.
- Instalador valida y registra Claude bajo rollback; evita cliente abierto y cambios concurrentes; conserva manifiesto previo; informa `rollback_incomplete` si una restauración falla.
- Plugin Codex/Claude, marketplace, dos skills, launcher diagnóstico/instalación, builder ZIP/MCPB y hashes de distribución. Runtime preparado `0.8.1`, contrato `a9b1dd257fd965fa8893bde3`.

**Evidencia local:**
- 445/445 tests Core/Server. Plugin 2025 release: cero errores y advertencias; quedan dos advertencias históricas xUnit1031 en tests.
- Registro Claude, rollback y bootstrap: pruebas aisladas aprobadas en PowerShell 5.1 y 7; forwarding stdio al servidor publicado probado. Enlaces simbólicos se prueban solo si Windows permite crearlos.
- 111 checks del túnel ChatGPT en PowerShell 5.1 aprobados, sin credenciales reales ni configuración de cuenta nueva.
- Builder y prueba de ZIP/MCPB aprobados: versión, scripts, skills, payload y SHA-256 correctos; prueba de metadatos disponible en CI sin Autodesk.
- Ambas skills validadas con el validador de `skill-creator`. Esquema oficial MCPB v0.4 revisado; falta importación en el cliente real.
- Paquetes preparados en `artifacts/` (ignorado): ZIP precompilado para 2025, ZIP plugin, MCPB y `SHA256SUMS`. No son una release publicada.

**Límites:** no se instaló, no se cambiaron settings/configuración real, no se ejecutaron escrituras de dibujos y no hubo commit/push/publicación. El ejecutable instalado no se encontró en la ruta estándar en esta máquina. Pruebas históricas L no se atribuyen a v0.8.1. `undo_last` de valores modificados aún necesita snapshots previos para una verificación completa.

**Qué sigue:** revisar los cambios; guardar dibujos y confirmar cierre de Civil 3D antes de desplegar servidor/add-in juntos; reiniciar cliente y probar en fixture dibujos homónimos, UNDO, canal C#, exportaciones y PDF existente + fallo. Probar importación/reinicio de plugin/MCPB y, con instrucción del dueño, publicar v0.8.1.

## 2026-10-05 - Codex - Ampliación de capacidades y revisión de mejoras aplicables
**Estado**: TERMINADO EN CÓDIGO; INSTALACIÓN Y REGRESIÓN EN VIVO PENDIENTES

**Objetivo autorizado:** revisar la cobertura real de herramientas y acciones contra los otros MCP y la hoja de ruta; ampliar capacidades que puedan implementarse con evidencia local y preparar su verificación en Civil 3D.

**Distribución multiagente:** catálogo consultable del contrato y permisos; auditoría de dibujos mediante API sondeada; inventario comparativo y priorización. El principal integra exportación DWG, documentación, pruebas y paquetes.

**Alcance:** mismo árbol de trabajo y cambios anteriores preservados; sin instalación, configuración real, escrituras de cliente, commit ni publicación. Las capacidades nuevas no se marcan L sin prueba en vivo.

**Hecho:**
- Catálogo derivado del contrato: **28 herramientas y 151 acciones**, seis herramientas sin enum de acción. 157 filas: 51 Read, 93 SafeWrite, 11 FullWrite, una HostState y una UnsafeCode.
- Nueva herramienta server-side `horizun_c3d_capabilities`: búsqueda por herramienta/efecto/texto, schemas y permisos; no atribuye prueba al anfitrión ni soporte L.
- `horizun_c3d_audit`: unidades, coordenadas, xrefs, referencias stale/invalid y superficies/corredores fuera de fecha. Conteos desconocidos/ilegibles y resumen parcial explícitos.
- `exchange export_dwg`: clona el estado actual, genera staging, lo reabre y compara unidades/conteos estructurales antes de promover; nunca sobrescribe ni guarda el origen. No agrupa referencias externas ni demuestra cada valor de diseño.
- Cuatro acciones de presión: listado y lectura paginada de redes/piezas/conexiones, creación de red vacía con superficie/capa opcionales y renombrado con rechazo de referencias. API sondeada, carga diferida de DLL y relectura tras commit; quedan pendientes catálogo/piezas/conexiones editables/hidráulica.
- CSV/LandXML pasan a FullWrite de acuerdo con efectos de archivos. LandXML usa las unidades de diseño Civil y distingue pie internacional de US survey foot; no presupone metros por INSUNITS sin unidades.
- Fixture actualizado para permisos de exportación, DWG, presión y auditoría; solo se comprobó su sintaxis. Informe `CAPABILITY_REVIEW.md`, catálogo reproducible, hoja de ruta, README, changelog y matriz de evidencia actualizados.

**Integración multiagente:** servidor entregó catálogo/pruebas; plugin entregó auditoría/sonda; instalación entregó el generador de inventario. Los agentes auxiliares alcanzaron su límite de uso antes del cierre; el principal completó la revisión de gaps, presión, DWG e integración y verificó el conjunto. No se presenta una revisión independiente posterior de todo el código añadido por el principal.

**Evidencia:**
- **473/473 Core/Server**, cero fallos. Build Release 2025 con cero errores/advertencias; quedan las advertencias históricas de xUnit en tests cuando se recompilan.
- Firmas nuevas en `docs/api-probes/2025/AcDbMgd.dwg-export.txt`, `AeccDbMgd.audit-2025.txt`, `AeccDbMgd.export-units.txt` y `AeccPressurePipesMgd.audit-network.txt`.
- Bootstrap PowerShell 5.1 y 7: aprobado contra el servidor recién publicado a staging, incluyendo stdio y `capabilities` con conteos iguales a tools/list. En 5.1 el fixture de symlink no estaba disponible; el resto pasó. La suite 7 sí completó ese fixture.
- Builder y gate ZIP/MCPB: aprobados con la nueva release ZIP; skill de workflow actualizada y validada; sintaxis de `verify_blocks.py` correcta.
- Artefactos actuales regenerados en `artifacts/`: ZIP 2025, plugin ZIP, MCPB y SHA256SUMS. Contrato **`60ab4ce4eb7756190fa7f4bf`**, versión preparada 0.8.1. Sustituyen los paquetes de la preparación anterior, no una release publicada.

**Pendientes y qué sigue:** guardar dibujos y confirmar cierre antes de instalar servidor/add-in juntos, según WORKFLOW; reiniciar cliente y ejecutar regresión sobre fixture. Validar unidades metric/international/US foot, copia DWG con cambios no guardados, exportación denegada bajo safe_write, auditoría y presión. Después: snapshots de UNDO, adaptadores transaccionales de execute_plan, catálogo/conexiones de presión y cómputos de materiales. Sin prueba en vivo nueva, sin soporte declarado para 2026/2027, sin instalación/configuración real, commit, push ni publicación.

## 2026-10-05 - Codex - Benchmark externo de herramientas Civil 3D
**Estado**: TERMINADO — BENCHMARK DOCUMENTAL/DE CÓDIGO

**Objetivo autorizado:** comparar Horizun contra MCP públicos, Dynamo/Camber y automatización especializada de Civil 3D; identificar brechas reales con fuentes primarias actuales y priorizar mejoras. El benchmark es documental/de código; no se presentan tiempos ni tasas de éxito de competidores que no se hayan medido.

**Alcance:** lectura de repositorios/documentación públicos y del contrato local; informe reproducible `docs/BENCHMARK.md`. Sin instalación de competidores, cambios de dibujo, configuración real, commit ni publicación. Se mantienen los cambios del bloque anterior.

**Hecho:** informe con siete referencias: Sacred-G, DaniGhosy, xuantinhnbs-rgb, Dynamo 2026/2026.2, Camber, CTC CIM Project Suite y Grading Optimization. Tres MCP y Camber fijados por SHA; fuentes cacheadas solo para lectura en `.local/benchmark/`, ignorado. Matriz por flujo, prioridades y protocolo de 14 tareas con resultados analíticos/relecturas esperadas; protocolo no ejecutado.

**Hallazgos:** faltan targets/geometría aplicada de corredores, cantidades por material/estación, construcción completa de presión, drenaje/hidráulica, espirales/peraltes, planos dinámicos y reportes as-built/Excel/Power BI. Los volúmenes de superficies, consultas genéricas, layouts/PDF y CSV/LandXML existentes se reconocen como cobertura parcial pertinente. Los stubs QTO de Sacred-G, operaciones planned de Dani y su búsqueda de sheet sets por reflexión no se presentan como ventajas ejecutadas. Camber tiene aviso de fin de mantenimiento activo; los nodos de drenaje 2026 no prueban disponibilidad 2025.

**Evidencia/límites:** revisión de código público fijado y documentación primaria Autodesk/CTC; ningún competidor instalado/compilado/ejecutado. No hay tiempos, tasas de éxito, ranking de exactitud ni pruebas L nuevas. El gate previo de 473 tests/build 2025 sigue registrado, no fue reejecutado por cambios exclusivamente documentales. Se actualizaron STATUS, CAPABILITY_REVIEW, CHANGELOG y CIVIL3D.

**Qué sigue:** validar/desplegar la v0.8.1 preparada por el proceso existente; completar recuperación/UNDO y planes transaccionales. Primer paquete de ingeniería recomendado: targets de corredores + geometría aplicada + cantidades trazables (P1); luego catálogo/piezas/conexiones de presión. Sondear las APIs antes de implementar y usar fixtures analíticos. La siguiente fase de benchmark medido requiere ejecutar los productos sobre fixtures equivalentes y años comparables.

## 2026-10-05 - Codex - Interoperabilidad de topografía Civil 3D → Revit
**Estado**: TERMINADO EN CÓDIGO — TRANSFERENCIA REAL Y VALIDACIÓN ESPACIAL PENDIENTES

**Objetivo autorizado:** priorizar exportación/interoperabilidad y preparar un flujo ejecutable para enviar una superficie TIN de Civil 3D a Revit, junto con los requisitos de corredores, cantidades, planos y comparación construido/diseñado seleccionados por el dueño.

**Plan:** aprovechar el importador tipado existente de Revit (`horizun_create_elements`, `kind=toposolid`, `landxml_path`), preparar exportación/paquete validado con unidades, caras visibles, coordenadas y procedencia; generar petición dry-run sin adivinar modelo/tipo/nivel ni modificar coordenadas compartidas. Registrar límites de retriangulación y contraste espacial. Sondear cualquier API nueva y probar sin anfitrión primero.

**Estado del anfitrión:** health de Revit rechazado por diálogo modal persistente; la llamada no comenzó. Sin operación sobre modelos. Se continúa con desarrollo/pruebas locales, sin instalar ni cerrar diálogos. Revit permanece solo como referencia de código en este bloque.

**Hecho:**
- `exchange export_revit` FullWrite: una TIN visible → ZIP nuevo con terrain.xml y manifest; sin guardar el origen ni adelgazar automáticamente. Conserva XYZ/caras en el archivo, unidades Civil exactas, origen/revisión/handle/contrato, controles, bounds, área y elevación min/max/promedio. El importador nativo por puntos vuelve a triangular y no promete breaklines/huecos/bordes.
- Plan atado a hashes de toda la geometría y manifest; escritura atómica y reapertura/comparación de todos los bytes antes/después. Refusa TIN inválida, coordenadas XY conflictivas, caras repetidas/degen/índices inválidos, bordes no manifold y límites 20000 vértices/40000 caras.
- `prepare-revit-terrain.ps1`: extracción acotada, hash/nombres/counts/unidades/DTD, destino nuevo y petición de ensayo para el receptor tipado existente. Tipo/nivel/modelo explícitos; no cambia coordenadas ni llama al anfitrión. Cobertura no convexa requiere revisión explícita. Helper incluido en runtime y plugin/MCPB; gate añadido a CI.
- LandXML pasa a precisión round-trip. Catálogo actual: **28 herramientas / 152 acciones**, 158 filas (51 Read, 93 SafeWrite, 12 FullWrite, una HostState, una UnsafeCode), contrato **`b5cfe04b65b9b303d9241d4b`**.
- Documentación INTEROPERABILITY, README, benchmark/roadmap/capacidades, matriz y skill actualizadas. Regresión futura de export_revit añadida a verify_blocks.py; sintaxis validada, no ejecución en Civil.

**Evidencia:** 490/490 Core/Server; plugin 2025 cero errores/advertencias. PS 5.1: 21 checks de helper; PS 7: 29, incluyendo Core real → preparador → lector LandXML real del repo Revit, sin ejecutar su API. Bootstrap 5.1/7 aprobado contra servidor publicado a staging (symlink omitido en 5.1 por falta de fixture). Suite ChatGPT 5.1 aprobada; paquete ZIP/plugin/MCPB y SHA-256 aprobados. Skill validada; PyYAML se añadió solo a `.local/skill-validation` ignorado por falta de dependencia en el Python local.

**Problemas resueltos:** build inicial falló por Array.Reverse devolviendo void; se usó Enumerable.Reverse. Eliminada comprobación redundante TinVolumeSurface incompatible con TinSurface. Builder plugin falló en PS 5.1 sin OutDirectory por evaluar PSScriptRoot al enlazar parámetros; cálculo trasladado al cuerpo y ejecución por defecto probada.

**Artefactos actuales** (reemplazan las preparaciones anteriores, no publicados/instalados):
- Release ZIP: `1930953b6bccdc5538a52fdebd78c7fa4b0d2c3d177bbbf91ade28850a0cce3f`.
- Plugin ZIP: `5784169254d88c9155ca77d12fd572b83eb8e4658f923d2e625cf84fe4a8a3b0`.
- MCPB: `45b2ce442f12d131b4ca24dc802be7e5ffa1165b2abf5e928a6081dc1979ab84`.

**Límites y qué sigue:** servidor Civil instalado sigue ausente en ruta estándar; Revit health bloqueado por modal. No modificación de dibujos/modelos, configuración real, instalación, commit/push ni prueba L nueva. Se pidió asincrónicamente DWG/superficie y modelo destino para prueba real; falta respuesta. Desplegar por WORKFLOW (guardar/cerrar Civil y confirmar primero), usar fixture y resolver el modal por la persona. Comprobar origen/rotación/cota/nivel y puntos interiores de Revit con controles independientes; el lector tiene límites de rotación/Z aún sin medición en anfitrión. Siguiente código: malla exacta/3DFACE/DirectShape, luego geometría aplicada/targets/cómputos. Los reportes RMSE, división/unión, planos dinámicos y edición por hojas de cálculo seleccionados siguen pendientes; no se atribuyen a este bloque.

## 2026-10-05 - Codex - Compatibilidad Civil 3D 2024 y 2026

**Estado:** TERMINADO el bloque de preparación; compatibilidad del add-in completo en 2024/2026 pendiente de sus DLL y pruebas en vivo.

**Objetivo:** adaptar Core/add-in/selección de runtime e instalador para 2024 (.NET Framework 4.8) y 2026 (.NET 8 hasta 2026.2.1; .NET 10 desde 2026.2.2), conservando 2025 y el contrato. El dueño confirma que no tiene 2024/2026 y pide asumirlas: preparar rutas de compilación y pruebas sin atribuir evidencia de sus DLL ni prueba en vivo.

**Plan:** portar APIs BCL incompatibles; probar Core en net48 y .NET moderno; comprobar que instalador/compilación rechazan referencias ausentes o runtime incorrecto; registrar requisitos de compilación y validación por año. Las DLL Autodesk disponibles son únicamente 2025. No instalación ni modificación de configuración/dibujos.

**Hecho:**
- Core `net48;net8.0;net10.0`: finitud, SHA/token/comparación, rutas absolutas, reemplazo de discovery, epoch, split y lecturas exactas compatibles; marcadores/compiler polyfills solo Framework. El servidor continúa net8 autocontenido.
- Add-in preparado para net48 2024 y variantes net8/net10 2026. Constructor de pipe ACL compatible con Framework; guard de año/CLR antes de publicar discovery o resetear sesión C#. `health` informa `build_runtime`. No API Autodesk nueva inventada ni sustitución de DLL de otro año.
- Inspector PE offline del servidor: lee identidad/TargetFrameworkAttribute de acmgd/AeccDbMgd sin cargar código Autodesk. Instalador lo usa para selección automática; host-builds.json vincula paquete a versiones exactas. R24.3 añadido; raíces de referencias explícitas; prebuilt incompatible rechazado antes de copia.
- Probe offline acepta `--acad-dir`/`--runtime-dir` y selecciona mscorlib Framework o BCL moderno correspondiente. Solo probado contra DLL 2025; registro host-runtime.txt añadido.
- Runtime.Tests enlaza pruebas existentes y el transporte/logging real del plugin, reemplazando únicamente Dispatcher por un double sin Autodesk. CI, solución, guía COMPATIBILITY, README, skill y matriz actualizados.

**Evidencia:** 492/492 Core/Server; **335/335 en cada net48/net8/net10** (suites solapadas, no 1497 casos distintos). Incluye contrato idéntico `b5cfe04b65b9b303d9241d4b`, autenticación del pipe real y exportación TIN. Add-in 2025 compila cero errores/advertencias. Ocho gates PS 5.1 de selección/metadata/año aprobados. 2024/2026 fallan correctamente por DLL ausentes; 2024 con referencias 2025 se rechaza por 25.0 frente a 24.3 antes de binding. Suite completa del instalador en modo PackageOut aprobada, incluyendo registro/rollback y ChatGPT; no instalación. Paquete extraído: DryRun acepta coincidencia y rechaza fixture con runtime cambiado. Plugin ZIP/MCPB, hashes y skill validados; git diff --check sin errores.

**Problemas/decisiones:** se corrigieron marcadores C# ausentes, BCL moderna, casing de identidad AcMgd, raíz del resolver con separador final y `$host` reservado de PowerShell (ahora `$hostInfo`). Los tests Framework requieren fijar el switch STJ desde el módulo de pruebas porque el test host no aplica runtimeconfig; no se alteró el AppContext global del producto. El lector web falló inicialmente en la guía 2026; contenido directo HTTP Autodesk confirma 2026.2.1/net8 y 2026.2.2/net10. Revisión automática rechazó un comando de fixture que incluía limpieza recursiva, solo con motivo "blocked by policy"; se ejecutó la comprobación sin borrado. Fixture temporal conservado bajo `%TEMP%/hz-c3d-compat-package-f360c3acf1224518914d53408d7d5aa7` (datos sintéticos, no instalación).

**Artefactos actuales** (solo 2025, sustituyen los hashes preparados anteriores; no publicados ni instalados):
- Release ZIP: `aecf6638f481d1ea15b443ffbba1cb972efddbb28f7957d1fbee9bff3aacf2e7`.
- Plugin ZIP: `a323368366220f3d88d4866e4ca51393c4049aa11c794c214f0b92ab4c58cc9c`.
- MCPB: `07cf47361c7803cb33c3cb343a90a98b28f9e78a0ecf413b8cc5ad2d3b2c9569`.

**Qué sigue:** obtener DLL Autodesk de 2024 y 2026 en sus dos familias de runtime; sondear todas las APIs utilizadas, compilar el add-in completo, resolver diferencias y generar paquetes correspondientes. Después guardar/cerrar/confirmar por WORKFLOW, desplegar y correr el fixture por año/actualización. El dueño ya confirmó que no las tiene: no pedir otra vez su instalación. Continuar en paralelo la ruta Civil → Revit seleccionada, con ubicación y fidelidad medidas antes de afirmar transferencia verificada. Sin commit/push, configuración real ni prueba L nueva.

## 2026-10-05 - Codex - Finalización de compilaciones 2024/2026

**Estado:** CERRADO: compilaciones y paquetes terminados; aceptación en anfitriones reales pendiente.

**Objetivo:** completar el add-in/paquetes 2024 y 2026 tras "termínalo", obteniendo referencias verificables sin instalar Autodesk ni asumir prueba L. Examinar fuentes oficiales y distribuciones de referencias; verificar identidad/framework/firma antes de usar archivos. Corregir diferencias solo con sondeo de APIs reales. La ausencia de 2024/2026 confirmada por el dueño permanece vigente.

**Resultado:** DLL Autodesk auténticas verificadas con Authenticode válido. Civil3D.NET oficial 13.6.1781/13.8.1516, Speckle.AutoCAD.API 2024.0.0/2026.0.0 (DLL firmadas Autodesk), AutoCAD.NET/Core/Model oficiales 25.1.1 para net10. Descargas ignoradas en `.local`; firmas/hashes de DLL en `docs/api-probes/reference-provenance-20261005.json`. Sondeos offline por año guardados. Ningún paquete NuGet de Autodesk fue ejecutado ni redistribuido.

**Correcciones:** exclusión explícita de todos los obj/bin al compilar años distintos; enums no genéricos, encoding Latin1 por código 28591, Zip/Contains/FirstOrDefault compatibles con Framework. Superficies 2024 protegen referencias usando propiedades existentes, sin inventar flags cloud-worksharing. Inspector acepta SDK Framework 4.7 para net48 y bibliotecas Civil net8 con AutoCAD net10.

**Pruebas:** 496 Core/Server; 335 pruebas en cada net48/net8/net10; registro Claude, rollback instalador y túnel PowerShell 5.1 pasan. Compilación add-in 2024: cero errores, cinco advertencias nullable; 2025 y 2026 net8/net10: cero errores/advertencias. ZIP reabiertos: metadatos correctos y ninguna DLL Autodesk. El build net10 usa Civil3D.NET/net8: aún se deben medir las bibliotecas Civil de la actualización de destino y comprobar coincidencia exigida por el instalador.

**Artefactos:** canónico `artifacts/horizun-civil3d-mcp-0.8.1.zip` incluye 2024/2025/2026 net8, SHA-256 `6e7d33076f52e30c4cb5778827824f85adef12a1fa2d6e88eb22086b8dfb3a8e`; separado `horizun-civil3d-mcp-0.8.1-2026-net10.zip`, SHA `cb09a910e50a62b439e27628af06a1fec3188bc5752d6a6579a094dbee9581b2`. Plugin ZIP regenerado SHA `30ba7497f658e0d39a79480d7f9b30be7703a26d1b3d9d6fff50914e7e21312c`; MCPB SHA `4bbbfe1c23de5e14d8439e7183a34265108d8d8d690025ca346ef3db0b4dcb22`. No instalación, configuración real, publicación, commit ni push.

**Qué sigue:** ejecutar aceptación en Civil 2024/2026 con referencias de su actualización; instalar solo después de guardar/cerrar confirmado según WORKFLOW. Luego ensayar Toposolid Revit con coordenadas y fidelidad verificadas. Las ampliaciones de corredores/RMSE/QTO/hojas siguen en el backlog; este bloque termina la preparación binaria por año.

## 2026-10-05 - Codex - Ampliaciones de ingeniería e interoperabilidad

**Estado:** TERMINADO código, revisión y paquetes; despliegue/ensayo nativo pendientes de confirmación de guardar/cerrar.

**Objetivo:** registrado EN CURSO antes de implementar: comparación construido/diseñado, targets/geometría/split-merge/cantidades de corredores, CSV editable, viewports vinculados y fidelidad TIN hacia Revit. El dueño pidió hacerlo de una vez y después eligió ensayo con archivos nuevos. Esta entrada inicialmente estaba al inicio; se mueve al final conservando su objetivo y evidencia.

**Hecho:** v0.9.0 preparada, 28 tools/163 acciones/169 filas, contrato `171049b89c39afc5120da6ff`. Tres agentes (comparison, corridor_engineering, interop) implementaron y auditaron sus bloques; root integró contratos, permisos, viewports, fixture, compilaciones y paquetes. Compare_design: muestras explícitas, desviación construido-diseñado, tolerancias, min/max/media/MAE/RMSE y cobertura nula con motivo. Corredores: get/set_targets, applied_geometry, region_quantities por integración estimada de áreas, split/merge nativos con snapshot completo de las definiciones soportadas; offsets/overrides/incompatibilidades se rechazan. COGO CSV: fuente/unidades/identidad de filas ligadas, ediciones verificadas; guardas de bloqueo/levantamiento/proyecto y export sin UNDO DWG. Viewports: referencia persistente al alineamiento, cámara con transform WCS/DCS y refresh explícito; cordón local estimado declarado.

**Interoperabilidad:** paquete agrega OBJ preservando triángulos/huecos con hash. Receptor Python de Revit crea DirectShape separado de Toposolid, transforma shared→internal, reread postcommit y rollback por defecto; apply liga hash de plan/paquete/documento/ubicación. API Revit 2025 sondeada, canal nunca autoactivado. Helper `-ExactMesh` prepara petición sin exigir type/level de Toposolid. Guías nuevas y CI pruebas Python incluidos. `.local/engineering-fixture-20261005` contiene plano sintético 100m², XML/OBJ/ZIP/CSV y comparación esperada RMSE0.02m; peticiones dry-run Civil/new_project Revit. No DWG/RVT nativos creados sin anfitrión.

**Evidencia:** 544 Core/Server y 383 pruebas solapadas por net48/net8/net10; siete guardas Python; helper terreno 21 PS5.1 y 35 PS7 con lector Revit real; instalador registro/rollback/túnel pasan. Builds completos 2024/net48 (cero errores, advertencias nullable previas), 2025/net8 y 2026/net8/net10 (cero errores/advertencias). Probe revela 2024 sin UseSameSideTarget: omitido/nullable en lectura, opción explícita rechazada antes de escribir. Plugin bootstrap pasa, ocho runtime gates pasan, plugin ZIP/MCPB con payload/hash coincidentes pasa. ZIP reabiertos: ninguna DLL Autodesk y receptor incluido. Instalador prebuilt DryRun2025 verifica coincidencia sin instalar. `git diff --check` limpio (avisos LF/CRLF existentes).

**Artefactos:** release 2024/2025/2026 net8 SHA `891c7291f31215ba76b05510fdaa173db04b57dc6f2a0c39e8a008ab59866c76`; 2026 net10 `1b14059ddb750b4eefca70758c2fcbf3ec8d412314a41bdebce71650ad00b221`; plugin ZIP `5c219d22cd86e9a1b1358b50cf70926c41ebaf5fed157dd18afeac1568e67a3c`; MCPB `9392c4c22af14ddbbf7d46797af79e7ccabbfe91bceaf93f14a356beff8352ae`. Todos en artifacts/, no publicados. Stage prebuilt listo `.local/install-090`.

**Límites/qué sigue:** health Civil falla por servidor estándar ausente; Revit responde no reachable. Procesos acad/Revit cerrados, no control de pantalla. Se solicitó confirmación requerida por WORKFLOW de trabajo guardado/Civil cerrado antes de instalar; pendiente. No configuración real, commit/push, publicación ni L nueva. Tras confirmar: instalar2025, abrir anfitriones, fixture DWG/modelo nuevo autorizados por API, ensayar geometría/CSV/targets/split-merge/viewports/UNDO y transferencia Toposolid/DirectShape con controles independientes. 2024/2026 y runtime/patch de destino todavía requieren aceptación real; QTO es estimado y viewports se refrescan explícitamente, no son viewframes nativos automáticos.

## 2026-10-05 - Codex - Instalación y revisión v0.9.0

**Estado:** TERMINADA instalación/revisión de archivos y servidor; arranque del add-in y aceptación en dibujo pendientes de intervención de inicio.
**Objetivo:** el dueño respondió "instálalo y revísalo" a la confirmación solicitada. No se detectan acad/Revit abiertos. Instalar paquete verificado para 2025 disponible, verificar hashes/versión/contrato/herramientas y health; intentar comprobación del anfitrión mediante API cuando esté disponible. Sin configurar canales inseguros ni modificar datos de cliente.

**Hecho/evidencia:** hash ZIP canónico coincide `891c7291f31215ba76b05510fdaa173db04b57dc6f2a0c39e8a008ab59866c76`. Instalación prebuilt `.local/install-090/install.ps1 -Years 2025` completada: 44 bundle+15 servidor. Revisión independiente de cada SHA contra manifiesto instalado pasa59/59. Exe v0.9.0, contrato `171049b89c39afc5120da6ff`; catálogo por stdio instalado confirma28tools/163acciones/169operaciones. Tolerancia negativa y handle inválido rechazados invalid_input; export_editable_csv denegado permission_denied bajo safe_write. No settings.json creado, ninguna config de clientes editada.

**Arranque:** se inició Civil2025 con argumentos reales del acceso directo Metric, Start-Process WindowStyleHidden (sin control de pantalla/teclado). Procesoacad50600 carga módulos Civil y CLR; no discovery/logplugin tras dos esperas espaciadas. health sigue no_civil3d_instance; COM AutoCAD.Application.25 no está en ROT (Operation unavailable). AdskLicensingService Running; sin evento Application erroracad detectado. No se atribuye licencia/modal como causa sin evidencia. No proceso terminado/cancelado ni dibujo modificado. Se pidió al dueño completar inicio, abrir dibujo nuevo, HZ_STATUS y HZ_BUILD_FIXTURE si publicado.

**Qué sigue:** resolver inicio/carga con el mensaje HZ_STATUS; cuando publique ejecutar fixture autorizado y aceptación de nuevas acciones, más Toposolid/DirectShape Revit. Reiniciar cliente MCP para contrato actualizado si mantiene sesión previa. No prueba L nueva, commit/push ni publicación. 2024/2026 no están instalados aquí; sus paquetes siguen preparados.

## 2026-10-05 - Codex - Corrección del alcance del autoloader

**Estado:** TERMINADA; puente e inicio verificados en nueva instancia2025.
**Objetivo:** tras la observación del dueño, revisar carga real. Fuente primaria Autodesk Developer Blog confirma RuntimeRequirements por ComponentEntry para .NET2025; nuestro XML los situaba solo en Components. Corregir generador, gate de paquetes y manifiesto instalado con respaldo/relectura, sin tocar DLL mientras acad esté abierto. Probar nuevo arranque/API; no declarar resuelta la carga antes de health.

**Hecho:** generador install.ps1 ahora anida RuntimeRequirements en ComponentEntry y usa AppType .Net. Gate build-plugin.ps1 valida alcance/año/plataforma/tipo; pruebas modifican ZIP reales para parent_scope/wrong_year/wrong_app_type y las tres son rechazadas. Paquete antiguo rechazado antes de reparar. XML instalado respaldado en _backup/autoloader-20261005-220929, corregido y manifest.json actualizado con hash/fecha; relectura59/59 coincide. No DLL instalada sobrescrita con acad abierto. ZIPs release/net10 reparados conservando binarios, instalador actualizado y plugin/MCPB regenerados; gate completo pasa.

**Arranque real:** se inició otra instancia con argumentos Civil Metric y /t plantilla oficial métrica, visible porque el dueño pide lanzarlo, sin control de pantalla/teclado. Publica discovery/log v0.9.0, runtime net8 y contrato171049b89c39afc5120da6ff. Health responde desde el hilo principal, un dibujo nuevo en metros, busy=false. La instancia previa sin ventana sigue abierta: no fue terminada. El defecto XML queda corregido y la nueva carga funciona; no se demuestra por separado que fuese la única causa de la instancia previa.

**L2025:** sesión MCP fijada a la instancia nueva, dibujo sin guardar y generado por este arranque. Dos TINs Fixture_Terrain/Fixture_Built; cuatro aplicaciones create_tin/add_data con token y verificación status=match en transacción nueva. Plano de 100m2, Built+0.02m; compare_design 3/3 muestras válidas, mínimo/máximo/media/RMSE0.02m, dentro0.05m. Auxiliar local inicialmente esperaba verified booleano y detuvo tras primer create verificado; corregido para esquema objeto status=match y reanudado sin duplicarlo. Evidencia JSON local install-live-090.json. Dibujos de cliente intactos; fixture permanece sin guardar. No L Revit ni otros bloques todavía.

**Hashes nuevos (sustituyen artefactos anteriores):** canónico fed9115c66da9c2e286bf657c90a3e01c6e53f6cbdb8db814939fbec8e434031; net10 ab7fc7611fc4c051c3021b5708787f1120676901e3286adf12bc4ab8724f7236; plugin c5bb307bdc0a068d60a46fa4959f7811a5c08d30b2abbe296424d3c8d9dbf328; MCPB 083e9e113c5fce1d56f4a9d3e65e84ff328692409018c99376be0b63ea118798. SHA256SUMS actualizado, sin publicar/commit/push ni config cliente.

**Qué sigue:** guardar fixture si se quiere conservar; aceptación corredores/CSV/viewports/UNDO y transferencia real a modelo Revit nuevo. Los años2024/2026 requieren L en equipos con esos anfitriones. No repetir instalación2025 ni atribuir carga fallida al dueño.

## 2026-10-05 - Codex - Aceptación completa en archivos nuevos

**Estado:** TERMINADO; instalación y aceptación nativa Civil2025/Revit2025 completadas.
**Objetivo:** el dueño autoriza todas las pruebas necesarias sin más preguntas. Completar ensayos Civil2025 y transferencia Revit por API sobre archivos nuevos; corregir fallos, verificar builds2024/2026 y paquetes si cambia código. Dos agentes apoyan preparación de aceptación corredores/viewports y revisión del receptor. Root ejecuta CSV/export/arranque Revit y coordina escrituras secuenciales. No configuración de canales inseguros ni pantalla/teclado; cerrar solo nuestros documentos mediante API tras guardarlos si hace falta desplegar correcciones. No cerrar dibujos ajenos ni terminar procesos por fuerza.

**Ampliación del diagnóstico antes del registro nativo:** tras guardar/cerrar nuestros tres dibujos por API y reinstalar v0.9.1 con hashes correctos, dos arranques no alcanzan el primer log del add-in. DLL/runtime/dependencias y Zone.Identifier descartados mediante lecturas independientes. Documentación Autodesk vigente confirma confianza explícita para bundles AppData; los perfiles Civil existentes carecen de nuestra carpeta en TRUSTEDPATHS. Se prepara auxiliar de registro limitado a Contents/año, con respaldo/CAS/relectura/rollback, opción explícita del instalador y plan visible del bootstrap. La autorización del dueño para instalar y reparar este MCP cubre su carga nativa; SECURELOAD y canales C#/Python se preservan. Primero pruebas sin registro real; luego registro y aceptación en nueva instancia. No se dismissan avisos ni se matan instancias anteriores.

**Resultado final:** v0.9.1 operativa en Civil2025, generación inmutable `0.9.1-20261005-230202`; health y relectura del DWG guardado pasan. Registro nativo en los dos perfiles2025 existentes con respaldo, CAS y rollback; SECURELOAD=1 conservado. Codex registrado con CLI oficial, configuración respaldada y comparación semántica de todos los ajustes anteriores correcta. Ningún dibujo de cliente editado ni DLL cargada sobrescrita. Dos arranques anteriores sin puente permanecen sin forzar su cierre; los demás ensayos propios se guardaron/cerraron por API.

**Correcciones obtenidas del ensayo:** getters de targets que fallan por tipo/cantidad devuelven null con motivo; planos cambian/restauran layout mediante API para crear viewport en paperspace. Export DWG usa SaveAs completo en archivo temporal para conservar bloques sin referencias, hash de origen con lectura compartida y PushDbmod/PopDbmod balanceados a través del commit. Receptor Revit selecciona transformación por controles nativos, empareja triángulos con tolerancia explícita y rechaza paquetes duplicados; evidencia e IDs numéricos permiten relectura independiente.

**Evidencia nativa:** 64 controles de corredores (targets, geometría, seis códigos de cantidades estimadas, split/merge conservando parámetros); 22 en fase planos/UNDO; export DWG final8/8, origen conservado y copia reabierta con elevaciones independientes. CSV COGO editado/releído y archivo cambiado rechazado antes de escribir; comparación RMSE0.02m y fuera de dominio reportado con cobertura. UNDO elimina la superficie, pero respuesta conserva verificación parcial por snapshots de contenedores ausentes. Fallos anteriores se conservan en los JSON; el resultado final no reescribe su historia.

**Transferencia real:** Toposolid editable y malla DirectShape en `RevitTerrainFixture.rvt`, con rotación37° y controles compartidos. Segundo modelo nuevo `RevitTerrainClean091.rvt`: malla2triángulos, cero advertencias, error máximo0.002713mm frente a tolerancia explícita0.01mm; bounding box comprobada por herramienta tipada y archivo guardado. No afirmar fidelidad volumétrica del Toposolid, conservación de su triangulación ni CRS de proyecto desconocido. Revit Python utilizó autorización preexistente. Permisos Civil temporales restaurados; settings.json ausente y C# deshabilitado.

**Pruebas/entrega:** 552 Core/Server, 391 por net48/net8/net10; receptor14/14; registro de confianza34 controles tanto PS5.1 como PS7; preparador de terreno30 PS7 con Core y21 PS5.1. Compilaciones2024/net48 y2025/2026 net8/net10 sin errores;2024 conserva cinco advertencias nullable. Paquetes canónico,2026net10,pluginZIP yMCPB en artifacts/ con SHA256SUMS; gate rechaza tres ZIP de autoload defectuoso. Informe `docs/ACCEPTANCE_20261005.md`; evidencia y DWG/RVT generados en `.local/acceptance-20261005-221756/`, ignorados. Sin commit/push/publicación.

**Qué sigue:** snapshots completos para UNDO y aceptación nativa2024/2026 cuando estén disponibles; publicar los artefactos solo por instrucción del dueño. El bloque solicitado de instalación, reparación y pruebas sobre archivos nuevos queda terminado.

**Cierre independiente:** manifiesto instalado144/144 archivos coincide por SHA-256; registro Codex conserva ajustes previos; gate final pluginZIP/MCPB pasa y rechaza tres autoloaders defectuosos; git diff --check sin errores. SHA256SUMS final contiene los cuatro artefactos: canónico `af2d9647b02719eb11ffc3f13da08f23abea1cc5a997e67df2fee053b3102db3`; net10 `d52a45453abe925d36d005224aeb2053f91b0a3fe95d6cb5be6421e91cd00a15`; plugin `072712f565b64d0d68c3280f74f8dabf5de4929ed430244ac279901c50ca9836`; MCPB `fdc0e7f396763ad11d838c6ddf27c5349ee2732e986b5c2220f2b574e456f63c`.

## 2026-10-06 - Codex - Preparación para repositorio público

**Estado:** TERMINADO preparación local; publicación pendiente de autorización por bloqueo automático.
**Objetivo:** registrado antes de implementar. El dueño pide continuar hasta poder hacer público GitHub. Revisar UNDO, privacidad de código/historial/releases, documentación, CI y distribución. No inventar L2024/2026.

**Corrección de alcance:** snapshot experimental de campos nativos compiló, pero UNDO no retiró una línea y la cobertura de contenedores Civil fue incompleta. No se reintentó el resultado confirmado. Código experimental retirado; v0.9.2 rechaza undo_last antes de emitir comando, committed=false/unsupported, y todas las escrituras dejan de prometer inversa automática. La compatibilidad conserva el enum reservado. Contrato nuevo `e36ee390efbb5d34ee0fe86c`.

**Evidencia:** payload final instalado en generación inmutable `0.9.2-20261006-085000`, servidor aislado registrado en Codex;262 hashes pasan. Native2025:78/78 controles de ingeniería, tres variantes de UNDO rechazadas con relecturas idénticas. Fixture propio guardado; settings.json restaurado a ausencia y C# deshabilitado. Se guardaron/cerraron todos los anfitriones antiguos propios por COM con comprobaciones de PID/ruta/Saved/CMDACTIVE; ninguno terminado por fuerza ni pantalla usada. Revit2025 conserva aceptación previa y modelos guardados.

**Privacidad/pruebas:** 552 Core,391 en cada runtime,14 receptor y8 cliente pasan. Builds2024/net48,2025/net8,2026/net8/net10 completos. Gitleaks oficial8.30.1 con checksum verifica historial y árbol publicable; excepción exacta para una frase histórica, sin credenciales. PathMap elimina rutas en DLL/EXE, y PDB se excluyen porque retenían entradas del compilador. Cuatro paquetes pasan gate de privacidad y payload/plugin/MCPB; tres autoloaders malformados rechazados. Antiguo ZIP GitHub0.8.0 respaldado con SHA `71c3d58c8b48ab30700761ede256eb395d56b7ee2d206635690075f911334460`; falla privacidad por ruta privada en Core.dll, por lo que se retira el asset antes de abrir visibilidad, conservando tag/fuente.

**Artefactos finales:** canónico `7682d2caed7ff04a3255fe1344f21328fee93932e90d4d922418a3d0281b004a`; net10 `8a4209412edad97000b84c8d1af02e7858232b697abf23bd33a3324f0aab23a0`; plugin `1c4396a13d3af5dae38b6bcfa2872213b86a966af3e03c8de5beb200417fd849`; MCPB `126b7738a6658bf1821b9634dd3765c528374e2cfae3c4e0522fb32a1fd13049`. SHA256SUMS y runtime-release enlazan el payload final. Documentación pública explicita UNDO desactivado, cantidades estimadas, refresh explícito y evidencia B/T2024/2026.

**Qué sigue:** sincronizar fuente revisada por PR y CI, publicar release0.9.2 y abrir GitHub solo tras esas puertas. Después aceptar2024/2026 reales e investigar una inversa completamente verificada antes de reactivar UNDO.

**Bloqueo de publicación:** fuente179 archivos/20k líneas preparada en rama `codex/public-readiness-092`, staged sin binarios ni datos privados. El intento agrupado commit/push/crearPR fue rechazado antes de ejecutarse por el control automático: `approval required by policy, but AskForApproval is set to Never`. No commit nuevo, push, PR, retirada del asset0.8.0, release nueva ni cambio de visibilidad. Se solicitó autorización explícita para esas acciones después de completar código/pruebas/paquetes. Repositorio continúa PRIVATE. Escaneos finales del árbol publicable y del historial pasan sin credenciales; git diff --check limpio.

## 2026-10-06 - Codex - Publicación autorizada v0.9.2

**Estado:** TERMINADO CON BLOQUEO DE EJECUCIÓN REMOTA.
**Autorización:** el dueño responde «te doy mi autorizacion» a la solicitud de commit/push/PR, integración tras CI, retirada del ZIP antiguo respaldado, release0.9.2 y cambio de visibilidad a público.
**Plan:** publicar fuente revisada y esperar CI; conservar los cuatro paquetes sellados y probados, retirar exclusivamente el asset0.8.0 con rutas privadas, publicar0.9.2 y verificar visibilidad y hashes remotos. Actualizar estado con evidencia real.
**Resultado:** git diff --cached --check pasó y se creó commit local `bc093083cf91e8847923939809a2b8e599605b20` (179 archivos). `git push -u origin codex/public-readiness-092` fue rechazado antes de ejecutarse: `approval required by policy, but AskForApproval is set to Never`. La autorización expresa permitió el commit, pero no resolvió el control de ejecución del push. No se usó otra vía para eludirlo. Sin PR, CI remota, retirada de asset, release ni cambio de visibilidad; los paquetes sellados permanecen intactos.
**Qué sigue:** permitir el push ya autorizado desde un entorno con ejecución habilitada, incluir este cierre documental y completar PR/CI/release/visibilidad. Conservar los límites de evidencia2024/2026 y UNDO deshabilitado.

## 2026-10-06 - Codex - Reanudación de publicación

**Estado:** EN CURSO.
**Objetivo:** el dueño reitera autorización y la sesión permite escalación de ejecución. Incluir cierre documental anterior, subir rama autorizada, verificar CI y completar release/visibilidad con los paquetes sellados. No modificar evidencia nativa ni recompilar sin fallo concreto.
**Qué sigue:** commit documental y push con aprobación de ejecución disponible.
