using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AsistenciaFacial.Nucleo
{
    /// <summary>
    /// Contraseñas: se guardan como sal:hash (PBKDF2-SHA256, 100 000 vueltas), nunca en texto.
    /// También normaliza nombres de usuario y frena los intentos de adivinar contraseñas.
    /// </summary>
    public static class Claves
    {
        public const int LargoMinimo = 6;
        static readonly Regex UsuarioValido = new Regex("^[a-z0-9._-]{3,30}$", RegexOptions.Compiled);
        /// <summary>Se usa cuando el usuario no existe, para que la respuesta tarde lo mismo (no revela qué usuarios hay).</summary>
        static readonly string Señuelo = Hash(Guid.NewGuid().ToString());

        public static string Hash(string clave)
        {
            byte[] sal = RandomNumberGenerator.GetBytes(16);
            byte[] h = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(clave ?? ""), sal, 100_000, HashAlgorithmName.SHA256, 32);
            return Convert.ToHexString(sal) + ":" + Convert.ToHexString(h);
        }

        /// <summary>true si la clave coincide con el hash guardado. Con hash vacío o nulo compara contra un señuelo y devuelve false.</summary>
        public static bool Verificar(string clave, string guardado)
        {
            bool hay = !string.IsNullOrEmpty(guardado);
            var partes = (hay ? guardado : Señuelo).Split(':');
            if (partes.Length != 2) return false;
            try
            {
                byte[] sal = Convert.FromHexString(partes[0]), esperado = Convert.FromHexString(partes[1]);
                byte[] h = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(clave ?? ""), sal, 100_000, HashAlgorithmName.SHA256, esperado.Length);
                return CryptographicOperations.FixedTimeEquals(h, esperado) && hay;
            }
            catch (FormatException) { return false; }
        }

        /// <summary>«  María.Quispe » → «maria.quispe» (minúsculas, sin tildes ni espacios).</summary>
        public static string NormalizarUsuario(string usuario)
        {
            var sb = new StringBuilder();
            foreach (char c in (usuario ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c == 'ñ' ? 'n' : c);
            return sb.ToString().Normalize(NormalizationForm.FormC).Replace(' ', '.');
        }

        public static void ValidarUsuario(string usuario)
        {
            if (!UsuarioValido.IsMatch(usuario ?? ""))
                throw new ErrorValidacion("El usuario debe tener de 3 a 30 caracteres: letras sin tilde, números, punto, guion o guion bajo (ej. maria.quispe).");
        }

        public static void ValidarNueva(string clave, string repetida)
        {
            if ((clave ?? "").Length < LargoMinimo) throw new ErrorValidacion($"La contraseña debe tener al menos {LargoMinimo} caracteres.");
            if (clave != repetida) throw new ErrorValidacion("Las dos contraseñas no coinciden.");
        }

        /// <summary>Usuario sugerido a partir del nombre: «María Fernanda» + «Quispe Mamani» → «maria.quispe».</summary>
        public static string Sugerir(string nombres, string apellidos)
        {
            string Primera(string s) => NormalizarUsuario((s ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } p ? p[0] : "");
            string u = Regex.Replace(string.Join(".", new[] { Primera(nombres), Primera(apellidos) }).Trim('.'), "[^a-z0-9._-]", "");
            return u.Length > 30 ? u.Substring(0, 30) : u;
        }

        /// <summary>Quién entra con ese usuario y contraseña (administrador o docente activo), o null.</summary>
        public static Usuario Ingresar(Configuracion cfg, BaseDatos db, string usuario, string clave)
        {
            if (cfg.ValidarAdmin(usuario, clave)) return new Usuario { EsAdmin = true, Nombre = "Administrador" };
            return db.Autenticar(usuario, clave) is Docente d ? new Usuario { DocenteId = d.Id, Nombre = d.NombreCompleto } : null;
        }

        // ── freno contra adivinar contraseñas: 5 fallos seguidos → espera ──
        static readonly ConcurrentDictionary<string, (int fallos, DateTime hasta)> _fallos = new ConcurrentDictionary<string, (int, DateTime)>();
        const int FallosPermitidos = 5;
        static readonly TimeSpan Espera = TimeSpan.FromMinutes(1);

        /// <summary>Segundos que faltan para poder intentar de nuevo con esa clave (usuario o IP); 0 = puede intentar.</summary>
        public static int SegundosBloqueado(string clave)
        {
            if (!_fallos.TryGetValue(clave, out var f) || f.hasta <= DateTime.UtcNow) return 0;
            return (int)Math.Ceiling((f.hasta - DateTime.UtcNow).TotalSeconds);
        }

        public static void RegistrarFallo(string clave) => _fallos.AddOrUpdate(clave, _ => (1, DateTime.MinValue), (_, f) =>
        {
            if (f.hasta > DateTime.UtcNow) return f;                     // ya está en espera
            int n = f.fallos + 1;                                         // tras una espera, la cuenta vuelve a empezar desde 0
            return n >= FallosPermitidos ? (0, DateTime.UtcNow + Espera) : (n, DateTime.MinValue);
        });

        public static void RegistrarExito(string clave) => _fallos.TryRemove(clave, out _);
    }
}
