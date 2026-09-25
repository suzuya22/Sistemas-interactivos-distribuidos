using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;

// ==========================================
// CONFIGURACIÓN DEL SERVIDOR
// ==========================================
string ip = "127.0.0.1";
int port = 1234;
string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "usuarios.json");

List<Usuario> MisUsuarios = new List<Usuario>();

// =========================================================================
// MECANISMO DE TOKEN FUNCIONAL: ALMACÉN DE SESIONES ACTIVAS (token -> username)
// =========================================================================
Dictionary<string, string> ActiveTokens = new Dictionary<string, string>(); 

// Cargar usuarios existentes si existen en el archivo JSON
CargarUsuarios();

HttpListener listener = new HttpListener();
listener.Prefixes.Add($"http://{ip}:{port}/");
listener.Prefixes.Add($"http://localhost:{port}/");

listener.Start();
Console.WriteLine($"===================================================");
Console.WriteLine($" Servidor HTTP iniciado en http://{ip}:{port}/");
Console.WriteLine($" También escuchando en http://localhost:{port}/");
Console.WriteLine($"===================================================");

Console.CancelKeyPress += (sender, e) =>
{
    Console.WriteLine("\nCerrando servidor HTTP...");
    listener.Stop();
    Environment.Exit(0);
};

while (true)
{   
    var context = await listener.GetContextAsync();
    HandleRequest(context); 
}

