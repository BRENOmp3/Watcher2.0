using System.Collections.Concurrent;
using System.ServiceProcess;
using WatcherApp.Models;

namespace WatcherApp.Services;

public class VerificadorDeSistema
{
    // Cada problema tem o seu nome: alertar um nunca esconde o outro.
    private const string ProblemaLogAusente = "log-ausente";
    private const string ProblemaServicoInexistente = "servico-inexistente";
    private const string ProblemaEstadoPendente = "estado-pendente";
    private const string ProblemaErroInesperado = "erro-inesperado";
    private const string ProblemaEmFalha = "em-falha";

    private readonly ServicoWindowsService _servicos;
    private readonly LogMonitor _logMonitor;
    private readonly RecuperadorDeSistema _recuperador;
    private readonly IAlertador _alertador;
    private readonly IWatcherLogger _watcherLogger;
    private readonly TimeSpan _toleranciaPendente;

    private readonly MemoriaDeAlertas _memoria;

    // Serviços que têm uma recuperação em andamento agora.
    private readonly ConcurrentDictionary<string, byte> _emRecuperacao = new();

    // Serviços em falha: a recuperação não funcionou e eles ainda não voltaram.
    private readonly ConcurrentDictionary<string, byte> _emFalha = new();

    // Desde quando cada serviço está em um estado de transição (*Pending).
    private readonly ConcurrentDictionary<string, DateTime> _pendenteDesde = new();

    // A tarefa de recuperação mais recente de cada serviço, para esperá-las ao encerrar.
    private readonly ConcurrentDictionary<string, Task> _tarefas = new();

    public VerificadorDeSistema(
        ServicoWindowsService servicos,
        LogMonitor logMonitor,
        RecuperadorDeSistema recuperador,
        IAlertador alertador,
        IWatcherLogger watcherLogger,
        WatcherConfig config)
    {
        _servicos = servicos;
        _logMonitor = logMonitor;
        _recuperador = recuperador;
        _alertador = alertador;
        _watcherLogger = watcherLogger;
        _toleranciaPendente = TimeSpan.FromSeconds(config.SegundosToleranciaServicoPendente);
        _memoria = new MemoriaDeAlertas(TimeSpan.FromMinutes(config.MinutosEntreLembretesDeFalha));
    }

    public void Verificar(SistemaMonitorado sistema, CancellationToken token)
    {
        try
        {
            VerificarInterno(sistema, token);

            if (_memoria.Resolver(sistema.Nome, ProblemaErroInesperado))
            {
                _watcherLogger.Informacao(
                    $"[{sistema.Nome}] A verificação voltou a funcionar sem erros.");
            }
        }
        catch (Exception ex)
        {
            var detalhe = $"{ex.GetType().Name}: {ex.Message}";

            if (_memoria.Registrar(sistema.Nome, ProblemaErroInesperado))
            {
                _alertador.Alertar(
                    NivelAlerta.Erro,
                    $"Erro inesperado ao verificar {sistema.Nome}",
                    $"{detalhe}. Erros iguais nos próximos ciclos ficam só no log.");
            }
            else
            {
                _watcherLogger.Informacao(
                    $"[{sistema.Nome}] O erro de verificação continua: {detalhe}");
                Lembrar(sistema, ProblemaErroInesperado, $"O erro de verificação continua: {detalhe}.");
            }
        }
    }

