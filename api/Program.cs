using System.Net.Http;
using Utility;
using Services;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Builder;

namespace MainSpace
{
    public class Program
    {
        static async Task Main(string[] args) {
            ApiService service = new ApiService();
            await service.Run();
        }
    }
}