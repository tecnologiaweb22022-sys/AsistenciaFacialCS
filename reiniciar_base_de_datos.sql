/* ════════════════════════════════════════════════════════════════════════════
   REINICIAR DESDE CERO — Control de asistencia con reconocimiento facial
   Motor: SQL Server 2014 o posterior

   ⚠ BORRA TODOS LOS DATOS: estudiantes, rostros registrados, asistencias, docentes,
     gestiones, materias, horarios, inscripciones y alertas. NO SE PUEDE DESHACER.
     Las carreras se vuelven a crear: Informática Industrial (semestral, 6 semestres)
     y Mecánica Automotriz (anual, 3 años).

   Después queda la base vacía con su estructura completa. Solo podrá entrar el
   ADMINISTRADOR, porque su usuario y contraseña no están en la base sino en
   config.json (usuario_admin / clave_admin). Para ponerle una contraseña nueva:
   escribe  "pin_admin": "nueva-clave"  en config.json y abre el programa
   (la cifra y la borra del archivo).

   Cómo usarlo:
     1. Cierra el programa de asistencia (para que no esté usando la base).
     2. (Recomendado) Haz una copia de seguridad — ver el paso 0 más abajo.
     3. Cambia  @confirmar = N'NO'  por  N'SI'  y ejecuta todo el script (F5 en SSMS)
        o:  sqlcmd -S DESKTOP-16132LP -f 65001 -i reiniciar_base_de_datos.sql
        (-f 65001 = leer el archivo en UTF-8, para que las tildes salgan bien)
     4. Abre el programa, entra como administrador y crea en «Académico» la gestión,
        los docentes y las materias; luego los estudiantes y sus rostros.

   Las fotos de rostros desconocidos (carpeta datos/desconocidos) no se tocan.
   ════════════════════════════════════════════════════════════════════════════ */

SET NOCOUNT ON;

/* ── seguro: sin confirmar, el script no hace NADA ── */
DECLARE @confirmar NVARCHAR(2) = N'NO';        -- ← cambia a N'SI' para borrar todo

IF @confirmar <> N'SI'
BEGIN
    RAISERROR(N'No se borró nada. Para reiniciar la base, cambia @confirmar a N''SI'' al inicio del script.', 16, 1);
    SET NOEXEC ON;                              -- las demás partes del script no se ejecutan
END
GO

/* ── paso 0 (opcional): copia de seguridad antes de borrar ──
   Descomenta y ajusta la carpeta (debe existir y SQL Server debe poder escribir en ella).
BACKUP DATABASE asistencia TO DISK = N'C:\Respaldos\asistencia_antes_de_reiniciar.bak' WITH INIT, NAME = N'Antes de reiniciar';
GO
*/

IF DB_ID(N'asistencia') IS NULL
    CREATE DATABASE asistencia;
GO

USE asistencia;
GO

/* ─────────────── 1. borrar todas las tablas (de las hijas a las madres) ─────────────── */
IF OBJECT_ID(N'asistencias',   N'U') IS NOT NULL DROP TABLE asistencias;
IF OBJECT_ID(N'horarios',      N'U') IS NOT NULL DROP TABLE horarios;
IF OBJECT_ID(N'inscripciones', N'U') IS NOT NULL DROP TABLE inscripciones;
IF OBJECT_ID(N'materias',      N'U') IS NOT NULL DROP TABLE materias;
IF OBJECT_ID(N'docentes',      N'U') IS NOT NULL DROP TABLE docentes;
IF OBJECT_ID(N'gestiones',     N'U') IS NOT NULL DROP TABLE gestiones;
IF OBJECT_ID(N'rostros',       N'U') IS NOT NULL DROP TABLE rostros;
IF OBJECT_ID(N'eventos',       N'U') IS NOT NULL DROP TABLE eventos;
IF OBJECT_ID(N'estudiantes',   N'U') IS NOT NULL DROP TABLE estudiantes;
IF OBJECT_ID(N'carreras',      N'U') IS NOT NULL DROP TABLE carreras;
GO

/* ─────────────── 2. crear la estructura vacía (versión actual del programa) ─────────────── */
CREATE TABLE estudiantes (
    id         BIGINT IDENTITY(1,1) PRIMARY KEY,
    codigo     NVARCHAR(30)  NOT NULL UNIQUE,
    nombres    NVARCHAR(60)  NOT NULL,
    apellidos  NVARCHAR(60)  NOT NULL DEFAULT N'',
    curso      NVARCHAR(100) NOT NULL DEFAULT N'',
    activo     INT           NOT NULL DEFAULT 1,
    creado     FLOAT         NOT NULL
);

CREATE TABLE rostros (
    id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    estudiante_id  BIGINT         NOT NULL REFERENCES estudiantes(id) ON DELETE CASCADE,
    vector         VARBINARY(MAX) NOT NULL,
    foto           VARBINARY(MAX) NULL,
    creado         FLOAT          NOT NULL
);

CREATE TABLE gestiones (
    id      BIGINT IDENTITY(1,1) PRIMARY KEY,
    nombre  NVARCHAR(20) NOT NULL UNIQUE,
    inicio  NVARCHAR(10) NOT NULL,
    fin     NVARCHAR(10) NOT NULL,
    activa  INT          NOT NULL DEFAULT 0
);

