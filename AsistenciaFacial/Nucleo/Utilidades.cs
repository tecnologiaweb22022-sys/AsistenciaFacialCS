using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using QRCoder;

namespace AsistenciaFacial.Nucleo
{
    public static class Red
    {
        /// <summary>IPs de esta laptop en la red local (la primera es la que va al router).</summary>
        public static List<string> IpsLocales()
        {
            var ips = new List<string>();
            try
            {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect("10.255.255.255", 1);            // no envía nada: solo elige la interfaz
                if (s.LocalEndPoint is IPEndPoint ep) ips.Add(ep.Address.ToString());
            }
            catch (SocketException) { }
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                             .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                             .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count))
                    foreach (var a in ni.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork) ips.Add(a.Address.ToString());
            }
            catch (NetworkInformationException) { }
            var r = ips.Where(i => !i.StartsWith("127.") && !i.StartsWith("169.254.")).Distinct().ToList();
            if (r.Count == 0) r.Add("127.0.0.1");
            return r;
        }
    }

    /// <summary>Códigos QR con QRCoder (sin internet).</summary>
    public static class CodigosQr
    {
        public static string TextoWifi(string red, string clave) =>
            new PayloadGenerator.WiFi(red, clave ?? "", string.IsNullOrEmpty(clave)
                ? PayloadGenerator.WiFi.Authentication.nopass : PayloadGenerator.WiFi.Authentication.WPA).ToString();

        public static string Svg(string texto, int pixelesPorModulo)
        {
            using var gen = new QRCodeGenerator();
            using var datos = gen.CreateQrCode(texto, QRCodeGenerator.ECCLevel.M);
            using var svg = new SvgQRCode(datos);
            return svg.GetGraphic(pixelesPorModulo, "#14213d", "#ffffff");
        }

        public static byte[] Png(string texto, int pixelesPorModulo)
        {
            using var gen = new QRCodeGenerator();
            using var datos = gen.CreateQrCode(texto, QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(datos);
            return png.GetGraphic(pixelesPorModulo);
        }
    }
}
