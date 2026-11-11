using System;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using AsistenciaFacial.Nucleo;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>Ingreso al programa: el administrador (PIN de config.json) o un docente (su propio PIN).</summary>
    internal sealed class FormIngreso : Form
    {
        readonly Configuracion _cfg;
        readonly BaseDatos _db;
        readonly ComboBox _quien = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox _pin = new TextBox { UseSystemPasswordChar = true };
        readonly Label _error = new Label { ForeColor = Tema.Rojo, AutoSize = true };
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
            Text = "Ingreso · Control de asistencia";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 250);
            Font = Tema.Normal;
            BackColor = Tema.Papel;

            var cab = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Tema.Tinta };
            cab.Controls.Add(new Label
            {
                Text = $"{cfg.Institucion}\nGestión {db.GestionActiva()?.Nombre ?? "(sin gestión activa)"}",
                ForeColor = Color.White, Font = Tema.Negrita, AutoSize = true, Location = new Point(14, 12),
            });
            Controls.Add(cab);

            foreach (var d in db.Docentes(soloActivos: true)) _quien.Items.Add(d);
            _quien.Items.Add("Administrador");
            _quien.SelectedIndex = 0;
            Controls.Add(new Label { Text = "¿Quién eres?", Left = 20, Top = 84, Width = 100 });
            _quien.SetBounds(130, 80, 250, 26);
            Controls.Add(_quien);
            Controls.Add(new Label { Text = "PIN", Left = 20, Top = 124, Width = 100 });
            _pin.SetBounds(130, 120, 250, 26);
            Controls.Add(_pin);
            _error.Location = new Point(130, 152);
            Controls.Add(_error);

            var ok = Tema.Boton("Entrar", Tema.Verde, (s, e) => Entrar());
            var no = Tema.Boton("Salir", Tema.Suave, (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            ok.Location = new Point(220, 196);
            no.Location = new Point(305, 196);
            Controls.Add(ok);
            Controls.Add(no);
            AcceptButton = ok;
            CancelButton = no;
            Shown += (s, e) => _pin.Focus();
        }

        void Entrar()
        {
            string pin = _pin.Text.Trim();
            if (_quien.SelectedItem is Docente d)
            {
                if (_db.ValidarDocente(d.Id, pin) != null) Usuario = new Usuario { DocenteId = d.Id, Nombre = d.NombreCompleto };
            }
            else if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pin), Encoding.UTF8.GetBytes(_cfg.PinAdmin)))
                Usuario = new Usuario { EsAdmin = true, Nombre = "Administrador" };

            if (Usuario == null)
            {
                Thread.Sleep(600);
                _error.Text = "PIN incorrecto";
                _pin.SelectAll();
                _pin.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
