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
        readonly BarraProgreso _barra = new BarraProgreso();
        readonly FlowLayoutPanel _muestras = new FlowLayoutPanel();
        readonly Label _estado = new Label(), _aviso = new Label();
        readonly BotonModerno _btnIniciar;
        int _enCamino;
        bool _terminado;

        public FormRegistroRostro(MotorAsistencia motor, Estudiante est, int total)
        {
            _motor = motor;
            _est = est;
            Text = $"Registrar rostro — {est.NombreCompleto} ({est.Codigo})";
            Size = new Size(1040, 720);
            StartPosition = FormStartPosition.CenterParent;
            var cabecera = Tema.Preparar(this, $"Registrar rostro · {est.NombreCompleto}", $"Código {est.Codigo}  ·  se toman {total} muestras con la cámara");

            // ── panel derecho: pasos, progreso y muestras ──
            var derecha = new Panel { Dock = DockStyle.Right, Width = 340, Padding = new Padding(0, 16, 16, 16), BackColor = Tema.Fondo };
            var tarjeta = new Tarjeta { Dock = DockStyle.Fill, Padding = new Padding(16) };
            var pasos = new Label
            {
                Dock = DockStyle.Top, Height = 196, ForeColor = Tema.Tinta, BackColor = Color.Transparent, Font = Tema.Normal,
                Text = $"1.  Que {est.Nombres} se siente frente a la cámara, con buena luz y SOLO (una persona).\n\n" +
                       "2.  Presiona «Comenzar» y pídele que gire un poco la cabeza: izquierda, derecha, arriba, abajo.\n\n" +
                       "3.  Las muestras se guardan solas. Sin lentes oscuros, gorra ni barbijo.",
            };
            var tituloPasos = new Label { Dock = DockStyle.Top, Height = 30, Text = "CÓMO REGISTRAR", Font = Tema.SemiChica, ForeColor = Tema.NaranjaOscuro, BackColor = Color.Transparent };
            _estado.Dock = DockStyle.Top;
            _estado.Height = 40;
            _estado.Font = Tema.Titulo;
            _estado.ForeColor = Tema.Marino;
            _estado.BackColor = Color.Transparent;
            _estado.TextAlign = ContentAlignment.BottomLeft;
            _estado.Text = $"0 / {total} muestras";
            _barra.Dock = DockStyle.Top;
            _barra.Height = 10;
            _barra.Maximo = total;
            var hueco = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Color.Transparent };
            _aviso.Dock = DockStyle.Top;
            _aviso.Height = 58;
            _aviso.ForeColor = Tema.Rojo;
            _aviso.BackColor = Color.Transparent;
            _aviso.Font = Tema.Semi;
            _muestras.Dock = DockStyle.Fill;
            _muestras.AutoScroll = true;
            _muestras.BackColor = Color.Transparent;
            tarjeta.Controls.Add(_muestras);
            tarjeta.Controls.Add(_aviso);
            tarjeta.Controls.Add(hueco);
            tarjeta.Controls.Add(_barra);
            tarjeta.Controls.Add(_estado);
            tarjeta.Controls.Add(pasos);
            tarjeta.Controls.Add(tituloPasos);
            derecha.Controls.Add(tarjeta);

            _btnIniciar = Tema.Boton("▶  Comenzar", Tema.Naranja, (s, e) => Comenzar());
            var pie = Tema.Pie(this, _btnIniciar, Tema.Boton("Cerrar", Tema.Suave, (s, e) => Close()));

            var marco = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Tema.Fondo };
            _vivo.Dock = DockStyle.Fill;
            _vivo.SizeMode = PictureBoxSizeMode.Zoom;
            _vivo.BackColor = Tema.Camara;
            marco.Controls.Add(_vivo);
            Controls.Add(marco);
            Controls.Add(derecha);
            marco.BringToFront();
            pie.SendToBack();
            cabecera.SendToBack();

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
            _barra.Valor = 0;
            _aviso.Text = "";
            _btnIniciar.Enabled = false;
            _btnIniciar.Text = "Capturando…";
            _estado.ForeColor = Tema.Marino;
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
            _barra.Valor = n;
            _muestras.Controls.Add(new PictureBox { Image = Tema.ImagenDeBytes(cara), Width = 88, Height = 88, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(3), BackColor = Tema.MarinoClaro });
        });

        void Aviso(string m) => EnHilo(() => _aviso.Text = m);

        void Terminado(bool exito, string mensaje) => EnHilo(() =>
        {
            _terminado = true;
            _btnIniciar.Enabled = true;
            _btnIniciar.Text = "▶  Repetir";
            _estado.ForeColor = exito ? Tema.Verde : Tema.Rojo;
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
