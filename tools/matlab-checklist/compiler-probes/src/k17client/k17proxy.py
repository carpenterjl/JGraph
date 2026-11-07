"""A logging HTTP proxy in front of jgraph-mps (compiler and SDK plan, stage K17a): every request
R2025b's Excel client sends is written to the log as it came over the wire (the request line, the
headers in order, the body as text or hex) and forwarded unchanged to the server; the reply's
status, headers and body are written the same way and returned. Standard library only; HTTP/1.1
without pipelining is enough for the client.

    python k17proxy.py <listen port> <server port> <log file>
"""
import http.client
import socketserver
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler

LISTEN, TARGET, LOG = int(sys.argv[1]), int(sys.argv[2]), sys.argv[3]
lock = threading.Lock()
counter = [0]
start = time.time()


def body_text(data):
    if not data:
        return '(empty)'
    try:
        text = data.decode('utf-8')
        if all(c == '\t' or c == '\n' or c == '\r' or 32 <= ord(c) < 127 for c in text):
            return 'text %d bytes: %s' % (len(data), text.replace('\r', '\\r').replace('\n', '\\n'))
    except UnicodeDecodeError:
        pass
    shown = data[:256].hex(' ')
    return 'bytes %d: %s%s' % (len(data), shown, ' ...' if len(data) > 256 else '')


def log(lines):
    with lock:
        with open(LOG, 'a', encoding='utf-8', newline='\n') as f:
            for line in lines:
                f.write(line + '\n')


class Handler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'

    def log_message(self, fmt, *args):  # quiet
        pass

    def do_ANY(self):
        counter[0] += 1
        n = counter[0]
        length = int(self.headers.get('Content-Length') or 0)
        body = self.rfile.read(length) if length else b''
        lines = ['== request %d at %.3f s' % (n, time.time() - start),
                 '  %s %s %s' % (self.command, self.path, self.request_version)]
        for k, v in self.headers.items():
            lines.append('  > %s: %s' % (k, v))
        lines.append('  > body %s' % body_text(body))
        try:
            conn = http.client.HTTPConnection('localhost', TARGET, timeout=120)
            headers = {k: v for k, v in self.headers.items() if k.lower() not in ('host',)}
            headers['Host'] = 'localhost:%d' % TARGET
            conn.request(self.command, self.path, body=body if length else None, headers=headers)
            resp = conn.getresponse()
            data = resp.read()
            lines.append('  < %d %s' % (resp.status, resp.reason))
            for k, v in resp.getheaders():
                lines.append('  < %s: %s' % (k, v))
            lines.append('  < body %s' % body_text(data))
            self.send_response(resp.status, resp.reason)
            for k, v in resp.getheaders():
                if k.lower() in ('transfer-encoding', 'connection', 'content-length'):
                    continue
                self.send_header(k, v)
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)
            conn.close()
        except Exception as e:  # noqa: BLE001 - a proxy reports and goes on
            lines.append('  < proxy error: %s' % e)
            try:
                self.send_response(502, 'Bad Gateway')
                self.send_header('Content-Length', '0')
                self.end_headers()
            except Exception:  # noqa: BLE001
                pass
        log(lines)

    do_GET = do_POST = do_PUT = do_DELETE = do_HEAD = do_OPTIONS = do_PATCH = do_ANY


class Server(socketserver.ThreadingMixIn, socketserver.TCPServer):
    allow_reuse_address = True
    daemon_threads = True


if __name__ == '__main__':
    log(['== proxy listening on %d for %d' % (LISTEN, TARGET)])
    with Server(('127.0.0.1', LISTEN), Handler) as server:
        server.serve_forever()
