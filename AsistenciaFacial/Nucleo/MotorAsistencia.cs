using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using OpenCvSharp;

namespace AsistenciaFacial.Nucleo
{
    /// <summary>
    /// Programa de la cámara (hilo aparte). Dos modos:
    ///  • Reconocer: ve rostros, los compara con la galería y marca la asistencia.
    ///  • Registrar: toma varias muestras del rostro de UN estudiante y las guarda.
    /// </summary>
    public sealed class MotorAsistencia : IDisposable
    {
        static readonly Scalar Verde = new Scalar(92, 157, 42), Amarillo = new Scalar(5, 183, 242), Rojo = new Scalar(91, 73, 209),
            Azul = new Scalar(137, 78, 29), Barra = new Scalar(61, 33, 20);

        /// <summary>Un rostro que se sigue entre cuadros (para confirmar antes de marcar).</summary>
        sealed class Pista
        {
            public Rect2d Caja;
            public DateTime Visto;
            public long UltimoId = -1;
            public int Seguidos;
            public bool Marcado;
            public DateTime? DesconocidoDesde;
            public bool AvisoDesconocido;
            public string Etiqueta = "";
            public Scalar Color;
        }

        readonly Configuracion _cfg;
        readonly BaseDatos _db;
        readonly object _candado = new object();
        Thread _hilo;
        volatile bool _seguir, _detenidoPorUsuario;
        byte[] _jpeg;

        // modo registro
        volatile bool _registrando;
        long _regId;
        string _regNombre = "";
        readonly List<(float[] vector, byte[] foto)> _muestras = new List<(float[], byte[])>();
        DateTime _ultimaMuestra = DateTime.MinValue;
        bool _avisoParecido;

        public bool Activo => _seguir;
        public string Fuente { get; private set; } = "";
        public double Fps { get; private set; }
        public long NumeroFrame { get; private set; }
        public DateTime UltimoFrame { get; private set; } = DateTime.MinValue;
        public bool Conectada => _seguir && (DateTime.UtcNow - UltimoFrame).TotalSeconds < 8;
        public int EstudiantesEnGaleria { get; private set; }
        public bool Registrando => _registrando;

        public event Action<Mat> FrameListo;                         // cada receptor libera su copia
        public event Action<string> Mensaje;
        /// <summary>La cámara no pudo abrir o se detuvo: texto con la explicación para el usuario.</summary>
        public event Action<string> Fallo;
        public event Action<ResultadoMarca, byte[]> MarcaRealizada;  // fila + foto de la cara (JPEG)
        public event Action<int, int, byte[]> RegistroProgreso;      // muestras, total, cara
        public event Action<bool, string> RegistroTerminado;         // éxito, mensaje
        public event Action<string> RegistroAviso;

        public MotorAsistencia(Configuracion cfg, BaseDatos db)
        {
            _cfg = cfg;
            _db = db;
        }

        public (long numero, byte[] jpeg) Video()
        {
            lock (_candado) return (NumeroFrame, _jpeg);
        }

        public void Iniciar(string fuente)
        {
            Detener();
            Fuente = string.IsNullOrWhiteSpace(fuente) ? "0" : fuente.Trim();
            _detenidoPorUsuario = false;
            _seguir = true;
            _hilo = new Thread(Bucle) { IsBackground = true, Name = "camara" };
            _hilo.Start();
        }

        public void Detener()
        {
            if (_seguir) _detenidoPorUsuario = true;
            _seguir = false;
            if (_hilo != null && _hilo.IsAlive && Thread.CurrentThread != _hilo) _hilo.Join(3000);
            _hilo = null;
        }

        public void IniciarRegistro(Estudiante e)
        {
            lock (_candado)
            {
                _muestras.Clear();
                _regId = e.Id;
                _regNombre = e.NombreCompleto;
                _avisoParecido = false;
                _ultimaMuestra = DateTime.MinValue;
                _registrando = true;
            }
        }

        public void CancelarRegistro()
        {
            lock (_candado)
            {
                _registrando = false;
                _muestras.Clear();
            }
        }

