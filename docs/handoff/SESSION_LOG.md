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

## 2026-10-08 - Claude Code (Opus 5.5) - v0.8.1: dry_run por defecto en lecturas + lectura de georreferencia
**Estado**: EN CURSO

**Objetivo:**
1. Bug visto en vivo: `entities query` y `layouts list` se rechazan con "Field 'dry_run' is not used" porque el cliente rellena el `default: true` del esquema. Arreglo genérico: un campo que no usa la acción y solo trae el valor por defecto del esquema se ignora; un valor distinto se sigue rechazando. Tests para todas las herramientas.
2. Nueva lectura `horizun_c3d_document action=geo`: GeoLocation, NORTHDIRECTION, VIEWTWIST, UCS, viewport activo, ajustes de transformación de Civil 3D y convergencia. Solo lectura, verificada en vivo sin modificar el dibujo abierto del dueño.
