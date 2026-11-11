using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using AsistenciaFacial.Nucleo;
using OpenCvSharp.Extensions;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>
    /// Registro del rostro de UN estudiante: la cámara toma varias muestras (vectores SFace)
    /// mientras el estudiante mueve un poco la cabeza. Se guardan solas al completarse.
    /// </summary>
    internal sealed class FormRegistroRostro : Form
    {
        readonly MotorAsistencia _motor;
        readonly Estudiante _est;
        readonly PictureBox _vivo = new PictureBox();
        readonly ProgressBar _barra = new ProgressBar();
        readonly FlowLayoutPanel _muestras = new FlowLayoutPanel();
        readonly Label _estado = new Label(), _aviso = new Label();
        readonly Button _btnIniciar;
        int _enCamino;
        bool _terminado;

        public FormRegistroRostro(MotorAsistencia motor, Estudiante est, int total)
        {
            _motor = motor;
            _est = est;
            Text = $"Registrar rostro — {est.NombreCompleto} ({est.Codigo})";
            Size = new Size(980, 680);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Tema.Fondo;
            Font = Tema.Normal;

            var instrucciones = new Label
            {
                Dock = DockStyle.Top, Height = 74, Padding = new Padding(12, 8, 12, 0), Font = Tema.Normal, ForeColor = Tema.Tinta,
                Text = $"1. Que {est.Nombres} se siente frente a la cámara, con buena luz y SOLO (una persona).\n" +
                       "2. Presiona «Comenzar» y pídele que gire un poco la cabeza: izquierda, derecha, arriba, abajo.\n" +
                       $"3. Se toman {total} muestras y se guardan automáticamente. Sin lentes oscuros, gorra ni barbijo.",
            };
            var derecha = new Panel { Dock = DockStyle.Right, Width = 300, Padding = new Padding(8) };
            _estado.Dock = DockStyle.Top;
            _estado.Height = 30;
            _estado.Font = Tema.Titulo;
            _estado.Text = $"0 / {total} muestras";
            _barra.Dock = DockStyle.Top;
            _barra.Height = 18;
            _barra.Maximum = total;
            _aviso.Dock = DockStyle.Top;
            _aviso.Height = 64;
            _aviso.ForeColor = Tema.Rojo;
            _aviso.Font = Tema.Negrita;
            _muestras.Dock = DockStyle.Fill;
            _muestras.AutoScroll = true;
            derecha.Controls.Add(_muestras);
            derecha.Controls.Add(_aviso);
            derecha.Controls.Add(_barra);
            derecha.Controls.Add(_estado);

            var abajo = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 0) };
            abajo.Controls.Add(Tema.Boton("Cerrar", Tema.Suave, (s, e) => Close()));
            _btnIniciar = Tema.Boton("▶ Comenzar", Tema.Verde, (s, e) => Comenzar());
            abajo.Controls.Add(_btnIniciar);

            _vivo.Dock = DockStyle.Fill;
            _vivo.SizeMode = PictureBoxSizeMode.Zoom;
            _vivo.BackColor = Color.FromArgb(18, 24, 38);
            Controls.Add(_vivo);
            Controls.Add(derecha);
            Controls.Add(abajo);
            Controls.Add(instrucciones);

            _motor.FrameListo += Frame;
            _motor.RegistroProgreso += Progreso;
            _motor.RegistroTerminado += Terminado;
            _motor.RegistroAviso += Aviso;
            FormClosing += (s, e) =>
            {
                if (!_terminado && _motor.Registrando) _motor.CancelarRegistro();
                _motor.FrameListo -= Frame;
                _motor.RegistroProgreso -= Progreso;
                _motor.RegistroTerminado -= Terminado;
                _motor.RegistroAviso -= Aviso;
            };
        }

        void Comenzar()
        {
            foreach (Control c in _muestras.Controls) { (c as PictureBox)?.Image?.Dispose(); }
            _muestras.Controls.Clear();
            _barra.Value = 0;
            _aviso.Text = "";
            _btnIniciar.Enabled = false;
            _btnIniciar.Text = "Capturando…";
            _terminado = false;
            _motor.IniciarRegistro(_est);
        }

        void EnHilo(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        void Frame(OpenCvSharp.Mat mat)
        {
            if (Interlocked.Exchange(ref _enCamino, 1) == 1 || IsDisposed) { mat.Dispose(); return; }
            Bitmap bmp;
            try { bmp = mat.ToBitmap(); } finally { mat.Dispose(); }
            try
            {
                BeginInvoke(new Action(() =>
                {
                    var a = _vivo.Image;
                    _vivo.Image = bmp;
                    a?.Dispose();
                    Interlocked.Exchange(ref _enCamino, 0);
                }));
            }
            catch (InvalidOperationException) { bmp.Dispose(); Interlocked.Exchange(ref _enCamino, 0); }
        }

        void Progreso(int n, int total, byte[] cara) => EnHilo(() =>
        {
            _estado.Text = $"{n} / {total} muestras";
            _barra.Value = Math.Min(_barra.Maximum, n);
            _muestras.Controls.Add(new PictureBox { Image = Tema.ImagenDeBytes(cara), Width = 84, Height = 84, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(3) });
        });

        void Aviso(string m) => EnHilo(() => _aviso.Text = m);

        void Terminado(bool exito, string mensaje) => EnHilo(() =>
        {
            _terminado = true;
            _btnIniciar.Enabled = true;
            _btnIniciar.Text = "▶ Repetir";
            MessageBox.Show(mensaje + (exito ? "\n\nDesde ahora la cámara lo reconocerá al llegar." : ""), Text,
                MessageBoxButtons.OK, exito ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (exito) { DialogResult = DialogResult.OK; Close(); }
        });

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _vivo.Image?.Dispose();
                foreach (Control c in _muestras.Controls) (c as PictureBox)?.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
