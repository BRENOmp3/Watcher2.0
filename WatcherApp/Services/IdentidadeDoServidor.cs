using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WatcherApp.Services;

public class IdentidadeDoServidor
{
    private const string IpDesconhecido = "IP desconhecido";

    private readonly object _trava = new();
    private string _ip;

    public string Nome { get; }

    // Enquanto o IP for desconhecido (rede ainda subindo), tenta de novo a cada consulta.
    public string Ip
    {
        get
        {
            lock (_trava)
            {
                if (_ip == IpDesconhecido)
                    _ip = DescobrirIp();

                return _ip;
            }
        }
    }

    public string Descricao => $"{Nome} ({Ip})";

    public IdentidadeDoServidor()
    {
        Nome = Environment.MachineName;
        _ip = DescobrirIp();
    }

    private static string DescobrirIp()
    {
        try
        {
            var ip = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => n.GetIPProperties())
                .OrderByDescending(p => p.GatewayAddresses.Count > 0)
                .SelectMany(p => p.UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                                  && !IPAddress.IsLoopback(a));

            return ip?.ToString() ?? IpDesconhecido;
        }
        catch
        {
            return IpDesconhecido;
        }
    }
}