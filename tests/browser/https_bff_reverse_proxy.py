#!/usr/bin/env python3
"""CI-only HTTPS reverse proxy: actual Angular assets + Kestrel /api and /bff.

Runs solely on disposable Github Actions runner. NOT a production proxy,
identity gateway or replacement for reviewed ingress configuration.
"""
import argparse
import http.client
import http.server
import mimetypes
import pathlib
import ssl
import urllib.parse


ROUTES = ("/api/", "/bff/", "/.well-known/", "/signin-oidc", "/signout-callback-oidc")
HOP = {
    "connection", "keep-alive", "proxy-authorization", "proxy-authenticate",
    "transfer-encoding", "te", "trailer", "upgrade"
}


class Server(http.server.ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_GET(self): self.dispatch()
    def do_POST(self): self.dispatch()
    def do_PATCH(self): self.dispatch()
    def do_PUT(self): self.dispatch()
    def do_DELETE(self): self.dispatch()
    def do_OPTIONS(self): self.dispatch()
    def do_HEAD(self): self.dispatch()

    def log_message(self, fmt, *args):
        # Do not log sensitive OIDC codes/state, login forms, cookies or JWTs.
        print("HTTPS frontend/BFF proxy: " + self.command + " response", flush=True)

    def dispatch(self):
        path = urllib.parse.urlsplit(self.path).path
        host = self.headers.get("Host", "").split(":")[0].lower()
        if host not in {
            "identity.shinera.localhost",
            "e2e-http-ci.shinera.localhost",
            "other.shinera.localhost",
        }:
            self.send_error(421, "Unknown test host")
            return
        if any(path == r.rstrip("/") or path.startswith(r)
               for r in ROUTES):
            self.upstream()
            return
        self.serve_angular(path)

    def upstream(self):
        size = int(self.headers.get("Content-Length") or "0")
        if size > 2_000_000:
            self.send_error(413)
            return
        body = self.rfile.read(size) if size else None
        headers = {name: value for name, value in self.headers.items()
                   if name.lower() not in HOP}
        # Host + port remain as supplied by the browser; ASP.NET Core's
        # ForwardedHeaders middleware sets the correct original scheme.
        headers["X-Forwarded-Proto"] = "https"
        headers["X-Forwarded-For"] = "127.0.0.1"
        headers["Connection"] = "close"
        try:
            upstream = http.client.HTTPConnection("127.0.0.1", 5069, timeout=50)
            upstream.request(self.command, self.path, body=body, headers=headers)
            resp = upstream.getresponse()
            payload = resp.read()
            self.send_response(resp.status)
            for key, value in resp.getheaders():
                if key.lower() not in HOP and key.lower() not in {"content-length", "server", "date"}:
                    self.send_header(key, value)
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            if self.command != "HEAD":
                self.wfile.write(payload)
            upstream.close()
        except (OSError, TimeoutError) as error:
            # Avoid logging credential-bearing request paths.
            print("BFF upstream unavailable: " + type(error).__name__, flush=True)
            self.send_error(502, "Test API unavailable")

    def serve_angular(self, url_path):
        base = self.server.angular_root
        path = pathlib.Path(urllib.parse.unquote(url_path).lstrip("/"))
        if ".." in path.parts:
            self.send_error(404)
            return
        file = (base / path).resolve()
        if not file.is_relative_to(base) or not file.is_file():
            if pathlib.PurePosixPath(url_path).suffix:
                self.send_error(404)
                return
            file = base / "index.html"
        if not file.is_file():
            self.send_error(503, "Angular build missing")
            return
        payload = file.read_bytes()
        content_type = mimetypes.guess_type(file.name)[0] or "application/octet-stream"
        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(payload)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--angular-root", required=True)
    parser.add_argument("--cert", required=True)
    parser.add_argument("--key", required=True)
    parser.add_argument("--port", type=int, default=8443)
    args = parser.parse_args()
    root = pathlib.Path(args.angular_root).resolve()
    if not (root / "index.html").is_file():
        raise RuntimeError("Missing compiled Angular index.html: " + str(root))
    httpd = Server(("0.0.0.0", args.port), Handler)
    httpd.angular_root = root
    tls = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    tls.minimum_version = ssl.TLSVersion.TLSv1_2
    tls.load_cert_chain(args.cert, args.key)
    httpd.socket = tls.wrap_socket(httpd.socket, server_side=True)
    print("FR-12G-C HTTPS reverse proxy ready", flush=True)
    httpd.serve_forever()


if __name__ == "__main__":
    main()
