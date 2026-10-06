---
name: civil3d-project-workflows
description: Consulta, modela, audita y modifica dibujos locales de Autodesk Civil 3D con Horizun Civil 3D MCP, incluyendo superficies, vías, redes, etiquetas y entregables con escrituras confirmadas y verificadas.
---

# Trabajar con Civil 3D

Empieza por `horizun_c3d_health`: identifica instancia, dibujo activo, unidades,
sistema de coordenadas y estado ocupado. Con varias instancias elige una con
`horizun_c3d_target` y conserva ese servidor durante las llamadas dependientes.
Las escrituras operan sobre el dibujo activo; usa su **ruta completa** en
`target_document`, especialmente si hay dibujos con el mismo nombre.

Consulta `horizun_c3d_capabilities` para buscar acciones por herramienta, efecto
o texto y comprobar permisos y parámetros. Es un catálogo del contrato; no
demuestra disponibilidad del anfitrión ni prueba en vivo. Para diagnóstico del
dibujo usa `horizun_c3d_audit`; conserva los contadores desconocidos y el
indicador `partial` al informar referencias o superficies fuera de fecha.

## Cambios en el dibujo

Resuelve objetos y estilos con `horizun_c3d_query` y `horizun_c3d_styles`.
Una referencia de acceso directo se edita en su fuente; un nombre ambiguo
se resuelve con las candidatas devueltas, sin adivinar.

Toda escritura tipada empieza con dry run. Lee el plan y aplica la misma
petición con `dry_run=false` y su `confirmation_token` de un solo uso.
El token vence en diez minutos y pierde validez si cambia el dibujo o el plan.
Cuando la autorización del usuario ya cubre ese cambio, continúa con la
aplicación del plan. Comprueba `committed`, `after` y cada elemento de
`verified`; una operación confirmada puede fallar en la verificación.

Antes de repetir una llamada que terminó en timeout, error de transporte o
resultado desconocido, reconsulta el estado. La operación podría haberse
aplicado. `undo_last` cubre solo la última edición DWG elegible: archivos,
configuración de proyectos y scripts no son reversibles por ese mecanismo.
Si un UNDO ya corrió pero la verificación fue parcial, reconsulta los objetos;
no envíes otro UNDO por la misma operación.

## Criterio por disciplina

- Superficies: conserva la base y usa versiones para alternativas. Mantén las notas de muestreo de estadísticas estimadas; comunica mínimo, máximo y promedio con unidades. Lee la convención de corte/relleno antes de sumar volúmenes.
- Vías: alineación, perfil, ensamblaje, corredor, superficie de corredor y luego líneas de muestreo/secciones. Importa ensamblajes completos desde DWG cuando la API no permita crear un subensamblaje de stock.
- Gradings: `create_geometric` produce geometría y TIN; identifica el resultado como grading geométrico, sin afirmar que es un grading nativo.
- Etiquetas: consulta `list_styles` y resuelve la familia correcta. Se pueden etiquetar referencias de accesos directos, pero no editar la geometría referenciada.
- Redes de presión: `pipes pressure_list` / `pressure_get` consultan redes y piezas; `pressure_create_network` crea una red vacía y `pressure_rename` cambia su nombre. La colocación/conexión de piezas y la hidráulica siguen pendientes. Las dimensiones rotuladas `api_raw` conservan la incertidumbre de unidades del catálogo.
- Archivos PDF/LandXML/CSV/DWG: exigen `full_write`; reconsulta y verifica el archivo escrito. DWG produce una copia del estado actual sin guardar el origen, con verificación estructural; no verifica cada valor de diseño ni agrupa referencias externas. Un UNDO del dibujo no restaura la exportación; conserva el respaldo al reemplazar un entregable.

## Topografía Civil 3D → Revit

Para entregar una TIN a Revit, usa `exchange export_revit` con `surface`, un
`output` ZIP nuevo y el dibujo explícito. Requiere full_write, dry run y token;
no guarda el DWG ni llama a Revit. El límite actual es 20000 vértices visibles,
sin simplificar automáticamente. Revisa unidades, controles y procedencia del
manifest; exportación verificada no significa importación verificada.

Ejecuta `scripts/prepare-revit-terrain.ps1` desde el plugin o `server/client-tools`
con paquete, destino nuevo y modelo/tipo/nivel de Revit resueltos. Emite una
petición dry-run para `horizun_create_elements`, `kind=toposolid`, `landxml_path`.
El importador interpreta las coordenadas como compartidas: comprueba origen,
rotación y datum vertical mediante controles independientes antes de aplicar.
No cambies coordenadas compartidas sin autorización que cubra ese cambio.

Revit vuelve a triangular: no afirmes conservación de caras, breaklines,
concavidades ni huecos desde la importación por puntos. El preparador rechaza
cobertura no convexa por defecto; `AllowRetriangulation` exige revisión explícita.
Verifica posición y cotas, incluyendo puntos interiores; registra desviaciones
y tolerancia, además de los checks del receptor. Conserva la base Civil.

## Límites operativos

No uses pantalla, mouse, teclado ni ESC. Si Civil 3D está ocupado, espera a que
la persona termine el comando. No escribas en dibujos reales de cliente sin
autorización que cubra esos cambios; usa el dibujo de ensayo para regresiones.

Los perfiles se comparten entre todas las instancias del usuario. El canal C#
requiere autorización del dueño y es código arbitrario sin sandbox. Incluso
`mode=query` puede dejar efectos externos o confirmar transacciones propias;
sus resultados son auto-reportados. Prefiere herramientas tipadas y verifica
con lecturas antes de declarar un cambio terminado.

Después de un bloque de cambios, recuerda guardar. `document save` requiere
`full_write`, dry run y token. No afirme soporte en vivo de años o acciones
que solo hayan compilado.
