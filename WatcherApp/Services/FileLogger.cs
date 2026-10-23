using System.Text;
using WatcherApp.Models;

namespace WatcherApp.Services;

public class FileLogger : IWatcherLogger
{
    private readonly object _trava = new();

    public string PastaLogs { get; }

    public FileLogger(WatcherConfig config)
    {
        PastaLogs = Path.IsPathRooted(config.PastaLogs)
            ? config.PastaLogs
            : Path.Combine(AppContext.BaseDirectory, config.PastaLogs);

        Directory.CreateDirectory(PastaLogs);       
    }

    public void Aviso(string mensagem) => Escrever("AVISO", mensagem, ConsoleColor.Yellow);

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
        var linha = $"{agora:dd/MM/yyyy HH:mm:ss} | {nivel,-5} | {mensagem}";

        lock (_trava)
        {
            var corOriginal = Console.ForegroundColor;
            Console.ForegroundColor = cor;
            Console.WriteLine(linha);
            Console.ForegroundColor = corOriginal;

            try
            {
                var arquivo = Path.Combine(PastaLogs, $"watcher-{agora:yyyy-MM-dd}.txt");
                File.AppendAllText(arquivo, linha + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception falha)
            {
                Console.WriteLine($"Falha ao gravar no arquivo de log: {falha.Message}");
            }
        }
    }
}