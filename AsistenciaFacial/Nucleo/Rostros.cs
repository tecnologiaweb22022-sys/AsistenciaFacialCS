using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace AsistenciaFacial.Nucleo
{
    /*  Reconocimiento facial en dos redes neuronales (las dos corren solo con OpenCV, sin internet):

        1) YuNet (detección)      : ¿DÓNDE hay rostros? → caja + 5 puntos (ojos, nariz, boca).
        2) SFace (reconocimiento) : ¿DE QUIÉN es el rostro? → convierte la cara alineada de 112×112
                                    en un VECTOR de 128 números ("huella del rostro").
        Dos fotos de la misma persona dan vectores parecidos: se comparan con la similitud coseno.

        CÁMARA → IMAGEN → YuNet (rostros) → alinear → SFace (vector) → comparar con la galería → DECISIÓN → marca   */

    // ═════════════════════════════ 1) YuNet ═════════════════════════════
    public sealed class DetectorRostros : IDisposable
    {
        static readonly int[] Pasos = { 8, 16, 32 };
        readonly Net _red;
        public float Confianza { get; set; } = 0.8f;
        public float Nms { get; set; } = 0.3f;

        public DetectorRostros(string rutaModelo)
        {
            if (!File.Exists(rutaModelo)) throw new FileNotFoundException("No encuentro el modelo YuNet", rutaModelo);
            _red = CvDnn.ReadNetFromOnnx(rutaModelo) ?? throw new InvalidOperationException("No se pudo leer YuNet");
        }

        static float[] Datos(Mat m)
        {
            var d = new float[m.Total() * m.Channels()];
            Marshal.Copy(m.Data, d, 0, d.Length);
            return d;
        }

        public List<Rostro> Detectar(Mat bgr)
        {
            int w = bgr.Cols, h = bgr.Rows;
            // La red trabaja con medidas múltiplos de 32: se rellena abajo y a la derecha.
            int pw = (w + 31) / 32 * 32, ph = (h + 31) / 32 * 32;
            using var relleno = new Mat();
            Cv2.CopyMakeBorder(bgr, relleno, 0, ph - h, 0, pw - w, BorderTypes.Constant, Scalar.All(0));
            using var blob = CvDnn.BlobFromImage(relleno, 1.0, new Size(pw, ph), new Scalar(0, 0, 0), false, false);
            _red.SetInput(blob);
            string[] nombres = _red.GetUnconnectedOutLayersNames().Select(x => x ?? "").ToArray();
            var salidas = nombres.Select(_ => new Mat()).ToArray();
            _red.Forward(salidas, nombres);
            var porNombre = new Dictionary<string, float[]>();
            for (int i = 0; i < nombres.Length; i++)
            {
                porNombre[nombres[i]] = Datos(salidas[i]);
                salidas[i].Dispose();
            }

            var cajas = new List<Rect2d>();
            var confs = new List<float>();
            var candidatos = new List<Rostro>();
            foreach (int s in Pasos)
            {
                if (!porNombre.TryGetValue($"cls_{s}", out var cls)) continue;
                var obj = porNombre[$"obj_{s}"];
                var bbox = porNombre[$"bbox_{s}"];
                var kps = porNombre[$"kps_{s}"];
                int filas = ph / s, cols = pw / s;
                for (int r = 0; r < filas; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int i = r * cols + c;
                        float sc = (float)Math.Sqrt(Math.Clamp(cls[i], 0f, 1f) * Math.Clamp(obj[i], 0f, 1f));
                        if (sc < Confianza) continue;
                        float cx = (c + bbox[i * 4]) * s, cy = (r + bbox[i * 4 + 1]) * s;
                        float bw = (float)Math.Exp(bbox[i * 4 + 2]) * s, bh = (float)Math.Exp(bbox[i * 4 + 3]) * s;
                        var ro = new Rostro { X = cx - bw / 2, Y = cy - bh / 2, W = bw, H = bh, Confianza = sc };
                        for (int k = 0; k < 5; k++)
                        {
                            ro.Puntos[k * 2] = (kps[i * 10 + k * 2] + c) * s;
                            ro.Puntos[k * 2 + 1] = (kps[i * 10 + k * 2 + 1] + r) * s;
                        }
                        candidatos.Add(ro);
                        cajas.Add(new Rect2d(ro.X, ro.Y, ro.W, ro.H));
                        confs.Add(sc);
                    }
            }
            if (candidatos.Count == 0) return new List<Rostro>();
            CvDnn.NMSBoxes(cajas, confs, Confianza, Nms, out int[] indices);
            return indices.Select(i => candidatos[i]).OrderByDescending(x => x.Area).ToList();
        }

        public void Dispose() => _red.Dispose();
    }

    // ═════════════════════════════ 2) SFace ═════════════════════════════
    public sealed class ReconocedorRostros : IDisposable
    {
        /// <summary>Dónde deben quedar ojos, nariz y boca en la cara alineada de 112×112.</summary>
        static readonly float[] Plantilla = { 38.2946f, 51.6963f, 73.5318f, 51.5014f, 56.0252f, 71.7366f, 41.5493f, 92.3655f, 70.7299f, 92.2041f };
        readonly Net _red;

        public ReconocedorRostros(string rutaModelo)
        {
            if (!File.Exists(rutaModelo)) throw new FileNotFoundException("No encuentro el modelo SFace", rutaModelo);
            _red = CvDnn.ReadNetFromOnnx(rutaModelo) ?? throw new InvalidOperationException("No se pudo leer SFace");
        }

        /// <summary>
        /// Gira, escala y recorta la cara para que los ojos y la boca queden siempre en el mismo lugar
        /// (transformación de semejanza por mínimos cuadrados: rotación + escala + traslación).
        /// </summary>
        public static Mat Alinear(Mat bgr, Rostro r)
        {
            double mx = 0, my = 0, qx = 0, qy = 0;
            for (int k = 0; k < 5; k++)
            {
                mx += r.Puntos[k * 2]; my += r.Puntos[k * 2 + 1];
                qx += Plantilla[k * 2]; qy += Plantilla[k * 2 + 1];
            }
            mx /= 5; my /= 5; qx /= 5; qy /= 5;
            double num1 = 0, num2 = 0, den = 0;
            for (int k = 0; k < 5; k++)
            {
                double px = r.Puntos[k * 2] - mx, py = r.Puntos[k * 2 + 1] - my;
                double tx = Plantilla[k * 2] - qx, ty = Plantilla[k * 2 + 1] - qy;
                num1 += px * tx + py * ty;
                num2 += px * ty - py * tx;
                den += px * px + py * py;
            }
            double a = num1 / den, b = num2 / den;
            using var m = new Mat(2, 3, MatType.CV_64FC1);
            m.Set(0, 0, a); m.Set(0, 1, -b); m.Set(0, 2, qx - (a * mx - b * my));
            m.Set(1, 0, b); m.Set(1, 1, a); m.Set(1, 2, qy - (b * mx + a * my));
            var salida = new Mat();
            Cv2.WarpAffine(bgr, salida, m, new Size(112, 112));
            return salida;
        }

        /// <summary>Vector de 128 números normalizado (largo 1): la "huella" del rostro.</summary>
        public float[] Vector(Mat caraAlineada)
        {
            using var blob = CvDnn.BlobFromImage(caraAlineada, 1.0, new Size(112, 112), new Scalar(0, 0, 0), true, false);
            _red.SetInput(blob);
            using var salida = _red.Forward();
            var v = new float[salida.Total()];
            Marshal.Copy(salida.Data, v, 0, v.Length);
            double norma = Math.Sqrt(v.Sum(x => (double)x * x));
            for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / Math.Max(norma, 1e-9));
            return v;
        }

        /// <summary>Similitud coseno entre dos vectores normalizados: 1 = idénticos, 0 = nada que ver.</summary>
        public static double Coseno(float[] a, float[] b)
        {
            double s = 0;
            for (int i = 0; i < a.Length && i < b.Length; i++) s += a[i] * b[i];
            return s;
        }

        public void Dispose() => _red.Dispose();
    }

    // ═════════════════════ Galería: rostros registrados ═════════════════════
    public sealed class Galeria
    {
        readonly List<(long id, string nombre, float[] vector)> _muestras;
        public int Estudiantes { get; }
        public int Muestras => _muestras.Count;

        public Galeria(List<(long id, string nombre, float[] vector)> muestras)
        {
            _muestras = muestras;
            Estudiantes = muestras.Select(m => m.id).Distinct().Count();
        }

        /// <summary>
        /// Compara con todos: para cada estudiante se toma su muestra más parecida.
        /// Se acepta si supera el umbral Y le gana al segundo más parecido por un margen
        /// (así no se confunde entre dos estudiantes parecidos).
        /// </summary>
        public Coincidencia Buscar(float[] vector, double umbral, double margen)
        {
            var mejores = _muestras.GroupBy(m => m.id)
                .Select(g => (id: g.Key, nombre: g.First().nombre, sim: g.Max(m => ReconocedorRostros.Coseno(vector, m.vector))))
                .OrderByDescending(x => x.sim).Take(2).ToList();
            var c = new Coincidencia();
            if (mejores.Count == 0) return c;
            c.Similitud = mejores[0].sim;
            c.Segunda = mejores.Count > 1 ? mejores[1].sim : 0;
            if (c.Similitud >= umbral && c.Similitud - c.Segunda >= margen)
            {
                c.Reconocido = true;
                c.EstudianteId = mejores[0].id;
                c.Nombre = mejores[0].nombre;
            }
            else if (c.Similitud >= umbral)
                c.Nombre = "¿" + mejores[0].nombre + "?";       // dudoso: parecido a dos personas
            return c;
        }
    }

    // ═════════════ Servicio para fotos sueltas (celular): detección + vector ═════════════
    /// <summary>
    /// Procesa fotos que llegan por la web (selfie para marcar, fotos para registrar).
    /// Tiene sus propias redes y un candado: atiende una foto a la vez sin molestar a la cámara.
    /// </summary>
    public sealed class ServicioRostros : IDisposable
    {
        readonly string _carpetaModelos;
        readonly object _candado = new object();
        DetectorRostros _detector;
        ReconocedorRostros _reconocedor;

        public ServicioRostros(string carpetaModelos) { _carpetaModelos = carpetaModelos; }

        /// <summary>Resultado de analizar una foto: vector del rostro más grande y su recorte alineado (JPEG).</summary>
        public sealed class Analisis
        {
            public float[] Vector;
            public byte[] Cara;
            public int Rostros;
            public string Error = "";
        }

        public Analisis Analizar(byte[] imagen, int rostroMinimoPx, bool exigirUno)
        {
            var a = new Analisis();
            if (imagen == null || imagen.Length < 100) { a.Error = "No llegó ninguna foto"; return a; }
            using var original = Cv2.ImDecode(imagen, ImreadModes.Color);
            if (original.Empty()) { a.Error = "El archivo no es una imagen válida"; return a; }
            // Fotos de celular muy grandes: se achican a 960 px de ancho (más rápido, igual de preciso).
            double escala = Math.Min(1.0, 960.0 / Math.Max(original.Cols, original.Rows));
            using var img = new Mat();
            Cv2.Resize(original, img, new Size((int)(original.Cols * escala), (int)(original.Rows * escala)));
            lock (_candado)
            {
                _detector ??= new DetectorRostros(Path.Combine(_carpetaModelos, "face_detection_yunet_2023mar.onnx"));
                _reconocedor ??= new ReconocedorRostros(Path.Combine(_carpetaModelos, "face_recognition_sface_2021dec.onnx"));
                var rostros = _detector.Detectar(img);
                a.Rostros = rostros.Count;
                if (rostros.Count == 0) { a.Error = "No se ve ningún rostro en la foto. Mira de frente, con buena luz."; return a; }
                if (exigirUno && rostros.Count > 1) { a.Error = "En la foto debe aparecer UNA sola persona."; return a; }
                var r = rostros[0];                                       // el más grande
                if (Math.Min(r.W, r.H) < rostroMinimoPx) { a.Error = "El rostro sale muy pequeño: acerca más el celular."; return a; }
                using var al = ReconocedorRostros.Alinear(img, r);
                a.Vector = _reconocedor.Vector(al);
                Cv2.ImEncode(".jpg", al, out a.Cara, new[] { (int)ImwriteFlags.JpegQuality, 85 });
            }
            return a;
        }

        public void Dispose()
        {
            _detector?.Dispose();
            _reconocedor?.Dispose();
        }
    }
}
