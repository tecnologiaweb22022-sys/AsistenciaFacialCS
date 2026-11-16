using System;
using System.Collections.Generic;

namespace AsistenciaFacial.Nucleo
{
    // En la web las propiedades viajan en snake_case: NombreCompleto → "nombre_completo".

    public class Estudiante
    {
        public long Id { get; set; }
        public string Codigo { get; set; } = "";        // CI o código de estudiante (único)
        public string Nombres { get; set; } = "";
        public string Apellidos { get; set; } = "";
        public string Curso { get; set; } = "";
        public bool Activo { get; set; } = true;
        public int Muestras { get; set; }               // cuántas fotos de su rostro hay registradas
        public int Semestre { get; set; }               // nivel en la gestión activa: semestre (1 a 6) o año (1 a 3); 0 = no inscrito
        public long CarreraId { get; set; }             // carrera de esa inscripción (0 = no inscrito)
        public string Carrera { get; set; } = "";
        public string Regimen { get; set; } = Carreras.Semestral;
        public double Creado { get; set; }
        public string NombreCompleto => $"{Nombres} {Apellidos}".Trim();
        /// <summary>«Informática Industrial · 6to semestre», «Mecánica Automotriz · 2do año» o «sin inscribir».</summary>
        public string Inscripcion => Semestre > 0 ? $"{Carrera} · {Academico.Nivel(Semestre, Regimen)}" : "sin inscribir";
    }

    /// <summary>
    /// Carrera: semestral (Informática Industrial, 6 semestres) o anual (Mecánica Automotriz, 3 años).
    /// El «nivel» de una materia o inscripción es el semestre o el año, según el régimen.
    /// </summary>
    public class Carrera
    {
        public long Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Sigla { get; set; } = "";
        public string Regimen { get; set; } = Carreras.Semestral;
        public int Duracion { get; set; } = 6;          // cantidad de semestres o de años
        public bool Activa { get; set; } = true;
        public int Materias { get; set; }               // en la gestión activa
        public int Inscritos { get; set; }              // en la gestión activa
        public bool EsAnual => Regimen == Carreras.Anual;
        public string TextoNivel => EsAnual ? "Año" : "Semestre";
        public string TextoDuracion => $"{Duracion} {(EsAnual ? (Duracion == 1 ? "año" : "años") : (Duracion == 1 ? "semestre" : "semestres"))}";
        public override string ToString() => Nombre;
    }

    public static class Carreras
    {
        public const string Semestral = "semestral", Anual = "anual";
    }

    /// <summary>Periodo académico: I-2026 (1ro, 3ro, 5to), II-2026 (2do, 4to, 6to)… Solo una está activa.</summary>
    public class Gestion
    {
        public long Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Inicio { get; set; } = "";        // yyyy-MM-dd
        public string Fin { get; set; } = "";
        public bool Activa { get; set; }
        public int Materias { get; set; }
        public int Inscritos { get; set; }
        public override string ToString() => Nombre + (Activa ? " (activa)" : "");
    }

    public class Docente
    {
        public long Id { get; set; }
        public string Nombres { get; set; } = "";
        public string Apellidos { get; set; } = "";
        public bool Activo { get; set; } = true;
        public string Usuario { get; set; } = "";           // para entrar: usuario + contraseña
        public bool TienePin { get; set; }                  // tiene contraseña asignada
        public int Materias { get; set; }               // materias a su cargo en la gestión activa
        public string NombreCompleto => $"{Nombres} {Apellidos}".Trim();
        public override string ToString() => NombreCompleto;
    }

    /// <summary>Un bloque del horario semanal: día 1 = lunes … 7 = domingo, horas "HH:mm".</summary>
    public class BloqueHorario
    {
        public int Dia { get; set; }
        public string Inicio { get; set; } = "";
        public string Fin { get; set; } = "";
        public TimeSpan HoraInicio => Academico.Hora(Inicio);
        public TimeSpan HoraFin => Academico.Hora(Fin);
        public override string ToString() => $"{Academico.DiaCorto(Dia)} {Inicio}–{Fin}";
    }

