#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Instala ou atualiza o WatcherApp como servico do Windows.

.DESCRIPTION
    Estrutura esperada do pacote (copie a pasta inteira para o servidor):

        Pacote\
            Instalar-Watcher.ps1
            app\                <- saida do "dotnet publish"

    O que o script faz, na ordem:
      1. Para o servico, se ele ja existir.
      2. Copia os arquivos para a pasta de destino, SEM sobrescrever o config.json
         nem a pasta Logs que ja estiverem la.
      3. Cria o servico (se ainda nao existir).
      4. Configura: inicio automatico com atraso e reinicio automatico em caso de falha.
      5. (Opcional) Troca a conta do servico.
      6. (Opcional) Grava a senha do email so para este servico.
      7. Inicia o servico e mostra o resultado.

    Os textos estao sem acento de proposito: o PowerShell 5.1 le arquivos .ps1
    sem BOM como ANSI e os acentos sairiam trocados.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Instalar-Watcher.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Instalar-Watcher.ps1 -ContaServico "DOMINIO\svc_watcher"

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Instalar-Watcher.ps1 -Desinstalar
#>
[CmdletBinding()]
param(
    # Pasta com os arquivos publicados (saida do dotnet publish).
    # Vazio = subpasta "app" ao lado deste script.
    [string]$Origem,

    # Pasta onde o servico fica instalado.
    [string]$Destino = 'C:\Servicos\WatcherApp',

    [string]$NomeServico = 'WatcherApp',
    [string]$NomeExibicao = 'Watcher de Servicos',

    # Nome da variavel de ambiente da senha (o mesmo de Email.VariavelAmbienteSenha no config.json).
    [string]$VariavelSenha = 'WATCHER_SMTP_SENHA',

    # Conta do servico (ex.: DOMINIO\usuario ou .\usuario). Vazio = LocalSystem.
    [string]$ContaServico,

    # Nao perguntar a senha do email (relay sem login, ou senha ja gravada antes).
    [switch]$SemSenhaEmail,

    # Remove o servico (a pasta de destino e mantida).
    [switch]$Desinstalar
)

$ErrorActionPreference = 'Stop'
$SegundosParaParar = 180

# No Windows PowerShell 5.1, $PSScriptRoot vem vazio dentro do bloco param() quando o
# script e chamado com -File. Por isso o padrao de $Origem e resolvido aqui, no corpo.
if (-not $Origem) {
    $pastaDoScript = $PSScriptRoot

    if (-not $pastaDoScript) {
        $pastaDoScript = Split-Path -Parent $MyInvocation.MyCommand.Path
    }

    $Origem = Join-Path $pastaDoScript 'app'
}

function Escrever-Passo {
    param([string]$Texto)
    Write-Host ''
    Write-Host "==> $Texto" -ForegroundColor Cyan
}

function Invocar-Sc {
    param([string[]]$Argumentos)

    $saida = & sc.exe @Argumentos

    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe $($Argumentos -join ' ') falhou (codigo $LASTEXITCODE): $($saida -join ' ')"
    }
}

function Parar-ServicoSeExistir {
    $servico = Get-Service -Name $NomeServico -ErrorAction SilentlyContinue

    if (-not $servico) {
        return $false
    }

    if ($servico.Status -ne 'Stopped') {
        Write-Host "Parando o servico (pode levar ate $SegundosParaParar s se houver recuperacao em andamento)..."
        Stop-Service -Name $NomeServico -Force -NoWait

        try {
            $servico.WaitForStatus('Stopped', [TimeSpan]::FromSeconds($SegundosParaParar))
        }
        catch {
            throw "O servico nao parou em $SegundosParaParar s. Confira no services.msc e rode o script de novo."
        }
    }

    return $true
}

# ---------------------------------------------------------------- Desinstalar
if ($Desinstalar) {
    Escrever-Passo "Removendo o servico '$NomeServico'"

    if (Parar-ServicoSeExistir) {
        Invocar-Sc @('delete', $NomeServico)
        Write-Host "Servico removido. A pasta '$Destino' foi mantida (config.json e logs)." -ForegroundColor Green
    }
    else {
        Write-Host "O servico '$NomeServico' nao existe neste computador."
    }

    return
}

# ---------------------------------------------------------------- Conferencias
Escrever-Passo 'Conferindo o pacote'

$exeOrigem = Join-Path $Origem 'WatcherApp.exe'

if (-not (Test-Path $exeOrigem)) {
    throw "Nao encontrei '$exeOrigem'. Publique o projeto em '$Origem' antes de instalar."
}

Write-Host "Origem : $Origem"
Write-Host "Destino: $Destino"
Write-Host "Servidor: $env:COMPUTERNAME"

# ---------------------------------------------------------------- 1. Parar
Escrever-Passo 'Parando o servico, se ja existir'

$jaExistia = Parar-ServicoSeExistir

if ($jaExistia) {
    Write-Host 'Servico existente: sera atualizado.'
}
else {
    Write-Host 'Servico ainda nao existe: sera criado.'
}

# ---------------------------------------------------------------- 2. Copiar
Escrever-Passo 'Copiando os arquivos'

New-Item -ItemType Directory -Path $Destino -Force | Out-Null

# /XF config.json e /XD Logs: nunca sobrescrever a configuracao nem os logs do servidor.
& robocopy.exe $Origem $Destino /E /XF config.json /XD Logs /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null

if ($LASTEXITCODE -ge 8) {
    throw "Falha ao copiar os arquivos (robocopy devolveu o codigo $LASTEXITCODE)."
}

$configDestino = Join-Path $Destino 'config.json'
$configNovo = $false

