using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AsistenciaFacial.Nucleo;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>
    /// Ingreso al programa con usuario y contraseña: el administrador (config.json, cifrada) o un docente (base de datos, cifrada).
    /// Tras 5 intentos fallidos con el mismo usuario espera 1 minuto.
    /// </summary>
    internal sealed class FormIngreso : Form
    {
        readonly Configuracion _cfg;
        readonly BaseDatos _db;
        readonly TextBox _usuario = new TextBox { MaxLength = 40 };
        readonly TextBox _clave = new TextBox { UseSystemPasswordChar = true, MaxLength = 100 };
        readonly Label _error = new Label { ForeColor = Tema.Rojo, AutoSize = false, Font = Tema.Semi, BackColor = Color.Transparent };
        readonly Label _mayus = new Label { ForeColor = Tema.Ambar, AutoSize = true, Font = Tema.Chica, Text = "⇪  Bloq Mayús está activado", Visible = false };
        readonly BotonModerno _ok;
        static readonly Font FuenteMarca = Tema.Fuente(24f, familia: Tema.FamiliaTitulos);
        static readonly Font FuenteBienvenida = Tema.Fuente(19f, familia: Tema.FamiliaTitulos);
        /// <summary>Último usuario que entró en esta sesión del programa (para «Cambiar usuario»).</summary>
        static string _ultimoUsuario = "";
        public Usuario Usuario { get; private set; }

        /// <summary>Muestra el ingreso y devuelve quién entró (null si se canceló).</summary>
        public static Usuario Pedir(Configuracion cfg, BaseDatos db, IWin32Window dueno = null)
        {
            using var f = new FormIngreso(cfg, db);
            return f.ShowDialog(dueno) == DialogResult.OK ? f.Usuario : null;
        }

        FormIngreso(Configuracion cfg, BaseDatos db)
        {
            _cfg = cfg;
            _db = db;
            Text = "Iniciar sesión · Control de asistencia";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(780, 470);
            Font = Tema.Normal;
            BackColor = Tema.Papel;
            ShowIcon = false;
            string gestion = db.GestionActiva()?.Nombre ?? "(sin gestión activa)";

            // ── panel de marca (izquierda) ──
            var marca = new Lienzo { Dock = DockStyle.Left, Width = 320 };
            marca.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var r = marca.ClientRectangle;
                float k = r.Width / 320f;                           // el panel crece con el zoom
                using (var b = new LinearGradientBrush(r, Tema.MarinoProfundo, Tema.MarinoMedio, 60f)) g.FillRectangle(b, r);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(r.Width - 160 * k, -140 * k, 320 * k, 320 * k);
                    using var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(80, Tema.Naranja), SurroundColors = new[] { Color.FromArgb(0, Tema.Naranja) } };
                    g.FillPath(pb, path);
                }
                using (var b = new SolidBrush(Tema.Naranja)) g.FillRectangle(b, r.Width - 4, 0, 4, r.Height);
                int m = (int)(36 * k);
                Tema.DibujarLogo(g, new RectangleF(m, 44 * k, 60 * k, 60 * k));
                int y = (int)(124 * k);
                var tam = TextRenderer.MeasureText(g, "Control de\nasistencia", FuenteMarca, new Size(r.Width - 2 * m, 400), TextFormatFlags.WordBreak);
                TextRenderer.DrawText(g, "Control de\nasistencia", FuenteMarca, new Rectangle(m - 2, y, r.Width - 2 * m, tam.Height), Color.White, TextFormatFlags.Left | TextFormatFlags.WordBreak);
                y += tam.Height + 8;
                TextRenderer.DrawText(g, "Reconocimiento facial con inteligencia artificial", Tema.Normal, new Rectangle(m, y, r.Width - 2 * m, 60),
                    Color.FromArgb(185, 199, 222), TextFormatFlags.Left | TextFormatFlags.WordBreak);
                int abajo = r.Height - (int)(26 * k), hG = Tema.Chica.Height, hI = Tema.Semi.Height * 2 + 4;
                TextRenderer.DrawText(g, "Gestión " + gestion, Tema.Chica, new Rectangle(m, abajo - hG, r.Width - 2 * m, hG), Color.FromArgb(255, 170, 100), TextFormatFlags.Left);
                TextRenderer.DrawText(g, cfg.Institucion, Tema.Semi, new Rectangle(m, abajo - hG - 6 - hI, r.Width - 2 * m, hI), Color.White,
                    TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.Bottom);
                using (var p = new Pen(Color.FromArgb(50, 255, 255, 255))) g.DrawLine(p, m, abajo - hG - hI - 18, r.Width - m, abajo - hG - hI - 18);
            };
            Controls.Add(marca);

            // ── formulario (derecha) ──
            int x = 368, ancho = 370;
            Controls.Add(new Label { Text = "Iniciar sesión", Font = FuenteBienvenida, ForeColor = Tema.Marino, AutoSize = true, Location = new Point(x - 3, 34) });
            Controls.Add(new Label { Text = "Ingresa tu usuario y contraseña", ForeColor = Tema.Suave, AutoSize = true, Location = new Point(x, 72) });

            Controls.Add(new Label { Text = "USUARIO", Font = Tema.SemiChica, ForeColor = Tema.Suave, AutoSize = true, Location = new Point(x, 112) });
            Tema.EstiloCampo(_usuario);
            _usuario.SetBounds(x, 132, ancho, 30);
            _usuario.PlaceholderText = "ej. maria.quispe";
            _usuario.Text = _ultimoUsuario;
            Controls.Add(_usuario);

            Controls.Add(new Label { Text = "CONTRASEÑA", Font = Tema.SemiChica, ForeColor = Tema.Suave, AutoSize = true, Location = new Point(x, 180) });
            Tema.EstiloCampo(_clave);
            _clave.SetBounds(x, 200, ancho - 44, 30);
            Controls.Add(_clave);
            var ojo = Tema.Boton("👁", Tema.Suave, null);
            ojo.AutoSize = false;
            ojo.Padding = Padding.Empty;
            ojo.SetBounds(x + ancho - 38, 199, 38, 32);
            ojo.TabStop = false;
            var ayudaOjo = new ToolTip();
            ayudaOjo.SetToolTip(ojo, "Mostrar u ocultar la contraseña");
            ojo.Click += (s, e) => { _clave.UseSystemPasswordChar = !_clave.UseSystemPasswordChar; _clave.Focus(); _clave.SelectionStart = _clave.TextLength; };
            Controls.Add(ojo);
            _mayus.Location = new Point(x, 236);
            Controls.Add(_mayus);
            _clave.KeyUp += (s, e) => _mayus.Visible = Control.IsKeyLocked(Keys.CapsLock);
            _clave.Enter += (s, e) => _mayus.Visible = Control.IsKeyLocked(Keys.CapsLock);
            _error.SetBounds(x, 258, ancho, 44);
            Controls.Add(_error);

            _ok = Tema.Boton("Entrar  →", Tema.Naranja, (s, e) => Entrar());
            _ok.AutoSize = false;
            _ok.Font = Tema.Fuente(11f, familia: "Segoe UI Semibold");
            _ok.SetBounds(x, 306, ancho, 44);
            var no = Tema.Boton("Salir", Tema.Suave, (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            no.AutoSize = false;
            no.SetBounds(x, 360, ancho, 38);
            Controls.Add(_ok);
            Controls.Add(no);
            Controls.Add(new Label
            {
                Text = "🛡  Contraseñas cifradas · 5 intentos fallidos bloquean 1 minuto", ForeColor = Tema.Tenue, Font = Tema.Chica,
                AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(x, 412), Size = new Size(ancho, 30),
            });
            AcceptButton = _ok;
            CancelButton = no;
            Tema.Escalar(this);
            Shown += (s, e) => (_usuario.TextLength > 0 ? _clave : _usuario).Focus();
        }

        void Entrar()
        {
            string usuario = Claves.NormalizarUsuario(_usuario.Text), clave = _clave.Text, cuenta = "escritorio:" + usuario;
            if (usuario.Length == 0) { Mostrar("Escribe tu usuario."); _usuario.Focus(); return; }
            if (clave.Length == 0) { Mostrar("Escribe tu contraseña."); _clave.Focus(); return; }
            int espera = Claves.SegundosBloqueado(cuenta);
            if (espera > 0) { Mostrar($"Demasiados intentos fallidos. Espera {espera} segundos."); return; }

            Cursor = Cursors.WaitCursor;
            _ok.Enabled = false;
            try { Usuario = Claves.Ingresar(_cfg, _db, usuario, clave); }
            finally { Cursor = Cursors.Default; _ok.Enabled = true; }

            if (Usuario == null)
            {
                Claves.RegistrarFallo(cuenta);
                try { _db.EventoSistema("ingreso_fallido", $"Intento de ingreso fallido en el programa con el usuario «{usuario}»", false, null); }
                catch (Exception) { /* la base no responde: igual se informa al usuario */ }
                espera = Claves.SegundosBloqueado(cuenta);
                Mostrar(espera > 0 ? $"Demasiados intentos fallidos. Espera {espera} segundos." : "Usuario o contraseña incorrectos.");
                _clave.SelectAll();
                _clave.Focus();
                return;
            }
            Claves.RegistrarExito(cuenta);
            _ultimoUsuario = usuario;
            if (Usuario.EsAdmin && (clave.Length < Claves.LargoMinimo || clave == "1234"))
                MessageBox.Show("La contraseña del administrador es muy débil.\n\nCámbiala en «Configuración → Acceso del administrador» " +
                                $"(mínimo {Claves.LargoMinimo} caracteres, mejor con letras y números).", "Seguridad",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.OK;
            Close();
        }

        void Mostrar(string texto) => _error.Text = "⚠  " + texto;
    }
}
