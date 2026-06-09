namespace Memory
{
    class CacheLockObject {
        private object _lock;
        private int _tasks;

        public object Lock
        {
            get { return _lock; }
        }

        public int Tasks
        {
            get { return _tasks; }
            set { _tasks = value; }
        }

        public CacheLockObject() {
            _lock = new object();
            _tasks = 0;
        }
    }
}