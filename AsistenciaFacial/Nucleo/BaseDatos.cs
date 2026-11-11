using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace AsistenciaFacial.Nucleo
{
    /// <summary>
    /// Base de datos MariaDB (la de XAMPP): gestiones, docentes, materias con su horario semanal,
    /// estudiantes inscritos por semestre, vectores de sus rostros, marcas de asistencia
    /// (una por estudiante, materia y día) y alertas. Funciona en la red local, sin internet.
    /// La usan igual la ventana Windows Forms y la web.
    /// </summary>
    public class BaseDatos
    {
        public static readonly string[] EstadosMarca = { "puntual", "atraso", "justificado" };

        // En MariaDB las claves foráneas deben ir como FOREIGN KEY al final de la tabla:
        // un REFERENCES escrito junto a la columna se ignora en silencio.
        // «asistencias» se crea con su forma original (sin materia) y ActualizarEsquema la completa:
        // así una base vieja y una nueva siguen exactamente el mismo camino.
        const string Esquema = @"
CREATE TABLE IF NOT EXISTS estudiantes (
    id BIGINT AUTO_INCREMENT PRIMARY KEY, codigo VARCHAR(30) NOT NULL UNIQUE, nombres VARCHAR(60) NOT NULL,
    apellidos VARCHAR(60) NOT NULL DEFAULT '', curso VARCHAR(100) NOT NULL DEFAULT '', activo INT NOT NULL DEFAULT 1,
    creado DOUBLE NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
-- Cada fila es una muestra del rostro: el VECTOR de 128 números que calcula la red SFace
-- (no se guarda la foto completa, solo un recorte chico de 112×112 para mostrarlo).
CREATE TABLE IF NOT EXISTS rostros (
    id BIGINT AUTO_INCREMENT PRIMARY KEY, estudiante_id BIGINT NOT NULL,
    vector BLOB NOT NULL, foto MEDIUMBLOB, creado DOUBLE NOT NULL,
    INDEX ix_rostros_estudiante (estudiante_id),
    FOREIGN KEY (estudiante_id) REFERENCES estudiantes(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS asistencias (
    id BIGINT AUTO_INCREMENT PRIMARY KEY, estudiante_id BIGINT NOT NULL,
    fecha VARCHAR(10) NOT NULL, hora DOUBLE NOT NULL, estado VARCHAR(20) NOT NULL, similitud DOUBLE NOT NULL DEFAULT 0,
    fuente VARCHAR(20) NOT NULL DEFAULT 'camara', observacion VARCHAR(120) NOT NULL DEFAULT '',
    UNIQUE (estudiante_id, fecha), INDEX ix_asistencias_fecha (fecha),
    FOREIGN KEY (estudiante_id) REFERENCES estudiantes(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS eventos (
    id BIGINT AUTO_INCREMENT PRIMARY KEY, estudiante_id BIGINT NULL, tipo VARCHAR(40) NOT NULL, mensaje TEXT NOT NULL,
    prioridad VARCHAR(10) NOT NULL DEFAULT 'normal', fuente VARCHAR(20) NOT NULL, atendido INT NOT NULL DEFAULT 0, creado DOUBLE NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
-- Periodos académicos: I-2026 (1ro, 3ro, 5to), II-2026 (2do, 4to, 6to). Solo una gestión está activa.
CREATE TABLE IF NOT EXISTS gestiones (
    id BIGINT AUTO_INCREMENT PRIMARY KEY, nombre VARCHAR(20) NOT NULL UNIQUE,
    inicio VARCHAR(10) NOT NULL, fin VARCHAR(10) NOT NULL, activa INT NOT NULL DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
-- El PIN se guarda como sal:hash (PBKDF2), nunca en texto.
CREATE TABLE IF NOT EXISTS docentes (
    id BIGINT AUTO_INCREMENT PRIMARY KEY, nombres VARCHAR(60) NOT NULL, apellidos VARCHAR(60) NOT NULL DEFAULT '',
    pin VARCHAR(120) NOT NULL DEFAULT '', activo INT NOT NULL DEFAULT 1, creado DOUBLE NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS materias (
    id BIGINT AUTO_INCREMENT PRIMARY KEY, gestion_id BIGINT NOT NULL, semestre INT NOT NULL,
    sigla VARCHAR(20) NOT NULL DEFAULT '', nombre VARCHAR(100) NOT NULL, docente_id BIGINT NULL,
    tolerancia_min INT NOT NULL DEFAULT 10,
    INDEX ix_materias_gestion (gestion_id, semestre),
    FOREIGN KEY (gestion_id) REFERENCES gestiones(id) ON DELETE CASCADE,
    FOREIGN KEY (docente_id) REFERENCES docentes(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
-- Horario semanal: dia 1 = lunes … 7 = domingo, horas 'HH:mm'.
-- (sin punto y coma en los comentarios: el esquema se separa en sentencias por ese carácter)
CREATE TABLE IF NOT EXISTS horarios (
    id BIGINT AUTO_INCREMENT PRIMARY KEY, materia_id BIGINT NOT NULL, dia INT NOT NULL,
    inicio VARCHAR(5) NOT NULL, fin VARCHAR(5) NOT NULL,
    INDEX ix_horarios_dia (dia),
    FOREIGN KEY (materia_id) REFERENCES materias(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
-- En cada gestión el estudiante cursa UN semestre (y todas sus materias).
CREATE TABLE IF NOT EXISTS inscripciones (
    id BIGINT AUTO_INCREMENT PRIMARY KEY, estudiante_id BIGINT NOT NULL, gestion_id BIGINT NOT NULL, semestre INT NOT NULL,
    UNIQUE (estudiante_id, gestion_id), INDEX ix_inscripciones_gestion (gestion_id, semestre),
    FOREIGN KEY (estudiante_id) REFERENCES estudiantes(id) ON DELETE CASCADE,
    FOREIGN KEY (gestion_id) REFERENCES gestiones(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

        readonly string _cadena;
        readonly ConfigBaseDatos _cfg;
        readonly object _candado = new object();
        /// <summary>Aumenta cada vez que cambian los rostros: el motor recarga su galería.</summary>
        public int VersionRostros { get; private set; }

        public BaseDatos(ConfigBaseDatos cfg)
        {
            _cfg = cfg;
            _cadena = Cadena(cfg, conBase: true);
        }

        static string Cadena(ConfigBaseDatos c, bool conBase) => new MySqlConnectionStringBuilder
        {
            Server = c.Servidor, Port = (uint)c.Puerto, UserID = c.Usuario, Password = c.Clave,
            Database = conBase ? c.Nombre : "", CharacterSet = "utf8mb4", ConnectionTimeout = 5,
        }.ToString();

        public static double Ahora() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        public static string Hoy() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public static string Dia(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public static DateTime Local(double unix) => DateTimeOffset.FromUnixTimeMilliseconds((long)(unix * 1000)).LocalDateTime;
        public static double Unix(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds() / 1000.0;

        static bool FechaValida(string f) => DateTime.TryParseExact(f ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        // ───────────────────────── sesión (conexión + transacción) ─────────────────────────
        sealed class Sesion : IDisposable
        {
            public MySqlConnection Con;
            public MySqlTransaction Tx;

            public MySqlCommand Cmd(string sql, object p = null)
            {
                var c = Con.CreateCommand();
                c.CommandText = sql;
                c.Transaction = Tx;
                if (p != null)
                    foreach (var prop in p.GetType().GetProperties())
                        c.Parameters.AddWithValue("@" + prop.Name, prop.GetValue(p) ?? DBNull.Value);
                return c;
            }

            public int Ejecutar(string sql, object p = null)
            {
                using var c = Cmd(sql, p);
                return c.ExecuteNonQuery();
            }

            public double Numero(string sql, object p = null)
            {
                using var c = Cmd(sql, p);
                object v = c.ExecuteScalar();
                return v == null || v is DBNull ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);
            }

            public List<T> Consultar<T>(string sql, Func<MySqlDataReader, T> mapa, object p = null)
            {
                using var c = Cmd(sql, p);
                using var r = c.ExecuteReader();
                var lista = new List<T>();
                while (r.Read()) lista.Add(mapa(r));
                return lista;
            }

            public long UltimoId() => (long)Numero("SELECT LAST_INSERT_ID()");

            public void Dispose()
            {
                Tx?.Dispose();
                Con.Dispose();
            }
        }

        T Operacion<T>(Func<Sesion, T> f)
        {
            lock (_candado)
            {
                var con = new MySqlConnection(_cadena);
                con.Open();
                using var s = new Sesion { Con = con };
                s.Tx = con.BeginTransaction();
                T r = f(s);
                s.Tx.Commit();
                return r;
            }
        }

        void Operacion(Action<Sesion> f) => Operacion<bool>(s => { f(s); return true; });

        /// <summary>CREATE/ALTER confirman solos en MariaDB: van fuera de cualquier transacción.</summary>
        void Ddl(string sql)
        {
            lock (_candado)
            {
                using var con = new MySqlConnection(_cadena);
                con.Open();
                using var c = con.CreateCommand();
                c.CommandText = sql;
                c.ExecuteNonQuery();
            }
        }

        static int Col(MySqlDataReader r, string c) => r.GetOrdinal(c);
        // Convert: en MariaDB COUNT() da BIGINT pero SUM() da DECIMAL y las columnas INT dan int
        static long L(MySqlDataReader r, string c) => r.IsDBNull(Col(r, c)) ? 0 : Convert.ToInt64(r.GetValue(Col(r, c)), CultureInfo.InvariantCulture);
        static long? LN(MySqlDataReader r, string c) => r.IsDBNull(Col(r, c)) ? (long?)null : L(r, c);
        static double D(MySqlDataReader r, string c) => r.IsDBNull(Col(r, c)) ? 0 : r.GetDouble(Col(r, c));
        static double? DN(MySqlDataReader r, string c) => r.IsDBNull(Col(r, c)) ? (double?)null : r.GetDouble(Col(r, c));
        static string S(MySqlDataReader r, string c) => r.IsDBNull(Col(r, c)) ? "" : r.GetString(Col(r, c));
        static byte[] B(MySqlDataReader r, string c) => r.IsDBNull(Col(r, c)) ? null : (byte[])r.GetValue(Col(r, c));

        static Estudiante MapaEstudiante(MySqlDataReader r) => new Estudiante
        {
            Id = L(r, "id"), Codigo = S(r, "codigo"), Nombres = S(r, "nombres"), Apellidos = S(r, "apellidos"),
            Curso = S(r, "curso"), Activo = L(r, "activo") == 1, Muestras = (int)L(r, "muestras"), Semestre = (int)L(r, "semestre"),
            Creado = D(r, "creado"),
        };

        static Evento MapaEvento(MySqlDataReader r) => new Evento
        {
            Id = L(r, "id"), EstudianteId = LN(r, "estudiante_id"),
            Tipo = S(r, "tipo"), Mensaje = S(r, "mensaje"), Prioridad = S(r, "prioridad"), Fuente = S(r, "fuente"),
            Atendido = L(r, "atendido") == 1, Creado = D(r, "creado"),
        };

        static Gestion MapaGestion(MySqlDataReader r) => new Gestion
        {
            Id = L(r, "id"), Nombre = S(r, "nombre"), Inicio = S(r, "inicio"), Fin = S(r, "fin"), Activa = L(r, "activa") == 1,
            Materias = (int)L(r, "materias"), Inscritos = (int)L(r, "inscritos"),
        };

        static Docente MapaDocente(MySqlDataReader r) => new Docente
        {
            Id = L(r, "id"), Nombres = S(r, "nombres"), Apellidos = S(r, "apellidos"), Activo = L(r, "activo") == 1,
            TienePin = S(r, "pin").Length > 0, Materias = (int)L(r, "materias"),
        };

        static Materia MapaMateria(MySqlDataReader r) => new Materia
        {
            Id = L(r, "id"), GestionId = L(r, "gestion_id"), Gestion = S(r, "gestion"), Semestre = (int)L(r, "semestre"),
            Sigla = S(r, "sigla"), Nombre = S(r, "nombre"), DocenteId = LN(r, "docente_id"), Docente = S(r, "docente").Trim(),
            ToleranciaMin = (int)L(r, "tolerancia_min"),
        };

        const string SqlEstudiantes =
            "SELECT e.*, (SELECT COUNT(*) FROM rostros r WHERE r.estudiante_id = e.id) AS muestras, " +
            "(SELECT i.semestre FROM inscripciones i JOIN gestiones g ON g.id = i.gestion_id WHERE i.estudiante_id = e.id AND g.activa = 1) AS semestre " +
            "FROM estudiantes e ";

        const string SqlGestiones =
            "SELECT g.*, (SELECT COUNT(*) FROM materias m WHERE m.gestion_id = g.id) AS materias, " +
            "(SELECT COUNT(*) FROM inscripciones i WHERE i.gestion_id = g.id) AS inscritos FROM gestiones g ";

        const string SqlDocentes =
            "SELECT d.*, (SELECT COUNT(*) FROM materias m JOIN gestiones g ON g.id = m.gestion_id WHERE m.docente_id = d.id AND g.activa = 1) AS materias " +
            "FROM docentes d ";

        const string SqlMaterias =
            "SELECT m.*, g.nombre AS gestion, CONCAT(COALESCE(d.nombres, ''), ' ', COALESCE(d.apellidos, '')) AS docente " +
            "FROM materias m JOIN gestiones g ON g.id = m.gestion_id LEFT JOIN docentes d ON d.id = m.docente_id ";

        // ───────────────────────── creación ─────────────────────────
        /// <summary>
        /// Crea la base y las tablas si faltan. Si la base está vacía y existe el antiguo
        /// archivo SQLite, copia sus datos (una sola vez) y lo renombra a .migrado.
        /// Luego la actualiza a materias por semestre usando los datos de «legado» (config.json).
        /// </summary>
        public void Inicializar(string sqliteAnterior = null, Configuracion legado = null)
        {
            try
            {
                lock (_candado)
                {
                    using (var con = new MySqlConnection(Cadena(_cfg, conBase: false)))
                    {
                        con.Open();
                        using var c = con.CreateCommand();
                        c.CommandText = $"CREATE DATABASE IF NOT EXISTS `{_cfg.Nombre.Replace("`", "")}` " +
                                        "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
                        c.ExecuteNonQuery();
                    }
                }
            }
            catch (MySqlException e)
            {
                throw new ErrorValidacion($"No se pudo conectar a MariaDB en {_cfg.Servidor}:{_cfg.Puerto} (base «{_cfg.Nombre}», usuario «{_cfg.Usuario}»).\n\n" +
                    "Abre el Panel de control de XAMPP e inicia «MySQL», o revisa la sección base_de_datos de config.json.\n\n" + e.Message);
            }
            try
            {
                foreach (string sql in Esquema.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
                    Ddl(sql);
            }
            catch (MySqlException e) { throw new ErrorValidacion("No se pudieron crear las tablas de la base de datos.\n\n" + e.Message); }
            if (!EsquemaActualizado() && sqliteAnterior != null && File.Exists(sqliteAnterior)) MigrarDesdeSqlite(sqliteAnterior);
            try { ActualizarEsquema(legado); }
            catch (MySqlException e) { throw new ErrorValidacion("No se pudo actualizar la base de datos a materias por semestre.\n\n" + e.Message); }
        }

        // ───────────────────────── actualización: una materia → materias por semestre ─────────────────────────
        bool EsquemaActualizado() => Operacion(s => s.Numero(
            "SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS " +
            "WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'fk_asistencia_materia'") > 0);

        /// <summary>
        /// La versión anterior tenía una sola materia (en config.json) y una marca por día.
        /// Aquí cada marca pasa a pertenecer a una materia. Cada paso se puede repetir sin daño,
        /// así que si algo se corta a medias, el siguiente arranque lo termina.
        /// </summary>
        void ActualizarEsquema(Configuracion legado)
        {
            if (!EsquemaActualizado()) AMaterias(legado);
            // Entrada y salida: la columna «hora» es la entrada; la salida se agregó después.
            Ddl("ALTER TABLE asistencias ADD COLUMN IF NOT EXISTS hora_salida DOUBLE NULL AFTER hora, " +
                "ADD COLUMN IF NOT EXISTS similitud_salida DOUBLE NULL AFTER hora_salida, " +
                "ADD COLUMN IF NOT EXISTS fuente_salida VARCHAR(20) NULL AFTER similitud_salida");
        }

        void AMaterias(Configuracion legado)
        {
            Ddl("ALTER TABLE asistencias ADD COLUMN IF NOT EXISTS materia_id BIGINT NULL AFTER estudiante_id");
            Operacion(s => Sembrar(s, legado));
            Ddl("ALTER TABLE asistencias MODIFY materia_id BIGINT NOT NULL");
            Ddl("ALTER TABLE asistencias ADD UNIQUE INDEX IF NOT EXISTS ux_asistencia (estudiante_id, materia_id, fecha)");
            string viejo = Operacion(s => s.Consultar(
                "SELECT INDEX_NAME AS n FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'asistencias' " +
                "AND NON_UNIQUE = 0 GROUP BY INDEX_NAME HAVING GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) = 'estudiante_id,fecha'",
                r => S(r, "n")).FirstOrDefault());
            if (viejo != null) Ddl($"ALTER TABLE asistencias DROP INDEX `{viejo.Replace("`", "")}`");
            Ddl("ALTER TABLE asistencias ADD CONSTRAINT fk_asistencia_materia FOREIGN KEY IF NOT EXISTS (materia_id) " +
                "REFERENCES materias(id) ON DELETE CASCADE");
        }

        /// <summary>Crea la gestión actual y, con los datos de config.json, la materia, su docente y su horario.</summary>
        void Sembrar(Sesion s, Configuracion legado)
        {
            if (s.Numero("SELECT COUNT(*) FROM gestiones") == 0)
            {
                int a = DateTime.Today.Year;
                bool segunda = DateTime.Today.Month >= 7;
                s.Ejecutar("INSERT INTO gestiones (nombre, inicio, fin, activa) VALUES (@n, @i, @f, @a)",
                    new { n = $"I-{a}", i = $"{a}-02-01", f = $"{a}-06-30", a = segunda ? 0 : 1 });
                s.Ejecutar("INSERT INTO gestiones (nombre, inicio, fin, activa) VALUES (@n, @i, @f, @a)",
                    new { n = $"II-{a}", i = $"{a}-07-01", f = $"{a}-12-31", a = segunda ? 1 : 0 });
            }
            long gestion = (long)s.Numero("SELECT COALESCE(MAX(id), 0) FROM gestiones WHERE activa = 1");
            bool hayMarcas = s.Numero("SELECT COUNT(*) FROM asistencias WHERE materia_id IS NULL") > 0;
            string nombre = (legado?.Materia ?? "").Trim();

            if (s.Numero("SELECT COUNT(*) FROM materias") == 0 && gestion > 0 && (nombre.Length > 0 || hayMarcas))
            {
                long? docente = null;
                string nombreDocente = (legado?.Docente ?? "").Trim();
                if (nombreDocente.Length > 0)
                {
                    // Mismo PIN que el administrador: conviene cambiarlo en «Académico → Docentes».
                    s.Ejecutar("INSERT INTO docentes (nombres, apellidos, pin, activo, creado) VALUES (@nombres, '', @pin, 1, @creado)",
                        new { nombres = Recortar(nombreDocente, 60), pin = HashPin(legado.PinAdmin ?? "1234"), creado = Ahora() });
                    docente = s.UltimoId();
                }
                // "IAR-600 Inteligencia Artificial y Robótica" → sigla IAR-600 + nombre
                var m = Regex.Match(nombre, @"^([A-ZÑ]{2,6}-?\d{2,4})\s+(.+)$");
                string sigla = m.Success ? m.Groups[1].Value : "";
                string titulo = m.Success ? m.Groups[2].Value : nombre.Length > 0 ? nombre : "Materia sin nombre";
                int semestre = SemestreEnTexto(legado?.Curso);
                var h = legado?.Horario ?? new ConfigHorario();
                string inicio = Academico.HoraValida(h.HoraEntrada) ? h.HoraEntrada : "08:00";
                string fin = Academico.HoraValida(h.HoraCierre) && Academico.Hora(h.HoraCierre) > Academico.Hora(inicio) ? h.HoraCierre : "13:00";
                s.Ejecutar("INSERT INTO materias (gestion_id, semestre, sigla, nombre, docente_id, tolerancia_min) VALUES (@gestion, @semestre, @sigla, @titulo, @docente, @tol)",
                    new { gestion, semestre, sigla, titulo = Recortar(titulo, 100), docente, tol = Math.Max(0, Math.Min(120, h.ToleranciaMin)) });
                long materia = s.UltimoId();
                for (int dia = 1; dia <= 5; dia++)        // lunes a viernes con el horario de siempre: se ajusta en «Académico»
                    s.Ejecutar("INSERT INTO horarios (materia_id, dia, inicio, fin) VALUES (@materia, @dia, @inicio, @fin)", new { materia, dia, inicio, fin });
                s.Ejecutar("INSERT IGNORE INTO inscripciones (estudiante_id, gestion_id, semestre) SELECT id, @gestion, @semestre FROM estudiantes",
                    new { gestion, semestre });
                RegistrarEvento(s, null, "actualizacion",
                    $"Base actualizada a materias por semestre: «{titulo}» quedó en {Academico.Semestre(semestre)} semestre de la gestión activa " +
                    "con sus estudiantes y asistencias. Revisa su horario en «Académico».", "normal", "sistema");
            }
            s.Ejecutar("UPDATE asistencias SET materia_id = (SELECT MIN(id) FROM materias) WHERE materia_id IS NULL");
        }

        /// <summary>"Informática Industrial - Sexto semestre" → 6 (1 si no dice).</summary>
        static int SemestreEnTexto(string curso)
        {
            string t = (curso ?? "").ToLowerInvariant();
            string[] palabras = { "", "primer", "segundo", "tercer", "cuarto", "quinto", "sexto" };
            string[] cortas = { "", "1ro", "2do", "3ro", "4to", "5to", "6to" };
            for (int i = 6; i >= 1; i--)
                if (t.Contains(palabras[i]) || t.Contains(cortas[i])) return i;
            return 1;
        }

        static string Recortar(string s, int n) => s.Length > n ? s.Substring(0, n) : s;

        // ───────────────────────── migración desde SQLite ─────────────────────────
        void MigrarDesdeSqlite(string ruta)
        {
            if (Operacion(s => s.Numero("SELECT COUNT(*) FROM estudiantes")) > 0) return;    // ya tiene datos: no se toca

            // Pooling=false: así el archivo queda libre al terminar y se puede renombrar.
            var origen = new SqliteConnectionStringBuilder { DataSource = ruta, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            int[] copiados;
            try
            {
                copiados = Operacion(s =>
                {
                    using var con = new SqliteConnection(origen);
                    con.Open();
                    // Se conservan los mismos id: así rostros y asistencias siguen apuntando al estudiante correcto.
                    // Los eventos reciben id nuevos (en el mismo orden): nada los referencia y la base puede tener ya alertas del sistema.
                    return new[]
                    {
                        Copiar(con, s, "estudiantes", "id, codigo, nombres, apellidos, curso, activo, creado"),
                        Copiar(con, s, "rostros", "id, estudiante_id, vector, foto, creado"),
                        Copiar(con, s, "asistencias", "id, estudiante_id, fecha, hora, estado, similitud, fuente, observacion"),
                        Copiar(con, s, "eventos", "estudiante_id, tipo, mensaje, prioridad, fuente, atendido, creado"),
                    };
                });
            }
            catch (Exception e) when (e is MySqlException || e is SqliteException)
            {
                throw new ErrorValidacion($"No se pudieron importar los datos de {ruta} a MariaDB. No se cambió nada: el archivo sigue intacto.\n\n{e.Message}");
            }
            File.Move(ruta, ruta + ".migrado", overwrite: true);
            foreach (var extra in new[] { "-wal", "-shm" })              // archivos auxiliares de SQLite, si quedaron
                if (File.Exists(ruta + extra)) File.Move(ruta + extra, ruta + ".migrado" + extra, overwrite: true);
            EventoSistema("migracion", $"Datos importados de SQLite a MariaDB: {copiados[0]} estudiantes, {copiados[1]} muestras de rostro, " +
                                       $"{copiados[2]} asistencias y {copiados[3]} eventos (el archivo viejo quedó como {Path.GetFileName(ruta)}.migrado)", false);
            VersionRostros++;
        }

        static int Copiar(SqliteConnection origen, Sesion s, string tabla, string columnas)
        {
            var cols = columnas.Split(',').Select(x => x.Trim()).ToArray();
            using var leer = origen.CreateCommand();
            leer.CommandText = $"SELECT {columnas} FROM {tabla} ORDER BY id";
            using var r = leer.ExecuteReader();
            using var ins = s.Cmd($"INSERT INTO {tabla} ({columnas}) VALUES ({string.Join(", ", cols.Select(c => "@" + c))})");
            int n = 0;
            while (r.Read())
            {
                ins.Parameters.Clear();
                for (int i = 0; i < cols.Length; i++) ins.Parameters.AddWithValue("@" + cols[i], r.GetValue(i));
                ins.ExecuteNonQuery();
                n++;
            }
            return n;
        }

        // ───────────────────────── eventos ─────────────────────────
        static void RegistrarEvento(Sesion s, long? estudianteId, string tipo, string mensaje, string prioridad, string fuente) =>
            s.Ejecutar("INSERT INTO eventos (estudiante_id, tipo, mensaje, prioridad, fuente, creado) " +
                       "VALUES (@estudianteId, @tipo, @mensaje, @prioridad, @fuente, @creado)",
                new { estudianteId, tipo, mensaje, prioridad, fuente, creado = Ahora() });

        public void EventoSistema(string tipo, string mensaje, bool alta, long? estudianteId = null) =>
            Operacion(s => RegistrarEvento(s, estudianteId, tipo, mensaje, alta ? "alta" : "normal", "sistema"));

        // ───────────────────────── gestiones ─────────────────────────
        public List<Gestion> Gestiones() => Operacion(s => s.Consultar(SqlGestiones + "ORDER BY g.inicio DESC, g.id DESC", MapaGestion));

        public Gestion GestionActiva() => Operacion(s => s.Consultar(SqlGestiones + "WHERE g.activa = 1", MapaGestion).FirstOrDefault());

        static long GestionActivaId(Sesion s) => (long)s.Numero("SELECT COALESCE(MAX(id), 0) FROM gestiones WHERE activa = 1");

        public long GuardarGestion(Gestion g)
        {
            string nombre = (g.Nombre ?? "").Trim().ToUpperInvariant();
            if (nombre.Length == 0 || nombre.Length > 20) throw new ErrorValidacion("Escribe el nombre de la gestión (por ejemplo II-2026)");
            if (!FechaValida(g.Inicio) || !FechaValida(g.Fin) || string.CompareOrdinal(g.Inicio, g.Fin) >= 0)
                throw new ErrorValidacion("Revisa las fechas: el inicio debe ser anterior al fin");
            return Operacion(s =>
            {
                if (s.Numero("SELECT COUNT(*) FROM gestiones WHERE nombre = @nombre AND id <> @id", new { nombre, id = g.Id }) > 0)
                    throw new ErrorValidacion($"Ya existe la gestión {nombre}");
                var v = new { id = g.Id, nombre, inicio = g.Inicio, fin = g.Fin };
                if (g.Id > 0)
                {
                    s.Ejecutar("UPDATE gestiones SET nombre = @nombre, inicio = @inicio, fin = @fin WHERE id = @id", v);
                    return g.Id;
                }
                // la primera gestión que se crea queda activa
                bool primera = s.Numero("SELECT COUNT(*) FROM gestiones") == 0;
                s.Ejecutar("INSERT INTO gestiones (nombre, inicio, fin, activa) VALUES (@nombre, @inicio, @fin, " + (primera ? "1" : "0") + ")", v);
                return s.UltimoId();
            });
        }

        /// <summary>Solo una gestión activa: la cámara marca en las materias de esa gestión.</summary>
        public void ActivarGestion(long id) => Operacion(s =>
        {
            var g = s.Consultar(SqlGestiones + "WHERE g.id = @id", MapaGestion, new { id }).FirstOrDefault()
                    ?? throw new ErrorValidacion("La gestión no existe");
            s.Ejecutar("UPDATE gestiones SET activa = (id = @id)", new { id });
            RegistrarEvento(s, null, "gestion", $"Gestión activa: {g.Nombre}", "normal", "admin");
        });

        public void EliminarGestion(long id) => Operacion(s =>
        {
            var g = s.Consultar(SqlGestiones + "WHERE g.id = @id", MapaGestion, new { id }).FirstOrDefault()
                    ?? throw new ErrorValidacion("La gestión no existe");
            if (g.Activa) throw new ErrorValidacion("No se puede eliminar la gestión activa. Activa otra primero.");
            int marcas = (int)s.Numero("SELECT COUNT(*) FROM asistencias a JOIN materias m ON m.id = a.materia_id WHERE m.gestion_id = @id", new { id });
            if (marcas > 0) throw new ErrorValidacion($"La gestión {g.Nombre} tiene {marcas} asistencias registradas: no se elimina para no perder el historial.");
            s.Ejecutar("DELETE FROM gestiones WHERE id = @id", new { id });
        });

        /// <summary>Copia materias (con docente y horario) de otra gestión. Devuelve cuántas se copiaron.</summary>
        public int CopiarMaterias(long origen, long destino)
        {
            if (origen == destino) throw new ErrorValidacion("Elige una gestión distinta");
            return Operacion(s =>
            {
                var materias = MateriasDe(s, "WHERE m.gestion_id = @origen", new { origen });
                var existentes = MateriasDe(s, "WHERE m.gestion_id = @destino", new { destino });
                int n = 0;
                foreach (var m in materias.Where(m => !existentes.Any(x => x.Semestre == m.Semestre && x.Nombre == m.Nombre)))
                {
                    s.Ejecutar("INSERT INTO materias (gestion_id, semestre, sigla, nombre, docente_id, tolerancia_min) VALUES (@destino, @semestre, @sigla, @nombre, @docente, @tol)",
                        new { destino, semestre = m.Semestre, sigla = m.Sigla, nombre = m.Nombre, docente = m.DocenteId, tol = m.ToleranciaMin });
                    long id = s.UltimoId();
                    foreach (var b in m.Horarios)
                        s.Ejecutar("INSERT INTO horarios (materia_id, dia, inicio, fin) VALUES (@id, @dia, @inicio, @fin)", new { id, dia = b.Dia, inicio = b.Inicio, fin = b.Fin });
                    n++;
                }
                return n;
            });
        }

        /// <summary>
        /// Inscribe en «destino» a los estudiantes activos de «origen» en el semestre siguiente
        /// (los de 6to egresan). No toca a quien ya esté inscrito en «destino».
        /// </summary>
        public int PromoverEstudiantes(long origen, long destino)
        {
            if (origen == destino) throw new ErrorValidacion("Elige una gestión distinta");
            return Operacion(s => s.Ejecutar(
                "INSERT IGNORE INTO inscripciones (estudiante_id, gestion_id, semestre) " +
                "SELECT i.estudiante_id, @destino, i.semestre + 1 FROM inscripciones i JOIN estudiantes e ON e.id = i.estudiante_id " +
                "WHERE i.gestion_id = @origen AND i.semestre < 6 AND e.activo = 1", new { origen, destino }));
        }

        // ───────────────────────── docentes ─────────────────────────
        public List<Docente> Docentes(bool soloActivos = false) => Operacion(s =>
            s.Consultar(SqlDocentes + (soloActivos ? "WHERE d.activo = 1 " : "") + "ORDER BY d.apellidos, d.nombres", MapaDocente));

        /// <summary>Crea o actualiza. «pinNuevo» vacío = no cambiar el PIN (obligatorio al crear).</summary>
        public long GuardarDocente(Docente d, string pinNuevo)
        {
            string nombres = (d.Nombres ?? "").Trim(), apellidos = (d.Apellidos ?? "").Trim();
            pinNuevo = (pinNuevo ?? "").Trim();
            if (nombres.Length == 0) throw new ErrorValidacion("Escribe el nombre del docente");
            if (nombres.Length > 60 || apellidos.Length > 60) throw new ErrorValidacion("Texto demasiado largo");
            if (d.Id == 0 && pinNuevo.Length == 0) throw new ErrorValidacion("Asigna un PIN al docente (mínimo 4 caracteres)");
            if (pinNuevo.Length > 0 && pinNuevo.Length < 4) throw new ErrorValidacion("El PIN debe tener al menos 4 caracteres");
            return Operacion(s =>
            {
                var v = new { id = d.Id, nombres, apellidos, activo = d.Activo ? 1 : 0, pin = pinNuevo.Length > 0 ? HashPin(pinNuevo) : "", creado = Ahora() };
                if (d.Id > 0)
                {
                    s.Ejecutar("UPDATE docentes SET nombres = @nombres, apellidos = @apellidos, activo = @activo" +
                               (pinNuevo.Length > 0 ? ", pin = @pin" : "") + " WHERE id = @id", v);
                    return d.Id;
                }
                s.Ejecutar("INSERT INTO docentes (nombres, apellidos, pin, activo, creado) VALUES (@nombres, @apellidos, @pin, @activo, @creado)", v);
                return s.UltimoId();
            });
        }

        /// <summary>Sus materias quedan «sin docente» (no se borran).</summary>
        public void EliminarDocente(long id) => Operacion(s => s.Ejecutar("DELETE FROM docentes WHERE id = @id", new { id }));

        /// <summary>El docente con ese PIN, o null si no coincide o está inactivo.</summary>
        public Docente ValidarDocente(long id, string pin) => Operacion(s =>
        {
            var fila = s.Consultar("SELECT pin FROM docentes WHERE id = @id AND activo = 1", r => S(r, "pin"), new { id }).FirstOrDefault();
            if (fila == null || !PinValido(pin ?? "", fila)) return null;
            return s.Consultar(SqlDocentes + "WHERE d.id = @id", MapaDocente, new { id }).FirstOrDefault();
        });

        static string HashPin(string pin)
        {
            byte[] sal = RandomNumberGenerator.GetBytes(16);
            byte[] h = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), sal, 100_000, HashAlgorithmName.SHA256, 32);
            return Convert.ToHexString(sal) + ":" + Convert.ToHexString(h);
        }

        static bool PinValido(string pin, string guardado)
        {
            var partes = guardado.Split(':');
            if (partes.Length != 2) return false;
            try
            {
                byte[] sal = Convert.FromHexString(partes[0]), esperado = Convert.FromHexString(partes[1]);
                byte[] h = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), sal, 100_000, HashAlgorithmName.SHA256, esperado.Length);
                return CryptographicOperations.FixedTimeEquals(h, esperado);
            }
            catch (FormatException) { return false; }
        }

        // ───────────────────────── materias y horarios ─────────────────────────
        static List<Materia> MateriasDe(Sesion s, string where, object p = null)
        {
            var lista = s.Consultar(SqlMaterias + where + " ORDER BY m.semestre, m.nombre", MapaMateria, p);
            if (lista.Count == 0) return lista;
            var porId = lista.ToDictionary(m => m.Id);
            // los id son números de la propia base: se pueden escribir directo en el IN (...)
            foreach (var (id, b) in s.Consultar($"SELECT * FROM horarios WHERE materia_id IN ({string.Join(",", porId.Keys)}) ORDER BY dia, inicio",
                         r => (L(r, "materia_id"), new BloqueHorario { Dia = (int)L(r, "dia"), Inicio = S(r, "inicio"), Fin = S(r, "fin") })))
                porId[id].Horarios.Add(b);
            return lista;
        }

        public List<Materia> Materias(long gestionId) => Operacion(s => MateriasDe(s, "WHERE m.gestion_id = @gestionId", new { gestionId }));

        /// <summary>Materias de la gestión activa que puede ver el usuario (el admin, todas; un docente, las suyas).</summary>
        public List<Materia> MateriasDe(Usuario u) => Operacion(s => u.EsAdmin
            ? MateriasDe(s, "WHERE g.activa = 1")
            : MateriasDe(s, "WHERE g.activa = 1 AND m.docente_id = @docente", new { docente = u.DocenteId }));

        public Materia MateriaPorId(long id) => Operacion(s => MateriasDe(s, "WHERE m.id = @id", new { id }).FirstOrDefault());

        public int AsistenciasDeMateria(long id) => (int)Operacion(s => s.Numero("SELECT COUNT(*) FROM asistencias WHERE materia_id = @id", new { id }));

        /// <summary>Crea o actualiza la materia y reemplaza su horario. Revisa que no choque con otra materia del mismo semestre.</summary>
        public long GuardarMateria(Materia m)
        {
            string nombre = (m.Nombre ?? "").Trim(), sigla = (m.Sigla ?? "").Trim().ToUpperInvariant();
            if (nombre.Length == 0) throw new ErrorValidacion("Escribe el nombre de la materia");
            if (nombre.Length > 100 || sigla.Length > 20) throw new ErrorValidacion("Texto demasiado largo");
            if (m.Semestre < 1 || m.Semestre > 6) throw new ErrorValidacion("Elige el semestre (1ro a 6to)");
            if (m.ToleranciaMin < 0 || m.ToleranciaMin > 120) throw new ErrorValidacion("La tolerancia debe estar entre 0 y 120 minutos");
            var bloques = m.Horarios ?? new List<BloqueHorario>();
            if (bloques.Count == 0) throw new ErrorValidacion("Agrega al menos un día al horario (si no, la cámara nunca marcará en esta materia)");
            foreach (var b in bloques)
            {
                if (b.Dia < 1 || b.Dia > 7) throw new ErrorValidacion("Día inválido en el horario");
                if (!Academico.HoraValida(b.Inicio) || !Academico.HoraValida(b.Fin))
                    throw new ErrorValidacion($"Hora inválida el {Academico.Dia(b.Dia)}: escribe HH:mm (por ejemplo 08:00)");
                if (b.HoraFin <= b.HoraInicio) throw new ErrorValidacion($"El {Academico.Dia(b.Dia)} la hora de fin debe ser posterior al inicio");
            }
            static bool Choca(BloqueHorario a, BloqueHorario b) => a.Dia == b.Dia && a.HoraInicio < b.HoraFin && b.HoraInicio < a.HoraFin;
            for (int i = 0; i < bloques.Count; i++)
                for (int j = i + 1; j < bloques.Count; j++)
                    if (Choca(bloques[i], bloques[j])) throw new ErrorValidacion($"El horario se repite: {bloques[i]} y {bloques[j]}");

            return Operacion(s =>
            {
                if (s.Numero("SELECT COUNT(*) FROM gestiones WHERE id = @id", new { id = m.GestionId }) == 0) throw new ErrorValidacion("Elige la gestión");
                // Un semestre no puede tener dos clases a la vez: la cámara no sabría en cuál marcar.
                foreach (var otra in MateriasDe(s, "WHERE m.gestion_id = @g AND m.semestre = @sem AND m.id <> @id", new { g = m.GestionId, sem = m.Semestre, id = m.Id }))
                    foreach (var b in bloques)
                        foreach (var o in otra.Horarios.Where(o => Choca(b, o)))
                            throw new ErrorValidacion($"{b} choca con «{otra.Titulo}» ({o}). Un semestre no puede tener dos clases a la vez.");
                var v = new { id = m.Id, gestion = m.GestionId, semestre = m.Semestre, sigla, nombre, docente = m.DocenteId, tol = m.ToleranciaMin };
                long id = m.Id;
                if (id > 0)
                    s.Ejecutar("UPDATE materias SET gestion_id = @gestion, semestre = @semestre, sigla = @sigla, nombre = @nombre, docente_id = @docente, " +
                               "tolerancia_min = @tol WHERE id = @id", v);
                else
                {
                    s.Ejecutar("INSERT INTO materias (gestion_id, semestre, sigla, nombre, docente_id, tolerancia_min) VALUES (@gestion, @semestre, @sigla, @nombre, @docente, @tol)", v);
                    id = s.UltimoId();
                }
                s.Ejecutar("DELETE FROM horarios WHERE materia_id = @id", new { id });
                foreach (var b in bloques.OrderBy(b => b.Dia).ThenBy(b => b.HoraInicio))
                    s.Ejecutar("INSERT INTO horarios (materia_id, dia, inicio, fin) VALUES (@id, @dia, @inicio, @fin)", new { id, dia = b.Dia, inicio = b.Inicio, fin = b.Fin });
                return id;
            });
        }

        /// <summary>Borra la materia con su horario y TODAS sus asistencias.</summary>
        public void EliminarMateria(long id) => Operacion(s =>
        {
            var m = MateriasDe(s, "WHERE m.id = @id", new { id }).FirstOrDefault() ?? throw new ErrorValidacion("La materia no existe");
            s.Ejecutar("DELETE FROM materias WHERE id = @id", new { id });
            RegistrarEvento(s, null, "materia_eliminada", $"Se eliminó la materia {m.Titulo} ({m.Gestion}) con sus asistencias", "normal", "admin");
        });

        /// <summary>
        /// Clases de la gestión activa en curso en «ahora»: desde «AnticipacionMin» antes del primer bloque
        /// del día hasta «SalidaDespuesMin» después del último (para poder marcar la salida).
        /// </summary>
        static List<ClaseEnCurso> ClasesEn(Sesion s, DateTime ahora, ConfigHorario h)
        {
            int dia = Academico.DiaDe(ahora);
            var t = ahora.TimeOfDay;
            var antes = TimeSpan.FromMinutes(Math.Max(0, h.AnticipacionMin));
            var despues = TimeSpan.FromMinutes(Math.Max(0, h.SalidaDespuesMin));
            return MateriasDe(s, "WHERE g.activa = 1 AND m.id IN (SELECT materia_id FROM horarios WHERE dia = @dia)", new { dia })
                .Select(m =>
                {
                    var hoy = m.Horarios.Where(b => b.Dia == dia).OrderBy(b => b.HoraInicio).ToList();
                    return new ClaseEnCurso { Materia = m, Inicio = hoy[0].Inicio, Fin = hoy.Max(b => b.HoraFin).ToString(@"hh\:mm") };
                })
                .Where(c => t >= c.HoraInicio - antes && t <= c.HoraFin + despues)
                .OrderBy(c => c.HoraInicio).ToList();
        }

        public List<ClaseEnCurso> ClasesEnCurso(DateTime ahora, ConfigHorario h) => Operacion(s => ClasesEn(s, ahora, h));

        // ───────────────────────── estudiantes ─────────────────────────
        public List<Estudiante> Estudiantes(bool soloActivos = false) => Operacion(s =>
            s.Consultar(SqlEstudiantes + (soloActivos ? "WHERE e.activo=1 " : "") + "ORDER BY e.apellidos, e.nombres", MapaEstudiante));

        public Estudiante EstudiantePorCodigo(string codigo) => Operacion(s =>
            s.Consultar(SqlEstudiantes + "WHERE e.codigo=@codigo", MapaEstudiante, new { codigo = (codigo ?? "").Trim() }).FirstOrDefault());

        public Estudiante EstudiantePorId(long id) => Operacion(s =>
            s.Consultar(SqlEstudiantes + "WHERE e.id=@id", MapaEstudiante, new { id }).FirstOrDefault());

        /// <summary>
        /// Crea o actualiza (si Id &gt; 0). Devuelve el id. Con «conSemestre» también lo inscribe en
        /// e.Semestre de la gestión activa (0 = quitar la inscripción).
        /// </summary>
        public long GuardarEstudiante(Estudiante e, bool conSemestre = true)
        {
            string codigo = (e.Codigo ?? "").Trim(), nombres = (e.Nombres ?? "").Trim(), apellidos = (e.Apellidos ?? "").Trim();
            if (codigo.Length == 0) throw new ErrorValidacion("Escribe el código o CI del estudiante");
            if (nombres.Length == 0) throw new ErrorValidacion("Escribe el nombre del estudiante");
            if (codigo.Length > 30 || nombres.Length > 60 || apellidos.Length > 60 || (e.Curso ?? "").Trim().Length > 100)
                throw new ErrorValidacion("Texto demasiado largo");
            if (conSemestre && (e.Semestre < 0 || e.Semestre > 6)) throw new ErrorValidacion("Semestre inválido (1ro a 6to)");
            return Operacion(s =>
            {
                var otro = s.Consultar("SELECT id FROM estudiantes WHERE codigo=@codigo AND id!=@id", r => L(r, "id"), new { codigo, id = e.Id });
                if (otro.Count > 0) throw new ErrorValidacion($"Ya existe un estudiante con el código {codigo}");
                var v = new { id = e.Id, codigo, nombres, apellidos, curso = (e.Curso ?? "").Trim(), activo = e.Activo ? 1 : 0, creado = Ahora() };
                long id = e.Id;
                if (id > 0)
                    s.Ejecutar("UPDATE estudiantes SET codigo=@codigo, nombres=@nombres, apellidos=@apellidos, curso=@curso, activo=@activo WHERE id=@id", v);
                else
                {
                    s.Ejecutar("INSERT INTO estudiantes (codigo, nombres, apellidos, curso, activo, creado) VALUES (@codigo, @nombres, @apellidos, @curso, @activo, @creado)", v);
                    id = s.UltimoId();
                }
                if (conSemestre)
                {
                    long gestion = GestionActivaId(s);
                    if (gestion == 0 && e.Semestre > 0) throw new ErrorValidacion("No hay gestión activa: créala en «Académico» para inscribir estudiantes");
                    if (e.Semestre == 0) s.Ejecutar("DELETE FROM inscripciones WHERE estudiante_id = @id AND gestion_id = @gestion", new { id, gestion });
                    else s.Ejecutar("INSERT INTO inscripciones (estudiante_id, gestion_id, semestre) VALUES (@id, @gestion, @semestre) " +
                                    "ON DUPLICATE KEY UPDATE semestre = @semestre", new { id, gestion, semestre = e.Semestre });
                }
                return id;
            });
        }

        /// <summary>Borra al estudiante con TODOS sus datos (rostros, inscripciones y asistencias).</summary>
        public void EliminarEstudiante(long id)
        {
            Operacion(s =>
            {
                var e = s.Consultar(SqlEstudiantes + "WHERE e.id=@id", MapaEstudiante, new { id }).FirstOrDefault()
                        ?? throw new ErrorValidacion("El estudiante no existe");
                s.Ejecutar("DELETE FROM estudiantes WHERE id=@id", new { id });
                RegistrarEvento(s, null, "eliminado", $"Se eliminó a {e.NombreCompleto} ({e.Codigo}) y sus datos de rostro", "normal", "admin");
            });
            VersionRostros++;
        }

        /// <summary>
        /// Importa una lista CSV: codigo,nombres,apellidos,curso,semestre (acepta ; o , y encabezado).
        /// El semestre (1 a 6, «4to», «cuarto») es opcional: si viene, inscribe en la gestión activa.
        /// </summary>
        public (int nuevos, int actualizados, List<string> errores) ImportarCsv(string texto)
        {
            int nuevos = 0, act = 0;
            var errores = new List<string>();
            int n = 0;
            foreach (var linea in texto.Replace("\r", "").Split('\n'))
            {
                n++;
                if (linea.Trim().Length == 0) continue;
                char sep = linea.Contains(';') ? ';' : linea.Contains('\t') ? '\t' : ',';
                var c = linea.Split(sep).Select(x => x.Trim().Trim('"')).ToArray();
                if (n == 1 && c[0].ToLowerInvariant().StartsWith("cod")) continue;      // encabezado
                if (c.Length < 2) { errores.Add($"Línea {n}: faltan columnas"); continue; }
                int semestre = c.Length > 4 ? Academico.LeerSemestre(c[4]) : 0;
                if (semestre < 0) { errores.Add($"Línea {n}: semestre «{c[4]}» no válido (usa 1 a 6)"); continue; }
                var existe = EstudiantePorCodigo(c[0]);
                try
                {
                    GuardarEstudiante(new Estudiante
                    {
                        Id = existe?.Id ?? 0, Codigo = c[0], Nombres = c[1], Apellidos = c.Length > 2 ? c[2] : "",
                        Curso = c.Length > 3 ? c[3] : "", Activo = true, Semestre = semestre,
                    }, conSemestre: semestre > 0);
                    if (existe == null) nuevos++; else act++;
                }
                catch (ErrorValidacion e) { errores.Add($"Línea {n}: {e.Message}"); }
            }
            return (nuevos, act, errores);
        }

        // ───────────────────────── rostros ─────────────────────────
        static byte[] ABytes(float[] v)
        {
            var b = new byte[v.Length * 4];
            Buffer.BlockCopy(v, 0, b, 0, b.Length);
            return b;
        }

        static float[] AVector(byte[] b)
        {
            var v = new float[b.Length / 4];
            Buffer.BlockCopy(b, 0, v, 0, b.Length);
            return v;
        }

        public void GuardarRostros(long estudianteId, IList<(float[] vector, byte[] foto)> muestras, bool reemplazar)
        {
            if (muestras == null || muestras.Count == 0) throw new ErrorValidacion("No hay muestras del rostro");
            Operacion(s =>
            {
                var e = s.Consultar(SqlEstudiantes + "WHERE e.id=@id", MapaEstudiante, new { id = estudianteId }).FirstOrDefault()
                        ?? throw new ErrorValidacion("El estudiante no existe");
                if (reemplazar) s.Ejecutar("DELETE FROM rostros WHERE estudiante_id=@estudianteId", new { estudianteId });
                foreach (var (vector, foto) in muestras)
                    s.Ejecutar("INSERT INTO rostros (estudiante_id, vector, foto, creado) VALUES (@estudianteId, @vector, @foto, @creado)",
                        new { estudianteId, vector = ABytes(vector), foto, creado = Ahora() });
                RegistrarEvento(s, estudianteId, "registro", $"Rostro de {e.NombreCompleto} registrado ({muestras.Count} muestras)", "normal", "admin");
            });
            VersionRostros++;
        }

        public void BorrarRostros(long estudianteId)
        {
            Operacion(s => s.Ejecutar("DELETE FROM rostros WHERE estudiante_id=@estudianteId", new { estudianteId }));
            VersionRostros++;
        }

        /// <summary>Todos los vectores de los estudiantes activos (la "galería" que compara la cámara).</summary>
        public List<(long id, string nombre, float[] vector)> Galeria() => Operacion(s =>
            s.Consultar("SELECT r.estudiante_id, e.nombres, e.apellidos, r.vector FROM rostros r JOIN estudiantes e ON e.id = r.estudiante_id " +
                        "WHERE e.activo = 1", r => (L(r, "estudiante_id"), (S(r, "nombres") + " " + S(r, "apellidos")).Trim(), AVector(B(r, "vector")))));

        /// <summary>Vectores de UN estudiante (para verificar 1:1 cuando marca desde el celular con su código).</summary>
        public List<(long id, string nombre, float[] vector)> Galeria(long estudianteId) => Operacion(s =>
            s.Consultar("SELECT r.estudiante_id, e.nombres, e.apellidos, r.vector FROM rostros r JOIN estudiantes e ON e.id = r.estudiante_id " +
                        "WHERE e.activo = 1 AND e.id = @estudianteId", r => (L(r, "estudiante_id"), (S(r, "nombres") + " " + S(r, "apellidos")).Trim(),
                        AVector(B(r, "vector"))), new { estudianteId }));

        public byte[] Foto(long estudianteId) => Operacion(s =>
            s.Consultar("SELECT foto FROM rostros WHERE estudiante_id=@estudianteId AND foto IS NOT NULL ORDER BY id LIMIT 1",
                r => B(r, "foto"), new { estudianteId }).FirstOrDefault());

        // ───────────────────────── marcas de asistencia ─────────────────────────
        // Lista de UNA materia en UN día: los inscritos en su semestre (y quien ya tenga marca).
        const string SqlFila =
            "SELECT e.id AS estudiante_id, e.codigo, e.nombres, e.apellidos, e.curso, " +
            "(SELECT COUNT(*) FROM rostros r WHERE r.estudiante_id = e.id) AS muestras, " +
            "m.id AS materia_id, m.sigla, m.nombre AS materia, " +
            "a.id AS asistencia_id, a.fecha, a.hora, a.hora_salida, a.estado, a.similitud, a.fuente, a.observacion " +
            "FROM estudiantes e JOIN materias m ON m.id = @materiaId " +
            "LEFT JOIN inscripciones i ON i.estudiante_id = e.id AND i.gestion_id = m.gestion_id " +
            "LEFT JOIN asistencias a ON a.estudiante_id = e.id AND a.materia_id = m.id AND a.fecha = @fecha ";

        static FilaAsistencia MapaFila(MySqlDataReader r) => new FilaAsistencia
        {
            EstudianteId = L(r, "estudiante_id"), Codigo = S(r, "codigo"),
            Nombre = (S(r, "apellidos") + " " + S(r, "nombres")).Trim(), Curso = S(r, "curso"), Muestras = (int)L(r, "muestras"),
            MateriaId = L(r, "materia_id"), Materia = (S(r, "sigla") + " " + S(r, "materia")).Trim(),
            Fecha = S(r, "fecha"), Hora = DN(r, "hora"), HoraSalida = DN(r, "hora_salida"), Estado = r.IsDBNull(Col(r, "estado")) ? "falta" : S(r, "estado"),
            Similitud = Math.Round(D(r, "similitud"), 3), Fuente = S(r, "fuente"), Observacion = S(r, "observacion"),
        };

        static FilaAsistencia Fila(Sesion s, long estudianteId, long materiaId, string fecha) =>
            s.Consultar(SqlFila + "WHERE e.id = @estudianteId", MapaFila, new { fecha, estudianteId, materiaId }).First();

        /// <summary>
        /// Marca por reconocimiento facial: "camara" (puerta del aula) o "celular" (selfie en la web).
        /// Busca el semestre del estudiante en la gestión activa y la materia que tiene clase ahora:
        ///  • sin entrada → ENTRADA (puntual hasta inicio + tolerancia, después atraso);
        ///  • con entrada y cerca del fin (desde SalidaAntesMin antes) → SALIDA;
        ///  • con entrada en medio de la clase (o en el descanso) → no cambia nada.
        /// </summary>
        public ResultadoMarca MarcarReconocido(long estudianteId, double similitud, DateTime ahora, ConfigHorario h, string fuente)
        {
            string fecha = Dia(ahora);
            return Operacion(s =>
            {
                var est = s.Consultar(SqlEstudiantes + "WHERE e.id=@id", MapaEstudiante, new { id = estudianteId }).FirstOrDefault()
                          ?? throw new ErrorValidacion("El estudiante no existe");
                var sinMarca = new FilaAsistencia
                {
                    EstudianteId = est.Id, Codigo = est.Codigo, Nombre = (est.Apellidos + " " + est.Nombres).Trim(), Curso = est.Curso,
                    Muestras = est.Muestras, Fecha = fecha,
                };
                if (est.Semestre == 0) return new ResultadoMarca { Fila = sinMarca, Motivo = "no está inscrito en la gestión activa" };
                var clases = ClasesEn(s, ahora, h).Where(c => c.Materia.Semestre == est.Semestre).ToList();
                if (clases.Count == 0)
                    return new ResultadoMarca { Fila = sinMarca, Motivo = $"{Academico.Semestre(est.Semestre)} semestre no tiene clase a esta hora" };
                var t = ahora.TimeOfDay;
                FilaAsistencia fila = null;
                bool termino = false;
                foreach (var c in clases)          // si ya marcó la clase que termina, se prueba la que empieza
                {
                    fila = Fila(s, estudianteId, c.Materia.Id, fecha);
                    if (fila.Hora.HasValue)
                    {
                        bool horaDeSalida = t >= c.HoraFin - TimeSpan.FromMinutes(Math.Max(0, h.SalidaAntesMin));
                        if (fila.HoraSalida.HasValue || !horaDeSalida || fila.Estado == "justificado") continue;
                        s.Ejecutar("UPDATE asistencias SET hora_salida = @hora, similitud_salida = @similitud, fuente_salida = @fuente " +
                                   "WHERE estudiante_id = @estudianteId AND materia_id = @materia AND fecha = @fecha",
                            new { hora = Unix(ahora), similitud, fuente, estudianteId, materia = c.Materia.Id, fecha });
                        fila = Fila(s, estudianteId, c.Materia.Id, fecha);
                        RegistrarEvento(s, estudianteId, "salida",
                            $"{fila.Nombre} salió de {c.Materia.Titulo} a las {ahora:HH:mm}" +
                            $"{(fuente == "celular" ? " · marcó con selfie desde el celular" : "")}", "normal", fuente);
                        return new ResultadoMarca { Fila = fila, Nueva = true, Tipo = "salida" };
                    }
                    if (t > c.HoraFin) { termino = true; continue; }       // llegó después del fin: ya no hay entrada
                    string estado = t <= c.Limite ? "puntual" : "atraso";
                    s.Ejecutar("INSERT INTO asistencias (estudiante_id, materia_id, fecha, hora, estado, similitud, fuente) " +
                               "VALUES (@estudianteId, @materia, @fecha, @hora, @estado, @similitud, @fuente)",
                        new { estudianteId, materia = c.Materia.Id, fecha, hora = Unix(ahora), estado, similitud, fuente });
                    fila = Fila(s, estudianteId, c.Materia.Id, fecha);
                    RegistrarEvento(s, estudianteId, "marca_" + estado,
                        $"{fila.Nombre} llegó a {c.Materia.Titulo} a las {ahora:HH:mm} — {(estado == "puntual" ? "puntual" : "con atraso")}" +
                        $"{(fuente == "celular" ? " · marcó con selfie desde el celular" : "")} (similitud {similitud.ToString("0.00", CultureInfo.InvariantCulture)})",
                        "normal", fuente);
                    return new ResultadoMarca { Fila = fila, Nueva = true };
                }
                if (termino && !fila.Hora.HasValue)
                    return new ResultadoMarca { Fila = fila, Motivo = $"la clase de {fila.Materia} ya terminó" };
                return new ResultadoMarca { Fila = fila, Nueva = false, Tipo = fila.HoraSalida.HasValue ? "salida" : "entrada" };
            });
        }

        /// <summary>Corrección del docente: marca la salida (ahora, o a la hora de fin si es otro día) o la quita.</summary>
        public void SalidaManual(long estudianteId, long materiaId, string fecha, bool quitar, string fuente = "manual")
        {
            if (!DateTime.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia))
                throw new ErrorValidacion("Fecha inválida");
            Operacion(s =>
            {
                var m = MateriasDe(s, "WHERE m.id = @materiaId", new { materiaId }).FirstOrDefault() ?? throw new ErrorValidacion("Elige una materia");
                var fila = Fila(s, estudianteId, materiaId, fecha);
                if (!fila.Hora.HasValue) throw new ErrorValidacion($"{fila.Nombre} no tiene entrada ese día: primero marca la entrada");
                double? hora = null;
                if (!quitar)
                {
                    var bloques = m.Horarios.Where(b => b.Dia == Academico.DiaDe(dia)).ToList();
                    hora = dia.Date == DateTime.Today ? Ahora()
                         : Unix(dia.Date + (bloques.Count > 0 ? bloques.Max(b => b.HoraFin) : TimeSpan.FromHours(13)));
                }
                s.Ejecutar("UPDATE asistencias SET hora_salida = @hora, similitud_salida = NULL, fuente_salida = @f " +
                           "WHERE estudiante_id = @estudianteId AND materia_id = @materiaId AND fecha = @fecha",
                    new { hora, f = quitar ? null : fuente, estudianteId, materiaId, fecha });
                RegistrarEvento(s, estudianteId, "manual", $"{fila.Nombre}: {(quitar ? "se quitó la salida" : "salida marcada a mano")} en {m.Titulo} el {fecha}",
                    "normal", fuente);
            });
        }

        /// <summary>Corrección del docente: puntual, atraso, justificado o "falta" (borra la marca).</summary>
        public void MarcaManual(long estudianteId, long materiaId, string fecha, string estado, string observacion, string fuente = "manual")
        {
            if (!DateTime.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia))
                throw new ErrorValidacion("Fecha inválida");
            if (estado != "falta" && !EstadosMarca.Contains(estado)) throw new ErrorValidacion("Estado inválido");
            observacion = (observacion ?? "").Trim();
            if (observacion.Length > 120) observacion = observacion.Substring(0, 120);
            Operacion(s =>
            {
                if (s.Numero("SELECT COUNT(*) FROM materias WHERE id = @materiaId", new { materiaId }) == 0) throw new ErrorValidacion("Elige una materia");
                var fila = Fila(s, estudianteId, materiaId, fecha);
                if (estado == "falta")
                    s.Ejecutar("DELETE FROM asistencias WHERE estudiante_id=@estudianteId AND materia_id=@materiaId AND fecha=@fecha", new { estudianteId, materiaId, fecha });
                else if (fila.Hora.HasValue)
                    s.Ejecutar("UPDATE asistencias SET estado=@estado, fuente=@fuente, observacion=@observacion " +
                               "WHERE estudiante_id=@estudianteId AND materia_id=@materiaId AND fecha=@fecha",
                        new { estado, fuente, observacion, estudianteId, materiaId, fecha });
                else
                {
                    double hora = dia.Date == DateTime.Today ? Ahora() : Unix(dia.Date.AddHours(12));
                    s.Ejecutar("INSERT INTO asistencias (estudiante_id, materia_id, fecha, hora, estado, fuente, observacion) " +
                               "VALUES (@estudianteId, @materiaId, @fecha, @hora, @estado, @fuente, @observacion)",
                        new { estudianteId, materiaId, fecha, hora, estado, fuente, observacion });
                }
                RegistrarEvento(s, estudianteId, "manual", $"{fila.Nombre}: marcado «{estado}» a mano en {fila.Materia} el {fecha}" +
                    (observacion.Length > 0 ? $" ({observacion})" : ""), "normal", fuente);
            });
        }

        public List<FilaAsistencia> ListaDelDia(long materiaId, string fecha) => Operacion(s =>
            s.Consultar(SqlFila + "WHERE (e.activo = 1 AND i.semestre = m.semestre) OR a.id IS NOT NULL ORDER BY e.apellidos, e.nombres",
                MapaFila, new { fecha, materiaId }));

        public static ResumenDia Resumir(string fecha, List<FilaAsistencia> lista) => new ResumenDia
        {
            Fecha = fecha, Total = lista.Count,
            Puntuales = lista.Count(f => f.Estado == "puntual"), Atrasos = lista.Count(f => f.Estado == "atraso"),
            Justificados = lista.Count(f => f.Estado == "justificado"), Faltan = lista.Count(f => f.Estado == "falta"),
            Presentes = lista.Count(f => f.Estado == "puntual" || f.Estado == "atraso"),
        };

        /// <summary>Lista de la materia en la fecha + alertas nuevas (materiaId 0 = solo alertas).</summary>
        public EstadoPanel EstadoAdmin(long desdeEvento, long materiaId, string fecha = null)
        {
            fecha ??= Hoy();
            var lista = materiaId > 0 ? ListaDelDia(materiaId, fecha) : new List<FilaAsistencia>();
            return Operacion(s => new EstadoPanel
            {
                Ahora = Ahora(),
                Lista = lista,
                Resumen = Resumir(fecha, lista),
                Eventos = desdeEvento > 0
                    ? s.Consultar("SELECT * FROM eventos WHERE id>@desdeEvento ORDER BY id LIMIT 100", MapaEvento, new { desdeEvento })
                    : s.Consultar("SELECT * FROM eventos ORDER BY id DESC LIMIT 40", MapaEvento).AsEnumerable().Reverse().ToList(),
                UltimoEvento = (long)s.Numero("SELECT COALESCE(MAX(id),0) FROM eventos"),
            });
        }

        public List<Evento> UltimosEventos(int n = 300) => Operacion(s =>
            s.Consultar("SELECT * FROM eventos ORDER BY id DESC LIMIT @n", MapaEvento, new { n }));

        public void AtenderTodos() => Operacion(s => s.Ejecutar("UPDATE eventos SET atendido=1 WHERE atendido=0"));

        // ───────────────────────── reportes ─────────────────────────
        /// <summary>Días de clase de la materia = fechas en las que al menos un estudiante marcó en ella.</summary>
        public List<string> DiasDeClase(long materiaId, string desde, string hasta) => Operacion(s =>
            s.Consultar("SELECT DISTINCT fecha FROM asistencias WHERE materia_id = @materiaId AND fecha BETWEEN @desde AND @hasta ORDER BY fecha",
                r => S(r, "fecha"), new { materiaId, desde, hasta }));

        /// <summary>Por estudiante inscrito en el semestre de la materia: puntuales, atrasos, justificados, faltas y %.</summary>
        public List<FilaReporte> Reporte(long materiaId, string desde, string hasta)
        {
            var materia = MateriaPorId(materiaId) ?? throw new ErrorValidacion("Elige una materia");
            int dias = DiasDeClase(materiaId, desde, hasta).Count;
            return Operacion(s => s.Consultar(
                "SELECT e.id, e.codigo, e.nombres, e.apellidos, " +
                " SUM(CASE WHEN a.estado='puntual' THEN 1 ELSE 0 END) AS p, SUM(CASE WHEN a.estado='atraso' THEN 1 ELSE 0 END) AS t, " +
                " SUM(CASE WHEN a.estado='justificado' THEN 1 ELSE 0 END) AS j, " +
                "SUM(CASE WHEN a.estado IN ('puntual', 'atraso') AND a.hora_salida IS NULL AND a.fecha < @hoy THEN 1 ELSE 0 END) AS ss " +
                "FROM estudiantes e JOIN materias m ON m.id = @materiaId " +
                "JOIN inscripciones i ON i.estudiante_id = e.id AND i.gestion_id = m.gestion_id AND i.semestre = m.semestre " +
                "LEFT JOIN asistencias a ON a.estudiante_id = e.id AND a.materia_id = m.id AND a.fecha BETWEEN @desde AND @hasta " +
                "WHERE e.activo = 1 GROUP BY e.id, e.codigo, e.nombres, e.apellidos ORDER BY e.apellidos, e.nombres",
                r =>
                {
                    int p = (int)L(r, "p"), t = (int)L(r, "t"), j = (int)L(r, "j");
                    return new FilaReporte
                    {
                        EstudianteId = L(r, "id"), Codigo = S(r, "codigo"), Nombre = (S(r, "apellidos") + " " + S(r, "nombres")).Trim(),
                        MateriaId = materia.Id, Materia = materia.Titulo, Docente = materia.Docente,
                        Dias = dias, Puntuales = p, Atrasos = t, Justificados = j, Faltas = Math.Max(0, dias - p - t - j), SinSalida = (int)L(r, "ss"),
                        Porcentaje = dias == 0 ? 0 : Math.Round(100.0 * (p + t + j) / dias, 1),
                    };
                }, new { materiaId, desde, hasta, hoy = Hoy() }));
        }

        /// <summary>Lo que ve un estudiante en la web: su % en cada materia de la gestión activa y sus últimas clases.</summary>
        public (Estudiante est, Gestion gestion, List<FilaReporte> materias, List<FilaAsistencia> dias) Historial(string codigo)
        {
            var e = EstudiantePorCodigo(codigo);
            if (e == null) return (null, null, null, null);
            var g = GestionActiva();
            if (g == null || e.Semestre == 0) return (e, g, new List<FilaReporte>(), new List<FilaAsistencia>());
            string hasta = string.CompareOrdinal(Hoy(), g.Fin) < 0 ? Hoy() : g.Fin;
            return Operacion(s =>
            {
                var v = new { est = e.Id, g = g.Id, sem = e.Semestre, desde = g.Inicio, hasta, hoy = Hoy() };
                var materias = s.Consultar(
                    "SELECT m.id, m.sigla, m.nombre, CONCAT(COALESCE(d.nombres, ''), ' ', COALESCE(d.apellidos, '')) AS docente, " +
                    " (SELECT COUNT(DISTINCT a2.fecha) FROM asistencias a2 WHERE a2.materia_id = m.id AND a2.fecha BETWEEN @desde AND @hasta) AS dias, " +
                    " SUM(CASE WHEN a.estado='puntual' THEN 1 ELSE 0 END) AS p, SUM(CASE WHEN a.estado='atraso' THEN 1 ELSE 0 END) AS t, " +
                    " SUM(CASE WHEN a.estado='justificado' THEN 1 ELSE 0 END) AS j, " +
                    " SUM(CASE WHEN a.estado IN ('puntual', 'atraso') AND a.hora_salida IS NULL AND a.fecha < @hoy THEN 1 ELSE 0 END) AS ss " +
                    "FROM materias m LEFT JOIN docentes d ON d.id = m.docente_id " +
                    "LEFT JOIN asistencias a ON a.materia_id = m.id AND a.estudiante_id = @est AND a.fecha BETWEEN @desde AND @hasta " +
                    "WHERE m.gestion_id = @g AND m.semestre = @sem GROUP BY m.id, m.sigla, m.nombre, d.nombres, d.apellidos ORDER BY m.nombre",
                    r =>
                    {
                        int dias = (int)L(r, "dias"), p = (int)L(r, "p"), t = (int)L(r, "t"), j = (int)L(r, "j");
                        return new FilaReporte
                        {
                            EstudianteId = e.Id, Codigo = e.Codigo, Nombre = e.NombreCompleto, MateriaId = L(r, "id"),
                            Materia = (S(r, "sigla") + " " + S(r, "nombre")).Trim(), Docente = S(r, "docente").Trim(),
                            Dias = dias, Puntuales = p, Atrasos = t, Justificados = j, Faltas = Math.Max(0, dias - p - t - j), SinSalida = (int)L(r, "ss"),
                            Porcentaje = dias == 0 ? 0 : Math.Round(100.0 * (p + t + j) / dias, 1),
                        };
                    }, v);
                // cada clase dictada (día en que alguien marcó en la materia) y lo que hizo este estudiante
                var dias = s.Consultar(
                    "SELECT x.fecha, m.id AS materia_id, m.sigla, m.nombre AS materia, a.hora, a.hora_salida, a.estado, a.observacion " +
                    "FROM (SELECT DISTINCT materia_id, fecha FROM asistencias WHERE fecha BETWEEN @desde AND @hasta) x " +
                    "JOIN materias m ON m.id = x.materia_id AND m.gestion_id = @g AND m.semestre = @sem " +
                    "LEFT JOIN asistencias a ON a.materia_id = x.materia_id AND a.fecha = x.fecha AND a.estudiante_id = @est " +
                    "ORDER BY x.fecha DESC, m.nombre LIMIT 200",
                    r => new FilaAsistencia
                    {
                        EstudianteId = e.Id, Codigo = e.Codigo, Nombre = e.NombreCompleto, Fecha = S(r, "fecha"),
                        MateriaId = L(r, "materia_id"), Materia = (S(r, "sigla") + " " + S(r, "materia")).Trim(),
                        Hora = DN(r, "hora"), HoraSalida = DN(r, "hora_salida"), Estado = r.IsDBNull(Col(r, "estado")) ? "falta" : S(r, "estado"), Observacion = S(r, "observacion"),
                    }, v);
                return (e, g, materias, dias);
            });
        }

        // ───────────────────────── exportar ─────────────────────────
        static string Csv(IEnumerable<string> celdas) =>
            string.Join(",", celdas.Select(t => (t ?? "").Contains(',') || (t ?? "").Contains('"') ? "\"" + t.Replace("\"", "\"\"") + "\"" : t ?? ""));

        /// <summary>CSV de las materias indicadas (asistencias, reporte) o de todo (estudiantes, eventos).</summary>
        public string ExportarCsv(string tipo, IList<long> materias, string desde, string hasta)
        {
            var sb = new StringBuilder("﻿");
            var inv = CultureInfo.InvariantCulture;
            switch (tipo)
            {
                case "asistencias":
                    sb.AppendLine("fecha,materia,codigo,estudiante,estado,entrada,salida,similitud,fuente,observacion");
                    foreach (long m in materias)
                        foreach (var f in DiasDeClase(m, desde, hasta))
                            foreach (var x in ListaDelDia(m, f))
                                sb.AppendLine(Csv(new[] { f, x.Materia, x.Codigo, x.Nombre, x.Estado, x.Hora.HasValue ? Local(x.Hora.Value).ToString("HH:mm:ss", inv) : "",
                                    x.HoraSalida.HasValue ? Local(x.HoraSalida.Value).ToString("HH:mm:ss", inv) : "",
                                    x.Similitud.ToString("0.000", inv), x.Fuente, x.Observacion }));
                    break;
                case "reporte":
                    sb.AppendLine($"materia,docente,codigo,estudiante,dias_de_clase,puntuales,atrasos,justificados,faltas,sin_salida,porcentaje (del {desde} al {hasta})");
                    foreach (long m in materias)
                        foreach (var r in Reporte(m, desde, hasta))
                            sb.AppendLine(Csv(new[] { r.Materia, r.Docente, r.Codigo, r.Nombre, r.Dias.ToString(), r.Puntuales.ToString(), r.Atrasos.ToString(),
                                r.Justificados.ToString(), r.Faltas.ToString(), r.SinSalida.ToString(), r.Porcentaje.ToString("0.0", inv) }));
                    break;
                case "estudiantes":
                    sb.AppendLine("codigo,nombres,apellidos,curso,semestre,activo,muestras_rostro");
                    foreach (var e in Estudiantes())
                        sb.AppendLine(Csv(new[] { e.Codigo, e.Nombres, e.Apellidos, e.Curso, e.Semestre > 0 ? e.Semestre.ToString() : "",
                            e.Activo ? "1" : "0", e.Muestras.ToString() }));
                    break;
                case "eventos":
                    sb.AppendLine("fecha_hora,tipo,prioridad,fuente,mensaje");
                    foreach (var ev in UltimosEventos(5000).AsEnumerable().Reverse())
                        sb.AppendLine(Csv(new[] { Local(ev.Creado).ToString("yyyy-MM-dd HH:mm:ss", inv), ev.Tipo, ev.Prioridad, ev.Fuente, ev.Mensaje }));
                    break;
                default:
                    throw new ErrorValidacion("Tipo de exportación inválido");
            }
            return sb.ToString();
        }
    }
}
