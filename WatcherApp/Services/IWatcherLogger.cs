namespace WatcherApp.Services;

public interface IWatcherLogger
{
    void Informacao(string mensagem);
    void Aviso(string mensagem);
    void Erro(string mensagem, Exception? ex = null);
}