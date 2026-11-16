namespace WatcherApp.Models;

public class EmailConfig
{
    public bool Habilitado { get; set; }
    public string ServidorSmtp { get; set; } = string.Empty;
    public int PortaSmtp { get; set; } = 587;
    public string Usuario { get; set; } = string.Empty;
    public string Remetente { get; set; } = string.Empty;
    public List<string> Destinatarios { get; set; } = new();
    public string VariavelAmbienteSenha { get; set; } = "WATCHER_SMTP_SENHA"; // setx WATCHER_SMTP_SENHA "SUA_SENHA" <= no terminal do Windows para definir a variável de ambiente
    public bool UsarSsl { get; set; } = true;
    public bool UsarCredenciais { get; set; } = true;
}