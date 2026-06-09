using ProjectServer;

namespace SysProgProj2
{
    internal class Program
    {
        static async Task Main(string[] args) {
            var server = new Server();
            await server.Run();
        }
    }
}