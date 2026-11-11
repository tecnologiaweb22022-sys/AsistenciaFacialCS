using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AsistenciaFacial.Nucleo;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>Cartel para la puerta del aula: QR del WiFi, de «mi asistencia» y de la lista de hoy.</summary>
    internal sealed class FormQR : Form
    {
        readonly Configuracion _cfg;
        readonly ComboBox _cmbIp = new ComboBox();
        readonly PictureBox _pic = new PictureBox();
        Bitmap _cartel;

        public FormQR(Configuracion cfg)
        {
            _cfg = cfg;
            Text = "Cartel con códigos QR";
            Size = new Size(820, 900);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Tema.Fondo;
            Font = Tema.Normal;
            var arriba = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 8, 8, 0) };
            arriba.Controls.Add(new Label { Text = "IP de esta laptop:", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
            var ips = Red.IpsLocales();
            if (!string.IsNullOrWhiteSpace(cfg.IpParaQr) && !ips.Contains(cfg.IpParaQr)) ips.Insert(0, cfg.IpParaQr);
            _cmbIp.Items.AddRange(ips.Cast<object>().ToArray());
            _cmbIp.Text = ips[0];
            _cmbIp.Width = 150;
            _cmbIp.SelectedIndexChanged += (s, e) => Generar();
            _cmbIp.Leave += (s, e) => Generar();
            arriba.Controls.Add(_cmbIp);
            arriba.Controls.Add(Tema.Boton("Imprimir…", Tema.Azul, (s, e) => Imprimir()));
            arriba.Controls.Add(Tema.Boton("Guardar imagen…", Tema.Suave, (s, e) => Guardar()));
            _pic.Dock = DockStyle.Fill;
            _pic.SizeMode = PictureBoxSizeMode.Zoom;
            Controls.Add(_pic);
            Controls.Add(arriba);
            Generar();
        }

        static Image Qr(string texto) => Tema.ImagenDeBytes(CodigosQr.Png(texto, 12));

        void Generar()
        {
            string url = $"http://{_cmbIp.Text.Trim()}:{_cfg.Puerto}";
            const int W = 2000, H = 2600;                              // ~ hoja carta a 240 ppp
            var b = new Bitmap(W, H);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.White);
                using (var franja = new SolidBrush(Tema.Tinta)) g.FillRectangle(franja, 0, 0, W, 330);
                var centro = new StringFormat { Alignment = StringAlignment.Center };
                using var f1 = new Font("Segoe UI", 90, FontStyle.Bold);
                using var f2 = new Font("Segoe UI", 40);
                using var f3 = new Font("Segoe UI", 44, FontStyle.Bold);
                using var f4 = new Font("Consolas", 30);
                g.DrawString("ASISTENCIA", f1, Brushes.White, new RectangleF(0, 40, W, 170), centro);
                g.DrawString(_cfg.Institucion, f2, Brushes.White, new RectangleF(0, 220, W, 80), centro);
                using var tinta = new SolidBrush(Tema.Tinta);
                using var suave = new SolidBrush(Tema.Suave);
                bool selfie = _cfg.MarcarDesdeCelular;
                string principal = selfie ? "/marcar" : "/mi-asistencia", secundaria = selfie ? "/mi-asistencia" : "/";
                g.DrawString(selfie ? "Al llegar, mira a la cámara del aula… o marca con una selfie desde tu celular."
                                    : "Al llegar, mira a la cámara: tu asistencia se marca sola.", f2, tinta, new RectangleF(80, 400, W - 160, 160), centro);
                bool wifi = !string.IsNullOrWhiteSpace(_cfg.WifiNombre);
                if (wifi)
                {
                    using var q = Qr(CodigosQr.TextoWifi(_cfg.WifiNombre, _cfg.WifiClave));
                    g.DrawImage(q, new Rectangle(180, 620, 700, 700));
                    g.DrawString("1. Conéctate al WiFi", f3, tinta, new RectangleF(80, 1340, 900, 80), centro);
                    g.DrawString($"Red: {_cfg.WifiNombre}" + (string.IsNullOrEmpty(_cfg.WifiClave) ? "" : $"\nClave: {_cfg.WifiClave}"), f2, suave, new RectangleF(80, 1430, 900, 160), centro);
                }
                using (var q = Qr(url + principal))
                    g.DrawImage(q, new Rectangle(wifi ? 1120 : 650, 620, 700, 700));
                g.DrawString((wifi ? "2. " : "") + (selfie ? "Marca con una selfie" : "Consulta TU asistencia"), f3, tinta, new RectangleF(wifi ? 1020 : 0, 1340, wifi ? 900 : W, 80), centro);
                g.DrawString(url + principal, f4, suave, new RectangleF(wifi ? 1020 : 0, 1430, wifi ? 900 : W, 60), centro);
                using (var q = Qr(url + secundaria))
                    g.DrawImage(q, new Rectangle(800, 1700, 400, 400));
                g.DrawString(selfie ? "Consulta tu asistencia" : "Lista de hoy", f3, tinta, new RectangleF(0, 2120, W, 80), centro);
                g.DrawString(url + secundaria, f4, suave, new RectangleF(0, 2200, W, 60), centro);
                g.DrawString("Control de asistencia con reconocimiento facial · funciona sin internet", f2, suave, new RectangleF(0, 2420, W, 80), centro);
            }
            var anterior = _cartel;
            _cartel = b;
            _pic.Image = _cartel;
            anterior?.Dispose();
        }

        void Imprimir()
        {
            using var doc = new PrintDocument { DocumentName = "Cartel de asistencia" };
            doc.PrintPage += (s, e) =>
            {
                var a = e.MarginBounds;
                float esc = Math.Min((float)a.Width / _cartel.Width, (float)a.Height / _cartel.Height);
                e.Graphics.DrawImage(_cartel, a.X + (a.Width - _cartel.Width * esc) / 2, a.Y, _cartel.Width * esc, _cartel.Height * esc);
            };
            using var vista = new PrintPreviewDialog { Document = doc, Width = 800, Height = 900 };
            vista.ShowDialog(this);
        }

        void Guardar()
        {
            using var d = new SaveFileDialog { FileName = "cartel_asistencia.png", Filter = "PNG (*.png)|*.png" };
            if (d.ShowDialog(this) == DialogResult.OK) _cartel.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cartel?.Dispose();
            base.Dispose(disposing);
        }
    }
}
