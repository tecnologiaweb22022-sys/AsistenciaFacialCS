using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace AsistenciaFacial.Nucleo
{
    /// <summary>
    /// Revisa que estén las dos redes neuronales (YuNet y SFace) y descarga SFace si falta.
    /// Sin ellas la cámara se abre pero el programa no puede reconocer a nadie.
    /// </summary>
    public static class ModelosIA
    {
        public const string YuNet = "face_detection_yunet_2023mar.onnx";
        public const string SFace = "face_recognition_sface_2021dec.onnx";
        const long TamYuNet = 232_589, TamSFace = 38_696_353;
        public const string UrlSFace = "https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx";

        public static string Carpeta(Configuracion cfg) => Path.Combine(cfg.Carpeta, "modelos");

        /// <summary>Lista de problemas (vacía = todo bien).</summary>
        public static List<string> Problemas(Configuracion cfg)
        {
            var lista = new List<string>();
            void Revisar(string archivo, long tam, string para)
            {
                string ruta = Path.Combine(Carpeta(cfg), archivo);
                if (!File.Exists(ruta)) lista.Add($"Falta {archivo} ({para}).");
                else if (new FileInfo(ruta).Length != tam)
                    lista.Add($"{archivo} está incompleto o dañado ({new FileInfo(ruta).Length:N0} de {tam:N0} bytes). Descárgalo otra vez.");
            }
            Revisar(YuNet, TamYuNet, "detecta los rostros");
            Revisar(SFace, TamSFace, "reconoce quién es");
            return lista;
        }

        public static bool FaltaSFace(Configuracion cfg) =>
            !File.Exists(Path.Combine(Carpeta(cfg), SFace)) || new FileInfo(Path.Combine(Carpeta(cfg), SFace)).Length != TamSFace;

        /// <summary>Descarga SFace (37 MB) del OpenCV Model Zoo. Informa el avance en % (0-100).</summary>
        public static async Task DescargarSFaceAsync(Configuracion cfg, IProgress<int> avance)
        {
            Directory.CreateDirectory(Carpeta(cfg));
            string destino = Path.Combine(Carpeta(cfg), SFace), temporal = destino + ".descargando";
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) })
            using (var resp = await http.GetAsync(UrlSFace, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? TamSFace;
                using var origen = await resp.Content.ReadAsStreamAsync();
                using var archivo = File.Create(temporal);
                var buffer = new byte[81920];
                long leidos = 0;
                int n, ultimo = -1;
                while ((n = await origen.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await archivo.WriteAsync(buffer, 0, n);
                    leidos += n;
                    int pct = (int)(leidos * 100 / Math.Max(1, total));
                    if (pct != ultimo) { avance?.Report(pct); ultimo = pct; }
                }
            }
            if (new FileInfo(temporal).Length != TamSFace)
            {
                File.Delete(temporal);
                throw new IOException("La descarga quedó incompleta. Revisa la conexión e inténtalo de nuevo.");
            }
            File.Move(temporal, destino, overwrite: true);
        }
    }
}