    private void VerificarInterno(SistemaMonitorado sistema, CancellationToken token)
    {
        if (_emRecuperacao.ContainsKey(sistema.Nome))
        {
            _watcherLogger.Informacao($"[{sistema.Nome}] Recuperação em andamento.");
            return;
        }

        if (!_servicos.TentarObterStatus(sistema.NomeServico, out var statusServico))
        {
            TratarServicoInexistente(sistema);
            return;
        }

        if (_memoria.Resolver(sistema.Nome, ProblemaServicoInexistente))
        {
            _watcherLogger.Informacao(
                $"[{sistema.Nome}] O serviço '{sistema.NomeServico}' agora existe.");
        }

        if (EstaEmTransicao(statusServico))
        {
            TratarEstadoPendente(sistema, statusServico);
            return;
        }

        _pendenteDesde.TryRemove(sistema.Nome, out _);

        if (_memoria.Resolver(sistema.Nome, ProblemaEstadoPendente))
        {
            _watcherLogger.Informacao(
                $"[{sistema.Nome}] O serviço saiu do estado de transição. Status atual: {statusServico}.");
        }

        if (statusServico == ServiceControllerStatus.Paused)
        {
            _watcherLogger.Aviso($"[{sistema.Nome}] Serviço pausado; iniciando retomada.");
            IniciarRecuperacao(sistema, TipoRecuperacao.Continuar, "Serviço estava pausado", token);
            return;
        }

        if (statusServico != ServiceControllerStatus.Running)
        {
            _watcherLogger.Aviso(
                $"[{sistema.Nome}] Serviço não está em execução. Status atual: {statusServico}.");

            IniciarRecuperacao(
                sistema,
                TipoRecuperacao.Iniciar,
                $"Serviço estava com status {statusServico}",
                token);
            return;
        }

        var statusLog = _logMonitor.Verificar(sistema);

        if (statusLog == StatusLog.EmCarencia)
        {
            _watcherLogger.Informacao(
                $"[{sistema.Nome}] Serviço Running; aguardando carência após recuperação.");
            return;
        }

        if (statusLog == StatusLog.AguardandoLog)
        {
            _watcherLogger.Aviso(
                $"[{sistema.Nome}] Serviço Running, mas o arquivo de log ainda não existe: " +
                $"{sistema.CaminhoLog}");
            return;
        }

        if (statusLog == StatusLog.LogNaoEncontrado)
        {
            AlertarLogAusente(sistema);
            return;
        }

        // A partir daqui, o arquivo de log existe.
        _memoria.Resolver(sistema.Nome, ProblemaLogAusente);

        if (statusLog == StatusLog.Travado)
        {
            _watcherLogger.Aviso(
                $"[{sistema.Nome}] Serviço Running, mas o log parou de atualizar.");

            IniciarRecuperacao(
                sistema,
                TipoRecuperacao.Reiniciar,
                $"Log sem crescimento há mais de {sistema.MinutosSemLogParaTravado} minuto(s)",
                token);
            return;
        }

        // Estava em falha e agora está Running com log ativo: voltou por conta própria
        // (por exemplo, alguém o consertou manualmente). Avisamos uma única vez.
        if (_emFalha.TryRemove(sistema.Nome, out _))
        {
            _memoria.Resolver(sistema.Nome, ProblemaEmFalha);
            _alertador.Alertar(
                NivelAlerta.Aviso,
                $"Serviço {sistema.Nome} normalizado",
                "O serviço estava em falha e voltou a funcionar sem intervenção do watcher " +
                "(Running e log ativo).");
        }

        _watcherLogger.Informacao(
            $"[{sistema.Nome}] Verificação OK: serviço Running e log ativo.");
    }

    /// <summary>
    /// Espera as recuperações em andamento terminarem, até o limite informado.
    /// Devolve false se o tempo esgotou antes de todas terminarem.
    /// </summary>
    public async Task<bool> AguardarRecuperacoesAsync(TimeSpan limite)
    {
        var pendentes = _tarefas.Values.Where(t => !t.IsCompleted).ToArray();

        if (pendentes.Length == 0)
            return true;

        _watcherLogger.Informacao(
            $"Aguardando {pendentes.Length} recuperação(ões) em andamento " +
            $"(até {limite.TotalSeconds:0}s).");

        var todas = Task.WhenAll(pendentes);
        var primeira = await Task.WhenAny(todas, Task.Delay(limite));

        if (primeira == todas)
            return true;

        _watcherLogger.Aviso(
            "Tempo esgotado esperando as recuperações; algum serviço pode ter ficado parado.");

        return false;
    }

    private static bool EstaEmTransicao(ServiceControllerStatus status) =>
        status is ServiceControllerStatus.StartPending
            or ServiceControllerStatus.StopPending
            or ServiceControllerStatus.ContinuePending
            or ServiceControllerStatus.PausePending;

    private void TratarServicoInexistente(SistemaMonitorado sistema)
    {
        if (_memoria.Registrar(sistema.Nome, ProblemaServicoInexistente))
        {
            _alertador.Alertar(
                NivelAlerta.Erro,
                $"Serviço não encontrado: {sistema.Nome}",
                $"O serviço '{sistema.NomeServico}' não existe neste computador. " +
                "Confira 'NomeServico' no config.json (é o nome do serviço, não o nome de exibição; " +
                "veja com: Get-Service | Select-Object Name, DisplayName). " +
                "O watcher continua conferindo a cada ciclo e registrará no log se ele passar a existir.");
        }
        else
        {
            _watcherLogger.Informacao(
                $"[{sistema.Nome}] O serviço '{sistema.NomeServico}' continua sem existir.");
            Lembrar(sistema, ProblemaServicoInexistente,
                $"O serviço '{sistema.NomeServico}' continua sem existir neste computador");
        }
    }

