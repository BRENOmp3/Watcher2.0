namespace WatcherApp.Services;

public interface IWatcherLogger
{
    void Aviso(string mensagem);
    void Erro(string mensagem, Exception? ex = null);
}