if (Test-Path $configDestino) {
    Write-Host 'config.json existente MANTIDO (nao foi sobrescrito).'
}
else {
    Copy-Item -Path (Join-Path $Origem 'config.json') -Destination $configDestino
    $configNovo = $true
    Write-Host 'config.json copiado do pacote (modelo). Ele precisa ser ajustado para este servidor.' -ForegroundColor Yellow
}

# ---------------------------------------------------------------- 3. Criar
$exeDestino = Join-Path $Destino 'WatcherApp.exe'

if (-not $jaExistia) {
    Escrever-Passo 'Criando o servico'

    New-Service `
        -Name $NomeServico `
        -BinaryPathName "`"$exeDestino`"" `
        -DisplayName $NomeExibicao `
        -Description 'Monitora servicos do Windows, reinicia os que param ou travam e envia alertas.' `
        -StartupType Automatic | Out-Null
}

# ---------------------------------------------------------------- 4. Configurar
Escrever-Passo 'Configurando inicio com atraso e reinicio automatico'

# Sobe sozinho no boot, mas depois que a rede e os outros servicos ja estabilizaram.
Invocar-Sc @('config', $NomeServico, 'start=', 'delayed-auto')

# Se o watcher cair: reinicia apos 60s, ate 3 vezes; o contador zera depois de 1 dia.
Invocar-Sc @('failure', $NomeServico, 'reset=', '86400', 'actions=', 'restart/60000/restart/60000/restart/60000')

# ---------------------------------------------------------------- 5. Conta
if ($ContaServico) {
    Escrever-Passo "Configurando a conta do servico: $ContaServico"

    $credencial = Get-Credential -UserName $ContaServico -Message 'Senha da conta do servico'
    $senhaConta = $credencial.GetNetworkCredential().Password

    Invocar-Sc @('config', $NomeServico, 'obj=', $ContaServico, 'password=', $senhaConta)

    Write-Host 'ATENCAO: a conta precisa do direito "Fazer logon como servico".' -ForegroundColor Yellow
    Write-Host 'Este script NAO concede esse direito. Se o servico falhar ao iniciar (erro 1069),' -ForegroundColor Yellow
    Write-Host 'informe a conta uma vez pela aba Logon do services.msc, ou peca ao TI via politica de grupo.' -ForegroundColor Yellow
}

# ---------------------------------------------------------------- 6. Senha do email
if (-not $SemSenhaEmail) {
    Escrever-Passo 'Senha da conta de email'

    $segura = Read-Host 'Digite a senha do email (Enter em branco mantem a que ja estiver gravada)' -AsSecureString
    $senhaEmail = (New-Object System.Net.NetworkCredential('', $segura)).Password

    if ($senhaEmail) {
        $chave = "HKLM:\SYSTEM\CurrentControlSet\Services\$NomeServico"
        $atual = @((Get-ItemProperty -Path $chave -Name Environment -ErrorAction SilentlyContinue).Environment)
        $outras = @($atual | Where-Object { $_ -and ($_ -notlike "$VariavelSenha=*") })
        $novo = [string[]]($outras + "$VariavelSenha=$senhaEmail")

        New-ItemProperty -Path $chave -Name Environment -PropertyType MultiString -Value $novo -Force | Out-Null

        Write-Host "Senha gravada na variavel '$VariavelSenha', visivel so para este servico."
    }
    else {
        Write-Host 'Nenhuma senha digitada: nada foi alterado.'
    }
}

# ---------------------------------------------------------------- 7. Iniciar
if ($configNovo) {
    Escrever-Passo 'Instalado, mas NAO iniciado'

    Write-Host "Ajuste o arquivo '$configDestino' para este servidor (servicos, caminhos de log, email)" -ForegroundColor Yellow
    Write-Host "e depois inicie com:  Start-Service $NomeServico" -ForegroundColor Yellow
    return
}

Escrever-Passo 'Iniciando o servico'

$arquivoErro = Join-Path $Destino 'watcher-erro-inicializacao.txt'
Remove-Item -Path $arquivoErro -ErrorAction SilentlyContinue

$falhaAoIniciar = $null

try {
    Start-Service -Name $NomeServico
}
catch {
    $falhaAoIniciar = $_.Exception.Message
}

Start-Sleep -Seconds 5

$info = Get-CimInstance -ClassName Win32_Service -Filter "Name='$NomeServico'"

Write-Host ''
Write-Host "Servico : $($info.Name)"
Write-Host "Estado  : $($info.State)"
Write-Host "Inicio  : $($info.StartMode) (com atraso)"
Write-Host "Conta   : $($info.StartName)"

if ($info.State -eq 'Running') {
    Write-Host ''
    Write-Host 'Instalacao concluida: o servico esta em execucao.' -ForegroundColor Green
}
else {
    Write-Host ''
    Write-Host 'O servico NAO ficou em execucao.' -ForegroundColor Red

    if ($falhaAoIniciar) {
        Write-Host "Motivo informado pelo Windows: $falhaAoIniciar"
    }

    if (Test-Path $arquivoErro) {
        Write-Host ''
        Write-Host 'Conteudo de watcher-erro-inicializacao.txt:' -ForegroundColor Yellow
        Get-Content -Path $arquivoErro -Encoding UTF8
    }
}

# Mostra o fim do log do dia, se estiver na pasta padrao (PastaLogs = "Logs").
$pastaLogs = Join-Path $Destino 'Logs'

if (Test-Path $pastaLogs) {
    $logMaisRecente = Get-ChildItem -Path $pastaLogs -Filter 'watcher-*.txt' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($logMaisRecente) {
        Write-Host ''
        Write-Host "Ultimas linhas de $($logMaisRecente.Name):" -ForegroundColor Cyan
        Get-Content -Path $logMaisRecente.FullName -Tail 12 -Encoding UTF8
    }
}
