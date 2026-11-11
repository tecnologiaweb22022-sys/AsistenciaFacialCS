using System;
using System.IO;
using System.Windows.Forms;
using AsistenciaFacial.Interfaz;
using AsistenciaFacial.Nucleo;

namespace AsistenciaFacial
{
    /// <summary>
    /// Control de asistencia con reconocimiento facial — IAR-600
    /// Punto de entrada: conecta la base de datos, pide el PIN (administrador o docente) y abre la ventana principal.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.Message, "Error inesperado", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            Configuracion cfg;
            BaseDatos db;
            try
            {
                cfg = Configuracion.Cargar();
                db = new BaseDatos(cfg.BaseDeDatos);
                db.Inicializar(sqliteAnterior: Path.Combine(cfg.CarpetaDatos, "asistencia.db"), legado: cfg);
            }
            catch (ErrorValidacion e)       // p. ej. MariaDB (XAMPP) no está iniciado
            {
                MessageBox.Show(e.Message, "No se puede abrir la base de datos", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var usuario = FormIngreso.Pedir(cfg, db);
            if (usuario == null) return;
            Application.Run(new FormPrincipal(cfg, db, usuario));
        }
    }
}
