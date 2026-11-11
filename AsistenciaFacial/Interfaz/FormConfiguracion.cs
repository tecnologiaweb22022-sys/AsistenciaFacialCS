using System;
using System.Drawing;
using System.Windows.Forms;
using AsistenciaFacial.Nucleo;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>
    /// Institución, sensibilidad del reconocimiento y opciones de la web (se guarda en config.json).
    /// Las materias, docentes y horarios están en la base de datos: ventana «Académico».
    /// </summary>
    internal sealed class FormConfiguracion : Form
    {
        readonly Configuracion _cfg;
        readonly TextBox _inst = new TextBox(), _wifi = new TextBox(), _clave = new TextBox(), _usuarioAdmin = new TextBox { MaxLength = 30 },
            _claveAdmin = new TextBox { UseSystemPasswordChar = true, MaxLength = 100 }, _repetirAdmin = new TextBox { UseSystemPasswordChar = true, MaxLength = 100 };
        readonly NumericUpDown _anticipacion = new NumericUpDown { Minimum = 0, Maximum = 60 },
            _salidaAntes = new NumericUpDown { Minimum = 0, Maximum = 60 }, _salidaDespues = new NumericUpDown { Minimum = 0, Maximum = 120 },
            _umbral = new NumericUpDown { Minimum = 0.30m, Maximum = 0.80m, Increment = 0.01m, DecimalPlaces = 2 },
            _confirm = new NumericUpDown { Minimum = 1, Maximum = 10 },
            _minimo = new NumericUpDown { Minimum = 30, Maximum = 300 };
        readonly CheckBox _publica = new CheckBox { Text = "La página pública muestra los nombres de quienes llegaron", AutoSize = true },
            _selfie = new CheckBox { Text = "Permitir marcar con una selfie desde el celular (página /marcar)", AutoSize = true },
            _pideCodigo = new CheckBox { Text = "La selfie pide el código del estudiante (compara 1 a 1, más seguro)", AutoSize = true };

        public FormConfiguracion(Configuracion cfg)
        {
            _cfg = cfg;
            Text = "Configuración";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(660, 560);
            Tema.Preparar(this, "Configuración", "Institución, horario, reconocimiento facial y opciones de la web", Tema.Papel);
            _inst.Text = cfg.Institucion;
            _usuarioAdmin.Text = cfg.UsuarioAdmin; _wifi.Text = cfg.WifiNombre; _clave.Text = cfg.WifiClave; _publica.Checked = cfg.ListaPublica;
            _selfie.Checked = cfg.MarcarDesdeCelular; _pideCodigo.Checked = cfg.CelularPideCodigo;
            _anticipacion.Value = Math.Min(60, Math.Max(0, cfg.Horario.AnticipacionMin));
            _salidaAntes.Value = Math.Min(60, Math.Max(0, cfg.Horario.SalidaAntesMin));
            _salidaDespues.Value = Math.Min(120, Math.Max(0, cfg.Horario.SalidaDespuesMin));
            _umbral.Value = (decimal)Math.Min(0.8, Math.Max(0.3, cfg.Reconocimiento.UmbralSimilitud));
            _confirm.Value = Math.Min(10, Math.Max(1, cfg.Reconocimiento.Confirmaciones));
            _minimo.Value = Math.Min(300, Math.Max(30, cfg.Reconocimiento.RostroMinimoPx));

            // contenido con desplazamiento: entra en pantallas de 768 px de alto
            var cuerpo = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Tema.Papel };
            int y = 18;
            void Titulo(string t)
            {
                if (y > 20) y += 10;
                cuerpo.Controls.Add(new Label { Text = t.ToUpperInvariant(), Left = 24, Top = y, AutoSize = true, Font = Tema.SemiChica, ForeColor = Tema.NaranjaOscuro });
                cuerpo.Controls.Add(new Panel { Left = 24, Top = y + 22, Width = 590, Height = 1, BackColor = Tema.Linea });
                y += 34;
            }
            void Fila(string t, Control c, string ayuda = null)
            {
                cuerpo.Controls.Add(new Label { Text = t, Left = 24, Top = y + 5, Width = 200, ForeColor = Tema.Tinta });
                Tema.EstiloCampo(c);
                c.Left = 230; c.Top = y;
                if (c is TextBox) c.Width = 384; else if (c is CheckBox) { } else c.Width = 90;
                cuerpo.Controls.Add(c);
                if (ayuda != null) cuerpo.Controls.Add(new Label { Text = ayuda, Left = 332, Top = y + 6, Width = 290, ForeColor = Tema.Suave, Font = Tema.Chica });
                y += 38;
            }
            Titulo("Institución");
            Fila("Nombre", _inst);
            cuerpo.Controls.Add(new Label
            {
                Text = "ℹ  Gestiones, docentes, materias y horarios se configuran en el botón «Académico».", Left = 24, Top = y, Width = 590, Height = 30,
                ForeColor = Tema.MarinoMedio, BackColor = Tema.MarinoBruma, Font = Tema.Chica, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0),
            });
            y += 36;
            Titulo("Horario");
            Fila("Entrada desde (min antes)", _anticipacion, "antes del inicio de cada clase");
            Fila("Salida desde (min antes)", _salidaAntes, "antes del fin de la clase");
            Fila("Salida hasta (min después)", _salidaDespues, "después del fin de la clase");
            Titulo("Reconocimiento facial");
            Fila("Umbral de similitud", _umbral, "más alto = más exigente (0,42)");
            Fila("Confirmaciones", _confirm, "cuadros seguidos antes de marcar");
            Fila("Rostro mínimo (px)", _minimo, "rostros más chicos se ignoran");
            Titulo("Acceso del administrador");
            Fila("Usuario", _usuarioAdmin);
            Fila("Nueva contraseña", _claveAdmin);
            Fila("Repetir contraseña", _repetirAdmin);
            _claveAdmin.PlaceholderText = $"vacío = no cambiar · mínimo {Claves.LargoMinimo} caracteres";
            Titulo("Web y red");
            Fila("Red WiFi (para el QR)", _wifi); Fila("Clave del WiFi", _clave);
            foreach (var c in new[] { _publica, _selfie, _pideCodigo })
            {
                c.Left = 24; c.Top = y; c.ForeColor = Tema.Tinta; c.Cursor = Cursors.Hand;
                cuerpo.Controls.Add(c);
                y += 30;
            }
            cuerpo.Controls.Add(new Panel { Left = 0, Top = y, Width = 1, Height = 16 });       // margen inferior al desplazar
            Controls.Add(cuerpo);
            var ok = Tema.Boton("Guardar cambios", Tema.Naranja, (s, e) => Guardar());
            var no = Tema.Boton("Cancelar", Tema.Suave, (s, e) => Close());
            Tema.Pie(this, ok, no);
            cuerpo.BringToFront();
            AcceptButton = ok;
            CancelButton = no;
            int alto = 68 + y + 16 + 60;
            ClientSize = new Size(660, Math.Min(alto, Math.Max(480, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 80)));
        }

        void Guardar()
        {
            string usuario = Claves.NormalizarUsuario(_usuarioAdmin.Text);
            try
            {
                Claves.ValidarUsuario(usuario);
                if (_claveAdmin.Text.Length > 0 || _repetirAdmin.Text.Length > 0) Claves.ValidarNueva(_claveAdmin.Text, _repetirAdmin.Text);
            }
            catch (ErrorValidacion ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            _cfg.Institucion = _inst.Text.Trim();
            _cfg.UsuarioAdmin = usuario;
            if (_claveAdmin.Text.Length > 0) _cfg.CambiarClaveAdmin(_claveAdmin.Text);
            _cfg.WifiNombre = _wifi.Text.Trim(); _cfg.WifiClave = _clave.Text; _cfg.ListaPublica = _publica.Checked;
            _cfg.MarcarDesdeCelular = _selfie.Checked; _cfg.CelularPideCodigo = _pideCodigo.Checked;
            _cfg.Horario.AnticipacionMin = (int)_anticipacion.Value;
            _cfg.Horario.SalidaAntesMin = (int)_salidaAntes.Value;
            _cfg.Horario.SalidaDespuesMin = (int)_salidaDespues.Value;
            _cfg.Reconocimiento.UmbralSimilitud = (double)_umbral.Value;
            _cfg.Reconocimiento.Confirmaciones = (int)_confirm.Value;
            _cfg.Reconocimiento.RostroMinimoPx = (int)_minimo.Value;
            try { _cfg.Guardar(); }
            catch (Exception ex) { MessageBox.Show("No se pudo guardar config.json: " + ex.Message, Text); return; }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
