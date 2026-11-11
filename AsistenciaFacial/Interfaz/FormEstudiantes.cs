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
        readonly Cabecera _cabecera;

        public FormEstudiantes(Configuracion cfg, BaseDatos db, MotorAsistencia motor)
        {
            _cfg = cfg;
            _db = db;
            _motor = motor;
            _gestion = db.GestionActiva()?.Nombre ?? "";
            Text = "Estudiantes y registro de rostros";
            Size = new Size(1180, 720);
            StartPosition = FormStartPosition.CenterParent;
            _cabecera = Tema.Preparar(this, "Estudiantes y registro de rostros", $"Gestión {_gestion}");

            var arriba = Tema.Fila(DockStyle.Top, new Padding(16, 12, 16, 4));
            arriba.Controls.Add(Tema.Boton("+ Nuevo estudiante", Tema.Naranja, (s, e) =>
                Editar(new Estudiante { Curso = _cfg.Curso, Semestre = _semestre.SelectedIndex is int i && i >= 1 && i <= 6 ? i : 0 })));
            arriba.Controls.Add(Tema.Boton("📷  Registrar rostro", Tema.MarinoMedio, (s, e) => RegistrarRostro()));
            arriba.Controls.Add(Tema.Boton("Editar", Tema.Suave, (s, e) => { var x = Seleccionado(); if (x != null) Editar(x); }));
            arriba.Controls.Add(Tema.Boton("Borrar rostro", Tema.Suave, (s, e) => BorrarRostro()));
            arriba.Controls.Add(Tema.Boton("Eliminar", Tema.Rojo, (s, e) => Eliminar()));
            arriba.Controls.Add(Tema.Boton("Importar CSV…", Tema.Suave, (s, e) => Importar()));
            arriba.Controls.Add(Tema.Boton("Exportar CSV…", Tema.Suave, (s, e) => Exportar()));
            var filtros = Tema.Fila(DockStyle.Top, new Padding(16, 0, 16, 10));
            _buscar.Width = 240;
            _buscar.Margin = new Padding(4, 5, 14, 3);
            Tema.EstiloCampo(_buscar);
            _buscar.PlaceholderText = "Nombre, apellido o código…";
            _buscar.TextChanged += (s, e) => Cargar();
            filtros.Controls.Add(Tema.Etiqueta("Buscar", Tema.Semi));
            filtros.Controls.Add(_buscar);
            // índice 0 = todos, 1..6 = semestre, 7 = sin inscribir en la gestión activa
            _semestre.Items.Add("Todos");
            for (int i = 1; i <= 6; i++) _semestre.Items.Add(Academico.Semestre(i) + " semestre");
            _semestre.Items.Add("Sin inscribir");
            _semestre.SelectedIndex = 0;
            _semestre.SelectedIndexChanged += (s, e) => Cargar();
            Tema.EstiloCampo(_semestre);
            _semestre.Width = 160;
            _semestre.Margin = new Padding(4, 5, 4, 3);
            filtros.Controls.Add(Tema.Etiqueta("Semestre", Tema.Semi));
            filtros.Controls.Add(_semestre);

            Tema.EstiloGrilla(_grid);
            _grid.RowTemplate.Height = 52;
            _grid.Columns.Add(new DataGridViewImageColumn { Name = "foto", HeaderText = "", Width = 62, ImageLayout = DataGridViewImageCellLayout.Zoom });
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
            _grid.Columns["curso"].Width = 140;
            _grid.Columns["activo"].Width = 70;
            _grid.Columns["codigo"].DefaultCellStyle.ForeColor = Tema.Suave;
            _grid.Columns["apellidos"].DefaultCellStyle.Font = Tema.Semi;
            _grid.Columns["rostro"].Width = 150;
            _grid.Columns["semestre"].Width = 135;
            Tema.FotosRedondas(_grid, "foto");
            Tema.Pildoras(_grid, "rostro");
            Tema.Pildoras(_grid, "semestre");
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) RegistrarRostro(); };

            _info.Dock = DockStyle.Bottom;
            _info.Height = 50;
            _info.ForeColor = Tema.Suave;
            _info.BackColor = Color.Transparent;
            _info.Padding = new Padding(4, 8, 4, 0);
            _info.Text = $"Doble clic = registrar rostro. CSV para importar: codigo;nombres;apellidos;curso;semestre (semestre 1 a 6: lo inscribe en {_gestion}).\n" +
                         "Al eliminar a un estudiante se borran también su rostro y sus asistencias (datos personales).";
            var tarjeta = new Tarjeta { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 4) };
            tarjeta.Controls.Add(_grid);
            tarjeta.Controls.Add(_info);
            var marco = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 16), BackColor = Tema.Fondo };
            marco.Controls.Add(tarjeta);
            Controls.Add(marco);
            Controls.Add(filtros);
            Controls.Add(arriba);
            _cabecera.SendToBack();
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
                    e.Semestre > 0 ? Academico.Semestre(e.Semestre) + " semestre" : "sin inscribir", e.Curso,
                    e.Muestras > 0 ? $"Sí · {e.Muestras} muestras" : "falta registrar", e.Activo ? "sí" : "no");
                var fila = _grid.Rows[i];
                fila.Tag = e;
                if (!e.Activo) fila.DefaultCellStyle.ForeColor = Tema.Suave;
                if (sel == e.Id) fila.Selected = true;
            }
            int total = _db.Estudiantes().Count, conRostro = _db.Estudiantes().Count(x => x.Muestras > 0);
            Text = $"Estudiantes y registro de rostros — {total} estudiantes, {conRostro} con rostro registrado";
            _cabecera.Subtitulo = $"Gestión {_gestion}  ·  {total} estudiantes  ·  {conRostro} con rostro registrado  ·  {total - conRostro} pendientes";
        }

        void Editar(Estudiante e)
        {
            using var f = new FormEditarEstudiante(e, _gestion);
            while (f.ShowDialog(Tema.Dueno(this)) == DialogResult.OK)
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
            f.ShowDialog(Tema.Dueno(this));
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
            if (d.ShowDialog(Tema.Dueno(this)) != DialogResult.OK) return;
            var (nuevos, act, errores) = _db.ImportarCsv(File.ReadAllText(d.FileName, Encoding.UTF8));
            Cargar();
            MessageBox.Show($"Nuevos: {nuevos}\nActualizados: {act}" + (errores.Count > 0 ? "\n\nNo se importaron:\n" + string.Join("\n", errores.Take(15)) : ""),
                "Importar lista");
        }

        void Exportar()
        {
            using var d = new SaveFileDialog { FileName = "estudiantes.csv", Filter = "CSV (*.csv)|*.csv" };
            if (d.ShowDialog(Tema.Dueno(this)) != DialogResult.OK) return;
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
            ClientSize = new Size(520, 400);
            Tema.Preparar(this, Text, e.Id == 0 ? "Datos del nuevo estudiante" : $"{e.NombreCompleto} · {e.Codigo}", Tema.Papel);
            _codigo.Text = e.Codigo;
            _nombres.Text = e.Nombres;
            _apellidos.Text = e.Apellidos;
            _curso.Text = e.Curso;
            _activo.Checked = e.Activo;
            for (int i = 0; i <= 6; i++) _semestre.Items.Add(Academico.Semestre(i));      // índice = semestre (0 = sin inscribir)
            _semestre.SelectedIndex = Math.Max(0, Math.Min(6, e.Semestre));
            int y = 88;
            foreach (var (txt, caja) in new (string, Control)[] { ("Código o CI *", _codigo), ("Nombres *", _nombres), ("Apellidos", _apellidos),
                         ("Carrera / curso", _curso), ($"Semestre {gestion}", _semestre) })
            {
                Controls.Add(new Label { Text = txt, Left = 22, Top = y + 5, Width = 120, ForeColor = Tema.Tinta });
                Tema.EstiloCampo(caja);
                caja.SetBounds(146, y, 350, 28);
                Controls.Add(caja);
                y += 40;
            }
            _activo.Location = new Point(146, y + 2);
            _activo.ForeColor = Tema.Suave;
            Controls.Add(_activo);
            var ok = Tema.Boton("Guardar", Tema.Naranja, null);
            ok.DialogResult = DialogResult.OK;
            var no = Tema.Boton("Cancelar", Tema.Suave, null);
            no.DialogResult = DialogResult.Cancel;
            Tema.Pie(this, ok, no);
            ClientSize = new Size(520, y + 36 + 60);
            AcceptButton = ok;
            CancelButton = no;
        }
    }
}
