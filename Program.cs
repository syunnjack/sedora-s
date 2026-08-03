Exit code: 0
Wall time: 0.8 seconds
Output:
namespace BarcodeWorkInfoComplete;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