    private void TratarEstadoPendente(SistemaMonitorado sistema, ServiceControllerStatus status)
    {
        var agora = DateTime.UtcNow;
        var desde = _pendenteDesde.GetOrAdd(sistema.Nome, agora);
        var tempo = agora - desde;

        if (tempo < _toleranciaPendente)
        {
            _watcherLogger.Informacao(
                $"[{sistema.Nome}] Serviço em transição ({status}) há {tempo.TotalSeconds:0}s; " +
                "aguardando o Windows concluir.");
            return;
        }

        if (_memoria.Registrar(sistema.Nome, ProblemaEstadoPendente))
        {
            _alertador.Alertar(
                NivelAlerta.Erro,
                $"Serviço {sistema.Nome} preso em {status}",
                $"O serviço está em {status} há mais de {_toleranciaPendente.TotalSeconds:0}s e o Windows " +
                "não concluiu a transição. Enquanto isso o watcher não envia Start/Stop (seriam recusados) " +
                "e NÃO encerrou o processo. Se ele não sair sozinho, finalize o processo do serviço " +
                "manualmente (Gerenciador de Tarefas > Detalhes).");
        }
        else
        {
            _watcherLogger.Informacao(
                $"[{sistema.Nome}] Serviço continua em {status} há {tempo.TotalSeconds:0}s.");
            Lembrar(sistema, ProblemaEstadoPendente, $"O serviço continua preso em {status}.");
        }
    }

    private void AlertarLogAusente(SistemaMonitorado sistema)
    {
        if (!_memoria.Registrar(sistema.Nome, ProblemaLogAusente))
        {
            Lembrar(sistema, ProblemaLogAusente,
                $"Oarquivo de log '{sistema.CaminhoLog}' continua não existindo.");
                return;
        }
            
        _alertador.Alertar(
            NivelAlerta.Erro,
            $"Log não encontrado: {sistema.Nome}",
            $"O serviço está Running, mas o arquivo '{sistema.CaminhoLog}' não existe há mais de " +
            $"{sistema.MinutosSemLogParaTravado} minuto(s). Confira 'CaminhoLog' no config.json. " +
            "O serviço NÃO foi reiniciado.");
    }

    private void IniciarRecuperacao(
        SistemaMonitorado sistema,
        TipoRecuperacao tipo,
        string motivo,
        CancellationToken token)
    {
        // Durante o encerramento, não começamos recuperações novas.
        if (token.IsCancellationRequested)
            return;

        if (!_emRecuperacao.TryAdd(sistema.Nome, 0))
            return;

        var jaEstavaEmFalha = _emFalha.ContainsKey(sistema.Nome);

        if (jaEstavaEmFalha)
        {
            _watcherLogger.Informacao(
                $"[{sistema.Nome}] Serviço ainda em falha; nova rodada de recuperação " +
                "(sem novos alertas).");
        }

        var tarefa = Task.Run(async () =>
        {
            try
            {
                var recuperado = await _recuperador.RecuperarAsync(
                    sistema,
                    tipo,
                    motivo,
                    jaEstavaEmFalha,
                    token);

                if (recuperado)
                {
                    _emFalha.TryRemove(sistema.Nome, out _);
                    _memoria.Resolver(sistema.Nome, ProblemaEmFalha);
                    _logMonitor.IniciarCarencia(sistema);
                }
                else if (_emFalha.TryAdd(sistema.Nome, 0))
                {
                    // Primeira falha: o alerta de erro já foi dado pelo Recuperador.
                    // Aqui só começamos a contar o tempo para os lembretes.
                    _memoria.Registrar(sistema.Nome, ProblemaEmFalha);
                }
                else
                {
                    Lembrar(sistema, ProblemaEmFalha,
                        "O watcher continua tentando recuperar o serviço, sem sucesso.");
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelar não é falhar: sem alerta de erro e sem marcar "em falha".
                _watcherLogger.Aviso(
                    $"[{sistema.Nome}] Recuperação interrompida pelo encerramento do watcher. " +
                    $"Status atual do serviço: {ObterStatusParaLog(sistema)}.");
            }
            catch (Exception ex)
            {
                _watcherLogger.Erro(
                    $"[{sistema.Nome}] Erro inesperado durante a recuperação.", ex);
            }
            finally
            {
                _emRecuperacao.TryRemove(sistema.Nome, out _);
            }
        });

        _tarefas[sistema.Nome] = tarefa;
    }

    private void Lembrar(SistemaMonitorado sistema, string problema, string descricao)
    {
        if (!_memoria.DeveLembrar(sistema.Nome, problema, out var duracao))
            return;

        _alertador.Alertar(
            NivelAlerta.Erro,
            $"Lembrete: {sistema.Nome} continua com problema",
            $"{descricao} O problema dura há {FormatarDuracao(duracao)}.");
    }

    private static string FormatarDuracao(TimeSpan duracao) =>
        duracao.TotalHours >= 1
            ? $"{(int)duracao.TotalHours}h{duracao.Minutes:00}min"
            : $"{Math.Max(1, (int)duracao.TotalMinutes)} minuto(s)";
    private string ObterStatusParaLog(SistemaMonitorado sistema)
    {
        try
        {
            return _servicos.ObterStatus(sistema.NomeServico).ToString();
        }
        catch (Exception ex)
        {
            return $"indisponível ({ex.Message})";
        }
    }
}