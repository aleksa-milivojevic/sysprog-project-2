using Newtonsoft.Json.Linq;
using System.Text;
using System.Net;
using System.Threading;
using System.Runtime.InteropServices;
using Utility;
using Memory; 

namespace Service
{
    class ApiService {

        private const int _task_limit = 10;

        private readonly CancellationTokenSource _cts;
        
        private readonly HttpListener _listener;

        private readonly string _hostUrl = "http://localhost:5050/";

        private List<HttpListenerContext> _contexts;

        private SemaphoreSlim _reqSem;

        private readonly object _reqLock;

        private List<Task> _tasks;

        private readonly Logger _logger;
        
        public Server() {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add(_hostUrl);
            _contexts = new List<HttpListenerContext>();
            _reqSem = new SemaphoreSlim(0);
            _reqLock = new object();
            _tasks = new List<Task>(_task_limit);
            _logger = new Logger();
        }

        public async Task Run() {
            _logger.Log($"[Server] [{DateTime.Now}] Starting up!");

            CancellationToken token = _cts.Token;

            for(int i = 0; i < _task_limit; i++) {
                _tasks.Add(Task.FromCanceled(new CancellationToken(true)));
            }

            var listentingTask = Task.Run(async () => {
                _logger.Log($"[Listener] [{DateTime.Now}] Starting on Task {Task.CurrentId}");
                _listener.Start();
                _logger.Log($"[Listener] [{DateTime.Now}] Listening on {_hostUrl}");
                while (true) {
                    HttpListenerContext context = await _listener.GetContextAsync();

                    Monitor.Enter(_reqLock);
                    _contexts.Add(context);
                    Monitor.Exit(_reqLock);
                    
                    _reqSem.Release();

                    _logger.Log($"[Listener] [${DateTime.Now}] Caught a request!");
                }
            }, token);

            var shutdownTask = Task.Run(() => {
                _logger.Log($"[Shutdown] [{DateTime.Now}] Waiting for shutdown request on Task {Task.CurrentId}");
                GracefulShutdown();
            });

            var taskDispatcher = Task.Run(async () => {
                _logger.Log($"[Dispatcher] [{DateTime.Now}] Starting on Task {Task.CurrentId}");
                while(true) {
                    _reqSem.Wait();
                    
                    await Task.WhenAny(_tasks);

                    for(int i = 0; i < _task_limit; i++) {
                        if (_tasks[i].IsCompleted) {
                            Monitor.Enter(_reqLock);
                            var context = _contexts[0];
                            _contexts.RemoveAt(0);
                            Monitor.Exit(_reqLock);

                            _tasks[i] = ContextHandle(context);
                            break;
                        }
                    }
                }
            }, token);

            await shutdownTask;

        }

        private async Task ContextHandle(object? context) {
            if (context == null) {
                return;
            }
            HttpListenerContext c = (HttpListenerContext)context;
            HttpListenerRequest request = c.Request;
            HttpListenerResponse response = c.Response;
            var requestUrl = request.Url.OriginalString;
            int offset = _hostUrl.Length;
            var file = requestUrl.Substring(offset);

            if (file == null) {
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes("File not specified");
                System.IO.Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
                output.Close();
                return;
            }
            
            _logger.Log($"[Task ${Task.CurrentId}] [{DateTime.Now}] Started for file: '{file}'");
            JObject cacheHit;

            if (_cache.Get(file, out cacheHit)) {
                _logger.Log($"[Task {Task.CurrentId}] [{DateTime.Now}] CACHE HIT  -> '{file}' (taken from cache)");
                Respond(file, cacheHit, response);
                return;
            }

            Monitor.Enter(_cacheLock);

            if (_cache.Get(file, out cacheHit)) {
                _logger.Log($"[Thread] [{DateTime.Now}] CACHE HIT  -> '{file}' (taken from cache)");
                Respond(file, cacheHit, response);
                Monitor.Exit(_cacheLock);
                return;
            }

            _logger.Log($"[Thread] [{DateTime.Now}] CACHE MISS -> '{file}' (sending API request)");

            string url = $"{_baseUrl}{file}";

            try {
                HttpResponseMessage serverResponse = _client.GetAsync(url).Result;
                string body = serverResponse.Content.ReadAsStringAsync().Result;
                JObject result = JObject.Parse(body);

                _cache.Set(file, result);
                _logger.Log($"[Thread] [{DateTime.Now}] Result stored in cache for query: '{file}'");

                Respond(file, result, response);
            }
            catch (Exception ex) {
                _logger.Log($"[Thread] [{DateTime.Now}] Error while fetching file '{file}': {ex.Message}");
                Respond(file, new JObject(), response);
            }
            finally {
                Monitor.Exit(_cacheLock);
            }

            _sem.Release();
        }

        private void Respond(string file, JObject result, HttpListenerResponse response) {
            FileUtility writer = new FileUtility();
            byte[] buffer;
            if (!result.HasValues) {
                buffer = System.Text.Encoding.UTF8.GetBytes($"Something went wrong");
            }
            else {
                buffer = System.Text.Encoding.UTF8.GetBytes($"Avarage word length: {result["result"]}");
            }
            System.IO.Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            output.Close();
            writer.Write(file, result);
        }
        
        private async Task GracefulShutdown() {
            var waitForExit = new ManualResetEventSlim(false);
            
            PosixSignalRegistration.Create(PosixSignal.SIGINT, context => {
                _logger.Log($"\n[Server] [{DateTime.Now}] SIGINT called");
                _logger.Log($"[Server] [{DateTime.Now}] Shutting down gracefuly...");
                Console.WriteLine(_contexts.Count);

                _cts.Cancel();

                foreach(var item in _contexts) {
                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes("<HTML><BODY>Server shut down</BODY></HTML>");
                    System.IO.Stream output = item.Response.OutputStream;
                    output.Write(buffer, 0, buffer.Length);
                    output.Close();
                }

                waitForExit.Set();
            });

            waitForExit.Wait();
        }
    }
}