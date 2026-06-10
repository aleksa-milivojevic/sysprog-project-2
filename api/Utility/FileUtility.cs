using Newtonsoft.Json.Linq;
using System;

namespace Utility
{
    public class FileUtility {

        private readonly Logger _logger;

        private readonly object _writeLock;

        public FileUtility() {
            _logger = new Logger();
            _writeLock = new object();
        }

        public async Task Write(string file, JObject result) {
            string path = $"query-results.txt";
            if (!result.HasValues) {
                _logger.Log($"[FileUtility] [{DateTime.Now}] Result null for file {file}");
                return;
            }

            Monitor.Enter(_writeLock);
            string content = $"\nTimeStamp: {DateTime.Now}\n" +
                                $"File: {file}\n"  +
                                $"Result: {result["result"]}\n";
            try {
                File.AppendAllText(path, content);
            }
            catch(Exception ex) {
                _logger.Log($"[FileUtility] [{DateTime.Now}] Error while writing to file: {ex.Message}");
            }
            finally {
                Monitor.Exit(_writeLock);
            }

            _logger.Log($"[FileUtility] [{DateTime.Now}] Results for file {file} saved");
        }
    }
}