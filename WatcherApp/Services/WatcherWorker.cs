using Microsoft.Extensions.Hosting;
using WatcherApp.Models;

namespace WatcherApp.Services
{
    public class WatcherWorker : BackgroundService
    {
        private readonly WatcherConfig _config;
        private readonly VerificadorDeSistema _verificador;
        private readonly IAlertador _alertador;
        private readonly IdentidadeDoServidor _servidor;
        private readonly IWatcherLogger _logger;
        private static readonly TimeSpan TempoMaximoParaEncerrar = TimeSpan.FromSeconds(90);
        public WatcherWorker(
            WatcherConfig config, 
            VerificadorDeSistema verificador,
            IAlertador alertador,
            IdentidadeDoServidor servidor,
            IWatcherLogger logger)
        {
            _config = config;
            _verificador = verificador;
            _alertador = alertador;
            _servidor = servidor;
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //DEVOLVE O CONTROLE AO HOST JÁ: o primeiro ciclo não pode atrasar o "serviço iniciado".
            await Task.Yield();

            _logger.Informacao($"Watcher inciado em {_servidor.Descricao}.");
            ConferirCaminhoDeLog();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    ExecutarCiclo(stoppingToken);

                    try
                    {
                        await Task.Delay(
                            TimeSpan.FromSeconds(_config.IntervaloCicloSegundos),
                            stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Pedido de parada durante durante a espera entre os ciclos: o laço termina.
                    }
                }
            }
            catch (Exception ex)
            {
                _alertador.Alertar(
                    NivelAlerta.Erro, 
                    "Erro fatal no watcher", 
                    $"{ex.GetType().Name}: {ex.Message}");

                throw; // o Host encerra o programa, e o Windows pode reiniciar o serviço
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Informacao("Encerramento solicitado. Aguardando recuperações em andamento.");

            //Cancela o stoppingToken para que o ExecuteAsync saia do laço e aguarde as recuperações.
            await base.StopAsync(cancellationToken);

            await _verificador.AguardarRecuperacoesAsync(TempoMaximoParaEncerrar);

            _logger.Informacao("Watcher encerrado.");
        }

        private void ConferirCaminhoDeLog()
        {
            foreach (var sistema in _config.Sistemas)
            {
                if (LogMonitor.LocalizarArquivo(sistema.CaminhoLog) is null)
                {
                    _logger.Aviso(
                        $"[{sistema.Nome}] Arquivo de log não enconstrado na largada: " +
                        $"\"{sistema.CaminhoLog}\". Normal se o serviço ainda não o criou; " +
                        "se não, confira o 'CaminhoLog'.");
                }
            }
        }
        private void ExecutarCiclo(CancellationToken token)
        {
            foreach (var sistema in _config.Sistemas)
            {
                if (token.IsCancellationRequested)
                    break;

                try
                {
                    _verificador.Verificar(sistema, token);
                }
                catch (Exception ex)
                {
                    _alertador.Alertar(
                        NivelAlerta.Erro,
                        $"Erro inesperado ao verificar {sistema.Nome}",
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }
}
