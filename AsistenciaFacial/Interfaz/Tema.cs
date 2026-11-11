using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace AsistenciaFacial.Interfaz
{
    /// <summary>Colores, fuentes y botones (mismos colores que la web).</summary>
    internal static class Tema
    {
        public static readonly Color Fondo = Color.FromArgb(246, 241, 231);
        public static readonly Color Papel = Color.FromArgb(255, 253, 248);
        public static readonly Color Tinta = Color.FromArgb(20, 33, 61);
        public static readonly Color Suave = Color.FromArgb(91, 100, 120);
        public static readonly Color Linea = Color.FromArgb(227, 220, 205);
        public static readonly Color Azul = Color.FromArgb(29, 78, 137);
        public static readonly Color Celeste = Color.FromArgb(58, 155, 213);
        public static readonly Color Naranja = Color.FromArgb(240, 138, 36);
        public static readonly Color Verde = Color.FromArgb(42, 157, 92);
        public static readonly Color Amarillo = Color.FromArgb(242, 183, 5);
        public static readonly Color Rojo = Color.FromArgb(209, 73, 91);
        public static readonly Color Gris = Color.FromArgb(150, 143, 128);

        public static readonly Font Normal = new Font("Segoe UI", 9.75f);
        public static readonly Font Negrita = new Font("Segoe UI", 9.75f, FontStyle.Bold);
        public static readonly Font Titulo = new Font("Segoe UI", 13f, FontStyle.Bold);
        public static readonly Font Grande = new Font("Segoe UI", 20f, FontStyle.Bold);
        public static readonly Font Chica = new Font("Segoe UI", 8.5f);

        public static Color ColorEstado(string estado)
        {
            switch (estado)
            {
                case "puntual": return Verde;
                case "atraso": return Naranja;
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

        public static Button Boton(string texto, Color fondo, EventHandler clic, Color? letra = null)
        {
            var b = new Button
            {
                Text = texto, BackColor = fondo, ForeColor = letra ?? Color.White, FlatStyle = FlatStyle.Flat, Font = Negrita,
                AutoSize = true, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(3), Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += clic;
            return b;
        }

        public static void EstiloGrilla(DataGridView g)
        {
            g.Dock = DockStyle.Fill;
            g.BackgroundColor = Papel;
            g.BorderStyle = BorderStyle.None;
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.ReadOnly = true;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Tinta;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            g.ColumnHeadersDefaultCellStyle.Font = Negrita;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 232, 245);
            g.DefaultCellStyle.SelectionForeColor = Tinta;
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