        void Avisar(string m) => Mensaje?.Invoke(m);

        /// <summary>Hershey (la letra de OpenCV) no tiene tildes: "Peña" → "Pena".</summary>
        public static string SinTildes(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c < 128) sb.Append(c);
            return sb.ToString();
        }

        // ───────────────────────── fuentes de imagen ─────────────────────────
        /// <summary>Cámara, URL, video o una carpeta con fotos (se muestran una tras otra).</summary>
        sealed class FuenteImagenes : IDisposable
        {
            VideoCapture _cap;
            readonly string _fuente;
            readonly string[] _fotos;
            int _indice;
            readonly Stopwatch _reloj = Stopwatch.StartNew();
            public bool EsArchivo { get; }
            public bool EsCarpeta => _fotos != null;

            public Action<string> Avisar;

            public FuenteImagenes(string fuente, Action<string> avisar = null)
            {
                Avisar = avisar;
                _fuente = fuente;
                if (Directory.Exists(fuente))
                {
                    _fotos = Directory.GetFiles(fuente).Where(f => new[] { ".jpg", ".jpeg", ".png", ".bmp" }
                        .Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToArray();
                    return;
                }
                EsArchivo = File.Exists(fuente);
                Abrir();
            }

            void Abrir()
            {
                _cap?.Dispose();
                if (int.TryParse(_fuente, out int i))
                {
                    // Prueba DirectShow, Media Foundation y automático; se queda con el que entregue imágenes.
                    _cap = DiagnosticoCamara.AbrirCamara(i, Avisar) ?? new VideoCapture();
                }
                else _cap = new VideoCapture(_fuente);
            }

            public bool Abierta => EsCarpeta ? _fotos.Length > 0 : _cap.IsOpened();

            public bool Leer(Mat destino)
            {
                if (EsCarpeta)
                {
                    // cada foto se "ve" 1,5 s, como si una persona estuviera frente a la cámara
                    int i = (int)(_reloj.Elapsed.TotalSeconds / 1.5) % _fotos.Length;
                    if (i != _indice || destino.Empty())
                    {
                        _indice = i;
                        using var img = Cv2.ImRead(_fotos[i]);
                        img.CopyTo(destino);
                    }
                    Thread.Sleep(60);
                    return !destino.Empty();
                }
                bool ok = _cap.Read(destino);
                if ((!ok || destino.Empty()) && EsArchivo)
                {
                    _cap.Set(VideoCaptureProperties.PosFrames, 0);     // video: repetir
                    ok = _cap.Read(destino);
                }
                if (EsArchivo) Thread.Sleep(30);
                return ok && !destino.Empty();
            }

            public void Reabrir() { if (!EsCarpeta) Abrir(); }
            public void Dispose() => _cap?.Dispose();
        }

