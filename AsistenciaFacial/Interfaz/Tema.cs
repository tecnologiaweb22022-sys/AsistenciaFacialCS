using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>
    /// Sistema visual «Aula» (el mismo que la web, wwwroot/static/estilo.css): azul marino y naranja
    /// institucionales, barra lateral, baldosas de color, botones en forma de píldora y títulos en Bahnschrift.
    /// </summary>
    internal static class Tema
    {
        // ── marca ──
        public static readonly Color MarinoNoche = Color.FromArgb(6, 19, 42);
        public static readonly Color MarinoProfundo = Color.FromArgb(10, 29, 61);
        public static readonly Color Marino = Color.FromArgb(16, 41, 79);
        public static readonly Color MarinoMedio = Color.FromArgb(23, 57, 104);
        public static readonly Color MarinoNiebla = Color.FromArgb(159, 179, 211);
        public static readonly Color MarinoSuave = Color.FromArgb(203, 215, 234);
        public static readonly Color MarinoClaro = Color.FromArgb(229, 236, 247);
        public static readonly Color MarinoBruma = Color.FromArgb(241, 245, 251);
        public static readonly Color Naranja = Color.FromArgb(247, 132, 31);
        public static readonly Color NaranjaVivo = Color.FromArgb(255, 160, 77);
        public static readonly Color NaranjaOscuro = Color.FromArgb(227, 109, 13);
        public static readonly Color NaranjaClaro = Color.FromArgb(255, 236, 217);
        public static readonly Color NaranjaBruma = Color.FromArgb(255, 246, 238);

        // ── superficies y texto ──
        public static readonly Color Fondo = Color.FromArgb(234, 239, 247);
        public static readonly Color Papel = Color.White;
        public static readonly Color Tinta = Color.FromArgb(12, 27, 51);
        public static readonly Color Suave = Color.FromArgb(87, 100, 124);
        public static readonly Color Tenue = Color.FromArgb(141, 152, 172);
        public static readonly Color Linea = Color.FromArgb(227, 233, 242);
        public static readonly Color LineaFuerte = Color.FromArgb(205, 214, 228);
        public static readonly Color Camara = Color.FromArgb(6, 15, 32);

        // ── estados ──
        public static readonly Color Verde = Color.FromArgb(19, 154, 91);
        public static readonly Color Ambar = Color.FromArgb(217, 119, 6);
        public static readonly Color Celeste = Color.FromArgb(43, 127, 196);
        public static readonly Color Rojo = Color.FromArgb(214, 58, 81);
        public static readonly Color Gris = Color.FromArgb(120, 132, 153);

        // nombres anteriores (compatibilidad)
        public static readonly Color Azul = MarinoMedio;
        public static readonly Color Amarillo = Ambar;

        // ── tipografía ──
        /// <summary>Puntos que se suman a TODAS las letras del programa (zoom general).</summary>
        public const float Zoom = 2f;
        /// <summary>Factor con que se agrandan las ventanas para acompañar a las letras (texto base 9,75 pt).</summary>
        public const float Escala = (9.75f + Zoom) / 9.75f;
        public static Font Fuente(float puntos, FontStyle estilo = FontStyle.Regular, string familia = "Segoe UI") => new Font(familia, puntos + Zoom, estilo);

        static bool Existe(string familia) => new InstalledFontCollection().Families.Any(f => f.Name.Equals(familia, StringComparison.OrdinalIgnoreCase));
        /// <summary>Títulos y cifras: Bahnschrift (viene con Windows 10/11); si falta, Segoe UI.</summary>
        public static readonly string FamiliaTitulos = Existe("Bahnschrift SemiBold") ? "Bahnschrift SemiBold" : "Segoe UI Semibold";
        public static readonly bool HayIconos = Existe("Segoe MDL2 Assets");

        public static readonly Font Normal = Fuente(9.75f);
        public static readonly Font Campo = Fuente(10.5f);
        public static readonly Font Negrita = Fuente(9.75f, FontStyle.Bold);
        public static readonly Font Semi = Fuente(9.75f, familia: "Segoe UI Semibold");
        public static readonly Font SemiChica = Fuente(8.5f, familia: "Segoe UI Semibold");
        public static readonly Font Titulo = Fuente(13f, familia: FamiliaTitulos);
        public static readonly Font Seccion = Fuente(11.5f, familia: FamiliaTitulos);
        public static readonly Font TituloPagina = Fuente(19f, familia: FamiliaTitulos);
        public static readonly Font Grande = Fuente(20f, familia: FamiliaTitulos);
        public static readonly Font Cifra = Fuente(23f, familia: FamiliaTitulos);
        public static readonly Font Chica = Fuente(8.5f);
        public static readonly Font Mini = Fuente(7.5f, familia: "Segoe UI Semibold");
        public static readonly Font Iconos = new Font(HayIconos ? "Segoe MDL2 Assets" : "Segoe UI Symbol", 12f + Zoom);
        public static readonly Font IconosGrandes = new Font(HayIconos ? "Segoe MDL2 Assets" : "Segoe UI Symbol", 30f + Zoom);

        /// <summary>Íconos de «Segoe MDL2 Assets» (incluida en Windows 10/11).</summary>
        public static class Icono
        {
            public const string Panel = "", Alertas = "", Estudiantes = "", Academico = "", Reportes = "",
                Configuracion = "", Qr = "", Web = "", Publica = "", Usuario = "", Sonido = "",
                Silencio = "", Camara = "", Puntual = "", Reloj = "", Documento = "", Aviso = "";
        }

        /// <summary>
        /// Agranda la ventana y todo lo que contiene en la misma proporción que las letras
        /// (las medidas en píxeles del código están pensadas para letra de 9,75 pt), sin pasarse de la pantalla.
        /// </summary>
        public static void Escalar(Form f)
        {
            var minimo = f.MinimumSize;
            f.MinimumSize = Size.Empty;
            f.Scale(new SizeF(Escala, Escala));
            foreach (var g in Descendientes<DataGridView>(f))
                foreach (DataGridViewColumn c in g.Columns)
                    if (c.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill) c.Width = (int)(c.Width * Escala);
                    else c.MinimumWidth = (int)(c.MinimumWidth * Escala);
            if (!f.TopLevel) return;                                       // incrustada en la ventana principal
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            f.MinimumSize = new Size(Math.Min((int)(minimo.Width * Escala), area.Width), Math.Min((int)(minimo.Height * Escala), area.Height));
            if (f.Width > area.Width || f.Height > area.Height)
                f.Size = new Size(Math.Min(f.Width, area.Width), Math.Min(f.Height, area.Height));
            if (f.StartPosition == FormStartPosition.CenterScreen || f.StartPosition == FormStartPosition.CenterParent)
            {
                var r = f.StartPosition == FormStartPosition.CenterParent && f.Owner != null ? f.Owner.Bounds : area;
                int x = r.X + (r.Width - f.Width) / 2, y = r.Y + (r.Height - f.Height) / 2;
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(Math.Max(area.X, Math.Min(x, area.Right - f.Width)), Math.Max(area.Y, Math.Min(y, area.Bottom - f.Height)));
            }
        }

        public static IEnumerable<T> Descendientes<T>(Control c) where T : Control
        {
            foreach (Control h in c.Controls)
            {
                if (h is T t) yield return t;
                foreach (var n in Descendientes<T>(h)) yield return n;
            }
        }

        /// <summary>Dueño para los diálogos: la ventana principal aunque la página esté incrustada en ella.</summary>
        public static IWin32Window Dueno(Control c) => c.TopLevelControl as Form ?? c.FindForm();

        public static Color ColorEstado(string estado)
        {
            switch (estado)
            {
                case "puntual": return Verde;
                case "atraso": return Ambar;
                case "justificado": return Celeste;
                default: return Gris;
            }
        }

        public static string TextoEstado(string estado)
        {
            switch (estado)
            {
                case "puntual": return "Puntual";
                case "atraso": return "Atraso";
                case "justificado": return "Justificado";
                default: return "Falta";
            }
        }

        /// <summary>Fondo y letra de la «píldora» que muestra un estado en las tablas.</summary>
        public static (Color fondo, Color letra) ColoresPildora(string texto)
        {
            var verde = (Color.FromArgb(220, 245, 232), Color.FromArgb(11, 106, 61));
            var ambar = (Color.FromArgb(255, 239, 214), Color.FromArgb(147, 75, 3));
            var celeste = (Color.FromArgb(224, 238, 251), Color.FromArgb(24, 90, 146));
            var gris = (Color.FromArgb(237, 240, 245), Suave);
            string t = texto ?? "";
            if (t == "Falta") return gris;                                         // estado de asistencia
            string m = t.ToLowerInvariant();
            if (m.StartsWith("puntual") || m.StartsWith("sí") || m.Contains("activa") || m.StartsWith("configurad")) return verde;
            if (m.StartsWith("justificado")) return celeste;
            if (m.StartsWith("atraso") || m.StartsWith("falta") || m.StartsWith("sin ")) return ambar;
            if (m.Contains("semestre")) return (MarinoClaro, MarinoMedio);
            return gris;
        }

        // ── utilidades de dibujo ──
        public static GraphicsPath Redondeado(RectangleF r, float radio)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radio * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Mezclar(Color a, Color b, double t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        static bool EsOscuro(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 110;

        /// <summary>Color al pasar el mouse: los oscuros se aclaran, los claros se oscurecen.</summary>
        public static Color Hover(Color c) => EsOscuro(c) ? Mezclar(c, Color.White, 0.12) : Mezclar(c, Color.Black, 0.08);

        /// <summary>Sombra suave bajo una forma redondeada (varias capas casi transparentes).</summary>
        public static void Sombra(Graphics g, RectangleF r, float radio, int capas = 6, int alfa = 7)
        {
            for (int i = capas; i >= 1; i--)
                using (var p = Redondeado(new RectangleF(r.X - i * 0.5f, r.Y + i * 0.7f, r.Width + i, r.Height + i * 0.6f), radio + i))
                using (var b = new SolidBrush(Color.FromArgb(alfa, MarinoProfundo))) g.FillPath(b, p);
        }

        /// <summary>Logotipo: cuadro naranja con un rostro dentro de un marco de enfoque.</summary>
        public static void DibujarLogo(Graphics g, RectangleF r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Redondeado(r, r.Width * 0.3f))
            using (var b = new LinearGradientBrush(r, NaranjaVivo, NaranjaOscuro, 45f))
                g.FillPath(b, path);
            float k = r.Width * 0.62f / 24f, x0 = r.X + r.Width * 0.19f, y0 = r.Y + r.Height * 0.19f;
            PointF P(float x, float y) => new PointF(x0 + x * k, y0 + y * k);
            using var pen = new Pen(Color.White, Math.Max(1.6f, 2.1f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[] { P(3, 8), P(3, 3), P(8, 3) });
            g.DrawLines(pen, new[] { P(16, 3), P(21, 3), P(21, 8) });
            g.DrawLines(pen, new[] { P(21, 16), P(21, 21), P(16, 21) });
            g.DrawLines(pen, new[] { P(8, 21), P(3, 21), P(3, 16) });
            g.DrawEllipse(pen, x0 + 9 * k, y0 + 7 * k, 6 * k, 6 * k);
            g.DrawBezier(pen, P(7.5f, 17.5f), P(8.5f, 15.5f), P(10.1f, 14.5f), P(12, 14.5f));
            g.DrawBezier(pen, P(12, 14.5f), P(13.9f, 14.5f), P(15.5f, 15.5f), P(16.5f, 17.5f));
        }

        static readonly Color[] ColoresAvatar =
        {
            Color.FromArgb(23, 57, 104), Color.FromArgb(227, 109, 13), Color.FromArgb(19, 154, 91), Color.FromArgb(43, 127, 196),
            Color.FromArgb(124, 77, 196), Color.FromArgb(196, 63, 111), Color.FromArgb(14, 133, 140),
        };

        public static string Iniciales(string nombre) =>
            string.Concat((nombre ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));

        /// <summary>Círculo de color con las iniciales (el color depende del nombre, siempre el mismo).</summary>
        public static void DibujarAvatar(Graphics g, Rectangle r, string nombre, Font fuente = null)
        {
            int h = 0;
            foreach (char c in nombre ?? "") h = h * 31 + c;
            var color = ColoresAvatar[Math.Abs(h % ColoresAvatar.Length)];
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Mezclar(color, Color.White, 0.84))) g.FillEllipse(b, r);
            TextRenderer.DrawText(g, Iniciales(nombre), fuente ?? Mini, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // ── controles ──
        /// <summary>
        /// Botón en forma de píldora. Con fondo = <see cref="Suave"/> se dibuja como botón secundario (contorno).
        /// </summary>
        public static BotonModerno Boton(string texto, Color fondo, EventHandler clic, Color? letra = null)
        {
            var b = new BotonModerno { Text = texto, Secundario = fondo == Suave };
            if (!b.Secundario) b.BackColor = fondo;
            b.Degradado = fondo == Naranja;
            b.ForeColor = letra ?? (!EsOscuroOVivo(fondo) ? Tinta : Color.White);
            if (clic != null) b.Click += clic;
            return b;
        }

        static bool EsOscuroOVivo(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 200;

        /// <summary>Botón para superficies oscuras (barra lateral, tarjeta de la cámara).</summary>
        public static BotonModerno BotonBarra(string texto, EventHandler clic)
        {
            var b = new BotonModerno { Text = texto, BackColor = Color.FromArgb(28, 52, 92), ForeColor = Color.White, Font = SemiChica };
            b.Click += clic;
            return b;
        }

        public static Label Etiqueta(string texto, Font fuente = null, Color? color = null, Padding? margen = null) => new Label
        {
            Text = texto, AutoSize = true, Font = fuente ?? Normal, ForeColor = color ?? Suave, BackColor = Color.Transparent,
            Margin = margen ?? new Padding(4, 10, 2, 0),
        };

        /// <summary>Barra de herramientas horizontal (fila de controles que se acomodan solos).</summary>
        public static FlowLayoutPanel Fila(DockStyle dock = DockStyle.Top, Padding? relleno = null) => new FlowLayoutPanel
        {
            Dock = dock, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent,
            Padding = relleno ?? new Padding(10, 8, 10, 8), WrapContents = true,
        };

        /// <summary>Pone la cabecera y el estilo base a un formulario.</summary>
        public static Cabecera Preparar(Form f, string titulo, string subtitulo = null, Color? fondo = null)
        {
            f.Font = Normal;
            f.Load += (s, e) => Escalar(f);          // al cargar, cuando todo ya está armado
            f.BackColor = fondo ?? Fondo;
            f.ShowIcon = false;
            var c = new Cabecera { Titulo = titulo, Subtitulo = subtitulo };
            f.Controls.Add(c);
            return c;
        }

        /// <summary>Barra inferior de un diálogo con los botones alineados a la derecha (en el orden dado: el último queda al extremo).</summary>
        public static Panel Pie(Form f, params Control[] botones)
        {
            var pie = new Lienzo { Dock = DockStyle.Bottom, Height = 64, BackColor = Papel };
            pie.Paint += (s, e) => { using var p = new Pen(Linea); e.Graphics.DrawLine(p, 0, 0, pie.Width, 0); };
            var flujo = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 13, 12, 0), BackColor = Color.Transparent, WrapContents = false };
            for (int i = botones.Length - 1; i >= 0; i--) flujo.Controls.Add(botones[i]);   // se pasan de izquierda a derecha
            pie.Controls.Add(flujo);
            f.Controls.Add(pie);
            return pie;
        }

        public static void EstiloCampo(Control c)
        {
            c.Font = Campo;
            if (c is TextBox t) { t.BorderStyle = BorderStyle.FixedSingle; t.BackColor = Color.White; }
            if (c is ComboBox cb)
            {
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Color.White;
                cb.ForeColor = Tinta;
                if (cb.DropDownStyle == ComboBoxStyle.DropDownList && cb.DrawMode == DrawMode.Normal)
                {
                    // dibujado propio: fondo blanco (el modo plano lo pinta gris) y selección naranja clara
                    cb.DrawMode = DrawMode.OwnerDrawFixed;
                    cb.ItemHeight = Math.Max(cb.ItemHeight, TextRenderer.MeasureText("Ág", Campo).Height + 6);
                    cb.DrawItem += (s, e) =>
                    {
                        bool edicion = (e.State & DrawItemState.ComboBoxEdit) != 0;
                        bool marcado = (e.State & DrawItemState.Selected) != 0 && !edicion;
                        using (var b = new SolidBrush(marcado ? NaranjaClaro : Color.White)) e.Graphics.FillRectangle(b, e.Bounds);
                        if (e.Index >= 0)
                            TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                                cb.Enabled ? Tinta : Tenue, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    };
                }
            }
        }

        /// <summary>Tabla limpia: encabezado claro en mayúsculas, filas altas, fila elegida con borde naranja.</summary>
        public static void EstiloGrilla(DataGridView g)
        {
            g.Dock = DockStyle.Fill;
            g.BackgroundColor = Papel;
            g.BorderStyle = BorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.GridColor = Linea;
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.ReadOnly = true;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = Mini.Height + 26;
            var h = g.ColumnHeadersDefaultCellStyle;
            h.BackColor = Papel;
            h.ForeColor = Tenue;
            h.SelectionBackColor = Papel;
            h.SelectionForeColor = Tenue;
            h.Font = Mini;
            h.Padding = new Padding(10, 0, 4, 0);
            h.WrapMode = DataGridViewTriState.False;
            var c = g.DefaultCellStyle;
            c.BackColor = Papel;
            c.ForeColor = Tinta;
            c.Font = Normal;
            c.Padding = new Padding(10, 0, 4, 0);
            c.SelectionBackColor = NaranjaBruma;
            c.SelectionForeColor = Tinta;
            g.AlternatingRowsDefaultCellStyle.BackColor = Papel;
            g.RowTemplate.Height = Normal.Height + 24;
            // encabezado: texto en mayúsculas espaciado y línea inferior
            g.CellPainting += (s, e) =>
            {
                if (e.RowIndex != -1 || e.ColumnIndex < 0) return;
                using (var b = new SolidBrush(Papel)) e.Graphics.FillRectangle(b, e.CellBounds);
                using (var p = new Pen(Linea, 2)) e.Graphics.DrawLine(p, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, (e.Value as string ?? "").ToUpperInvariant(), Mini, new Rectangle(e.CellBounds.X + 10, e.CellBounds.Y, e.CellBounds.Width - 12, e.CellBounds.Height),
                    Tenue, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.Handled = true;
            };
            // sin el recuadro punteado de la celda activa
            g.RowPrePaint += (s, e) => e.PaintParts &= ~DataGridViewPaintParts.Focus;
            // fila elegida: barra naranja a la izquierda
            g.RowPostPaint += (s, e) =>
            {
                if ((e.State & DataGridViewElementStates.Selected) == 0) return;
                using var b = new SolidBrush(Naranja);
                e.Graphics.FillRectangle(b, e.RowBounds.Left, e.RowBounds.Top + 6, 4, e.RowBounds.Height - 12);
            };
            typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(g, true);
        }

        /// <summary>Antepone al texto de una columna un círculo con las iniciales.</summary>
        public static void Avatares(DataGridView g, string columna, Func<int, string> nombreDeFila = null)
        {
            g.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || g.Columns[e.ColumnIndex].Name != columna) return;
                e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
                string t = e.FormattedValue as string ?? "";
                int d = Math.Min(e.CellBounds.Height - 14, 34);
                var r = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - d) / 2, d, d);
                DibujarAvatar(e.Graphics, r, nombreDeFila?.Invoke(e.RowIndex) ?? t);
                var estilo = e.CellStyle;
                TextRenderer.DrawText(e.Graphics, t, estilo.Font ?? Normal, new Rectangle(r.Right + 10, e.CellBounds.Y, e.CellBounds.Right - r.Right - 12, e.CellBounds.Height),
                    estilo.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                e.Handled = true;
            };
        }

        /// <summary>Dibuja el texto de una columna como píldora de color (Puntual, Atraso, …).</summary>
        public static void Pildoras(DataGridView g, string columna)
        {
            g.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || g.Columns[e.ColumnIndex].Name != columna) return;
                string t = e.FormattedValue as string ?? "";
                e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
                if (t.Length > 0)
                {
                    var (fondo, letra) = ColoresPildora(t);
                    var tam = TextRenderer.MeasureText(t, SemiChica);
                    int alto = SemiChica.Height + 8, ancho = Math.Min(e.CellBounds.Width - 12, tam.Width + 24);
                    var r = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - alto) / 2, ancho, alto);
                    var gr = e.Graphics;
                    gr.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = Redondeado(r, alto / 2f)) using (var b = new SolidBrush(fondo)) gr.FillPath(b, path);
                    using (var b = new SolidBrush(letra)) gr.FillEllipse(b, r.X + 10, r.Y + alto / 2 - 3, 7, 7);
                    TextRenderer.DrawText(gr, t, SemiChica, new Rectangle(r.X + 20, r.Y, r.Width - 22, r.Height), letra,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
                e.Handled = true;
            };
        }

        /// <summary>Dibuja un porcentaje ("85,0 %") como barra de progreso con color verde/ámbar/rojo.</summary>
        public static void BarrasPorcentaje(DataGridView g, string columna)
        {
            g.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || g.Columns[e.ColumnIndex].Name != columna) return;
                string t = e.FormattedValue as string ?? "";
                if (!double.TryParse(t.Replace("%", "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double p)) return;
                e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
                Color c = p >= 80 ? Verde : p >= 60 ? Ambar : Rojo;
                var gr = e.Graphics;
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                int anchoTexto = TextRenderer.MeasureText("100,0 %", Negrita).Width + 4;
                var pista = new RectangleF(e.CellBounds.X + 10, e.CellBounds.Y + e.CellBounds.Height / 2f - 5, Math.Max(10, e.CellBounds.Width - anchoTexto - 22), 10);
                using (var path = Redondeado(pista, 5)) using (var b = new SolidBrush(MarinoClaro)) gr.FillPath(b, path);
                var lleno = new RectangleF(pista.X, pista.Y, (float)(pista.Width * Math.Min(100, Math.Max(0, p)) / 100), pista.Height);
                if (lleno.Width > 1) using (var path = Redondeado(lleno, 5)) using (var b = new SolidBrush(c)) gr.FillPath(b, path);
                TextRenderer.DrawText(gr, t, Negrita, new Rectangle(e.CellBounds.Right - anchoTexto - 6, e.CellBounds.Y, anchoTexto, e.CellBounds.Height), c,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
                e.Handled = true;
            };
        }

        /// <summary>Muestra las fotos de una columna de imágenes recortadas en círculo.</summary>
        public static void FotosRedondas(DataGridView g, string columna)
        {
            g.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || g.Columns[e.ColumnIndex].Name != columna) return;
                e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
                int d = Math.Min(e.CellBounds.Height - 10, e.CellBounds.Width - 10);
                var r = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - d) / 2, e.CellBounds.Y + (e.CellBounds.Height - d) / 2, d, d);
                var gr = e.Graphics;
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (e.Value is Image img)
                {
                    using var path = new GraphicsPath();
                    path.AddEllipse(r);
                    var antes = gr.Clip;
                    gr.SetClip(path, CombineMode.Intersect);
                    float k = Math.Max((float)d / img.Width, (float)d / img.Height);
                    gr.DrawImage(img, r.X + (d - img.Width * k) / 2, r.Y + (d - img.Height * k) / 2, img.Width * k, img.Height * k);
                    gr.Clip = antes;
                    using var p = new Pen(NaranjaClaro, 2.5f);
                    gr.DrawEllipse(p, r);
                }
                else
                {
                    using var b = new SolidBrush(MarinoClaro);
                    gr.FillEllipse(b, r);
                    TextRenderer.DrawText(gr, "?", Negrita, r, Tenue, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                e.Handled = true;
            };
        }

        public static Image ImagenDeBytes(byte[] jpg)
        {
            if (jpg == null || jpg.Length == 0) return null;
            using var ms = new MemoryStream(jpg);
            using var tmp = Image.FromStream(ms);
            return new Bitmap(tmp);                     // copia: no depende del stream
        }

        public static string Hora(double unix) => Nucleo.BaseDatos.Local(unix).ToString("HH:mm:ss");
    }

    // ═════════════════════════════ controles propios ═════════════════════════════

    /// <summary>Panel con doble búfer (para dibujar sin parpadeo).</summary>
    internal class Lienzo : Panel
    {
        public Lienzo()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    /// <summary>Botón en forma de píldora, con efecto al pasar el mouse y anillo de foco naranja.</summary>
    internal sealed class BotonModerno : Button
    {
        bool _sobre, _presionado;
        public bool Secundario { get; set; }
        /// <summary>Relleno en degradado (para la acción principal naranja).</summary>
        public bool Degradado { get; set; }
        /// <summary>Radio de las esquinas; negativo = píldora (la mitad del alto).</summary>
        public int Radio { get; set; } = -1;

        public BotonModerno()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Tema.Semi;
            BackColor = Tema.MarinoMedio;
            ForeColor = Color.White;
            Padding = new Padding(18, 7, 18, 7);
            Margin = new Padding(4);
            MinimumSize = new Size(0, 36);
            AutoSize = true;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var t = TextRenderer.MeasureText(Text, Font);
            return new Size(Math.Max(MinimumSize.Width, t.Width + Padding.Horizontal), Math.Max(MinimumSize.Height, t.Height + Padding.Vertical));
        }

        protected override void OnMouseEnter(EventArgs e) { _sobre = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _sobre = _presionado = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _presionado = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _presionado = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // las esquinas muestran el fondo real del contenedor (aunque sea un degradado)
            if (Parent != null)
            {
                var estado = g.Save();
                g.TranslateTransform(-Left, -Top);
                var pe = new PaintEventArgs(g, new Rectangle(Left, Top, Width, Height));
                InvokePaintBackground(Parent, pe);
                InvokePaint(Parent, pe);
                g.Restore(estado);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.75f, 0.75f, Width - 2f, Height - 2f);
            float radio = Radio < 0 ? r.Height / 2f : Radio;
            Color letra = Secundario ? Tema.Marino : ForeColor;
            using (var path = Tema.Redondeado(r, radio))
            {
                if (Secundario)
                {
                    Color fondo = _presionado ? Tema.MarinoClaro : _sobre ? Tema.MarinoBruma : Color.FromArgb(0, 255, 255, 255);
                    using (var b = new SolidBrush(fondo)) g.FillPath(b, path);
                    using (var p = new Pen(_sobre ? Tema.MarinoNiebla : Tema.LineaFuerte, 1.5f)) g.DrawPath(p, path);
                }
                else
                {
                    Color fondo = BackColor;
                    if (_presionado) fondo = Tema.Mezclar(fondo, Color.Black, 0.15);
                    else if (_sobre) fondo = Tema.Hover(fondo);
                    if (Degradado && Enabled)
                        using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height + 1), Tema.Mezclar(Tema.NaranjaVivo, fondo, _sobre ? 0.4 : 0.15), Tema.NaranjaOscuro, 30f))
                            g.FillPath(b, path);
                    else
                        using (var b = new SolidBrush(fondo)) g.FillPath(b, path);
                }
                if (!Enabled)
                {
                    using (var b = new SolidBrush(Color.FromArgb(150, Tema.Fondo))) g.FillPath(b, path);
                    letra = Tema.Mezclar(letra, Tema.Fondo, 0.4);
                }
            }
            if (Focused && ShowFocusCues)
                using (var path = Tema.Redondeado(RectangleF.Inflate(r, -2f, -2f), Math.Max(2, radio - 2)))
                using (var p = new Pen(Color.FromArgb(210, Secundario ? Tema.Naranja : Color.White), 2f)) g.DrawPath(p, path);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, letra,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Superficie blanca con esquinas muy redondeadas y sombra suave (sin borde).</summary>
    internal class Tarjeta : Panel
    {
        Color _relleno = Tema.Papel;
        public int Radio { get; set; } = 18;
        public Color Borde { get; set; } = Color.Empty;
        public bool ConSombra { get; set; } = true;
        public Color Relleno { get => _relleno; set { _relleno = value; Invalidate(true); } }

        public Tarjeta()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Padding = new Padding(14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = ConSombra ? new RectangleF(3, 1, Width - 7, Height - 7) : new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (ConSombra) Tema.Sombra(g, r, Radio);
            using var path = Tema.Redondeado(r, Radio);
            using (var b = new SolidBrush(_relleno)) g.FillPath(b, path);
            if (Borde != Color.Empty && Borde != Color.Transparent) using (var p = new Pen(Borde)) g.DrawPath(p, path);
            base.OnPaint(e);
        }
    }

    /// <summary>
    /// Encabezado claro: título grande en Bahnschrift con un acento naranja a la izquierda.
    /// En los diálogos es blanco con el logotipo; en la ventana principal es el título de la página.
    /// </summary>
    internal sealed class Cabecera : Lienzo
    {
        string _titulo = "", _subtitulo = "";
        bool _pagina;
        public string Titulo { get => _titulo; set { _titulo = value ?? ""; Invalidate(); } }
        public string Subtitulo { get => _subtitulo; set { _subtitulo = value ?? ""; Invalidate(); } }
        /// <summary>true = título de página (fondo gris azulado, sin logotipo, letra grande).</summary>
        public bool EsPagina
        {
            get => _pagina;
            set { _pagina = value; BackColor = value ? Tema.Fondo : Tema.Papel; Height = value ? 92 : 72; Invalidate(); }
        }
        /// <summary>Contenedor a la derecha para botones o insignias.</summary>
        public FlowLayoutPanel Derecha { get; }

        public Cabecera()
        {
            Dock = DockStyle.Top;
            Height = 72;
            BackColor = Tema.Papel;
            Derecha = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                BackColor = Color.Transparent, Padding = new Padding(0, 18, 16, 0),
            };
            Controls.Add(Derecha);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int x = _pagina ? 4 : 20;
            if (!_pagina)
            {
                float lado = Math.Min(Height * 0.56f, Height - 16);
                Tema.DibujarLogo(g, new RectangleF(x, (Height - lado) / 2, lado, lado));
                x += (int)lado + 14;
                using var p = new Pen(Tema.Linea);
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            }
            var fT = _pagina ? Tema.TituloPagina : Tema.Titulo;
            int ancho = Math.Max(10, Width - x - Derecha.Width - 12);
            bool dos = _subtitulo.Length > 0;
            int hT = fT.Height, hS = Tema.Chica.Height, y0 = (Height - hT - (dos ? hS + 2 : 0)) / 2;
            if (_pagina)
                using (var b = new SolidBrush(Tema.Naranja)) g.FillRectangle(b, 0, y0 + 4, 5, hT + (dos ? hS : 0) - 4);
            TextRenderer.DrawText(g, _titulo, fT, new Rectangle(x + (_pagina ? 14 : 0), y0, ancho, hT), Tema.MarinoProfundo,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (dos)
                TextRenderer.DrawText(g, _subtitulo, Tema.Chica, new Rectangle(x + (_pagina ? 15 : 0), y0 + hT + 2, ancho, hS), Tema.Suave,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Baldosa de cifra del día: fondo de color en degradado, número grande y un ícono de fondo.</summary>
    internal sealed class Indicador : Control
    {
        string _valor = "–";
        public string Titulo { get; set; } = "";
        public Color Acento { get; set; } = Tema.MarinoMedio;
        public string Icono { get; set; } = "";
        public string Valor { get => _valor; set { if (_valor != value) { _valor = value; Invalidate(); } } }

        public Indicador()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Dock = DockStyle.Fill;
            Margin = new Padding(0, 0, 14, 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(3, 1, Width - 7, Height - 7);
            if (r.Width < 10 || r.Height < 10) return;
            Tema.Sombra(g, r, 18, 5, 9);
            using var path = Tema.Redondeado(r, 18);
            using (var b = new LinearGradientBrush(r, Tema.Mezclar(Acento, Color.White, 0.14), Tema.Mezclar(Acento, Color.Black, 0.22), 35f)) g.FillPath(b, path);
            var antes = g.Clip;
            g.SetClip(path, CombineMode.Intersect);
            using (var b = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) g.FillEllipse(b, r.Right - r.Height * 0.85f, r.Y - r.Height * 0.45f, r.Height * 1.2f, r.Height * 1.2f);
            if (Icono.Length > 0 && Tema.HayIconos)
                TextRenderer.DrawText(g, Icono, Tema.IconosGrandes, new Rectangle((int)(r.Right - r.Height * 0.95f), (int)r.Y, (int)(r.Height * 0.9f), (int)r.Height),
                    Tema.Mezclar(Acento, Color.White, 0.3), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            g.Clip = antes;
            int hT = Tema.Mini.Height;
            TextRenderer.DrawText(g, Titulo.ToUpperInvariant(), Tema.Mini, new Rectangle(20, 14, Width - 30, hT), Color.FromArgb(225, 236, 247), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _valor, Tema.Cifra, new Rectangle(17, 14 + hT, Width - 26, Height - 22 - hT), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Insignia redondeada con texto (p. ej. «● EN VIVO»).</summary>
    internal sealed class Insignia : Control
    {
        Color _relleno = Tema.Gris;
        public Color Relleno { get => _relleno; set { _relleno = value; Invalidate(); } }
        /// <summary>Punto de color antes del texto (Color.Empty = sin punto).</summary>
        public Color Punto { get; set; } = Color.Empty;

        public Insignia()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            Font = Tema.SemiChica;
            Height = 28;
            Margin = new Padding(6, 4, 0, 0);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            int ancho = TextRenderer.MeasureText(Text, Font).Width + 28 + (Punto.IsEmpty ? 0 : 14);
            if (Anchor.HasFlag(AnchorStyles.Right) && Parent != null && !(Parent is FlowLayoutPanel)) Left = Right - ancho;
            Width = ancho;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Tema.Redondeado(r, Height / 2f))
            using (var b = new SolidBrush(_relleno)) g.FillPath(b, path);
            int x = 0;
            if (!Punto.IsEmpty)
            {
                using (var b = new SolidBrush(Punto)) g.FillEllipse(b, 14, Height / 2f - 4.5f, 9, 9);
                x = 12;
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x, Height), ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Barra de progreso plana y redondeada (naranja).</summary>
    internal sealed class BarraProgreso : Control
    {
        int _valor, _maximo = 100;
        public int Valor { get => _valor; set { _valor = Math.Max(0, Math.Min(_maximo, value)); Invalidate(); } }
        public int Maximo { get => _maximo; set { _maximo = Math.Max(1, value); Invalidate(); } }

        public BarraProgreso()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 12;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Tema.Redondeado(r, Height / 2f)) using (var b = new SolidBrush(Tema.MarinoClaro)) g.FillPath(b, path);
            float w = r.Width * _valor / _maximo;
            if (w >= Height)
                using (var path = Tema.Redondeado(new RectangleF(0, 0, w, r.Height), Height / 2f))
                using (var b = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(1, w), Height), Tema.NaranjaVivo, Tema.NaranjaOscuro, 0f))
                    g.FillPath(b, path);
        }
    }

    /// <summary>Pestañas en forma de control segmentado: una «vía» gris azulada con la opción activa en azul marino.</summary>
    internal sealed class Pestanas : TabControl
    {
        const int TCM_ADJUSTRECT = 0x1328;
        int _sobre = -1;
        public Color ColorFranja { get; set; } = Tema.Papel;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        public Pestanas()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(220, Tema.Semi.Height + 22);
            Font = Tema.Semi;
            Cursor = Cursors.Hand;
            Padding = new Point(10, 4);
        }

        protected override void WndProc(ref Message m)
        {
            // área de las páginas: todo lo que está debajo de la franja, sin bordes
            if (m.Msg == TCM_ADJUSTRECT && m.WParam == IntPtr.Zero && !DesignMode)
            {
                var r = Marshal.PtrToStructure<RECT>(m.LParam);
                r.Top += ItemSize.Height + 16;
                Marshal.StructureToPtr(r, m.LParam, false);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = -1;
            for (int k = 0; k < TabCount; k++) if (GetTabRect(k).Contains(e.Location)) i = k;
            if (i != _sobre) { _sobre = i; Invalidate(new Rectangle(0, 0, Width, ItemSize.Height + 16)); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _sobre = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int alto = ItemSize.Height + 16;
            using (var b = new SolidBrush(ColorFranja)) g.FillRectangle(b, 0, 0, Width, alto);
            if (TabCount == 0) return;
            var primera = GetTabRect(0);
            var ultima = GetTabRect(TabCount - 1);
            var via = new RectangleF(primera.X - 4, 4, ultima.Right - primera.X + 8, ItemSize.Height + 6);
            using (var path = Tema.Redondeado(via, via.Height / 2)) using (var b = new SolidBrush(Tema.MarinoBruma)) g.FillPath(b, path);
            for (int i = 0; i < TabCount; i++)
            {
                var t = GetTabRect(i);
                var r = new RectangleF(t.X, 7, t.Width, ItemSize.Height);
                bool activa = i == SelectedIndex;
                if (activa || i == _sobre)
                    using (var path = Tema.Redondeado(r, r.Height / 2))
                    using (var b = new SolidBrush(activa ? Tema.Marino : Tema.MarinoClaro)) g.FillPath(b, path);
                TextRenderer.DrawText(g, TabPages[i].Text, Tema.Semi, Rectangle.Round(r), activa ? Color.White : Tema.Suave,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
    }

    /// <summary>
    /// Barra lateral de navegación (azul marino): logotipo, opciones con ícono, contador de alertas
    /// y tarjeta del usuario abajo. Se dibuja completa y avisa qué opción se eligió.
    /// </summary>
    internal sealed class BarraLateral : Lienzo
    {
        public sealed class Opcion
        {
            public string Clave = "", Icono = "", Texto = "", Grupo = "";
            public bool Abajo;
            public int Contador;
            internal RectangleF Area;
        }

        readonly List<Opcion> _opciones = new List<Opcion>();
        string _activa = "", _usuario = "", _rol = "", _gestion = "";
        int _sobre = -1;
        RectangleF _tarjetaUsuario;
        public event Action<string> Elegida;

        public BarraLateral()
        {
            Dock = DockStyle.Left;
            Width = 252;
            BackColor = Tema.MarinoProfundo;
        }

        public string Activa { get => _activa; set { _activa = value ?? ""; Invalidate(); } }
        public void Usuario(string nombre, string rol, string gestion) { _usuario = nombre ?? ""; _rol = rol ?? ""; _gestion = gestion ?? ""; Invalidate(); }
        public void Limpiar() { _opciones.Clear(); Invalidate(); }
        public void Agregar(string clave, string icono, string texto, string grupo = "", bool abajo = false) =>
            _opciones.Add(new Opcion { Clave = clave, Icono = icono, Texto = texto, Grupo = grupo, Abajo = abajo });
        public void Contador(string clave, int n) { var o = _opciones.FirstOrDefault(x => x.Clave == clave); if (o != null && o.Contador != n) { o.Contador = n; Invalidate(); } }
        public void Cambiar(string clave, string icono, string texto) { var o = _opciones.FirstOrDefault(x => x.Clave == clave); if (o != null) { o.Icono = icono; o.Texto = texto; Invalidate(); } }

        int AltoOpcion => Tema.Semi.Height + 20;

        void Distribuir()
        {
            float m = 14, y = 26 + Math.Min(46, Width * 0.18f) + 26, alto = AltoOpcion, grupoAlto = Tema.Mini.Height + 18;
            string grupo = null;
            foreach (var o in _opciones.Where(o => !o.Abajo))
            {
                if (o.Grupo != grupo) { grupo = o.Grupo; if (grupo.Length > 0) y += grupoAlto; }
                o.Area = new RectangleF(m, y, Width - 2 * m, alto);
                y += alto + 4;
            }
            float tarjeta = Tema.Semi.Height + Tema.Chica.Height + 30;
            _tarjetaUsuario = new RectangleF(m, Height - m - tarjeta, Width - 2 * m, tarjeta);
            float yb = _tarjetaUsuario.Y - 8;
            foreach (var o in _opciones.Where(o => o.Abajo).Reverse())
            {
                yb -= alto;
                o.Area = new RectangleF(m, yb, Width - 2 * m, alto);
                yb -= 4;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Distribuir();
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = ClientRectangle;
            using (var b = new LinearGradientBrush(r, Tema.MarinoProfundo, Tema.MarinoNoche, 90f)) g.FillRectangle(b, r);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-r.Width * 0.6f, -r.Width * 0.6f, r.Width * 1.4f, r.Width * 1.2f);
                using var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(46, Tema.Naranja), SurroundColors = new[] { Color.FromArgb(0, Tema.Naranja) } };
                g.FillPath(pb, path);
            }
            // marca
            float lado = Math.Min(46, Width * 0.18f);
            Tema.DibujarLogo(g, new RectangleF(20, 24, lado, lado));
            int xt = (int)(20 + lado + 12);
            TextRenderer.DrawText(g, "Asistencia", Tema.Titulo, new Rectangle(xt, 22, Width - xt - 10, Tema.Titulo.Height), Color.White, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _gestion.Length > 0 ? "Gestión " + _gestion : "Reconocimiento facial", Tema.Chica,
                new Rectangle(xt, 24 + Tema.Titulo.Height, Width - xt - 10, Tema.Chica.Height), Tema.MarinoNiebla, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            // opciones
            string grupo = null;
            for (int i = 0; i < _opciones.Count; i++)
            {
                var o = _opciones[i];
                if (!o.Abajo && o.Grupo != grupo)
                {
                    grupo = o.Grupo;
                    if (grupo.Length > 0)
                        TextRenderer.DrawText(g, grupo.ToUpperInvariant(), Tema.Mini, new Rectangle((int)o.Area.X + 12, (int)o.Area.Y - Tema.Mini.Height - 8, Width, Tema.Mini.Height),
                            Color.FromArgb(120, 203, 215, 234), TextFormatFlags.Left);
                }
                bool activa = o.Clave == _activa, sobre = i == _sobre;
                using (var path = Tema.Redondeado(o.Area, 14))
                {
                    if (activa)
                    {
                        Tema.Sombra(g, o.Area, 14, 4, 14);
                        using var b = new LinearGradientBrush(o.Area, Tema.NaranjaVivo, Tema.NaranjaOscuro, 20f);
                        g.FillPath(b, path);
                    }
                    else if (sobre) using (var b = new SolidBrush(Color.FromArgb(20, 255, 255, 255))) g.FillPath(b, path);
                }
                Color letra = activa || sobre ? Color.White : Tema.MarinoSuave;
                int x = (int)o.Area.X + 14, alto = (int)o.Area.Height, y = (int)o.Area.Y;
                if (Tema.HayIconos)
                    TextRenderer.DrawText(g, o.Icono, Tema.Iconos, new Rectangle(x, y, 26, alto), letra, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, o.Texto, Tema.Semi, new Rectangle(x + 38, y, (int)o.Area.Width - 60, alto), letra, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (o.Contador > 0)
                {
                    string n = o.Contador > 99 ? "99+" : o.Contador.ToString();
                    int an = Math.Max(24, TextRenderer.MeasureText(n, Tema.Mini).Width + 12), al = Tema.Mini.Height + 6;
                    var rc = new RectangleF(o.Area.Right - an - 10, o.Area.Y + (o.Area.Height - al) / 2, an, al);
                    using (var path = Tema.Redondeado(rc, al / 2f)) using (var b = new SolidBrush(activa ? Color.White : Tema.Rojo)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, n, Tema.Mini, Rectangle.Round(rc), activa ? Tema.Rojo : Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
            // usuario
            if (_usuario.Length > 0)
            {
                using (var path = Tema.Redondeado(_tarjetaUsuario, 16)) using (var b = new SolidBrush(Color.FromArgb(16, 255, 255, 255))) g.FillPath(b, path);
                int d = (int)(_tarjetaUsuario.Height - 22);
                var ra = new Rectangle((int)_tarjetaUsuario.X + 12, (int)(_tarjetaUsuario.Y + (_tarjetaUsuario.Height - d) / 2), d, d);
                using (var path = Tema.Redondeado(ra, d * 0.32f)) using (var b = new LinearGradientBrush(ra, Tema.NaranjaVivo, Tema.NaranjaOscuro, 45f)) g.FillPath(b, path);
                TextRenderer.DrawText(g, Tema.Iniciales(_usuario), Tema.Semi, ra, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int xu = ra.Right + 10, au = (int)_tarjetaUsuario.Right - xu - 8;
                TextRenderer.DrawText(g, _usuario, Tema.Semi, new Rectangle(xu, ra.Y, au, Tema.Semi.Height), Color.White, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, _rol, Tema.Chica, new Rectangle(xu, ra.Bottom - Tema.Chica.Height, au, Tema.Chica.Height), Tema.MarinoNiebla, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }

        int Buscar(Point p)
        {
            for (int i = 0; i < _opciones.Count; i++) if (_opciones[i].Area.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = Buscar(e.Location);
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            if (i != _sobre) { _sobre = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _sobre = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = Buscar(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left) Elegida?.Invoke(_opciones[i].Clave);
        }
    }

    /// <summary>Sonidos generados en memoria (sin archivos).</summary>
    internal static class Sonido
    {
        static byte[] Wav(int[] notas, double dur = 0.15)
        {
            const int frec = 22050;
            var m = new List<short>();
            foreach (int f in notas)
            {
                int n = (int)(frec * dur);
                for (int i = 0; i < n; i++)
                {
                    double env = Math.Min(1, i / (frec * 0.01)) * Math.Exp(-3.0 * i / n);
                    m.Add((short)(2 / Math.PI * Math.Asin(Math.Sin(2 * Math.PI * f * i / frec)) * env * 9000));
                }
                for (int i = 0; i < frec * 0.02; i++) m.Add(0);
            }
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write("RIFF".ToCharArray()); w.Write(36 + m.Count * 2); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(frec); w.Write(frec * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(m.Count * 2);
            foreach (short s in m) w.Write(s);
            w.Flush();
            return ms.ToArray();
        }

        static SoundPlayer Crear(int[] notas)
        {
            var p = new SoundPlayer(new MemoryStream(Wav(notas)));
            p.Load();
            return p;
        }

        static readonly SoundPlayer Bien = Crear(new[] { 784, 1047 });
        static readonly SoundPlayer Atraso = Crear(new[] { 784, 659, 523 });
        static readonly SoundPlayer Alerta = Crear(new[] { 440, 330, 440, 330 });
        public static bool Activo { get; set; } = true;

        public static void Tocar(string tipo)
        {
            if (!Activo) return;
            var p = tipo == "puntual" || tipo == "justificado" ? Bien : tipo == "atraso" ? Atraso : Alerta;
            try { p.Play(); } catch (Exception) { SystemSounds.Exclamation.Play(); }
        }
    }
}
