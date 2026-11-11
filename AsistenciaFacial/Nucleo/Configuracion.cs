using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsistenciaFacial.Nucleo
{
    /// <summary>
    /// El horario de cada materia está en la base de datos (ventana «Académico»).
    /// Aquí solo queda cuánto antes del inicio de la clase ya se puede marcar.
    /// </summary>
    public class ConfigHorario
    {
        public int AnticipacionMin { get; set; } = 15;         // la cámara marca la entrada desde 15 min antes del inicio
        public int SalidaAntesMin { get; set; } = 15;          // la salida se marca desde 15 min antes del fin…
        public int SalidaDespuesMin { get; set; } = 30;        // …hasta 30 min después del fin

        // Horario único de la versión anterior (una sola materia): solo se usa al actualizar
        // la base de datos, para crear el horario de esa materia.
        public string HoraEntrada { get; set; } = "08:00";
        public int ToleranciaMin { get; set; } = 10;
        public string HoraCierre { get; set; } = "13:00";
    }

    public class ConfigReconocimiento
    {
        public string Fuente { get; set; } = "0";              // "0", "1", URL, video .mp4 o carpeta con fotos
        public bool IniciarAutomatico { get; set; } = true;
        public int AnchoProceso { get; set; } = 800;
        public double UmbralSimilitud { get; set; } = 0.42;    // coseno SFace: ≥ umbral → es la misma persona
        public double MargenMinimo { get; set; } = 0.05;       // diferencia mínima con el 2.º más parecido
        public int Confirmaciones { get; set; } = 3;           // cuadros seguidos con la misma identidad
        public int RostroMinimoPx { get; set; } = 60;          // rostros más chicos se ignoran (muy lejos)
        public int MuestrasRegistro { get; set; } = 8;         // fotos del rostro al registrar un estudiante
        public double SegundosDesconocido { get; set; } = 4;
        public int VideoFps { get; set; } = 5;
        public int CalidadJpeg { get; set; } = 70;
    }

    /// <summary>Conexión a SQL Server. La base se crea sola si no existe.</summary>
    public class ConfigBaseDatos
    {
        public string Servidor { get; set; } = "localhost";    // nombre del equipo, o EQUIPO\SQLEXPRESS si es instancia con nombre
        public int Puerto { get; set; } = 0;                    // 0 = el de la instancia; 1433 u otro para forzar TCP
        public string Nombre { get; set; } = "asistencia";
        public string Usuario { get; set; } = "";               // vacío = autenticación de Windows; si no, inicio de sesión SQL
        public string Clave { get; set; } = "";

        /// <summary>¿Son los datos de la versión con MariaDB (XAMPP)? Puerto 3306 nunca es SQL Server.</summary>
        [JsonIgnore] public bool EsDeMariaDb => Puerto == 3306;
    }

    /// <summary>Lee y guarda config.json (está junto al .exe).</summary>
    public class Configuracion
    {
        public string Institucion { get; set; } = "Mi institución";
        // Carrera o curso, materia y docente de la versión anterior (una sola materia). Ahora las
        // materias y docentes están en la base de datos; estos datos solo crean la primera materia.
        public string Curso { get; set; } = "";
        public string Materia { get; set; } = "";
        public string Docente { get; set; } = "";
        public int Puerto { get; set; } = 5000;
        public string PinAdmin { get; set; } = "1234";         // PIN del administrador (cada docente tiene el suyo)
        public string IpParaQr { get; set; } = "";
        public string WifiNombre { get; set; } = "";
        public string WifiClave { get; set; } = "";
        public bool ListaPublica { get; set; } = true;          // la página pública muestra nombres de quienes llegaron
        public bool MarcarDesdeCelular { get; set; } = true;    // el estudiante puede marcar con una selfie en la web
        public bool CelularPideCodigo { get; set; } = true;     // la selfie se compara SOLO con el estudiante de ese código (1:1)
        public ConfigHorario Horario { get; set; } = new ConfigHorario();
        public ConfigReconocimiento Reconocimiento { get; set; } = new ConfigReconocimiento();
        public ConfigBaseDatos BaseDeDatos { get; set; } = new ConfigBaseDatos();

        [JsonIgnore] public string Carpeta { get; private set; } = AppContext.BaseDirectory;
        [JsonIgnore] public string CarpetaDatos => Path.Combine(Carpeta, "datos");

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static Configuracion Cargar(string carpeta = null)
        {
            carpeta ??= AppContext.BaseDirectory;
            string ruta = Path.Combine(carpeta, "config.json");
            var c = File.Exists(ruta)
                ? JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(ruta), Json) ?? new Configuracion()
                : new Configuracion();
            c.Carpeta = carpeta;
            c.Horario ??= new ConfigHorario();
            c.Reconocimiento ??= new ConfigReconocimiento();
            bool sinBase = c.BaseDeDatos == null;                  // config.json de la versión con SQLite
            c.BaseDeDatos ??= new ConfigBaseDatos();
            if (c.BaseDeDatos.EsDeMariaDb)                         // config.json de la versión con MariaDB: pasa a SQL Server local
            {
                string nombre = string.IsNullOrWhiteSpace(c.BaseDeDatos.Nombre) ? "asistencia" : c.BaseDeDatos.Nombre;
                c.BaseDeDatos = new ConfigBaseDatos { Nombre = nombre };
                sinBase = true;
            }
            if (!File.Exists(ruta) || sinBase) c.Guardar();
            Directory.CreateDirectory(c.CarpetaDatos);
            return c;
        }

        public void Guardar() => File.WriteAllText(Path.Combine(Carpeta, "config.json"), JsonSerializer.Serialize(this, Json));
    }
}
