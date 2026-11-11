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
        readonly ComboBox _cmbMateria = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340, Margin = new Padding(4, 5, 4, 3) };
        readonly CheckBox _chkSeguir = new CheckBox { Text = "Seguir la clase en curso", AutoSize = true, Checked = true, Margin = new Padding(12, 9, 0, 0), ForeColor = Tema.Tinta, BackColor = Color.Transparent, Cursor = Cursors.Hand };
        int _ticks;

        // ── estructura: barra lateral + título de página + contenido ──
        readonly BarraLateral _lateral = new BarraLateral();
        readonly Cabecera _cabecera = new Cabecera { EsPagina = true };
        readonly Panel _contenido = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Fondo, Padding = new Padding(26, 0, 26, 22) };
        Control _paginaPanel, _paginaAlertas;
        Form _incrustada;
        Action _alSalirDePagina;
        string _pagina = "";
        int _alertasNuevas;

        readonly Insignia _lblServidor = new Insignia { Relleno = Tema.Papel, ForeColor = Tema.Marino, Punto = Tema.Gris },
            _lblCamara = new Insignia { Relleno = Tema.Papel, ForeColor = Tema.Marino, Punto = Tema.Gris };
        readonly Label _lblResumen = new Label(), _lblAlerta = new Label();
        readonly Panel _panelAlerta = new Panel();
        readonly Tarjeta _bienvenida = new Tarjeta { ConSombra = false };
        readonly PictureBox _picCamara = new PictureBox(), _picFoto = new PictureBox();
        readonly Label _lblNombre = new Label(), _lblDetalle = new Label();
        readonly Insignia _enVivo = new Insignia { Text = "DETENIDA", Anchor = AnchorStyles.Top | AnchorStyles.Right, Punto = Color.White, Relleno = Color.FromArgb(40, 62, 100) };
        readonly Indicador _kPresentes = new Indicador { Titulo = "Presentes", Acento = Tema.MarinoMedio, Icono = Tema.Icono.Estudiantes },
            _kPuntuales = new Indicador { Titulo = "Puntuales", Acento = Tema.Verde, Icono = Tema.Icono.Puntual },
            _kAtrasos = new Indicador { Titulo = "Atrasos", Acento = Tema.Ambar, Icono = Tema.Icono.Reloj },
            _kJustificados = new Indicador { Titulo = "Justificados", Acento = Tema.Celeste, Icono = Tema.Icono.Documento },
            _kFaltan = new Indicador { Titulo = "Faltan", Acento = Tema.Gris, Icono = Tema.Icono.Aviso, Margin = Padding.Empty };
        readonly ComboBox _cmbFuente = new ComboBox();
        readonly BotonModerno _btnCamara;
        readonly DateTimePicker _fecha = new DateTimePicker();
        readonly DataGridView _grid = new DataGridView();
        readonly ListView _lstAlertas = new ListView();
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
            MinimumSize = new Size(1180, 720);
            Size = new Size(1366, 800);
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            _btnCamara = Tema.Boton("▶  Iniciar cámara", Tema.Verde, (s, e) => AlternarCamara());

            _paginaPanel = ConstruirPanel();
            _paginaAlertas = ConstruirAlertas();
            _contenido.Controls.Add(_paginaPanel);
            _contenido.Controls.Add(_paginaAlertas);
            _cabecera.Derecha.Controls.Add(_lblServidor);
            _cabecera.Derecha.Controls.Add(_lblCamara);
            _cabecera.Derecha.Padding = new Padding(0, 30, 26, 0);
            _cabecera.Padding = new Padding(26, 0, 0, 0);
            _lblServidor.Text = "Servidor web: iniciando…";
            _lblCamara.Text = "Cámara detenida";

            var derecha = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Fondo };
            derecha.Controls.Add(_contenido);
            derecha.Controls.Add(ConstruirAlerta());
            derecha.Controls.Add(_cabecera);
            Controls.Add(derecha);
            Controls.Add(_lateral);
            _lateral.Elegida += Navegar;
            OpcionesSegunUsuario();
            Navegar("panel");
            Tema.Escalar(this);
            MinimumSize = new Size(Math.Min(MinimumSize.Width, Screen.PrimaryScreen.WorkingArea.Width), Math.Min(MinimumSize.Height, Screen.PrimaryScreen.WorkingArea.Height));

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

        // ───────────────────────── navegación ─────────────────────────
        /// <summary>El administrador ve todo; un docente, solo su panel, alertas, reportes y el cartel QR.</summary>
        void OpcionesSegunUsuario()
        {
            _lateral.Limpiar();
            _lateral.Agregar("panel", Tema.Icono.Panel, "Panel del día", "Menú");
            _lateral.Agregar("alertas", Tema.Icono.Alertas, "Alertas", "Menú");
            if (_usuario.EsAdmin)
            {
                _lateral.Agregar("estudiantes", Tema.Icono.Estudiantes, "Estudiantes", "Menú");
                _lateral.Agregar("academico", Tema.Icono.Academico, "Académico", "Menú");
            }
            _lateral.Agregar("reportes", Tema.Icono.Reportes, "Reportes", "Menú");
            _lateral.Agregar("qr", Tema.Icono.Qr, "Cartel QR", "Menú");
            if (_usuario.EsAdmin) _lateral.Agregar("config", Tema.Icono.Configuracion, "Configuración", "Menú");
            _lateral.Agregar("web", Tema.Icono.Web, "Panel web", "Accesos");
            _lateral.Agregar("publica", Tema.Icono.Publica, "Vista pública", "Accesos");
            _lateral.Agregar("sonido", Sonido.Activo ? Tema.Icono.Sonido : Tema.Icono.Silencio, Sonido.Activo ? "Sonido activado" : "Sonido apagado", abajo: true);
            _lateral.Agregar("usuario", Tema.Icono.Usuario, "Cambiar usuario", abajo: true);
            _lateral.Contador("alertas", _alertasNuevas);
            ActualizarTitulo();
        }

        void ActualizarTitulo()
        {
            _lateral.Usuario(_usuario.Nombre, _usuario.EsAdmin ? "Administrador" : "Docente", _db.GestionActiva()?.Nombre ?? "");
            if (_pagina == "panel") TituloPagina("Panel del día", null);
        }

        void TituloPagina(string titulo, string subtitulo)
        {
            _cabecera.Titulo = titulo;
            _cabecera.Subtitulo = subtitulo ?? $"{DateTime.Now.ToString("dddd d 'de' MMMM", new System.Globalization.CultureInfo("es-BO"))}  ·  {_cfg.Institucion}";
        }

        void Navegar(string clave)
        {
            switch (clave)
            {
                case "web": Navegador("/admin"); return;
                case "publica": Navegador("/"); return;
                case "usuario": CambiarUsuario(); return;
                case "sonido":
                    Sonido.Activo = !Sonido.Activo;
                    _lateral.Cambiar("sonido", Sonido.Activo ? Tema.Icono.Sonido : Tema.Icono.Silencio, Sonido.Activo ? "Sonido activado" : "Sonido apagado");
                    return;
            }
            if (clave == _pagina && _incrustada == null) return;
            CerrarIncrustada();
            _pagina = clave;
            _lateral.Activa = clave;
            _paginaPanel.Visible = clave == "panel";
            _paginaAlertas.Visible = clave == "alertas";
            switch (clave)
            {
                case "panel": TituloPagina("Panel del día", null); break;
                case "alertas":
                    TituloPagina("Alertas y movimientos", "Rostros desconocidos, marcas y avisos del sistema en tiempo real");
                    _alertasNuevas = 0;
                    _lateral.Contador("alertas", 0);
                    break;
                case "estudiantes": Incrustar(new FormEstudiantes(_cfg, _db, _motor), () => { _firma = ""; Refrescar(); }); break;
                case "academico": Incrustar(new FormAcademico(_db), CargarMaterias); break;
                case "reportes": Incrustar(new FormReportes(_db, _usuario, MateriaElegida), null); break;
                case "qr": Incrustar(new FormQR(_cfg), null); break;
                case "config":
                    var fc = new FormConfiguracion(_cfg);
                    Incrustar(fc, () => { if (fc.DialogResult == DialogResult.OK) { OpcionesSegunUsuario(); _firma = ""; Refrescar(); } });
                    break;
            }
        }

        /// <summary>
        /// Muestra una ventana (Estudiantes, Académico, Reportes…) DENTRO del área de contenido, como una página más.
        /// Su encabezado pasa a ser el título de la página. Al cerrarse (Guardar/Cancelar) vuelve al panel.
        /// </summary>
        void Incrustar(Form f, Action alSalir)
        {
            var cab = f.Controls.OfType<Cabecera>().FirstOrDefault();
            TituloPagina(cab?.Titulo ?? f.Text, cab?.Subtitulo ?? "");
            if (cab != null) cab.Visible = false;
            f.TopLevel = false;
            f.FormBorderStyle = FormBorderStyle.None;
            f.Dock = DockStyle.Fill;
            f.BackColor = Tema.Fondo;
            _incrustada = f;
            _alSalirDePagina = alSalir;
            f.FormClosed += (s, e) => { if (_incrustada == f) BeginInvoke(new Action(() => Navegar("panel"))); };
            _contenido.Controls.Add(f);
            f.Show();
            f.BringToFront();
        }

        void CerrarIncrustada()
        {
            var f = _incrustada;
            _incrustada = null;
            if (f != null)
            {
                _contenido.Controls.Remove(f);
                if (!f.IsDisposed) f.Dispose();
            }
            var accion = _alSalirDePagina;
            _alSalirDePagina = null;
            accion?.Invoke();
        }

        void CambiarUsuario()
        {
            var u = FormIngreso.Pedir(_cfg, _db, this);
            if (u == null) return;
            _usuario = u;
            OpcionesSegunUsuario();
            Navegar("panel");
            CargarMaterias();
        }

        Control ConstruirAlerta()
        {
            _panelAlerta.Dock = DockStyle.Top;
            _panelAlerta.Height = 46;
            _panelAlerta.BackColor = Tema.Rojo;
            _panelAlerta.Visible = false;
            _panelAlerta.Cursor = Cursors.Hand;
            _lblAlerta.Dock = DockStyle.Fill;
            _lblAlerta.Font = Tema.Seccion;
            _lblAlerta.ForeColor = Color.White;
            _lblAlerta.TextAlign = ContentAlignment.MiddleLeft;
            _lblAlerta.Padding = new Padding(26, 0, 0, 0);
            _lblAlerta.Click += (s, e) => { _panelAlerta.Visible = false; Navegar("alertas"); };
            _panelAlerta.Controls.Add(_lblAlerta);
            return _panelAlerta;
        }

        // ───────────────────────── página «Panel del día» ─────────────────────────
        Control ConstruirPanel()
        {
            var pagina = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Fondo };

            // cifras del día: baldosas de color
            var kpis = new TableLayoutPanel { Dock = DockStyle.Top, Height = 112, ColumnCount = 5, RowCount = 1, Padding = new Padding(0, 0, 0, 16), BackColor = Tema.Fondo };
            for (int i = 0; i < 5; i++) kpis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            kpis.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            kpis.Controls.Add(_kPresentes, 0, 0);
            kpis.Controls.Add(_kPuntuales, 1, 0);
            kpis.Controls.Add(_kAtrasos, 2, 0);
            kpis.Controls.Add(_kJustificados, 3, 0);
            kpis.Controls.Add(_kFaltan, 4, 0);

            // lista a la izquierda, visor de la cámara a la derecha
            var division = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 14, BackColor = Tema.Fondo };
            division.Panel1.BackColor = division.Panel2.BackColor = Tema.Fondo;
            division.Panel1.Controls.Add(ConstruirLista());
            division.Panel2.Controls.Add(ConstruirCamara());
            Shown += (s, e) =>
            {
                try { division.SplitterDistance = (int)(division.Width * 0.6); } catch (InvalidOperationException) { }
            };
            pagina.Controls.Add(division);
            pagina.Controls.Add(kpis);
            return pagina;
        }

        Control ConstruirCamara()
        {
            // tarjeta oscura: la imagen de la cámara manda
            var tarjeta = new Tarjeta { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 20, 16), Relleno = Tema.Camara };

            var cab = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.Transparent };
            cab.Controls.Add(new Label { Text = "Cámara en vivo", Font = Tema.Seccion, ForeColor = Color.White, AutoSize = true, Location = new Point(2, 6), BackColor = Color.Transparent });
            cab.Controls.Add(_enVivo);
            cab.Resize += (s, e) => _enVivo.Location = new Point(cab.Width - _enVivo.Width, 6);

            _picCamara.Dock = DockStyle.Fill;
            _picCamara.SizeMode = PictureBoxSizeMode.Zoom;
            _picCamara.BackColor = Color.FromArgb(3, 9, 20);
            _picCamara.Paint += (s, e) =>
            {
                if (_picCamara.Image != null) return;
                var r = _picCamara.ClientRectangle;
                int lado = Math.Min(72, r.Height / 4);
                Tema.DibujarLogo(e.Graphics, new RectangleF(r.Width / 2f - lado / 2f, r.Height / 2f - lado - 30, lado, lado));
                TextRenderer.DrawText(e.Graphics, _motor.Activo ? "Esperando imagen de la cámara…" : "Cámara detenida", Tema.Seccion,
                    new Rectangle(0, r.Height / 2 - 10, r.Width, Tema.Seccion.Height + 6), Color.White, TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(e.Graphics, _motor.Activo ? "" : "Presiona «Iniciar cámara» para comenzar a marcar asistencia", Tema.Normal,
                    new Rectangle(10, r.Height / 2 + Tema.Seccion.Height + 4, r.Width - 20, Tema.Normal.Height * 2 + 4), Tema.MarinoNiebla, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            };

            // tarjeta de bienvenida: aparece unos segundos al marcar asistencia
            _bienvenida.Dock = DockStyle.Bottom;
            _bienvenida.Height = 118;
            _bienvenida.Relleno = Tema.Verde;
            _bienvenida.Radio = 16;
            _bienvenida.Padding = new Padding(14);
            _bienvenida.Visible = false;
            _picFoto.Dock = DockStyle.Left;
            _picFoto.Width = 90;
            _picFoto.SizeMode = PictureBoxSizeMode.Zoom;
            _picFoto.BackColor = Color.White;
            _picFoto.SizeChanged += (s, e) =>
            {
                using var p = new System.Drawing.Drawing2D.GraphicsPath();
                p.AddEllipse(0, 0, _picFoto.Width, _picFoto.Width);
                _picFoto.Region = new Region(p);
            };
            _lblNombre.Dock = DockStyle.Top;
            _lblNombre.Height = 46;
            _lblNombre.Font = Tema.Grande;
            _lblNombre.ForeColor = Color.White;
            _lblNombre.BackColor = Color.Transparent;
            _lblNombre.AutoEllipsis = true;
            _lblNombre.Padding = new Padding(14, 4, 0, 0);
            _lblDetalle.Dock = DockStyle.Fill;
            _lblDetalle.Font = Tema.Semi;
            _lblDetalle.ForeColor = Color.White;
            _lblDetalle.BackColor = Color.Transparent;
            _lblDetalle.Padding = new Padding(16, 2, 0, 0);
            _bienvenida.Controls.Add(_lblDetalle);
            _bienvenida.Controls.Add(_lblNombre);
            _bienvenida.Controls.Add(_picFoto);
            var espacio = new Panel { Dock = DockStyle.Bottom, Height = 12, BackColor = Color.Transparent };
            _bienvenida.VisibleChanged += (s, e) => espacio.Visible = _bienvenida.Visible;
            espacio.Visible = false;

            var controles = Tema.Fila(DockStyle.Bottom, new Padding(0, 14, 0, 0));
            _cmbFuente.DropDownStyle = ComboBoxStyle.DropDown;
            _cmbFuente.Items.AddRange(new object[] { "0", "1", "2" });
            _cmbFuente.Text = _cfg.Reconocimiento.Fuente;
            _cmbFuente.Width = 150;
            _cmbFuente.Margin = new Padding(4, 7, 8, 3);
            Tema.EstiloCampo(_cmbFuente);
            controles.Controls.Add(Tema.Etiqueta("Fuente", Tema.Semi, Tema.MarinoNiebla));
            controles.Controls.Add(_cmbFuente);
            controles.Controls.Add(_btnCamara);
            controles.Controls.Add(Tema.BotonBarra("Probar con fotos…", (s, e) =>
            {
                using var d = new FolderBrowserDialog { Description = "Carpeta con fotos de rostros (se mostrarán como si fueran la cámara)" };
                if (d.ShowDialog(this) != DialogResult.OK) return;
                _cmbFuente.Text = d.SelectedPath;
                if (_motor.Activo) AlternarCamara();
                AlternarCamara();
            }));
            controles.Controls.Add(Tema.BotonBarra("Diagnóstico", (s, e) => Diagnosticar()));
            var ayuda = new Label
            {
                Dock = DockStyle.Bottom, Height = 46, ForeColor = Tema.MarinoNiebla, Font = Tema.Chica, BackColor = Color.Transparent, Padding = new Padding(4, 10, 0, 0),
                Text = "0 = webcam · 1 = cámara USB · URL = celular (cámara IP) · video .mp4 o carpeta con fotos.\n" +
                       "Recuadro verde = marcado · amarillo = confirmando o atraso · rojo = rostro no registrado.",
            };
            tarjeta.Controls.Add(_picCamara);
            tarjeta.Controls.Add(espacio);
            tarjeta.Controls.Add(_bienvenida);
            tarjeta.Controls.Add(controles);
            tarjeta.Controls.Add(ayuda);
            tarjeta.Controls.Add(cab);
            return tarjeta;
        }

        Control ConstruirLista()
        {
            var tarjeta = new Tarjeta { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 22, 16) };

            var titulo = new Label { Dock = DockStyle.Top, Height = 36, Text = "Lista de la clase", Font = Tema.Seccion, ForeColor = Tema.MarinoProfundo, BackColor = Color.Transparent };
            var arriba = Tema.Fila(DockStyle.Top, new Padding(0, 2, 0, 6));
            _fecha.Format = DateTimePickerFormat.Short;
            _fecha.Width = 130;
            _fecha.Margin = new Padding(4, 5, 4, 3);
            _fecha.Font = Tema.Campo;
            _fecha.ValueChanged += (s, e) => { _firma = ""; Refrescar(); };
            Tema.EstiloCampo(_cmbMateria);
            _cmbMateria.SelectionChangeCommitted += (s, e) => { _chkSeguir.Checked = false; _firma = ""; Refrescar(); };
            _cmbMateria.SelectedIndexChanged += (s, e) => { _firma = ""; };
            _chkSeguir.CheckedChanged += (s, e) => { if (_chkSeguir.Checked) SeguirClase(); };
            arriba.Controls.Add(Tema.Etiqueta("Materia", Tema.Semi, Tema.Suave));
            arriba.Controls.Add(_cmbMateria);
            arriba.Controls.Add(_chkSeguir);
            arriba.SetFlowBreak(_chkSeguir, true);
            arriba.Controls.Add(Tema.Etiqueta("Fecha", Tema.Semi, Tema.Suave, new Padding(4, 10, 16, 0)));
            arriba.Controls.Add(_fecha);
            arriba.Controls.Add(Tema.Boton("Hoy", Tema.Suave, (s, e) => _fecha.Value = DateTime.Today));

            _lblResumen.Dock = DockStyle.Top;
            _lblResumen.Height = 38;
            _lblResumen.Font = Tema.Semi;
            _lblResumen.ForeColor = Tema.NaranjaOscuro;
            _lblResumen.BackColor = Tema.NaranjaBruma;
            _lblResumen.TextAlign = ContentAlignment.MiddleLeft;
            _lblResumen.AutoEllipsis = true;
            _lblResumen.Padding = new Padding(12, 0, 0, 0);
            var separacion = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Color.Transparent };

            Tema.EstiloGrilla(_grid);
            _grid.Columns.Add("codigo", "Código");
            _grid.Columns.Add("nombre", "Estudiante");
            _grid.Columns.Add("estado", "Estado");
            _grid.Columns.Add("hora", "Entrada");
            _grid.Columns.Add("salida", "Salida");
            _grid.Columns.Add("similitud", "Simil.");
            _grid.Columns.Add("fuente", "Origen");
            _grid.Columns.Add("obs", "Observación");
            foreach (var (n, peso, minimo) in new[] { ("codigo", 70, 74), ("nombre", 240, 150), ("estado", 90, 104), ("hora", 56, 56), ("salida", 60, 60),
                         ("similitud", 50, 46), ("fuente", 55, 50), ("obs", 80, 40) })
            {
                var col = _grid.Columns[n];
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                col.FillWeight = peso;
                col.MinimumWidth = minimo;
            }
            _grid.Columns["codigo"].DefaultCellStyle.ForeColor = Tema.Suave;
            _grid.Columns["nombre"].DefaultCellStyle.Font = Tema.Semi;
            _grid.Columns["similitud"].DefaultCellStyle.ForeColor = Tema.Suave;
            _grid.Columns["fuente"].DefaultCellStyle.ForeColor = Tema.Suave;
            Tema.Avatares(_grid, "nombre", fila => (_grid.Rows[fila].Tag as FilaAsistencia)?.Nombre);
            Tema.Pildoras(_grid, "estado");
            var menu = new ContextMenuStrip { Font = Tema.Normal, ShowImageMargin = false };
            menu.Items.Add("Marcar PUNTUAL", null, (s, e) => Corregir("puntual"));
            menu.Items.Add("Marcar ATRASO", null, (s, e) => Corregir("atraso"));
            menu.Items.Add("JUSTIFICAR falta…", null, (s, e) => Corregir("justificado"));
            menu.Items.Add("Quitar marca (falta)", null, (s, e) => Corregir("falta"));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Marcar SALIDA", null, (s, e) => Corregir("salida"));
            menu.Items.Add("Quitar salida", null, (s, e) => Corregir("sin_salida"));
            _grid.ContextMenuStrip = menu;
            _grid.CellMouseDown += (s, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) _grid.Rows[e.RowIndex].Selected = true; };

            var abajo = Tema.Fila(DockStyle.Bottom, new Padding(0, 12, 0, 0));
            abajo.Controls.Add(Tema.Etiqueta("Corregir:", Tema.Semi, Tema.Suave, new Padding(2, 12, 4, 0)));
            abajo.Controls.Add(Tema.Boton("Puntual", Tema.Verde, (s, e) => Corregir("puntual")));
            abajo.Controls.Add(Tema.Boton("Atraso", Tema.Ambar, (s, e) => Corregir("atraso")));
            abajo.Controls.Add(Tema.Boton("Justificar…", Tema.Celeste, (s, e) => Corregir("justificado")));
            abajo.Controls.Add(Tema.Boton("Salida", Tema.MarinoMedio, (s, e) => Corregir("salida")));
            abajo.Controls.Add(Tema.Boton("Quitar marca", Tema.Suave, (s, e) => Corregir("falta")));
            abajo.Controls.Add(Tema.Boton("Quitar salida", Tema.Suave, (s, e) => Corregir("sin_salida")));
            tarjeta.Controls.Add(_grid);
            tarjeta.Controls.Add(abajo);
            tarjeta.Controls.Add(separacion);
            tarjeta.Controls.Add(_lblResumen);
            tarjeta.Controls.Add(arriba);
            tarjeta.Controls.Add(titulo);
            return tarjeta;
        }

        // ───────────────────────── página «Alertas» ─────────────────────────
        Control ConstruirAlertas()
        {
            var tarjeta = new Tarjeta { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 22, 16), Visible = false };
            _lstAlertas.Dock = DockStyle.Fill;
            _lstAlertas.View = View.Details;
            _lstAlertas.FullRowSelect = true;
            _lstAlertas.BorderStyle = BorderStyle.None;
            _lstAlertas.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _lstAlertas.Font = Tema.Normal;
            _lstAlertas.ForeColor = Tema.Tinta;
            _lstAlertas.OwnerDraw = true;
            _lstAlertas.DrawColumnHeader += (s, e) =>
            {
                e.Graphics.FillRectangle(Brushes.White, e.Bounds);
                using (var p = new Pen(Tema.Linea, 2)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), Tema.Mini, Rectangle.Inflate(e.Bounds, -8, 0), Tema.Tenue,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            _lstAlertas.DrawItem += (s, e) => e.DrawDefault = true;
            _lstAlertas.DrawSubItem += (s, e) => e.DrawDefault = true;
            _lstAlertas.Columns.Add("Hora", 100);
            _lstAlertas.Columns.Add("Origen", 100);
            _lstAlertas.Columns.Add("Mensaje", 620);
            _lstAlertas.Resize += (s, e) => _lstAlertas.Columns[2].Width = Math.Max(300, _lstAlertas.ClientSize.Width - _lstAlertas.Columns[0].Width - _lstAlertas.Columns[1].Width - 4);
            var abajo = Tema.Fila(DockStyle.Bottom, new Padding(0, 12, 0, 0));
            abajo.Controls.Add(Tema.Boton("✓  Marcar todas como vistas", Tema.MarinoMedio, (s, e) => { _db.AtenderTodos(); }));
            abajo.Controls.Add(Tema.Boton("Ver fotos de desconocidos", Tema.Suave, (s, e) =>
            {
                string c = Path.Combine(_cfg.CarpetaDatos, "desconocidos");
                Directory.CreateDirectory(c);
                Abrir(c);
            }));
            tarjeta.Controls.Add(_lstAlertas);
            tarjeta.Controls.Add(abajo);
            return tarjeta;
        }

        // ───────────────────────── arranque y cierre ─────────────────────────
        async void AlCargar(object sender, EventArgs e)
        {
            CargarMaterias();
            _timer.Start();
            try
            {
                await _web.IniciarAsync();
                _lblServidor.Text = $"Celulares: http://{Red.IpsLocales()[0]}:{_cfg.Puerto}";
                _lblServidor.Punto = Tema.Verde;
            }
            catch (Exception ex)
            {
                _lblServidor.Text = "Servidor web NO iniciado";
                _lblServidor.Punto = Tema.Rojo;
                MessageBox.Show($"No se pudo abrir el puerto {_cfg.Puerto} para los celulares.\n\n{ex.Message}\n\n¿Hay otra copia abierta? Cambia \"puerto\" en config.json.",
                    "Servidor web", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (_usuario.EsAdmin && (_cmbMateria.Items.Count == 0 || _db.Estudiantes().Count == 0))
                MessageBox.Show("Para empezar:\n\n1. «Académico»: crea la gestión, los docentes (con su usuario y contraseña) y las materias de cada semestre con su horario.\n" +
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
                EstadoBotonCamara(false);
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
            EstadoBotonCamara(true);
        }

        void EstadoBotonCamara(bool encendida)
        {
            _btnCamara.Text = encendida ? "■  Detener cámara" : "▶  Iniciar cámara";
            _btnCamara.BackColor = encendida ? Tema.Rojo : Tema.Verde;
            _enVivo.Text = encendida ? "EN VIVO" : "DETENIDA";
            _enVivo.Relleno = encendida ? Tema.Rojo : Color.FromArgb(40, 62, 100);
            if (!encendida)
            {
                var anterior = _picCamara.Image;
                _picCamara.Image = null;
                anterior?.Dispose();
            }
            _picCamara.Invalidate();
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
            if (r.Motivo.Length > 0) { _lblDetalle.Text = $"No se marcó: {r.Motivo}"; _bienvenida.Relleno = Tema.Gris; }
            else if (r.Nueva && r.Tipo == "salida")
            {
                _lblDetalle.Text = $"¡Hasta luego!  Salida {BaseDatos.Local(f.HoraSalida.Value):HH:mm}  ·  {f.Materia}";
                _bienvenida.Relleno = Tema.MarinoMedio;
                Sonido.Tocar("puntual");
            }
            else if (!r.Nueva) { _lblDetalle.Text = $"Ya registraste {f.Materia} hoy a las {hora} ({Tema.TextoEstado(f.Estado)})"; _bienvenida.Relleno = Tema.MarinoMedio; }
            else
            {
                _lblDetalle.Text = f.Estado == "puntual" ? $"¡Bienvenido/a!  {hora}  ·  {f.Materia}  ·  PUNTUAL" : $"{hora}  ·  {f.Materia}  ·  LLEGASTE CON ATRASO";
                _bienvenida.Relleno = Tema.ColorEstado(f.Estado);
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

        // ───────────────────────── materias ─────────────────────────
        Materia MateriaElegida => _cmbMateria.SelectedItem as Materia;

        /// <summary>Recarga las materias que puede ver el usuario y elige la que tiene clase ahora.</summary>
        void CargarMaterias()
        {
            long? antes = MateriaElegida?.Id;
            _cmbMateria.Items.Clear();
            foreach (var m in _db.MateriasDe(_usuario)) _cmbMateria.Items.Add(m);
            ActualizarTitulo();
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
                : $"{(materia.Docente.Length > 0 ? materia.Docente : "Sin docente")}      ·      {materia.TextoHorario}";
            _kPresentes.Valor = $"{r.Presentes}/{r.Total}";
            _kPuntuales.Valor = r.Puntuales.ToString();
            _kAtrasos.Valor = r.Atrasos.ToString();
            _kJustificados.Valor = r.Justificados.ToString();
            _kFaltan.Valor = r.Faltan.ToString();

            string firma = string.Join("|", est.Lista.Select(x => $"{x.EstudianteId}{x.Estado}{x.Hora}{x.HoraSalida}{x.Observacion}"));
            if (firma != _firma)
            {
                _firma = firma;
                long? seleccionado = _grid.SelectedRows.Count > 0 ? ((FilaAsistencia)_grid.SelectedRows[0].Tag).EstudianteId : (long?)null;
                _grid.Rows.Clear();
                foreach (var x in est.Lista)
                {
                    int i = _grid.Rows.Add(x.Codigo, x.Nombre + (x.Muestras == 0 ? "   · sin rostro" : ""), Tema.TextoEstado(x.Estado),
                        x.Hora.HasValue ? Tema.Hora(x.Hora.Value) : "—",
                        x.HoraSalida.HasValue ? Tema.Hora(x.HoraSalida.Value) : x.Hora.HasValue && x.Estado != "justificado" ? "sin salida" : "", x.Similitud > 0 ? x.Similitud.ToString("0.00") : "", x.Fuente, x.Observacion);
                    var fila = _grid.Rows[i];
                    fila.Tag = x;
                    if (x.Muestras == 0) fila.Cells["nombre"].Style.ForeColor = Tema.Suave;
                    if (x.Hora.HasValue && !x.HoraSalida.HasValue) fila.Cells["salida"].Style.ForeColor = Tema.Ambar;
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
                if (!_primera && ev.Prioridad == "alta" && _pagina != "alertas") _lateral.Contador("alertas", ++_alertasNuevas);
            }
            while (_lstAlertas.Items.Count > 300) _lstAlertas.Items.RemoveAt(_lstAlertas.Items.Count - 1);
            _lstAlertas.EndUpdate();
            _ultimoEvento = est.UltimoEvento;
            _primera = false;

            if (!_motor.Activo && _btnCamara.Text.StartsWith("■")) EstadoBotonCamara(false);
            else if (_motor.Activo)
                _lblCamara.Text = $"Cámara {(_motor.Conectada ? "activa" : "esperando imagen")} · {_motor.EstudiantesEnGaleria} rostros · {_motor.Fps:0.0} fps";
            _lblCamara.Punto = _motor.Activo && _motor.Conectada ? Tema.Verde : _motor.Activo ? Tema.Ambar : Tema.Gris;
        }

        void Alertar(Evento ev)
        {
            Sonido.Tocar("alerta");
            _lblAlerta.Text = "⚠   " + ev.Mensaje + "        (clic para ver las alertas)";
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
                Text = titulo, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(480, 128 + 68 + 60),
            };
            Tema.Preparar(f, titulo, null, Tema.Papel);
            var l = new Label { Text = texto, Left = 20, Top = 88, Width = 440, Height = 40, ForeColor = Tema.Tinta, BackColor = Color.Transparent };
            var t = new TextBox { Text = inicial, Left = 20, Top = 130, Width = 440 };
            Tema.EstiloCampo(t);
            var ok = Tema.Boton("Aceptar", Tema.Naranja, null);
            ok.DialogResult = DialogResult.OK;
            var no = Tema.Boton("Cancelar", Tema.Suave, null);
            no.DialogResult = DialogResult.Cancel;
            Tema.Pie(f, ok, no);
            f.Controls.AddRange(new Control[] { l, t });
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
            EstadoBotonCamara(false);
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
            using var f = new Form { Text = "Diagnóstico de cámara", Size = new Size(820, 620), StartPosition = FormStartPosition.CenterParent };
            Tema.Preparar(f, "Diagnóstico de cámara", "Prueba de todas las cámaras con todos los motores de video");
            var txt = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false,
                Font = new Font("Consolas", 10f), Text = informe.Replace("\n", Environment.NewLine), BackColor = Color.White,
                BorderStyle = BorderStyle.None, ForeColor = Tema.Tinta,
            };
            var marco = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Tema.Fondo };
            marco.Controls.Add(txt);
            var botones = new List<Control>
            {
                Tema.Boton("Copiar informe", Tema.MarinoMedio, (s, e) => { Clipboard.SetText(txt.Text); MessageBox.Show("Copiado. Puedes pegarlo para pedir ayuda."); }),
            };
            if (informe.Contains(DiagnosticoCamara.EnlaceVcRedist))
                botones.Add(Tema.Boton("Descargar Visual C++", Tema.Naranja, (s, e) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DiagnosticoCamara.EnlaceVcRedist) { UseShellExecute = true }); }
                    catch (Exception) { }
                }));
            botones.Add(Tema.Boton("Cerrar", Tema.Suave, (s, e) => f.Close()));
            f.Controls.Add(marco);
            Tema.Pie(f, botones.ToArray());
            marco.BringToFront();
            f.ShowDialog(this);
        }

        void EnHilo(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch (InvalidOperationException) { }
        }
    }
}