CREATE TABLE carreras (
    id        BIGINT IDENTITY(1,1) PRIMARY KEY,
    nombre    NVARCHAR(80) NOT NULL UNIQUE,
    sigla     NVARCHAR(20) NOT NULL DEFAULT N'',
    regimen   NVARCHAR(10) NOT NULL DEFAULT N'semestral',      -- semestral | anual
    duracion  INT          NOT NULL DEFAULT 6,                 -- semestres o años
    activa    INT          NOT NULL DEFAULT 1
);

CREATE TABLE docentes (
    id         BIGINT IDENTITY(1,1) PRIMARY KEY,
    nombres    NVARCHAR(60)  NOT NULL,
    apellidos  NVARCHAR(60)  NOT NULL DEFAULT N'',
    pin        NVARCHAR(120) NOT NULL DEFAULT N'',     -- contraseña cifrada (sal:hash)
    activo     INT           NOT NULL DEFAULT 1,
    creado     FLOAT         NOT NULL,
    usuario    NVARCHAR(40)  NULL
);

CREATE TABLE materias (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    gestion_id      BIGINT        NOT NULL REFERENCES gestiones(id) ON DELETE CASCADE,
    carrera_id      BIGINT        NULL REFERENCES carreras(id),
    semestre        INT           NOT NULL,                    -- semestre o año, según la carrera
    sigla           NVARCHAR(20)  NOT NULL DEFAULT N'',
    nombre          NVARCHAR(100) NOT NULL,
    docente_id      BIGINT        NULL REFERENCES docentes(id) ON DELETE SET NULL,
    tolerancia_min  INT           NOT NULL DEFAULT 10
);

CREATE TABLE horarios (
    id          BIGINT IDENTITY(1,1) PRIMARY KEY,
    materia_id  BIGINT      NOT NULL REFERENCES materias(id) ON DELETE CASCADE,
    dia         INT         NOT NULL,
    inicio      NVARCHAR(5) NOT NULL,
    fin         NVARCHAR(5) NOT NULL
);

CREATE TABLE inscripciones (
    id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    estudiante_id  BIGINT NOT NULL REFERENCES estudiantes(id) ON DELETE CASCADE,
    gestion_id     BIGINT NOT NULL REFERENCES gestiones(id) ON DELETE CASCADE,
    semestre       INT    NOT NULL,
    carrera_id     BIGINT NULL REFERENCES carreras(id),
    UNIQUE (estudiante_id, gestion_id)
);

CREATE TABLE asistencias (
    id                BIGINT IDENTITY(1,1) PRIMARY KEY,
    estudiante_id     BIGINT        NOT NULL REFERENCES estudiantes(id) ON DELETE CASCADE,
    materia_id        BIGINT        NOT NULL,
    fecha             NVARCHAR(10)  NOT NULL,
    hora              FLOAT         NOT NULL,
    estado            NVARCHAR(20)  NOT NULL,
    similitud         FLOAT         NOT NULL DEFAULT 0,
    fuente            NVARCHAR(20)  NOT NULL DEFAULT N'camara',
    observacion       NVARCHAR(120) NOT NULL DEFAULT N'',
    hora_salida       FLOAT         NULL,
    similitud_salida  FLOAT         NULL,
    fuente_salida     NVARCHAR(20)  NULL,
    -- el programa reconoce la versión actual de la base por esta restricción: no cambiarle el nombre
    CONSTRAINT fk_asistencia_materia FOREIGN KEY (materia_id) REFERENCES materias(id) ON DELETE CASCADE
);

CREATE TABLE eventos (
    id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    estudiante_id  BIGINT        NULL,
    tipo           NVARCHAR(40)  NOT NULL,
    mensaje        NVARCHAR(MAX) NOT NULL,
    prioridad      NVARCHAR(10)  NOT NULL DEFAULT N'normal',
    fuente         NVARCHAR(20)  NOT NULL,
    atendido       INT           NOT NULL DEFAULT 0,
    creado         FLOAT         NOT NULL
);
GO

CREATE INDEX ix_rostros_estudiante    ON rostros (estudiante_id);
CREATE INDEX ix_asistencias_fecha     ON asistencias (fecha);
CREATE UNIQUE INDEX ux_asistencia     ON asistencias (estudiante_id, materia_id, fecha);
CREATE INDEX ix_materias_gestion      ON materias (gestion_id, semestre);
CREATE INDEX ix_materias_carrera      ON materias (carrera_id, semestre);
CREATE INDEX ix_horarios_dia          ON horarios (dia);
CREATE INDEX ix_inscripciones_gestion ON inscripciones (gestion_id, semestre);
GO

/* ─────────────── 3. carreras de la institución ─────────────── */
INSERT INTO carreras (nombre, sigla, regimen, duracion, activa) VALUES
    (N'Informática Industrial', N'INF', N'semestral', 6, 1),
    (N'Mecánica Automotriz',    N'MEC', N'anual',     3, 1);

/* ─────────────── 4. dejar constancia en las alertas ─────────────── */
INSERT INTO eventos (estudiante_id, tipo, mensaje, prioridad, fuente, atendido, creado)
VALUES (NULL, N'sistema', N'Base de datos reiniciada desde cero (solo queda el administrador y las 2 carreras).', N'normal', N'sistema', 1,
        DATEDIFF(SECOND, '1970-01-01', GETUTCDATE()));

PRINT N'Listo: la base «asistencia» quedó vacía. Entra al programa como administrador para empezar.';
GO

SET NOEXEC OFF;
GO
