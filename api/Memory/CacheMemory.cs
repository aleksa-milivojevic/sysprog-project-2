using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Utility;

namespace Memory
{
    public class CacheMemory {
        private readonly int _capacity = 8;

        private readonly Dictionary<string, JObject> _cacheMap = new Dictionary<string, JObject>();
        
        private readonly Dictionary<string, TimeSpan> _lruList = new Dictionary<string, TimeSpan>();

        private readonly Logger _logger;

        public CacheMemory() {
            _logger = new Logger();
        }

        public async Task<JObject?> Get(string key) {
            if (_cacheMap.TryGetValue(key, out var val)) {
                if (DateTime.Now.TimeOfDay - _lruList[key] > new TimeSpan(0, 0, 10)) {
                    _logger.Log($"[CACHE] [{DateTime.Now}] From cache {key} info is stale");
                    return null;
                }
                _lruList[key] = DateTime.Now.TimeOfDay;                
                _logger.Log($"[CACHE] [{DateTime.Now}] Read from cache {key}");
                return val;
            }
            return null;
        }

        public async Task Set(string key, JObject value) {
            if (_cacheMap.TryGetValue(key, out var val)) {
                _lruList[key] = DateTime.Now.TimeOfDay;
                _cacheMap[key] = value;
                _logger.Log($"[CACHE] [{DateTime.Now}] Overwritten in cache {key}");
            }
            else {
                if (_cacheMap.Count == _capacity) {
                    var last = GetLRUItem();
                    _lruList.Remove(last);
                    _cacheMap.Remove(last);
                    _logger.Log($"[CACHE] [{DateTime.Now}] Removed from cache {last}");
                }
                _lruList.Add(key, DateTime.Now.TimeOfDay);
                _cacheMap.Add(key, value);
                _logger.Log($"[CACHE] [{DateTime.Now}] Added to cache {key}");
            }
        }

        public string GetLRUItem() {
            TimeSpan min = DateTime.Now.TimeOfDay;
            string minKey = "";
            foreach (var key in _lruList.Keys)
                if (_lruList[key] < min) {
                    min = _lruList[key];
                    minKey = key;
                }
            return minKey;
        }

        public int Count() {
            return _cacheMap.Count;
        }

        public IEnumerable<string> Keys() {
            return _cacheMap.Keys;
        }
    }
}