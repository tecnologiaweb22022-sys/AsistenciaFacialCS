using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using OpenCvSharp;

namespace AsistenciaFacial.Nucleo
{
    /// <summary>
    /// Abre la cámara probando varios "motores" de Windows y explica por qué falla.
    /// Causas típicas: falta el Visual C++ Redistributable, Windows "N" sin Media Feature Pack,
    /// permisos de cámara de Windows, o la cámara ocupada por otra aplicación.
    /// </summary>
    public static class DiagnosticoCamara
    {
        public const string EnlaceVcRedist = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

        /// <summary>¿Carga la librería nativa de OpenCV? Si no, devuelve la explicación.</summary>
        public static (bool ok, string mensaje) ProbarOpenCv()
        {
            try
            {
                return (true, "OpenCV " + Cv2.GetVersionString());
            }
            catch (Exception e) when (e is DllNotFoundException || e is TypeInitializationException || e is BadImageFormatException || e is EntryPointNotFoundException)
            {
                var sb = new StringBuilder();
                sb.AppendLine("No se pudo cargar OpenCV (la librería nativa OpenCvSharpExtern.dll).");
                sb.AppendLine("Por eso no funciona NINGUNA fuente: ni cámara, ni fotos, ni video.");
                sb.AppendLine();
                sb.AppendLine("Solución (en este orden):");
                sb.AppendLine("1. Instala el «Microsoft Visual C++ Redistributable 2015-2022 (x64)»:");
                sb.AppendLine("   " + EnlaceVcRedist);
                sb.AppendLine("2. Si tu Windows es edición «N» (ej. Windows 10 Pro N), instala el «Media Feature Pack»:");
                sb.AppendLine("   Configuración → Aplicaciones → Características opcionales → Agregar → Media Feature Pack.");
                sb.AppendLine("3. Usa Windows de 64 bits y compila en «Any CPU» o «x64» (no x86).");
                sb.AppendLine("4. Reinicia la laptop y vuelve a abrir el programa.");
                sb.AppendLine();
                sb.AppendLine("Detalle técnico: " + Detalle(e));
                return (false, sb.ToString());
            }
        }

        static string Detalle(Exception e)
        {
            var sb = new StringBuilder();
            for (var x = e; x != null; x = x.InnerException) sb.Append(x.GetType().Name).Append(": ").Append(x.Message).Append(" | ");
            return sb.ToString();
        }

        static IEnumerable<VideoCaptureAPIs> Motores()
        {
            if (OperatingSystem.IsWindows())
            {
                yield return VideoCaptureAPIs.DSHOW;   // DirectShow: el más compatible con webcams
                yield return VideoCaptureAPIs.MSMF;    // Media Foundation
            }
            yield return VideoCaptureAPIs.ANY;
        }

        /// <summary>Lee varios cuadros: algunas cámaras entregan los primeros vacíos o negros.</summary>
        static bool EntregaImagen(VideoCapture cap, out int ancho, out int alto)
        {
            ancho = alto = 0;
            using var m = new Mat();
            var reloj = Stopwatch.StartNew();
            for (int i = 0; i < 25 && reloj.ElapsedMilliseconds < 4000; i++)
            {
                if (cap.Read(m) && !m.Empty())
                {
                    ancho = m.Cols;
                    alto = m.Rows;
                    if (i >= 2 || Cv2.Mean(m).Val0 + Cv2.Mean(m).Val1 + Cv2.Mean(m).Val2 > 3) return true;
                }
            }
            return ancho > 0;
        }

