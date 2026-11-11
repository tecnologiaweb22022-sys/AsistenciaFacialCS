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
            Size = new Size(860, 940);
            StartPosition = FormStartPosition.CenterParent;
            var cabecera = Tema.Preparar(this, "Cartel con códigos QR", "Imprímelo y pégalo en la puerta del aula");
            var arriba = Tema.Fila(DockStyle.Top, new Padding(16, 12, 16, 8));
            arriba.Controls.Add(Tema.Etiqueta("IP de esta laptop en el router", Tema.Semi));
            var ips = Red.IpsLocales();
            if (!string.IsNullOrWhiteSpace(cfg.IpParaQr) && !ips.Contains(cfg.IpParaQr)) ips.Insert(0, cfg.IpParaQr);
            _cmbIp.Items.AddRange(ips.Cast<object>().ToArray());
            _cmbIp.Text = ips[0];
            _cmbIp.Width = 170;
            Tema.EstiloCampo(_cmbIp);
            _cmbIp.Margin = new Padding(4, 5, 14, 3);
            _cmbIp.SelectedIndexChanged += (s, e) => Generar();
            _cmbIp.Leave += (s, e) => Generar();
            arriba.Controls.Add(_cmbIp);
            arriba.Controls.Add(Tema.Boton("🖨  Imprimir…", Tema.Naranja, (s, e) => Imprimir()));
            arriba.Controls.Add(Tema.Boton("Guardar imagen…", Tema.Suave, (s, e) => Guardar()));
            _pic.Dock = DockStyle.Fill;
            _pic.SizeMode = PictureBoxSizeMode.Zoom;
            _pic.BackColor = Tema.Fondo;
            var marco = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 16), BackColor = Tema.Fondo };
            marco.Controls.Add(_pic);
            Controls.Add(marco);
            Controls.Add(arriba);
            cabecera.SendToBack();
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
                g.InterpolationMode = InterpolationMode.NearestNeighbor;          // QR nítido
                g.Clear(Color.White);
                // franja institucional
                using (var franja = new LinearGradientBrush(new Rectangle(0, 0, W, 380), Tema.MarinoProfundo, Tema.MarinoMedio, 0f))
                    g.FillRectangle(franja, 0, 0, W, 380);
                using (var naranja = new SolidBrush(Tema.Naranja)) g.FillRectangle(naranja, 0, 380, W, 22);
                Tema.DibujarLogo(g, new RectangleF(W / 2f - 75, 40, 150, 150));
                var centro = new StringFormat { Alignment = StringAlignment.Center };
                using var f1 = new Font("Segoe UI", 80, FontStyle.Bold);
                using var f2 = new Font("Segoe UI", 38);
                using var f3 = new Font("Segoe UI Semibold", 44);
                using var f4 = new Font("Consolas", 28);
                using var fPaso = new Font("Segoe UI", 40, FontStyle.Bold);
                using var claro = new SolidBrush(Color.FromArgb(200, 212, 232));
                g.DrawString("ASISTENCIA", f1, Brushes.White, new RectangleF(0, 190, W, 130), centro);
                g.DrawString(_cfg.Institucion, f2, claro, new RectangleF(60, 305, W - 120, 70), centro);
                using var tinta = new SolidBrush(Tema.Marino);
                using var suave = new SolidBrush(Tema.Suave);
                using var naranjaB = new SolidBrush(Tema.Naranja);
                using var linea = new Pen(Tema.Linea, 6);
                bool selfie = _cfg.MarcarDesdeCelular;
                string principal = selfie ? "/marcar" : "/mi-asistencia", secundaria = selfie ? "/mi-asistencia" : "/";
                g.DrawString(selfie ? "Al llegar, mira a la cámara del aula… o marca con una selfie desde tu celular."
                                    : "Al llegar, mira a la cámara: tu asistencia se marca sola.", f2, suave, new RectangleF(120, 450, W - 240, 140), centro);

                void Bloque(Rectangle marco, string qr, int paso, string titulo, string detalle, Font fDetalle)
                {
                    using (var path = Tema.Redondeado(marco, 40)) g.DrawPath(linea, path);
                    using (var q = Qr(qr)) g.DrawImage(q, new Rectangle(marco.X + 50, marco.Y + 50, marco.Width - 100, marco.Width - 100));
                    float yt = marco.Y + marco.Width - 30;
                    if (paso > 0)
                    {
                        var tam = g.MeasureString(titulo, f3);
                        float xc = marco.X + (marco.Width - tam.Width - 90) / 2f;
                        g.FillEllipse(naranjaB, xc, yt + 6, 72, 72);
                        g.DrawString(paso.ToString(), fPaso, Brushes.White, new RectangleF(xc, yt + 6, 72, 72), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                        g.DrawString(titulo, f3, tinta, xc + 90, yt + 6);
                    }
                    else g.DrawString(titulo, f3, tinta, new RectangleF(marco.X, yt + 6, marco.Width, 80), centro);
                    g.DrawString(detalle, fDetalle, suave, new RectangleF(marco.X + 20, yt + 92, marco.Width - 40, marco.Bottom - yt - 100), centro);
                }

                bool wifi = !string.IsNullOrWhiteSpace(_cfg.WifiNombre);
                if (wifi)
                {
                    Bloque(new Rectangle(130, 640, 820, 1060), CodigosQr.TextoWifi(_cfg.WifiNombre, _cfg.WifiClave), 1, "Conéctate al WiFi",
                        $"Red: {_cfg.WifiNombre}" + (string.IsNullOrEmpty(_cfg.WifiClave) ? "" : $"\nClave: {_cfg.WifiClave}"), f2);
                    Bloque(new Rectangle(1050, 640, 820, 1060), url + principal, 2, selfie ? "Marca con una selfie" : "Consulta TU asistencia", url + principal, f4);
                }
                else
                    Bloque(new Rectangle(590, 640, 820, 1060), url + principal, 0, selfie ? "Marca con una selfie" : "Consulta TU asistencia", url + principal, f4);

                // código secundario, más pequeño
                using (var q = Qr(url + secundaria)) g.DrawImage(q, new Rectangle(260, 1800, 380, 380));
                g.DrawString(selfie ? "Consulta tu asistencia" : "Lista de hoy", f3, tinta, 700, 1880);
                g.DrawString(url + secundaria, f4, suave, 704, 1970);
                using (var pie = new SolidBrush(Tema.MarinoBruma)) g.FillRectangle(pie, 0, H - 200, W, 200);
                using (var naranja = new SolidBrush(Tema.Naranja)) g.FillRectangle(naranja, 0, H - 200, W, 8);
                g.DrawString("Control de asistencia con reconocimiento facial · funciona sin internet", f2, suave, new RectangleF(0, H - 140, W, 80), centro);
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
            vista.ShowDialog(Tema.Dueno(this));
        }

        void Guardar()
        {
            using var d = new SaveFileDialog { FileName = "cartel_asistencia.png", Filter = "PNG (*.png)|*.png" };
            if (d.ShowDialog(Tema.Dueno(this)) == DialogResult.OK) _cartel.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cartel?.Dispose();
            base.Dispose(disposing);
        }
    }
}
