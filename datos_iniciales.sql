/* ════════════════════════════════════════════════════════════════════════════
   DATOS INICIALES — Control de asistencia con reconocimiento facial
   Motor: SQL Server 2014 o posterior

   Carga lo mínimo para que el programa funcione de inmediato:
     • 2 carreras:  Informática Industrial (semestral, 6 semestres)
                    Mecánica Automotriz   (anual, 3 años)
     • 1 gestión ACTIVA:  II-2026 (01/07/2026 – 31/12/2026)
         Informática cursa 2do, 4to y 6to semestre · Mecánica cursa 1er, 2do y 3er año
     • 2 docentes con usuario y contraseña
     • 9 materias con su carrera, semestre/año, docente, tolerancia y horario semanal
     • 18 estudiantes DE EJEMPLO inscritos en su carrera y semestre/año (bórralos o cámbialos por los reales)

   Úsalo DESPUÉS de crear las tablas (base_de_datos_sqlserver.sql o reiniciar_base_de_datos.sql).
   Se puede ejecutar varias veces: lo que ya existe no se duplica.
     SSMS: abrir y F5      ·      sqlcmd -S DESKTOP-16132LP -f 65001 -i datos_iniciales.sql

   ACCESOS
     Administrador → el de config.json (usuario «admin»; no está en la base)
     Docentes      → usuario  erik.huallpa     contraseña  Docente2026
                     usuario  docente.prueba   contraseña  Docente2026
     Las contraseñas se guardan cifradas (PBKDF2-SHA256, igual que el programa).
     CÁMBIALAS al entrar: Académico → Docentes → «Editar / cambiar contraseña».

   ROSTROS: no se pueden cargar por SQL (son vectores que calcula la red neuronal).
   Regístralos en el programa: Estudiantes → elegir al estudiante → «Registrar rostro».
   ════════════════════════════════════════════════════════════════════════════ */

USE asistencia;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;                 -- ante cualquier error se deshace TODO (no queda a medias)

IF OBJECT_ID(N'docentes', N'U') IS NULL OR COL_LENGTH(N'docentes', N'usuario') IS NULL OR OBJECT_ID(N'fk_asistencia_materia', N'F') IS NULL
   OR OBJECT_ID(N'carreras', N'U') IS NULL OR COL_LENGTH(N'materias', N'carrera_id') IS NULL OR COL_LENGTH(N'inscripciones', N'carrera_id') IS NULL
BEGIN
    RAISERROR(N'Primero crea las tablas con base_de_datos_sqlserver.sql (o abre una vez el programa).', 16, 1);
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;

DECLARE @ahora FLOAT = DATEDIFF(SECOND, '1970-01-01', GETUTCDATE());

/* ───────────────────────── 1. carreras ───────────────────────── */
IF NOT EXISTS (SELECT 1 FROM carreras WHERE nombre = N'Informática Industrial')
    INSERT INTO carreras (nombre, sigla, regimen, duracion, activa) VALUES (N'Informática Industrial', N'INF', N'semestral', 6, 1);
IF NOT EXISTS (SELECT 1 FROM carreras WHERE nombre = N'Mecánica Automotriz')
    INSERT INTO carreras (nombre, sigla, regimen, duracion, activa) VALUES (N'Mecánica Automotriz', N'MEC', N'anual', 3, 1);
DECLARE @inf BIGINT = (SELECT id FROM carreras WHERE nombre = N'Informática Industrial');
DECLARE @mec BIGINT = (SELECT id FROM carreras WHERE nombre = N'Mecánica Automotriz');

/* ───────────────────────── 2. gestión activa ───────────────────────── */
IF NOT EXISTS (SELECT 1 FROM gestiones WHERE nombre = N'II-2026')
    INSERT INTO gestiones (nombre, inicio, fin, activa) VALUES (N'II-2026', N'2026-07-01', N'2026-12-31', 0);
UPDATE gestiones SET activa = CASE WHEN nombre = N'II-2026' THEN 1 ELSE 0 END;     -- solo una activa
DECLARE @gestion BIGINT = (SELECT id FROM gestiones WHERE nombre = N'II-2026');

