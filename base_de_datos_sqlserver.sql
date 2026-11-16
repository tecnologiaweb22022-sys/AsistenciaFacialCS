/* ════════════════════════════════════════════════════════════════════════════
   Control de asistencia con reconocimiento facial — esquema de la base de datos
   Motor: SQL Server 2014 o posterior (también Express / LocalDB)

   • Se puede ejecutar varias veces sin dañar nada: cada objeto se crea solo si falta.
   • El programa crea y actualiza este mismo esquema solo al arrancar
     (Nucleo/BaseDatos.cs → Inicializar). Este script es para crearla a mano,
     revisarla o documentarla.
   • Convenciones:
       - fechas de día      → NVARCHAR(10) 'yyyy-MM-dd'
       - instantes          → FLOAT, segundos Unix (UTC)
       - horas de horario   → NVARCHAR(5) 'HH:mm'
       - booleanos          → INT 0 / 1
   • El usuario y la contraseña del ADMINISTRADOR no están aquí: están en config.json
     (usuario_admin y clave_admin, cifrada).
   ════════════════════════════════════════════════════════════════════════════ */

IF DB_ID(N'asistencia') IS NULL
    CREATE DATABASE asistencia;
GO

USE asistencia;
GO

/* ─────────────────────────── estudiantes ─────────────────────────── */
IF OBJECT_ID(N'estudiantes', N'U') IS NULL
CREATE TABLE estudiantes (
    id         BIGINT IDENTITY(1,1) PRIMARY KEY,
    codigo     NVARCHAR(30)  NOT NULL UNIQUE,          -- código o CI
    nombres    NVARCHAR(60)  NOT NULL,
    apellidos  NVARCHAR(60)  NOT NULL DEFAULT N'',
    curso      NVARCHAR(100) NOT NULL DEFAULT N'',     -- carrera / curso
    activo     INT           NOT NULL DEFAULT 1,
    creado     FLOAT         NOT NULL
);
GO

/* ─────────────────── rostros (muestras de cada estudiante) ───────────────────
   Cada fila es una muestra: el VECTOR de 128 números que calcula la red SFace.
   No se guarda la foto completa, solo un recorte chico (112×112) para mostrarlo. */
IF OBJECT_ID(N'rostros', N'U') IS NULL
CREATE TABLE rostros (
    id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    estudiante_id  BIGINT         NOT NULL REFERENCES estudiantes(id) ON DELETE CASCADE,
    vector         VARBINARY(MAX) NOT NULL,
    foto           VARBINARY(MAX) NULL,
    creado         FLOAT          NOT NULL
);
GO

/* ─────────────── gestiones (periodos: I-2026, II-2026, …) ───────────────
   Solo UNA está activa: en ella marca la cámara y se inscriben los estudiantes. */
IF OBJECT_ID(N'gestiones', N'U') IS NULL
CREATE TABLE gestiones (
    id      BIGINT IDENTITY(1,1) PRIMARY KEY,
    nombre  NVARCHAR(20) NOT NULL UNIQUE,
    inicio  NVARCHAR(10) NOT NULL,
    fin     NVARCHAR(10) NOT NULL,
    activa  INT          NOT NULL DEFAULT 0
);
GO

/* ─────────────── carreras (semestrales o anuales) ───────────────
   Informática Industrial: semestral, 6 semestres · Mecánica Automotriz: anual, 3 años.
   El «semestre» de materias e inscripciones es el NIVEL: semestre o año según el régimen. */
IF OBJECT_ID(N'carreras', N'U') IS NULL
CREATE TABLE carreras (
    id        BIGINT IDENTITY(1,1) PRIMARY KEY,
    nombre    NVARCHAR(80) NOT NULL UNIQUE,
    sigla     NVARCHAR(20) NOT NULL DEFAULT N'',               -- ej. INF, MEC
    regimen   NVARCHAR(10) NOT NULL DEFAULT N'semestral',      -- semestral | anual
    duracion  INT          NOT NULL DEFAULT 6,                 -- cantidad de semestres o de años
    activa    INT          NOT NULL DEFAULT 1
);
GO

