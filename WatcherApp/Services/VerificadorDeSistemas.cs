using WatcherApp.Models;

namespace WatcherApp.Services;

public class VerificadorDeSistema
{
    private readonly ProcessoService _processos;
    private readonly LogMonitor _logMonitor;
    private readonly RecuperadorDeSistema _recuperador;
    private readonly HashSet<string> _emFalha = new();

    public VerificadorDeSistema(
        ProcessoService processos,
        LogMonitor logMonitor,
        RecuperadorDeSistema recuperador)
    {
        _processos = processos;
        _logMonitor = logMonitor;
        _recuperador = recuperador;
    }

    public void Verificar(SistemaMonitorado sistema)
    {
        if (!_processos.EstaRodando(sistema.NomeProcesso))
        {
            Recuperar(sistema, reiniciar: false, motivo: "Processo não estava rodando");
            return;
        }

        var status = _logMonitor.Verificar(sistema);

        if (status == StatusLog.Travado)
        {
            Recuperar(
                sistema,
                reiniciar: true,
                motivo: $"Log sem crescimento há mais de {sistema.MinutosSemLogParaTravado} min");
            return;
        }

        if (status == StatusLog.Ok)
            _emFalha.Remove(sistema.Nome);
    }

    private void Recuperar(SistemaMonitorado sistema, bool reiniciar, string motivo)
    {
        var primeiraFalha = !_emFalha.Contains(sistema.Nome);

        var sucesso = _recuperador.Recuperar(sistema, reiniciar, motivo, registrarFalhas: primeiraFalha);

        if (sucesso)
        {
            _emFalha.Remove(sistema.Nome);
            _logMonitor.IniciarCarencia(sistema);
        }
        else
        {
            _emFalha.Add(sistema.Nome);
        }
    }
}