using System.ServiceProcess;
using System.ComponentModel;

namespace WatcherApp.Services;

public class ServicoWindowsService
{
    private const int ErroServicoInexistente = 1060;

    public bool TentarObterStatus(string nomeServico, out ServiceControllerStatus status)
    {
        try
        {
            using var servico = new ServiceController(nomeServico);
            status = servico.Status;
            return true;
        }
        catch (InvalidOperationException ex)
            when (ex.InnerException is Win32Exception { NativeErrorCode: ErroServicoInexistente })
        {
            status = default;
            return false;
        }
    }

    private static readonly TimeSpan TempoLimite =
        TimeSpan.FromSeconds(30);

    public ServiceControllerStatus ObterStatus(string nomeServico)
    {
        using var servico = new ServiceController(nomeServico);
        return servico.Status;
    }

    public bool EstaRodando(string nomeServico)
    {
        return ObterStatus(nomeServico) == ServiceControllerStatus.Running;
    }

    public void Iniciar(string nomeServico)
    {
        using var servico = new ServiceController(nomeServico);

        if (servico.Status == ServiceControllerStatus.Running)
            return;

        servico.Start();
        servico.WaitForStatus(ServiceControllerStatus.Running, TempoLimite);
    }

    public void Continuar(string nomeServico)
    {
        using var servico = new ServiceController(nomeServico);

        if (servico.Status == ServiceControllerStatus.Running)
            return;

        if (servico.Status != ServiceControllerStatus.Paused)
        {
            throw new InvalidOperationException(
                $"O serviço '{nomeServico}' não está pausado. " +
                $"Status atual: {servico.Status}.");
        }

        servico.Continue();
        servico.WaitForStatus(ServiceControllerStatus.Running, TempoLimite);
    }

    public void Reiniciar(string nomeServico)
    {
        using var servico = new ServiceController(nomeServico);

        if (servico.Status != ServiceControllerStatus.Stopped)
        {
            servico.Stop();
            servico.WaitForStatus(
                ServiceControllerStatus.Stopped,
                TempoLimite);
        }

        servico.Start();
        servico.WaitForStatus(
            ServiceControllerStatus.Running,
            TempoLimite);
    }
}