/* ─────────────────────────── docentes ───────────────────────────
   Entran con «usuario» + contraseña. La contraseña se guarda en «pin» como
   sal:hash (PBKDF2-SHA256, 100 000 vueltas), nunca en texto: créala desde el programa
   (Académico → Docentes), no con INSERT. */
IF OBJECT_ID(N'docentes', N'U') IS NULL
CREATE TABLE docentes (
    id         BIGINT IDENTITY(1,1) PRIMARY KEY,
    nombres    NVARCHAR(60)  NOT NULL,
    apellidos  NVARCHAR(60)  NOT NULL DEFAULT N'',
    pin        NVARCHAR(120) NOT NULL DEFAULT N'',     -- contraseña cifrada (sal:hash)
    activo     INT           NOT NULL DEFAULT 1,
    creado     FLOAT         NOT NULL,
    usuario    NVARCHAR(40)  NULL                      -- ej. maria.quispe (único, en minúsculas)
);
GO
IF COL_LENGTH(N'docentes', N'usuario') IS NULL
    ALTER TABLE docentes ADD usuario NVARCHAR(40) NULL;
GO

/* ─────────────── materias (de una carrera y un semestre/año, en una gestión) ─────────────── */
IF OBJECT_ID(N'materias', N'U') IS NULL
CREATE TABLE materias (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    gestion_id      BIGINT        NOT NULL REFERENCES gestiones(id) ON DELETE CASCADE,
    carrera_id      BIGINT        NULL REFERENCES carreras(id),
    semestre        INT           NOT NULL,              -- nivel: semestre (1 a 6) o año (1 a 3), según la carrera
    sigla           NVARCHAR(20)  NOT NULL DEFAULT N'',  -- ej. IAR-600
    nombre          NVARCHAR(100) NOT NULL,
    docente_id      BIGINT        NULL REFERENCES docentes(id) ON DELETE SET NULL,
    tolerancia_min  INT           NOT NULL DEFAULT 10    -- minutos después del inicio en que aún es PUNTUAL
);
GO

/* ───────── horarios (semana: dia 1 = lunes … 7 = domingo) ───────── */
IF OBJECT_ID(N'horarios', N'U') IS NULL
CREATE TABLE horarios (
    id          BIGINT IDENTITY(1,1) PRIMARY KEY,
    materia_id  BIGINT      NOT NULL REFERENCES materias(id) ON DELETE CASCADE,
    dia         INT         NOT NULL,
    inicio      NVARCHAR(5) NOT NULL,                  -- 'HH:mm'
    fin         NVARCHAR(5) NOT NULL
);
GO

/* ───── inscripciones (en cada gestión el estudiante cursa UN semestre o año de UNA carrera) ───── */
IF OBJECT_ID(N'inscripciones', N'U') IS NULL
CREATE TABLE inscripciones (
    id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    estudiante_id  BIGINT NOT NULL REFERENCES estudiantes(id) ON DELETE CASCADE,
    gestion_id     BIGINT NOT NULL REFERENCES gestiones(id) ON DELETE CASCADE,
    semestre       INT    NOT NULL,                     -- nivel: semestre o año
    carrera_id     BIGINT NULL REFERENCES carreras(id),
    UNIQUE (estudiante_id, gestion_id)
);
GO

/* bases creadas antes de las carreras: agregar las columnas */
IF COL_LENGTH(N'materias', N'carrera_id') IS NULL
    ALTER TABLE materias ADD carrera_id BIGINT NULL REFERENCES carreras(id);
IF COL_LENGTH(N'inscripciones', N'carrera_id') IS NULL
    ALTER TABLE inscripciones ADD carrera_id BIGINT NULL REFERENCES carreras(id);
GO

