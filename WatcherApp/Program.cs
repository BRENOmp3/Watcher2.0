using WatcherApp.Models;
using WatcherApp.Services;

namespace WatcherApp;

internal class Program
{
    private static void Main(string[] args)
    {
        WatcherConfig config;

        try
        {
            config = ConfigLoader.Carregar();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao carregar a configuração: {ex.Message}");
            return;
        }

        var logger = new FileLogger(config);
        var alertador = new AlertadorLog(logger);

        try
        {
            var processos = new ProcessoService();
            var logMonitor = new LogMonitor();
            var recuperador = new RecuperadorDeSistema(processos, alertador, logger, config);
            var verificador = new VerificadorDeSistema(processos, logMonitor, recuperador);

            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Encerrando o watcher...");
            };

            Console.WriteLine("Watcher em execução (só anomalias são exibidas e registradas). Ctrl+C para sair.");

            while (!cts.IsCancellationRequested)
            {
                ExecutarCiclo(config, verificador, alertador, cts.Token);
                cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(config.IntervaloCicloSegundos));
            }
        }
        catch (Exception ex)
        {
            alertador.Alertar(NivelAlerta.Erro, "Erro fatal no watcher", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ExecutarCiclo(
        WatcherConfig config,
        VerificadorDeSistema verificador,
        IAlertador alertador,
        CancellationToken token)
    {
        foreach (var sistema in config.Sistemas)
        {
            if (token.IsCancellationRequested)
                break;

            try
            {
                verificador.Verificar(sistema);
            }
            catch (Exception ex)
            {
                alertador.Alertar(
                    NivelAlerta.Erro,
                    $"Erro inesperado ao verificar {sistema.Nome}",
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}