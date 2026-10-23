using WatcherApp.Models;

namespace WatcherApp.Services;

public class AlertadorLog : IAlertador
{
    private readonly IWatcherLogger _logger;

    public AlertadorLog(IWatcherLogger logger)
    {
        _logger = logger;
    }

    public void Alertar(NivelAlerta nivel, string assunto, string mensagem)
    {
        var texto = $"{assunto} | {mensagem}";

        if (nivel == NivelAlerta.Erro)
            _logger.Erro(texto);
        else
            _logger.Aviso(texto);
    }
}