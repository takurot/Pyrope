using Garnet;
using Garnet.server;

namespace Pyrope.GarnetServer
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                using var server = new Garnet.GarnetServer(args);
                

                
                // Register Custom Commands
                server.Register.NewCommand("VEC.ADD", Garnet.server.CommandType.ReadModifyWrite, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Add), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_ADD, Name = "VEC.ADD" });
                server.Register.NewCommand("VEC.UPSERT", Garnet.server.CommandType.ReadModifyWrite, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Upsert), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_UPSERT, Name = "VEC.UPSERT" });
                server.Register.NewCommand("VEC.DEL", Garnet.server.CommandType.ReadModifyWrite, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Del), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_DEL, Name = "VEC.DEL" });
                
                // VEC.SEARCH is typically Read only but might need custom object or transaction. 
                // For simplified VEC.SEARCH mapped to CustomRawStringFunctions.Reader:
                server.Register.NewCommand("VEC.SEARCH", Garnet.server.CommandType.Read, new Pyrope.GarnetServer.Extensions.VectorCommandSet(Pyrope.GarnetServer.Extensions.VectorCommandType.Search), new Garnet.server.RespCommandsInfo { Command = (Garnet.server.RespCommand)Pyrope.GarnetServer.Extensions.VectorCommandSet.VEC_SEARCH, Name = "VEC.SEARCH" });
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
