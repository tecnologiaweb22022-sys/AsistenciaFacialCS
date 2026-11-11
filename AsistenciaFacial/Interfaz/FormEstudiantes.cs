using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using AsistenciaFacial.Nucleo;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>Lista de estudiantes: crear, editar, registrar su rostro, importar y eliminar.</summary>
    internal sealed class FormEstudiantes : Form
    {
        readonly Configuracion _cfg;
        readonly BaseDatos _db;
        readonly MotorAsistencia _motor;
        readonly DataGridView _grid = new DataGridView();
        readonly TextBox _buscar = new TextBox();
        readonly ComboBox _semestre = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Margin = new Padding(3, 6, 3, 3) };
        readonly Label _info = new Label();
        readonly string _gestion;

        public FormEstudiantes(Configuracion cfg, BaseDatos db, MotorAsistencia motor)
        {
            _cfg = cfg;
            _db = db;
            _motor = motor;
            _gestion = db.GestionActiva()?.Nombre ?? "";
            Text = "Estudiantes y registro de rostros";
            Size = new Size(1100, 680);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Tema.Fondo;
            Font = Tema.Normal;

            var arriba = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 8, 8, 4) };
            arriba.Controls.Add(Tema.Boton("+ Nuevo", Tema.Verde, (s, e) =>
                Editar(new Estudiante { Curso = _cfg.Curso, Semestre = _semestre.SelectedIndex is int i && i >= 1 && i <= 6 ? i : 0 })));
            arriba.Controls.Add(Tema.Boton("Editar", Tema.Azul, (s, e) => { var x = Seleccionado(); if (x != null) Editar(x); }));
            arriba.Controls.Add(Tema.Boton("Registrar rostro", Tema.Naranja, (s, e) => RegistrarRostro()));
            arriba.Controls.Add(Tema.Boton("Borrar rostro", Tema.Suave, (s, e) => BorrarRostro()));
            arriba.Controls.Add(Tema.Boton("Eliminar estudiante", Tema.Rojo, (s, e) => Eliminar()));
            arriba.Controls.Add(Tema.Boton("Importar lista CSV…", Tema.Suave, (s, e) => Importar()));
            arriba.Controls.Add(Tema.Boton("Exportar CSV…", Tema.Suave, (s, e) => Exportar()));
            _buscar.Width = 170;
            _buscar.Margin = new Padding(10, 6, 3, 3);
            _buscar.TextChanged += (s, e) => Cargar();
            arriba.Controls.Add(new Label { Text = "Buscar:", AutoSize = true, Margin = new Padding(10, 9, 0, 0) });
            arriba.Controls.Add(_buscar);
            // índice 0 = todos, 1..6 = semestre, 7 = sin inscribir en la gestión activa
            _semestre.Items.Add("Todos");
            for (int i = 1; i <= 6; i++) _semestre.Items.Add(Academico.Semestre(i) + " semestre");
            _semestre.Items.Add("Sin inscribir");
            _semestre.SelectedIndex = 0;
            _semestre.SelectedIndexChanged += (s, e) => Cargar();
            arriba.Controls.Add(new Label { Text = "Semestre:", AutoSize = true, Margin = new Padding(10, 9, 0, 0) });
            arriba.Controls.Add(_semestre);

            Tema.EstiloGrilla(_grid);
            _grid.RowTemplate.Height = 52;
            _grid.Columns.Add(new DataGridViewImageColumn { Name = "foto", HeaderText = "", Width = 56, ImageLayout = DataGridViewImageCellLayout.Zoom });
            _grid.Columns["foto"].DefaultCellStyle.NullValue = null;       // sin foto: celda vacía (no la "X" roja)
            _grid.Columns.Add("codigo", "Código");
            _grid.Columns.Add("apellidos", "Apellidos");
            _grid.Columns.Add("nombres", "Nombres");
            _grid.Columns.Add("semestre", $"Semestre {_gestion}");
            _grid.Columns.Add("curso", "Carrera / curso");
            _grid.Columns.Add("rostro", "Rostro");
            _grid.Columns.Add("activo", "Activo");
            _grid.Columns["codigo"].Width = 90;
            _grid.Columns["apellidos"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns["nombres"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns["semestre"].Width = 125;
            _grid.Columns["curso"].Width = 140;
            _grid.Columns["rostro"].Width = 130;
            _grid.Columns["activo"].Width = 60;
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) RegistrarRostro(); };

            _info.Dock = DockStyle.Bottom;
            _info.Height = 44;
            _info.ForeColor = Tema.Suave;
            _info.Padding = new Padding(10, 4, 10, 0);
            _info.Text = $"Doble clic = registrar rostro. CSV para importar: codigo;nombres;apellidos;curso;semestre (semestre 1 a 6: lo inscribe en {_gestion}).\n" +
                         "Al eliminar a un estudiante se borran también su rostro y sus asistencias (datos personales).";
            Controls.Add(_grid);
            Controls.Add(_info);
            Controls.Add(arriba);
            Load += (s, e) => Cargar();
        }

        Estudiante Seleccionado()
        {
            if (_grid.SelectedRows.Count == 0) { MessageBox.Show("Selecciona un estudiante.", Text); return null; }
            return (Estudiante)_grid.SelectedRows[0].Tag;
        }

        void Cargar()
        {
            long? sel = _grid.SelectedRows.Count > 0 ? ((Estudiante)_grid.SelectedRows[0].Tag).Id : (long?)null;
            string q = _buscar.Text.Trim().ToLowerInvariant();
            foreach (DataGridViewRow r in _grid.Rows) (r.Cells["foto"].Value as Image)?.Dispose();
            _grid.Rows.Clear();
            int sem = _semestre.SelectedIndex;
            foreach (var e in _db.Estudiantes().Where(x => (q.Length == 0 || (x.Codigo + " " + x.NombreCompleto + " " + x.Apellidos).ToLowerInvariant().Contains(q))
                                                           && (sem == 0 || (sem == 7 ? x.Semestre == 0 : x.Semestre == sem))))
            {
                int i = _grid.Rows.Add(e.Muestras > 0 ? Tema.ImagenDeBytes(_db.Foto(e.Id)) : null, e.Codigo, e.Apellidos, e.Nombres,
                    e.Semestre > 0 ? Academico.Semestre(e.Semestre) : "sin inscribir", e.Curso,
                    e.Muestras > 0 ? $"Sí ({e.Muestras} muestras)" : "falta registrar", e.Activo ? "sí" : "no");
                var fila = _grid.Rows[i];
                fila.Tag = e;
                fila.Cells["rostro"].Style.ForeColor = e.Muestras > 0 ? Tema.Verde : Tema.Rojo;
                if (e.Semestre == 0) fila.Cells["semestre"].Style.ForeColor = Tema.Rojo;
                if (!e.Activo) fila.DefaultCellStyle.ForeColor = Tema.Suave;
                if (sel == e.Id) fila.Selected = true;
            }
            int total = _db.Estudiantes().Count, conRostro = _db.Estudiantes().Count(x => x.Muestras > 0);
            Text = $"Estudiantes y registro de rostros — {total} estudiantes, {conRostro} con rostro registrado";
        }

        void Editar(Estudiante e)
        {
            using var f = new FormEditarEstudiante(e, _gestion);
            while (f.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    long id = _db.GuardarEstudiante(f.Resultado);
                    Cargar();
                    if (e.Id == 0 && MessageBox.Show("Estudiante guardado. ¿Registrar su rostro ahora?", Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
                        RegistrarRostro(_db.EstudiantePorId(id));
                    return;
                }
                catch (ErrorValidacion ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void RegistrarRostro(Estudiante e = null)
        {
            e ??= Seleccionado();
            if (e == null) return;
            if (!_motor.Activo)
            {
                if (MessageBox.Show("La cámara está apagada. ¿Encenderla?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                _motor.Iniciar(_cfg.Reconocimiento.Fuente);
            }
            if (e.Muestras > 0 && MessageBox.Show($"{e.NombreCompleto} ya tiene su rostro registrado. ¿Reemplazarlo?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            using var f = new FormRegistroRostro(_motor, e, _cfg.Reconocimiento.MuestrasRegistro);
            f.ShowDialog(this);
            Cargar();
        }

        void BorrarRostro()
        {
            var e = Seleccionado();
            if (e == null || e.Muestras == 0) return;
            if (MessageBox.Show($"¿Borrar los datos del rostro de {e.NombreCompleto}? (sus asistencias se conservan)", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            _db.BorrarRostros(e.Id);
            Cargar();
        }

        void Eliminar()
        {
            var e = Seleccionado();
            if (e == null) return;
            if (MessageBox.Show($"¿Eliminar a {e.NombreCompleto} ({e.Codigo})?\n\nSe borran también su rostro y TODAS sus asistencias. No se puede deshacer.\n" +
                                "Si solo dejó la materia, mejor edítalo y desmarca «Activo».", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { _db.EliminarEstudiante(e.Id); } catch (ErrorValidacion ex) { MessageBox.Show(ex.Message); }
            Cargar();
        }

        void Importar()
        {
            using var d = new OpenFileDialog { Filter = "Lista CSV o texto (*.csv;*.txt)|*.csv;*.txt|Todos|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var (nuevos, act, errores) = _db.ImportarCsv(File.ReadAllText(d.FileName, Encoding.UTF8));
            Cargar();
            MessageBox.Show($"Nuevos: {nuevos}\nActualizados: {act}" + (errores.Count > 0 ? "\n\nNo se importaron:\n" + string.Join("\n", errores.Take(15)) : ""),
                "Importar lista");
        }

        void Exportar()
        {
            using var d = new SaveFileDialog { FileName = "estudiantes.csv", Filter = "CSV (*.csv)|*.csv" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(d.FileName, _db.ExportarCsv("estudiantes", Array.Empty<long>(), BaseDatos.Hoy(), BaseDatos.Hoy()), new UTF8Encoding(false));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (DataGridViewRow r in _grid.Rows) (r.Cells["foto"].Value as Image)?.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Datos de un estudiante.</summary>
    internal sealed class FormEditarEstudiante : Form
    {
        readonly TextBox _codigo = new TextBox(), _nombres = new TextBox(), _apellidos = new TextBox(), _curso = new TextBox();
        readonly ComboBox _semestre = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckBox _activo = new CheckBox { Text = "Activo (aparece en la lista y la cámara lo reconoce)", AutoSize = true };
        readonly Estudiante _original;

        public Estudiante Resultado => new Estudiante
        {
            Id = _original.Id, Codigo = _codigo.Text, Nombres = _nombres.Text, Apellidos = _apellidos.Text, Curso = _curso.Text, Activo = _activo.Checked,
            Semestre = _semestre.SelectedIndex,
        };

        public FormEditarEstudiante(Estudiante e, string gestion)
        {
            _original = e;
            Text = e.Id == 0 ? "Nuevo estudiante" : "Editar estudiante";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 330);
            Font = Tema.Normal;
            BackColor = Tema.Papel;
            _codigo.Text = e.Codigo;
            _nombres.Text = e.Nombres;
            _apellidos.Text = e.Apellidos;
            _curso.Text = e.Curso;
            _activo.Checked = e.Activo;
            for (int i = 0; i <= 6; i++) _semestre.Items.Add(Academico.Semestre(i));      // índice = semestre (0 = sin inscribir)
            _semestre.SelectedIndex = Math.Max(0, Math.Min(6, e.Semestre));
            int y = 16;
            foreach (var (txt, caja) in new (string, Control)[] { ("Código o CI *", _codigo), ("Nombres *", _nombres), ("Apellidos", _apellidos),
                         ("Carrera / curso", _curso), ($"Semestre {gestion}", _semestre) })
            {
                Controls.Add(new Label { Text = txt, Left = 16, Top = y + 3, Width = 112 });
                caja.SetBounds(130, y, 270, 24);
                Controls.Add(caja);
                y += 40;
            }
            _activo.Location = new Point(130, y);
            Controls.Add(_activo);
            var ok = new Button { Text = "Guardar", DialogResult = DialogResult.OK, Left = 220, Top = y + 44, Width = 85 };
            var no = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Left = 315, Top = y + 44, Width = 85 };
            Controls.Add(ok);
            Controls.Add(no);
            AcceptButton = ok;
            CancelButton = no;
        }
    }
}