        // ───────────────────────── bucle principal ─────────────────────────
        void Bucle()
        {
            var rc = _cfg.Reconocimiento;
            FuenteImagenes fuente = null;
            DetectorRostros detector = null;
            ReconocedorRostros reconocedor = null;
            try
            {
                var (cvOk, cvMsg) = DiagnosticoCamara.ProbarOpenCv();
                if (!cvOk)
                {
                    Avisar("OpenCV no se pudo cargar: instala el Visual C++ Redistributable x64.");
                    Fallo?.Invoke(cvMsg);
                    _seguir = false;
                    return;
                }
                // Primero las redes neuronales: si faltan, se avisa claro (antes de tocar la cámara).
                var problemas = ModelosIA.Problemas(_cfg);
                if (problemas.Count > 0)
                {
                    Avisar("Faltan las redes neuronales: " + string.Join(" ", problemas));
                    Fallo?.Invoke("La cámara funciona, pero faltan las redes neuronales del reconocimiento facial:\n\n• " +
                                  string.Join("\n• ", problemas) + "\n\nSin ellas el programa no puede reconocer a nadie.\n[MODELO]");
                    _seguir = false;
                    return;
                }
                string carpetaModelos = ModelosIA.Carpeta(_cfg);
                detector = new DetectorRostros(Path.Combine(carpetaModelos, ModelosIA.YuNet));
                reconocedor = new ReconocedorRostros(Path.Combine(carpetaModelos, ModelosIA.SFace));
                fuente = new FuenteImagenes(Fuente, Avisar);
                if (!fuente.Abierta)
                {
                    Avisar($"No se pudo abrir la fuente «{Fuente}». Prueba otra cámara (1), revisa permisos o usa una carpeta con fotos.");
                    Fallo?.Invoke(int.TryParse(Fuente, out _)
                        ? $"No se pudo abrir la cámara {Fuente} (se probaron DirectShow, Media Foundation y automático).\n\n" +
                          "Revisa:\n• Windows → Configuración → Privacidad y seguridad → Cámara: activa el acceso y «Permitir que las aplicaciones de escritorio accedan a la cámara».\n" +
                          "• Cierra Zoom, Meet, Teams, WhatsApp o la app Cámara (solo un programa puede usarla).\n" +
                          "• Prueba con el número 1 o 2 (cámara USB).\n• Revisa la tecla o tapa física de la cámara.\n\n" +
                          "Pulsa «Diagnóstico» para probar todas las cámaras."
                        : $"No se pudo abrir «{Fuente}».\nSi es un celular (cámara IP), revisa que la URL abra en el navegador de la laptop y que ambos estén en el mismo WiFi.");
                    _db.EventoSistema("camara_off", $"No se pudo abrir la cámara «{Fuente}»", true);
                    _seguir = false;
                    return;
                }
                int version = -1;
                Galeria galeria = null;
                var pistas = new List<Pista>();
                var ultimoAviso = new Dictionary<long, DateTime>();
                DateTime ultimoDesconocido = DateTime.MinValue;
                Avisar($"Cámara iniciada · fuente: {Fuente}");
                _db.EventoSistema("camara_on", "Cámara de asistencia conectada", false);

                var reloj = Stopwatch.StartNew();
                double tFps = 0, tVideo = -10;
                int nFps = 0, fallos = 0;
                using var original = new Mat();
                using var frame = new Mat();

                while (_seguir)
                {
                    if (!fuente.Leer(original))
                    {
                        if (++fallos % 20 == 1) Avisar("No llegan imágenes de la cámara, reintentando…");
                        Thread.Sleep(100);
                        if (fallos > 100) { fuente.Reabrir(); fallos = 0; }
                        continue;
                    }
                    fallos = 0;
                    if (version != _db.VersionRostros)
                    {
                        version = _db.VersionRostros;
                        galeria = new Galeria(_db.Galeria());
                        EstudiantesEnGaleria = galeria.Estudiantes;
                    }
                    double escala = Math.Min(1.0, (double)rc.AnchoProceso / original.Cols);
                    Cv2.Resize(original, frame, new Size((int)(original.Cols * escala), (int)(original.Rows * escala)));
                    var ahora = DateTime.Now;

                    // ── 1. DETECCIÓN: ¿dónde hay rostros? ──
                    var rostros = detector.Detectar(frame);
                    var vista = frame.Clone();
                    string encabezado;

                    if (_registrando)
                    {
                        encabezado = ProcesarRegistro(frame, vista, rostros, reconocedor, galeria, rc);
                    }
                    else
                    {
                        // ── 2. RECONOCIMIENTO y 3. DECISIÓN con confirmación ──
                        foreach (var r in rostros)
                        {
                            var caja = new Rect2d(r.X, r.Y, r.W, r.H);
                            var pista = pistas.OrderByDescending(p => IoU(p.Caja, caja)).FirstOrDefault(p => IoU(p.Caja, caja) > 0.25);
                            if (pista == null) { pista = new Pista(); pistas.Add(pista); }
                            pista.Caja = caja;
                            pista.Visto = ahora;
                            if (Math.Min(r.W, r.H) < rc.RostroMinimoPx)
                            {
                                pista.Etiqueta = "Acercate a la camara";
                                pista.Color = Azul;
                                continue;
                            }
                            Coincidencia c;
                            using (var alineada = ReconocedorRostros.Alinear(frame, r))
                                c = galeria == null || galeria.Muestras == 0 ? new Coincidencia()
                                    : galeria.Buscar(reconocedor.Vector(alineada), rc.UmbralSimilitud, rc.MargenMinimo);
                            if (c.Reconocido)
                            {
                                pista.DesconocidoDesde = null;
                                pista.Seguidos = c.EstudianteId == pista.UltimoId ? pista.Seguidos + 1 : 1;
                                if (c.EstudianteId != pista.UltimoId) pista.Marcado = false;
                                pista.UltimoId = c.EstudianteId;
                                if (pista.Marcado) { /* ya resuelto: mantener la etiqueta */ }
                                else if (pista.Seguidos >= rc.Confirmaciones)
                                {
                                    pista.Marcado = true;
                                    var res = _db.MarcarReconocido(c.EstudianteId, c.Similitud, ahora, _cfg.Horario, "camara");
                                    string hora = res.Fila.Hora.HasValue ? BaseDatos.Local(res.Fila.Hora.Value).ToString("HH:mm") : "";
                                    pista.Etiqueta = res.Motivo.Length > 0 ? $"{c.Nombre} - {res.Motivo}"
                                        : res.Nueva && res.Tipo == "salida" ? $"{c.Nombre} - SALIDA {BaseDatos.Local(res.Fila.HoraSalida.Value):HH:mm}"
                                        : res.Nueva ? $"{c.Nombre} - {res.Fila.Materia} {hora} {res.Fila.Estado.ToUpperInvariant()}"
                                        : $"{c.Nombre} - ya marco {res.Fila.Materia} {hora}";
                                    pista.Color = res.Motivo.Length > 0 ? Azul : res.Fila.Estado == "atraso" ? Amarillo : Verde;
                                    bool avisar = res.Nueva || !ultimoAviso.TryGetValue(c.EstudianteId, out var t) || (ahora - t).TotalSeconds > 60;
                                    if (avisar)
                                    {
                                        ultimoAviso[c.EstudianteId] = ahora;
                                        byte[] foto = Recorte(frame, r);
                                        MarcaRealizada?.Invoke(res, foto);
                                    }
                                }
                                else
                                {
                                    pista.Etiqueta = $"{c.Nombre} ({pista.Seguidos}/{rc.Confirmaciones}) {c.Similitud:0.00}";
                                    pista.Color = Amarillo;
                                }
                            }
                            else
                            {
                                pista.Seguidos = 0;
                                pista.UltimoId = -1;
                                pista.Marcado = false;
                                pista.DesconocidoDesde ??= ahora;
                                pista.Etiqueta = (c.Similitud >= rc.UmbralSimilitud ? c.Nombre : "Desconocido") + $" {c.Similitud:0.00}";
                                pista.Color = Rojo;
                                // Rostro no registrado varios segundos → alerta (máx. una cada 30 s) y foto para revisar.
                                if (!pista.AvisoDesconocido && (ahora - pista.DesconocidoDesde.Value).TotalSeconds >= rc.SegundosDesconocido
                                    && (ahora - ultimoDesconocido).TotalSeconds > 30)
                                {
                                    pista.AvisoDesconocido = true;
                                    ultimoDesconocido = ahora;
                                    string carpeta = Path.Combine(_cfg.CarpetaDatos, "desconocidos");
                                    Directory.CreateDirectory(carpeta);
                                    File.WriteAllBytes(Path.Combine(carpeta, $"{ahora:yyyyMMdd_HHmmss}.jpg"), Recorte(frame, r));
                                    _db.EventoSistema("desconocido", "Rostro NO registrado frente a la cámara (foto en datos/desconocidos)", true);
                                }
                            }
                            DibujarRostro(vista, r, pista.Etiqueta, pista.Color);
                        }
                        pistas.RemoveAll(p => (ahora - p.Visto).TotalSeconds > 1.5);
                        encabezado = $"ASISTENCIA | {galeria?.Estudiantes ?? 0} estudiantes registrados | {ahora:HH:mm:ss}";
                    }

                    // ── 4. VIDEO para la ventana y la web ──
                    nFps++;
                    double seg = reloj.Elapsed.TotalSeconds;
                    if (seg - tFps >= 1) { Fps = Math.Round(nFps / (seg - tFps), 1); tFps = seg; nFps = 0; }
                    int h = vista.Rows;
                    Cv2.Rectangle(vista, new Point(0, h - 26), new Point(vista.Cols, h), Barra, -1);
                    Cv2.PutText(vista, SinTildes($"{encabezado} | {Fps:0.0} fps"), new Point(8, h - 8), HersheyFonts.HersheySimplex, 0.5, Scalar.White, 1, LineTypes.AntiAlias);
                    byte[] jpg = null;
                    if (seg - tVideo >= 1.0 / Math.Max(1, rc.VideoFps))
                    {
                        Cv2.ImEncode(".jpg", vista, out jpg, new[] { (int)ImwriteFlags.JpegQuality, rc.CalidadJpeg });
                        tVideo = seg;
                    }
                    lock (_candado)
                    {
                        if (jpg != null) { _jpeg = jpg; NumeroFrame++; }
                        UltimoFrame = DateTime.UtcNow;
                    }
                    // cada ventana que muestra la cámara recibe su propia copia (y la libera)
                    var receptores = FrameListo?.GetInvocationList() ?? Array.Empty<Delegate>();
                    foreach (Action<Mat> r in receptores) r(vista.Clone());
                    vista.Dispose();
                }
            }
            catch (Exception e)
            {
                Avisar("La cámara se detuvo por un error: " + e.Message);
                var (cvOk2, cvMsg2) = DiagnosticoCamara.ProbarOpenCv();
                Fallo?.Invoke(cvOk2 ? "La cámara se detuvo por un error:\n" + e.Message + "\n\nPulsa «Diagnóstico» para revisar la cámara." : cvMsg2);
            }
            finally
            {
                _seguir = false;
                fuente?.Dispose();
                detector?.Dispose();
                reconocedor?.Dispose();
                try
                {
                    _db.EventoSistema("camara_off", _detenidoPorUsuario ? "Cámara detenida desde el programa"
                        : "La cámara dejó de enviar imágenes", !_detenidoPorUsuario);
                }
                catch { /* la base de datos puede estar cerrándose */ }
            }
        }

