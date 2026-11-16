using System.Text;
using WatcherApp.Models;

namespace WatcherApp.Services;

public class FileLogger : IWatcherLogger
{
    private readonly object _trava = new();
    private readonly IdentidadeDoServidor _servidor;


    public string PastaLogs { get; }

    public FileLogger(WatcherConfig config, IdentidadeDoServidor servidor)
    {
        PastaLogs = Path.IsPathRooted(config.PastaLogs)
            ? config.PastaLogs
            : Path.Combine(AppContext.BaseDirectory, config.PastaLogs);

        Directory.CreateDirectory(PastaLogs);
        _servidor = servidor;
    }

    public void Aviso(string mensagem) => Escrever("AVISO", mensagem, ConsoleColor.Yellow);

    public void Informacao(string mensagem) =>
        Escrever("INFO", mensagem, ConsoleColor.Green);

    public void Erro(string mensagem, Exception? ex = null)
    {
        var texto = ex is null
            ? mensagem
            : $"{mensagem} | {ex.GetType().Name}: {ex.Message}";

        Escrever("ERRO", texto, ConsoleColor.Red);
    }

    private void Escrever(string nivel, string mensagem, ConsoleColor cor)
    {
        var agora = DateTime.Now;

        var texto = $"""
        ----------------------------------------------------------------
        Data/Hora : {agora:dd/MM/yyyy HH:mm:ss}
        Servidor  : {_servidor.Descricao}
        Nível     : {nivel}
        Mensagem  : {mensagem}
        ----------------------------------------------------------------

        """;

        lock (_trava)
        {
            var corOriginal = Console.ForegroundColor;

            try
            {
                Console.ForegroundColor = cor;
                Console.Write(texto);
            }
            finally
            {
                Console.ForegroundColor = corOriginal;
            }

            try
            {
                var arquivo = Path.Combine(
                    PastaLogs,
                    $"watcher-{agora:yyyy-MM-dd}.txt");

                File.AppendAllText(arquivo, texto, Encoding.UTF8);
            }
            catch (Exception falha)
            {
                Console.WriteLine(
                    $"Falha ao gravar no arquivo de log: {falha.Message}");
            }
        }
    }
}