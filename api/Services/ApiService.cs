using Newtonsoft.Json.Linq;
using System.Text;
using System.Net;
using System.Threading;
using System.Runtime.InteropServices;
using Utility;
using Memory; 

namespace Services
{
    class ApiService {

        private const int _task_limit = 10;

        private readonly CancellationTokenSource _cts;
        
        private readonly HttpListener _listener;

        private HttpClient _client;

        private readonly string _baseUrl = "http://localhost:5050/";

        private readonly string _hostUrl = "http://localhost:5000/";

        private List<HttpListenerContext> _contexts;

        private SemaphoreSlim _reqSem;

        private readonly object _reqLock;

        private List<Task> _tasks;

        private readonly Logger _logger;

        private CacheMemory _cache;

        private Dictionary<string, CacheLockObject> _queuedFiles;
        
        public ApiService() {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add(_hostUrl);
            _client = new HttpClient();
            _contexts = new List<HttpListenerContext>();
            _reqSem = new SemaphoreSlim(0);
            _reqLock = new object();
            _tasks = new List<Task>(_task_limit);
            _logger = new Logger();
            _cache = new CacheMemory();
            _queuedFiles = new Dictionary<string, CacheLockObject>();
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

                    _logger.Log($"[Listener] [{DateTime.Now}] Caught a request!");
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
            
            _logger.Log($"[Task {Task.CurrentId}] [{DateTime.Now}] Started for file: '{file}'");
            JObject? cacheHit;

            cacheHit = _cache.Get(file).Result;
            if (cacheHit != null) {
                _logger.Log($"[Task {Task.CurrentId}] [{DateTime.Now}] CACHE HIT  -> '{file}' (taken from cache)");
                Respond(file, cacheHit, response);
                return;
            }

            if (_queuedFiles.ContainsKey(file)) { // zahtevi koji cekaju upis u cache
                _queuedFiles[file].Tasks++;
                Monitor.Enter(_queuedFiles[file].Lock);

                cacheHit = _cache.Get(file).Result;
                _logger.Log($"[Thread] [{DateTime.Now}] CACHE HIT  -> '{file}' (taken from cache)");
                Respond(file, cacheHit, response);

                _queuedFiles[file].Tasks--;
                if (_queuedFiles[file].Tasks == 0)
                    _queuedFiles.Remove(file);
                Monitor.Exit(_queuedFiles[file].Lock);
                
            }
            else { //prvi zahtev koji upisuje u cache
                _queuedFiles.Add(file, new CacheLockObject());
                Monitor.Enter(_queuedFiles[file].Lock);

                _logger.Log($"[Thread] [{DateTime.Now}] CACHE MISS -> '{file}' (sending API request)");

                string url = $"{_baseUrl}{file}";

                try {
                    HttpResponseMessage serverResponse = _client.GetAsync(url).Result;
                    string body = serverResponse.Content.ReadAsStringAsync().Result;
                    JObject result = JObject.Parse(body);

                    await _cache.Set(file, result);
                    _logger.Log($"[Thread] [{DateTime.Now}] Result stored in cache for query: '{file}'");

                    Respond(file, result, response);
                }
                catch (Exception ex) {
                    _logger.Log($"[Thread] [{DateTime.Now}] Error while fetching file '{file}': {ex.Message}");
                    Respond(file, new JObject(), response);
                }
                finally {
                    Monitor.Exit(_queuedFiles[file].Lock);
                }
            }
        }

        private void Respond(string file, JObject? result, HttpListenerResponse response) {
            FileUtility writer = new FileUtility();
            byte[] buffer;
            if (result == null || !result.HasValues) {
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