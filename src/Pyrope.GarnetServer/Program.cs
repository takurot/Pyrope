using Garnet;

namespace Pyrope.GarnetServer
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                using var server = new Garnet.GarnetServer(args);
                server.Start();
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to start Garnet server: {ex.Message}");
            }
        }
    }
}
