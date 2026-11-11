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
        readonly TextBox _inst = new TextBox(), _pin = new TextBox(), _wifi = new TextBox(), _clave = new TextBox();
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
            ClientSize = new Size(600, 560);
            Font = Tema.Normal;
            BackColor = Tema.Papel;
            _inst.Text = cfg.Institucion;
            _pin.Text = cfg.PinAdmin; _wifi.Text = cfg.WifiNombre; _clave.Text = cfg.WifiClave; _publica.Checked = cfg.ListaPublica;
            _selfie.Checked = cfg.MarcarDesdeCelular; _pideCodigo.Checked = cfg.CelularPideCodigo;
            _anticipacion.Value = Math.Min(60, Math.Max(0, cfg.Horario.AnticipacionMin));
            _salidaAntes.Value = Math.Min(60, Math.Max(0, cfg.Horario.SalidaAntesMin));
            _salidaDespues.Value = Math.Min(120, Math.Max(0, cfg.Horario.SalidaDespuesMin));
            _umbral.Value = (decimal)Math.Min(0.8, Math.Max(0.3, cfg.Reconocimiento.UmbralSimilitud));
            _confirm.Value = Math.Min(10, Math.Max(1, cfg.Reconocimiento.Confirmaciones));
            _minimo.Value = Math.Min(300, Math.Max(30, cfg.Reconocimiento.RostroMinimoPx));

            int y = 12;
            void Titulo(string t) { Controls.Add(new Label { Text = t, Left = 12, Top = y, AutoSize = true, Font = Tema.Negrita, ForeColor = Tema.Azul }); y += 28; }
            void Fila(string t, Control c, string ayuda = null)
            {
                Controls.Add(new Label { Text = t, Left = 20, Top = y + 3, Width = 190 });
                c.Left = 215; c.Top = y;
                if (c is TextBox) c.Width = 360; else if (c is CheckBox) { } else c.Width = 90;
                Controls.Add(c);
                if (ayuda != null) Controls.Add(new Label { Text = ayuda, Left = 315, Top = y + 3, Width = 270, ForeColor = Tema.Suave, Font = Tema.Chica });
                y += 32;
            }
            Titulo("Institución");
            Fila("Nombre", _inst);
            Controls.Add(new Label
            {
                Text = "Gestiones, docentes, materias y horarios: botón «Académico».", Left = 20, Top = y, AutoSize = true,
                ForeColor = Tema.Suave, Font = Tema.Chica,
            });
            y += 26;
            Titulo("Horario");
            Fila("Entrada desde (min antes)", _anticipacion, "antes del inicio de cada clase");
            Fila("Salida desde (min antes)", _salidaAntes, "antes del fin de la clase");
            Fila("Salida hasta (min después)", _salidaDespues, "después del fin de la clase");
            Titulo("Reconocimiento facial");
            Fila("Umbral de similitud", _umbral, "más alto = más exigente (0,42)");
            Fila("Confirmaciones", _confirm, "cuadros seguidos antes de marcar");
            Fila("Rostro mínimo (px)", _minimo, "rostros más chicos se ignoran");
            Titulo("Web y red");
            Fila("PIN del administrador", _pin); Fila("Red WiFi (para el QR)", _wifi); Fila("Clave del WiFi", _clave);
            foreach (var c in new[] { _publica, _selfie, _pideCodigo }) { c.Left = 20; c.Top = y; Controls.Add(c); y += 28; }
            y += 6;
            var ok = Tema.Boton("Guardar", Tema.Verde, (s, e) => Guardar());
            var no = Tema.Boton("Cancelar", Tema.Suave, (s, e) => Close());
            ok.Location = new Point(400, y); no.Location = new Point(490, y);
            Controls.Add(ok); Controls.Add(no);
            ClientSize = new Size(600, y + 50);
        }

        void Guardar()
        {
            if (_pin.Text.Trim().Length < 4) { MessageBox.Show("El PIN debe tener al menos 4 caracteres.", Text); return; }
            _cfg.Institucion = _inst.Text.Trim();
            _cfg.PinAdmin = _pin.Text.Trim(); _cfg.WifiNombre = _wifi.Text.Trim(); _cfg.WifiClave = _clave.Text; _cfg.ListaPublica = _publica.Checked;
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
