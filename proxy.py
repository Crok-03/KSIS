import socket
import threading
from urllib.parse import urlparse

BUFFER_SIZE = 8192

def handle_client(client_socket):
    try:
        request = client_socket.recv(BUFFER_SIZE)
        if not request:
            client_socket.close()
            return

        request_line, rest = request.split(b'\r\n', 1)
        request_line = request_line.decode(errors='ignore')

        parts = request_line.split()
        if len(parts) < 3:
            client_socket.close()
            return

        method, full_url, http_version = parts

        if method.upper() == "CONNECT":
            client_socket.close()
            return

        parsed_url = urlparse(full_url)
        if not parsed_url.hostname:
            client_socket.close()
            return

        host = parsed_url.hostname
        port = parsed_url.port or 80
        path = parsed_url.path or '/'
        if parsed_url.query:
            path += '?' + parsed_url.query

        new_request_line = f"{method} {path} {http_version}\r\n"
        modified_request = new_request_line.encode() + rest

        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server_socket:
            server_socket.connect((host, port))
            server_socket.sendall(modified_request)

            response = b""
            first_line_received = False
            while True:
                data = server_socket.recv(BUFFER_SIZE)
                if not data:
                    break
                if not first_line_received:
                    try:
                        status_line = data.split(b'\r\n', 1)[0].decode()
                        status_code = status_line.split()[1]
                        print(f"[LOG] {full_url} -> {status_code}")
                        first_line_received = True
                    except:
                        pass
                client_socket.sendall(data)

    except:
        pass
    finally:
        client_socket.close()



def start_proxy(host='127.0.0.2', port=8888):
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.bind((host, port))
    server.listen(100)
    print(f"[STARTED] Proxy listening on {host}:{port}")

    while True:
        client_socket, addr = server.accept()
        thread = threading.Thread(target=handle_client, args=(client_socket,))
        thread.daemon = True
        thread.start()

if __name__ == "__main__":
    start_proxy()