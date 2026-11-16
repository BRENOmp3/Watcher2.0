using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using WatcherApp.Models;

namespace WatcherApp.Services;

public class AlertadorEmail : IAlertador, IAsyncDisposable
{
    private const int TentativasDeEnvio = 3;
    private static readonly TimeSpan EsperaEntreEnvios = TimeSpan.FromSeconds(20);
    private sealed record Mensagem(NivelAlerta Nivel, string Assunto, string Texto, DateTime Quando);

    private readonly EmailConfig _config;
    private readonly string? _senha;
    private readonly IdentidadeDoServidor _servidor;
    private readonly IWatcherLogger _logger;
    private readonly Channel<Mensagem> _fila = Channel.CreateUnbounded<Mensagem>();
    private readonly Task _consumidor;

    public AlertadorEmail(
        EmailConfig config,
        string? senha,
        IdentidadeDoServidor servidor,
        IWatcherLogger logger)
    {
        _config = config;
        _senha = senha;
        _servidor = servidor;
        _logger = logger;
        _consumidor = Task.Run(ProcessarFilaAsync);
    }

    public async ValueTask DisposeAsync() =>
        await EncerrarAsync(TimeSpan.FromSeconds(15));

    // Só enfileira e volta na hora: nunca trava o ciclo de verificação.
    public void Alertar(NivelAlerta nivel, string assunto, string mensagem)
    {
        _fila.Writer.TryWrite(new Mensagem(nivel, assunto, mensagem, DateTime.Now));
    }

    // Fecha a fila e espera enviar o que sobrou, até o limite.
    public async Task EncerrarAsync(TimeSpan limite)
    {
        _fila.Writer.TryComplete();

        var primeira = await Task.WhenAny(_consumidor, Task.Delay(limite));

        if (primeira != _consumidor)
            _logger.Aviso("Tempo esgotado esperando o envio dos emails pendentes; alguns podem não ter saído.");
    }

    private async Task ProcessarFilaAsync()
    {
        await foreach (var mensagem in _fila.Reader.ReadAllAsync())
        {
            for (var tentativa = 1; tentativa <= TentativasDeEnvio; tentativa++)
            {
                try
                {
                    await EnviarAsync(mensagem);
                    break; // enviado: passa para a próxima mensagem
                }
                catch (Exception ex)
                {
                    var texto =
                        $"Falha ao enviar o email do alerta '{mensagem.Assunto}' " +
                        $"(tentativa {tentativa}/{TentativasDeEnvio}): {ex.GetType().Name}: {ex.Message}";

                    if (tentativa == TentativasDeEnvio)
                    {
                        _logger.Erro(texto);
                    }
                    else
                    {
                        _logger.Aviso(texto);
                        await Task.Delay(EsperaEntreEnvios);
                    }
                }
            }
        }
    }
    private async Task EnviarAsync(Mensagem m)
    {
        var nivel = m.Nivel == NivelAlerta.Erro ? "ERRO" : "AVISO";

        using var email = new MailMessage
        {
            From = new MailAddress(_config.Remetente),
            Subject = $"[Watcher][{nivel}][{_servidor.Nome}] {m.Assunto}",
            Body =
                $"{m.Assunto}{Environment.NewLine}{Environment.NewLine}" +
                $"{m.Texto}{Environment.NewLine}{Environment.NewLine}" +
                $"Data/Hora: {m.Quando:dd/MM/yyyy HH:mm:ss}{Environment.NewLine}" +
                $"Servidor: {_servidor.Descricao}"
        };

        foreach (var destinatario in _config.Destinatarios)
            email.To.Add(destinatario);

        using var smtp = new SmtpClient(_config.ServidorSmtp, _config.PortaSmtp)
        {
            EnableSsl = _config.UsarSsl,
            Timeout = 15000
        };

        if (_config.UsarCredenciais)
            smtp.Credentials = new NetworkCredential(_config.Usuario, _senha);
        await smtp.SendMailAsync(email);
    }
}