async void HandleRequest(HttpListenerContext context)
{
    var request = context.Request;
    var response = context.Response;

    string method = request.HttpMethod.ToUpperInvariant();
    string path = request.Url?.AbsolutePath ?? request.RawUrl ?? "/";
    if (path.Length > 1 && path.EndsWith('/')) path = path.TrimEnd('/');

    Console.WriteLine($"Received request: {method} {path}");

    // Soporte Preflight CORS (para WebGL y navegadores)
    if (method == "OPTIONS")
    {
        SendResponse(response, 200, "");
        return;
    }

    // =========================================================================
    // 1. IMPLEMENTAR LAS RESPUESTAS PARA LA RUTA DE REGISTRO (POST /api/usuarios)
    // =========================================================================
    if (method == "POST" && path == "/api/usuarios")
    {
        var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var requestbody = await reader.ReadToEndAsync();

        AuthData? authData = JsonConvert.DeserializeObject<AuthData>(requestbody);

        if (authData == null || string.IsNullOrEmpty(authData.username))
        {
            var error = new ErrorMessage { msg = "Debe enviar el usuario", field = "username" };
            string contentResponse = JsonConvert.SerializeObject(error);
            SendResponse(response, 400, contentResponse);
        }
        else if (string.IsNullOrEmpty(authData.password))
        {
            var error = new ErrorMessage { msg = "Debe enviar la contraseña", field = "password" };
            string contentResponse = JsonConvert.SerializeObject(error);
            SendResponse(response, 400, contentResponse);
        }
        else if (MisUsuarios.Any(u => u.username.Equals(authData.username, StringComparison.OrdinalIgnoreCase)))
        {
            var error = new ErrorMessage { msg = "Ya existe usuario con ese username", field = "username" };
            string contentResponse = JsonConvert.SerializeObject(error);
            SendResponse(response, 400, contentResponse);
        }
        else
        {
            Usuario nuevoUsuario = new Usuario 
            { 
                _id = Guid.NewGuid().ToString("N").Substring(0, 24),
                username = authData.username, 
                password = authData.password,
                estado = true,
                data = new { score = 0 }
            };
            MisUsuarios.Add(nuevoUsuario);
            GuardarUsuarios();

            string jsonResponse = JsonConvert.SerializeObject(new RegistroResponse(nuevoUsuario));
            SendResponse(response, 200, jsonResponse);
        }
        return;
    }

    // =========================================================================
    // 2. IMPLEMENTAR LAS RESPUESTAS PARA LA RUTA DE LOGIN (POST /api/auth/login)
    // =========================================================================
    if (method == "POST" && path == "/api/auth/login")
    {
        var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        string requestBody = await reader.ReadToEndAsync();

        AuthData? authData = JsonConvert.DeserializeObject<AuthData>(requestBody);

        if (authData == null || string.IsNullOrEmpty(authData.username))
        {
            var error = new ErrorMessage { msg = "debe enviar el campo username en la petición", field = "username" };
            string contentResponse = JsonConvert.SerializeObject(error);
            SendResponse(response, 400, contentResponse);
            return;
        }
        if (string.IsNullOrEmpty(authData.password))
        {
            var error = new ErrorMessage { msg = "debe enviar el campo password en la petición", field = "password" };
            string contentResponse = JsonConvert.SerializeObject(error);
            SendResponse(response, 400, contentResponse);
            return;
        }

        Usuario? userDb = MisUsuarios.FirstOrDefault(user => user.username.Equals(authData.username, StringComparison.OrdinalIgnoreCase));
        if (userDb == null)
        {
            var error = new ErrorMessage { msg = "Usuario o contraseña no son correctos - correo" };
            string contentResponse = JsonConvert.SerializeObject(error);
            SendResponse(response, 400, contentResponse);
        }
        else if (userDb.password != authData.password)
        {
            var error = new ErrorMessage { msg = "Usuario o contraseña no son correctos - password" };
            string contentResponse = JsonConvert.SerializeObject(error);
            SendResponse(response, 400, contentResponse);
        }
        else
        {
            // =========================================================================
            // 3. IMPLEMENTAR UN MECANISMO DE TOKEN FUNCIONAL (GENERACIÓN DE SESIÓN)
            // =========================================================================
            string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            ActiveTokens[token] = userDb.username;

            string jsonResponse = JsonConvert.SerializeObject(new RegistroResponse(userDb, token));
            SendResponse(response, 200, jsonResponse);
        }
        return;
    }

    // =========================================================================
    // 4. IMPLEMENTAR LAS RESPUESTAS PARA LA RUTA DE LISTAR USUARIOS (GET /api/usuarios)
    // =========================================================================
    if (method == "GET" && path == "/api/usuarios")
    {
        // Validación del token funcional
        string? token = request.Headers["x-token"];
        Console.WriteLine($"Received token: {token}");

        if (string.IsNullOrEmpty(token))
        {
            var error = new ErrorMessage { msg = "No se proporcionó un token de autenticación" };
            string contentResponse = JsonConvert.SerializeObject(error);
            SendResponse(response, 401, contentResponse);
        }
        else if (!ActiveTokens.ContainsKey(token))
        {
            var error = new ErrorMessage { msg = "Token no válido o sesión expirada" };
            string contentResponse = JsonConvert.SerializeObject(error);
            SendResponse(response, 401, contentResponse);
        }
        else
        {
            // Devolvemos la lista de usuarios con el formato que Unity espera
            var listaDtos = MisUsuarios.Select(u => new UsuarioDto 
            {
                _id = u._id,
                username = u.username,
                estado = u.estado,
                data = u.data
            }).ToList();

            string jsonResponse = JsonConvert.SerializeObject(new { usuarios = listaDtos });
            SendResponse(response, 200, jsonResponse);
        }
        return;
    }

    // =========================================================================
    // 5. IMPLEMENTAR LAS RESPUESTAS PARA LA RUTA DE OBTENER PERFIL (GET /api/usuarios/{username})
    // =========================================================================
    if (method == "GET" && path.StartsWith("/api/usuarios/"))
    {
        // Validación del token funcional
        string? token = request.Headers["x-token"];
        if (string.IsNullOrEmpty(token) || !ActiveTokens.ContainsKey(token))
        {
            var error = new ErrorMessage { msg = "Token no válido o no proporcionado" };
            SendResponse(response, 401, JsonConvert.SerializeObject(error));
            return;
        }

        string targetUsername = path.Substring("/api/usuarios/".Length).Trim();
        Usuario? userDb = MisUsuarios.FirstOrDefault(u => u.username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));

        if (userDb == null)
        {
            var error = new ErrorMessage { msg = $"Usuario '{targetUsername}' no encontrado" };
            SendResponse(response, 404, JsonConvert.SerializeObject(error));
        }
        else
        {
            UsuarioDto dto = new UsuarioDto { _id = userDb._id, username = userDb.username, estado = userDb.estado, data = userDb.data };
            string jsonResponse = JsonConvert.SerializeObject(new { usuario = dto });
            SendResponse(response, 200, jsonResponse);
        }
        return;
    }

    // =========================================================================
    // 6. IMPLEMENTAR LAS RESPUESTAS PARA LA RUTA DE ACTUALIZAR USUARIO (PATCH /api/usuarios)
    // =========================================================================
    if (method == "PATCH" && path == "/api/usuarios")
    {
        // Validación del token funcional
        string? token = request.Headers["x-token"];
        if (string.IsNullOrEmpty(token) || !ActiveTokens.ContainsKey(token))
        {
            var error = new ErrorMessage { msg = "Token no válido o no proporcionado" };
            SendResponse(response, 401, JsonConvert.SerializeObject(error));
            return;
        }

        var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        string requestBody = await reader.ReadToEndAsync();

        UpdateScorePayload? payload = JsonConvert.DeserializeObject<UpdateScorePayload>(requestBody);
        string usernameTarget = !string.IsNullOrEmpty(payload?.username) ? payload.username : ActiveTokens[token];

        Usuario? userDb = MisUsuarios.FirstOrDefault(u => u.username.Equals(usernameTarget, StringComparison.OrdinalIgnoreCase));
        if (userDb == null)
        {
            var error = new ErrorMessage { msg = "Usuario no encontrado" };
            SendResponse(response, 404, JsonConvert.SerializeObject(error));
        }
        else
        {
            int nuevoScore = payload?.data?.score ?? payload?.score ?? 0;
            userDb.data = new { score = nuevoScore };
            GuardarUsuarios();

            UsuarioDto dto = new UsuarioDto { _id = userDb._id, username = userDb.username, estado = userDb.estado, data = userDb.data };
            string jsonResponse = JsonConvert.SerializeObject(new { usuario = dto, msg = "Puntaje actualizado con éxito" });
            SendResponse(response, 200, jsonResponse);
        }
        return;
    }

    // Ruta no encontrada
    SendResponse(response, 404, JsonConvert.SerializeObject(new ErrorMessage { msg = "Ruta no encontrada" }));
}

