#!/usr/bin/env python3
"""GUI launch coordinator; subprocess output and runtime secrets stay private."""
import json
import os
from pathlib import Path
import shutil
import signal
import ssl
import socket
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
STOP = threading.Event()
HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}))
HTTPS = None


def emit(kind, **values):
    print(json.dumps({"kind": kind, **values}, ensure_ascii=False), flush=True)


def read_port():
    port = 5100
    for name in ("appsettings.json", "appsettings.Development.json"):
        path = ROOT / "src/EduCenterOS.Api" / name
        if path.exists():
            settings = json.loads(path.read_text())
            port = settings.get("Platform", {}).get("Host", {}).get("Port", port)
    if type(port) is not int or not 1024 <= port <= 65535:
        raise RuntimeError("إعداد منفذ التطبيق غير صالح.")
    return port


def read_https_port():
    port = 5101
    for name in ("appsettings.json", "appsettings.Development.json"):
        path = ROOT / "src/EduCenterOS.Api" / name
        if path.exists():
            settings = json.loads(path.read_text())
            port = settings.get("Platform", {}).get("Host", {}).get("HttpsPort", port)
    if type(port) is not int or not 1024 <= port <= 65535 or port == read_port():
        raise RuntimeError("إعداد منفذ الاتصال الآمن غير صالح.")
    return port


def trusted_https_opener():
    # Check OS trust, then export only the public certificate for Python's TLS verifier.
    # No --password/--no-password flag: dotnet exports no private key.
    with tempfile.TemporaryDirectory(prefix="educenteros-tls-") as directory:
        certificate = Path(directory) / "localhost.pem"
        for arguments in (["dotnet", "dev-certs", "https", "--check", "--trust", "--quiet"],
                          ["dotnet", "dev-certs", "https", "--export-path", str(certificate), "--format", "Pem", "--quiet"]):
            result = subprocess.run(arguments, stdin=subprocess.DEVNULL, capture_output=True, timeout=30)
            if result.returncode:
                raise RuntimeError("شهادة HTTPS المحلية تحتاج إعداد الثقة الموضح في README.")
        text = certificate.read_text()
        if "PRIVATE KEY" in text or not text.startswith("-----BEGIN CERTIFICATE-----"):
            raise RuntimeError("تعذر التحقق من شهادة HTTPS المحلية.")
        context = ssl.create_default_context(cafile=str(certificate))
    return urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context))


def secure_api_ready(port):
    try:
        with HTTPS.open(f"https://localhost:{port}/health/ready", timeout=2) as response:
            if response.read(1024) != b"Healthy": return False
        with HTTPS.open(f"https://localhost:{port}/openapi/v1.json", timeout=2) as response:
            document = json.loads(response.read(2 * 1024 * 1024))
        return document.get("info", {}).get("title") == "EduCenterOS API" and "/api/v1/accounts" in document.get("paths", {})
    except (OSError, ValueError, urllib.error.URLError):
        return False


def get_response(port, path):
    try:
        with HTTP.open(f"http://127.0.0.1:{port}{path}", timeout=1) as response:
            return response.read(2 * 1024 * 1024)
    except (OSError, urllib.error.URLError):
        return None


def occupied(port):
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=0.5):
            return True
    except OSError:
        return False


def is_educenteros(port):
    if get_response(port, "/health/live") != b"Healthy":
        return False
    try:
        document = json.loads(get_response(port, "/openapi/v1.json") or b"{}")
        return document.get("info", {}).get("title") == "EduCenterOS API" and "/api/v1/accounts" in document.get("paths", {})
    except (ValueError, TypeError, AttributeError):
        return False


def stop_owned_process(process):
    for sig, seconds in ((signal.SIGINT, 12), (signal.SIGTERM, 5), (signal.SIGKILL, 5)):
        try:
            os.killpg(process.pid, sig)
        except ProcessLookupError:
            break
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            process.poll()
            try:
                os.killpg(process.pid, 0)
            except ProcessLookupError:
                process.wait()
                return
            time.sleep(0.1)
    process.wait(timeout=5)


def start_process(arguments, output):
    environment = {key: os.environ[key] for key in ("PATH", "HOME", "USER", "TMPDIR", "DOTNET_ROOT") if key in os.environ}
    return subprocess.Popen(arguments, cwd=ROOT, env=environment, start_new_session=True,
                            stdin=subprocess.DEVNULL, stdout=output, stderr=subprocess.STDOUT)