        string ProcesarRegistro(Mat frame, Mat vista, List<Rostro> rostros, ReconocedorRostros reconocedor, Galeria galeria, ConfigReconocimiento rc)
        {
            int total = Math.Max(3, rc.MuestrasRegistro);
            string titulo = $"REGISTRANDO: {_regNombre} {_muestras.Count}/{total}";
            if (rostros.Count != 1)
            {
                foreach (var r in rostros) DibujarRostro(vista, r, "", Rojo);
                Aviso(vista, rostros.Count == 0 ? "No veo ningun rostro: mira a la camara" : "Debe haber UNA sola persona frente a la camara");
                return titulo;
            }
            var ro = rostros[0];
            if (Math.Min(ro.W, ro.H) < rc.RostroMinimoPx * 1.3 || ro.Confianza < 0.85)
            {
                DibujarRostro(vista, ro, "", Amarillo);
                Aviso(vista, "Acercate un poco mas a la camara");
                return titulo;
            }
            DibujarRostro(vista, ro, $"{_muestras.Count}/{total}", Verde);
            if ((DateTime.Now - _ultimaMuestra).TotalSeconds < 0.35) return titulo;

            float[] v;
            byte[] foto;
            using (var al = ReconocedorRostros.Alinear(frame, ro))
            {
                v = reconocedor.Vector(al);
                Cv2.ImEncode(".jpg", al, out foto, new[] { (int)ImwriteFlags.JpegQuality, 85 });
            }
            lock (_candado)
            {
                if (!_registrando) return titulo;
                // Muestras casi idénticas no aportan: se pide mover un poco la cabeza.
                if (_muestras.Any(m => ReconocedorRostros.Coseno(m.vector, v) > 0.985))
                {
                    Aviso(vista, "Gira un poco la cabeza (izquierda, derecha, arriba)");
                    return titulo;
                }
                // ¿Este rostro ya pertenece a OTRO estudiante?
                if (!_avisoParecido && galeria != null && galeria.Muestras > 0)
                {
                    var c = galeria.Buscar(v, rc.UmbralSimilitud + 0.05, 0);
                    if (c.Reconocido && c.EstudianteId != _regId)
                    {
                        _avisoParecido = true;
                        RegistroAviso?.Invoke($"Atención: este rostro se parece a {c.Nombre} (similitud {c.Similitud:0.00}). ¿Es la persona correcta?");
                    }
                }
                _muestras.Add((v, foto));
                _ultimaMuestra = DateTime.Now;
            }
            RegistroProgreso?.Invoke(_muestras.Count, total, foto);
            if (_muestras.Count >= total)
            {
                List<(float[], byte[])> copia;
                lock (_candado)
                {
                    copia = _muestras.ToList();
                    _registrando = false;
                    _muestras.Clear();
                }
                try
                {
                    _db.GuardarRostros(_regId, copia, reemplazar: true);
                    RegistroTerminado?.Invoke(true, $"Rostro de {_regNombre} registrado con {copia.Count} muestras.");
                }
                catch (Exception e) { RegistroTerminado?.Invoke(false, "No se pudo guardar: " + e.Message); }
            }
            return $"REGISTRANDO: {_regNombre} {Math.Min(total, _muestras.Count)}/{total}";
        }

