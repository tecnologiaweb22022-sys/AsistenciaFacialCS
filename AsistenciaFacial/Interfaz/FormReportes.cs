using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using AsistenciaFacial.Nucleo;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>Reporte de una materia por rango de fechas: puntuales, atrasos, justificados, faltas y %.</summary>
    internal sealed class FormReportes : Form
    {
        readonly BaseDatos _db;
        readonly Usuario _usuario;
        readonly ComboBox _materia = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        readonly DateTimePicker _desde = new DateTimePicker(), _hasta = new DateTimePicker();
        readonly DataGridView _grid = new DataGridView();
        readonly Label _info = new Label();
        readonly FiltroCurso _filtro = new FiltroCurso();
        readonly System.Collections.Generic.List<Materia> _todas;
        /// <summary>Primera opción de «Materia»: todas las materias del curso elegido, sumadas por estudiante.</summary>
        sealed class TodoElCurso { public string Texto = ""; public override string ToString() => Texto; }

        public FormReportes(BaseDatos db, Usuario usuario, Materia inicial = null)
        {
            _db = db;
            _usuario = usuario;
            Text = "Reportes de asistencia";
            Size = new Size(1120, 700);
            StartPosition = FormStartPosition.CenterParent;
            var cabecera = Tema.Preparar(this, "Reportes de asistencia", "Puntuales, atrasos, justificados, faltas y porcentaje por estudiante");
            var gestion = db.GestionActiva();
            _desde.Format = _hasta.Format = DateTimePickerFormat.Short;
            _desde.Width = _hasta.Width = 130;
            _desde.Font = _hasta.Font = Tema.Campo;
            _desde.Margin = _hasta.Margin = new Padding(4, 5, 10, 3);
            Tema.EstiloCampo(_materia);
            _materia.Margin = new Padding(4, 5, 14, 3);
            // por defecto, toda la gestión activa hasta hoy
            _desde.Value = DateTime.TryParseExact(gestion?.Inicio ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ini) && ini < DateTime.Today
                ? ini : DateTime.Today.AddDays(-30);
            _hasta.Value = DateTime.Today;
            _todas = db.MateriasDe(usuario);
            _filtro.Cargar(_todas);
            Llenar(inicial?.Id);
            _materia.SelectedIndexChanged += (s, e) => Cargar();
            _materia.Width = 380;
            _filtro.Cambio += () => { Llenar(Elegida?.Id); Cargar(); };

            var arriba = Tema.Fila(DockStyle.Top, new Padding(16, 12, 16, 4));
            arriba.Controls.Add(Tema.Etiqueta("Carrera", Tema.Semi));
            arriba.Controls.Add(_filtro.Carrera);
            arriba.Controls.Add(Tema.Etiqueta("Semestre / año", Tema.Semi));
            arriba.Controls.Add(_filtro.Nivel);
            arriba.SetFlowBreak(_filtro.Nivel, true);
            arriba.Controls.Add(Tema.Etiqueta("Materia", Tema.Semi));
            arriba.Controls.Add(_materia);
            arriba.Controls.Add(Tema.Etiqueta("Desde", Tema.Semi));
            arriba.Controls.Add(_desde);
            arriba.Controls.Add(Tema.Etiqueta("Hasta", Tema.Semi));
            arriba.Controls.Add(_hasta);
            arriba.Controls.Add(Tema.Boton("Ver reporte", Tema.Naranja, (s, e) => Cargar()));
            var exportar = Tema.Fila(DockStyle.Top, new Padding(16, 0, 16, 10));
            exportar.Controls.Add(Tema.Etiqueta("Exportar:", Tema.Semi, Tema.Suave, new Padding(4, 11, 4, 0)));
            exportar.Controls.Add(Tema.Boton("⬇ reporte.csv", Tema.Suave, (s, e) => Exportar("reporte", false)));
            exportar.Controls.Add(Tema.Boton("⬇ asistencias.csv", Tema.Suave, (s, e) => Exportar("asistencias", false)));
            exportar.Controls.Add(Tema.Boton("⬇ Todas las materias del filtro (reporte.csv)", Tema.Suave, (s, e) => Exportar("reporte", true)));
            exportar.Controls.Add(Tema.Boton("⬇ alertas.csv", Tema.Suave, (s, e) => Exportar("eventos", false)));

            Tema.EstiloGrilla(_grid);
            foreach (var (n, t, w) in new[] { ("codigo", "Código", 90), ("nombre", "Estudiante", 0), ("dias", "Días", 60), ("p", "Puntual", 75),
                         ("a", "Atraso", 70), ("j", "Justif.", 70), ("f", "Faltas", 70), ("ss", "Sin salida", 90), ("pct", "% asistencia", 210) })
            {
                _grid.Columns.Add(n, t);
                if (w > 0) _grid.Columns[n].Width = w; else _grid.Columns[n].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }
            Tema.BarrasPorcentaje(_grid, "pct");
            _grid.Columns["codigo"].DefaultCellStyle.ForeColor = Tema.Suave;
            _grid.Columns["nombre"].DefaultCellStyle.Font = Tema.Semi;
            _info.Dock = DockStyle.Bottom;
            _info.Height = 54;
            _info.ForeColor = Tema.Suave;
            _info.BackColor = Color.Transparent;
            _info.Padding = new Padding(4, 10, 4, 0);
            var tarjeta = new Tarjeta { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 4) };
            tarjeta.Controls.Add(_grid);
            tarjeta.Controls.Add(_info);
            var marco = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 16), BackColor = Tema.Fondo };
            marco.Controls.Add(tarjeta);
            Controls.Add(marco);
            Controls.Add(exportar);
            Controls.Add(arriba);
            cabecera.SendToBack();
            Load += (s, e) => Cargar();
        }

        string F(DateTimePicker d) => d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Materia Elegida => _materia.SelectedItem as Materia;
        System.Collections.Generic.List<Materia> DelFiltro => _todas.Where(_filtro.Pasa).ToList();

        /// <summary>«Materia»: «Todo el curso» (si hay varias) y las materias de la carrera y el semestre/año elegidos.</summary>
        void Llenar(long? elegir)
        {
            var lista = DelFiltro;
            _materia.Items.Clear();
            if (lista.Count > 1) _materia.Items.Add(new TodoElCurso { Texto = $"★ Todas las materias · {_filtro.Descripcion} ({lista.Count})" });
            foreach (var m in lista) _materia.Items.Add(m);
            int i = _materia.Items.Cast<object>().ToList().FindIndex(x => x is Materia m && m.Id == elegir);
            if (_materia.Items.Count > 0) _materia.SelectedIndex = i >= 0 ? i : 0;
        }

        void Cargar()
        {
            _grid.Rows.Clear();
            if (_materia.SelectedItem is TodoElCurso)
            {
                CargarCurso();
                return;
            }
            if (Elegida == null)
            {
                _info.Text = _todas.Count > 0 ? $"No hay materias de {_filtro.Descripcion}." :
                             _usuario.EsAdmin ? "No hay materias en la gestión activa." : "No tienes materias asignadas en la gestión activa.";
                return;
            }
            var filas = _db.Reporte(Elegida.Id, F(_desde), F(_hasta));
            int dias = _db.DiasDeClase(Elegida.Id, F(_desde), F(_hasta)).Count;
            foreach (var r in filas)
            {
                int i = _grid.Rows.Add(r.Codigo, r.Nombre, r.Dias, r.Puntuales, r.Atrasos, r.Justificados, r.Faltas, r.SinSalida, r.Dias == 0 ? "—" : r.Porcentaje.ToString("0.0") + " %");
            }
            double prom = filas.Count == 0 ? 0 : filas.Average(r => r.Porcentaje);
            _info.Text = $"{Elegida.Titulo} · {Elegida.Curso} · {Elegida.Docente}. " +
                         $"{dias} día(s) de clase en el rango (días en que al menos un estudiante marcó). Promedio: {prom:0.0} %.\n" +
                         "Verde ≥ 80 %   ·   Ámbar 60–79 %   ·   Rojo < 60 %   ·   «Sin salida»: días pasados con entrada pero sin salida.";
        }

        /// <summary>
        /// Todo el curso (carrera + semestre/año): por estudiante, la suma de sus clases en todas las materias del filtro.
        /// % = (puntuales + atrasos + justificados) / clases dictadas en esas materias.
        /// </summary>
        void CargarCurso()
        {
            var materias = DelFiltro;
            var porEstudiante = materias.SelectMany(m => _db.Reporte(m.Id, F(_desde), F(_hasta)))
                .GroupBy(r => r.EstudianteId)
                .Select(g => new FilaReporte
                {
                    EstudianteId = g.Key, Codigo = g.First().Codigo, Nombre = g.First().Nombre,
                    Dias = g.Sum(r => r.Dias), Puntuales = g.Sum(r => r.Puntuales), Atrasos = g.Sum(r => r.Atrasos),
                    Justificados = g.Sum(r => r.Justificados), Faltas = g.Sum(r => r.Faltas), SinSalida = g.Sum(r => r.SinSalida),
                })
                .OrderBy(r => r.Nombre).ToList();
            foreach (var r in porEstudiante)
            {
                r.Porcentaje = r.Dias == 0 ? 0 : Math.Round(100.0 * (r.Puntuales + r.Atrasos + r.Justificados) / r.Dias, 1);
                _grid.Rows.Add(r.Codigo, r.Nombre, r.Dias, r.Puntuales, r.Atrasos, r.Justificados, r.Faltas, r.SinSalida, r.Dias == 0 ? "—" : r.Porcentaje.ToString("0.0") + " %");
            }
            double prom = porEstudiante.Count == 0 ? 0 : porEstudiante.Average(r => r.Porcentaje);
            _info.Text = $"{_filtro.Descripcion}: {materias.Count} materia(s) — {string.Join(", ", materias.Select(m => m.Sigla.Length > 0 ? m.Sigla : m.Nombre))}. " +
                         $"«Días» = clases dictadas sumando todas las materias. Promedio: {prom:0.0} %.\n" +
                         "Verde ≥ 80 %   ·   Ámbar 60–79 %   ·   Rojo < 60 %   ·   «Sin salida»: días pasados con entrada pero sin salida.";
        }

        void Exportar(string tipo, bool todas)
        {
            if (_materia.SelectedItem is TodoElCurso) todas = true;
            if (Elegida == null && !todas && tipo != "eventos") return;
            var materias = todas ? DelFiltro.Select(m => m.Id).ToList() : Elegida != null ? new[] { Elegida.Id }.ToList() : new System.Collections.Generic.List<long>();
            string nombre = todas ? Claves.NormalizarUsuario(_filtro.Descripcion).Replace('.', '_') : (Elegida?.Sigla.Length > 0 ? Elegida.Sigla : "materia");
            using var d = new SaveFileDialog { FileName = $"{tipo}_{nombre}_{F(_desde)}_{F(_hasta)}.csv", Filter = "CSV (*.csv)|*.csv" };
            if (d.ShowDialog(Tema.Dueno(this)) != DialogResult.OK) return;
            File.WriteAllText(d.FileName, _db.ExportarCsv(tipo, materias, F(_desde), F(_hasta)), new UTF8Encoding(false));
            MessageBox.Show("Archivo guardado:\n" + d.FileName, Text);
        }
    }
}
