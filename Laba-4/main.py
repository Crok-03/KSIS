import socket
import threading
import time
from urllib.parse import urlparse
from collections import defaultdict

# Константы для настройки прокси-сервера
REQUEST_BUFFER_SIZE = 8192
CONNECTION_TIMEOUT = 60
PROXY_DEFAULT_PORT = 8080
ALLOWED_DOMAINS = {'example.com', 'live.legendy.by'}
MAX_HTTPS_LOG = 3  # Максимальное количество одинаковых HTTPS-логов

# Для отслеживания HTTPS-запросов
https_logs = defaultdict(int)


def extract_http_request_details(raw_request_data):
    """Извлекает метод, URL и версию HTTP из сырых данных запроса."""
    try:
        request_first_line = raw_request_data.split(b'\r\n')[0].decode('utf-8')
        parts = request_first_line.split(' ')
        if len(parts) >= 3:
            method = parts[0]
            url = parts[1]
            version = parts[2]

            # Если URL начинается с http://, оставляем как есть
            if url.startswith('http://'):
                return method, url, version
            # Иначе пытаемся извлечь хост из заголовков
            else:
                return method, url, version
        return None, None, None
    except Exception as error:
        print(f"[ERROR] Не удалось разобрать HTTP запрос: {error}")
        return None, None, None


def find_target_host_in_headers(headers_data):
    """Ищет и извлекает хост из заголовков HTTP запроса."""
    try:
        headers_text = headers_data.decode('utf-8', errors='ignore')
        for header_line in headers_text.split('\r\n'):
            if header_line.lower().startswith('host:'):
                host = header_line.split(':', 1)[1].strip()
                return host
        return None
    except:
        return None


def is_domain_allowed(url, host_header=None):
    """Проверяет, разрешен ли домен для проксирования."""
    try:
        # Если URL полный (начинается с http://)
        if url.startswith('http://'):
            domain = urlparse(url).hostname
        else:
            # Иначе используем хост из заголовка
            if host_header:
                domain = host_header.split(':')[0]
            else:
                return False

        if not domain:
            return False

        return any(domain.endswith('.' + allowed) or domain == allowed
                   for allowed in ALLOWED_DOMAINS)
    except:
        return False


def adjust_request_for_target_server(original_request, original_url):
    """Модифицирует оригинальный запрос для отправки целевому серверу."""
    try:
        # Если URL полный (начинается с http://)
        if original_url.startswith('http://'):
            parsed_url = urlparse(original_url)
            resource_path = parsed_url.path if parsed_url.path else "/"
            if parsed_url.query:
                resource_path += "?" + parsed_url.query
        else:
            # Иначе используем URL как есть (относительный путь)
            resource_path = original_url

        first_line = original_request.split(b'\r\n')[0].decode('utf-8')
        http_method, _, http_version = first_line.split(' ')

        modified_first_line = f"{http_method} {resource_path} {http_version}"
        modified_request = original_request.replace(
            original_request.split(b'\r\n')[0],
            modified_first_line.encode('utf-8'))

        # Удаляем заголовок Proxy-Connection если есть
        modified_request = modified_request.replace(b'Proxy-Connection:', b'Connection:')
        return modified_request
    except Exception as error:
        print(f"[ERROR] Ошибка модификации запроса: {error}")
        return original_request


def extract_http_status_code(response_data):
    """Извлекает статус код из HTTP ответа сервера."""
    try:
        status_line = response_data.split(b'\r\n')[0].decode('utf-8')
        return status_line.split(' ')[1] if len(status_line.split(' ')) > 1 else "???"
    except:
        return "???"


def log_https_request(target_url):
    """Логирует HTTPS-запрос с ограничением количества одинаковых сообщений."""
    domain = target_url.split(':')[0]
    https_logs[domain] += 1
    if https_logs[domain] <= MAX_HTTPS_LOG:
        print(f"[INFO] Пропущен HTTPS-запрос: {target_url}")
    elif https_logs[domain] == MAX_HTTPS_LOG + 1:
        print(f"[INFO] (и ещё {sum(https_logs.values()) - MAX_HTTPS_LOG} HTTPS-запросов скрыто)")


