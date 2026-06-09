namespace LogUtil
{
    public class Logger {

        private string _logFile;

        private static readonly object _logLock = new object();

        public Logger() {
            _logFile = "server-logs.txt";
        }

        public void Log(string log) {
            Monitor.Enter(_logLock);
            try {
                File.AppendAllText(_logFile, log+"\n");
                Console.WriteLine(log);
            }
            catch(Exception ex) {
                Console.WriteLine($"[Log] [{DateTime.Now}] Error while logging to file: {ex.Message}");
            }
            finally {
                Monitor.Exit(_logLock);
            }
        }
    }
}