/* ───────────────────────── 3. docentes ───────────────────────── */
IF NOT EXISTS (SELECT 1 FROM docentes WHERE usuario = N'erik.huallpa')
    INSERT INTO docentes (nombres, apellidos, usuario, pin, activo, creado)
    VALUES (N'Erik', N'Huallpa Aruquipa', N'erik.huallpa',
            N'2FF4F51F1157009131905C1D05AB38FB:C069884CC84AE6667CA6296D1DB143FB80A2A699DECC8CD47183268029DEFBF6', 1, @ahora);
IF NOT EXISTS (SELECT 1 FROM docentes WHERE usuario = N'docente.prueba')
    INSERT INTO docentes (nombres, apellidos, usuario, pin, activo, creado)
    VALUES (N'Docente', N'De Prueba', N'docente.prueba',
            N'110D16D0DF33A5B4E9AE055BB700ECC1:F03A0A8899C0FC93BE0710E8BDDE1B1EA5B01CBE00406559C9A700A699782E56', 1, @ahora);
DECLARE @erik BIGINT = (SELECT id FROM docentes WHERE usuario = N'erik.huallpa');
DECLARE @prueba BIGINT = (SELECT id FROM docentes WHERE usuario = N'docente.prueba');

/* ───────────────────────── 4. materias ─────────────────────────
   «nivel» = semestre (Informática) o año (Mecánica).
   Ningún curso (carrera + nivel) ni docente tiene dos clases a la misma hora.
   Tolerancia = minutos después del inicio en que todavía es PUNTUAL. */
DECLARE @m TABLE (carrera BIGINT, nivel INT, sigla NVARCHAR(20), nombre NVARCHAR(100), docente BIGINT, tolerancia INT);
INSERT INTO @m VALUES
    (@inf, 2, N'BDD-200', N'Bases de Datos I',                     @erik,   10),
    (@inf, 2, N'MAT-200', N'Matemática II',                        @prueba, 10),
    (@inf, 4, N'RED-400', N'Redes de Computadoras',                @prueba, 10),
    (@inf, 4, N'PLC-400', N'Automatización con PLC',               @erik,   10),
    (@inf, 6, N'IAR-600', N'Inteligencia Artificial y Robótica',   @erik,   10),
    (@inf, 6, N'INS-600', N'Ingeniería de Software',               @erik,   10),
    (@mec, 1, N'MEC-110', N'Fundamentos de Mecánica Automotriz',   @prueba, 10),
    (@mec, 2, N'MEC-210', N'Motores de Combustión Interna',        @prueba, 10),
    (@mec, 3, N'MEC-310', N'Sistemas Eléctricos del Automóvil',    @prueba, 10);

INSERT INTO materias (gestion_id, carrera_id, semestre, sigla, nombre, docente_id, tolerancia_min)
SELECT @gestion, x.carrera, x.nivel, x.sigla, x.nombre, x.docente, x.tolerancia
FROM @m x
WHERE NOT EXISTS (SELECT 1 FROM materias m WHERE m.gestion_id = @gestion AND m.sigla = x.sigla);

/* ───────────────────────── 5. horarios (1 = lunes … 5 = viernes) ───────────────────────── */
DECLARE @h TABLE (sigla NVARCHAR(20), dia INT, inicio NVARCHAR(5), fin NVARCHAR(5));
INSERT INTO @h VALUES
    (N'MAT-200', 1, N'08:00', N'10:00'), (N'MAT-200', 3, N'08:00', N'10:00'),       -- Informática 2do
    (N'BDD-200', 2, N'08:00', N'11:00'), (N'BDD-200', 4, N'08:00', N'11:00'),
    (N'RED-400', 1, N'10:30', N'13:00'), (N'RED-400', 3, N'10:30', N'13:00'),       -- Informática 4to
    (N'PLC-400', 5, N'08:00', N'12:00'),
    (N'IAR-600', 1, N'08:00', N'11:00'), (N'IAR-600', 3, N'08:00', N'11:00'),       -- Informática 6to
    (N'INS-600', 2, N'14:00', N'17:00'), (N'INS-600', 4, N'14:00', N'17:00'),
    (N'MEC-110', 1, N'14:00', N'17:00'), (N'MEC-110', 3, N'14:00', N'17:00'),       -- Mecánica (tardes)
    (N'MEC-210', 2, N'14:00', N'17:00'), (N'MEC-210', 4, N'14:00', N'17:00'),
    (N'MEC-310', 5, N'14:00', N'18:00');

