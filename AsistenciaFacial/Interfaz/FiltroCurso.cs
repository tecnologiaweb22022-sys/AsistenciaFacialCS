using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using AsistenciaFacial.Nucleo;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>
    /// Filtro «Carrera» + «Semestre / año» para elegir materias por curso. Las opciones salen de las
    /// materias que puede ver el usuario: solo aparecen carreras y niveles que tienen materias.
    /// </summary>
    internal sealed class FiltroCurso
    {
        public ComboBox Carrera { get; } = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        public ComboBox Nivel { get; } = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        /// <summary>El usuario cambió la carrera o el nivel.</summary>
        public event Action Cambio;

        sealed class OpcionCarrera
        {
            public long Id;
            public string Nombre = "", Regimen = Carreras.Semestral;
            public override string ToString() => Nombre;
        }

        sealed class OpcionNivel
        {
            public int Nivel;
            public string Texto = "";
            public override string ToString() => Texto;
        }

        List<Materia> _materias = new List<Materia>();
        bool _cargando;

        public FiltroCurso()
        {
            Tema.EstiloCampo(Carrera);
            Tema.EstiloCampo(Nivel);
            Carrera.Margin = new Padding(4, 5, 14, 3);
            Nivel.Margin = new Padding(4, 5, 14, 3);
            Carrera.SelectedIndexChanged += (s, e) => { if (_cargando) return; LlenarNiveles(); Cambio?.Invoke(); };
            Nivel.SelectedIndexChanged += (s, e) => { if (!_cargando) Cambio?.Invoke(); };
        }

        public long CarreraId => (Carrera.SelectedItem as OpcionCarrera)?.Id ?? 0;
        public int NivelElegido => (Nivel.SelectedItem as OpcionNivel)?.Nivel ?? 0;

        /// <summary>¿La materia es de la carrera y el nivel elegidos? («Todas» / «Todos» dejan pasar todo).</summary>
        public bool Pasa(Materia m) => (CarreraId == 0 || m.CarreraId == CarreraId) && (NivelElegido == 0 || m.Semestre == NivelElegido);

        /// <summary>«Informática Industrial · 6to semestre», «Mecánica Automotriz», o «Todas las carreras».</summary>
        public string Descripcion => Carrera.SelectedItem is OpcionCarrera c
            ? c.Nombre + (NivelElegido > 0 ? " · " + Academico.Nivel(NivelElegido, c.Regimen) : "")
            : "Todas las carreras";

        /// <summary>Vuelve a armar las opciones con estas materias, conservando lo elegido si sigue existiendo.</summary>
        public void Cargar(IEnumerable<Materia> materias)
        {
            _materias = materias.ToList();
            long antes = CarreraId;
            int nivelAntes = NivelElegido;
            _cargando = true;
            Carrera.Items.Clear();
            Carrera.Items.Add(new OpcionCarrera { Nombre = "Todas las carreras" });
            foreach (var g in _materias.Where(m => m.CarreraId > 0).GroupBy(m => m.CarreraId).OrderBy(g => g.First().Carrera))
                Carrera.Items.Add(new OpcionCarrera { Id = g.Key, Nombre = g.First().Carrera, Regimen = g.First().Regimen });
            Carrera.SelectedIndex = Math.Max(0, Carrera.Items.Cast<OpcionCarrera>().ToList().FindIndex(c => c.Id == antes));
            LlenarNiveles(nivelAntes);
            _cargando = false;
        }

        void LlenarNiveles(int elegir = -1)
        {
            bool antes = _cargando;
            _cargando = true;
            if (elegir < 0) elegir = NivelElegido;
            Nivel.Items.Clear();
            Nivel.Items.Add(new OpcionNivel { Texto = "Todos" });
            var c = Carrera.SelectedItem as OpcionCarrera;
            foreach (int n in _materias.Where(m => c == null || c.Id == 0 || m.CarreraId == c.Id).Select(m => m.Semestre).Distinct().OrderBy(n => n))
                Nivel.Items.Add(new OpcionNivel
                {
                    Nivel = n,
                    Texto = c != null && c.Id > 0 ? Academico.Nivel(n, c.Regimen) : $"{Academico.Semestre(n)} (semestre o año)",
                });
            Nivel.SelectedIndex = Math.Max(0, Nivel.Items.Cast<OpcionNivel>().ToList().FindIndex(x => x.Nivel == elegir));
            _cargando = antes;
        }
    }
}