async void SendResponse(HttpListenerResponse response, int statusCode, string content)
{
    // Cabeceras CORS esenciales para comunicación desde WebGL / Unity
    response.Headers["Access-Control-Allow-Origin"] = "*";
    response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PATCH, OPTIONS, PUT, DELETE";
    response.Headers["Access-Control-Allow-Headers"] = "Content-Type, x-token, Authorization";

    int contentLength = Encoding.UTF8.GetByteCount(content);
    response.ContentLength64 = contentLength;
    response.StatusCode = statusCode;
    response.ContentType = "application/json";

    var output = response.OutputStream;
    var buffer = Encoding.UTF8.GetBytes(content);

    await output.WriteAsync(buffer, 0, buffer.Length);
    output.Close();
}

void CargarUsuarios()
{
    try
    {
        if (File.Exists(dbPath))
        {
            string json = File.ReadAllText(dbPath, Encoding.UTF8);
            var guardados = JsonConvert.DeserializeObject<List<Usuario>>(json);
            if (guardados != null)
            {
                MisUsuarios = guardados;
                return;
            }
        }
    }
    catch { }

    // Usuario demo inicial si la lista está vacía
    if (MisUsuarios.Count == 0)
    {
        MisUsuarios.Add(new Usuario
        {
            _id = "5f6eec605ada4e648c59fe65",
            username = "sebas12",
            password = "123",
            estado = true,
            data = new { score = 2 }
        });
        GuardarUsuarios();
    }
}

void GuardarUsuarios()
{
    try
    {
        string json = JsonConvert.SerializeObject(MisUsuarios, Formatting.Indented);
        File.WriteAllText(dbPath, json, Encoding.UTF8);
    }
    catch { }
}

public class Usuario
{
    public string _id { get; set; } = string.Empty;
    public string username { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;
    public bool estado { get; set; } = true;
    public object data { get; set; } = new { score = 0 };
}

public class ErrorMessage
{
    public string msg { get; set; } = string.Empty;
    public string? field { get; set; }
}

public class AuthData
{
    public string? username { get; set; }
    public string? password { get; set; }
}

public class RegistroResponse
{
    public UsuarioDto usuario;
    public string token;

    public RegistroResponse(Usuario _usuario, string tokenGenerado = "your_generated_token_here")
    {
        this.usuario = new UsuarioDto { username = _usuario.username, _id = _usuario._id, data = _usuario.data, estado = true };
        this.token = tokenGenerado;
    }
}

public class UsuarioDto
{
    public string _id { get; set; } = string.Empty;
    public string username { get; set; } = string.Empty;
    public bool estado { get; set; } = true;
    public object data { get; set; } = new { score = 0 };
}

public class UpdateScorePayload
{
    public string? username { get; set; }
    public int? score { get; set; }
    public ScoreSubData? data { get; set; }
}

public class ScoreSubData
{
    public int score { get; set; }
}
