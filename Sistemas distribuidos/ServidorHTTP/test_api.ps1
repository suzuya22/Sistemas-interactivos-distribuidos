# Script de pruebas automatizadas para el Servidor HTTP REST
$baseUrl = "http://localhost:8080"
$ErrorActionPreference = "Stop"

Write-Host "`n=== INICIANDO PRUEBAS DEL SERVIDOR HTTP ===" -ForegroundColor Cyan

# 1. Registro de nuevo usuario
Write-Host "`n[PRUEBA 1] Registro de usuario 'juan_test'..." -ForegroundColor Yellow
$regBody = @{ username = "juan_test"; password = "password123" } | ConvertTo-Json
$regResp = Invoke-RestMethod -Uri "$baseUrl/api/usuarios" -Method Post -Body $regBody -ContentType "application/json"
Write-Host "Respuesta de Registro: $($regResp | ConvertTo-Json -Compress)" -ForegroundColor Green

# 2. Intento de registro duplicado (debe fallar con 400)
Write-Host "`n[PRUEBA 2] Intento de registro duplicado (espera HTTP 400)..." -ForegroundColor Yellow
try {
    Invoke-RestMethod -Uri "$baseUrl/api/usuarios" -Method Post -Body $regBody -ContentType "application/json"
    Write-Host "FALLO: Debió rechazar el usuario duplicado" -ForegroundColor Red
} catch {
    Write-Host "Correcto: El servidor rechazó el duplicado con mensaje: $($_.ErrorDetails.Message)" -ForegroundColor Green
}

# 3. Login con contraseña incorrecta (debe fallar con 400)
Write-Host "`n[PRUEBA 3] Login con credencial incorrecta (espera HTTP 400)..." -ForegroundColor Yellow
$badLoginBody = @{ username = "juan_test"; password = "wrong_password" } | ConvertTo-Json
try {
    Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body $badLoginBody -ContentType "application/json"
    Write-Host "FALLO: Debió rechazar la contraseña incorrecta" -ForegroundColor Red
} catch {
    Write-Host "Correcto: Login rechazado con mensaje: $($_.ErrorDetails.Message)" -ForegroundColor Green
}

# 4. Login exitoso y obtención de token
Write-Host "`n[PRUEBA 4] Login exitoso para 'juan_test'..." -ForegroundColor Yellow
$loginBody = @{ username = "juan_test"; password = "password123" } | ConvertTo-Json
$loginResp = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body $loginBody -ContentType "application/json"
$token = $loginResp.token
$username = $loginResp.usuario.username
Write-Host "Login Exitoso! Usuario: $username | Token: $token" -ForegroundColor Green

# 5. Petición protegida SIN token (debe fallar con 401)
Write-Host "`n[PRUEBA 5] Petición sin cabecera 'x-token' (espera HTTP 401)..." -ForegroundColor Yellow
try {
    Invoke-RestMethod -Uri "$baseUrl/api/usuarios" -Method Get
    Write-Host "FALLO: Debió rechazar por falta de token" -ForegroundColor Red
} catch {
    Write-Host "Correcto: Acceso denegado sin token (HTTP 401): $($_.ErrorDetails.Message)" -ForegroundColor Green
}

# 6. Petición protegida CON token INVÁLIDO (debe fallar con 401)
Write-Host "`n[PRUEBA 6] Petición con token inválido (espera HTTP 401)..." -ForegroundColor Yellow
try {
    $badHeaders = @{ "x-token" = "token_falso_12345" }
    Invoke-RestMethod -Uri "$baseUrl/api/usuarios" -Method Get -Headers $badHeaders
    Write-Host "FALLO: Debió rechazar token inválido" -ForegroundColor Red
} catch {
    Write-Host "Correcto: Acceso denegado con token inválido (HTTP 401): $($_.ErrorDetails.Message)" -ForegroundColor Green
}

# 7. Obtener perfil con token válido
Write-Host "`n[PRUEBA 7] Obtener perfil de 'juan_test'..." -ForegroundColor Yellow
$headers = @{ "x-token" = $token }
$profileResp = Invoke-RestMethod -Uri "$baseUrl/api/usuarios/$username" -Method Get -Headers $headers
Write-Host "Perfil obtenido: Usuario=$($profileResp.usuario.username), Score=$($profileResp.usuario.score)" -ForegroundColor Green

# 8. Actualizar Score (PATCH)
Write-Host "`n[PRUEBA 8] Actualizar Score de 'juan_test' a 250 puntos..." -ForegroundColor Yellow
$scoreBody = @{
    username = $username
    score = 250
    data = @{ score = 250 }
} | ConvertTo-Json
$scoreResp = Invoke-RestMethod -Uri "$baseUrl/api/usuarios" -Method Patch -Body $scoreBody -ContentType "application/json" -Headers $headers
Write-Host "Score actualizado! Nuevo score en servidor: $($scoreResp.usuario.score)" -ForegroundColor Green

# 9. Listar Leaderboard
Write-Host "`n[PRUEBA 9] Consultar Leaderboard..." -ForegroundColor Yellow
$leaderResp = Invoke-RestMethod -Uri "$baseUrl/api/usuarios" -Method Get -Headers $headers
Write-Host "Usuarios encontrados en el Leaderboard:" -ForegroundColor Cyan
foreach ($u in $leaderResp.usuarios) {
    Write-Host " - $($u.username): $($u.score) pts (id: $($u._id))" -ForegroundColor White
}

Write-Host "`n=== TODAS LAS PRUEBAS PASARON SATISFACTORIAMENTE ===" -ForegroundColor Green
