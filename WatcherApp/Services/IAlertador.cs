using WatcherApp.Models;

namespace WatcherApp.Services;

public interface IAlertador
{
    void Alertar(NivelAlerta nivel, string assunto, string mensagem);
}
