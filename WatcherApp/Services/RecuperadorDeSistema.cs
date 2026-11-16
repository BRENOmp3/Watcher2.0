using WatcherApp.Models;

namespace WatcherApp.Services;

public class RecuperadorDeSistema
{
    private const int SegundosVerificacaoAposAcao = 5;

    private readonly ServicoWindowsService _servicos;
    private readonly IAlertador _alertador;
    private readonly IWatcherLogger _logger;
    private readonly int _maximoTentativas;
    private readonly TimeSpan _intervaloEntreTentativas;

    public RecuperadorDeSistema(
        ServicoWindowsService servicos,
        IAlertador alertador,
        IWatcherLogger logger,
        WatcherConfig config)
    {
        _servicos = servicos;
        _alertador = alertador;
        _logger = logger;
        _maximoTentativas = config.MaximoTentativas;

        _intervaloEntreTentativas =
            TimeSpan.FromSeconds(config.SegundosEntreTentativas);
    }

    public async Task<bool> RecuperarAsync(
        SistemaMonitorado sistema,
        TipoRecuperacao tipo,
        string motivo,
        bool jaEstavaEmFalha,
        CancellationToken token)
    {
        Exception? ultimoErro = null;

        for (var tentativa = 1; tentativa <= _maximoTentativas; tentativa++)
        {
            // Ponto de checagem: não começamos uma tentativa nova durante o encerramento.
            token.ThrowIfCancellationRequested();

            try
            {
                // Esta ação NÃO é cancelável de propósito: um Stop seguido de Start
                // precisa terminar, senão o serviço ficaria parado por nossa causa.
                ExecutarAcao(sistema.NomeServico, tipo);

                await Task.Delay(
                    TimeSpan.FromSeconds(SegundosVerificacaoAposAcao),
                    token);

                if (_servicos.EstaRodando(sistema.NomeServico))
                {
                    var complemento = jaEstavaEmFalha
                        ? " O serviço estava em falha e voltou ao normal."
                        : string.Empty;

                    _alertador.Alertar(
                        NivelAlerta.Aviso,
                        $"Serviço {sistema.Nome} recuperado",
                        $"{motivo}. Ação: {tipo}. " +
                        $"Sucesso na tentativa {tentativa}/{_maximoTentativas}.{complemento}");

                    return true;
                }

                ultimoErro = new InvalidOperationException(
                    "A ação foi executada, mas o serviço não ficou em execução.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // O filtro deixa o cancelamento passar: ele não é uma falha de tentativa.
                ultimoErro = ex;
            }

            // Se o serviço já estava em falha, as tentativas ficam em silêncio.
            if (!jaEstavaEmFalha)
            {
                _logger.Aviso(
                    $"[{sistema.Nome}] Tentativa {tentativa}/{_maximoTentativas} " +
                    $"de {tipo} falhou: {ultimoErro?.Message}");
            }

            if (tentativa < _maximoTentativas)
                await Task.Delay(_intervaloEntreTentativas, token);
        }

        if (jaEstavaEmFalha)
        {
            _logger.Informacao(
                $"[{sistema.Nome}] Nova rodada de recuperação falhou; " +
                $"o serviço continua em falha. Último erro: {ultimoErro?.Message}");
        }
        else
        {
            _alertador.Alertar(
                NivelAlerta.Erro,
                $"Falha ao recuperar o serviço {sistema.Nome}",
                $"{motivo}. Todas as {_maximoTentativas} tentativas falharam. " +
                $"Último erro: {ultimoErro?.Message}. " +
                "O watcher continuará tentando em silêncio e avisará quando o serviço voltar ao normal.");
        }

        return false;
    }

    private void ExecutarAcao(
        string nomeServico,
        TipoRecuperacao tipo)
    {
        switch (tipo)
        {
            case TipoRecuperacao.Iniciar:
                _servicos.Iniciar(nomeServico);
                break;

            case TipoRecuperacao.Continuar:
                _servicos.Continuar(nomeServico);
                break;

            case TipoRecuperacao.Reiniciar:
                _servicos.Reiniciar(nomeServico);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(tipo));
        }
    }
}