    public class Materia
    {
        public long Id { get; set; }
        public long GestionId { get; set; }
        public string Gestion { get; set; } = "";
        public int Semestre { get; set; }               // nivel: semestre o año, según la carrera
        public long CarreraId { get; set; }
        public string Carrera { get; set; } = "";
        public string CarreraSigla { get; set; } = "";
        public string Regimen { get; set; } = Carreras.Semestral;
        public string Sigla { get; set; } = "";
        public string Nombre { get; set; } = "";
        public long? DocenteId { get; set; }
        public string Docente { get; set; } = "";
        public int ToleranciaMin { get; set; } = 10;    // hasta inicio + tolerancia → puntual; después → atraso
        public List<BloqueHorario> Horarios { get; set; } = new List<BloqueHorario>();
        public string Titulo => $"{Sigla} {Nombre}".Trim();
        public string TextoHorario => string.Join(" · ", Horarios);
        /// <summary>«6to semestre», «2do año».</summary>
        public string NivelTexto => Academico.Nivel(Semestre, Regimen);
        /// <summary>«Informática Industrial · 6to semestre».</summary>
        public string Curso => $"{Carrera} · {NivelTexto}";
        public override string ToString() => $"{(CarreraSigla.Length > 0 ? CarreraSigla : Carrera)} {Academico.Nivel(Semestre, Regimen, corto: true)} · {Titulo}";
    }

    /// <summary>
    /// Una materia que tiene clase hoy y está en curso. Si ese día tiene varios bloques (por ejemplo
    /// 08:00–11:00, descanso, 11:30–13:00) cuenta como UNA clase: la entrada se mide contra el primer
    /// bloque y la salida contra el último (el descanso no es una salida).
    /// </summary>
    public class ClaseEnCurso
    {
        public Materia Materia { get; set; }
        public string Inicio { get; set; } = "";
        public string Fin { get; set; } = "";
        public TimeSpan HoraInicio => Academico.Hora(Inicio);
        public TimeSpan HoraFin => Academico.Hora(Fin);
        public TimeSpan Limite => HoraInicio + TimeSpan.FromMinutes(Materia.ToleranciaMin);
    }

    /// <summary>Quién usa el programa o el panel web: el administrador o un docente.</summary>
    public class Usuario
    {
        public bool EsAdmin { get; set; }
        public long DocenteId { get; set; }
        public string Nombre { get; set; } = "";
        public bool PuedeVer(Materia m) => EsAdmin || (m != null && m.DocenteId == DocenteId);
    }

