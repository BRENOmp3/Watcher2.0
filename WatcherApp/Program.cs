using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WatcherApp.Models;
using WatcherApp.Services;

namespace WatcherApp;

internal class Program
{
    private static async Task Main(string[] args)
    {
        WatcherConfig config;

        try
        {
            config = ConfigLoader.Carregar();
        }
        catch (Exception ex)
        {
            RegistrarErroDeInicializacao(ex);
            return;
        }

        var builder = Host.CreateApplicationBuilder(args);

        // Usa o próprio logger (FileLogger); desligamos os do Host para não duplicar.
        builder.Logging.ClearProviders();

        // Faz o programa se comportar como serviço do Windows quando iniciado pelo Windows.
        // Rodando pelo Visual Studio ou pelo terminal, continua sendo um console normal.
        builder.Services.AddWindowsService(opcoes => opcoes.ServiceName = "WatcherApp");

        // O padrão (30s) cortaria uma recuperação em andamento.
        builder.Services.Configure<HostOptions>(opcoes =>
            opcoes.ShutdownTimeout = TimeSpan.FromSeconds(120));

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<IdentidadeDoServidor>();
        builder.Services.AddSingleton<IWatcherLogger, FileLogger>();
        builder.Services.AddSingleton<AlertadorLog>();

        // Email só entra se estiver habilitado e, quando usa login, se a senha existir.
        string? avisoDeInicializacao = null;
        string? senha = null;
        var emailAtivo = config.Email.Habilitado;

        if (emailAtivo && config.Email.UsarCredenciais)
        {
            senha = Environment.GetEnvironmentVariable(config.Email.VariavelAmbienteSenha);

            if (string.IsNullOrEmpty(senha))
            {
                emailAtivo = false;
                avisoDeInicializacao =
                    $"Email habilitado, mas a variável de ambiente '{config.Email.VariavelAmbienteSenha}' " +
                    "não está definida. Os alertas ficarão só no log.";
            }
        }

        if (emailAtivo)
        {
            builder.Services.AddSingleton(sp => new AlertadorEmail(
                config.Email,
                senha,
                sp.GetRequiredService<IdentidadeDoServidor>(),
                sp.GetRequiredService<IWatcherLogger>()));
        }

        // IAlertador: log sozinho, ou log + email quando o AlertadorEmail existe.
        builder.Services.AddSingleton<IAlertador>(sp =>
        {
            var log = sp.GetRequiredService<AlertadorLog>();
            var email = sp.GetService<AlertadorEmail>(); // null se não foi registrado

            return email is null
                ? log
                : new AlertadorComposto(sp.GetRequiredService<IWatcherLogger>(), log, email);
        });

        builder.Services.AddSingleton<ServicoWindowsService>();
        builder.Services.AddSingleton<LogMonitor>();
        builder.Services.AddSingleton<RecuperadorDeSistema>();
        builder.Services.AddSingleton<VerificadorDeSistema>();

        builder.Services.AddHostedService<WatcherWorker>();

        var host = builder.Build();

        if (avisoDeInicializacao is not null)
            host.Services.GetRequiredService<IWatcherLogger>().Aviso(avisoDeInicializacao);

        await host.RunAsync();
    }

    // Como serviço não existe console, então guardamos o motivo num arquivo ao lado do .exe.
    private static void RegistrarErroDeInicializacao(Exception ex)
    {
        var mensagem = $"Erro ao carregar a configuração: {ex.Message}";

        Console.WriteLine(mensagem);

        try
        {
            var arquivo = Path.Combine(AppContext.BaseDirectory, "watcher-erro-inicializacao.txt");
            File.WriteAllText(
                arquivo,
                $"{DateTime.Now:dd/MM/yyyy HH:mm:ss}{Environment.NewLine}{mensagem}{Environment.NewLine}");
        }
        catch
        {
            // Sem onde gravar: o console já mostrou.
        }

        Environment.ExitCode = 1;
    }
}