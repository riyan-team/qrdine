using System.Text;

namespace QrDine.PrintBridge;

/// <summary>
/// Turns a print job into raw ESC/POS bytes and sends them to an 80mm thermal
/// printer. THIS IS THE ONLY HARDWARE-DEPENDENT PART. It's written but not called
/// by default (the bridge prints to console/file so the pipeline is testable).
/// To go live: set the printer path/port for your printer and call EscPos.Send(...)
/// from Program.cs. Most USB/LAN thermal printers expose a shared name or a COM/LPT
/// port; network printers take raw bytes on TCP 9100.
/// </summary>
public static class EscPos
{
    // ESC/POS control codes
    static readonly byte[] Init = { 0x1B, 0x40 };              // ESC @ — reset
    static readonly byte[] CenterOn = { 0x1B, 0x61, 0x01 };
    static readonly byte[] LeftOn = { 0x1B, 0x61, 0x00 };
    static readonly byte[] BoldOn = { 0x1B, 0x45, 0x01 };
    static readonly byte[] BoldOff = { 0x1B, 0x45, 0x00 };
    static readonly byte[] DoubleOn = { 0x1D, 0x21, 0x11 };     // double height+width
    static readonly byte[] Normal = { 0x1D, 0x21, 0x00 };
    static readonly byte[] Feed3 = { 0x1B, 0x64, 0x03 };       // feed 3 lines
    static readonly byte[] Cut = { 0x1D, 0x56, 0x00 };        // full cut

    public static byte[] Build(string title, string[] lines, string? qr, string? footer)
    {
        using var ms = new MemoryStream();
        void W(byte[] b) => ms.Write(b);
        void T(string s) => ms.Write(Encoding.UTF8.GetBytes(s + "\n"));

        W(Init);
        W(CenterOn); W(DoubleOn); W(BoldOn); T(title); W(BoldOff); W(Normal);
        W(LeftOn);
        foreach (var line in lines) T(line);
        if (!string.IsNullOrWhiteSpace(qr)) { W(CenterOn); W(QrCode(qr!)); W(LeftOn); }
        if (!string.IsNullOrWhiteSpace(footer)) { W(CenterOn); T(footer!); W(LeftOn); }
        W(Feed3); W(Cut);
        return ms.ToArray();
    }

    // Builds the ESC/POS "print QR" command sequence (model 2).
    static byte[] QrCode(string data)
    {
        var bytes = Encoding.ASCII.GetBytes(data);
        using var ms = new MemoryStream();
        void Store(int fn, byte[] extra) // GS ( k
        {
            int len = extra.Length + 3;
            ms.Write(new byte[] { 0x1D, 0x28, 0x6B, (byte)(len % 256), (byte)(len / 256), 0x31, (byte)fn });
            ms.Write(extra);
        }
        Store(0x41, new byte[] { 0x32, 0x00 });               // model 2
        Store(0x43, new byte[] { 0x06 });                     // module size 6
        Store(0x45, new byte[] { 0x31 });                     // error correction M
        // store data
        int dlen = bytes.Length + 3;
        ms.Write(new byte[] { 0x1D, 0x28, 0x6B, (byte)(dlen % 256), (byte)(dlen / 256), 0x31, 0x50, 0x30 });
        ms.Write(bytes);
        Store(0x51, new byte[] { 0x30 });                     // print
        return ms.ToArray();
    }

    /// <summary>
    /// Sends the bytes to the printer. Pick ONE transport for your hardware:
    ///   • Network (most common): raw TCP to port 9100 — new TcpClient(ip, 9100).
    ///   • Windows shared printer: write to \\.\ share or use the RawPrinterHelper P/Invoke.
    ///   • Serial: new SerialPort("COM3", 9600).
    /// Left unimplemented on purpose — needs the real device.
    /// </summary>
    public static void Send(string type, string title, string[] lines, string? qr, string? footer)
    {
        var payload = Build(title, lines, qr, footer);
        // Example (network printer):
        //   using var client = new System.Net.Sockets.TcpClient("192.168.1.50", 9100);
        //   client.GetStream().Write(payload, 0, payload.Length);
        _ = payload;
        throw new NotImplementedException("Configure your printer transport in EscPos.Send().");
    }
}