    public static class Academico
    {
        static readonly string[] Semestres = { "Sin inscribir", "1ro", "2do", "3ro", "4to", "5to", "6to" };
        static readonly string[] Dias = { "", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo" };

        public static string Semestre(int s) => s >= 0 && s < Semestres.Length ? Semestres[s] : s.ToString();

        /// <summary>«6to semestre» / «6to sem.» en carreras semestrales; «1er año», «2do año», «3er año» en las anuales.</summary>
        public static string Nivel(int n, string regimen, bool corto = false)
        {
            if (n <= 0) return "sin inscribir";
            if (regimen == Carreras.Anual)
                return (n == 1 ? "1er" : n == 3 ? "3er" : Semestre(n)) + " año";
            return Semestre(n) + (corto ? " sem." : " semestre");
        }
        public static string Dia(int d) => d >= 1 && d <= 7 ? Dias[d] : "?";
        public static string DiaCorto(int d) => Dia(d).Substring(0, Math.Min(3, Dia(d).Length));
        /// <summary>1 = lunes … 7 = domingo.</summary>
        public static int DiaDe(DateTime f) => ((int)f.DayOfWeek + 6) % 7 + 1;

        public static TimeSpan Hora(string hhmm) =>
            TimeSpan.TryParseExact(hhmm ?? "", @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : TimeSpan.Zero;

        public static bool HoraValida(string hhmm) =>
            TimeSpan.TryParseExact(hhmm ?? "", @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out _);

        /// <summary>"6", "6to", "sexto" → 6. Vacío → 0. Algo raro → -1.</summary>
        public static int LeerSemestre(string texto)
        {
            string t = (texto ?? "").Trim().ToLowerInvariant();
            if (t.Length == 0) return 0;
            string[] palabras = { "", "prim", "seg", "terc", "cuar", "quin", "sext" };
            for (int i = 1; i <= 6; i++)
                if (t.StartsWith(i.ToString()) || t.StartsWith(palabras[i])) return i;
            return -1;
        }
    }

    /// <summary>Fila de la lista del día: un estudiante y su marca (o "falta" si no llegó).</summary>
    public class FilaAsistencia
    {
        public long EstudianteId { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public string Curso { get; set; }
        public int Muestras { get; set; }
        public long MateriaId { get; set; }
        public string Materia { get; set; } = "";
        public string Fecha { get; set; }
        public double? Hora { get; set; }               // entrada, segundos Unix; null si no marcó
        public double? HoraSalida { get; set; }         // salida; null si todavía no marcó la salida
        public string Estado { get; set; } = "falta";   // puntual, atraso, justificado, falta
        public double Similitud { get; set; }
        public string Fuente { get; set; } = "";        // camara, manual, web
        public string Observacion { get; set; } = "";
    }

    public class ResumenDia
    {
        public string Fecha { get; set; }
        public int Total { get; set; }
        public int Presentes { get; set; }
        public int Puntuales { get; set; }
        public int Atrasos { get; set; }
        public int Justificados { get; set; }
        public int Faltan { get; set; }
    }

    public class FilaReporte
    {
        public long EstudianteId { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public long MateriaId { get; set; }
        public string Materia { get; set; } = "";
        public string Docente { get; set; } = "";
        public int Dias { get; set; }
        public int Puntuales { get; set; }
        public int Atrasos { get; set; }
        public int Justificados { get; set; }
        public int Faltas { get; set; }
        public int SinSalida { get; set; }              // días pasados en que marcó entrada pero no salida
        public double Porcentaje { get; set; }          // (puntuales + atrasos + justificados) / días
    }

    public class Evento
    {
        public long Id { get; set; }
        public long? EstudianteId { get; set; }
        public string Tipo { get; set; }
        public string Mensaje { get; set; }
        public string Prioridad { get; set; }           // normal o alta
        public string Fuente { get; set; }
        public bool Atendido { get; set; }
        public double Creado { get; set; }
    }

    public class EstadoPanel
    {
        public double Ahora { get; set; }
        public ResumenDia Resumen { get; set; }
        public List<FilaAsistencia> Lista { get; set; }
        public List<Evento> Eventos { get; set; }
        public long UltimoEvento { get; set; }
        public object Camara { get; set; }
        public object Horario { get; set; }
    }

    /// <summary>Resultado de una marca: la fila guardada y si fue nueva o ya existía.</summary>
    public class ResultadoMarca
    {
        public FilaAsistencia Fila { get; set; }
        public bool Nueva { get; set; }
        public string Tipo { get; set; } = "entrada";   // qué se marcó: "entrada" o "salida"
        public string Motivo { get; set; } = "";        // por qué no se marcó (fuera de horario…)
    }

    /// <summary>Rostro encontrado por YuNet: caja, 5 puntos (ojos, nariz, comisuras) y confianza.</summary>
    public class Rostro
    {
        public float X, Y, W, H, Confianza;
        public float[] Puntos = new float[10];          // x0,y0 … x4,y4
        public float Area => W * H;
    }

    /// <summary>A quién se parece un rostro.</summary>
    public class Coincidencia
    {
        public long EstudianteId = -1;
        public string Nombre = "Desconocido";
        public double Similitud;                        // mejor coseno
        public double Segunda;                          // coseno del segundo estudiante más parecido
        public bool Reconocido;
    }

    public class ErrorValidacion : Exception
    {
        public ErrorValidacion(string mensaje) : base(mensaje) { }
    }
}