        // ───────────────────────── utilidades ─────────────────────────
        static double IoU(Rect2d a, Rect2d b)
        {
            double x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
            double x2 = Math.Min(a.X + a.Width, b.X + b.Width), y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
            double inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
            double union = a.Width * a.Height + b.Width * b.Height - inter;
            return union <= 0 ? 0 : inter / union;
        }

        static byte[] Recorte(Mat frame, Rostro r)
        {
            int m = (int)(Math.Max(r.W, r.H) * 0.3);
            int x1 = Math.Max(0, (int)r.X - m), y1 = Math.Max(0, (int)r.Y - m);
            int x2 = Math.Min(frame.Cols, (int)(r.X + r.W) + m), y2 = Math.Min(frame.Rows, (int)(r.Y + r.H) + m);
            if (x2 - x1 < 4 || y2 - y1 < 4) return Array.Empty<byte>();
            using var roi = new Mat(frame, new Rect(x1, y1, x2 - x1, y2 - y1));
            Cv2.ImEncode(".jpg", roi, out byte[] jpg, new[] { (int)ImwriteFlags.JpegQuality, 85 });
            return jpg;
        }

        static void DibujarRostro(Mat img, Rostro r, string texto, Scalar color)
        {
            var p1 = new Point((int)r.X, (int)r.Y);
            var p2 = new Point((int)(r.X + r.W), (int)(r.Y + r.H));
            Cv2.Rectangle(img, p1, p2, color, 2);
            for (int k = 0; k < 5; k++)
                Cv2.Circle(img, new Point((int)r.Puntos[k * 2], (int)r.Puntos[k * 2 + 1]), 2, color, -1);
            if (texto.Length == 0) return;
            texto = SinTildes(texto);
            var tam = Cv2.GetTextSize(texto, HersheyFonts.HersheySimplex, 0.55, 2, out _);
            int y = Math.Max(tam.Height + 8, p1.Y);
            Cv2.Rectangle(img, new Point(p1.X, y - tam.Height - 8), new Point(p1.X + tam.Width + 8, y), color, -1);
            Cv2.PutText(img, texto, new Point(p1.X + 4, y - 5), HersheyFonts.HersheySimplex, 0.55, Scalar.White, 2, LineTypes.AntiAlias);
        }

        static void Aviso(Mat img, string texto)
        {
            Cv2.Rectangle(img, new Point(0, 0), new Point(img.Cols, 36), Rojo, -1);
            Cv2.PutText(img, SinTildes(texto), new Point(10, 25), HersheyFonts.HersheySimplex, 0.65, Scalar.White, 2, LineTypes.AntiAlias);
        }

        public void Dispose() => Detener();
    }
}
