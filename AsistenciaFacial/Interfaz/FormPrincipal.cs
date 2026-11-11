using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AsistenciaFacial.Nucleo;
using OpenCvSharp.Extensions;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>
    /// Ventana principal: cámara que reconoce rostros y marca la asistencia en la materia que tiene
    /// clase en ese momento, lista por materia y día con correcciones, alertas, y acceso a estudiantes,
    /// estructura académica, reportes y configuración (según sea administrador o docente).
    /// </summary>
    public sealed class FormPrincipal : Form
    {
        readonly Configuracion _cfg;
        readonly BaseDatos _db;
        readonly MotorAsistencia _motor;
        readonly ServidorWeb _web;
        Usuario _usuario;
        readonly ComboBox _cmbMateria = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        readonly CheckBox _chkSeguir = new CheckBox { Text = "Seguir la clase en curso", AutoSize = true, Checked = true, Margin = new Padding(10, 8, 0, 0) };
        readonly FlowLayoutPanel _botones = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Tema.Tinta, Padding = new Padding(0, 14, 8, 0) };
        int _ticks;

        readonly Label _lblTitulo = new Label(), _lblServidor = new Label(), _lblCamara = new Label(), _lblResumen = new Label(), _lblAlerta = new Label();
        readonly Panel _panelAlerta = new Panel(), _bienvenida = new Panel();
        readonly PictureBox _picCamara = new PictureBox(), _picFoto = new PictureBox();
        readonly Label _lblNombre = new Label(), _lblDetalle = new Label();
        readonly ComboBox _cmbFuente = new ComboBox();
        readonly Button _btnCamara;
        readonly CheckBox _chkSonido = new CheckBox();
        readonly DateTimePicker _fecha = new DateTimePicker();
        readonly DataGridView _grid = new DataGridView();
        readonly ListView _lstAlertas = new ListView();
        readonly TabControl _pestanas = new TabControl();
        readonly NotifyIcon _bandeja = new NotifyIcon();
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 1000 };

        long _ultimoEvento;
        bool _primera = true;
        string _firma = "";
        int _frameEnCamino;
        DateTime _ocultarBienvenida = DateTime.MaxValue, _ocultarAlerta = DateTime.MaxValue;

        public BaseDatos Db => _db;
        public MotorAsistencia Motor => _motor;

        public FormPrincipal(Configuracion cfg, BaseDatos db, Usuario usuario)
        {
            _cfg = cfg;
            _db = db;
            _usuario = usuario;
            _motor = new MotorAsistencia(_cfg, _db);
            _web = new ServidorWeb(_cfg, _db, _motor);

            Text = $"Control de asistencia · reconocimiento facial · {_cfg.Institucion}";
            Font = Tema.Normal;
            BackColor = Tema.Fondo;
            MinimumSize = new Size(1100, 680);
            Size = new Size(1366, 800);
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            _btnCamara = Tema.Boton("▶ Iniciar cámara", Tema.Verde, (s, e) => AlternarCamara());

            Controls.Add(ConstruirCuerpo());
            Controls.Add(ConstruirAlerta());
            Controls.Add(ConstruirBarra());
            Controls.Add(ConstruirEstado());

            _bandeja.Icon = SystemIcons.Information;
            _bandeja.Text = "Asistencia IAR-600";
            _bandeja.Visible = true;

            _motor.FrameListo += MostrarFrame;
            _motor.Mensaje += m => EnHilo(() => _lblCamara.Text = "Cámara: " + m);
            _motor.Fallo += m => EnHilo(() => MostrarFallo(m));
            _motor.MarcaRealizada += (r, foto) => EnHilo(() => Bienvenida(r, foto));
            _timer.Tick += (s, e) => Refrescar();
            Load += AlCargar;
            FormClosing += AlCerrar;
        }

        // ───────────────────────── construcción ─────────────────────────
        Control ConstruirBarra()
        {
            var barra = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Tema.Tinta };
            _lblTitulo.ForeColor = Color.White;
            _lblTitulo.Font = Tema.Negrita;
            _lblTitulo.AutoSize = true;
            _lblTitulo.Location = new Point(14, 10);
            _chkSonido.Text = "Sonido";
            _chkSonido.Checked = true;
            _chkSonido.ForeColor = Color.White;
            _chkSonido.AutoSize = true;
            _chkSonido.Margin = new Padding(8, 7, 0, 0);
            _chkSonido.CheckedChanged += (s, e) => Sonido.Activo = _chkSonido.Checked;
            BotonesSegunUsuario();
            barra.Controls.Add(_lblTitulo);
            barra.Controls.Add(_botones);
            return barra;
        }

        /// <summary>El administrador ve todo; un docente, solo reportes y sus materias.</summary>
        void BotonesSegunUsuario()
        {
            _botones.Controls.Clear();
            Color c = Color.FromArgb(52, 66, 99);
            if (_usuario.EsAdmin)
            {
                _botones.Controls.Add(Tema.Boton("Estudiantes", Tema.Verde, (s, e) => { using var f = new FormEstudiantes(_cfg, _db, _motor); f.ShowDialog(this); _firma = ""; Refrescar(); }));
                _botones.Controls.Add(Tema.Boton("Académico", Tema.Naranja, (s, e) => { using var f = new FormAcademico(_db); f.ShowDialog(this); CargarMaterias(); }));
            }
            _botones.Controls.Add(Tema.Boton("Reportes", c, (s, e) => { using var f = new FormReportes(_db, _usuario, MateriaElegida); f.ShowDialog(this); }));
            if (_usuario.EsAdmin) _botones.Controls.Add(Tema.Boton("Configuración", c, (s, e) => Configurar()));
            _botones.Controls.Add(Tema.Boton("Cartel QR", c, (s, e) => { using var f = new FormQR(_cfg); f.ShowDialog(this); }));
            _botones.Controls.Add(Tema.Boton("Panel web", c, (s, e) => Navegador("/admin")));
            _botones.Controls.Add(Tema.Boton("Vista pública", c, (s, e) => Navegador("/")));
            _botones.Controls.Add(Tema.Boton("Cambiar usuario", c, (s, e) => CambiarUsuario()));
            _botones.Controls.Add(_chkSonido);
            _lblTitulo.Text = $"{_usuario.Nombre} · Gestión {_db.GestionActiva()?.Nombre ?? "(sin gestión activa)"}\n{_cfg.Institucion}";
        }

        void CambiarUsuario()
        {
            var u = FormIngreso.Pedir(_cfg, _db, this);
            if (u == null) return;
            _usuario = u;
            BotonesSegunUsuario();
            CargarMaterias();
        }

        /// <summary>Barra inferior: dirección para los celulares y estado de la cámara.</summary>
        Control ConstruirEstado()
        {
            var barra = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 26, BackColor = Tema.Tinta, Padding = new Padding(10, 5, 10, 0), WrapContents = false };
            foreach (var l in new[] { _lblServidor, _lblCamara })
            {
                l.ForeColor = Color.FromArgb(200, 210, 230);
                l.AutoSize = true;
                l.Font = Tema.Chica;
                l.Margin = new Padding(0, 0, 30, 0);
                barra.Controls.Add(l);
            }
            _lblServidor.Text = "Servidor web: iniciando…";
            _lblCamara.Text = "Cámara: detenida";
            return barra;
        }

        Control ConstruirAlerta()
        {
            _panelAlerta.Dock = DockStyle.Top;
            _panelAlerta.Height = 40;
            _panelAlerta.BackColor = Tema.Rojo;
            _panelAlerta.Visible = false;
            _lblAlerta.Dock = DockStyle.Fill;
            _lblAlerta.Font = Tema.Titulo;
            _lblAlerta.ForeColor = Color.White;
            _lblAlerta.TextAlign = ContentAlignment.MiddleLeft;
            _lblAlerta.Padding = new Padding(14, 0, 0, 0);
            _lblAlerta.Click += (s, e) => _panelAlerta.Visible = false;
            _panelAlerta.Controls.Add(_lblAlerta);
            return _panelAlerta;
        }

        Control ConstruirCuerpo()
        {
            var division = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, BackColor = Tema.Linea };
            division.Panel1.BackColor = Tema.Fondo;
            division.Panel2.BackColor = Tema.Fondo;
            division.Panel1.Padding = new Padding(10);
            division.Panel2.Padding = new Padding(4, 10, 10, 10);
            division.Panel1.Controls.Add(ConstruirCamara());
            division.Panel2.Controls.Add(ConstruirLista());
            Shown += (s, e) =>
            {
                try { division.SplitterDistance = (int)(division.Width * 0.46); } catch (InvalidOperationException) { }
            };
            return division;
        }

        Control ConstruirCamara()
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            _picCamara.Dock = DockStyle.Fill;
            _picCamara.SizeMode = PictureBoxSizeMode.Zoom;
            _picCamara.BackColor = Color.FromArgb(18, 24, 38);

            // Tarjeta de bienvenida: aparece unos segundos al marcar asistencia.
            _bienvenida.Dock = DockStyle.Bottom;
            _bienvenida.Height = 110;
            _bienvenida.BackColor = Tema.Verde;
            _bienvenida.Padding = new Padding(10);
            _bienvenida.Visible = false;
            _picFoto.Dock = DockStyle.Left;
            _picFoto.Width = 90;
            _picFoto.SizeMode = PictureBoxSizeMode.Zoom;
            _picFoto.BackColor = Color.White;
            _lblNombre.Dock = DockStyle.Top;
            _lblNombre.Height = 46;
            _lblNombre.Font = Tema.Grande;
            _lblNombre.ForeColor = Color.White;
            _lblNombre.Padding = new Padding(12, 4, 0, 0);
            _lblDetalle.Dock = DockStyle.Fill;
            _lblDetalle.Font = Tema.Titulo;
            _lblDetalle.ForeColor = Color.White;
            _lblDetalle.Padding = new Padding(14, 2, 0, 0);
            _bienvenida.Controls.Add(_lblDetalle);
            _bienvenida.Controls.Add(_lblNombre);
            _bienvenida.Controls.Add(_picFoto);

            var controles = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 6, 0, 0) };
            _cmbFuente.DropDownStyle = ComboBoxStyle.DropDown;
            _cmbFuente.Items.AddRange(new object[] { "0", "1", "2" });
            _cmbFuente.Text = _cfg.Reconocimiento.Fuente;
            _cmbFuente.Width = 230;
            controles.Controls.Add(new Label { Text = "Cámara:", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
            controles.Controls.Add(_cmbFuente);
            controles.Controls.Add(_btnCamara);
            controles.Controls.Add(Tema.Boton("Probar con fotos…", Tema.Suave, (s, e) =>
            {
                using var d = new FolderBrowserDialog { Description = "Carpeta con fotos de rostros (se mostrarán como si fueran la cámara)" };
                if (d.ShowDialog(this) != DialogResult.OK) return;
                _cmbFuente.Text = d.SelectedPath;
                if (_motor.Activo) AlternarCamara();
                AlternarCamara();
            }));
            controles.Controls.Add(Tema.Boton("Diagnóstico", Tema.Azul, (s, e) => Diagnosticar()));
            var ayuda = new Label
            {
                Dock = DockStyle.Bottom, Height = 34, ForeColor = Tema.Suave, Font = Tema.Chica,
                Text = "Cámara: 0 = webcam · 1 = cámara USB · URL = celular (cámara IP) · también un video .mp4 o una carpeta con fotos.\n" +
                       "Verde = asistencia marcada · Amarillo = confirmando (3 cuadros) o atraso · Rojo = rostro no registrado.",
            };
            panel.Controls.Add(_picCamara);
            panel.Controls.Add(_bienvenida);
            panel.Controls.Add(controles);
            panel.Controls.Add(ayuda);
            return panel;
        }

        Control ConstruirLista()
        {
            _pestanas.Dock = DockStyle.Fill;
            var hoy = new TabPage("Asistencia") { BackColor = Tema.Papel, Padding = new Padding(6) };
            var arriba = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            _fecha.Format = DateTimePickerFormat.Short;
            _fecha.Width = 120;
            _fecha.ValueChanged += (s, e) => { _firma = ""; Refrescar(); };
            _cmbMateria.SelectionChangeCommitted += (s, e) => { _chkSeguir.Checked = false; _firma = ""; Refrescar(); };
            _cmbMateria.SelectedIndexChanged += (s, e) => { _firma = ""; };
            _chkSeguir.CheckedChanged += (s, e) => { if (_chkSeguir.Checked) SeguirClase(); };
            arriba.Controls.Add(new Label { Text = "Materia:", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
            arriba.Controls.Add(_cmbMateria);
            arriba.Controls.Add(_chkSeguir);
            arriba.SetFlowBreak(_chkSeguir, true);
            arriba.Controls.Add(new Label { Text = "Fecha:", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
            arriba.Controls.Add(_fecha);
            arriba.Controls.Add(Tema.Boton("Hoy", Tema.Suave, (s, e) => _fecha.Value = DateTime.Today));
            _lblResumen.Dock = DockStyle.Top;
            _lblResumen.Height = 44;
            _lblResumen.Font = Tema.Negrita;
            _lblResumen.ForeColor = Tema.Tinta;
            _lblResumen.Padding = new Padding(4, 4, 0, 0);

            Tema.EstiloGrilla(_grid);
            _grid.Columns.Add("codigo", "Código");
            _grid.Columns.Add("nombre", "Estudiante");
            _grid.Columns.Add("estado", "Estado");
            _grid.Columns.Add("hora", "Entrada");
            _grid.Columns.Add("salida", "Salida");
            _grid.Columns.Add("similitud", "Similitud");
            _grid.Columns.Add("fuente", "Origen");
            _grid.Columns.Add("obs", "Observación");
            _grid.Columns["codigo"].Width = 70;
            _grid.Columns["nombre"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns["nombre"].MinimumWidth = 170;
            _grid.Columns["estado"].Width = 90;
            _grid.Columns["hora"].Width = 70;
            _grid.Columns["salida"].Width = 75;
            _grid.Columns["similitud"].Width = 65;
            _grid.Columns["fuente"].Width = 65;
            _grid.Columns["obs"].Width = 120;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Marcar PUNTUAL", null, (s, e) => Corregir("puntual"));
            menu.Items.Add("Marcar ATRASO", null, (s, e) => Corregir("atraso"));
            menu.Items.Add("JUSTIFICAR falta…", null, (s, e) => Corregir("justificado"));
            menu.Items.Add("Quitar marca (falta)", null, (s, e) => Corregir("falta"));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Marcar SALIDA", null, (s, e) => Corregir("salida"));
            menu.Items.Add("Quitar salida", null, (s, e) => Corregir("sin_salida"));
            _grid.ContextMenuStrip = menu;
            _grid.CellMouseDown += (s, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) _grid.Rows[e.RowIndex].Selected = true; };

            var abajo = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
            abajo.Controls.Add(new Label { Text = "Corregir el seleccionado:", AutoSize = true, Margin = new Padding(3, 10, 0, 0), ForeColor = Tema.Suave });
            abajo.Controls.Add(Tema.Boton("Puntual", Tema.Verde, (s, e) => Corregir("puntual")));
            abajo.Controls.Add(Tema.Boton("Atraso", Tema.Naranja, (s, e) => Corregir("atraso")));
            abajo.Controls.Add(Tema.Boton("Justificar…", Tema.Celeste, (s, e) => Corregir("justificado")));
            abajo.Controls.Add(Tema.Boton("Quitar marca", Tema.Suave, (s, e) => Corregir("falta")));
            abajo.Controls.Add(Tema.Boton("Salida", Tema.Azul, (s, e) => Corregir("salida")));
            abajo.Controls.Add(Tema.Boton("Quitar salida", Tema.Suave, (s, e) => Corregir("sin_salida")));
            hoy.Controls.Add(_grid);
            hoy.Controls.Add(abajo);
            hoy.Controls.Add(_lblResumen);
            hoy.Controls.Add(arriba);

            var alertas = new TabPage("Alertas") { BackColor = Tema.Papel, Padding = new Padding(6) };
            _lstAlertas.Dock = DockStyle.Fill;
            _lstAlertas.View = View.Details;
            _lstAlertas.FullRowSelect = true;
            _lstAlertas.Columns.Add("Hora", 80);
            _lstAlertas.Columns.Add("Origen", 80);
            _lstAlertas.Columns.Add("Mensaje", 600);
            var abajo2 = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40 };
            abajo2.Controls.Add(Tema.Boton("Marcar todas como vistas", Tema.Azul, (s, e) => { _db.AtenderTodos(); }));
            abajo2.Controls.Add(Tema.Boton("Ver fotos de desconocidos", Tema.Suave, (s, e) =>
            {
                string c = Path.Combine(_cfg.CarpetaDatos, "desconocidos");
                Directory.CreateDirectory(c);
                Abrir(c);
            }));
            alertas.Controls.Add(_lstAlertas);
            alertas.Controls.Add(abajo2);
            _pestanas.TabPages.Add(hoy);
            _pestanas.TabPages.Add(alertas);
            return _pestanas;
        }

        // ───────────────────────── arranque y cierre ─────────────────────────
        async void AlCargar(object sender, EventArgs e)
        {
            CargarMaterias();
            _timer.Start();
            try
            {
                await _web.IniciarAsync();
                _lblServidor.Text = $"Celulares (mismo WiFi): http://{Red.IpsLocales()[0]}:{_cfg.Puerto}   ·   Panel web: entra con tu PIN";
            }
            catch (Exception ex)
            {
                _lblServidor.Text = "Servidor web NO iniciado";
                MessageBox.Show($"No se pudo abrir el puerto {_cfg.Puerto} para los celulares.\n\n{ex.Message}\n\n¿Hay otra copia abierta? Cambia \"puerto\" en config.json.",
                    "Servidor web", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (_usuario.EsAdmin && (_cmbMateria.Items.Count == 0 || _db.Estudiantes().Count == 0))
                MessageBox.Show("Para empezar:\n\n1. «Académico»: crea la gestión, los docentes (con su PIN) y las materias de cada semestre con su horario.\n" +
                                "2. «Estudiantes»: agrégalos (o importa la lista CSV) e indica su semestre.\n" +
                                "3. Selecciona a cada estudiante → «Registrar rostro» y que mire a la cámara.\n" +
                                "4. ¡Listo! Al llegar, la cámara marca su asistencia en la materia que tiene clase en ese momento.",
                    "Primeros pasos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (!_usuario.EsAdmin && _cmbMateria.Items.Count == 0)
                MessageBox.Show("No tienes materias asignadas en la gestión activa. Pide al administrador que te asigne en «Académico».",
                    "Sin materias", MessageBoxButtons.OK, MessageBoxIcon.Information);
            var faltan = ModelosIA.Problemas(_cfg);
            if (faltan.Count > 0) OfrecerDescarga("Faltan las redes neuronales del reconocimiento facial:\n\n• " + string.Join("\n• ", faltan));
            else if (_cfg.Reconocimiento.IniciarAutomatico) AlternarCamara();
        }

        void AlCerrar(object sender, FormClosingEventArgs e)
        {
            _timer.Stop();
            _motor.FrameListo -= MostrarFrame;
            _motor.Dispose();
            Task.Run(() => _web.DetenerAsync()).Wait(3000);
            _bandeja.Visible = false;
            _bandeja.Dispose();
        }

        // ───────────────────────── cámara ─────────────────────────
        void AlternarCamara()
        {
            if (_motor.Activo)
            {
                _motor.Detener();
                _btnCamara.Text = "▶ Iniciar cámara";
                _btnCamara.BackColor = Tema.Verde;
                _lblCamara.Text = "Cámara: detenida";
                return;
            }
            string fuente = _cmbFuente.Text.Trim();
            if (fuente.Length == 0) fuente = "0";
            if (fuente != _cfg.Reconocimiento.Fuente && !Directory.Exists(fuente))
            {
                _cfg.Reconocimiento.Fuente = fuente;
                try { _cfg.Guardar(); } catch (IOException) { }
            }
            _motor.Iniciar(fuente);
            _btnCamara.Text = "■ Detener cámara";
            _btnCamara.BackColor = Tema.Rojo;
        }

        void MostrarFrame(OpenCvSharp.Mat mat)
        {
            if (Interlocked.Exchange(ref _frameEnCamino, 1) == 1 || IsDisposed)
            {
                mat.Dispose();
                return;
            }
            Bitmap bmp;
            try { bmp = mat.ToBitmap(); }
            finally { mat.Dispose(); }
            try
            {
                BeginInvoke(new Action(() =>
                {
                    var anterior = _picCamara.Image;
                    _picCamara.Image = bmp;
                    anterior?.Dispose();
                    Interlocked.Exchange(ref _frameEnCamino, 0);
                }));
            }
            catch (InvalidOperationException)
            {
                bmp.Dispose();
                Interlocked.Exchange(ref _frameEnCamino, 0);
            }
        }

        /// <summary>Tarjeta grande con foto, nombre y hora al reconocer a un estudiante.</summary>
        void Bienvenida(ResultadoMarca r, byte[] foto)
        {
            var f = r.Fila;
            string hora = f.Hora.HasValue ? BaseDatos.Local(f.Hora.Value).ToString("HH:mm") : DateTime.Now.ToString("HH:mm");
            _lblNombre.Text = f.Nombre;
            if (r.Motivo.Length == 0 && _chkSeguir.Checked) Seleccionar(f.MateriaId);
            if (r.Motivo.Length > 0) { _lblDetalle.Text = $"No se marcó: {r.Motivo}"; _bienvenida.BackColor = Tema.Suave; }
            else if (r.Nueva && r.Tipo == "salida")
            {
                _lblDetalle.Text = $"¡Hasta luego!  Salida {BaseDatos.Local(f.HoraSalida.Value):HH:mm}  ·  {f.Materia}";
                _bienvenida.BackColor = Tema.Azul;
                Sonido.Tocar("puntual");
            }
            else if (!r.Nueva) { _lblDetalle.Text = $"Ya registraste {f.Materia} hoy a las {hora} ({Tema.TextoEstado(f.Estado)})"; _bienvenida.BackColor = Tema.Azul; }
            else
            {
                _lblDetalle.Text = f.Estado == "puntual" ? $"¡Bienvenido/a!  {hora}  ·  {f.Materia}  ·  PUNTUAL" : $"{hora}  ·  {f.Materia}  ·  LLEGASTE CON ATRASO";
                _bienvenida.BackColor = Tema.ColorEstado(f.Estado);
                Sonido.Tocar(f.Estado);
            }
            var anterior = _picFoto.Image;
            _picFoto.Image = Tema.ImagenDeBytes(foto);
            anterior?.Dispose();
            _bienvenida.Visible = true;
            _ocultarBienvenida = DateTime.Now.AddSeconds(6);
            _firma = "";
            Refrescar();
        }

        void Configurar()
        {
            using var f = new FormConfiguracion(_cfg);
            if (f.ShowDialog(this) != DialogResult.OK) return;
            BotonesSegunUsuario();
            _firma = "";
            Refrescar();
        }

        // ───────────────────────── materias ─────────────────────────
        Materia MateriaElegida => _cmbMateria.SelectedItem as Materia;

        /// <summary>Recarga las materias que puede ver el usuario y elige la que tiene clase ahora.</summary>
        void CargarMaterias()
        {
            long? antes = MateriaElegida?.Id;
            _cmbMateria.Items.Clear();
            foreach (var m in _db.MateriasDe(_usuario)) _cmbMateria.Items.Add(m);
            _lblTitulo.Text = $"{_usuario.Nombre} · Gestión {_db.GestionActiva()?.Nombre ?? "(sin gestión activa)"}\n{_cfg.Institucion}";
            if (!(_chkSeguir.Checked && SeguirClase()) && !(antes.HasValue && Seleccionar(antes.Value)) && _cmbMateria.Items.Count > 0)
                _cmbMateria.SelectedIndex = 0;
            _firma = "";
            Refrescar();
        }

        bool Seleccionar(long materiaId)
        {
            for (int i = 0; i < _cmbMateria.Items.Count; i++)
                if (((Materia)_cmbMateria.Items[i]).Id == materiaId)
                {
                    if (_cmbMateria.SelectedIndex != i) { _cmbMateria.SelectedIndex = i; _firma = ""; }
                    return true;
                }
            return false;
        }

        /// <summary>Elige la primera clase en curso que el usuario puede ver. true si había alguna.</summary>
        bool SeguirClase()
        {
            try
            {
                foreach (var c in _db.ClasesEnCurso(DateTime.Now, _cfg.Horario))
                    if (Seleccionar(c.Materia.Id)) return true;
            }
            catch (Exception) { /* base de datos ocupada: se intenta en el próximo ciclo */ }
            return false;
        }

        // ───────────────────────── refresco ─────────────────────────
        string FechaElegida => _fecha.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        void Refrescar()
        {
            if (DateTime.Now > _ocultarBienvenida) { _bienvenida.Visible = false; _ocultarBienvenida = DateTime.MaxValue; }
            if (DateTime.Now > _ocultarAlerta) { _panelAlerta.Visible = false; _ocultarAlerta = DateTime.MaxValue; }
            if (++_ticks % 20 == 0 && _chkSeguir.Checked) SeguirClase();      // cada ~20 s: ¿empezó otra clase?
            var materia = MateriaElegida;
            EstadoPanel est;
            try { est = _db.EstadoAdmin(_primera ? 0 : _ultimoEvento, materia?.Id ?? 0, FechaElegida); }
            catch (Exception ex) { _lblResumen.Text = "Error de base de datos: " + ex.Message; return; }
            var r = est.Resumen;
            _lblResumen.Text = materia == null
                ? (_usuario.EsAdmin ? "No hay materias en la gestión activa: créalas en «Académico»." : "No tienes materias asignadas en la gestión activa.")
                : $"{materia.Docente}   ·   {materia.TextoHorario}\n" +
                  $"Presentes {r.Presentes}/{r.Total}   ·   Puntuales {r.Puntuales}   ·   Atrasos {r.Atrasos}   ·   Justificados {r.Justificados}   ·   Faltan {r.Faltan}";

            string firma = string.Join("|", est.Lista.Select(x => $"{x.EstudianteId}{x.Estado}{x.Hora}{x.HoraSalida}{x.Observacion}"));
            if (firma != _firma)
            {
                _firma = firma;
                long? seleccionado = _grid.SelectedRows.Count > 0 ? ((FilaAsistencia)_grid.SelectedRows[0].Tag).EstudianteId : (long?)null;
                _grid.Rows.Clear();
                foreach (var x in est.Lista)
                {
                    int i = _grid.Rows.Add(x.Codigo, x.Nombre + (x.Muestras == 0 ? "   (sin rostro)" : ""), Tema.TextoEstado(x.Estado),
                        x.Hora.HasValue ? Tema.Hora(x.Hora.Value) : "—",
                        x.HoraSalida.HasValue ? Tema.Hora(x.HoraSalida.Value) : x.Hora.HasValue && x.Estado != "justificado" ? "sin salida" : "", x.Similitud > 0 ? x.Similitud.ToString("0.00") : "", x.Fuente, x.Observacion);
                    var fila = _grid.Rows[i];
                    fila.Tag = x;
                    fila.Cells["estado"].Style.BackColor = Tema.ColorEstado(x.Estado);
                    fila.Cells["estado"].Style.ForeColor = Color.White;
                    fila.Cells["estado"].Style.Font = Tema.Negrita;
                    if (x.Muestras == 0) fila.Cells["nombre"].Style.ForeColor = Tema.Suave;
                    if (x.Hora.HasValue && !x.HoraSalida.HasValue) fila.Cells["salida"].Style.ForeColor = Tema.Naranja;
                    if (seleccionado == x.EstudianteId) fila.Selected = true;
                }
            }

            _lstAlertas.BeginUpdate();
            foreach (var ev in est.Eventos)
            {
                var item = new ListViewItem(new[] { Tema.Hora(ev.Creado), ev.Fuente, ev.Mensaje });
                if (ev.Prioridad == "alta") item.ForeColor = Tema.Rojo;
                _lstAlertas.Items.Insert(0, item);
                if (!_primera && ev.Prioridad == "alta") Alertar(ev);
            }
            while (_lstAlertas.Items.Count > 300) _lstAlertas.Items.RemoveAt(_lstAlertas.Items.Count - 1);
            _lstAlertas.EndUpdate();
            _ultimoEvento = est.UltimoEvento;
            _primera = false;

            if (!_motor.Activo && _btnCamara.Text.StartsWith("■"))
            {
                _btnCamara.Text = "▶ Iniciar cámara";
                _btnCamara.BackColor = Tema.Verde;
            }
            else if (_motor.Activo)
                _lblCamara.Text = $"Cámara: {(_motor.Conectada ? "activa" : "esperando imagen")} · {_motor.EstudiantesEnGaleria} rostros registrados · {_motor.Fps:0.0} fps";
        }

        void Alertar(Evento ev)
        {
            Sonido.Tocar("alerta");
            _lblAlerta.Text = "  " + ev.Mensaje + "     (clic para cerrar)";
            _panelAlerta.Visible = true;
            _ocultarAlerta = DateTime.Now.AddSeconds(10);
            if (WindowState == FormWindowState.Minimized || !ContainsFocus)
                _bandeja.ShowBalloonTip(5000, "Asistencia", ev.Mensaje, ToolTipIcon.Warning);
        }

        void Corregir(string estado)
        {
            if (MateriaElegida == null) { MessageBox.Show("Elige una materia.", "Corregir"); return; }
            if (_grid.SelectedRows.Count == 0) { MessageBox.Show("Selecciona un estudiante de la lista.", "Corregir"); return; }
            var x = (FilaAsistencia)_grid.SelectedRows[0].Tag;
            string obs = "";
            if (estado == "justificado")
            {
                obs = Preguntar($"Motivo de la justificación de {x.Nombre}:", "Justificar");
                if (obs == null) return;
            }
            if (estado == "sin_salida" && MessageBox.Show($"¿Quitar la salida de {x.Nombre}?", "Quitar salida", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            if (estado == "falta" && MessageBox.Show($"¿Quitar la marca de {x.Nombre} en {MateriaElegida.Titulo} del {FechaElegida}?", "Quitar marca", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                if (estado == "salida" || estado == "sin_salida") _db.SalidaManual(x.EstudianteId, MateriaElegida.Id, FechaElegida, quitar: estado == "sin_salida");
                else _db.MarcaManual(x.EstudianteId, MateriaElegida.Id, FechaElegida, estado, obs);
                _firma = "";
                Refrescar();
            }
            catch (ErrorValidacion ex) { MessageBox.Show(ex.Message, "Corregir", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        /// <summary>Cuadro simple para escribir un texto (null si se cancela).</summary>
        public static string Preguntar(string texto, string titulo, string inicial = "")
        {
            using var f = new Form
            {
                Text = titulo, Width = 440, Height = 170, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, Font = Tema.Normal,
            };
            var l = new Label { Text = texto, Left = 12, Top = 12, Width = 400 };
            var t = new TextBox { Text = inicial, Left = 12, Top = 40, Width = 400 };
            var ok = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, Left = 236, Top = 76, Width = 85 };
            var no = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Left = 327, Top = 76, Width = 85 };
            f.Controls.AddRange(new Control[] { l, t, ok, no });
            f.AcceptButton = ok;
            f.CancelButton = no;
            return f.ShowDialog() == DialogResult.OK ? t.Text : null;
        }

        void Navegador(string ruta)
        {
            try { Process.Start(new ProcessStartInfo($"http://localhost:{_cfg.Puerto}{ruta}") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        public static void Abrir(string ruta)
        {
            try { Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true }); } catch (Exception) { }
        }

        void MostrarFallo(string mensaje)
        {
            _btnCamara.Text = "▶ Iniciar cámara";
            _btnCamara.BackColor = Tema.Verde;
            if (mensaje.Contains("[MODELO]"))
            {
                OfrecerDescarga(mensaje.Replace("[MODELO]", "").Trim());
                return;
            }
            if (MessageBox.Show(mensaje + "\n\n¿Hacer ahora el diagnóstico completo de cámaras?", "La cámara no funciona",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                Diagnosticar();
        }

        bool _descargando;

        /// <summary>Si falta SFace, lo descarga desde la aplicación (37 MB, una sola vez) y enciende la cámara.</summary>
        async void OfrecerDescarga(string motivo)
        {
            if (_descargando) return;
            if (!ModelosIA.FaltaSFace(_cfg))
            {
                MessageBox.Show(motivo + "\n\nVuelve a descargar el proyecto: el modelo YuNet viene incluido en la carpeta «modelos».",
                    "Faltan archivos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(motivo + "\n\n¿Descargar ahora el modelo SFace (37 MB)? Necesita internet una sola vez.\n\n" +
                                "Sin internet: copia sface.parte1 y sface.parte2 y ejecuta unir_modelo_sface.bat.",
                    "Falta el modelo de reconocimiento", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _descargando = true;
            _btnCamara.Enabled = false;
            try
            {
                var avance = new Progress<int>(p => _lblCamara.Text = $"Descargando modelo SFace… {p} %");
                await ModelosIA.DescargarSFaceAsync(_cfg, avance);
                _lblCamara.Text = "Modelo SFace descargado. Iniciando cámara…";
                _btnCamara.Enabled = true;
                if (!_motor.Activo) AlternarCamara();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo descargar el modelo:\n" + ex.Message + "\n\nAlternativa: sface.parte1 + sface.parte2 → unir_modelo_sface.bat",
                    "Descarga", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _descargando = false;
                _btnCamara.Enabled = true;
            }
        }

        /// <summary>Prueba todas las cámaras con todos los motores y muestra un informe que se puede copiar.</summary>
        async void Diagnosticar()
        {
            if (_motor.Activo) AlternarCamara();                 // la cámara debe estar libre para probarla
            Cursor = Cursors.WaitCursor;
            _lblCamara.Text = "Cámara: diagnosticando… (unos segundos)";
            string informe;
            try { informe = await System.Threading.Tasks.Task.Run(() => DiagnosticoCamara.Informe(_cfg)); }
            catch (Exception ex) { informe = "Error durante el diagnóstico: " + ex; }
            finally { Cursor = Cursors.Default; }
            _lblCamara.Text = "Cámara: diagnóstico terminado";
            using var f = new Form
            {
                Text = "Diagnóstico de cámara", Size = new Size(760, 560), StartPosition = FormStartPosition.CenterParent,
                Font = Tema.Normal, BackColor = Tema.Fondo,
            };
            var txt = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false,
                Font = new Font("Consolas", 10f), Text = informe.Replace("\n", Environment.NewLine), BackColor = Color.White,
            };
            var abajo = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
            abajo.Controls.Add(Tema.Boton("Cerrar", Tema.Suave, (s, e) => f.Close()));
            abajo.Controls.Add(Tema.Boton("Copiar informe", Tema.Azul, (s, e) => { Clipboard.SetText(txt.Text); MessageBox.Show("Copiado. Puedes pegarlo para pedir ayuda."); }));
            if (informe.Contains(DiagnosticoCamara.EnlaceVcRedist))
                abajo.Controls.Add(Tema.Boton("Descargar Visual C++", Tema.Naranja, (s, e) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DiagnosticoCamara.EnlaceVcRedist) { UseShellExecute = true }); }
                    catch (Exception) { }
                }));
            f.Controls.Add(txt);
            f.Controls.Add(abajo);
            f.ShowDialog(this);
        }

        void EnHilo(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch (InvalidOperationException) { }
        }
    }
}
