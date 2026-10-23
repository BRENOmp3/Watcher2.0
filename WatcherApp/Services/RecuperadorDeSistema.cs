using WatcherApp.Models;

namespace WatcherApp.Services;

public class RecuperadorDeSistema
{
    private const int SegundosVerificacaoAposStart = 5;

    private readonly ProcessoService _processos;
    private readonly IAlertador _alertador;
    private readonly IWatcherLogger _logger;
    private readonly int _maximoTentativas;
    private readonly TimeSpan _intervaloEntreTentativas;

    public RecuperadorDeSistema(
        ProcessoService processos,
        IAlertador alertador,
        IWatcherLogger logger,
        WatcherConfig config)
    {
        _processos = processos;
        _alertador = alertador;
        _logger = logger;
        _maximoTentativas = config.MaximoTentativas;
        _intervaloEntreTentativas = TimeSpan.FromSeconds(config.SegundosEntreTentativas);
    }

    public bool Recuperar(
        SistemaMonitorado sistema,
        bool reiniciar,
        string motivo,
        bool registrarFalhas = true)
    {
        var acao = reiniciar ? "reiniciar" : "iniciar";
        var acaoPassado = reiniciar ? "reiniciado" : "iniciado";
        Exception? ultimoErro = null;

        for (var tentativa = 1; tentativa <= _maximoTentativas; tentativa++)
        {
            try
            {
                if (reiniciar)
                    _processos.Encerrar(sistema.NomeProcesso);

                _processos.Iniciar(sistema.CaminhoExecutavel);

                Thread.Sleep(TimeSpan.FromSeconds(SegundosVerificacaoAposStart));

                if (_processos.EstaRodando(sistema.NomeProcesso))
                {
                    _alertador.Alertar(
                        NivelAlerta.Aviso,
                        $"Sistema {sistema.Nome} {acaoPassado}",
                        $"{motivo}. Sistema {acaoPassado} com sucesso na tentativa {tentativa}/{_maximoTentativas}.");

                    return true;
                }

                ultimoErro = new InvalidOperationException(
                    "O processo foi iniciado, mas não está rodando após a verificação.");
            }
            catch (Exception ex)
            {
                ultimoErro = ex;
            }

            if (registrarFalhas)
            {
                _logger.Aviso(
                    $"[{sistema.Nome}] Tentativa {tentativa}/{_maximoTentativas} de {acao} falhou: {ultimoErro?.Message}");
            }

            if (tentativa < _maximoTentativas)
                Thread.Sleep(_intervaloEntreTentativas);
        }

        if (registrarFalhas)
        {
            _alertador.Alertar(
                NivelAlerta.Erro,
                $"Falha ao {acao} o sistema {sistema.Nome}",
                $"{motivo}. Todas as {_maximoTentativas} tentativas falharam. Último erro: {ultimoErro?.Message}");
        }

        return false;
    }
}