def process_client_connection(client_socket, client_address):
    """Обрабатывает соединение от клиента."""
    try:
        client_request = b''
        while b'\r\n\r\n' not in client_request:
            data_chunk = client_socket.recv(REQUEST_BUFFER_SIZE)
            if not data_chunk:
                break
            client_request += data_chunk

        if not client_request:
            client_socket.close()
            return

        http_method, target_url, http_version = extract_http_request_details(client_request)

        # Пропускаем HTTPS-запросы
        if http_method == "CONNECT":
            log_https_request(target_url)
            client_socket.close()
            return

        if not target_url or not http_method:
            client_socket.close()
            return

        # Получаем хост из заголовков для проверки домена
        target_host = find_target_host_in_headers(client_request)

        # Проверяем разрешен ли домен
        if not is_domain_allowed(target_url, target_host):
            print(f"[BLOCKED] Запрос к запрещенному домену: {target_url} (Host: {target_host})")
            client_socket.close()
            return

        print(f"[REQUEST] {http_method} {target_url}")

        try:
            # Определяем целевой хост и порт
            if target_url.startswith('http://'):
                parsed_url = urlparse(target_url)
                target_host = parsed_url.hostname
                target_port = parsed_url.port or 80
            else:
                if not target_host:
                    print(f"[ERROR] Не удалось определить хост для URL: {target_url}")
                    client_socket.close()
                    return
                # Извлекаем хост и порт из заголовка Host
                if ':' in target_host:
                    target_host, target_port = target_host.split(':')
                    target_port = int(target_port)
                else:
                    target_port = 80

            proxy_request = adjust_request_for_target_server(client_request, target_url)

            server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            server_socket.settimeout(CONNECTION_TIMEOUT)

            try:
                server_socket.connect((target_host, target_port))
                server_socket.sendall(proxy_request)

                server_response = b''
                transfer_start_time = time.time()

                while True:
                    try:
                        response_chunk = server_socket.recv(REQUEST_BUFFER_SIZE)
                        if not response_chunk:
                            break

                        client_socket.sendall(response_chunk)

                        if not server_response:
                            server_response = response_chunk
                            status_code = extract_http_status_code(server_response)
                            print(f"[RESPONSE] {target_url} -> {status_code}")

                        if time.time() - transfer_start_time > 60:
                            print(f"[INFO] Длительное соединение (>60s): {target_url}")
                            transfer_start_time = time.time()

                    except socket.timeout:
                        print(f"[TIMEOUT] Превышено время ожидания ответа: {target_url}")
                        break
                    except ConnectionResetError:
                        print(f"[INFO] Соединение разорвано клиентом: {target_url}")
                        break

            except ConnectionResetError:
                print(f"[INFO] Соединение с сервером разорвано: {target_url}")
            except Exception as connection_error:
                print(f"[ERROR] Ошибка соединения с {target_host}:{target_port}: {connection_error}")
                error_response = (
                    "HTTP/1.1 502 Bad Gateway\r\n"
                    "Content-Length: 21\r\n\r\n"
                    f"Error: {str(connection_error)[:100]}"
                )
                client_socket.sendall(error_response.encode('utf-8'))

            finally:
                server_socket.close()

        except Exception as url_error:
            print(f"[ERROR] Ошибка обработки URL {target_url}: {url_error}")
            client_socket.close()
            return

    except ConnectionResetError:
        print("[INFO] Клиент разорвал соединение")
    except Exception as processing_error:
        print(f"[ERROR] Ошибка обработки запроса: {processing_error}")

    finally:
        client_socket.close()


def run_proxy_server():
    """Запускает основной цикл работы прокси-сервера."""
    proxy_port = PROXY_DEFAULT_PORT

    proxy_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    proxy_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)

    try:
        proxy_socket.bind(('0.0.0.0', proxy_port))
        proxy_socket.listen(100)
        print(f"[START] Прокси-сервер запущен на порту {proxy_port}")
        print(f"[INFO] Настройте браузер использовать прокси: localhost:{proxy_port}")
        print("[INFO] Разрешенные домены:", ', '.join(ALLOWED_DOMAINS))
        print("[INFO] Примеры для теста:")
        print("       http://example.com/")
        print("       http://live.legendy.by:8000/legendyfm")

        while True:
            client_socket, client_address = proxy_socket.accept()
            client_thread = threading.Thread(
                target=process_client_connection,
                args=(client_socket, client_address)
            )
            client_thread.daemon = True
            client_thread.start()

    except KeyboardInterrupt:
        print("\n[STOP] Работа прокси-сервера остановлена по запросу пользователя")
    except Exception as server_error:
        print(f"[CRITICAL] Критическая ошибка сервера: {server_error}")
    finally:
        proxy_socket.close()


if __name__ == "__main__":
    run_proxy_server()