        /// <summary>
        /// Abre la cámara con el primer motor que realmente entregue imágenes.
        /// Primero intenta 1280×720; si así no entrega imágenes, la abre con su resolución por defecto.
        /// </summary>
        public static VideoCapture AbrirCamara(int indice, Action<string> avisar = null)
        {
            foreach (var motor in Motores())
            {
                foreach (bool hd in new[] { true, false })
                {
                    VideoCapture cap = null;
                    try
                    {
                        cap = new VideoCapture(indice, motor);
                        if (!cap.IsOpened()) { cap.Dispose(); break; }       // este motor no ve la cámara: probar el siguiente
                        if (hd)
                        {
                            cap.Set(VideoCaptureProperties.FrameWidth, 1280);
                            cap.Set(VideoCaptureProperties.FrameHeight, 720);
                        }
                        if (EntregaImagen(cap, out int w, out int h))
                        {
                            avisar?.Invoke($"Cámara {indice} abierta con {motor} ({w}×{h})");
                            return cap;
                        }
                        cap.Dispose();
                    }
                    catch (Exception)
                    {
                        cap?.Dispose();
                    }
                }
            }
            return null;
        }

        /// <summary>Informe completo para mostrar al usuario (y copiar si hay que pedir ayuda).</summary>
        public static string Informe(Configuracion cfg, int maxCamaras = 4)
        {
            var sb = new StringBuilder();
            sb.AppendLine("DIAGNÓSTICO DE CÁMARA — IAR-600");
            sb.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Windows: {Environment.OSVersion} · {(Environment.Is64BitOperatingSystem ? "64" : "32")} bits · proceso de {(Environment.Is64BitProcess ? "64" : "32")} bits");
            sb.AppendLine($".NET: {Environment.Version}");
            var (ok, msg) = ProbarOpenCv();
            sb.AppendLine();
            sb.AppendLine(ok ? "[OK] " + msg : "[X] " + msg);
            if (!ok) return sb.ToString();
            var problemas = ModelosIA.Problemas(cfg);
            sb.AppendLine(problemas.Count == 0 ? "[OK] Redes neuronales YuNet y SFace presentes"
                                               : "[X] Redes neuronales: " + string.Join(" ", problemas) + " → botón «Descargar modelo» o descargar_modelo_sface.bat");
            sb.AppendLine();
            sb.AppendLine("Buscando cámaras (puede tardar unos segundos)…");
            int encontradas = 0;
            for (int i = 0; i < maxCamaras; i++)
            {
                foreach (var motor in Motores())
                {
                    string resultado;
                    try
                    {
                        using var cap = new VideoCapture(i, motor);
                        if (!cap.IsOpened()) resultado = "no abre";
                        else if (EntregaImagen(cap, out int w, out int h)) { resultado = $"FUNCIONA ({w}×{h})"; encontradas++; }
                        else resultado = "abre pero NO entrega imágenes (¿la usa otra aplicación?)";
                    }
                    catch (Exception e) { resultado = "error: " + e.Message; }
                    sb.AppendLine($"  Cámara {i} · {motor}: {resultado}");
                }
            }
            sb.AppendLine();
            if (encontradas > 0)
                sb.AppendLine("Usa en «Cámara» el número que dice FUNCIONA.");
            else
            {
                sb.AppendLine("Ninguna cámara entregó imágenes. Revisa:");
                sb.AppendLine("1. Windows → Configuración → Privacidad y seguridad → Cámara:");
                sb.AppendLine("   activa «Acceso a la cámara» y «Permitir que las aplicaciones de escritorio accedan a la cámara».");
                sb.AppendLine("2. Cierra Zoom, Meet, Teams, WhatsApp, OBS o la app Cámara (solo un programa a la vez).");
                sb.AppendLine("3. Algunas laptops tienen una tecla o tapa física para la cámara (F8, F10, ícono de cámara).");
                sb.AppendLine("4. Administrador de dispositivos → Cámaras: que la cámara no tenga un signo de advertencia.");
                sb.AppendLine("5. Prueba con la app «Cámara» de Windows: si ahí tampoco se ve, es un problema del equipo.");
                sb.AppendLine("6. Mientras tanto: usa el celular como cámara IP (URL) o «Probar con fotos».");
            }
            return sb.ToString();
        }
    }
}
