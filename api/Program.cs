using System.Net.Http;
using Utility;
using Services;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Builder;

namespace MainSpace
{
    public class Program
    {
        static void Main(string[] args) {
            ApiService service = new ApiService();
            service.Start();
        }
    }
}