/* ─────── asistencias (una por estudiante, materia y día: entrada + salida) ─────── */
IF OBJECT_ID(N'asistencias', N'U') IS NULL
CREATE TABLE asistencias (
    id                BIGINT IDENTITY(1,1) PRIMARY KEY,
    estudiante_id     BIGINT        NOT NULL REFERENCES estudiantes(id) ON DELETE CASCADE,
    materia_id        BIGINT        NOT NULL,
    fecha             NVARCHAR(10)  NOT NULL,                    -- 'yyyy-MM-dd'
    hora              FLOAT         NOT NULL,                    -- entrada (segundos Unix)
    estado            NVARCHAR(20)  NOT NULL,                    -- puntual | atraso | justificado
    similitud         FLOAT         NOT NULL DEFAULT 0,          -- parecido con el rostro registrado (0 a 1)
    fuente            NVARCHAR(20)  NOT NULL DEFAULT N'camara',  -- camara | celular | manual | web
    observacion       NVARCHAR(120) NOT NULL DEFAULT N'',
    hora_salida       FLOAT         NULL,                        -- salida (segundos Unix); NULL = aún no marcó
    similitud_salida  FLOAT         NULL,
    fuente_salida     NVARCHAR(20)  NULL,
    CONSTRAINT fk_asistencia_materia FOREIGN KEY (materia_id) REFERENCES materias(id) ON DELETE CASCADE
);
GO

/* ─────────────── eventos (alertas y movimientos del sistema) ─────────────── */
IF OBJECT_ID(N'eventos', N'U') IS NULL
CREATE TABLE eventos (
    id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    estudiante_id  BIGINT        NULL,
    tipo           NVARCHAR(40)  NOT NULL,              -- marca_entrada, desconocido, ingreso_fallido, …
    mensaje        NVARCHAR(MAX) NOT NULL,
    prioridad      NVARCHAR(10)  NOT NULL DEFAULT N'normal',   -- normal | alta
    fuente         NVARCHAR(20)  NOT NULL,
    atendido       INT           NOT NULL DEFAULT 0,
    creado         FLOAT         NOT NULL
);
GO

/* ─────────────────────────── índices ─────────────────────────── */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'rostros') AND name = N'ix_rostros_estudiante')
    CREATE INDEX ix_rostros_estudiante ON rostros (estudiante_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'asistencias') AND name = N'ix_asistencias_fecha')
    CREATE INDEX ix_asistencias_fecha ON asistencias (fecha);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'asistencias') AND name = N'ux_asistencia')
    CREATE UNIQUE INDEX ux_asistencia ON asistencias (estudiante_id, materia_id, fecha);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'materias') AND name = N'ix_materias_carrera')
    CREATE INDEX ix_materias_carrera ON materias (carrera_id, semestre);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'materias') AND name = N'ix_materias_gestion')
    CREATE INDEX ix_materias_gestion ON materias (gestion_id, semestre);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'horarios') AND name = N'ix_horarios_dia')
    CREATE INDEX ix_horarios_dia ON horarios (dia);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'inscripciones') AND name = N'ix_inscripciones_gestion')
    CREATE INDEX ix_inscripciones_gestion ON inscripciones (gestion_id, semestre);
GO

/* ─────────────── carreras de la institución (si no hay ninguna) ─────────────── */
IF NOT EXISTS (SELECT 1 FROM carreras)
    INSERT INTO carreras (nombre, sigla, regimen, duracion, activa) VALUES
        (N'Informática Industrial', N'INF', N'semestral', 6, 1),
        (N'Mecánica Automotriz',    N'MEC', N'anual',     3, 1);
GO

/* ───────────────── datos de ejemplo (opcional, descomenta para usar) ─────────────────
INSERT INTO gestiones (nombre, inicio, fin, activa) VALUES (N'II-2026', N'2026-07-01', N'2026-12-31', 1);

INSERT INTO estudiantes (codigo, nombres, apellidos, curso, activo, creado)
VALUES (N'1234567', N'María', N'Quispe Mamani', N'Informática Industrial', 1, DATEDIFF(SECOND, '1970-01-01', GETUTCDATE()));

-- La materia, su horario y el docente se crean mejor desde el programa (Académico),
-- porque la contraseña del docente debe guardarse cifrada.
─────────────────────────────────────────────────────────────────────────────────────── */
