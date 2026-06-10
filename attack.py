import requests
import threading

BASE_URL = "http://localhost:5000/"
FILES = [
    "f1.txt", "f2.txt", "f3.txt", "f4.txt", "f5.txt",
    "f6.txt", "f7.txt", "f8.txt", "f9.txt", "f10.txt"
]
REQNUM = 2

def send_req(url, file):
    try:
        full_url = f"{url}{file}"
        response = requests.get(full_url, timeout=50)
        print(f"File: {file} | Status: {response.status_code} | Body (bytes): {response.content}")
    except Exception as e:
        print(f"Error: {file}: {e}")

threads = []

for file in FILES:
    for _ in range(REQNUM):
        t = threading.Thread(target=send_req, args=(BASE_URL, file))
        threads.append(t)
        t.start()

for t in threads:
    t.join()

print("\nSvi zahtevi su poslati")