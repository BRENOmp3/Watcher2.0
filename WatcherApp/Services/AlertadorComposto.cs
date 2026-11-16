using WatcherApp.Models;

namespace WatcherApp.Services;

public class AlertadorComposto : IAlertador
{
    private readonly IWatcherLogger _logger;
    private readonly IAlertador[] _alertadores;

    public AlertadorComposto(IWatcherLogger logger, params IAlertador[] alertadores)
    {
        _logger = logger;
        _alertadores = alertadores;
    }

    public void Alertar(NivelAlerta nivel, string assunto, string mensagem)
    {
        foreach (var alertador in _alertadores)
        {
            try
            {
                alertador.Alertar(nivel, assunto, mensagem);
            }
            catch (Exception ex)
            {
                _logger.Erro($"Falha no alertador {alertador.GetType().Name}: {ex.Message}");
            }
        }
    }
}