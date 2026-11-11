using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace AsistenciaFacial.Nucleo
{
    /// <summary>
    /// Servidor web local dentro de la misma aplicación (Kestrel). Por el WiFi del router,
    /// sin internet: http://IP-DE-LA-LAPTOP:5000
    ///   /               clases en curso y quién llegó (pantalla del aula)
    ///   /mi-asistencia  cada estudiante consulta su asistencia por materia con su código
    ///   /admin          panel web (administrador o docente con usuario y contraseña): lista por materia, correcciones, reportes, cámara
    /// </summary>
    public sealed class ServidorWeb
    {
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        readonly Configuracion _cfg;
        readonly BaseDatos _db;
        readonly MotorAsistencia _motor;
        readonly ConcurrentDictionary<string, Usuario> _sesiones = new ConcurrentDictionary<string, Usuario>();
        WebApplication _app;
        string Www => Path.Combine(_cfg.Carpeta, "wwwroot");
        readonly ServicioRostros _rostros;
        readonly ConcurrentDictionary<string, Queue<DateTime>> _intentos = new ConcurrentDictionary<string, Queue<DateTime>>();

        public ServidorWeb(Configuracion cfg, BaseDatos db, MotorAsistencia motor)
        {
            _cfg = cfg;
            _db = db;
            _motor = motor;
            _rostros = new ServicioRostros(Path.Combine(cfg.Carpeta, "modelos"));
        }

        public async Task IniciarAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = _cfg.Carpeta, WebRootPath = Www, Args = Array.Empty<string>(),
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(o => o.ListenAnyIP(_cfg.Puerto));
            _app = builder.Build();
            _app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(Www, "static")), RequestPath = "/static",
            });
            Rutas(_app);
            await _app.StartAsync();
        }

        public async Task DetenerAsync()
        {
            if (_app == null) return;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _app.StopAsync(cts.Token);
            }
            catch { /* cerrando */ }
            await _app.DisposeAsync();
            _app = null;
        }

        // ───────────────────────── utilidades ─────────────────────────
        static IResult J(object o, int codigo = 200) => Results.Json(o, Json, statusCode: codigo);
        static IResult Error(string m, int codigo = 400) => J(new { error = m }, codigo);
        /// <summary>Quién inició sesión en el panel web (null = nadie).</summary>
        Usuario Quien(HttpContext c) => c.Request.Cookies.TryGetValue("sesion", out var t) && t != null && _sesiones.TryGetValue(t, out var u) ? u : null;
        bool Conectado(HttpContext c) => Quien(c) != null;
        bool EsAdmin(HttpContext c) => Quien(c)?.EsAdmin == true;

        /// <summary>La materia, si el usuario puede verla (el admin todas; un docente, las suyas).</summary>
        Materia MateriaPermitida(HttpContext c, long id)
        {
            var m = _db.MateriaPorId(id) ?? throw new ErrorValidacion("Elige una materia");
            if (!(Quien(c)?.PuedeVer(m) ?? false)) throw new ErrorValidacion("Esa materia no está a tu cargo");
            return m;
        }

        /// <summary>Escapa solo lo peligroso en HTML (deja tildes y ñ legibles).</summary>
        static string E(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

        IResult Pagina(string archivo, Dictionary<string, string> valores = null)
        {
            string html = File.ReadAllText(Path.Combine(Www, archivo), Encoding.UTF8)
                .Replace("{{INSTITUCION}}", E(_cfg.Institucion)).Replace("{{GESTION}}", E(_db.GestionActiva()?.Nombre ?? ""));
            if (valores != null) foreach (var kv in valores) html = html.Replace("{{" + kv.Key + "}}", kv.Value);
            return Results.Content(html, "text/html; charset=utf-8");
        }

        static async Task<JsonElement> Cuerpo(HttpRequest r)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(r.Body);
                return doc.RootElement.Clone();
            }
            catch (JsonException) { return default; }
        }

        static JsonElement Prop(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) ? v : default;
        static string Texto(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ValueKind == JsonValueKind.Number ? e.GetRawText() : "";
        static long Entero(JsonElement e) => long.TryParse(Texto(e), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;
        static bool Logico(JsonElement e) => e.ValueKind == JsonValueKind.True || (e.ValueKind == JsonValueKind.Number && e.GetDouble() != 0);

        static async Task<IResult> Intentar(Func<Task<IResult>> f)
        {
            try { return await f(); }
            catch (ErrorValidacion e) { return Error(e.Message); }
        }

        /// <summary>Máximo 6 selfies por minuto desde un mismo celular (evita abusos).</summary>
        bool Permitir(string clave)
        {
            var cola = _intentos.GetOrAdd(clave, _ => new Queue<DateTime>());
            lock (cola)
            {
                while (cola.Count > 0 && (DateTime.UtcNow - cola.Peek()).TotalSeconds > 60) cola.Dequeue();
                if (cola.Count >= 6) return false;
                cola.Enqueue(DateTime.UtcNow);
                return true;
            }
        }

        static async Task<byte[]> Bytes(IFormFile f)
        {
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms);
            return ms.ToArray();
        }

        static string Fecha(string f) =>
            DateTime.TryParseExact(f ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? f : BaseDatos.Hoy();

        static object MateriaJson(Materia m) => new
        {
            id = m.Id, semestre = m.Semestre, semestre_texto = Academico.Semestre(m.Semestre), sigla = m.Sigla, nombre = m.Nombre,
            titulo = m.Titulo, docente = m.Docente, tolerancia_min = m.ToleranciaMin, horario = m.TextoHorario,
        };

        static object ClaseJson(ClaseEnCurso c) => new
        {
            materia = MateriaJson(c.Materia), inicio = c.Inicio, fin = c.Fin,
            limite = DateTime.Today.Add(c.Limite).ToString("HH:mm"),
        };

        object EstadoCamara()
        {
            var (_, jpeg) = _motor.Video();
            double hace = (DateTime.UtcNow - _motor.UltimoFrame).TotalSeconds;
            return new
            {
                conectada = _motor.Conectada, video = jpeg != null && hace < 5, fps = _motor.Fps, fuente = _motor.Fuente,
                registrados = _motor.EstudiantesEnGaleria, registrando = _motor.Registrando,
            };
        }

        // ───────────────────────── rutas ─────────────────────────
        void Rutas(WebApplication app)
        {
            app.MapGet("/favicon.ico", () => Results.NoContent());
            app.MapGet("/", () => Pagina("publico.html"));
            app.MapGet("/mi-asistencia", () => Pagina("mi_asistencia.html"));

            // ── marcar con una selfie desde el celular ──
            app.MapGet("/marcar", () => Pagina("marcar.html", new Dictionary<string, string>
            {
                ["HABILITADO"] = _cfg.MarcarDesdeCelular ? "true" : "false", ["PIDE_CODIGO"] = _cfg.CelularPideCodigo ? "true" : "false",
            }));
            app.MapPost("/api/marcar", async (HttpContext ctx) =>
            {
                if (!_cfg.MarcarDesdeCelular) return Error("El docente desactivó la marcación desde el celular. Mira a la cámara del aula.", 403);
                if (!Permitir(ctx.Connection.RemoteIpAddress?.ToString() ?? "?")) return Error("Demasiados intentos seguidos. Espera un minuto.", 429);
                if (!ctx.Request.HasFormContentType) return Error("Envía la foto");
                var form = await ctx.Request.ReadFormAsync();
                var archivo = form.Files["foto"];
                if (archivo == null || archivo.Length == 0) return Error("Primero toma la selfie");
                if (archivo.Length > 10_000_000) return Error("La foto es demasiado grande");
                Estudiante est = null;
                if (_cfg.CelularPideCodigo)
                {
                    est = _db.EstudiantePorCodigo(form["codigo"].ToString());
                    if (est == null || !est.Activo) return Error("No hay ningún estudiante activo con ese código", 404);
                    if (est.Muestras == 0) return Error("Tu rostro todavía no está registrado. Pide al docente que lo registre.");
                }
                byte[] foto = await Bytes(archivo);
                var rc = _cfg.Reconocimiento;
                var an = await Task.Run(() => _rostros.Analizar(foto, Math.Min(rc.RostroMinimoPx, 50), exigirUno: false));
                if (an.Error.Length > 0) return Error(an.Error);
                // Con código: verificación 1 a 1 (¿esta selfie es de ESE estudiante?). Sin código: búsqueda entre todos.
                var galeria = new Galeria(est != null ? _db.Galeria(est.Id) : _db.Galeria());
                var c = galeria.Buscar(an.Vector, rc.UmbralSimilitud, est != null ? 0 : rc.MargenMinimo);
                if (!c.Reconocido)
                {
                    _db.EventoSistema("celular_rechazado", est != null
                        ? $"Selfie con el código {est.Codigo} NO coincide con {est.NombreCompleto} (similitud {c.Similitud.ToString("0.00", CultureInfo.InvariantCulture)})"
                        : "Selfie desde un celular: rostro no reconocido", true, est?.Id);
                    return J(new { error = est != null ? "La selfie no coincide con el rostro registrado para ese código."
                                                       : "No te reconocí. Intenta con mejor luz, de frente y sin lentes oscuros.",
                                   similitud = Math.Round(c.Similitud, 2) }, 403);
                }
                var res = _db.MarcarReconocido(c.EstudianteId, c.Similitud, DateTime.Now, _cfg.Horario, "celular");
                return J(new
                {
                    ok = true, nueva = res.Nueva, tipo = res.Tipo, motivo = res.Motivo, nombre = res.Fila.Nombre, estado = res.Fila.Estado, materia = res.Fila.Materia,
                    hora = res.Fila.Hora.HasValue ? BaseDatos.Local(res.Fila.Hora.Value).ToString("HH:mm") : "",
                    salida = res.Fila.HoraSalida.HasValue ? BaseDatos.Local(res.Fila.HoraSalida.Value).ToString("HH:mm") : "",
                    similitud = Math.Round(c.Similitud, 2),
                });
            });

            // ── sesión: administrador o docente, cada uno con su usuario y contraseña ──
            IResult Ingreso(string error, string usuario = "") => Pagina("login.html", new Dictionary<string, string>
            {
                ["ERROR"] = error.Length > 0 ? $"<div class=\"error\" role=\"alert\">{E(error)}</div>" : "",
                ["USUARIO_PREVIO"] = E(usuario),
            });
            app.MapGet("/login", () => Ingreso(""));
            app.MapPost("/login", async (HttpContext ctx) =>
            {
                var form = await ctx.Request.ReadFormAsync();
                string usuario = Claves.NormalizarUsuario(form["usuario"].ToString()), clave = form["clave"].ToString();
                string ip = "web:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "?"), cuenta = "usuario:" + usuario;
                int espera = Math.Max(Claves.SegundosBloqueado(ip), Claves.SegundosBloqueado(cuenta));
                if (espera > 0) return Ingreso($"Demasiados intentos fallidos. Espera {espera} segundos e inténtalo de nuevo.", usuario);
                var u = usuario.Length == 0 ? null : await Task.Run(() => Claves.Ingresar(_cfg, _db, usuario, clave));
                if (u != null)
                {
                    Claves.RegistrarExito(ip);
                    Claves.RegistrarExito(cuenta);
                    string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                    _sesiones[token] = u;
                    ctx.Response.Cookies.Append("sesion", token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(7) });
                    string destino = ctx.Request.Query["siguiente"].ToString();
                    return Results.Redirect(destino.StartsWith("/") && !destino.StartsWith("//") ? destino : "/admin");
                }
                Claves.RegistrarFallo(ip);
                Claves.RegistrarFallo(cuenta);
                _db.EventoSistema("ingreso_fallido", $"Intento de ingreso fallido al panel web con el usuario «{usuario}» desde {ip.Substring(4)}", false, null);
                await Task.Delay(800);
                return Ingreso("Usuario o contraseña incorrectos.", usuario);
            });
            app.MapGet("/logout", (HttpContext ctx) =>
            {
                if (ctx.Request.Cookies.TryGetValue("sesion", out var t) && t != null) _sesiones.TryRemove(t, out _);
                ctx.Response.Cookies.Delete("sesion");
                return Results.Redirect("/");
            });
            app.MapGet("/admin", (HttpContext ctx) => Quien(ctx) is Usuario u
                ? Pagina("admin.html", new Dictionary<string, string> { ["USUARIO"] = E(u.Nombre), ["ES_ADMIN"] = u.EsAdmin ? "true" : "false" })
                : Results.Redirect("/login?siguiente=/admin"));
            app.MapGet("/admin/qr", (HttpContext ctx) => Conectado(ctx)
                ? Results.Content(PaginaQr(ctx.Request.Query["ip"].ToString()), "text/html; charset=utf-8")
                : Results.Redirect("/login?siguiente=/admin/qr"));
            app.MapGet("/admin/exportar/{tipo}.csv", (HttpContext ctx, string tipo) =>
            {
                if (!Conectado(ctx)) return Results.Unauthorized();
                string desde = Fecha(ctx.Request.Query["desde"]), hasta = Fecha(ctx.Request.Query["hasta"]);
                try
                {
                    if (tipo == "estudiantes" && !EsAdmin(ctx)) return Results.Unauthorized();
                    long.TryParse(ctx.Request.Query["materia"].ToString(), out long materia);
                    var materias = tipo == "asistencias" || tipo == "reporte" ? new List<long> { MateriaPermitida(ctx, materia).Id } : new List<long>();
                    return Results.File(Encoding.UTF8.GetBytes(_db.ExportarCsv(tipo, materias, desde, hasta)), "text/csv", $"{tipo}_{desde}_{hasta}.csv");
                }
                catch (ErrorValidacion) { return Results.NotFound(); }
            });
            app.MapGet("/foto/{id:long}", (HttpContext ctx, long id) =>
            {
                if (!Conectado(ctx)) return Results.Unauthorized();
                var f = _db.Foto(id);
                return f == null ? Results.NotFound() : Results.Bytes(f, "image/jpeg");
            });

            // ── video de la cámara (solo docente) ──
            app.MapGet("/video", async (HttpContext ctx) =>
            {
                if (!Conectado(ctx)) { ctx.Response.StatusCode = 401; return; }
                ctx.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
                long ultimo = -1;
                var sinImagen = DateTime.UtcNow;
                try
                {
                    while (!ctx.RequestAborted.IsCancellationRequested)
                    {
                        var (n, jpg) = _motor.Video();
                        if (n != ultimo && jpg != null)
                        {
                            ultimo = n;
                            sinImagen = DateTime.UtcNow;
                            await ctx.Response.Body.WriteAsync(Encoding.ASCII.GetBytes($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpg.Length}\r\n\r\n"), ctx.RequestAborted);
                            await ctx.Response.Body.WriteAsync(jpg, ctx.RequestAborted);
                            await ctx.Response.Body.WriteAsync(Encoding.ASCII.GetBytes("\r\n"), ctx.RequestAborted);
                            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                        }
                        else
                        {
                            if ((DateTime.UtcNow - sinImagen).TotalSeconds > 60) break;
                            await Task.Delay(40, ctx.RequestAborted);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            });
            app.MapGet("/video/foto", (HttpContext ctx) =>
            {
                if (!Conectado(ctx)) return Results.Unauthorized();
                var (_, jpg) = _motor.Video();
                return jpg == null ? Results.NoContent() : Results.Bytes(jpg, "image/jpeg");
            });

            // ── API pública ──
            // Pantalla del aula: cada clase que corre ahora con su resumen y quién llegó.
            app.MapGet("/api/publico", () =>
            {
                string hoy = BaseDatos.Hoy();
                var clases = _db.ClasesEnCurso(DateTime.Now, _cfg.Horario).Select(c =>
                {
                    var lista = _db.ListaDelDia(c.Materia.Id, hoy);
                    var llegaron = lista.Where(f => f.Hora.HasValue && f.Estado != "justificado").OrderByDescending(f => f.Hora)
                        .Select(f => new
                        {
                            nombre = f.Nombre, hora = BaseDatos.Local(f.Hora.Value).ToString("HH:mm"), estado = f.Estado,
                            salida = f.HoraSalida.HasValue ? BaseDatos.Local(f.HoraSalida.Value).ToString("HH:mm") : "",
                        });
                    return new { clase = ClaseJson(c), resumen = BaseDatos.Resumir(hoy, lista), llegaron = _cfg.ListaPublica ? llegaron : null };
                });
                return J(new
                {
                    institucion = _cfg.Institucion, gestion = _db.GestionActiva()?.Nombre ?? "", fecha = hoy, clases,
                    lista_publica = _cfg.ListaPublica,
                    camara = new { conectada = _motor.Conectada },
                    marcar_celular = _cfg.MarcarDesdeCelular,
                });
            });
            app.MapGet("/api/estudiante/{codigo}", (string codigo) =>
            {
                var (e, g, materias, dias) = _db.Historial(codigo);
                if (e == null) return Error("No hay ningún estudiante con ese código", 404);
                return J(new
                {
                    estudiante = new
                    {
                        codigo = e.Codigo, nombre = e.NombreCompleto, curso = e.Curso, rostro_registrado = e.Muestras > 0,
                        semestre = e.Semestre, semestre_texto = Academico.Semestre(e.Semestre),
                    },
                    gestion = g?.Nombre ?? "",
                    materias,
                    dias = dias.Select(d => new
                    {
                        fecha = d.Fecha, materia = d.Materia, estado = d.Estado, observacion = d.Observacion,
                        hora = d.Hora.HasValue ? BaseDatos.Local(d.Hora.Value).ToString("HH:mm") : "",
                        salida = d.HoraSalida.HasValue ? BaseDatos.Local(d.HoraSalida.Value).ToString("HH:mm") : "",
                    }),
                });
            });

            // ── API del panel (administrador o docente) ──
            var adm = app.MapGroup("/api/admin");
            adm.AddEndpointFilter(async (ctx, next) => Conectado(ctx.HttpContext) ? await next(ctx) : Error("Inicia sesión", 401));
            adm.MapGet("/materias", (HttpContext ctx) =>
            {
                var u = Quien(ctx);
                var en_curso = _db.ClasesEnCurso(DateTime.Now, _cfg.Horario).Where(c => u.PuedeVer(c.Materia)).Select(c => c.Materia.Id);
                return J(new
                {
                    usuario = u.Nombre, es_admin = u.EsAdmin, gestion = _db.GestionActiva()?.Nombre ?? "",
                    materias = _db.MateriasDe(u).Select(MateriaJson), en_curso,
                });
            });
            adm.MapGet("/estado", (HttpRequest peticion) => Intentar(() =>
            {
                var ctx = peticion.HttpContext;
                var req = ctx.Request;
                long.TryParse(req.Query["desde"].ToString(), out long desde);
                long.TryParse(req.Query["materia"].ToString(), out long materia);
                if (materia > 0) MateriaPermitida(ctx, materia);
                var e = _db.EstadoAdmin(desde, materia, Fecha(req.Query["fecha"]));
                e.Camara = EstadoCamara();
                return Task.FromResult(J(e));
            }));
            adm.MapPost("/marcar", (HttpRequest peticion) => Intentar(async () =>
            {
                var ctx = peticion.HttpContext;
                var c = await Cuerpo(ctx.Request);
                var m = MateriaPermitida(ctx, Entero(Prop(c, "materia_id")));
                string estado = Texto(Prop(c, "estado"));
                if (estado == "salida" || estado == "sin_salida")
                    _db.SalidaManual(Entero(Prop(c, "estudiante_id")), m.Id, Fecha(Texto(Prop(c, "fecha"))), quitar: estado == "sin_salida", "web");
                else
                    _db.MarcaManual(Entero(Prop(c, "estudiante_id")), m.Id, Fecha(Texto(Prop(c, "fecha"))), estado, Texto(Prop(c, "observacion")), "web");
                return J(new { ok = true });
            }));
            adm.MapGet("/estudiantes", (HttpContext ctx) => EsAdmin(ctx) ? J(new { estudiantes = _db.Estudiantes() }) : Error("Solo el administrador", 403));
            adm.MapPost("/estudiante", (HttpRequest peticion) => Intentar(async () =>
            {
                var ctx = peticion.HttpContext;
                if (!EsAdmin(ctx)) return Error("Solo el administrador", 403);
                var c = await Cuerpo(ctx.Request);
                var activo = Prop(c, "activo");
                long id = _db.GuardarEstudiante(new Estudiante
                {
                    Id = Entero(Prop(c, "id")), Codigo = Texto(Prop(c, "codigo")), Nombres = Texto(Prop(c, "nombres")),
                    Apellidos = Texto(Prop(c, "apellidos")), Curso = Texto(Prop(c, "curso")), Semestre = (int)Entero(Prop(c, "semestre")),
                    Activo = activo.ValueKind == JsonValueKind.Undefined || Logico(activo),
                });
                return J(new { ok = true, id });
            }));
            adm.MapGet("/reporte", (HttpRequest peticion) => Intentar(() =>
            {
                var ctx = peticion.HttpContext;
                var req = ctx.Request;
                string desde = Fecha(req.Query["desde"]), hasta = Fecha(req.Query["hasta"]);
                long.TryParse(req.Query["materia"].ToString(), out long materia);
                var m = MateriaPermitida(ctx, materia);
                return Task.FromResult(J(new { desde, hasta, materia = MateriaJson(m), dias = _db.DiasDeClase(m.Id, desde, hasta), filas = _db.Reporte(m.Id, desde, hasta) }));
            }));
            adm.MapPost("/eventos/atender", () => { _db.AtenderTodos(); return J(new { ok = true }); });

            // Lo que sigue (rostros y estudiantes) es solo del administrador.
            var solo = app.MapGroup("/api/admin/estudiante");
            solo.AddEndpointFilter(async (ctx, next) => EsAdmin(ctx.HttpContext) ? await next(ctx) : Error("Solo el administrador", 403));
            // Registrar el rostro con fotos tomadas desde el celular (1 a 10 fotos, una persona por foto).
            solo.MapPost("/{id:long}/fotos", (long id, HttpRequest req) => Intentar(async () =>
            {
                var est = _db.EstudiantePorId(id) ?? throw new ErrorValidacion("El estudiante no existe");
                if (!req.HasFormContentType) throw new ErrorValidacion("Envía las fotos");
                var form = await req.ReadFormAsync();
                if (form.Files.Count == 0) throw new ErrorValidacion("Elige al menos una foto");
                var muestras = new List<(float[] vector, byte[] foto)>();
                var errores = new List<string>();
                foreach (var f in form.Files.Take(10))
                {
                    var an = await Task.Run(async () => _rostros.Analizar(await Bytes(f), _cfg.Reconocimiento.RostroMinimoPx, exigirUno: true));
                    if (an.Error.Length > 0) errores.Add($"{f.FileName}: {an.Error}");
                    else muestras.Add((an.Vector, an.Cara));
                }
                if (muestras.Count == 0) throw new ErrorValidacion("Ninguna foto sirvió. " + string.Join(" · ", errores));
                // ¿Estas fotos se parecen a OTRO estudiante ya registrado?
                var otros = new Galeria(_db.Galeria().Where(m => m.id != id).ToList());
                string aviso = "";
                foreach (var (v, _) in muestras)
                {
                    var c = otros.Buscar(v, _cfg.Reconocimiento.UmbralSimilitud + 0.05, 0);
                    if (c.Reconocido) { aviso = $"Atención: estas fotos se parecen a {c.Nombre}. ¿Es la persona correcta?"; break; }
                }
                _db.GuardarRostros(id, muestras, reemplazar: form["reemplazar"].ToString() == "1");
                return J(new { ok = true, guardadas = muestras.Count, errores, aviso, total = _db.EstudiantePorId(id).Muestras });
            }));
            solo.MapPost("/{id:long}/borrar-rostro", (long id) => { _db.BorrarRostros(id); return J(new { ok = true }); });
            solo.MapPost("/{id:long}/eliminar", (long id) => Intentar(() => { _db.EliminarEstudiante(id); return Task.FromResult(J(new { ok = true })); }));
        }

        // ───────────────────────── cartel con códigos QR ─────────────────────────
        string PaginaQr(string ipPedida)
        {
            var ips = Red.IpsLocales();
            string ip = !string.IsNullOrWhiteSpace(ipPedida) ? ipPedida : !string.IsNullOrWhiteSpace(_cfg.IpParaQr) ? _cfg.IpParaQr : ips[0];
            string url = $"http://{ip}:{_cfg.Puerto}";
            var sb = new StringBuilder();
            const string logo = "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M3 8V5a2 2 0 0 1 2-2h3M16 3h3a2 2 0 0 1 2 2v3M21 16v3a2 2 0 0 1-2 2h-3M8 21H5a2 2 0 0 1-2-2v-3\"/><circle cx=\"12\" cy=\"10\" r=\"3\"/><path d=\"M7.5 17.5c1-2 2.6-3 4.5-3s3.5 1 4.5 3\"/></svg>";
            sb.Append($@"<!doctype html><html lang=""es""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>Cartel QR · Asistencia</title><link rel=""stylesheet"" href=""/static/estilo.css""><style>
main{{max-width:880px;margin:0 auto;padding:24px 18px}} .config{{padding:14px 18px;margin-bottom:18px;display:flex;gap:12px;align-items:center;flex-wrap:wrap;justify-content:space-between}}
.config form{{display:flex;gap:10px;align-items:center}} .config select{{width:auto}}
.cartel{{background:#fff;border-radius:22px;overflow:hidden;box-shadow:var(--sombra-2);text-align:center}}
.cartel .cab{{background:linear-gradient(120deg,var(--marino-950),var(--marino-700));color:#fff;padding:30px 24px 26px;border-bottom:6px solid var(--naranja-500)}}
.cartel .logo{{width:64px;height:64px;border-radius:18px;margin:0 auto 12px}} .cartel .logo svg{{width:38px;height:38px}}
.cartel h1{{margin:0;font-size:2.4rem;letter-spacing:.06em}} .cartel h2{{margin:6px 0 0;color:rgba(255,255,255,.78);font-weight:600;font-size:1.05rem}}
.qrs{{display:flex;justify-content:center;gap:22px;flex-wrap:wrap;padding:30px 24px 10px}}
.qr{{border:2px solid var(--linea);border-radius:18px;padding:16px 18px 14px;min-width:220px;max-width:300px}} .qr svg{{display:block;margin:0 auto}}
.qr b{{display:block;margin-top:10px;color:var(--marino-900);font-size:1.02rem}} .qr code{{color:var(--suave);font-size:.78rem;word-break:break-all}}
.paso{{display:inline-grid;place-items:center;width:26px;height:26px;border-radius:50%;background:var(--naranja-500);color:#fff;font-size:.85rem;margin-right:6px}}
.cartel .nota{{color:var(--suave);margin:14px 24px 28px}}
@media print{{.barra,.config{{display:none}} body{{background:#fff}} main{{padding:0}} .cartel{{box-shadow:none}} *{{-webkit-print-color-adjust:exact;print-color-adjust:exact}}}}</style></head><body>
<header class=""barra""><div class=""marca""><div class=""logo"">{logo}</div><span><b>Cartel con códigos QR</b><small>Imprime (Ctrl + P) y pégalo en la puerta del aula</small></span></div></header>
<main><div class=""tarjeta config""><form method=""get""><label for=""ip"">IP de esta laptop en el router</label><select id=""ip"" name=""ip"" onchange=""this.form.submit()"">");
            foreach (var i in ips.Union(new[] { ip })) sb.Append($"<option{(i == ip ? " selected" : "")}>{E(i)}</option>");
            sb.Append($@"</select></form><button class=""naranja"" onclick=""print()"">🖨 Imprimir</button></div>
<div class=""cartel""><div class=""cab""><div class=""logo"">{logo}</div><h1>ASISTENCIA</h1><h2>{E(_cfg.Institucion)}</h2></div><div class=""qrs"">");
            int n = 0;
            string Paso() => $"<span class=\"paso\">{++n}</span>";
            if (!string.IsNullOrWhiteSpace(_cfg.WifiNombre))
                sb.Append($"<div class=\"qr\">{CodigosQr.Svg(CodigosQr.TextoWifi(_cfg.WifiNombre, _cfg.WifiClave), 5)}<b>{Paso()}Conéctate al WiFi</b><code>{E(_cfg.WifiNombre)}</code></div>");
            if (_cfg.MarcarDesdeCelular)
                sb.Append($"<div class=\"qr\">{CodigosQr.Svg(url + "/marcar", 5)}<b>{Paso()}Marca con una selfie</b><code>{E(url)}/marcar</code></div>");
            sb.Append($"<div class=\"qr\">{CodigosQr.Svg(url + "/mi-asistencia", 5)}<b>Consulta TU asistencia</b><code>{E(url)}/mi-asistencia</code></div>");
            sb.Append($"<div class=\"qr\">{CodigosQr.Svg(url + "/", 5)}<b>Asistencia de hoy</b><code>{E(url)}/</code></div>");
            sb.Append("</div><p class=\"nota\">Para marcar asistencia: <b>mira a la cámara al llegar</b> (o usa la selfie si el docente lo permite). No necesitas internet.</p></div></main></body></html>");
            return sb.ToString();
        }
    }
}
