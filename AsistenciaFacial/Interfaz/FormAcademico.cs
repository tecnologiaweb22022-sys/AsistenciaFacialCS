using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AsistenciaFacial.Nucleo;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>
    /// Estructura académica (solo administrador): gestiones (I-2026, II-2026…), docentes con su usuario y contraseña
    /// y materias de cada semestre con su docente y horario semanal.
    /// </summary>
    internal sealed class FormAcademico : Form
    {
        readonly BaseDatos _db;
        readonly DataGridView _gGestiones = new DataGridView(), _gDocentes = new DataGridView(), _gMaterias = new DataGridView();
        readonly ComboBox _gestionMaterias = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Margin = new Padding(3, 6, 3, 3) };

        public FormAcademico(BaseDatos db)
        {
            _db = db;
            Text = "Académico · gestiones, docentes y materias";
            Size = new Size(1180, 720);
            StartPosition = FormStartPosition.CenterParent;
            var cabecera = Tema.Preparar(this, "Estructura académica", "Gestiones, docentes con su usuario y contraseña, y materias de cada semestre con su horario");

            var tabs = new Pestanas { Dock = DockStyle.Fill };
            tabs.TabPages.Add(PaginaMaterias());
            tabs.TabPages.Add(PaginaDocentes());
            tabs.TabPages.Add(PaginaGestiones());
            var tarjeta = new Tarjeta { Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 6) };
            tarjeta.Controls.Add(tabs);
            var marco = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Tema.Fondo };
            marco.Controls.Add(tarjeta);
            Controls.Add(marco);
            cabecera.SendToBack();
            Load += (s, e) => { CargarGestiones(); CargarDocentes(); };
        }

        static TabPage Pagina(string titulo, DataGridView grid, Control arriba, string ayuda)
        {
            var p = new TabPage(titulo) { BackColor = Tema.Papel, Padding = new Padding(10, 6, 10, 8) };
            Tema.EstiloGrilla(grid);
            p.Controls.Add(grid);
            p.Controls.Add(new Label
            {
                Dock = DockStyle.Bottom, Height = 54, ForeColor = Tema.MarinoMedio, BackColor = Tema.MarinoBruma, Font = Tema.Chica,
                Padding = new Padding(10, 8, 10, 0), Text = "ℹ  " + ayuda.Replace("\n", "\n     "),
            });
            p.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 8, BackColor = Color.Transparent });
            p.Controls.Add(arriba);
            return p;
        }

        static FlowLayoutPanel Barra() => Tema.Fila(DockStyle.Top, new Padding(0, 4, 0, 8));

        static T Elegido<T>(DataGridView g) where T : class => g.SelectedRows.Count > 0 ? g.SelectedRows[0].Tag as T : null;

        static void Error(Exception e) => MessageBox.Show(e.Message, "Académico", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        // ───────────────────────── materias ─────────────────────────
        TabPage PaginaMaterias()
        {
            var b = Barra();
            b.Controls.Add(Tema.Etiqueta("Gestión", Tema.Semi));
            Tema.EstiloCampo(_gestionMaterias);
            _gestionMaterias.Margin = new Padding(4, 5, 14, 3);
            b.Controls.Add(_gestionMaterias);
            _gestionMaterias.SelectedIndexChanged += (s, e) => CargarMaterias();
            b.Controls.Add(Tema.Boton("+ Nueva materia", Tema.Naranja, (s, e) => EditarMateria(new Materia { GestionId = (_gestionMaterias.SelectedItem as Gestion)?.Id ?? 0 })));
            b.Controls.Add(Tema.Boton("Editar", Tema.Suave, (s, e) => { if (Elegido<Materia>(_gMaterias) is Materia m) EditarMateria(m); }));
            b.Controls.Add(Tema.Boton("Eliminar", Tema.Rojo, (s, e) => EliminarMateria()));
            foreach (var (n, t, w) in new[] { ("sem", "Semestre", 80), ("sigla", "Sigla", 90), ("nombre", "Materia", 0), ("docente", "Docente", 200),
                         ("horario", "Horario", 300), ("tol", "Tolerancia", 80) })
            {
                _gMaterias.Columns.Add(n, t);
                if (w > 0) _gMaterias.Columns[n].Width = w; else _gMaterias.Columns[n].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }
            _gMaterias.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && Elegido<Materia>(_gMaterias) is Materia m) EditarMateria(m); };
            _gMaterias.Columns["sem"].DefaultCellStyle.Font = Tema.Semi;
            _gMaterias.Columns["nombre"].DefaultCellStyle.Font = Tema.Semi;
            Tema.Pildoras(_gMaterias, "tol");
            return Pagina("Materias y horarios", _gMaterias, b,
                "Cada materia pertenece a un semestre de la gestión. La cámara marca en la materia del semestre del estudiante que tenga clase en ese momento.\n" +
                "Tolerancia: minutos después del inicio en que todavía es PUNTUAL. Un semestre no puede tener dos clases a la misma hora.");
        }

        void CargarMaterias()
        {
            _gMaterias.Rows.Clear();
            if (!(_gestionMaterias.SelectedItem is Gestion g)) return;
            foreach (var m in _db.Materias(g.Id))
            {
                int i = _gMaterias.Rows.Add(Academico.Semestre(m.Semestre), m.Sigla, m.Nombre, m.Docente.Length > 0 ? m.Docente : "(sin docente)",
                    m.TextoHorario, $"{m.ToleranciaMin} min");
                _gMaterias.Rows[i].Tag = m;
                if (m.DocenteId == null) _gMaterias.Rows[i].Cells["docente"].Style.ForeColor = Tema.Ambar;
            }
        }

        void EditarMateria(Materia m)
        {
            if (m.GestionId == 0) { MessageBox.Show("Primero crea una gestión (pestaña «Gestiones»).", Text); return; }
            using var f = new FormEditarMateria(m, _db.Gestiones(), _db.Docentes());
            while (f.ShowDialog(Tema.Dueno(this)) == DialogResult.OK)
            {
                try { _db.GuardarMateria(f.Resultado); CargarMaterias(); CargarDocentes(); return; }
                catch (ErrorValidacion ex) { Error(ex); }
            }
        }

        void EliminarMateria()
        {
            if (!(Elegido<Materia>(_gMaterias) is Materia m)) return;
            int n = _db.AsistenciasDeMateria(m.Id);
            if (MessageBox.Show($"¿Eliminar la materia {m.Titulo}?" + (n > 0 ? $"\n\nSe borran también sus {n} asistencias registradas. No se puede deshacer." : ""),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { _db.EliminarMateria(m.Id); } catch (ErrorValidacion ex) { Error(ex); }
            CargarMaterias();
            CargarDocentes();
        }

        // ───────────────────────── docentes ─────────────────────────
        TabPage PaginaDocentes()
        {
            var b = Barra();
            b.Controls.Add(Tema.Boton("+ Nuevo docente", Tema.Naranja, (s, e) => EditarDocente(new Docente())));
            b.Controls.Add(Tema.Boton("Editar / cambiar contraseña", Tema.Suave, (s, e) => { if (Elegido<Docente>(_gDocentes) is Docente d) EditarDocente(d); }));
            b.Controls.Add(Tema.Boton("Eliminar", Tema.Rojo, (s, e) => EliminarDocente()));
            foreach (var (n, t, w) in new[] { ("apellidos", "Apellidos", 0), ("nombres", "Nombres", 0), ("usuario", "Usuario", 170),
                         ("materias", "Materias (gestión activa)", 180), ("pin", "Contraseña", 130), ("activo", "Activo", 70) })
            {
                _gDocentes.Columns.Add(n, t);
                if (w > 0) _gDocentes.Columns[n].Width = w; else _gDocentes.Columns[n].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }
            _gDocentes.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && Elegido<Docente>(_gDocentes) is Docente d) EditarDocente(d); };
            Tema.Pildoras(_gDocentes, "pin");
            _gDocentes.Columns["apellidos"].DefaultCellStyle.Font = Tema.Semi;
            return Pagina("Docentes", _gDocentes, b,
                "Cada docente entra al programa y al panel web con su usuario y contraseña, y ve solo sus materias.\n" +
                "El administrador (usuario y contraseña en «Configuración») gestiona todo. Las contraseñas se guardan cifradas.");
        }

        void CargarDocentes()
        {
            _gDocentes.Rows.Clear();
            foreach (var d in _db.Docentes())
            {
                int i = _gDocentes.Rows.Add(d.Apellidos, d.Nombres, d.Usuario, d.Materias, d.TienePin ? "configurada" : "falta", d.Activo ? "sí" : "no");
                _gDocentes.Rows[i].Tag = d;
                if (!d.Activo) _gDocentes.Rows[i].DefaultCellStyle.ForeColor = Tema.Suave;
            }
        }

        void EditarDocente(Docente d)
        {
            using var f = new FormEditarDocente(d);
            while (f.ShowDialog(Tema.Dueno(this)) == DialogResult.OK)
            {
                try { _db.GuardarDocente(f.Resultado, f.ClaveNueva, f.ClaveRepetida); CargarDocentes(); CargarMaterias(); return; }
                catch (ErrorValidacion ex) { Error(ex); }
            }
        }

        void EliminarDocente()
        {
            if (!(Elegido<Docente>(_gDocentes) is Docente d)) return;
            if (MessageBox.Show($"¿Eliminar a {d.NombreCompleto}?\n\nSus materias quedan «sin docente» (no se borran). " +
                                "Si solo deja de trabajar, mejor edítalo y desmarca «Activo».", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _db.EliminarDocente(d.Id);
            CargarDocentes();
            CargarMaterias();
        }

        // ───────────────────────── gestiones ─────────────────────────
        TabPage PaginaGestiones()
        {
            var b = Barra();
            b.Controls.Add(Tema.Boton("+ Nueva gestión", Tema.Naranja, (s, e) => EditarGestion(new Gestion())));
            b.Controls.Add(Tema.Boton("Editar", Tema.Suave, (s, e) => { if (Elegido<Gestion>(_gGestiones) is Gestion g) EditarGestion(g); }));
            b.Controls.Add(Tema.Boton("★ Activar", Tema.MarinoMedio, (s, e) => Activar()));
            b.Controls.Add(Tema.Boton("Copiar materias / promover estudiantes…", Tema.Suave, (s, e) => Copiar()));
            b.Controls.Add(Tema.Boton("Eliminar", Tema.Rojo, (s, e) => EliminarGestion()));
            foreach (var (n, t, w) in new[] { ("nombre", "Gestión", 0), ("inicio", "Desde", 110), ("fin", "Hasta", 110), ("materias", "Materias", 90),
                         ("inscritos", "Inscritos", 90), ("activa", "Activa", 90) })
            {
                _gGestiones.Columns.Add(n, t);
                if (w > 0) _gGestiones.Columns[n].Width = w; else _gGestiones.Columns[n].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }
            Tema.Pildoras(_gGestiones, "activa");
            return Pagina("Gestiones", _gGestiones, b,
                "Solo una gestión está ACTIVA: en ella marca la cámara y en ella se inscriben los estudiantes. Las demás quedan como historial.\n" +
                "Al empezar una gestión nueva: créala, copia las materias de la misma gestión del año pasado, promueve a los estudiantes y actívala.");
        }

        void CargarGestiones()
        {
            long? antes = (_gestionMaterias.SelectedItem as Gestion)?.Id;
            var lista = _db.Gestiones();
            _gGestiones.Rows.Clear();
            foreach (var g in lista)
            {
                int i = _gGestiones.Rows.Add(g.Nombre, g.Inicio, g.Fin, g.Materias, g.Inscritos, g.Activa ? "★ activa" : "");
                _gGestiones.Rows[i].Tag = g;
                if (g.Activa) _gGestiones.Rows[i].DefaultCellStyle.Font = Tema.Negrita;
            }
            _gestionMaterias.Items.Clear();
            foreach (var g in lista) _gestionMaterias.Items.Add(g);
            int sel = lista.FindIndex(g => g.Id == antes);
            if (sel < 0) sel = lista.FindIndex(g => g.Activa);
            if (lista.Count > 0) _gestionMaterias.SelectedIndex = Math.Max(0, sel);
            CargarMaterias();
        }

        void EditarGestion(Gestion g)
        {
            using var f = new FormEditarGestion(g);
            while (f.ShowDialog(Tema.Dueno(this)) == DialogResult.OK)
            {
                try { _db.GuardarGestion(f.Resultado); CargarGestiones(); return; }
                catch (ErrorValidacion ex) { Error(ex); }
            }
        }

        void Activar()
        {
            if (!(Elegido<Gestion>(_gGestiones) is Gestion g) || g.Activa) return;
            if (MessageBox.Show($"¿Activar la gestión {g.Nombre}?\n\nDesde ahora la cámara marcará en las materias de {g.Nombre} " +
                                "y el semestre de cada estudiante será el de esa gestión.", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { _db.ActivarGestion(g.Id); } catch (ErrorValidacion ex) { Error(ex); }
            CargarGestiones();
            CargarDocentes();
        }

        void Copiar()
        {
            if (!(Elegido<Gestion>(_gGestiones) is Gestion destino)) { MessageBox.Show("Selecciona la gestión de destino (la nueva).", Text); return; }
            using var f = new FormCopiarGestion(destino, _db.Gestiones().Where(x => x.Id != destino.Id).ToList());
            if (f.ShowDialog(Tema.Dueno(this)) != DialogResult.OK) return;
            try
            {
                string res = "";
                if (f.Materias != null) res += $"Materias copiadas de {f.Materias.Nombre}: {_db.CopiarMaterias(f.Materias.Id, destino.Id)}\n";
                if (f.Promover != null) res += $"Estudiantes de {f.Promover.Nombre} inscritos en el semestre siguiente: {_db.PromoverEstudiantes(f.Promover.Id, destino.Id)}\n" +
                                               "(los de 6to egresan y no se inscriben)";
                if (res.Length > 0) MessageBox.Show(res, Text);
            }
            catch (ErrorValidacion ex) { Error(ex); }
            CargarGestiones();
        }

        void EliminarGestion()
        {
            if (!(Elegido<Gestion>(_gGestiones) is Gestion g)) return;
            if (MessageBox.Show($"¿Eliminar la gestión {g.Nombre} con sus materias e inscripciones?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { _db.EliminarGestion(g.Id); } catch (ErrorValidacion ex) { Error(ex); }
            CargarGestiones();
        }
    }

    // ───────────────────────── diálogos de edición ─────────────────────────
    internal abstract class Dialogo : Form
    {
        protected int Y = 68 + 20;          // debajo de la cabecera

        protected Dialogo(string titulo, int ancho)
        {
            Text = titulo;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(ancho, 200);
            Tema.Preparar(this, titulo, null, Tema.Papel);
        }

        protected void Fila(string texto, Control c, int ancho = 270, string ayuda = null)
        {
            Controls.Add(new Label { Text = texto, Left = 22, Top = Y + 5, Width = 128, ForeColor = Tema.Tinta });
            if (!(c is DateTimePicker)) Tema.EstiloCampo(c); else c.Font = Tema.Campo;
            c.SetBounds(154, Y, ancho, 28);
            Controls.Add(c);
            if (ayuda != null) Controls.Add(new Label { Text = ayuda, Left = 162 + ancho, Top = Y + 6, AutoSize = true, ForeColor = Tema.Suave, Font = Tema.Chica });
            Y += 40;
        }

        protected void Botones()
        {
            var ok = Tema.Boton("Guardar", Tema.Naranja, null);
            ok.DialogResult = DialogResult.OK;
            var no = Tema.Boton("Cancelar", Tema.Suave, null);
            no.DialogResult = DialogResult.Cancel;
            Tema.Pie(this, ok, no);
            AcceptButton = ok;
            CancelButton = no;
            ClientSize = new Size(ClientSize.Width, Y + 12 + 60);
        }
    }

    internal sealed class FormEditarGestion : Dialogo
    {
        readonly TextBox _nombre = new TextBox();
        readonly DateTimePicker _inicio = new DateTimePicker { Format = DateTimePickerFormat.Short }, _fin = new DateTimePicker { Format = DateTimePickerFormat.Short };
        readonly Gestion _g;

        public Gestion Resultado => new Gestion
        {
            Id = _g.Id, Nombre = _nombre.Text, Inicio = BaseDatos.Dia(_inicio.Value), Fin = BaseDatos.Dia(_fin.Value),
        };

        public FormEditarGestion(Gestion g) : base(g.Id == 0 ? "Nueva gestión" : "Editar gestión", 470)
        {
            _g = g;
            int a = DateTime.Today.Year;
            bool segunda = DateTime.Today.Month >= 7;
            _nombre.Text = g.Id > 0 ? g.Nombre : segunda ? $"I-{a + 1}" : $"II-{a}";
            _inicio.Value = Leer(g.Inicio, segunda ? new DateTime(a + 1, 2, 1) : new DateTime(a, 7, 1));
            _fin.Value = Leer(g.Fin, segunda ? new DateTime(a + 1, 6, 30) : new DateTime(a, 12, 31));
            Fila("Nombre", _nombre, 120, "ej. I-2027, II-2027");
            Fila("Desde", _inicio, 130);
            Fila("Hasta", _fin, 130);
            Botones();
        }

        static DateTime Leer(string f, DateTime defecto) =>
            DateTime.TryParseExact(f ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : defecto;
    }

    internal sealed class FormEditarDocente : Dialogo
    {
        readonly TextBox _nombres = new TextBox(), _apellidos = new TextBox(), _usuario = new TextBox { MaxLength = 30 },
            _clave = new TextBox { UseSystemPasswordChar = true, MaxLength = 100 }, _repetir = new TextBox { UseSystemPasswordChar = true, MaxLength = 100 };
        readonly CheckBox _activo = new CheckBox { Text = "Activo (puede entrar al programa)", AutoSize = true };
        readonly Docente _d;
        bool _usuarioEditado;

        public Docente Resultado => new Docente { Id = _d.Id, Nombres = _nombres.Text, Apellidos = _apellidos.Text, Usuario = _usuario.Text, Activo = _activo.Checked };
        public string ClaveNueva => _clave.Text;
        public string ClaveRepetida => _repetir.Text;

        public FormEditarDocente(Docente d) : base(d.Id == 0 ? "Nuevo docente" : "Editar docente", 560)
        {
            _d = d;
            _nombres.Text = d.Nombres;
            _apellidos.Text = d.Apellidos;
            _usuario.Text = d.Usuario;
            _usuarioEditado = d.Id > 0;
            _activo.Checked = d.Id == 0 || d.Activo;
            // al crear, el usuario se sugiere solo a partir del nombre (se puede cambiar)
            void Sugerir(object s, EventArgs e) { if (!_usuarioEditado) _usuario.Text = Claves.Sugerir(_nombres.Text, _apellidos.Text); }
            _nombres.TextChanged += Sugerir;
            _apellidos.TextChanged += Sugerir;
            _usuario.KeyPress += (s, e) => _usuarioEditado = true;
            Fila("Nombres *", _nombres, 360);
            Fila("Apellidos", _apellidos, 360);
            Fila("Usuario *", _usuario, 200, "para entrar, ej. maria.quispe");
            Fila(d.Id == 0 ? "Contraseña *" : "Nueva contraseña", _clave, 200, d.Id == 0 ? $"mínimo {Claves.LargoMinimo} caracteres" : "vacío = no cambiar");
            Fila("Repetir contraseña", _repetir, 200);
            _activo.Location = new Point(154, Y + 2);
            _activo.ForeColor = Tema.Suave;
            Controls.Add(_activo);
            Y += 30;
            Botones();
        }
    }

    internal sealed class FormEditarMateria : Dialogo
    {
        readonly ComboBox _gestion = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList },
            _semestre = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList },
            _docente = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox _sigla = new TextBox(), _nombre = new TextBox();
        readonly NumericUpDown _tolerancia = new NumericUpDown { Minimum = 0, Maximum = 120 };
        readonly DataGridView _horario = new DataGridView();
        readonly Materia _m;
        static readonly string[] Dias = Enumerable.Range(1, 7).Select(Academico.Dia).ToArray();

        public Materia Resultado
        {
            get
            {
                var bloques = new List<BloqueHorario>();
                foreach (DataGridViewRow r in _horario.Rows)
                {
                    if (r.IsNewRow) continue;
                    string dia = r.Cells["dia"].Value as string, ini = (r.Cells["inicio"].Value as string ?? "").Trim(), fin = (r.Cells["fin"].Value as string ?? "").Trim();
                    if (string.IsNullOrEmpty(dia) && ini.Length == 0 && fin.Length == 0) continue;
                    bloques.Add(new BloqueHorario { Dia = Array.IndexOf(Dias, dia) + 1, Inicio = Normalizar(ini), Fin = Normalizar(fin) });
                }
                return new Materia
                {
                    Id = _m.Id, GestionId = (_gestion.SelectedItem as Gestion)?.Id ?? 0, Semestre = _semestre.SelectedIndex + 1,
                    Sigla = _sigla.Text, Nombre = _nombre.Text, DocenteId = (_docente.SelectedItem as Docente)?.Id,
                    ToleranciaMin = (int)_tolerancia.Value, Horarios = bloques,
                };
            }
        }

        /// <summary>"8:00" o "8" → "08:00".</summary>
        static string Normalizar(string h)
        {
            if (int.TryParse(h, out int solo) && solo >= 0 && solo < 24) return $"{solo:00}:00";
            return TimeSpan.TryParse(h, CultureInfo.InvariantCulture, out var t) && t.TotalHours < 24 ? t.ToString(@"hh\:mm") : h;
        }

        public FormEditarMateria(Materia m, List<Gestion> gestiones, List<Docente> docentes) : base(m.Id == 0 ? "Nueva materia" : "Editar materia", 560)
        {
            _m = m;
            foreach (var g in gestiones) _gestion.Items.Add(g);
            _gestion.SelectedIndex = Math.Max(0, gestiones.FindIndex(g => g.Id == m.GestionId));
            for (int i = 1; i <= 6; i++) _semestre.Items.Add(Academico.Semestre(i) + " semestre");
            _semestre.SelectedIndex = Math.Max(0, Math.Min(5, m.Semestre - 1));
            _docente.Items.Add("(sin docente)");
            foreach (var d in docentes.Where(d => d.Activo || d.Id == m.DocenteId)) _docente.Items.Add(d);
            _docente.SelectedIndex = Math.Max(0, _docente.Items.Cast<object>().ToList().FindIndex(x => x is Docente d && d.Id == m.DocenteId));
            _sigla.Text = m.Sigla;
            _nombre.Text = m.Nombre;
            _tolerancia.Value = Math.Max(0, Math.Min(120, m.Id == 0 ? 10 : m.ToleranciaMin));

            Fila("Gestión", _gestion, 150);
            Fila("Semestre", _semestre, 150);
            Fila("Sigla", _sigla, 120, "ej. IAR-600");
            Fila("Nombre *", _nombre, 380);
            Fila("Docente", _docente, 300);
            Fila("Tolerancia (min)", _tolerancia, 70, "después del inicio todavía es PUNTUAL");

            Y += 4;
            Controls.Add(new Label { Text = "HORARIO SEMANAL  ·  una fila por día, horas HH:mm", Left = 22, Top = Y, AutoSize = true, Font = Tema.SemiChica, ForeColor = Tema.NaranjaOscuro });
            Y += 24;
            _horario.SetBounds(22, Y, 516, 190);
            _horario.BackgroundColor = Color.White;
            _horario.BorderStyle = BorderStyle.FixedSingle;
            _horario.EnableHeadersVisualStyles = false;
            _horario.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            _horario.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _horario.ColumnHeadersHeight = 34;
            _horario.ColumnHeadersDefaultCellStyle.BackColor = Tema.Marino;
            _horario.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _horario.ColumnHeadersDefaultCellStyle.Font = Tema.SemiChica;
            _horario.ColumnHeadersDefaultCellStyle.SelectionBackColor = Tema.Marino;
            _horario.DefaultCellStyle.SelectionBackColor = Tema.NaranjaClaro;
            _horario.DefaultCellStyle.SelectionForeColor = Tema.Tinta;
            _horario.GridColor = Tema.Linea;
            _horario.RowTemplate.Height = 30;
            _horario.RowHeadersWidth = 28;
            _horario.AllowUserToAddRows = true;
            _horario.AllowUserToDeleteRows = true;
            _horario.Columns.Add(new DataGridViewComboBoxColumn { Name = "dia", HeaderText = "Día", Width = 170, DataSource = Dias, FlatStyle = FlatStyle.Flat });
            _horario.Columns.Add("inicio", "Inicio");
            _horario.Columns.Add("fin", "Fin");
            _horario.Columns["inicio"].Width = _horario.Columns["fin"].Width = 140;
            _horario.Columns["dia"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            foreach (var b in m.Horarios) _horario.Rows.Add(Academico.Dia(b.Dia), b.Inicio, b.Fin);
            _horario.DataError += (s, e) => e.ThrowException = false;
            Controls.Add(_horario);
            Y += 196;
            Controls.Add(new Label { Text = "Escribe en la última fila para agregar un día. Selecciona una fila y pulsa Supr para quitarla.", Left = 22, Top = Y, AutoSize = true, ForeColor = Tema.Suave, Font = Tema.Chica });
            Y += 22;
            Botones();
        }
    }

    internal sealed class FormCopiarGestion : Dialogo
    {
        readonly CheckBox _chkMaterias = new CheckBox { AutoSize = true, Text = "Copiar materias (con docente y horario) de:" },
            _chkPromover = new CheckBox { AutoSize = true, Text = "Promover estudiantes al semestre siguiente desde:" };
        readonly ComboBox _materias = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList },
            _promover = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

        public Gestion Materias => _chkMaterias.Checked ? _materias.SelectedItem as Gestion : null;
        public Gestion Promover => _chkPromover.Checked ? _promover.SelectedItem as Gestion : null;

        public FormCopiarGestion(Gestion destino, List<Gestion> otras) : base($"Preparar la gestión {destino.Nombre}", 520)
        {
            foreach (var g in otras) { _materias.Items.Add(g); _promover.Items.Add(g); }
            // Materias: la misma gestión del año anterior (I con I, II con II). Estudiantes: la gestión inmediata anterior.
            string tipo = destino.Nombre.Split('-')[0];
            int mismaTipo = otras.FindIndex(g => g.Nombre.Split('-')[0] == tipo);
            int anterior = otras.FindIndex(g => string.CompareOrdinal(g.Inicio, destino.Inicio) < 0);
            if (otras.Count > 0)
            {
                _materias.SelectedIndex = Math.Max(0, mismaTipo);
                _promover.SelectedIndex = Math.Max(0, anterior);
            }
            Controls.Add(new Label
            {
                Text = $"Llena la gestión {destino.Nombre} con datos de otra gestión. Lo que ya exista no se duplica.", Left = 22, Top = Y,
                AutoSize = true, ForeColor = Tema.Suave,
            });
            Y += 32;
            _chkMaterias.Location = new Point(22, Y); _chkMaterias.ForeColor = Tema.Tinta; Controls.Add(_chkMaterias);
            Tema.EstiloCampo(_materias);
            _materias.SetBounds(360, Y - 3, 140, 28); Controls.Add(_materias);
            Y += 26;
            Controls.Add(new Label { Text = "ej. para I-2027 copia las de I-2026 (1ro, 3ro y 5to)", Left = 42, Top = Y, AutoSize = true, ForeColor = Tema.Suave, Font = Tema.Chica });
            Y += 32;
            _chkPromover.Location = new Point(22, Y); _chkPromover.ForeColor = Tema.Tinta; Controls.Add(_chkPromover);
            Tema.EstiloCampo(_promover);
            _promover.SetBounds(360, Y - 3, 140, 28); Controls.Add(_promover);
            Y += 26;
            Controls.Add(new Label { Text = "ej. de II-2026: 2do → 3ro, 4to → 5to; los de 6to egresan", Left = 42, Top = Y, AutoSize = true, ForeColor = Tema.Suave, Font = Tema.Chica });
            Y += 30;
            _chkMaterias.Checked = _chkPromover.Checked = otras.Count > 0;
            Botones();
        }
    }
}