INSERT INTO horarios (materia_id, dia, inicio, fin)
SELECT m.id, h.dia, h.inicio, h.fin
FROM @h h JOIN materias m ON m.gestion_id = @gestion AND m.sigla = h.sigla
WHERE NOT EXISTS (SELECT 1 FROM horarios x WHERE x.materia_id = m.id AND x.dia = h.dia AND x.inicio = h.inicio);

/* ───────────────────────── 6. estudiantes de ejemplo ─────────────────────────
   Códigos 9000001 … 9000018: fáciles de reconocer y de borrar después. */
DECLARE @e TABLE (codigo NVARCHAR(30), nombres NVARCHAR(60), apellidos NVARCHAR(60), carrera BIGINT, nivel INT);
INSERT INTO @e VALUES
    (N'9000001', N'Ana',      N'Mamani Quispe',     @inf, 2), (N'9000002', N'Bruno',   N'Choque Lima',       @inf, 2),
    (N'9000003', N'Carla',    N'Flores Rojas',      @inf, 2), (N'9000004', N'Diego',   N'Condori Apaza',     @inf, 2),
    (N'9000005', N'Elena',    N'Ticona Huanca',     @inf, 4), (N'9000006', N'Fabio',   N'Paredes Sosa',      @inf, 4),
    (N'9000007', N'Gabriela', N'Vargas Cruz',       @inf, 4), (N'9000008', N'Hugo',    N'Ramos Laura',       @inf, 4),
    (N'9000009', N'Irene',    N'Calle Poma',        @inf, 6), (N'9000010', N'Jorge',   N'Limachi Ríos',      @inf, 6),
    (N'9000011', N'Karen',    N'Quisbert Mendoza',  @inf, 6), (N'9000012', N'Luis',    N'Gutiérrez Alanoca', @inf, 6),
    (N'9000013', N'Marco',    N'Huanca Tola',       @mec, 1), (N'9000014', N'Nadia',   N'Copa Arce',         @mec, 1),
    (N'9000015', N'Óscar',    N'Pari Villca',       @mec, 2), (N'9000016', N'Paola',   N'Yupanqui Soria',    @mec, 2),
    (N'9000017', N'Rubén',    N'Aliaga Choque',     @mec, 3), (N'9000018', N'Sandra',  N'Torrez Nina',       @mec, 3);

INSERT INTO estudiantes (codigo, nombres, apellidos, curso, activo, creado)
SELECT x.codigo, x.nombres, x.apellidos, N'', 1, @ahora
FROM @e x
WHERE NOT EXISTS (SELECT 1 FROM estudiantes s WHERE s.codigo = x.codigo);

/* ───────────────────────── 7. inscripciones en la gestión activa (carrera + semestre/año) ───────────────────────── */
INSERT INTO inscripciones (estudiante_id, gestion_id, semestre, carrera_id)
SELECT s.id, @gestion, x.nivel, x.carrera
FROM @e x JOIN estudiantes s ON s.codigo = x.codigo
WHERE NOT EXISTS (SELECT 1 FROM inscripciones i WHERE i.estudiante_id = s.id AND i.gestion_id = @gestion);

IF NOT EXISTS (SELECT 1 FROM eventos WHERE tipo = N'datos_iniciales')
    INSERT INTO eventos (estudiante_id, tipo, mensaje, prioridad, fuente, atendido, creado)
    VALUES (NULL, N'datos_iniciales', N'Datos iniciales cargados: 2 carreras, gestión II-2026, 2 docentes, 9 materias y 18 estudiantes de ejemplo.', N'normal', N'sistema', 1, @ahora);

COMMIT TRANSACTION;

/* ── resumen por carrera ── */
SELECT c.nombre AS carrera, c.regimen, c.duracion,
       (SELECT COUNT(*) FROM materias m WHERE m.carrera_id = c.id AND m.gestion_id = @gestion)      AS materias,
       (SELECT COUNT(*) FROM inscripciones i WHERE i.carrera_id = c.id AND i.gestion_id = @gestion) AS inscritos
FROM carreras c ORDER BY c.nombre;
GO

SET NOEXEC OFF;
GO