def run_step(arguments, message, failure, timeout=180):
    if STOP.is_set():
        raise InterruptedError()
    emit("status", message=message)
    with tempfile.TemporaryFile() as output:
        process = start_process(arguments, output)
        try:
            deadline = time.monotonic() + timeout
            while process.poll() is None:
                if STOP.wait(0.2):
                    raise InterruptedError()
                if time.monotonic() >= deadline:
                    raise RuntimeError(failure)
            if process.returncode != 0:
                raise RuntimeError(failure)
        finally:
            stop_owned_process(process)


def launch():
    global HTTPS
    port = read_port()
    https_port = read_https_port()
    HTTPS = trusted_https_opener()
    url = f"https://localhost:{https_port}/swagger"
    if occupied(port):
        if not is_educenteros(port):
            raise RuntimeError("المنفذ مستخدم بواسطة برنامج آخر. لم يتم إيقافه أو تغييره.")
        if get_response(port, "/health/ready") != b"Healthy" or not secure_api_ready(https_port):
            raise RuntimeError("EduCenterOS يعمل بالفعل، لكن اتصال قاعدة البيانات غير جاهز.")
        emit("ready", message="التطبيق يعمل بالفعل. تم فتح Swagger.", url=url, owned=False)
        return

    if occupied(https_port):
        raise RuntimeError("منفذ HTTPS مستخدم بالفعل. لم يتم إيقاف البرنامج الذي يستخدمه.")

    for tool in ("dotnet", "psql", "infisical"):
        if shutil.which(tool) is None:
            raise RuntimeError(f"الأداة المطلوبة غير موجودة: {tool}.")
    trust = Path.home() / ".config/EduCenterOS/infisical-trust.json"
    if not trust.is_file() or trust.is_symlink():
        raise RuntimeError("هذا الجهاز يحتاج إعداد Infisical الأولي الموضح في README.")

    database_trust = Path.home() / ".config/EduCenterOS/supabase-targets.json"
    if not database_trust.is_file() or database_trust.is_symlink():
        raise RuntimeError("هذا الجهاز يحتاج إعداد اتصال Supabase الموضح في README.")
    dev = [sys.executable, str(ROOT / "scripts/dev.py")]
    run_step(["dotnet", "restore", "EduCenterOS.sln", "--locked-mode"], "جاري تجهيز حزم المشروع…", "تعذر تجهيز حزم المشروع. تحقق من اتصال الإنترنت ونسخة .NET.")
    run_step(["dotnet", "build", "EduCenterOS.sln", "-c", "Release", "--no-restore"], "جاري بناء آخر تعديلات الكود…", "البناء فشل. راجع أخطاء الكود قبل إعادة التشغيل.")
    run_step([*dev, "identity-migrate"], "جاري الاتصال بـ Supabase وتطبيق تحديثات قاعدة البيانات…", "تعذر تطبيق تحديثات قاعدة البيانات. تحقق من إعدادات التطوير وInfisical.")

    emit("status", message="جاري تشغيل EduCenterOS…")
    with tempfile.TemporaryFile() as output:
        process = start_process([*dev, "run"], output)
        try:
            deadline = time.monotonic() + 120
            while True:
                if STOP.is_set():
                    raise InterruptedError()
                if process.poll() is not None:
                    raise RuntimeError("تعذر تشغيل التطبيق. تحقق من إعدادات التطوير وتسجيل دخول Infisical.")
                if is_educenteros(port) and get_response(port, "/health/ready") == b"Healthy" and secure_api_ready(https_port):
                    break
                if time.monotonic() >= deadline:
                    raise RuntimeError("التطبيق لم يصل إلى حالة الجاهزية في الوقت المحدد.")
                STOP.wait(0.5)
            emit("ready", message="التطبيق جاهز. تم فتح Swagger.", url=url, owned=True)
            while process.poll() is None:
                if STOP.wait(0.25):
                    raise InterruptedError()
            if process.returncode != 0:
                raise RuntimeError("التطبيق توقف بسبب خطأ. يمكنك إعادة المحاولة.")
        finally:
            stop_owned_process(process)


def main():
    signal.signal(signal.SIGINT, lambda *_: STOP.set())
    signal.signal(signal.SIGTERM, lambda *_: STOP.set())
    try:
        launch()
    except InterruptedError:
        emit("stopped", message="تم إيقاف تشغيل التطبيق. قاعدة البيانات ما زالت متاحة.")
    except RuntimeError as error:
        emit("error", message=str(error))
        return 1
    except (OSError, ValueError, TypeError, AttributeError):
        emit("error", message="تعذر تجهيز التشغيل. تحقق من الأدوات وملفات الإعدادات.")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
